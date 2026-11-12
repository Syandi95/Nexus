using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Nexus.AI.EditingLite.Exceptions;

namespace Nexus.AI.EditingLite.Services;

public static class SafePath
{
    public static string ResolveRelative(string root,string relative)
    {
        try
        {
            if(!Path.IsPathFullyQualified(root) || string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Length>256 ||
                relative.IndexOfAny([':', '\0', '~'])>=0) throw new ArgumentException();
            var parts=relative.Replace('/',Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar);
            if(parts.Length>8 || parts.Any(p=>p.Length==0 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
                p.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || IsDeviceName(p))) throw new ArgumentException();
            var canonicalRoot=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var target=Path.GetFullPath(Path.Combine(canonicalRoot,Path.Combine(parts)));
            var prefix=canonicalRoot+Path.DirectorySeparatorChar;
            var comparison=OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if(!target.StartsWith(prefix,comparison)) throw new ArgumentException();
            for(var current=target;current is not null;current=Path.GetDirectoryName(current))
                if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                    throw new ArgumentException();
            return target;
        }
        catch(Exception e) when(e is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        { throw new EditingException(EditingFailureCode.PathOutsideProjectRoot,"Target path denied.",e); }
    }
    private static bool IsDeviceName(string name)
    {
        var stem=name.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" ||
            stem.Length==4 && (stem.StartsWith("COM")||stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9';
    }
}

// Pins every directory component against ordinary rename/deletion/reparse swaps.
// Rejects all reparse points instead of following an ambiguous final target.
public sealed class SafePathLease : IDisposable
{
    private readonly List<(string Path,SafeFileHandle Handle)> directories=[];
    public string TargetPath { get; }
    public SafePathLease(string root,string relative)
    {
        TargetPath=SafePath.ResolveRelative(root,relative);
        try
        {
            if(!OperatingSystem.IsWindows() || new DriveInfo(Path.GetPathRoot(TargetPath)!).DriveType!=DriveType.Fixed ||
                new DriveInfo(Path.GetPathRoot(TargetPath)!).DriveFormat!="NTFS" ||
                Path.TrimEndingDirectorySeparator(root)==Path.GetPathRoot(root)) throw new IOException("Unsupported root.");
            var parent=Path.GetDirectoryName(TargetPath)!;
            var components=new Stack<string>();
            for(var p=parent;p is not null;p=Path.GetDirectoryName(p)) components.Push(p);
            while(components.Count>0)
            {
                var path=components.Pop();
                var handle=Native.CreateFile(path,0x80000000,3,IntPtr.Zero,3,0x02200000,IntPtr.Zero);
                ValidateHandle(handle,path,true);
                directories.Add((path,handle));
            }
            using var target=OpenTarget();
        }
        catch(Exception e) { Dispose(); throw new IOException("Target boundary denied.",e); }
    }
    public FileStream OpenTarget()
    {
        ValidateDirectories();
        var handle=Native.CreateFile(TargetPath,0x80000000,1,IntPtr.Zero,3,0x00200000,IntPtr.Zero);
        ValidateHandle(handle,TargetPath,false);
        return new FileStream(handle,FileAccess.Read);
    }
    public void ValidateDirectories()
    { foreach(var item in directories) ValidateHandle(item.Handle,item.Path,true); }
    internal static void ValidateHandle(SafeFileHandle handle,string expected,bool directory)
    {
        try
        {
            if(handle.IsInvalid || !Native.GetFileInformationByHandle(handle,out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if((info.Attributes&0x400)!=0 || (!directory && ((info.Attributes&~(uint)(0x20|0x80))!=0 || info.Links!=1)))
                throw new IOException("Unsupported target attributes.");
            var name=new StringBuilder(32768);
            if(Native.GetFinalPathNameByHandle(handle,name,(uint)name.Capacity,0)==0) throw new Win32Exception(Marshal.GetLastWin32Error());
            var actual=name.ToString();
            if(actual.StartsWith("\\\\?\\",StringComparison.Ordinal)) actual=actual[4..];
            if(!string.Equals(Path.TrimEndingDirectorySeparator(actual),Path.TrimEndingDirectorySeparator(expected),StringComparison.OrdinalIgnoreCase))
                throw new IOException("Resolved target differs.");
        }
        catch { handle.Dispose(); throw; }
    }
    public void Dispose() { foreach(var item in directories) item.Handle.Dispose(); directories.Clear(); }
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct FileInfo
        { public uint Attributes,CreationLow,CreationHigh,AccessLow,AccessHigh,WriteLow,WriteHigh,Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow; }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        internal static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool GetFileInformationByHandle(SafeFileHandle file,out FileInfo info);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern uint GetFinalPathNameByHandle(SafeFileHandle file,StringBuilder name,uint size,uint flags);
    }
}
