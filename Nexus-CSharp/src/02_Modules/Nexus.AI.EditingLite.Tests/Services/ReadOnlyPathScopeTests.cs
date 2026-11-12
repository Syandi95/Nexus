using Nexus.AI.EditingLite.Services;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using System.Text.Json;
using System.Security.Cryptography;
namespace Nexus.AI.EditingLite.Tests.Services;
public sealed class ReadOnlyPathScopeTests
{
    [Fact]
    public void A_SymbolicFileEscape_IsRejectedByTheSameReparseBoundary()
    {
        var fixture=Environment.GetEnvironmentVariable("NEXUS_I2A_SYMLINK_FIXTURE");
        if(!string.IsNullOrEmpty(fixture))
        {
            Assert.False(new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator));
            VerifyPreparedSymbolicFixture(fixture);return;
        }
        var root=Path.Combine(Path.GetTempPath(),"nexus-i2a-symlink-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var outside=root+"-outside.cs";File.WriteAllText(outside,"outside source");
        try
        {
            File.CreateSymbolicLink(Path.Combine(root,"Alias.cs"),outside);
            using var scope=new ReadOnlyPathScope(root);Assert.True(Assert.Single(scope.EnumerateDirectory("",4096,CancellationToken.None)).IsReparsePoint);Assert.ThrowsAny<Exception>(()=>scope.FileIdentity("Alias.cs"));
        }
        finally {File.Delete(Path.Combine(root,"Alias.cs"));Directory.Delete(root);File.Delete(outside);}
    }
    private static void VerifyPreparedSymbolicFixture(string requestPath)
    {
        var approved=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Temp","opencode","gate-i2a-symlink-recovery");
        if (Path.GetFullPath(requestPath) == @"D:\NEXUS\scratch\recovery-first-20261017\symlink-checkpoint\fixture-request.json")
        {
            approved = @"D:\NEXUS\scratch\recovery-first-20261017\symlink-checkpoint";
            Assert.Equal("2967ac59a2ad9ae3d340b0df85af967682ec1396b091fc8abc57f448669f2633", HashFixtureFile(requestPath));
        }
        Assert.Equal(Path.Combine(approved,"fixture-request.json"),Path.GetFullPath(requestPath));
        using var request=JsonDocument.Parse(File.ReadAllText(requestPath));var r=request.RootElement;
        Assert.Equal("NEXUS-I2A-OWNED-SYMLINK-FIXTURE-V1",r.GetProperty("Contract").GetString());
        var nonce=r.GetProperty("Nonce").GetString()!;Assert.Equal(32,nonce.Length);Assert.All(nonce,c=>Assert.True(Uri.IsHexDigit(c)));
        var parent=Path.Combine(approved,"fixture-"+nonce);var root=Path.Combine(parent,"root");var outside=Path.Combine(parent,"outside.cs");var alias=Path.Combine(root,"Alias.cs");
        Assert.Equal(parent,r.GetProperty("Parent").GetString());Assert.Equal(root,r.GetProperty("Root").GetString());Assert.Equal(outside,r.GetProperty("Target").GetString());Assert.Equal(alias,r.GetProperty("Link").GetString());
        var marker=Path.Combine(parent,"ownership.json");Assert.Equal(marker,r.GetProperty("Marker").GetString());Assert.Equal(r.GetProperty("MarkerHash").GetString(),HashFixtureFile(marker));
        using var markerJson=JsonDocument.Parse(File.ReadAllText(marker));Assert.Equal(nonce,markerJson.RootElement.GetProperty("nonce").GetString());
        using var parentPin=new ReadOnlyPathScope(parent);using var rootPin=new ReadOnlyPathScope(root);
        Assert.Equal(r.GetProperty("ParentIdentity").GetProperty("Volume").GetUInt32(),parentPin.Identity.Volume);Assert.Equal(r.GetProperty("ParentIdentity").GetProperty("FileId").GetUInt64(),parentPin.Identity.FileId);
        Assert.Equal(r.GetProperty("RootIdentity").GetProperty("Volume").GetUInt32(),rootPin.Identity.Volume);Assert.Equal(r.GetProperty("RootIdentity").GetProperty("FileId").GetUInt64(),rootPin.Identity.FileId);
        Assert.Equal(r.GetProperty("TargetHash").GetString(),HashFixtureFile(outside));Assert.Equal("outside source",File.ReadAllText(outside));
        var targetIdentity=parentPin.FileIdentity("outside.cs");Assert.Equal(r.GetProperty("TargetIdentity").GetProperty("FileId").GetUInt64(),targetIdentity.FileId);
        // Pin the link itself with OPEN_REPARSE_POINT. Validate the actual tag,
        // target and normal-token follow-read BEFORE exercising reader denial.
        using var linkHandle=CreateFile(alias,0,1,IntPtr.Zero,3,0x00200000,IntPtr.Zero);Assert.False(linkHandle.IsInvalid);
        var reparse=new byte[16384];Assert.True(GetReparsePoint(linkHandle,0x000900A8,IntPtr.Zero,0,reparse,reparse.Length,out var returned,IntPtr.Zero));Assert.True(returned>=20);
        Assert.Equal(0xA000000Cu,BitConverter.ToUInt32(reparse));Assert.Equal(outside,new FileInfo(alias).LinkTarget);
        Assert.Equal("outside source",File.ReadAllText(alias));Assert.Equal(r.GetProperty("TargetHash").GetString(),HashFixtureFile(alias));
        using var receipt=JsonDocument.Parse(File.ReadAllText(Path.Combine(approved,"setup-receipt.json")));Assert.Equal(nonce,receipt.RootElement.GetProperty("nonce").GetString());
        Assert.Equal(receipt.RootElement.GetProperty("linkIdentity").GetProperty("FileId").GetUInt64(),ReadOnlyPathScope.Capture(linkHandle).FileId);
        // Original production security assertions are retained verbatim.
        using var scope=new ReadOnlyPathScope(root);Assert.True(Assert.Single(scope.EnumerateDirectory("",4096,CancellationToken.None)).IsReparsePoint);Assert.ThrowsAny<Exception>(()=>scope.FileIdentity("Alias.cs"));
        Assert.Equal("outside source",File.ReadAllText(outside));
        // Prepared fixture cleanup belongs to the separately verified normal-token
        // recovery wrapper; do not recursively delete or follow the alias here.
    }
    private static string HashFixtureFile(string path){using var input=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();}
    [DllImport("kernel32.dll",EntryPoint="DeviceIoControl",SetLastError=true)]private static extern bool GetReparsePoint(SafeFileHandle handle,uint code,IntPtr input,int length,byte[] output,int outputLength,out int returned,IntPtr overlapped);
    [Fact]
    public void A_PinBlocksDirectoryReplacement_AndFileBoundaryRejectsAdsHardlinksUnreadable()
    {
        var root=Path.Combine(Path.GetTempPath(),"nexus-i2a-boundary-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var file=Path.Combine(root,"Source.cs");File.WriteAllText(file,"class Sample {}");
        try
        {
            using(var scope=new ReadOnlyPathScope(root))
            {
                Assert.Single(scope.EnumerateDirectory("",4096,CancellationToken.None));Assert.Throws<IOException>(()=>Directory.Move(root,root+"-swap"));
                Assert.ThrowsAny<Exception>(()=>scope.FileIdentity("Source.cs:secret"));Assert.ThrowsAny<Exception>(()=>scope.FileIdentity("../Source.cs"));
                Assert.True(CreateHardLink(Path.Combine(root,"Linked.cs"),file,IntPtr.Zero));Assert.Throws<IOException>(()=>scope.FileIdentity("Linked.cs"));File.Delete(Path.Combine(root,"Linked.cs"));
                var info=new FileInfo(file);var original=info.GetAccessControl();var denied=new FileSecurity();denied.SetAccessRuleProtection(true,true);denied.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,FileSystemRights.ReadData,AccessControlType.Deny));
                try {info.SetAccessControl(denied);Assert.Throws<IOException>(()=>scope.FileIdentity("Source.cs"));}
                finally {info.SetAccessControl(original);}
            }
            Directory.Move(root,root+"-swap");Directory.Move(root+"-swap",root);
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public void A_JunctionRootAndChildEscape_AreNeverFollowed()
    {
        var root=Path.Combine(Path.GetTempPath(),"nexus-i2a-junction-"+Guid.NewGuid().ToString("N"));var outside=root+"-outside";Directory.CreateDirectory(root);Directory.CreateDirectory(outside);File.WriteAllText(Path.Combine(outside,"Secret.cs"),"outside must not be read");
        var junction=Path.Combine(root,"escape");Directory.CreateDirectory(junction);
        try
        {
            SetJunction(junction,outside);
            Assert.ThrowsAny<Exception>(()=>new ReadOnlyPathScope(junction));
            using var scope=new ReadOnlyPathScope(root);Assert.True(Assert.Single(scope.EnumerateDirectory("",4096,CancellationToken.None)).IsReparsePoint);
            Assert.ThrowsAny<Exception>(()=>scope.EnumerateDirectory("escape",4096,CancellationToken.None));Assert.ThrowsAny<Exception>(()=>scope.FileIdentity("escape/Secret.cs"));
        }
        finally {Directory.Delete(junction);Directory.Delete(root);Directory.Delete(outside,true);}
    }
    private static void SetJunction(string path,string target)
    {
        var substitute=System.Text.Encoding.Unicode.GetBytes("\\??\\"+target);var print=System.Text.Encoding.Unicode.GetBytes(target);var data=new byte[16+substitute.Length+2+print.Length+2];
        BitConverter.GetBytes(0xA0000003u).CopyTo(data,0);BitConverter.GetBytes((ushort)(data.Length-8)).CopyTo(data,4);
        BitConverter.GetBytes((ushort)substitute.Length).CopyTo(data,10);BitConverter.GetBytes((ushort)(substitute.Length+2)).CopyTo(data,12);BitConverter.GetBytes((ushort)print.Length).CopyTo(data,14);substitute.CopyTo(data,16);print.CopyTo(data,18+substitute.Length);
        using var handle=CreateFile(path,0x40000000,0,IntPtr.Zero,3,0x02200000,IntPtr.Zero);Assert.False(handle.IsInvalid);Assert.True(DeviceIoControl(handle,0x000900A4,data,data.Length,IntPtr.Zero,0,out _,IntPtr.Zero));
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool CreateHardLink(string link,string existing,IntPtr security);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern SafeFileHandle CreateFile(string path,uint access,uint sharing,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool DeviceIoControl(SafeFileHandle handle,uint code,byte[] input,int length,IntPtr output,int outputLength,out int returned,IntPtr overlapped);
    [Fact]
    public void A_DirectoryPin_IsReadOnlyAndRejectsTraversal()
    {
        var root=Path.Combine(Path.GetTempPath(),"nexus-i2a-pin-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var type=typeof(SafePath).Assembly.GetType("Nexus.AI.EditingLite.Services.ReadOnlyPathScope");Assert.NotNull(type);
            using var scope=(IDisposable)Activator.CreateInstance(type!,[root])!;
            var method=type!.GetMethod("EnumerateDirectory")!;
            Assert.Throws<System.Reflection.TargetInvocationException>(()=>method.Invoke(scope,["../outside",4096,CancellationToken.None]));
        }
        finally {Directory.Delete(root,true);}
    }
}
