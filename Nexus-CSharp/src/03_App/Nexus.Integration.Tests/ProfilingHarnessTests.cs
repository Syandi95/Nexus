using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Nexus.AI.EditingLite.Services;
using Nexus.AI.Llama.Models;
using Nexus.FileUtilities.Services;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Integration.Tests
{
    public class ProfilingHarnessTests
    {
        private readonly ITestOutputHelper _output;

        public ProfilingHarnessTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Profile_SSE_Chunk_Parsing_Memory_Allocation()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long beforeAllocated = GC.GetTotalAllocatedBytes(true);

            string sseLine = "data: {\"id\":\"chatcmpl-1\",\"object\":\"chat.completion.chunk\",\"created\":1677652288,\"model\":\"llama3\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hello world\"},\"finish_reason\":null}]}";

            for (int i = 0; i < 5000; i++)
            {
                string json = sseLine.Substring(6).Trim();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string content = root.GetProperty("choices")[0].GetProperty("delta").GetProperty("content").GetString() ?? "";
            }

            long afterAllocated = GC.GetTotalAllocatedBytes(true);
            long totalAllocated = afterAllocated - beforeAllocated;

            _output.WriteLine($"[PROFILING TAHAP 1] SSE 5,000 Chunk Parsing Total Memory Allocated: {totalAllocated:N0} bytes ({totalAllocated / 1024.0:N2} KB)");
            Assert.True(totalAllocated > 0);
        }

        [Fact]
        public void Profile_Diff_Generator_Memory_Allocation()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long beforeAllocated = GC.GetTotalAllocatedBytes(true);

            var diffService = new DiffGeneratorService();
            string oldText = string.Join("\n", new string[500]);
            string newText = string.Join("\n", new string[500]);

            for (int i = 0; i < 100; i++)
            {
                var diff = diffService.CreateDiff(oldText, newText);
            }

            long afterAllocated = GC.GetTotalAllocatedBytes(true);
            long totalAllocated = afterAllocated - beforeAllocated;

            _output.WriteLine($"[PROFILING TAHAP 1] Diff Generator 100 Runs Total Memory Allocated: {totalAllocated:N0} bytes ({totalAllocated / 1024.0:N2} KB)");
            Assert.True(totalAllocated > 0);
        }

        [Fact]
        public void Profile_File_Metadata_Formatting_Allocation()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long beforeAllocated = GC.GetTotalAllocatedBytes(true);

            var metaService = new FileMetadataService();
            for (int i = 0; i < 10000; i++)
            {
                string sizeStr = metaService.FormatFileSize(1048576 * (i + 1));
                string ageStr = metaService.GetAgeText(DateTime.Now.AddDays(-i));
            }

            long afterAllocated = GC.GetTotalAllocatedBytes(true);
            long totalAllocated = afterAllocated - beforeAllocated;

            _output.WriteLine($"[PROFILING TAHAP 1] File Metadata 10,000 Formatting Total Memory Allocated: {totalAllocated:N0} bytes ({totalAllocated / 1024.0:N2} KB)");
            Assert.True(totalAllocated > 0);
        }
    }
}
