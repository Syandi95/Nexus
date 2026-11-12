using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Nexus.AI.EditingLite.Services;

// ADAPT the verified certification launcher: suspended root, verified anonymous
// Job assignment before resume, inherited membership, cleanup only via that Job.
// Internal SDK lifecycle helper, not a tool/executor or public process capability.
internal sealed class OwnedSdkProcess : IDisposable
{
    private SafeFileHandle job=null!, process=null!;
    private AnonymousPipeServerStream stdout=null!,stderr=null!;
    internal StreamReader StandardOutput { get; private set; }=null!;
    internal StreamReader StandardError { get; private set; }=null!;
    internal int Id { get; private set; }
    internal bool MembershipVerifiedBeforeResume { get; private set; }
    internal bool HasExited=>WaitForSingleObject(process,0)==0;
    internal int ExitCode { get {if(!GetExitCodeProcess(process,out var code))throw Error();return (int)code;} }
    internal IntPtr Handle=>process.DangerousGetHandle();
    internal bool Contains(OwnedSdkProcess other)=>IsProcessInJob(other.process,job,out var member)&&member;

    internal static OwnedSdkProcess Start(ProcessStartInfo info)
    {
        var owned=new OwnedSdkProcess();
        try {owned.Launch(info);return owned;} catch {owned.Dispose();throw;}
    }
    private void Launch(ProcessStartInfo info)
    {
        job=CreateJobObjectW(IntPtr.Zero,null);if(job.IsInvalid)throw Error();
        var limits=new EXTENDED_LIMITS();limits.Basic.Flags=0x2000; // KILL_ON_JOB_CLOSE, no breakaway/CPU/UI restrictions.
        if(!SetInformationJobObject(job,9,ref limits,Marshal.SizeOf<EXTENDED_LIMITS>()))throw Error();
        stdout=new(PipeDirection.In,HandleInheritability.Inheritable);stderr=new(PipeDirection.In,HandleInheritability.Inheritable);
        if(!SetHandleInformation(stdout.SafePipeHandle,1,0) || !SetHandleInformation(stderr.SafePipeHandle,1,0))throw Error();
        using var input=CreateFileW("NUL",0x80000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
        if(input.IsInvalid || !SetHandleInformation(input,1,1))throw Error();
        var handles=new[] {input.DangerousGetHandle(),stdout.ClientSafePipeHandle.DangerousGetHandle(),stderr.ClientSafePipeHandle.DangerousGetHandle()};
        var size=IntPtr.Zero;InitializeProcThreadAttributeList(IntPtr.Zero,1,0,ref size);
        var attributes=Marshal.AllocHGlobal(size);var list=Marshal.AllocHGlobal(IntPtr.Size*3);IntPtr environment=IntPtr.Zero;
        var initialized=false;PROCESS_INFORMATION child=default;var assigned=false;
        try
        {
            if(!InitializeProcThreadAttributeList(attributes,1,0,ref size))throw Error();initialized=true;
            Marshal.Copy(handles,0,list,3);
            if(!UpdateProcThreadAttribute(attributes,0,(IntPtr)0x20002,list,(IntPtr)(IntPtr.Size*3),IntPtr.Zero,IntPtr.Zero))throw Error();
            var startup=new STARTUPINFOEX();startup.Startup.Size=Marshal.SizeOf<STARTUPINFOEX>();startup.Startup.Flags=0x100;
            startup.Startup.Input=handles[0];startup.Startup.Output=handles[1];startup.Startup.Error=handles[2];startup.Attributes=attributes;
            environment=Marshal.StringToHGlobalUni(string.Join('\0',info.Environment.OrderBy(p=>p.Key,StringComparer.OrdinalIgnoreCase).Select(p=>p.Key+"="+p.Value))+"\0\0");
            var command=new StringBuilder(Quote(info.FileName)+" "+string.Join(' ',info.ArgumentList.Select(Quote)));
            if(!CreateProcessW(info.FileName,command,IntPtr.Zero,IntPtr.Zero,true,0x08000000|0x80000|0x400|0x4,environment,info.WorkingDirectory,ref startup,out child))throw Error();
            process=new SafeFileHandle(child.Process,true);Id=(int)child.Id;
            if(!AssignProcessToJobObject(job,process) || !IsProcessInJob(process,job,out var member) || !member)throw Error();
            assigned=true;MembershipVerifiedBeforeResume=true;
            if(ResumeThread(child.Thread)==uint.MaxValue)throw Error();
            stdout.DisposeLocalCopyOfClientHandle();stderr.DisposeLocalCopyOfClientHandle();
            StandardOutput=new StreamReader(stdout,Encoding.UTF8);StandardError=new StreamReader(stderr,Encoding.UTF8);
        }
        catch
        {
            if(child.Process!=IntPtr.Zero)
            {
                // Exact newly created suspended handle only, never enumerated PIDs.
                if(assigned)TerminateJobObject(job,1);else TerminateProcess(child.Process,1);
            }
            throw;
        }
        finally
        {
            if(child.Thread!=IntPtr.Zero)CloseHandle(child.Thread);
            if(environment!=IntPtr.Zero)Marshal.FreeHGlobal(environment);
            if(initialized)DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(attributes);Marshal.FreeHGlobal(list);
        }
    }
    internal async Task WaitForExitAsync(CancellationToken token)
    {
        while(!HasExited) {token.ThrowIfCancellationRequested();await Task.Delay(10,token).ConfigureAwait(false);}
    }
    internal async Task DrainJobAsync()
    {
        if(job is null || job.IsInvalid || !MembershipVerifiedBeforeResume)return;
        if(!TerminateJobObject(job,1))throw Error();
        var timer=Stopwatch.StartNew();
        while(true)
        {
            if(!QueryInformationJobObject(job,1,out var counts,Marshal.SizeOf<ACCOUNTING>(),IntPtr.Zero))throw Error();
            // Job accounting can reach zero before the terminated root handle
            // becomes signalled. Join both fences before temp cleanup/recovery.
            if(counts.ActiveProcesses==0 && HasExited)return;
            if(timer.Elapsed>TimeSpan.FromSeconds(5))throw new IOException("Owned SDK Job cleanup incomplete.");
            await Task.Delay(10).ConfigureAwait(false);
        }
    }
    public void Dispose()
    {
        job?.Dispose(); // Kernel KILL_ON_JOB_CLOSE final ownership guard.
        StandardOutput?.Dispose();StandardError?.Dispose();stdout?.Dispose();stderr?.Dispose();process?.Dispose();
    }
    internal static string Quote(string arg)
    {
        if(arg.Length>0 && !arg.Any(c=>char.IsWhiteSpace(c)||c=='"'))return arg;
        var text=new StringBuilder("\"");var slashes=0;
        foreach(var c in arg)
        {
            if(c=='\\') {slashes++;continue;}
            text.Append('\\',c=='"' ? slashes*2+1 : slashes);text.Append(c);slashes=0;
        }
        text.Append('\\',slashes*2);return text.Append('"').ToString();
    }
    private static Win32Exception Error()=>new(Marshal.GetLastWin32Error());
    [StructLayout(LayoutKind.Sequential)] private struct STARTUPINFO {internal int Size;internal IntPtr Reserved,Desktop,Title;internal int X,Y,Width,Height,XChars,YChars,Fill,Flags;internal short Show,ReservedSize;internal IntPtr ReservedBytes,Input,Output,Error;}
    [StructLayout(LayoutKind.Sequential)] private struct STARTUPINFOEX {internal STARTUPINFO Startup;internal IntPtr Attributes;}
    [StructLayout(LayoutKind.Sequential)] private struct PROCESS_INFORMATION {internal IntPtr Process,Thread;internal uint Id,ThreadId;}
    [StructLayout(LayoutKind.Sequential)] private struct BASIC_LIMITS {internal long ProcessTime,JobTime;internal uint Flags;internal UIntPtr Minimum,Maximum;internal uint Active;internal UIntPtr Affinity;internal uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)] private struct IO_COUNTERS {internal ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)] private struct EXTENDED_LIMITS {internal BASIC_LIMITS Basic;internal IO_COUNTERS Io;internal UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob;}
    [StructLayout(LayoutKind.Sequential)] private struct ACCOUNTING {internal long User,Kernel,PeriodUser,PeriodKernel;internal uint Faults,TotalProcesses,ActiveProcesses,Terminated;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetInformationJobObject(SafeFileHandle job,int kind,ref EXTENDED_LIMITS data,int size);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job,SafeFileHandle process);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool IsProcessInJob(SafeFileHandle process,SafeFileHandle job,out bool member);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool TerminateJobObject(SafeFileHandle job,uint code);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool QueryInformationJobObject(SafeFileHandle job,int kind,out ACCOUNTING data,int size,IntPtr returned);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetHandleInformation(SafeHandle handle,uint mask,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr attributes,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list,int count,uint flags,ref IntPtr size);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool UpdateProcThreadAttribute(IntPtr list,uint flags,IntPtr attribute,IntPtr value,IntPtr size,IntPtr previous,IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool CreateProcessW(string file,StringBuilder command,IntPtr processAttributes,IntPtr threadAttributes,bool inherit,uint flags,IntPtr environment,string cwd,ref STARTUPINFOEX startup,out PROCESS_INFORMATION process);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeFileHandle handle,uint milliseconds);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetExitCodeProcess(SafeFileHandle process,out uint exit);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool TerminateProcess(IntPtr process,uint exit);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
