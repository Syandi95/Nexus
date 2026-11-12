using System.Diagnostics;
using System.Text;
using Nexus.AI.EditingLite.Services;

namespace Nexus.AI.EditingLite.Tests.Services;

// Engineering provisioning, before Agent/workflow admission. Restore never an Agent tool.
internal sealed class SourcePlanFixture : IDisposable
{
    internal const string Sdk=@"C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe";
    public string Root { get; }=Path.Combine(Path.GetTempPath(),"nexus-source-plan-"+Guid.NewGuid().ToString("N"));
    public EditingWorkflowService Editing { get; }=new(new FileSnapshotService(),new ProposalService(),new SyntaxValidatorService(),new FileWriterService());
    public SourceVerificationProfile Profile { get; private set; }=null!;
    public string PathOf(string target)=>Path.Combine(Root,target.Replace('/',Path.DirectorySeparatorChar));
    public string Hash(string target)=>new FileSnapshotService().ComputeSha256Hash(PathOf(target));
    public static async Task<SourcePlanFixture> Create()
    {
        var f=new SourcePlanFixture(); Directory.CreateDirectory(f.PathOf("Product")); Directory.CreateDirectory(f.PathOf("Tests"));
        File.WriteAllText(f.PathOf("Directory.Build.props"),"<Project />"); File.WriteAllText(f.PathOf("Directory.Build.targets"),"<Project />");
        File.WriteAllText(f.PathOf("global.json"),"{\"sdk\":{\"version\":\"8.0.424\",\"rollForward\":\"disable\"}}");
        foreach(var name in new[] { "Alpha","Beta","Gamma" }) File.WriteAllText(f.PathOf("Product/"+name+".cs"),$"namespace SourceFixture;\npublic static class {name} {{ public static int Value() {{ return 1; }} }}\n",new UTF8Encoding(false));
        const string properties="<TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><UseSharedCompilation>false</UseSharedCompilation><ImportDirectoryBuildProps>false</ImportDirectoryBuildProps><ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>";
        File.WriteAllText(f.PathOf("Product/Product.csproj"),"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>"+properties+"</PropertyGroup><ItemGroup><Compile Include=\"Alpha.cs\"/><Compile Include=\"Beta.cs\"/><Compile Include=\"Gamma.cs\"/></ItemGroup></Project>");
        File.WriteAllText(f.PathOf("Tests/Tests.csproj"),"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>"+properties+"<IsTestProject>true</IsTestProject></PropertyGroup><ItemGroup><ProjectReference Include=\"../Product/Product.csproj\"/><Compile Include=\"FixtureTests.cs\"/></ItemGroup><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.8.0\"/><PackageReference Include=\"xunit\" Version=\"2.5.3\"/><PackageReference Include=\"xunit.runner.visualstudio\" Version=\"2.5.3\"/></ItemGroup></Project>");
        File.WriteAllText(f.PathOf("Tests/FixtureTests.cs"),"using Xunit; using SourceFixture; namespace SourceFixtureTests; public sealed class RequiredFacts { [Fact] public void AlphaDesired() { Assert.Equal(2,Alpha.Value()); } [Fact] public void BetaDesired() { Assert.Equal(2,Beta.Value()); } [Fact] public void GammaBounded() { Assert.InRange(Gamma.Value(),1,2); } }");
        await f.Provision("restore","Tests/Tests.csproj","--verbosity","quiet");
        await f.Provision("msbuild","Tests/Tests.csproj","-preprocess:"+f.PathOf("sealed-build.xml"),"-p:ImportDirectoryBuildProps=false","-p:ImportDirectoryBuildTargets=false");
        var graph=File.ReadAllText(f.PathOf("sealed-build.xml"));
        if(graph.Contains("AutoSignNexusOutputs") || graph.Contains("powershell",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected hook.");
        f.Profile=new("certified-csharp-fixture-v1",f.Root,Sdk,["Product/Alpha.cs","Product/Beta.cs","Product/Gamma.cs"]);
        return f;
    }
    private async Task Provision(params string[] arguments)
    {
        var info=new ProcessStartInfo(Sdk) { WorkingDirectory=Root,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true };
        foreach(var key in info.Environment.Keys.Where(k=>k.StartsWith("MSBuild",StringComparison.OrdinalIgnoreCase)||k.StartsWith("DOTNET_MSBUILD",StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(key);
        info.Environment["DOTNET_ROOT"]=Path.GetDirectoryName(Sdk)!;
        info.Environment["DOTNET_HOST_PATH"]=Sdk;
        info.Environment["DOTNET_MULTILEVEL_LOOKUP"]="0";
        foreach(var arg in arguments) info.ArgumentList.Add(arg);
        using var process=Process.Start(info)!; var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if(!process.HasExited) process.Kill(true); throw; }
        await Task.WhenAll(stdout,stderr); if(process.ExitCode!=0) throw new InvalidOperationException("Provisioning failed: "+await stdout+await stderr);
    }
    public SourceRunControl Control(CancellationToken token=default)
    {
        var clock=Stopwatch.StartNew(); return new(()=>TimeSpan.FromSeconds(120)-clock.Elapsed,token,()=>{});
    }
    public Task<SourceEditPlan> Plan(SourceRunControl run,params SourceFileEdit[] edits)=>Editing.PrepareSourcePlanAsync(Profile,
        edits.Length==0 ? [new("Product/Alpha.cs",Hash("Product/Alpha.cs"),"return 1;","return 2;"),new("Product/Beta.cs",Hash("Product/Beta.cs"),"return 1;","return 2;")] : edits,
        Guid.NewGuid(),"source-call",new(),run);
    public void Dispose() { if(Directory.Exists(Root)) Directory.Delete(Root,true); }
}

internal sealed class SourceApproval(Func<SourceEditPlan,SourcePlanApproval>? decision=null) : IWriteApprovalProvider
{
    public int Calls;
    public Task<WriteApprovalDecision> RequestAsync(SafeWriteProposal p,CancellationToken token)=>throw new InvalidOperationException("Per-file approval forbidden in plan proof.");
    public Task<SourcePlanApproval> RequestPlanAsync(SourceEditPlan p,CancellationToken token)
    { Calls++; token.ThrowIfCancellationRequested(); return Task.FromResult(decision?.Invoke(p) ?? SourcePlanApproval.For(p,true)); }
}
