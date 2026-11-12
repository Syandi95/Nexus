using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Moq;
using Nexus.App.Views;
using Nexus.App.ViewModels;
using Nexus.Core.HomeSettings.Services;
using Nexus.FileUtilities.Services;

namespace Nexus.Integration.Tests;

public sealed class FileManagementShellTests
{
    [Fact]
    public void FileManagementView_UsesTwoHeaderOnlyTabsAndOneContentHost()
    {
        var source = File.ReadAllText(FindAppFile("Views", "FileManagementView.xaml"));
        var document = XDocument.Parse(source);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var root = Assert.IsType<XElement>(document.Root);

        Assert.Equal("Nexus.App.Views.FileManagementView", (string?)root.Attribute(xaml + "Class"));
        var selector = Assert.Single(root.Descendants(presentation + "TabControl"));
        Assert.Equal("WorkspaceTabSelector", (string?)selector.Attribute(xaml + "Name"));
        Assert.Null(selector.Attribute("ItemsSource"));

        var tabs = selector.Elements(presentation + "TabItem").ToArray();
        Assert.Equal(["Bersihkan Download", "Rapikan File"], tabs.Select(tab => (string?)tab.Attribute("Header")));
        Assert.All(tabs, tab => Assert.Empty(tab.Elements()));

        var host = Assert.Single(root.Descendants(presentation + "ContentControl"));
        Assert.Equal("FileManagementContentHost", (string?)host.Attribute(xaml + "Name"));
        Assert.DoesNotContain("<views:CleanDownloadsView", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<views:RapikanFileView", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NexusIcon", source, StringComparison.Ordinal);
        Assert.DoesNotContain("+", tabs.Select(tab => (string?)tab.Attribute("Header")));
        Assert.DoesNotContain("×", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FileManagementView_ReusesOneHostAndBothChildViewsWhilePreservingState()
    {
        RunOnSta(() =>
        {
            var clean = CreateCleanDownloadsViewModel();
            var rapikan = CreateRapikanFileViewModel();
            var shell = new FileManagementViewModel(clean, rapikan);
            var view = new FileManagementView { DataContext = shell };
            DrainDispatcher();

            var host = Assert.IsType<ContentControl>(view.FindName("FileManagementContentHost"));
            var selector = Assert.IsType<TabControl>(view.FindName("WorkspaceTabSelector"));
            var originalHost = host;
            var cleanView = Assert.IsType<CleanDownloadsView>(host.Content);
            clean.SelectedFilter = "image";

            selector.SelectedValue = FileManagementTab.RapikanFile;
            DrainDispatcher();
            Assert.Equal(FileManagementTab.RapikanFile, shell.SelectedTab);
            var rapikanView = Assert.IsType<RapikanFileView>(host.Content);
            rapikan.TargetPath = @"C:\StatePreservation";

            shell.SelectedTab = FileManagementTab.CleanDownloads;
            DrainDispatcher();
            Assert.Same(originalHost, view.FindName("FileManagementContentHost"));
            Assert.Same(cleanView, host.Content);
            Assert.Equal("image", clean.SelectedFilter);
            Assert.Equal(@"C:\StatePreservation", rapikan.TargetPath);

            shell.SelectedTab = FileManagementTab.RapikanFile;
            DrainDispatcher();
            Assert.Same(rapikanView, host.Content);
        });
    }

    [Fact]
    public void MainWindow_DeclaresOneSharedFileManagementViewInstance()
    {
        var document = XDocument.Load(FindAppFile("MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var sharedView = Assert.Single(
            document.Descendants(),
            element => element.Name.LocalName == "FileManagementView" &&
                (string?)element.Attribute(xaml + "Key") == "FileManagementViewInstance");
        Assert.NotNull(sharedView);

        var shellTemplate = Assert.Single(
            document.Descendants(presentation + "DataTemplate"),
            template => ((string?)template.Attribute("DataType"))?.Contains(
                "FileManagementViewModel",
                StringComparison.Ordinal) == true);
        Assert.Empty(shellTemplate.Descendants().Where(
            element => element.Name.LocalName == "FileManagementView"));

        var contentReference = Assert.Single(shellTemplate.Descendants(presentation + "ContentControl"));
        Assert.Equal(
            "{StaticResource FileManagementViewInstance}",
            (string?)contentReference.Attribute("Content"));
    }

    [Fact]
    public void GlobalNavigation_ExposesOneFileManagementDestination()
    {
        var document = XDocument.Load(FindAppFile("MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var navigationButtons = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("CommandParameter") is not null)
            .ToArray();

        Assert.Single(navigationButtons, element =>
            (string?)element.Attribute("CommandParameter") == "FileManagement");
        Assert.DoesNotContain(navigationButtons, element =>
            (string?)element.Attribute("CommandParameter") is "CleanDownloads" or "RapikanFile");
        Assert.Single(document.Descendants(presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "Manajemen File");
    }

    [Fact]
    public void GlobalWiring_UsesOnlyFileManagementShellContract()
    {
        var mainViewModelType = typeof(MainViewModel);
        Assert.NotNull(mainViewModelType.GetProperty("FileManagementVM"));
        Assert.Null(mainViewModelType.GetProperty("FileUtilitiesVM"));
        Assert.Null(mainViewModelType.GetProperty("CleanDownloadsVM"));
        Assert.Null(mainViewModelType.GetProperty("RapikanFileVM"));

        var shellConstructor = Assert.Single(typeof(FileManagementViewModel).GetConstructors());
        Assert.Equal(
            [typeof(CleanDownloadsViewModel), typeof(RapikanFileViewModel)],
            shellConstructor.GetParameters().Select(parameter => parameter.ParameterType));

        var mainSource = File.ReadAllText(FindAppFile("ViewModels", "MainViewModel.cs"));
        Assert.Contains("\"FileManagement\" => FileManagementVM", mainSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"CleanDownloads\" =>", mainSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"RapikanFile\" =>", mainSource, StringComparison.Ordinal);

        var appSource = File.ReadAllText(FindAppFile("App.xaml.cs"));
        Assert.Contains("services.AddSingleton<FileManagementViewModel>();", appSource, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<CleanDownloadsViewModel>();", appSource, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<RapikanFileViewModel>();", appSource, StringComparison.Ordinal);
        Assert.DoesNotContain("services.AddSingleton<FileUtilitiesViewModel>();", appSource, StringComparison.Ordinal);

        var viewModelDirectory = Path.GetDirectoryName(
            FindAppFile("ViewModels", "FileManagementViewModel.cs"))!;
        Assert.False(File.Exists(Path.Combine(viewModelDirectory, "FileUtilitiesViewModel.cs")));
    }

    [Fact]
    public void ChildViews_RemoveOnlyDuplicateIdentityAndPreserveWorkflowContracts()
    {
        var cleanSource = File.ReadAllText(FindAppFile("Views", "CleanDownloadsView.xaml"));
        var rapikanSource = File.ReadAllText(FindAppFile("Views", "RapikanFileView.xaml"));

        Assert.DoesNotContain("Text=\"Clean Downloads\"", cleanSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Pindai, filter, dan rapikan folder Downloads Anda",
            cleanSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Rapikan File\"", rapikanSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Rapikan file Anda dengan aman, cepat, dan terstruktur.",
            rapikanSource,
            StringComparison.Ordinal);

        Assert.Contains("ItemsSource=\"{Binding Items}\"", cleanSource, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ExecuteCleanCommand}\"", cleanSource, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding BrowseFolderCommand}\"", rapikanSource, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding StartRapikanCommand}\"", rapikanSource, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding LogEntries}\"", rapikanSource, StringComparison.Ordinal);
    }

    [Fact]
    public void FileManagementView_UsesOneConnectedContainerAroundThePermanentHost()
    {
        var document = XDocument.Load(FindAppFile("Views", "FileManagementView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var container = Assert.Single(
            document.Descendants(presentation + "Border"),
            element => (string?)element.Attribute(xaml + "Name") == "FileManagementContainer");
        Assert.Equal("1", (string?)container.Attribute("Grid.Row"));
        Assert.Equal("0,-1,0,0", (string?)container.Attribute("Margin"));
        Assert.Equal("0,8,8,8", (string?)container.Attribute("CornerRadius"));
        Assert.Equal("{DynamicResource NexusSurfaceBase}", (string?)container.Attribute("Background"));
        Assert.Equal("{DynamicResource NexusStrokeSubtle}", (string?)container.Attribute("BorderBrush"));
        Assert.Equal("1", (string?)container.Attribute("BorderThickness"));

        var host = Assert.Single(container.Descendants(presentation + "ContentControl"));
        Assert.Equal("FileManagementContentHost", (string?)host.Attribute(xaml + "Name"));
        Assert.Empty(document.Descendants(presentation + "ContentControl").Where(
            candidate => !ReferenceEquals(candidate, host)));
    }

    [Fact]
    public void FileManagement_DefaultsToCleanDownloadsWithoutCreatingASecondChild()
    {
        var clean = CreateCleanDownloadsViewModel();
        var rapikan = CreateRapikanFileViewModel();

        var shell = new FileManagementViewModel(clean, rapikan);

        Assert.Equal(FileManagementTab.CleanDownloads, shell.SelectedTab);
        Assert.Same(clean, shell.CleanDownloadsVM);
        Assert.Same(rapikan, shell.RapikanFileVM);
        Assert.Same(clean, shell.ActiveViewModel);
    }

    [Fact]
    public void SelectTab_ReusesInjectedViewModelsAndRaisesOnlySelectorState()
    {
        var clean = CreateCleanDownloadsViewModel();
        var rapikan = CreateRapikanFileViewModel();
        var shell = new FileManagementViewModel(clean, rapikan);
        var changed = new List<string?>();
        shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        shell.SelectTabCommand.Execute(FileManagementTab.RapikanFile);

        Assert.Equal(FileManagementTab.RapikanFile, shell.SelectedTab);
        Assert.Same(rapikan, shell.ActiveViewModel);
        Assert.Equal(
            [
                nameof(FileManagementViewModel.SelectedTab),
                nameof(FileManagementViewModel.ActiveViewModel)
            ],
            changed);
    }

    [Fact]
    public void SelectingTheActiveTab_IsANoOp()
    {
        var shell = new FileManagementViewModel(
            CreateCleanDownloadsViewModel(),
            CreateRapikanFileViewModel());
        var changes = 0;
        shell.PropertyChanged += (_, _) => changes++;

        shell.SelectTabCommand.Execute(FileManagementTab.CleanDownloads);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void SwitchingTabs_PreservesIndependentChildViewModelState()
    {
        var clean = CreateCleanDownloadsViewModel();
        var rapikan = CreateRapikanFileViewModel();
        var shell = new FileManagementViewModel(clean, rapikan);

        clean.SelectedFilter = "image";
        clean.SelectedSortIndex = 2;
        rapikan.TargetPath = @"C:\Test\Target";
        rapikan.LogEntries.Add(new LogEntryModel { Message = "still-running" });

        shell.SelectedTab = FileManagementTab.RapikanFile;
        shell.SelectedTab = FileManagementTab.CleanDownloads;

        Assert.Same(clean, shell.CleanDownloadsVM);
        Assert.Same(rapikan, shell.RapikanFileVM);
        Assert.Equal("image", clean.SelectedFilter);
        Assert.Equal(2, clean.SelectedSortIndex);
        Assert.Equal(@"C:\Test\Target", rapikan.TargetPath);
        Assert.Contains(rapikan.LogEntries, entry => entry.Message == "still-running");
    }

    [Fact]
    public void SwitchingTabs_PreservesChildViewAndVisualStateIdentity()
    {
        RunOnSta(() =>
        {
            var clean = CreateCleanDownloadsViewModel();
            var rapikan = CreateRapikanFileViewModel();
            var shell = new FileManagementViewModel(clean, rapikan);
            var view = new FileManagementView { DataContext = shell };
            DrainDispatcher();

            var host = Assert.IsType<ContentControl>(view.FindName("FileManagementContentHost"));
            var cleanView = Assert.IsType<CleanDownloadsView>(host.Content);
            var list = Assert.IsType<ListView>(cleanView.FindName("LvCleanFiles"));
            var gridView = Assert.IsType<GridView>(list.View);
            var firstColumn = gridView.Columns[0];
            firstColumn.Width = 217;

            shell.SelectedTab = FileManagementTab.RapikanFile;
            DrainDispatcher();
            var rapikanView = Assert.IsType<RapikanFileView>(host.Content);

            shell.SelectedTab = FileManagementTab.CleanDownloads;
            DrainDispatcher();

            Assert.Same(cleanView, host.Content);
            Assert.Same(list, cleanView.FindName("LvCleanFiles"));
            Assert.Same(firstColumn, Assert.IsType<GridView>(list.View).Columns[0]);
            Assert.Equal(217, firstColumn.Width);
            Assert.Same(clean, cleanView.DataContext);
            Assert.Same(rapikan, rapikanView.DataContext);
        });
    }

    [Fact]
    public async Task SwitchingAway_DoesNotCancelRestartOrResetRunningChildOperation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<Dictionary<string, long>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new Mock<IRapikanFileService>();
        service
            .Setup(candidate => candidate.PreScanDirectoryAsync(@"C:\Test\Target"))
            .Returns(() =>
            {
                started.TrySetResult();
                return completion.Task;
            });

        var clean = CreateCleanDownloadsViewModel();
        var rapikan = new RapikanFileViewModel(service.Object);
        var shell = new FileManagementViewModel(clean, rapikan)
        {
            SelectedTab = FileManagementTab.RapikanFile
        };
        var startPreScan = typeof(RapikanFileViewModel).GetMethod(
            "StartPreScanAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(startPreScan);

        var operation = Assert.IsAssignableFrom<Task>(
            startPreScan.Invoke(rapikan, [@"C:\Test\Target"]));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(rapikan.IsBusy);

        shell.SelectedTab = FileManagementTab.CleanDownloads;
        Assert.Same(clean, shell.ActiveViewModel);
        Assert.Same(rapikan, shell.RapikanFileVM);
        service.Verify(
            candidate => candidate.PreScanDirectoryAsync(@"C:\Test\Target"),
            Times.Once);

        completion.SetResult(new Dictionary<string, long>
        {
            ["TotalFile"] = 1,
            ["TotalSize"] = 128,
            ["Images"] = 1
        });
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(rapikan.IsBusy);
        Assert.Equal("Siap", rapikan.PhaseText);
        Assert.Contains(rapikan.LogEntries, entry =>
            entry.Message.Contains("Pre-scan selesai", StringComparison.Ordinal));
        service.Verify(
            candidate => candidate.PreScanDirectoryAsync(@"C:\Test\Target"),
            Times.Once);

        shell.SelectedTab = FileManagementTab.RapikanFile;
        Assert.Same(rapikan, shell.ActiveViewModel);
        Assert.Equal("Siap", rapikan.PhaseText);
    }

    private static CleanDownloadsViewModel CreateCleanDownloadsViewModel()
    {
        var service = new Moq.Mock<ICleanDownloadsService>();
        service
            .Setup(candidate => candidate.ScanDownloadsFolderAsync(Moq.It.IsAny<string>()))
            .ReturnsAsync([]);

        return new CleanDownloadsViewModel(
            service.Object,
            Moq.Mock.Of<ISettingsManagerService>());
    }

    private static RapikanFileViewModel CreateRapikanFileViewModel() =>
        new(Moq.Mock.Of<IRapikanFileService>());

    private static string FindAppFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(
                [current.FullName, "src", "03_App", "Nexus.App", .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var directCandidate = Path.Combine(
                [current.FullName, "Nexus.App", .. segments]);
            if (File.Exists(directCandidate))
            {
                return directCandidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(string.Join('/', segments));
    }

    private static void RunOnSta(Action assertion)
    {
        WpfAppHost.Run(assertion);
    }

    private static void DrainDispatcher() =>
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
            static () => { },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
}
