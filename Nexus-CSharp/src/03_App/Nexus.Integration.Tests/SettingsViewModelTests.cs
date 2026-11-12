using Nexus.App.ViewModels;
using Nexus.Core.HomeSettings.Services;
using Nexus.Engine.ColorManagement;
using Nexus.Foundation.Config;
using Xunit;

namespace Nexus.Integration.Tests
{
    public class SettingsViewModelTests
    {
        [Fact]
        public void IsDirty_StartsFalse()
        {
            var vm = CreateVm();
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void IsDirty_TrueAfterChangingTheme()
        {
            var vm = CreateVm();
            vm.IsLightTheme = true;
            Assert.True(vm.IsDirty);
        }

        [Fact]
        public void IsDirty_TrueAfterChangingDownloadsFolder()
        {
            var vm = CreateVm();
            vm.DownloadsFolder = @"C:\New\Path";
            Assert.True(vm.IsDirty);
        }

        [Fact]
        public void IsDirty_TrueAfterChangingModelRootFolder()
        {
            var vm = CreateVm();
            vm.ModelRootFolder = @"D:\Models";
            Assert.True(vm.IsDirty);
        }

        [Fact]
        public void IsDirty_TrueAfterChangingEngineRootFolder()
        {
            var vm = CreateVm();
            vm.EngineRootFolder = @"D:\Engine";
            Assert.True(vm.IsDirty);
        }

        [Fact]
        public void IsDirty_TrueAfterChangingConfirmExit()
        {
            var vm = CreateVm();
            vm.ConfirmExit = !vm.ConfirmExit;
            Assert.True(vm.IsDirty);
        }

        [Fact]
        public void IsDirty_TrueAfterChangingConfirmModule()
        {
            var vm = CreateVm();
            vm.ConfirmModule = !vm.ConfirmModule;
            Assert.True(vm.IsDirty);
        }

        [Fact]
        public void IsDirty_FalseAfterSave()
        {
            var vm = CreateVm();
            vm.IsLightTheme = true;
            Assert.True(vm.IsDirty);

            vm.SaveChangesSync();
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void IsDirty_FalseAfterSaveAndThemeChangeStillDirty()
        {
            var vm = CreateVm();
            vm.IsLightTheme = true;
            vm.SaveChangesSync();
            Assert.False(vm.IsDirty);

            vm.ConfirmModule = false;
            Assert.True(vm.IsDirty);
        }

        [Fact]
        public void IsDirty_FalseAfterReset()
        {
            var vm = CreateVm();
            vm.IsLightTheme = true;
            vm.ConfirmModule = false;
            Assert.True(vm.IsDirty);

            vm.LoadSettings();
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void SaveChangesSync_ReturnsFalse_WhenDownloadsFolderEmpty()
        {
            var vm = CreateVm();
            vm.DownloadsFolder = "";
            vm.IsLightTheme = true;

            var result = vm.SaveChangesSync();

            Assert.False(result);
            Assert.True(vm.IsDirty);
        }

        [Fact]
        public void SaveChangesSync_ReturnsTrue_AfterSuccessfulSave()
        {
            var vm = CreateVm();
            vm.DownloadsFolder = @"C:\Valid\Path";
            vm.IsLightTheme = true;

            var result = vm.SaveChangesSync();

            Assert.True(result);
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void IsDirty_StaysTrue_AfterFailedSave()
        {
            var vm = CreateVm();
            vm.DownloadsFolder = "";
            vm.IsLightTheme = true;

            vm.SaveChangesSync();

            Assert.True(vm.IsDirty);
        }

        [Fact]
        public void SaveChangesSync_PersistsToConfig()
        {
            var configService = new FakeConfigService();
            var manager = new SettingsManagerService(configService);
            var vm = CreateVm(manager);

            vm.DownloadsFolder = @"D:\Downloads";
            vm.ConfirmExit = false;
            vm.SaveChangesSync();

            Assert.Equal("Dark", configService.GetConfig().Theme);
            Assert.Equal(@"D:\Downloads", configService.GetConfig().DownloadsFolder);
            Assert.False(configService.GetConfig().ConfirmExit);
        }

        [Fact]
        public void SaveChangesSync_PersistsModelRootFolder()
        {
            var configService = new FakeConfigService();
            var manager = new SettingsManagerService(configService);
            var vm = CreateVm(manager);

            vm.DownloadsFolder = @"D:\Downloads";
            vm.ModelRootFolder = @"D:\Models";
            vm.SaveChangesSync();

            Assert.Equal(@"D:\Models", configService.GetConfig().ModelRootFolder);
        }

        [Fact]
        public void SaveChangesSync_PersistsEngineRootFolder()
        {
            var configService = new FakeConfigService();
            var manager = new SettingsManagerService(configService);
            var vm = CreateVm(manager);

            vm.DownloadsFolder = @"D:\Downloads";
            vm.EngineRootFolder = @"D:\Engine";
            vm.SaveChangesSync();

            Assert.Equal(@"D:\Engine", configService.GetConfig().EngineRootFolder);
        }

        [Fact]
        public void SaveChangesSync_ReturnsFalse_WhenEngineRootFolderEmpty()
        {
            var vm = CreateVm();
            vm.EngineRootFolder = "";
            vm.IsLightTheme = true;

            var result = vm.SaveChangesSync();

            Assert.False(result);
            Assert.True(vm.IsDirty);
        }

        [Fact]
        public void SaveChangesSync_ReturnsFalse_WhenModelRootFolderEmpty()
        {
            var vm = CreateVm();
            vm.ModelRootFolder = "";
            vm.IsLightTheme = true;

            var result = vm.SaveChangesSync();

            Assert.False(result);
            Assert.True(vm.IsDirty);
        }

        [Fact]
        public void EditTheme_Cancel_RevertsToInitialTheme()
        {
            var configService = new FakeConfigService();
            var manager = new SettingsManagerService(configService);
            var vm = CreateVm(manager);

            Assert.False(vm.IsLightTheme);
            Assert.True(vm.IsDarkTheme);
            Assert.Equal(Nexus.App.Services.ThemeMode.Dark, Nexus.App.Services.ThemeManager.CurrentTheme);

            vm.IsLightTheme = true;
            Assert.True(vm.IsDirty);
            Assert.Equal(Nexus.App.Services.ThemeMode.Light, Nexus.App.Services.ThemeManager.CurrentTheme);

            vm.CancelChanges();

            Assert.False(vm.IsLightTheme);
            Assert.True(vm.IsDarkTheme);
            Assert.Equal(Nexus.App.Services.ThemeMode.Dark, Nexus.App.Services.ThemeManager.CurrentTheme);
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void EditTheme_Discard_RevertsToPersistedTheme()
        {
            var configService = new FakeConfigService();
            var manager = new SettingsManagerService(configService);
            var vm = CreateVm(manager);

            vm.IsLightTheme = true;
            Assert.True(vm.IsDirty);
            Assert.Equal(Nexus.App.Services.ThemeMode.Light, Nexus.App.Services.ThemeManager.CurrentTheme);

            vm.DiscardChanges();

            Assert.False(vm.IsLightTheme);
            Assert.True(vm.IsDarkTheme);
            Assert.Equal(Nexus.App.Services.ThemeMode.Dark, Nexus.App.Services.ThemeManager.CurrentTheme);
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void EditTheme_Save_PersistsStateChanges()
        {
            var configService = new FakeConfigService();
            var manager = new SettingsManagerService(configService);
            var vm = CreateVm(manager);

            vm.DownloadsFolder = @"D:\Downloads";
            vm.IsLightTheme = true;
            var result = vm.SaveChangesSync();

            Assert.True(result);
            Assert.False(vm.IsDirty);
            Assert.Equal("Light", configService.GetConfig().Theme);
            Assert.Equal(Nexus.App.Services.ThemeMode.Light, Nexus.App.Services.ThemeManager.CurrentTheme);
        }

        [Fact]
        public void PersistenceFailure_NotConsideredSuccess()
        {
            var configService = new FakeConfigService();
            var manager = new SettingsManagerService(configService);
            var vm = CreateVm(manager);

            vm.DownloadsFolder = @"D:\Downloads";
            vm.IsLightTheme = true;

            configService.ShouldFailSave = true;

            var result = vm.SaveChangesSync();

            Assert.False(result);
            Assert.True(vm.IsDirty);
        }

        [Fact]
        public void ExistingSettings_NotRelatedToTheme_RemainUnchanged()
        {
            var configService = new FakeConfigService();
            var manager = new SettingsManagerService(configService);
            var vm = CreateVm(manager);

            vm.DownloadsFolder = @"D:\MyDownloads";
            vm.ConfirmExit = false;
            vm.ConfirmModule = true;
            vm.SaveChangesSync();

            vm.IsLightTheme = true;
            Assert.True(vm.IsDirty);

            vm.CancelChanges();

            Assert.False(vm.IsLightTheme);
            Assert.True(vm.IsDarkTheme);
            Assert.Equal(@"D:\MyDownloads", vm.DownloadsFolder);
            Assert.False(vm.ConfirmExit);
            Assert.True(vm.ConfirmModule);
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void SaveChangesSync_MultipleFieldChanges_ExecutesExactlyOnePersistenceOperation()
        {
            var configService = new FakeConfigService();
            var manager = new SettingsManagerService(configService);
            var vm = CreateVm(manager);

            vm.DownloadsFolder = @"D:\Downloads";
            vm.ModelRootFolder = @"D:\Models";
            vm.EngineRootFolder = @"D:\Engine";
            vm.IsLightTheme = true;
            vm.ConfirmExit = false;
            vm.ConfirmModule = false;

            var initialWrites = configService.SaveCount;

            var result = vm.SaveChangesSync();

            Assert.True(result);
            Assert.False(vm.IsDirty);
            Assert.Equal(initialWrites + 1, configService.SaveCount);
            Assert.Equal(@"D:\Downloads", configService.GetConfig().DownloadsFolder);
            Assert.Equal(@"D:\Models", configService.GetConfig().ModelRootFolder);
            Assert.Equal(@"D:\Engine", configService.GetConfig().EngineRootFolder);
            Assert.Equal("Light", configService.GetConfig().Theme);
        }

        [Fact]
        public async Task SaveSettingsAsync_MultipleFieldChanges_ExecutesExactlyOnePersistenceOperation()
        {
            var configService = new FakeConfigService();
            var manager = new SettingsManagerService(configService);
            var vm = CreateVm(manager);

            vm.DownloadsFolder = AppDomain.CurrentDomain.BaseDirectory;
            vm.ModelRootFolder = @"D:\CustomModels";
            vm.EngineRootFolder = @"D:\CustomEngine";
            vm.IsLightTheme = true;

            var initialWrites = configService.SaveCount;

            await vm.SaveSettingsAsync();

            Assert.False(vm.IsDirty);
            Assert.Equal(initialWrites + 1, configService.SaveCount);
            Assert.Equal(AppDomain.CurrentDomain.BaseDirectory, configService.GetConfig().DownloadsFolder);
            Assert.Equal(@"D:\CustomModels", configService.GetConfig().ModelRootFolder);
            Assert.Equal(@"D:\CustomEngine", configService.GetConfig().EngineRootFolder);
        }

        private static SettingsViewModel CreateVm()
        {
            var configService = new FakeConfigService();
            var manager = new SettingsManagerService(configService);
            return CreateVm(manager);
        }

        private static SettingsViewModel CreateVm(ISettingsManagerService manager)
        {
            var cmykFake = new FakeCmykProfileManager();
            return new SettingsViewModel(manager, cmykFake);
        }
    }

    public class FakeConfigService : IConfigService
    {
        private AppConfigRecord _config = new AppConfigRecord();
        public bool ShouldFailSave { get; set; }
        public int SaveCount { get; private set; }

        public AppConfigRecord GetConfig() => _config;

        public bool SaveConfig(AppConfigRecord config)
        {
            SaveCount++;
            if (ShouldFailSave)
            {
                return false;
            }

            _config = config;
            return true;
        }
    }

    public class FakeCmykProfileManager : ICmykProfileManager
    {
        public bool IsCmykAvailable => false;
        public string? ProfileStorePath => null;

        public CmykProfileInfo? GetDefaultProfile() => null;

        public CmykProfileInfo? ValidateProfile(string filePath) => null;

        public IReadOnlyList<CmykProfileInfo> GetBuiltInProfiles()
            => System.Array.Empty<CmykProfileInfo>();

        public IReadOnlyList<CmykProfileInfo> GetCustomProfiles()
            => System.Array.Empty<CmykProfileInfo>();

        public IReadOnlyList<CmykProfileInfo> GetAllProfiles()
            => System.Array.Empty<CmykProfileInfo>();

        public void SetDefaultProfile(string profileId) { }
    }
}
