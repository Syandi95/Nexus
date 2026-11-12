using System.Net;
using System.Net.Http;

namespace Nexus.App.Services.Agent;

public static class RetryClassifier
{
    // No automatic tool retry: current tools have no certified transient failure.
    public static bool CanRetry(AgentFailureCategory category,HttpStatusCode? status=null) =>
        category==AgentFailureCategory.ModelTransportFailure && status is HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
    internal static AgentFailureCategory Classify(Exception error) => error switch
    {
        HttpRequestException => AgentFailureCategory.ModelTransportFailure,
        ToolProtocolException p => p.ReasonCode switch
        {
            "NativeToolCapabilityUnknown" => AgentFailureCategory.NativeToolUnsupported,
            "ToolCallLimit" or "ToolCallLimitReached" => AgentFailureCategory.ToolCallLimitReached,
            "ModelTurnLimitReached" => AgentFailureCategory.ModelTurnLimitReached,
            "FailureBudgetExceeded" => AgentFailureCategory.FailureBudgetExceeded,
            "ContextBudgetExceeded" or "RequestLimit" => AgentFailureCategory.ContextBudgetExceeded,
            "OutputLimit" or "ResponseLimit" => AgentFailureCategory.FinalOutputLimitReached,
            "VerificationFailed" => AgentFailureCategory.VerificationFailed,
            "ToolNotFound" => AgentFailureCategory.ToolNotFound,
            "ToolInvalidArguments" => AgentFailureCategory.InvalidToolArguments,
            "ToolDenied" => AgentFailureCategory.ToolDenied,
            "ToolTimeout" => AgentFailureCategory.ToolTimeout,
            "ToolExecutionFailure" => AgentFailureCategory.ToolExecutionFailure,
            "ToolResultTooLarge" => AgentFailureCategory.ToolResultTooLarge,
            "RuntimeIdentityChanged" => AgentFailureCategory.ModelIdentityChanged,
            "ToolProtocolUnavailable" => AgentFailureCategory.NativeToolUnsupported,
            "ToolConflict" => AgentFailureCategory.Conflict,
            "ToolApprovalDenied" => AgentFailureCategory.ApprovalDenied,
            "ToolPathDenied" => AgentFailureCategory.PathDenied,
            "ToolRecoveryConflict" => AgentFailureCategory.RecoveryConflict,
            "ToolWriteOperationLimitReached" => AgentFailureCategory.WriteOperationLimitReached,
            "ToolVerificationFailed" or "ToolRecoverySucceeded" => AgentFailureCategory.VerificationFailed,
            "ToolBackupFailed" or "ToolTempWriteFailed" or "ToolCommitFailed" => AgentFailureCategory.ToolExecutionFailure,
            "MalformedNativeResponse" or "InvalidChoices" or "InvalidAssistantRole" or "MissingNativeToolCall" or
            "InvalidToolCalls" or "InvalidToolCallId" or "DuplicateToolCallId" or "InvalidCallType" or
            "InvalidToolName" or "ArgumentLimit" or "InvalidToolFinishReason" or "InvalidCallShape" or
            "UnexpectedCallMetadata" or "DuplicateNativeProperty" or "IncompleteModelResponse" => AgentFailureCategory.MalformedModelResponse,
            _ => AgentFailureCategory.Unknown
        },
        System.Text.Json.JsonException => AgentFailureCategory.MalformedModelResponse,
        _ => AgentFailureCategory.Unknown
    };
}
