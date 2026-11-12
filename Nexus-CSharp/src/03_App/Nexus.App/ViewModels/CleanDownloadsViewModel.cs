using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexus.App.Controls.ContentDialog;
using Nexus.App.Controls.Snackbar;
using Nexus.App.Services;
using Nexus.Core.HomeSettings.Services;
using Nexus.FileUtilities.Services;
using Brush = System.Windows.Media.Brush;

namespace Nexus.App.ViewModels
{
    public class CleanFileItemViewModel : ViewModelBase
    {
        private bool _checked;
        public bool Checked
        {
            get => _checked;
            set
            {
                if (SetProperty(ref _checked, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusFg));
                    OnCheckChanged?.Invoke();
                }
            }
        }

        public string Name { get; set; } = string.Empty;
        public string Ext { get; set; } = string.Empty;
        public Brush ExtBg { get; set; } = System.Windows.Media.Brushes.Blue;
        public string SizeDisplay { get; set; } = "0 B";
        public long RawSize { get; set; }
        public string AgeDisplay { get; set; } = string.Empty;
        public Brush AgeFg { get; set; } = System.Windows.Media.Brushes.Green;
        public string TargetFolder { get; set; } = string.Empty;

        public string StatusText => Checked ? "HAPUS" : "SIMPAN";

        public Brush StatusFg
        {
            get
            {
                var hex = Checked ? "#DC2626" : "#16A34A";
                return (Brush)new BrushConverter().ConvertFromString(hex)!;
            }
        }

        public string FullPath { get; set; } = string.Empty;
        public DateTime LastWriteTime { get; set; }
        public Action? OnCheckChanged { get; set; }
    }

    public class CleanDownloadsViewModel : ViewModelBase
    {
        private readonly ICleanDownloadsService _cleanDownloadsService;
        private readonly ISettingsManagerService _settingsManagerService;
        private readonly INotificationService? _notificationService;
        private readonly IContentDialogService? _contentDialogService;

        private int _totalFileCount;
        private string _totalSizeText = "0 B";
        private int _selectedCount;
        private string _folderNameText = "Downloads";
        private string _rightCountText = "0 file";
        private string _progressText = "Centang = hapus. Kosongkan = pindah kategori folder.";
        private string _executeButtonText = "BERSIHKAN BERKAS";
        private Brush _executeButtonBackground;

        private string _selectedFilter = "all";
        private int _selectedSortIndex = 0;

        private CleanFileItemViewModel? _selectedItem;
        private ImageSource? _previewImage;
        private bool _isPreviewEmpty = true;
        private string _previewInfoText = string.Empty;

        private bool _isProgressVisible;
        private double _progressValue;

        private List<CleanFileItemViewModel> _allMasterFiles = new();
        private bool _isBatchUpdating;

        public int TotalFileCount
        {
            get => _totalFileCount;
            set => SetProperty(ref _totalFileCount, value);
        }

        public string TotalSizeText
        {
            get => _totalSizeText;
            set => SetProperty(ref _totalSizeText, value);
        }

        public int SelectedCount
        {
            get => _selectedCount;
            set => SetProperty(ref _selectedCount, value);
        }

        public string FolderNameText
        {
            get => _folderNameText;
            set => SetProperty(ref _folderNameText, value);
        }

        public string RightCountText
        {
            get => _rightCountText;
            set => SetProperty(ref _rightCountText, value);
        }

        public string ProgressText
        {
            get => _progressText;
            set => SetProperty(ref _progressText, value);
        }

        public string ExecuteButtonText
        {
            get => _executeButtonText;
            set => SetProperty(ref _executeButtonText, value);
        }

        public Brush ExecuteButtonBackground
        {
            get => _executeButtonBackground;
            set => SetProperty(ref _executeButtonBackground, value);
        }

        public string SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                if (SetProperty(ref _selectedFilter, value))
                {
                    OnPropertyChanged(nameof(IsFilterAll));
                    OnPropertyChanged(nameof(IsFilterImage));
                    OnPropertyChanged(nameof(IsFilterVideo));
                    OnPropertyChanged(nameof(IsFilterDoc));
                    OnPropertyChanged(nameof(IsFilterDesign));
                    OnPropertyChanged(nameof(IsFilterArchive));
                    OnPropertyChanged(nameof(IsFilterOther));
                    ApplyFilterAndSort();
                }
            }
        }

        public bool IsFilterAll { get => SelectedFilter == "all"; set { if (value) SelectedFilter = "all"; } }
        public bool IsFilterImage { get => SelectedFilter == "image"; set { if (value) SelectedFilter = "image"; } }
        public bool IsFilterVideo { get => SelectedFilter == "video"; set { if (value) SelectedFilter = "video"; } }
        public bool IsFilterDoc { get => SelectedFilter == "doc"; set { if (value) SelectedFilter = "doc"; } }
        public bool IsFilterDesign { get => SelectedFilter == "design"; set { if (value) SelectedFilter = "design"; } }
        public bool IsFilterArchive { get => SelectedFilter == "archive"; set { if (value) SelectedFilter = "archive"; } }
        public bool IsFilterOther { get => SelectedFilter == "other"; set { if (value) SelectedFilter = "other"; } }

        public int SelectedSortIndex
        {
            get => _selectedSortIndex;
            set
            {
                if (SetProperty(ref _selectedSortIndex, value))
                {
                    ApplyFilterAndSort();
                }
            }
        }

        public CleanFileItemViewModel? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    UpdatePreviewPanel(value);
                }
            }
        }

        public ImageSource? PreviewImage
        {
            get => _previewImage;
            set => SetProperty(ref _previewImage, value);
        }

        public bool IsPreviewEmpty
        {
            get => _isPreviewEmpty;
            set
            {
                if (SetProperty(ref _isPreviewEmpty, value))
                {
                    OnPropertyChanged(nameof(IsPreviewInfoVisible));
                }
            }
        }

        public bool IsPreviewInfoVisible => !IsPreviewEmpty;

        public string PreviewInfoText
        {
            get => _previewInfoText;
            set => SetProperty(ref _previewInfoText, value);
        }

        public bool IsProgressVisible
        {
            get => _isProgressVisible;
            set => SetProperty(ref _isProgressVisible, value);
        }

        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        public ObservableCollection<CleanFileItemViewModel> Items { get; } = new();

        public ICommand RefreshCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand ClearSelectionCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand ExecuteCleanCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand OpenLogCommand { get; }

        public CleanDownloadsViewModel(ICleanDownloadsService cleanDownloadsService, ISettingsManagerService settingsManagerService)
            : this(cleanDownloadsService, settingsManagerService, null, null)
        {
        }

        public CleanDownloadsViewModel(
            ICleanDownloadsService cleanDownloadsService,
            ISettingsManagerService settingsManagerService,
            INotificationService? notificationService)
            : this(cleanDownloadsService, settingsManagerService, notificationService, null)
        {
        }

        public CleanDownloadsViewModel(
            ICleanDownloadsService cleanDownloadsService,
            ISettingsManagerService settingsManagerService,
            INotificationService? notificationService,
            IContentDialogService? contentDialogService)
        {
            _cleanDownloadsService = cleanDownloadsService;
            _settingsManagerService = settingsManagerService;
            _notificationService = notificationService;
            _contentDialogService = contentDialogService;

            var defaultAccent = (Brush)new BrushConverter().ConvertFromString("#0078D4")!;
            _executeButtonBackground = defaultAccent;

            RefreshCommand = new AsyncRelayCommand(RefreshDownloadsAsync);
            SelectAllCommand = new RelayCommand(SelectAll);
            ClearSelectionCommand = new RelayCommand(ClearSelection);
            UndoCommand = new AsyncRelayCommand(UndoAsync);
            ExecuteCleanCommand = new AsyncRelayCommand(ExecuteCleanAsync);
            OpenFileCommand = new RelayCommand(OpenFile);
            OpenLogCommand = new RelayCommand(OpenLog);

            _ = RefreshDownloadsAsync();
        }

        private async Task RefreshDownloadsAsync()
        {
            try
            {
                var downloadsFolder = _settingsManagerService.GetDownloadsPath();
                if (string.IsNullOrWhiteSpace(downloadsFolder) || !Directory.Exists(downloadsFolder))
                {
                    downloadsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                }

                FolderNameText = Path.GetFileName(downloadsFolder.TrimEnd(Path.DirectorySeparatorChar));

                ProgressText = "Memindai folder...";
                var rawItems = await _cleanDownloadsService.ScanDownloadsFolderAsync(downloadsFolder);

                var bc = new BrushConverter();
                _allMasterFiles = rawItems.Select(r => new CleanFileItemViewModel
                {
                    Checked = r.Checked,
                    Name = r.Name,
                    Ext = r.Ext,
                    ExtBg = (Brush)bc.ConvertFromString(r.ExtBgHex)!,
                    SizeDisplay = r.SizeDisplay,
                    RawSize = r.RawSize,
                    AgeDisplay = r.AgeDisplay,
                    AgeFg = (Brush)bc.ConvertFromString(r.AgeFgHex)!,
                    TargetFolder = r.TargetFolder,
                    FullPath = r.FullPath,
                    LastWriteTime = r.LastWriteTime,
                    OnCheckChanged = OnItemCheckChanged
                }).ToList();

                TotalFileCount = _allMasterFiles.Count;
                long totalSize = _allMasterFiles.Sum(f => f.RawSize);
                TotalSizeText = FormatFileSize(totalSize);

                ApplyFilterAndSort();
                SelectedItem = null;
            }
            catch (Exception ex)
            {
                _notificationService?.ShowError("Error", $"Gagal memindai folder Downloads: {ex.Message}");
            }
        }

        private void ApplyFilterAndSort()
        {
            IEnumerable<CleanFileItemViewModel> filtered = _allMasterFiles;

            if (_selectedFilter != "all")
            {
                filtered = _selectedFilter switch
                {
                    "image" => filtered.Where(f => new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tiff", ".raw" }.Contains(Path.GetExtension(f.FullPath).ToLower())),
                    "video" => filtered.Where(f => new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".3gp" }.Contains(Path.GetExtension(f.FullPath).ToLower())),
                    "doc" => filtered.Where(f => new[] { ".pdf", ".doc", ".docx", ".rtf", ".xls", ".xlsx", ".csv", ".ppt", ".pptx", ".txt" }.Contains(Path.GetExtension(f.FullPath).ToLower())),
                    "design" => filtered.Where(f => new[] { ".ai", ".eps", ".cdr", ".psd" }.Contains(Path.GetExtension(f.FullPath).ToLower())),
                    "archive" => filtered.Where(f => new[] { ".zip", ".rar", ".7z", ".tar", ".gz" }.Contains(Path.GetExtension(f.FullPath).ToLower())),
                    "other" => filtered.Where(f => _cleanDownloadsService.GetDestinationSubfolder(f.Name) == "Others"),
                    _ => filtered
                };
            }

            filtered = _selectedSortIndex switch
            {
                0 => filtered.OrderByDescending(f => f.LastWriteTime),
                1 => filtered.OrderBy(f => f.LastWriteTime),
                2 => filtered.OrderByDescending(f => f.RawSize),
                3 => filtered.OrderBy(f => f.RawSize),
                4 => filtered.OrderBy(f => f.Name),
                5 => filtered.OrderByDescending(f => f.Name),
                _ => filtered
            };

            Items.Clear();
            foreach (var item in filtered)
            {
                Items.Add(item);
            }

            RightCountText = $"{Items.Count} file";
            UpdateDashboardAndStatus();
        }

        private void OnItemCheckChanged()
        {
            if (_isBatchUpdating) return;
            UpdateDashboardAndStatus();
        }

        private void SelectAll()
        {
            _isBatchUpdating = true;
            try
            {
                foreach (var item in Items)
                {
                    item.Checked = true;
                }
            }
            finally
            {
                _isBatchUpdating = false;
            }
            UpdateDashboardAndStatus();
        }

        private void ClearSelection()
        {
            _isBatchUpdating = true;
            try
            {
                foreach (var item in Items)
                {
                    item.Checked = false;
                }
            }
            finally
            {
                _isBatchUpdating = false;
            }
            UpdateDashboardAndStatus();
        }

        private void UpdateDashboardAndStatus()
        {
            if (_isBatchUpdating) return;

            SelectedCount = _allMasterFiles.Count(f => f.Checked);
            long selectedSize = _allMasterFiles.Where(f => f.Checked).Sum(f => f.RawSize);

            var bc = new BrushConverter();
            if (SelectedCount > 0)
            {
                ProgressText = $"{SelectedCount} file dicentang (HAPUS) - {FormatFileSize(selectedSize)}";
                ExecuteButtonText = $"BERSIHKAN BERKAS ({SelectedCount})";
                ExecuteButtonBackground = (Brush)bc.ConvertFromString("#DC2626")!;
            }
            else
            {
                ProgressText = "Centang = hapus. Kosongkan = pindah kategori folder.";
                ExecuteButtonText = "BERSIHKAN BERKAS";
                ExecuteButtonBackground = (Brush)bc.ConvertFromString("#0078D4")!;
            }

            if (SelectedItem != null)
            {
                var statusMsg = SelectedItem.Checked ? "DICENTANG (akan dihapus)" : "TIDAK DICENTANG (akan dipindah)";
                PreviewInfoText = $"Nama: {SelectedItem.Name}\nUkuran: {SelectedItem.SizeDisplay}\nUmur: {SelectedItem.AgeDisplay}\nTarget: {SelectedItem.TargetFolder}\nStatus: {statusMsg}";
            }
        }

        private void UpdatePreviewPanel(CleanFileItemViewModel? item)
        {
            if (item == null)
            {
                PreviewImage = null;
                IsPreviewEmpty = true;
                PreviewInfoText = string.Empty;
                return;
            }

            IsPreviewEmpty = false;
            var statusMsg = item.Checked ? "DICENTANG (akan dihapus)" : "TIDAK DICENTANG (akan dipindah)";
            PreviewInfoText = $"Nama: {item.Name}\nUkuran: {item.SizeDisplay}\nUmur: {item.AgeDisplay}\nTarget: {item.TargetFolder}\nStatus: {statusMsg}";

            try
            {
                var ext = Path.GetExtension(item.FullPath).ToLower();
                var imageExts = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };

                if (imageExts.Contains(ext))
                {
                    byte[] bytes = File.ReadAllBytes(item.FullPath);
                    using var ms = new MemoryStream(bytes);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = ms;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    PreviewImage = bitmap;
                }
                else
                {
                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(item.FullPath);
                    if (icon != null)
                    {
                        using var bmp = icon.ToBitmap();
                        using var ms = new MemoryStream();
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        ms.Position = 0;
                        var wpfIcon = new BitmapImage();
                        wpfIcon.BeginInit();
                        wpfIcon.StreamSource = ms;
                        wpfIcon.CacheOption = BitmapCacheOption.OnLoad;
                        wpfIcon.EndInit();
                        wpfIcon.Freeze();
                        PreviewImage = wpfIcon;
                    }
                    else
                    {
                        PreviewImage = null;
                    }
                }
            }
            catch
            {
                PreviewImage = null;
            }
        }

        private async Task ExecuteCleanAsync()
        {
            if (_allMasterFiles.Count == 0)
            {
                _notificationService?.ShowInfo("Clean Downloads", "Tidak ada file untuk diproses.");
                return;
            }

            int delCount = _allMasterFiles.Count(f => f.Checked);
            int moveCount = _allMasterFiles.Count - delCount;

            if (_contentDialogService != null)
            {
                var confirm = await _contentDialogService.ShowConfirmationAsync(
                    "Konfirmasi Clean Downloads",
                    $"Konfirmasi Aksi:\n• Hapus ke Recycle Bin: {delCount} file\n• Pindah Kategori: {moveCount} file\n\nLanjutkan pembersihan?",
                    "Bersihkan",
                    "Batal",
                    iconGlyph: "Delete");

                if (confirm != ContentDialogResult.Primary) return;
            }

            IsProgressVisible = true;
            ProgressValue = 0;
            ProgressText = "Memulai pemrosesan...";

            var downloadsFolder = _settingsManagerService.GetDownloadsPath();
            var serviceItems = _allMasterFiles.Select(m => new CleanFileItemModel
            {
                Checked = m.Checked,
                Name = m.Name,
                Ext = m.Ext,
                RawSize = m.RawSize,
                TargetFolder = m.TargetFolder,
                FullPath = m.FullPath
            }).ToList();

            var progress = new Progress<double>(val => ProgressValue = val);

            try
            {
                var report = await _cleanDownloadsService.ExecuteCleanDownloadsAsync(downloadsFolder, serviceItems, progress);

                var summaryMsg = $"Berhasil memproses {report.DeletedCount + report.MovedCount} file ({FormatFileSize(report.ProcessedSize)}) dalam {report.Duration:hh\\:mm\\:ss}.";
                if (report.FailedCount > 0)
                {
                    summaryMsg += $" Dilewati: {report.FailedCount} file.";
                }
                _notificationService?.ShowSuccess("Clean Downloads Selesai", summaryMsg);

                await RefreshDownloadsAsync();
            }
            catch (Exception ex)
            {
                _notificationService?.ShowError("Error", $"Gagal memproses berkas: {ex.Message}");
            }
            finally
            {
                IsProgressVisible = false;
            }
        }

        private async Task UndoAsync()
        {
            var downloadsFolder = _settingsManagerService.GetDownloadsPath();
            if (_contentDialogService != null)
            {
                var confirm = await _contentDialogService.ShowConfirmationAsync(
                    "Konfirmasi Undo",
                    "Apakah Anda yakin ingin membatalkan perpindahan file terakhir?",
                    "Batalkan (Undo)",
                    "Tutup",
                    iconGlyph: "Help");

                if (confirm != ContentDialogResult.Primary) return;
            }

            try
            {
                var resultMsg = await _cleanDownloadsService.UndoCleanDownloadsAsync(downloadsFolder);
                _notificationService?.ShowSuccess("Sukses Undo", resultMsg);
                await RefreshDownloadsAsync();
            }
            catch (Exception ex)
            {
                _notificationService?.ShowError("Error", $"Gagal Undo: {ex.Message}");
            }
        }

        private void OpenFile()
        {
            if (SelectedItem != null && File.Exists(SelectedItem.FullPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = SelectedItem.FullPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    _notificationService?.ShowError("Error", $"Gagal membuka file: {ex.Message}");
                }
            }
        }

        private void OpenLog()
        {
            try
            {
                var downloadsFolder = _settingsManagerService.GetDownloadsPath();
                var logPath = _cleanDownloadsService.GetLatestLogPath(downloadsFolder);

                if (logPath != null && File.Exists(logPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = logPath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    _notificationService?.ShowInfo("Informasi", "Belum ada file log yang tersedia.");
                }
            }
            catch (Exception ex)
            {
                _notificationService?.ShowError("Error", $"Gagal membuka file log: {ex.Message}");
            }
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1073741824) return $"{bytes / 1073741824.0:N1} GB";
            if (bytes >= 1048576) return $"{bytes / 1048576.0:N1} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:N0} KB";
            return $"{bytes} B";
        }
    }
}
