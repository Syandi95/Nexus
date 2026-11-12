using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Xml.Linq;
using Nexus.App;
using Nexus.App.ViewModels;
using Nexus.App.Services;
using Nexus.Contracts;

namespace Nexus.Integration.Tests;

public sealed class NavigationLifecycleTests
{
    [Fact]
    public void DialogCard_CyclesTabWithinTheExistingActionContract()
    {
        var document = XDocument.Load(FindAppFile("MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var dialogCard = document
            .Descendants(presentation + "Border")
            .Single(element => (string?)element.Attribute(xaml + "Name") == "DialogCard");

        Assert.Equal("Cycle", (string?)dialogCard.Attribute("KeyboardNavigation.TabNavigation"));

        var actions = dialogCard.Descendants(presentation + "Button").ToList();
        Assert.Equal(3, actions.Count);
        Assert.Equal("{Binding DialogCancelCommand}", (string?)actions[0].Attribute("Command"));
        Assert.Equal("DialogSecondaryButton", (string?)actions[1].Attribute(xaml + "Name"));
        Assert.Equal(
            "{Binding IsDialogSecondaryVisible, Converter={StaticResource BooleanToVisibilityConverter}}",
            (string?)actions[1].Attribute("Visibility"));
        Assert.Equal("DialogPrimaryButton", (string?)actions[2].Attribute(xaml + "Name"));

        var overlay = document
            .Descendants(presentation + "Border")
            .Single(element => (string?)element.Attribute(xaml + "Name") == "DialogOverlay");
        Assert.Equal("DialogOverlay_PreviewMouseDown", (string?)overlay.Attribute("PreviewMouseDown"));
        Assert.Equal("DialogOverlay_PreviewKeyDown", (string?)overlay.Attribute("PreviewKeyDown"));
    }

    [Fact]
    public void DialogVisibilityLifecycle_CapturesPrimaryFocusAndRestoresBeforeReopen()
    {
        var focusField = typeof(MainWindow).GetField(
            "_dialogFocusBeforeOpen",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(focusField);
        Assert.Equal(typeof(IInputElement), focusField!.FieldType);

        var source = File.ReadAllText(FindAppFile("MainWindow.xaml.cs"));
        var lifecycle = ExtractMethodBody(source, "DialogOverlay_IsVisibleChanged");
        var keyboardCapture = lifecycle.IndexOf("Keyboard.FocusedElement", StringComparison.Ordinal);
        var clearSavedFocus = lifecycle.IndexOf("_dialogFocusBeforeOpen = null", StringComparison.Ordinal);
        var restoreFocus = lifecycle.IndexOf("Keyboard.Focus(", StringComparison.Ordinal);
        var primaryFocus = lifecycle.IndexOf("DialogPrimaryButton.Focus()", StringComparison.Ordinal);

        Assert.True(keyboardCapture >= 0, "Dialog open must capture Keyboard.FocusedElement.");
        Assert.True(primaryFocus > keyboardCapture, "Primary focus must remain after focus capture.");
        Assert.True(clearSavedFocus >= 0, "Dialog close must clear saved focus before a possible reopen.");
        Assert.True(restoreFocus > clearSavedFocus, "Saved focus must be cleared before focus is restored.");

        var keyHandler = ExtractMethodBody(source, "DialogOverlay_PreviewKeyDown");
        Assert.Contains("Key.Escape", keyHandler, StringComparison.Ordinal);
        Assert.Contains("Key.Enter", keyHandler, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransitionLifecycleAsync_LeavesBeforeActivationAndEntersAfter()
    {
        var events = new List<string>();
        var previous = new LifecycleViewModel(
            onEnter: () => events.Add("previous-enter"),
            onLeave: () => events.Add("previous-leave"));
        var next = new LifecycleViewModel(
            onEnter: () => events.Add("next-enter"),
            onLeave: () => events.Add("next-leave"));

        await MainViewModel.TransitionLifecycleAsync(
            previous,
            next,
            () => events.Add("activate-next"));

        Assert.Equal(
            ["previous-leave", "activate-next", "next-enter"],
            events);
    }

    [Fact]
    public async Task ImageSubsystem_OnNavigatedFrom_UnloadsGenericImageEngine()
    {
        var engine = new Moq.Mock<IGenericImageEngine>();
        var viewModel = CreateImageSubsystemViewModel(engine.Object);

        await viewModel.OnNavigatedFromAsync();

        engine.Verify(
            item => item.UnloadAllModelsAsync(),
            Moq.Times.Once);
    }

    private static ImageSubsystemViewModel CreateImageSubsystemViewModel(
        IGenericImageEngine engine)
    {
        var settings = new Moq.Mock<Nexus.Core.HomeSettings.Services.ISettingsManagerService>();
        settings
            .Setup(item => item.ModelRootFolder)
            .Returns(Path.Combine(Path.GetTempPath(), "NexusTest", "Models"));
        return new(
            Moq.Mock.Of<IImageSubsystemWorkflow>(),
            engine,
            Moq.Mock.Of<Nexus.Engine.ColorManagement.ICmykProfileManager>(),
            () => Moq.Mock.Of<Nexus.Engine.ColorManagement.IColorManagementService>(),
            settings.Object);
    }

    private static string FindAppFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "03_App",
                "Nexus.App",
                fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate Nexus.App/{fileName}.");
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        var methodStart = source.IndexOf(methodName, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Method {methodName} is missing.");

        var bodyStart = source.IndexOf('{', methodStart);
        Assert.True(bodyStart >= 0, $"Method {methodName} has no body.");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            if (source[index] != '}') continue;

            depth--;
            if (depth == 0)
            {
                return source[bodyStart..(index + 1)];
            }
        }

        throw new InvalidDataException($"Method {methodName} body is incomplete.");
    }

    private sealed class LifecycleViewModel(
        Action onEnter,
        Action onLeave) : ViewModelBase, IModuleLifecycle
    {
        public Task OnNavigatedToAsync()
        {
            onEnter();
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync()
        {
            onLeave();
            return Task.CompletedTask;
        }
    }
}
