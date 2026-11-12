# Manajemen File Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Consolidate the two top-level sidebar destinations into one `Manajemen File` module with Connected Desktop Tabs selecting one cached child View inside one permanent Content Host, while preserving both workflows and all frozen state, lifecycle, dependency, naming, and UX contracts.

**Architecture:** Rename only the existing UI shell from `FileUtilities` to `FileManagement`. `FileManagementViewModel` owns `SelectedTab`, `ActiveViewModel`, and `SelectTabCommand`, while `FileManagementView` owns exactly one permanent `ContentControl` and two cached child View instances. `CleanDownloadsView`, `RapikanFileView`, their ViewModels, Services, Models, project, namespace, tests, and storage contracts remain independent and retain their existing names.

**Tech Stack:** .NET 8, C# 12, WPF, XAML, Microsoft.Extensions.DependencyInjection, xUnit, Moq.

## Global Constraints

- Baseline status is `FINAL`; Source of Truth is `ACTIVE`; scope is `FROZEN`; Design Phase is `CLOSED`.
- Do not reopen design or introduce a new contract unless a demonstrated technical blocker makes a frozen contract impossible.
- If a blocker occurs, stop the affected task and report: impacted contract, technical cause, evidence, root-cause analysis, alternatives with trade-offs, and recommendation. Do not work around the contract silently.
- Sidebar exposes exactly one module named `Manajemen File`.
- Connected Desktop Tabs are a selector only. They are not document tabs and do not own content panels.
- Tabs contain text only: `Bersihkan Download` and `Rapikan File`. No icon, number, badge, dropdown, `+`, `×`, close, add, reorder, drag, or context-menu behavior.
- Exactly one permanent Content Host owns the entire area below the selector. Its position, size, margin, padding, Grid placement, alignment, and shell layout do not change when selection changes.
- Selection changes only the cached Active View rendered inside that one Content Host.
- `SelectedTab` is the selector state. Do not introduce `SelectedWorkspace` or `FileManagementWorkspace`.
- `FileManagementViewModel` may depend on the two child ViewModels, never on their Services.
- Child Views, ViewModels, Services, and workflows do not reference one another directly.
- The two child View and child ViewModel instances are reused for the application session; tab changes never recreate them.
- Switching tabs never invokes global navigation lifecycle, opens a Window or Dialog, cancels work, resets state, or starts work.
- An operation continues in the background when its View is inactive; returning shows its current state.
- Preserve child bindings, commands, business behavior, Services, Models, project and namespace names, test names, log/storage paths, manifest paths, and Undo compatibility.
- Rename only shell and navigation identities that actually changed.
- Remove only the large child headings and their descriptive subtitles; retain operational instructions, warnings, confirmation behavior, and workflow microcopy.
- Add no package or external dependency.
- Make no unrelated cleanup, refactor, feature, optimization, or formatting sweep.
- The dirty worktree belongs to the user. Before every task, inspect the exact target diff and preserve unrelated changes.
- Use project-scoped Git commands (`git ... -- <paths>`) because this checkout has existing unrelated changes.
- Do not claim completion until focused tests, full regression tests, build, runtime/visual acceptance, and Baseline Compliance Audit pass with no new warnings.

## Frozen Names and Interfaces

```csharp
namespace Nexus.App.ViewModels;

public enum FileManagementTab
{
    CleanDownloads,
    RapikanFile
}

public sealed class FileManagementViewModel : ViewModelBase
{
    public FileManagementTab SelectedTab { get; set; }
    public ViewModelBase ActiveViewModel { get; }
    public CleanDownloadsViewModel CleanDownloadsVM { get; }
    public RapikanFileViewModel RapikanFileVM { get; }
    public ICommand SelectTabCommand { get; }
}
```

`FileManagementTab` is a shell-local selector type, not a domain type or a new workspace abstraction. `ActiveViewModel` always returns one of the two injected singleton child ViewModels.

## File Map

### Rename

- `src/03_App/Nexus.App/Views/FileUtilitiesView.xaml` -> `src/03_App/Nexus.App/Views/FileManagementView.xaml`
- `src/03_App/Nexus.App/Views/FileUtilitiesView.xaml.cs` -> `src/03_App/Nexus.App/Views/FileManagementView.xaml.cs`
- Add `src/03_App/Nexus.App/ViewModels/FileManagementViewModel.cs` in Task 1.
- Keep `src/03_App/Nexus.App/ViewModels/FileUtilitiesViewModel.cs` temporarily as a compatibility dependency only; Task 4 removes it after DI and global navigation wiring move to `FileManagementViewModel`.

### Modify

- `src/03_App/Nexus.App/MainWindow.xaml` — one cached shell View resource, one shell DataTemplate, and one sidebar destination.
- `src/03_App/Nexus.App/ViewModels/MainViewModel.cs` — one `FileManagementVM` property/constructor dependency and one `FileManagement` route.
- `src/03_App/Nexus.App/App.xaml.cs` — register renamed shell ViewModel; keep child ViewModels and Services unchanged.
- `src/03_App/Nexus.App/Themes/Components/Layout.xaml` — canonical Connected Desktop Tabs selector styles using existing tokens/semantic resources.
- `src/03_App/Nexus.App/Views/CleanDownloadsView.xaml` — remove only the large heading/subtitle region and let the existing body fill the child View.
- `src/03_App/Nexus.App/Views/RapikanFileView.xaml` — remove only the large heading/subtitle region and let the existing body fill the child View.
- `src/03_App/Nexus.Integration.Tests/DesignSystemGovernanceTests.cs` — update only path/style baseline entries genuinely changed by the shell and new canonical selector style.
- `src/03_App/Nexus.Integration.Tests/WorkspaceTabStyleTests.cs` — verify selector resources; existing child foundation coverage remains in `NexusUIFoundationTests.cs`.
- `src/03_App/Nexus.Integration.Tests/NavigationLifecycleTests.cs` — verify one global route and no child lifecycle transition during local selection.
- `src/03_App/Nexus.Integration.Tests/IntegrationSmokeTests.cs` — add shell/child DI registrations only where the test builds its own container.

### Create

- `src/03_App/Nexus.Integration.Tests/FileManagementShellTests.cs` — frozen shell, selector, single-host, cache, state, and dependency-boundary contracts.

### Explicitly Do Not Rename or Modify Unless a Failing Regression Proves It Necessary

- `CleanDownloadsView*`, `RapikanFileView*`
- `CleanDownloadsViewModel`, `RapikanFileViewModel`
- `ICleanDownloadsService`, `CleanDownloadsService`
- `IRapikanFileService`, `RapikanFileService`
- all File Utilities Models
- `Nexus.FileUtilities` and its namespace/project
- File Utilities unit-test file/class names
- `CleanDownloads_Log`, `RapikanFile_Log`, `RapikanFile_Last.log`, manifest/Undo paths
- historical migration documents

---

### Task 1: Freeze the Shell ViewModel Contract and Add the New Shell ViewModel

**Files:**
- Create: `src/03_App/Nexus.App/ViewModels/FileManagementViewModel.cs`
- Create: `src/03_App/Nexus.Integration.Tests/FileManagementShellTests.cs`
- Preserve unchanged: `src/03_App/Nexus.App/ViewModels/FileUtilitiesViewModel.cs` as a compile-time compatibility dependency until Task 4.

**Temporary implementation contract:** `FileUtilitiesViewModel` may exist only as a compatibility dependency during the transition to Task 4. Task 1 must not change its behavior or wiring. Task 4 must delete the class and prove that no references remain.

**Interfaces:**
- Consumes: existing `ViewModelBase`, `RelayCommand<T>`, `CleanDownloadsViewModel`, and `RapikanFileViewModel`.
- Produces: `FileManagementTab`, `FileManagementViewModel.SelectedTab`, `ActiveViewModel`, `CleanDownloadsVM`, `RapikanFileVM`, and `SelectTabCommand` exactly as frozen above.

- [x] **Step 1: Inspect the exact target state and preserve unrelated changes**

Run:

```powershell
git diff -- src/03_App/Nexus.App/ViewModels/FileUtilitiesViewModel.cs src/03_App/Nexus.Integration.Tests
git status --short -- src/03_App/Nexus.App/ViewModels/FileUtilitiesViewModel.cs src/03_App/Nexus.Integration.Tests
```

Expected: record existing user changes; do not overwrite or normalize unrelated test changes.

- [x] **Step 2: Write failing ViewModel contract tests**

Add tests equivalent to:

```csharp
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
    Assert.Contains(nameof(FileManagementViewModel.SelectedTab), changed);
    Assert.Contains(nameof(FileManagementViewModel.ActiveViewModel), changed);
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
```

Use a configured `Moq.Mock<ICleanDownloadsService>` whose `ScanDownloadsFolderAsync` returns a completed empty list, plus `Moq.Mock.Of<IRapikanFileService>()` and `Moq.Mock.Of<ISettingsManagerService>()`, to construct the real child ViewModels. The completed scan task prevents the child constructor's initial refresh from opening a modal error dialog during tests. Do not add a shell Service mock because the shell must not accept Services.

Use these exact helpers in the test class:

```csharp
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

private static void DrainDispatcher() =>
    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
        static () => { },
        System.Windows.Threading.DispatcherPriority.ApplicationIdle);
```

- [x] **Step 3: Run the focused tests and verify RED**

Run:

```powershell
dotnet test src/03_App/Nexus.Integration.Tests/Nexus.Integration.Tests.csproj --filter FullyQualifiedName~FileManagementShellTests --no-restore --configuration Debug
```

Expected: FAIL because `FileManagementViewModel` and `FileManagementTab` do not exist.

- [x] **Step 4: Add the new file and implement the minimal selector state**

Implement only:

```csharp
public enum FileManagementTab
{
    CleanDownloads,
    RapikanFile
}

public sealed class FileManagementViewModel : ViewModelBase
{
    private FileManagementTab _selectedTab = FileManagementTab.CleanDownloads;

    public FileManagementViewModel(
        CleanDownloadsViewModel cleanDownloads,
        RapikanFileViewModel rapikanFile)
    {
        CleanDownloadsVM = cleanDownloads ?? throw new ArgumentNullException(nameof(cleanDownloads));
        RapikanFileVM = rapikanFile ?? throw new ArgumentNullException(nameof(rapikanFile));
        SelectTabCommand = new RelayCommand<FileManagementTab>(tab => SelectedTab = tab);
    }

    public CleanDownloadsViewModel CleanDownloadsVM { get; }
    public RapikanFileViewModel RapikanFileVM { get; }
    public ICommand SelectTabCommand { get; }

    public FileManagementTab SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!SetProperty(ref _selectedTab, value)) return;
            OnPropertyChanged(nameof(ActiveViewModel));
        }
    }

    public ViewModelBase ActiveViewModel => SelectedTab switch
    {
        FileManagementTab.CleanDownloads => CleanDownloadsVM,
        FileManagementTab.RapikanFile => RapikanFileVM,
        _ => CleanDownloadsVM
    };
}
```

Do not inject or reference `ICleanDownloadsService` or `IRapikanFileService` in this shell.

- [x] **Step 5: Run the focused tests and verify GREEN**

Run the Step 3 command.

Expected: PASS; both child object identities remain unchanged.

- [x] **Step 6: Commit this independently reviewable slice**

```powershell
git add -- docs/superpowers/plans/2026-08-03-file-management-shell-implementation.md src/03_App/Nexus.App/ViewModels/FileManagementViewModel.cs src/03_App/Nexus.Integration.Tests/FileManagementShellTests.cs
git commit -m "refactor: define file management shell state"
```

---

### Task 2: Add Canonical Connected Desktop Tabs Visuals

**Files:**
- Modify: `src/03_App/Nexus.App/Themes/Components/Layout.xaml`
- Create: `src/03_App/Nexus.Integration.Tests/WorkspaceTabStyleTests.cs`
- Modify only if governed counts actually change: `src/03_App/Nexus.Integration.Tests/DesignSystemGovernanceTests.cs`

**Interfaces:**
- Consumes: existing Nexus semantic brushes, typography, focus resources, heights `40/44`, spacing, and corner-radius tokens.
- Produces: `NexusWorkspaceTabControlStyle` and `NexusWorkspaceTabItemStyle`. No package or theme-management dependency.

- [x] **Step 1: Write failing resource-load and template-shape tests**

Add focused assertions that load `Themes/Components/Layout.xaml` on an STA thread and require:

```csharp
Assert.True(dictionary.Contains("NexusWorkspaceTabControlStyle"));
Assert.True(dictionary.Contains("NexusWorkspaceTabItemStyle"));

var selectorStyle = Assert.IsType<Style>(dictionary["NexusWorkspaceTabControlStyle"]);
Assert.Equal(typeof(TabControl), selectorStyle.TargetType);

var itemStyle = Assert.IsType<Style>(dictionary["NexusWorkspaceTabItemStyle"]);
Assert.Equal(typeof(TabItem), itemStyle.TargetType);
```

Also inspect the source and reject document-tab affordances:

```csharp
Assert.DoesNotContain("Close", source, StringComparison.OrdinalIgnoreCase);
Assert.DoesNotContain("CanUserAdd", source, StringComparison.Ordinal);
Assert.DoesNotContain("CanUserDelete", source, StringComparison.Ordinal);
Assert.DoesNotContain("AllowDrop=\"True\"", source, StringComparison.Ordinal);
```

- [x] **Step 2: Run focused UI Foundation tests and verify RED**

Run:

```powershell
dotnet test src/03_App/Nexus.Integration.Tests/Nexus.Integration.Tests.csproj --filter "FullyQualifiedName~WorkspaceTabStyleTests.WorkspaceTabStyles" --no-restore --configuration Debug -- RunConfiguration.MaxCpuCount=1
```

Expected: FAIL because the two styles are absent.

- [x] **Step 3: Add the minimal selector-only styles**

In `Layout.xaml`, add:

- A `TabControl` template containing only the horizontal header presenter/tab strip. It must not contain `PART_SelectedContentHost`, a second `ContentPresenter` for selected content, add/close buttons, or document controls.
- A `TabItem` template containing one text `ContentPresenter` inside a top-rounded `Border`.
- Fixed hit-target height of 40–44 px using existing metrics.
- Active state: content-surface background, primary text, semibold weight, top/side border, and bottom edge visually merged with the permanent Content Host.
- Inactive state: tab-strip surface, secondary text, subtle border.
- Hover, pressed, selected, disabled, and `IsKeyboardFocusWithin` triggers using existing semantic/focus resources.
- `KeyboardNavigation.TabNavigation="Local"` and no animation other than an optional existing 100–150 ms color transition; do not add an animation framework.

Do not add new hard-coded hex values. Do not edit Light/Dark theme files if existing semantic brushes can express every state. If they cannot, stop and report the missing semantic contract rather than inventing a color.

- [x] **Step 4: Run focused UI and governance tests**

Run:

```powershell
dotnet test src/03_App/Nexus.Integration.Tests/Nexus.Integration.Tests.csproj --filter "FullyQualifiedName~NexusUIFoundationTests|FullyQualifiedName~DesignSystemGovernanceTests" --no-restore --configuration Debug -- RunConfiguration.MaxCpuCount=1
```

Expected: PASS with no new hard-coded color, primitive-token, module-named style, or common-template violation.

Recorded Task 2 result: the isolated Workspace Tab test passes and `Nexus.App` builds with zero warnings/errors. The combined suite reports 27 PASS / 11 pre-existing failures in unrelated view debt, compatibility typography resources, and legacy WPF Application test lifecycle; the Workspace Tab test remains PASS. `Layout.xaml` is outside the governed module-view debt count, so `DesignSystemGovernanceTests.cs` is not changed.

- [x] **Step 5: Commit the canonical visual slice**

```powershell
git add -- docs/superpowers/plans/2026-08-03-file-management-shell-implementation.md src/03_App/Nexus.App/Themes/Components/Layout.xaml src/03_App/Nexus.Integration.Tests/WorkspaceTabStyleTests.cs
git commit -m "feat: add connected workspace tab styles"
```

---

### Task 3: Rename and Build the Single-Host File Management View

**Files:**
- Rename: `src/03_App/Nexus.App/Views/FileUtilitiesView.xaml` -> `src/03_App/Nexus.App/Views/FileManagementView.xaml`
- Rename: `src/03_App/Nexus.App/Views/FileUtilitiesView.xaml.cs` -> `src/03_App/Nexus.App/Views/FileManagementView.xaml.cs`
- Modify: `src/03_App/Nexus.Integration.Tests/FileManagementShellTests.cs`

**Interfaces:**
- Consumes: Task 1 selector API and Task 2 styles.
- Produces: `FileManagementView`, `WorkspaceTabSelector`, exactly one `FileManagementContentHost`, and two cached child View instances.

- [x] **Step 1: Write failing XAML structure tests**

Load the shell XAML with `XDocument` and assert:

```csharp
Assert.Equal("Nexus.App.Views.FileManagementView", (string?)root.Attribute(xaml + "Class"));
Assert.Single(root.Descendants(presentation + "TabControl"));
Assert.Equal(2, root.Descendants(presentation + "TabItem").Count());
Assert.Single(root.Descendants(presentation + "ContentControl")
    .Where(e => (string?)e.Attribute(xaml + "Name") == "FileManagementContentHost"));
Assert.All(root.Descendants(presentation + "TabItem"), tab => Assert.Empty(tab.Elements()));
```

Assert exact headers `Bersihkan Download` and `Rapikan File`, and reject `+`, multiplication/close glyphs, icon properties, per-tab content elements, nested child Views inside `TabItem`, multiple hosts, and dynamic item sources.

- [x] **Step 2: Write failing runtime identity/state tests on one STA thread**

The test must:

1. Reuse one existing `Application` or create one only if none exists.
2. Construct real child ViewModels with mocked Services.
3. Construct `FileManagementViewModel` and one `FileManagementView`.
4. Assign the shell DataContext.
5. Capture cached `CleanDownloadsView` and `RapikanFileView` references exposed internally for tests or retrieved by resource key.
6. Switch `SelectedTab` Clean -> Rapikan -> Clean.
7. Assert `FileManagementContentHost` is the same object for all transitions.
8. Assert each child View reference is the same object when revisited.
9. Set representative child state before switching and assert it remains after returning.

Representative assertions:

```csharp
var host = Assert.IsType<ContentControl>(view.FindName("FileManagementContentHost"));
var originalHost = host;
var cleanView = Assert.IsType<CleanDownloadsView>(host.Content);
shell.SelectedTab = FileManagementTab.RapikanFile;
DrainDispatcher();
var rapikanView = Assert.IsType<RapikanFileView>(host.Content);
shell.SelectedTab = FileManagementTab.CleanDownloads;
DrainDispatcher();

Assert.Same(originalHost, view.FindName("FileManagementContentHost"));
Assert.Same(cleanView, host.Content);
Assert.NotSame(cleanView, rapikanView);
```

- [x] **Step 3: Run focused tests and verify RED**

Run:

```powershell
dotnet test src/03_App/Nexus.Integration.Tests/Nexus.Integration.Tests.csproj --filter FullyQualifiedName~FileManagementShellTests --no-restore --configuration Debug -- RunConfiguration.MaxCpuCount=1
```

Expected: FAIL because the renamed shell and one-host/cache structure do not exist.

- [x] **Step 4: Rename the shell View and implement only selector plus one host**

Required XAML shape:

```xml
<Grid x:Name="FileManagementRoot">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="*"/>
    </Grid.RowDefinitions>

    <TabControl x:Name="WorkspaceTabSelector"
                Grid.Row="0"
                Style="{StaticResource NexusWorkspaceTabControlStyle}"
                ItemContainerStyle="{StaticResource NexusWorkspaceTabItemStyle}"
                SelectedValuePath="Tag"
                SelectedValue="{Binding SelectedTab, Mode=TwoWay}">
        <TabItem Header="Bersihkan Download"
                 Tag="{x:Static viewmodels:FileManagementTab.CleanDownloads}"/>
        <TabItem Header="Rapikan File"
                 Tag="{x:Static viewmodels:FileManagementTab.RapikanFile}"/>
    </TabControl>

    <ContentControl x:Name="FileManagementContentHost"
                    Grid.Row="1"
                    HorizontalContentAlignment="Stretch"
                    VerticalContentAlignment="Stretch"/>
</Grid>
```

The TabControl template from Task 2 renders headers only. It must not render selected content.

In `FileManagementView.xaml.cs`, cache exactly one `CleanDownloadsView` and one `RapikanFileView` per shell View instance, assign each the matching child ViewModel DataContext, subscribe to shell `PropertyChanged`, update only `FileManagementContentHost.Content`, and detach the previous shell handler on DataContext change. Do not call Services or child commands.

Do not use `new CleanDownloadsView()` or `new RapikanFileView()` inside the selection-change handler. Construction occurs once in the shell View constructor or once through a shared shell resource cache.

- [x] **Step 5: Add the application-session shell reuse contract test**

Add a test against `MainWindow.xaml` requiring one shared `FileManagementView` resource and the shell DataTemplate to reference that instance. The test must fail if the DataTemplate directly constructs `<views:FileManagementView/>` on every activation.

Required shape:

```xml
<views:FileManagementView x:Key="FileManagementViewInstance"/>
<DataTemplate DataType="{x:Type viewmodels:FileManagementViewModel}">
    <ContentControl Content="{StaticResource FileManagementViewInstance}"/>
</DataTemplate>
```

This step adds the failing test only; Task 4 updates `MainWindow.xaml`.

- [x] **Step 6: Run the focused tests**

Run the Step 3 command.

Expected: all shell-local tests PASS; the MainWindow shared-shell test remains RED until Task 4.

Recorded Task 3 result: all 5 shell-local tests PASS, including direct selector-to-ViewModel selection, one permanent host, cached child View identity, and representative state preservation for both workflows. The single MainWindow shared-shell test remains RED exactly as planned for Task 4. `Nexus.App` builds with zero warnings/errors. Standalone Light-theme renders confirm both selector states and unchanged host geometry; child headings remain intentionally until Task 5.

**STOP GATE:** If WPF proves that a cached `UserControl` cannot be safely detached/reused by the one permanent host, or the shared shell resource cannot be safely reattached after global navigation, stop. Report the exact exception, parent/lifetime evidence, impacted reuse contract, and alternatives. Do not silently switch to recreated DataTemplates, multiple hosts, hidden panels, or View objects in a ViewModel.

- [x] **Step 7: Commit the single-host View slice**

```powershell
git add -- docs/superpowers/plans/2026-08-03-file-management-shell-implementation.md src/03_App/Nexus.App/Views/FileManagementView.xaml src/03_App/Nexus.App/Views/FileManagementView.xaml.cs src/03_App/Nexus.Integration.Tests/FileManagementShellTests.cs
git commit -m "feat: add single-host file management view"
```

---

### Task 4: Consolidate Global Navigation and Dependency Injection

**Files:**
- Modify: `src/03_App/Nexus.App/MainWindow.xaml`
- Modify: `src/03_App/Nexus.App/ViewModels/MainViewModel.cs`
- Modify: `src/03_App/Nexus.App/App.xaml.cs`
- Delete: `src/03_App/Nexus.App/ViewModels/FileUtilitiesViewModel.cs` after all wiring has moved to `FileManagementViewModel`.
- Modify: `src/03_App/Nexus.Integration.Tests/FileManagementShellTests.cs`
- Modify: `src/03_App/Nexus.Integration.Tests/NavigationLifecycleTests.cs`
- Modify where it builds an independent container: `src/03_App/Nexus.Integration.Tests/IntegrationSmokeTests.cs`

**Interfaces:**
- Consumes: Tasks 1 and 3 shell types.
- Produces: one `FileManagement` global route and one `Manajemen File` sidebar destination.

- [ ] **Step 1: Write failing navigation and DI tests**

Add tests requiring:

```csharp
Assert.DoesNotContain("CommandParameter=\"CleanDownloads\"", mainWindowSource);
Assert.DoesNotContain("CommandParameter=\"RapikanFile\"", mainWindowSource);
Assert.Contains("CommandParameter=\"FileManagement\"", mainWindowSource);
var sidebarLabels = mainWindowDocument
    .Descendants(presentation + "TextBlock")
    .Count(element => (string?)element.Attribute("Text") == "Manajemen File");
Assert.Equal(1, sidebarLabels);
```

Use reflection/source inspection to require:

- `MainViewModel.FileManagementVM` exists.
- `MainViewModel` no longer has top-level `CleanDownloadsVM`, `RapikanFileVM`, or `FileUtilitiesVM` properties.
- the route switch maps only `"FileManagement" => FileManagementVM` for these workflows.
- `App` registers `FileManagementViewModel`, `CleanDownloadsViewModel`, and `RapikanFileViewModel` as singleton.
- the shell constructor depends on child ViewModels, not Services.
- `MainWindow.xaml` uses the one shared shell View resource required by Task 3.

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet test src/03_App/Nexus.Integration.Tests/Nexus.Integration.Tests.csproj --filter "FullyQualifiedName~FileManagementShellTests|FullyQualifiedName~NavigationLifecycleTests|FullyQualifiedName~IntegrationSmokeTests" --no-restore --configuration Debug -- RunConfiguration.MaxCpuCount=1
```

Expected: FAIL on the old registrations, properties, routes, templates, and sidebar entries.

- [ ] **Step 3: Update DI minimally**

Replace only:

```csharp
services.AddSingleton<FileUtilitiesViewModel>();
```

with:

```csharp
services.AddSingleton<FileManagementViewModel>();
```

Keep child ViewModel and Service registrations unchanged and singleton.

- [ ] **Step 4: Update MainViewModel minimally**

- Replace the three top-level properties `FileUtilitiesVM`, `CleanDownloadsVM`, and `RapikanFileVM` with `FileManagementVM`.
- Replace the three constructor parameters with one `FileManagementViewModel fileManagement` parameter.
- Replace the two route arms with `"FileManagement" => FileManagementVM`.
- Do not alter `_navigationLock`, Settings dirty-state handling, dialog behavior, theme behavior, or `TransitionLifecycleAsync`.

- [ ] **Step 5: Update MainWindow shell template and sidebar**

- Remove global DataTemplates for `CleanDownloadsViewModel` and `RapikanFileViewModel`.
- Add the shared `FileManagementViewInstance` and one shell DataTemplate.
- Replace the two sidebar items with one item named `Manajemen File`, tag/parameter `FileManagement`, and one existing suitable file-management glyph if already available.
- Do not put icons in Connected Desktop Tabs.
- Keep all other sidebar positions, styles, widths, title bar, dialogs, and content host unchanged.

If no suitable existing sidebar glyph exists, use an already-approved generic folder/file glyph. Do not create an icon-system feature or rename child glyph keys.

- [ ] **Step 6: Update only affected test composition roots**

Where an integration test registers ViewModels itself, add the three existing child/shell ViewModels with their real singleton lifetimes. Do not rename test files/classes or File Utilities Services.

- [ ] **Step 7: Run focused tests and verify GREEN**

Run the Step 2 command.

Expected: PASS. Global navigation transitions to the shell once; tab changes do not call `MainViewModel.NavigateCommand` or module lifecycle.

- [ ] **Step 8: Commit navigation consolidation**

```powershell
git add -- src/03_App/Nexus.App/MainWindow.xaml src/03_App/Nexus.App/ViewModels/MainViewModel.cs src/03_App/Nexus.App/App.xaml.cs src/03_App/Nexus.Integration.Tests/FileManagementShellTests.cs src/03_App/Nexus.Integration.Tests/NavigationLifecycleTests.cs src/03_App/Nexus.Integration.Tests/IntegrationSmokeTests.cs
git commit -m "refactor: consolidate file management navigation"
```

---

### Task 5: Remove Duplicate Child Headings Without Changing Workflow Layout

**Files:**
- Modify: `src/03_App/Nexus.App/Views/CleanDownloadsView.xaml`
- Modify: `src/03_App/Nexus.App/Views/RapikanFileView.xaml`
- Modify: `src/03_App/Nexus.Integration.Tests/FileManagementShellTests.cs`

**Interfaces:**
- Consumes: permanent shell selector identity.
- Produces: child Views without duplicated page identity; all workflow controls and bindings remain unchanged.

- [ ] **Step 1: Write failing source-contract tests**

Require:

```csharp
Assert.DoesNotContain("Text=\"Clean Downloads\"", cleanSource);
Assert.DoesNotContain("Pindai, filter, dan rapikan folder Downloads Anda", cleanSource);
Assert.DoesNotContain("Text=\"Rapikan File\"", rapikanSource);
Assert.DoesNotContain("Rapikan file Anda dengan aman, cepat, dan terstruktur.", rapikanSource);
```

Also assert representative workflow bindings remain:

```csharp
Assert.Contains("ItemsSource=\"{Binding Items}\"", cleanSource);
Assert.Contains("Command=\"{Binding ExecuteCleanCommand}\"", cleanSource);
Assert.Contains("Command=\"{Binding BrowseFolderCommand}\"", rapikanSource);
Assert.Contains("Command=\"{Binding StartRapikanCommand}\"", rapikanSource);
Assert.Contains("ItemsSource=\"{Binding LogEntries}\"", rapikanSource);
```

- [ ] **Step 2: Run focused tests and verify RED**

Run the FileManagementShellTests command from Task 3.

Expected: FAIL because headings/subtitles still exist.

- [ ] **Step 3: Remove only the heading/subtitle elements and reclaim their existing space**

- Remove `CleanHeader` and `CleanSubtitle` only; preserve dashboard, toolbar, filter, list, preview, progress, margins internal to workflow, bindings, names, and commands.
- Remove `RapikanHeader` and `RapikanSubtitle` only; preserve folder picker, action panel, statistics, log, progress footer, bindings, names, and commands.
- Adjust only the immediate row/margin previously dedicated to those headings so the workflow body fills the child View.
- Do not standardize the two child layouts or introduce a common body panel.

- [ ] **Step 4: Run focused shell, UI Foundation, and governance tests**

```powershell
dotnet test src/03_App/Nexus.Integration.Tests/Nexus.Integration.Tests.csproj --filter "FullyQualifiedName~FileManagementShellTests|FullyQualifiedName~NexusUIFoundationTests|FullyQualifiedName~DesignSystemGovernanceTests" --no-restore --configuration Debug -- RunConfiguration.MaxCpuCount=1
```

Expected: PASS; no child binding or style contract changes.

- [ ] **Step 5: Commit the heading-only slice**

```powershell
git add -- src/03_App/Nexus.App/Views/CleanDownloadsView.xaml src/03_App/Nexus.App/Views/RapikanFileView.xaml src/03_App/Nexus.Integration.Tests/FileManagementShellTests.cs
git commit -m "refactor: remove duplicate file workflow headings"
```

---

### Task 6: Prove State Preservation and Background Operation Isolation

**Files:**
- Modify: `src/03_App/Nexus.Integration.Tests/FileManagementShellTests.cs`
- Modify production files only if a test exposes a baseline defect, and only within the previously named shell files.

**Interfaces:**
- Consumes: cached child instances and singleton child ViewModels.
- Produces: executable proof that selection changes neither reset state nor cancel/restart child work.

- [ ] **Step 1: Add ViewModel state-preservation tests**

Before selection changes, set representative existing state:

```csharp
clean.SelectedFilter = "image";
clean.SelectedSortIndex = 2;
rapikan.TargetPath = @"C:\Test\Target";
rapikan.LogEntries.Add(new LogEntryModel { Message = "still-running" });

shell.SelectedTab = FileManagementTab.RapikanFile;
shell.SelectedTab = FileManagementTab.CleanDownloads;

Assert.Equal("image", clean.SelectedFilter);
Assert.Equal(2, clean.SelectedSortIndex);
Assert.Equal(@"C:\Test\Target", rapikan.TargetPath);
Assert.Contains(rapikan.LogEntries, item => item.Message == "still-running");
```

Use only state already exposed by the child ViewModels. Do not add mirror properties to the shell.

- [ ] **Step 2: Add visual-state identity tests**

On one STA thread:

- capture the Clean Downloads `ListView`, its owning child View, and relevant column/splitter state;
- switch to Rapikan File and back;
- assert the child View and named visual elements are the same instances;
- set and verify a non-default splitter/column width or scroll offset after dispatcher layout.

This verifies actual View reuse, not merely ViewModel reuse.

- [ ] **Step 3: Add background-operation isolation tests**

Use controllable mock Services with `TaskCompletionSource`:

1. Start an existing child command or directly exercise its async operation through the established command/test seam.
2. Wait until the Service reports it started.
3. Change `shell.SelectedTab`.
4. Assert the Service task was not cancelled, invoked again, or disposed.
5. Report progress while inactive.
6. Return to the original tab and assert the same ViewModel exposes updated progress/state.

Do not add a cancellation token, background coordinator, shell progress mirror, or new lifecycle interface solely for this test.

- [ ] **Step 4: Run focused tests and classify any failure**

Run:

```powershell
dotnet test src/03_App/Nexus.Integration.Tests/Nexus.Integration.Tests.csproj --filter FullyQualifiedName~FileManagementShellTests --no-restore --configuration Debug -- RunConfiguration.MaxCpuCount=1
```

Expected: PASS without production changes beyond Tasks 1–5.

If a child workflow itself resets state or cannot continue while its View is inactive, stop and file a baseline-defect report before modifying that child ViewModel. The frozen implementation scope does not authorize speculative child workflow refactoring.

- [ ] **Step 5: Commit test evidence**

```powershell
git add -- src/03_App/Nexus.Integration.Tests/FileManagementShellTests.cs
git commit -m "test: prove file management state preservation"
```

---

### Task 7: Full Verification, Visual Acceptance, and Baseline Compliance Audit

**Files:**
- Review only: all files changed in Tasks 1–6.
- Do not create a new design or alter baseline documents.

**Interfaces:**
- Consumes: completed implementation.
- Produces: evidence-backed PASS report or a defect list; never an unverified completion claim.

- [ ] **Step 1: Audit the exact scoped diff**

Run:

```powershell
git status --short -- src/03_App/Nexus.App src/03_App/Nexus.Integration.Tests
git diff -- src/03_App/Nexus.App/MainWindow.xaml src/03_App/Nexus.App/App.xaml.cs src/03_App/Nexus.App/ViewModels/MainViewModel.cs src/03_App/Nexus.App/ViewModels/FileManagementViewModel.cs src/03_App/Nexus.App/Views/FileManagementView.xaml src/03_App/Nexus.App/Views/FileManagementView.xaml.cs src/03_App/Nexus.App/Views/CleanDownloadsView.xaml src/03_App/Nexus.App/Views/RapikanFileView.xaml src/03_App/Nexus.App/Themes/Components/Layout.xaml src/03_App/Nexus.Integration.Tests/FileManagementShellTests.cs src/03_App/Nexus.Integration.Tests/NavigationLifecycleTests.cs src/03_App/Nexus.Integration.Tests/WorkspaceTabStyleTests.cs src/03_App/Nexus.Integration.Tests/NexusUIFoundationTests.cs src/03_App/Nexus.Integration.Tests/DesignSystemGovernanceTests.cs src/03_App/Nexus.Integration.Tests/IntegrationSmokeTests.cs
```

Check that no unrelated user changes are included in the task commit/diff.

- [ ] **Step 2: Run focused shell/navigation/UI tests serially**

```powershell
dotnet test src/03_App/Nexus.Integration.Tests/Nexus.Integration.Tests.csproj --filter "FullyQualifiedName~FileManagementShellTests|FullyQualifiedName~NavigationLifecycleTests|FullyQualifiedName~NexusUIFoundationTests|FullyQualifiedName~DesignSystemGovernanceTests|FullyQualifiedName~IntegrationSmokeTests" --configuration Release -- RunConfiguration.MaxCpuCount=1
```

Expected: all selected tests PASS; zero hang, crash, or cross-thread failure.

- [ ] **Step 3: Run File Utilities regression tests**

```powershell
dotnet test src/02_Modules/Nexus.FileUtilities.Tests/Nexus.FileUtilities.Tests.csproj --configuration Release
```

Expected: all existing Clean Downloads, Rapikan File, manifest, and metadata tests PASS unchanged.

- [ ] **Step 4: Run the full solution regression suite**

```powershell
dotnet test Nexus.sln --configuration Release -- RunConfiguration.MaxCpuCount=1
```

Expected: PASS. Record any pre-existing failure separately and prove whether the scoped diff caused it.

- [ ] **Step 5: Build Release and compare warning baseline**

```powershell
dotnet build Nexus.sln --configuration Release --no-restore
```

Expected: zero errors and no warning newly introduced by this implementation. Do not claim “zero warnings” if existing environment warnings remain; report exact before/after warning codes and counts.

- [ ] **Step 6: Run production startup smoke**

Start the Release application using the checkout's established hidden-window startup harness. Verify:

- process remains alive through the observation window;
- `Foundation/crash.log` or the active crash log is unchanged;
- MainWindow opens without XAML parse/resource errors;
- no duplicate-parent exception occurs when leaving and returning to Manajemen File.

- [ ] **Step 7: Perform manual visual acceptance in Light and Dark themes**

At standard width and the application minimum/narrow width, verify:

- sidebar contains one `Manajemen File` item;
- tabs are connected desktop tabs, text-only, top-rounded, and not classic Windows document tabs;
- only one tab is active;
- no icon, badge, dropdown, plus, close, drag, reorder, or context-menu affordance exists;
- one Content Host keeps identical bounds, margin, padding, and placement across switches;
- selected tab visually merges with the Content Host;
- click and keyboard selection work;
- focus indication is visible;
- child headings/subtitles are absent;
- workflow controls remain unchanged and usable;
- active operations continue while their View is inactive;
- returning preserves selection, filters, preview, statistics, logs, progress, splitter/column sizes, and scroll position.

Capture screenshots of both selected states in both themes for review evidence. Do not change styling based on personal preference; deviations from the frozen reference are defects.

- [ ] **Step 8: Run Baseline Compliance Audit**

Produce a requirement-by-requirement matrix with `PASS`, `DEFECT`, or `BLOCKED` for:

- one module and one global route;
- one Connected Desktop Tabs selector;
- exactly one permanent Content Host;
- stable host geometry/layout;
- one Active View at a time;
- cached View/ViewModel identity;
- state preservation;
- background operation continuity;
- shell responsibility;
- dependency boundary;
- no child/domain rename;
- no new dependency;
- no feature addition;
- no incidental refactor;
- no UX change beyond baseline;
- build, warnings, regression, startup, and visual acceptance.

If any item is `DEFECT`, report it before fixing it. If any item is `BLOCKED`, report the technical evidence and stop; do not weaken the baseline or tests.

- [ ] **Step 9: Final scoped commit only after every gate passes**

```powershell
git status --short -- .
git log -7 --oneline
```

Do not stage unrelated files. If Tasks 1–6 were committed separately and no compliance repair was needed, no additional code commit is required. If an approved defect repair was necessary, commit only that repair with a message identifying the baseline defect.

## Plan Self-Review Results

- **Spec coverage:** Every frozen architecture, lifecycle, state, dependency, naming, UI, verification, and compliance requirement maps to at least one task and final gate.
- **Scope:** Only shell/navigation identity, selector resources, the two heading/subtitle regions, and directly affected tests are included. Domain, storage, project, namespace, and child workflow renames are excluded.
- **Type consistency:** `FileManagementTab`, `SelectedTab`, `ActiveViewModel`, `CleanDownloadsVM`, `RapikanFileVM`, and `SelectTabCommand` are consistent across tasks.
- **Single-host consistency:** Tests reject per-tab panels, multiple hosts, hidden duplicate hosts, and selected-content presenters in the selector template.
- **Lifecycle consistency:** Local selection never routes through `MainViewModel` or `IModuleLifecycle`; application-session reuse has an explicit test and stop gate.
- **Placeholder scan:** No unresolved placeholder or unspecified test/implementation step remains.
