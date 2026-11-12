using System;
using System.IO;
using System.Threading.Tasks;
using Nexus.Core.HomeSettings.Services;
using Xunit;

namespace Nexus.Core.HomeSettings.Tests
{
    public class SettingsManagerServiceTests
    {
        [Fact]
        public void GetTheme_ShouldReturnDefault()
        {
            var configService = new FakeConfigService();
            var service = new SettingsManagerService(configService);

            Assert.Equal("Dark", service.GetTheme());
        }

        [Fact]
        public void GetDownloadsPath_ShouldReturnDefault()
        {
            var configService = new FakeConfigService();
            var service = new SettingsManagerService(configService);

            Assert.Equal(string.Empty, service.GetDownloadsPath());
        }

        [Fact]
        public async Task SaveSettingsAsync_ShouldUpdateSettings()
        {
            var configService = new FakeConfigService();
            var service = new SettingsManagerService(configService);

            await service.SaveSettingsAsync("Light", "D:\\Downloads", false, false);

            Assert.Equal("Light", service.GetTheme());
            Assert.Equal("D:\\Downloads", service.GetDownloadsPath());

            var config = configService.GetConfig();
            Assert.False(config.ConfirmExit);
            Assert.False(config.ConfirmModule);
        }

        [Fact]
        public void Sync_HeaderAndSettings_ReadSameTheme()
        {
            var configService = new FakeConfigService();
            var manager = new SettingsManagerService(configService);

            configService.GetConfig().Theme = "Light";

            Assert.Equal("Light", manager.GetTheme());
        }

        [Fact]
        public async Task RepeatedThemeSaves_NoDrift()
        {
            var configService = new FakeConfigService();
            var manager = new SettingsManagerService(configService);

            await manager.SaveSettingsAsync("Light", "", true, true);
            Assert.Equal("Light", manager.GetTheme());
            Assert.Equal("Light", configService.GetConfig().Theme);

            await manager.SaveSettingsAsync("Dark", "", true, true);
            Assert.Equal("Dark", manager.GetTheme());
            Assert.Equal("Dark", configService.GetConfig().Theme);

            await manager.SaveSettingsAsync("Light", "", true, true);
            Assert.Equal("Light", manager.GetTheme());
            Assert.Equal("Light", configService.GetConfig().Theme);
        }

        [Fact]
        public async Task Restart_ReadsSameConfig_AsBeforeReload()
        {
            var configService = new FakeConfigService();
            var manager = new SettingsManagerService(configService);
            await manager.SaveSettingsAsync("Light", "D:\\Downloads", false, false);

            var reloadedService = new FakeConfigService();
            reloadedService.SaveConfig(configService.GetConfig());
            var reloadedManager = new SettingsManagerService(reloadedService);

            Assert.Equal("Light", reloadedManager.GetTheme());
            Assert.Equal("D:\\Downloads", reloadedManager.GetDownloadsPath());
            Assert.False(reloadedManager.GetConfirmExit());
            Assert.False(reloadedManager.GetConfirmModule());
        }

        [Fact]
        public async Task ResetToDefaultAsync_ShouldResetSettings()
        {
            var configService = new FakeConfigService();
            var service = new SettingsManagerService(configService);

            // Change first
            await service.SaveSettingsAsync("Light", "D:\\Downloads", false, false);
            
            // Reset
            await service.ResetToDefaultAsync();

            Assert.Equal("Dark", service.GetTheme());
            Assert.Equal(string.Empty, service.GetDownloadsPath());
            
            var config = configService.GetConfig();
            Assert.True(config.ConfirmExit);
            Assert.True(config.ConfirmModule);
        }

        [Fact]
        public void ModelRootFolder_Default_ResolvesToBaseDirectoryModels()
        {
            var configService = new FakeConfigService();
            var service = new SettingsManagerService(configService);

            var expected = Path.GetFullPath(@".\Models", AppContext.BaseDirectory);

            Assert.Equal(expected, service.ModelRootFolder);
        }

        [Fact]
        public void ModelRootFolder_Getter_ResolvesRelativeAgainstBaseDirectory()
        {
            var configService = new FakeConfigService();
            configService.GetConfig().ModelRootFolder = "PortableModels";
            var service = new SettingsManagerService(configService);

            var expected = Path.GetFullPath("PortableModels", AppContext.BaseDirectory);

            Assert.Equal(expected, service.ModelRootFolder);
        }

        [Fact]
        public void ModelRootFolder_Getter_PreservesAbsoluteValue()
        {
            var configService = new FakeConfigService();
            configService.GetConfig().ModelRootFolder = @"E:\Models";
            var service = new SettingsManagerService(configService);

            Assert.Equal(@"E:\Models", service.ModelRootFolder);
        }

        [Fact]
        public void ModelRootFolder_Setter_PersistsToConfig()
        {
            var configService = new FakeConfigService();
            var service = new SettingsManagerService(configService);

            service.ModelRootFolder = @"E:\Models";

            Assert.Equal(@"E:\Models", configService.GetConfig().ModelRootFolder);
            Assert.Equal(@"E:\Models", service.ModelRootFolder);
        }

        [Fact]
        public async Task ResetToDefaultAsync_RestoresDefaultModelRoot()
        {
            var configService = new FakeConfigService();
            var service = new SettingsManagerService(configService);

            service.ModelRootFolder = @"E:\Models";

            await service.ResetToDefaultAsync();

            var expected = Path.GetFullPath(@".\Models", AppContext.BaseDirectory);

            Assert.Equal(expected, service.ModelRootFolder);
        }

        [Fact]
        public void EngineRootFolder_Default_ResolvesToBaseDirectoryEngine()
        {
            var configService = new FakeConfigService();
            var service = new SettingsManagerService(configService);

            var expected = Path.GetFullPath(@".\Engine", AppContext.BaseDirectory);

            Assert.Equal(expected, service.EngineRootFolder);
        }

        [Fact]
        public void EngineRootFolder_Getter_ResolvesRelativeAgainstBaseDirectory()
        {
            var configService = new FakeConfigService();
            configService.GetConfig().EngineRootFolder = "PortableEngine";
            var service = new SettingsManagerService(configService);

            var expected = Path.GetFullPath("PortableEngine", AppContext.BaseDirectory);

            Assert.Equal(expected, service.EngineRootFolder);
        }

        [Fact]
        public void EngineRootFolder_Getter_PreservesAbsoluteValue()
        {
            var configService = new FakeConfigService();
            configService.GetConfig().EngineRootFolder = @"E:\Engine";
            var service = new SettingsManagerService(configService);

            Assert.Equal(@"E:\Engine", service.EngineRootFolder);
        }

        [Fact]
        public void EngineRootFolder_Setter_PersistsToConfig()
        {
            var configService = new FakeConfigService();
            var service = new SettingsManagerService(configService);

            service.EngineRootFolder = @"E:\Engine";

            Assert.Equal(@"E:\Engine", configService.GetConfig().EngineRootFolder);
            Assert.Equal(@"E:\Engine", service.EngineRootFolder);
        }

        [Fact]
        public async Task ResetToDefaultAsync_RestoresDefaultEngineRoot()
        {
            var configService = new FakeConfigService();
            var service = new SettingsManagerService(configService);

            service.EngineRootFolder = @"E:\Engine";

            await service.ResetToDefaultAsync();

            var expected = Path.GetFullPath(@".\Engine", AppContext.BaseDirectory);

            Assert.Equal(expected, service.EngineRootFolder);
        }

        [Fact]
        public async Task SaveSettingsAsync_ThrowsIOException_WhenSaveConfigFails()
        {
            var configService = new FakeConfigService { ShouldFailSave = true };
            var service = new SettingsManagerService(configService);

            await Assert.ThrowsAsync<IOException>(() =>
                service.SaveSettingsAsync("Light", "D:\\Downloads", false, false));
        }

        [Fact]
        public void SaveSettings_Synchronous_PersistsAllSettingsInSingleWrite()
        {
            var configService = new FakeConfigService();
            var service = new SettingsManagerService(configService);

            var result = service.SaveSettings("Light", @"D:\Downloads", false, false, @"D:\Models", @"D:\Engine");

            Assert.True(result);
            Assert.Equal(1, configService.SaveCount);
            Assert.Equal("Light", service.GetTheme());
            Assert.Equal(@"D:\Downloads", service.GetDownloadsPath());
            Assert.Equal(@"D:\Models", configService.GetConfig().ModelRootFolder);
            Assert.Equal(@"D:\Engine", configService.GetConfig().EngineRootFolder);
            Assert.False(service.GetConfirmExit());
            Assert.False(service.GetConfirmModule());
        }

        [Fact]
        public async Task SaveSettingsAsync_WithRoots_PersistsAllSettingsInSingleWrite()
        {
            var configService = new FakeConfigService();
            var service = new SettingsManagerService(configService);

            await service.SaveSettingsAsync("Light", @"D:\Downloads", false, false, @"D:\Models", @"D:\Engine");

            Assert.Equal(1, configService.SaveCount);
            Assert.Equal("Light", service.GetTheme());
            Assert.Equal(@"D:\Downloads", service.GetDownloadsPath());
            Assert.Equal(@"D:\Models", configService.GetConfig().ModelRootFolder);
            Assert.Equal(@"D:\Engine", configService.GetConfig().EngineRootFolder);
        }
    }
}
