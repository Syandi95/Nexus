using System.Collections.Immutable;
using System.Net.Http;
using System.Reflection;
using Moq;
using Nexus.AI.EditingLite.Services;
using Nexus.AI.Llama.Services;
using Nexus.App.Services;
using Nexus.App.Services.Agent;
using Nexus.Foundation.Logging;
using Nexus.App.ViewModels;
using Nexus.App.Services.Agent.Tools;
using System.Text.Json;

namespace Nexus.Integration.Tests;

public sealed class I2BReadinessDeadlineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceToolTimer_UsesRemainingPilotBudget_AndPreservesUnscoped120Cap(bool pilot)
    {
        using var f=await WorkflowFixture.Create();await f.CheckToolTimer(pilot);f.AssertPreserved();
    }

    [Theory]
    [InlineData(-3600)]
    [InlineData(0)]
    [InlineData(3600)]
    public async Task Pilot240_PreflightUsesSameElapsedBudget_WithoutRefill(int wallShift)
    {
        using var f=await WorkflowFixture.Create();
        f.AfterRead=_=>{f.Clock.Advance(TimeSpan.FromSeconds(45));f.Clock.Utc=f.Clock.Utc.AddSeconds(wallShift);};
        var result=await f.Run(seconds:240);
        Assert.NotEqual("InvalidRequestOrLimit",result.ErrorCategory);
        Assert.InRange(result.Context.Limits!.MaxRunDuration.TotalSeconds,194.99,195);
        Assert.InRange((result.Context.Deadline-result.Context.StartedAt).TotalSeconds,194.99,195);
        Assert.Equal(2,result.Context.Limits.MaxModelTurns);Assert.Equal(1,result.Context.Limits.MaxToolCalls);
        Assert.Equal(1,result.Context.Limits.MaxSourcePlans);Assert.Equal(0,result.Context.Limits.MaxRepairPlans);Assert.Equal(0,result.Context.Limits.MaxRetries);
        Assert.Equal(1,result.Context.Limits.MaxBuildStarts);Assert.Equal(1,result.Context.Limits.MaxTestStarts);
        Assert.Equal(256,result.Context.MaxOutputTokens);Assert.Equal(0,f.Transport.Calls);f.AssertPreserved();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pilot240_PreflightOvertimeOrCancellation_PreventsDispatch(bool caller)
    {
        using var f=await WorkflowFixture.Create();
        f.AfterRead=token=>{if(caller)f.Caller.Cancel();else f.Clock.Advance(TimeSpan.FromSeconds(241));token.ThrowIfCancellationRequested();};
        var error=await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Run(seconds:240));
        Assert.Equal(!caller,error.Data["ExecutionDeadline"] is true);
        Assert.Equal(caller,f.Caller.IsCancellationRequested);
        Assert.Equal(0,f.Transport.Calls);f.AssertPreserved();
    }

    [Fact]
    public async Task Pilot240_ActiveDeadlineStillExpiresAfterPreflight_NoSecondAllocation()
    {
        using var f=await WorkflowFixture.Create();f.AfterRead=_=>f.Clock.Advance(TimeSpan.FromSeconds(45));
        var result=await f.Run(state=>{if(state=="Analyzing")f.Clock.Advance(TimeSpan.FromSeconds(196));},240);
        Assert.Equal(AgentRunOutcome.TimedOut,result.Outcome);Assert.Equal("ExecutionDeadline",result.ErrorCategory);
        Assert.False(f.Caller.IsCancellationRequested);Assert.Equal(0,f.Transport.Calls);f.AssertPreserved();
    }

    [Fact]
    public async Task Pilot241_IsRejectedBeforePreflightRead()
    {
        using var f=await WorkflowFixture.Create();var reads=0;f.AfterRead=_=>reads++;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Run(seconds:241));
        Assert.Equal(0,reads);Assert.Equal(0,f.Transport.Calls);f.AssertPreserved();
    }

    [Theory]
    [InlineData(121)]
    [InlineData(180)]
    [InlineData(181)]
    [InlineData(240)]
    [InlineData(241)]
    public async Task GenericAgent_CannotClaimPilotDurationWithoutHostScope(int seconds)
    {
        using var f=new SharedRuntimeAgentTests.Fixture();
        var result=await f.Agent.ExecuteAsync(new("unadmitted","offline",Limits:new(RunDuration:TimeSpan.FromSeconds(seconds))),CancellationToken.None);
        Assert.Equal(AgentRunOutcome.Rejected,result.Outcome);Assert.Equal("InvalidRequestOrLimit",result.ErrorCategory);Assert.Equal(0,f.Handler.Calls);
        Assert.Equal(TimeSpan.FromSeconds(60),new AgentRunLimits().MaxRunDuration);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("session")]
    [InlineData("revoked")]
    [InlineData("turns")]
    [InlineData("calls")]
    public async Task ExtendedAgentRequiresCurrentPilotScope_SessionAndUnchangedCaps(string mode)
    {
        using var f=await WorkflowFixture.Create();
        var result=await f.ScopedRun(mode);
        Assert.Equal(mode=="valid"?"ToolProtocolUnavailable":mode=="revoked"?"ModelTransportCancelled":"InvalidRequestOrLimit",result.ErrorCategory);
        Assert.Equal(0,f.Transport.Calls);f.AssertPreserved();
    }

    [Fact]
    public async Task OnlyExactReviewedPilotGets240_LegacyAndOtherProfilesStay120_InUiAndHost()
    {
        using var reader=new ProjectReadContextWorkflow();var catalog=new ReviewedProjectCatalog(reader);
        var select=typeof(ReviewedProjectCatalog).GetMethod("Select",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var pilot=(AgentWorkspaceSelection)select.Invoke(catalog,[ReviewedProjectCatalog.DisposableRoot])!;
        var legacy=(AgentWorkspaceSelection)select.Invoke(catalog,[ReviewedProjectCatalog.Root])!;
        using var ordinary=await SourcePlanTestWorkspace.Create();
        var selections=new[] {pilot,legacy,new AgentWorkspaceSelection(ordinary.Profile,[])};
        var method=typeof(ReviewedProjectCatalog).GetMethod("RunBudget",BindingFlags.NonPublic|BindingFlags.Static);
        foreach(var (selection,index) in selections.Select((s,i)=>(s,i)))
        {
            var expected=index==0?240:120;
            Assert.Equal(TimeSpan.FromSeconds(expected),method is null?TimeSpan.FromSeconds(120):(TimeSpan)method.Invoke(null,[selection.Profile])!);
            var workflow=new Mock<IAgentWorkspaceWorkflow>();workflow.Setup(w=>w.Select(selection.Profile.Root)).Returns(selection);
            var entered=new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
            workflow.Setup(w=>w.RunAsync(It.IsAny<AgentWorkspaceSelection>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<ImmutableArray<SourceFileEdit>>(),It.IsAny<DateTimeOffset>(),It.IsAny<Action<string>>(),It.IsAny<Action<SourceEditPlan,AgentWorkspaceApprovalProvider>>(),It.IsAny<CancellationToken>()))
                .Returns(async(AgentWorkspaceSelection s,string session,string prompt,ImmutableArray<SourceFileEdit> edits,DateTimeOffset deadline,Action<string> state,Action<SourceEditPlan,AgentWorkspaceApprovalProvider> review,CancellationToken token)=>
                {entered.TrySetResult(deadline);await Task.Delay(Timeout.Infinite,token);throw new InvalidOperationException("Cancellation required");});
            ProjectReadGrant? grant=null;
            if(index<2)
            {
                grant=await reader.GrantAsync(selection.Profile.Root,"ui-budget",CancellationToken.None);
                var authorized=(AgentWorkspaceSelection)typeof(ReviewedProjectCatalog).GetMethod("Authorize",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(catalog,[selection,grant])!;
                workflow.Setup(w=>w.AuthorizeReviewedEdit(selection,grant)).Returns(authorized);
            }
            await WpfAppHost.RunAsync(async()=>
            {
                var vm=new AgentWorkspaceViewModel(workflow.Object);await vm.SelectWorkspaceAsync(selection.Profile.Root);
                Assert.Contains(expected+" detik",vm.RemainingText);
                if(grant is null)return;
                vm.BindReadGrant(grant);vm.ReviewedEditAuthorized=true;vm.Prompt="offline UI budget";
                var before=DateTimeOffset.UtcNow;var running=vm.RunAsync();
                try
                {
                    var deadline=await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.InRange((deadline-before).TotalSeconds,expected-1,expected+1);
                }
                finally {await vm.CancelAndDrainAsync();await running;}
            });
        }
    }

    [Theory]
    [InlineData(-3600)]
    [InlineData(0)]
    [InlineData(3600)]
    public async Task PreflightElapsedIsDeducted_FromAgentBudget_DespiteWallClockShift(int wallShift)
    {
        using var f=await WorkflowFixture.Create();
        f.AfterRead=_=>{f.Clock.Advance(TimeSpan.FromSeconds(45));f.Clock.Utc=f.Clock.Utc.AddSeconds(wallShift);};
        var result=await f.Run();
        Assert.InRange(result.Context.Limits!.MaxRunDuration.TotalSeconds,74.99,75);
        Assert.Equal(2,result.Context.Limits.MaxModelTurns);Assert.Equal(1,result.Context.Limits.MaxToolCalls);Assert.Equal(0,result.Context.Limits.MaxRetries);
        Assert.Equal(256,result.Context.MaxOutputTokens);Assert.Equal(0,f.Transport.Calls);f.AssertPreserved();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreflightStopsOnDeadlineOrCallerCancellation_WithoutModelDispatch(bool caller)
    {
        using var f=await WorkflowFixture.Create();
        f.AfterRead=token=>{if(caller)f.Caller.Cancel();else f.Clock.Advance(TimeSpan.FromSeconds(121));token.ThrowIfCancellationRequested();};
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Run());
        Assert.Equal(caller,f.Caller.IsCancellationRequested);Assert.Equal(0,f.Transport.Calls);f.AssertPreserved();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveDeadlineRemainsBound_AfterPreflight_AndDoesNotBecomeCallerCancellation(bool caller)
    {
        using var f=await WorkflowFixture.Create();f.AfterRead=_=>f.Clock.Advance(TimeSpan.FromSeconds(45));
        var result=await f.Run(state=>
        {
            if(state!="Analyzing")return;
            if(caller)f.Caller.Cancel();else f.Clock.Advance(TimeSpan.FromSeconds(76));
        });
        Assert.Equal(caller?AgentRunOutcome.Cancelled:AgentRunOutcome.TimedOut,result.Outcome);
        Assert.Equal(caller?"CallerCancelled":"ExecutionDeadline",result.ErrorCategory);
        Assert.Equal(caller,f.Caller.IsCancellationRequested);Assert.Equal(0,f.Transport.Calls);f.AssertPreserved();
    }

    [Theory]
    [InlineData(false,120)]
    [InlineData(true,120)]
    [InlineData(false,180)]
    [InlineData(true,180)]
    public async Task ApprovalUsesOnlyRemainingBudget_AndExpiresWhileOwnerReviews(bool backwards,int seconds)
    {
        using var f=await SourcePlanTestWorkspace.Create();var clock=new ControlledClock();var deadline=clock.Utc.AddSeconds(seconds);
        clock.Advance(TimeSpan.FromSeconds(seconds-25));if(backwards)clock.Utc-=TimeSpan.FromHours(1);
        var plan=await Plan(f);using var provider=Approval(f.Profile,deadline,_=>{},clock,TimeSpan.FromSeconds(25));provider.BindRun(plan.RunId);
        var waiting=provider.RequestPlanAsync(plan,CancellationToken.None);Assert.False(waiting.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(26));
        Assert.Equal("ApprovalExpired",provider.DenialReason);
        // The decision continuation runs asynchronously after the elapsed timer fires.
        Assert.False((await waiting.WaitAsync(TimeSpan.FromSeconds(2))).Approved);Assert.False(provider.Decide(plan.RunId,plan.PlanId,plan.PlanDigest,true));
        Assert.Equal("ApprovalExpired",provider.DenialReason);Assert.All(plan.Entries,e=>Assert.Equal(e.BeforeHash,f.Hash(e.Target)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApprovalExpiredBeforePublication_OrWallClockForward_IsDeniedWithoutWrite(bool wallForward)
    {
        using var f=await SourcePlanTestWorkspace.Create();var clock=new ControlledClock();var deadline=clock.Utc.AddSeconds(120);var plan=await Plan(f);var published=0;
        if(wallForward)clock.Utc+=TimeSpan.FromHours(1);
        using var provider=Approval(f.Profile,deadline,_=>published++,clock,wallForward?TimeSpan.FromSeconds(30):TimeSpan.Zero);provider.BindRun(plan.RunId);
        var decision=provider.RequestPlanAsync(plan,CancellationToken.None);
        Assert.Equal(0,published);Assert.True(decision.IsCompleted);Assert.False((await decision).Approved);
        Assert.All(plan.Entries,e=>Assert.Equal(e.BeforeHash,f.Hash(e.Target)));
    }

    private static Task<SourceEditPlan> Plan(SourcePlanTestWorkspace f)=>f.Editing.PrepareSourcePlanAsync(f.Profile,
        [new("Product/Alpha.cs",f.Hash("Product/Alpha.cs"),"return 1;","return 2;"),new("Product/Beta.cs",f.Hash("Product/Beta.cs"),"return 1;","return 2;")],
        Guid.NewGuid(),"readiness-clock",new(),new(()=>TimeSpan.FromSeconds(120),CancellationToken.None,()=>{}));
    private static AgentWorkspaceApprovalProvider Approval(SourceVerificationProfile p,DateTimeOffset deadline,Action<SourceEditPlan> publish,TimeProvider clock,TimeSpan budget)
    {
        // The old constructor fallback makes the original behavior observable RED.
        var signature=new[] {typeof(SourceVerificationProfile),typeof(DateTimeOffset),typeof(Action<SourceEditPlan>),typeof(TimeProvider),typeof(TimeSpan?)};
        var updated=typeof(AgentWorkspaceApprovalProvider).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,signature,null);
        return updated is null?new(p,deadline,publish,clock):(AgentWorkspaceApprovalProvider)updated.Invoke([p,deadline,publish,clock,budget]);
    }

    private sealed class WorkflowFixture : IDisposable
    {
        private const string Root=@"D:\NEXUS\scratch\recovery-first-20261017\reviewed-i2b-v2";
        private readonly ProjectReadContextWorkflow reader=new();
        private readonly SharedRuntimeAgentTests.Fixture runtime=new();
        private readonly HttpClient http;
        private readonly AgentWorkspaceWorkflow workflow;
        private readonly ReviewedProjectCatalog catalog;
        private AgentWorkspaceSelection selected=null!;
        public ControlledClock Clock {get;}=new();
        public CancellationTokenSource Caller {get;}=new();
        public DenyTransport Transport {get;}=new();
        public Action<CancellationToken>? AfterRead {get;set;}
        private WorkflowFixture()
        {
            http=new(Transport);var bridge=new Mock<IProjectReadContextWorkflow>();bridge.SetupGet(r=>r.Current).Returns(()=>reader.Current);bridge.Setup(r=>r.Revoke()).Callback(reader.Revoke);
            bridge.Setup(r=>r.ReadExactForEditAsync(It.IsAny<ProjectReadGrant>(),It.IsAny<string>(),It.IsAny<IReadOnlyList<string>>(),It.IsAny<CancellationToken>()))
                .Returns(async(ProjectReadGrant grant,string session,IReadOnlyList<string> targets,CancellationToken token)=>
                {var snapshots=await reader.ReadExactForEditAsync(grant,session,targets,token);AfterRead?.Invoke(token);return snapshots;});
            catalog=new ReviewedProjectCatalog(bridge.Object);var server=new Mock<ILlamaServerManager>();server.SetupGet(s=>s.State).Returns(ServerState.Ready);
            server.SetupGet(s=>s.OwnedPid).Returns(123);server.SetupGet(s=>s.RuntimeModelPath).Returns(runtime.Original.ResolvedGgufPath);
            var editing=new EditingWorkflowService(new FileSnapshotService(),new ProposalService(),new SyntaxValidatorService(),new FileWriterService());
            object[] arguments=[runtime.Workflow,server.Object,http,editing,new NativeToolCapability(server.Object),Mock.Of<ILoggerService>(),catalog];
            var updated=typeof(AgentWorkspaceWorkflow).GetConstructors().SingleOrDefault(c=>c.GetParameters().Length==8);
            workflow=(AgentWorkspaceWorkflow)(updated is null?Activator.CreateInstance(typeof(AgentWorkspaceWorkflow),arguments)!:updated.Invoke([..arguments,Clock]));
        }
        public static async Task<WorkflowFixture> Create()
        {
            var f=new WorkflowFixture();try
            {
                await f.runtime.Workflow.StartActiveModelAsync(); // mocked engine only, no process or generation
                f.selected=f.workflow.Select(Root);var grant=await f.reader.GrantAsync(Root,"readiness-clock",CancellationToken.None);
                f.selected=f.workflow.AuthorizeReviewedEdit(f.selected,grant);f.Clock.Utc=DateTimeOffset.UtcNow;return f;
            }
            catch {f.Dispose();throw;}
        }
        public Task<AgentResult> Run(Action<string>? state=null,int seconds=120)=>workflow.RunAsync(selected,"readiness-clock","offline deadline check",ImmutableArray<SourceFileEdit>.Empty,Clock.Utc.AddSeconds(seconds),state??(_=>{}),(_,_)=>throw new InvalidOperationException("No model proposal is authorized in this test"),Caller.Token);
        public async Task<AgentResult> ScopedRun(string mode)
        {
            var task=(Task<SourceProposalScope>)typeof(ReviewedProjectCatalog).GetMethod("CreateScopeAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(catalog,[selected,"readiness-clock",CancellationToken.None])!;
            var scope=await task;if(mode=="revoked")reader.Revoke();
            var limits=new AgentRunLimits(MaxModelTurns:mode=="turns"?3:2,MaxToolCalls:mode=="calls"?2:1,MaxRetries:0,RunDuration:TimeSpan.FromSeconds(240));
            return await runtime.Agent.ExecuteAsync(new(mode=="session"?"foreign":"readiness-clock","offline scope",MaxOutputTokens:256,EnableReadOnlyToolCall:true,Limits:limits,Verification:AgentVerificationRequirement.SourcePlan,ProposalScope:scope),CancellationToken.None);
        }
        public async Task CheckToolTimer(bool pilot)
        {
            var admitted=await ScopedRun("valid");
            var anchors=new[] {"    public ExecutionProvider ExecutionProvider { get; init; } = ExecutionProvider.Cpu;","    public int ActiveSessionsCount { get; init; }"};
            var edits=selected.Profile.Targets.Select((t,i)=>new SourceFileEdit(t,selected.Profile.InitialSourceHashes[t],anchors[i],anchors[i]+"\r\n    public bool DeadlineProbe => false;")).ToImmutableArray();
            var run=admitted.Context with {Progress=new AgentRunProgress(),
                SourceIntent=new(selected.Profile.Id,edits),ProposalScope=pilot?admitted.Context.ProposalScope:null};
            var approval=new HoldApproval();var logger=Mock.Of<ILoggerService>();var catalog=new SourcePlanCatalog(selected.Profile);
            var editing=new EditingWorkflowService(new FileSnapshotService(),new ProposalService(),new SyntaxValidatorService(),new FileWriterService());
            var registry=new ToolRegistry([new ApplySourcePlanTool(catalog,editing,approval,logger)]);var policy=new ReadOnlyToolPolicy([],sourceCatalog:catalog);
            var constructor=typeof(ToolExecutor).GetConstructors().Single(c=>c.GetParameters().Length==4);
            var executor=(ToolExecutor)constructor.Invoke([registry,policy,logger,Clock]);
            var args=JsonSerializer.Serialize(new {a=edits[0].Edits.Select(e=>new {old=e.OldText,@new=e.NewText}),b=edits[1].Edits.Select(e=>new {old=e.OldText,@new=e.NewText})});
            var executing=executor.ExecuteAsync(new(run),"deadline-probe",ApplySourcePlanTool.ToolName,args,CancellationToken.None);
            var token=await approval.Pending.Task.WaitAsync(TimeSpan.FromSeconds(60));
            try
            {
                Clock.Advance(TimeSpan.FromSeconds(121));
                Assert.Equal(!pilot,token.IsCancellationRequested);
                Clock.Advance(TimeSpan.FromSeconds(120));Assert.True(token.IsCancellationRequested);
            }
            finally {Clock.Advance(TimeSpan.FromSeconds(180));await executing.WaitAsync(TimeSpan.FromSeconds(5));}
            Assert.Empty(run.Progress.SourceResult!.Operations);Assert.Null(run.Progress.SourceResult.Build);Assert.Null(run.Progress.SourceResult.Tests);
        }
        public void AssertPreserved(){foreach(var entry in selected.Profile.InitialSourceHashes)Assert.Equal(entry.Value,new FileSnapshotService().ComputeSha256Hash(System.IO.Path.Combine(Root,entry.Key)));}
        public void Dispose(){Caller.Dispose();reader.Dispose();http.Dispose();runtime.Dispose();}
    }
    private sealed class HoldApproval : IWriteApprovalProvider
    {
        public TaskCompletionSource<CancellationToken> Pending {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<WriteApprovalDecision> RequestAsync(SafeWriteProposal p,CancellationToken token)=>throw new InvalidOperationException();
        public async Task<SourcePlanApproval> RequestPlanAsync(SourceEditPlan p,CancellationToken token)
        {Pending.TrySetResult(token);await Task.Delay(Timeout.Infinite,token);return SourcePlanApproval.For(p,false);}
    }
    private sealed class DenyTransport : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {Calls++;throw new InvalidOperationException("No model inference or network dispatch is authorized");}
    }
    private sealed class ControlledClock : TimeProvider
    {
        public DateTimeOffset Utc=DateTimeOffset.UtcNow;
        private long ticks;
        private readonly List<ControlledTimer> timers=[];
        public override DateTimeOffset GetUtcNow()=>Utc;
        public override long GetTimestamp()=>ticks;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override ITimer CreateTimer(TimerCallback callback,object? state,TimeSpan dueTime,TimeSpan period)
        {var timer=new ControlledTimer(this,callback,state);timers.Add(timer);timer.Change(dueTime,period);return timer;}
        public void Advance(TimeSpan elapsed)
        {ticks+=elapsed.Ticks;Utc+=elapsed;foreach(var timer in timers.ToArray())timer.Fire();}
        private sealed class ControlledTimer(ControlledClock clock,TimerCallback callback,object? state) : ITimer
        {
            private long due=long.MaxValue;private bool disposed;
            public bool Change(TimeSpan dueTime,TimeSpan period){if(disposed)return false;due=dueTime==Timeout.InfiniteTimeSpan?long.MaxValue:clock.ticks+dueTime.Ticks;return true;}
            public void Fire(){if(!disposed && clock.ticks>=due){due=long.MaxValue;callback(state);}}
            public void Dispose()=>disposed=true;
            public ValueTask DisposeAsync(){Dispose();return ValueTask.CompletedTask;}
        }
    }
}
