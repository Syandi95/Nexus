using Microsoft.Win32.SafeHandles;

namespace Nexus.AI.EditingLite.Services;

public sealed record ReadPathIdentity(uint Volume,ulong FileId,long Length,ulong LastWrite);
public sealed record ReadDirectoryEntry(string RelativePath,bool IsDirectory,bool IsReparsePoint);

// Additive read-only directory pin. Uses the certified file validator/native APIs;
// neither opens a writer nor changes existing SafePath/SafeWrite behavior.
public sealed class ReadOnlyPathScope : IDisposable
{
    private readonly List<(string Path,SafeFileHandle Handle)> pins=[];
    private bool disposed;
    public string Root {get;}
    public ReadPathIdentity Identity {get;}
    public ReadOnlyPathScope(string root)
    {
        if(!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(root) || root.StartsWith("\\\\",StringComparison.Ordinal))throw new IOException("Local NTFS root required.");
        Root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var drive=new DriveInfo(Path.GetPathRoot(Root)!);
        if(drive.DriveType!=DriveType.Fixed || drive.DriveFormat!="NTFS" || Root==Path.TrimEndingDirectorySeparator(drive.Name))throw new IOException("Local NTFS folder required.");
        SafePath.ResolveRelative(Root,"scope-check.cs");
        try
        {
            var chain=new Stack<string>();for(var p=Root;p is not null;p=Path.GetDirectoryName(p))chain.Push(p);
            while(chain.Count>0){var p=chain.Pop();pins.Add((p,Pin(p)));}
            Identity=Capture(pins[^1].Handle);
        }
        catch {Dispose();throw;}
    }
    private static SafeFileHandle Pin(string path)
    {
        var handle=SafePathLease.Native.CreateFile(path,0x80000000,3,IntPtr.Zero,3,0x02200000,IntPtr.Zero);
        SafePathLease.ValidateHandle(handle,path,true);return handle;
    }
    public void Validate()
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        foreach(var pin in pins)SafePathLease.ValidateHandle(pin.Handle,pin.Path,true);
        if(Capture(pins[^1].Handle).Volume!=Identity.Volume || Capture(pins[^1].Handle).FileId!=Identity.FileId)throw new IOException("Root identity changed.");
    }
    public IReadOnlyList<ReadDirectoryEntry> EnumerateDirectory(string relative,int maximum,CancellationToken token)
    {
        Validate();var path=string.IsNullOrEmpty(relative) ? Root : Path.GetDirectoryName(SafePath.ResolveRelative(Root,relative.Replace('\\','/')+"/scope-check.cs"))!;
        var acquired=new List<(string Path,SafeFileHandle Handle)>();
        try
        {
            if(path!=Root)
            {
                var parts=Path.GetRelativePath(Root,path).Split(Path.DirectorySeparatorChar);var current=Root;
                foreach(var part in parts){current=Path.Combine(current,part);acquired.Add((current,Pin(current)));}
            }
            var entries=new List<ReadDirectoryEntry>();
            foreach(var item in Directory.EnumerateFileSystemEntries(path,"*",new EnumerationOptions {RecurseSubdirectories=false,IgnoreInaccessible=false,AttributesToSkip=0}))
            {
                token.ThrowIfCancellationRequested();if(entries.Count>=maximum)throw new IOException("Discovery entry limit exceeded.");
                var attributes=File.GetAttributes(item);
                entries.Add(new(Path.GetRelativePath(Root,item).Replace('\\','/'),attributes.HasFlag(FileAttributes.Directory),attributes.HasFlag(FileAttributes.ReparsePoint)));
            }
            foreach(var pin in acquired)SafePathLease.ValidateHandle(pin.Handle,pin.Path,true);Validate();return entries.AsReadOnly();
        }
        finally {foreach(var pin in acquired)pin.Handle.Dispose();}
    }
    public ReadPathIdentity FileIdentity(string relative)
    {Validate();using var lease=new SafePathLease(Root,relative);using var stream=lease.OpenTarget();return Capture(stream.SafeFileHandle);}
    public static ReadPathIdentity Capture(SafeFileHandle handle)
    {
        if(!SafePathLease.Native.GetFileInformationByHandle(handle,out var info))throw new IOException("Cannot capture read identity.");
        return new(info.Volume,((ulong)info.IndexHigh<<32)|info.IndexLow,checked((long)(((ulong)info.SizeHigh<<32)|info.SizeLow)),((ulong)info.WriteHigh<<32)|info.WriteLow);
    }
    public void Dispose(){if(disposed)return;disposed=true;foreach(var pin in pins)pin.Handle.Dispose();pins.Clear();}
}
