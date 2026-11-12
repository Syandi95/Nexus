using System.Reflection;
using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moq;
using Nexus.AI.Llama.Services;
using Nexus.AI.EditingLite.Services;
using Nexus.App.Services;
using Nexus.App.Services.Agent;
using Nexus.App.Services.Agent.Tools;
using Nexus.App.ViewModels;
using Nexus.Foundation.Logging;

namespace Nexus.Integration.Tests;

public sealed class I2BApprovalBudgetTests
{
    [Fact]
    public async Task OwnerReview_PausesActiveBudgetUntilExactDecision_WithoutRefill()
    {
        using var workspace=await SourcePlanTestWorkspace.Create();var clock=new ReviewClock();
        using var progress=Budget(clock,240);clock.Advance(80);
        var plan=await Plan(workspace);using var provider=Approval(workspace.Profile,clock,progress);
        progress.BindBudgetRun(plan.RunId);provider.BindRun(plan.RunId);var pending=provider.RequestPlanAsync(plan,CancellationToken.None);
        clock.Advance(300);
        Assert.Null(provider.DenialReason);Assert.False(pending.IsCompleted);Assert.InRange(Remaining(progress).TotalSeconds,159.99,160);
        Assert.False(provider.Decide(plan.RunId,plan.PlanId,new string('0',64),true));
        Assert.True(provider.Decide(plan.RunId,plan.PlanId,plan.PlanDigest,true));
        Assert.True((await pending.WaitAsync(TimeSpan.FromSeconds(2))).Approved);
        clock.Advance(159);Assert.InRange(Remaining(progress).TotalSeconds,.99,1);
        clock.Advance(1);Assert.True(Remaining(progress)<=TimeSpan.Zero);
        Assert.False(provider.Decide(plan.RunId,plan.PlanId,plan.PlanDigest,true));Preserved(workspace,plan);
    }

    [Fact]
    public async Task NoResponse_ExpiresAt600MonotonicSeconds_AndCannotBeRearmed()
    {
        using var workspace=await SourcePlanTestWorkspace.Create();var clock=new ReviewClock();using var progress=Budget(clock,240);
        var plan=await Plan(workspace);using var provider=Approval(workspace.Profile,clock,progress);progress.BindBudgetRun(plan.RunId);provider.BindRun(plan.RunId);
        var pending=provider.RequestPlanAsync(plan,CancellationToken.None);clock.Advance(599);
        Assert.Null(provider.DenialReason);Assert.False(pending.IsCompleted);clock.Utc-=TimeSpan.FromHours(4);clock.Advance(1);
        Assert.False((await pending.WaitAsync(TimeSpan.FromSeconds(2))).Approved);
        Assert.Equal("ApprovalExpired",provider.DenialReason);
        Assert.False(provider.Decide(plan.RunId,plan.PlanId,plan.PlanDigest,true));
        Assert.False((await provider.RequestPlanAsync(plan,CancellationToken.None)).Approved);Preserved(workspace,plan);
    }

    [Theory]
    [InlineData(-3600)]
    [InlineData(3600)]
    public async Task ReviewAndActiveAccounting_IgnoreWallClockJumps(int shift)
    {
        using var workspace=await SourcePlanTestWorkspace.Create();var clock=new ReviewClock();using var progress=Budget(clock,240);
        clock.Advance(90);var plan=await Plan(workspace);progress.BindBudgetRun(plan.RunId);
        using var provider=Approval(workspace.Profile,clock,progress);provider.BindRun(plan.RunId);
        var pending=provider.RequestPlanAsync(plan,CancellationToken.None);clock.Utc+=TimeSpan.FromSeconds(shift);clock.Advance(590);
        Assert.True(provider.CanDecide);Assert.Equal(TimeSpan.FromSeconds(150),Remaining(progress));
        Assert.Equal(TimeSpan.FromSeconds(10),provider.RemainingReview);
        Assert.True(provider.Decide(plan.RunId,plan.PlanId,plan.PlanDigest,true));Assert.True((await pending).Approved);
        clock.Advance(150);Assert.True(progress.ActiveCancellation.IsCancellationRequested);Preserved(workspace,plan);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("deny")]
    [InlineData("stale")]
    public async Task ReviewCancellationDenialAndStaleDecision_RejectWithoutWrite(string mode)
    {
        using var workspace=await SourcePlanTestWorkspace.Create();var clock=new ReviewClock();using var progress=Budget(clock);
        var plan=await Plan(workspace);progress.BindBudgetRun(plan.RunId);using var caller=new CancellationTokenSource();
        using var provider=Approval(workspace.Profile,clock,progress);provider.BindRun(plan.RunId);
        var pending=provider.RequestPlanAsync(plan,caller.Token);clock.Advance(250);
        if(mode=="cancel")caller.Cancel();
        else
        {
            if(mode=="stale")
            {
                Assert.False(provider.Decide(Guid.NewGuid(),plan.PlanId,plan.PlanDigest,true));
                Assert.False(provider.Decide(plan.RunId,Guid.NewGuid(),plan.PlanDigest,true));
                Assert.True(provider.CanDecide);
            }
            Assert.True(provider.Decide(plan.RunId,plan.PlanId,plan.PlanDigest,false));
        }
        if(mode=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>pending);
        else Assert.False((await pending).Approved);
        Assert.False(provider.CanDecide);Assert.False(progress.IsWaitingForApproval);
        Assert.False((await provider.RequestPlanAsync(plan,CancellationToken.None)).Approved);Preserved(workspace,plan);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("graph")]
    public async Task ExactApproval_RevalidatesDisposableSourceAndProfile_BeforeAnyCommit(string mode)
    {
        using var workspace=await SourcePlanTestWorkspace.Create();var clock=new ReviewClock();using var progress=Budget(clock);
        var plan=await Plan(workspace);progress.BindBudgetRun(plan.RunId);using var provider=Approval(workspace.Profile,clock,progress);provider.BindRun(plan.RunId);
        var executing=workspace.Editing.ExecuteSourcePlanAsync(plan,provider,new(()=>Remaining(progress),CancellationToken.None,()=>{}));
        Assert.True(provider.CanDecide);clock.Advance(300);
        var changed=System.IO.Path.Combine(workspace.Root,mode=="source"?"Product/Alpha.cs":"Tests/FixtureTests.cs");
        System.IO.File.AppendAllText(changed,"\n// external drift in owned disposable fixture\n");
        var changedHash=new FileSnapshotService().ComputeSha256Hash(changed);
        Assert.True(provider.Decide(plan.RunId,plan.PlanId,plan.PlanDigest,true));var result=await executing;
        Assert.NotEqual(SourcePlanOutcome.Success,result.Outcome);Assert.Empty(result.Operations);
        Assert.Null(result.Build);Assert.Null(result.Tests);Assert.Equal(changedHash,new FileSnapshotService().ComputeSha256Hash(changed));
        Assert.Equal(plan.Entries[1].BeforeHash,workspace.Hash(plan.Entries[1].Target));
    }

    [Fact]
    public async Task WaitingForOwner_BlocksNewGenerationAndToolDispatch_WithoutConsumingBudgets()
    {
        var clock=new ReviewClock();using var progress=Budget(clock);Assert.True(progress.BeginApprovalWait());
        using var handler=new ProposalHandler();using var http=new HttpClient(handler);var logger=Mock.Of<ILoggerService>();
        var context=new AgentRunContext(Guid.NewGuid(),"wait",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddSeconds(180),256,
            new("one","fake-model",123,"http://127.0.0.1:8080/v1/chat/completions",1),new(RunDuration:TimeSpan.FromSeconds(180)),progress);
        var error=await Assert.ThrowsAsync<ToolProtocolException>(()=>new NativeToolModelAdapter(http).GenerateAsync(context,new JsonArray(),[],CancellationToken.None));
        Assert.Equal("ApprovalPending",error.ReasonCode);
        var registry=new ToolRegistry([]);var executor=new ToolExecutor(registry,new ReadOnlyToolPolicy([]),logger,clock);
        var dispatch=await executor.ExecuteAsync(new(context),"extra","nexus_get_model_snapshot","{}",CancellationToken.None);
        Assert.Equal(ToolOutcome.Denied,dispatch.Result.Outcome);Assert.Equal("ApprovalPending",dispatch.Result.ReasonCode);
        Assert.Equal(0,handler.Calls);Assert.Equal(1,progress.ModelTurnCount);Assert.Equal(1,progress.ToolCallCount);
        progress.EndApprovalWait();Assert.False(progress.BeginApprovalWait());
    }

    [Theory]
    [InlineData(599.999,true)]
    [InlineData(600,false)]
    public async Task ExactExpiryBoundary_DecisionCannotResetOrOutraceTheTtl(double elapsed,bool accepted)
    {
        using var workspace=await SourcePlanTestWorkspace.Create();var clock=new ReviewClock();using var progress=Budget(clock);
        var plan=await Plan(workspace);progress.BindBudgetRun(plan.RunId);using var provider=Approval(workspace.Profile,clock,progress);provider.BindRun(plan.RunId);
        var pending=provider.RequestPlanAsync(plan,CancellationToken.None);clock.Advance(elapsed);
        Assert.Equal(accepted,provider.Decide(plan.RunId,plan.PlanId,plan.PlanDigest,true));
        clock.Advance(.01);Assert.Equal(accepted,(await pending).Approved);
        Assert.False(provider.Decide(plan.RunId,plan.PlanId,plan.PlanDigest,true));Preserved(workspace,plan);
    }

    [Fact]
    public void PauseRequiresQuiescenceAndUnexpiredActiveTime_AndBudgetBindingIsOneShot()
    {
        var clock=new ReviewClock();using var progress=Budget(clock);
        var run=Guid.NewGuid();Assert.True(progress.BindBudgetRun(run));Assert.False(progress.BindBudgetRun(Guid.NewGuid()));
        progress.State=AgentRunState.ExecutingTool;Assert.False(progress.BeginApprovalWait());
        progress.State=AgentRunState.AwaitingApproval;progress.BuildStartCount=1;Assert.False(progress.BeginApprovalWait());
        progress.BuildStartCount=0;clock.Advance(180);Assert.False(progress.BeginApprovalWait());Assert.True(progress.ActiveCancellation.IsCancellationRequested);
        Assert.Throws<ArgumentOutOfRangeException>(()=>new AgentRunProgress(TimeSpan.FromSeconds(241),clock));
    }

    [Fact]
    public async Task ActiveExpiryAfterCommit_DrainsProtectedRecovery_AndRestoresExactBaseline()
    {
        using var workspace=await SourcePlanTestWorkspace.Create();var clock=new ReviewClock();using var progress=Budget(clock,240);
        clock.Advance(230);var plan=await Plan(workspace);progress.BindBudgetRun(plan.RunId);
        using var provider=Approval(workspace.Profile,clock,progress);provider.BindRun(plan.RunId);
        workspace.Editing.SourcePlanProbe=(stage,_,index)=>
        {
            if(stage=="SourceApplyCompleted" && index==0)clock.Advance(11);
            if(stage=="SourceRecoveryStarted")clock.Advance(1000);
        };
        var executing=workspace.Editing.ExecuteSourcePlanAsync(plan,provider,new(()=>Remaining(progress),progress.ActiveCancellation,()=>{}));
        clock.Advance(599);Assert.True(provider.Decide(plan.RunId,plan.PlanId,plan.PlanDigest,true));var result=await executing;
        Assert.True(progress.ActiveCancellation.IsCancellationRequested);Assert.Equal(SourcePlanOutcome.Cancelled,result.Outcome);
        var operation=Assert.Single(result.Operations);Assert.True(operation.CommitReached);Assert.Equal(SafeWriteRecovery.Restored,operation.Recovery);
        Assert.True(result.BaselineVerified);Assert.False(result.FinalVerified);Assert.Null(result.Build);Assert.Null(result.Tests);Preserved(workspace,plan);
    }

    [Fact]
    public async Task PilotUi_UsesHostReviewTtlAndRemainingActiveTime_AfterDecisionAndExpiry()
    {
        using var reader=new ProjectReadContextWorkflow();var catalog=new ReviewedProjectCatalog(reader);
        var selected=catalog.Select(ReviewedProjectCatalog.DisposableRoot);using var dependencies=selected.Profile.AcquireExecutionLease();
        var editing=new EditingWorkflowService(new FileSnapshotService(),new ProposalService(),new SyntaxValidatorService(),new FileWriterService());
        foreach(var expire in new[] {false,true})
        {
            var grant=await reader.GrantAsync(selected.Profile.Root,"option-b-ui",CancellationToken.None);
            var authorized=catalog.Authorize(selected,grant);var clock=new ReviewClock();using var progress=Budget(clock);clock.Advance(80);
            var plan=await editing.PrepareSourcePlanAsync(selected.Profile,selected.Profile.Targets.Select((target,i)=>new SourceFileEdit(target,
                selected.Profile.InitialSourceHashes[target],ReviewedProjectCatalog.Anchors[i],ReviewedProjectCatalog.Anchors[i]+"\r\n    public bool DeadlineProbe => false;")),
                Guid.NewGuid(),"offline-ui",new(),new(()=>Remaining(progress),CancellationToken.None,()=>{}));
            progress.BindBudgetRun(plan.RunId);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var workflow=new Mock<IAgentWorkspaceWorkflow>();workflow.Setup(w=>w.Select(selected.Profile.Root)).Returns(selected);
            workflow.Setup(w=>w.AuthorizeReviewedEdit(selected,grant)).Returns(authorized);
            workflow.Setup(w=>w.RunAsync(It.IsAny<AgentWorkspaceSelection>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<ImmutableArray<SourceFileEdit>>(),It.IsAny<DateTimeOffset>(),It.IsAny<Action<string>>(),It.IsAny<Action<SourceEditPlan,AgentWorkspaceApprovalProvider>>(),It.IsAny<CancellationToken>()))
                .Returns(async(AgentWorkspaceSelection s,string session,string prompt,ImmutableArray<SourceFileEdit> edits,DateTimeOffset deadline,Action<string> state,Action<SourceEditPlan,AgentWorkspaceApprovalProvider> review,CancellationToken token)=>
                {
                    AgentWorkspaceApprovalProvider? provider=null;
                    provider=new(s.Profile,deadline,p=>review(p,provider!),clock,Remaining(progress),progress);
                    using(provider)
                    {
                        provider.BindRun(plan.RunId);await provider.RequestPlanAsync(plan,token);await release.Task.WaitAsync(token);
                        return new(new(plan.RunId,session,DateTimeOffset.UtcNow,deadline,256,null,new(RunDuration:TimeSpan.FromSeconds(180)),progress),AgentRunOutcome.Failed,"",null,"OfflineUiOnly",TimeSpan.Zero);
                    }
                });
            await WpfAppHost.RunAsync(async()=>
            {
                var vm=new AgentWorkspaceViewModel(workflow.Object);await vm.SelectWorkspaceAsync(selected.Profile.Root);
                vm.BindReadGrant(grant);vm.ReviewedEditAuthorized=true;vm.Prompt="offline UI review";var running=vm.RunAsync();
                var view=new Nexus.App.Views.AgentWorkspaceView {DataContext=vm};var window=new System.Windows.Window {Content=view,Width=650,Height=700};window.Show();
                try
                {
                    using var wait=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(!vm.CanDecide)await Task.Delay(10,wait.Token);
                    var exact=vm.PendingPlan;var file=vm.ReviewFiles[0];vm.SelectReviewFileCommand.Execute(file);vm.CloseReviewFileCommand.Execute(file);Assert.False(vm.IsOpen);
                    clock.Advance(300);Assert.True(vm.CanDecide);Assert.Contains("100 detik",vm.RemainingText);Assert.Contains("300 detik",vm.RemainingText);
                    vm.SelectReviewFileCommand.Execute(file);Assert.Same(exact,vm.PendingPlan);await Task.Delay(250);view.UpdateLayout();
                    var budget=Assert.IsType<System.Windows.Controls.TextBlock>(view.FindName("ReviewBudgetText"));Assert.Contains("100 detik",budget.Text);Assert.Contains("300 detik",budget.Text);
                    GateIAgentWorkspaceUiTests.CaptureWrapView(view,(System.Windows.Controls.RichTextBox)view.FindName("ColoredDiffReview"),vm,expire?"Pilot-before-expiry":"Pilot-review",96);
                    if(expire)
                    {
                        vm.HideReviewCommand.Execute(null);clock.Advance(300);Assert.False(vm.CanDecide);vm.SelectReviewFileCommand.Execute(file);
                        Assert.False(((System.Windows.Controls.Button)view.FindName("ApprovePlanButton")).IsEnabled);
                        vm.ApproveCommand.Execute(null);Assert.False(vm.CanDecide);Assert.False(((System.Windows.Controls.Button)view.FindName("ApprovePlanButton")).IsEnabled);
                        Assert.Contains("tidak berlaku",vm.FriendlyStatus);
                    }
                    else
                    {
                        vm.ApproveCommand.Execute(null);Assert.False(vm.CanDecide);clock.Advance(10);
                        Assert.Contains("Plan disetujui",vm.FriendlyStatus);
                        Assert.Contains("90 detik",vm.RemainingText);Assert.DoesNotContain("termasuk persetujuan",vm.RemainingText);
                    }
                }
                finally {release.TrySetResult();await vm.CancelAndDrainAsync();await running;window.Close();}
            });
            foreach(var source in selected.Profile.InitialSourceHashes)Assert.Equal(source.Value,new FileSnapshotService().ComputeSha256Hash(System.IO.Path.Combine(selected.Profile.Root,source.Key)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PilotUi_PreflightDeadlineIsDistinctFromCallerCancellation_EvenAfterGrantCleanup(bool expired)
    {
        using var reader=new ProjectReadContextWorkflow();var catalog=new ReviewedProjectCatalog(reader);
        var selected=catalog.Select(ReviewedProjectCatalog.DisposableRoot);
        var grant=await reader.GrantAsync(selected.Profile.Root,"option-b-preflight-ui",CancellationToken.None);
        var authorized=catalog.Authorize(selected,grant);var workflow=new Mock<IAgentWorkspaceWorkflow>();
        workflow.Setup(w=>w.Select(selected.Profile.Root)).Returns(selected);
        workflow.Setup(w=>w.AuthorizeReviewedEdit(selected,grant)).Returns(authorized);
        workflow.Setup(w=>w.RunAsync(It.IsAny<AgentWorkspaceSelection>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<ImmutableArray<SourceFileEdit>>(),It.IsAny<DateTimeOffset>(),It.IsAny<Action<string>>(),It.IsAny<Action<SourceEditPlan,AgentWorkspaceApprovalProvider>>(),It.IsAny<CancellationToken>()))
            .Returns(()=>
            {
                var error=new OperationCanceledException();if(expired)error.Data["ExecutionDeadline"]=true;
                reader.Revoke();return Task.FromException<AgentResult>(error);
            });
        await WpfAppHost.RunAsync(async()=>
        {
            var vm=new AgentWorkspaceViewModel(workflow.Object);await vm.SelectWorkspaceAsync(selected.Profile.Root);
            vm.BindReadGrant(grant);vm.ReviewedEditAuthorized=true;vm.Prompt="offline preflight status";
            await vm.RunAsync();Assert.Equal(expired?"TimedOut: ExecutionDeadline":"Cancelled",vm.Status);
            Assert.Equal(expired,JsonDocument.Parse(vm.ResultText).RootElement.GetProperty("uiDeadlineExpired").GetBoolean());
            Assert.Null(vm.LastResult);Assert.False(vm.IsRunning);
        });
    }

    [Fact]
    public async Task PilotAgentAndSourceTool_ShareReviewPause_AndRevalidateBeforeResuming()
    {
        using var runtime=new SharedRuntimeAgentTests.Fixture();await runtime.Workflow.StartActiveModelAsync();
        using var reader=new ProjectReadContextWorkflow();var catalog=new ReviewedProjectCatalog(reader);
        var selected=catalog.Select(ReviewedProjectCatalog.DisposableRoot);using var dependencies=selected.Profile.AcquireExecutionLease();
        var hashes=selected.Profile.InitialSourceHashes;
        foreach(var mode in new[] {"deny","expire","cancel","revoke","grant-expired","revision","model","runtime","approved-prewrite","insufficient-budget"})
        {
            await runtime.Workflow.StartActiveModelAsync();
            var grant=await reader.GrantAsync(selected.Profile.Root,"option-b-components",CancellationToken.None);
            var authorized=catalog.Authorize(selected,grant);var scope=await catalog.CreateScopeAsync(authorized,grant.Session,CancellationToken.None);
            var clock=new ReviewClock();using var progress=new AgentRunProgress(TimeSpan.FromSeconds(240),clock);clock.Advance(45);
            var published=new TaskCompletionSource<SourceEditPlan>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var approval=new AgentWorkspaceApprovalProvider(selected.Profile,clock.Utc.AddSeconds(195),p=>published.SetResult(p),clock,TimeSpan.FromSeconds(195),progress);
            var logger=new AgentWorkspaceRunLogger(Mock.Of<ILoggerService>(),grant.Session,approval,_=>{});
            var editing=new EditingWorkflowService(new FileSnapshotService(),new ProposalService(),new SyntaxValidatorService(),new FileWriterService());
            var prewrite=false;
            // Independent guard: these negative tests must never commit to the pinned acceptance fixture.
            editing.SourcePlanProbe=(stage,_,_)=>
            {
                if(stage=="SourceApprovalGranted" && mode=="insufficient-budget")clock.Advance(166);
                if(stage=="SourcePreflightPassed")prewrite=true;
                if(stage=="SourceApplyStarted")throw new InvalidOperationException("Offline prewrite stop");
            };
            var server=new Mock<ILlamaServerManager>();var modelPath=runtime.Original.ResolvedGgufPath;
            server.SetupGet(s=>s.State).Returns(()=>runtime.State);server.SetupGet(s=>s.OwnedPid).Returns(123);
            server.SetupGet(s=>s.RuntimeModelPath).Returns(()=>modelPath);
            using var handler=new ProposalHandler();using var http=new HttpClient(handler);using var caller=new CancellationTokenSource();
            var sourceCatalog=new SourcePlanCatalog(selected.Profile);var registry=new ToolRegistry([new ApplySourcePlanTool(sourceCatalog,editing,approval,logger)]);
            var policy=new ReadOnlyToolPolicy([],sourceCatalog:sourceCatalog);
            var orchestrator=new AgentOrchestrator(new NativeToolModelAdapter(http),registry,policy,new ToolExecutor(registry,policy,logger,clock),(_,_)=>Task.FromResult(NativeToolSupport.Supported),logger);
            var agent=new SharedRuntimeAgent(runtime.Workflow,server.Object,logger,orchestrator,progress.ActiveCancellation);
            var running=agent.ExecuteAsync(new(grant.Session,"offline component timing",MaxOutputTokens:256,EnableReadOnlyToolCall:true,
                Limits:new(MaxModelTurns:2,MaxToolCalls:1,MaxRetries:0,RunDuration:TimeSpan.FromSeconds(195)),Verification:AgentVerificationRequirement.SourcePlan,ProposalScope:scope)
                {ExecutionProgress=progress},caller.Token);
            var plan=await published.Task.WaitAsync(TimeSpan.FromSeconds(60));clock.Advance(599);
            Assert.True(approval.CanDecide);Assert.False(running.IsCompleted);Assert.False(progress.ActiveCancellation.IsCancellationRequested);
            Assert.Equal(TimeSpan.FromSeconds(195),Remaining(progress));Assert.Equal(1,handler.Calls);Assert.False(prewrite);
            Task? stopping=null;
            switch(mode)
            {
                case "expire":clock.Advance(1);break;
                case "cancel":caller.Cancel();break;
                case "runtime":stopping=runtime.Workflow.StopAsync();break;
                case "revoke":reader.Revoke();approval.Decide(plan.RunId,plan.PlanId,plan.PlanDigest,true);break;
                case "grant-expired":typeof(ProjectReadGrant).GetField("<ExpiresAt>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(grant,DateTimeOffset.UtcNow.AddSeconds(-1));approval.Decide(plan.RunId,plan.PlanId,plan.PlanDigest,true);break;
                case "revision":await reader.GrantAsync(selected.Profile.Root,"new-session",CancellationToken.None);approval.Decide(plan.RunId,plan.PlanId,plan.PlanDigest,true);break;
                case "model":modelPath=runtime.Other.ResolvedGgufPath;approval.Decide(plan.RunId,plan.PlanId,plan.PlanDigest,true);break;
                default:approval.Decide(plan.RunId,plan.PlanId,plan.PlanDigest,mode is "approved-prewrite" or "insufficient-budget");break;
            }
            var result=await running.WaitAsync(TimeSpan.FromSeconds(30));if(stopping is not null)await stopping;
            Assert.NotEqual(AgentRunOutcome.Completed,result.Outcome);Assert.Equal(1,handler.Calls);
            Assert.Equal(mode is "approved-prewrite" or "insufficient-budget",prewrite);Assert.Empty(progress.SourceResult!.Operations);
            if(mode=="insufficient-budget")
            {
                Assert.Equal("InsufficientRemainingBudget",progress.SourceResult.Outcome.ToString());
                Assert.Equal("InsufficientRemainingBudget",Assert.Single(result.ToolDispatches!).Result.ReasonCode);
            }
            Assert.Null(progress.SourceResult.Build);Assert.Null(progress.SourceResult.Tests);
            foreach(var file in hashes)Assert.Equal(file.Value,new FileSnapshotService().ComputeSha256Hash(System.IO.Path.Combine(selected.Profile.Root,file.Key)));
            Assert.False(approval.Decide(plan.RunId,plan.PlanId,plan.PlanDigest,true));
            reader.Revoke();
        }
    }

    private sealed class ProposalHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;if(Calls!=1)throw new InvalidOperationException("No follow-up generation authorized in this negative test");
            var edits=ReviewedProjectCatalog.Anchors.Select(a=>new[] {new {old=a,@new=a+"\r\n    public bool DeadlineProbe => false;"}}).ToArray();
            var response=ToolProtocolTests.Response(callId:"option-b-native-fixture",name:ApplySourcePlanTool.ToolName,arguments:JsonSerializer.Serialize(new {a=edits[0],b=edits[1]}));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(response)});
        }
    }

    private static AgentRunProgress Budget(ReviewClock clock,int seconds=180)
    {
        var ctor=typeof(AgentRunProgress).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,[typeof(TimeSpan),typeof(TimeProvider)],null);
        var progress=ctor is null?new AgentRunProgress():(AgentRunProgress)ctor.Invoke([TimeSpan.FromSeconds(seconds),clock]);
        progress.State=AgentRunState.AwaitingApproval;progress.ModelTurnCount=1;progress.ToolCallCount=1;progress.SourcePlanCount=1;
        return progress;
    }
    private static TimeSpan Remaining(AgentRunProgress progress)=>
        (TimeSpan?)typeof(AgentRunProgress).GetProperty("RemainingActive",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(progress) ?? TimeSpan.FromSeconds(180)-progress.Elapsed;
    private static AgentWorkspaceApprovalProvider Approval(SourceVerificationProfile profile,ReviewClock clock,AgentRunProgress progress)
    {
        var deadline=clock.Utc+Remaining(progress);Action<SourceEditPlan> publish=_=>{};
        var ctor=typeof(AgentWorkspaceApprovalProvider).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,
            [typeof(SourceVerificationProfile),typeof(DateTimeOffset),typeof(Action<SourceEditPlan>),typeof(TimeProvider),typeof(TimeSpan?),typeof(AgentRunProgress)],null);
        return ctor is null?new(profile,deadline,publish,clock,Remaining(progress)):
            (AgentWorkspaceApprovalProvider)ctor.Invoke([profile,deadline,publish,clock,Remaining(progress),progress]);
    }
    private static Task<SourceEditPlan> Plan(SourcePlanTestWorkspace f)=>f.Editing.PrepareSourcePlanAsync(f.Profile,
        [new("Product/Alpha.cs",f.Hash("Product/Alpha.cs"),"return 1;","return 2;"),new("Product/Beta.cs",f.Hash("Product/Beta.cs"),"return 1;","return 2;")],
        Guid.NewGuid(),"option-b-clock",new(),new(()=>TimeSpan.FromSeconds(120),CancellationToken.None,()=>{}));
    private static void Preserved(SourcePlanTestWorkspace f,SourceEditPlan plan)=>
        Assert.All(plan.Entries,e=>Assert.Equal(e.BeforeHash,f.Hash(e.Target)));

    internal sealed class ReviewClock : TimeProvider
    {
        public DateTimeOffset Utc=DateTimeOffset.UtcNow;
        private long ticks;private readonly List<Timer> timers=[];
        public override DateTimeOffset GetUtcNow()=>Utc;
        public override long GetTimestamp()=>ticks;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override ITimer CreateTimer(TimerCallback callback,object? state,TimeSpan dueTime,TimeSpan period)
        {var timer=new Timer(this,callback,state);timers.Add(timer);timer.Change(dueTime,period);return timer;}
        public void Advance(double seconds)
        {var elapsed=TimeSpan.FromSeconds(seconds);ticks+=elapsed.Ticks;Utc+=elapsed;foreach(var timer in timers.ToArray())timer.Fire();}
        private sealed class Timer(ReviewClock clock,TimerCallback callback,object? state) : ITimer
        {
            private long due=long.MaxValue;private bool disposed;
            public bool Change(TimeSpan dueTime,TimeSpan period)
            {if(disposed)return false;due=dueTime==Timeout.InfiniteTimeSpan?long.MaxValue:clock.ticks+dueTime.Ticks;return true;}
            public void Fire(){if(!disposed && clock.ticks>=due){due=long.MaxValue;callback(state);}}
            public void Dispose()=>disposed=true;
            public ValueTask DisposeAsync(){Dispose();return ValueTask.CompletedTask;}
        }
    }
}
