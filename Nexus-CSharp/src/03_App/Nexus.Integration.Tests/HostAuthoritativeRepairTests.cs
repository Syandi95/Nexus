using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Diagnostics;
using Xunit.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moq;
using Nexus.AI.EditingLite.Services;
using Nexus.App.Services.Agent;
using Nexus.App.Services.Agent.Tools;
using Nexus.Foundation.Logging;

namespace Nexus.Integration.Tests;

public sealed class HostAuthoritativeRepairTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("F",0,2,false,"wrong")]
    [InlineData("G",0,2,false,"empty")]
    [InlineData("H",0,2,true,"wrong")]
    [InlineData("G",1,2,false,"wrong")]
    [InlineData("G",0,3,false,"empty")]
    public async Task RealApprovedRecoveredA_HostCarriesCollections_NewBApproval(string category,int failed,int files,bool multi,string echo)
    {
        await using var f=await Fixture.Create(category,failed,files,multi);var first=f.Run.Progress!.Attempts.Single();
        Assert.True(first.Result.HasMatchingApproval(first.Plan));Assert.True(f.Work.Editing.VerifyApprovedRecoveredSourcePlan(first.Plan,first.Result));
        var raw=f.Correction(echo);Assert.True(f.Tool.TryValidateRepair(f.Run,raw,out var input,out var reason),reason);
        var projectionWatch=Stopwatch.StartNew();Assert.True(f.Tool.TryComposeRepair(f.Run,input!,out var candidate,out reason),reason);
        output.WriteLine("Host composition ms="+projectionWatch.Elapsed.TotalMilliseconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(first.Plan.Entries.Select(e=>e.Target),candidate.Select(e=>e.Target));
        for(var i=0;i<files;i++) if(i!=failed) Assert.True(ExactSourceEdits.SameIntent(candidate[i].Edits,first.Plan.Entries[i].ResolvedEdits.Select(e=>new SourceTextEdit(e.OldText,e.NewText))));
        var applied=await f.Executor.ExecuteAsync(new(f.Run),"real-B",ApplySourcePlanTool.ToolName,raw,CancellationToken.None);
        Assert.Equal(ToolOutcome.Success,applied.Result.Outcome);Assert.Equal(2,f.Approval.Calls);Assert.Equal(2,f.Run.Progress.SourcePlanCount);Assert.Equal(1,f.Run.Progress.RepairPlanCount);
        var b=f.Run.Progress.SourcePlan!;Assert.NotEqual(first.PlanId,b.PlanId);Assert.NotEqual(first.PlanDigest,b.PlanDigest);Assert.False(f.Approval.First!.Matches(b));
        Assert.True(f.Run.Progress.SourceResult!.HasMatchingApproval(b));Assert.All(b.Entries,e=>Assert.Equal(e.DesiredHash,f.Work.Hash(e.Target)));
        Assert.Equal(3,f.Run.Progress.SourceResult.Tests!.TestsPassed);Assert.Equal(0,f.Run.Progress.RetryCount);
        Assert.Equal(first.PlanId,f.Run.Progress.Attempts[1].ParentPlanId);
    }

    [Fact]
    public async Task EquivalentAdvisoryEchoes_IdenticalAuthoritativeImages_NoRawCarrier()
    {
        await using var f=await Fixture.Create("H",0,2,true);var bindings=new HashSet<string>();
        foreach(var echo in new[] { "wrong","empty","noop","omitted","arbitrary" })
        {
            Assert.True(f.Tool.TryValidateRepair(f.Run,f.Correction(echo),out var input,out _));
            Assert.True(f.Tool.TryComposeRepair(f.Run,input!,out var candidate,out _));
            var prepared=await f.Work.Editing.PrepareSourcePlanAsync(f.Work.Profile,candidate,f.Run.RunId,"proof-only",new(),Control());
            bindings.Add(JsonSerializer.Serialize(prepared.Entries.Select(e=>new { e.Target,e.BeforeHash,e.DesiredHash,e.Diff,edits=e.ResolvedEdits })));
            Assert.Equal(1,f.Approval.Calls);Assert.All(f.Initial,e=>Assert.Equal(e.BeforeHash,f.Work.Hash(e.Target)));
        }
        Assert.Single(bindings);
        Assert.DoesNotContain(typeof(ValidatedSourceRepairInput).GetProperties(BindingFlags.NonPublic|BindingFlags.Instance),p=>p.PropertyType==typeof(JsonElement) || p.Name is "AdvisoryEdits" or "RawArguments");
    }

    [Fact]
    public async Task ClosedRepairArguments_RejectBeforeAnyBPreparationApprovalOrWrite()
    {
        await using var f=await Fixture.Create("G");var good=f.Correction("empty");
        var invalid=new List<string> { "{bad","null","[]","{\"b\":[]}","{\"a\":[],\"b\":[]}",good.Replace("\"b\":[]","\"b\":null"),good.Replace("\"b\":[]","\"b\":42"),
            good.Replace("\"new\":\"return 2;\"","\"new\":null"),good.Replace("\"old\":\"return 1;\"","\"old\":\"return 1;\",\"old\":\"x\""),good[..^1]+",\"b\":[]}",
            good.Replace("\"b\":[]","\"b\":[{\"old\":\"x\",\"new\":\"y\",\"path\":\"other.cs\"}]"),good[..^1]+",\"c\":[]}" };
        foreach(var field in new[] { "path","profile","approval","limits","commands","workspace","failedTarget","hash" }) invalid.Add(good[..^1]+",\""+field+"\":true}");
        foreach(var raw in invalid)
        {
            Assert.False(f.Tool.TryValidateRepair(f.Run,raw,out var input,out _));Assert.Null(input);Assert.Equal(1,f.Approval.Calls);
            Assert.Equal(1,f.Run.Progress!.SourcePlanCount);Assert.Equal(0,f.Run.Progress.RepairPlanCount);Assert.All(f.Initial,e=>Assert.Equal(e.BeforeHash,f.Work.Hash(e.Target)));
        }
        // One accepted native request consumes its bounded repair slot even when invalid.
        var result=await f.Executor.ExecuteAsync(new(f.Run),"bad-B",ApplySourcePlanTool.ToolName,invalid.Last(),CancellationToken.None);
        Assert.Equal(ToolOutcome.InvalidArguments,result.Result.Outcome);Assert.Equal(1,f.Approval.Calls);Assert.Single(f.Run.Progress!.Attempts);
        Assert.Equal(2,f.Run.Progress.SourcePlanCount);Assert.Equal(1,f.Run.Progress.RepairPlanCount);
        Assert.Equal(ToolOutcome.WriteOperationLimitReached,(await f.Executor.ExecuteAsync(new(f.Run),"no-C",ApplySourcePlanTool.ToolName,good,CancellationToken.None)).Result.Outcome);
    }

    [Theory]
    [InlineData("MissingApproval")]
    [InlineData("Digest")]
    [InlineData("Lineage")]
    [InlineData("PreservedEdit")]
    [InlineData("BeforeSnapshot")]
    [InlineData("External")]
    public async Task ActualAIntegrityOrBaselineTamper_DeniesBeforeBApproval(string scenario)
    {
        await using var f=await Fixture.Create("G");var p=f.Run.Progress!;var first=p.Attempts.Single();
        if(scenario=="MissingApproval") { var copy=first.Result with { ApprovedPlan=null };p.SourceResult=copy;p.RecordSourceAttempt(first with { Result=copy }); }
        if(scenario=="Digest") p.RecordSourceAttempt(first with { PlanDigest=new string('0',64) });
        if(scenario=="Lineage") p.RecordSourceAttempt(first with { RunId=Guid.NewGuid() });
        if(scenario=="PreservedEdit") typeof(SafeWriteProposal).GetProperty(nameof(SafeWriteProposal.ResolvedEdits))!.SetValue(first.Plan.Entries[1],first.Plan.Entries[1].ResolvedEdits.SetItem(0,first.Plan.Entries[1].ResolvedEdits[0] with { NewText="return 7;" }));
        if(scenario=="BeforeSnapshot") typeof(FileSnapshot).GetProperty(nameof(FileSnapshot.Content))!.SetValue(first.Plan.Entries[1].Before,"changed in-memory original");
        if(scenario=="External") File.AppendAllText(Path.Combine(f.Work.Root,"Product/Alpha.cs"),"// external");
        var result=await f.Executor.ExecuteAsync(new(f.Run),"bad-B",ApplySourcePlanTool.ToolName,f.Correction("empty"),CancellationToken.None);
        Assert.Equal(ToolOutcome.InvalidArguments,result.Result.Outcome);Assert.Equal(1,f.Approval.Calls);Assert.Single(p.Attempts);
        Assert.Equal(1,p.BuildStartCount);Assert.Equal(0,p.TestStartCount);Assert.Equal(0,p.RetryCount);
        if(scenario=="External") Assert.EndsWith("// external",File.ReadAllText(Path.Combine(f.Work.Root,"Product/Alpha.cs")));
    }

    [Fact]
    public async Task MultipleActualAssertionFailures_NoRepairTargetSelected()
    {
        await using var f=await Fixture.Create("F",bothFailed:true);
        Assert.Equal(2,f.Run.Progress!.SourceResult!.Tests!.AssertionFailures.Select(e=>e.LogicalSourcePath).Distinct().Count());
        Assert.False(f.Tool.TryCaptureApprovedA(f.Run,out _,out _,out var reason));Assert.Equal("AmbiguousFailedSource",reason);
        Assert.False(f.Tool.CanRepair(f.Run));Assert.Equal(1,f.Approval.Calls);
    }

    [Theory]
    [InlineData("Replay")]
    [InlineData("BeforeApproval")]
    [InlineData("AfterApproval")]
    public async Task FreshBApprovalReplayOrExternalDrift_CannotWrite(string scenario)
    {
        await using var f=await Fixture.Create("G");var first=f.Run.Progress!.Attempts.Single();
        f.Approval.Replay=scenario=="Replay";
        if(scenario=="BeforeApproval") File.AppendAllText(Path.Combine(f.Work.Root,"Product/Beta.cs"),"// external");
        if(scenario=="AfterApproval") f.Approval.AtB=()=>File.AppendAllText(Path.Combine(f.Work.Root,"Product/Beta.cs"),"// external");
        var result=await f.Executor.ExecuteAsync(new(f.Run),"B",ApplySourcePlanTool.ToolName,f.Correction("empty"),CancellationToken.None);
        Assert.NotEqual(ToolOutcome.Success,result.Result.Outcome);Assert.Equal(first.Plan.Entries[0].BeforeHash,f.Work.Hash("Product/Alpha.cs"));
        if(scenario=="BeforeApproval") Assert.Equal(1,f.Approval.Calls);
        else {Assert.Equal(2,f.Approval.Calls);Assert.Empty(f.Run.Progress.SourceResult!.Operations);Assert.False(f.Approval.First!.Matches(f.Run.Progress.SourcePlan!));}
        if(scenario!="Replay") Assert.EndsWith("// external",File.ReadAllText(Path.Combine(f.Work.Root,"Product/Beta.cs")));
    }
    private static SourceRunControl Control()=>new(()=>TimeSpan.FromSeconds(120),CancellationToken.None,()=>{});
    [Theory]
    [InlineData("failed-Beta",1,2)]
    [InlineData("three-targets",0,3)]
    public async Task RetainedOutputLimit_RealApprovedA_RecoversAndTerminatesWithoutB(string evidence,int failed,int files)
    {
        using var work=await SourcePlanTestWorkspace.Create();work.ObserveCompiler();var approval=new Approval();
        var initial=new[] { "Product/Alpha.cs","Product/Beta.cs","Product/Gamma.cs" }.Take(files).Select((target,i)=>new SourceFileEdit(target,work.Hash(target),"return 1;",i==failed ? "return \"two\";" : "return 2;")).ToImmutableArray();
        var run=NativeToolSelectionTests.Context() with { SourceIntent=new(work.Profile.Id,initial),Limits=GateFRepairLiveProof.Limits() };
        var catalog=new SourcePlanCatalog(work.Profile);catalog.EnableRepair(new(work.Profile.Digest,run.Model!.ModelId,run.Model.ModelPath,1,100,80,true,CompilerAdequate:true));
        var log=Mock.Of<ILoggerService>();var tool=new ApplySourcePlanTool(catalog,work.Editing,approval,log);var registry=new ToolRegistry([tool]);var policy=new ReadOnlyToolPolicy([],sourceCatalog:catalog);
        var retained=File.ReadAllText(Path.Combine(@"C:\Users\Syand\AppData\Local\Temp\opencode\gate-h-p5",evidence+"-1-response.json"));
        using var handler=new OutputLimitHandler(MultiHunkSourceToolTests.Arguments(initial),retained);using var http=new HttpClient(handler);
        var orchestrator=new AgentOrchestrator(new NativeToolModelAdapter(http),registry,policy,new ToolExecutor(registry,policy,log),(_,_)=>Task.FromResult(NativeToolSupport.Supported),log);
        var error=await Assert.ThrowsAsync<ToolProtocolException>(()=>orchestrator.ExecuteAsync(run,[],AgentVerificationRequirement.SourcePlan,()=>{},CancellationToken.None));
        Assert.Equal("OutputLimit",error.ReasonCode);Assert.Equal(1,approval.Calls);Assert.Equal(2,handler.Calls);Assert.Equal(2,run.Progress!.ModelTurnCount);
        Assert.Equal(1,run.Progress.SourcePlanCount);Assert.Equal(0,run.Progress.RepairPlanCount);Assert.Equal(0,run.Progress.RetryCount);
        Assert.True(run.Progress.SourceResult!.HasMatchingApproval(run.Progress.SourcePlan!));Assert.True(run.Progress.SourceResult.BaselineVerified);
        Assert.All(initial,e=>Assert.Equal(e.BeforeHash,work.Hash(e.Target)));
    }
    private sealed class OutputLimitHandler(string initial,string retained) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        { Calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(Calls==1 ? ToolProtocolTests.Response(name:ApplySourcePlanTool.ToolName,arguments:initial) : retained) }); }
    }
    private sealed class Approval : IWriteApprovalProvider
    {
        internal int Calls;internal SourcePlanApproval? First;internal Action? AtB;internal bool Replay;
        public Task<WriteApprovalDecision> RequestAsync(SafeWriteProposal p,CancellationToken token)=>throw new InvalidOperationException();
        public Task<SourcePlanApproval> RequestPlanAsync(SourceEditPlan p,CancellationToken token)
        { Calls++;token.ThrowIfCancellationRequested();if(Calls==2) AtB?.Invoke();var grant=SourcePlanApproval.For(p,true);if(First is not null) Assert.False(First.Matches(p));First??=grant;return Task.FromResult(Calls==2 && Replay ? First : grant); }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        internal SourcePlanTestWorkspace Work=null!;internal AgentRunContext Run=null!;internal ApplySourcePlanTool Tool=null!;internal ToolExecutor Executor=null!;
        internal Approval Approval=new();internal ImmutableArray<SourceFileEdit> Initial;internal int Failed;
        internal static async Task<Fixture> Create(string category,int failed=0,int files=2,bool multi=false,bool bothFailed=false)
        {
            var f=new Fixture { Work=await SourcePlanTestWorkspace.Create(),Failed=failed };if(category!="F") f.Work.ObserveCompiler();
            f.Initial=new[] { "Product/Alpha.cs","Product/Beta.cs","Product/Gamma.cs" }.Take(files).Select((target,i)=>
            {
                var edits=new List<SourceTextEdit>();if(multi && i==failed) edits.Add(new("int Value()",MultiHunkSourceToolTests.Signature));
                edits.Add(new("return 1;",i==failed || bothFailed ? category=="F" ? "return 3;" : "return \"two\";" : "return 2;"));
                return new SourceFileEdit(target,f.Work.Hash(target),edits);
            }).ToImmutableArray();
            f.Run=NativeToolSelectionTests.Context() with { SourceIntent=new(f.Work.Profile.Id,f.Initial),Limits=GateFRepairLiveProof.Limits() };
            var catalog=new SourcePlanCatalog(f.Work.Profile);catalog.EnableRepair(new(f.Work.Profile.Digest,f.Run.Model!.ModelId,f.Run.Model.ModelPath,1,100,80,true,CompilerAdequate:category!="F"));
            var log=Mock.Of<ILoggerService>();f.Tool=new(catalog,f.Work.Editing,f.Approval,log);var registry=new ToolRegistry([f.Tool]);f.Executor=new(registry,new ReadOnlyToolPolicy([],sourceCatalog:catalog),log);
            var a=await f.Executor.ExecuteAsync(new(f.Run),"actual-A",ApplySourcePlanTool.ToolName,MultiHunkSourceToolTests.Arguments(f.Initial),CancellationToken.None);
            Assert.Equal(ToolOutcome.VerificationFailed,a.Result.Outcome);Assert.Equal(1,f.Approval.Calls);Assert.True(f.Run.Progress!.SourceResult!.HasMatchingApproval(f.Run.Progress.SourcePlan!));
            Assert.True(f.Run.Progress.SourceResult.BaselineVerified);f.Run.Progress.RepairPlanning=true;return f;
        }
        internal string Correction(string echo)
        {
            var root=new JsonObject();for(var i=0;i<Initial.Length;i++)
            {
                if(i==Failed) root[((char)('a'+i)).ToString()]=JsonSerializer.SerializeToNode(Initial[i].Edits.Select(e=>new { old=e.OldText,@new=e.NewText is "return 3;" or "return \"two\";" ? "return 2;" : e.NewText }));
                else if(echo!="omitted") root[((char)('a'+i)).ToString()]=echo=="empty" ? new JsonArray() : new JsonArray(new JsonObject { ["old"]=echo=="arbitrary" ? "unmatched advisory" : "return 1;",["new"]=echo=="noop" ? "return 1;" : echo=="arbitrary" ? "unrelated text" : "return 3;" });
            }
            return root.ToJsonString();
        }
        public ValueTask DisposeAsync() { Work.Dispose();return ValueTask.CompletedTask; }
    }
}
