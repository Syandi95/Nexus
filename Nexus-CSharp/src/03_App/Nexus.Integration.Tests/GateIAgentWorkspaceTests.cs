using System.Reflection;
using Nexus.AI.EditingLite.Services;
using Microsoft.Extensions.DependencyInjection;
using Nexus.App.Services;

namespace Nexus.Integration.Tests;

public sealed class GateIAgentWorkspaceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handoff_UiElapsedDeadlineIsDistinctFromUserCancellation(bool deadline)
    {
        using var f=await SourcePlanTestWorkspace.Create();
        await WpfAppHost.RunAsync(async()=>
        {
            var workflow=new Moq.Mock<IAgentWorkspaceWorkflow>();
            workflow.Setup(w=>w.Select(f.Root)).Returns(new AgentWorkspaceSelection(f.Profile,
                System.Collections.Immutable.ImmutableArray.Create(f.Editing.ReadScopedSource(f.Profile.Workspace,"Product/Alpha.cs"),f.Editing.ReadScopedSource(f.Profile.Workspace,"Product/Beta.cs"))));
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            workflow.Setup(w=>w.RunAsync(Moq.It.IsAny<AgentWorkspaceSelection>(),Moq.It.IsAny<string>(),Moq.It.IsAny<string>(),Moq.It.IsAny<System.Collections.Immutable.ImmutableArray<SourceFileEdit>>(),Moq.It.IsAny<DateTimeOffset>(),Moq.It.IsAny<Action<string>>(),Moq.It.IsAny<Action<SourceEditPlan,AgentWorkspaceApprovalProvider>>(),Moq.It.IsAny<CancellationToken>()))
                .Returns(async (AgentWorkspaceSelection s,string session,string prompt,System.Collections.Immutable.ImmutableArray<SourceFileEdit> edits,DateTimeOffset d,Action<string> state,Action<SourceEditPlan,AgentWorkspaceApprovalProvider> review,CancellationToken token)=>
                {entered.SetResult();await Task.Delay(Timeout.Infinite,token);return (Nexus.App.Services.Agent.AgentResult)null!;});
            var vm=new Nexus.App.ViewModels.AgentWorkspaceViewModel(workflow.Object);
            await vm.SelectWorkspaceAsync(f.Root);vm.Prompt="offline cancellation";vm.AlphaOld=vm.BetaOld="return 1;";
            var run=vm.RunAsync();await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var field=typeof(Nexus.App.ViewModels.AgentWorkspaceViewModel).GetField(deadline?"deadlineCancellation":"cancellation",BindingFlags.NonPublic|BindingFlags.Instance)
                ?? typeof(Nexus.App.ViewModels.AgentWorkspaceViewModel).GetField("cancellation",BindingFlags.NonPublic|BindingFlags.Instance)!;
            ((CancellationTokenSource)field.GetValue(vm)!).Cancel();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.StartsWith(deadline?"TimedOut":"Cancelled",vm.Status);
            Assert.Equal(deadline?"Waktu perubahan habis":"Tugas dibatalkan",vm.FriendlyStatus);
            Assert.Null(vm.LastResult);Assert.Null(vm.PendingPlan);
        });
    }
    [Fact]
    public void NaturalLanguageMode_HasSeparateReviewedExecutionConsent_AndLiteralCompatibility()
    {
        var type=typeof(Nexus.App.ViewModels.AgentWorkspaceViewModel);
        Assert.NotNull(type.GetProperty("IsNaturalLanguage"));
        Assert.NotNull(type.GetProperty("ReviewedEditAuthorized"));
        Assert.NotNull(type.GetMethod("BindReadGrant"));
        Assert.NotNull(type.GetProperty("AlphaOld"));Assert.NotNull(type.GetProperty("BetaOld"));
        Assert.NotNull(typeof(IAgentWorkspaceWorkflow).GetMethod("AuthorizeReviewedEdit"));
    }
    [Fact]
    public async Task Admission_RequiresHostPinnedManifest_NotJustFolderSelection()
    {
        await using var provider=ArchitectureGovernanceTests.CreateProductionServices().BuildServiceProvider();
        var workflow=provider.GetRequiredService<IAgentWorkspaceWorkflow>();
        Assert.Throws<InvalidOperationException>(()=>workflow.Select(@"D:\NEXUS\Nexus-CSharp"));
        using var fixture=await SourcePlanTestWorkspace.Create();
        var root=fixture.Root;
        var graph=fixture.Profile.GraphHashes.ToDictionary(k=>k.Key,k=>k.Value);
        foreach(var source in fixture.Profile.InitialSourceHashes)graph[source.Key]=source.Value;
        var manifest=System.Text.Json.JsonSerializer.Serialize(new {contract="NEXUS-GATE-I-I1-REVIEWED-V1",root,files=graph,sdk_sha256=new FileSnapshotService().ComputeSha256Hash(fixture.Profile.Sdk)});
        var manifestPath=System.IO.Path.Combine(root,"nexus-reviewed-i1.json");System.IO.File.WriteAllText(manifestPath,manifest);
        var pin=new FileSnapshotService().ComputeSha256Hash(manifestPath);
        var admission=typeof(AgentWorkspaceWorkflow).GetMethod("SelectPinned",BindingFlags.Static|BindingFlags.NonPublic)!;
        Assert.NotNull(admission);
        AgentWorkspaceSelection Select()=> (AgentWorkspaceSelection)admission.Invoke(null,[root,root,pin,fixture.Editing])!;
        var selected=Select();
        Assert.Equal(new[] {"Product/Alpha.cs","Product/Beta.cs"},selected.Profile.Targets);
        Assert.True(selected.Profile.IsIntact());
        Assert.Equal(2,selected.Files.Length);
        Assert.All(selected.Files,f=>Assert.Contains("return 1;",f.Content));
        System.IO.File.AppendAllText(System.IO.Path.Combine(root,"Product/Alpha.cs"),"// drift");
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(()=>Select()).InnerException);
        // Restore only this disposable fixture's own source, never the engineering project.
        System.IO.File.WriteAllText(System.IO.Path.Combine(root,"Product/Alpha.cs"),selected.Files[0].Content);
        System.IO.File.AppendAllText(System.IO.Path.Combine(root,"Tests/FixtureTests.cs"),"// graph drift");
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(()=>Select()).InnerException);
    }
    [Fact]
    public async Task Handoff_ApprovalElapsedDeadlineCannotBeExtendedByWallClock()
    {
        using var f=await SourcePlanTestWorkspace.Create();
        var plan=await f.Editing.PrepareSourcePlanAsync(f.Profile,
            [new("Product/Alpha.cs",f.Hash("Product/Alpha.cs"),"return 1;","return 2;"),
             new("Product/Beta.cs",f.Hash("Product/Beta.cs"),"return 1;","return 2;")],
            Guid.NewGuid(),"handoff-clock",new(),new(()=>TimeSpan.FromSeconds(120),CancellationToken.None,()=>{}));
        var clock=new ApprovalTestClock();
        var constructor=ApprovalType.GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,
            [typeof(SourceVerificationProfile),typeof(DateTimeOffset),typeof(Action<SourceEditPlan>),typeof(TimeProvider)],null);
        var provider=constructor is null ? Create(f.Profile,clock.GetUtcNow().AddSeconds(30),_=>{}) :
            constructor.Invoke([f.Profile,clock.GetUtcNow().AddSeconds(30),(Action<SourceEditPlan>)(_=>{}),clock]);
        using var owned=(IDisposable)provider;
        Bind(provider,plan.RunId);
        var pending=((IWriteApprovalProvider)provider).RequestPlanAsync(plan,CancellationToken.None);
        Assert.False(pending.IsCompleted);
        // Monotonic elapsed time has expired, while wall clock moved backwards.
        clock.Timestamp+=TimeSpan.FromSeconds(31).Ticks;
        clock.Utc-=TimeSpan.FromHours(1);
        Assert.False(Decide(provider,plan,true));
        ApprovalType.GetMethod("Deny")!.Invoke(provider,["ApprovalExpired"]);
        Assert.False((await pending).Approved);
        Assert.All(plan.Entries,e=>Assert.Equal(e.BeforeHash,f.Hash(e.Target)));
    }
    private sealed class ApprovalTestClock : TimeProvider
    {
        internal DateTimeOffset Utc=DateTimeOffset.UtcNow;
        internal long Timestamp;
        public override DateTimeOffset GetUtcNow()=>Utc;
        public override long GetTimestamp()=>Timestamp;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
    }

    // Reflection permits a real RED behavior test before the integration exists.
    private static Type ApprovalType => typeof(Nexus.App.App).Assembly.GetType(
        "Nexus.App.Services.AgentWorkspaceApprovalProvider", throwOnError: true)!;

    private static object Create(SourceVerificationProfile profile, DateTimeOffset deadline, Action<SourceEditPlan> publish) =>
        Activator.CreateInstance(ApprovalType, profile, deadline, publish)!;

    private static void Bind(object provider, Guid run) => ApprovalType.GetMethod("BindRun")!.Invoke(provider, [run]);
    private static bool Decide(object provider, SourceEditPlan plan, bool approve, string? digest = null) =>
        (bool)ApprovalType.GetMethod("Decide")!.Invoke(provider, [plan.RunId, plan.PlanId, digest ?? plan.PlanDigest, approve])!;

    [Fact]
    public async Task ExactApproval_IsOneShot_StaleDigestCannotGrant_AndNoDiskMutationDuringReview()
    {
        using var f = await SourcePlanTestWorkspace.Create();
        var plan = await f.Editing.PrepareSourcePlanAsync(f.Profile,
            [new("Product/Alpha.cs",f.Hash("Product/Alpha.cs"),"return 1;","return 2;"),
             new("Product/Beta.cs",f.Hash("Product/Beta.cs"),"return 1;","return 2;")],
            Guid.NewGuid(), "i1-test", new(), new(()=>TimeSpan.FromSeconds(30),CancellationToken.None,()=>{}));
        SourceEditPlan? shown = null;
        var provider = Create(f.Profile, DateTimeOffset.UtcNow.AddSeconds(30), p => shown = p);
        Bind(provider, plan.RunId);
        var pending = ((IWriteApprovalProvider)provider).RequestPlanAsync(plan,CancellationToken.None);
        Assert.Same(plan,shown); Assert.False(pending.IsCompleted);
        Assert.Equal(plan.Entries[0].BeforeHash,f.Hash("Product/Alpha.cs"));
        Assert.False(Decide(provider,plan,true,new string('0',64)));
        Assert.False(pending.IsCompleted);
        Assert.True(Decide(provider,plan,true));
        Assert.True((await pending).Matches(plan));
        Assert.False(Decide(provider,plan,true));
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("cancel")]
    [InlineData("expire")]
    [InlineData("wrong-run")]
    public async Task DenialCancellationExpiryAndWrongRun_NeverGrant(string mode)
    {
        using var f = await SourcePlanTestWorkspace.Create();
        var plan = await f.Editing.PrepareSourcePlanAsync(f.Profile,
            [new("Product/Alpha.cs",f.Hash("Product/Alpha.cs"),"return 1;","return 2;"),
             new("Product/Beta.cs",f.Hash("Product/Beta.cs"),"return 1;","return 2;")],
            Guid.NewGuid(), "i1-deny", new(), new(()=>TimeSpan.FromSeconds(30),CancellationToken.None,()=>{}));
        using var cts = new CancellationTokenSource();
        var provider = Create(f.Profile, DateTimeOffset.UtcNow.AddMilliseconds(mode=="expire" ? 80 : 30000), _=>{});
        Bind(provider,mode=="wrong-run" ? Guid.NewGuid() : plan.RunId);
        var pending=((IWriteApprovalProvider)provider).RequestPlanAsync(plan,cts.Token);
        if(mode=="reject") Assert.True(Decide(provider,plan,false));
        if(mode=="cancel") cts.Cancel();
        try { Assert.False((await pending.WaitAsync(TimeSpan.FromSeconds(3))).Approved); }
        catch(OperationCanceledException) { Assert.Equal("cancel",mode); }
        Assert.False(Decide(provider,plan,true));
        Assert.All(plan.Entries,p=>Assert.Equal(p.BeforeHash,f.Hash(p.Target)));
    }
}
