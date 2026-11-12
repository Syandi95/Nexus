using System.IO;
using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Text;
using Moq;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Pipelines;
using Nexus.AI.Llama.Services;
using Nexus.App.Services;
using Nexus.App.Services.Agent;
using Nexus.App.Services.Agent.Tools;
using Nexus.Contracts;
using Nexus.Foundation.Logging;
using Nexus.Foundation.Registry;

namespace Nexus.Integration.Tests;

public sealed class SharedRuntimeAgentTests
{
    [Fact]
    public async Task NaturalSourceProposal_SendsGenericMemberGuidance_WithoutChangingLiteralOrReadOnlyRequests()
    {
        using var workspace=await SourcePlanTestWorkspace.Create();
        using var reader=new ProjectReadContextWorkflow();
        var profile=new Nexus.AI.EditingLite.Services.SourceVerificationProfile(workspace.Profile.Id,workspace.Root,workspace.Profile.Sdk,["Product/Alpha.cs","Product/Beta.cs"]);
        var grant=await reader.GrantAsync(workspace.Root,"synthetic-guidance",CancellationToken.None);
        var sources=await reader.ReadExactForEditAsync(grant,grant.Session,profile.Targets,CancellationToken.None);
        var before=profile.Targets.ToDictionary(p=>p,p=>workspace.Hash(p));
        foreach(var mode in new[] {"proposal","literal","read-only"})
        {
            using var handler=new GuidanceCaptureHandler();
            using var runtime=new Fixture(handler,[]);
            await runtime.Workflow.StartActiveModelAsync();
            var scope=mode=="proposal" ? new SourceProposalScope(profile,grant.Session,sources,["return 1;","return 1;"],()=>grant.Check(grant.Session),grant.Consume,grant.Cancellation) : null;
            var intent=mode=="literal" ? new SourcePlanIntent(profile.Id,profile.Targets.Select(p=>new Nexus.AI.EditingLite.Services.SourceFileEdit(p,before[p],[new("return 1;","return 2;")])).ToImmutableArray()) : null;
            var result=await runtime.Agent.ExecuteAsync(new(grant.Session,"Add a computed readonly Total to a synthetic invoice from its current quantity and price; preserve requested semantics.",
                MaxOutputTokens:256,EnableReadOnlyToolCall:true,Limits:new(MaxModelTurns:2,MaxToolCalls:1,MaxRetries:0),
                Verification:mode=="read-only"?AgentVerificationRequirement.ModelSnapshot:AgentVerificationRequirement.SourcePlan,SourceIntent:intent,ProposalScope:scope),CancellationToken.None);
            Assert.Equal("OutputLimit",result.ErrorCategory);
            var payload=System.Text.Json.Nodes.JsonNode.Parse(Assert.Single(handler.Requests))!;
            var system=payload["messages"]![0]!["content"]!.GetValue<string>();
            if(mode=="proposal")
            {
                Assert.Contains("auto-property initializer",system);
                Assert.Contains("instance members",system);
                Assert.Contains("computed getter",system);
                Assert.Contains("requested behavior",system);
            }
            else Assert.DoesNotContain("auto-property initializer",system);
            Assert.Equal(256,payload["max_tokens"]!.GetValue<int>());
            Assert.Empty(result.Context.Progress!.ToolDispatches);
            Assert.Equal(0,result.Context.Progress.WriteOperationCount);
            Assert.All(before,p=>Assert.Equal(p.Value,workspace.Hash(p.Key)));
        }
    }

    private sealed class GuidanceCaptureHandler : HttpMessageHandler
    {
        public List<string> Requests {get;}=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Requests.Add(await request.Content!.ReadAsStringAsync(token));
            var response=System.Text.Json.Nodes.JsonNode.Parse(ToolProtocolTests.Response("partial",null))!;
            response["choices"]![0]!["finish_reason"]="length";
            return new(HttpStatusCode.OK) {Content=new StringContent(response.ToJsonString())};
        }
    }

    [Fact]
    public async Task InactiveRuntime_RejectsWithoutStartingInferenceOrServer()
    {
        using var fixture = new Fixture();
        var result = await fixture.Agent.ExecuteAsync(new AgentRequest("run","quick"),CancellationToken.None);
        Assert.Equal(AgentRunOutcome.Rejected,result.Outcome);
        Assert.Equal(0,fixture.Handler.Calls);
        fixture.Engine.Verify(e=>e.StartServerAsync(It.IsAny<string>(),It.IsAny<int>()),Times.Never);
    }

    [Fact]
    public async Task CancelAgent_DoesNotCancelConcurrentChatRequest()
    {
        using var fixture = new Fixture();
        await fixture.Workflow.StartActiveModelAsync();
        using var agentCancel = new CancellationTokenSource();
        using var chatCancel = new CancellationTokenSource();
        var agent = fixture.Agent.ExecuteAsync(new AgentRequest("agent-session","agent-block"),agentCancel.Token);
        var chat = Consume(fixture.Workflow,"chat-block",chatCancel.Token);
        await fixture.Handler.AgentStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Handler.ChatStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        agentCancel.Cancel();
        Assert.Equal(AgentRunOutcome.Cancelled,(await agent.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
        Assert.False(chat.IsCompleted);
        Assert.False(chatCancel.IsCancellationRequested);
        chatCancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>chat);
        Assert.Equal(ServerState.Ready,fixture.State);
        fixture.Engine.Verify(e=>e.StopServerAsync(),Times.Never);
    }

    [Fact]
    public async Task CancelChat_DoesNotCancelAgentRequest()
    {
        using var fixture = new Fixture();
        await fixture.Workflow.StartActiveModelAsync();
        using var agentCancel = new CancellationTokenSource();
        using var chatCancel = new CancellationTokenSource();
        var agent = fixture.Agent.ExecuteAsync(new AgentRequest("agent-session","agent-block"),agentCancel.Token);
        var chat = Consume(fixture.Workflow,"chat-block",chatCancel.Token);
        await fixture.Handler.ChatStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Handler.AgentStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        chatCancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>chat);
        Assert.False(agent.IsCompleted);
        Assert.False(agentCancel.IsCancellationRequested);
        agentCancel.Cancel();
        Assert.Equal(AgentRunOutcome.Cancelled,(await agent).Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NavigationOrSwitch_CancelsAndDrainsAgentBeforeStoppingModel(bool switchModel)
    {
        using var fixture = new Fixture();
        await fixture.Workflow.StartActiveModelAsync();
        var agent = fixture.Agent.ExecuteAsync(new AgentRequest("s","agent-block"),CancellationToken.None);
        await fixture.Handler.AgentStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.OnStop = ()=>Assert.True(agent.IsCompleted,"Model must not stop until consumer completed.");
        if(switchModel) await fixture.Workflow.SwitchModelAsync(fixture.Other).WaitAsync(TimeSpan.FromSeconds(5));
        else await fixture.Workflow.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var result = await agent;
        Assert.Equal(AgentRunOutcome.Cancelled,result.Outcome);
        Assert.Equal("WorkspaceRuntimeDeactivated",result.ErrorCategory);
        Assert.Equal(fixture.Original.Id,result.Context.Model!.ModelId);
        var next = await fixture.Agent.ExecuteAsync(new AgentRequest("next","quick"),CancellationToken.None);
        Assert.Equal(switchModel ? AgentRunOutcome.Completed : AgentRunOutcome.Rejected,next.Outcome);
        if(switchModel) Assert.Equal(fixture.Other.Id,next.Context.Model!.ModelId);
    }

    [Fact]
    public async Task SameModelReuse_DoesNotRevokeAgentOrRestartRuntime()
    {
        using var fixture = new Fixture();
        await fixture.Workflow.StartActiveModelAsync();
        fixture.Engine.Invocations.Clear();
        using var stop = new CancellationTokenSource();
        var agent = fixture.Agent.ExecuteAsync(new AgentRequest("s","agent-block"),stop.Token);
        await fixture.Handler.AgentStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Workflow.StartActiveModelAsync();
        await fixture.Workflow.SwitchModelAsync(fixture.Original);
        Assert.False(agent.IsCompleted);
        fixture.Engine.Verify(e=>e.StartServerAsync(It.IsAny<string>(),It.IsAny<int>()),Times.Never);
        fixture.Engine.Verify(e=>e.StopServerAsync(),Times.Never);
        var busy=await fixture.Agent.ExecuteAsync(new AgentRequest("another","quick"),CancellationToken.None);
        Assert.Equal(AgentRunOutcome.Rejected,busy.Outcome);
        stop.Cancel();
        await agent;
    }

    [Fact]
    public async Task TimeoutOrFailure_ReleasesLeaseAndDoesNotRetainSessionOrLogContent()
    {
        using var fixture = new Fixture();
        await fixture.Workflow.StartActiveModelAsync();
        var timed = await fixture.Agent.ExecuteAsync(new AgentRequest("timeout","agent-block",Timeout:TimeSpan.FromMilliseconds(40)),CancellationToken.None);
        Assert.Equal(AgentRunOutcome.TimedOut,timed.Outcome);
        var failed = await fixture.Agent.ExecuteAsync(new AgentRequest("failed","malformed"),CancellationToken.None);
        Assert.Equal("MalformedModelResponse",failed.ErrorCategory);
        var partial = await fixture.Agent.ExecuteAsync(new AgentRequest("partial","unfinished"),CancellationToken.None);
        Assert.Equal("IncompleteModelResponse",partial.ErrorCategory);
        var good = await fixture.Agent.ExecuteAsync(new AgentRequest("separate","quick","context-secret"),CancellationToken.None);
        Assert.Equal(AgentRunOutcome.Completed,good.Outcome);
        Assert.Equal("separate",good.Context.SessionId);
        Assert.NotEqual(timed.Context.RunId,good.Context.RunId);
        Assert.DoesNotContain(fixture.Logs,message=>message.Contains("context-secret",StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Logs,message=>message.Contains("answer-secret",StringComparison.Ordinal));
        Assert.Equal(ServerState.Ready,fixture.State);
    }

    [Fact]
    public async Task InputAndResultLimits_AreEnforcedWithoutToolsOrPersistence()
    {
        using var fixture = new Fixture();
        await fixture.Workflow.StartActiveModelAsync();
        var rejected = await fixture.Agent.ExecuteAsync(new AgentRequest("s",new string('x',SharedRuntimeAgent.MaxInputCharacters+1)),CancellationToken.None);
        Assert.Equal(AgentRunOutcome.Rejected,rejected.Outcome);
        Assert.Equal(0,fixture.Handler.Calls);
        var oversized = await fixture.Agent.ExecuteAsync(new AgentRequest("s","oversized"),CancellationToken.None);
        Assert.Equal("ResultLimit",oversized.ErrorCategory);
        Assert.Empty(oversized.Content);
        var next=await fixture.Agent.ExecuteAsync(new AgentRequest("s2","quick"),CancellationToken.None);
        Assert.Equal(AgentRunOutcome.Completed,next.Outcome);
    }

    private static async Task Consume(ConversationRuntimeWorkflow runtime,string prompt,CancellationToken token)
    {
        await foreach(var chunk in runtime.StreamAsync([new ChatMessagePayload { Role="user",Content=prompt }],false,token)) { }
    }

    internal sealed class Fixture : IDisposable
    {
        private readonly string _root=Path.Combine(Path.GetTempPath(),"nexus-agent-tests-"+Guid.NewGuid().ToString("N"));
        private readonly HttpClient _http;
        private ModelCatalogSnapshot _snapshot;
        private string? _path;
        public ServerState State { get; private set; }
        public Action? OnStop { get; set; }
        public ModelCatalogItem Original { get; }
        public ModelCatalogItem Other { get; }
        public Mock<IGenericLlamaEngine> Engine { get; } = new();
        public ControlledHandler Handler { get; } = new();
        public ConversationRuntimeWorkflow Workflow { get; }
        public SharedRuntimeAgent Agent { get; }
        public List<string> Logs { get; } = new();
        public Fixture(HttpMessageHandler? transport=null,INexusTool[]? tools=null,Nexus.AI.EditingLite.Services.ApprovedTextWorkspace? workspace=null)
        {
            Directory.CreateDirectory(_root);
            var first=Path.Combine(_root,"one.gguf");
            var second=Path.Combine(_root,"two.gguf");
            File.WriteAllBytes(first,[1]); File.WriteAllBytes(second,[2]);
            Original=new ModelCatalogItem("one","One","one.json",first,ModelCatalogItemStatus.Valid,null,true);
            Other=new ModelCatalogItem("two","Two","two.json",second,ModelCatalogItemStatus.Valid,null,false);
            _snapshot=new ModelCatalogSnapshot(Original.Id,[Original,Other]);
            var catalog=new Mock<IModelCatalogService>();
            catalog.SetupGet(c=>c.CurrentSnapshot).Returns(()=>_snapshot);
            catalog.Setup(c=>c.Load()).Returns(()=>_snapshot);
            catalog.Setup(c=>c.UpdateActiveModelAsync(It.IsAny<string>(),It.IsAny<CancellationToken>()))
                .Returns<string,CancellationToken>((id,_)=>Task.FromResult(_snapshot=new ModelCatalogSnapshot(id,[Original,Other])));
            var server=new Mock<ILlamaServerManager>();
            server.SetupGet(s=>s.State).Returns(()=>State);
            server.SetupGet(s=>s.RuntimeModelPath).Returns(()=>_path);
            server.SetupGet(s=>s.OwnedPid).Returns(()=>State==ServerState.Ready ? 123 : null);
            server.Setup(s=>s.IsServerHealthyAsync()).ReturnsAsync(true);
            Engine.SetupGet(e=>e.Port).Returns(8080);
            Engine.Setup(e=>e.LoadEngineAsync()).Returns(Task.CompletedTask);
            Engine.Setup(e=>e.StartServerAsync(It.IsAny<string>(),It.IsAny<int>())).Returns<string,int>((path,_)=>
            {
                _path=path; State=ServerState.Ready; return Task.FromResult(true);
            });
            Engine.Setup(e=>e.StopServerAsync()).Returns(()=>
            {
                OnStop?.Invoke(); State=ServerState.Stopped; _path=null; return Task.CompletedTask;
            });
            _http=new HttpClient(transport ?? Handler);
            var policy=new ImageApplicationModelSelectionPolicy(new ModelRegistry(),new ModelMetadataParser(),_root,Path.Combine(_root,"unused.json"));
            var pipeline=new LlamaConversationPipeline(_http,policy,Engine.Object);
            Workflow=new ConversationRuntimeWorkflow(catalog.Object,policy,Engine.Object,pipeline,server.Object);
            var logger=new Mock<ILoggerService>();
            logger.Setup(l=>l.LogInfo(It.IsAny<string>(),It.IsAny<string>())).Callback<string,string>((_,message)=>Logs.Add(message));
            AgentOrchestrator? orchestrator=null;
            if(tools is not null)
            {
                var registry=new ToolRegistry(tools);
                var toolPolicy=new ReadOnlyToolPolicy(tools.Where(t=>t.Descriptor.Risk==ToolRisk.ReadOnly).Select(t=>t.Descriptor.Name),
                    tools.Where(t=>t.Descriptor.Risk==ToolRisk.SafeWrite).Select(t=>t.Descriptor.Name),workspace);
                orchestrator=new AgentOrchestrator(new NativeToolModelAdapter(_http),registry,toolPolicy,new ToolExecutor(registry,toolPolicy,logger.Object),
                    (_,_)=>Task.FromResult(NativeToolSupport.Supported),logger.Object);
            }
            Agent=new SharedRuntimeAgent(Workflow,server.Object,logger.Object,orchestrator);
        }
        public void Dispose() { _http.Dispose(); Directory.Delete(_root,true); }
    }

    internal sealed class ControlledHandler : HttpMessageHandler
    {
        public int Calls;
        public TaskCompletionSource AgentStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ChatStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            var body=await request.Content!.ReadAsStringAsync(token);
            if(body.Contains("agent-block",StringComparison.Ordinal) || body.Contains("chat-block",StringComparison.Ordinal))
            {
                var signal=body.Contains("agent-block",StringComparison.Ordinal) ? AgentStarted : ChatStarted;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StreamContent(new BlockingStream(signal)) };
            }
            if(body.Contains("malformed",StringComparison.Ordinal))
                return Response("data: {invalid}\n\n");
            if(body.Contains("unfinished",StringComparison.Ordinal))
                return Response("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n");
            var text=body.Contains("oversized",StringComparison.Ordinal) ? new string('x',SharedRuntimeAgent.MaxResultCharacters+1) : "answer-secret";
            return Response("data: {\"choices\":[{\"delta\":{\"content\":\""+text+"\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n");
        }
        private static HttpResponseMessage Response(string text) => new(HttpStatusCode.OK) { Content=new StringContent(text) };
    }

    private sealed class BlockingStream(TaskCompletionSource started) : Stream
    {
        private readonly byte[] _first=Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"waiting\"}}]}\n\n");
        private bool _sent;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)
        {
            if(!_sent)
            {
                _sent=true; _first.AsMemory().CopyTo(buffer); started.TrySetResult(); return _first.Length;
            }
            await Task.Delay(Timeout.Infinite,token); return 0;
        }
        public override bool CanRead=>true; public override bool CanSeek=>false; public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();
        public override long Position { get=>throw new NotSupportedException(); set=>throw new NotSupportedException(); }
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override void Flush()=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
