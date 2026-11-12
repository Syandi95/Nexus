using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Nexus.AI.EditingLite.Services;

// Private generated SDK data, never a source target or model-selected path.
internal sealed class VerificationTempDirectory : IDisposable
{
    internal string Path { get; }
    private readonly string marker;
    private readonly string identity=Guid.NewGuid().ToString("N");
    private SafePathLease? lease;
    internal VerificationTempDirectory():this(path=>new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {}
    internal VerificationTempDirectory(Func<string,FileStream> createMarker)
    {
        var id=new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16");
        Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(ref id,0,IntPtr.Zero,out var pointer));
        string parent;
        try {parent=Marshal.PtrToStringUni(pointer)!;} finally {Marshal.FreeCoTaskMem(pointer);}
        var relative="Nexus-Sdk-"+identity;
        Path=SafePath.ResolveRelative(parent,relative);
        var user=WindowsIdentity.GetCurrent().User!;
        var security=new DirectorySecurity();security.SetAccessRuleProtection(true,false);security.SetOwner(user);
        foreach(var sid in new[] {user,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)})
            security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        var descriptor=security.GetSecurityDescriptorBinaryForm();var pin=GCHandle.Alloc(descriptor,GCHandleType.Pinned);
        try
        {
            var attributes=new SECURITY_ATTRIBUTES {Length=Marshal.SizeOf<SECURITY_ATTRIBUTES>(),Descriptor=pin.AddrOfPinnedObject()};
            if(!CreateDirectoryW(Path,ref attributes))throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally {pin.Free();}
        // Resolve only this newly-created, private SDK directory. Packaged-launch
        // virtualization can redirect writes even when this process has no package
        // identity and known-folder APIs return the unredirected path. Never apply
        // this translation to a source workspace or accept a model-selected path.
        using var created=SafePathLease.Native.CreateFile(Path,0x80000000,3,IntPtr.Zero,3,0x02200000,IntPtr.Zero);
        var physical=new StringBuilder(32768);
        if(created.IsInvalid || SafePathLease.Native.GetFinalPathNameByHandle(created,physical,(uint)physical.Capacity,0)==0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var resolved=physical.ToString();
        if(resolved.StartsWith("\\\\?\\",StringComparison.Ordinal))resolved=resolved[4..];
        SafePathLease.ValidateHandle(created,resolved,true);
        Path=SafePath.ResolveRelative(System.IO.Path.GetDirectoryName(resolved)!,relative);
        if(!string.Equals(Path,resolved,StringComparison.OrdinalIgnoreCase))throw new IOException("SDK temp identity differs.");
        parent=System.IO.Path.GetDirectoryName(Path)!;
        marker=System.IO.Path.Combine(Path,".nexus-owner");
        try
        {
            using(var file=createMarker(marker))
            {var bytes=System.Text.Encoding.ASCII.GetBytes(identity);file.Write(bytes);file.Flush(true);}
            // LocalLow inherits NotContentIndexed. Normalize only our new marker,
            // preserving the existing strict source/path attribute validator.
            File.SetAttributes(marker,FileAttributes.Archive);
            lease=new SafePathLease(parent,relative+"/.nexus-owner");
            SafePathLease.ValidateHandle(created,Path,true);
        }
        catch
        {
            lease?.Dispose();lease=null;
            created.Dispose();
            if(File.Exists(marker) && File.ReadAllText(marker)==identity)Remove(Path);
            throw;
        }
    }
    public void Dispose()
    {
        if(lease is null)return;
        lease.ValidateDirectories();
        using(var file=lease.OpenTarget())
        using(var reader=new StreamReader(file))
            if(reader.ReadToEnd()!=identity)throw new IOException("SDK temp ownership conflict.");
        // Pins prevent a root swap until all owned SDK processes have drained.
        lease.Dispose();lease=null;
        Remove(Path);
    }
    private static void Remove(string directory)
    {
        if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("SDK temp reparse denied.");
        foreach(var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes=File.GetAttributes(entry);
            if((attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("SDK temp reparse denied.");
            if((attributes&FileAttributes.Directory)!=0)Remove(entry);else File.Delete(entry);
        }
        Directory.Delete(directory);
    }
    [StructLayout(LayoutKind.Sequential)] private struct SECURITY_ATTRIBUTES {internal int Length;internal IntPtr Descriptor;internal int Inherit;}
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern int SHGetKnownFolderPath(ref Guid id,uint flags,IntPtr token,out IntPtr path);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool CreateDirectoryW(string path,ref SECURITY_ATTRIBUTES attributes);
}
