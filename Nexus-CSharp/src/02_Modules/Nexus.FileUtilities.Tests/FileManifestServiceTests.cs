using System.IO;
using System.Threading.Tasks;
using Xunit;
using Nexus.FileUtilities.Services;
using Nexus.FileUtilities.Models;
using System.Collections.Generic;

namespace Nexus.FileUtilities.Tests
{
    public class FileManifestServiceTests
    {
        [Fact]
        public async Task SaveAndLoadManifest_ShouldWork()
        {
            var service = new FileManifestService();
            var path = Path.GetTempFileName();
            var manifest = new FileManifest { OriginalPaths = new Dictionary<string, string> { { "a", "b" } } };
            
            await service.SaveManifestAsync(manifest, path);
            var loaded = await service.LoadManifestAsync(path);
            
            Assert.Equal("b", loaded.OriginalPaths["a"]);
            File.Delete(path);
        }
    }
}
