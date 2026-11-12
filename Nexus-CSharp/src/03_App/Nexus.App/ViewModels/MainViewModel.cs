using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Nexus.App.Controls.ContentDialog;
using Nexus.App.Services;
using Nexus.Contracts;
using Nexus.Core.HomeSettings.Services;

namespace Nexus.App.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private ViewModelBase? _currentViewModel;
        private readonly Lazy<ImageSubsystemViewModel> _lazyImageSubsystem;
        private readonly ISettingsManagerService _settingsManagerService;
        private readonly SemaphoreSlim _navigationLock = new(1, 1);

        // Dialog state
        private bool _isDialogVisible;
        private string _dialogTitle = string.Empty;
        private string _dialogDescription = string.Empty;
        private string _dialogIconGlyph = "Info";
        private string _dialogPrimaryText = "OK";
        private string _dialogSecondaryText = string.Empty;
        private string _dialogCancelText = "Cancel";
        private bool _isDialogSecondaryVisible;
        private Action? _dialogPrimaryAction;
        private Action? _dialogSecondaryAction;
        private Action? _dialogCancelAction;

        public ViewModelBase? CurrentViewModel
        {
            get => _currentViewModel;
            set => SetProperty(ref _currentViewModel, value);
        }

        public bool IsDialogVisible
        {
            get => _isDialogVisible;
            set => SetProperty(ref _isDialogVisible, value);
        }

        public string DialogTitle
        {
            get => _dialogTitle;
            set => SetProperty(ref _dialogTitle, value);
        }

        public string DialogDescription
        {
            get => _dialogDescription;
            set => SetProperty(ref _dialogDescription, value);
        }

        public string DialogIconGlyph
        {
            get => _dialogIconGlyph;
            set => SetProperty(ref _dialogIconGlyph, value);
        }

        public string DialogPrimaryText
        {
            get => _dialogPrimaryText;
            set => SetProperty(ref _dialogPrimaryText, value);
        }

        public string DialogSecondaryText
        {
            get => _dialogSecondaryText;
            set => SetProperty(ref _dialogSecondaryText, value);
        }

        public string DialogCancelText
        {
            get => _dialogCancelText;
            set => SetProperty(ref _dialogCancelText, value);
        }

        public bool IsDialogSecondaryVisible
        {
            get => _isDialogSecondaryVisible;
            set => SetProperty(ref _isDialogSecondaryVisible, value);
        }
        private bool _isGlobalNavigationExpanded = true;

        public bool IsGlobalNavigationExpanded
        {
            get => _isGlobalNavigationExpanded;
            set
            {
                if (SetProperty(ref _isGlobalNavigationExpanded, value))
                {
                    if (value && ConversationVM is { IsConversationPanelVisible: true })
                    {
                        ConversationVM.IsConversationPanelVisible = false;
                    }
                }
            }
        }

        public HomeViewModel HomeVM { get; }
        public ConversationViewModel ConversationVM { get; }
        public FileManagementViewModel FileManagementVM { get; }
        public PdfWorkspaceViewModel PdfUtilitiesVM { get; }
        public ImageSubsystemViewModel ImageSubsystemVM => _lazyImageSubsystem.Value;
        public SettingsViewModel SettingsVM { get; }

        public ICommand NavigateCommand { get; }
        public ICommand DialogPrimaryCommand { get; }
        public ICommand DialogSecondaryCommand { get; }
        public ICommand DialogCancelCommand { get; }
        public ICommand ToggleThemeCommand { get; }

        public MainViewModel(
            HomeViewModel home,
            ConversationViewModel conversation,
            FileManagementViewModel fileManagement,
            PdfWorkspaceViewModel pdfUtilities,
            Lazy<ImageSubsystemViewModel> lazyImageSubsystem,
            SettingsViewModel settings,
            ISettingsManagerService settingsManagerService,
            IContentDialogService? contentDialogService = null)
        {
            HomeVM = home;
            ConversationVM = conversation;
            FileManagementVM = fileManagement;
            PdfUtilitiesVM = pdfUtilities;
            _lazyImageSubsystem = lazyImageSubsystem;
            SettingsVM = settings;
            _settingsManagerService = settingsManagerService ?? throw new ArgumentNullException(nameof(settingsManagerService));

            CurrentViewModel = HomeVM;

            NavigateCommand = new AsyncRelayCommand<string>(NavigateAsync);
            DialogPrimaryCommand = new RelayCommand(ExecuteDialogPrimary);
            DialogSecondaryCommand = new RelayCommand(ExecuteDialogSecondary);
            DialogCancelCommand = new RelayCommand(ExecuteDialogCancel);
            ToggleThemeCommand = new AsyncRelayCommand(ToggleThemeAsync);

            ConversationVM.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(ConversationViewModel.IsConversationPanelVisible))
                {
                    if (ConversationVM.IsConversationPanelVisible && IsGlobalNavigationExpanded)
                    {
                        IsGlobalNavigationExpanded = false;
                    }
                }
            };

            if (contentDialogService is ContentDialogService cds)
            {
                cds.RegisterHandler(request =>
                {
                    var tcs = new TaskCompletionSource<ContentDialogResult>();

                    DialogTitle = request.Title;
                    DialogDescription = request.Message;
                    DialogIconGlyph = request.IconGlyph;
                    DialogPrimaryText = request.PrimaryText;
                    DialogCancelText = string.IsNullOrEmpty(request.CancelText) ? "Batal" : request.CancelText;
                    DialogSecondaryText = request.SecondaryText ?? string.Empty;
                    IsDialogSecondaryVisible = !string.IsNullOrEmpty(request.SecondaryText);

                    _dialogPrimaryAction = () =>
                    {
                        tcs.TrySetResult(ContentDialogResult.Primary);
                    };
                    _dialogSecondaryAction = () =>
                    {
                        tcs.TrySetResult(ContentDialogResult.Secondary);
                    };
                    _dialogCancelAction = () =>
                    {
                        tcs.TrySetResult(ContentDialogResult.None);
                    };

                    IsDialogVisible = true;
                    return tcs.Task;
                });
            }
        }

        public bool IsDarkTheme
        {
            get
            {
                var theme = _settingsManagerService.GetTheme();
                return !string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
            }
        }

        public async Task ToggleThemeAsync()
        {
            var currentTheme = _settingsManagerService.GetTheme();
            var isLight = string.Equals(currentTheme, "Light", StringComparison.OrdinalIgnoreCase);
            var newTheme = isLight ? "Dark" : "Light";

            try
            {
                await _settingsManagerService.SaveSettingsAsync(
                    newTheme,
                    _settingsManagerService.GetDownloadsPath(),
                    _settingsManagerService.GetConfirmExit(),
                    _settingsManagerService.GetConfirmModule());

                ApplyThemeImmediate(newTheme);
                SettingsVM.SyncPersistedTheme();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[MainViewModel] ToggleTheme failed: {ex.Message}");
            }
        }

        private void ToggleTheme()
        {
            ToggleThemeAsync().GetAwaiter().GetResult();
        }

        public static void ApplyThemeImmediate(string theme)
        {
            try
            {
                ThemeManager.ApplyTheme(theme.Equals("Dark", StringComparison.OrdinalIgnoreCase)
                    ? ThemeMode.Dark
                    : ThemeMode.Light);
            }
            catch
            {
            }
        }

        private string? _pendingViewName;

        private async Task NavigateAsync(string? viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName))
            {
                return;
            }

            if (CurrentViewModel == SettingsVM && SettingsVM.IsDirty)
            {
                _pendingViewName = viewName;
                PromptSaveOrProceed(
                    () => ContinuePendingNavigation(_pendingViewName!));
            }
            else
            {
                await ExecuteNavigationAsync(viewName);
            }
        }

        private async void ContinuePendingNavigation(string viewName)
        {
            await ExecuteNavigationAsync(viewName);
        }

        public void PromptSaveOrProceed(Action onProceed)
        {
            ShowUnsavedChangesDialog(
                onSave: TrySaveOrRetry,
                onDiscard: () => { SettingsVM.DiscardChanges(); HideDialog(); onProceed(); },
                onCancel: () => HideDialog());

            void TrySaveOrRetry()
            {
                if (SettingsVM.SaveChangesSync())
                {
                    HideDialog();
                    onProceed();
                }
                else
                {
                    DialogDescription = "Save failed. Check your settings and try again, or discard changes.";
                    OnPropertyChanged(nameof(DialogDescription));
                    ShowUnsavedChangesDialog(TrySaveOrRetry,
                        onDiscard: () => { SettingsVM.DiscardChanges(); HideDialog(); onProceed(); },
                        onCancel: () => HideDialog());
                }
            }
        }

        private async Task ExecuteNavigationAsync(string viewName)
        {
            await _navigationLock.WaitAsync();
            try
            {
                var nextViewModel = viewName switch
                {
                    "Home" => HomeVM,
                    "AIWorkspace" => ConversationVM,
                    "ImageTools" => ImageSubsystemVM,
                    "PdfTools" => PdfUtilitiesVM,
                    "FileManagement" => FileManagementVM,
                    "Pengaturan" => SettingsVM,
                    _ => CurrentViewModel
                };

                if (nextViewModel is null ||
                    ReferenceEquals(nextViewModel, CurrentViewModel))
                {
                    return;
                }

                await TransitionLifecycleAsync(
                    CurrentViewModel,
                    nextViewModel,
                    () => CurrentViewModel = nextViewModel);
            }
            finally
            {
                _navigationLock.Release();
            }
        }

        internal static async Task TransitionLifecycleAsync(
            ViewModelBase? previousViewModel,
            ViewModelBase nextViewModel,
            Action activateNext)
        {
            ArgumentNullException.ThrowIfNull(nextViewModel);
            ArgumentNullException.ThrowIfNull(activateNext);

            if (previousViewModel is IModuleLifecycle previousLifecycle)
            {
                await previousLifecycle.OnNavigatedFromAsync();
            }

            activateNext();

            if (nextViewModel is IModuleLifecycle nextLifecycle)
            {
                await nextLifecycle.OnNavigatedToAsync();
            }
        }

        // ============================================================
        // Dialog System
        // ============================================================

        public void ShowExitDialog(Action onExit, Action onCancel)
        {
            DialogIconGlyph = "Close";
            DialogTitle = "Keluar dari Nexus?";
            DialogDescription = "Apakah Anda yakin ingin menutup aplikasi Nexus?";
            DialogPrimaryText = "Keluar";
            IsDialogSecondaryVisible = false;
            DialogCancelText = "Batal";
            _dialogPrimaryAction = onExit;
            _dialogSecondaryAction = null;
            _dialogCancelAction = onCancel;
            IsDialogVisible = true;
        }

        public void ShowModuleSwitchDialog(Action onContinue, Action onCancel)
        {
            DialogIconGlyph = "Engine";
            DialogTitle = "Proses Aktif Terdeteksi";
            DialogDescription = "Anda memiliki proses aktif. Berpindah modul akan membatalkannya. Lanjutkan?";
            DialogPrimaryText = "Lanjutkan";
            IsDialogSecondaryVisible = false;
            DialogCancelText = "Batal";
            _dialogPrimaryAction = onContinue;
            _dialogSecondaryAction = null;
            _dialogCancelAction = onCancel;
            IsDialogVisible = true;
        }

        public void ShowUnsavedChangesDialog(Action onSave, Action onDiscard, Action onCancel)
        {
            DialogIconGlyph = "Save";
            DialogTitle = "Perubahan Belum Disimpan";
            DialogDescription = "Ada perubahan pengaturan yang belum disimpan.";
            DialogPrimaryText = "Simpan & Keluar";
            DialogSecondaryText = "Abaikan";
            IsDialogSecondaryVisible = true;
            DialogCancelText = "Batal";
            _dialogPrimaryAction = onSave;
            _dialogSecondaryAction = onDiscard;
            _dialogCancelAction = onCancel;
            IsDialogVisible = true;
        }

        public void HideDialog()
        {
            var cancel = _dialogCancelAction;
            IsDialogVisible = false;
            _dialogPrimaryAction = null;
            _dialogSecondaryAction = null;
            _dialogCancelAction = null;
            cancel?.Invoke();
        }

        private void ExecuteDialogPrimary()
        {
            var action = _dialogPrimaryAction;
            _dialogCancelAction = null;
            HideDialog();
            action?.Invoke();
        }

        private void ExecuteDialogSecondary()
        {
            var action = _dialogSecondaryAction;
            _dialogCancelAction = null;
            HideDialog();
            action?.Invoke();
        }

        private void ExecuteDialogCancel()
        {
            HideDialog();
        }
    }

    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _executeAsync;
        private readonly Func<bool>? _canExecute;

        public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
        {
            _executeAsync = executeAsync;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();
        public async void Execute(object? parameter) => await _executeAsync();
    }
}
