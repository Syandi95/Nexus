using System.Collections.Immutable;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moq;
using Nexus.AI.EditingLite.Services;
using Nexus.App.Services.Agent;
using Nexus.App.Services.Agent.Tools;
using Nexus.Foundation.Logging;

namespace Nexus.Integration.Tests;

public sealed class AgentSourceRepairTests
{
    [Theory]
    [InlineData("Success")]
    [InlineData("BTestFailure")]
    [InlineData("BBuildFailure")]
    [InlineData("FinalMismatch")]
    [InlineData("ApprovalReplay")]
    [InlineData("ApprovalDenied")]
    [InlineData("NewFile")]
    [InlineData("Profile")]
    [InlineData("Budget")]
    [InlineData("Duplicate")]
    [InlineData("Transport")]
    [InlineData("ExternalEdit")]
    [InlineData("Identity")]
    [InlineData("CancelBeforeB")]
    [InlineData("CancelBMutation")]
    [InlineData("CancelBBuild")]
    [InlineData("CancelBTest")]
    [InlineData("RepeatedRepair")]
    [InlineData("MalformedProtocol")]
    [InlineData("RuntimeDeactivate")]
    [InlineData("BuildBudget")]
    [InlineData("TestBudget")]
    [InlineData("ExternalUntouchedSource")]
    [InlineData("ExternalAtApproval")]
    [InlineData("CancelAtBApproval")]
    [InlineData("UnfailedNoOp")]
    [InlineData("BRecoveryConflict")]
    public async Task OneRepair_IndependentAuthority_TruthfulHistory_TerminalBound(string scenario)
        =>await RunScenario(scenario,false);
    [Theory]
    [InlineData("Success")]
    [InlineData("BTestFailure")]
    [InlineData("BBuildFailure")]
    [InlineData("FinalMismatch")]
    [InlineData("ApprovalReplay")]
    [InlineData("ApprovalDenied")]
    [InlineData("BRecoveryConflict")]
    [InlineData("Identity")]
    [InlineData("RuntimeDeactivate")]
    [InlineData("CancelBeforeB")]
    [InlineData("CancelAtBApproval")]
    [InlineData("CancelBMutation")]
    [InlineData("CancelBBuild")]
    [InlineData("CancelBTest")]
    [InlineData("ExternalEdit")]
    [InlineData("ExternalUntouchedSource")]
    [InlineData("ExternalAtApproval")]
    [InlineData("RepeatedRepair")]
    [InlineData("UnfailedNoOp")]
    [InlineData("NewFile")]
    public async Task CompilerRepair_OneBoundCS0029_FreshAuthority_NoPlanC(string scenario)
        =>await RunScenario(scenario,true);
    private async Task RunScenario(string scenario,bool compiler)
    {
        using var f=await SourcePlanTestWorkspace.Create(); using var cancellation=new CancellationTokenSource();
        var profile=compiler ? new SourceVerificationProfile(f.Profile.Id,f.Root,@"C:\Users\Syand\AppData\Local\Microsoft\dotnet\dotnet.exe",f.Profile.Targets,observeCompiler:true) : f.Profile;
        var log=Mock.Of<ILoggerService>(); var catalog=new SourcePlanCatalog(profile);
        var model=new AgentModelSnapshot("fixture","private-model",123,"http://127.0.0.1:8080/v1/chat/completions",1);
        // Synthetic receipt only for deterministic unit boundary tests. Live catalog
        // is enabled exclusively by actual read-only pinned-model probe evidence.
        catalog.EnableRepair(new(profile.Digest,model.ModelId,model.ModelPath,1,100,50,true,CompilerAdequate:compiler));
        var approval=new PlanApproval(scenario,()=>
        {
            if(scenario=="ExternalAtApproval") File.WriteAllText(Path.Combine(f.Root,"Product/Alpha.cs"),"newer external source");
            if(scenario=="CancelAtBApproval") cancellation.Cancel();
        }); var tool=new ApplySourcePlanTool(catalog,f.Editing,approval,log);
        var progress=new AgentRunProgress();
        var limits=new AgentRunLimits(RunDuration:TimeSpan.FromSeconds(120),MaxSourcePlans:2,MaxRepairPlans:1,
            MaxBuildStarts:scenario=="BuildBudget" ? 1 : 2,MaxTestStarts:scenario=="TestBudget" ? 1 : 2);
        var edits=new[] { "Product/Alpha.cs","Product/Beta.cs" }.Select(t=>new SourceFileEdit(t,f.Hash(t),"return 1;",t.Contains("Alpha") ? compiler ? "return \"two\";" : "return 3;" : "return 2;")).ToImmutableArray();
        var run=new AgentRunContext(Guid.NewGuid(),"repair-unit",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddSeconds(120),256,model,limits,progress,cancellation.Token,SourceIntent:new(f.Profile.Id,edits));
        var identityChanged=false;
        using var handler=new RepairHandler(scenario,()=>
        {
            if(scenario=="ExternalEdit") File.WriteAllText(Path.Combine(f.Root,"Product/Alpha.cs"),"newer external source");
            if(scenario=="ExternalUntouchedSource") File.WriteAllText(Path.Combine(f.Root,"Product/Gamma.cs"),"newer external source");
            if(scenario=="Identity") identityChanged=true;
            if(scenario is "CancelBeforeB" or "RuntimeDeactivate") cancellation.Cancel();
        },compiler);
        using var http=new HttpClient(handler); var registry=new ToolRegistry([tool]); var policy=new ReadOnlyToolPolicy([],sourceCatalog:catalog);
        var orchestrator=new AgentOrchestrator(new NativeToolModelAdapter(http),registry,policy,new ToolExecutor(registry,policy,log),(_,_)=>Task.FromResult(NativeToolSupport.Supported),log);
        typeof(EditingWorkflowService).GetProperty("SourcePlanProbe",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(f.Editing,(Action<string,SourceEditPlan,int>)((stage,_,index)=>
        {
            if(progress.SourcePlanCount==2 && stage=="SourceApplyCompleted" && index==0 && scenario=="CancelBMutation") cancellation.Cancel();
            if(progress.SourcePlanCount==2 && stage=="SourceTestCompleted" && scenario=="BRecoveryConflict") File.WriteAllText(Path.Combine(f.Root,"Product/Alpha.cs"),"newer external source");
        }));
        var verifier=typeof(EditingWorkflowService).GetProperty("SourceVerifier",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Editing)!;
        verifier.GetType().GetProperty("Probe",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(verifier,(Action<string,int>)((stage,_)=>
        {
            if(progress.SourcePlanCount==2 && (scenario=="CancelBBuild" && stage=="Build" || scenario=="CancelBTest" && stage=="Test")) cancellation.Cancel();
        }));
        void Identity() { if(identityChanged) throw new ToolProtocolException("RuntimeIdentityChanged"); }
        if(scenario is "Success" or "UnfailedNoOp")
        {
            await orchestrator.ExecuteAsync(run,[],AgentVerificationRequirement.SourcePlan,Identity,cancellation.Token);
            Assert.True(progress.Verification!.Passed); Assert.Equal(SourcePlanOutcome.Success,progress.SourceResult!.Outcome);
            Assert.Equal(3,progress.ModelTurnCount); Assert.Equal(2,progress.SourcePlanCount); Assert.Equal(1,progress.RepairPlanCount);
            Assert.Equal(2,progress.BuildStartCount); Assert.Equal(compiler ? 1 : 2,progress.TestStartCount); Assert.Equal(2,approval.Calls);
            Assert.Equal(progress.Attempts[0].PlanId,progress.Attempts[1].ParentPlanId); Assert.NotEqual(progress.Attempts[0].PlanId,progress.Attempts[1].PlanId);
            Assert.Equal(compiler ? SourcePlanOutcome.BuildFailed : SourcePlanOutcome.TestFailed,progress.Attempts[0].Result.Outcome); Assert.True(progress.Attempts[0].Result.BaselineVerified);
            // Entire-task finalization failure after the orchestrator returns must
            // still restore the latest plan until final success is accepted.
            orchestrator.RecoverSourceFailure(run);
            Assert.True(progress.SourceResult!.BaselineVerified);
        }
        else
        {
            await Assert.ThrowsAnyAsync<Exception>(()=>orchestrator.ExecuteAsync(run,[],AgentVerificationRequirement.SourcePlan,Identity,cancellation.Token));
            Assert.False(progress.SourceResult?.FinalVerified==true);
        }
        Assert.InRange(progress.SourcePlanCount,1,2); Assert.InRange(progress.RepairPlanCount,0,1);
        Assert.InRange(progress.BuildStartCount,1,2); Assert.InRange(progress.TestStartCount,compiler ? 0 : 1,compiler ? 1 : 2); Assert.Equal(0,progress.RetryCount);
        Assert.Equal(compiler ? SourcePlanOutcome.BuildFailed : SourcePlanOutcome.TestFailed,progress.Attempts[0].Result.Outcome); Assert.True(progress.Attempts[0].Result.BaselineVerified);
        if(compiler)
        {
            var first=progress.Attempts[0]; Assert.Null(first.Result.Tests);
            Assert.Equal(first.Plan.Entries[0].DesiredHash,first.Result.Build!.CompilerFailure!.FailedSourceHash);
            Assert.True(first.Result.Build.CompilerFailure.Matches(first.Plan,1));
        }
        Assert.InRange(handler.Calls,2,3); Assert.True(progress.TotalToolResultBytes<=6144); Assert.True(progress.TotalDiffBytes<=4096); Assert.True(progress.TotalChangedLines<=12);
        foreach(var edit in edits)
            if(edit.Target.Contains("Alpha") && scenario is "ExternalEdit" or "BRecoveryConflict" or "ExternalAtApproval") Assert.Equal("newer external source",File.ReadAllText(Path.Combine(f.Root,edit.Target)));
            else Assert.Equal(edit.BeforeHash,f.Hash(edit.Target));
        if(scenario=="BRecoveryConflict") Assert.Equal(SourcePlanOutcome.RecoveryConflict,progress.SourceResult!.Outcome);
        if(scenario=="ApprovalReplay") Assert.Equal(SourcePlanOutcome.ApprovalDenied,progress.Attempts[1].Result.Outcome);
    }
    private sealed class PlanApproval(string scenario,Action beforeSecond) : IWriteApprovalProvider
    {
        private SourcePlanApproval? first; public int Calls;
        public Task<WriteApprovalDecision> RequestAsync(SafeWriteProposal p,CancellationToken token)=>throw new InvalidOperationException();
        public Task<SourcePlanApproval> RequestPlanAsync(SourceEditPlan p,CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++;
            if(Calls==1) { first=SourcePlanApproval.For(p,true); return Task.FromResult(first); }
            beforeSecond();
            Assert.False(first!.Matches(p));
            return Task.FromResult(scenario=="ApprovalReplay" ? first : SourcePlanApproval.For(p,scenario!="ApprovalDenied"));
        }
    }
    private sealed class RepairHandler(string scenario,Action beforeB,bool compiler=false) : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!; Calls++;
            Assert.Equal(Calls<=2 ? "required" : "none",body["tool_choice"]!.GetValue<string>());
            if(Calls<=2) Assert.Equal(ApplySourcePlanTool.ToolName,Assert.Single(body["tools"]!.AsArray())!["function"]!["name"]!.GetValue<string>());
            else Assert.Empty(body["tools"]!.AsArray());
            if(Calls==2) { beforeB(); if(scenario=="Transport") throw new HttpRequestException("Transport failure."); }
            if(Calls==2 && scenario=="MalformedProtocol") return new(HttpStatusCode.OK) { Content=new StringContent("malformed response") };
            string response;
            if(Calls<=2)
            {
                var desired=Calls==1 || scenario is "BTestFailure" or "BRecoveryConflict" ? "return 3;" : scenario=="BBuildFailure" ? "return undefined;" : "return 2;";
                if(compiler && (Calls==1 || scenario=="BBuildFailure")) desired="return \"two\";";
                var args=new JsonObject { ["a"]=new JsonArray(new JsonObject { ["old"]="return 1;",["new"]=desired }),["b"]=new JsonArray(new JsonObject { ["old"]="return 1;",["new"]="return 2;" }) };
                if(Calls==2)
                {
                    Assert.Contains(compiler ? "CS0029" : "KnownRequiredTestAssertionFailure",body["messages"]!.ToJsonString());
                    if(scenario=="NewFile") args["c"]=new JsonArray(new JsonObject { ["old"]="return 1;",["new"]="return 2;" });
                    if(scenario=="Profile") args["profileId"]="foreign";
                    if(scenario=="Budget") args["maxRepairs"]=100;
                    if(scenario=="UnfailedNoOp") args["b"]![0]!["new"]="return 1;";
                }
                response=ToolProtocolTests.Response(callId:scenario=="Duplicate" ? "same-call" : "repair-call-"+Calls,name:ApplySourcePlanTool.ToolName,arguments:args.ToJsonString());
            }
            else if(scenario=="RepeatedRepair") response=ToolProtocolTests.Response(callId:"third-repair",name:ApplySourcePlanTool.ToolName,arguments:"{\"a\":[{\"old\":\"return 1;\",\"new\":\"return 2;\"}],\"b\":[{\"old\":\"return 1;\",\"new\":\"return 2;\"}]}");
            else
            {
                var data=JsonNode.Parse(body["messages"]!.AsArray().Last()!["content"]!.GetValue<string>())!["data"]!;
                response=ToolProtocolTests.Response(JsonSerializer.Serialize(new { planId=data["planId"]!.GetValue<string>(),planDigest=scenario=="FinalMismatch" ? "wrong" : data["planDigest"]!.GetValue<string>(),verified=true }),null);
            }
            return new(HttpStatusCode.OK) { Content=new StringContent(response) };
        }
    }
    [Fact]
    public async Task DefaultCatalogAndDefaultDeadline_CannotEnableRepair_AndCapsCannotIncrease()
    {
        using var f=await SourcePlanTestWorkspace.Create(); var catalog=new SourcePlanCatalog(f.Profile);
        var model=new AgentModelSnapshot("fixture","model",1,"http://127.0.0.1:8080/v1/chat/completions",1);
        var run=new AgentRunContext(Guid.NewGuid(),"denied",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddSeconds(120),256,model,
            new(MaxSourcePlans:2,MaxRepairPlans:1,RunDuration:TimeSpan.FromSeconds(120)),new());
        Assert.False(catalog.RepairAvailable(run));
        catalog.EnableRepair(new(f.Profile.Digest,model.ModelId,model.ModelPath,1,100,57,true));
        Assert.False(catalog.RepairAvailable(run with { Limits=run.Limits! with { RunDuration=TimeSpan.FromSeconds(60) } }));
        Assert.False(catalog.RepairAvailable(run with { Model=model with { ModelId="foreign" } }));
        Assert.False(new AgentRunLimits(MaxRepairPlans:2).IsValid); Assert.False(new AgentRunLimits(MaxSourcePlans:3).IsValid);
        Assert.False(new AgentRunLimits(MaxBuildStarts:3).IsValid); Assert.False(new AgentRunLimits(MaxTestStarts:3).IsValid);
        Assert.False(new AgentRunLimits(RunDuration:TimeSpan.FromSeconds(121)).IsValid);
        Assert.Throws<ArgumentException>(()=>catalog.EnableRepair(new(f.Profile.Digest,model.ModelId,model.ModelPath,121,100,57,true)));
    }
}
