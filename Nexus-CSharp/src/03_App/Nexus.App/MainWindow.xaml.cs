using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Nexus.App.Services;
using Nexus.App.ViewModels;

namespace Nexus.App
{
    public partial class MainWindow : Window
    {
        private bool _isSidebarCollapsed = false;
        private bool _isClosingConfirmed;
        private bool _isClosePromptPending;
        private IInputElement? _dialogFocusBeforeOpen;

        private const double SidebarExpandedWidth = 220;
        private const double SidebarCollapsedWidth = 48;

        private const double ThemeToggleLightMargin = 4;
        private const double ThemeToggleDarkMargin = 38;

        private const double WindowCornerRadius = 8;

        private Dictionary<Type, Border>? _navIndicatorMap;
        private Dictionary<Type, Button>? _navButtonMap;

        private readonly INotificationService? _notificationService;
        private System.Windows.Threading.DispatcherTimer? _snackbarTimer;

        public MainWindow(MainViewModel viewModel)
            : this(viewModel, null)
        {
        }

        public MainWindow(MainViewModel viewModel, INotificationService? notificationService)
        {
            InitializeComponent();
            DataContext = viewModel;
            _notificationService = notificationService;
            if (_notificationService != null)
            {
                _notificationService.NotificationRequested += OnNotificationRequested;
            }
            AttachNavigationIndicatorSync(viewModel);
            StateChanged += MainWindow_StateChanged;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosingConfirmed) return;
            if (_isClosePromptPending) { e.Cancel = true; return; }

            if (DataContext is MainViewModel vm)
            {
                if (vm.SettingsVM.IsDirty)
                {
                    e.Cancel = true;
                    _isClosePromptPending = true;
                    Dispatcher.BeginInvoke(() =>
                    {
                        _isClosePromptPending = false;
                        vm.PromptSaveOrProceed(() => { _isClosingConfirmed = true; Close(); });
                    });
                }
                else if (vm.SettingsVM.ConfirmExit)
                {
                    e.Cancel = true;
                    _isClosePromptPending = true;
                    Dispatcher.BeginInvoke(() =>
                    {
                        _isClosePromptPending = false;
                        vm.ShowExitDialog(() => { _isClosingConfirmed = true; Close(); }, () => { });
                    });
                }
                else
                {
                    _isClosingConfirmed = true;
                }
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.IsDarkTheme)
            {
                SetToggleToDark(animate: false);
            }
            else
            {
                SetToggleToLight(animate: false);
            }
        }

        private void SetToggleToLight(bool animate)
        {
            var targetMargin = new Thickness(ThemeToggleLightMargin, 0, 0, 0);
            if (animate)
            {
                var anim = new ThicknessAnimation(ThemeToggleThumb.Margin, targetMargin, TimeSpan.FromMilliseconds(250));
                anim.EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut };
                ThemeToggleThumb.BeginAnimation(FrameworkElement.MarginProperty, anim);
            }
            else
            {
                ThemeToggleThumb.Margin = targetMargin;
            }
            ThemeToggleLightIcon.Opacity = 1.0;
            ThemeToggleDarkIcon.Opacity = 0.5;
        }

        private void SetToggleToDark(bool animate)
        {
            var targetMargin = new Thickness(ThemeToggleDarkMargin, 0, 0, 0);
            if (animate)
            {
                var anim = new ThicknessAnimation(ThemeToggleThumb.Margin, targetMargin, TimeSpan.FromMilliseconds(250));
                anim.EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut };
                ThemeToggleThumb.BeginAnimation(FrameworkElement.MarginProperty, anim);
            }
            else
            {
                ThemeToggleThumb.Margin = targetMargin;
            }
            ThemeToggleLightIcon.Opacity = 0.5;
            ThemeToggleDarkIcon.Opacity = 1.0;
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowRoot.Padding = new Thickness(
                    SystemParameters.WindowResizeBorderThickness.Left + 2,
                    SystemParameters.WindowResizeBorderThickness.Top + 2,
                    SystemParameters.WindowResizeBorderThickness.Right + 2,
                    SystemParameters.WindowResizeBorderThickness.Bottom + 2);
                MainBorder.CornerRadius = new CornerRadius(0);
                MainBorder.BorderThickness = new Thickness(0);
                NexusIcon.SetGlyph(IconMaximizeRestore, "Restore");
                BtnMaximize.ToolTip = "Kembalikan";
            }
            else
            {
                WindowRoot.Padding = new Thickness(0);
                MainBorder.CornerRadius = new CornerRadius(WindowCornerRadius);
                MainBorder.BorderThickness = new Thickness(1);
                NexusIcon.SetGlyph(IconMaximizeRestore, "Maximize");
                BtnMaximize.ToolTip = "Maksimalkan";
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _isClosingConfirmed = false;
            Close();
        }


        private void BtnSidebarToggle_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.IsGlobalNavigationExpanded = !vm.IsGlobalNavigationExpanded;
            }
            else
            {
                _isSidebarCollapsed = !_isSidebarCollapsed;
                AnimateGlobalNavigation(!_isSidebarCollapsed);
            }
        }

        private void AnimateGlobalNavigation(bool expanded)
        {
            _isSidebarCollapsed = !expanded;
            var targetWidth = expanded ? SidebarExpandedWidth : SidebarCollapsedWidth;
            var currentWidth = double.IsNaN(SidebarBorder.Width) ? SidebarBorder.ActualWidth : SidebarBorder.Width;
            if (currentWidth <= 0) currentWidth = expanded ? SidebarExpandedWidth : SidebarCollapsedWidth;
            if (Math.Abs(currentWidth - targetWidth) < 0.5) return;
            var animation = new DoubleAnimation(currentWidth, targetWidth, TimeSpan.FromMilliseconds(160))
            {
                AccelerationRatio = 0.4
            };
            SidebarBorder.BeginAnimation(FrameworkElement.WidthProperty, animation);
        }

        // ============================================================
        // Sidebar Active Navigation Indicator
        // ============================================================

        private void AttachNavigationIndicatorSync(MainViewModel viewModel)
        {
            _navIndicatorMap = new Dictionary<Type, Border>
            {
                [typeof(HomeViewModel)] = HomeNavIndicator,
                [typeof(ConversationViewModel)] = AiWorkspaceNavIndicator,
                [typeof(ImageSubsystemViewModel)] = ImageToolsNavIndicator,
                [typeof(PdfUtilitiesViewModel)] = PdfToolsNavIndicator,
                [typeof(FileManagementViewModel)] = FileManagementNavIndicator,
                [typeof(SettingsViewModel)] = SettingsNavIndicator
            };

            _navButtonMap = new Dictionary<Type, Button>
            {
                [typeof(HomeViewModel)] = HomeNavButton,
                [typeof(ConversationViewModel)] = AiWorkspaceNavButton,
                [typeof(ImageSubsystemViewModel)] = ImageToolsNavButton,
                [typeof(PdfUtilitiesViewModel)] = PdfToolsNavButton,
                [typeof(FileManagementViewModel)] = FileManagementNavButton,
                [typeof(SettingsViewModel)] = SettingsNavButton
            };

            viewModel.PropertyChanged += MainViewModel_PropertyChanged;
            UpdateActiveNavigationIndicator(viewModel.CurrentViewModel);
        }

        private void MainViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentViewModel) &&
                DataContext is MainViewModel viewModel)
            {
                UpdateActiveNavigationIndicator(viewModel.CurrentViewModel);
            }
            else if (e.PropertyName == nameof(MainViewModel.IsGlobalNavigationExpanded) &&
                sender is MainViewModel vm)
            {
                AnimateGlobalNavigation(vm.IsGlobalNavigationExpanded);
            }
        }

        private void UpdateActiveNavigationIndicator(ViewModelBase? currentViewModel)
        {
            if (_navIndicatorMap is null || _navButtonMap is null || currentViewModel is null)
            {
                return;
            }

            var activeType = currentViewModel.GetType();
            foreach (var entry in _navIndicatorMap)
            {
                if (entry.Key == activeType)
                {
                    entry.Value.SetResourceReference(Border.BackgroundProperty, "NexusAccentSelected");
                }
                else
                {
                    entry.Value.Background = Brushes.Transparent;
                }
            }

            foreach (var entry in _navButtonMap)
            {
                if (entry.Key == activeType)
                {
                    entry.Value.SetResourceReference(Button.BackgroundProperty, "NexusSurfaceSunken");
                }
                else
                {
                    entry.Value.Background = Brushes.Transparent;
                }
            }
        }

        // ============================================================
        // Day & Night Theme Toggle
        // ============================================================

        private void ThemeToggle_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                var isCurrentlyDark = vm.IsDarkTheme;
                vm.ToggleThemeCommand.Execute(null);
                if (isCurrentlyDark)
                    SetToggleToLight(animate: true);
                else
                    SetToggleToDark(animate: true);
            }
        }

        // ============================================================
        // Dialog Overlay
        // ============================================================

        private void DialogOverlay_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!DialogCard.IsMouseOver)
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.HideDialog();
                }
                e.Handled = true;
            }
        }

        private void DialogOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
            {
                _dialogFocusBeforeOpen ??= Keyboard.FocusedElement;

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                DialogOverlay.BeginAnimation(OpacityProperty, fadeIn);

                var scaleIn = new DoubleAnimation(0.95, 1.0, TimeSpan.FromMilliseconds(200));
                scaleIn.EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut };
                DialogCard.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
                DialogCard.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DialogOverlay.Focus();
                    DialogPrimaryButton.Focus();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
            else
            {
                var focusBeforeDialog = _dialogFocusBeforeOpen;
                _dialogFocusBeforeOpen = null;
                if (focusBeforeDialog is UIElement { IsVisible: true, IsEnabled: true })
                {
                    Keyboard.Focus(focusBeforeDialog);
                }

                DialogOverlay.Opacity = 0;
                if (DialogCard.RenderTransform is ScaleTransform st)
                {
                    st.ScaleX = 0.95;
                    st.ScaleY = 0.95;
                }
            }
        }

        private void DialogOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (DataContext is MainViewModel vm)
                    vm.HideDialog();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (DataContext is MainViewModel vm && vm.DialogPrimaryCommand.CanExecute(null))
                    vm.DialogPrimaryCommand.Execute(null);
                e.Handled = true;
            }
        }

        // ============================================================
        // Snackbar Notification Presenter (Modern Windows 11 Toast)
        // ============================================================

        private void OnNotificationRequested(object? sender, NotificationEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _snackbarTimer?.Stop();

                SnackbarTitle.Text = e.Title;
                SnackbarMessage.Text = e.Message;

                switch (e.Severity)
                {
                    case NotificationSeverity.Success:
                        NexusIcon.SetGlyph(SnackbarIcon, "Check");
                        SnackbarIcon.SetResourceReference(TextBlock.ForegroundProperty, "NexusSuccessStroke");
                        break;
                    case NotificationSeverity.Warning:
                        NexusIcon.SetGlyph(SnackbarIcon, "Warning");
                        SnackbarIcon.SetResourceReference(TextBlock.ForegroundProperty, "NexusWarningStroke");
                        break;
                    case NotificationSeverity.Error:
                        NexusIcon.SetGlyph(SnackbarIcon, "Error");
                        SnackbarIcon.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
                        break;
                    case NotificationSeverity.Info:
                    default:
                        NexusIcon.SetGlyph(SnackbarIcon, "Info");
                        SnackbarIcon.SetResourceReference(TextBlock.ForegroundProperty, "NexusAccentPrimary");
                        break;
                }

                SnackbarHost.Visibility = Visibility.Visible;

                var slideIn = new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
                };
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));

                SnackbarTranslate.BeginAnimation(TranslateTransform.YProperty, slideIn);
                SnackbarHost.BeginAnimation(OpacityProperty, fadeIn);

                _snackbarTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = e.Duration
                };
                _snackbarTimer.Tick += (s, args) =>
                {
                    _snackbarTimer?.Stop();
                    HideSnackbar();
                };
                _snackbarTimer.Start();
            }));
        }

        private void HideSnackbar()
        {
            _snackbarTimer?.Stop();

            var slideOut = new DoubleAnimation(0, 30, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn }
            };
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180));
            fadeOut.Completed += (s, e) =>
            {
                SnackbarHost.Visibility = Visibility.Collapsed;
            };

            SnackbarTranslate.BeginAnimation(TranslateTransform.YProperty, slideOut);
            SnackbarHost.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void SnackbarCloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideSnackbar();
        }
    }
}
