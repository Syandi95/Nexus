using System.Diagnostics;
using System.Text.Json;
using Nexus.AI.EditingLite.Services;
using Xunit.Abstractions;

namespace Nexus.AI.EditingLite.Tests.Services;

public sealed class CompilerSdkCouplingTests(ITestOutputHelper output)
{
    [Fact]
    public async Task PinnedSdk_EmitsPerProjectSarif_AndTypedCurrentCscEvents()
    {
        using var f=await SourcePlanFixture.Create(); var sdkDirectory=Path.Combine(Path.GetDirectoryName(SourcePlanFixture.Sdk)!,"sdk","8.0.424");
        var id=Guid.NewGuid().ToString("N"); var sarif=".nexus-compiler-"+id+".sarif"; var log=f.PathOf("invocation-"+id+".binlog");
        async Task<int> Build()
        {
            var info=new ProcessStartInfo(SourcePlanFixture.Sdk) { WorkingDirectory=f.Root,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true };
            foreach(var arg in new[] { "build","Tests/Tests.csproj","--no-restore","--disable-build-servers","-t:Rebuild","-m:1","-nr:false","-p:UseSharedCompilation=false",
                "-p:ImportDirectoryBuildProps=false","-p:ImportDirectoryBuildTargets=false","-p:ErrorLog="+sarif+"%2cversion=2.1","-bl:"+log+";ProjectImports=None","--verbosity","quiet" }) info.ArgumentList.Add(arg);
            using var process=Process.Start(info)!; var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
            await Task.WhenAll(stdout,stderr); return process.ExitCode;
        }
        Assert.Equal(0,await Build());
        Assert.True(File.Exists(f.PathOf("Product/"+sarif))); Assert.True(File.Exists(f.PathOf("Tests/"+sarif))); Assert.False(File.Exists(f.PathOf(sarif)));
        var success=CompilerBuildLedger.Read(sdkDirectory,log,CancellationToken.None);
        Assert.Equal(2,success.Count(e=>e.Kind=="TaskStartedEventArgs" && e.Task=="Csc")); Assert.True(success.Last(e=>e.Kind=="BuildFinishedEventArgs").Succeeded);
        File.Delete(log); File.Delete(f.PathOf("Product/"+sarif)); File.Delete(f.PathOf("Tests/"+sarif));
        File.WriteAllText(f.PathOf("Product/Alpha.cs"),File.ReadAllText(f.PathOf("Product/Alpha.cs")).Replace("return 1;","return \"two\";"));
        Assert.Equal(1,await Build());
        var ledger=CompilerBuildLedger.Read(sdkDirectory,log,CancellationToken.None);
        var error=Assert.Single(ledger.Where(e=>e.Kind=="BuildErrorEventArgs")); Assert.Equal("CS0029",error.Code);
        var csc=Assert.Single(ledger.Where(e=>e.Kind=="TaskStartedEventArgs" && e.Task=="Csc")); Assert.Equal(csc.Context,error.Context);
        Assert.False(ledger.Last(e=>e.Kind=="BuildFinishedEventArgs").Succeeded);
        using var json=JsonDocument.Parse(File.ReadAllBytes(f.PathOf("Product/"+sarif)));
        Assert.Equal("2.1.0",json.RootElement.GetProperty("version").GetString());
        var diagnostic=Assert.Single(json.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()); Assert.Equal("CS0029",diagnostic.GetProperty("ruleId").GetString());
        Assert.False(File.Exists(f.PathOf("Tests/"+sarif)));
        output.WriteLine(JsonSerializer.Serialize(new { sdk="8.0.424",csc,error,failedTasks=ledger.Where(e=>e.Kind=="TaskFinishedEventArgs" && e.Succeeded==false),
            sarif=json.RootElement }));
    }
}
