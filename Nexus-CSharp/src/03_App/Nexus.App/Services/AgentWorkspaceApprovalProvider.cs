using Nexus.AI.EditingLite.Services;
using Nexus.App.Services.Agent;

namespace Nexus.App.Services;

/// <summary>One exact plan, one run, one asynchronous UI decision; default deny.</summary>
public sealed class AgentWorkspaceApprovalProvider : IWriteApprovalProvider, IDisposable
{
    private readonly SourceVerificationProfile profile;
    private readonly DateTimeOffset deadline;
    private readonly Action<SourceEditPlan> publish;
    private readonly TimeProvider clock;
    private readonly long started;
    private readonly TimeSpan budget;
    private readonly AgentRunProgress? execution;
    public AgentWorkspaceApprovalProvider(SourceVerificationProfile profile,DateTimeOffset deadline,Action<SourceEditPlan> publish)
        : this(profile,deadline,publish,TimeProvider.System) { }
    internal AgentWorkspaceApprovalProvider(SourceVerificationProfile profile,DateTimeOffset deadline,Action<SourceEditPlan> publish,TimeProvider clock)
        : this(profile,deadline,publish,clock,null) { }
    internal AgentWorkspaceApprovalProvider(SourceVerificationProfile profile,DateTimeOffset deadline,Action<SourceEditPlan> publish,TimeProvider clock,TimeSpan? remainingBudget)
        : this(profile,deadline,publish,clock,remainingBudget,null) { }
    internal AgentWorkspaceApprovalProvider(SourceVerificationProfile profile,DateTimeOffset deadline,Action<SourceEditPlan> publish,TimeProvider clock,TimeSpan? remainingBudget,AgentRunProgress? execution)
    {
        this.profile=profile;this.deadline=deadline;this.publish=publish;this.clock=clock;
        if(execution is not null && (!execution.HasActiveBudget || !ReferenceEquals(execution.BudgetClock,clock)))throw new ArgumentException("Approval clock/budget mismatch.");
        this.execution=execution;
        started=clock.GetTimestamp();var wallRemaining=deadline-clock.GetUtcNow();
        budget=remainingBudget is {} remaining && remaining<wallRemaining?remaining:wallRemaining;
    }
    private TimeSpan Remaining=>execution?.RemainingReview ?? budget-clock.GetElapsedTime(started);
    private bool Expired=>execution is null ? Remaining<=TimeSpan.Zero || clock.GetUtcNow()>=deadline :
        execution.RemainingActive<=TimeSpan.Zero || execution.ActiveCancellation.IsCancellationRequested || Remaining<=TimeSpan.Zero;
    internal bool UsesSeparateReview=>execution is not null;
    public TimeSpan RemainingExecution=>execution?.RemainingActive ?? Remaining;
    public TimeSpan RemainingReview=>execution is null?TimeSpan.Zero:Remaining;
    public bool CanDecide {get {lock(sync)return pending is not null && completion?.Task.IsCompleted==false && !Expired;}}
    private readonly object sync = new();
    private Guid runId;
    private SourceEditPlan? pending;
    private TaskCompletionSource<SourcePlanApproval>? completion;
    private bool consumed;
    public string? DenialReason { get; private set; }

    public void BindRun(Guid run)
    {
        lock(sync)
        {
            if(run == Guid.Empty || runId != Guid.Empty && runId != run || execution is not null && execution.BudgetRun!=run)
                throw new InvalidOperationException("Run binding conflict.");
            runId = run;
        }
    }

    public Task<WriteApprovalDecision> RequestAsync(SafeWriteProposal p, CancellationToken token) =>
        Task.FromResult(WriteApprovalDecision.For(p,false));

    public async Task<SourcePlanApproval> RequestPlanAsync(SourceEditPlan plan, CancellationToken token)
    {
        Task<SourcePlanApproval> task;
        lock(sync)
        {
            if(consumed || runId == Guid.Empty || plan.RunId != runId || !ReferenceEquals(plan.Profile,profile) ||
                plan.VerificationProfileId != profile.Id || Expired || token.IsCancellationRequested)
                return SourcePlanApproval.For(plan,false);
            consumed = true;
            if(execution is not null && !execution.BeginApprovalWait())
            { execution.EndApprovalWait();DenialReason="ApprovalUnavailable";return SourcePlanApproval.For(plan,false); }
            pending = plan;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            task = completion.Task;
        }
        var remaining = Remaining;
        using var elapsedExpiry = new CancellationTokenSource(remaining>TimeSpan.Zero ? remaining : TimeSpan.FromTicks(1),clock);
        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(token,elapsedExpiry.Token);
        using var registration = expiry.Token.Register(() => Deny(token.IsCancellationRequested ? "ApprovalCancelled" : "ApprovalExpired"));
        try
        {
            publish(plan);
            var decision = await task.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            // Recheck expiry after continuation as well as at button click.
            return !Expired ? decision : SourcePlanApproval.For(plan,false);
        }
        finally { lock(sync) {execution?.EndApprovalWait();pending = null;} }
    }

    public bool Decide(Guid run, Guid planId, string digest, bool approve)
    {
        lock(sync)
        {
            if(pending is not { } plan || completion is null || completion.Task.IsCompleted ||
                run != runId || run != plan.RunId || planId != plan.PlanId || digest != plan.PlanDigest ||
                Expired) return false;
            if(!approve) DenialReason="ApprovalRejected";
            // Resume accounting at the decision, not after scheduling its continuation.
            execution?.EndApprovalWait();
            return completion.TrySetResult(SourcePlanApproval.For(plan,approve));
        }
    }

    public void Deny(string reason)
    {
        lock(sync)
        {
            consumed=true;
            if(completion?.Task.IsCompleted==true)return;
            DenialReason=reason;
            execution?.EndApprovalWait();
            if(pending is { } plan) completion?.TrySetResult(SourcePlanApproval.For(plan,false));
        }
    }
    public void Dispose() => Deny("ApprovalClosed");
}
