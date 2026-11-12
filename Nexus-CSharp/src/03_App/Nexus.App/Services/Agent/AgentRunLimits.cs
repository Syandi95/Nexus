using System.Diagnostics;
using Nexus.App.Services.Agent.Tools;

namespace Nexus.App.Services.Agent;

public sealed record AgentRunLimits(int MaxModelTurns=4,int MaxToolCalls=3,int MaxToolFailures=2,int MaxRetries=1,
    TimeSpan? RunDuration=null,int MaxInputContext=32768,int MaxToolResultBytes=6144,int MaxFinalOutput=16384,
    int MaxWriteOperations=1,int MaxWriteInputBytes=1024,int MaxTargetFileBytes=4096,int MaxWriteRetries=0,int MaxSourcePlans=1,
    int MaxRepairPlans=0,int MaxBuildStarts=1,int MaxTestStarts=1)
{
    public TimeSpan MaxRunDuration => RunDuration ?? TimeSpan.FromSeconds(60);
    internal bool IsValid => MaxModelTurns is >=1 and <=4 && MaxToolCalls is >=0 and <=3 &&
        MaxToolFailures is >=1 and <=2 && MaxRetries is >=0 and <=1 && MaxRunDuration>TimeSpan.Zero &&
        MaxRunDuration<=TimeSpan.FromSeconds(120) && MaxInputContext is >=256 and <=32768 &&
        MaxToolResultBytes is >=256 and <=6144 && MaxFinalOutput is >=1 and <=16384 && MaxWriteOperations is >=0 and <=1 &&
        MaxWriteInputBytes is >=1 and <=1024 && MaxTargetFileBytes is >=1 and <=4096 && MaxWriteRetries==0 && MaxSourcePlans is >=0 and <=2 &&
        MaxRepairPlans is >=0 and <=1 && MaxBuildStarts is >=0 and <=2 && MaxTestStarts is >=0 and <=2 &&
        (MaxSourcePlans<=1 ? MaxRepairPlans==0 : MaxRepairPlans==1);
}

public enum AgentRunState { Idle, Analyzing, ExecutingTool, Verifying, Completed, Failed, Cancelled, AwaitingApproval, Recovering }
public enum AgentVerificationRequirement { ModelSnapshot, ModelAndRuntime, SafeTextChange, SourcePlan }
public enum AgentFailureCategory
{
    Cancelled, DeadlineExceeded, ModelTransportFailure, NativeToolUnsupported, ToolNotFound, InvalidToolArguments,
    ToolDenied, ToolTimeout, ToolExecutionFailure, ToolResultTooLarge, ToolCallLimitReached, ModelTurnLimitReached,
    FailureBudgetExceeded, MalformedModelResponse, VerificationFailed, RuntimeDeactivated, ModelIdentityChanged,
    ContextBudgetExceeded, FinalOutputLimitReached, Unknown, Conflict, ApprovalDenied, PathDenied, RecoveryConflict, WriteOperationLimitReached
}

// Run-local facts. Only Nexus code can mutate these; no prompt is authority.
public sealed class AgentRunProgress : IDisposable
{
    private readonly Stopwatch timer=Stopwatch.StartNew();
    private readonly object timingSync=new();
    private readonly TimeProvider clock=TimeProvider.System;
    private readonly CancellationTokenSource? activeDeadline;
    private readonly CancellationToken activeCancellation;
    private readonly TimeSpan? activeLimit;
    private long activeStarted,reviewStarted;
    private TimeSpan activeSpent,reviewSpent;
    private bool waiting,reviewUsed,stopped;
    private Guid budgetRun;
    public AgentRunProgress() { }
    internal AgentRunProgress(TimeSpan budget,TimeProvider clock)
    {
        if(budget<=TimeSpan.Zero || budget>ReviewedProjectCatalog.PilotExecutionBudget)throw new ArgumentOutOfRangeException(nameof(budget));
        this.clock=clock;activeLimit=budget;activeStarted=clock.GetTimestamp();
        activeDeadline=new(budget,clock);activeCancellation=activeDeadline.Token;
    }
    internal bool HasActiveBudget=>activeLimit is not null;
    internal TimeProvider BudgetClock=>clock;
    internal CancellationToken ActiveCancellation=>activeCancellation;
    internal bool IsWaitingForApproval { get {lock(timingSync)return waiting;} }
    internal Guid BudgetRun { get {lock(timingSync)return budgetRun;} }
    private TimeSpan ActiveElapsed=>activeSpent+(waiting || stopped?TimeSpan.Zero:clock.GetElapsedTime(activeStarted));
    internal TimeSpan? RemainingActive { get {lock(timingSync)return activeLimit-ActiveElapsed;} }
    internal TimeSpan RemainingReview { get {lock(timingSync)return ReviewedProjectCatalog.PilotApprovalTtl-(waiting?clock.GetElapsedTime(reviewStarted):reviewSpent);} }
    internal bool BindBudgetRun(Guid run)
    {lock(timingSync){if(budgetRun!=Guid.Empty || stopped)return false;budgetRun=run;return true;}}
    internal bool BeginApprovalWait()
    {
        lock(timingSync)
        {
            if(activeDeadline is null || stopped || reviewUsed || ActiveElapsed>=activeLimit || activeCancellation.IsCancellationRequested ||
                State!=AgentRunState.AwaitingApproval || ModelTurnCount!=1 || ToolCallCount!=1 || SourcePlanCount!=1 ||
                BuildStartCount!=0 || TestStartCount!=0 || mutations.Count!=0)return false;
            activeSpent=ActiveElapsed;reviewStarted=clock.GetTimestamp();waiting=true;reviewUsed=true;
            activeDeadline.CancelAfter(Timeout.InfiniteTimeSpan);
            return !activeCancellation.IsCancellationRequested;
        }
    }
    internal void EndApprovalWait()
    {
        lock(timingSync)
        {
            if(!waiting)return;
            reviewSpent=clock.GetElapsedTime(reviewStarted);waiting=false;activeStarted=clock.GetTimestamp();
            var remaining=activeLimit!.Value-activeSpent;
            activeDeadline!.CancelAfter(remaining>TimeSpan.Zero?remaining:TimeSpan.Zero);
        }
    }
    private readonly List<ToolDispatchResult> dispatches=[];
    private readonly List<Nexus.AI.EditingLite.Services.WriteOperationRecord> mutations=[];
    private readonly List<SourceAttemptFact> attempts=[];
    public AgentRunState State { get; internal set; }=AgentRunState.Idle;
    public int ModelTurnCount { get; internal set; }
    public int ToolCallCount { get; internal set; }
    public int FailureCount { get; internal set; }
    public int RetryCount { get; internal set; }
    public int TotalToolResultBytes { get; internal set; }
    public int ContextBytes { get; internal set; }
    public int WriteOperationCount { get; internal set; }
    public int SourcePlanCount { get; internal set; }
    public int RepairPlanCount { get; internal set; }
    public int BuildStartCount { get; internal set; }
    public int TestStartCount { get; internal set; }
    public int TotalDiffBytes { get; internal set; }
    public int TotalChangedLines { get; internal set; }
    public bool RepairPlanning { get; internal set; }
    public bool FinalSuccessAccepted { get; internal set; }
    [System.Text.Json.Serialization.JsonIgnore] public SourcePlanIntent? AcceptedSourceIntent {get;internal set;}
    public IReadOnlyList<SourceAttemptFact> Attempts=>attempts.AsReadOnly();
    internal void RecordSourceAttempt(SourceAttemptFact fact)
    {
        var index=attempts.FindIndex(a=>a.AttemptNumber==fact.AttemptNumber);
        if(index<0) attempts.Add(fact); else attempts[index]=fact;
    }
    [System.Text.Json.Serialization.JsonIgnore] public Nexus.AI.EditingLite.Services.SourceEditPlan? SourcePlan { get; internal set; }
    [System.Text.Json.Serialization.JsonIgnore] public Nexus.AI.EditingLite.Services.SourcePlanResult? SourceResult { get; internal set; }
    public object? SourceSummary=>SourceResult is null ? null : new { SourceResult.PlanId,SourceResult.PlanDigest,outcome=SourceResult.Outcome.ToString(),SourceResult.FinalVerified,
        files=SourceResult.Operations,build=SourceResult.Build?.Category,tests=SourceResult.Tests?.Category,testsPassed=SourceResult.Tests?.TestsPassed };
    public IReadOnlyList<Nexus.AI.EditingLite.Services.WriteOperationRecord> WriteOperations=>mutations.AsReadOnly();
    public TimeSpan Elapsed { get {if(!HasActiveBudget)return timer.Elapsed;lock(timingSync)return ActiveElapsed;} }
    public TimeSpan ReviewElapsed { get {lock(timingSync)return waiting?clock.GetElapsedTime(reviewStarted):reviewSpent;} }
    public IReadOnlyList<ToolDispatchResult> ToolDispatches => dispatches.AsReadOnly();
    public AgentVerificationResult? Verification { get; internal set; }
    internal void Add(ToolDispatchResult result) => dispatches.Add(result);
    internal void Stop()
    {
        timer.Stop();lock(timingSync)
        {
            if(stopped)return;activeSpent=ActiveElapsed;stopped=true;
            activeDeadline?.CancelAfter(Timeout.InfiniteTimeSpan);
        }
    }
    public void Dispose(){Stop();activeDeadline?.Dispose();}
    internal void AddWrite(Nexus.AI.EditingLite.Services.WriteOperationRecord record)=>mutations.Add(record);
}
