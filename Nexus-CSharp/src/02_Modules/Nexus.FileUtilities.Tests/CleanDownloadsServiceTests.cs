using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Nexus.FileUtilities.Services;
using Nexus.FileUtilities.Models;

namespace Nexus.FileUtilities.Tests
{
    public class CleanDownloadsServiceTests
    {
        [Theory]
        [InlineData("WhatsApp Image 2023-01-01.jpeg", @"Images\WhatsApp")]
        [InlineData("random_file.pdf", @"Documents\PDF")]
        [InlineData("spanduk_event.pdf", @"Desain\PDF")]
        [InlineData("document.docx", @"Documents\Word")]
        [InlineData("archive.zip", "Archives")]
        [InlineData("unknown.xyz", "Others")]
        public void GetDestinationSubfolder_ShouldMapCorrectly(string fileName, string expected)
        {
            var service = new CleanDownloadsService(new FileManifestService());
            Assert.Equal(expected, service.GetDestinationSubfolder(fileName));
        }

        [Fact]
        public async Task Scan1000Files_PerformanceGate_ShouldCompleteUnder1Second()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                for (int i = 0; i < 1000; i++)
                {
                    File.WriteAllText(Path.Combine(tempDir, $"file_{i:D4}.txt"), "sample content");
                }

                var service = new CleanDownloadsService(new FileManifestService());
                var sw = Stopwatch.StartNew();
                var items = await service.ScanDownloadsFolderAsync(tempDir);
                sw.Stop();

                Assert.Equal(1000, items.Count);
                Assert.True(sw.ElapsedMilliseconds < 1000, $"Scan 1000 files took {sw.ElapsedMilliseconds}ms");
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
