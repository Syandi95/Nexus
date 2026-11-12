using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.AI.Llama.Services
{
    /// <summary>
    /// Port of Cherry Studio's water-filling attachment budget allocation algorithm
    /// (src/main/ai/messages/attachmentBudget.ts:77-129).
    /// Guarantees that attachment token expenditure never exceeds the allocated request budget,
    /// prioritizes complete inclusion of smaller attachments, and fairly shares leftover budget
    /// among larger attachments.
    /// </summary>
    public static class WaterFillingBudgetAllocator
    {
        public const int DefaultCharsPerToken = 3;

        /// <summary>
        /// Water-filling allocation: smallest first, each takes an equal share of what is left or
        /// its whole size, whichever is smaller, and returns the difference to the pool.
        /// Port 1:1 of Cherry Studio allocateAttachmentBudget (attachmentBudget.ts:92-105).
        /// </summary>
        public static int[] AllocateAttachmentBudget(IReadOnlyList<int> sizes, int total)
        {
            if (sizes == null || sizes.Count == 0)
                return Array.Empty<int>();

            var allocated = new int[sizes.Count];
            var smallestFirst = sizes
                .Select((size, index) => (Size: size, Index: index))
                .OrderBy(x => x.Size)
                .ToList();

            int remaining = Math.Max(0, total);
            int unserved = smallestFirst.Count;

            foreach (var item in smallestFirst)
            {
                int take = Math.Min(Math.Max(0, item.Size), remaining / unserved);
                allocated[item.Index] = take;
                remaining -= take;
                unserved--;
            }

            return allocated;
        }

        /// <summary>
        /// Finds the largest prefix of body that costs at most tokenCap tokens.
        /// Uses measured binary search with surrogate safety to avoid splitting UTF-16 surrogate pairs.
        /// Port 1:1 of Cherry Studio charCapFor (attachmentBudget.ts:116-129).
        /// </summary>
        public static int CharCapFor(string body, int tokens, int tokenCap, Func<string, int>? tokenCounter = null)
        {
            if (string.IsNullOrEmpty(body) || tokenCap <= 0)
                return 0;

            if (tokenCap >= tokens)
                return body.Length;

            tokenCounter ??= (text) => (int)Math.Ceiling((double)text.Length / DefaultCharsPerToken);

            int lo = 0;
            int hi = body.Length;

            while (lo < hi)
            {
                // Math.Ceiling keeps mid > lo, ensuring each round strictly narrows and terminates
                int mid = (int)Math.Ceiling((lo + hi) / 2.0);
                if (tokenCounter(body[..mid]) <= tokenCap)
                    lo = mid;
                else
                    hi = mid - 1;
            }

            // Backing off a surrogate pair only removes a character, never adds tokens
            return SurrogateSafeEnd(body, lo);
        }

        /// <summary>
        /// Ensures a string truncation boundary does not split a UTF-16 surrogate pair.
        /// Port 1:1 of Cherry Studio surrogateSafeEnd (textPaging.ts / attachmentBudget.ts:128).
        /// </summary>
        public static int SurrogateSafeEnd(string text, int length)
        {
            if (string.IsNullOrEmpty(text) || length <= 0)
                return 0;

            if (length >= text.Length)
                return text.Length;

            // If the last included char is a high surrogate (first half of a pair), back off by 1
            if (char.IsHighSurrogate(text[length - 1]))
            {
                return length - 1;
            }

            return length;
        }

        /// <summary>
        /// Slices text to length safely without splitting UTF-16 surrogate pairs.
        /// </summary>
        public static string SliceSafe(string text, int length)
        {
            if (string.IsNullOrEmpty(text) || length <= 0)
                return string.Empty;

            int safeLength = SurrogateSafeEnd(text, length);
            return text[..safeLength];
        }

        /// <summary>
        /// Calculates character caps for each pending inline attachment from one shared token pool.
        /// Port 1:1 of Cherry Studio allocateInlineCaps (attachmentBudget.ts:77-81).
        /// </summary>
        public static int[] AllocateInlineCaps(IReadOnlyList<string> bodies, int totalBudget, Func<string, int>? tokenCounter = null)
        {
            if (bodies == null || bodies.Count == 0)
                return Array.Empty<int>();

            tokenCounter ??= (text) => (int)Math.Ceiling((double)text.Length / DefaultCharsPerToken);

            var sizes = bodies.Select(b => tokenCounter(b)).ToList();
            var allocatedTokens = AllocateAttachmentBudget(sizes, totalBudget);

            var caps = new int[bodies.Count];
            for (int i = 0; i < bodies.Count; i++)
            {
                caps[i] = CharCapFor(bodies[i], sizes[i], allocatedTokens[i], tokenCounter);
            }

            return caps;
        }
    }
}
