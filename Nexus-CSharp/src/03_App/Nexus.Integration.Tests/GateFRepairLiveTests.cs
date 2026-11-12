using System.Collections.Immutable;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nexus.AI.EditingLite.Services;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Services;
using Nexus.App.Services;
using Nexus.App.Services.Agent;
using Nexus.App.Services.Agent.Tools;
using Xunit.Abstractions;

namespace Nexus.Integration.Tests;

public sealed class GateFLiveFactAttribute : FactAttribute
{
    public GateFLiveFactAttribute() { if(Environment.GetEnvironmentVariable("NEXUS_GATE_F_LIVE")!="1") Skip="Explicit pinned-model Gate F prerequisite and live source fixture required."; }
}
public sealed class GateFRepairLiveTests(ITestOutputHelper output)
{
    [GateFLiveFact]
    public Task ReadOnlyProbeThenOneApprovedRepair_PreservesSharedChat()=>new GateCToolLiveTests(output).ExecuteProof(true,gateF:true);
}
internal sealed class GateFProofApproval(bool compiler=false) : IWriteApprovalProvider
{
    public int Calls;
    private SourcePlanApproval? first;
    public Task<WriteApprovalDecision> RequestAsync(SafeWriteProposal p,CancellationToken token)=>throw new InvalidOperationException();
    public Task<SourcePlanApproval> RequestPlanAsync(SourceEditPlan plan,CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Calls++;
        Assert.Equal(new[] { "Product/Alpha.cs","Product/Beta.cs" },plan.Entries.Select(e=>e.Target));
        Assert.All(plan.Entries,e=>Assert.Equal("return 1;",e.OldText));
        if(Calls==1) { Assert.Equal(compiler ? "return \"two\";" : "return 3;",plan.Entries[0].NewText); Assert.Equal("return 2;",plan.Entries[1].NewText); }
        else { Assert.Equal(2,Calls); Assert.False(first!.Matches(plan)); Assert.All(plan.Entries,e=>Assert.Equal("return 2;",e.NewText)); }
        var grant=SourcePlanApproval.For(plan,true); first??=grant; return Task.FromResult(grant);
    }
}
internal static class GateFRepairLiveProof
{
    internal static AgentRunLimits Limits()=>new(RunDuration:TimeSpan.FromSeconds(120),MaxSourcePlans:2,MaxRepairPlans:1,MaxBuildStarts:2,MaxTestStarts:2);
    internal static AgentRequest Request(SourcePlanTestWorkspace f,bool compiler=false)=>new("bounded-repair-proof",compiler
        ? "Apply the initial exact source changes. The task goal is that all fixed required tests pass. If the first plan fails with the recognized compiler error, use the single permitted repair from the recovered original source."
        : "Apply the initial source changes. The task goal is that all fixed required tests pass. If the approved first plan fails a known assertion, use the single permitted repair from the recovered original source.",
        MaxOutputTokens:256,EnableReadOnlyToolCall:true,Limits:Limits(),Verification:AgentVerificationRequirement.SourcePlan,
        SourceIntent:new(f.Profile.Id,Edits(f,compiler)));
    private static ImmutableArray<SourceFileEdit> Edits(SourcePlanTestWorkspace f,bool compiler=false)=>new[] { "Product/Alpha.cs","Product/Beta.cs" }.Select(t=>new SourceFileEdit(t,f.Hash(t),"return 1;",t.Contains("Alpha") ? compiler ? "return \"two\";" : "return 3;" : "return 2;")).ToImmutableArray();

    internal static async Task<SourceRepairProbeReceipt> Probe(SourcePlanTestWorkspace f,ConversationRuntimeWorkflow runtime,ILlamaServerManager server,
        ToolRegistry registry,GateFProofApproval liveApproval,List<object> evidence,bool compiler=false)
    {
        // Engineering prepares real failure facts before probe. The probe itself
        // never dispatches a tool, asks approval, writes source or runs build/tests.
        var clock=Stopwatch.StartNew(); var control=new SourceRunControl(()=>TimeSpan.FromSeconds(120)-clock.Elapsed,CancellationToken.None,()=>{});
        var plan=await f.Editing.PrepareSourcePlanAsync(f.Profile,Edits(f,compiler),Guid.NewGuid(),"probe-call",new(),control);
        var result=await f.Editing.ExecuteSourcePlanAsync(plan,new EngineeringApproval(),control);
        if(compiler) { Assert.Equal("RecognizedBoundCompilerFailure",result.Build!.Category); Assert.True(result.Build.CompilerFailure!.Matches(plan,1)); Assert.Null(result.Tests); }
        else Assert.Equal("KnownRequiredTestAssertionFailure",result.Tests!.Category);
        Assert.True(result.BaselineVerified);
        using var lease=runtime.AcquireAdditionalConsumer();
        var model=new AgentModelSnapshot(lease.Model.Id,lease.Model.ResolvedGgufPath!,lease.Pid,lease.Endpoint,lease.RuntimeIdentity);
        var progress=new AgentRunProgress { SourcePlanCount=1,ModelTurnCount=1,ToolCallCount=1,BuildStartCount=1,TestStartCount=compiler ? 0 : 1,SourcePlan=plan,SourceResult=result,RepairPlanning=true };
        var run=new AgentRunContext(plan.RunId,"read-only-repair-probe",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddSeconds(120),256,model,Limits(),progress,lease.CancellationToken,SourceIntent:new(f.Profile.Id,Edits(f,compiler)));
        progress.RecordSourceAttempt(SourceAttemptFact.For(run,plan,result));
        Assert.True(result.HasMatchingApproval(plan));
        var feedback=SourceRepairFeedback.Create(run,f.Editing);
        var data=JsonSerializer.SerializeToNode(new { planId=plan.PlanId.ToString("D"),planDigest=plan.PlanDigest,verified=false,outcome=result.Outcome.ToString(),
            files=result.Operations.Select(o=>new { o.Target,o.BeforeHash,o.DesiredHash,o.CommitReached,o.Recovery }),
            build=result.Build?.Category,tests=result.Tests?.Category,testsPassed=result.Tests?.TestsPassed })!;
        data["repair"]=JsonSerializer.SerializeToNode(feedback);
        var observation=new ToolExecutionResult(ToolOutcome.VerificationFailed,JsonSerializer.SerializeToElement(data),compiler ? "BuildFailed" : "TestFailed");
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(ToolExecutor.SerializeResult(observation))<=3072);
        var messages=new JsonArray
        {
            new JsonObject { ["role"]="system",["content"]=compiler ? SourceRepairFeedback.CompilerInstruction : SourceRepairFeedback.Instruction },
            new JsonObject { ["role"]="user",["content"]=Request(f,compiler).Prompt },
            new JsonObject { ["role"]="assistant",["tool_calls"]=new JsonArray(new JsonObject { ["id"]="probe-call",["type"]="function",["function"]=new JsonObject
                { ["name"]=ApplySourcePlanTool.ToolName,["arguments"]="{\"a\":[{\"old\":\"return 1;\",\"new\":\"return 3;\"}],\"b\":[{\"old\":\"return 1;\",\"new\":\"return 2;\"}]}" } }) },
            new JsonObject { ["role"]="tool",["tool_call_id"]="probe-call",["content"]=ToolExecutor.SerializeResult(observation) }
        };
        if(compiler)
        {
            var literalArguments=new JsonObject { ["a"]=new JsonArray(new JsonObject { ["old"]="return 1;",["new"]="return \"two\";" }),["b"]=new JsonArray(new JsonObject { ["old"]="return 1;",["new"]="return 2;" }) };
            messages.Insert(2,new JsonObject { ["role"]="user",["content"]="Exact source-plan literal arguments (data): "+literalArguments.ToJsonString() });
            messages[3]!["content"]="";
            messages[3]!["tool_calls"]![0]!["function"]!["arguments"]=literalArguments.ToJsonString();
        }
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(messages.ToJsonString())<=8192);
        using var handler=new Measurement(); using var http=new HttpClient(handler); var adapter=new NativeToolModelAdapter(http);
        registry.TryGet(ApplySourcePlanTool.ToolName,out var registered);
        SourceRepairProbeReceipt? receipt=null;
        var maximumLatency=0d;
        foreach(var mode in new[] { "ColdRepairPrompt","WarmRepairPrompt","ChatContention" })
        {
            var hashes=plan.Entries.Select(e=>f.Hash(e.Target)).ToArray(); var approvals=liveApproval.Calls; var timer=Stopwatch.StartNew();
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lease.CancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(120));
            Task? chat=null;
            if(mode=="ChatContention") chat=StreamChat(runtime,timeout.Token);
            var proposed=await adapter.GenerateAsync(run,messages,[registered!.Descriptor],timeout.Token,requireTool:true);
            evidence.Add(new { scenario="ReadOnlyProbeResponse",mode,durationMs=timer.ElapsedMilliseconds,
                usage=handler.Latest!["usage"]!.DeepClone(),timings=handler.Latest["timings"]!.DeepClone(),nativeResponse=handler.Latest.DeepClone(),toolDispatches=0,approvalRequests=0 });
            if(chat is not null) await chat;
            Assert.Equal(ApplySourcePlanTool.ToolName,proposed.ToolCall!.Name);
            var source=Assert.IsType<ApplySourcePlanTool>(registered!.Tool);
            Assert.True(source.TryValidateRepair(run,proposed.ToolCall.Arguments,out var repairInput,out var reason),reason);
            Assert.True(source.TryComposeRepair(run,repairInput!,out var composed,out reason),reason);
            Assert.Equal(2,composed.Length);
            foreach(var file in composed) { var edit=Assert.Single(file.Edits);Assert.Equal("return 1;",edit.OldText);Assert.Equal("return 2;",edit.NewText); }
            evidence.Add(new { scenario="HostRepairProjection",mode,repairInput!.FailedTarget,repairInput.APlanId,repairInput.APlanDigest,
                advisory=repairInput.AdvisoryExclusions,approvedA=result.HasMatchingApproval(plan),modelOwnsNonfailed=false });
            Assert.Equal(approvals,liveApproval.Calls); Assert.Equal(hashes,plan.Entries.Select(e=>f.Hash(e.Target))); Assert.True(f.Editing.VerifySourcePlanBaseline(plan));
            var raw=handler.Latest!; var usage=raw["usage"]!; var timings=raw["timings"]!;
            maximumLatency=Math.Max(maximumLatency,timer.Elapsed.TotalSeconds);
            receipt=new(f.Profile.Digest,model.ModelId,model.ModelPath,maximumLatency,usage["prompt_tokens"]!.GetValue<int>(),usage["completion_tokens"]!.GetValue<int>(),true,CompilerAdequate:compiler);
            evidence.Add(new { scenario="ReadOnlyDiagnosticProbe",mode,durationMs=timer.ElapsedMilliseconds,promptTokens=receipt.PromptTokens,completionTokens=receipt.CompletionTokens,
                timings=timings.DeepClone(),adequate=true,sourceWrites=0,approvalRequests=0,toolDispatches=0,
                feedbackBytes=SourceRepairFeedback.Bytes(feedback),contextBytes=System.Text.Encoding.UTF8.GetByteCount(messages.ToJsonString()),
                failedStateDigest=EditingWorkflowService.SourcePlanFailedDigest(plan),recoveredBaseDigest=result.RecoveredBaseDigest });
            Assert.True(timer.Elapsed<TimeSpan.FromSeconds(120)); Assert.Equal(lease.Pid,server.OwnedPid);
        }
        return receipt!;
    }
    private static async Task StreamChat(ConversationRuntimeWorkflow runtime,CancellationToken token)
    {
        await foreach(var _ in runtime.StreamAsync([new ChatMessagePayload { Role="user",Content="Reply READY only." }],false,new LlamaGenerationOptions { MaxTokens=64,Temperature=0 },token)) { }
    }
    private sealed class EngineeringApproval : IWriteApprovalProvider
    {
        public Task<WriteApprovalDecision> RequestAsync(SafeWriteProposal p,CancellationToken token)=>throw new InvalidOperationException();
        public Task<SourcePlanApproval> RequestPlanAsync(SourceEditPlan plan,CancellationToken token)=>Task.FromResult(SourcePlanApproval.For(plan,true));
    }
    private sealed class Measurement : DelegatingHandler
    {
        public JsonNode? Latest;
        public Measurement():base(new HttpClientHandler()) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Assert.Equal("127.0.0.1",request.RequestUri!.Host);
            var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;
            Assert.Equal("required",body["tool_choice"]!.GetValue<string>());
            Assert.Equal(ApplySourcePlanTool.ToolName,Assert.Single(body["tools"]!.AsArray())!["function"]!["name"]!.GetValue<string>());
            var response=await base.SendAsync(request,token); Latest=JsonNode.Parse(await response.Content.ReadAsStringAsync(token)); return response;
        }
    }
}
