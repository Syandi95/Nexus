using System.Diagnostics;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Xml.Linq;

namespace Nexus.AI.EditingLite.Services;

// No model-callable process API. Only two fixed SDK commands for one catalog entry.
internal sealed class SourceVerification
{
    internal Action<string,int>? Probe { get; set; }
    internal Action<ProcessStartInfo,OwnedSdkProcess>? InvocationProbe { get; set; }
    internal Action<CompilerArtifacts>? CompilerProbe { get; set; }
    internal sealed record BuildReceipt(SourceProcessResult Result,string ProductHash,string TestHash,Guid ProductMvid,Guid TestMvid);
    internal async Task<BuildReceipt> BuildAsync(SourceVerificationProfile p,SourceRunControl run,SourceEditPlan? plan=null)
    {
        run.Check(); using var graph=p.LockGraph();
        var started=DateTime.UtcNow;
        using var artifacts=p.CompilerObservationEnabled ? new CompilerArtifacts(p) : null;
        var arguments=new List<string> { "build",p.TestProjectRelative,"--no-restore","--disable-build-servers","-t:Rebuild","-m:1","-nr:false",
            "-p:UseSharedCompilation=false","-p:ImportDirectoryBuildProps=false","-p:ImportDirectoryBuildTargets=false","--verbosity","quiet" };
        if(artifacts is not null) arguments.AddRange(artifacts.Instrumentation());
        if(p.ReviewedManifest is not null)arguments.AddRange(["-noAutoResponse","-p:ImportDirectoryPackagesProps=false","-p:NuGetAudit=false"]);
        var result=await Execute(p,run,"Build",arguments.ToArray(),p.BuildTimeout);
        if(artifacts is not null) { CompilerProbe?.Invoke(artifacts); result=artifacts.Complete(result,plan,run.AttemptNumber,run); }
        if(!result.Passed) return new(result,"","",Guid.Empty,Guid.Empty);
        if(!p.IsIntact() || p.ReviewedManifest?.ArtifactsMatch()==false || !File.Exists(p.ProductBinary) || !File.Exists(p.TestBinary) || File.GetLastWriteTimeUtc(p.ProductBinary)<started.AddSeconds(-1) ||
            File.GetLastWriteTimeUtc(p.TestBinary)<started.AddSeconds(-1)) return new(result with { Passed=false,Category="StaleBuildArtifacts" },"","",Guid.Empty,Guid.Empty);
        return new(result,SourceVerificationProfile.HashFile(p.ProductBinary),SourceVerificationProfile.HashFile(p.TestBinary),Mvid(p.ProductBinary),Mvid(p.TestBinary));
    }
    internal async Task<SourceProcessResult> TestAsync(SourceVerificationProfile p,SourceRunControl run,BuildReceipt build)
    {
        run.Check(); using var graph=p.LockGraph();
        using var artifactLocks=p.ReviewedManifest?.LockArtifacts();
        if(!build.Result.Passed || build.ProductHash!=SourceVerificationProfile.HashFile(p.ProductBinary) || build.TestHash!=SourceVerificationProfile.HashFile(p.TestBinary) ||
            build.ProductMvid!=Mvid(p.ProductBinary) || build.TestMvid!=Mvid(p.TestBinary)) return new(false,"StaleBuildArtifacts",null,0,TimeSpan.Zero,"",false);
        var directory=Path.Combine(p.Root,"verification-results",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var started=DateTime.UtcNow;
        var args=new List<string> {"test",p.TestProjectRelative,"--no-build","--no-restore","--filter","FullyQualifiedName~"+p.TestFilter,
            "--logger","trx;LogFileName=required.trx","--results-directory",directory,"--verbosity","quiet"};
        if(p.ReviewedManifest is not null)args.AddRange(["-noAutoResponse","-p:ImportDirectoryBuildProps=false","-p:ImportDirectoryBuildTargets=false","-p:ImportDirectoryPackagesProps=false"]);
        var result=await Execute(p,run,"Test",args.ToArray(),p.TestTimeout);
        if(!result.Passed && (result.Category!="ProcessExited" || result.ExitCode!=1)) return result;
        if(!p.IsIntact() || p.ReviewedManifest?.ArtifactsMatch()==false || build.ProductHash!=SourceVerificationProfile.HashFile(p.ProductBinary) || build.TestHash!=SourceVerificationProfile.HashFile(p.TestBinary))
            return result with { Passed=false,Category="ArtifactChangedDuringTests" };
        var path=Path.Combine(directory,"required.trx");
        if(!File.Exists(path) || File.GetLastWriteTimeUtc(path)<started.AddSeconds(-1)) return result with { Passed=false,Category="InvalidTestResult" };
        return ParseRequiredTestsForProfile(path,p.StructuredResultBytes,result,p.RequiredTests,p.ReviewedManifest is null);
    }
    internal static SourceProcessResult ParseRequiredTests(string path,int limit,SourceProcessResult process)=>ParseRequiredTestsForProfile(path,limit,process,null,true);
    private static SourceProcessResult ParseRequiredTestsForProfile(string path,int limit,SourceProcessResult process,string[]? required,bool fixtureAssertions)
    {
        try
        {
            if(!File.Exists(path) || new FileInfo(path).Length>limit) return process with { Passed=false,Category="InvalidTestResult" };
            using var stream=File.OpenRead(path); var doc=XDocument.Load(stream); XNamespace ns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
            var summary=doc.Descendants(ns+"ResultSummary").Single(); var counts=summary.Element(ns+"Counters")!;
            var rows=doc.Descendants(ns+"UnitTestResult").ToArray();
            var expected=required??new[] { "SourceFixtureTests.RequiredFacts.AlphaDesired","SourceFixtureTests.RequiredFacts.BetaDesired","SourceFixtureTests.RequiredFacts.GammaBounded" };
            if(!fixtureAssertions && !process.Passed && process.Category=="ProcessExited" && process.ExitCode==1)
            {
                var passed=rows.Count(r=>r.Attribute("outcome")?.Value=="Passed");var failed=rows.Count(r=>r.Attribute("outcome")?.Value=="Failed");
                var complete=summary.Attribute("outcome")?.Value=="Failed" && rows.Length==3 && failed>0 && passed+failed==3 &&
                    counts.Attribute("total")?.Value=="3" && counts.Attribute("executed")?.Value=="3" && counts.Attribute("passed")?.Value==passed.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                    counts.Attribute("failed")?.Value==failed.ToString(System.Globalization.CultureInfo.InvariantCulture) && expected.All(n=>rows.Count(r=>r.Attribute("testName")?.Value==n)==1) &&
                    new[] {"error","timeout","aborted","notRunnable","notExecuted","pending","inProgress","passedButRunAborted"}.All(k=>counts.Attribute(k)?.Value is null or "0");
                return process with {Passed=false,Category=complete?"RequiredTestsFailed":"RequiredTestsNotVerified",TestsPassed=complete?passed:0};
            }
            if(fixtureAssertions && !process.Passed && process.Category=="ProcessExited" && process.ExitCode==1)
            {
                var failures=rows.Where(r=>r.Attribute("outcome")?.Value=="Failed").ToArray();
                var complete=summary.Attribute("outcome")?.Value=="Failed" && counts.Attribute("total")?.Value=="3" && counts.Attribute("executed")?.Value=="3" &&
                    counts.Attribute("failed")?.Value==failures.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                    counts.Attribute("passed")?.Value==(3-failures.Length).ToString(System.Globalization.CultureInfo.InvariantCulture) && failures.Length>0 && rows.Length==3 &&
                    new[] { "error","timeout","aborted","notRunnable","notExecuted","pending","inProgress","passedButRunAborted" }.All(k=>counts.Attribute(k)?.Value is null or "0") &&
                    rows.All(r=>r.Attribute("outcome")?.Value is "Passed" or "Failed") && expected.All(n=>rows.Count(r=>r.Attribute("testName")?.Value==n)==1);
                var assertions=new List<RequiredAssertionFailure>();
                foreach(var row in failures)
                {
                    var name=row.Attribute("testName")?.Value;
                    var source=name==expected[0] ? "Product/Alpha.cs" : name==expected[1] ? "Product/Beta.cs" : null;
                    var message=row.Element(ns+"Output")?.Element(ns+"ErrorInfo")?.Element(ns+"Message")?.Value ?? "";
                    var match=System.Text.RegularExpressions.Regex.Match(message,@"\AAssert\.Equal\(\) Failure: Values differ\s+Expected: 2\s+Actual:\s+([0-9])\s*\z",
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50));
                    if(source is null || message.Length>256 || !match.Success || match.Groups[1].Value=="2") { complete=false; break; }
                    assertions.Add(new(name!,source,2,int.Parse(match.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture)));
                }
                return process with { Passed=false,Category=complete ? "KnownRequiredTestAssertionFailure" : "RequiredTestsNotVerified",
                    AssertionFailures=complete ? assertions.ToImmutableArray() : [] };
            }
            var valid=summary.Attribute("outcome")?.Value=="Completed" && counts.Attribute("total")?.Value=="3" && counts.Attribute("executed")?.Value=="3" &&
                counts.Attribute("passed")?.Value=="3" && counts.Attribute("failed")?.Value=="0" && counts.Attribute("aborted")?.Value=="0" && rows.Length==3 &&
                new[] { "error","timeout","notRunnable","notExecuted","pending","inProgress","passedButRunAborted" }.All(k=>counts.Attribute(k)?.Value is null or "0") &&
                rows.All(r=>r.Attribute("outcome")?.Value=="Passed") && expected.All(n=>rows.Count(r=>r.Attribute("testName")?.Value==n)==1);
            return process with { Passed=valid,Category=valid ? "RequiredTestsVerified" : "RequiredTestsNotVerified",TestsPassed=valid ? 3 : 0 };
        }
        catch { return process with { Passed=false,Category="InvalidTestResult" }; }
    }
    private static Guid Mvid(string file)
    { using var stream=File.OpenRead(file); using var pe=new PEReader(stream); var reader=pe.GetMetadataReader(); return reader.GetGuid(reader.GetModuleDefinition().Mvid); }

    private async Task<SourceProcessResult> Execute(SourceVerificationProfile p,SourceRunControl run,string stage,string[] args,TimeSpan timeout)
    {
        run.Check(); var timer=Stopwatch.StartNew();
        var info=new ProcessStartInfo(p.Sdk) { WorkingDirectory=p.Root,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true };
        foreach(var arg in args) info.ArgumentList.Add(arg);
        info.Environment.Clear(); foreach(var entry in p.EnvironmentValues) info.Environment[entry.Key]=entry.Value;
        info.Environment["DOTNET_ROOT"]=Path.GetDirectoryName(p.Sdk)!; info.Environment["DOTNET_HOST_PATH"]=p.Sdk;
        info.Environment["PATH"]=Path.GetDirectoryName(p.Sdk)+Path.PathSeparator+Path.Combine(info.Environment["SystemRoot"]!,"System32");
        using var temporary=new VerificationTempDirectory();
        info.Environment["TEMP"]=temporary.Path;info.Environment["TMP"]=temporary.Path;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(run.Cancellation);
        var remaining=run.Remaining(); deadline.CancelAfter(remaining<timeout ? remaining : timeout);
        run.Check(); run.BeforeProcessStart?.Invoke(stage);
        using var process=OwnedSdkProcess.Start(info); var pid=process.Id;
        var capture=new Capture(p.DiagnosticBytes);
        var output=Drain(process.StandardOutput,capture); var error=Drain(process.StandardError,capture);
        var category="ProcessExited";
        try { InvocationProbe?.Invoke(info,process); Probe?.Invoke(stage,pid); await process.WaitForExitAsync(deadline.Token); }
        catch(OperationCanceledException)
        {
            category=run.Cancellation.IsCancellationRequested ? "Cancelled" : "ProcessDeadline";
            await process.DrainJobAsync();
        }
        finally
        {
            try
            {
                await process.DrainJobAsync();
            }
            finally
            {
                // Also drain on probe/identity exceptions, before disposing the
                // owned process streams or returning to plan recovery.
                await Task.WhenAll(output,error).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        var success=category=="ProcessExited" && process.ExitCode==0;
        return new(success,category,process.ExitCode,pid,timer.Elapsed,capture.Text,capture.Truncated);
    }
    private static async Task Drain(StreamReader reader,Capture capture)
    { var buffer=new char[1024]; int count; while((count=await reader.ReadAsync(buffer))!=0) capture.Add(new string(buffer,0,count)); }
    private sealed class Capture(int limit)
    {
        private readonly StringBuilder text=new(); private int bytes;
        public bool Truncated { get; private set; }
        public string Text { get { lock(text) return text.ToString(); } }
        public void Add(string value)
        {
            lock(text)
            {
                foreach(var rune in value.EnumerateRunes())
                { if(bytes+rune.Utf8SequenceLength>limit) { Truncated=true; continue; } text.Append(rune.ToString()); bytes+=rune.Utf8SequenceLength; }
            }
        }
    }
}
