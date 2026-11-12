using System.Diagnostics;
using Nexus.AI.EditingLite.Services;

namespace Nexus.AI.EditingLite.Tests.Services;

public sealed class SafePathTests : IDisposable
{
    private readonly string parent=Path.Combine(Path.GetTempPath(),"nexus-safe-path-"+Guid.NewGuid().ToString("N"));
    private readonly string root;
    public SafePathTests()
    {
        root=Path.Combine(parent,"app"); Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(parent,"application")); File.WriteAllText(Path.Combine(root,"sample.txt"),"hello");
    }
    [Theory]
    [InlineData("../application/file.txt")]
    [InlineData("..\\secret.txt")]
    [InlineData("sub\\..\\..\\outside.txt")]
    [InlineData("sub/../sample.txt")]
    [InlineData("C:\\Windows\\file.txt")]
    [InlineData("\\\\host\\share\\file.txt")]
    [InlineData("sample.txt:stream")]
    [InlineData("sample.txt.")]
    [InlineData("sample.txt ")]
    [InlineData("NUL.txt")]
    [InlineData("sub//sample.txt")]
    [InlineData("SUB~1/sample.txt")]
    public void UnsafeNames_DeniedBeforeIO(string name)=>Assert.ThrowsAny<Exception>(()=>SafePath.ResolveRelative(root,name));
    [Fact]
    public void AbsoluteSiblingPrefix_IsDenied()=>Assert.ThrowsAny<Exception>(()=>SafePath.ResolveRelative(root,Path.Combine(parent,"application","file.txt")));
    [Fact]
    public void MixedSeparatorsAndWindowsCase_AreResolvedWithPinnedDirectories()
    {
        Directory.CreateDirectory(Path.Combine(root,"sub")); File.WriteAllText(Path.Combine(root,"sub","sample.txt"),"hello");
        using var lease=new SafePathLease(root.ToUpperInvariant(),"sub/sample.txt");
        using var stream=lease.OpenTarget(); Assert.Equal(5,stream.Length);
    }
    [Fact]
    public void JunctionEscape_IsDenied()
    {
        var link=Path.Combine(root,"link");
        using var process=Process.Start(new ProcessStartInfo("cmd.exe") { Arguments=$"/c mklink /J \"{link}\" \"{Path.Combine(parent,"application")}\"",UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true })!;
        Assert.True(process.WaitForExit(5000)); Assert.Equal(0,process.ExitCode);
        try { Assert.ThrowsAny<Exception>(()=>new SafePathLease(root,"link/file.txt")); }
        finally { Directory.Delete(link); }
    }
    [Fact]
    public void FileSymlinkEscapeOrUnavailablePrivilege_IsFailClosed()
    {
        var outside=Path.Combine(parent,"application","file.txt"); File.WriteAllText(outside,"outside");
        var link=Path.Combine(root,"link.txt");
        try { File.CreateSymbolicLink(link,outside); }
        catch(IOException e) when((e.HResult&0xffff)==1314)
        {
            // This host cannot create symlinks. Do not elevate or silently skip:
            // the unavailable target is denied; an actual junction independently
            // proves the same tag-independent reparse boundary above.
            Assert.ThrowsAny<Exception>(()=>new SafePathLease(root,"link.txt"));
            Assert.Equal("outside",File.ReadAllText(outside)); return;
        }
        try { Assert.ThrowsAny<Exception>(()=>new SafePathLease(root,"link.txt")); }
        finally { File.Delete(link); }
    }
    [Fact]
    public void PinnedDirectory_CannotBeRenamedDuringOperation()
    {
        Directory.CreateDirectory(Path.Combine(root,"sub")); File.WriteAllText(Path.Combine(root,"sub","file.txt"),"hello");
        using var lease=new SafePathLease(root,"sub/file.txt");
        Assert.Throws<IOException>(()=>Directory.Move(Path.Combine(root,"sub"),Path.Combine(parent,"moved")));
    }
    [Fact]
    public void HardLinkedTarget_IsDenied()
    {
        var target=Path.Combine(root,"sample.txt");
        Assert.True(CreateHardLink(Path.Combine(parent,"outside-link.txt"),target,IntPtr.Zero));
        Assert.Throws<IOException>(()=>new SafePathLease(root,"sample.txt"));
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]
    private static extern bool CreateHardLink(string newName,string existing,IntPtr security);
    public void Dispose()=>Directory.Delete(parent,true);
}
