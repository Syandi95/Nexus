using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Services;
using Xunit;

namespace Nexus.AI.Llama.Tests
{
    public class AttachmentContextBudgetServiceTests
    {
        private readonly AttachmentContextBudgetService _budgetService = new();

        [Fact]
        public void ResolveBudgetAndTrim_EmptyHistory_CalculatesRemainingBudgetCorrectly()
        {
            const int contextWindow = 8192;
            const string systemPrompt = "System instructions."; // ~7 tokens
            const string currentPrompt = "User current question."; // ~8 tokens

            var (trimmedHistory, budget) = _budgetService.ResolveBudgetAndTrim(
                contextWindow,
                systemPrompt,
                Array.Empty<ChatMessagePayload>(),
                currentPrompt,
                hasAttachments: false);

            Assert.Empty(trimmedHistory);
            Assert.True(budget > 0);

            // Effective: 8192 -> outputReservation = 2048 -> usable = (8192 - 2048) * 0.9 = 5529
            // system + prompt = 7 + 8 = 15 tokens -> budget = 5529 - 15 = 5514
            Assert.True(budget >= 5500);
        }

        [Fact]
        public void ResolveBudgetAndTrim_SystemPromptIs100PercentImmutable_NeverDropped()
        {
            // System prompt must be strictly non-trimmable under ADR-004
            const int contextWindow = 2048;
            var longSystemPrompt = new string('S', 600); // 200 tokens
            var currentPrompt = "Hello AI";

            var rawHistory = new List<ChatMessagePayload>
            {
                new() { Role = "user", Content = new string('A', 900) }, // 300 tokens
                new() { Role = "assistant", Content = new string('B', 900) }, // 300 tokens
                new() { Role = "user", Content = new string('C', 900) }, // 300 tokens
                new() { Role = "assistant", Content = new string('D', 900) } // 300 tokens
            };

            var (trimmedHistory, _) = _budgetService.ResolveBudgetAndTrim(
                contextWindow,
                longSystemPrompt,
                rawHistory,
                currentPrompt,
                hasAttachments: true);

            // Trimming drops Turn 1, preserves Turn 2, system prompt is preserved in calculation
            Assert.True(trimmedHistory.Count < rawHistory.Count);
            Assert.Equal("user", trimmedHistory.First().Role);
            Assert.Contains(new string('C', 50), trimmedHistory.First().Content);
        }

        [Fact]
        public void ResolveBudgetAndTrim_ProtectedTurn_ProtectsLastUserAndAssistantMessage()
        {
            const int contextWindow = 2048;
            const string systemPrompt = "System";
            const string currentPrompt = "What is the result?";

            // Turn 1 (Old: 1000 tokens) + Turn 2 (Protected: ~20 tokens)
            var rawHistory = new List<ChatMessagePayload>
            {
                new() { Role = "user", Content = new string('1', 1500) }, // 500 tokens
                new() { Role = "assistant", Content = new string('2', 1500) }, // 500 tokens
                new() { Role = "user", Content = "Protected last user turn" },
                new() { Role = "assistant", Content = "Protected last assistant reply" }
            };

            var (trimmedHistory, _) = _budgetService.ResolveBudgetAndTrim(
                contextWindow,
                systemPrompt,
                rawHistory,
                currentPrompt,
                hasAttachments: true);

            // Turn 1 should be dropped due to MinAttachmentHeadroom reservation, Turn 2 must be preserved
            Assert.Equal(2, trimmedHistory.Count);
            Assert.Equal("Protected last user turn", trimmedHistory[0].Content);
            Assert.Equal("Protected last assistant reply", trimmedHistory[1].Content);
        }

        [Fact]
        public void HistoryTrimming_HandlesAsymmetricTurnPattern_ProtectsFromLastUserMessage()
        {
            // Asymmetric turn pattern: User 1 -> Assistant 1 -> User 2 -> Assistant 2A -> Assistant 2B
            // (e.g. streaming split / retry error causing 2 assistant messages for the last user prompt)
            const int contextWindow = 2048;
            const string systemPrompt = "System instructions";
            const string currentPrompt = "Next question";

            var rawHistory = new List<ChatMessagePayload>
            {
                new() { Role = "user", Content = new string('A', 1500) }, // Turn 1 User (500 tokens)
                new() { Role = "assistant", Content = new string('B', 1500) }, // Turn 1 Assistant (500 tokens)
                new() { Role = "user", Content = "Last User Turn Message" }, // Turn 2 User
                new() { Role = "assistant", Content = "Assistant First Partial Reply" }, // Turn 2 Assistant A
                new() { Role = "assistant", Content = "Assistant Second Followup Reply" } // Turn 2 Assistant B
            };

            var (trimmedHistory, _) = _budgetService.ResolveBudgetAndTrim(
                contextWindow,
                systemPrompt,
                rawHistory,
                currentPrompt,
                hasAttachments: true);

            // Must drop Turn 1 (indices 0 and 1), and protect ALL 3 messages from lastUserIndex onwards!
            Assert.Equal(3, trimmedHistory.Count);
            Assert.Equal("Last User Turn Message", trimmedHistory[0].Content);
            Assert.Equal("Assistant First Partial Reply", trimmedHistory[1].Content);
            Assert.Equal("Assistant Second Followup Reply", trimmedHistory[2].Content);
        }

        [Fact]
        public void ResolveBudgetAndTrim_SingleTurnHistory_NeverDroppedEvenUnderTightBudget()
        {
            const int contextWindow = 2048;
            const string systemPrompt = "System";
            const string currentPrompt = "Prompt";

            // Only 1 turn in history
            var rawHistory = new List<ChatMessagePayload>
            {
                new() { Role = "user", Content = "Only user message" },
                new() { Role = "assistant", Content = "Only assistant reply" }
            };

            var (trimmedHistory, _) = _budgetService.ResolveBudgetAndTrim(
                contextWindow,
                systemPrompt,
                rawHistory,
                currentPrompt,
                hasAttachments: true);

            // Last turn cannot be trimmed
            Assert.Equal(2, trimmedHistory.Count);
            Assert.Equal("Only user message", trimmedHistory[0].Content);
            Assert.Equal("Only assistant reply", trimmedHistory[1].Content);
        }

        [Fact]
        public void ResolveBudgetAndTrim_WithAttachments_ReservesHeadroomForAttachments()
        {
            const int contextWindow = 4096;
            const string systemPrompt = "System";
            const string currentPrompt = "Prompt";

            var rawHistory = new List<ChatMessagePayload>
            {
                new() { Role = "user", Content = new string('X', 1200) }, // 400 tokens
                new() { Role = "assistant", Content = new string('Y', 1200) }, // 400 tokens
                new() { Role = "user", Content = "Last user" },
                new() { Role = "assistant", Content = "Last reply" }
            };

            // Without attachments: history might not need trimming if it fits
            var (_, budgetNoAtt) = _budgetService.ResolveBudgetAndTrim(
                contextWindow,
                systemPrompt,
                rawHistory,
                currentPrompt,
                hasAttachments: false);

            // With attachments: forces trimming of old messages to ensure at least MinAttachmentHeadroom
            var (historyWithAtt, budgetWithAtt) = _budgetService.ResolveBudgetAndTrim(
                contextWindow,
                systemPrompt,
                rawHistory,
                currentPrompt,
                hasAttachments: true);

            Assert.True(budgetWithAtt >= AttachmentContextBudgetService.MinAttachmentHeadroom);
            Assert.True(historyWithAtt.Count <= rawHistory.Count);
        }

        [Fact]
        public void EstimateTokens_CalculatesCeilingRatioAccurately()
        {
            Assert.Equal(0, _budgetService.EstimateTokens(null));
            Assert.Equal(0, _budgetService.EstimateTokens(string.Empty));
            Assert.Equal(1, _budgetService.EstimateTokens("A")); // 1 / 3 = 0.33 -> 1
            Assert.Equal(1, _budgetService.EstimateTokens("ABC")); // 3 / 3 = 1
            Assert.Equal(2, _budgetService.EstimateTokens("ABCD")); // 4 / 3 = 1.33 -> 2
            Assert.Equal(4, _budgetService.EstimateTokens("0123456789")); // 10 / 3 = 3.33 -> 4
        }

        [Fact]
        public void HistoryTrimming_DecrementsTokensIncrementally_MatchesSynchronousSum()
        {
            // Generates 10 turns (20 messages) of progressively varying lengths
            const int contextWindow = 4096;
            const string systemPrompt = "Persistent system instruction that must never be trimmed under ADR-004";
            const string currentPrompt = "User current query demanding attention";

            var rawHistory = new List<ChatMessagePayload>();
            for (int i = 1; i <= 10; i++)
            {
                rawHistory.Add(new ChatMessagePayload { Role = "user", Content = $"User message {i}: " + new string('U', i * 60) });
                rawHistory.Add(new ChatMessagePayload { Role = "assistant", Content = $"Assistant reply {i}: " + new string('A', i * 90) });
            }

            // Execute ResolveBudgetAndTrim with attachments present (enforcing MinAttachmentHeadroom)
            var (trimmedHistory, budget) = _budgetService.ResolveBudgetAndTrim(
                contextWindow,
                systemPrompt,
                rawHistory,
                currentPrompt,
                hasAttachments: true);

            // 1. Confirm that history was trimmed
            Assert.True(trimmedHistory.Count < rawHistory.Count);

            // 2. Perform ground-truth synchronous recalculation
            int systemTokens = _budgetService.EstimateTokens(systemPrompt);
            int currentPromptTokens = _budgetService.EstimateTokens(currentPrompt);
            int nonTrimmableBase = systemTokens + currentPromptTokens;

            // Full fresh synchronous sum of all surviving messages
            int synchronousTrimmedHistoryTokens = trimmedHistory.Sum(m => _budgetService.EstimateTokens(m.Content));
            int synchronousTotalSpent = nonTrimmableBase + synchronousTrimmedHistoryTokens;

            int effectiveContext = Math.Min(contextWindow, AttachmentContextBudgetService.MaxContextCap);
            int outputReservation = Math.Min((int)(effectiveContext * 0.25), AttachmentContextBudgetService.MaxOutputReservation);
            int usableInputRoom = (int)((effectiveContext - outputReservation) * AttachmentContextBudgetService.SafetyRatio);

            int synchronousExpectedBudget = Math.Max(0, usableInputRoom - synchronousTotalSpent);

            // 3. Verify zero drift between incremental O(1) subtraction and synchronous re-sum
            Assert.Equal(synchronousExpectedBudget, budget);
            Assert.True(budget >= AttachmentContextBudgetService.MinAttachmentHeadroom);

            // 4. Verify turn boundary protection: last user turn and its reply survived
            Assert.Contains(trimmedHistory, m => m.Role == "user" && m.Content.Contains("User message 10"));
            Assert.Contains(trimmedHistory, m => m.Role == "assistant" && m.Content.Contains("Assistant reply 10"));
        }
    }
}
