using System.Reflection;
using Moq;
using Nexus.App.Services;
using Nexus.Conversation.Manager;
using Nexus.App.ViewModels;
using Nexus.Core.HomeSettings.Services;
using Nexus.Engine.ColorManagement;

namespace Nexus.Integration.Tests;

public class MainViewModelThemeSyncTests
{
    [Fact]
    public async Task ToolbarThemeToggle_SynchronizesSettingsBeforeNavigationAndSave()
    {
        var configService = new FakeConfigService();
        var settingsManager = new SettingsManagerService(configService);
        var settings = new SettingsViewModel(settingsManager, new FakeCmykProfileManager());
        var main = new MainViewModel(
            null!,
            new ConversationViewModel(Mock.Of<IConversationManagerService>(), Mock.Of<IConversationRuntimeWorkflow>()),
            null!,
            null!,
            new Lazy<ImageSubsystemViewModel>(() => null!),
            settings,
            settingsManager);

        await main.ToggleThemeAsync();

        Assert.Equal("Light", settingsManager.GetTheme());
        Assert.True(settings.IsLightTheme);
        Assert.False(settings.IsDarkTheme);
        Assert.False(settings.IsDirty);

        var navigate = typeof(MainViewModel).GetMethod("NavigateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)navigate.Invoke(main, ["Pengaturan"])!;

        Assert.Same(settings, main.CurrentViewModel);
        Assert.True(settings.IsLightTheme);
        Assert.True(settings.SaveChangesSync());
        Assert.Equal("Light", settingsManager.GetTheme());
        Assert.False(settings.IsDirty);
    }

    [Fact]
    public async Task ToolbarThemeToggle_PreservesOtherPendingSettingsEdits()
    {
        var configService = new FakeConfigService();
        var settingsManager = new SettingsManagerService(configService);
        var settings = new SettingsViewModel(settingsManager, new FakeCmykProfileManager());
        settings.ConfirmModule = false;
        var main = new MainViewModel(
            null!,
            new ConversationViewModel(Mock.Of<IConversationManagerService>(), Mock.Of<IConversationRuntimeWorkflow>()),
            null!,
            null!,
            new Lazy<ImageSubsystemViewModel>(() => null!),
            settings,
            settingsManager);

        await main.ToggleThemeAsync();

        Assert.True(settings.IsLightTheme);
        Assert.True(settings.IsDirty);
        Assert.True(settings.SaveChangesSync());
        Assert.Equal("Light", settingsManager.GetTheme());
        Assert.False(settingsManager.GetConfirmModule());
    }
}
