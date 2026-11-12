using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Nexus.App.Controls.ContentDialog;
using Nexus.App.Services;
using Nexus.App.ViewModels;

namespace Nexus.App.Views
{
    public partial class ConversationView : UserControl
    {
        private const double ConversationPanelExpandedWidth = 260;
        private const double ConversationPanelCollapsedWidth = 0;
        private static readonly TimeSpan ConversationPanelAnimationDuration =
            TimeSpan.FromMilliseconds(160);
        private bool _followTail = true;
        private bool _scrollPending;
        private AgentWorkspaceViewModel? _observedAgent;
        private FrameworkElement? _reviewReturnFocus;
        private IReadOnlyList<ReviewFileViewModel>? _reviewReturnFiles;
        private bool _reviewWasOpen;

        public ConversationView()
        {
            InitializeComponent();
            UpdateResponsiveReadingGutter(
                GetMetric("NexusAIWorkspaceReadingViewportNormal"));
            DataContextChanged += OnDataContextChanged;
        }

        private void AIWorkspaceMain_SizeChanged(
            object sender,
            SizeChangedEventArgs e)
        {
            if (e.WidthChanged)
            {
                UpdateResponsiveReadingGutter(e.NewSize.Width);
                UpdateContextPanelLayout();
            }
        }

        private void UpdateResponsiveReadingGutter(double viewportWidth)
        {
            var normalBreakpoint =
                GetMetric("NexusAIWorkspaceReadingViewportNormal");
            var mediumBreakpoint =
                GetMetric("NexusAIWorkspaceReadingViewportMedium");
            var gutter = viewportWidth >= normalBreakpoint
                ? GetMetric("NexusAIWorkspaceReadingGutterNormal")
                : viewportWidth >= mediumBreakpoint
                    ? GetMetric("NexusAIWorkspaceReadingGutterMedium")
                    : GetMetric("NexusAIWorkspaceReadingGutterMinimum");

            Resources["AIWorkspaceActiveReadingGutter"] =
                new Thickness(gutter, 0, gutter, 0);
            var scrollChromeWidth = AIChatScroll is { ViewportWidth: > 0 }
                ? Math.Max(0, AIChatScroll.ActualWidth - AIChatScroll.ViewportWidth)
                : 0;
            Resources["AIWorkspaceActiveComposerMargin"] =
                new Thickness(
                    gutter,
                    0,
                    gutter + scrollChromeWidth,
                    GetMetric("NexusSpacing24"));
        }

        private double GetMetric(string key)
        {
            return FindResource(key) is double value
                ? value
                : throw new InvalidOperationException(
                    $"Resource '{key}' must be a Double.");
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if(_observedAgent!=null)_observedAgent.PropertyChanged-=Agent_PropertyChanged;
            _observedAgent=(e.NewValue as ConversationViewModel)?.AgentWorkspace;
            _reviewReturnFocus=null;_reviewReturnFiles=null;_reviewWasOpen=_observedAgent?.IsOpen==true;
            if(_observedAgent!=null)_observedAgent.PropertyChanged+=Agent_PropertyChanged;
            if (e.OldValue is ConversationViewModel oldVm)
            {
                oldVm.Messages.CollectionChanged -= Messages_CollectionChanged;
                oldVm.ComposerFocusRequested -= ComposerFocusRequested;
                oldVm.PropertyChanged -= Vm_PropertyChanged;
            }
            if (e.NewValue is ConversationViewModel newVm)
            {
                newVm.Messages.CollectionChanged += Messages_CollectionChanged;
                newVm.ComposerFocusRequested += ComposerFocusRequested;
                newVm.PropertyChanged += Vm_PropertyChanged;
                SubscribeToItemChanges(newVm);
                ScrollToBottom();
                DshSidebar?.SetExpandedState(newVm.IsConversationPanelVisible, animate: false);
            }
            UpdateContextPanelLayout();
        }
        private void Agent_PropertyChanged(object? sender,PropertyChangedEventArgs e)
        {
            if(e.PropertyName!=nameof(AgentWorkspaceViewModel.IsOpen))return;
            var agent=_observedAgent;var opened=agent?.IsOpen==true;var wasOpen=_reviewWasOpen;
            if(opened && !wasOpen)
            {
                _reviewReturnFocus=Keyboard.FocusedElement is FrameworkElement focused && focused.IsDescendantOf(this)?focused:null;
                _reviewReturnFiles=agent?.ReviewFiles;
            }
            _reviewWasOpen=opened;UpdateContextPanelLayout(animate: IsLoaded && opened != wasOpen);
            if(!wasOpen || opened)return;
            var target=_reviewReturnFocus;var files=_reviewReturnFiles;_reviewReturnFocus=null;_reviewReturnFiles=null;
            Dispatcher.InvokeAsync(()=>
            {
                if(!IsVisible || !ReferenceEquals(_observedAgent,agent) || agent?.IsOpen==true || DataContext is not ConversationViewModel {IsChatModeActive:true})return;
                if(ReferenceEquals(files,agent?.ReviewFiles) && target is {IsVisible:true,IsEnabled:true,Focusable:true} && target.IsDescendantOf(this))target.Focus();
                else TxtAIInput.Focus();
            },DispatcherPriority.Loaded);
        }
        private void UpdateContextPanelLayout(bool animate = false)
        {
            if(ContextReviewHost is null)return;
            var opened=(DataContext as ConversationViewModel) is {IsChatModeActive:true,AgentWorkspace.IsOpen:true};
            var wide=AIWorkspaceMain.ActualWidth>=760;
            double targetWidth = _isReviewPanelExpandedWidth ? Math.Min(760, Math.Max(500, AIWorkspaceMain.ActualWidth * 0.56)) : 480;
            double finalWidth = (opened && wide) ? targetWidth : 0;
            double headerOffset = WorkspaceHeaderBar is { ActualHeight: > 0 } ? WorkspaceHeaderBar.ActualHeight + 18 : 54;

            Grid.SetColumn(ContextReviewHost,opened && !wide ? 0 : 2);
            Grid.SetColumnSpan(ContextReviewHost,opened && !wide ? 3 : 1);
            AIChatPanelBorder.SetCurrentValue(VisibilityProperty,opened && !wide ? Visibility.Collapsed : Visibility.Visible);
            AIComposerHost.SetCurrentValue(VisibilityProperty,opened && !wide ? Visibility.Collapsed : Visibility.Visible);
            AIWelcomeGlow.SetCurrentValue(VisibilityProperty,!opened && (DataContext as ConversationViewModel)?.IsInitialState==true ? Visibility.Visible : Visibility.Collapsed);

            if (animate && IsLoaded && wide && Application.Current?.Dispatcher is not null)
            {
                _rightPanelAnimTimer?.Stop();
                _rightPanelAnimFrom = ContextReviewColumn.Width.IsAbsolute ? ContextReviewColumn.Width.Value : 0;
                _rightPanelAnimTo = finalWidth;
                _rightPanelAnimStartUtc = DateTime.UtcNow;
                ContextReviewColumn.MinWidth = 0;
                ContextSplitterColumn.Width = new GridLength(opened ? 1 : 0);
                ContextReviewSplitter.Visibility = opened ? Visibility.Visible : Visibility.Collapsed;
                ContextReviewHost.Margin = new Thickness(0, -headerOffset, 0, 0);
                ContextReviewSplitter.Margin = new Thickness(0, -headerOffset, 0, 0);
                ContextReviewHost.SetCurrentValue(VisibilityProperty, Visibility.Visible);

                _rightPanelAnimTimer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(12)
                };
                _rightPanelAnimTimer.Tick += (_, _) =>
                {
                    double t = Math.Clamp((DateTime.UtcNow - _rightPanelAnimStartUtc).TotalMilliseconds / 150.0, 0.0, 1.0);
                    double eased = 1.0 - Math.Pow(1.0 - t, 3.0); // CubicEaseOut
                    double curW = _rightPanelAnimFrom + ((_rightPanelAnimTo - _rightPanelAnimFrom) * eased);
                    ContextReviewColumn.Width = new GridLength(Math.Max(0, curW));
                    if (WorkspaceHeaderBar is not null)
                    {
                        WorkspaceHeaderBar.Margin = curW > 1
                            ? new Thickness(20, 9, curW + 10, 9)
                            : new Thickness(20, 9, 10, 9);
                    }
                    if (t >= 1.0)
                    {
                        _rightPanelAnimTimer?.Stop();
                        ContextReviewColumn.MinWidth = opened && wide ? 300 : 0;
                        ContextReviewColumn.Width = new GridLength(finalWidth);
                        if (!opened)
                        {
                            ContextReviewHost.SetCurrentValue(VisibilityProperty, Visibility.Collapsed);
                            ContextReviewHost.Margin = new Thickness(0);
                            ContextReviewSplitter.Margin = new Thickness(0);
                        }
                    }
                };
                _rightPanelAnimTimer.Start();
                return;
            }

            _rightPanelAnimTimer?.Stop();
            ContextReviewColumn.MinWidth = opened && wide ? 300 : 0;
            ContextReviewColumn.Width = new GridLength(finalWidth);
            ContextSplitterColumn.Width = new GridLength(opened && wide ? 1 : 0);
            ContextReviewSplitter.Visibility=opened && wide ? Visibility.Visible : Visibility.Collapsed;
            if (WorkspaceHeaderBar is not null)
            {
                WorkspaceHeaderBar.Margin = opened && wide
                    ? new Thickness(20, 9, targetWidth + 10, 9)
                    : new Thickness(20, 9, 10, 9);
            }
            ContextReviewHost.Margin = opened && wide ? new Thickness(0, -headerOffset, 0, 0) : new Thickness(0);
            ContextReviewSplitter.Margin = opened && wide ? new Thickness(0, -headerOffset, 0, 0) : new Thickness(0);
            ContextReviewHost.SetCurrentValue(VisibilityProperty,opened ? Visibility.Visible : Visibility.Collapsed);
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if(e.PropertyName==nameof(ConversationViewModel.IsChatModeActive))UpdateContextPanelLayout();
            if (e.PropertyName == nameof(ConversationViewModel.IsConversationPanelVisible) &&
                sender is ConversationViewModel vm)
            {
                AnimateConversationPanel(vm.IsConversationPanelVisible);
            }
        }

        private void AnimateConversationPanel(bool expanded)
        {
            DshSidebar?.SetExpandedState(expanded, animate: true);
        }

        private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset ||
                e.NewItems?.OfType<ChatMessageItemViewModel>().Any(item => item.IsUser) == true)
                _followTail = true;
            if (e.NewItems != null)
            {
                foreach (ChatMessageItemViewModel item in e.NewItems)
                {
                    item.PropertyChanged -= Item_PropertyChanged;
                    item.PropertyChanged += Item_PropertyChanged;
                }
            }
            ScrollToBottom();
        }

        private void SubscribeToItemChanges(ConversationViewModel vm)
        {
            foreach (var item in vm.Messages)
            {
                item.PropertyChanged -= Item_PropertyChanged;
                item.PropertyChanged += Item_PropertyChanged;
            }
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatMessageItemViewModel.RenderedDocument))
            {
                ScrollToBottom();
            }
        }

        private void ScrollToBottom()
        {
            if (!_followTail || _scrollPending) return;
            _scrollPending = true;
            Dispatcher.InvokeAsync(() =>
            {
                _scrollPending = false;
                if (_followTail) AIChatScroll?.ScrollToBottom();
            }, DispatcherPriority.Loaded);
        }

        private void AIChatScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.OriginalSource != AIChatScroll) return;
            if (e.ViewportWidthChange != 0)
                UpdateResponsiveReadingGutter(AIWorkspaceMain.ActualWidth);
            if (e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0 && e.VerticalChange != 0)
                _followTail = AIChatScroll.ScrollableHeight - AIChatScroll.VerticalOffset <= 1;
            // Only auto-scroll on ExtentHeightChange while actively streaming (IsSending == true).
            // When IsSending == false (e.g. toggling Completed in Xs v/^), keep VerticalOffset anchored so upper bubbles never shift!
            bool isActivelySending = (DataContext as ConversationViewModel)?.IsSending == true;
            if (isActivelySending && (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0))
                ScrollToBottom();
        }

        private void AIInputBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.HeightChanged)
            {
                ScrollToBottom();
            }
            if (e.WidthChanged)
                UpdateComposerExpansion();
        }

        private void TxtAIInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateComposerExpansion();
        }

        private void UpdateComposerExpansion()
        {
            if (DataContext is not ConversationViewModel vm || TxtAIInput is null ||
                AIInputBorder is null || BtnAIAddAttachment is null ||
                ComposerTopBar is null || BtnAISendBorder is null)
                return;

            var value = TxtAIInput.Text ?? string.Empty;
            if (value.Contains('\n') || value.Contains('\r'))
            {
                vm.IsComposerExpanded = true;
                return;
            }

            var measured = new TextBlock
            {
                Text = string.IsNullOrEmpty(value) ? TxtAIInputPlaceholder.Text : value,
                FontFamily = TxtAIInput.FontFamily,
                FontStyle = TxtAIInput.FontStyle,
                FontWeight = TxtAIInput.FontWeight,
                FontStretch = TxtAIInput.FontStretch,
                FontSize = TxtAIInput.FontSize,
                FlowDirection = TxtAIInput.FlowDirection
            };
            measured.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            // In the permanent 2-tier layout, text area occupies the top row with full width.
            var compactWidth = (AIComposerLayout.ColumnDefinitions.Count > 1)
                ? AIComposerLayout.ColumnDefinitions[1].ActualWidth -
                    ((FrameworkElement)TxtAIInput.Parent).Margin.Left -
                    ((FrameworkElement)TxtAIInput.Parent).Margin.Right
                : (TxtAIInput.ActualWidth > 0 ? TxtAIInput.ActualWidth : 300);
            vm.IsComposerExpanded = compactWidth > 0 &&
                measured.DesiredSize.Width > compactWidth;
        }

        private void TxtAIInput_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key == Key.ImeProcessed)
            {
                return;
            }

            if (e.Key == Key.Escape &&
                DataContext is ConversationViewModel editViewModel &&
                editViewModel.CancelPendingEdit())
            {
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Enter ||
                (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                return;
            }

            e.Handled = true;
            if (DataContext is ConversationViewModel viewModel &&
                viewModel.SendWorkspaceCommand.CanExecute(null))
            {
                viewModel.SendWorkspaceCommand.Execute(null);
            }
        }

        private void ComposerFocusRequested(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                TxtAIInput.Focus();
                TxtAIInput.CaretIndex = TxtAIInput.Text?.Length ?? 0;
            });
        }
        private bool _projectPopupWasOpenOnMouseDown;

        private void BtnProjectSelector_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _projectPopupWasOpenOnMouseDown = ProjectMenuPopup is { IsOpen: true };
        }

        private void BtnProjectSelector_Click(object sender, RoutedEventArgs e)
        {
            if (ProjectMenuPopup is null) return;
            if (_projectPopupWasOpenOnMouseDown)
            {
                ProjectMenuPopup.IsOpen = false;
            }
            else
            {
                ProjectMenuPopup.PlacementTarget = BtnProjectSelector;
                ProjectMenuPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                ProjectMenuPopup.IsOpen = true;
            }
            _projectPopupWasOpenOnMouseDown = false;
        }

        private void BtnProjectMenuClose_Click(object sender, RoutedEventArgs e)
        {
            if (ProjectMenuPopup is not null) ProjectMenuPopup.IsOpen = false;
        }

        private void ProjectMenuPopup_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && ProjectMenuPopup is { IsOpen: true })
            {
                ProjectMenuPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private async void SelectProject_Click(object sender,RoutedEventArgs e)
        {
            if (ProjectMenuPopup is not null) ProjectMenuPopup.IsOpen = false;
            if(DataContext is not ConversationViewModel vm)return;
            var dialog=new Microsoft.Win32.OpenFolderDialog {Title="Pilih proyek"};
            if(dialog.ShowDialog(Window.GetWindow(this))==true)await vm.SelectProjectFromUiAsync(dialog.FolderName);
        }

        private bool _isReviewPanelExpandedWidth;
        private DispatcherTimer? _rightPanelAnimTimer;
        private double _rightPanelAnimFrom;
        private double _rightPanelAnimTo;
        private DateTime _rightPanelAnimStartUtc;

        public void ToggleReviewPanelWidth()
        {
            _isReviewPanelExpandedWidth = !_isReviewPanelExpandedWidth;
            UpdateContextPanelLayout(animate: true);
        }

        private void OpenDiffReviewPreview_Click(object sender, RoutedEventArgs e)
        {
            if (ProjectMenuPopup is not null) ProjectMenuPopup.IsOpen = false;
            if (DataContext is not ConversationViewModel vm || vm.AgentWorkspace is null) return;
            var agent = vm.AgentWorkspace;
            if (agent.IsOpen && ReferenceEquals(sender, BtnAIToggleDiffReview))
            {
                agent.IsOpen = false;
                return;
            }
            if (agent.ReviewFiles.Count > 0 && agent.OpenReviewFiles.Count == 0)
            {
                foreach (var tab in agent.ReviewFiles)
                {
                    agent.SelectReviewFileCommand.Execute(tab);
                }
            }
            agent.IsOpen = true;
        }
        private async void GrantProjectRead_Click(object sender,RoutedEventArgs e)
        {
            if(DataContext is not ConversationViewModel vm || !vm.CanGrantProjectRead)return;
            var permission=MessageBox.Show(Window.GetWindow(this),
                "Izinkan Nexus membaca source pada proyek ini untuk sesi percakapan sekarang?\n\n"+vm.SelectedProjectPath+
                "\n\nHanya .cs, .csproj, .sln, .slnx. Maksimum 10 menit; 256 source, 256 KiB/file, 8 MiB total baca. Context dibatasi 6 file / 24 KiB / 3072 token perkiraan. Depth 6, 128 direktori, 4096 entry.\n"+
                "Secrets, .env, keys, .git, bin/obj/.vs, generated files dan file di luar root tidak dimuat otomatis. Tidak ada edit, build, restore atau script. Izin dapat dicabut.",
                "Izin baca proyek",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No);
            if(permission==MessageBoxResult.Yes)await vm.GrantProjectReadAsync();
        }

        private void Composer_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Composer_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Composer_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0 && DataContext is ConversationViewModel vm)
                {
                    _ = vm.AddAttachmentsAsync(files);
                }
            }
            e.Handled = true;
        }

        private void BtnAIAddAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (BtnAIAddAttachment?.ContextMenu != null)
            {
                BtnAIAddAttachment.ContextMenu.PlacementTarget = BtnAIAddAttachment;
                BtnAIAddAttachment.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                BtnAIAddAttachment.ContextMenu.IsOpen = true;
            }
        }

        private void ConversationRename_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { CommandParameter: ConversationListItemViewModel item })
            {
                item.BeginRename();
            }
        }

        private async void ConversationDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { CommandParameter: ConversationListItemViewModel item } ||
                DataContext is not ConversationViewModel viewModel)
            {
                return;
            }

            var dialogService = (Application.Current as App)?.Services.GetService(typeof(IContentDialogService)) as IContentDialogService;
            if (dialogService != null)
            {
                var confirm = await dialogService.ShowDangerConfirmationAsync(
                    "Hapus Percakapan?",
                    $"Apakah Anda yakin ingin menghapus percakapan '{item.Title}'?\nFile riwayat akan dipindahkan ke Recycle Bin.",
                    "Hapus",
                    "Batal",
                    iconGlyph: "Delete");

                if (confirm != ContentDialogResult.Primary) return;
            }

            try
            {
                await viewModel.DeleteConversationAsync(item);
                var notifService = (Application.Current as App)?.Services.GetService(typeof(INotificationService)) as INotificationService;
                notifService?.ShowSuccess("Percakapan Dihapus", $"Percakapan '{item.Title}' berhasil dipindahkan ke Recycle Bin.");
            }
            catch (Exception ex)
            {
                var notifService = (Application.Current as App)?.Services.GetService(typeof(INotificationService)) as INotificationService;
                notifService?.ShowError("Gagal Menghapus", $"Percakapan tidak dapat dipindahkan ke Recycle Bin: {ex.Message}");
            }
        }

        private async void ConversationRenameTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox { DataContext: ConversationListItemViewModel item } ||
                DataContext is not ConversationViewModel viewModel)
            {
                return;
            }

            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                item.CancelRename();
                return;
            }

            if (e.Key != Key.Enter) return;
            e.Handled = true;
            try
            {
                await viewModel.CommitRenameAsync(item);
            }
            catch (Exception ex)
            {
                var notifService = (Application.Current as App)?.Services.GetService(typeof(INotificationService)) as INotificationService;
                notifService?.ShowError("Gagal Mengubah Nama", $"Nama percakapan tidak dapat disimpan: {ex.Message}");
            }
        }

        private void ConversationRenameTextBox_IsVisibleChanged(
            object sender,
            DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.IsVisible)
            {
                textBox.Dispatcher.InvokeAsync(() =>
                {
                    textBox.Focus();
                    textBox.SelectAll();
                });
            }
        }

        private void AttachmentPreviewBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ConversationViewModel viewModel)
            {
                viewModel.CloseAttachmentPreviewCommand.Execute(null);
            }
        }

        private void AccountSettingsBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ConversationViewModel viewModel)
            {
                viewModel.IsAccountSettingsModalOpen = false;
                e.Handled = true;
            }
        }

        private void ConversationView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (ProjectMenuPopup is { IsOpen: true })
                {
                    ProjectMenuPopup.IsOpen = false;
                    e.Handled = true;
                    return;
                }
            }
            if (e.Key == Key.Escape && DataContext is ConversationViewModel settingsVm && settingsVm.IsAccountSettingsModalOpen)
            {
                settingsVm.IsAccountSettingsModalOpen = false;
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && DataContext is ConversationViewModel promptViewModel && promptViewModel.IsSystemPromptEditorOpen)
            {
                promptViewModel.CancelSystemPromptEditCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape &&
                DataContext is ConversationViewModel viewModel &&
                viewModel.IsAttachmentPreviewOpen)
            {
                viewModel.CloseAttachmentPreviewCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void SystemPromptEditor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (SystemPromptEditorOverlay.IsVisible)
                Dispatcher.InvokeAsync(() => SystemPromptDraftEditor.Focus());
        }

        private void ListBoxAIConversations_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is DependencyObject dep)
            {
                var scrollViewer = FindVisualAncestor<ScrollViewer>(dep) ?? FindVisualChild<ScrollViewer>(dep);
                if (scrollViewer != null)
                {
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (e.Delta * 0.4));
                    e.Handled = true;
                }
            }
        }

        private void ConversationItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is TextBox)
            {
                return;
            }

            if (sender is ListBoxItem item)
            {
                item.IsSelected = true;
                item.Focus();
            }
        }

        private static T? FindVisualAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(current);
            while (parent != null)
            {
                if (parent is T typedParent)
                {
                    return typedParent;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }
                var nested = FindVisualChild<T>(child);
                if (nested != null)
                {
                    return nested;
                }
            }
            return null;
        }
    }
}
