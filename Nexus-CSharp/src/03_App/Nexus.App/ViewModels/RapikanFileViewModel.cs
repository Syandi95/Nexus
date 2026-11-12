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
using System.Windows.Threading;
using Nexus.App.Controls.ContentDialog;
using Nexus.App.Controls.Snackbar;
using Nexus.App.Services;
using Nexus.FileUtilities.Services;

namespace Nexus.App.ViewModels
{
    public class RapikanStatCardViewModel : ViewModelBase
    {
        public string Category { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string IconName { get; set; } = string.Empty;
        public string IconGlyph { get; set; } = string.Empty;

        private string _valueText = "0";
        public string ValueText
        {
            get => _valueText;
            set => SetProperty(ref _valueText, value);
        }

        private string _percentText = "";
        public string PercentText
        {
            get => _percentText;
            set => SetProperty(ref _percentText, value);
        }

        private double _percentValue = 0;
        public double PercentValue
        {
            get => _percentValue;
            set => SetProperty(ref _percentValue, value);
        }
    }

    public class LogEntryModel
    {
        public string Message { get; set; } = string.Empty;
        public Brush Color { get; set; } = Brushes.Gray;
    }

    public class RapikanFileViewModel : ViewModelBase
    {
        private readonly IRapikanFileService _rapikanFileService;
        private readonly INotificationService? _notificationService;
        private readonly IContentDialogService? _contentDialogService;

        private string _targetPath = string.Empty;
        private bool _canStart;
        private bool _canResetStats;
        private bool _isProcessing;
        private bool _isAnalyzing;
        private bool _isProgressVisible;

        private string _phaseText = "Pilih Folder Target";
        private string _progressPercentText = "0%";
        private double _progressPercentValue;
        private string _statusText = "Pilih folder target untuk memulai analisis.";
        private string _statusIconGlyph = "Info";
        private string _currentFileText = "-";
        private string _elapsedText = "00:00:00";
        private string _statsLabelText = "Estimasi File";

        private DispatcherTimer? _elapsedTimer;
        private DateTime _startTime;

        public string TargetPath
        {
            get => _targetPath;
            set => SetProperty(ref _targetPath, value);
        }

        public bool CanStart
        {
            get => _canStart;
            set => SetProperty(ref _canStart, value);
        }

        public bool CanResetStats
        {
            get => _canResetStats;
            set => SetProperty(ref _canResetStats, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (SetProperty(ref _isProcessing, value))
                {
                    OnPropertyChanged(nameof(CanBrowse));
                    OnPropertyChanged(nameof(IsBusy));
                }
            }
        }

        public bool IsAnalyzing
        {
            get => _isAnalyzing;
            private set
            {
                if (SetProperty(ref _isAnalyzing, value))
                {
                    OnPropertyChanged(nameof(IsBusy));
                }
            }
        }

        public bool IsBusy => IsProcessing || IsAnalyzing;

        public bool CanBrowse => !IsProcessing;

        public bool IsProgressVisible
        {
            get => _isProgressVisible;
            set => SetProperty(ref _isProgressVisible, value);
        }

        public string PhaseText
        {
            get => _phaseText;
            set => SetProperty(ref _phaseText, value);
        }

        public string ProgressPercentText
        {
            get => _progressPercentText;
            set => SetProperty(ref _progressPercentText, value);
        }

        public double ProgressPercentValue
        {
            get => _progressPercentValue;
            set => SetProperty(ref _progressPercentValue, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string StatusIconGlyph
        {
            get => _statusIconGlyph;
            set => SetProperty(ref _statusIconGlyph, value);
        }

        public string CurrentFileText
        {
            get => _currentFileText;
            set => SetProperty(ref _currentFileText, value);
        }

        public string ElapsedText
        {
            get => _elapsedText;
            set => SetProperty(ref _elapsedText, value);
        }

        public string StatsLabelText
        {
            get => _statsLabelText;
            set => SetProperty(ref _statsLabelText, value);
        }

        public ObservableCollection<RapikanStatCardViewModel> StatCards { get; } = new();
        public ObservableCollection<LogEntryModel> LogEntries { get; } = new();

        public ICommand BrowseFolderCommand { get; }
        public ICommand StartRapikanCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand ResetStatsCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand OpenLogFileCommand { get; }

        public RapikanFileViewModel(IRapikanFileService rapikanFileService)
            : this(rapikanFileService, null, null)
        {
        }

        public RapikanFileViewModel(IRapikanFileService rapikanFileService, INotificationService? notificationService)
            : this(rapikanFileService, notificationService, null)
        {
        }

        public RapikanFileViewModel(
            IRapikanFileService rapikanFileService,
            INotificationService? notificationService,
            IContentDialogService? contentDialogService)
        {
            _rapikanFileService = rapikanFileService;
            _notificationService = notificationService;
            _contentDialogService = contentDialogService;

            BrowseFolderCommand = new RelayCommand(BrowseFolder);
            StartRapikanCommand = new AsyncRelayCommand(StartRapikanAsync);
            UndoCommand = new AsyncRelayCommand(UndoAsync);
            ResetStatsCommand = new RelayCommand(ResetStats);
            ClearLogCommand = new RelayCommand(ClearLog);
            OpenLogFileCommand = new RelayCommand(OpenLogFile);

            InitializeStatCards();
        }

        private void InitializeStatCards()
        {
            StatCards.Clear();
            var categories = new[]
            {
                ("Images", "Images", "Image"),
                ("Videos", "Videos", "Videos"),
                ("Desain", "Desain", "Edit"),
                ("Documents", "Documents", "Document"),
                ("Audio", "Audio", "Audio"),
                ("Font", "Font", "Font"),
                ("Archives", "Archives", "Archive"),
                ("Applications", "Applications", "Applications"),
                ("Others", "Others", "NewFolder"),
                ("Sampah", "Sampah", "Delete"),
                ("TotalFile", "Total File", "Tag"),
                ("TotalSize", "Total Size", "Size")
            };

            foreach (var (cat, label, icon) in categories)
            {
                StatCards.Add(new RapikanStatCardViewModel
                {
                    Category = cat,
                    Label = label,
                    IconName = icon,
                    ValueText = cat == "TotalSize" ? "0 B" : "0",
                    PercentText = "",
                    PercentValue = 0
                });
            }
        }

        private void BrowseFolder()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Pilih Folder Target Rapikan File",
                    InitialDirectory = Directory.Exists(TargetPath) ? TargetPath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                };

                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
                {
                    ResetProgressForNewTarget();
                    TargetPath = dialog.FolderName;
                    CanStart = true;
                    CanResetStats = true;
                    _ = StartPreScanAsync(TargetPath);
                }
            }
            catch
            {
                // Dialog cancelled or unavailable
            }
        }

        private void ResetProgressForNewTarget()
        {
            IsProgressVisible = false;
            PhaseText = "Pilih Folder Target";
            ProgressPercentText = "0%";
            ProgressPercentValue = 0;
            StatusText = "Pilih folder target untuk memulai analisis.";
            StatusIconGlyph = "Info";
            CurrentFileText = "-";
            ElapsedText = "00:00:00";
        }

        private async Task StartPreScanAsync(string folder)
        {
            IsAnalyzing = true;
            PhaseText = "Menganalisis Folder";
            StatusText = "Memindai isi folder...";
            StatusIconGlyph = "Sync";
            ProgressPercentText = "0%";
            ProgressPercentValue = 0;
            CurrentFileText = "-";
            ElapsedText = "00:00:00";
            StatsLabelText = "Memindai...";
            CanResetStats = false;
            AddLog("Memindai folder untuk estimasi (Background)...");

            try
            {
                var stats = await _rapikanFileService.PreScanDirectoryAsync(folder);
                UpdateStatCards(stats);
                StatsLabelText = "Estimasi File (Pre-Scan)";
                if (!IsProcessing)
                {
                    PhaseText = "Siap";
                    StatusText = "Estimasi selesai. Siap memulai proses.";
                    StatusIconGlyph = "Info";
                }
                CanResetStats = !IsProcessing;
                AddLog($"Pre-scan selesai. Total file estimasi: {stats["TotalFile"]}");
            }
            catch (Exception ex)
            {
                AddLog($"Gagal memindai folder: {ex.Message}", "#EF4444");
                StatsLabelText = "Estimasi File";
                if (!IsProcessing)
                {
                    PhaseText = "Gagal";
                    StatusText = "Periksa Log Aktivitas untuk informasi lebih lanjut.";
                    StatusIconGlyph = "Error";
                }
                CanResetStats = !IsProcessing;
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        private void UpdateStatCards(Dictionary<string, long> stats)
        {
            long totalFiles = stats.TryGetValue("TotalFile", out var tf) ? tf : 0;

            foreach (var card in StatCards)
            {
                if (stats.TryGetValue(card.Category, out var val))
                {
                    if (card.Category == "TotalSize")
                    {
                        card.ValueText = FormatFileSize(val);
                        card.PercentText = "";
                        card.PercentValue = 0;
                    }
                    else if (card.Category == "TotalFile")
                    {
                        card.ValueText = val.ToString("N0");
                        card.PercentText = "";
                        card.PercentValue = 0;
                    }
                    else
                    {
                        card.ValueText = val.ToString("N0");
                        if (totalFiles > 0)
                        {
                            double pct = Math.Round((double)val / totalFiles * 100);
                            card.PercentText = $"{pct}%";
                            card.PercentValue = pct;
                        }
                        else
                        {
                            card.PercentText = "";
                            card.PercentValue = 0;
                        }
                    }
                }
            }
        }

        private async Task StartRapikanAsync()
        {
            if (string.IsNullOrWhiteSpace(TargetPath))
            {
                _notificationService?.ShowWarning("Peringatan", "Silakan pilih folder target terlebih dahulu!");
                return;
            }

            if (!Directory.Exists(TargetPath))
            {
                _notificationService?.ShowWarning("Peringatan", "Folder target tidak ditemukan!");
                AddLog("Folder target tidak ditemukan!", "#EF4444");
                return;
            }

            var totalFile = StatCards.FirstOrDefault(c => c.Category == "TotalFile")?.ValueText ?? "0";
            if (_contentDialogService != null)
            {
                var confirm = await _contentDialogService.ShowConfirmationAsync(
                    "Konfirmasi Rapikan File",
                    $"Folder: {TargetPath}\nTotal file estimasi: {totalFile}\n\nFile akan dikelompokkan dan dipindahkan secara permanen. Lanjutkan?",
                    "Rapikan Sekarang",
                    "Batal",
                    iconGlyph: "Folder");

                if (confirm != ContentDialogResult.Primary) return;
            }

            IsProcessing = true;
            CanStart = false;
            CanResetStats = false;
            IsProgressVisible = true;

            StatsLabelText = "Sedang Memproses...";
            PhaseText = "Sedang Merapikan";
            ProgressPercentText = "0%";
            ProgressPercentValue = 0;
            StatusText = "Mengelompokkan file...";
            StatusIconGlyph = "Sync";
            CurrentFileText = "-";
            ElapsedText = "00:00:00";

            StartTimer();

            var progress = new Progress<RapikanProgressReport>(report =>
            {
                ProgressPercentText = $"{report.ProgressPct}%";
                ProgressPercentValue = report.ProgressPct;
                CurrentFileText = report.CurrentFile;

                if (report.RealtimeStats != null)
                {
                    UpdateStatCards(report.RealtimeStats);
                }
            });

            Action<string, string?> logCallback = (msg, colorHex) =>
            {
                Application.Current?.Dispatcher.Invoke(() => AddLog(msg, colorHex));
            };

            try
            {
                await _rapikanFileService.ExecuteOrganizeAsync(TargetPath, progress, logCallback);
                PhaseText = "Selesai";
                StatusText = "Proses selesai.";
                ProgressPercentText = "100%";
                ProgressPercentValue = 100;
                StatusIconGlyph = "Check";
                StatsLabelText = "Estimasi File (Selesai)";
            }
            catch (Exception ex)
            {
                AddLog($"[FATAL ERROR] {ex.Message}", "#EF4444");
                PhaseText = "Gagal";
                StatusText = "Periksa Log Aktivitas untuk informasi lebih lanjut.";
                StatusIconGlyph = "Error";
                StatsLabelText = "Estimasi File (Gagal)";
            }
            finally
            {
                StopTimer();
                IsProcessing = false;
                CanStart = true;
                CanResetStats = true;
            }
        }

        private async Task UndoAsync()
        {
            if (string.IsNullOrWhiteSpace(TargetPath))
            {
                _notificationService?.ShowWarning("Peringatan", "Silakan pilih folder target terlebih dahulu!");
                return;
            }

            var manifestPath = Path.Combine(TargetPath, "undo_manifest.json");
            if (!File.Exists(manifestPath))
            {
                _notificationService?.ShowWarning("Peringatan", "Tidak ada data Undo yang tersimpan untuk folder ini.");
                return;
            }

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
                IsProcessing = true;
                AddLog("Memulai proses Undo...");
                await _rapikanFileService.UndoOrganizeAsync(manifestPath);
                AddLog("Proses Undo selesai. Seluruh file dikembalikan.");
                _notificationService?.ShowSuccess("Sukses", "Berhasil membatalkan perpindahan file terakhir.");

                _ = StartPreScanAsync(TargetPath);
            }
            catch (Exception ex)
            {
                AddLog($"Gagal Undo: {ex.Message}", "#EF4444");
                _notificationService?.ShowError("Error", $"Gagal Undo: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private void ResetStats()
        {
            if (IsProcessing) return;

            foreach (var card in StatCards)
            {
                card.ValueText = card.Category == "TotalSize" ? "0 B" : "0";
                card.PercentText = "";
                card.PercentValue = 0;
            }
            ResetProgressForNewTarget();
            CanResetStats = false;
            StatsLabelText = "Estimasi File";
            AddLog("Statistik estimasi di-reset.");
        }

        private void ClearLog()
        {
            LogEntries.Clear();
        }

        private void OpenLogFile()
        {
            try
            {
                var logPath = _rapikanFileService.GetLastLogFilePath(TargetPath);
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
                    _notificationService?.ShowInfo("Informasi", "File log belum tersedia. Jalankan proses Rapikan File terlebih dahulu.");
                }
            }
            catch (Exception ex)
            {
                _notificationService?.ShowError("Error", $"Gagal membuka file log: {ex.Message}");
            }
        }

        private void AddLog(string message, string? colorHex = null)
        {
            Brush brush = Brushes.Gray;
            if (!string.IsNullOrEmpty(colorHex))
            {
                try
                {
                    brush = (Brush)new BrushConverter().ConvertFromString(colorHex)!;
                }
                catch { }
            }

            LogEntries.Add(new LogEntryModel
            {
                Message = $"[{DateTime.Now:HH:mm:ss}] {message}",
                Color = brush
            });

            while (LogEntries.Count > 300)
            {
                LogEntries.RemoveAt(0);
            }
        }

        private void StartTimer()
        {
            _startTime = DateTime.Now;
            _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _elapsedTimer.Tick += (s, e) =>
            {
                var elapsed = DateTime.Now - _startTime;
                ElapsedText = $"{elapsed:hh\\:mm\\:ss}";
            };
            _elapsedTimer.Start();
        }

        private void StopTimer()
        {
            if (_elapsedTimer != null)
            {
                _elapsedTimer.Stop();
                _elapsedTimer = null;
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
