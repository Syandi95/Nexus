using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Nexus.AI.EditingLite.Services;

// Host-created immutable catalog entry, not model arguments. Reviewed code is
// executable with user authority: this profile is not a sandbox.
public sealed class SourceVerificationProfile
{
    public string Id { get; }
    public string Root { get; }
    public string Sdk { get; }
    public ImmutableArray<string> Targets { get; }
    public ImmutableDictionary<string,string> GraphHashes { get; }
    public ApprovedTextWorkspace Workspace { get; }
    public string Digest { get; }
    public bool CompilerObservationEnabled { get; }
    internal string SdkDirectory=>Path.Combine(Path.GetDirectoryName(Sdk)!,"sdk","8.0.424");
    internal ImmutableDictionary<string,string> CompilerSdkHashes { get; }
    private readonly string sdkHash;
    public ImmutableDictionary<string,string> InitialSourceHashes { get; }
    public TimeSpan BuildTimeout=>TimeSpan.FromSeconds(15);
    public TimeSpan TestTimeout=>TimeSpan.FromSeconds(15);
    public int DiagnosticBytes=>4096;
    public int StructuredResultBytes=>32768;
    internal ImmutableDictionary<string,string> EnvironmentValues { get; }
    public ReviewedProjectVerificationManifest? ReviewedManifest {get;}
    internal string TestProjectRelative=>ReviewedManifest is null?Path.Combine("Tests","Tests.csproj"):Path.Combine(".nexus-verification","Tests.csproj");
    internal string TestFilter=>ReviewedManifest is null?"SourceFixtureTests.RequiredFacts":"I2BContracts.RequiredFacts";
    internal string[] RequiredTests=>ReviewedManifest is null?["SourceFixtureTests.RequiredFacts.AlphaDesired","SourceFixtureTests.RequiredFacts.BetaDesired","SourceFixtureTests.RequiredFacts.GammaBounded"]:["I2BContracts.RequiredFacts.CpuFlag","I2BContracts.RequiredFacts.ActiveSessionFlag","I2BContracts.RequiredFacts.Compatibility"];
    internal string TestProject=>Path.Combine(Root,TestProjectRelative);
    internal string ProductBinary=>ReviewedManifest is null?Path.Combine(Root,"Product","bin","Debug","net8.0","Product.dll"):Path.Combine(Root,".nexus-verification","bin","Contracts","Debug","net8.0","Nexus.Contracts.I2B.dll");
    internal string TestBinary=>ReviewedManifest is null?Path.Combine(Root,"Tests","bin","Debug","net8.0","Tests.dll"):Path.Combine(Root,".nexus-verification","bin","Tests","Debug","net8.0","I2BContracts.Tests.dll");
    internal static readonly string[] GraphFiles=["Product/Product.csproj","Tests/Tests.csproj","Tests/FixtureTests.cs","global.json","Directory.Build.props","Directory.Build.targets",
        "Product/obj/project.assets.json","Tests/obj/project.assets.json","Product/obj/Product.csproj.nuget.g.props","Product/obj/Product.csproj.nuget.g.targets",
        "Tests/obj/Tests.csproj.nuget.g.props","Tests/obj/Tests.csproj.nuget.g.targets"];

    public SourceVerificationProfile(string id,string reviewedRoot,string sdk,IEnumerable<string> exactTargets,bool observeCompiler=false)
    {
        Root=Path.GetFullPath(reviewedRoot); Id=id; Sdk=Path.GetFullPath(sdk);
        if(string.IsNullOrWhiteSpace(id) || !File.Exists(Sdk) || Path.GetFileName(Sdk)!="dotnet.exe" ||
            !Directory.Exists(Path.Combine(Path.GetDirectoryName(Sdk)!,"sdk","8.0.424"))) throw new ArgumentException("Pinned SDK 8.0.424 required.");
        Targets=exactTargets.ToImmutableArray();
        if(Targets.Length is <2 or >3 || Targets.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=Targets.Length ||
            Targets.Any(t=>!new[] { "Product/Alpha.cs","Product/Beta.cs","Product/Gamma.cs" }.Contains(t,StringComparer.Ordinal))) throw new ArgumentException("Unknown/duplicate fixture mapping.");
        Workspace=ApprovedTextWorkspace.ForSourceFiles(Root,Targets);
        foreach(var target in Targets) Workspace.ResolveTarget(target);
        // Fixed reviewed fixture graph: explicit compile items, no linked files,
        // imports/Exec/custom targets. Do not accept arbitrary project XML.
        ValidateProject(Path.Combine(Root,"Product","Product.csproj"),false);
        ValidateProject(TestProject,true);
        if(File.ReadAllText(Path.Combine(Root,"Directory.Build.props")).Trim()!="<Project />" ||
            File.ReadAllText(Path.Combine(Root,"Directory.Build.targets")).Trim()!="<Project />") throw new ArgumentException("Foreign hooks denied.");
        using(var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,"global.json"))))
            if(json.RootElement.GetProperty("sdk").GetProperty("version").GetString()!="8.0.424" || json.RootElement.GetProperty("sdk").GetProperty("rollForward").GetString()!="disable") throw new ArgumentException("SDK graph not pinned.");
        GraphHashes=GraphFiles.ToImmutableDictionary(f=>f,f=>HashFile(ResolveGraph(f)),StringComparer.Ordinal);
        InitialSourceHashes=new[] { "Product/Alpha.cs","Product/Beta.cs","Product/Gamma.cs" }.ToImmutableDictionary(f=>f,f=>HashFile(ResolveGraph(f)),StringComparer.Ordinal);
        sdkHash=HashFile(Sdk);
        CompilerObservationEnabled=observeCompiler;
        CompilerSdkHashes=observeCompiler ? new[] { "Microsoft.Build.dll","Microsoft.Build.Framework.dll","Microsoft.NET.StringTools.dll",
            "Microsoft.Common.CurrentVersion.targets","Roslyn/Microsoft.CSharp.Core.targets","Roslyn/Microsoft.Build.Tasks.CodeAnalysis.dll",
            "Roslyn/bincore/Microsoft.CodeAnalysis.dll","Roslyn/bincore/Microsoft.CodeAnalysis.CSharp.dll" }.ToImmutableDictionary(f=>f,f=>HashFile(Path.Combine(SdkDirectory,f))) : ImmutableDictionary<string,string>.Empty;
        if(observeCompiler && (!System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(SdkDirectory,"Microsoft.Build.dll")).ProductVersion!.StartsWith("17.11.48",StringComparison.Ordinal) ||
            !System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(SdkDirectory,"Roslyn/bincore/Microsoft.CodeAnalysis.CSharp.dll")).ProductVersion!.StartsWith("4.11.0-3.25569.22",StringComparison.Ordinal)))
            throw new NotSupportedException("Pinned compiler instrumentation prerequisite failed.");
        var env=new Dictionary<string,string>();
        foreach(var name in new[] { "SystemRoot","WINDIR","TEMP","TMP","USERPROFILE","APPDATA","LOCALAPPDATA","ProgramFiles","ProgramFiles(x86)" })
            if(System.Environment.GetEnvironmentVariable(name) is string value) env[name]=value;
        env["DOTNET_CLI_TELEMETRY_OPTOUT"]="1"; env["DOTNET_NOLOGO"]="1"; env["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"]="1";
        env["MSBUILDDISABLENODEREUSE"]="1"; env["DOTNET_MULTILEVEL_LOOKUP"]="0";
        EnvironmentValues=env.ToImmutableDictionary();
        Digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Id,Root,Sdk,sdkHash,Targets,GraphHashes,InitialSourceHashes,EnvironmentValues,BuildTimeout,TestTimeout,DiagnosticBytes })))).ToLowerInvariant();
        if(observeCompiler) Digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { baseline=Digest,CompilerSdkHashes,
            instrumentation="sarif-2.1-and-msbuild-17.11.48-v1",CompilerArtifacts.SarifLimit,CompilerBuildLedger.CompressedLimit,CompilerBuildLedger.ExpandedLimit,CompilerBuildLedger.EventLimit })))).ToLowerInvariant();
    }
    public SourceVerificationProfile(ReviewedProjectVerificationManifest manifest)
    {
        ReviewedManifest=manifest;Root=manifest.Root;Id=ReviewedProjectVerificationManifest.ProfileId;Sdk=manifest.Sdk;Targets=ReviewedProjectVerificationManifest.WritableTargets;
        Workspace=ApprovedTextWorkspace.ForSourceFiles(Root,Targets);foreach(var target in Targets)Workspace.ResolveTarget(target);
        GraphHashes=manifest.Graph.AddRange(manifest.Sources.Where(p=>!Targets.Contains(p.Key,StringComparer.Ordinal)));
        InitialSourceHashes=manifest.Sources;sdkHash=HashFile(Sdk);CompilerSdkHashes=ImmutableDictionary<string,string>.Empty;CompilerObservationEnabled=false;
        var env=new Dictionary<string,string>();
        foreach(var name in new[] {"SystemRoot","WINDIR","TEMP","TMP","USERPROFILE","APPDATA","LOCALAPPDATA","ProgramFiles","ProgramFiles(x86)"})
            if(System.Environment.GetEnvironmentVariable(name) is string value)env[name]=value;
        env["DOTNET_CLI_TELEMETRY_OPTOUT"]="1";env["DOTNET_NOLOGO"]="1";env["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"]="1";env["MSBUILDDISABLENODEREUSE"]="1";env["DOTNET_MULTILEVEL_LOOKUP"]="0";env["MSBuildEnableWorkloadResolver"]="false";
        EnvironmentValues=env.ToImmutableDictionary();
        Digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {Id,Root,Sdk,sdkHash,manifest.ManifestHash,Targets,GraphHashes,InitialSourceHashes,EnvironmentValues,BuildTimeout,TestTimeout})))).ToLowerInvariant();
        if(!IsIntact())throw new IOException("Reviewed verification profile not ready.");
    }
    public IDisposable AcquireExecutionLease(CancellationToken cancellation=default)=>ReviewedManifest?.LockDependencies(cancellation) ?? new GraphLease([]);
    private string ResolveGraph(string relative)=>SafePath.ResolveRelative(Root,relative);
    public bool IsIntact()
    { try { return HashFile(Sdk)==sdkHash && GraphHashes.All(kv=>HashFile(ResolveGraph(kv.Key))==kv.Value) && CompilerSdkHashes.All(kv=>HashFile(Path.Combine(SdkDirectory,kv.Key))==kv.Value) && (ReviewedManifest?.DependenciesIntact()??true); } catch { return false; } }
    internal IDisposable LockSources(IReadOnlyDictionary<string,string> expected)
    {
        var locks=new List<IDisposable>();
        try
        {
            foreach(var file in expected)
            {
                var lease=new SafePathLease(Root,file.Key); locks.Add(lease); var stream=lease.OpenTarget(); locks.Add(stream);
                if(FileWriterService.Hash(stream)!=file.Value) throw new IOException("Verification source state changed.");
            }
            return new GraphLease(locks);
        }
        catch { foreach(var item in locks) item.Dispose(); throw; }
    }
    internal IDisposable LockGraph()
    {
        var locks=new List<IDisposable>();
        try
        {
            if(ReviewedManifest is not null)locks.Add(ReviewedManifest.LockDependencies());
            foreach(var item in GraphHashes)
            {
                var lease=new SafePathLease(Root,item.Key); locks.Add(lease); var stream=lease.OpenTarget(); locks.Add(stream);
                if(FileWriterService.Hash(stream)!=item.Value) throw new IOException("Verification graph changed.");
            }
            return new GraphLease(locks);
        }
        catch { foreach(var item in locks) item.Dispose(); throw; }
    }
    private sealed class GraphLease(List<IDisposable> locks) : IDisposable
    { public void Dispose() { foreach(var item in locks.AsEnumerable().Reverse()) item.Dispose(); } }
    internal static string HashFile(string path)
    { using var f=File.OpenRead(path); return FileWriterService.Hash(f); }

    private static void ValidateProject(string path,bool tests)
    {
        var root=XDocument.Load(path).Root!;
        var allowed=new HashSet<string>(["Project","PropertyGroup","ItemGroup","TargetFramework","EnableDefaultCompileItems","UseSharedCompilation","ImportDirectoryBuildProps","ImportDirectoryBuildTargets","IsTestProject","Compile","ProjectReference","PackageReference"]);
        if(root.Name!="Project" || root.Attributes().Count()!=1 || root.Attribute("Sdk")?.Value!="Microsoft.NET.Sdk" || root.Descendants().Any(e=>!allowed.Contains(e.Name.LocalName))) throw new ArgumentException("Build graph element denied.");
        var expectedProperties=new Dictionary<string,string> { ["TargetFramework"]="net8.0",["EnableDefaultCompileItems"]="false",["UseSharedCompilation"]="false",["ImportDirectoryBuildProps"]="false",["ImportDirectoryBuildTargets"]="false" };
        if(tests) expectedProperties["IsTestProject"]="true";
        var props=root.Elements("PropertyGroup").Elements().ToArray();
        if(props.Length!=expectedProperties.Count || props.Any(p=>p.HasAttributes || !expectedProperties.TryGetValue(p.Name.LocalName,out var value) || p.Value!=value) || props.Select(p=>p.Name).Distinct().Count()!=props.Length) throw new ArgumentException("Build properties denied.");
        var compile=root.Descendants("Compile").ToArray(); var names=tests ? new[] { "FixtureTests.cs" } : new[] { "Alpha.cs","Beta.cs","Gamma.cs" };
        if(!compile.Select(e=>e.Attribute("Include")?.Value).SequenceEqual(names) || compile.Any(e=>e.Attributes().Count()!=1 || e.HasElements)) throw new ArgumentException("Compile mapping denied.");
        var refs=root.Descendants("ProjectReference").ToArray();
        if(tests ? refs.Length!=1 || refs[0].Attribute("Include")?.Value!="../Product/Product.csproj" || refs[0].Attributes().Count()!=1 || refs[0].HasElements : refs.Length!=0) throw new ArgumentException("Project mapping denied.");
        var packages=root.Descendants("PackageReference").ToArray();
        var expected=tests ? new Dictionary<string,string> { ["Microsoft.NET.Test.Sdk"]="17.8.0",["xunit"]="2.5.3",["xunit.runner.visualstudio"]="2.5.3" } : new Dictionary<string,string>();
        if(packages.Length!=expected.Count || packages.Any(p=>p.HasElements || p.Attributes().Count()!=2 || !expected.TryGetValue(p.Attribute("Include")?.Value ?? "",out var v) || p.Attribute("Version")?.Value!=v) || packages.Select(p=>p.Attribute("Include")?.Value).Distinct().Count()!=packages.Length) throw new ArgumentException("Dependencies denied.");
        if(root.Elements().Any(e=>e.HasAttributes)) throw new ArgumentException("Conditional/custom graph denied.");
    }
}
