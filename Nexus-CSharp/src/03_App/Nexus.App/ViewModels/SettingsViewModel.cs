using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Services;
using Nexus.App.Controls.ContentDialog;
using Nexus.App.Controls.InfoBar;
using Nexus.App.Controls.Snackbar;
using Nexus.App.Services;
using Nexus.Core.HomeSettings.Services;
using Nexus.Engine.ColorManagement;

namespace Nexus.App.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ISettingsManagerService _settingsManagerService;
        private readonly ICmykProfileManager _cmykProfileManager;
        private readonly IModelCatalogService? _modelCatalogService;
        private readonly INotificationService? _notificationService;
        private readonly IContentDialogService? _contentDialogService;

        private bool _isInfoBarOpen;
        private string _infoBarTitle = string.Empty;
        private string _infoBarMessage = string.Empty;
        private InfoBarSeverity _infoBarSeverity = InfoBarSeverity.Informational;

        private bool _isLoading;
        private bool _isDirty;
        private bool _isLightTheme;
        private bool _isDarkTheme;
        private string _downloadsFolder = string.Empty;
        private string _modelRootFolder = string.Empty;
        private string _engineRootFolder = string.Empty;
        private bool _confirmExit = true;
        private bool _confirmModule = true;

        private string _initialTheme = "Dark";
        private string _initialDownloadsFolder = string.Empty;
        private string _initialModelRootFolder = string.Empty;
        private string _initialEngineRootFolder = string.Empty;
        private bool _initialConfirmExit = true;
        private bool _initialConfirmModule = true;

        private bool _isScanningModels;
        private bool _isCopyingModel;
        private double _copyProgressPercentage;
        private string _modelOperationStatusMessage = string.Empty;

        public bool IsInfoBarOpen
        {
            get => _isInfoBarOpen;
            set => SetProperty(ref _isInfoBarOpen, value);
        }

        public string InfoBarTitle
        {
            get => _infoBarTitle;
            set => SetProperty(ref _infoBarTitle, value);
        }

        public string InfoBarMessage
        {
            get => _infoBarMessage;
            set => SetProperty(ref _infoBarMessage, value);
        }

        public InfoBarSeverity InfoBarSeverity
        {
            get => _infoBarSeverity;
            set => SetProperty(ref _infoBarSeverity, value);
        }

        public void ShowInfoBar(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Warning)
        {
            InfoBarTitle = title;
            InfoBarMessage = message;
            InfoBarSeverity = severity;
            IsInfoBarOpen = true;
        }

        public void HideInfoBar()
        {
            IsInfoBarOpen = false;
        }

        public bool IsDirty
        {
            get => _isDirty;
            set => SetProperty(ref _isDirty, value);
        }

        public bool IsLightTheme
        {
            get => _isLightTheme;
            set
            {
                if (SetProperty(ref _isLightTheme, value))
                {
                    if (value)
                    {
                        _isDarkTheme = false;
                        OnPropertyChanged(nameof(IsDarkTheme));
                        ApplyThemeImmediate("Light");
                    }
                    if (!_isLoading) IsDirty = true;
                }
            }
        }

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (SetProperty(ref _isDarkTheme, value))
                {
                    if (value)
                    {
                        _isLightTheme = false;
                        OnPropertyChanged(nameof(IsLightTheme));
                        ApplyThemeImmediate("Dark");
                    }
                    if (!_isLoading) IsDirty = true;
                }
            }
        }

        public string DownloadsFolder
        {
            get => _downloadsFolder;
            set
            {
                if (SetProperty(ref _downloadsFolder, value))
                    if (!_isLoading) IsDirty = true;
            }
        }

        public string ModelRootFolder
        {
            get => _modelRootFolder;
            set
            {
                if (SetProperty(ref _modelRootFolder, value))
                    if (!_isLoading) IsDirty = true;
            }
        }

        public string EngineRootFolder
        {
            get => _engineRootFolder;
            set
            {
                if (SetProperty(ref _engineRootFolder, value))
                    if (!_isLoading) IsDirty = true;
            }
        }

        public bool ConfirmExit
        {
            get => _confirmExit;
            set
            {
                if (SetProperty(ref _confirmExit, value))
                    if (!_isLoading) IsDirty = true;
            }
        }

        public bool ConfirmModule
        {
            get => _confirmModule;
            set
            {
                if (SetProperty(ref _confirmModule, value))
                    if (!_isLoading) IsDirty = true;
            }
        }

        private List<CmykProfileInfo> _cmykProfiles = new();
        public List<CmykProfileInfo> CmykProfiles
        {
            get => _cmykProfiles;
            set => SetProperty(ref _cmykProfiles, value);
        }

        private CmykProfileInfo? _selectedCmykProfile;
        public CmykProfileInfo? SelectedCmykProfile
        {
            get => _selectedCmykProfile;
            set
            {
                if (SetProperty(ref _selectedCmykProfile, value) && value != null)
                {
                    _cmykProfileManager.SetDefaultProfile(value.Id);
                    OnPropertyChanged(nameof(IsCmykUnavailable));
                }
            }
        }

        public bool IsCmykUnavailable => !_cmykProfileManager.IsCmykAvailable && _cmykProfiles.Count == 0;

        public bool IsScanningModels
        {
            get => _isScanningModels;
            set => SetProperty(ref _isScanningModels, value);
        }

        public bool IsCopyingModel
        {
            get => _isCopyingModel;
            set => SetProperty(ref _isCopyingModel, value);
        }

        public double CopyProgressPercentage
        {
            get => _copyProgressPercentage;
            set => SetProperty(ref _copyProgressPercentage, value);
        }

        public string ModelOperationStatusMessage
        {
            get => _modelOperationStatusMessage;
            set => SetProperty(ref _modelOperationStatusMessage, value);
        }

        public ObservableCollection<AiModelSettingItemViewModel> InstalledAiModels { get; } = new();

        public Func<string?>? FilePicker { get; set; }

        private bool _isAiModelsExpanded = true;
        public bool IsAiModelsExpanded
        {
            get => _isAiModelsExpanded;
            set => SetProperty(ref _isAiModelsExpanded, value);
        }

        public ICommand OpenProfilesFolderCommand { get; }
        public ICommand BrowseDownloadsCommand { get; }
        public ICommand BrowseModelFolderCommand { get; }
        public ICommand BrowseEngineFolderCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand ResetSettingsCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand DiscardCommand { get; }

        public ICommand ScanAiModelsCommand { get; }
        public ICommand AddAiModelCommand { get; }
        public ICommand DeleteAiModelCommand { get; }
        public ICommand OpenAiModelsFolderCommand { get; }
        public ICommand MoveModelToTopCommand { get; }
        public ICommand MoveModelUpCommand { get; }
        public ICommand MoveModelDownCommand { get; }

        public SettingsViewModel(
            ISettingsManagerService settingsManagerService,
            ICmykProfileManager cmykProfileManager)
            : this(settingsManagerService, cmykProfileManager, null, null, null)
        {
        }

        public SettingsViewModel(
            ISettingsManagerService settingsManagerService,
            ICmykProfileManager cmykProfileManager,
            IModelCatalogService? modelCatalogService)
            : this(settingsManagerService, cmykProfileManager, modelCatalogService, null, null)
        {
        }

        public SettingsViewModel(
            ISettingsManagerService settingsManagerService,
            ICmykProfileManager cmykProfileManager,
            IModelCatalogService? modelCatalogService,
            INotificationService? notificationService)
            : this(settingsManagerService, cmykProfileManager, modelCatalogService, notificationService, null)
        {
        }

        public SettingsViewModel(
            ISettingsManagerService settingsManagerService,
            ICmykProfileManager cmykProfileManager,
            IModelCatalogService? modelCatalogService,
            INotificationService? notificationService,
            IContentDialogService? contentDialogService)
        {
            _settingsManagerService = settingsManagerService ?? throw new ArgumentNullException(nameof(settingsManagerService));
            _cmykProfileManager = cmykProfileManager ?? throw new ArgumentNullException(nameof(cmykProfileManager));
            _modelCatalogService = modelCatalogService!;
            _notificationService = notificationService;
            _contentDialogService = contentDialogService;

            BrowseDownloadsCommand = new RelayCommand(BrowseDownloads);
            BrowseModelFolderCommand = new RelayCommand(BrowseModelFolder);
            BrowseEngineFolderCommand = new RelayCommand(BrowseEngineFolder);
            SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
            ResetSettingsCommand = new AsyncRelayCommand(ResetSettingsAsync);
            CancelCommand = new RelayCommand(CancelChanges);
            DiscardCommand = new RelayCommand(DiscardChanges);
            OpenProfilesFolderCommand = new RelayCommand(OpenProfilesFolder);

            ScanAiModelsCommand = new AsyncRelayCommand(ScanAiModelsAsync);
            AddAiModelCommand = new AsyncRelayCommand(AddAiModelAsync);
            DeleteAiModelCommand = new AsyncRelayCommand<object>(DeleteAiModelAsync);
            OpenAiModelsFolderCommand = new RelayCommand(OpenAiModelsFolder);
            MoveModelToTopCommand = new AsyncRelayCommand<object>(p => MoveModelAsync(p, ModelOrderDirection.ToTop));
            MoveModelUpCommand = new AsyncRelayCommand<object>(p => MoveModelAsync(p, ModelOrderDirection.Up));
            MoveModelDownCommand = new AsyncRelayCommand<object>(p => MoveModelAsync(p, ModelOrderDirection.Down));

            _isLoading = true;
            LoadSettings();
            LoadCmykProfiles();
            if (_modelCatalogService != null)
            {
                LoadAiModels();
            }
            _isLoading = false;
            IsDirty = false;
        }

        public void LoadSettings()
        {
            var theme = _settingsManagerService.GetTheme();
            if (string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase))
            {
                _isLightTheme = true;
                _isDarkTheme = false;
            }
            else
            {
                _isLightTheme = false;
                _isDarkTheme = true;
            }

            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(IsDarkTheme));

            var folder = _settingsManagerService.GetDownloadsPath();
            if (string.IsNullOrWhiteSpace(folder))
            {
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
            _downloadsFolder = folder;
            OnPropertyChanged(nameof(DownloadsFolder));

            _modelRootFolder = _settingsManagerService.ModelRootFolder;
            OnPropertyChanged(nameof(ModelRootFolder));

            _engineRootFolder = _settingsManagerService.EngineRootFolder;
            OnPropertyChanged(nameof(EngineRootFolder));

            _confirmExit = _settingsManagerService.GetConfirmExit();
            _confirmModule = _settingsManagerService.GetConfirmModule();
            OnPropertyChanged(nameof(ConfirmExit));
            OnPropertyChanged(nameof(ConfirmModule));

            string savedTheme = _isLightTheme ? "Light" : "Dark";
            CaptureInitialState(savedTheme);
            ApplyThemeImmediate(savedTheme);

            IsDirty = false;
        }

        public void SyncPersistedTheme()
        {
            var theme = _settingsManagerService.GetTheme();
            var isLight = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);

            _isLoading = true;
            try
            {
                _isLightTheme = isLight;
                _isDarkTheme = !isLight;
                OnPropertyChanged(nameof(IsLightTheme));
                OnPropertyChanged(nameof(IsDarkTheme));
                _initialTheme = isLight ? "Light" : "Dark";
            }
            finally
            {
                _isLoading = false;
            }

            IsDirty = !string.Equals(_initialDownloadsFolder, DownloadsFolder, StringComparison.Ordinal)
                || !string.Equals(_initialModelRootFolder, ModelRootFolder, StringComparison.Ordinal)
                || !string.Equals(_initialEngineRootFolder, EngineRootFolder, StringComparison.Ordinal)
                || _initialConfirmExit != ConfirmExit
                || _initialConfirmModule != ConfirmModule;
        }

        private void CaptureInitialState(string theme)
        {
            _initialTheme = theme;
            _initialDownloadsFolder = _downloadsFolder;
            _initialModelRootFolder = _modelRootFolder;
            _initialEngineRootFolder = _engineRootFolder;
            _initialConfirmExit = _confirmExit;
            _initialConfirmModule = _confirmModule;
        }

        public void CancelChanges()
        {
            _isLoading = true;
            try
            {
                var isLight = string.Equals(_initialTheme, "Light", StringComparison.OrdinalIgnoreCase);
                _isLightTheme = isLight;
                _isDarkTheme = !isLight;
                OnPropertyChanged(nameof(IsLightTheme));
                OnPropertyChanged(nameof(IsDarkTheme));

                DownloadsFolder = _initialDownloadsFolder;
                ModelRootFolder = _initialModelRootFolder;
                EngineRootFolder = _initialEngineRootFolder;
                ConfirmExit = _initialConfirmExit;
                ConfirmModule = _initialConfirmModule;

                ApplyThemeImmediate(_initialTheme);
            }
            finally
            {
                _isLoading = false;
                IsDirty = false;
            }
        }

        public void Cancel() => CancelChanges();

        public void DiscardChanges()
        {
            LoadSettings();
        }

        public void Discard() => DiscardChanges();

        private void BrowseDownloads()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Pilih Folder Downloads",
                    InitialDirectory = Directory.Exists(DownloadsFolder) ? DownloadsFolder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                };

                if (dialog.ShowDialog() == true)
                {
                    if (!string.IsNullOrWhiteSpace(dialog.FolderName))
                    {
                        DownloadsFolder = dialog.FolderName;
                    }
                }
            }
            catch
            {
                // Dialog error or cancelled
            }
        }

        private void BrowseModelFolder()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Pilih Folder Model",
                    InitialDirectory = Directory.Exists(ModelRootFolder)
                        ? ModelRootFolder
                        : AppDomain.CurrentDomain.BaseDirectory
                };

                if (dialog.ShowDialog() == true)
                {
                    if (!string.IsNullOrWhiteSpace(dialog.FolderName))
                    {
                        ModelRootFolder = dialog.FolderName;
                    }
                }
            }
            catch
            {
                // Dialog error or cancelled
            }
        }

        private void BrowseEngineFolder()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Pilih Folder Engine",
                    InitialDirectory = Directory.Exists(EngineRootFolder)
                        ? EngineRootFolder
                        : AppDomain.CurrentDomain.BaseDirectory
                };

                if (dialog.ShowDialog() == true)
                {
                    if (!string.IsNullOrWhiteSpace(dialog.FolderName))
                    {
                        EngineRootFolder = dialog.FolderName;
                    }
                }
            }
            catch
            {
                // Dialog error or cancelled
            }
        }

        public bool SaveChangesSync()
        {
            if (string.IsNullOrWhiteSpace(DownloadsFolder))
                return false;
            if (string.IsNullOrWhiteSpace(ModelRootFolder))
                return false;
            if (string.IsNullOrWhiteSpace(EngineRootFolder))
                return false;

            try
            {
                string theme = IsLightTheme ? "Light" : "Dark";
                _settingsManagerService.SaveSettings(theme, DownloadsFolder, ConfirmExit, ConfirmModule, ModelRootFolder, EngineRootFolder);
                ApplyThemeImmediate(theme);
                CaptureInitialState(theme);
                IsDirty = false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal async Task SaveSettingsAsync()
        {
            if (string.IsNullOrWhiteSpace(DownloadsFolder))
            {
                ShowInfoBar("Peringatan", "Folder Downloads tidak boleh kosong.", InfoBarSeverity.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(ModelRootFolder))
            {
                ShowInfoBar("Peringatan", "Folder Model tidak boleh kosong.", InfoBarSeverity.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(EngineRootFolder))
            {
                ShowInfoBar("Peringatan", "Folder Engine tidak boleh kosong.", InfoBarSeverity.Warning);
                return;
            }

            if (!Directory.Exists(DownloadsFolder))
            {
                if (_contentDialogService != null)
                {
                    var confirmResult = await _contentDialogService.ShowConfirmationAsync(
                        "Konfirmasi Folder",
                        $"Folder Downloads '{DownloadsFolder}' tidak ditemukan di sistem.\nApakah Anda tetap ingin menyimpannya?",
                        "Tetap Simpan",
                        "Batal",
                        iconGlyph: "Folder");

                    if (confirmResult != ContentDialogResult.Primary)
                    {
                        return;
                    }
                }
            }

            try
            {
                string theme = IsLightTheme ? "Light" : "Dark";
                await _settingsManagerService.SaveSettingsAsync(theme, DownloadsFolder, ConfirmExit, ConfirmModule, ModelRootFolder, EngineRootFolder);
                ApplyThemeImmediate(theme);
                CaptureInitialState(theme);
                IsDirty = false;
                HideInfoBar();

                _notificationService?.ShowSuccess("Sukses", "Pengaturan berhasil disimpan.");
            }
            catch (Exception ex)
            {
                ShowInfoBar("Error", $"Gagal menyimpan pengaturan: {ex.Message}", InfoBarSeverity.Error);
                _notificationService?.ShowError("Error", $"Gagal menyimpan pengaturan: {ex.Message}");
            }
        }

        private async Task ResetSettingsAsync()
        {
            if (_contentDialogService != null)
            {
                var result = await _contentDialogService.ShowConfirmationAsync(
                    "Konfirmasi Reset",
                    "Apakah Anda yakin ingin mengembalikan seluruh pengaturan ke nilai default?",
                    "Reset Default",
                    "Batal",
                    iconGlyph: "Alert");

                if (result != ContentDialogResult.Primary) return;
            }

            try
            {
                await _settingsManagerService.ResetToDefaultAsync();
                LoadSettings();

                string defaultTheme = IsLightTheme ? "Light" : "Dark";
                ApplyThemeImmediate(defaultTheme);

                IsDirty = false;
                HideInfoBar();
                _notificationService?.ShowInfo("Reset", "Pengaturan dikembalikan ke default.");
            }
            catch (Exception ex)
            {
                ShowInfoBar("Error", $"Gagal mengembalikan pengaturan: {ex.Message}", InfoBarSeverity.Error);
                _notificationService?.ShowError("Error", $"Gagal mengembalikan pengaturan: {ex.Message}");
            }
        }

        private static void ApplyThemeImmediate(string theme)
            => MainViewModel.ApplyThemeImmediate(theme);

        private void LoadCmykProfiles()
        {
            var all = _cmykProfileManager.GetAllProfiles().ToList();
            CmykProfiles = all;
            var defaultProfile = _cmykProfileManager.GetDefaultProfile();
            SelectedCmykProfile = defaultProfile != null
                ? all.FirstOrDefault(p => p.Id == defaultProfile.Id) ?? all.FirstOrDefault()
                : all.FirstOrDefault();
            OnPropertyChanged(nameof(IsCmykUnavailable));
        }

        private void OpenProfilesFolder()
        {
            try
            {
                var path = _cmykProfileManager.ProfileStorePath;
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    System.Diagnostics.Process.Start("explorer.exe", path);
                }
                else
                {
                    _notificationService?.ShowInfo("Informasi", "Folder profil tidak ditemukan.");
                }
            }
            catch (Exception ex)
            {
                _notificationService?.ShowError("Error", $"Gagal membuka folder: {ex.Message}");
            }
        }

        public void LoadAiModels()
        {
            if (_modelCatalogService == null) return;
            try
            {
                var snapshot = _modelCatalogService.CurrentSnapshot ?? _modelCatalogService.Load();
                RefreshInstalledAiModels(snapshot);
            }
            catch (Exception ex)
            {
                ModelOperationStatusMessage = $"Gagal memuat model: {ex.Message}";
            }
        }

        private async Task ScanAiModelsAsync()
        {
            if (_modelCatalogService == null || IsScanningModels || IsCopyingModel) return;
            IsScanningModels = true;
            ModelOperationStatusMessage = "Memindai folder model...";
            try
            {
                var snapshot = await _modelCatalogService.SyncCatalogAsync().ConfigureAwait(true);
                RefreshInstalledAiModels(snapshot);
                ModelOperationStatusMessage = $"Penyelarasan selesai. {snapshot.Models.Count} model terdaftar.";
            }
            catch (Exception ex)
            {
                ModelOperationStatusMessage = $"Penyelarasan gagal: {ex.Message}";
            }
            finally
            {
                IsScanningModels = false;
            }
        }

        private async Task AddAiModelAsync()
        {
            if (_modelCatalogService == null || IsScanningModels || IsCopyingModel) return;

            var picker = FilePicker ?? DefaultFilePicker;
            var filePath = picker();
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return;
            }

            IsCopyingModel = true;
            CopyProgressPercentage = 0;
            ModelOperationStatusMessage = "Menyiapkan penyalinan model...";

            var progress = new Progress<double>(p =>
            {
                CopyProgressPercentage = Math.Round(p * 100, 1);
                ModelOperationStatusMessage = $"Menyalin model... {CopyProgressPercentage}%";
            });

            try
            {
                var snapshot = await _modelCatalogService.AddModelAsync(filePath, null, progress).ConfigureAwait(true);
                RefreshInstalledAiModels(snapshot);
                ModelOperationStatusMessage = "Model berhasil ditambahkan ke AI Workspace.";
            }
            catch (Exception ex)
            {
                ModelOperationStatusMessage = $"Gagal menambahkan model: {ex.Message}";
            }
            finally
            {
                IsCopyingModel = false;
                CopyProgressPercentage = 0;
            }
        }

        private async Task DeleteAiModelAsync(object? parameter)
        {
            if (_modelCatalogService == null || IsScanningModels || IsCopyingModel) return;
            if (parameter is not string modelId || string.IsNullOrWhiteSpace(modelId)) return;

            var targetModel = InstalledAiModels.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.Ordinal));
            var displayName = targetModel?.DisplayName ?? modelId;

            if (_contentDialogService != null)
            {
                var confirm = await _contentDialogService.ShowDangerConfirmationAsync(
                    "Hapus Model AI?",
                    $"Apakah Anda yakin ingin menghapus model '{displayName}'?\nFile model di disk akan dihapus permanen.",
                    "Hapus Model",
                    "Batal",
                    iconGlyph: "Delete");

                if (confirm != ContentDialogResult.Primary) return;
            }

            ModelOperationStatusMessage = $"Menghapus model {displayName}...";
            try
            {
                var snapshot = await _modelCatalogService.DeleteModelAsync(modelId).ConfigureAwait(true);
                RefreshInstalledAiModels(snapshot);
                ModelOperationStatusMessage = $"Model '{displayName}' berhasil dihapus.";
            }
            catch (Exception ex)
            {
                ModelOperationStatusMessage = $"Gagal menghapus model: {ex.Message}";
            }
        }

        private async Task MoveModelAsync(object? parameter, ModelOrderDirection direction)
        {
            if (_modelCatalogService == null || IsScanningModels || IsCopyingModel) return;
            if (parameter is not string modelId || string.IsNullOrWhiteSpace(modelId)) return;

            try
            {
                var snapshot = await _modelCatalogService.ReorderModelAsync(modelId, direction).ConfigureAwait(true);
                RefreshInstalledAiModels(snapshot);
            }
            catch (Exception ex)
            {
                ModelOperationStatusMessage = $"Gagal mengubah urutan model: {ex.Message}";
            }
        }

        private void RefreshInstalledAiModels(ModelCatalogSnapshot snapshot)
        {
            InstalledAiModels.Clear();
            for (int i = 0; i < snapshot.Models.Count; i++)
            {
                var itemVm = new AiModelSettingItemViewModel(snapshot.Models[i]);
                itemVm.UpdatePosition(isFirst: i == 0, isLast: i == snapshot.Models.Count - 1);
                InstalledAiModels.Add(itemVm);
            }
        }

        private void OpenAiModelsFolder()
        {
            try
            {
                var path = Path.Combine(_settingsManagerService.ModelRootFolder, "AI Workspace");
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ModelOperationStatusMessage = $"Gagal membuka folder: {ex.Message}";
            }
        }

        private static string? DefaultFilePicker()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pilih Model AI (.gguf)",
                Filter = "GGUF Model (*.gguf)|*.gguf|All Files (*.*)|*.*",
                CheckFileExists = true
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }

    public sealed class AiModelSettingItemViewModel : ViewModelBase
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string FileSizeFormatted { get; }
        public string StatusBadge { get; }
        public bool IsActive { get; }
        public bool IsAvailable { get; }
        public string? GgufPath { get; }

        private bool _isFirst;
        private bool _isLast;

        public bool IsFirst
        {
            get => _isFirst;
            private set
            {
                if (SetProperty(ref _isFirst, value))
                {
                    OnPropertyChanged(nameof(CanMoveUp));
                }
            }
        }

        public bool IsLast
        {
            get => _isLast;
            private set
            {
                if (SetProperty(ref _isLast, value))
                {
                    OnPropertyChanged(nameof(CanMoveDown));
                }
            }
        }

        public bool CanMoveUp => !IsFirst;
        public bool CanMoveDown => !IsLast;

        public void UpdatePosition(bool isFirst, bool isLast)
        {
            IsFirst = isFirst;
            IsLast = isLast;
        }

        public AiModelSettingItemViewModel(ModelCatalogItem item)
        {
            Id = item.Id;
            DisplayName = item.DisplayName;
            IsActive = item.IsActive;
            IsAvailable = item.IsAvailable;
            GgufPath = item.ResolvedGgufPath;

            if (item.IsActive)
            {
                StatusBadge = "Aktif";
            }
            else if (item.IsAvailable)
            {
                StatusBadge = "Tersedia";
            }
            else
            {
                StatusBadge = item.Status == ModelCatalogItemStatus.GgufNotFound ? "File Hilang" : "Tidak Valid";
            }

            if (!string.IsNullOrEmpty(item.ResolvedGgufPath) && File.Exists(item.ResolvedGgufPath))
            {
                try
                {
                    var length = new FileInfo(item.ResolvedGgufPath).Length;
                    FileSizeFormatted = FormatFileSize(length);
                }
                catch
                {
                    FileSizeFormatted = "-";
                }
            }
            else
            {
                FileSizeFormatted = "-";
            }
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
            {
                return $"{(double)bytes / (1024L * 1024 * 1024):F2} GB";
            }
            if (bytes >= 1024 * 1024)
            {
                return $"{(double)bytes / (1024 * 1024):F1} MB";
            }
            return $"{bytes / 1024} KB";
        }
    }
}
