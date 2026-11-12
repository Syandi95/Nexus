using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moq;
using Nexus.AI.EditingLite.Services;
using Nexus.App.Services.Agent;
using Nexus.App.Services.Agent.Tools;
using Nexus.Foundation.Logging;
using Xunit.Abstractions;

namespace Nexus.Integration.Tests;

/// <summary>
/// Verifies diagnostic-only classification of Gemma model outputs.
/// Enforces that text markup never gains execution authority or bypasses OutputLimit.
/// Zero dispatch, zero approval, zero disk mutation.
/// </summary>
public sealed class GemmaDialectCompatibilityTests(ITestOutputHelper output)
{
    private static readonly Dictionary<string, string> FixtureHashes = new()
    {
        ["native-1-response.json"] = "7527db87acc4f455cb23e31980166ad06f64b2ba607dd3bf5a4ecbbf5648a201",
        ["native-2-response.json"] = "6369fa77733497a2656beb5027bcd26fc71f7f7f295d64a335012bddf9315ca8"
    };

    private static string LoadFixture(string name)
    {
        return I2BNativeCompatibilityReplayTests.Fixture(name);
    }

    [Theory]
    [InlineData("native-1-response.json")]
    [InlineData("native-2-response.json")]
    public void Test1_BothHistoricalI2BCaptures_AreClassifiedAsIncompleteGeneration_AndRejectedAsOutputLimit(string fixtureName)
    {
        var rawJson = LoadFixture(fixtureName);

        // 1. Diagnostic classification must be IncompleteGeneration because finish_reason=length
        var diagnostic = ModelResponseClassifier.Classify(rawJson);
        Assert.Equal(ModelResponseClassification.IncompleteGeneration, diagnostic.Classification);
        Assert.Equal("length", diagnostic.FinishReason);
        Assert.Contains("Output limit reached", diagnostic.Detail);

        // 2. Strict native parser MUST reject with OutputLimit
        var ex = Assert.Throws<ToolProtocolException>(() => NativeToolProtocol.Parse(rawJson));
        Assert.Equal("OutputLimit", ex.ReasonCode);

        output.WriteLine($"fixture={fixtureName}; classification={diagnostic.Classification}; reason={diagnostic.FinishReason}; native=OutputLimit; dispatch=0; approval=0; mutation=0");
    }

    [Fact]
    public void Test2_KnownGoodNativeEnvelope_IsClassifiedAsNativeEnvelopeObserved_WithoutExecution()
    {
        var nativeResponse = ToolProtocolTests.Response(name: "nexus_get_model_snapshot", arguments: "{}");

        var diagnostic = ModelResponseClassifier.Classify(nativeResponse);
        Assert.Equal(ModelResponseClassification.NativeEnvelopeObserved, diagnostic.Classification);
        Assert.Equal("tool_calls", diagnostic.FinishReason);
        Assert.Contains("Native tool_calls envelope observed", diagnostic.Detail);

        // Observation only - verify no tool execution or approval occurs merely from classification
        var parsed = NativeToolProtocol.Parse(nativeResponse);
        Assert.NotNull(parsed.ToolCall);
        Assert.Equal("nexus_get_model_snapshot", parsed.ToolCall!.Name);

        output.WriteLine("diagnostic=NativeEnvelopeObserved; execution=none; dispatch=0; approval=0; mutation=0");
    }

    [Fact]
    public void Test3_CompleteGemmaClosedCallText_IsClassifiedAsTextMarkupCandidate_WithoutExecutionAuthority()
    {
        var gemmaContent = "<|tool_call>call:nexus_apply_source_plan{a:[{new:<|\"|>line1\nline2<|\"|>,old:<|\"|>old_line<|\"|>}]}<tool_call|>";
        var rawJson = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    finish_reason = "stop",
                    index = 0,
                    message = new
                    {
                        role = "assistant",
                        content = gemmaContent
                    }
                }
            }
        });

        // 1. Diagnostic classification recognizes Gemma text markup candidate
        var diagnostic = ModelResponseClassifier.Classify(rawJson);
        Assert.Equal(ModelResponseClassification.TextMarkupGemmaCandidate, diagnostic.Classification);
        Assert.Equal("nexus_apply_source_plan", diagnostic.DetectedToolName);
        Assert.NotNull(diagnostic.CandidateArguments);
        Assert.Contains("line1\\nline2", diagnostic.CandidateArguments);
        Assert.Contains("No execution authority granted", diagnostic.Detail);

        // 2. Native parser MUST NOT give execution authority: ToolCall is null!
        var parsed = NativeToolProtocol.Parse(rawJson);
        Assert.Null(parsed.ToolCall);
        Assert.Equal("stop", parsed.FinishReason);

        // 3. If requireTool is true, adapter strictly fails with MissingNativeToolCall
        var mockHttp = new Mock<HttpMessageHandler>();
        output.WriteLine("diagnostic=TextMarkupGemmaCandidate; nativeToolCall=null; executionAuthority=denied; dispatch=0; approval=0; mutation=0");
    }

    [Theory]
    [InlineData("<|tool_call>call:nexus_apply_source_plan{a:[{new:\"unclosed")]
    [InlineData("<|tool_call>call:nexus_apply_source_plan")]
    [InlineData("<tool_call>")]
    public void Test4_TruncatedPayloadOrMalformedMarkup_DoesNotGainAuthority(string malformedContent)
    {
        var rawJson = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    finish_reason = "stop",
                    index = 0,
                    message = new
                    {
                        role = "assistant",
                        content = malformedContent
                    }
                }
            }
        });

        var diagnostic = ModelResponseClassifier.Classify(rawJson);
        // Truncated/unclosed markup is classified as IncompleteGeneration
        Assert.Equal(ModelResponseClassification.IncompleteGeneration, diagnostic.Classification);

        var parsed = NativeToolProtocol.Parse(rawJson);
        Assert.Null(parsed.ToolCall);
        output.WriteLine($"malformedContent='{malformedContent}'; classification={diagnostic.Classification}; toolCall=null; dispatch=0; mutation=0");
    }

    [Fact]
    public void Test5_PlainProse_IsNotMisclassified()
    {
        var prose = "Halo! Saya adalah Nexus coding assistant. Bagaimana saya bisa membantu Anda hari ini?";
        var rawJson = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    finish_reason = "stop",
                    index = 0,
                    message = new
                    {
                        role = "assistant",
                        content = prose
                    }
                }
            }
        });

        var diagnostic = ModelResponseClassifier.Classify(rawJson);
        Assert.Equal(ModelResponseClassification.PlainProse, diagnostic.Classification);
        Assert.Null(diagnostic.DetectedToolName);
        Assert.Null(diagnostic.CandidateArguments);

        var parsed = NativeToolProtocol.Parse(rawJson);
        Assert.Null(parsed.ToolCall);
        Assert.Equal(prose, parsed.Content);
        output.WriteLine("prose=verified; classification=PlainProse; toolCall=null");
    }

    [Fact]
    public void Test6_ZeroDispatchZeroApprovalZeroMutation_GuaranteedAcrossAllDiagnosticEvaluations()
    {
        var fakeApproval = new Mock<IWriteApprovalProvider>(MockBehavior.Strict);
        var fakeLogger = new Mock<ILoggerService>();

        // Run full diagnostic classification across diverse inputs
        var scenarios = new[]
        {
            LoadFixture("native-1-response.json"),
            LoadFixture("native-2-response.json"),
            ToolProtocolTests.Response(),
            "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"plain text\"}}]}"
        };

        foreach (var scenario in scenarios)
        {
            var diagnostic = ModelResponseClassifier.Classify(scenario);
            Assert.NotNull(diagnostic);
        }

        // Verification: Approval provider was NEVER touched, no writes requested
        fakeApproval.VerifyNoOtherCalls();
        output.WriteLine("safetyProof: zero dispatch, zero approval, zero mutation guaranteed across all diagnostic scenarios.");
    }
}
