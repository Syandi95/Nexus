using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace Nexus.AI.EditingLite.Services;

// A host-reviewed execution graph, not a repository loader or a sandbox.
// The caller must supply its compiled host pin, never a UI/model-supplied digest.
public sealed class ReviewedProjectVerificationManifest
{
    public const string ProfileId="contracts-i2b-p1-v1";
    public static ImmutableArray<string> WritableTargets {get;}=["Nexus.Contracts/EngineSessionOptions.cs","Nexus.Contracts/EngineMemoryMetrics.cs"];
    public string Root {get;}
    public string Sdk {get;}
    public string ManifestHash {get;}
    public ImmutableDictionary<string,string> Sources {get;}
    public ImmutableDictionary<string,string> Graph {get;}
    internal ImmutableDictionary<string,string> Dependencies {get;}
    private readonly ImmutableDictionary<string,string> artifacts;
    private readonly ImmutableHashSet<string> produced;
    private readonly object sync=new();
    private List<IDisposable>? dependencyLocks;
    private int leases;
    private ReviewedProjectVerificationManifest(string root,string pin,JsonElement json)
    {
        Root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));ManifestHash=pin;
        if(json.GetProperty("contract").GetString()!="NEXUS-I2B-REVIEWED-CONTRACTS-V1" || json.GetProperty("id").GetString()!=ProfileId ||
            !string.Equals(json.GetProperty("root").GetString(),Root,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Reviewed manifest identity denied.");
        Sdk=json.GetProperty("sdk").GetString()!;
        var sdkRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","dotnet");
        if(Sdk!=Path.Combine(sdkRoot,"dotnet.exe"))throw new ArgumentException("Fixed SDK required.");
        ImmutableDictionary<string,string> Map(string key)=>json.GetProperty(key).EnumerateObject().ToImmutableDictionary(p=>p.Name,p=>p.Value.GetString()!,StringComparer.Ordinal);
        Sources=Map("sources");Graph=Map("graph").Add("nexus-reviewed-i2b.json",pin);Dependencies=Map("dependencies");artifacts=Map("artifacts");
        produced=json.GetProperty("produced_artifacts").EnumerateArray().Select(p=>p.GetString()!).ToImmutableHashSet(StringComparer.Ordinal);
        if(Sources.Count!=17 || !WritableTargets.All(Sources.ContainsKey) || Sources.Keys.Any(p=>!p.StartsWith("Nexus.Contracts/",StringComparison.Ordinal)||!p.EndsWith(".cs",StringComparison.Ordinal)) || Dependencies.Count==0)
            throw new ArgumentException("Reviewed source graph denied.");
        foreach(var path in Sources.Keys.Concat(Graph.Keys).Concat(artifacts.Keys))SafePath.ResolveRelative(Root,path);
        var packages=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".nuget","packages")+Path.DirectorySeparatorChar;
        foreach(var path in Dependencies.Keys)
            if(!Path.IsPathFullyQualified(path) || !(path.StartsWith(sdkRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||path.StartsWith(packages,StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Dependency root denied.");
        if(!Dependencies.ContainsKey(Sdk)||!Directory.Exists(Path.Combine(sdkRoot,"sdk","8.0.424")))throw new ArgumentException("SDK assets missing.");
        foreach(var target in new[] {"Contracts","Tests"})
        {
            var xml=XDocument.Load(SafePath.ResolveRelative(Root,$".nexus-verification/{target}.csproj"));
            var expected=target=="Contracts"?Sources.Keys.OrderBy(p=>p,StringComparer.Ordinal).Select(p=>"../"+p).ToArray():new[] {"RequiredFacts.cs"};
            if(!xml.Descendants("Compile").Select(e=>e.Attribute("Include")?.Value).SequenceEqual(expected)||xml.Descendants("Compile").Any(e=>e.Attributes().Count()!=1 || e.HasElements))
                throw new ArgumentException("Explicit compile graph required.");
            if(xml.Descendants("Exec").Any() || xml.Descendants("UsingTask").Any() || xml.Descendants("Import").Any(e=>
                !(e.Attribute("Sdk")?.Value=="Microsoft.NET.Sdk" && e.Attribute("Project")?.Value is "Sdk.props" or "Sdk.targets") &&
                !(target=="Tests" && e.Attribute("Sdk") is null && e.Attribute("Project")?.Value=="$(MSBuildToolsPath)/Microsoft.TestPlatform.targets")))
                throw new ArgumentException("Unreviewed execution graph denied.");
            foreach(var property in new[] {"ImportDirectoryBuildProps","ImportDirectoryBuildTargets","ImportDirectoryPackagesProps","EnableDefaultCompileItems","RunAnalyzers","EnableNETAnalyzers","UseSharedCompilation"})
                if(xml.Descendants(property).Single().Value!="false")throw new ArgumentException("Unsafe verification property.");
        }
        if(!LocalIntact())throw new IOException("Reviewed graph/source drift.");
    }
    public static ReviewedProjectVerificationManifest LoadPinned(string root,string expectedSha256)
    {
        using var lease=new SafePathLease(root,"nexus-reviewed-i2b.json");using var stream=lease.OpenTarget();
        if(stream.Length>4*1024*1024 || FileWriterService.Hash(stream)!=expectedSha256)throw new IOException("Host manifest pin mismatch.");
        stream.Position=0;using var doc=JsonDocument.Parse(stream);return new(root,expectedSha256,doc.RootElement);
    }
    private bool LocalIntact()=>Graph.Concat(Sources.Where(p=>!WritableTargets.Contains(p.Key,StringComparer.Ordinal))).All(p=>SourceVerificationProfile.HashFile(SafePath.ResolveRelative(Root,p.Key))==p.Value);
    internal bool DependenciesIntact()
    {
        lock(sync)
        {
            if(dependencyLocks is not null)return true; // All verified file handles deny write/delete until the final lease drains.
            try{return Dependencies.All(p=>string.Equals(p.Key,Sdk,StringComparison.OrdinalIgnoreCase)?File.Exists(SafeDependency(p.Key)):SourceVerificationProfile.HashFile(SafeDependency(p.Key))==p.Value);}catch{return false;}
        }
    }
    private static string SafeDependency(string file)=>SafePath.ResolveRelative(Path.GetDirectoryName(file)!,Path.GetFileName(file));
    public IDisposable LockDependencies(CancellationToken cancellation=default)
    {
        lock(sync)
        {
            cancellation.ThrowIfCancellationRequested();
            if(dependencyLocks is not null){leases++;return new DependencyLease(this);}
            var held=new List<IDisposable>();
            try
            {
                foreach(var item in Dependencies.OrderBy(p=>p.Key,StringComparer.Ordinal))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var lease=new SafePathLease(Path.GetDirectoryName(item.Key)!,Path.GetFileName(item.Key));held.Add(lease);
                    var stream=lease.OpenTarget();held.Add(stream);
                    if(!string.Equals(item.Key,Sdk,StringComparison.OrdinalIgnoreCase) && FileWriterService.Hash(stream)!=item.Value)throw new IOException("Pinned dependency changed: "+item.Key);
                    cancellation.ThrowIfCancellationRequested();
                }
                dependencyLocks=held;leases=1;return new DependencyLease(this);
            }
            catch{foreach(var item in held.AsEnumerable().Reverse())item.Dispose();throw;}
        }
    }
    private sealed class DependencyLease(ReviewedProjectVerificationManifest owner):IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if(Interlocked.Exchange(ref disposed,1)!=0)return;
            lock(owner.sync)if(--owner.leases==0){foreach(var item in owner.dependencyLocks!.AsEnumerable().Reverse())item.Dispose();owner.dependencyLocks=null;}
        }
    }
    internal bool ArtifactsMatch()
    {
        try
        {
            var files=Directory.EnumerateFiles(Path.Combine(Root,".nexus-verification","bin"),"*",SearchOption.AllDirectories).Select(p=>Path.GetRelativePath(Root,p).Replace('\\','/')).ToArray();
            return files.Length==artifacts.Count && files.All(p=>artifacts.ContainsKey(p) && (produced.Contains(p)||SourceVerificationProfile.HashFile(SafePath.ResolveRelative(Root,p))==artifacts[p]));
        }
        catch{return false;}
    }
    internal IDisposable LockArtifacts()
    {
        if(!ArtifactsMatch())throw new IOException("Verification artifact inventory changed.");
        var locks=new List<IDisposable>();
        try
        {
            foreach(var path in artifacts.Keys){var lease=new SafePathLease(Root,path);locks.Add(lease);locks.Add(lease.OpenTarget());}
            if(!ArtifactsMatch())throw new IOException("Verification artifact drift.");
            return new ArtifactLease(locks);
        }
        catch{foreach(var item in locks.AsEnumerable().Reverse())item.Dispose();throw;}
    }
    private sealed class ArtifactLease(List<IDisposable> locks):IDisposable
    {public void Dispose(){foreach(var item in locks.AsEnumerable().Reverse())item.Dispose();}}
}
