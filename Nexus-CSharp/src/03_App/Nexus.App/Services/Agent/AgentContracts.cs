namespace Nexus.App.Services.Agent;

// Explicit routing: one streaming call, or finite native read-only orchestration.
public sealed record AgentRequest(string SessionId,string Prompt,string? RelevantContext=null,
    int MaxOutputTokens=64,TimeSpan? Timeout=null,bool EnableReadOnlyToolCall=false,
    AgentRunLimits? Limits=null,AgentVerificationRequirement Verification=AgentVerificationRequirement.ModelSnapshot,
    SafeTextChangeIntent? WriteIntent=null,SourcePlanIntent? SourceIntent=null,
    [property:System.Text.Json.Serialization.JsonIgnore] SourceProposalScope? ProposalScope=null)
{
    [System.Text.Json.Serialization.JsonIgnore] internal AgentRunProgress? ExecutionProgress {get;init;}
}

public sealed record SourcePlanIntent(string ProfileId,System.Collections.Immutable.ImmutableArray<Nexus.AI.EditingLite.Services.SourceFileEdit> Files)
{
    internal bool IsValid=>!string.IsNullOrWhiteSpace(ProfileId) && Files.Length is >=2 and <=3 && Files.Select(f=>f.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count()==Files.Length &&
        Files.All(f=>f.Target is not null && f.Target.Length<=64 && f.BeforeHash is not null && f.BeforeHash.Length==64 && f.BeforeHash.All(Uri.IsHexDigit) &&
            !f.Edits.IsDefaultOrEmpty && f.Edits.Length<=2 && f.Edits.All(Nexus.AI.EditingLite.Services.ExactSourceEdits.ValidLiteral)) && Files.Sum(f=>f.Edits.Length)<=3 &&
        Nexus.AI.EditingLite.Services.ExactSourceEdits.LiteralBytes(Files.SelectMany(f=>f.Edits))<=1024;
}

// Trusted caller supplies the task/precondition, never an approved root or approval.
public sealed record SafeTextChangeIntent(string Target,string ExpectedSha256,string OldText,string NewText)
{
    internal bool IsValid(AgentRunLimits limits)
    {
        if(string.IsNullOrWhiteSpace(Target) || Target.Length>64 || ExpectedSha256 is null || ExpectedSha256.Length!=64 ||
            ExpectedSha256.Any(c=>!Uri.IsHexDigit(c)) || string.IsNullOrEmpty(OldText) || NewText is null ||
            OldText.Length>256 || NewText.Length>256 || OldText.Contains('\0') || NewText.Contains('\0')) return false;
        try { var utf8=new System.Text.UTF8Encoding(false,true); return utf8.GetByteCount(OldText)+utf8.GetByteCount(NewText)<=limits.MaxWriteInputBytes; }
        catch(ArgumentException) { return false; }
    }
}

public sealed record AgentModelSnapshot(string ModelId,string ModelPath,int? Pid,string Endpoint,int RuntimeIdentity);

public sealed record AgentRunContext(Guid RunId,string SessionId,DateTimeOffset StartedAt,
    DateTimeOffset Deadline,int MaxOutputTokens,AgentModelSnapshot? Model,
    AgentRunLimits? Limits=null,AgentRunProgress? Progress=null,
    [property: System.Text.Json.Serialization.JsonIgnore] CancellationToken Cancellation=default,
    [property: System.Text.Json.Serialization.JsonIgnore] SafeTextChangeIntent? WriteIntent=null,
    string? ActiveToolCallId=null,
    [property: System.Text.Json.Serialization.JsonIgnore] SourcePlanIntent? SourceIntent=null,
    [property: System.Text.Json.Serialization.JsonIgnore] Action? CheckIdentity=null,
    [property: System.Text.Json.Serialization.JsonIgnore] SourceProposalScope? ProposalScope=null)
{
    public TimeSpan RemainingDuration => Progress?.RemainingActive ?? (Progress is null ? Deadline-DateTimeOffset.UtcNow : Limits!.MaxRunDuration-Progress.Elapsed);
}

public enum AgentRunOutcome { Completed, Cancelled, TimedOut, Rejected, Failed }

public sealed record AgentResult(AgentRunContext Context,AgentRunOutcome Outcome,string Content,
    string? FinishReason,string? ErrorCategory,TimeSpan Duration,Tools.ToolDispatchResult? Tool=null,
    IReadOnlyList<Tools.ToolDispatchResult>? ToolDispatches=null,AgentVerificationResult? Verification=null,
    AgentFailureCategory? Failure=null)
{
    public int Attempts=>Context.Progress?.Attempts.Count ?? 0;
    public bool RepairUsed=>Context.Progress?.RepairPlanCount==1;
    public string? Attempt1FailureCategory=>Context.Progress?.Attempts.FirstOrDefault()?.Result.Build?.CompilerFailure is not null ? "CompilerError" : Context.Progress?.Attempts.FirstOrDefault()?.FailureSource;
    public string? DiagnosticId=>Context.Progress?.Attempts.FirstOrDefault()?.Result.Build?.CompilerFailure?.DiagnosticId;
    public string? Attempt2Outcome=>Attempts==2 ? Outcome==AgentRunOutcome.Completed ? "Completed" : Context.Progress!.Attempts[1].VerificationOutcome : null;
}

public interface INexusAgent
{
    Task<AgentResult> ExecuteAsync(AgentRequest request,CancellationToken cancellationToken);
}
