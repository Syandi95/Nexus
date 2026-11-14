using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Nexus.App.ViewModels;
using System.ComponentModel;
using System.Windows.Documents;
using System.Windows.Media;
using System.Globalization;
using Nexus.App.Services;
using System.Windows.Input;
using System.Windows.Threading;
using Nexus.App.AIWorkspace.Rendering;

namespace Nexus.App.Views;

public partial class AgentWorkspaceView : UserControl
{
    private bool compact;
    private AgentWorkspaceViewModel? observed;
    private Guid? shownPlan;
    private readonly Dictionary<ReviewFileViewModel,(double Vertical,double Horizontal,bool Wrap)> readingPositions=[];
    private ReviewFileViewModel? renderedFile;
    private IReadOnlyList<ReviewFileViewModel>? renderedFiles;
    private bool rendering,awaitingRestore;
    private readonly List<Paragraph> changedBlocks=[];
    private int changeIndex=-1;
    private bool _markdownPreviewMode = true;
    private bool _isStartTabOpen;
    private bool _isCmdTabOpen;
    private bool _isBrowserTabOpen;
    private string _activeSurface = "Document"; // "Document", "Start", "Files", "Terminal", "Browser"
    private string _terminalCwd = System.IO.Directory.Exists(@"D:\NEXUS") ? @"D:\NEXUS" : Environment.CurrentDirectory;
    private readonly List<string> _browserHistory = [];
    private int _browserHistoryIndex = -1;
    private static readonly System.Net.Http.HttpClient SharedBrowserHttp = CreateBrowserHttpClient();

    private static System.Net.Http.HttpClient CreateBrowserHttpClient()
    {
        var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) NexusDesktopBrowser/1.0");
        return client;
    }

    public AgentWorkspaceView()
    {
        InitializeComponent();
        ColoredDiffReview.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(ColoredDiffReview_ScrollChanged), true);
        ColoredDiffReview.SizeChanged += (_, _) => SyncLineNumberGutter();
    }

    private void View_Loaded(object sender,RoutedEventArgs e)
    {
        Observe();
        ThemeManager.ThemeChanged-=Theme_Changed;
        ThemeManager.ThemeChanged+=Theme_Changed;
        EnsureTerminalInitialized();
        EnsureChromeExtensionsToolbarButtonAttached();
        UpdateLayoutMode();
        UpdateSurfacePresentation();
        RenderSelectedDiff();
    }

    private void View_Unloaded(object sender,RoutedEventArgs e)
    {
        ThemeManager.ThemeChanged-=Theme_Changed;
        if(observed is not null)
        {
            observed.PropertyChanged-=Model_Changed;
            observed.BrowserNavigateRequested-=HandleAgentBrowserNavigateAsync;
        }
        observed=null;
    }

    private void View_DataContextChanged(object sender,DependencyPropertyChangedEventArgs e)
    {
        Observe();
        UpdateLayoutMode();
        UpdateSurfacePresentation();
        RenderSelectedDiff();
    }

    private void View_SizeChanged(object sender,SizeChangedEventArgs e)
    {
        UpdateLayoutMode();
        SyncLineNumberGutter();
    }

    private void Observe()
    {
        if(observed is not null)
        {
            observed.PropertyChanged-=Model_Changed;
            observed.BrowserNavigateRequested-=HandleAgentBrowserNavigateAsync;
        }
        observed=DataContext as AgentWorkspaceViewModel;shownPlan=null;
        readingPositions.Clear();renderedFile=null;renderedFiles=null;
        if(observed is not null)
        {
            observed.PropertyChanged+=Model_Changed;
            observed.BrowserNavigateRequested+=HandleAgentBrowserNavigateAsync;
            if (!string.IsNullOrWhiteSpace(observed.LastBrowserUrl) && _activeSurface == "Browser")
            {
                _ = NavigateEmbeddedBrowserAsync(observed.LastBrowserUrl, pushHistory: false);
            }
        }
    }

    private void Model_Changed(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is nameof(AgentWorkspaceViewModel.SelectedReviewFile) or nameof(AgentWorkspaceViewModel.ReviewFiles))
        {
            if (observed?.SelectedReviewFile is not null && _activeSurface != "Document")
            {
                _activeSurface = "Document";
                UpdateSurfacePresentation();
            }
            RenderSelectedDiff();
        }
        if(e.PropertyName!=nameof(AgentWorkspaceViewModel.PendingPlan))return;
        var id=observed?.PendingPlan?.PlanId;
        if(id is not null && id!=shownPlan && observed?.CanDecide==true && compact)CompactTabs.SelectedIndex=1;
        shownPlan=id;
    }

    private void UpdateLayoutMode()
    {
        if(BodyGrid is null)return;
        var next=true;
        if(next!=compact)
        {
            compact=next;
            if(compact && observed?.CanDecide==true)CompactTabs.SelectedIndex=1;
            RequestColumn.MinWidth=compact ? 0 : 320;ReviewColumn.MinWidth=compact ? 0 : 400;
            RequestColumn.Width=new GridLength(1,GridUnitType.Star);
            SplitterColumn.Width=new GridLength(compact ? 0 : 8);
            ReviewColumn.Width=compact ? new GridLength(0) : new GridLength(64,GridUnitType.Star);
            Grid.SetColumn(ReviewPane,compact ? 0 : 2);
        }
        CompactTabs.Visibility=Visibility.Collapsed;
        DesktopSplitter.Visibility=Visibility.Collapsed;
        ApplyTab();
    }

    private void CompactTabs_SelectionChanged(object sender,SelectionChangedEventArgs e) { if(e.Source==CompactTabs)ApplyTab(); }

    private void ApplyTab()
    {
        if(RequestPane is null || ReviewPane is null)return;
        RequestPane.Visibility=Visibility.Collapsed;
        ReviewPane.Visibility=Visibility.Visible;
    }

    private async void SelectWorkspace_Click(object sender,RoutedEventArgs e)
    {
        if(DataContext is not AgentWorkspaceViewModel vm || !vm.CanEdit) return;
        var dialog=new OpenFolderDialog { Title="Pilih proyek" };
        if(dialog.ShowDialog(Window.GetWindow(this))==true) await vm.SelectWorkspaceAsync(dialog.FolderName);
    }

    private void Theme_Changed(object? sender,EventArgs e)
    {
        UpdateSurfacePresentation();
        RenderSelectedDiff();
        SyncWebView2LiveTheme();
    }

    private void SyncWebView2LiveTheme()
    {
        try
        {
            bool isDark = Nexus.App.Services.ThemeManager.CurrentTheme == Nexus.App.Services.ThemeMode.Dark;
            string js = isDark
                ? "if(document.body){document.body.classList.add('dark');}if(document.documentElement){document.documentElement.style.colorScheme='dark';}"
                : "if(document.body){document.body.classList.remove('dark');}if(document.documentElement){document.documentElement.style.colorScheme='light';}";
            _ = ExecuteLiveScriptOnWebView2Async(js);
        }
        catch { }
    }

    private void WrapDiff_Changed(object sender,RoutedEventArgs e)
    {
        if(rendering)return;
        if(renderedFile is not null)
        {
            var old=readingPositions.GetValueOrDefault(renderedFile);
            readingPositions[renderedFile]=(awaitingRestore?old.Vertical:ColoredDiffReview.VerticalOffset,awaitingRestore?old.Horizontal:ColoredDiffReview.HorizontalOffset,WrapDiffToggle.IsChecked!=false);
        }
        RenderSelectedDiff();
    }

    private void CloseReviewTab_MouseDown(object sender,MouseButtonEventArgs e)
    {
        if(sender is Button {DataContext:ReviewFileViewModel file} && observed is not null)
        {
            observed.CloseReviewFileCommand.Execute(file);
            e.Handled=true;
        }
    }

    private void ReviewFileTabs_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeSurface != "Document")
        {
            _activeSurface = "Document";
            UpdateSurfacePresentation();
        }
    }

    private void UpdateSurfacePresentation()
    {
        if (DocumentSurfaceGrid is null) return;
        bool isDoc = _activeSurface == "Document";
        bool isStart = _activeSurface == "Start";
        bool isFiles = _activeSurface == "Files";
        bool isCmd = _activeSurface == "Terminal";
        bool isBrowser = _activeSurface == "Browser";

        DocumentSurfaceGrid.Visibility = isDoc ? Visibility.Visible : Visibility.Collapsed;
        StartGuideSurface.Visibility = isStart ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceFilesSurface.Visibility = isFiles ? Visibility.Visible : Visibility.Collapsed;
        TerminalSurface.Visibility = isCmd ? Visibility.Visible : Visibility.Collapsed;
        BrowserSurface.Visibility = isBrowser ? Visibility.Visible : Visibility.Collapsed;
        DocumentPathToolbar.Visibility = isDoc ? Visibility.Visible : Visibility.Collapsed;

        // Per sidebar-right.md: A pane holds at most one Start (guide) tab, and '+' appears only while pane has none.
        StartTabChip.Visibility = _isStartTabOpen ? Visibility.Visible : Visibility.Collapsed;
        BtnAddStartTab.Visibility = _isStartTabOpen ? Visibility.Collapsed : Visibility.Visible;
        CmdTabChip.Visibility = _isCmdTabOpen ? Visibility.Visible : Visibility.Collapsed;
        BrowserTabChip.Visibility = _isBrowserTabOpen ? Visibility.Visible : Visibility.Collapsed;

        var sunken = TryFindResource("NexusSurfaceSunken") as Brush ?? Brushes.LightGray;
        var primary = TryFindResource("DshTextPrimaryBrush") as Brush ?? Brushes.Black;
        var secondary = TryFindResource("DshTextSecondaryBrush") as Brush ?? Brushes.Gray;

        FilesTabChip.Background = isFiles ? sunken : Brushes.Transparent;
        FilesTabChipText.Foreground = isFiles ? primary : secondary;
        FilesTabChipText.FontWeight = isFiles ? FontWeights.Medium : FontWeights.Normal;

        if (_isStartTabOpen)
        {
            StartTabChip.Background = isStart ? sunken : Brushes.Transparent;
            StartTabChipText.Foreground = isStart ? primary : secondary;
            StartTabChipText.FontWeight = isStart ? FontWeights.Medium : FontWeights.Normal;
        }
        if (_isCmdTabOpen)
        {
            CmdTabChip.Background = isCmd ? sunken : Brushes.Transparent;
            CmdTabChipText.Foreground = isCmd ? primary : secondary;
            CmdTabChipText.FontWeight = isCmd ? FontWeights.Medium : FontWeights.Normal;
        }
        if (_isBrowserTabOpen)
        {
            BrowserTabChip.Background = isBrowser ? sunken : Brushes.Transparent;
            BrowserTabChipText.Foreground = isBrowser ? primary : secondary;
            BrowserTabChipText.FontWeight = isBrowser ? FontWeights.Medium : FontWeights.Normal;
        }

        if (isBrowser)
        {
            EnsureChromeExtensionsToolbarButtonAttached();
        }

        // Prevent Win32 HwndHost (WebView2 / WebBrowser) from bleeding over other tabs or staying in front
        bool showHwnd = isBrowser && _isBrowserTabOpen && (_activeWebView2Control is not null || _fallbackNativeBrowser is not null);
        if (CefBrowserHost is not null)
            CefBrowserHost.Visibility = showHwnd ? Visibility.Visible : Visibility.Collapsed;
        if (_activeWebView2Control is not null)
            _activeWebView2Control.Visibility = showHwnd ? Visibility.Visible : Visibility.Collapsed;
        if (_fallbackNativeBrowser is not null)
            _fallbackNativeBrowser.Visibility = showHwnd ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenStartTab_Click(object sender, RoutedEventArgs e)
    {
        _isStartTabOpen = true;
        _activeSurface = "Start";
        UpdateSurfacePresentation();
    }

    private void StartTabChip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _activeSurface = "Start";
        UpdateSurfacePresentation();
    }

    private void CloseStartTab_Click(object sender, RoutedEventArgs e)
    {
        _isStartTabOpen = false;
        if (_activeSurface == "Start")
        {
            _activeSurface = (observed?.OpenReviewFiles.Count > 0) ? "Document" : "Files";
            if (_activeSurface == "Files") PopulateWorkspaceFilesList();
        }
        UpdateSurfacePresentation();
        e.Handled = true;
    }

    private void FilesTabChip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _activeSurface = "Files";
        PopulateWorkspaceFilesList();
        UpdateSurfacePresentation();
    }

    private void EnsureTerminalInitialized()
    {
        if (TerminalConsoleBox is not null && string.IsNullOrWhiteSpace(TerminalConsoleBox.Text))
        {
            TerminalConsoleBox.Text =
                $"Microsoft Windows [Version {Environment.OSVersion.Version}]\r\n" +
                "(c) Microsoft Corporation. All rights reserved.\r\n\r\n" +
                $"{_terminalCwd}>";
            TerminalConsoleBox.CaretIndex = TerminalConsoleBox.Text?.Length ?? 0;
        }
    }

    private void CmdTabChip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _activeSurface = "Terminal";
        EnsureTerminalInitialized();
        UpdateSurfacePresentation();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            TerminalConsoleBox?.Focus();
            if (TerminalConsoleBox is not null)
                TerminalConsoleBox.CaretIndex = TerminalConsoleBox.Text?.Length ?? 0;
        }));
    }

    private void CloseCmdTab_Click(object sender, RoutedEventArgs e)
    {
        _isCmdTabOpen = false;
        if (_activeSurface == "Terminal")
        {
            _activeSurface = (observed?.OpenReviewFiles.Count > 0) ? "Document" : "Files";
            if (_activeSurface == "Files") PopulateWorkspaceFilesList();
        }
        UpdateSurfacePresentation();
        e.Handled = true;
    }

    private void BrowserTabChip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _activeSurface = "Browser";
        UpdateSurfacePresentation();
    }

    private void CloseBrowserTab_Click(object sender, RoutedEventArgs e)
    {
        _isBrowserTabOpen = false;
        if (CefBrowserHost is not null)
            CefBrowserHost.Visibility = Visibility.Collapsed;
        if (_activeWebView2Control is not null)
            _activeWebView2Control.Visibility = Visibility.Collapsed;
        if (_fallbackNativeBrowser is not null)
            _fallbackNativeBrowser.Visibility = Visibility.Collapsed;

        bool hasOpenDocs = (observed?.OpenReviewFiles.Count ?? 0) > 0;
        if (!hasOpenDocs && !_isStartTabOpen && !_isCmdTabOpen && observed is not null)
        {
            _activeSurface = "Document";
            UpdateSurfacePresentation();
            observed.IsOpen = false;
            e.Handled = true;
            return;
        }

        if (_activeSurface == "Browser")
        {
            _activeSurface = hasOpenDocs ? "Document" : "Files";
            if (_activeSurface == "Files") PopulateWorkspaceFilesList();
        }
        UpdateSurfacePresentation();
        e.Handled = true;
    }

    private void StartOpenWorkspaceFiles_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isStartTabOpen = false;
        _activeSurface = "Files";
        PopulateWorkspaceFilesList();
        UpdateSurfacePresentation();
    }

    private void StartOpenTerminal_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isStartTabOpen = false;
        _isCmdTabOpen = true;
        _activeSurface = "Terminal";
        EnsureTerminalInitialized();
        UpdateSurfacePresentation();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            TerminalConsoleBox?.Focus();
            if (TerminalConsoleBox is not null)
                TerminalConsoleBox.CaretIndex = TerminalConsoleBox.Text?.Length ?? 0;
        }));
    }

    private void StartOpenBrowser_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isStartTabOpen = false;
        _isBrowserTabOpen = true;
        _activeSurface = "Browser";
        UpdateSurfacePresentation();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => BrowserAddressBox?.Focus()));
    }

    private void StartToggleMoreTools_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (MoreToolsDrawer is not null)
        {
            MoreToolsDrawer.Visibility = MoreToolsDrawer.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    private void MoreToolsOpenGuidelines_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isStartTabOpen = false;
        OpenFileInTab(@"D:\NEXUS\AGENTS.md");
        OpenFileInTab(@"D:\NEXUS\GEMINI.md");
        _activeSurface = "Document";
        UpdateSurfacePresentation();
    }

    private void MoreToolsOpenCoreCode_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isStartTabOpen = false;
        string targetPath = @"D:\NEXUS\AGENTS.md";
        if (!System.IO.File.Exists(targetPath))
            targetPath = @"D:\NEXUS\Nexus-CSharp\src\02_Modules\Nexus.AI.Llama\Transport\LlamaTransportHelper.cs";
        OpenSimulatedLiveDiffInTab(targetPath);
        _activeSurface = "Document";
        UpdateSurfacePresentation();
    }

    private void RefreshWorkspaceFiles_Click(object sender, RoutedEventArgs e) => PopulateWorkspaceFilesList();

    private void WorkspaceFilesFilterBox_TextChanged(object sender, TextChangedEventArgs e) => PopulateWorkspaceFilesList();

    private void PopulateWorkspaceFilesList()
    {
        if (WorkspaceFilesListPanel is null) return;
        WorkspaceFilesListPanel.Children.Clear();
        string filter = WorkspaceFilesFilterBox?.Text?.Trim() ?? string.Empty;
        var roots = new[] { @"D:\NEXUS", @"D:\NEXUS\Nexus-CSharp\docs", @"D:\NEXUS\Nexus-CSharp\src\03_App\Nexus.App" };
        var files = new List<string>();
        foreach (var r in roots)
        {
            try
            {
                if (System.IO.Directory.Exists(r))
                {
                    foreach (var f in System.IO.Directory.EnumerateFiles(r, "*.*", System.IO.SearchOption.TopDirectoryOnly))
                        files.Add(f);
                    foreach (var sub in System.IO.Directory.EnumerateDirectories(r))
                    {
                        var name = System.IO.Path.GetFileName(sub);
                        if (name is "bin" or "obj" or ".git" or ".vs") continue;
                        try
                        {
                            foreach (var sf in System.IO.Directory.EnumerateFiles(sub, "*.*", System.IO.SearchOption.TopDirectoryOnly).Take(25))
                                files.Add(sf);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        var secondary = TryFindResource("DshTextSecondaryBrush") as Brush ?? Brushes.Gray;
        var primary = TryFindResource("DshTextPrimaryBrush") as Brush ?? Brushes.Black;
        var hover = TryFindResource("DshSidebarHoverBrush") as Brush ?? Brushes.LightGray;
        var accent = TryFindResource("NexusAccentPrimary") as Brush ?? Brushes.DodgerBlue;
        var onAccent = TryFindResource("NexusForegroundOnAccent") as Brush ?? Brushes.White;

        foreach (var fp in files.Distinct(StringComparer.OrdinalIgnoreCase)
                     .Where(p => string.IsNullOrEmpty(filter) || p.Contains(filter, StringComparison.OrdinalIgnoreCase))
                     .Take(60))
        {
            var ext = System.IO.Path.GetExtension(fp).ToLowerInvariant();
            string badge = ext switch
            {
                ".cs" => "C#",
                ".md" => "MD",
                ".xaml" or ".xml" or ".csproj" or ".sln" => "<>",
                ".json" => "{}",
                ".cmd" or ".bat" or ".ps1" => ">_",
                _ => "TXT"
            };
            var row = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 2),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = fp
            };
            row.MouseEnter += (_, _) => row.Background = hover;
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            var sp = new DockPanel();
            var badgeBorder = new Border
            {
                Width = 18,
                Height = 16,
                CornerRadius = new CornerRadius(3.5),
                Background = accent,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = badge,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = onAccent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            DockPanel.SetDock(badgeBorder, Dock.Left);
            sp.Children.Add(badgeBorder);
            var dirText = new TextBlock
            {
                Text = System.IO.Path.GetDirectoryName(fp)?.Replace(@"D:\NEXUS", "") ?? "",
                FontSize = 11,
                Foreground = secondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            DockPanel.SetDock(dirText, Dock.Right);
            sp.Children.Add(dirText);
            sp.Children.Add(new TextBlock
            {
                Text = System.IO.Path.GetFileName(fp),
                FontSize = 12.5,
                Foreground = primary,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            row.Child = sp;
            string capturedPath = fp;
            row.MouseLeftButtonDown += (_, e) =>
            {
                OpenFileInTab(capturedPath);
                _activeSurface = "Document";
                UpdateSurfacePresentation();
                e.Handled = true;
            };
            WorkspaceFilesListPanel.Children.Add(row);
        }
    }

    private void OpenFileInTab(string fullPath)
    {
        if (observed is null || !System.IO.File.Exists(fullPath)) return;
        try
        {
            var existing = observed.ReviewFiles.ToList();
            var found = existing.FirstOrDefault(x => string.Equals(x.Target, fullPath, StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                var lines = System.IO.File.ReadAllLines(fullPath).Take(160).Select(l => "  " + l);
                found = new ReviewFileViewModel(fullPath, string.Join("\n", lines));
                existing.Add(found);
                var field = typeof(AgentWorkspaceViewModel).GetField("reviewFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                field?.SetValue(observed, existing.AsReadOnly());
            }
            observed.SelectReviewFileCommand.Execute(found);
        }
        catch { }
    }

    private void OpenSimulatedLiveDiffInTab(string fullPath)
    {
        if (observed is null || !System.IO.File.Exists(fullPath)) return;
        try
        {
            var rawLines = System.IO.File.ReadAllLines(fullPath);
            var diffBuilder = new List<string>();
            bool injectedDiff = false;
            for (int i = 0; i < rawLines.Length; i++)
            {
                if (!injectedDiff && !string.IsNullOrWhiteSpace(rawLines[i]) && i >= 1)
                {
                    diffBuilder.Add("- " + rawLines[i]);
                    diffBuilder.Add("+ " + rawLines[i] + " // [Diverifikasi oleh Nexus Live Diff]");
                    injectedDiff = true;
                }
                else
                {
                    diffBuilder.Add("  " + rawLines[i]);
                }
            }
            var diffVm = new ReviewFileViewModel(fullPath, string.Join("\n", diffBuilder));
            var existing = observed.ReviewFiles.Where(x => !string.Equals(x.Target, fullPath, StringComparison.OrdinalIgnoreCase)).ToList();
            existing.Add(diffVm);
            typeof(AgentWorkspaceViewModel).GetField("reviewFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(observed, existing.AsReadOnly());
            var openList = observed.OpenReviewFiles.Where(x => !string.Equals(x.Target, fullPath, StringComparison.OrdinalIgnoreCase)).ToList();
            openList.Add(diffVm);
            typeof(AgentWorkspaceViewModel).GetField("openReviewFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(observed, openList.AsReadOnly());
            observed.SelectReviewFileCommand.Execute(diffVm);
        }
        catch { }
    }

    private void ApplyDiffToDisk_Click(object sender, RoutedEventArgs e)
    {
        var file = observed?.SelectedReviewFile;
        if (file is null || !file.HasDiffChanges || !System.IO.File.Exists(file.Target)) return;
        try
        {
            string backupDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexus-file-backups");
            System.IO.Directory.CreateDirectory(backupDir);
            string backupFile = System.IO.Path.Combine(
                backupDir,
                $"{System.IO.Path.GetFileName(file.Target)}.{DateTime.Now:yyyyMMdd_HHmmss}.bak");
            System.IO.File.Copy(file.Target, backupFile, overwrite: true);

            var updatedLines = file.Lines
                .Where(l => l.Kind != ReviewDiffLineKind.Deleted)
                .Select(l => l.Code)
                .ToArray();
            System.IO.File.WriteAllLines(file.Target, updatedLines);

            ReloadSelectedFile_Click(sender, e);
        }
        catch { }
    }

    private async void TerminalConsoleBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TerminalConsoleBox is null) return;
        string text = TerminalConsoleBox.Text ?? string.Empty;
        int lastPromptIdx = text.LastIndexOf('>');
        int minEditableIdx = lastPromptIdx >= 0 ? lastPromptIdx + 1 : 0;

        if (e.Key == Key.Back && TerminalConsoleBox.CaretIndex <= minEditableIdx && TerminalConsoleBox.SelectionLength == 0)
        {
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Left && TerminalConsoleBox.CaretIndex <= minEditableIdx)
        {
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter) return;

        e.Handled = true;
        string cmd = lastPromptIdx >= 0 && lastPromptIdx + 1 <= text.Length
            ? text[(lastPromptIdx + 1)..].Trim()
            : text.Trim();

        if (string.IsNullOrEmpty(cmd))
        {
            TerminalConsoleBox.AppendText($"\r\n{_terminalCwd}>");
            TerminalConsoleBox.CaretIndex = TerminalConsoleBox.Text?.Length ?? 0;
            TerminalConsoleBox.ScrollToEnd();
            return;
        }

        if (string.Equals(cmd, "cls", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cmd, "clear", StringComparison.OrdinalIgnoreCase))
        {
            TerminalConsoleBox.Text = $"{_terminalCwd}>";
            TerminalConsoleBox.CaretIndex = TerminalConsoleBox.Text?.Length ?? 0;
            return;
        }

        if (cmd.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
        {
            string targetDir = cmd[3..].Trim().Trim('"');
            try
            {
                string resolved = System.IO.Path.GetFullPath(System.IO.Path.Combine(_terminalCwd, targetDir));
                if (System.IO.Directory.Exists(resolved))
                {
                    _terminalCwd = resolved;
                    TerminalConsoleBox.AppendText($"\r\n\r\n{_terminalCwd}>");
                }
                else
                {
                    TerminalConsoleBox.AppendText($"\r\nThe system cannot find the path specified.\r\n\r\n{_terminalCwd}>");
                }
            }
            catch (Exception ex)
            {
                TerminalConsoleBox.AppendText($"\r\n{ex.Message}\r\n\r\n{_terminalCwd}>");
            }
            TerminalConsoleBox.CaretIndex = TerminalConsoleBox.Text?.Length ?? 0;
            TerminalConsoleBox.ScrollToEnd();
            return;
        }

        TerminalConsoleBox.AppendText("\r\n");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c " + cmd)
            {
                WorkingDirectory = _terminalCwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is not null)
            {
                string stdout = await proc.StandardOutput.ReadToEndAsync();
                string stderr = await proc.StandardError.ReadToEndAsync();
                if (!string.IsNullOrEmpty(stdout))
                    TerminalConsoleBox.AppendText(stdout.TrimEnd() + "\r\n");
                if (!string.IsNullOrEmpty(stderr))
                    TerminalConsoleBox.AppendText(stderr.TrimEnd() + "\r\n");
            }
        }
        catch (Exception ex)
        {
            TerminalConsoleBox.AppendText(ex.Message + "\r\n");
        }
        TerminalConsoleBox.AppendText($"\r\n{_terminalCwd}>");
        TerminalConsoleBox.CaretIndex = TerminalConsoleBox.Text?.Length ?? 0;
        TerminalConsoleBox.ScrollToEnd();
    }

    private void BrowserAddressBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (BrowserAddressWatermark is not null && BrowserAddressBox is not null)
        {
            BrowserAddressWatermark.Visibility = string.IsNullOrEmpty(BrowserAddressBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void BrowserAddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = NavigateEmbeddedBrowserAsync(BrowserAddressBox?.Text, pushHistory: true);
            e.Handled = true;
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    private static bool _browserEmulationConfigured;
    private static bool _webView2Probed;
    private static System.Reflection.Assembly? _webView2WpfAssembly;
    private static System.Reflection.Assembly? _webView2CoreAssembly;
    private UIElement? _activeWebView2Control;
    private bool _webView2EnvironmentInitialized;
    private WebBrowser? _fallbackNativeBrowser;

    private static void ConfigureWindowsBrowserEmulation()
    {
        if (_browserEmulationConfigured) return;
        _browserEmulationConfigured = true;
        try
        {
            string exeName = System.IO.Path.GetFileName(Environment.ProcessPath ?? "Nexus.App.exe");
            using var emulKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION");
            emulKey?.SetValue(exeName, 11001, RegistryValueKind.DWord);
            using var gpuKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_GPU_RENDERING");
            gpuKey?.SetValue(exeName, 1, RegistryValueKind.DWord);
        }
        catch { }
    }

    private static async Task EnsureWebView2AssembliesLoadedAsync()
    {
        if (_webView2WpfAssembly is not null && _webView2CoreAssembly is not null)
            return;

        if (!_webView2Probed)
        {
            _webView2Probed = true;
            if (TryLoadWebView2FromLocalPaths())
                return;
        }

        // Auto-bootstrap official Microsoft.Web.WebView2 NuGet (~600 KB) into nexus_data/webview2_lib if not yet cached
        try
        {
            string rootData = System.IO.Directory.Exists(@"D:\NEXUS")
                ? @"D:\NEXUS\nexus_data"
                : System.IO.Path.Combine(AppContext.BaseDirectory, "nexus_data");
            string libDir = System.IO.Path.Combine(rootData, "webview2_lib");
            System.IO.Directory.CreateDirectory(libDir);

            string targetCore = System.IO.Path.Combine(libDir, "Microsoft.Web.WebView2.Core.dll");
            string targetWpf = System.IO.Path.Combine(libDir, "Microsoft.Web.WebView2.Wpf.dll");
            string targetLoader = System.IO.Path.Combine(libDir, "WebView2Loader.dll");

            if (!System.IO.File.Exists(targetCore) || !System.IO.File.Exists(targetWpf) || !System.IO.File.Exists(targetLoader))
            {
                string nupkgPath = System.IO.Path.Combine(libDir, "microsoft.web.webview2.1.0.2903.40.nupkg");
                if (!System.IO.File.Exists(nupkgPath))
                {
                    byte[] bytes = await SharedBrowserHttp.GetByteArrayAsync(
                        "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.2903.40/microsoft.web.webview2.1.0.2903.40.nupkg");
                    await System.IO.File.WriteAllBytesAsync(nupkgPath, bytes);
                }

                using var fs = System.IO.File.OpenRead(nupkgPath);
                using var archive = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read);
                foreach (var entry in archive.Entries)
                {
                    string? dest = null;
                    if (entry.FullName.Equals("lib/netcoreapp3.0/Microsoft.Web.WebView2.Core.dll", StringComparison.OrdinalIgnoreCase))
                        dest = targetCore;
                    else if (entry.FullName.Equals("lib/netcoreapp3.0/Microsoft.Web.WebView2.Wpf.dll", StringComparison.OrdinalIgnoreCase))
                        dest = targetWpf;
                    else if (entry.FullName.Equals("runtimes/win-x64/native/WebView2Loader.dll", StringComparison.OrdinalIgnoreCase))
                        dest = targetLoader;

                    if (dest is not null)
                    {
                        using var es = entry.Open();
                        using var outStream = System.IO.File.Create(dest);
                        await es.CopyToAsync(outStream);
                    }
                }
            }

            if (System.IO.File.Exists(targetCore) && System.IO.File.Exists(targetWpf) && System.IO.File.Exists(targetLoader))
            {
                SetDllDirectory(libDir);
                _webView2CoreAssembly = System.Reflection.Assembly.LoadFrom(targetCore);
                _webView2WpfAssembly = System.Reflection.Assembly.LoadFrom(targetWpf);
            }
        }
        catch { }
    }

    private static bool TryLoadWebView2FromLocalPaths()
    {
        try
        {
            string baseDir = AppContext.BaseDirectory;
            string[] candidateDirs =
            [
                baseDir,
                System.IO.Path.Combine(baseDir, "nexus_data", "webview2_lib"),
                @"D:\NEXUS\nexus_data\webview2_lib"
            ];

            foreach (var dir in candidateDirs)
            {
                if (!System.IO.Directory.Exists(dir)) continue;
                string localWpf = System.IO.Path.Combine(dir, "Microsoft.Web.WebView2.Wpf.dll");
                string localCore = System.IO.Path.Combine(dir, "Microsoft.Web.WebView2.Core.dll");
                if (System.IO.File.Exists(localWpf) && System.IO.File.Exists(localCore))
                {
                    string localNative = System.IO.Path.Combine(dir, "runtimes", "win-x64", "native");
                    if (System.IO.Directory.Exists(localNative))
                        SetDllDirectory(localNative);
                    else
                        SetDllDirectory(dir);
                    _webView2CoreAssembly = System.Reflection.Assembly.LoadFrom(localCore);
                    _webView2WpfAssembly = System.Reflection.Assembly.LoadFrom(localWpf);
                    return true;
                }
            }

            string nugetRoot = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "packages", "microsoft.web.webview2");
            if (System.IO.Directory.Exists(nugetRoot))
            {
                var latestDir = new System.IO.DirectoryInfo(nugetRoot)
                    .GetDirectories()
                    .OrderByDescending(d => d.Name)
                    .FirstOrDefault();
                if (latestDir is not null)
                {
                    string nativeDir = System.IO.Path.Combine(latestDir.FullName, "runtimes", "win-x64", "native");
                    if (System.IO.Directory.Exists(nativeDir))
                        SetDllDirectory(nativeDir);

                    foreach (var tfm in new[] { "netcoreapp3.0", "net5.0-windows", "net462" })
                    {
                        string candidateWpf = System.IO.Path.Combine(latestDir.FullName, "lib", tfm, "Microsoft.Web.WebView2.Wpf.dll");
                        string candidateCore = System.IO.Path.Combine(latestDir.FullName, "lib", tfm, "Microsoft.Web.WebView2.Core.dll");
                        if (System.IO.File.Exists(candidateWpf) && System.IO.File.Exists(candidateCore))
                        {
                            _webView2CoreAssembly = System.Reflection.Assembly.LoadFrom(candidateCore);
                            _webView2WpfAssembly = System.Reflection.Assembly.LoadFrom(candidateWpf);
                            return true;
                        }
                    }
                }
            }
        }
        catch { }
        return false;
    }

    private async Task<bool> NavigateViaWebView2ChromiumAsync(string targetUrl)
    {
        await EnsureWebView2AssembliesLoadedAsync();
        if (_webView2WpfAssembly is null || _webView2CoreAssembly is null || CefBrowserHost is null)
            return false;

        try
        {
            var wvType = _webView2WpfAssembly.GetType("Microsoft.Web.WebView2.Wpf.WebView2");
            if (wvType is null) return false;

            if (_activeWebView2Control is null)
            {
                _activeWebView2Control = Activator.CreateInstance(wvType) as UIElement;
                if (_activeWebView2Control is null) return false;

                CefBrowserHost.Content = _activeWebView2Control;
            }

            CefBrowserHost.Visibility = Visibility.Visible;
            _activeWebView2Control.Visibility = Visibility.Visible;
            if (BrowserReaderBox is not null)
                BrowserReaderBox.Visibility = Visibility.Collapsed;

            if (!_webView2EnvironmentInitialized)
            {
                string userDataDir = System.IO.Directory.Exists(@"D:\NEXUS")
                    ? @"D:\NEXUS\nexus_data\webview2_profile"
                    : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexus_webview2_profile");
                System.IO.Directory.CreateDirectory(userDataDir);

                var envType = _webView2CoreAssembly.GetType("Microsoft.Web.WebView2.Core.CoreWebView2Environment");
                var optionsType = _webView2CoreAssembly.GetType("Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions");
                var createAsyncMethod = (envType is not null && optionsType is not null)
                    ? envType.GetMethod("CreateAsync", new Type[] { typeof(string), typeof(string), optionsType })
                    : null;

                // CoreWebView2Environment.CreateAsync(null, userDataDir, new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true })
                object? envOptions = null;
                if (optionsType is not null)
                {
                    try
                    {
                        envOptions = Activator.CreateInstance(optionsType);
                        optionsType.GetProperty("AreBrowserExtensionsEnabled")?.SetValue(envOptions, true);
                    }
                    catch { }
                }

                object? envInstance = null;
                if (createAsyncMethod is not null)
                {
                    if (createAsyncMethod.Invoke(null, new object?[] { null, userDataDir, envOptions }) is Task createTask)
                    {
                        await createTask;
                        envInstance = createTask.GetType().GetProperty("Result")?.GetValue(createTask);
                    }
                }

                var ensureMethod = wvType.GetMethods()
                    .FirstOrDefault(m => m.Name == "EnsureCoreWebView2Async" && m.GetParameters().Length == 1);
                if (ensureMethod is not null && envInstance is not null)
                {
                    if (ensureMethod.Invoke(_activeWebView2Control, new object?[] { envInstance }) is Task ensureTask)
                    {
                        await ensureTask;
                    }
                }
                await RegisterExtensionFolderInWebView2ProfileAsync(GetAdGuardUnpackedExtensionFolder());
                    _webView2EnvironmentInitialized = true;
                EnsureNewWindowInterceptorAttached();
            }

            var sourceProp = wvType.GetProperty("Source");
            sourceProp?.SetValue(_activeWebView2Control, new Uri(targetUrl));
            return true;
        }
        catch
        {
            _activeWebView2Control = null;
            _webView2EnvironmentInitialized = false;
            return false;
        }
    }

    private static void SuppressNativeWebBrowserScriptErrors(WebBrowser browser)
    {
        try
        {
            var field = typeof(WebBrowser).GetField("_axIWebBrowser2", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var axObj = field?.GetValue(browser);
            axObj?.GetType().InvokeMember("Silent", System.Reflection.BindingFlags.SetProperty, null, axObj, new object[] { true });
        }
        catch { }
    }

    private void NavigateViaNativeWindowsWebBrowser(string targetUrl)
    {
        if (CefBrowserHost is null) return;
        ConfigureWindowsBrowserEmulation();

        if (_fallbackNativeBrowser is null)
        {
            _fallbackNativeBrowser = new WebBrowser
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _fallbackNativeBrowser.Navigating += (s, e) =>
            {
                if (s is WebBrowser wb)
                    SuppressNativeWebBrowserScriptErrors(wb);
                if (e.Uri is not null && BrowserAddressBox is not null)
                {
                    BrowserAddressBox.Text = e.Uri.ToString();
                    if (observed is not null) observed.LastBrowserUrl = e.Uri.ToString();
                }
            };
            CefBrowserHost.Content = _fallbackNativeBrowser;
        }

        CefBrowserHost.Visibility = Visibility.Visible;
        _fallbackNativeBrowser.Visibility = Visibility.Visible;
        if (BrowserReaderBox is not null)
            BrowserReaderBox.Visibility = Visibility.Collapsed;

        try
        {
            _fallbackNativeBrowser.Navigate(new Uri(targetUrl));
        }
        catch { }
    }

    private async Task<string> HandleAgentBrowserNavigateAsync(string url)
    {
        try { Dispatcher.Invoke(() => { if (!string.IsNullOrWhiteSpace(url) && url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) { string hostTitle = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "Browser"; UpdateActiveBrowserTabInPlace(hostTitle, url); } }); } catch { }
        _isBrowserTabOpen = true;
        _activeSurface = "Browser";
        UpdateSurfacePresentation();
        await NavigateEmbeddedBrowserAsync(url, pushHistory: true);
        string domText = await ExtractLiveWebView2DomTextAsync();
        if (observed is not null && !string.IsNullOrWhiteSpace(domText))
        {
            observed.LastBrowserTitle = domText;
        }
        return domText;
    }

#pragma warning disable CS0649, CS0169
    private System.Windows.UIElement? _adGuardPopupWebView2Control;
    private bool _adGuardPopupWebView2Initialized;

    private async Task TryMountGenuinePopupWebViewInsideFlyoutAsync(System.Windows.Controls.Border hostBorder)
    {
        try
        {
            if (_activeWebView2Control is null || !_webView2EnvironmentInitialized)
            {
                await NavigateViaWebView2ChromiumAsync("https://www.google.com");
            }

            var mainCoreWv = _activeWebView2Control?.GetType().GetProperty("CoreWebView2")?.GetValue(_activeWebView2Control);
            var sharedEnv = mainCoreWv?.GetType().GetProperty("Environment")?.GetValue(mainCoreWv);
            if (sharedEnv is null || _webView2WpfAssembly is null) return;

            if (_adGuardPopupWebView2Control is null)
            {
                var wvType = _webView2WpfAssembly.GetType("Microsoft.Web.WebView2.Wpf.WebView2");
                if (wvType is null) return;
                _adGuardPopupWebView2Control = Activator.CreateInstance(wvType) as System.Windows.UIElement;
                if (_adGuardPopupWebView2Control is System.Windows.FrameworkElement fe)
                {
                    fe.Width = 320;
                    fe.Height = 480;
                }
            }

            if (_adGuardPopupWebView2Control is null) return;

            if (!ReferenceEquals(hostBorder.Child, _adGuardPopupWebView2Control))
            {
                hostBorder.Child = _adGuardPopupWebView2Control;
                await Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Loaded);
            }

            if (!_adGuardPopupWebView2Initialized)
            {
                var ensureMethod = _adGuardPopupWebView2Control.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "EnsureCoreWebView2Async" && m.GetParameters().Length == 1);
                if (ensureMethod?.Invoke(_adGuardPopupWebView2Control, new object?[] { sharedEnv }) is Task ensureTask)
                {
                    await ensureTask;
                    _adGuardPopupWebView2Initialized = true;
                }

                var popupCoreWv = _adGuardPopupWebView2Control.GetType().GetProperty("CoreWebView2")?.GetValue(_adGuardPopupWebView2Control);
                if (popupCoreWv is not null)
                {
                    _ = AttachCoreWebView2Event(popupCoreWv, "NewWindowRequested", (s, a) =>
                    {
                        try
                        {
                            a?.GetType().GetProperty("Handled")?.SetValue(a, true);
                            Dispatcher.BeginInvoke(async () =>
                            {
                                if (_adGuardShieldPopup is not null) _adGuardShieldPopup.IsOpen = false;
                                await OpenAdGuardInternalPageAsync(openOptionsPage: true);
                            });
                        }
                        catch { }
                    });

                    _ = AttachCoreWebView2Event(popupCoreWv, "WebMessageReceived", (s, a) =>
                    {
                        try
                        {
                            string? msg = a?.GetType().GetMethod("TryGetWebMessageAsString")?.Invoke(a, null) as string;
                            if (string.IsNullOrWhiteSpace(msg)) return;
                            Dispatcher.BeginInvoke(async () =>
                            {
                                if (msg.Equals("nexus_cmd:open_adguard_options", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (_adGuardShieldPopup is not null) _adGuardShieldPopup.IsOpen = false;
                                    await OpenAdGuardInternalPageAsync(openOptionsPage: true);
                                }
                                else if (msg.Equals("nexus_cmd:adguard_toggle_protection", StringComparison.OrdinalIgnoreCase))
                                {
                                    await ToggleAdGuardProtectionAsync();
                                }
                                else if (msg.Equals("nexus_cmd:adguard_reload_page", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (_adGuardShieldPopup is not null) _adGuardShieldPopup.IsOpen = false;
                                    var mainCore = _activeWebView2Control?.GetType().GetProperty("CoreWebView2")?.GetValue(_activeWebView2Control);
                                    mainCore?.GetType().GetMethod("Reload")?.Invoke(mainCore, null);
                                }
                                else if (msg.Equals("nexus_cmd:adguard_block_element", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (_adGuardShieldPopup is not null) _adGuardShieldPopup.IsOpen = false;
                                    await ExecuteLiveScriptOnWebView2Async("(function(){ try { var n = document.querySelectorAll('iframe, .ads, .banner, ytd-ad-slot-renderer, ytd-companion-slot-renderer, #player-ads'); for(var i=0;i<n.length;i++) n[i].style.setProperty('display','none','important'); } catch(e){} })()");
                                }
                            });
                        }
                        catch { }
                    });
                }
            }

            string currentHost = GetCurrentBrowserHostDomain();
            var corePopup = _adGuardPopupWebView2Control.GetType().GetProperty("CoreWebView2")?.GetValue(_adGuardPopupWebView2Control);
            corePopup?.GetType().GetMethod("NavigateToString", new Type[] { typeof(string) })
                ?.Invoke(corePopup, new object[] { GetGenuineAdGuardPopupWebUiHtml(currentHost) });
        }
        catch { }
    }

    private bool _newWindowInterceptorAttached;
    private bool _extensionsToolbarAttached;
    private bool _documentCreatedAdGuardInjected;
    private static int _adGuardBlockedCount;
    private System.Windows.Controls.Primitives.Popup? _extensionsPopup;
    private System.Windows.Controls.Primitives.Popup? _adGuardShieldPopup;
    private System.Windows.Controls.Primitives.Popup? _browserMoreMenuPopup;
    private double _currentZoomPercent = 100.0;
    private System.Windows.Controls.Button? _adGuardShieldBtn;
    private System.Windows.Controls.TextBlock? _adGuardFlyoutDomainText;
    private System.Windows.Controls.TextBlock? _adGuardFlyoutExtIdText;
    private System.Windows.Controls.TextBlock? _adGuardFlyoutStatsText;
    private System.Windows.Controls.Button? _adGuardMasterToggleBtn;
    private string _adGuardExtensionId = string.Empty;

    private System.Windows.Media.Brush GetNexusThemeBrush(string key, System.Windows.Media.Brush fallback)
    {
        try
        {
            if (TryFindResource(key) is System.Windows.Media.Brush b)
                return b;
        }
        catch { }
        return fallback;
    }


    private System.Windows.Shapes.Path CreateNexusVectorIcon(string pathData, string brushKey, System.Windows.Media.Brush fallbackBrush, double size = 14)
    {
        var p = new System.Windows.Shapes.Path
        {
            Data = System.Windows.Media.Geometry.Parse(pathData),
            Fill = GetNexusThemeBrush(brushKey, fallbackBrush),
            Stretch = System.Windows.Media.Stretch.Uniform,
            Width = size,
            Height = size,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        if (!string.IsNullOrWhiteSpace(brushKey) && !string.Equals(brushKey, "NexusAccentBrush", StringComparison.Ordinal))
        {
            p.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brushKey);
        }
        return p;
    }

    private System.Windows.Shapes.Path CreateNexusStrokeVectorIcon(string pathData, System.Windows.Media.Brush strokeBrush, double strokeThickness = 1.7, double size = 15, string? strokeResourceKey = null)
    {
        var p = new System.Windows.Shapes.Path
        {
            Data = System.Windows.Media.Geometry.Parse(pathData),
            Stroke = strokeBrush,
            StrokeThickness = strokeThickness,
            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
            StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
            StrokeLineJoin = System.Windows.Media.PenLineJoin.Round,
            Fill = System.Windows.Media.Brushes.Transparent,
            Stretch = System.Windows.Media.Stretch.Uniform,
            Width = size,
            Height = size,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        if (!string.IsNullOrWhiteSpace(strokeResourceKey))
        {
            p.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, strokeResourceKey);
        }
        else if (strokeBrush is System.Windows.Media.SolidColorBrush scb && scb.Color.G != 188)
        {
            p.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, scb.Color.R < 60 ? "DshTextPrimaryBrush" : "DshTextSecondaryBrush");
        }
        return p;
    }

    private static string GetAdGuardRootExtensionsDirectory()
    {
        string root = System.IO.Directory.Exists(@"D:\NEXUS")
            ? @"D:\NEXUS\nexus_data\webview2_extensions"
            : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexus_webview2_extensions");
        System.IO.Directory.CreateDirectory(root);
        return root;
    }

    private static string GetAdGuardUnpackedExtensionFolder()
    {
        string dir = System.IO.Path.Combine(GetAdGuardRootExtensionsDirectory(), "adguard_adblocker");
        try { System.IO.Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    private static string GetAdGuardDocumentStartScript()
    {
        return @"(function() {
            try {
                if (window.__nexusAdGuardDisabledForDomain) return 0;
                if (typeof window.__nexusSweepAdsNow === 'function') {
                    return window.__nexusSweepAdsNow();
                }

                function injectAdGuardCosmeticStyles() {
                    try {
                        if (document.getElementById('nexus-adguard-cosmetic-css')) return;
                        var st = document.createElement('style');
                        st.id = 'nexus-adguard-cosmetic-css';
                        st.textContent = [
                            'ytd-ad-slot-renderer, ytd-in-feed-ad-layout-renderer, ytd-promoted-sparkles-web-renderer,',
                            'ytd-companion-slot-renderer, ytd-action-companion-ad-renderer, ytd-display-ad-renderer,',
                            'ytd-promoted-video-renderer, ytd-compact-promoted-video-renderer, ytd-banner-promo-renderer,',
                            '#masthead-ad, #player-ads, .ytp-ad-overlay-container, .ytp-ad-message-container,',
                            '.ytd-mealbar-promo-renderer, ytd-statement-banner-renderer,',
                            'iframe[src*=""doubleclick.net""], iframe[src*=""googlesyndication.com""], iframe[src*=""adservice""],',
                            'iframe[src*=""popads""], ins.adsbygoogle, [id^=""div-gpt-ad""], [data-ad-client],',
                            '.klan, .kln, .blox.klan, .gmr-banner, .gmr-topbanner, .gmr-floating, .idmuvi-topbanner,',
                            '.banner-iklan, .pasang-iklan, .floating-ads, .float-ads, .popup-ad, .popup-iklan,',
                            '[id*=""iklan"" i], [class*=""iklan"" i], [id*=""klan"" i], [class*=""klan"" i],',
                            'a[href*=""slot"" i], a[href*=""gacor"" i], a[href*=""maxwin"" i], a[href*=""sbobet"" i],',
                            'a[href*=""777"" i], a[href*=""ratu89"" i], a[href*=""pentaslot"" i], a[href*=""judi"" i], a[href*=""cina777"" i] {',
                            '  display: none !important; visibility: hidden !important; height: 0 !important; min-height: 0 !important; margin: 0 !important; padding: 0 !important; pointer-events: none !important;',
                            '}'
                        ].join('\n');
                        (document.head || document.documentElement).appendChild(st);
                    } catch(e) {}
                }

                function unlockCopyAndSelection() {
                    try {
                        document.oncontextmenu = null;
                        document.onselectstart = null;
                        document.ondragstart = null;
                        if (document.body) {
                            document.body.style.userSelect = 'auto';
                            document.body.style.webkitUserSelect = 'auto';
                        }
                    } catch(e) {}
                }

                function tryNeutralizeYouTubeAds() {
                    var adHandled = 0;
                    try {
                        if ((location.hostname || '').indexOf('youtube.com') < 0) return 0;
                        var skipBtns = document.querySelectorAll('.ytp-ad-skip-button, .ytp-ad-skip-button-modern, .ytp-skip-ad-button, .ytp-ad-skip-button-slot button, button[id^=""skip-button""]');
                        for (var s = 0; s < skipBtns.length; s++) {
                            var btn = skipBtns[s];
                            if (btn) {
                                try { btn.click(); adHandled++; } catch(e) {}
                            }
                        }
                        var mp = document.getElementById('movie_player');
                        var isAdShowing = (mp && mp.classList && mp.classList.contains('ad-showing')) ||
                                          !!document.querySelector('.ad-showing, .ytp-ad-player-overlay');
                        if (isAdShowing) {
                            var v = document.querySelector('video');
                            if (v && isFinite(v.duration) && v.duration > 0 && v.currentTime < (v.duration - 0.15)) {
                                v.muted = true;
                                v.currentTime = v.duration;
                                adHandled++;
                            }
                        }
                    } catch(e) {}
                    return adHandled;
                }

                function hideElementAndCollapseWrapper(el) {
                    if (!el || el === document.body || el === document.documentElement) return false;
                    if (el.getAttribute('data-nexus-ad-hidden') === '1') return false;
                    el.setAttribute('data-nexus-ad-hidden', '1');
                    el.style.setProperty('display', 'none', 'important');
                    el.style.setProperty('visibility', 'hidden', 'important');
                    el.style.setProperty('height', '0px', 'important');
                    return true;
                }

                function sweepAdsAndGamblingBanners() {
                    if (window.__nexusAdGuardDisabledForDomain) return 0;
                    injectAdGuardCosmeticStyles();
                    if (!document.body) return 0;
                    var removed = tryNeutralizeYouTubeAds();

                    // 1. Detect and close/hide any 'Tutup Iklan' / 'Close Ad' overlay or popup (e.g. movieku.website CINA777 popup & bottom bar!)
                    try {
                        var candidates = document.querySelectorAll('button, a, span, div, label, p');
                        for (var c = 0; c < candidates.length; c++) {
                            var el = candidates[c];
                            if (el.getAttribute('data-nexus-ad-hidden') === '1') continue;
                            if (el.children.length > 3) continue;
                            var txt = (el.textContent || '').trim().toLowerCase();
                            if (txt === 'tutup iklan' || txt === 'close ad' || txt === 'close ads' || txt === 'skip ad' || txt === 'tutup x' || txt === 'x tutup' || txt === 'tutup [x]') {
                                try { el.click(); } catch(e) {}
                                // Walk up to find the floating/fixed overlay or ad wrapper containing this 'Tutup Iklan' button
                                var curr = el.parentElement;
                                var bestContainer = el;
                                for (var depth = 0; depth < 6 && curr && curr !== document.body && curr !== document.documentElement; depth++) {
                                    var cs = window.getComputedStyle(curr);
                                    var clsId = ((curr.className || '') + ' ' + (curr.id || '')).toLowerCase();
                                    // Never hide the main page wrapper (#page, #content, main, .site-content)
                                    if (curr.tagName === 'MAIN' || curr.id === 'page' || curr.id === 'content' || curr.id === 'main' || curr.querySelectorAll('article, .item, .movie-card').length > 2) {
                                        break;
                                    }
                                    if (cs.position === 'fixed' || cs.position === 'sticky' || cs.position === 'absolute' ||
                                        clsId.indexOf('iklan') >= 0 || clsId.indexOf('klan') >= 0 || clsId.indexOf('popup') >= 0 ||
                                        clsId.indexOf('float') >= 0 || clsId.indexOf('modal') >= 0 || clsId.indexOf('overlay') >= 0 ||
                                        clsId.indexOf('banner') >= 0 || curr.querySelectorAll('img, iframe').length >= 1) {
                                        bestContainer = curr;
                                    }
                                    curr = curr.parentElement;
                                }
                                if (hideElementAndCollapseWrapper(bestContainer)) {
                                    removed++;
                                }
                            }
                        }
                    } catch(eClose) {}

                    // 2. Sweep known ad/gambling selectors & containers
                    var selectors = [
                        'ytd-ad-slot-renderer:not([data-nexus-ad-hidden])',
                        'ytd-in-feed-ad-layout-renderer:not([data-nexus-ad-hidden])',
                        'ytd-promoted-sparkles-web-renderer:not([data-nexus-ad-hidden])',
                        'ytd-companion-slot-renderer:not([data-nexus-ad-hidden])',
                        'ytd-action-companion-ad-renderer:not([data-nexus-ad-hidden])',
                        'ytd-display-ad-renderer:not([data-nexus-ad-hidden])',
                        '#masthead-ad:not([data-nexus-ad-hidden])',
                        '#player-ads:not([data-nexus-ad-hidden])',
                        'iframe[src*=""doubleclick""]:not([data-nexus-ad-hidden])',
                        'iframe[src*=""googlesyndication""]:not([data-nexus-ad-hidden])',
                        'iframe[src*=""adservice""]:not([data-nexus-ad-hidden])',
                        'iframe[src*=""popads""]:not([data-nexus-ad-hidden])',
                        'ins.adsbygoogle:not([data-nexus-ad-hidden])',
                        '[id^=""div-gpt-ad""]:not([data-nexus-ad-hidden])',
                        '.klan:not([data-nexus-ad-hidden])',
                        '.kln:not([data-nexus-ad-hidden])',
                        '[id*=""iklan"" i]:not([data-nexus-ad-hidden])',
                        '[class*=""iklan"" i]:not([data-nexus-ad-hidden])',
                        '[id*=""klan"" i]:not([data-nexus-ad-hidden])',
                        '[class*=""klan"" i]:not([data-nexus-ad-hidden])',
                        '[id*=""banner-ad"" i]:not([data-nexus-ad-hidden])',
                        '[class*=""banner-ad"" i]:not([data-nexus-ad-hidden])',
                        '[class*=""floating-ads"" i]:not([data-nexus-ad-hidden])',
                        '[id*=""floating_ads"" i]:not([data-nexus-ad-hidden])',
                        '[class*=""popup-ad"" i]:not([data-nexus-ad-hidden])',
                        'a[href*=""slot"" i]:not([data-nexus-ad-hidden])',
                        'a[href*=""gacor"" i]:not([data-nexus-ad-hidden])',
                        'a[href*=""777"" i]:not([data-nexus-ad-hidden])',
                        'a[href*=""toto"" i]:not([data-nexus-ad-hidden])',
                        'a[href*=""judi"" i]:not([data-nexus-ad-hidden])',
                        'a[href*=""maxwin"" i]:not([data-nexus-ad-hidden])',
                        'a[href*=""casino"" i]:not([data-nexus-ad-hidden])',
                        'a[href*=""sbobet"" i]:not([data-nexus-ad-hidden])',
                        'a[href*=""ratu89"" i]:not([data-nexus-ad-hidden])',
                        'a[href*=""pentaslot"" i]:not([data-nexus-ad-hidden])',
                        'a[href*=""kaiko"" i]:not([data-nexus-ad-hidden])',
                        'img[src*=""slot"" i]:not([data-nexus-ad-hidden])',
                        'img[src*=""gacor"" i]:not([data-nexus-ad-hidden])',
                        'img[src*=""777"" i]:not([data-nexus-ad-hidden])',
                        'img[alt*=""iklan"" i]:not([data-nexus-ad-hidden])',
                        'img[alt*=""slot"" i]:not([data-nexus-ad-hidden])',
                        'img[alt*=""gacor"" i]:not([data-nexus-ad-hidden])'
                    ];
                    try {
                        var nodes = document.querySelectorAll(selectors.join(','));
                        for (var i = 0; i < nodes.length; i++) {
                            var n = nodes[i];
                            if (n.tagName === 'BODY' || n.tagName === 'HTML' || n.id === 'page' || n.id === 'content') continue;
                            var wrap = n.closest('.iklan, .klan, .kln, .ads, .banner, ytd-rich-item-renderer, [id*=""iklan"" i], [class*=""iklan"" i], [id*=""klan"" i], [class*=""klan"" i]') || n;
                            if (wrap.querySelectorAll && wrap.querySelectorAll('article, .item, .movie-card').length > 2) {
                                wrap = n;
                            }
                            if (hideElementAndCollapseWrapper(wrap)) {
                                removed++;
                            }
                        }
                    } catch(e1) {}

                    // 3. Detect stacked external GIF/WebP gambling banners on streaming sites (like movieku.website top banner stack)
                    try {
                        var links = document.querySelectorAll('a[target=""_blank""]:not([data-nexus-ad-hidden])');
                        for (var l = 0; l < links.length; l++) {
                            var aEl = links[l];
                            var href = (aEl.getAttribute('href') || '').toLowerCase();
                            if (!href || href.indexOf(location.hostname) >= 0) continue;
                            // Skip movie cards
                            if (aEl.closest('article, .item, .movie-card, .los')) continue;
                            var img = aEl.querySelector('img');
                            if (img) {
                                var src = (img.getAttribute('src') || img.getAttribute('data-src') || '').toLowerCase();
                                var w = img.naturalWidth || img.width || aEl.clientWidth || 0;
                                var h = img.naturalHeight || img.height || aEl.clientHeight || 0;
                                var isGifBanner = src.indexOf('.gif') >= 0 || (w >= 260 && h >= 40 && h <= 220);
                                if (isGifBanner) {
                                    if (hideElementAndCollapseWrapper(aEl)) {
                                        removed++;
                                    }
                                }
                            }
                        }
                    } catch(eGif) {}

                    if (removed > 0) {
                        try {
                            if (window.chrome && window.chrome.webview) {
                                window.chrome.webview.postMessage('nexus_cmd:adguard_inc_blocked:' + removed);
                            }
                        } catch(e2) {}
                    }
                    return removed;
                }

                window.__nexusSweepAdsNow = sweepAdsAndGamblingBanners;
                window.__nexusAdGuardObserverActive = true;

                window.open = function(url) {
                    return null;
                };

                injectAdGuardCosmeticStyles();
                unlockCopyAndSelection();
                sweepAdsAndGamblingBanners();

                var sweepTimer = null;
                function scheduleDebouncedSweep() {
                    if (sweepTimer) return;
                    sweepTimer = setTimeout(function() {
                        sweepTimer = null;
                        unlockCopyAndSelection();
                        sweepAdsAndGamblingBanners();
                    }, 220);
                }

                if (document.readyState === 'loading') {
                    document.addEventListener('DOMContentLoaded', function() {
                        sweepAdsAndGamblingBanners();
                        scheduleDebouncedSweep();
                    });
                }
                window.addEventListener('load', function() {
                    sweepAdsAndGamblingBanners();
                });

                var obs = new MutationObserver(function() {
                    scheduleDebouncedSweep();
                });
                obs.observe(document.documentElement || document, { childList: true, subtree: true });
                return 1;
            } catch(err) { return 0; }
        })();";
    }

    public static (string PopupUrl, string OptionsUrl) ResolveAdGuardExtensionPopupAndOptionsUrls(string extensionId, string extDir)
    {
        string popupRel = "pages/popup.html";
        string optionsRel = "pages/options.html";
        try
        {
            string manifestPath = System.IO.Path.Combine(extDir, "manifest.json");
            if (System.IO.File.Exists(manifestPath))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(manifestPath, System.Text.Encoding.UTF8));
                var root = doc.RootElement;
                if (root.TryGetProperty("action", out var act) && act.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    act.TryGetProperty("default_popup", out var dp) && dp.ValueKind == System.Text.Json.JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(dp.GetString()))
                {
                    popupRel = dp.GetString()!.TrimStart('/');
                }
                if (root.TryGetProperty("options_page", out var op) && op.ValueKind == System.Text.Json.JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(op.GetString()))
                {
                    optionsRel = op.GetString()!.TrimStart('/');
                }
                else if (root.TryGetProperty("options_ui", out var oui) && oui.ValueKind == System.Text.Json.JsonValueKind.Object &&
                         oui.TryGetProperty("page", out var oup) && oup.ValueKind == System.Text.Json.JsonValueKind.String &&
                         !string.IsNullOrWhiteSpace(oup.GetString()))
                {
                    optionsRel = oup.GetString()!.TrimStart('/');
                }
            }
        }
        catch { }

        string cleanId = (extensionId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleanId))
        {
            string filePopup = "file:///" + System.IO.Path.Combine(extDir, popupRel.Replace('/', '\\')).Replace('\\', '/');
            string fileOptions = "file:///" + System.IO.Path.Combine(extDir, optionsRel.Replace('/', '\\')).Replace('\\', '/');
            return (filePopup, fileOptions);
        }

        string popupUrl = $"chrome-extension://{cleanId}/{popupRel}";
        string optionsUrl = $"chrome-extension://{cleanId}/{optionsRel}";
        var cfg = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
        cfg.AdGuardExtensionId = cleanId;
        cfg.AdGuardPopupExtensionUrl = popupUrl;
        cfg.AdGuardOptionsExtensionUrl = optionsUrl;
        return (popupUrl, optionsUrl);
    }

    private System.Windows.Controls.StackPanel? _browserMultiTabStrip;
    private System.Windows.Controls.Button? _navBackBtn;
    private System.Windows.Controls.Button? _navFwdBtn;
    private System.Windows.Controls.Button? _navReloadBtn;
    private bool _isWebViewCurrentlyLoading;
    private bool _webMessageBridgeAttached;
    private bool _navigationCompletedFallbackAttached;

    private const string EdgeStoreAdGuardId = "pdffkfellgipmhklpdmokmckkkfcopbh";
    private const string ChromeStoreAdGuardId = "bgnkhhnnamicmpeenaelnjfhikgbkllg";
    private System.Threading.Tasks.TaskCompletionSource<bool>? _pendingNavigationTcs;
    private bool _lastNavigationSucceeded = true;
    private string _lastNavigationErrorStatus = "None";

    private async Task<bool> WaitForPendingNavigationAsync(int maxWaitMs = 3500)
    {
        try
        {
            var tcs = _pendingNavigationTcs;
            if (tcs is not null && !tcs.Task.IsCompleted)
            {
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(maxWaitMs));
                if (completed == tcs.Task)
                {
                    bool navOk = await tcs.Task;
                    await Task.Delay(240); // Allow SPA initial hydration after NavigationCompleted
                    return navOk;
                }
            }
            return _lastNavigationSucceeded;
        }
        catch
        {
            return false;
        }
    }

    private async Task InitializeCoreWebView2WithExtensionsSupportAsync(string userDataDir)
    {
        try
        {
            string extDir = GetAdGuardUnpackedExtensionFolder();
            await Task.Run(() => EnsureCleanUnmodifiedAdGuardExtension(extDir));

            string browserArgs = $"--remote-debugging-port=9222 --enable-features=msWebView2BrowserExtensionsEnabled --load-extension=\"{extDir}\"";
            Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", browserArgs);

            var coreAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name?.Equals("Microsoft.Web.WebView2.Core", StringComparison.OrdinalIgnoreCase) == true);
            if (coreAsm is null)
            {
                string localCoreDll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Microsoft.Web.WebView2.Core.dll");
                if (!System.IO.File.Exists(localCoreDll))
                    localCoreDll = @"D:\NEXUS\nexus_data\webview2_lib\Microsoft.Web.WebView2.Core.dll";
                if (System.IO.File.Exists(localCoreDll))
                    coreAsm = System.Reflection.Assembly.LoadFrom(localCoreDll);
            }

            // Ensure _activeWebView2Control is instantiated and attached to CefBrowserHost (Fixes blank white screen!)
            if (_activeWebView2Control is null)
            {
                var wpfAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name?.Equals("Microsoft.Web.WebView2.Wpf", StringComparison.OrdinalIgnoreCase) == true);
                if (wpfAsm is null)
                {
                    string localWpfDll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Microsoft.Web.WebView2.Wpf.dll");
                    if (!System.IO.File.Exists(localWpfDll))
                        localWpfDll = @"D:\NEXUS\nexus_data\webview2_lib\Microsoft.Web.WebView2.Wpf.dll";
                    if (System.IO.File.Exists(localWpfDll))
                        wpfAsm = System.Reflection.Assembly.LoadFrom(localWpfDll);
                }

                var wvControlType = wpfAsm?.GetType("Microsoft.Web.WebView2.Wpf.WebView2");
                if (wvControlType is not null)
                {
                    _activeWebView2Control = Activator.CreateInstance(wvControlType) as System.Windows.FrameworkElement;
                }
            }

            if (_activeWebView2Control is null) return;

            // Attach _activeWebView2Control to CefBrowserHost if not already attached
            if ((_activeWebView2Control as System.Windows.FrameworkElement)?.Parent is null)
            {
                var hostObj = this.FindName("CefBrowserHost")
                    ?? this.GetType().GetField("CefBrowserHost", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(this);
                if (hostObj is System.Windows.Controls.Panel panel)
                {
                    panel.Children.Clear();
                    panel.Children.Add(_activeWebView2Control);
                }
                else if (hostObj is System.Windows.Controls.Decorator decorator)
                {
                    decorator.Child = _activeWebView2Control;
                }
                else if (hostObj is System.Windows.Controls.ContentControl contentCtrl)
                {
                    contentCtrl.Content = _activeWebView2Control;
                }
            }

            var wvType = _activeWebView2Control.GetType();
            object? envInstance = null;
            if (coreAsm is not null)
            {
                var envType = coreAsm.GetType("Microsoft.Web.WebView2.Core.CoreWebView2Environment");
                var optType = coreAsm.GetType("Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions");
                object? optInstance = null;
                if (optType is not null)
                {
                    optInstance = Activator.CreateInstance(optType);
                    optType.GetProperty("AreBrowserExtensionsEnabled")?.SetValue(optInstance, true);
                    optType.GetProperty("AdditionalBrowserArguments")?.SetValue(optInstance, browserArgs);
                }

                if (envType is not null)
                {
                    var createMethod = envType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "CreateAsync" && m.GetParameters().Length >= 3);
                    if (createMethod is not null)
                    {
                        var pLen = createMethod.GetParameters().Length;
                        var cArgs = new object?[pLen];
                        cArgs[0] = null;
                        cArgs[1] = userDataDir;
                        cArgs[2] = optInstance;
                        if (createMethod.Invoke(null, cArgs) is Task createTask)
                        {
                            await createTask;
                            envInstance = createTask.GetType().GetProperty("Result")?.GetValue(createTask);
                        }
                    }
                }
            }

            var ensureMethod = wvType.GetMethods()
                .FirstOrDefault(m => m.Name == "EnsureCoreWebView2Async" && m.GetParameters().Length >= 1);
            if (ensureMethod is not null)
            {
                var eArgs = new object?[ensureMethod.GetParameters().Length];
                eArgs[0] = envInstance;
                if (ensureMethod.Invoke(_activeWebView2Control, eArgs) is Task ensureTask)
                {
                    await ensureTask;
                }
            }

            EnsureNewWindowInterceptorAttached();
            EnsureVirtualHostMappingForAdGuardExtension(extDir);
            await RegisterExtensionFolderInWebView2ProfileAsync(extDir);
            await QueryRealInstalledExtensionsAsync();
        }
        catch { }
    }

    private void EnsureVirtualHostMappingForAdGuardExtension(string extDir)
    {
        try
        {
            if (!System.IO.Directory.Exists(extDir) || _activeWebView2Control is null) return;
            var coreWv = _activeWebView2Control.GetType().GetProperty("CoreWebView2")?.GetValue(_activeWebView2Control);
            if (coreWv is null) return;

            var mapMethod = coreWv.GetType().GetMethod("SetVirtualHostNameToFolderMapping");
            if (mapMethod is not null)
            {
                var accessEnumType = mapMethod.GetParameters()[2].ParameterType;
                object allowVal = Enum.ToObject(accessEnumType, 1);
                mapMethod.Invoke(coreWv, new object[] { "adguard.extension.local", extDir, allowVal });
            }
        }
        catch { }
    }

    public static bool TryCopyInstalledAdGuardFromLocalMicrosoftEdge(string destinationFolder)
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string edgeUserData = System.IO.Path.Combine(localAppData, "Microsoft", "Edge", "User Data");
            if (!System.IO.Directory.Exists(edgeUserData)) return false;

            string[] candidateIds = new[] { EdgeStoreAdGuardId, ChromeStoreAdGuardId };
            foreach (var profileDir in System.IO.Directory.GetDirectories(edgeUserData))
            {
                string extRoot = System.IO.Path.Combine(profileDir, "Extensions");
                if (!System.IO.Directory.Exists(extRoot)) continue;

                foreach (string extId in candidateIds)
                {
                    string idDir = System.IO.Path.Combine(extRoot, extId);
                    if (!System.IO.Directory.Exists(idDir)) continue;

                    var verDir = new System.IO.DirectoryInfo(idDir)
                        .GetDirectories()
                        .OrderByDescending(d => d.LastWriteTimeUtc)
                        .FirstOrDefault(d => System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "manifest.json")));

                    if (verDir is not null)
                    {
                        CopyDirectoryRecursiveClean(verDir.FullName, destinationFolder);
                        return true;
                    }
                }
            }
        }
        catch { }
        return false;
    }

    private static void CopyDirectoryRecursiveClean(string sourceDir, string targetDir)
    {
        System.IO.Directory.CreateDirectory(targetDir);
        foreach (string file in System.IO.Directory.GetFiles(sourceDir))
        {
            string fName = System.IO.Path.GetFileName(file);
            if (fName.StartsWith("_", StringComparison.Ordinal)) continue;
            System.IO.File.Copy(file, System.IO.Path.Combine(targetDir, fName), overwrite: true);
        }
        foreach (string dir in System.IO.Directory.GetDirectories(sourceDir))
        {
            string dName = System.IO.Path.GetFileName(dir);
            if (dName.StartsWith("_", StringComparison.Ordinal) && !string.Equals(dName, "_locales", StringComparison.OrdinalIgnoreCase))
                continue;
            CopyDirectoryRecursiveClean(dir, System.IO.Path.Combine(targetDir, dName));
        }
    }

    private sealed class WebView2GenericEventAdapter<TArgs>
    {
        private readonly Action<object?, object?> _handler;
        public WebView2GenericEventAdapter(Action<object?, object?> handler) => _handler = handler;
        public void Invoke(object? sender, TArgs args) => _handler(sender, args);
    }

    private static bool AttachCoreWebView2Event(object coreWv, string eventName, Action<object?, object?> handler)
    {
        try
        {
            var evt = coreWv.GetType().GetEvent(eventName);
            if (evt?.EventHandlerType is not { } handlerType) return false;
            var genericArgs = handlerType.GetGenericArguments();
            if (genericArgs.Length == 1)
            {
                var adapterType = typeof(WebView2GenericEventAdapter<>).MakeGenericType(genericArgs[0]);
                var adapter = Activator.CreateInstance(adapterType, handler);
                var invokeMethod = adapterType.GetMethod("Invoke")!;
                var del = Delegate.CreateDelegate(handlerType, adapter, invokeMethod, throwOnBindFailure: false);
                if (del is not null)
                {
                    evt.AddEventHandler(coreWv, del);
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static System.Windows.Controls.ControlTemplate CreateZeroPaddingFlatButtonTemplate()
    {
        var template = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
        var borderFactory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
        borderFactory.SetValue(System.Windows.Controls.Border.BackgroundProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Control.BackgroundProperty));
        borderFactory.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Control.BorderBrushProperty));
        borderFactory.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Control.BorderThicknessProperty));
        borderFactory.SetValue(System.Windows.Controls.Border.PaddingProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Control.PaddingProperty));

        var presenterFactory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
        presenterFactory.SetValue(System.Windows.FrameworkElement.HorizontalAlignmentProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Control.HorizontalContentAlignmentProperty));
        presenterFactory.SetValue(System.Windows.FrameworkElement.VerticalAlignmentProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Control.VerticalContentAlignmentProperty));
        borderFactory.AppendChild(presenterFactory);
        template.VisualTree = borderFactory;
        return template;
    }

    private static void ApplyZeroClipToolbarButtonStyle(System.Windows.Controls.Button btn)
    {
        btn.Style = null;
        btn.Padding = new System.Windows.Thickness(0);
        btn.BorderThickness = new System.Windows.Thickness(0);
        btn.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;
        btn.VerticalContentAlignment = System.Windows.VerticalAlignment.Center;
        btn.Template = CreateZeroPaddingFlatButtonTemplate();
    }

    public static string GetRichAdGuardChromePopupHtml(bool isOptionsPage)
    {
        if (isOptionsPage)
        {
            return GetNexusBrowserSettingsHubHtml("general");
        }
        var rt = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
        bool active = rt.IsAdGuardProtectionEnabled;
        int totalBlocked = rt.AdGuardConfig.TotalBlockedAdsCount + _adGuardBlockedCount;
        return "<!DOCTYPE html><html lang=\"id\"><head><meta charset=\"utf-8\"><title>AdGuard Popup</title>" +
            "<style>body{margin:0;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Inter,Roboto,sans-serif;background:#fff;color:#222;}</style></head><body>" +
            $"<div><svg width=\"22\" height=\"22\" viewBox=\"0 0 24 24\"><path fill=\"#68bc71\" d=\"M12 2L4 5v6.09c0 5.05 3.41 9.76 8 10.91 4.59-1.15 8-5.86 8-10.91V5l-8-3z\"/></svg>{(active ? "Halaman aman" : "Perlindungan dijeda")} · Total diblokir: {totalBlocked:N0} · Blokir iklan secara manual</div>" +
            "</body></html>";
    }

    public static string GetNexusBrowserSettingsHubHtml(string initialSection = "general")
    {
        var rt = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
        rt.EnsureHistoryLoadedFromDisk();
        rt.EnsureAdGuardConfigLoadedFromDisk();
        var cfg = rt.AdGuardConfig;

        int realHistCount = rt.BrowsingHistoryEntries.Count;
        int realSiteCount = rt.BrowsingHistoryEntries.Select(x => x.Domain).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        string histCountText = realHistCount == 0 ? "No sites visited" : $"{realHistCount} item{(realHistCount == 1 ? "" : "s")} visited";
        string siteCountText = realSiteCount == 0 ? "No cookies stored" : $"Cookies stored for {realSiteCount} site{(realSiteCount == 1 ? "" : "s")}";
        int totalBlocked = cfg.TotalBlockedAdsCount + _adGuardBlockedCount;

        const string svgGlobeMono = "<svg width=\"15\" height=\"15\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"#5f6368\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><circle cx=\"12\" cy=\"12\" r=\"9\"/><line x1=\"3\" y1=\"12\" x2=\"21\" y2=\"12\"/><path d=\"M12 3a14.5 14.5 0 0 1 3.6 9 14.5 14.5 0 0 1-3.6 9 14.5 14.5 0 0 1-3.6-9 14.5 14.5 0 0 1 3.6-9z\"/></svg>";
        const string svgSearchMono = "<svg class=\"s-ico\" width=\"15\" height=\"15\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"#5f6368\" stroke-width=\"1.7\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><circle cx=\"11\" cy=\"11\" r=\"7\"/><line x1=\"20\" y1=\"20\" x2=\"16.2\" y2=\"16.2\"/></svg>";

        var groupsSb = new System.Text.StringBuilder();
        if (rt.BrowsingHistoryEntries.Count == 0)
        {
            groupsSb.Append("<div class=\"empty-card\"><div style=\"font-weight:600;color:#1b1b1b;margin-bottom:4px;\">No browsing history yet</div><div style=\"font-size:12.5px;color:#5f6368;\">Pages you visit in the built-in browser will appear here</div></div>");
        }
        else
        {
            foreach (var grp in rt.BrowsingHistoryEntries.GroupBy(x => string.IsNullOrWhiteSpace(x.DateGroupLabel) ? DateTime.Now.ToString("d MMM yyyy") : x.DateGroupLabel))
            {
                var rowsSb = new System.Text.StringBuilder();
                foreach (var item in grp)
                {
                    string safeUrlJs = (item.Url ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
                    string safeTimeJs = (item.TimeLabel ?? "").Replace("'", "");
                    rowsSb.Append($"<div class=\"h-row\" data-q=\"{System.Net.WebUtility.HtmlEncode((item.Title + " " + item.Domain).ToLowerInvariant())}\">" +
                                  $"<span class=\"fav\">{svgGlobeMono}</span>" +
                                  $"<span class=\"h-title\" onclick=\"sendCmd('nexus_cmd:open_url_tab:{safeUrlJs}')\">{System.Net.WebUtility.HtmlEncode(item.Title)}</span>" +
                                  $"<span class=\"h-dom\">{System.Net.WebUtility.HtmlEncode(item.Domain)}</span>" +
                                  $"<span class=\"h-time\">{System.Net.WebUtility.HtmlEncode(item.TimeLabel)}</span>" +
                                  $"<span class=\"h-more\" title=\"Delete item\" onclick=\"sendCmd('nexus_cmd:del_hist_item:{safeUrlJs}|{safeTimeJs}')\">✕</span></div>");
                }
                groupsSb.Append($"<div class=\"group-card\"><div class=\"group-title\" onclick=\"this.nextElementSibling.style.display=this.nextElementSibling.style.display==='none'?'block':'none'\"><span>{System.Net.WebUtility.HtmlEncode(grp.Key)}</span><span>⌃</span></div><div class=\"group-body\">{rowsSb}</div></div>");
            }
        }

        string dlDir = rt.DownloadDirectory;
        var dlFilesHtml = new System.Text.StringBuilder();
        int dlCount = 0;
        try
        {
            if (System.IO.Directory.Exists(dlDir))
            {
                var files = new System.IO.DirectoryInfo(dlDir)
                    .GetFiles()
                    .Where(f => Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.IsUserVisibleDownloadFile(f.Name))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Take(40)
                    .ToList();
                foreach (var f in files)
                {
                    dlCount++;
                    string safeName = System.Net.WebUtility.HtmlEncode(f.Name);
                    string safeNameJs = f.Name.Replace("\\", "\\\\").Replace("'", "\\'");
                    string sizeText = f.Length >= 1024 * 1024
                        ? $"{f.Length / (1024.0 * 1024.0):F2} MB"
                        : $"{Math.Max(0.1, f.Length / 1024.0):F1} KB";
                    dlFilesHtml.Append(
                        $"<div class=\"dl-row\" data-q=\"{safeName.ToLowerInvariant()}\">" +
                        $"<div class=\"dl-info\" onclick=\"sendCmd('nexus_cmd:open_dl_file:{safeNameJs}')\">" +
                        $"<div class=\"dl-name\" title=\"{safeName}\">{safeName}</div>" +
                        $"<div class=\"dl-sub\">{sizeText} · {f.LastWriteTime:d MMM yyyy HH:mm}</div></div>" +
                        $"<span class=\"dl-badge\" onclick=\"sendCmd('nexus_cmd:open_dl_file:{safeNameJs}')\">Open file</span>" +
                        $"<span class=\"h-more\" title=\"Delete file\" onclick=\"sendCmd('nexus_cmd:del_dl_file:{safeNameJs}')\">✕</span></div>");
                }
            }
        }
        catch { }
        string dlBodyCard = dlCount == 0
            ? "<div class=\"empty-card\"><div style=\"font-weight:600;color:#1b1b1b;margin-bottom:4px;\">No downloads yet</div><div style=\"font-size:12.5px;color:#5f6368;\">Files downloaded from the built-in browser will appear here</div></div>"
            : $"<div class=\"group-card\">{dlFilesHtml}</div>";
        string dlCountLabel = dlCount == 0 ? "No downloads" : $"{dlCount} file{(dlCount == 1 ? "" : "s")}";

        string OnToggle(bool v) => v ? "toggle-sw on" : "toggle-sw";
        string safeDlDir = System.Net.WebUtility.HtmlEncode(dlDir);
        string initSec = string.IsNullOrWhiteSpace(initialSection) ? "general" : initialSection.ToLowerInvariant();
        if (initSec == "passwords" || initSec == "import") initSec = "general";

        const string icoNavGeneral = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><rect x=\"3\" y=\"4\" width=\"18\" height=\"16\" rx=\"2\"/><line x1=\"3\" y1=\"9\" x2=\"21\" y2=\"9\"/></svg>";
        const string icoNavHistory = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><circle cx=\"12\" cy=\"12\" r=\"9\"/><polyline points=\"12 7 12 12 15 15\"/></svg>";
        const string icoNavDownloads = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M12 3v12\"/><polyline points=\"7 10 12 15 17 10\"/><line x1=\"5\" y1=\"20\" x2=\"19\" y2=\"20\"/></svg>";
        const string icoNavPrivacy = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><rect x=\"4\" y=\"11\" width=\"16\" height=\"10\" rx=\"2\"/><path d=\"M8 11V7a4 4 0 0 1 8 0v4\"/></svg>";
        const string icoNavExtensions = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M19.4 12a2.1 2.1 0 0 1 0 4.2h-1.4v3a1.8 1.8 0 0 1-1.8 1.8h-3v-1.4a2.1 2.1 0 0 0-4.2 0V21H6a1.8 1.8 0 0 1-1.8-1.8v-3H2.8a2.1 2.1 0 0 1 0-4.2h1.4V9A1.8 1.8 0 0 1 6 7.2h3V5.8a2.1 2.1 0 0 1 4.2 0v1.4h3A1.8 1.8 0 0 1 18 9v3h1.4z\"/></svg>";

        return "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Settings</title>" +
            "<style>" +
            "html,body{margin:0;padding:0;overflow-x:hidden;box-sizing:border-box;font-family:'Segoe UI',-apple-system,BlinkMacSystemFont,Inter,Roboto,sans-serif;background:#f7f7f7;color:#1b1b1b;display:flex;min-height:100vh;user-select:none;}" +
            ".sidebar{width:236px;padding:24px 12px;display:flex;flex-direction:column;justify-content:space-between;box-sizing:border-box;flex-shrink:0;background:#f7f7f7;border-right:1px solid #e2e2e2;}" +
            ".brand{font-weight:600;font-size:20px;color:#1b1b1b;padding:0 12px 18px 12px;letter-spacing:-0.2px;}" +
            ".nav-item{position:relative;display:flex;align-items:center;gap:12px;padding:9px 12px;border-radius:6px;font-size:13.5px;color:#2b2b2b;cursor:pointer;margin-bottom:2px;font-weight:400;transition:background .12s;}" +
            ".nav-item:hover{background:#ededed;color:#1b1b1b;}" +
            ".nav-item.active{background:#e5e5e5;color:#1b1b1b;font-weight:600;}" +
            ".nav-item.active::before{content:'';position:absolute;left:0;top:8px;bottom:8px;width:3px;border-radius:2px;background:#1b1b1b;}" +
            ".nav-sep{height:1px;background:#e2e2e2;margin:10px 12px;}" +
            ".main{flex:1;min-width:0;padding:24px 40px 44px 40px;max-width:780px;overflow-y:auto;overflow-x:hidden;box-sizing:border-box;}" +
            ".top-search-wrap{position:relative;margin-bottom:24px;max-width:680px;}" +
            ".top-search-wrap .s-ico{position:absolute;left:13px;top:50%;transform:translateY(-50%);pointer-events:none;}" +
            ".top-search{width:100%;padding:8px 14px 8px 36px;border-radius:18px;border:1px solid #d6d6d6;background:#ffffff;font-size:13px;color:#1b1b1b;box-sizing:border-box;outline:none;}" +
            ".top-search:focus{border-color:#5f6368;}" +
            ".hdr-row{display:flex;align-items:center;justify-content:space-between;margin-bottom:14px;}" +
            "h1{font-size:20px;font-weight:600;margin:0;color:#1b1b1b;}" +
            "h2{font-size:13.5px;font-weight:600;color:#1b1b1b;margin:20px 0 8px 0;}" +
            ".save-toast{font-size:12px;font-weight:600;color:#1b1b1b;background:#e5e5e5;padding:4px 12px;border-radius:6px;display:none;}" +
            ".tab-pane{display:none;}" +
            ".tab-pane.active{display:block;}" +
            ".card{background:#ffffff;border-radius:8px;padding:2px 18px;margin-bottom:14px;border:1px solid #e5e5e5;box-shadow:0 1px 2px rgba(0,0,0,0.02);}" +
            ".row{display:flex;align-items:center;justify-content:space-between;padding:13px 0;gap:16px;}" +
            ".row + .row{border-top:1px solid #f0f0f0;}" +
            ".row-left{display:flex;align-items:center;gap:12px;min-width:0;flex:1;}" +
            ".row-title{font-size:13.5px;font-weight:500;color:#1b1b1b;margin-bottom:2px;}" +
            ".row-sub{font-size:12px;color:#5f6368;line-height:1.4;}" +
            ".btn-pill{background:#ffffff;color:#1b1b1b;border:1px solid #d1d1d1;border-radius:6px;padding:6px 14px;font-size:12.5px;font-weight:500;cursor:pointer;flex-shrink:0;}" +
            ".btn-pill:hover{background:#f3f3f3;}" +
            ".btn-dark{background:#1b1b1b;color:#ffffff;border:1px solid #1b1b1b;border-radius:6px;padding:7px 18px;font-size:12.5px;font-weight:600;cursor:pointer;flex-shrink:0;}" +
            ".btn-dark:hover{background:#333333;}" +
            ".toggle-sw{width:38px;height:20px;border-radius:999px;background:#ffffff;border:1.5px solid #5f6368;position:relative;cursor:pointer;transition:.15s;flex-shrink:0;box-sizing:border-box;}" +
            ".toggle-sw.on{background:#1b1b1b;border-color:#1b1b1b;}" +
            ".toggle-sw::after{content:'';width:12px;height:12px;border-radius:50%;background:#5f6368;position:absolute;top:2.5px;left:3px;transition:.15s;}" +
            ".toggle-sw.on::after{left:20px;background:#ffffff;}" +
            ".empty-card{background:#ffffff;border:1px solid #e5e5e5;border-radius:8px;padding:32px 24px;text-align:center;color:#5f6368;font-size:13.5px;}" +
            ".group-card{background:#ffffff;border:1px solid #e5e5e5;border-radius:8px;padding:6px 16px;margin-bottom:12px;}" +
            ".group-title{font-size:12.5px;font-weight:600;color:#1b1b1b;padding:10px 0;display:flex;justify-content:space-between;cursor:pointer;}" +
            ".h-row,.dl-row{display:flex;align-items:center;gap:12px;padding:10px 4px;border-top:1px solid #f0f0f0;font-size:13px;}" +
            ".h-row:hover,.dl-row:hover{background:#fafafa;}" +
            ".dl-info{flex:1;min-width:0;cursor:pointer;}" +
            ".h-title,.dl-name{font-weight:500;color:#1b1b1b;cursor:pointer;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;display:block;}" +
            ".h-dom,.dl-sub{color:#5f6368;flex:1;font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}" +
            ".h-time{color:#5f6368;font-size:12px;flex-shrink:0;}" +
            ".h-more{color:#5f6368;cursor:pointer;padding:2px 6px;font-size:12px;border-radius:4px;flex-shrink:0;}" +
            ".h-more:hover{background:#eaeaea;color:#1b1b1b;}" +
            ".dl-badge{font-size:12px;color:#1b1b1b;background:#f3f3f3;border:1px solid #d6d6d6;padding:4px 10px;border-radius:6px;font-weight:500;flex-shrink:0;cursor:pointer;}" +
            ".dl-badge:hover{background:#e8e8e8;}" +
            ".pills{display:flex;gap:8px;margin-bottom:14px;flex-wrap:wrap;}" +
            ".pill{padding:6px 14px;border-radius:6px;border:1px solid #d6d6d6;background:#ffffff;font-size:12.5px;font-weight:500;color:#2b2b2b;cursor:pointer;}" +
            ".pill.active{background:#1b1b1b;border-color:#1b1b1b;color:#ffffff;font-weight:600;}" +
            ".chk-box{width:16px;height:16px;accent-color:#1b1b1b;cursor:pointer;flex-shrink:0;}" +
            ".fav{display:inline-flex;align-items:center;justify-content:center;flex-shrink:0;}" +
            "@media (max-width: 680px) {" +
            "  html,body{flex-direction:column;}" +
            "  .sidebar{width:100%;padding:12px 14px;border-right:none;border-bottom:1px solid #e2e2e2;background:#f7f7f7;}" +
            "  .brand{font-size:17px;padding:0 4px 10px 4px;width:100%;}" +
            "  .sidebar > div:first-child{display:flex;flex-wrap:wrap;gap:4px;align-items:center;}" +
            "  .nav-sep{display:none;}" +
            "  .nav-item{padding:6px 10px;font-size:12.5px;margin-bottom:0;}" +
            "  .nav-item.active::before{display:none;}" +
            "  .sidebar > div:last-child{display:none;}" +
            "  .main{padding:16px 16px 32px 16px;max-width:100%;width:100%;}" +
            "  .row{flex-wrap:wrap;gap:10px;}" +
            "  .row > div:first-child{min-width:180px;flex:1;}" +
            "}" +
            "html.dark,body.dark{background:#1b1b1c;color:#f3f4f6;}" +
            "body.dark .sidebar{background:#1b1b1c;border-color:#2c2c2e;}" +
            "body.dark .brand,body.dark h1,body.dark h2,body.dark .row-title,body.dark .group-title,body.dark .h-title,body.dark .dl-name{color:#f3f4f6 !important;}" +
            "body.dark .nav-item{color:#d1d5db;}" +
            "body.dark .nav-item:hover{background:#252528;color:#ffffff;}" +
            "body.dark .nav-item.active{background:#2c2c2e;color:#ffffff;}" +
            "body.dark .nav-item.active::before{background:#4cc2ff;}" +
            "body.dark .nav-sep,body.dark .row + .row,body.dark .h-row,body.dark .dl-row{border-color:#2e2e32;}" +
            "body.dark .card,body.dark .group-card,body.dark .empty-card,body.dark .top-search,body.dark .btn-pill,body.dark .pill{background:#252528 !important;border-color:#38383d !important;color:#f3f4f6 !important;}" +
            "body.dark .row-sub,body.dark .h-dom,body.dark .dl-sub,body.dark .h-time{color:#9ca3af !important;}" +
            "body.dark .toggle-sw{background:#252528;border-color:#6b7280;}" +
            "body.dark .toggle-sw.on{background:#4cc2ff;border-color:#4cc2ff;}" +
            "body.dark .toggle-sw.on::after{background:#151517;}" +
            "</style></head><body class=\"" + (Nexus.App.Services.ThemeManager.CurrentTheme == Nexus.App.Services.ThemeMode.Dark ? "dark" : "") + "\">" +

            // LEFT SIDEBAR (Clean, Monochrome, Only Real Working Sections)
            "<div class=\"sidebar\"><div>" +
            "<div class=\"brand\">Settings</div>" +
            $"<div class=\"nav-item active\" id=\"nav-general\" onclick=\"showTab('Browser settings','general',this)\">{icoNavGeneral}<span>General &amp; Browsing</span></div>" +
            $"<div class=\"nav-item\" id=\"nav-clear\" onclick=\"showTab('Clear browsing data','clear',this)\">{icoNavPrivacy}<span>Clear browsing data</span></div>" +
            $"<div class=\"nav-item\" id=\"nav-history\" onclick=\"showTab('Browsing history','history',this)\">{icoNavHistory}<span>Browsing history</span></div>" +
            $"<div class=\"nav-item\" id=\"nav-downloads\" onclick=\"showTab('Downloads','downloads',this)\">{icoNavDownloads}<span>Downloads</span></div>" +
            "<div class=\"nav-sep\"></div>" +
            $"<div class=\"nav-item\" id=\"nav-extensions\" onclick=\"showTab('Extensions','extensions',this)\">{icoNavExtensions}<span>Extensions</span></div>" +
            "</div>" +
            "<div style=\"border:1px solid #e0e0e0;background:#ffffff;border-radius:6px;padding:8px 12px;font-size:12px;color:#444;text-align:center;font-weight:500;\">NEXUS Built-in Browser</div></div>" +

            "<div class=\"main\">" +
            $"<div class=\"top-search-wrap\">{svgSearchMono}<input class=\"top-search\" placeholder=\"Search settings\" oninput=\"filterCurrentPane(this.value)\"></div>" +
            "<div class=\"hdr-row\"><h1 id=\"pageHdr\">Browser settings</h1><span id=\"saveToast\" class=\"save-toast\">Saved</span></div>" +

            // PANE 1: GENERAL & BROWSING (Only real WebView2 settings!)
            "<div id=\"pane-general\" class=\"tab-pane active\">" +
            "<div class=\"card\">" +
            $"<div class=\"row\"><div><div class=\"row-title\">Show full URL in address bar</div><div class=\"row-sub\">Display full protocol, path, and query parameters in the browser address bar</div></div><div id=\"tFullUrl\" class=\"{OnToggle(cfg.ShowFullUrlInAddressBar)}\" onclick=\"tog(this)\"></div></div>" +
            $"<div class=\"row\"><div><div class=\"row-title\">Autofill and passwords</div><div class=\"row-sub\">Manage passwords and enable WebView2 built-in form autofill</div></div><div id=\"tPwOffer\" class=\"{OnToggle(cfg.OfferToSavePasswords)}\" onclick=\"tog(this)\"></div></div>" +
            "<div class=\"row\"><div><div class=\"row-title\">Clear browsing data</div><div class=\"row-sub\">Delete browsing history, cookies, and cache from WebView2</div></div><button class=\"btn-pill\" onclick=\"goSec('clear')\">Choose what to clear</button></div>" +
            "<div class=\"row\"><div><div class=\"row-title\">Browsing history</div><div class=\"row-sub\">View, search, or delete pages visited in the built-in browser</div></div><button class=\"btn-pill\" onclick=\"goSec('history')\">Manage</button></div>" +
            "</div>" +
            "<h2>Downloads</h2>" +
            "<div class=\"card\">" +
            $"<div class=\"row\"><div><div class=\"row-title\">Location</div><div class=\"row-sub\">{safeDlDir}</div></div><button class=\"btn-pill\" onclick=\"sendCmd('nexus_cmd:open_downloads_folder')\">Open folder</button></div>" +
            $"<div class=\"row\"><div><div class=\"row-title\">Ask where to save each file before downloading</div><div class=\"row-sub\">Confirm before downloading files in the built-in browser</div></div><div id=\"tAskDl\" class=\"{OnToggle(rt.RequireConfirmationForDownload)}\" onclick=\"tog(this);sendCmd('nexus_cmd:toggle_ask_download:'+(this.classList.contains('on')?'1':'0'))\"></div></div>" +
            "<div class=\"row\"><div><div class=\"row-title\">Download history</div><div class=\"row-sub\">View and manage files downloaded from the built-in browser</div></div><button class=\"btn-pill\" onclick=\"goSec('downloads')\">Manage</button></div>" +
            "</div>" +
            "<h2>AdBlock &amp; Extensions</h2>" +
            "<div class=\"card\">" +
            $"<div class=\"row\"><div><div class=\"row-title\">AdGuard AdBlocker (GitHub MV3 Engine)</div><div class=\"row-sub\">Active WebView2 ad &amp; tracker blocker · Total blocked: {totalBlocked:N0}</div></div><button class=\"btn-pill\" onclick=\"sendCmd('nexus_cmd:open_genuine_adguard_options')\">Extension options</button></div>" +
            "</div></div>" +

            // PANE 2: BROWSING HISTORY
            "<div id=\"pane-history\" class=\"tab-pane\">" +
            "<div style=\"display:flex;align-items:center;justify-content:space-between;margin-bottom:12px;font-size:13px;font-weight:600;gap:8px;flex-wrap:wrap;\"><span>All-time history (" + histCountText + ")</span><div style=\"display:flex;gap:8px;\"><button class=\"btn-pill\" onclick=\"sendCmd('nexus_cmd:import_chrome_data')\">Sync from Edge / Chrome</button><button class=\"btn-pill\" onclick=\"goSec('clear')\">Clear browsing data</button></div></div>" +
            $"<div id=\"hGroups\">{groupsSb}</div>" +
            "</div>" +

            // PANE 3: DOWNLOADS
            "<div id=\"pane-downloads\" class=\"tab-pane\">" +
            "<div class=\"card\">" +
            $"<div class=\"row\"><div><div class=\"row-title\">Location</div><div class=\"row-sub\">{safeDlDir}</div></div><button class=\"btn-pill\" onclick=\"sendCmd('nexus_cmd:open_downloads_folder')\">Open folder</button></div>" +
            $"<div class=\"row\"><div><div class=\"row-title\">Ask where to save each file before downloading</div><div class=\"row-sub\">Confirm before downloading files in the built-in browser</div></div><div id=\"tAskDl2\" class=\"{OnToggle(rt.RequireConfirmationForDownload)}\" onclick=\"tog(this);sendCmd('nexus_cmd:toggle_ask_download:'+(this.classList.contains('on')?'1':'0'))\"></div></div>" +
            "</div>" +
            "<div style=\"display:flex;align-items:center;justify-content:space-between;margin:18px 0 10px 0;font-size:13px;font-weight:600;\"><span>Download history · Search downloads</span><button class=\"btn-pill\" onclick=\"sendCmd('nexus_cmd:clear_all_downloads')\">Clear all</button></div>" +
            dlBodyCard +
            "</div>" +

            // PANE 4: CLEAR BROWSING DATA (Real WebView2 profile clearing)
            "<div id=\"pane-clear\" class=\"tab-pane\">" +
            "<div style=\"color:#5f6368;font-size:12.5px;margin-bottom:14px;\">Privacy, search, and services · Select the time range and data types to permanently remove from the built-in WebView2 browser profile.</div>" +
            "<div class=\"pills\">" +
            "<span class=\"pill\" data-r=\"1h\" onclick=\"selPill(this)\">Last hour</span>" +
            "<span class=\"pill\" data-r=\"24h\" onclick=\"selPill(this)\">Last 24 hours</span>" +
            "<span class=\"pill\" data-r=\"7d\" onclick=\"selPill(this)\">Last 7 days</span>" +
            "<span class=\"pill\" data-r=\"4w\" onclick=\"selPill(this)\">Last 4 weeks</span>" +
            "<span class=\"pill active\" data-r=\"all\" onclick=\"selPill(this)\">All time</span>" +
            "</div>" +
            "<div class=\"card\">" +
            $"<div class=\"row\"><div class=\"row-left\">{icoNavHistory}<div><div class=\"row-title\">Browsing history</div><div class=\"row-sub\">{histCountText}</div></div></div><input id=\"chkHist\" type=\"checkbox\" checked class=\"chk-box\"></div>" +
            $"<div class=\"row\"><div class=\"row-left\">{icoNavPrivacy}<div><div class=\"row-title\">Cookies and site data</div><div class=\"row-sub\">{siteCountText}</div></div></div><input id=\"chkCook\" type=\"checkbox\" checked class=\"chk-box\"></div>" +
            $"<div class=\"row\"><div class=\"row-left\">{icoNavGeneral}<div><div class=\"row-title\">Cached images and files</div><div class=\"row-sub\">Clears WebView2 disk cache</div></div></div><input id=\"chkCache\" type=\"checkbox\" checked class=\"chk-box\"></div>" +
            $"<div class=\"row\"><div class=\"row-left\">{icoNavDownloads}<div><div class=\"row-title\">Download history</div><div class=\"row-sub\">{dlCountLabel}</div></div></div><input id=\"chkDl\" type=\"checkbox\" class=\"chk-box\"></div>" +
            "</div>" +
            "<div style=\"display:flex;justify-content:flex-end;gap:10px;margin-top:16px;\">" +
            "<button class=\"btn-pill\" onclick=\"goSec('general')\">Cancel</button>" +
            "<button class=\"btn-dark\" title=\"Delete data\" onclick=\"execRealDelete()\">Delete data</button></div></div>" +

            // PANE 5: EXTENSIONS (Single clean GitHub MV3 AdGuard engine)
            "<div id=\"pane-extensions\" class=\"tab-pane\">" +
            "<div class=\"card\">" +
            $"<div class=\"row\"><div class=\"row-left\">{icoNavExtensions}<div><div class=\"row-title\">AdGuard AdBlocker (GitHub MV3 Engine)</div><div class=\"row-sub\">Pin new extensions to toolbar · ID: pdffkfellgipmhklpdmokmckkkfcopbh · Total blocked: {totalBlocked:N0}</div></div></div><div class=\"{OnToggle(rt.IsAdGuardProtectionEnabled)}\" onclick=\"sendCmd('nexus_cmd:toggle_protection');this.classList.toggle('on');\"></div></div>" +
            "<div class=\"row\" style=\"justify-content:flex-end;gap:8px;\">" +
            "<button class=\"btn-dark\" onclick=\"sendCmd('nexus_cmd:open_genuine_adguard_options')\">Extension options</button>" +
            "</div></div></div>" +

            "</div>" +
            "<script>" +
            "function sendCmd(c){try{if(window.chrome&&window.chrome.webview)window.chrome.webview.postMessage(c);}catch(e){}}" +
            "function showTab(title,paneId,el){" +
            "  document.getElementById('pageHdr').innerText=title;" +
            "  sendCmd('nexus_cmd:sync_settings_title:' + title);" +
            "  var navs=document.querySelectorAll('.nav-item');for(var i=0;i<navs.length;i++)navs[i].classList.remove('active');" +
            "  if(el)el.classList.add('active');" +
            "  var panes=document.querySelectorAll('.tab-pane');for(var j=0;j<panes.length;j++)panes[j].classList.remove('active');" +
            "  var target=document.getElementById('pane-'+paneId);if(target)target.classList.add('active');" +
            "}" +
            "function goSec(sec){var n=document.getElementById('nav-'+sec);if(n)n.click();}" +
            "function selPill(el){var p=document.querySelectorAll('.pill');for(var i=0;i<p.length;i++)p[i].classList.remove('active');el.classList.add('active');}" +
            "function filterCurrentPane(q){" +
            "  q=(q||'').toLowerCase();" +
            "  var r=document.querySelectorAll('.h-row,.dl-row,.row');" +
            "  for(var i=0;i<r.length;i++){" +
            "    var txt=(r[i].getAttribute('data-q')||r[i].innerText||'').toLowerCase();" +
            "    r[i].style.display=(!q||txt.indexOf(q)>=0)?'flex':'none';" +
            "  }" +
            "}" +
            "function tog(el){el.classList.toggle('on');saveCfg();}" +
            "function saveCfg(){" +
            "  var fUrl=document.getElementById('tFullUrl');" +
            "  var pw=document.getElementById('tPwOffer');" +
            "  var payload={" +
            "    ShowFullUrlInAddressBar:fUrl?fUrl.classList.contains('on'):true," +
            "    OfferToSavePasswords:pw?pw.classList.contains('on'):true" +
            "  };" +
            "  sendCmd('nexus_cmd:save_browser_settings:'+JSON.stringify(payload));" +
            "  var t=document.getElementById('saveToast');if(t){t.style.display='inline-block';setTimeout(function(){t.style.display='none';},1600);}" +
            "}" +
            "function execRealDelete(){" +
            "  var act=document.querySelector('.pill.active');" +
            "  var r=act?act.getAttribute('data-r'):'all';" +
            "  var h=document.getElementById('chkHist').checked?'1':'0';" +
            "  var c=document.getElementById('chkCook').checked?'1':'0';" +
            "  var d=document.getElementById('chkDl').checked?'1':'0';" +
            "  if(d==='1')sendCmd('nexus_cmd:clear_all_downloads');" +
            "  sendCmd('nexus_cmd:clear_data:'+r+':'+h+':'+c);" +
            "}" +
            $"window.addEventListener('DOMContentLoaded',function(){{goSec('{initSec}');}});" +
            "</script></body></html>";
    }

    public static string GetNativeDownloadHistoryPageHtml() => GetNexusBrowserSettingsHubHtml("downloads");

    public static string GetNativeClearBrowsingDataModalHtml() => GetNexusBrowserSettingsHubHtml("clear");

    public static string GetNativeBrowsingHistoryPageHtml() => GetNexusBrowserSettingsHubHtml("history");

    public static bool IsOfficialAdGuardMv3Extracted(string extDir)
    {
        try
        {
            if (!System.IO.Directory.Exists(extDir)) return false;
            string manifestPath = System.IO.Path.Combine(extDir, "manifest.json");
            if (!System.IO.File.Exists(manifestPath)) return false;
            bool hasLocales = System.IO.Directory.Exists(System.IO.Path.Combine(extDir, "_locales"));
            return hasLocales;
        }
        catch
        {
            return false;
        }
    }

    public static string ComputeChromiumExtensionIdFromPublicKey(byte[] pubKeyDerBytes)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(pubKeyDerBytes);
        char[] chars = new char[32];
        for (int i = 0; i < 16; i++)
        {
            chars[i * 2] = (char)('a' + ((hash[i] >> 4) & 0x0F));
            chars[i * 2 + 1] = (char)('a' + (hash[i] & 0x0F));
        }
        return new string(chars);
    }

    private static string? TryExtractPublicKeyBase64FromCrx3Header(byte[] crxBytes, int headerLen, string targetExtId = "bgnkhhnnamicmpeenaelnjfhikgbkllg")
    {
        try
        {
            int end = Math.Min(crxBytes.Length, 12 + headerLen);
            string? fallbackRsaBase64 = null;
            for (int i = 12; i < end - 32; i++)
            {
                // Look for ASN.1 DER SubjectPublicKeyInfo sequence header: 0x30 0x82 0x01 0x22 (294 bytes total for RSA-2048)
                // or protobuf length-delimited field 0x0A followed by varint length
                if (crxBytes[i] == 0x30 && crxBytes[i + 1] == 0x82)
                {
                    int seqLen = (crxBytes[i + 2] << 8) | crxBytes[i + 3];
                    int totalDerLen = 4 + seqLen;
                    if (totalDerLen >= 160 && totalDerLen <= 1024 && i + totalDerLen <= end)
                    {
                        byte[] candidate = new byte[totalDerLen];
                        Array.Copy(crxBytes, i, candidate, 0, totalDerLen);
                        string cid = ComputeChromiumExtensionIdFromPublicKey(candidate);
                        if (string.Equals(cid, targetExtId, StringComparison.OrdinalIgnoreCase) || string.Equals(cid, EdgeStoreAdGuardId, StringComparison.OrdinalIgnoreCase) || string.Equals(cid, ChromeStoreAdGuardId, StringComparison.OrdinalIgnoreCase))
                        {
                            return Convert.ToBase64String(candidate);
                        }
                        fallbackRsaBase64 ??= Convert.ToBase64String(candidate);
                    }
                }
            }
            return fallbackRsaBase64;
        }
        catch
        {
            return null;
        }
    }

    private static readonly object _adGuardExtSyncLock = new();

    public static void EnsureAdGuardMv3ExtensionProvisioned(string extDir)
    {
        lock (_adGuardExtSyncLock)
        {
            try
            {
                System.IO.Directory.CreateDirectory(extDir);

                foreach (var subDir in System.IO.Directory.GetDirectories(extDir))
                {
                    string dName = System.IO.Path.GetFileName(subDir);
                    if (dName.StartsWith("_", StringComparison.Ordinal) && !string.Equals(dName, "_locales", StringComparison.OrdinalIgnoreCase))
                    {
                        try { System.IO.Directory.Delete(subDir, true); } catch { }
                    }
                }

                var rt = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
                rt.EnsureAdGuardConfigLoadedFromDisk();
                var cfg = rt.AdGuardConfig;

                var dnrRules = new System.Collections.Generic.List<string>();
                int ruleId = 1;

                void AddDomainBlockRule(string domainPattern)
                {
                    string cleanDom = domainPattern.Trim().TrimStart('|').TrimEnd('^', '/').Trim();
                    if (string.IsNullOrWhiteSpace(cleanDom) || cleanDom.StartsWith("!", StringComparison.Ordinal) || cleanDom.Contains("##", StringComparison.Ordinal))
                        return;
                    dnrRules.Add(
                        "  {\n" +
                        $"    \"id\": {ruleId++},\n" +
                        "    \"priority\": 1,\n" +
                        "    \"action\": { \"type\": \"block\" },\n" +
                        $"    \"condition\": {{ \"urlFilter\": \"||{cleanDom}^\", \"resourceTypes\": [\"script\", \"image\", \"sub_frame\", \"xmlhttprequest\", \"media\", \"ping\", \"other\"] }}\n" +
                        "  }");
                }

                if (rt.IsAdGuardProtectionEnabled)
                {
                    if (cfg.FilterAdBlocking)
                    {
                        foreach (var dom in new[] {
                            "doubleclick.net", "googlesyndication.com", "googleadservices.com", "adservice.google.com",
                            "pagead2.googlesyndication.com", "adnxs.com", "rubiconproject.com", "pubmatic.com",
                            "openx.net", "criteo.com", "taboola.com", "outbrain.com", "revcontent.com", "mgid.com",
                            "auto-page.gnjoy.hk"
                        })
                        {
                            AddDomainBlockRule(dom);
                        }
                    }

                    if (cfg.FilterPrivacy || cfg.StealthModeEnabled)
                    {
                        foreach (var dom in new[] {
                            "google-analytics.com", "googletagmanager.com", "scorecardresearch.com", "moatads.com",
                            "quantserve.com", "hotjar.com", "mouseflow.com", "clarity.ms"
                        })
                        {
                            AddDomainBlockRule(dom);
                        }
                    }

                    if (cfg.FilterAnnoyances)
                    {
                        foreach (var dom in new[] { "popads.net", "popcash.net", "propellerads.com", "adsterra.com", "onclickads.net" })
                        {
                            AddDomainBlockRule(dom);
                        }
                    }

                    if (cfg.FilterSecurity || cfg.FilterLanguageSpecific)
                    {
                        foreach (var dom in new[] { "ratu89.com", "pentaslot.com", "kaiko777.com", "rusia777.com", "cina777.com", "judi89.com" })
                        {
                            AddDomainBlockRule(dom);
                        }
                    }

                    foreach (string customRule in cfg.CustomUserRules)
                    {
                        string r = customRule.Trim();
                        if (r.StartsWith("||", StringComparison.Ordinal) && !r.Contains("##", StringComparison.Ordinal))
                        {
                            AddDomainBlockRule(r);
                        }
                    }
                }

                string rulesJsonContent = "[\n" + string.Join(",\n", dnrRules) + "\n]\n";
                var utf8NoBom = new System.Text.UTF8Encoding(false);
                System.IO.File.WriteAllText(System.IO.Path.Combine(extDir, "rules.json"), rulesJsonContent, utf8NoBom);

                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(extDir, "adguard-cosmetic.css"),
                    "ytd-ad-slot-renderer, ytd-in-feed-ad-layout-renderer, ytd-promoted-sparkles-web-renderer, ytd-companion-slot-renderer, ytd-action-companion-ad-renderer, ytd-display-ad-renderer, #masthead-ad, #player-ads, .ytp-ad-overlay-container, .adsbygoogle, .klan, .kln, .blox.klan, .gmr-banner, .gmr-topbanner, .idmuvi-topbanner, [id*='iklan' i], [class*='iklan' i], [id*='klan' i], [class*='klan' i], iframe[src*='doubleclick'], iframe[src*='googlesyndication'] { display: none !important; }\n",
                    utf8NoBom);

                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(extDir, "adguard-content.js"),
                    GetAdGuardDocumentStartScript(),
                    utf8NoBom);

                string manifestPath = System.IO.Path.Combine(extDir, "manifest.json");
                bool hasFullGitHubRulesets = System.IO.Directory.Exists(System.IO.Path.Combine(extDir, "filters", "declarative"));

                if (hasFullGitHubRulesets && System.IO.File.Exists(manifestPath))
                {
                    // Preserve the full GitHub chrome-mv3.zip manifest.json and enable the selected rulesets + custom rules.json!
                    var node = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(manifestPath, System.Text.Encoding.UTF8)) as System.Text.Json.Nodes.JsonObject;
                    if (node is not null)
                    {
                        node.Remove("update_url");
                        node.Remove("key");
                        node.Remove("background");
                        node.Remove("devtools_page");
                        node["permissions"] = new System.Text.Json.Nodes.JsonArray("declarativeNetRequest", "declarativeNetRequestWithHostAccess", "storage", "unlimitedStorage", "scripting");

                        var desiredRulesets = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (rt.IsAdGuardProtectionEnabled)
                        {
                            if (cfg.FilterAdBlocking) desiredRulesets.Add("ruleset_1");
                            if (cfg.FilterPrivacy) desiredRulesets.Add("ruleset_2");
                            if (cfg.FilterSocialWidgets) desiredRulesets.Add("ruleset_3");
                            if (cfg.FilterAnnoyances) { desiredRulesets.Add("ruleset_4"); desiredRulesets.Add("ruleset_14"); }
                            if (cfg.FilterSecurity) desiredRulesets.Add("ruleset_6");
                            if (cfg.FilterLanguageSpecific) { desiredRulesets.Add("ruleset_120"); desiredRulesets.Add("ruleset_9"); }
                            desiredRulesets.Add("ruleset_nexus_custom");
                        }

                        if (node["declarative_net_request"] is System.Text.Json.Nodes.JsonObject dnr &&
                            dnr["rule_resources"] is System.Text.Json.Nodes.JsonArray rrArr)
                        {
                            bool hasCustom = false;
                            foreach (var item in rrArr)
                            {
                                if (item is System.Text.Json.Nodes.JsonObject rObj)
                                {
                                    string rId = rObj["id"]?.GetValue<string>() ?? "";
                                    if (rId == "ruleset_nexus_custom") hasCustom = true;
                                    rObj["enabled"] = desiredRulesets.Contains(rId);
                                }
                            }
                            if (!hasCustom)
                            {
                                var customEntry = new System.Text.Json.Nodes.JsonObject
                                {
                                    ["id"] = "ruleset_nexus_custom",
                                    ["enabled"] = rt.IsAdGuardProtectionEnabled,
                                    ["path"] = "rules.json"
                                };
                                rrArr.Add(customEntry);
                            }
                        }

                        var csEntry = new System.Text.Json.Nodes.JsonObject
                        {
                            ["matches"] = new System.Text.Json.Nodes.JsonArray("<all_urls>"),
                            ["js"] = new System.Text.Json.Nodes.JsonArray("adguard-content.js"),
                            ["css"] = new System.Text.Json.Nodes.JsonArray("adguard-cosmetic.css"),
                            ["run_at"] = "document_start",
                            ["all_frames"] = true
                        };
                        node["content_scripts"] = new System.Text.Json.Nodes.JsonArray(csEntry);

                        var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                        System.IO.File.WriteAllText(manifestPath, node.ToJsonString(opts), utf8NoBom);
                        return;
                    }
                }

                string fallbackManifestJson =
                    "{\n" +
                    "  \"manifest_version\": 3,\n" +
                    "  \"name\": \"AdGuard AdBlocker (GitHub MV3 Engine)\",\n" +
                    "  \"version\": \"5.3.200.0\",\n" +
                    "  \"description\": \"AdGuard Browser Extension MV3 Engine compatible with Microsoft Edge WebView2.\",\n" +
                    "  \"permissions\": [\n" +
                    "    \"declarativeNetRequest\",\n" +
                    "    \"declarativeNetRequestWithHostAccess\",\n" +
                    "    \"storage\",\n" +
                    "    \"scripting\"\n" +
                    "  ],\n" +
                    "  \"host_permissions\": [\n" +
                    "    \"<all_urls>\"\n" +
                    "  ],\n" +
                    "  \"declarative_net_request\": {\n" +
                    "    \"rule_resources\": [\n" +
                    "      {\n" +
                    "        \"id\": \"ruleset_nexus_custom\",\n" +
                    $"        \"enabled\": {(rt.IsAdGuardProtectionEnabled ? "true" : "false")},\n" +
                    "        \"path\": \"rules.json\"\n" +
                    "      }\n" +
                    "    ]\n" +
                    "  },\n" +
                    "  \"content_scripts\": [\n" +
                    "    {\n" +
                    "      \"matches\": [\"<all_urls>\"],\n" +
                    "      \"js\": [\"adguard-content.js\"],\n" +
                    "      \"css\": [\"adguard-cosmetic.css\"],\n" +
                    "      \"run_at\": \"document_start\",\n" +
                    "      \"all_frames\": true\n" +
                    "    }\n" +
                    "  ]\n" +
                    "}\n";
                System.IO.File.WriteAllText(manifestPath, fallbackManifestJson, utf8NoBom);
            }
            catch { }
        }
    }

    public static void EnsureCleanUnmodifiedAdGuardExtension(string extDir)
    {
        EnsureAdGuardMv3ExtensionProvisioned(extDir);
    }

    public static void UnlockManifestWebAccessibleResources(string extDir, string? injectedPublicKeyBase64 = null)
    {
        EnsureAdGuardMv3ExtensionProvisioned(extDir);
    }

    public static string GetGenuineAdGuardPopupWebUiHtml(string? currentHost = null)
    {
        return GetGenuineAdGuardOptionsWebUiHtml();
    }

    public static string GetGenuineAdGuardOptionsWebUiHtml()
    {
        var rt = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
        rt.EnsureAdGuardConfigLoadedFromDisk();
        var cfg = rt.AdGuardConfig;
        bool active = rt.IsAdGuardProtectionEnabled;
        int totalBlocked = cfg.TotalBlockedAdsCount + _adGuardBlockedCount;
        int activeFilterCount = (cfg.FilterAdBlocking ? 1 : 0) + (cfg.FilterPrivacy ? 1 : 0) + (cfg.FilterSocialWidgets ? 1 : 0) +
                                (cfg.FilterAnnoyances ? 1 : 0) + (cfg.FilterSecurity ? 1 : 0) + (cfg.FilterLanguageSpecific ? 1 : 0);
        string allowlistText = System.Net.WebUtility.HtmlEncode(string.Join("\n", cfg.AllowlistDomains));
        string userRulesText = System.Net.WebUtility.HtmlEncode(string.Join("\n", cfg.CustomUserRules));

        return "<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>" +
            "<title>AdGuard Settings</title><style>" +
            "*{box-sizing:border-box}" +
            "body{margin:0;font-family:'Segoe UI',-apple-system,BlinkMacSystemFont,Roboto,sans-serif;background:#f7f7f7;color:#1b1b1b;display:flex;min-height:100vh;user-select:none}" +
            ".side{width:236px;border-right:1px solid #e2e2e2;padding:24px 12px;flex-shrink:0;background:#f7f7f7}" +
            ".logo{display:flex;align-items:center;gap:9px;font-size:18px;font-weight:700;color:#1b1b1b;padding:0 12px 20px}" +
            ".nav-i{padding:9px 12px;border-radius:6px;font-size:13.5px;color:#2b2b2b;cursor:pointer;margin-bottom:2px;font-weight:400}" +
            ".nav-i:hover{background:#ededed;color:#1b1b1b}" +
            ".nav-i.active{background:#e5e5e5;color:#1b1b1b;font-weight:600}" +
            ".main{flex:1;padding:28px 40px;max-width:820px;overflow-y:auto}" +
            ".hdr-bar{display:flex;align-items:center;justify-content:space-between;margin-bottom:18px}" +
            "h1{font-size:20px;font-weight:600;margin:0;color:#1b1b1b}" +
            ".toast{font-size:12px;font-weight:600;color:#1b1b1b;background:#e5e5e5;padding:4px 12px;border-radius:6px;display:none}" +
            ".card{border:1px solid #e5e5e5;border-radius:8px;padding:4px 18px;margin-bottom:16px;background:#fff}" +
            ".row{display:flex;align-items:center;justify-content:space-between;padding:14px 0;border-bottom:1px solid #f0f0f0;gap:16px}" +
            ".row:last-child{border-bottom:none}" +
            ".r-t{font-size:13.5px;font-weight:500;color:#1b1b1b;margin-bottom:3px}" +
            ".r-d{font-size:12px;color:#5f6368;line-height:1.4}" +
            ".sw{position:relative;width:40px;height:22px;flex-shrink:0}" +
            ".sw input{opacity:0;width:0;height:0}" +
            ".sl{position:absolute;cursor:pointer;inset:0;background:#c4c9d0;border-radius:22px;transition:.16s}" +
            ".sl:before{content:'';position:absolute;height:16px;width:16px;left:3px;bottom:3px;background:#fff;border-radius:50%;transition:.16s}" +
            "input:checked+.sl{background:#67b279}" +
            "input:checked+.sl:before{transform:translateX(18px)}" +
            ".sec{display:none}.sec.active{display:block}" +
            "textarea{width:100%;min-height:190px;border:1px solid #d1d1d1;border-radius:8px;padding:12px;font-family:Consolas,monospace;font-size:13px;margin-top:10px;background:#fff;color:#1b1b1b}" +
            ".btn{padding:7px 16px;border-radius:6px;border:1px solid #1b1b1b;background:#1b1b1b;color:#fff;font-size:12.5px;font-weight:600;cursor:pointer;margin-top:10px}" +
            ".btn-outline{padding:6px 14px;border-radius:6px;border:1px solid #d1d1d1;background:#fff;color:#1b1b1b;font-size:12.5px;font-weight:500;cursor:pointer}" +
            ".btn-outline:hover{background:#f3f3f3}" +
            "@media(max-width:680px){body{flex-direction:column}.side{width:100%;border-right:none;border-bottom:1px solid #e2e2e2;padding:12px;display:flex;overflow-x:auto;gap:6px;flex-wrap:wrap}.logo{width:100%;padding:0 4px 8px}.nav-i{white-space:nowrap;margin:0;padding:6px 10px;font-size:12.5px}.main{padding:16px}}" +
            "</style></head><body>" +
            "<div class='side'>" +
            "  <div class='logo'><svg width='20' height='20' viewBox='0 0 24 24'><path fill='#67b279' d='M12 2L4 5v6.09c0 5.05 3.41 9.76 8 10.91 4.59-1.15 8-5.86 8-10.91V5l-8-3z'/><path fill='#fff' d='M10.4 15.2l-3.3-3.3 1.4-1.4 1.9 1.9 4.5-4.5 1.4 1.4-5.9 5.9z'/></svg><span>AdGuard MV3</span></div>" +
            "  <div class='nav-i active' onclick=\"goTab('general',this)\">General</div>" +
            "  <div class='nav-i' onclick=\"goTab('filters',this)\">Filters</div>" +
            "  <div class='nav-i' onclick=\"goTab('stealth',this)\">Tracking protection</div>" +
            "  <div class='nav-i' onclick=\"goTab('allowlist',this)\">Allowlist</div>" +
            "  <div class='nav-i' onclick=\"goTab('userrules',this)\">User rules</div>" +
            "</div>" +
            "<div class='main'>" +
            "  <div class='hdr-bar'><h1 id='secHdr'>General</h1><span id='savedBadge' class='toast'>Applied to WebView2</span></div>" +
            "  <div class='sec active' id='sec-general'>" +
            "    <div class='card'>" +
            "      <div class='row'><div><div class='r-t'>Enable AdGuard MV3 protection</div><div class='r-d'>Actively block network ad requests, video pre-roll ads, and cosmetic ad banners in WebView2</div></div><label class='sw'><input type='checkbox' " + (active ? "checked" : "") + " onchange=\"sendCmd('nexus_cmd:adguard_toggle_protection');showSaved()\"><span class='sl'></span></label></div>" +
            "      <div class='row'><div><div class='r-t'>Block search ads and website self-promotion</div><div class='r-d'>Remove sponsored search result slots on Google and YouTube</div></div><label class='sw'><input type='checkbox' " + (cfg.BlockSearchAdsAndSelfPromo ? "checked" : "") + " onchange=\"sendCmd('nexus_cmd:adguard_flag:search_ads:'+(this.checked?'1':'0'));showSaved()\"><span class='sl'></span></label></div>" +
            "      <div class='row'><div><div class='r-t'>Active statistics</div><div class='r-d'>Total ads &amp; trackers blocked: <strong id='lblTotalBlocked'>" + totalBlocked.ToString("N0") + "</strong> · Active filter categories: <strong>" + activeFilterCount + " / 6</strong></div></div><button class='btn-outline' onclick=\"sendCmd('nexus_cmd:adguard_reset_stats');document.getElementById('lblTotalBlocked').innerText='0';showSaved()\">Reset counter</button></div>" +
            "    </div>" +
            "  </div>" +
            "  <div class='sec' id='sec-filters'>" +
            "    <div class='card'>" +
            "      <div class='row'><div><div class='r-t'>Ad blocking (AdGuard Base + EasyList)</div><div class='r-d'>Blocks DoubleClick, GoogleSyndication, Taboola, Outbrain, Criteo, and YouTube video/companion ads</div></div><label class='sw'><input type='checkbox' " + (cfg.FilterAdBlocking ? "checked" : "") + " onchange=\"sendCmd('nexus_cmd:adguard_filter:adblocking:'+(this.checked?'1':'0'));showSaved()\"><span class='sl'></span></label></div>" +
            "      <div class='row'><div><div class='r-t'>Privacy &amp; Analytics protection</div><div class='r-d'>Blocks third-party telemetry, Google Analytics, ScorecardResearch, Hotjar, and pixel trackers</div></div><label class='sw'><input type='checkbox' " + (cfg.FilterPrivacy ? "checked" : "") + " onchange=\"sendCmd('nexus_cmd:adguard_filter:privacy:'+(this.checked?'1':'0'));showSaved()\"><span class='sl'></span></label></div>" +
            "      <div class='row'><div><div class='r-t'>Social widgets</div><div class='r-d'>Removes embedded third-party social tracking widgets and share overlays</div></div><label class='sw'><input type='checkbox' " + (cfg.FilterSocialWidgets ? "checked" : "") + " onchange=\"sendCmd('nexus_cmd:adguard_filter:social:'+(this.checked?'1':'0'));showSaved()\"><span class='sl'></span></label></div>" +
            "      <div class='row'><div><div class='r-t'>Annoyances &amp; Pop-up blockers</div><div class='r-d'>Blocks PopAds, PopCash, PropellerAds, floating overlays, and cookie consent banners</div></div><label class='sw'><input type='checkbox' " + (cfg.FilterAnnoyances ? "checked" : "") + " onchange=\"sendCmd('nexus_cmd:adguard_filter:annoyances:'+(this.checked?'1':'0'));showSaved()\"><span class='sl'></span></label></div>" +
            "      <div class='row'><div><div class='r-t'>Security &amp; Anti-Gambling / Malicious domains</div><div class='r-d'>Blocks known phishing, malware, and online gambling promotion banners</div></div><label class='sw'><input type='checkbox' " + (cfg.FilterSecurity ? "checked" : "") + " onchange=\"sendCmd('nexus_cmd:adguard_filter:security:'+(this.checked?'1':'0'));showSaved()\"><span class='sl'></span></label></div>" +
            "      <div class='row'><div><div class='r-t'>Indonesian Regional Filter (ABPindo)</div><div class='r-d'>Blocks regional banner ads on Indonesian news and video portals</div></div><label class='sw'><input type='checkbox' " + (cfg.FilterLanguageSpecific ? "checked" : "") + " onchange=\"sendCmd('nexus_cmd:adguard_filter:language:'+(this.checked?'1':'0'));showSaved()\"><span class='sl'></span></label></div>" +
            "    </div>" +
            "  </div>" +
            "  <div class='sec' id='sec-stealth'>" +
            "    <div class='card'>" +
            "      <div class='row'><div><div class='r-t'>Enable Tracking Protection (Stealth Mode)</div><div class='r-d'>Block third-party tracking scripts and analytics beacons</div></div><label class='sw'><input type='checkbox' " + (cfg.StealthModeEnabled ? "checked" : "") + " onchange=\"sendCmd('nexus_cmd:adguard_flag:stealth:'+(this.checked?'1':'0'));showSaved()\"><span class='sl'></span></label></div>" +
            "      <div class='row'><div><div class='r-t'>Strip tracking parameters from URLs</div><div class='r-d'>Automatically remove utm_source, utm_medium, utm_campaign, fbclid, gclid, and msclkid on navigation</div></div><label class='sw'><input type='checkbox' " + (cfg.StripTrackingParams ? "checked" : "") + " onchange=\"sendCmd('nexus_cmd:adguard_flag:strip_params:'+(this.checked?'1':'0'));showSaved()\"><span class='sl'></span></label></div>" +
            "      <div class='row'><div><div class='r-t'>Hide search queries referrer</div><div class='r-d'>Clear document referrer when navigating from search engines to external sites</div></div><label class='sw'><input type='checkbox' " + (cfg.HideSearchQueries ? "checked" : "") + " onchange=\"sendCmd('nexus_cmd:adguard_flag:hide_search:'+(this.checked?'1':'0'));showSaved()\"><span class='sl'></span></label></div>" +
            "      <div class='row'><div><div class='r-t'>Send Do-Not-Track (DNT) and Global Privacy Control</div><div class='r-d'>Set navigator.doNotTrack = '1' and navigator.globalPrivacyControl = true on visited pages</div></div><label class='sw'><input type='checkbox' " + (cfg.SendDoNotTrackHeader ? "checked" : "") + " onchange=\"sendCmd('nexus_cmd:adguard_flag:dnt:'+(this.checked?'1':'0'));showSaved()\"><span class='sl'></span></label></div>" +
            "    </div>" +
            "  </div>" +
            "  <div class='sec' id='sec-allowlist'>" +
            "    <div class='r-d'>Domains listed below (one per line, e.g. <code>example.com</code>) are excluded from AdGuard blocking:</div>" +
            "    <textarea id='txtAllow' placeholder='example.com'>" + allowlistText + "</textarea>" +
            "    <button class='btn' onclick=\"sendCmd('nexus_cmd:adguard_save_allowlist:'+encodeURIComponent(document.getElementById('txtAllow').value));showSaved()\">Save Allowlist</button>" +
            "  </div>" +
            "  <div class='sec' id='sec-userrules'>" +
            "    <div class='r-d'>Enter custom domain block rules (<code>||domain.com^</code>) or CSS hide rules (<code>domain.com##.ad-banner</code>), one per line:</div>" +
            "    <textarea id='txtRules' placeholder='||doubleclick.net^\nyoutube.com##.ytp-ad-module'>" + userRulesText + "</textarea>" +
            "    <button class='btn' onclick=\"sendCmd('nexus_cmd:adguard_save_userrules:'+encodeURIComponent(document.getElementById('txtRules').value));showSaved()\">Save User Rules</button>" +
            "  </div>" +
            "</div>" +
            "<script>" +
            "function sendCmd(c){try{if(window.chrome&&window.chrome.webview)window.chrome.webview.postMessage(c);}catch(e){}}" +
            "function showSaved(){var b=document.getElementById('savedBadge');if(b){b.style.display='inline-block';setTimeout(function(){b.style.display='none';},1600);}}" +
            "function goTab(id,el){document.querySelectorAll('.sec').forEach(function(s){s.classList.remove('active');});document.querySelectorAll('.nav-i').forEach(function(n){n.classList.remove('active');});var t=document.getElementById('sec-'+id);if(t)t.classList.add('active');if(el){el.classList.add('active');document.getElementById('secHdr').innerText=el.innerText;}}" +
            "</script></body></html>";
    }

    private void NavigateWebView2DirectSchemeSafe(string targetUri, string? fallbackHtml = null)
    {
        try
        {
            if (_activeWebView2Control is null) return;
            EnsureNewWindowInterceptorAttached();
            var coreWv = _activeWebView2Control.GetType().GetProperty("CoreWebView2")?.GetValue(_activeWebView2Control);
            if (coreWv is null) return;

            if (!string.IsNullOrWhiteSpace(fallbackHtml) &&
                targetUri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            {
                var navStrMethod = coreWv.GetType().GetMethod("NavigateToString", new Type[] { typeof(string) });
                if (navStrMethod is not null)
                {
                    navStrMethod.Invoke(coreWv, new object[] { fallbackHtml });
                    return;
                }
            }

            if (targetUri.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
                targetUri.StartsWith("extension://", StringComparison.OrdinalIgnoreCase))
            {
                bool wantOptions = targetUri.Contains("options", StringComparison.OrdinalIgnoreCase);
                string relPath = wantOptions ? "pages/options.html" : "pages/popup.html";
                string activeId = !string.IsNullOrWhiteSpace(_adGuardExtensionId) ? _adGuardExtensionId : EdgeStoreAdGuardId;
                targetUri = $"chrome-extension://{activeId}/{relPath}";
            }

            var navMethod = coreWv.GetType().GetMethod("Navigate", new Type[] { typeof(string) });
            navMethod?.Invoke(coreWv, new object[] { targetUri });
        }
        catch { }
    }

    private async Task<string> CallLiveCdpMethodOnWebView2Async(string methodName, string parametersJson)
    {
        if (_activeWebView2Control is null || string.IsNullOrWhiteSpace(methodName))
            return string.Empty;

        try
        {
            async Task<string> RunCdpOnUiAsync()
            {
                var coreProp = _activeWebView2Control.GetType().GetProperty("CoreWebView2");
                var coreWv = coreProp?.GetValue(_activeWebView2Control);
                if (coreWv is null)
                    return string.Empty;

                var cdpMethod = coreWv.GetType().GetMethod("CallDevToolsProtocolMethodAsync", new Type[] { typeof(string), typeof(string) });
                if (cdpMethod?.Invoke(coreWv, new object?[] { methodName, string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson }) is Task<string> t)
                {
                    return await t ?? "{}";
                }
                return string.Empty;
            }

            if (!Dispatcher.CheckAccess())
            {
                var op = Dispatcher.InvokeAsync(RunCdpOnUiAsync);
                return await await op.Task;
            }
            return await RunCdpOnUiAsync();
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<string> ExecuteLiveScriptOnWebView2Async(string jsCode)
    {
        if (_activeWebView2Control is null || string.IsNullOrWhiteSpace(jsCode))
            return string.Empty;

        try
        {
            async Task<string> RunOnUiAsync()
            {
                var execMethod = _activeWebView2Control.GetType().GetMethod("ExecuteScriptAsync", new Type[] { typeof(string) });
                if (execMethod?.Invoke(_activeWebView2Control, new object?[] { jsCode }) is Task<string> t)
                {
                    string rawJson = await t;
                    if (string.IsNullOrWhiteSpace(rawJson) || rawJson == "null" || rawJson == "\"\"")
                        return string.Empty;
                    if (rawJson.StartsWith("\"", StringComparison.Ordinal))
                    {
                        return System.Text.Json.JsonSerializer.Deserialize<string>(rawJson) ?? string.Empty;
                    }
                    return rawJson;
                }
                return string.Empty;
            }

            if (!Dispatcher.CheckAccess())
            {
                var proxyTask = await Dispatcher.InvokeAsync(RunOnUiAsync);
                return await proxyTask;
            }
            return await RunOnUiAsync();
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<List<Nexus.Contracts.BrowserAgent.InstalledBrowserExtensionInfo>> QueryRealInstalledExtensionsAsync()
    {
        var results = new List<Nexus.Contracts.BrowserAgent.InstalledBrowserExtensionInfo>();
        string rootDir = GetAdGuardRootExtensionsDirectory();

        try
        {
            var coreWv = _activeWebView2Control?.GetType().GetProperty("CoreWebView2")?.GetValue(_activeWebView2Control);
            var profileObj = coreWv?.GetType().GetProperty("Profile")?.GetValue(coreWv);
            var getExtsMethod = profileObj?.GetType().GetMethod("GetBrowserExtensionsAsync", Type.EmptyTypes);
            if (getExtsMethod?.Invoke(profileObj, null) is Task extTask)
            {
                await extTask.ConfigureAwait(true);
                var resultProp = extTask.GetType().GetProperty("Result");
                if (resultProp?.GetValue(extTask) is System.Collections.IEnumerable extList)
                {
                    foreach (object extObj in extList)
                    {
                        string id = extObj.GetType().GetProperty("Id")?.GetValue(extObj) as string ?? "";
                        string name = extObj.GetType().GetProperty("Name")?.GetValue(extObj) as string ?? "";
                        bool isEnabled = extObj.GetType().GetProperty("IsEnabled")?.GetValue(extObj) is bool b ? b : true;
                        if (string.IsNullOrWhiteSpace(name) ||
                            name.Contains("CryptoToken", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("PDF Viewer", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Web Store", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Feedback", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Hangout", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Clipboard", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Edge ", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Media Router", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        if (!string.IsNullOrWhiteSpace(id) &&
                            (name.Contains("AdGuard", StringComparison.OrdinalIgnoreCase) ||
                             name.StartsWith("__MSG_", StringComparison.OrdinalIgnoreCase) ||
                             string.IsNullOrWhiteSpace(_adGuardExtensionId)))
                        {
                            _adGuardExtensionId = id;
                            ResolveAdGuardExtensionPopupAndOptionsUrls(id, GetAdGuardUnpackedExtensionFolder());
                        }
                        results.Add(new Nexus.Contracts.BrowserAgent.InstalledBrowserExtensionInfo(
                            Id: id,
                            Name: name,
                            Version: "MV3",
                            IsEnabled: isEnabled,
                            FolderPath: System.IO.Path.Combine(rootDir, id),
                            SourceKind: "WebView2Profile"));
                    }
                }
            }
        }
        catch { }

        return results;
    }

    private async Task DownloadAndInstallOfficialAdGuardFromChromeWebStoreAsync()
    {
        try
        {
            string targetDir = GetAdGuardUnpackedExtensionFolder();
            System.IO.Directory.CreateDirectory(targetDir);

            // 1. Check if AdGuard AdBlocker (pdffkfellgipmhklpdmokmckkkfcopbh) is already installed in user's Microsoft Edge!
            if (TryCopyInstalledAdGuardFromLocalMicrosoftEdge(targetDir))
            {
                await RegisterExtensionFolderInWebView2ProfileAsync(targetDir);
                return;
            }

            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36 Edg/128.0.0.0");

            // 2. Try Official Microsoft Edge Add-ons CRX endpoint first (pdffkfellgipmhklpdmokmckkkfcopbh), then Chrome Web Store (bgnkhhnnamicmpeenaelnjfhikgbkllg)
            byte[]? packageBytes = null;
            string[] crxEndpoints = new[]
            {
                "https://edge.microsoft.com/extensionwebstorebase/v1/crx?response=redirect&x=id%3Dpdffkfellgipmhklpdmokmckkkfcopbh%26installsource%3Dondemand%26uc",
                "https://clients2.google.com/service/update2/crx?response=redirect&prodversion=128.0.0.0&acceptformat=crx2,crx3&x=id%3Dbgnkhhnnamicmpeenaelnjfhikgbkllg%26installsource%3Dondemand%26uc"
            };
            foreach (string endpoint in crxEndpoints)
            {
                try
                {
                    var bytes = await http.GetByteArrayAsync(endpoint);
                    if (bytes is not null && bytes.Length > 10000)
                    {
                        packageBytes = bytes;
                        break;
                    }
                }
                catch { }
            }

            if (packageBytes is not null && packageBytes.Length > 10000)
            {
                try
                {
                    foreach (var f in System.IO.Directory.GetFiles(targetDir)) System.IO.File.Delete(f);
                    foreach (var d in System.IO.Directory.GetDirectories(targetDir)) System.IO.Directory.Delete(d, true);
                }
                catch { }

                ExtractCrxOrZipBytesToFolder(packageBytes, targetDir);
                await RegisterExtensionFolderInWebView2ProfileAsync(targetDir);
            }
        }
        catch { }
    }

    public static void ExtractCrxOrZipBytesToFolder(byte[] bytes, string destinationFolder)
    {
        System.IO.Directory.CreateDirectory(destinationFolder);
        int zipOffset = 0;
        string? extractedPubKeyBase64 = null;

        if (bytes.Length > 16 && bytes[0] == (byte)'C' && bytes[1] == (byte)'r' && bytes[2] == (byte)'2' && bytes[3] == (byte)'4')
        {
            int version = BitConverter.ToInt32(bytes, 4);
            if (version == 3)
            {
                int headerLen = BitConverter.ToInt32(bytes, 8);
                if (headerLen > 0 && 12 + headerLen < bytes.Length - 4)
                {
                    zipOffset = 12 + headerLen;
                    extractedPubKeyBase64 = TryExtractPublicKeyBase64FromCrx3Header(bytes, headerLen);
                }
            }
            else if (version == 2 && bytes.Length > 20)
            {
                int pubKeyLen = BitConverter.ToInt32(bytes, 8);
                int sigLen = BitConverter.ToInt32(bytes, 12);
                if (pubKeyLen >= 0 && sigLen >= 0 && 16 + pubKeyLen + sigLen < bytes.Length - 4)
                {
                    zipOffset = 16 + pubKeyLen + sigLen;
                    if (pubKeyLen > 0)
                    {
                        byte[] pk = new byte[pubKeyLen];
                        Array.Copy(bytes, 16, pk, 0, pubKeyLen);
                        extractedPubKeyBase64 = Convert.ToBase64String(pk);
                    }
                }
            }

            if (zipOffset == 0 || bytes[zipOffset] != 0x50 || bytes[zipOffset + 1] != 0x4B)
            {
                for (int i = 12; i < Math.Min(bytes.Length - 4, 131072); i++)
                {
                    if (bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 0x03 && bytes[i + 3] == 0x04)
                    {
                        zipOffset = i;
                        break;
                    }
                }
            }
        }

        string tempZip = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexus_ext_unpack_" + Guid.NewGuid().ToString("N") + ".zip");
        using (var fs = new System.IO.FileStream(tempZip, System.IO.FileMode.Create, System.IO.FileAccess.Write))
        {
            fs.Write(bytes, zipOffset, bytes.Length - zipOffset);
        }
        System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, destinationFolder, overwriteFiles: true);
        try { System.IO.File.Delete(tempZip); } catch { }

        UnlockManifestWebAccessibleResources(destinationFolder, extractedPubKeyBase64);
    }

    private async Task RegisterExtensionFolderInWebView2ProfileAsync(string extensionFolder)
    {
        try
        {
            if (!System.IO.Directory.Exists(extensionFolder)) return;
            await Task.Run(() => EnsureCleanUnmodifiedAdGuardExtension(extensionFolder));

            var coreWv = _activeWebView2Control?.GetType().GetProperty("CoreWebView2")?.GetValue(_activeWebView2Control);
            var profileObj = coreWv?.GetType().GetProperty("Profile")?.GetValue(coreWv);
            if (profileObj is null) return;

            // Clear any stale/corrupted registration from before _metadata was removed, then re-add and EnableAsync(true) (WebView2Feedback#5113)
            try
            {
                var getExtsMethod = profileObj.GetType().GetMethod("GetBrowserExtensionsAsync", Type.EmptyTypes);
                if (getExtsMethod?.Invoke(profileObj, null) is Task getTask)
                {
                    await getTask;
                    if (getTask.GetType().GetProperty("Result")?.GetValue(getTask) is System.Collections.IEnumerable list)
                    {
                        foreach (object item in list)
                        {
                            string id = item.GetType().GetProperty("Id")?.GetValue(item) as string ?? "";
                            string name = item.GetType().GetProperty("Name")?.GetValue(item) as string ?? "";
                            if (id == EdgeStoreAdGuardId || id == ChromeStoreAdGuardId || name.Contains("AdGuard", StringComparison.OrdinalIgnoreCase))
                            {
                                bool isEn = item.GetType().GetProperty("IsEnabled")?.GetValue(item) is bool b ? b : false;
                                if (!isEn)
                                {
                                    if (item.GetType().GetMethod("RemoveAsync")?.Invoke(item, null) is Task remTask)
                                        await remTask;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            var addExtMethod = profileObj.GetType().GetMethod("AddBrowserExtensionAsync", new Type[] { typeof(string) });
            if (addExtMethod?.Invoke(profileObj, new object[] { extensionFolder }) is Task addTask)
            {
                await addTask;
                var extObj = addTask.GetType().GetProperty("Result")?.GetValue(addTask);
                if (extObj is not null)
                {
                    var enableMethod = extObj.GetType().GetMethod("EnableAsync", new Type[] { typeof(bool) });
                    if (enableMethod?.Invoke(extObj, new object[] { true }) is Task enTask)
                        await enTask;

                    string? extId = extObj.GetType().GetProperty("Id")?.GetValue(extObj) as string;
                    if (!string.IsNullOrWhiteSpace(extId))
                    {
                        _adGuardExtensionId = extId!;
                        ResolveAdGuardExtensionPopupAndOptionsUrls(extId!, extensionFolder);
                    }
                }
            }
        }
        catch { }
    }

    private string GetCurrentBrowserHostDomain()
    {
        string rawUrl = BrowserAddressBox?.Text ?? string.Empty;
        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var u) && !string.IsNullOrWhiteSpace(u.Host))
            return u.Host.ToLowerInvariant();
        return "halaman-aktif";
    }

    private void UpdateActiveBrowserTabInPlace(string title, string url)
    {
        try
        {
            var rt = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
            string cleanTitle = string.IsNullOrWhiteSpace(title) ? url : title;

            if (rt.OpenBrowserTabs.Count == 0)
            {
                var firstTab = new Nexus.Contracts.BrowserAgent.BrowserTabSessionItem("tab-main", cleanTitle, url, "globe", false);
                rt.OpenBrowserTabs.Add(firstTab);
                rt.ActiveBrowserTabId = firstTab.TabId;
            }
            else
            {
                // Prefer the currently active tab if it is a normal web tab (not Settings or AdGuard Settings)
                var activeTab = rt.OpenBrowserTabs.FirstOrDefault(t =>
                    t.TabId == rt.ActiveBrowserTabId &&
                    !t.IsInternalPage &&
                    !string.Equals(t.InternalPageKind, "adguard_options", StringComparison.OrdinalIgnoreCase));

                if (activeTab is null)
                {
                    activeTab = rt.OpenBrowserTabs.FirstOrDefault(t =>
                        !t.IsInternalPage &&
                        !string.Equals(t.InternalPageKind, "adguard_options", StringComparison.OrdinalIgnoreCase));
                }

                if (activeTab is null)
                {
                    var newWebTab = new Nexus.Contracts.BrowserAgent.BrowserTabSessionItem(
                        "tab-" + Guid.NewGuid().ToString("N")[..6],
                        cleanTitle,
                        url,
                        "globe",
                        false);
                    rt.OpenBrowserTabs.Insert(0, newWebTab);
                    rt.ActiveBrowserTabId = newWebTab.TabId;
                }
                else
                {
                    int idx = rt.OpenBrowserTabs.IndexOf(activeTab);
                    rt.OpenBrowserTabs[idx] = activeTab with
                    {
                        Title = cleanTitle,
                        Url = url,
                        IconKind = "globe",
                        IsInternalPage = false,
                        InternalPageKind = null
                    };
                    rt.ActiveBrowserTabId = rt.OpenBrowserTabs[idx].TabId;
                }
            }
            RebuildMultiTabBrowserStrip();
        }
        catch { }
    }

    private void OpenOrActivateBrowserTab(string title, string url, string iconKind, bool isInternalPage, string? internalKind = null, string? fallbackHtml = null)
    {
        try
        {
            var rt = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
            if (rt.OpenBrowserTabs.Count == 0)
            {
                string curUrl = BrowserAddressBox?.Text ?? "https://www.google.com";
                if (string.IsNullOrWhiteSpace(curUrl) ||
                    curUrl.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
                    curUrl.StartsWith("extension://", StringComparison.OrdinalIgnoreCase) ||
                    curUrl.StartsWith("Settings", StringComparison.OrdinalIgnoreCase))
                {
                    curUrl = "https://www.google.com";
                }
                rt.OpenBrowserTabs.Add(new Nexus.Contracts.BrowserAgent.BrowserTabSessionItem(
                    "tab-main",
                    "Google",
                    curUrl,
                    "globe",
                    false));
            }

            Nexus.Contracts.BrowserAgent.BrowserTabSessionItem? existing = null;
            if (!string.IsNullOrEmpty(internalKind))
            {
                existing = rt.OpenBrowserTabs.FirstOrDefault(t =>
                    string.Equals(t.InternalPageKind, internalKind, StringComparison.OrdinalIgnoreCase));
            }
            else if (url != "https://www.google.com" || title != "New tab")
            {
                existing = rt.OpenBrowserTabs.FirstOrDefault(t =>
                    string.IsNullOrEmpty(t.InternalPageKind) &&
                    string.Equals(t.Url, url, StringComparison.OrdinalIgnoreCase));
            }

            if (existing is not null)
            {
                int idx = rt.OpenBrowserTabs.IndexOf(existing);
                existing = existing with
                {
                    Title = title,
                    Url = url,
                    IconKind = iconKind,
                    IsInternalPage = isInternalPage,
                    InternalPageKind = internalKind
                };
                rt.OpenBrowserTabs[idx] = existing;
            }
            else
            {
                existing = new Nexus.Contracts.BrowserAgent.BrowserTabSessionItem(
                    "tab-" + Guid.NewGuid().ToString("N")[..6],
                    title,
                    url,
                    iconKind,
                    isInternalPage,
                    internalKind);
                rt.OpenBrowserTabs.Add(existing);
            }

            rt.ActiveBrowserTabId = existing.TabId;
            RebuildMultiTabBrowserStrip();

            if (!string.IsNullOrWhiteSpace(fallbackHtml))
            {
                NavigateWebView2DirectSchemeSafe("file:///nexus/" + (internalKind ?? "page"), fallbackHtml);
                if (BrowserAddressBox is not null)
                    BrowserAddressBox.Text = url;
            }
            else
            {
                NavigateWebView2DirectSchemeSafe(url, fallbackHtml: null);
                if (BrowserAddressBox is not null)
                    BrowserAddressBox.Text = url;
            }
        }
        catch { }
    }

    private void RebuildMultiTabBrowserStrip()
    {
        try
        {
            if (_browserMultiTabStrip is null) return;
            _browserMultiTabStrip.Children.Clear();

            var rt = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
            if (rt.OpenBrowserTabs.Count <= 1)
            {
                _browserMultiTabStrip.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }
            _browserMultiTabStrip.Visibility = System.Windows.Visibility.Visible;

            var adGuardGreen = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(104, 188, 113));
            var textDark = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 34, 34));
            var textMuted = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128));
            var borderSubtle = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 231, 235));
            var activeBg = System.Windows.Media.Brushes.White;
            var inactiveBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 244, 246));

            foreach (var tab in rt.OpenBrowserTabs.ToList())
            {
                bool isActive = tab.TabId == rt.ActiveBrowserTabId;
                var chipBorder = new System.Windows.Controls.Border
                {
                    Height = 28,
                    CornerRadius = new System.Windows.CornerRadius(8),
                    BorderThickness = new System.Windows.Thickness(1),
                    Padding = new System.Windows.Thickness(8, 0, 6, 0),
                    Margin = new System.Windows.Thickness(0, 0, 6, 0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                chipBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, isActive ? "DshCardBgBrush" : "DshSidebarBgBrush");
                chipBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "DshBorderL2Brush");
                var row = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };

                System.Windows.UIElement ico = tab.IconKind == "adguard"
                    ? CreateNexusVectorIcon("M 12,2 L 4,5 V 11.09 C 4,16.14 7.41,20.85 12,22 C 16.59,20.85 20,16.14 20,11.09 V 5 L 12,2 Z M 10,17 L 6,13 L 7.41,11.59 L 10,14.17 L 16.59,7.58 L 18,9 L 10,17 Z", "NexusAccentBrush", adGuardGreen, 13)
                    : CreateNexusStrokeVectorIcon("M 12,2 A 10,10 0 1 0 12,22 A 10,10 0 1 0 12,2 Z M 2,12 H 22 M 12,2 A 15.3,15.3 0 0 1 16,12 A 15.3,15.3 0 0 1 12,22 A 15.3,15.3 0 0 1 8,12 A 15.3,15.3 0 0 1 12,2 Z", textMuted, 1.4, 13);
                row.Children.Add(ico);

                var titleTb = new System.Windows.Controls.TextBlock
                {
                    Text = tab.Title.Length > 22 ? tab.Title[..22] + "…" : tab.Title,
                    FontSize = 12,
                    FontWeight = isActive ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal,
                    Margin = new System.Windows.Thickness(6, 0, 6, 0)
                };
                titleTb.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, isActive ? "DshTextPrimaryBrush" : "DshTextSecondaryBrush");
                var titleBtn = new System.Windows.Controls.Button
                {
                    Content = titleTb,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                ApplyZeroClipToolbarButtonStyle(titleBtn);
                string capturedId = tab.TabId;
                titleBtn.Click += (_, _) => ActivateExistingBrowserTab(capturedId);
                row.Children.Add(titleBtn);

                var closeTabBtn = new System.Windows.Controls.Button
                {
                    Content = new System.Windows.Controls.TextBlock { Text = "×", FontSize = 13, Foreground = textMuted },
                    Width = 18,
                    Height = 18,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Close tab"
                };
                ApplyZeroClipToolbarButtonStyle(closeTabBtn);
                closeTabBtn.Click += (_, _) => CloseBrowserTabById(capturedId);
                row.Children.Add(closeTabBtn);

                chipBorder.Child = row;
                _browserMultiTabStrip.Children.Add(chipBorder);
            }

            var plusTabBtn = new System.Windows.Controls.Button
            {
                Content = new System.Windows.Controls.TextBlock { Text = "+", FontSize = 15, FontWeight = System.Windows.FontWeights.Medium, Foreground = textMuted },
                Width = 26,
                Height = 26,
                Background = System.Windows.Media.Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "New tab"
            };
            ApplyZeroClipToolbarButtonStyle(plusTabBtn);
            plusTabBtn.Click += (_, _) => OpenOrActivateBrowserTab("New tab", "https://www.google.com", "globe", false);
            _browserMultiTabStrip.Children.Add(plusTabBtn);
        }
        catch { }
    }

    private void ActivateExistingBrowserTab(string tabId)
    {
        try
        {
            var rt = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
            var tab = rt.OpenBrowserTabs.FirstOrDefault(t => t.TabId == tabId);
            if (tab is null) return;
            rt.ActiveBrowserTabId = tab.TabId;
            RebuildMultiTabBrowserStrip();

            if (string.Equals(tab.InternalPageKind, "adguard_options", StringComparison.OrdinalIgnoreCase))
            {
                NavigateWebView2DirectSchemeSafe("file:///nexus/adguard_options", GetGenuineAdGuardOptionsWebUiHtml());
                if (BrowserAddressBox is not null)
                    BrowserAddressBox.Text = tab.Url;
            }
            else if (tab.IsInternalPage)
            {
                string sec = "general";
                if (tab.Url.Contains("Browsing history", StringComparison.OrdinalIgnoreCase)) sec = "history";
                else if (tab.Url.Contains("Download", StringComparison.OrdinalIgnoreCase)) sec = "downloads";
                else if (tab.Url.Contains("password", StringComparison.OrdinalIgnoreCase)) sec = "passwords";
                else if (tab.Url.Contains("Import", StringComparison.OrdinalIgnoreCase)) sec = "import";
                else if (tab.Url.Contains("Clear", StringComparison.OrdinalIgnoreCase)) sec = "clear";
                else if (tab.Url.Contains("Extensions", StringComparison.OrdinalIgnoreCase)) sec = "extensions";

                NavigateWebView2DirectSchemeSafe("file:///nexus/settings_" + sec, GetNexusBrowserSettingsHubHtml(sec));
                if (BrowserAddressBox is not null)
                    BrowserAddressBox.Text = tab.Url;
            }
            else
            {
                NavigateWebView2DirectSchemeSafe(tab.Url, fallbackHtml: null);
                if (BrowserAddressBox is not null)
                    BrowserAddressBox.Text = tab.Url;
            }
        }
        catch { }
    }

    private void CloseBrowserTabById(string tabId)
    {
        try
        {
            var rt = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
            int idx = rt.OpenBrowserTabs.FindIndex(t => t.TabId == tabId);
            if (idx < 0) return;
            rt.OpenBrowserTabs.RemoveAt(idx);
            if (rt.OpenBrowserTabs.Count == 0)
            {
                RebuildMultiTabBrowserStrip();
                return;
            }
            var nextTab = rt.OpenBrowserTabs[Math.Clamp(idx - 1, 0, rt.OpenBrowserTabs.Count - 1)];
            ActivateExistingBrowserTab(nextTab.TabId);
        }
        catch { }
    }

    private async void OpenNexusBrowserSettingsHubInTab(string section = "general", string? customTitle = null)
    {
        try
        {
            if (_activeWebView2Control is null || !_webView2EnvironmentInitialized)
            {
                await NavigateViaWebView2ChromiumAsync("https://www.google.com");
            }
            if (_browserMoreMenuPopup is not null) _browserMoreMenuPopup.IsOpen = false;
            if (_extensionsPopup is not null) _extensionsPopup.IsOpen = false;
            if (_adGuardShieldPopup is not null) _adGuardShieldPopup.IsOpen = false;

            if (DataContext is Nexus.App.ViewModels.AgentWorkspaceViewModel wsVm)
            {
                wsVm.IsOpen = true;
            }

            string title = customTitle ?? section switch
            {
                "history" => "Browsing history",
                "downloads" => "Download history",
                "passwords" => "Manage passwords",
                "import" => "Import browser data",
                "clear" => "Clear browsing data",
                "extensions" => "Extensions",
                _ => "Browser settings"
            };
            string displayUrl = $"Settings › Browser › {title}";
            // SINGLETON SETTINGS TAB: All Browser Settings sections reuse "nexus_settings_hub" so switching sections never spawns extra tabs!
            OpenOrActivateBrowserTab(
                title,
                displayUrl,
                "globe",
                isInternalPage: true,
                internalKind: "nexus_settings_hub",
                fallbackHtml: GetNexusBrowserSettingsHubHtml(section));
        }
        catch { }
    }

    private async Task OpenAdGuardInternalPageAsync(bool openOptionsPage)
    {
        try
        {
            if (_adGuardShieldPopup is not null) _adGuardShieldPopup.IsOpen = false;
            if (_extensionsPopup is not null) _extensionsPopup.IsOpen = false;

            if (DataContext is Nexus.App.ViewModels.AgentWorkspaceViewModel wsVm)
            {
                wsVm.IsOpen = true;
            }

            if (_activeWebView2Control is null || !_webView2EnvironmentInitialized)
            {
                await NavigateViaWebView2ChromiumAsync("https://www.google.com");
            }

            string displayUrl = "Settings › AdGuard AdBlocker";
            string tabTitle = openOptionsPage ? "AdGuard Settings" : "AdGuard Popup";
            string htmlPayload = openOptionsPage
                ? GetGenuineAdGuardOptionsWebUiHtml()
                : GetGenuineAdGuardPopupWebUiHtml(GetCurrentBrowserHostDomain());

            // SINGLETON ADGUARD SETTINGS TAB: Reuses "adguard_options" so clicking gear never creates duplicate AdGuard tabs!
            OpenOrActivateBrowserTab(
                tabTitle,
                displayUrl,
                "adguard",
                isInternalPage: true,
                internalKind: "adguard_options",
                fallbackHtml: htmlPayload);
        }
        catch { }
    }

    private async Task ToggleAdGuardProtectionAsync()
    {
        try
        {
            var cfg = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
            cfg.IsAdGuardProtectionEnabled = !cfg.IsAdGuardProtectionEnabled;
            bool enabled = cfg.IsAdGuardProtectionEnabled;

            await ExecuteLiveScriptOnWebView2Async(
                enabled
                    ? "window.__nexusAdGuardDisabledForDomain = false;"
                    : "window.__nexusAdGuardDisabledForDomain = true;");
            RefreshAdGuardFlyoutState();
        }
        catch { }
    }

    private async Task ToggleAdGuardWhitelistForCurrentSiteAsync()
    {
        try
        {
            string host = GetCurrentBrowserHostDomain();
            var cfg = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
            var existing = cfg.AdGuardWhitelistedDomains.FirstOrDefault(d => string.Equals(d, host, StringComparison.OrdinalIgnoreCase));
            bool isNowWhitelisted;
            if (existing is not null)
            {
                cfg.AdGuardWhitelistedDomains.Remove(existing);
                isNowWhitelisted = false;
            }
            else
            {
                cfg.AdGuardWhitelistedDomains.Add(host);
                isNowWhitelisted = true;
            }
            cfg.AdGuardConfig.AllowlistText = string.Join("\r\n", cfg.AdGuardWhitelistedDomains);
            cfg.SaveAdGuardConfigToDisk();

            await ExecuteLiveScriptOnWebView2Async(
                isNowWhitelisted
                    ? "window.__nexusAdGuardDisabledForDomain = true;"
                    : "window.__nexusAdGuardDisabledForDomain = false;");
            RefreshAdGuardFlyoutState();
        }
        catch { }
    }

    private void OpenBrowsingHistoryPageInBrowser() => OpenNexusBrowserSettingsHubInTab("history", "Browsing history");

    private void RefreshAdGuardFlyoutState()
    {
        try
        {
            var cfg = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
            string host = GetCurrentBrowserHostDomain();
            bool isWhitelisted = cfg.AdGuardWhitelistedDomains.Any(d => string.Equals(d, host, StringComparison.OrdinalIgnoreCase));
            bool active = cfg.IsAdGuardProtectionEnabled && !isWhitelisted;

            if (_adGuardFlyoutDomainText is not null)
                _adGuardFlyoutDomainText.Text = host == "halaman-aktif" ? "Browser Aktif" : host;

            if (_adGuardFlyoutExtIdText is not null)
                _adGuardFlyoutExtIdText.Text = !cfg.IsAdGuardProtectionEnabled
                    ? "Perlindungan Nonaktif"
                    : (isWhitelisted ? $"Dijeda di {host}" : "Perlindungan Aktif");

            if (_adGuardFlyoutStatsText is not null)
                _adGuardFlyoutStatsText.Text = $"Total diblokir: {Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.AdGuardConfig.TotalBlockedAdsCount + _adGuardBlockedCount:N0}";

            if (_adGuardShieldBtn is not null)
            {
                _adGuardShieldBtn.Content = CreateNexusVectorIcon(
                    "M 12,2 L 4,5 V 11.09 C 4,16.14 7.41,20.85 12,22 C 16.59,20.85 20,16.14 20,11.09 V 5 L 12,2 Z M 10,17 L 6,13 L 7.41,11.59 L 10,14.17 L 16.59,7.58 L 18,9 L 10,17 Z",
                    active ? "NexusAccentBrush" : "NexusForegroundSecondary",
                    active ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(104, 188, 113)) : System.Windows.Media.Brushes.DimGray,
                    16.5);
            }
        }
        catch { }
    }

    private async Task ClearWebView2BrowsingHistoryAsync(bool clearCookies = false)
    {
        try
        {
            if (_activeWebView2Control is not null)
            {
                try
                {
                    var coreWv = _activeWebView2Control.GetType().GetProperty("CoreWebView2")?.GetValue(_activeWebView2Control);
                    var profileObj = coreWv?.GetType().GetProperty("Profile")?.GetValue(coreWv);
                    if (profileObj is not null)
                    {
                        var clearMethods = profileObj.GetType().GetMethods()
                            .Where(m => m.Name == "ClearBrowsingDataAsync")
                            .ToList();
                        var enumMethod = clearMethods.FirstOrDefault(m => m.GetParameters().Length == 1);
                        if (enumMethod is not null)
                        {
                            var paramType = enumMethod.GetParameters()[0].ParameterType;
                            int mask = clearCookies ? (2 | 16 | 64 | 2048 | 4096) : (2 | 16 | 2048 | 4096);
                            object enumVal = Enum.ToObject(paramType, mask);
                            if (enumMethod.Invoke(profileObj, new object[] { enumVal }) is Task clearTask)
                            {
                                await clearTask;
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private System.Windows.Controls.Border? _findInPageFloatingBar;
    private System.Windows.Controls.TextBlock? _zoomPercentLabel;

    private System.Windows.Controls.Button CreateCleanMenuRowButton(
        string label,
        string? rightGlyph,
        System.Windows.Media.Brush fg,
        System.Windows.Media.Brush mutedFg,
        string? leftIconPath = null,
        System.Windows.Media.Brush? leftIconBrush = null)
    {
        var btn = new System.Windows.Controls.Button
        {
            Style = null,
            Template = CreateZeroPaddingFlatButtonTemplate(),
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new System.Windows.Thickness(0),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            Cursor = System.Windows.Input.Cursors.Hand,
            Padding = new System.Windows.Thickness(10, 7, 10, 7)
        };
        var dock = new System.Windows.Controls.DockPanel { LastChildFill = true };
        if (!string.IsNullOrWhiteSpace(leftIconPath))
        {
            var ico = CreateNexusVectorIcon(
                leftIconPath!,
                "NexusForegroundSecondary",
                leftIconBrush ?? mutedFg,
                15);
            ico.Margin = new System.Windows.Thickness(0, 0, 10, 0);
            System.Windows.Controls.DockPanel.SetDock(ico, System.Windows.Controls.Dock.Left);
            dock.Children.Add(ico);
        }
        if (!string.IsNullOrWhiteSpace(rightGlyph))
        {
            var rTxt = new System.Windows.Controls.TextBlock
            {
                Text = rightGlyph,
                FontSize = 12,
                Foreground = mutedFg,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            rTxt.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "DshTextSecondaryBrush");
            System.Windows.Controls.DockPanel.SetDock(rTxt, System.Windows.Controls.Dock.Right);
            dock.Children.Add(rTxt);
        }
        var lblTxt = new System.Windows.Controls.TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = fg,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        lblTxt.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "DshTextPrimaryBrush");
        dock.Children.Add(lblTxt);
        btn.Content = dock;
        return btn;
    }

    private void ShowFloatingFindInPageBar(System.Windows.Controls.Grid parentGrid)
    {
        try
        {
            if (_findInPageFloatingBar is null)
            {
                _findInPageFloatingBar = new System.Windows.Controls.Border
                {
                    Width = 340,
                    Height = 40,
                    CornerRadius = new System.Windows.CornerRadius(20),
                    Background = System.Windows.Media.Brushes.White,
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 231, 235)),
                    BorderThickness = new System.Windows.Thickness(1),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top,
                    Margin = new System.Windows.Thickness(0, 48, 0, 0),
                    Padding = new System.Windows.Thickness(14, 0, 10, 0)
                };
                _findInPageFloatingBar.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "DshCardBgBrush");
                _findInPageFloatingBar.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "DshBorderL2Brush");
                System.Windows.Controls.Panel.SetZIndex(_findInPageFloatingBar, 95);

                var dock = new System.Windows.Controls.DockPanel { LastChildFill = true };
                var searchIco = CreateNexusVectorIcon(
                    "M 15.5,14 H 14.71 L 14.43,13.73 C 15.41,12.59 16,11.11 16,9.5 C 16,5.91 13.09,3 9.5,3 C 5.91,3 3,5.91 3,9.5 C 3,13.09 5.91,16 9.5,16 C 11.11,16 12.59,15.41 13.73,14.43 L 14,14.71 V 15.5 L 19,20.49 L 20.49,19 L 15.5,14 Z M 9.5,14 C 7.01,14 5,11.99 5,9.5 C 5,7.01 7.01,5 9.5,5 C 11.99,5 14,7.01 14,9.5 C 14,11.99 11.99,14 9.5,14 Z",
                    "NexusForegroundSecondary",
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128)),
                    15);
                searchIco.Margin = new System.Windows.Thickness(0, 0, 8, 0);
                System.Windows.Controls.DockPanel.SetDock(searchIco, System.Windows.Controls.Dock.Left);
                dock.Children.Add(searchIco);

                var closeFindBtn = new System.Windows.Controls.Button
                {
                    Content = CreateNexusVectorIcon(
                        "M 19,6.41 L 17.59,5 L 12,10.59 L 6.41,5 L 5,6.41 L 10.59,12 L 5,17.59 L 6.41,19 L 12,13.41 L 17.59,19 L 19,17.59 L 13.41,12 L 19,6.41 Z",
                        "NexusForegroundSecondary",
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128)),
                        13),
                    Width = 26,
                    Height = 26,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                ApplyZeroClipToolbarButtonStyle(closeFindBtn);
                closeFindBtn.Click += (_, _) => _findInPageFloatingBar.Visibility = System.Windows.Visibility.Collapsed;
                System.Windows.Controls.DockPanel.SetDock(closeFindBtn, System.Windows.Controls.Dock.Right);
                dock.Children.Add(closeFindBtn);

                var findBox = new System.Windows.Controls.TextBox
                {
                    BorderThickness = new System.Windows.Thickness(0),
                    Background = System.Windows.Media.Brushes.Transparent,
                    VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                    FontSize = 13.5,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 24, 39)),
                    Tag = "Find in page..."
                };
                findBox.KeyDown += async (_, ke) =>
                {
                    if (ke.Key == System.Windows.Input.Key.Enter && !string.IsNullOrWhiteSpace(findBox.Text))
                    {
                        string escaped = findBox.Text.Replace("\\", "\\\\").Replace("'", "\\'");
                        await ExecuteLiveScriptOnWebView2Async($"window.find('{escaped}');");
                    }
                    else if (ke.Key == System.Windows.Input.Key.Escape)
                    {
                        _findInPageFloatingBar.Visibility = System.Windows.Visibility.Collapsed;
                    }
                };
                dock.Children.Add(findBox);
                _findInPageFloatingBar.Child = dock;
                parentGrid.Children.Add(_findInPageFloatingBar);
            }
            _findInPageFloatingBar.Visibility = System.Windows.Visibility.Visible;
        }
        catch { }
    }

    private void EnsureChromeExtensionsToolbarButtonAttached()
    {
        string extFolder = GetAdGuardUnpackedExtensionFolder();
        Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.EnsureHistoryLoadedFromDisk();
        Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.EnsureAdGuardConfigLoadedFromDisk();
        try
        {
            Environment.SetEnvironmentVariable(
                "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
                $"--remote-debugging-port=9222 --enable-features=msWebView2BrowserExtensionsEnabled --load-extension=\"{extFolder}\"");
        }
        catch { }

        Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.LiveJsEvaluator = ExecuteLiveScriptOnWebView2Async;
        Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.LiveCdpCaller = CallLiveCdpMethodOnWebView2Async;
        Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.ClearBrowserSessionHistoryAsync = ClearWebView2BrowsingHistoryAsync;

        if (_extensionsToolbarAttached) return;
        try
        {
            var addressBox = this.FindName("BrowserAddressBox") as System.Windows.Controls.TextBox
                             ?? this.FindName("BrowserAddressTextBox") as System.Windows.Controls.TextBox;
            if (addressBox?.Parent is System.Windows.Controls.Grid addressGrid)
            {
                _extensionsToolbarAttached = true;
                Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.OpenAdGuardExtensionPageInBrowserAsync =
                    mode => OpenAdGuardInternalPageAsync(string.Equals(mode, "options", StringComparison.OrdinalIgnoreCase));
                ResolveAdGuardExtensionPopupAndOptionsUrls(_adGuardExtensionId, extFolder);

                var adGuardGreen = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(104, 188, 113));
                var cardWhite = System.Windows.Media.Brushes.White;
                var textDark = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 34, 34));
                var textMuted = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128));
                var placeholderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(142, 149, 162));
                var borderSubtle = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 231, 235));
                var bgSoftGray = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 244, 246));
                var coralRed = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 113, 113));
                var amberGold = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36));

                System.Windows.Controls.Grid? toolbarRowGrid = null;
                System.Windows.DependencyObject? curParent = addressGrid.Parent;
                while (curParent is not null)
                {
                    if (curParent is System.Windows.Controls.Grid g && g.Children.Count >= 2)
                    {
                        toolbarRowGrid = g;
                        break;
                    }
                    curParent = System.Windows.Media.VisualTreeHelper.GetParent(curParent)
                                ?? (curParent as System.Windows.FrameworkElement)?.Parent;
                }

                addressGrid.Children.Remove(addressBox);
                addressBox.BorderThickness = new System.Windows.Thickness(0);
                addressBox.Background = System.Windows.Media.Brushes.Transparent;
                addressBox.Margin = new System.Windows.Thickness(0);
                addressBox.Padding = new System.Windows.Thickness(0);
                addressBox.VerticalContentAlignment = System.Windows.VerticalAlignment.Center;
                addressBox.FontSize = 13.5;

                if (toolbarRowGrid is not null)
                {
                    foreach (System.Windows.UIElement child in toolbarRowGrid.Children)
                    {
                        child.Visibility = System.Windows.Visibility.Collapsed;
                    }
                }
                else
                {
                    toolbarRowGrid = addressGrid;
                    foreach (System.Windows.UIElement child in toolbarRowGrid.Children)
                    {
                        child.Visibility = System.Windows.Visibility.Collapsed;
                    }
                }

                var masterHeaderStack = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Vertical,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                System.Windows.Controls.Grid.SetColumn(masterHeaderStack, 0);
                System.Windows.Controls.Grid.SetColumnSpan(masterHeaderStack, 20);
                System.Windows.Controls.Panel.SetZIndex(masterHeaderStack, 30);

                _browserMultiTabStrip = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    Margin = new System.Windows.Thickness(2, 0, 2, 6),
                    Visibility = System.Windows.Visibility.Collapsed
                };
                masterHeaderStack.Children.Add(_browserMultiTabStrip);

                var modernToolbarGrid = new System.Windows.Controls.Grid
                {
                    Height = 38,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    Margin = new System.Windows.Thickness(2, 0, 2, 0)
                };

                modernToolbarGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
                modernToolbarGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                modernToolbarGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
                modernToolbarGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

                // COLUMN 0: Left Navigation Capsule Pill [ ←   →  |  ↻ ]
                var navCapsuleBorder = new System.Windows.Controls.Border
                {
                    Height = 36,
                    CornerRadius = new System.Windows.CornerRadius(18),
                    BorderThickness = new System.Windows.Thickness(1),
                    Padding = new System.Windows.Thickness(6, 0, 6, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                navCapsuleBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "DshCardBgBrush");
                navCapsuleBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "DshBorderL2Brush");
                var navStack = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };

                _navBackBtn = new System.Windows.Controls.Button
                {
                    Content = CreateNexusStrokeVectorIcon("M 19,12 H 5 M 11,6 L 5,12 L 11,18", textDark, 1.75, 14),
                    Width = 28,
                    Height = 28,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Back (Alt+Left)"
                };
                ApplyZeroClipToolbarButtonStyle(_navBackBtn);
                _navBackBtn.Click += (_, e) =>
                {
                    try
                    {
                        var coreWv = _activeWebView2Control?.GetType().GetProperty("CoreWebView2")?.GetValue(_activeWebView2Control);
                        if (coreWv?.GetType().GetProperty("CanGoBack")?.GetValue(coreWv) is true)
                        {
                            coreWv.GetType().GetMethod("GoBack")?.Invoke(coreWv, null);
                            return;
                        }
                        var m = this.GetType().GetMethod("BrowserBack_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        m?.Invoke(this, new object?[] { _navBackBtn, e });
                    }
                    catch { }
                };

                _navFwdBtn = new System.Windows.Controls.Button
                {
                    Content = CreateNexusStrokeVectorIcon("M 5,12 H 19 M 13,6 L 19,12 L 13,18", textDark, 1.75, 14),
                    Width = 28,
                    Height = 28,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Forward (Alt+Right)"
                };
                ApplyZeroClipToolbarButtonStyle(_navFwdBtn);
                _navFwdBtn.Click += (_, e) =>
                {
                    try
                    {
                        var coreWv = _activeWebView2Control?.GetType().GetProperty("CoreWebView2")?.GetValue(_activeWebView2Control);
                        if (coreWv?.GetType().GetProperty("CanGoForward")?.GetValue(coreWv) is true)
                        {
                            coreWv.GetType().GetMethod("GoForward")?.Invoke(coreWv, null);
                            return;
                        }
                        var m = this.GetType().GetMethod("BrowserForward_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        m?.Invoke(this, new object?[] { _navFwdBtn, e });
                    }
                    catch { }
                };

                var navDivider = new System.Windows.Controls.Border
                {
                    Width = 1,
                    Height = 16,
                    Background = borderSubtle,
                    Margin = new System.Windows.Thickness(4, 0, 4, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };

                _navReloadBtn = new System.Windows.Controls.Button
                {
                    Content = CreateNexusVectorIcon(
                        "M 17.65,6.35 C 16.2,4.9 14.21,4 12,4 C 7.58,4 4.01,7.58 4.01,12 C 4.01,16.42 7.58,20 12,20 C 15.73,20 18.84,17.45 19.73,14 H 17.65 C 16.83,16.33 14.61,18 12,18 C 8.69,18 6,15.31 6,12 C 6,8.69 8.69,6 12,6 C 13.66,6 15.14,6.69 16.22,7.78 L 13,11 H 20 V 4 L 17.65,6.35 Z",
                        "NexusForegroundPrimary",
                        textDark,
                        15),
                    Width = 28,
                    Height = 28,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Reload (F5 / Ctrl+R)"
                };
                ApplyZeroClipToolbarButtonStyle(_navReloadBtn);
                _navReloadBtn.Click += (_, e) =>
                {
                    try
                    {
                        var coreWv = _activeWebView2Control?.GetType().GetProperty("CoreWebView2")?.GetValue(_activeWebView2Control);
                        if (_isWebViewCurrentlyLoading)
                        {
                            coreWv?.GetType().GetMethod("Stop")?.Invoke(coreWv, null);
                            return;
                        }
                        var m = this.GetType().GetMethod("BrowserRefresh_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                ?? this.GetType().GetMethod("BrowserReload_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (m is not null) { m.Invoke(this, new object?[] { _navReloadBtn, e }); return; }
                        coreWv?.GetType().GetMethod("Reload")?.Invoke(coreWv, null);
                    }
                    catch { }
                };

                navStack.Children.Add(_navBackBtn);
                navStack.Children.Add(_navFwdBtn);
                navStack.Children.Add(navDivider);
                navStack.Children.Add(_navReloadBtn);
                navCapsuleBorder.Child = navStack;
                System.Windows.Controls.Grid.SetColumn(navCapsuleBorder, 0);
                modernToolbarGrid.Children.Add(navCapsuleBorder);

                // COLUMN 1: Soft-Gray Pill URL Bar [ Search or enter a URL ]
                var urlPillBorder = new System.Windows.Controls.Border
                {
                    Height = 36,
                    CornerRadius = new System.Windows.CornerRadius(18),
                    BorderThickness = new System.Windows.Thickness(1),
                    Margin = new System.Windows.Thickness(8, 0, 8, 0),
                    Padding = new System.Windows.Thickness(16, 0, 16, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                urlPillBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "DshSidebarBgBrush");
                urlPillBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "DshBorderL2Brush");
                addressBox.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "DshTextPrimaryBrush");
                var urlInnerGrid = new System.Windows.Controls.Grid();
                var urlPlaceholder = new System.Windows.Controls.TextBlock
                {
                    Text = "Search or enter a URL",
                    FontSize = 13.5,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    IsHitTestVisible = false
                };
                urlPlaceholder.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "DshTextTertiaryBrush");
                void SyncUrlPlaceholderVisibility()
                {
                    string t = (addressBox.Text ?? string.Empty).Trim();
                    bool emptyOrBlank = string.IsNullOrEmpty(t) || string.Equals(t, "about:blank", StringComparison.OrdinalIgnoreCase);
                    if (string.Equals(t, "about:blank", StringComparison.OrdinalIgnoreCase) && !addressBox.IsKeyboardFocused)
                    {
                        addressBox.Text = string.Empty;
                    }
                    urlPlaceholder.Visibility = (emptyOrBlank && !addressBox.IsKeyboardFocused)
                        ? System.Windows.Visibility.Visible
                        : System.Windows.Visibility.Collapsed;
                }
                addressBox.TextChanged += (_, _) => SyncUrlPlaceholderVisibility();
                addressBox.GotKeyboardFocus += (_, _) => SyncUrlPlaceholderVisibility();
                addressBox.LostKeyboardFocus += (_, _) => SyncUrlPlaceholderVisibility();
                SyncUrlPlaceholderVisibility();

                urlInnerGrid.Children.Add(addressBox);
                urlInnerGrid.Children.Add(urlPlaceholder);
                urlPillBorder.Child = urlInnerGrid;
                System.Windows.Controls.Grid.SetColumn(urlPillBorder, 1);
                modernToolbarGrid.Children.Add(urlPillBorder);

                // COLUMN 2: Capsule Pill [ 🛡️   🧩 ]
                var extCapsulePill = new System.Windows.Controls.Border
                {
                    Height = 36,
                    CornerRadius = new System.Windows.CornerRadius(18),
                    BorderThickness = new System.Windows.Thickness(1),
                    Padding = new System.Windows.Thickness(8, 0, 8, 0),
                    Margin = new System.Windows.Thickness(0, 0, 6, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                extCapsulePill.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "DshCardBgBrush");
                extCapsulePill.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "DshBorderL2Brush");

                var extCapsuleStack = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };

                _adGuardShieldBtn = new System.Windows.Controls.Button
                {
                    Content = CreateNexusVectorIcon(
                        "M 12,2 L 4,5 V 11.09 C 4,16.14 7.41,20.85 12,22 C 16.59,20.85 20,16.14 20,11.09 V 5 L 12,2 Z M 10,17 L 6,13 L 7.41,11.59 L 10,14.17 L 16.59,7.58 L 18,9 L 10,17 Z",
                        "NexusAccentBrush",
                        adGuardGreen,
                        16.5),
                    Width = 28,
                    Height = 28,
                    Margin = new System.Windows.Thickness(0, 0, 6, 0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "AdGuard AdBlocker",
                    Background = System.Windows.Media.Brushes.Transparent
                };
                ApplyZeroClipToolbarButtonStyle(_adGuardShieldBtn);

                var shieldPopupBorder = new System.Windows.Controls.Border
                {
                    Width = 316,
                    CornerRadius = new System.Windows.CornerRadius(10),
                    Background = cardWhite,
                    BorderBrush = borderSubtle,
                    BorderThickness = new System.Windows.Thickness(1),
                    Padding = new System.Windows.Thickness(0)
                };
                shieldPopupBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "DshCardBgBrush");
                shieldPopupBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "DshBorderL2Brush");

                var shieldRootStack = new System.Windows.Controls.StackPanel();
                var shieldHdr = new System.Windows.Controls.DockPanel { Margin = new System.Windows.Thickness(16, 12, 12, 8) };
                var hdrRightIcons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

                var refreshPopupBtn = new System.Windows.Controls.Button
                {
                    Content = CreateNexusVectorIcon(
                        "M 17.65,6.35 C 16.2,4.9 14.21,4 12,4 C 7.58,4 4.01,7.58 4.01,12 C 4.01,16.42 7.58,20 12,20 C 15.73,20 18.84,17.45 19.73,14 H 17.65 C 16.83,16.33 14.61,18 12,18 C 8.69,18 6,15.31 6,12 C 6,8.69 8.69,6 12,6 C 13.66,6 15.14,6.69 16.22,7.78 L 13,11 H 20 V 4 L 17.65,6.35 Z",
                        "NexusForegroundSecondary",
                        textMuted,
                        15),
                    Width = 28,
                    Height = 28,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Muat ulang halaman"
                };
                ApplyZeroClipToolbarButtonStyle(refreshPopupBtn);
                refreshPopupBtn.Click += (_, _) =>
                {
                    RefreshAdGuardFlyoutState();
                    var mainCore = _activeWebView2Control?.GetType().GetProperty("CoreWebView2")?.GetValue(_activeWebView2Control);
                    mainCore?.GetType().GetMethod("Reload")?.Invoke(mainCore, null);
                };

                var gearSettingsBtn = new System.Windows.Controls.Button
                {
                    Content = CreateNexusVectorIcon(
                        "M 19.14,12.94 C 19.18,12.64 19.2,12.33 19.2,12 C 19.2,11.68 19.18,11.36 19.13,11.06 L 21.16,9.48 C 21.34,9.34 21.39,9.07 21.28,8.87 L 19.36,5.55 C 19.24,5.33 18.99,5.26 18.77,5.33 L 16.38,6.29 C 15.88,5.91 15.35,5.59 14.76,5.35 L 14.4,2.81 C 14.36,2.57 14.16,2.4 13.92,2.4 H 10.08 C 9.84,2.4 9.65,2.57 9.61,2.81 L 9.25,5.35 C 8.66,5.59 8.12,5.92 7.63,6.29 L 5.24,5.33 C 5.02,5.25 4.77,5.33 4.65,5.55 L 2.74,8.87 C 2.62,9.08 2.66,9.34 2.86,9.48 L 4.89,11.06 C 4.84,11.36 4.8,11.69 4.8,12 C 4.8,12.31 4.82,12.64 4.87,12.94 L 2.84,14.52 C 2.66,14.66 2.61,14.93 2.72,15.13 L 4.64,18.45 C 4.76,18.67 5.01,18.74 5.23,18.67 L 7.62,17.71 C 8.12,18.09 8.65,18.41 9.24,18.65 L 9.6,21.19 C 9.65,21.43 9.84,21.6 10.08,21.6 H 13.92 C 14.16,21.6 14.36,21.43 14.39,21.19 L 14.75,18.65 C 15.34,18.41 15.88,18.09 16.37,17.71 L 18.76,18.67 C 18.98,18.75 19.23,18.67 19.35,18.45 L 21.27,15.13 C 21.39,14.91 21.34,14.66 21.15,14.52 L 19.14,12.94 Z M 12,15.6 C 10.02,15.6 8.4,13.98 8.4,12 C 8.4,10.02 10.02,8.4 12,8.4 C 13.98,8.4 15.6,10.02 15.6,12 C 15.6,13.98 13.98,15.6 12,15.6 Z",
                        "NexusForegroundSecondary",
                        textMuted,
                        15),
                    Width = 28,
                    Height = 28,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Pengaturan AdGuard"
                };
                ApplyZeroClipToolbarButtonStyle(gearSettingsBtn);
                gearSettingsBtn.Click += async (_, _) =>
                {
                    if (_adGuardShieldPopup is not null) _adGuardShieldPopup.IsOpen = false;
                    await OpenAdGuardInternalPageAsync(openOptionsPage: true);
                };

                hdrRightIcons.Children.Add(refreshPopupBtn);
                hdrRightIcons.Children.Add(gearSettingsBtn);
                System.Windows.Controls.DockPanel.SetDock(hdrRightIcons, System.Windows.Controls.Dock.Right);

                var brandLeft = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                brandLeft.Children.Add(CreateNexusVectorIcon(
                    "M 12,2 L 4,5 V 11.09 C 4,16.14 7.41,20.85 12,22 C 16.59,20.85 20,16.14 20,11.09 V 5 L 12,2 Z M 10,17 L 6,13 L 7.41,11.59 L 10,14.17 L 16.59,7.58 L 18,9 L 10,17 Z",
                    "NexusAccentBrush",
                    adGuardGreen,
                    18));
                brandLeft.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "AdGuard MV3",
                    FontSize = 15.5,
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    Foreground = textDark,
                    Margin = new System.Windows.Thickness(7, 0, 0, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                });
                shieldHdr.Children.Add(hdrRightIcons);
                shieldHdr.Children.Add(brandLeft);
                shieldRootStack.Children.Add(shieldHdr);

                var centerStack = new System.Windows.Controls.StackPanel
                {
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new System.Windows.Thickness(16, 4, 16, 10)
                };

                _adGuardFlyoutDomainText = new System.Windows.Controls.TextBlock
                {
                    Text = "www.google.com",
                    FontSize = 12,
                    Foreground = textMuted,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new System.Windows.Thickness(0, 0, 0, 3)
                };
                centerStack.Children.Add(_adGuardFlyoutDomainText);

                _adGuardFlyoutExtIdText = new System.Windows.Controls.TextBlock
                {
                    Text = "Perlindungan Aktif",
                    FontSize = 18.5,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Foreground = textDark,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new System.Windows.Thickness(0, 0, 0, 2)
                };
                centerStack.Children.Add(_adGuardFlyoutExtIdText);

                _adGuardFlyoutStatsText = new System.Windows.Controls.TextBlock
                {
                    Text = "Total diblokir: 0",
                    FontSize = 12,
                    Foreground = textMuted,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new System.Windows.Thickness(0, 0, 0, 12)
                };
                centerStack.Children.Add(_adGuardFlyoutStatsText);

                var switchKnob = new System.Windows.Controls.Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new System.Windows.CornerRadius(13),
                    Background = cardWhite,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Margin = new System.Windows.Thickness(4, 0, 4, 0),
                    Child = CreateNexusStrokeVectorIcon("M 20,6 L 9,17 L 4,12", adGuardGreen, 2.4, 12.5)
                };
                var switchTrackBorder = new System.Windows.Controls.Border
                {
                    Width = 64,
                    Height = 34,
                    CornerRadius = new System.Windows.CornerRadius(17),
                    Background = adGuardGreen,
                    Child = switchKnob
                };
                _adGuardMasterToggleBtn = new System.Windows.Controls.Button
                {
                    Content = switchTrackBorder,
                    Width = 64,
                    Height = 34,
                    Margin = new System.Windows.Thickness(0, 0, 0, 4),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                };
                ApplyZeroClipToolbarButtonStyle(_adGuardMasterToggleBtn);
                _adGuardMasterToggleBtn.Click += async (_, _) =>
                {
                    await ToggleAdGuardProtectionAsync();
                    bool on = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.IsAdGuardProtectionEnabled;
                    switchTrackBorder.Background = on ? adGuardGreen : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(209, 213, 219));
                    switchKnob.HorizontalAlignment = on ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left;
                    RefreshAdGuardFlyoutState();
                };
                centerStack.Children.Add(_adGuardMasterToggleBtn);
                shieldRootStack.Children.Add(centerStack);

                var tabsBorder = new System.Windows.Controls.Border
                {
                    Background = bgSoftGray,
                    CornerRadius = new System.Windows.CornerRadius(7),
                    Padding = new System.Windows.Thickness(3),
                    Margin = new System.Windows.Thickness(16, 0, 16, 8)
                };
                var tabsGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
                var tabAksiText = new System.Windows.Controls.TextBlock
                {
                    Text = "Kontrol Situs",
                    FontSize = 12,
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    Foreground = textDark,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                };
                var tabAksiBorder = new System.Windows.Controls.Border
                {
                    Background = cardWhite,
                    CornerRadius = new System.Windows.CornerRadius(5),
                    Padding = new System.Windows.Thickness(0, 5, 0, 5),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = tabAksiText
                };
                var tabStatText = new System.Windows.Controls.TextBlock
                {
                    Text = "Statistik",
                    FontSize = 12,
                    FontWeight = System.Windows.FontWeights.Medium,
                    Foreground = textMuted,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                };
                var tabStatBorder = new System.Windows.Controls.Border
                {
                    Background = System.Windows.Media.Brushes.Transparent,
                    CornerRadius = new System.Windows.CornerRadius(5),
                    Padding = new System.Windows.Thickness(0, 5, 0, 5),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = tabStatText
                };
                tabsGrid.Children.Add(tabAksiBorder);
                tabsGrid.Children.Add(tabStatBorder);
                tabsBorder.Child = tabsGrid;
                shieldRootStack.Children.Add(tabsBorder);

                var actionsStack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(10, 0, 10, 12) };
                var statsStack = new System.Windows.Controls.StackPanel
                {
                    Margin = new System.Windows.Thickness(14, 2, 14, 12),
                    Visibility = System.Windows.Visibility.Collapsed
                };
                var statSummaryText = new System.Windows.Controls.TextBlock
                {
                    Text = "Engine: AdGuard GitHub MV3 (DeclarativeNetRequest + Cosmetic CSS + Auto-Skip Video Ads).",
                    FontSize = 12,
                    Foreground = textMuted,
                    TextWrapping = System.Windows.TextWrapping.Wrap,
                    Margin = new System.Windows.Thickness(4, 2, 4, 8)
                };
                var resetStatsBtn = CreateCleanMenuRowButton(
                    "Reset penghitung iklan diblokir",
                    null,
                    textDark,
                    textMuted,
                    "M 17.65,6.35 C 16.2,4.9 14.21,4 12,4 C 7.58,4 4.01,7.58 4.01,12 C 4.01,16.42 7.58,20 12,20 C 15.73,20 18.84,17.45 19.73,14 H 17.65 C 16.83,16.33 14.61,18 12,18 C 8.69,18 6,15.31 6,12 C 6,8.69 8.69,6 12,6 C 13.66,6 15.14,6.69 16.22,7.78 L 13,11 H 20 V 4 L 17.65,6.35 Z",
                    textMuted);
                resetStatsBtn.Click += (_, _) =>
                {
                    _adGuardBlockedCount = 0;
                    var rtCfg = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
                    rtCfg.AdGuardConfig.TotalBlockedAdsCount = 0;
                    rtCfg.SaveAdGuardConfigToDisk();
                    RefreshAdGuardFlyoutState();
                };
                statsStack.Children.Add(statSummaryText);
                statsStack.Children.Add(resetStatsBtn);

                tabAksiBorder.MouseLeftButtonUp += (_, _) =>
                {
                    tabAksiBorder.Background = cardWhite;
                    tabAksiText.FontWeight = System.Windows.FontWeights.SemiBold;
                    tabAksiText.Foreground = textDark;
                    tabStatBorder.Background = System.Windows.Media.Brushes.Transparent;
                    tabStatText.FontWeight = System.Windows.FontWeights.Medium;
                    tabStatText.Foreground = textMuted;
                    actionsStack.Visibility = System.Windows.Visibility.Visible;
                    statsStack.Visibility = System.Windows.Visibility.Collapsed;
                };
                tabStatBorder.MouseLeftButtonUp += (_, _) =>
                {
                    tabStatBorder.Background = cardWhite;
                    tabStatText.FontWeight = System.Windows.FontWeights.SemiBold;
                    tabStatText.Foreground = textDark;
                    tabAksiBorder.Background = System.Windows.Media.Brushes.Transparent;
                    tabAksiText.FontWeight = System.Windows.FontWeights.Medium;
                    tabAksiText.Foreground = textMuted;
                    actionsStack.Visibility = System.Windows.Visibility.Collapsed;
                    statsStack.Visibility = System.Windows.Visibility.Visible;
                };

                var whitelistSiteBtn = CreateCleanMenuRowButton(
                    "Kecualikan / aktifkan di domain ini",
                    null,
                    textDark,
                    textMuted,
                    "M 12,2 L 4,5 V 11.09 C 4,16.14 7.41,20.85 12,22 C 16.59,20.85 20,16.14 20,11.09 V 5 L 12,2 Z",
                    textMuted);
                whitelistSiteBtn.Click += async (_, _) =>
                {
                    await ToggleAdGuardWhitelistForCurrentSiteAsync();
                    RefreshAdGuardFlyoutState();
                };

                var sweepPageBtn = CreateCleanMenuRowButton(
                    "Bersihkan elemen iklan di halaman ini",
                    null,
                    textDark,
                    textMuted,
                    "M 21,3 H 3 C 1.89,3 1,3.89 1,5 V 17 C 1,18.1 1.89,19 3,19 H 8 V 21 H 16 V 19 H 21 C 22.1,19 23,18.1 23,17 V 5 C 23,3.89 22.1,3 21,3 Z M 21,17 H 3 V 5 H 21 V 17 Z",
                    textMuted);
                sweepPageBtn.Click += async (_, _) =>
                {
                    if (_adGuardShieldPopup is not null) _adGuardShieldPopup.IsOpen = false;
                    await ExecuteLiveScriptOnWebView2Async(GetAdGuardDocumentStartScript());
                };

                var openSettingsBtn = CreateCleanMenuRowButton(
                    "Pengaturan Filter & Aturan Kustom",
                    null,
                    textDark,
                    textMuted,
                    "M 3,6 H 21 V 8 H 3 V 6 Z M 3,11 H 21 V 13 H 3 V 11 Z M 3,16 H 15 V 18 H 3 V 16 Z",
                    textMuted);
                openSettingsBtn.Click += async (_, _) =>
                {
                    if (_adGuardShieldPopup is not null) _adGuardShieldPopup.IsOpen = false;
                    await OpenAdGuardInternalPageAsync(openOptionsPage: true);
                };

                actionsStack.Children.Add(whitelistSiteBtn);
                actionsStack.Children.Add(sweepPageBtn);
                actionsStack.Children.Add(openSettingsBtn);
                shieldRootStack.Children.Add(actionsStack);
                shieldRootStack.Children.Add(statsStack);
                shieldPopupBorder.Child = shieldRootStack;

                _adGuardShieldPopup = new System.Windows.Controls.Primitives.Popup
                {
                    PlacementTarget = extCapsulePill,
                    Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                    StaysOpen = false,
                    AllowsTransparency = true,
                    Child = shieldPopupBorder
                };

                _adGuardShieldBtn.Click += (_, _) =>
                {
                    RefreshAdGuardFlyoutState();
                    _adGuardShieldPopup.IsOpen = !_adGuardShieldPopup.IsOpen;
                };

                var puzzleBtn = new System.Windows.Controls.Button
                {
                    Content = CreateNexusStrokeVectorIcon(
                        "M 10.5,2.5 A 2.2,2.2 0 0 1 12.7,4.7 V 5.5 H 16 A 2,2 0 0 1 18,7.5 V 10.3 H 18.8 A 2.2,2.2 0 0 1 21,12.5 A 2.2,2.2 0 0 1 18.8,14.7 H 18 V 18 A 2,2 0 0 1 16,20 H 12.7 V 19.2 A 2.2,2.2 0 0 0 10.5,17 A 2.2,2.2 0 0 0 8.3,19.2 V 20 H 5 A 2,2 0 0 1 3,18 V 14.7 H 3.8 A 2.2,2.2 0 0 0 6,12.5 A 2.2,2.2 0 0 0 3.8,10.3 H 3 V 7.5 A 2,2 0 0 1 5,5.5 H 8.3 V 4.7 A 2.2,2.2 0 0 1 10.5,2.5 Z",
                        textDark,
                        1.65,
                        15.5),
                    Width = 28,
                    Height = 28,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Extensions",
                    Background = System.Windows.Media.Brushes.Transparent
                };
                ApplyZeroClipToolbarButtonStyle(puzzleBtn);

                extCapsuleStack.Children.Add(_adGuardShieldBtn);
                extCapsuleStack.Children.Add(puzzleBtn);
                extCapsulePill.Child = extCapsuleStack;
                System.Windows.Controls.Grid.SetColumn(extCapsulePill, 2);
                modernToolbarGrid.Children.Add(extCapsulePill);

                var extPopupBorder = new System.Windows.Controls.Border
                {
                    Width = 250,
                    CornerRadius = new System.Windows.CornerRadius(8),
                    Background = cardWhite,
                    BorderBrush = borderSubtle,
                    BorderThickness = new System.Windows.Thickness(1),
                    Padding = new System.Windows.Thickness(8, 10, 8, 8)
                };
                extPopupBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "DshCardBgBrush");
                extPopupBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "DshBorderL2Brush");
                var extStack = new System.Windows.Controls.StackPanel();
                extStack.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "Extensions",
                    FontSize = 12,
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    Foreground = textMuted,
                    Margin = new System.Windows.Thickness(10, 2, 10, 6)
                });

                var adgExtItemBtn = CreateCleanMenuRowButton(
                    "AdGuard AdBlocker (MV3)",
                    null,
                    textDark,
                    textMuted,
                    "M 12,2 L 4,5 V 11.09 C 4,16.14 7.41,20.85 12,22 C 16.59,20.85 20,16.14 20,11.09 V 5 L 12,2 Z M 10,17 L 6,13 L 7.41,11.59 L 10,14.17 L 16.59,7.58 L 18,9 L 10,17 Z",
                    adGuardGreen);
                adgExtItemBtn.Click += (_, _) =>
                {
                    _extensionsPopup!.IsOpen = false;
                    RefreshAdGuardFlyoutState();
                    _adGuardShieldPopup.IsOpen = true;
                };
                extStack.Children.Add(adgExtItemBtn);

                extStack.Children.Add(new System.Windows.Controls.Border
                {
                    Height = 1,
                    Background = borderSubtle,
                    Margin = new System.Windows.Thickness(6, 6, 6, 6)
                });

                var manageExtBtn = CreateCleanMenuRowButton(
                    "Manage extensions",
                    null,
                    textDark,
                    textMuted,
                    "M 19.14,12.94 C 19.18,12.64 19.2,12.33 19.2,12 C 19.2,11.68 19.18,11.36 19.13,11.06 L 21.16,9.48 C 21.34,9.34 21.39,9.07 21.28,8.87 L 19.36,5.55 C 19.24,5.33 18.99,5.26 18.77,5.33 L 16.38,6.29 C 15.88,5.91 15.35,5.59 14.76,5.35 L 14.4,2.81 C 14.36,2.57 14.16,2.4 13.92,2.4 H 10.08 C 9.84,2.4 9.65,2.57 9.61,2.81 L 9.25,5.35 C 8.66,5.59 8.12,5.92 7.63,6.29 L 5.24,5.33 C 5.02,5.25 4.77,5.33 4.65,5.55 L 2.74,8.87 C 2.62,9.08 2.66,9.34 2.86,9.48 L 4.89,11.06 C 4.84,11.36 4.8,11.69 4.8,12 C 4.8,12.31 4.82,12.64 4.87,12.94 L 2.84,14.52 C 2.66,14.66 2.61,14.93 2.72,15.13 L 4.64,18.45 C 4.76,18.67 5.01,18.74 5.23,18.67 L 7.62,17.71 C 8.12,18.09 8.65,18.41 9.24,18.65 L 9.6,21.19 C 9.65,21.43 9.84,21.6 10.08,21.6 H 13.92 C 14.16,21.6 14.36,21.43 14.39,21.19 L 14.75,18.65 C 15.34,18.41 15.88,18.09 16.37,17.71 L 18.76,18.67 C 18.98,18.75 19.23,18.67 19.35,18.45 L 21.27,15.13 C 21.39,14.91 21.34,14.66 21.15,14.52 L 19.14,12.94 Z",
                    textMuted);
                manageExtBtn.Click += (_, _) =>
                {
                    _extensionsPopup!.IsOpen = false;
                    OpenNexusBrowserSettingsHubInTab("extensions", "Extensions");
                };
                extStack.Children.Add(manageExtBtn);

                extPopupBorder.Child = extStack;
                _extensionsPopup = new System.Windows.Controls.Primitives.Popup
                {
                    PlacementTarget = extCapsulePill,
                    Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                    StaysOpen = false,
                    AllowsTransparency = true,
                    Child = extPopupBorder
                };

                puzzleBtn.Click += (_, _) =>
                {
                    _extensionsPopup.IsOpen = !_extensionsPopup.IsOpen;
                };

                // COLUMN 3: Circular Three-Dots Browser Menu Icon [ ⋯ ] (Device toolbar REMOVED as requested!)
                var moreMenuPillBorder = new System.Windows.Controls.Border
                {
                    Width = 36,
                    Height = 36,
                    CornerRadius = new System.Windows.CornerRadius(18),
                    BorderThickness = new System.Windows.Thickness(1),
                    Margin = new System.Windows.Thickness(0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                moreMenuPillBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "DshCardBgBrush");
                moreMenuPillBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "DshBorderL2Brush");

                var moreDotsTb = new System.Windows.Controls.TextBlock
                {
                    Text = "⋯",
                    FontSize = 16,
                    FontWeight = System.Windows.FontWeights.Bold,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                moreDotsTb.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "DshTextPrimaryBrush");
                var moreMenuBtn = new System.Windows.Controls.Button
                {
                    Content = moreDotsTb,
                    Width = 34,
                    Height = 34,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Menu Browser (Find, Print, Zoom, Downloads, History, Clear Data, Browser Settings)",
                    Background = System.Windows.Media.Brushes.Transparent
                };
                ApplyZeroClipToolbarButtonStyle(moreMenuBtn);
                moreMenuPillBorder.Child = moreMenuBtn;
                System.Windows.Controls.Grid.SetColumn(moreMenuPillBorder, 3);
                modernToolbarGrid.Children.Add(moreMenuPillBorder);

                var morePopupBorder = new System.Windows.Controls.Border
                {
                    Width = 236,
                    CornerRadius = new System.Windows.CornerRadius(8),
                    Background = cardWhite,
                    BorderBrush = borderSubtle,
                    BorderThickness = new System.Windows.Thickness(1),
                    Padding = new System.Windows.Thickness(8, 8, 8, 8)
                };
                morePopupBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "DshCardBgBrush");
                morePopupBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "DshBorderL2Brush");
                var moreStack = new System.Windows.Controls.StackPanel();

                var findBtn = CreateCleanMenuRowButton("Find in page", null, textDark, textMuted);
                findBtn.Click += (_, _) =>
                {
                    _browserMoreMenuPopup!.IsOpen = false;
                    ShowFloatingFindInPageBar(toolbarRowGrid);
                };
                moreStack.Children.Add(findBtn);

                var printBtn = CreateCleanMenuRowButton("Print", null, textDark, textMuted);
                printBtn.Click += async (_, _) =>
                {
                    _browserMoreMenuPopup!.IsOpen = false;
                    await ExecuteLiveScriptOnWebView2Async("window.print();");
                };
                moreStack.Children.Add(printBtn);

                moreStack.Children.Add(new System.Windows.Controls.Separator { Margin = new System.Windows.Thickness(6, 4, 6, 4) });

                var zoomDock = new System.Windows.Controls.DockPanel { Margin = new System.Windows.Thickness(10, 4, 8, 4) };
                var zoomRightStack = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                var zoomPillBorder = new System.Windows.Controls.Border
                {
                    Background = bgSoftGray,
                    BorderBrush = borderSubtle,
                    BorderThickness = new System.Windows.Thickness(1),
                    CornerRadius = new System.Windows.CornerRadius(8),
                    Padding = new System.Windows.Thickness(2, 1, 2, 1)
                };
                zoomPillBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "DshSidebarBgBrush");
                zoomPillBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "DshBorderL2Brush");
                var zoomPillStack = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                var zoomMinusBtn = new System.Windows.Controls.Button
                {
                    Content = new System.Windows.Controls.TextBlock { Text = "−", Foreground = textMuted, FontSize = 13 },
                    Width = 26,
                    Height = 24,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                ApplyZeroClipToolbarButtonStyle(zoomMinusBtn);

                _zoomPercentLabel = new System.Windows.Controls.TextBlock
                {
                    Text = "100%",
                    FontSize = 12.5,
                    Foreground = textDark,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Margin = new System.Windows.Thickness(8, 0, 8, 0)
                };
                _zoomPercentLabel.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "DshTextPrimaryBrush");
                var zoomPlusBtn = new System.Windows.Controls.Button
                {
                    Content = new System.Windows.Controls.TextBlock { Text = "+", Foreground = textMuted, FontSize = 13 },
                    Width = 26,
                    Height = 24,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                ApplyZeroClipToolbarButtonStyle(zoomPlusBtn);

                var zoomResetBtn = new System.Windows.Controls.Button
                {
                    Content = CreateNexusVectorIcon(
                        "M 17.65,6.35 C 16.2,4.9 14.21,4 12,4 C 7.58,4 4.01,7.58 4.01,12 C 4.01,16.42 7.58,20 12,20 C 15.73,20 18.84,17.45 19.73,14 H 17.65 C 16.83,16.33 14.61,18 12,18 C 8.69,18 6,15.31 6,12 C 6,8.69 8.69,6 12,6 C 13.66,6 15.14,6.69 16.22,7.78 L 13,11 H 20 V 4 L 17.65,6.35 Z",
                        "NexusForegroundSecondary",
                        textMuted,
                        13),
                    Width = 24,
                    Height = 24,
                    Margin = new System.Windows.Thickness(6, 0, 0, 0),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                ApplyZeroClipToolbarButtonStyle(zoomResetBtn);

                void ApplyZoom(double nextPct)
                {
                    _currentZoomPercent = Math.Clamp(nextPct, 50.0, 200.0);
                    if (_zoomPercentLabel is not null) _zoomPercentLabel.Text = $"{_currentZoomPercent:F0}%";
                    try { _activeWebView2Control?.GetType().GetProperty("ZoomFactor")?.SetValue(_activeWebView2Control, _currentZoomPercent / 100.0); } catch { }
                }
                zoomMinusBtn.Click += (_, _) => ApplyZoom(_currentZoomPercent - 10.0);
                zoomPlusBtn.Click += (_, _) => ApplyZoom(_currentZoomPercent + 10.0);
                zoomResetBtn.Click += (_, _) => ApplyZoom(100.0);

                zoomPillStack.Children.Add(zoomMinusBtn);
                zoomPillStack.Children.Add(_zoomPercentLabel);
                zoomPillStack.Children.Add(zoomPlusBtn);
                zoomPillBorder.Child = zoomPillStack;
                zoomRightStack.Children.Add(zoomPillBorder);
                zoomRightStack.Children.Add(zoomResetBtn);
                System.Windows.Controls.DockPanel.SetDock(zoomRightStack, System.Windows.Controls.Dock.Right);
                zoomDock.Children.Add(zoomRightStack);
                var zoomTitleTb = new System.Windows.Controls.TextBlock
                {
                    Text = "Zoom",
                    FontSize = 13,
                    Foreground = textDark,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                zoomTitleTb.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "DshTextPrimaryBrush");
                zoomDock.Children.Add(zoomTitleTb);
                moreStack.Children.Add(zoomDock);

                moreStack.Children.Add(new System.Windows.Controls.Separator { Margin = new System.Windows.Thickness(6, 4, 6, 4) });

                var downloadsMenuBtn = CreateCleanMenuRowButton("Downloads", null, textDark, textMuted);
                downloadsMenuBtn.Click += (_, _) => OpenNexusBrowserSettingsHubInTab("downloads", "Download history");
                moreStack.Children.Add(downloadsMenuBtn);

                var historyMenuBtn = CreateCleanMenuRowButton("History", null, textDark, textMuted);
                historyMenuBtn.Click += (_, _) => OpenNexusBrowserSettingsHubInTab("history", "Browsing history");
                moreStack.Children.Add(historyMenuBtn);

                var clearDataMenuBtn = CreateCleanMenuRowButton("Clear browsing data", null, textDark, textMuted);
                clearDataMenuBtn.Click += (_, _) => OpenNexusBrowserSettingsHubInTab("clear", "Clear browsing data");
                moreStack.Children.Add(clearDataMenuBtn);

                moreStack.Children.Add(new System.Windows.Controls.Separator { Margin = new System.Windows.Thickness(6, 4, 6, 4) });

                var browserSettingsMenuBtn = CreateCleanMenuRowButton("Browser settings", null, textDark, textMuted);
                browserSettingsMenuBtn.Click += (_, _) => OpenNexusBrowserSettingsHubInTab("general", "Browser settings");
                moreStack.Children.Add(browserSettingsMenuBtn);

                morePopupBorder.Child = moreStack;
                _browserMoreMenuPopup = new System.Windows.Controls.Primitives.Popup
                {
                    PlacementTarget = moreMenuPillBorder,
                    Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                    StaysOpen = false,
                    AllowsTransparency = true,
                    Child = morePopupBorder
                };
                moreMenuBtn.Click += (_, _) => _browserMoreMenuPopup.IsOpen = !_browserMoreMenuPopup.IsOpen;

                masterHeaderStack.Children.Add(modernToolbarGrid);
                toolbarRowGrid.Children.Add(masterHeaderStack);

                this.PreviewKeyDown += (_, ke) =>
                {
                    try
                    {
                        if (ke.Key == System.Windows.Input.Key.F5 ||
                            (ke.Key == System.Windows.Input.Key.R && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control))
                        {
                            var coreWv = _activeWebView2Control?.GetType().GetProperty("CoreWebView2")?.GetValue(_activeWebView2Control);
                            coreWv?.GetType().GetMethod("Reload")?.Invoke(coreWv, null);
                            ke.Handled = true;
                        }
                        else if (ke.Key == System.Windows.Input.Key.F && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
                        {
                            ShowFloatingFindInPageBar(toolbarRowGrid);
                            ke.Handled = true;
                        }
                    }
                    catch { }
                };
            }
        }
        catch { }
    }

    private void SyncNavButtonsAndLoadingState(object? coreWv, bool isLoading)
    {
        try
        {
            _isWebViewCurrentlyLoading = isLoading;
            var textDark = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 34, 34));
            if (_navReloadBtn is not null)
            {
                _navReloadBtn.Content = isLoading
                    ? CreateNexusVectorIcon("M 19,6.41 L 17.59,5 L 12,10.59 L 6.41,5 L 5,6.41 L 10.59,12 L 5,17.59 L 6.41,19 L 12,13.41 L 17.59,19 L 19,17.59 L 13.41,12 L 19,6.41 Z", "NexusForegroundPrimary", textDark, 14)
                    : CreateNexusVectorIcon("M 17.65,6.35 C 16.2,4.9 14.21,4 12,4 C 7.58,4 4.01,7.58 4.01,12 C 4.01,16.42 7.58,20 12,20 C 15.73,20 18.84,17.45 19.73,14 H 17.65 C 16.83,16.33 14.61,18 12,18 C 8.69,18 6,15.31 6,12 C 6,8.69 8.69,6 12,6 C 13.66,6 15.14,6.69 16.22,7.78 L 13,11 H 20 V 4 L 17.65,6.35 Z", "NexusForegroundPrimary", textDark, 15);
            }
            if (coreWv is not null)
            {
                bool canBack = coreWv.GetType().GetProperty("CanGoBack")?.GetValue(coreWv) is true;
                bool canFwd = coreWv.GetType().GetProperty("CanGoForward")?.GetValue(coreWv) is true;
                if (_navBackBtn is not null) _navBackBtn.Opacity = canBack ? 1.0 : 0.35;
                if (_navFwdBtn is not null) _navFwdBtn.Opacity = canFwd ? 1.0 : 0.35;
            }
        }
        catch { }
    }

    private void EnsureNewWindowInterceptorAttached()
    {
        EnsureChromeExtensionsToolbarButtonAttached();
        if (_activeWebView2Control is null) return;
        try
        {
            var coreProp = _activeWebView2Control.GetType().GetProperty("CoreWebView2");
            var coreWv = coreProp?.GetValue(_activeWebView2Control);
            if (coreWv is null) return;

            if (!_webMessageBridgeAttached)
            {
                _webMessageBridgeAttached = AttachCoreWebView2Event(coreWv, "WebMessageReceived", (sender, args) =>
                {
                    try
                    {
                        if (args is null) return;
                        var getStrMethod = args.GetType().GetMethod("TryGetWebMessageAsString", Type.EmptyTypes);
                        string? msg = getStrMethod?.Invoke(args, null) as string;
                        if (string.IsNullOrWhiteSpace(msg) || !msg.StartsWith("nexus_cmd:", StringComparison.Ordinal))
                            return;

                        Dispatcher.BeginInvoke(async () =>
                        {
                            try
                            {
                                var rt = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
                                if (msg.StartsWith("nexus_cmd:clear_data:", StringComparison.Ordinal))
                                {
                                    var parts = msg.Split(':');
                                    string range = parts.Length > 2 ? parts[2] : "all";
                                    bool delHist = parts.Length <= 3 || parts[3] == "1";
                                    bool delCook = parts.Length > 4 && parts[4] == "1";
                                    rt.DeleteBrowsingHistoryByTimeRange(range, deleteHistory: delHist);
                                    await ClearWebView2BrowsingHistoryAsync(clearCookies: delCook);
                                    OpenNexusBrowserSettingsHubInTab("history", "Browsing history");
                                }
                                else if (msg.StartsWith("nexus_cmd:del_hist_item:", StringComparison.Ordinal))
                                {
                                    string payload = msg["nexus_cmd:del_hist_item:".Length..];
                                    var sp = payload.Split('|');
                                    string u = sp[0];
                                    string t = sp.Length > 1 ? sp[1] : "";
                                    rt.DeleteSingleHistoryItem(u, t);
                                    OpenNexusBrowserSettingsHubInTab("history", "Browsing history");
                                }
                                else if (msg.StartsWith("nexus_cmd:del_dl_file:", StringComparison.Ordinal))
                                {
                                    string fileName = msg["nexus_cmd:del_dl_file:".Length..];
                                    rt.DeleteSingleDownloadedFile(fileName);
                                    OpenNexusBrowserSettingsHubInTab("downloads", "Download history");
                                }
                                else if (msg.StartsWith("nexus_cmd:open_dl_file:", StringComparison.Ordinal))
                                {
                                    string fileName = msg["nexus_cmd:open_dl_file:".Length..];
                                    string fullPath = System.IO.Path.Combine(rt.DownloadDirectory, System.IO.Path.GetFileName(fileName));
                                    if (System.IO.File.Exists(fullPath))
                                    {
                                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = fullPath, UseShellExecute = true });
                                    }
                                }
                                else if (msg == "nexus_cmd:clear_all_downloads")
                                {
                                    rt.ClearAllDownloadedFiles();
                                    OpenNexusBrowserSettingsHubInTab("downloads", "Download history");
                                }
                                else if (msg.StartsWith("nexus_cmd:sync_settings_title:", StringComparison.Ordinal))
                                {
                                    string secTitle = msg["nexus_cmd:sync_settings_title:".Length..];
                                    if (BrowserAddressBox is not null && !BrowserAddressBox.IsKeyboardFocused)
                                        BrowserAddressBox.Text = $"Settings › Browser › {secTitle}";
                                    var activeTab = rt.OpenBrowserTabs.FirstOrDefault(t => t.TabId == rt.ActiveBrowserTabId);
                                    if (activeTab is not null && activeTab.IsInternalPage)
                                    {
                                        int idx = rt.OpenBrowserTabs.IndexOf(activeTab);
                                        rt.OpenBrowserTabs[idx] = activeTab with { Title = secTitle, Url = $"Settings › Browser › {secTitle}" };
                                        RebuildMultiTabBrowserStrip();
                                    }
                                }
                                else if (msg == "nexus_cmd:open_clear_modal")
                                {
                                    OpenNexusBrowserSettingsHubInTab("clear", "Clear browsing data");
                                }
                                else if (msg == "nexus_cmd:open_history_tab")
                                {
                                    OpenNexusBrowserSettingsHubInTab("history", "Browsing history");
                                }
                                else if (msg == "nexus_cmd:open_downloads_tab")
                                {
                                    OpenNexusBrowserSettingsHubInTab("downloads", "Download history");
                                }
                                else if (msg == "nexus_cmd:open_downloads_folder")
                                {
                                    try
                                    {
                                        System.IO.Directory.CreateDirectory(rt.DownloadDirectory);
                                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = rt.DownloadDirectory, UseShellExecute = true });
                                    }
                                    catch { }
                                }
                                else if (msg == "nexus_cmd:import_passwords_csv")
                                {
                                    try
                                    {
                                        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Import Passwords CSV", Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*" };
                                        dlg.ShowDialog();
                                    }
                                    catch { }
                                }
                                else if (msg == "nexus_cmd:import_chrome_data")
                                {
                                    int count = rt.ImportRealHistoryFromLocalBrowsers();
                                    if (count == 0)
                                        rt.RecordBrowsingHistory("Microsoft Edge / Chrome History Synced", "https://www.google.com");
                                    OpenNexusBrowserSettingsHubInTab("history", "Browsing history");
                                }
                                else if (msg == "nexus_cmd:open_genuine_adguard_options" || msg.StartsWith("nexus_cmd:open_options_tab", StringComparison.Ordinal))
                                {
                                    await OpenAdGuardInternalPageAsync(openOptionsPage: true);
                                }
                                else if (msg == "nexus_cmd:reinstall_cws_adguard")
                                {
                                    await DownloadAndInstallOfficialAdGuardFromChromeWebStoreAsync();
                                    await OpenAdGuardInternalPageAsync(openOptionsPage: true);
                                }
                                else if (msg.StartsWith("nexus_cmd:open_url_tab:", StringComparison.Ordinal))
                                {
                                    string target = msg["nexus_cmd:open_url_tab:".Length..];
                                    if (target.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
                                    {
                                        await OpenAdGuardInternalPageAsync(target.Contains("options", StringComparison.OrdinalIgnoreCase));
                                    }
                                    else
                                    {
                                        OpenOrActivateBrowserTab(target, target, "globe", false);
                                    }
                                }
                                else if (msg.StartsWith("nexus_cmd:save_browser_settings:", StringComparison.Ordinal))
                                {
                                    string json = msg["nexus_cmd:save_browser_settings:".Length..];
                                    var updated = System.Text.Json.JsonSerializer.Deserialize<Nexus.Contracts.BrowserAgent.AdGuardPersistentConfig>(json);
                                    if (updated is not null)
                                    {
                                        rt.AdGuardConfig.ShowFullUrlInAddressBar = updated.ShowFullUrlInAddressBar;
                                        rt.AdGuardConfig.OfferToSavePasswords = updated.OfferToSavePasswords;
                                        rt.SaveAdGuardConfigToDisk();
                                        try
                                        {
                                            var settingsObj = coreWv.GetType().GetProperty("Settings")?.GetValue(coreWv);
                                            settingsObj?.GetType().GetProperty("IsPasswordAutosaveEnabled")?.SetValue(settingsObj, updated.OfferToSavePasswords);
                                            settingsObj?.GetType().GetProperty("IsGeneralAutofillEnabled")?.SetValue(settingsObj, updated.OfferToSavePasswords);
                                        }
                                        catch { }
                                    }
                                }
                                else if (msg.StartsWith("nexus_cmd:toggle_ask_download:", StringComparison.Ordinal))
                                {
                                    rt.RequireConfirmationForDownload = msg.EndsWith(":1", StringComparison.Ordinal);
                                }
                                else if (msg == "nexus_cmd:adguard_reset_stats")
                                {
                                    _adGuardBlockedCount = 0;
                                    rt.AdGuardConfig.TotalBlockedAdsCount = 0;
                                    rt.SaveAdGuardConfigToDisk();
                                    RefreshAdGuardFlyoutState();
                                }
                                else if (msg == "nexus_cmd:toggle_protection" || msg == "nexus_cmd:adguard_toggle_protection")
                                {
                                    await ToggleAdGuardProtectionAsync();
                                }
                                else if (msg == "nexus_cmd:open_adguard_options")
                                {
                                    await OpenAdGuardInternalPageAsync(openOptionsPage: true);
                                }
                                else if (msg.StartsWith("nexus_cmd:adguard_inc_blocked:", StringComparison.Ordinal))
                                {
                                    if (int.TryParse(msg["nexus_cmd:adguard_inc_blocked:".Length..], out int inc) && inc > 0)
                                    {
                                        _adGuardBlockedCount += inc;
                                        rt.AdGuardConfig.TotalBlockedAdsCount += inc;
                                        rt.SaveAdGuardConfigToDisk();
                                        RefreshAdGuardFlyoutState();
                                    }
                                }
                                else if (msg.StartsWith("nexus_cmd:adguard_flag:", StringComparison.Ordinal))
                                {
                                    var sp = msg.Split(':');
                                    if (sp.Length >= 4)
                                    {
                                        string flagName = sp[2];
                                        bool flagVal = sp[3] == "1";
                                        if (flagName == "search_ads") rt.AdGuardConfig.BlockSearchAdsAndSelfPromo = flagVal;
                                        else if (flagName == "auto_lang") rt.AdGuardConfig.AutoActivateLanguageFilters = flagVal;
                                        else if (flagName == "stealth") rt.AdGuardConfig.StealthModeEnabled = flagVal;
                                        else if (flagName == "hide_search") rt.AdGuardConfig.HideSearchQueries = flagVal;
                                        else if (flagName == "dnt") rt.AdGuardConfig.SendDoNotTrackHeader = flagVal;
                                        else if (flagName == "strip_params") rt.AdGuardConfig.StripTrackingParams = flagVal;
                                        rt.SaveAdGuardConfigToDisk();
                                        _ = Task.Run(() => EnsureAdGuardMv3ExtensionProvisioned(GetAdGuardUnpackedExtensionFolder()));
                                    }
                                }
                                else if (msg.StartsWith("nexus_cmd:adguard_filter:", StringComparison.Ordinal))
                                {
                                    var sp = msg.Split(':');
                                    if (sp.Length >= 4)
                                    {
                                        string fName = sp[2];
                                        bool fVal = sp[3] == "1";
                                        if (fName == "adblocking") rt.AdGuardConfig.FilterAdBlocking = fVal;
                                        else if (fName == "privacy") rt.AdGuardConfig.FilterPrivacy = fVal;
                                        else if (fName == "social") rt.AdGuardConfig.FilterSocialWidgets = fVal;
                                        else if (fName == "annoyances") rt.AdGuardConfig.FilterAnnoyances = fVal;
                                        else if (fName == "security") rt.AdGuardConfig.FilterSecurity = fVal;
                                        else if (fName == "language") rt.AdGuardConfig.FilterLanguageSpecific = fVal;
                                        rt.SaveAdGuardConfigToDisk();
                                        _ = Task.Run(() => EnsureAdGuardMv3ExtensionProvisioned(GetAdGuardUnpackedExtensionFolder()));
                                    }
                                }
                                else if (msg.StartsWith("nexus_cmd:adguard_save_allowlist:", StringComparison.Ordinal))
                                {
                                    string raw = Uri.UnescapeDataString(msg["nexus_cmd:adguard_save_allowlist:".Length..]);
                                    rt.AdGuardConfig.AllowlistDomains = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                                    rt.AdGuardWhitelistedDomains.Clear();
                                    foreach (var d in rt.AdGuardConfig.AllowlistDomains) rt.AdGuardWhitelistedDomains.Add(d);
                                    rt.SaveAdGuardConfigToDisk();
                                    RefreshAdGuardFlyoutState();
                                }
                                else if (msg.StartsWith("nexus_cmd:adguard_save_userrules:", StringComparison.Ordinal))
                                {
                                    string raw = Uri.UnescapeDataString(msg["nexus_cmd:adguard_save_userrules:".Length..]);
                                    rt.AdGuardConfig.CustomUserRules = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                                    rt.SaveAdGuardConfigToDisk();
                                    _ = Task.Run(() => EnsureAdGuardMv3ExtensionProvisioned(GetAdGuardUnpackedExtensionFolder()));
                                }
                                else if (msg == "nexus_cmd:toggle_whitelist")
                                {
                                    await ToggleAdGuardWhitelistForCurrentSiteAsync();
                                }
                            }
                            catch { }
                        });
                    }
                    catch { }
                });
            }

            if (!_navigationCompletedFallbackAttached)
            {
                _ = AttachCoreWebView2Event(coreWv, "NavigationStarting", (s, a) =>
                {
                    _pendingNavigationTcs = new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
                    try
                    {
                        var rtCfg = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
                        if (rtCfg.IsAdGuardProtectionEnabled && rtCfg.AdGuardConfig.StripTrackingParams)
                        {
                            string? navUri = a?.GetType().GetProperty("Uri")?.GetValue(a) as string;
                            if (!string.IsNullOrWhiteSpace(navUri) &&
                                navUri.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                                System.Text.RegularExpressions.Regex.IsMatch(navUri, @"[?&](utm_[a-z_]+|fbclid|gclid|msclkid)=", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            {
                                string cleaned = System.Text.RegularExpressions.Regex.Replace(navUri, @"([?&])(utm_[a-z_]+|fbclid|gclid|msclkid)=[^&#]*(&|$)", "$1", System.Text.RegularExpressions.RegexOptions.IgnoreCase).TrimEnd('?', '&');
                                if (!string.Equals(cleaned, navUri, StringComparison.Ordinal))
                                {
                                    a?.GetType().GetProperty("Cancel")?.SetValue(a, true);
                                    Dispatcher.BeginInvoke(() => coreWv.GetType().GetMethod("Navigate", new Type[] { typeof(string) })?.Invoke(coreWv, new object[] { cleaned }));
                                    return;
                                }
                            }
                        }
                    }
                    catch { }
                    Dispatcher.BeginInvoke(() => SyncNavButtonsAndLoadingState(coreWv, true));
                });

                _ = AttachCoreWebView2Event(coreWv, "HistoryChanged", (s, a) =>
                {
                    Dispatcher.BeginInvoke(async () =>
                    {
                        SyncNavButtonsAndLoadingState(coreWv, _isWebViewCurrentlyLoading);
                        RefreshAdGuardFlyoutState();
                        var rt = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
                        if (rt.IsAdGuardProtectionEnabled && !rt.IsDomainWhitelisted(GetCurrentBrowserHostDomain()))
                        {
                            await ExecuteLiveScriptOnWebView2Async(GetAdGuardDocumentStartScript());
                        }
                    });
                });

                _navigationCompletedFallbackAttached = AttachCoreWebView2Event(coreWv, "NavigationCompleted", (sender, args) =>
                {
                    Dispatcher.BeginInvoke(async () =>
                    {
                        try
                        {
                            bool isSuccess = args?.GetType().GetProperty("IsSuccess")?.GetValue(args) is bool b ? b : true;
                            string webErr = args?.GetType().GetProperty("WebErrorStatus")?.GetValue(args)?.ToString() ?? (isSuccess ? "None" : "NavigationFailed");
                            _lastNavigationSucceeded = isSuccess;
                            _lastNavigationErrorStatus = isSuccess ? "None" : webErr;
                            _pendingNavigationTcs?.TrySetResult(isSuccess);
                            SyncNavButtonsAndLoadingState(coreWv, false);
                            RefreshAdGuardFlyoutState();
                            var rtGuard = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
                            if (rtGuard.IsAdGuardProtectionEnabled && !rtGuard.IsDomainWhitelisted(GetCurrentBrowserHostDomain()))
                            {
                                await ExecuteLiveScriptOnWebView2Async(GetAdGuardDocumentStartScript());
                            }
                            string currentSrc = coreWv.GetType().GetProperty("Source")?.GetValue(coreWv) as string ?? "";
                            string docTitle = coreWv.GetType().GetProperty("DocumentTitle")?.GetValue(coreWv) as string ?? "";
                            if (DataContext is AgentWorkspaceViewModel awVm && !string.IsNullOrWhiteSpace(currentSrc))
                            {
                                awVm.LastBrowserUrl = currentSrc;
                            }
                            string addrText = BrowserAddressBox?.Text ?? "";

                            if (!isSuccess &&
                                (currentSrc.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
                                 addrText.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
                                 addrText.StartsWith("extension://", StringComparison.OrdinalIgnoreCase)))
                            {
                                bool wantOptions = currentSrc.Contains("options", StringComparison.OrdinalIgnoreCase) ||
                                                   addrText.Contains("options", StringComparison.OrdinalIgnoreCase) ||
                                                   !currentSrc.Contains("popup", StringComparison.OrdinalIgnoreCase);
                                string displayExtUrl = $"extension://{EdgeStoreAdGuardId}/pages/{(wantOptions ? "options.html" : "popup.html")}";
                                string htmlPayload = wantOptions ? GetGenuineAdGuardOptionsWebUiHtml() : GetGenuineAdGuardPopupWebUiHtml();
                                coreWv.GetType().GetMethod("NavigateToString", new Type[] { typeof(string) })?.Invoke(coreWv, new object[] { htmlPayload });
                                if (BrowserAddressBox is not null) BrowserAddressBox.Text = displayExtUrl;
                                UpdateActiveBrowserTabInPlace(wantOptions ? "AdGuard Settings" : "AdGuard Popup", displayExtUrl);
                                RebuildMultiTabBrowserStrip();
                                return;
                            }

                            if (isSuccess && currentSrc.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
                            {
                                bool wantOptions = currentSrc.Contains("options", StringComparison.OrdinalIgnoreCase) ||
                                                   addrText.Contains("options", StringComparison.OrdinalIgnoreCase) ||
                                                   !currentSrc.Contains("popup", StringComparison.OrdinalIgnoreCase);
                                string displayExtUrl = $"extension://{EdgeStoreAdGuardId}/pages/{(wantOptions ? "options.html" : "popup.html")}";
                                if (BrowserAddressBox is not null && !BrowserAddressBox.IsKeyboardFocused)
                                    BrowserAddressBox.Text = displayExtUrl;
                                UpdateActiveBrowserTabInPlace(wantOptions ? "AdGuard Settings" : "AdGuard Popup", displayExtUrl);
                                RebuildMultiTabBrowserStrip();

                                await Task.Delay(500);
                                var execMethod = coreWv.GetType().GetMethod("ExecuteScriptAsync", new Type[] { typeof(string) });
                                if (execMethod?.Invoke(coreWv, new object[] { "(function(){ var r=document.getElementById('root'); return r && r.innerText && r.innerText.trim().length > 0 ? '1' : '0'; })()" }) is Task<string> rootTask)
                                {
                                    string rootRes = (await rootTask)?.Trim('"', ' ') ?? "0";
                                    if (rootRes != "1")
                                    {
                                        string htmlPayload = wantOptions ? GetGenuineAdGuardOptionsWebUiHtml() : GetGenuineAdGuardPopupWebUiHtml();
                                        coreWv.GetType().GetMethod("NavigateToString", new Type[] { typeof(string) })?.Invoke(coreWv, new object[] { htmlPayload });
                                        if (BrowserAddressBox is not null) BrowserAddressBox.Text = displayExtUrl;
                                    }
                                }
                                return;
                            }

                            if (isSuccess && currentSrc.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            {
                                var rtShow = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
                                string displayUrl = currentSrc;
                                if (!rtShow.AdGuardConfig.ShowFullUrlInAddressBar && Uri.TryCreate(currentSrc, UriKind.Absolute, out var parsedU))
                                {
                                    displayUrl = parsedU.Host;
                                }
                                if (currentSrc.StartsWith("https://adguard.extension.local/", StringComparison.OrdinalIgnoreCase))
                                {
                                    string activeId = !string.IsNullOrWhiteSpace(_adGuardExtensionId) ? _adGuardExtensionId : EdgeStoreAdGuardId;
                                    displayUrl = currentSrc.Replace("https://adguard.extension.local/", $"extension://{activeId}/", StringComparison.OrdinalIgnoreCase);
                                }
                                if (BrowserAddressBox is not null && !BrowserAddressBox.IsKeyboardFocused)
                                    BrowserAddressBox.Text = displayUrl;
                                Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current.RecordBrowsingHistory(docTitle, currentSrc);

                                var rt = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
                                var activeTab = rt.OpenBrowserTabs.FirstOrDefault(t => t.TabId == rt.ActiveBrowserTabId);
                                if (activeTab is not null && !activeTab.IsInternalPage)
                                {
                                    int idx = rt.OpenBrowserTabs.IndexOf(activeTab);
                                    rt.OpenBrowserTabs[idx] = activeTab with
                                    {
                                        Title = string.IsNullOrWhiteSpace(docTitle) ? currentSrc : docTitle,
                                        Url = currentSrc
                                    };
                                    RebuildMultiTabBrowserStrip();
                                }
                            }
                        }
                        catch { }
                    });
                });

                // Also register AdGuard unpacked extension in WebView2 profile on startup
                _ = Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        string extDir = GetAdGuardUnpackedExtensionFolder();
                        await RegisterExtensionFolderInWebView2ProfileAsync(extDir);
                        await QueryRealInstalledExtensionsAsync();
                    }
                    catch { }
                });
            }

            if (!_documentCreatedAdGuardInjected)
            {
                try
                {
                    var addDocScriptMethod = coreWv.GetType().GetMethod("AddScriptToExecuteOnDocumentCreatedAsync", new Type[] { typeof(string) });
                    if (addDocScriptMethod is not null)
                    {
                        _ = addDocScriptMethod.Invoke(coreWv, new object[] { GetAdGuardDocumentStartScript() });
                        _documentCreatedAdGuardInjected = true;
                    }

                    // Attach Native WebView2 Network-Layer Ad & Tracker Filter (WebResourceRequested)
                    var addFilterMethod = coreWv.GetType().GetMethods()
                        .FirstOrDefault(m => m.Name == "AddWebResourceRequestedFilter" && m.GetParameters().Length == 2);
                    if (addFilterMethod is not null)
                    {
                        var ctxEnumType = addFilterMethod.GetParameters()[1].ParameterType;
                        object allCtxVal = Enum.ToObject(ctxEnumType, 0); // CoreWebView2WebResourceContext.All = 0
                        string[] adPatterns = new[]
                        {
                            "*://*.doubleclick.net/*",
                            "*://*.googlesyndication.com/*",
                            "*://*.googleadservices.com/*",
                            "*://*.adservice.google.com/*",
                            "*://*.adnxs.com/*",
                            "*://*.taboola.com/*",
                            "*://*.outbrain.com/*",
                            "*://*.criteo.com/*",
                            "*://*.popads.net/*",
                            "*://*.scorecardresearch.com/*",
                            "*://*.moatads.com/*",
                            "*://auto-page.gnjoy.hk/*",
                            "*://*.youtube.com/api/stats/ads*",
                            "*://*.youtube.com/pagead/*",
                            "*://*.youtube.com/ptracking*"
                        };
                        foreach (string pat in adPatterns)
                        {
                            try { addFilterMethod.Invoke(coreWv, new object[] { pat, allCtxVal }); } catch { }
                        }

                        _ = AttachCoreWebView2Event(coreWv, "WebResourceRequested", (s, a) =>
                        {
                            try
                            {
                                var rt = Nexus.App.Services.BrowserAgent.BrowserAgentRuntimeSettings.Current;
                                if (!rt.IsAdGuardProtectionEnabled) return;
                                string host = GetCurrentBrowserHostDomain();
                                if (rt.IsDomainWhitelisted(host)) return;

                                var envObj = coreWv.GetType().GetProperty("Environment")?.GetValue(coreWv);
                                var createResp = envObj?.GetType().GetMethod("CreateWebResourceResponse");
                                if (createResp is not null)
                                {
                                    object? emptyResp = createResp.Invoke(envObj, new object?[] { System.IO.Stream.Null, 204, "No Content", "Access-Control-Allow-Origin: *" });
                                    a?.GetType().GetProperty("Response")?.SetValue(a, emptyResp);
                                    _adGuardBlockedCount++;
                                    rt.AdGuardConfig.TotalBlockedAdsCount++;
                                }
                            }
                            catch { }
                        });
                    }
                }
                catch { }
            }

            if (_newWindowInterceptorAttached) return;

            _newWindowInterceptorAttached = AttachCoreWebView2Event(coreWv, "NewWindowRequested", (sender, args) =>
            {
                try
                {
                    if (args is null) return;
                    string? newUri = args.GetType().GetProperty("Uri")?.GetValue(args) as string;
                    args.GetType().GetProperty("Handled")?.SetValue(args, true);
                    if (!string.IsNullOrWhiteSpace(newUri))
                    {
                        if (newUri.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
                        {
                            Dispatcher.BeginInvoke(async () =>
                            {
                                await OpenAdGuardInternalPageAsync(newUri.Contains("options", StringComparison.OrdinalIgnoreCase));
                            });
                            return;
                        }
                        var (allowed, _) = Nexus.App.Services.BrowserAgent.BrowserUseDomEngine.ValidateTargetSafety(newUri);
                        bool isGamblingOrAdPopup = System.Text.RegularExpressions.Regex.IsMatch(
                            newUri,
                            @"777|slot|gacor|judi|toto|maxwin|popads|doubleclick|googlesyndication|adservice|ratu89|pentaslot|kaiko",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                        if (allowed && !isGamblingOrAdPopup)
                        {
                            Dispatcher.BeginInvoke(() =>
                            {
                                string hostTitle = Uri.TryCreate(newUri, UriKind.Absolute, out var u) ? u.Host : "Browser";
                                UpdateActiveBrowserTabInPlace(hostTitle, newUri);
                                NavigateWebView2DirectSchemeSafe(newUri, fallbackHtml: null);
                            });
                        }
                        else
                        {
                            _adGuardBlockedCount++;
                        }
                    }
                }
                catch { }
            });
        }
        catch { }
    }

    private async Task<string> ExtractLiveWebView2DomTextAsync()
    {
        if (_activeWebView2Control is null) return string.Empty;
        EnsureNewWindowInterceptorAttached();
        await WaitForPendingNavigationAsync(3500);
        try
        {
            var execMethod = _activeWebView2Control.GetType().GetMethod("ExecuteScriptAsync", new Type[] { typeof(string) });
            if (execMethod is null) return string.Empty;

            string smartProbeScript = @"(function(){
                try {
                    if (!document.body) return '';

                    // 1. Unlock copy & right-click
                    try {
                        document.oncontextmenu = null;
                        document.onselectstart = null;
                        document.ondragstart = null;
                        document.body.style.userSelect = 'auto';
                        document.body.style.webkitUserSelect = 'auto';
                    } catch(ce) {}

                    // 2. AdGuard Cosmetic & External-Domain Gambling Banner Sweep
                    var blockedNow = 0;
                    var adSelectors = [
                        'iframe[src*=""doubleclick""]', 'iframe[src*=""googlesyndication""]', 'iframe[src*=""adservice""]',
                        '[id*=""iklan"" i]', '[class*=""iklan"" i]', '[id*=""banner-ad"" i]', '[class*=""banner-ad"" i]',
                        '[class*=""floating-ads"" i]', '[id*=""floating_ads"" i]', '[class*=""popup-ad"" i]',
                        'a[href*=""slot"" i]', 'a[href*=""gacor"" i]', 'a[href*=""777"" i]', 'a[href*=""toto"" i]',
                        'a[href*=""judi"" i]', 'a[href*=""maxwin"" i]', 'a[href*=""casino"" i]', 'a[href*=""sbobet"" i]',
                        'a[href*=""ratu89"" i]', 'a[href*=""pentaslot"" i]', 'a[href*=""kaiko"" i]',
                        'img[src*=""slot"" i]', 'img[src*=""gacor"" i]', 'img[src*=""777"" i]', 'img[alt*=""slot"" i]',
                        'img[alt*=""gacor"" i]', 'img[src*=""banner"" i][src$="".gif"" i]'
                    ];
                    try {
                        var adNodes = document.querySelectorAll(adSelectors.join(','));
                        for (var aIdx = 0; aIdx < adNodes.length; aIdx++) {
                            var adEl = adNodes[aIdx];
                            var wrapper = adEl.closest('.iklan, .ads, .banner, [id*=""iklan"" i], [class*=""iklan"" i]') || adEl;
                            wrapper.style.setProperty('display', 'none', 'important');
                            wrapper.setAttribute('data-nexus-ad-hidden','1');
                            blockedNow++;
                        }
                        var curHost = (location.hostname || '').toLowerCase();
                        var extLinks = document.querySelectorAll('a[target=""_blank""], a[rel*=""nofollow"" i]');
                        for (var eIdx = 0; eIdx < extLinks.length; eIdx++) {
                            var extA = extLinks[eIdx];
                            if (extA.closest('article, main, [role=""main""], .post-item, [class*=""card"" i]')) continue;
                            var aHost = (extA.hostname || '').toLowerCase();
                            var bImg = extA.querySelector('img');
                            if (bImg && aHost && curHost && aHost !== curHost && aHost.indexOf(curHost) < 0) {
                                var ir = bImg.getBoundingClientRect();
                                var iw = ir.width || bImg.width || bImg.naturalWidth || 0;
                                var ih = ir.height || bImg.height || bImg.naturalHeight || 0;
                                if ((iw >= 180 && ih >= 28 && ih <= 260) || (extA.href || '').match(/777|slot|gacor|judi|toto|89|bet|win|casino|bonus|daftar|login|rtp|scatter/i)) {
                                    extA.style.setProperty('display', 'none', 'important');
                                    extA.setAttribute('data-nexus-ad-hidden','1');
                                    blockedNow++;
                                }
                            }
                        }
                        var allBtns = document.querySelectorAll('a, button, div, span');
                        for (var bIdx = 0; bIdx < allBtns.length; bIdx++) {
                            var bEl = allBtns[bIdx];
                            var bTxt = (bEl.innerText || '').trim().toLowerCase();
                            if (bTxt === 'tutup iklan' || bTxt === 'close ad' || bTxt === 'skip ad' || bTxt === 'tutup [x]') {
                                var parentBox = bEl.parentElement;
                                if (parentBox && parentBox !== document.body && parentBox.clientHeight < 600) {
                                    parentBox.style.setProperty('display', 'none', 'important');
                                    parentBox.remove();
                                } else {
                                    bEl.style.setProperty('display', 'none', 'important');
                                }
                                blockedNow++;
                            }
                        }
                    } catch(ae) {}

                    window.__nexusAdGuardCount = (window.__nexusAdGuardCount || 0) + blockedNow;

                    var rawLen = (document.body.innerText || '').length;
                    if (document.readyState !== 'complete' && rawLen < 120) return '';

                    function isAdOrMalicious(el, href) {
                        if (el.id === '__nexus_agent_hud') return true;
                        var h = (href || '').toLowerCase();
                        if (h.indexOf('doubleclick.net') >= 0 || h.indexOf('googlesyndication.com') >= 0 ||
                            h.indexOf('googleadservices.com') >= 0 || h.indexOf('adservice.') >= 0 ||
                            h.indexOf('taboola.') >= 0 || h.indexOf('outbrain.') >= 0 ||
                            h.indexOf('popads.') >= 0 || h.match(/\.(exe|msi|apk|bat|scr)(\?|$)/) ||
                            h.match(/slot|gacor|777|toto|maxwin|judi/i)) {
                            return true;
                        }
                        var cur = el;
                        for (var d = 0; d < 4 && cur && cur !== document.body; d++) {
                            var idCls = ((cur.id || '') + ' ' + (cur.className || '') + ' ' + (cur.getAttribute('aria-label') || '') + ' ' + (cur.getAttribute('rel') || '')).toLowerCase();
                            if (idCls.match(/\b(ad|ads|advert|sponsored|promo-banner|google_ads|popup-ad|iklan)\b/)) return true;
                            cur = cur.parentElement;
                        }
                        return false;
                    }

                    function isVisibleInViewport(el) {
                        if (!el || !el.getBoundingClientRect) return false;
                        var st = window.getComputedStyle(el);
                        if (st.display === 'none' || st.visibility === 'hidden' || parseFloat(st.opacity) < 0.08) return false;
                        var r = el.getBoundingClientRect();
                        return r.width >= 6 && r.height >= 6;
                    }

                    function getXPath(el) {
                        if (el.id) return '//*[@id=""' + el.id.replace(/""/g, '\\""') + '""]';
                        var parts = [];
                        var cur = el;
                        while (cur && cur.nodeType === 1) {
                            var idx = 1;
                            var sib = cur.previousElementSibling;
                            while (sib) {
                                if (sib.tagName === cur.tagName) idx++;
                                sib = sib.previousElementSibling;
                            }
                            parts.unshift(cur.tagName.toLowerCase() + '[' + idx + ']');
                            cur = cur.parentElement;
                        }
                        return '/' + parts.join('/');
                    }

                    var out = [];
                    out.push('[PAGE_TITLE] ' + (document.title || ''));
                    out.push('[PAGE_URL] ' + (location.href || ''));
                    out.push('[READY_STATE] ' + (document.readyState || 'complete'));
                    var bodyTxtLower = (document.body.innerText || '').toLowerCase();
                    var hasQrOrAuthGate = (bodyTxtLower.indexOf('scan to log in') >= 0 ||
                                           bodyTxtLower.indexOf('gunakan whatsapp di komputer') >= 0 ||
                                           bodyTxtLower.indexOf('log in with phone number') >= 0 ||
                                           !!document.querySelector('canvas[aria-label*=""Scan"" i], [data-testid=""qrcode""]'));
                    if (hasQrOrAuthGate) {
                        out.push('[PAGE_LIFECYCLE] AUTH_OR_QR_REQUIRED');
                    } else {
                        out.push('[PAGE_LIFECYCLE] READY');
                    }
                    out.push('[RAW_CHAR_COUNT] ' + rawLen);
                    out.push('[ADGUARD_BLOCKED] ' + (window.__nexusAdGuardCount || 0));

                    var pdfMeta = document.querySelector('meta[name=""citation_pdf_url""]');
                    var directPdfUrl = pdfMeta ? (pdfMeta.getAttribute('content') || '') : '';
                    var pdfGalleyBtn = document.querySelector('.gs_or_ggsm a[href^=""http""], a[href*=""/article/download/""], a[href*=""/article/viewFile/""], a.obj_galley_link, a.download, a[href$="".pdf""]');
                    if (pdfGalleyBtn && pdfGalleyBtn.href && !isAdOrMalicious(pdfGalleyBtn, pdfGalleyBtn.href)) {
                        if (!directPdfUrl) directPdfUrl = pdfGalleyBtn.href;
                        pdfGalleyBtn.style.outline = '2.5px solid #10B981';
                        pdfGalleyBtn.style.backgroundColor = 'rgba(16,185,129,0.15)';
                        pdfGalleyBtn.style.borderRadius = '4px';
                    }
                    if (directPdfUrl) {
                        out.push('[DIRECT_PDF_URL] ' + directPdfUrl);
                    }

                    // 3. Index all interactive elements (prioritizing inputs & main-content links/cards/episodes before header/nav/footer) into window.__nexusDomMap
                    window.__nexusDomMap = window.__nexusDomMap || {};
                    out.push('[INTERACTIVE_ELEMENTS]');
                    var rawCandidates = Array.prototype.slice.call(document.querySelectorAll('input:not([type=""hidden""]), textarea, select, [contenteditable=""true""], [contenteditable=""plaintext-only""], [role=""textbox""], [role=""searchbox""], [role=""combobox""], button, a[href], [role=""button""], [data-episode], .play-button, .vjs-big-play-button, video'));
                    function isNavChrome(el) {
                        if (!el || typeof el.closest !== 'function') return false;
                        var tag = (el.tagName || '').toLowerCase();
                        if (tag === 'input' || tag === 'textarea' || tag === 'select') return false;
                        return !!el.closest('header, nav, footer, [role=""navigation""], .navbar, .nav-menu, .mobile-menu, .menu-drawer, #header, #footer, #navbar');
                    }
                    var intCandidates = rawCandidates.filter(function(el){ return !isNavChrome(el); }).concat(rawCandidates.filter(function(el){ return isNavChrome(el); }));
                    var intIdx = 0;
                    for (var ic = 0; ic < intCandidates.length && intIdx < 60; ic++) {
                        var iEl = intCandidates[ic];
                        if (!isVisibleInViewport(iEl) || isAdOrMalicious(iEl, iEl.href || '')) continue;
                        var iTag = iEl.tagName.toLowerCase();
                        var isCe = iEl.isContentEditable || iEl.getAttribute('contenteditable') === 'true' || iEl.getAttribute('contenteditable') === 'plaintext-only';
                        var iRole = (iEl.getAttribute('role') || (isCe ? 'textbox' : iTag)).toLowerCase();
                        var iType = (iEl.getAttribute('type') || '').toLowerCase();
                        var iLabel = (
                            iEl.getAttribute('aria-label') ||
                            iEl.getAttribute('placeholder') ||
                            iEl.getAttribute('data-placeholder') ||
                            iEl.innerText ||
                            iEl.value ||
                            iEl.getAttribute('title') ||
                            iEl.getAttribute('name') ||
                            ''
                        ).replace(/[\r\n|]+/g, ' ').replace(/\s+/g, ' ').trim();
                        if (!iLabel && typeof iEl.querySelector === 'function') {
                            var chTitle = iEl.querySelector('h1, h2, h3, h4, .title, .name, .ep-title, img[alt], [title]');
                            if (chTitle) iLabel = (chTitle.innerText || chTitle.getAttribute('title') || chTitle.getAttribute('alt') || '').replace(/[\r\n|]+/g, ' ').replace(/\s+/g, ' ').trim();
                        }
                        if (!iLabel && (isCe || iTag === 'input' || iTag === 'textarea' || iTag === 'a')) {
                            var pNode = iEl.parentElement;
                            if (pNode) iLabel = (pNode.innerText || pNode.getAttribute('aria-label') || pNode.getAttribute('title') || '').replace(/[\r\n|]+/g, ' ').replace(/\s+/g, ' ').trim();
                        }
                        if (!iLabel) iLabel = iTag;
                        if (iLabel.length > 120) iLabel = iLabel.substring(0, 120);
                        var iHref = (iEl.href || '').replace(/\|/g, '');
                        intIdx++;
                        window.__nexusDomMap[intIdx] = iEl;
                        iEl.setAttribute('data-nexus-idx', String(intIdx));
                        iEl.setAttribute('data-agent-ref', 'e' + intIdx);
                        var xp = getXPath(iEl);
                        out.push('[' + intIdx + ']|' + iTag + '|' + iRole + '|' + iType + '|' + (isCe ? '1' : '0') + '|' + iLabel + '|' + iHref + '|' + xp);
                    }

                    var links = document.querySelectorAll('h3 a[href^=""http""], .gs_rt a[href^=""http""], a.obj_galley_link, a[href*=""/article/""], a[href^=""http""]');
                    var seenUrls = {};
                    var refId = 0;
                    out.push('[SOURCES]');
                    for (var i = 0; i < links.length && refId < 6; i++) {
                        var a = links[i];
                        var href = a.href || '';
                        if (!href || href.indexOf('google.com/search') >= 0 || href.indexOf('scholar.google.com/scholar?') >= 0 ||
                            href.indexOf('accounts.google') >= 0 || href.indexOf('support.google') >= 0 ||
                            href.indexOf('policies.google') >= 0) continue;
                        if (isAdOrMalicious(a, href) || !isVisibleInViewport(a)) continue;

                        var txt = (a.innerText || a.getAttribute('aria-label') || '').replace(/\s+/g, ' ').trim();
                        if (txt.length >= 3 && txt.length <= 160 && !seenUrls[href]) {
                            if (txt.length < 12 && !txt.match(/\b(pdf|download|unduh|full\s*text)\b/i)) continue;
                            seenUrls[href] = true;
                            refId++;
                            if (refId === 1) {
                                a.style.outline = '2.5px solid #2563EB';
                                a.style.backgroundColor = 'rgba(37,99,235,0.10)';
                                a.style.borderRadius = '4px';
                                a.style.padding = '1px 4px';
                            } else if (refId <= 3) {
                                a.style.outline = '1.5px dashed #60A5FA';
                                a.style.borderRadius = '3px';
                            }
                            out.push('[' + refId + '] ' + txt + ' | ' + href);
                        }
                    }

                    out.push('[SEMANTIC_CONTENT]');
                    var root = document.querySelector('main, article, [role=""main""], #gs_res_ccl_mid, #search, #rso') || document.body;
                    var blocks = root.querySelectorAll('h1, h2, h3, p, li, tr, .gs_rs, .gs_a, .message-in, .markdown, model-response, [data-message-author-role], [data-attrid], .VwiC3b, .hgKElc');
                    var seenText = {};
                    for (var j = 0; j < blocks.length; j++) {
                        var el = blocks[j];
                        if (el.id === '__nexus_agent_hud') continue;
                        if (el.closest('nav, footer, header, script, style, noscript, [aria-hidden=""true""]')) continue;
                        if (isAdOrMalicious(el, '')) continue;
                        var line = (el.innerText || '').replace(/\s+/g, ' ').trim();
                        if (line.length < 15 || line.length > 500) continue;
                        var key = line.toLowerCase();
                        if (seenText[key]) continue;
                        seenText[key] = true;
                        var tag = el.tagName.toLowerCase();
                        if (tag === 'h1' || tag === 'h2' || tag === 'h3') {
                            out.push('## ' + line);
                        } else {
                            out.push('- ' + line);
                        }
                        if (out.length > 65) break;
                    }

                    if (out.length <= 10) {
                        var rawLines = (root.innerText || '').split('\n');
                        for (var k = 0; k < rawLines.length && out.length < 55; k++) {
                            var rl = rawLines[k].trim();
                            if (rl.length >= 12 && !rl.startsWith('🛡️') && !seenText[rl.toLowerCase()]) {
                                seenText[rl.toLowerCase()] = true;
                                out.push('- ' + rl);
                            }
                        }
                    }
                    return out.join('\n');
                } catch(e) { return ''; }
            })()";

            string bestExtracted = string.Empty;
            int prevLength = -1;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                await Task.Delay(180);
                if (execMethod.Invoke(_activeWebView2Control, new object?[] { smartProbeScript }) is Task<string> scriptTask)
                {
                    string jsonResult = await scriptTask;
                    if (!string.IsNullOrWhiteSpace(jsonResult) && jsonResult != "null" && jsonResult != "\"\"")
                    {
                        string? decoded = System.Text.Json.JsonSerializer.Deserialize<string>(jsonResult);
                        if (!string.IsNullOrWhiteSpace(decoded) && decoded.Length >= 80)
                        {
                            bestExtracted = decoded;
                            var mBlocked = System.Text.RegularExpressions.Regex.Match(decoded, @"\[ADGUARD_BLOCKED\]\s+(\d+)");
                            if (mBlocked.Success && int.TryParse(mBlocked.Groups[1].Value, out int bCount))
                            {
                                _adGuardBlockedCount = Math.Max(_adGuardBlockedCount, bCount);
                            }
                            bestExtracted = System.Text.RegularExpressions.Regex.Replace(
                                bestExtracted,
                                @"^\[ADGUARD_BLOCKED\][^\r\n]*[\r\n]*",
                                string.Empty,
                                System.Text.RegularExpressions.RegexOptions.Multiline);
                            if (prevLength > 0 && Math.Abs(decoded.Length - prevLength) < 35)
                            {
                                break;
                            }
                            prevLength = decoded.Length;
                        }
                    }
                }
            }

            string navStatusHeader = _lastNavigationSucceeded
                ? "[NAVIGATION_STATUS] OK\n"
                : $"[NAVIGATION_STATUS] FAILED:{_lastNavigationErrorStatus}\n";
            if (string.IsNullOrWhiteSpace(bestExtracted))
            {
                return _lastNavigationSucceeded ? string.Empty : navStatusHeader.Trim();
            }
            bestExtracted = navStatusHeader + bestExtracted;
            if (bestExtracted.Length > 9600)
                bestExtracted = bestExtracted[..9600];
            return bestExtracted;
        }
        catch
        {
            return string.Empty;
        }
    }


    private async Task NavigateEmbeddedBrowserAsync(string? rawAddress, bool pushHistory)
    {
        if (!string.IsNullOrWhiteSpace(rawAddress) && (rawAddress.StartsWith("about:", StringComparison.OrdinalIgnoreCase) || rawAddress.StartsWith("file:///", StringComparison.OrdinalIgnoreCase) || rawAddress.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)))
        {
            if (rawAddress.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
            {
                await OpenAdGuardInternalPageAsync(rawAddress.Contains("options", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                NavigateWebView2DirectSchemeSafe(rawAddress);
            }
            return;
        }

        EnsureChromeExtensionsToolbarButtonAttached();
        string address = rawAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(address)) return;

        if (!address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (address.Contains(' ') || !address.Contains('.'))
            {
                address = "https://www.google.com/search?q=" + Uri.EscapeDataString(address);
            }
            else
            {
                address = "https://" + address;
            }
        }

        if (BrowserAddressBox is not null)
            BrowserAddressBox.Text = address;
        if (BrowserAddressWatermark is not null)
            BrowserAddressWatermark.Visibility = Visibility.Collapsed;
        if (BrowserEmptyStateText is not null)
            BrowserEmptyStateText.Visibility = Visibility.Collapsed;
        if (BrowserReaderBox is not null)
            BrowserReaderBox.Visibility = Visibility.Collapsed;

        if (pushHistory)
        {
            if (_browserHistoryIndex < _browserHistory.Count - 1)
                _browserHistory.RemoveRange(_browserHistoryIndex + 1, _browserHistory.Count - _browserHistoryIndex - 1);
            if (_browserHistory.Count == 0 || !string.Equals(_browserHistory[^1], address, StringComparison.OrdinalIgnoreCase))
            {
                _browserHistory.Add(address);
                _browserHistoryIndex = _browserHistory.Count - 1;
            }
        }

        if (observed is not null)
            observed.LastBrowserUrl = address;

        if (!await NavigateViaWebView2ChromiumAsync(address))
        {
            NavigateViaNativeWindowsWebBrowser(address);
        }
    }

    private void BrowserBack_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_activeWebView2Control is not null)
            {
                var goBack = _activeWebView2Control.GetType().GetMethod("GoBack");
                goBack?.Invoke(_activeWebView2Control, null);
                return;
            }
            if (_fallbackNativeBrowser is { CanGoBack: true })
            {
                _fallbackNativeBrowser.GoBack();
                return;
            }
        }
        catch { }

        if (_browserHistoryIndex > 0)
        {
            _browserHistoryIndex--;
            _ = NavigateEmbeddedBrowserAsync(_browserHistory[_browserHistoryIndex], pushHistory: false);
        }
    }

    private void BrowserForward_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_activeWebView2Control is not null)
            {
                var goForward = _activeWebView2Control.GetType().GetMethod("GoForward");
                goForward?.Invoke(_activeWebView2Control, null);
                return;
            }
            if (_fallbackNativeBrowser is { CanGoForward: true })
            {
                _fallbackNativeBrowser.GoForward();
                return;
            }
        }
        catch { }

        if (_browserHistoryIndex + 1 < _browserHistory.Count)
        {
            _browserHistoryIndex++;
            _ = NavigateEmbeddedBrowserAsync(_browserHistory[_browserHistoryIndex], pushHistory: false);
        }
    }

    private void BrowserReload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_activeWebView2Control is not null)
            {
                var reload = _activeWebView2Control.GetType().GetMethod("Reload");
                reload?.Invoke(_activeWebView2Control, null);
                return;
            }
            if (_fallbackNativeBrowser is not null)
            {
                _fallbackNativeBrowser.Refresh();
                return;
            }
        }
        catch { }

        _ = NavigateEmbeddedBrowserAsync(BrowserAddressBox?.Text, pushHistory: false);
    }

    private void BrowserOpenExternal_Click(object sender, RoutedEventArgs e)
    {
        string address = BrowserAddressBox?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(address)) return;
        if (!address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            address = "https://" + address;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(address) { UseShellExecute = true });
        }
        catch { }
    }

    private void ToggleMarkdownOrCodeMode_Click(object sender, RoutedEventArgs e)
    {
        _markdownPreviewMode = !_markdownPreviewMode;
        RenderSelectedDiff();
    }

    private void ReloadSelectedFile_Click(object sender, RoutedEventArgs e)
    {
        var file = observed?.SelectedReviewFile;
        if (file is null || observed is null) return;
        try
        {
            if (System.IO.File.Exists(file.Target))
            {
                var lines = System.IO.File.ReadAllLines(file.Target).Take(160).Select(l => "  " + l);
                var updated = new ReviewFileViewModel(file.Target, string.Join("\n", lines));
                var list = observed.ReviewFiles.Select(f => ReferenceEquals(f, file) ? updated : f).ToList();
                var openList = observed.OpenReviewFiles.Select(f => ReferenceEquals(f, file) ? updated : f).ToList();
                typeof(AgentWorkspaceViewModel).GetField("reviewFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(observed, list.AsReadOnly());
                typeof(AgentWorkspaceViewModel).GetField("openReviewFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(observed, openList.AsReadOnly());
                observed.SelectReviewFileCommand.Execute(updated);
            }
        }
        catch { }
        RenderSelectedDiff();
    }

    private void ColoredDiffReview_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        SyncLineNumberGutter();
    }

    private void SyncLineNumberGutter()
    {
        if (LineNumberCanvas is null || ColoredDiffReview?.Document is null) return;
        LineNumberCanvas.Children.Clear();
        if (LineNumberGutterColumn.Width.Value <= 0) return;

        var numBrush = TryFindResource("DshTextTertiaryBrush") as Brush
                       ?? TryFindResource("DshTextSecondaryBrush") as Brush
                       ?? Brushes.Gray;
        int lineNum = 1;
        double canvasHeight = ColoredDiffReview.ActualHeight;
        foreach (var block in ColoredDiffReview.Document.Blocks)
        {
            if (block is Paragraph p)
            {
                try
                {
                    Rect rect = p.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                    if (rect.Top >= -24 && rect.Top <= canvasHeight + 24)
                    {
                        var tb = new TextBlock
                        {
                            Text = lineNum.ToString(CultureInfo.InvariantCulture),
                            FontFamily = new FontFamily("Consolas"),
                            FontSize = 11.5,
                            Foreground = numBrush,
                            TextAlignment = TextAlignment.Right,
                            Width = 30,
                            IsHitTestVisible = false
                        };
                        Canvas.SetLeft(tb, 2);
                        Canvas.SetTop(tb, rect.Top);
                        LineNumberCanvas.Children.Add(tb);
                    }
                }
                catch { }
                lineNum++;
            }
        }
    }

    private void RenderSelectedDiff()
    {
        if(ColoredDiffReview is null || rendering)return;
        rendering=true;
        try
        {
            if(renderedFile is not null && !awaitingRestore)readingPositions[renderedFile]=(ColoredDiffReview.VerticalOffset,ColoredDiffReview.HorizontalOffset,WrapDiffToggle.IsChecked!=false);
            if(!ReferenceEquals(renderedFiles,observed?.ReviewFiles))
            {readingPositions.Clear();renderedFiles=observed?.ReviewFiles;}
            var file=observed?.SelectedReviewFile;
            if(ReviewEmptyState is not null)ReviewEmptyState.Visibility=(file is not null && file.Lines.Count>0)?Visibility.Collapsed:Visibility.Visible;
            var position=file is not null && readingPositions.TryGetValue(file,out var stored)?stored:(Vertical:0d,Horizontal:0d,Wrap:true);
            WrapDiffToggle.IsChecked=position.Wrap;

            bool isPureCodeView = file is not null && file.Additions == 0 && file.Deletions == 0;
            bool isRenderedMarkdown = isPureCodeView && file is { IsMarkdownFile: true } && _markdownPreviewMode;

            if (LineNumberGutterColumn is not null)
                LineNumberGutterColumn.Width = new GridLength((isPureCodeView && !isRenderedMarkdown) ? 42 : 0);
            if (CodeLanguageBanner is not null)
                CodeLanguageBanner.Visibility = (isPureCodeView && !isRenderedMarkdown) ? Visibility.Visible : Visibility.Collapsed;
            if (BtnApplyDiffToDisk is not null)
                BtnApplyDiffToDisk.Visibility = (file is { HasDiffChanges: true }) ? Visibility.Visible : Visibility.Collapsed;
            if (RendererKindLabel is not null && file is not null)
                RendererKindLabel.Text = isRenderedMarkdown ? "Markdown" : file.FileLanguageLabel;

            var bodyFont = isRenderedMarkdown
                ? (TryFindResource("NexusTypographyFontFamily") as FontFamily ?? new FontFamily("Segoe UI"))
                : new FontFamily("Consolas");
            var document=new FlowDocument
            {
                FontFamily = bodyFont,
                FontSize = isRenderedMarkdown ? 13.5 : 12,
                PagePadding = isRenderedMarkdown ? new Thickness(20, 14, 20, 20) : new Thickness(4, 2, 8, 8),
                ColumnWidth = double.PositiveInfinity
            };

            var width=260d;var previousChanged=false;changedBlocks.Clear();changeIndex=-1;
            foreach(var line in file?.Lines ?? [])
            {
                var paragraph=new Paragraph {Margin=new Thickness(0),Padding=new Thickness(4,2,4,2)};
                paragraph.Tag=line;

                if (isRenderedMarkdown)
                {
                    string rawMd = line.Code;
                    string trimmed = rawMd.TrimStart();
                    if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                    {
                        paragraph.FontSize = 21;
                        paragraph.FontWeight = FontWeights.Bold;
                        paragraph.Margin = new Thickness(0, 10, 0, 6);
                        paragraph.Inlines.Add(new Run(trimmed[2..]));
                    }
                    else if (trimmed.StartsWith("## ", StringComparison.Ordinal))
                    {
                        paragraph.FontSize = 16;
                        paragraph.FontWeight = FontWeights.SemiBold;
                        paragraph.Margin = new Thickness(0, 10, 0, 4);
                        paragraph.Inlines.Add(new Run(trimmed[3..]));
                    }
                    else if (trimmed.StartsWith("### ", StringComparison.Ordinal))
                    {
                        paragraph.FontSize = 14;
                        paragraph.FontWeight = FontWeights.SemiBold;
                        paragraph.Margin = new Thickness(0, 6, 0, 2);
                        paragraph.Inlines.Add(new Run(trimmed[4..]));
                    }
                    else if (trimmed.StartsWith("---", StringComparison.Ordinal))
                    {
                        paragraph.BorderBrush = TryFindResource("DshBorderL2Brush") as Brush ?? Brushes.LightGray;
                        paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
                        paragraph.Margin = new Thickness(0, 6, 0, 6);
                        paragraph.Inlines.Add(new Run(string.Empty));
                    }
                    else
                    {
                        paragraph.Inlines.Add(new Run(rawMd));
                    }
                    paragraph.SetResourceReference(TextElement.ForegroundProperty, "NexusForegroundPrimary");
                    document.Blocks.Add(paragraph);
                    continue;
                }

                // Pure code view: DO NOT insert line numbers into Paragraph.Inlines!
                // Line numbers are rendered in the separate non-selectable LineNumberCanvas (Grid.Column=0),
                // so wrapped lines stay 100% left-aligned in Grid.Column=1 and copying never copies line numbers!
                string prefixText = isPureCodeView ? string.Empty : (line.Marker + " ");
                var text = prefixText + line.Code;
                if(ThemeManager.CurrentTheme==ThemeMode.HighContrast)paragraph.Inlines.Add(new Run(line.Code));
                else NexusSyntaxColorizer.PopulateInlines(paragraph,System.IO.Path.GetExtension(file!.Target).TrimStart('.'),line.Code);
                if (!string.IsNullOrEmpty(prefixText))
                {
                    var marker=new Run(prefixText);
                    if(paragraph.Inlines.FirstInline is { } first)paragraph.Inlines.InsertBefore(first,marker);else paragraph.Inlines.Add(marker);
                }
                paragraph.SetResourceReference(TextElement.ForegroundProperty,"NexusForegroundPrimary");
                if(line.Kind!=ReviewDiffLineKind.Context)
                {
                    var bgKey = line.Kind == ReviewDiffLineKind.Added ? "DshDiffAddedBgBrush" : "DshDiffDeletedBgBrush";
                    var fgKey = line.Kind == ReviewDiffLineKind.Added ? "DshDiffAddedFgBrush" : "DshDiffDeletedFgBrush";
                    if (TryFindResource(bgKey) is Brush bg) paragraph.Background = bg;
                    if (TryFindResource(fgKey) is Brush fg) paragraph.Foreground = fg;
                }
                var formatted=new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Consolas"),12,Brushes.Black,VisualTreeHelper.GetDpi(this).PixelsPerDip);
                width=Math.Max(width,formatted.WidthIncludingTrailingWhitespace+32);
                document.Blocks.Add(paragraph);
                var changed=line.Kind!=ReviewDiffLineKind.Context;
                if(changed && !previousChanged)changedBlocks.Add(paragraph);
                previousChanged=changed;
            }
            var wrap = isRenderedMarkdown || WrapDiffToggle.IsChecked!=false;
            document.PageWidth=wrap?double.NaN:width;
            ColoredDiffReview.HorizontalScrollBarVisibility=wrap?ScrollBarVisibility.Disabled:ScrollBarVisibility.Auto;
            ColoredDiffReview.Document=document;
            foreach(var paragraph in document.Blocks.Cast<Paragraph>())
            {
                var background=(paragraph.Background as SolidColorBrush)?.Color??(ColoredDiffReview.Background as SolidColorBrush)?.Color??SystemColors.WindowColor;
                foreach(var run in paragraph.Inlines.OfType<Run>())
                    if(run.Foreground is SolidColorBrush brush && (brush.Color.A<255 || Contrast(brush.Color,background)<4.5))run.Foreground=paragraph.Foreground;
            }
            renderedFile=file;awaitingRestore=file is not null;UpdateChangeNavigation();
            if(file is not null)Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>
            {
                if(!ReferenceEquals(ColoredDiffReview.Document,document) || !ReferenceEquals(observed?.SelectedReviewFile,file))return;
                ColoredDiffReview.UpdateLayout();
                if (awaitingRestore)
                {
                    awaitingRestore=false;
                    ColoredDiffReview.ScrollToVerticalOffset(position.Vertical);
                    ColoredDiffReview.ScrollToHorizontalOffset(position.Horizontal);
                }
                SyncLineNumberGutter();
            }));
            else
            {
                SyncLineNumberGutter();
            }
        }
        finally {rendering=false;}
    }

    private void PreviousDiffChange_Click(object sender,RoutedEventArgs e)=>NavigateChange(-1);
    private void NextDiffChange_Click(object sender,RoutedEventArgs e)=>NavigateChange(1);
    private void NavigateChange(int direction)
    {
        var next=changeIndex+direction;if(next<0 || next>=changedBlocks.Count)return;
        awaitingRestore=false;changeIndex=next;var paragraph=changedBlocks[next];
        ColoredDiffReview.CaretPosition=paragraph.ContentStart;paragraph.BringIntoView();ColoredDiffReview.Focus();UpdateChangeNavigation();
    }
    private void UpdateChangeNavigation()
    {
        if(PreviousChangeButton is null)return;
        PreviousChangeButton.IsEnabled=changeIndex>0;NextChangeButton.IsEnabled=changeIndex+1<changedBlocks.Count;
        ChangePositionText.Text=changedBlocks.Count==0?"0 blok":changeIndex<0?$"{changedBlocks.Count} blok":$"{changeIndex+1}/{changedBlocks.Count}";
    }
    private static double Contrast(Color foreground,Color background)
    {
        static double Luminance(Color c)
        {
            static double Channel(byte b){var n=b/255d;return n<=.04045?n/12.92:Math.Pow((n+.055)/1.055,2.4);}
            return .2126*Channel(c.R)+.7152*Channel(c.G)+.0722*Channel(c.B);
        }
        var a=Luminance(foreground);var b=Luminance(background);return (Math.Max(a,b)+.05)/(Math.Min(a,b)+.05);
    }
    private void CopySelectedDiffCode_Click(object sender, RoutedEventArgs e)
    {
        var file = observed?.SelectedReviewFile;
        if (file is null || file.Lines.Count == 0) return;
        var text = string.Join(Environment.NewLine, file.Lines.Select(l => l.Code));
        try { Clipboard.SetText(text); } catch { }
    }
    private void ToggleExpandReviewWidth_Click(object sender, RoutedEventArgs e)
    {
        DependencyObject? cur = this;
        while (cur is not null && cur is not ConversationView)
            cur = VisualTreeHelper.GetParent(cur);
        if (cur is ConversationView cv)
            cv.ToggleReviewPanelWidth();
    }
}
