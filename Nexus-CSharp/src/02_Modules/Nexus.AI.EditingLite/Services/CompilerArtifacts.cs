using System.Security.Cryptography;
using System.Text.Json;

namespace Nexus.AI.EditingLite.Services;

internal sealed class CompilerArtifacts : IDisposable
{
    internal const int SarifLimit=1024*1024; // Pinned SDK emits ~338KB of rule descriptors, not model feedback.
    internal Guid Id { get; }=Guid.NewGuid();
    private readonly SourceVerificationProfile profile;
    private readonly DateTime created=DateTime.UtcNow;
    internal string SarifName=>".nexus-compiler-"+Id.ToString("N")+".sarif";
    internal string LedgerRelative=>".nexus-build-"+Id.ToString("N")+"/build.binlog";
    internal string LedgerPath=>SafePath.ResolveRelative(profile.Root,LedgerRelative);
    internal CompilerArtifacts(SourceVerificationProfile p)
    {
        profile=p;
        var directory=SafePath.ResolveRelative(p.Root,".nexus-build-"+Id.ToString("N"));
        if(Directory.Exists(directory) || new[] { "Product/","Tests/" }.Any(project=>File.Exists(SafePath.ResolveRelative(p.Root,project+SarifName)))) throw new IOException("Compiler artifact collision.");
        Directory.CreateDirectory(directory);
    }
    internal string[] Instrumentation()=>["-p:ErrorLog="+SarifName+"%2cversion=2.1","-bl:"+LedgerPath+";ProjectImports=None"];
    private byte[] Read(string relative,int maximum)
    {
        using var lease=new SafePathLease(profile.Root,relative); using var file=lease.OpenTarget();
        if(file.Length is <=0 || file.Length>maximum || File.GetLastWriteTimeUtc(lease.TargetPath)<created.AddSeconds(-1)) throw new IOException("Compiler artifact size/freshness denied.");
        var bytes=new byte[(int)file.Length]; file.ReadExactly(bytes); return bytes;
    }
    internal Action<List<CompilerBuildEvent>>? LedgerProbe { get; set; }
    internal SourceProcessResult Complete(SourceProcessResult process,SourceEditPlan? plan,int attempt,SourceRunControl control)
    {
        if(process.Category!="ProcessExited" || process.ExitCode is not (0 or 1)) return process;
        try
        {
            if(!profile.IsIntact()) throw new IOException("Compiler profile changed.");
            var ledgerBytes=Read(LedgerRelative,CompilerBuildLedger.CompressedLimit);
            control.Check();
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(control.Cancellation);
            deadline.CancelAfter(control.Remaining()<TimeSpan.FromSeconds(2) ? control.Remaining() : TimeSpan.FromSeconds(2));
            var events=CompilerBuildLedger.ReadBytes(profile.SdkDirectory,ledgerBytes,deadline.Token).ToList(); LedgerProbe?.Invoke(events);
            if(events.Count(e=>e.Kind=="BuildStartedEventArgs")!=1 || events.Count(e=>e.Kind=="BuildFinishedEventArgs")!=1 ||
                events.Last().Kind!="BuildFinishedEventArgs" || events.Last().Succeeded!=process.Passed || events.First(e=>e.Kind=="BuildStartedEventArgs").Timestamp<created.AddSeconds(-1)) throw new IOException("Incomplete compiler invocation.");
            var starts=events.Where(e=>e.Kind=="TaskStartedEventArgs" && e.Task=="Csc").ToArray();
            if(starts.Length is <1 or >2 || starts.Select(e=>e.Project).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=starts.Length) throw new IOException("Ambiguous Csc producers.");
            foreach(var start in starts)
            {
                var logical=Project(start.Project); if(logical is null || !Same(start.TaskFile,Path.Combine(profile.SdkDirectory,"Roslyn","Microsoft.CSharp.Core.targets")) ||
                    !Same(start.Assembly,Path.Combine(profile.SdkDirectory,"Roslyn","Microsoft.Build.Tasks.CodeAnalysis.dll")) ||
                    events.Count(e=>e.Kind=="TaskFinishedEventArgs" && e.Context==start.Context)!=1 ||
                    events.Count(e=>e.Kind=="TaskCommandLineEventArgs" && e.Context==start.Context && e.Message?.Contains("/errorlog:\""+SarifName+",version=2.1\"",StringComparison.Ordinal)==true)!=1)
                    throw new IOException("Unknown compiler origin/invocation.");
                var data=Read(logical+"/"+SarifName,SarifLimit);
                using var doc=JsonDocument.Parse(data,new JsonDocumentOptions { MaxDepth=16 }); NoDuplicateKeys(doc.RootElement);
                var run=Run(doc.RootElement);
                if(process.Passed && run.GetProperty("results").EnumerateArray().Any(e=>e.GetProperty("level").GetString()=="error")) throw new IOException("Unexpected error in successful build.");
            }
            control.Check(); if(process.Passed) return process;
            if(plan is null || attempt is <1 or >2 || !ReferenceEquals(plan.Profile,profile)) return process;
            var errors=events.Where(e=>e.Kind=="BuildErrorEventArgs").ToArray();
            if(errors.Length!=1 || starts.Length!=1 || errors[0].Code!="CS0029") return process;
            var csc=starts[0]; var error=errors[0];
            if(Project(csc.Project)!="Product" || error.Context!=csc.Context || !Same(error.Project,csc.Project) ||
                events.Single(e=>e.Kind=="TaskFinishedEventArgs" && e.Context==csc.Context).Succeeded!=false) return process;
            if(events.Where(e=>e.Kind=="TaskFinishedEventArgs" && e.Succeeded==false).Any(e=>e.Context!=csc.Context &&
                !(e.Task=="MSBuild" && Project(e.Project)=="Tests" && Same(e.TaskFile,Path.Combine(profile.SdkDirectory,"Microsoft.Common.CurrentVersion.targets"))))) return process;
            if(events.Where(e=>e.Kind=="TaskCommandLineEventArgs" && e.Context==csc.Context).Any(e=>e.Message?.Contains("/pathmap:",StringComparison.OrdinalIgnoreCase)==true)) return process;
            var sarif=Read("Product/"+SarifName,SarifLimit);
            return Seal(process,plan,attempt,events,sarif,ledgerBytes);
        }
        catch(OperationCanceledException) { return process with { Passed=false,Category=control.Cancellation.IsCancellationRequested ? "Cancelled" : "ProcessDeadline",CompilerFailure=null }; }
        catch(Exception error) { return process with { Passed=false,Category="CompilerArtifactFailure",CompilerFailure=null,CompilerArtifactError=error.GetType().Name+": "+error.Message }; }
    }
    private SourceProcessResult Seal(SourceProcessResult process,SourceEditPlan plan,int attempt,IReadOnlyList<CompilerBuildEvent> events,byte[] sarif,byte[] ledgerBytes)
    {
        using var doc=JsonDocument.Parse(sarif,new JsonDocumentOptions { MaxDepth=16 }); NoDuplicateKeys(doc.RootElement); var run=Run(doc.RootElement);
        var results=run.GetProperty("results").EnumerateArray().ToArray();
        if(results.Length!=1) return process;
        var diagnostic=results[0];
        if(diagnostic.GetProperty("ruleId").GetString()!="CS0029" || diagnostic.GetProperty("level").GetString()!="error" ||
            diagnostic.TryGetProperty("suppressions",out _) || diagnostic.TryGetProperty("relatedLocations",out _) || diagnostic.TryGetProperty("rule",out _)) return process;
        var driver=run.GetProperty("tool").GetProperty("driver"); var rule=driver.GetProperty("rules")[diagnostic.GetProperty("ruleIndex").GetInt32()];
        if(rule.GetProperty("id").GetString()!="CS0029" || rule.GetProperty("defaultConfiguration").GetProperty("level").GetString()!="error" ||
            rule.GetProperty("properties").GetProperty("category").GetString()!="Compiler" ||
            !rule.GetProperty("properties").GetProperty("tags").EnumerateArray().Any(t=>t.GetString()=="Compiler")) return process;
        const string message="Cannot implicitly convert type 'string' to 'int'";
        if(diagnostic.GetProperty("message").GetProperty("text").GetString()!=message) return process;
        var locations=diagnostic.GetProperty("locations").EnumerateArray().ToArray(); if(locations.Length!=1) return process;
        var physical=locations[0].GetProperty("physicalLocation"); var artifact=physical.GetProperty("artifactLocation");
        if(artifact.TryGetProperty("uriBaseId",out _)) return process;
        var raw=artifact.GetProperty("uri").GetString()!; string path;
        if(Uri.TryCreate(raw,UriKind.Absolute,out var uri)) { if(!uri.IsFile || !string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return process; path=uri.LocalPath; }
        else { if(raw.Contains('%') || raw.Contains("..") || raw.StartsWith('/') || raw.StartsWith('\\')) return process; path=Path.GetFullPath(raw,Path.Combine(profile.Root,"Product")); }
        var entry=plan.Entries.SingleOrDefault(e=>Same(path,SafePath.ResolveRelative(profile.Root,e.Target))); if(entry is null) return process;
        using var lease=new SafePathLease(profile.Root,entry.Target); using var source=lease.OpenTarget();
        var sourceHash=FileWriterService.Hash(source); if(sourceHash!=entry.DesiredHash || entry.DesiredText.Contains('#')) return process;
        var error=events.Single(e=>e.Kind=="BuildErrorEventArgs");
        var region=physical.GetProperty("region"); var line=region.GetProperty("startLine").GetInt32(); var column=region.GetProperty("startColumn").GetInt32();
        var lines=entry.DesiredText.Split('\n');
        if(!Same(path,error.File) || error.Code!="CS0029" || error.Message!=message || line!=error.Line || column!=error.Column || line<1 || line>lines.Length ||
            column<1 || column>lines[line-1].Length || run.GetProperty("columnKind").GetString()!="utf16CodeUnits" || !profile.IsIntact()) return process;
        var digest=Convert.ToHexString(SHA256.HashData(sarif.Concat(ledgerBytes).ToArray())).ToLowerInvariant();
        var receipt=new CompilerFailureReceipt(Id,attempt,plan,entry.Target,line,column,sourceHash,error.Context,digest);
        return process with { Category="RecognizedBoundCompilerFailure",CompilerFailure=receipt };
    }
    private JsonElement Run(JsonElement root)
    {
        if(root.GetProperty("version").GetString()!="2.1.0" || root.GetProperty("runs").GetArrayLength()!=1) throw new IOException("SARIF shape denied.");
        var run=root.GetProperty("runs")[0]; var driver=run.GetProperty("tool").GetProperty("driver");
        if(driver.GetProperty("name").GetString()!="Microsoft (R) Visual C# Compiler" || !driver.GetProperty("version").GetString()!.StartsWith("4.11.0-3.25569.22",StringComparison.Ordinal) ||
            run.GetProperty("tool").TryGetProperty("extensions",out _)) throw new IOException("Foreign compiler diagnostic producer.");
        return run;
    }
    private string? Project(string? project)=>Same(project,Path.Combine(profile.Root,"Product","Product.csproj")) ? "Product" : Same(project,profile.TestProject) ? "Tests" : null;
    private static bool Same(string? a,string? b)=>a is not null && b is not null && string.Equals(a.Replace('/','\\'),b.Replace('/','\\'),StringComparison.OrdinalIgnoreCase);
    private static void NoDuplicateKeys(JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Object) { var seen=new HashSet<string>(StringComparer.Ordinal); foreach(var p in element.EnumerateObject()) { if(!seen.Add(p.Name)) throw new IOException("Ambiguous SARIF keys."); NoDuplicateKeys(p.Value); } }
        else if(element.ValueKind==JsonValueKind.Array) foreach(var e in element.EnumerateArray()) NoDuplicateKeys(e);
    }
    public void Dispose()
    {
        foreach(var relative in new[] { "Product/"+SarifName,"Tests/"+SarifName,LedgerRelative })
        {
            var path=SafePath.ResolveRelative(profile.Root,relative);
            if(File.Exists(path)) { using var lease=new SafePathLease(profile.Root,relative); lease.ValidateDirectories(); File.Delete(path); }
        }
        var directory=SafePath.ResolveRelative(profile.Root,Path.GetDirectoryName(LedgerRelative)!);
        if(Directory.Exists(directory)) Directory.Delete(directory,false);
    }
}
