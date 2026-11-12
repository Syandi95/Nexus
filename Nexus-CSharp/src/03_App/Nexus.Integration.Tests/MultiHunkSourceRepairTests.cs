using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;
using Moq;
using Nexus.AI.EditingLite.Services;
using Nexus.App.Services.Agent;
using Nexus.App.Services.Agent.Tools;
using Nexus.Foundation.Logging;

namespace Nexus.Integration.Tests;

public sealed class MultiHunkSourceRepairTests
{
    [Theory]
    [InlineData("Success")]
    [InlineData("NewTarget")]
    [InlineData("ChangedBeta")]
    [InlineData("Malformed")]
    [InlineData("BFailure")]
    [InlineData("Replay")]
    [InlineData("ExternalBeforeB")]
    public async Task OneFileLevelCompilerRepair_WholeCollectionsFreshApproval_NoPlanC(string scenario)
    {
        using var f=await SourcePlanTestWorkspace.Create();f.ObserveCompiler();var initial=MultiHunkSourceToolTests.Edits(f,true);
        var run=NativeToolSelectionTests.Context() with { SourceIntent=new(f.Profile.Id,initial),Limits=GateFRepairLiveProof.Limits() };
        var catalog=new SourcePlanCatalog(f.Profile);
        catalog.EnableRepair(new(f.Profile.Digest,run.Model!.ModelId,run.Model.ModelPath,1,100,80,true,CompilerAdequate:true)); // unit boundary only
        var approval=new Approval(scenario);var log=Mock.Of<ILoggerService>();var tool=new ApplySourcePlanTool(catalog,f.Editing,approval,log);
        var registry=new ToolRegistry([tool]);var policy=new ReadOnlyToolPolicy([],sourceCatalog:catalog);
        using var handler=new Handler(f,scenario);using var http=new HttpClient(handler);
        var orchestrator=new AgentOrchestrator(new NativeToolModelAdapter(http),registry,policy,new ToolExecutor(registry,policy,log),(_,_)=>Task.FromResult(NativeToolSupport.Supported),log);
        if(scenario is "Success" or "ChangedBeta")
        {
            await orchestrator.ExecuteAsync(run,[],AgentVerificationRequirement.SourcePlan,()=>{},CancellationToken.None);
            Assert.True(run.Progress!.Verification!.Passed);Assert.Equal(2,approval.Calls);Assert.Equal(2,run.Progress.BuildStartCount);Assert.Equal(1,run.Progress.TestStartCount);
            Assert.All(run.Progress.SourcePlan!.Entries,e=>Assert.Equal(e.DesiredHash,f.Hash(e.Target)));
        }
        else
        {
            await Assert.ThrowsAnyAsync<Exception>(()=>orchestrator.ExecuteAsync(run,[],AgentVerificationRequirement.SourcePlan,()=>{},CancellationToken.None));
            Assert.False(run.Progress!.SourceResult?.FinalVerified==true);
            Assert.InRange(approval.Calls,1,2);
            foreach(var file in initial)
                if(scenario=="ExternalBeforeB" && file.Target.Contains("Alpha")) Assert.EndsWith("// external",File.ReadAllText(Path.Combine(f.Root,file.Target)));
                else Assert.Equal(file.BeforeHash,f.Hash(file.Target));
        }
        var progress=run.Progress!;Assert.Equal(1,progress.RepairPlanCount);Assert.Equal(2,progress.SourcePlanCount);Assert.Equal(0,progress.RetryCount);
        var a=progress.Attempts[0];Assert.True(a.Result.BaselineVerified);Assert.Equal("CS0029",a.Result.Build!.CompilerFailure!.DiagnosticId);Assert.Null(a.Result.Tests);
        Assert.Equal(new[] { 2,1 },a.Plan.Entries.Select(e=>e.ResolvedEdits.Length));Assert.InRange(handler.Calls,2,3);
        if(progress.Attempts.Count==2) Assert.Equal(a.PlanId,progress.Attempts[1].ParentPlanId);
    }
    private sealed class Approval(string scenario) : IWriteApprovalProvider
    {
        internal int Calls;private SourcePlanApproval? first;
        public Task<WriteApprovalDecision> RequestAsync(SafeWriteProposal p,CancellationToken token)=>throw new InvalidOperationException();
        public Task<SourcePlanApproval> RequestPlanAsync(SourceEditPlan p,CancellationToken token)
        { Calls++;var grant=SourcePlanApproval.For(p,true);if(first is not null) Assert.False(first.Matches(p));if(Calls==1) first=grant;return Task.FromResult(scenario=="Replay" ? first! : grant); }
    }
    private sealed class Handler(SourcePlanTestWorkspace f,string scenario) : HttpMessageHandler
    {
        internal int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;Calls++;
            if(Calls==2)
            {
                Assert.Contains("CS0029",body["messages"]!.ToJsonString());
                Assert.Equal("required",body["tool_choice"]!.GetValue<string>());Assert.Single(body["tools"]!.AsArray());
                if(scenario=="ExternalBeforeB") File.AppendAllText(Path.Combine(f.Root,"Product/Alpha.cs"),"// external");
            }
            string raw;
            if(Calls<=2)
            {
                var args=JsonNode.Parse(MultiHunkSourceToolTests.Arguments(MultiHunkSourceToolTests.Edits(f,Calls==1 || scenario=="BFailure")))!;
                if(Calls==2 && scenario=="NewTarget") args["c"]=new JsonArray(new JsonObject { ["old"]="return 1;",["new"]="return 2;" });
                if(Calls==2 && scenario=="ChangedBeta") args["b"]![0]!["new"]="return 4;";
                if(Calls==2 && scenario=="Malformed") args["a"]=new JsonArray();
                raw=ToolProtocolTests.Response(callId:"h-call-"+Calls,name:ApplySourcePlanTool.ToolName,arguments:args.ToJsonString());
            }
            else
            {
                var data=JsonNode.Parse(body["messages"]!.AsArray().Last()!["content"]!.GetValue<string>())!["data"]!;
                raw=ToolProtocolTests.Response(JsonSerializer.Serialize(new { planId=data["planId"]!.GetValue<string>(),planDigest=data["planDigest"]!.GetValue<string>(),verified=true }),null);
            }
            return new(HttpStatusCode.OK) { Content=new StringContent(raw) };
        }
    }
}
