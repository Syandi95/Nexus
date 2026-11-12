using System.Threading.Tasks;
using Nexus.Core.HomeSettings.Services;
using Xunit;

namespace Nexus.Core.HomeSettings.Tests
{
    public class HomeDashboardServiceTests
    {
        [Fact]
        public async Task UpdateActivityAsync_ShouldUpdateConfig()
        {
            var configService = new FakeConfigService();
            var service = new HomeDashboardService(configService);

            await service.UpdateActivityAsync("ModuleA", 10, 2048);

            var (module, fileCount, totalSize, timestamp) = service.GetLastActivity();
            Assert.Equal("ModuleA", module);
            Assert.Equal(10, fileCount);
            Assert.Equal(2048, totalSize);
            Assert.False(string.IsNullOrEmpty(timestamp));
        }

        [Fact]
        public void SetEngineStatus_ShouldUpdateConfig()
        {
            var configService = new FakeConfigService();
            var service = new HomeDashboardService(configService);

            service.SetEngineStatus("Running", "Active");

            var config = configService.GetConfig();
            Assert.Equal("Running", config.EngineStatus);
            Assert.Equal("Active", config.EngineState);
        }

        [Fact]
        public void SetFolderStatus_ShouldUpdateConfig()
        {
            var configService = new FakeConfigService();
            var service = new HomeDashboardService(configService);

            service.SetFolderStatus("C:\\TestFolder");

            var config = configService.GetConfig();
            Assert.Equal("C:\\TestFolder", config.ActiveFolderPath);
        }
    }
}
