using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nexus.AI.EditingLite.Services;

namespace Nexus.Integration.Tests;

public sealed class RecoveryFirstOracleTests
{
    private const string Reviewed=@"D:\NEXUS\scratch\recovery-first-20261017\reviewed-i2b-v2";
    [Theory]
    [InlineData("correct",SourcePlanOutcome.Success)]
    [InlineData("tautology",SourcePlanOutcome.TestFailed)]
    [InlineData("inverted",SourcePlanOutcome.TestFailed)]
    [InlineData("initializer",SourcePlanOutcome.BuildFailed)]
    [InlineData("session-zero",SourcePlanOutcome.TestFailed)]
    [InlineData("session-positive",SourcePlanOutcome.TestFailed)]
    public async Task RevisedOracle_ExecutesCompiledProperties_AndRestoresAllSourceHashes(string mode,SourcePlanOutcome expected)
    {
        var parent=Path.Combine(Path.GetTempPath(),"nexus-recovery-oracle-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            var json=JsonNode.Parse(File.ReadAllText(Path.Combine(Reviewed,"nexus-reviewed-i2b.json")))!;
            foreach(var path in json["graph"]!.AsObject().Select(p=>p.Key).Concat(json["sources"]!.AsObject().Select(p=>p.Key)).Concat(json["artifacts"]!.AsObject().Select(p=>p.Key)))
            {
                var dest=Path.Combine(parent,path);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(Path.Combine(Reviewed,path),dest);
            }
            json["root"]=parent;
            var manifest=Path.Combine(parent,"nexus-reviewed-i2b.json");File.WriteAllText(manifest,json.ToJsonString());
            var profile=new SourceVerificationProfile(ReviewedProjectVerificationManifest.LoadPinned(parent,new FileSnapshotService().ComputeSha256Hash(manifest)));
            using var dependencies=profile.AcquireExecutionLease();
            var cpu=mode switch {"tautology"=>"true","inverted"=>"ExecutionProvider == ExecutionProvider.Cpu",_=>"ExecutionProvider != ExecutionProvider.Cpu"};
            var sessions=mode switch {"session-zero"=>"ActiveSessionsCount >= 0","session-positive"=>"false",_=>"ActiveSessionsCount > 0"};
            const string a="    public ExecutionProvider ExecutionProvider { get; init; } = ExecutionProvider.Cpu;";
            const string b="    public int ActiveSessionsCount { get; init; }";
            var editA=new SourceFileEdit(profile.Targets[0],profile.InitialSourceHashes[profile.Targets[0]],a,a+"\n    public bool IsCpu => "+cpu+";");
            var editB=new SourceFileEdit(profile.Targets[1],profile.InitialSourceHashes[profile.Targets[1]],b,b+"\n    public bool HasActiveSessions "+(mode=="initializer"?"{ get; init; } = ":"=> ")+sessions+";");
            var editing=new EditingWorkflowService(new FileSnapshotService(),new ProposalService(),new SyntaxValidatorService(),new FileWriterService());
            var clock=Stopwatch.StartNew();var run=new SourceRunControl(()=>TimeSpan.FromSeconds(120)-clock.Elapsed,CancellationToken.None,()=>{});
            var plan=await editing.PrepareSourcePlanAsync(profile,[editA,editB],Guid.NewGuid(),"recovery-oracle",new(),run);
            var result=await editing.ExecuteSourcePlanAsync(plan,new ExactApproval(),run);
            if(Environment.GetEnvironmentVariable("NEXUS_TEST_EVIDENCE_ROOT") is string rawEvidence)
            {
                var capture=Path.Combine(rawEvidence,"oracle-raw-"+mode+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(capture);
                File.WriteAllText(Path.Combine(capture,"result.json"),JsonSerializer.Serialize(result));
                foreach(var trx in Directory.EnumerateFiles(parent,"*.trx",SearchOption.AllDirectories))File.Copy(trx,Path.Combine(capture,Path.GetFileName(trx)));
            }
            Assert.True(expected==result.Outcome,JsonSerializer.Serialize(new {expected,result}));
            if(expected==SourcePlanOutcome.TestFailed)
            {
                Assert.True(result.Build!.Passed);Assert.Equal("RequiredTestsFailed",result.Tests!.Category);Assert.Equal(2,result.Tests.TestsPassed);
                var trx=Directory.EnumerateFiles(parent,"required.trx",SearchOption.AllDirectories).Single();
                var xml=System.Xml.Linq.XDocument.Load(trx);System.Xml.Linq.XNamespace ns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
                var failure=Assert.Single(xml.Descendants(ns+"UnitTestResult").Where(r=>r.Attribute("outcome")?.Value=="Failed"));
                Assert.Equal("I2BContracts.RequiredFacts."+(mode is "tautology" or "inverted"?"CpuFlag":"ActiveSessionFlag"),failure.Attribute("testName")?.Value);
            }
            if(expected==SourcePlanOutcome.BuildFailed)Assert.Contains("CS0236",result.Build!.Output);
            if(expected==SourcePlanOutcome.Success)
            {
                Assert.True(result.FinalVerified);Assert.Equal(3,result.Tests!.TestsPassed);
                result=editing.RecoverSourcePlanAfterFinalFailure(plan,result);
            }
            Assert.True(result.BaselineVerified);
            var hashes=profile.InitialSourceHashes.ToDictionary(p=>p.Key,p=>new FileSnapshotService().ComputeSha256Hash(Path.Combine(parent,p.Key)));
            Assert.All(profile.InitialSourceHashes,p=>Assert.Equal(p.Value,hashes[p.Key]));
            if(Environment.GetEnvironmentVariable("NEXUS_TEST_EVIDENCE_ROOT") is string evidence)
            {
                Directory.CreateDirectory(evidence);
                File.WriteAllText(Path.Combine(evidence,"oracle-"+mode+".json"),JsonSerializer.Serialize(new {mode,expected,result,before=profile.InitialSourceHashes,after=hashes}));
            }
        }
        finally{Directory.Delete(parent,true);}
    }
    private sealed class ExactApproval:IWriteApprovalProvider
    {
        public Task<WriteApprovalDecision> RequestAsync(SafeWriteProposal p,CancellationToken token)=>throw new InvalidOperationException();
        public Task<SourcePlanApproval> RequestPlanAsync(SourceEditPlan p,CancellationToken token)=>Task.FromResult(SourcePlanApproval.For(p,true));
    }
}
