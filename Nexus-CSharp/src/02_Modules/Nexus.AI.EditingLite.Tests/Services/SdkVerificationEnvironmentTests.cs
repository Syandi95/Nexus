using System.Diagnostics;
using Nexus.AI.EditingLite.Services;

namespace Nexus.AI.EditingLite.Tests.Services;

public sealed class SdkVerificationEnvironmentTests
{
    [Fact]
    public void MarkerFlushFailure_RetiresOwnedDirectory_AndPreservesOriginalFailure()
    {
        string? directory=null;
        try
        {
            var error=Assert.Throws<IOException>(()=>new VerificationTempDirectory(path=>
            {
                directory=Path.GetDirectoryName(path);
                return new FailedMarkerFlush(path);
            }));
            Assert.Equal("marker flush failure",error.Message);
            Assert.NotNull(directory);Assert.False(Directory.Exists(directory));
        }
        finally
        {
            // This invocation owns the unique leaf captured from its stream factory.
            // Nonrecursive cleanup refuses to remove any unexpected remaining data.
            if(directory is not null && Directory.Exists(directory))Directory.Delete(directory);
        }
    }
    private sealed class FailedMarkerFlush(string path):FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)
    {
        public override void Flush(bool flushToDisk)
        {base.Flush(flushToDisk);throw new IOException("marker flush failure");}
    }
    [Fact]
    public async Task FixedSdkInvocation_UsesPrivateLowTemp_VerifiedJob_AndLeavesGlobalEnvironmentUnchanged()
    {
        using var f=await SourcePlanFixture.Create();
        var temp=Environment.GetEnvironmentVariable("TEMP");var tmp=Environment.GetEnvironmentVariable("TMP");
        string? ownedTemp=null;int pid=0;
        var verifier=new SourceVerification();
        verifier.InvocationProbe=(info,process)=>
        {
            ownedTemp=info.Environment["TEMP"];pid=process.Id;
            Assert.Equal(ownedTemp,info.Environment["TMP"]);
            Assert.Contains("LocalLow",ownedTemp);
            Assert.True(Directory.Exists(ownedTemp));
            Assert.True(process.MembershipVerifiedBeforeResume);
            Assert.Equal(SourcePlanFixture.Sdk,info.FileName);
            Assert.Equal("Tests/Tests.csproj",info.ArgumentList[1].Replace('\\','/'));
            Assert.Equal(temp,Environment.GetEnvironmentVariable("TEMP"));
            Assert.Equal(tmp,Environment.GetEnvironmentVariable("TMP"));
        };
        var result=await verifier.BuildAsync(f.Profile,f.Control());
        Assert.True(result.Result.Passed,result.Result.Output);
        Assert.NotNull(ownedTemp);Assert.False(Directory.Exists(ownedTemp));
        Assert.Throws<ArgumentException>(()=>Process.GetProcessById(pid));
        Assert.Equal(temp,Environment.GetEnvironmentVariable("TEMP"));Assert.Equal(tmp,Environment.GetEnvironmentVariable("TMP"));
    }

    [Fact]
    public async Task CancelSdkAfterOwnedLaunch_DrainsJobAndRetiresTemp_BeforeSourceRecovery()
    {
        using var f=await SourcePlanFixture.Create();using var cancellation=new CancellationTokenSource();
        string? ownedTemp=null;
        f.Editing.SourceVerifier.InvocationProbe=(info,process)=>
        { Assert.True(process.MembershipVerifiedBeforeResume);ownedTemp=info.Environment["TEMP"];cancellation.Cancel(); };
        var plan=await f.Plan(f.Control());
        var result=await f.Editing.ExecuteSourcePlanAsync(plan,new SourceApproval(),f.Control(cancellation.Token));
        Assert.Equal(SourcePlanOutcome.Cancelled,result.Outcome);Assert.True(result.BaselineVerified);
        Assert.NotNull(ownedTemp);Assert.False(Directory.Exists(ownedTemp));
        Assert.All(plan.Entries,p=>Assert.Equal(p.BeforeHash,f.Hash(p.Target)));
    }

    [Fact]
    public void PrivateDirectory_IdentityConflictPreservesDataInsteadOfUnownedCleanup()
    {
        var temporary=new VerificationTempDirectory();var path=temporary.Path;
        var marker=Path.Combine(path,".nexus-owner");var original=File.ReadAllText(marker);
        File.WriteAllText(marker,"changed-owner");
        Assert.Throws<IOException>(()=>temporary.Dispose());Assert.True(Directory.Exists(path));
        File.WriteAllText(marker,original);temporary.Dispose();Assert.False(Directory.Exists(path));
    }

    [Fact]
    public async Task DrainingOneSdkJob_DoesNotTerminateAnotherIndependentlyOwnedBuild()
    {
        using var first=await SourcePlanFixture.Create();using var second=await SourcePlanFixture.Create();
        using var one=OwnedSdkProcess.Start(BuildInfo(first.Root));using var two=OwnedSdkProcess.Start(BuildInfo(second.Root));
        Assert.True(one.MembershipVerifiedBeforeResume);Assert.True(two.MembershipVerifiedBeforeResume);
        Assert.False(one.Contains(two));Assert.False(two.Contains(one));
        var output=two.StandardOutput.ReadToEndAsync();var error=two.StandardError.ReadToEndAsync();
        await one.DrainJobAsync();Assert.True(one.HasExited);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));await two.WaitForExitAsync(timeout.Token);
        await two.DrainJobAsync();await Task.WhenAll(output,error);
        Assert.Equal(0,two.ExitCode);
    }
    private static ProcessStartInfo BuildInfo(string root)
    {
        var info=new ProcessStartInfo(SourcePlanFixture.Sdk) { WorkingDirectory=root };
        foreach(var a in new[] {"build","Tests/Tests.csproj","--no-restore","--disable-build-servers","-m:1","-p:UseSharedCompilation=false","--verbosity","quiet"})info.ArgumentList.Add(a);
        return info;
    }

    [Theory]
    [InlineData("", "\"\"")]
    [InlineData("Tests/Tests.csproj", "Tests/Tests.csproj")]
    [InlineData("C:\\a b\\", "\"C:\\a b\\\\\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    public void NativeArgvQuoting_PreservesExactFixedArguments(string input,string expected)=>Assert.Equal(expected,OwnedSdkProcess.Quote(input));
}
