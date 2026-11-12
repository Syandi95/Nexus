using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.AI.Llama.Models;

namespace Nexus.AI.Llama.Services
{
    /// <summary>
    /// Implements ADR-004: Explicit Context Budget & Reserved-First History Trimming.
    /// Manages prompt request-level context headroom, guarantees 100% immutability of system prompts,
    /// protects the active user prompt and the last semantic conversation turn, and frees budget
    /// for pending attachments by trimming older messages oldest-first.
    /// </summary>
    public class AttachmentContextBudgetService
    {
        public const double SafetyRatio = 0.90;          // Cherry Studio ATTACHMENT_INPUT_SAFETY_RATIO
        public const int CharsPerToken = 3;             // Cherry Studio APPROX_CHARS_PER_TOKEN (conservative)
        public const int MinAttachmentHeadroom = 1000;  // Minimal token headroom reserved for attachments
        public const int MaxContextCap = 32768;
        public const int MaxOutputReservation = 4096;

        /// <summary>
        /// Resolves usable input space, trims older history messages while protecting
        /// system instructions and the last complete semantic turn, and computes the attachment token budget.
        /// </summary>
        public (List<ChatMessagePayload> TrimmedHistory, int AttachmentTokenBudget) ResolveBudgetAndTrim(
            int modelContextWindow,
            string systemPrompt,
            IReadOnlyList<ChatMessagePayload> rawHistory,
            string currentPrompt,
            bool hasAttachments)
        {
            int effectiveContext = Math.Min(modelContextWindow, MaxContextCap);
            int outputReservation = Math.Min((int)(effectiveContext * 0.25), MaxOutputReservation);
            int usableInputRoom = (int)((effectiveContext - outputReservation) * SafetyRatio);

            // System prompt (ADR-004: 100% immutable) and current user prompt are strictly non-trimmable
            int systemTokens = EstimateTokens(systemPrompt);
            int currentPromptTokens = EstimateTokens(currentPrompt);
            int nonTrimmableBase = systemTokens + currentPromptTokens;

            if (rawHistory == null || rawHistory.Count == 0)
            {
                int emptyHistoryBudget = Math.Max(0, usableInputRoom - nonTrimmableBase);
                return (new List<ChatMessagePayload>(), emptyHistoryBudget);
            }

            // Wrap history messages with their single-pass estimated token count
            var historyWithTokens = rawHistory
                .Where(m => m != null && (m.Role is "user" or "assistant"))
                .Select(m => (Message: m, Tokens: EstimateTokens(m.Content)))
                .ToList();

            int historyTokens = historyWithTokens.Sum(x => x.Tokens);
            int totalSpent = nonTrimmableBase + historyTokens;

            // Determine semantic turn boundary: find index of last "user" message.
            // Protect all messages from lastUserIndex to the end of historyWithTokens (including asymmetric
            // turn patterns such as user -> assistant -> assistant).
            int lastUserIndex = historyWithTokens.FindLastIndex(x => x.Message.Role == "user");
            int trimmableLimit = lastUserIndex >= 0 ? lastUserIndex : 0;

            // Pre-trimming condition: trim oldest non-protected messages if totalSpent exceeds usable input room
            // OR if attachments are present and remaining room is less than MinAttachmentHeadroom.
            int maxAllowedSpent = usableInputRoom - (hasAttachments ? MinAttachmentHeadroom : 0);

            int dropIndex = 0;
            while (dropIndex < trimmableLimit && totalSpent > maxAllowedSpent)
            {
                totalSpent -= historyWithTokens[dropIndex].Tokens;
                dropIndex++;
            }

            var finalHistory = historyWithTokens
                .Skip(dropIndex)
                .Select(x => x.Message)
                .ToList();

            int attachmentTokenBudget = Math.Max(0, usableInputRoom - totalSpent);
            return (finalHistory, attachmentTokenBudget);
        }

        public int EstimateTokens(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            return (int)Math.Ceiling((double)text.Length / CharsPerToken);
        }
    }
}
