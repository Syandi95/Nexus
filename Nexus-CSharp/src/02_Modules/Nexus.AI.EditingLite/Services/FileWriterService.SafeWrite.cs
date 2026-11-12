using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Security.AccessControl;

namespace Nexus.AI.EditingLite.Services;

public partial class FileWriterService
{
    internal Action<string>? SafeWriteProbe { get; set; }
    public async Task<string> WriteTempFileAsync(EditSession session,CancellationToken token)
    {
        if(!Guid.TryParseExact(session.Id,"N",out _)) throw new ArgumentException("Operation ID required.");
        var path=Path.Combine(Path.GetDirectoryName(session.TargetPath)!,".nexus-edit-"+session.Id+".tmp");
        var bytes=session.Encoding.GetPreamble().Concat(session.Encoding.GetBytes(session.ProposedContent)).ToArray();
        var created=false;
        try
        {
            var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.Asynchronous|FileOptions.WriteThrough);
            await using (stream.ConfigureAwait(false))
            {
            created=true;
            var split=bytes.Length/2;
            await stream.WriteAsync(bytes.AsMemory(0,split),token).ConfigureAwait(false);
            SafeWriteProbe?.Invoke("TempHalfWritten"); token.ThrowIfCancellationRequested();
            await stream.WriteAsync(bytes.AsMemory(split),token).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false); stream.Flush(true);
            return path;
            }
        }
        catch { if(created && File.Exists(path)) File.Delete(path); throw; }
    }

    public static string Hash(Stream stream)
    { stream.Position=0; var hash=Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); stream.Position=0; return hash; }

    // NTFS transactional replacement. Hash checked with an exclusive transacted
    // target handle; a same-length private update enlists the write lock before
    // closing that handle and moving the temp. There is no unprotected handoff.
    // Unsupported TxF fails closed; never fall back to an unlocked replace.
    public void AtomicReplaceChecked(string tempPath,string targetPath,string expectedHash,string desiredHash,CancellationToken token,Action? authorizeCommit=null)
    {
        if(!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows NTFS transaction required.");
        using var transaction=Transactions.CreateTransaction(IntPtr.Zero,IntPtr.Zero,0,0,0,5000,"Nexus bounded text mutation");
        if(transaction.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var committed=false;
        try
        {
            token.ThrowIfCancellationRequested();
            using(var handle=Transactions.CreateFileTransacted(targetPath,0xC0000000,1,IntPtr.Zero,3,0x00200000,IntPtr.Zero,transaction,IntPtr.Zero,IntPtr.Zero))
            {
                SafePathLease.ValidateHandle(handle,targetPath,false);
                using var current=new FileStream(handle,FileAccess.ReadWrite);
                if(Hash(current)!=expectedHash) throw new SafeWriteConflictException();
                var sections=AccessControlSections.Access|AccessControlSections.Owner|AccessControlSections.Group;
                if(new FileInfo(targetPath).GetAccessControl(sections).GetSecurityDescriptorSddlForm(sections)!=
                    new FileInfo(tempPath).GetAccessControl(sections).GetSecurityDescriptorSddlForm(sections))
                    throw new IOException("Security descriptor preservation unavailable.");
                current.SetLength(current.Length); current.Flush(true);
            }
            using(var handle=Transactions.CreateFileTransacted(tempPath,0xC0000000,1,IntPtr.Zero,3,0x00200000,IntPtr.Zero,transaction,IntPtr.Zero,IntPtr.Zero))
            {
                SafePathLease.ValidateHandle(handle,tempPath,false);
                using var source=new FileStream(handle,FileAccess.ReadWrite);
                if(Hash(source)!=desiredHash) throw new IOException("Temporary content differs.");
                source.SetLength(source.Length); source.Flush(true);
            }
            SafeWriteProbe?.Invoke("Enlisted");
            if(!Transactions.MoveFileTransacted(tempPath,targetPath,IntPtr.Zero,IntPtr.Zero,1,transaction))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            SafeWriteProbe?.Invoke("BeforeCommit");
            token.ThrowIfCancellationRequested();
            authorizeCommit?.Invoke();
            if(!Transactions.CommitTransaction(transaction)) throw new Win32Exception(Marshal.GetLastWin32Error());
            committed=true;
        }
        finally { if(!committed) Transactions.RollbackTransaction(transaction); }
    }
    private static class Transactions
    {
        [DllImport("KtmW32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern SafeFileHandle CreateTransaction(IntPtr attributes,IntPtr guid,uint options,uint isolation,uint flags,uint timeout,string description);
        [DllImport("KtmW32.dll",SetLastError=true)] internal static extern bool CommitTransaction(SafeFileHandle transaction);
        [DllImport("KtmW32.dll",SetLastError=true)] internal static extern bool RollbackTransaction(SafeFileHandle transaction);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern SafeFileHandle CreateFileTransacted(string name,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template,SafeFileHandle transaction,IntPtr version,IntPtr miniVersion);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern bool MoveFileTransacted(string existing,string destination,IntPtr progress,IntPtr data,uint flags,SafeFileHandle transaction);
    }
}

public sealed class SafeWriteConflictException : IOException { }
