using System.Text.Json;

namespace Nexus.App.Services.Agent.Tools;

public enum ToolRisk { ReadOnly, Write, Process, SystemCritical, SafeWrite, Reversible, Dangerous, SourcePlan }
public enum ToolOutcome { Success, Denied, InvalidArguments, ToolNotFound, Timeout, Cancelled, ExecutionFailure, ResultTooLarge,
    Conflict, ApprovalDenied, PathDenied, BackupFailed, TempWriteFailed, CommitFailed, VerificationFailed, RecoverySucceeded, RecoveryConflict, WriteOperationLimitReached }
public sealed record ToolDescriptor(string Name, string Description, ToolRisk Risk, bool ReadOnly,
    TimeSpan Timeout, int MaxResultSize, JsonElement ArgumentSchema);
public sealed record ToolExecutionContext(AgentRunContext Run);
public sealed record ToolExecutionResult(ToolOutcome Outcome, JsonElement? Data = null, string? ReasonCode = null);
public sealed record ToolPolicyDecision(bool Allowed, string ReasonCode);
public sealed record ToolDispatchResult(string ToolCallId, string ToolName, string ValidationResult,
    ToolPolicyDecision Policy, ToolExecutionResult Result, int ResultLength);

public interface INexusTool
{
    ToolDescriptor Descriptor { get; }
    Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, JsonElement arguments, CancellationToken cancellationToken);
}
