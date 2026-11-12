using System;
using System.Linq;
using Nexus.AI.Llama.Services;
using Xunit;

namespace Nexus.AI.Llama.Tests
{
    public class WaterFillingBudgetAllocatorTests
    {
        [Fact]
        public void AllocateAttachmentBudget_EmptySizes_ReturnsEmptyArray()
        {
            var result = WaterFillingBudgetAllocator.AllocateAttachmentBudget(Array.Empty<int>(), 1000);
            Assert.Empty(result);
        }

        [Fact]
        public void AllocateAttachmentBudget_ZeroOrNegativeBudget_AllocatesZeros()
        {
            var result = WaterFillingBudgetAllocator.AllocateAttachmentBudget(new[] { 100, 200 }, 0);
            Assert.Equal(new[] { 0, 0 }, result);

            var negativeResult = WaterFillingBudgetAllocator.AllocateAttachmentBudget(new[] { 100, 200 }, -50);
            Assert.Equal(new[] { 0, 0 }, negativeResult);
        }

        [Fact]
        public void AllocateAttachmentBudget_BudgetExceedsAll_AllocatesFullSizes()
        {
            var sizes = new[] { 100, 200, 300 };
            var allocated = WaterFillingBudgetAllocator.AllocateAttachmentBudget(sizes, 1000);

            Assert.Equal(new[] { 100, 200, 300 }, allocated);
            Assert.True(allocated.Sum() <= 1000);
        }

        [Fact]
        public void AllocateAttachmentBudget_WaterFilling_SmallFilesSatisfiedAndRemainingSharedFairly()
        {
            // Direct comparison with Cherry Studio test case:
            // Sizes = [50, 500, 1000], total budget = 600
            // 1. Smallest is 50 -> unserved = 3, remaining = 600. take = min(50, 600/3=200) = 50.
            //    Remaining = 550, unserved = 2.
            // 2. Next is 500 -> take = min(500, 550/2=275) = 275.
            //    Remaining = 275, unserved = 1.
            // 3. Last is 1000 -> take = min(1000, 275/1=275) = 275.
            // Expected: [50, 275, 275], sum = 600.
            var sizes = new[] { 50, 500, 1000 };
            var allocated = WaterFillingBudgetAllocator.AllocateAttachmentBudget(sizes, 600);

            Assert.Equal(new[] { 50, 275, 275 }, allocated);
            Assert.Equal(600, allocated.Sum());
        }

        [Fact]
        public void AllocateAttachmentBudget_PreservesOriginalOrder()
        {
            // Sizes in unsorted order: [1000, 50, 500], total = 600
            // Expected allocated order maps to original indices: [275, 50, 275]
            var sizes = new[] { 1000, 50, 500 };
            var allocated = WaterFillingBudgetAllocator.AllocateAttachmentBudget(sizes, 600);

            Assert.Equal(new[] { 275, 50, 275 }, allocated);
            Assert.Equal(600, allocated.Sum());
        }

        [Theory]
        [InlineData(new[] { 10, 20, 30 }, 15)]
        [InlineData(new[] { 500, 500, 500 }, 1000)]
        [InlineData(new[] { 1, 1, 1 }, 2)]
        [InlineData(new[] { 10000, 20000 }, 5000)]
        public void AllocateAttachmentBudget_SumNeverExceedsTotal(int[] sizes, int totalBudget)
        {
            var allocated = WaterFillingBudgetAllocator.AllocateAttachmentBudget(sizes, totalBudget);
            Assert.True(allocated.Sum() <= totalBudget);
        }

        [Fact]
        public void CharCapFor_TokenCapExceedsTokens_ReturnsFullLength()
        {
            const string body = "Hello NEXUS World";
            int cap = WaterFillingBudgetAllocator.CharCapFor(body, tokens: 6, tokenCap: 10);
            Assert.Equal(body.Length, cap);
        }

        [Fact]
        public void CharCapFor_TokenCapZeroOrNegative_ReturnsZero()
        {
            const string body = "Hello NEXUS World";
            Assert.Equal(0, WaterFillingBudgetAllocator.CharCapFor(body, 6, 0));
            Assert.Equal(0, WaterFillingBudgetAllocator.CharCapFor(body, 6, -1));
        }

        [Fact]
        public void CharCapFor_BinarySearch_FindsExactBoundary()
        {
            // 300 chars of ASCII: 3 chars per token -> 100 tokens
            var body = new string('A', 300);
            // Cap at 30 tokens -> exactly 90 characters
            int cap = WaterFillingBudgetAllocator.CharCapFor(body, tokens: 100, tokenCap: 30);
            Assert.Equal(90, cap);
        }

        [Fact]
        public void CharCapFor_SurrogateSafety_DoesNotSplitSurrogatePair()
        {
            // Emoji "😀" (U+1F600) consists of:
            // High surrogate: \uD83D (0xD83D)
            // Low surrogate:  \uDE00 (0xDE00)
            const string emoji = "\uD83D\uDE00";
            var text = "Prefix" + emoji + "Suffix"; // length = 6 + 2 + 6 = 14
            // Index 7 lands right on high surrogate \uD83D. SurrogateSafeEnd must back off to 6!
            int safeEnd = WaterFillingBudgetAllocator.SurrogateSafeEnd(text, 7);
            Assert.Equal(6, safeEnd);

            // Index 8 includes both high and low surrogate, so 8 is safe
            int safeEndPair = WaterFillingBudgetAllocator.SurrogateSafeEnd(text, 8);
            Assert.Equal(8, safeEndPair);
        }

        [Fact]
        public void AllocateInlineCaps_MultiFile_ReturnsExactCaps()
        {
            var bodies = new[]
            {
                new string('A', 30),  // 10 tokens
                new string('B', 300), // 100 tokens
                new string('C', 600)  // 200 tokens
            };

            // Total budget = 70 tokens:
            // Smallest (10) takes 10, remaining = 60, unserved = 2.
            // Next (100) takes min(100, 60/2=30) = 30 tokens -> 90 chars.
            // Next (200) takes min(200, 30/1=30) = 30 tokens -> 90 chars.
            var caps = WaterFillingBudgetAllocator.AllocateInlineCaps(bodies, 70);

            Assert.Equal(3, caps.Length);
            Assert.Equal(30, caps[0]); // Full length
            Assert.Equal(90, caps[1]); // Capped at 30 tokens = 90 chars
            Assert.Equal(90, caps[2]); // Capped at 30 tokens = 90 chars
        }
    }
}
