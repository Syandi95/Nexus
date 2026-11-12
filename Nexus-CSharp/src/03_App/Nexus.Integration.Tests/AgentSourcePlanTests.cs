using System.Collections.Immutable;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moq;
using Nexus.AI.EditingLite.Services;
using Nexus.App.Services.Agent;
using Nexus.App.Services.Agent.Tools;
using Nexus.Foundation.Logging;

namespace Nexus.Integration.Tests;

public sealed class AgentSourcePlanTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativePlanRequest_UsesOnePlanBudget_AndRecoversInvalidFinalFacts(bool invalidFinal)
    {
        using var workspace=await SourcePlanTestWorkspace.Create();
        var logger=Mock.Of<ILoggerService>(); var catalog=new SourcePlanCatalog(workspace.Profile); var approval=new Approval();
        var tool=new ApplySourcePlanTool(catalog,workspace.Editing,approval,logger);
        using var handler=new NativeHandler(invalidFinal); using var http=new HttpClient(handler);
        var registry=new ToolRegistry([tool]); var policy=new ReadOnlyToolPolicy([],sourceCatalog:catalog);
        var round=new AgentOrchestrator(new NativeToolModelAdapter(http),registry,policy,new ToolExecutor(registry,policy,logger),(_,_)=>Task.FromResult(NativeToolSupport.Supported),logger);
        var progress=new AgentRunProgress(); var limits=new AgentRunLimits(RunDuration:TimeSpan.FromSeconds(120));
        var files=new[] { "Product/Alpha.cs","Product/Beta.cs" }.Select(t=>new SourceFileEdit(t,workspace.Hash(t),"return 1;","return 2;")).ToImmutableArray();
        var context=new AgentRunContext(Guid.NewGuid(),"source-agent",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddSeconds(120),256,
            new("one","private-model",123,"http://127.0.0.1:8080/v1/chat/completions",1),limits,progress,SourceIntent:new(workspace.Profile.Id,files));
        if(invalidFinal)
        {
            await Assert.ThrowsAsync<ToolProtocolException>(()=>round.ExecuteAsync(context,[],AgentVerificationRequirement.SourcePlan,()=>{},CancellationToken.None));
            Assert.Equal(SourcePlanOutcome.VerificationConflict,progress.SourceResult!.Outcome);
            Assert.False(progress.SourceResult.FinalVerified);
            Assert.All(progress.SourcePlan!.Entries,e=>Assert.Equal(e.BeforeHash,workspace.Hash(e.Target)));
            Assert.All(progress.SourceResult.Operations,o=>Assert.Equal(SafeWriteRecovery.Restored,o.Recovery));
            Assert.Equal(1,approval.Calls); Assert.Equal(1,progress.SourcePlanCount);
            return;
        }
        var result=await round.ExecuteAsync(context,[],AgentVerificationRequirement.SourcePlan,()=>{},CancellationToken.None);
        Assert.True(progress.Verification!.Passed); Assert.Equal(1,progress.SourcePlanCount); Assert.Equal(0,progress.WriteOperationCount);
        Assert.Equal(1,progress.ToolCallCount); Assert.Equal(2,progress.ModelTurnCount); Assert.Equal(1,approval.Calls);
        Assert.Equal(SourcePlanOutcome.Success,progress.SourceResult!.Outcome); Assert.Equal(2,progress.SourceResult.Operations.Length);
        Assert.Equal(1,limits.MaxWriteOperations); Assert.Contains(progress.SourceResult.PlanDigest,result.Content);
    }
    private sealed class Approval : IWriteApprovalProvider
    {
        public int Calls;
        public Task<WriteApprovalDecision> RequestAsync(SafeWriteProposal p,CancellationToken token)=>throw new InvalidOperationException("No individual approval.");
        public Task<SourcePlanApproval> RequestPlanAsync(SourceEditPlan p,CancellationToken token)
        { Calls++; return Task.FromResult(SourcePlanApproval.For(p,true)); }
    }
    private sealed class NativeHandler(bool invalidFinal=false) : HttpMessageHandler
    {
        private int calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var payload=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;
            string response;
            if(++calls==1) response=ToolProtocolTests.Response(callId:"source-native-call",name:ApplySourcePlanTool.ToolName,arguments:"{\"a\":[{\"old\":\"return 1;\",\"new\":\"return 2;\"}],\"b\":[{\"old\":\"return 1;\",\"new\":\"return 2;\"}]}");
            else
            {
                var content=payload["messages"]!.AsArray().Last()!["content"]!.GetValue<string>(); var data=JsonNode.Parse(content)!["data"]!;
                response=ToolProtocolTests.Response(JsonSerializer.Serialize(new { planId=data["planId"]!.GetValue<string>(),planDigest=invalidFinal ? new string('0',64) : data["planDigest"]!.GetValue<string>(),verified=true }),null);
            }
            return new(HttpStatusCode.OK) { Content=new StringContent(response) };
        }
    }
}
