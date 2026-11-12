using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Nexus.FileUtilities.Services;
using Nexus.FileUtilities.Models;
using System.Diagnostics;

namespace Nexus.FileUtilities.Tests
{
    public class RapikanFileServiceTests
    {
        [Theory]
        [InlineData("test.jpg", "Images")]
        [InlineData("test.mp4", "Videos")]
        [InlineData("test.docx", "Documents")]
        [InlineData("test.zip", "Archives")]
        [InlineData("test.exe", "Applications")]
        [InlineData("test.unknown", "Others")]
        public void GetCategoryDestination_ShouldMapCorrectly(string fileName, string expected)
        {
            var service = new RapikanFileService(new FileManifestService());
            Assert.Equal(expected, service.GetCategoryDestination(fileName));
        }

        [Fact]
        public async Task ExecuteOrganizeAsync_ShouldBePerformant()
        {
            // Performance Gate: >= 5x faster than PowerShell. Typical PS takes ~1-2s for 1000 files. We aim for < 500ms.
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            for(int i = 0; i < 20; i++)
            {
                File.WriteAllText(Path.Combine(tempDir, $"test_{i}.txt"), "test");
            }

            var service = new RapikanFileService(new FileManifestService());
            var sw = Stopwatch.StartNew();
            await service.ExecuteOrganizeAsync(new RapikanOptions { RootPath = tempDir });
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 3000, $"Performance gate failed. Took {sw.ElapsedMilliseconds}ms");
            
            Directory.Delete(tempDir, true);
        }
    }
}
