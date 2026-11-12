using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Nexus.App.Services;
using Nexus.Contracts.AiCheck;
using Nexus.Contracts.ReferenceData;
using Nexus.Core.HomeSettings.Services;
using Nexus.Engine.ImageSubsystem.Ocr;
using Nexus.Engine.ImageSubsystem.Services;
using Nexus.Engine.ImageSubsystem.Triage;
using Nexus.Engine.ImageSubsystem.Evidence;

namespace Nexus.App.ViewModels;

public enum OverlayBoxStatus
{
    Safe,
    Review,
    Correction
}

public sealed class AiCheckOverlayBoxViewModel : ViewModelBase
{
    private static readonly SolidColorBrush SafeStrokeBrush = new(System.Windows.Media.Color.FromRgb(67, 160, 71));
    private static readonly SolidColorBrush ReviewStrokeBrush = new(System.Windows.Media.Color.FromRgb(251, 140, 0));
    private static readonly SolidColorBrush CorrectionStrokeBrush = new(System.Windows.Media.Color.FromRgb(229, 57, 53));

    private static readonly SolidColorBrush SafeFillBrush = new(System.Windows.Media.Color.FromArgb(0x18, 67, 160, 71));
    private static readonly SolidColorBrush ReviewFillBrush = new(System.Windows.Media.Color.FromArgb(0x22, 251, 140, 0));
    private static readonly SolidColorBrush CorrectionFillBrush = new(System.Windows.Media.Color.FromArgb(0x22, 229, 57, 53));

    private static readonly SolidColorBrush HighlightStrokeBrush = new(System.Windows.Media.Color.FromRgb(255, 213, 79)); // Amber Gold #FFD54F
    private static readonly SolidColorBrush HighlightFillBrush = new(System.Windows.Media.Color.FromArgb(0x40, 255, 213, 79));

    static AiCheckOverlayBoxViewModel()
    {
        SafeStrokeBrush.Freeze();
        ReviewStrokeBrush.Freeze();
        CorrectionStrokeBrush.Freeze();
        SafeFillBrush.Freeze();
        ReviewFillBrush.Freeze();
        CorrectionFillBrush.Freeze();
        HighlightStrokeBrush.Freeze();
        HighlightFillBrush.Freeze();
    }

    public int Id { get; init; }
    public int X1 { get; init; }
    public int Y1 { get; init; }
    public int X2 { get; init; }
    public int Y2 { get; init; }

    public int Width => Math.Max(0, X2 - X1);
    public int Height => Math.Max(0, Y2 - Y1);

    private bool _isHighlighted;
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (SetProperty(ref _isHighlighted, value))
            {
                OnPropertyChanged(nameof(StrokeBrush));
                OnPropertyChanged(nameof(FillBrush));
                OnPropertyChanged(nameof(BorderThickness));
            }
        }
    }

    public double BorderThickness => IsHighlighted ? 3.5 : 2.0;

    private OverlayBoxStatus _status = OverlayBoxStatus.Safe;
    public OverlayBoxStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StrokeBrushName));
                OnPropertyChanged(nameof(FillBrushName));
                OnPropertyChanged(nameof(StrokeBrush));
                OnPropertyChanged(nameof(FillBrush));
            }
        }
    }

    public string OriginalText { get; init; } = string.Empty;
    public string RecommendedText { get; set; } = string.Empty;
    public string TooltipText { get; set; } = string.Empty;

    public SolidColorBrush StrokeBrush => IsHighlighted ? HighlightStrokeBrush : (Status switch
    {
        OverlayBoxStatus.Correction => CorrectionStrokeBrush,
        OverlayBoxStatus.Review => ReviewStrokeBrush,
        _ => SafeStrokeBrush
    });

    public SolidColorBrush FillBrush => IsHighlighted ? HighlightFillBrush : (Status switch
    {
        OverlayBoxStatus.Correction => CorrectionFillBrush,
        OverlayBoxStatus.Review => ReviewFillBrush,
        _ => SafeFillBrush
    });

    public string StrokeBrushName => IsHighlighted ? "#FFD54F" : (Status switch
    {
        OverlayBoxStatus.Correction => "#E53935", // Red
        OverlayBoxStatus.Review => "#FB8C00",     // Orange / Amber
        _ => "#43A047"                           // Green
    });

    public string FillBrushName => IsHighlighted ? "#40FFD54F" : (Status switch
    {
        OverlayBoxStatus.Correction => "#22E53935",
        OverlayBoxStatus.Review => "#22FB8C00",
        _ => "#1543A047"
    });
}

public sealed class AiCheckFindingItemViewModel : ViewModelBase
{
    public string CandidateId { get; init; } = string.Empty;
    public int TokenIndex { get; init; }
    public int BoxId { get; init; }
    public string DisplayBoxNumber => $"Box {BoxId:D2}";
    public string OriginalText { get; init; } = string.Empty;

    private string _recommendedText = string.Empty;
    public string RecommendedText
    {
        get => _recommendedText;
        set => SetProperty(ref _recommendedText, value);
    }

    private AiCheckJudgmentStatus _status;
    public AiCheckJudgmentStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(Severity));
                OnPropertyChanged(nameof(SeverityText));
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(StatusBadgeBackground));
                OnPropertyChanged(nameof(StatusBadgeForeground));
            }
        }
    }

    private string _reason = string.Empty;
    public string Reason
    {
        get => _reason;
        set => SetProperty(ref _reason, value);
    }

    private long _durationMs;
    public long DurationMs
    {
        get => _durationMs;
        set => SetProperty(ref _durationMs, value);
    }

    private bool _isDismissed;
    public bool IsDismissed
    {
        get => _isDismissed;
        set => SetProperty(ref _isDismissed, value);
    }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    private bool _isCopied;
    public bool IsCopied
    {
        get => _isCopied;
        set => SetProperty(ref _isCopied, value);
    }

    private System.Windows.Threading.DispatcherTimer? _copyFeedbackTimer;

    public void ShowCopyFeedback()
    {
        IsCopied = true;
        try
        {
            if (_copyFeedbackTimer == null)
            {
                _copyFeedbackTimer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Normal)
                {
                    Interval = TimeSpan.FromMilliseconds(1500)
                };
                _copyFeedbackTimer.Tick += (_, _) =>
                {
                    _copyFeedbackTimer?.Stop();
                    IsCopied = false;
                };
            }
            _copyFeedbackTimer.Stop();
            _copyFeedbackTimer.Start();
        }
        catch
        {
            // Fallback for headless environments without dispatcher loop
        }
    }

    public Nexus.App.Controls.InfoBar.InfoBarSeverity Severity => Status switch
    {
        AiCheckJudgmentStatus.Koreksi => Nexus.App.Controls.InfoBar.InfoBarSeverity.Error,
        AiCheckJudgmentStatus.PerluVerifikasi => Nexus.App.Controls.InfoBar.InfoBarSeverity.Warning,
        AiCheckJudgmentStatus.OcrError => Nexus.App.Controls.InfoBar.InfoBarSeverity.Warning,
        _ => Nexus.App.Controls.InfoBar.InfoBarSeverity.Success
    };

    public string SeverityText => Status switch
    {
        AiCheckJudgmentStatus.Koreksi => "KOREKSI TYPO",
        AiCheckJudgmentStatus.PerluVerifikasi => "PERLU VERIFIKASI",
        AiCheckJudgmentStatus.OcrError => "SALAH BACA OCR",
        _ => "AMAN"
    };

    public string StatusBadgeText => Status switch
    {
        AiCheckJudgmentStatus.Koreksi => "KOREKSI DIREKOMENDASIKAN",
        AiCheckJudgmentStatus.PerluVerifikasi => "PERLU VERIFIKASI MANUAL",
        AiCheckJudgmentStatus.OcrError => "KEMUNGKINAN SALAH BACA OCR",
        _ => "AMAN"
    };

    public string StatusBadgeBackground => Status switch
    {
        AiCheckJudgmentStatus.Koreksi => "#33E53935",
        AiCheckJudgmentStatus.PerluVerifikasi => "#33FB8C00",
        AiCheckJudgmentStatus.OcrError => "#331E88E5",
        _ => "#3343A047"
    };

    public string StatusBadgeForeground => Status switch
    {
        AiCheckJudgmentStatus.Koreksi => "#FF5252",
        AiCheckJudgmentStatus.PerluVerifikasi => "#FFA726",
        AiCheckJudgmentStatus.OcrError => "#42A5F5",
        _ => "#66BB6A"
    };

    public void NotifyPropertiesChanged()
    {
        OnPropertyChanged(nameof(RecommendedText));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Reason));
        OnPropertyChanged(nameof(Severity));
        OnPropertyChanged(nameof(SeverityText));
        OnPropertyChanged(nameof(StatusBadgeText));
        OnPropertyChanged(nameof(StatusBadgeBackground));
        OnPropertyChanged(nameof(StatusBadgeForeground));
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(IsCopied));
    }
}

public class AiCheckViewModel : ViewModelBase, IDisposable
{
    private readonly IReferenceDataManager _referenceManager;
    private readonly IAiCheckJudgeService _judgeService;
    private readonly INotificationService? _notificationService;
    private readonly ISettingsManagerService? _settingsManagerService;
    private CancellationTokenSource? _activeCheck;

    private readonly PpOcrEngine? _ocrEngine;
    private readonly Func<PpOcrEngine>? _ocrEngineFactory;

    private string _uploadedFilePath = string.Empty;
    private string _fileName = string.Empty;
    private string _fileSizeFormatted = string.Empty;
    private ImageSource? _previewImage;
    private int _originalPixelWidth;
    private int _originalPixelHeight;

    private bool _isAnalyzing;
    private string _analysisStageText = string.Empty;
    private string _summaryText = string.Empty;
    private double _zoomLevel = 1.0;
    private bool _isAiCheckModeActive;

    public bool IsAiCheckModeActive
    {
        get => _isAiCheckModeActive;
        set => SetProperty(ref _isAiCheckModeActive, value);
    }

    private double _temperature = 0.70;
    public double Temperature
    {
        get => _temperature;
        set => SetProperty(ref _temperature, Math.Clamp(value, 0, 2));
    }

    private double _topP = 0.90;
    public double TopP
    {
        get => _topP;
        set => SetProperty(ref _topP, Math.Clamp(value, 0, 1));
    }

    private int _topK = 40;
    public int TopK
    {
        get => _topK;
        set => SetProperty(ref _topK, Math.Clamp(value, 1, 100));
    }

    private double _minP = 0.05;
    public double MinP
    {
        get => _minP;
        set => SetProperty(ref _minP, Math.Clamp(value, 0, 1));
    }

    private ReferenceDatasetMetadata? _selectedDataset;
    private string _activeReferenceSummaryText = "Referensi: Otomatis Aktif";
    private AiCheckFindingItemViewModel? _selectedFinding;

    public string ActiveReferenceSummaryText
    {
        get => _activeReferenceSummaryText;
        set => SetProperty(ref _activeReferenceSummaryText, value);
    }

    public ObservableCollection<ReferenceDatasetMetadata> AvailableDatasets { get; } = new();
    public ObservableCollection<AiCheckOverlayBoxViewModel> OverlayBoxes { get; } = new();
    public ObservableCollection<AiCheckFindingItemViewModel> Findings { get; } = new();

    private string _textContent = string.Empty;
    public string TextContent
    {
        get => _textContent;
        private set
        {
            if (SetProperty(ref _textContent, value))
            {
                OnPropertyChanged(nameof(HasTextContent));
                OnPropertyChanged(nameof(HasContent));
                OnPropertyChanged(nameof(HasNoContent));
                OnPropertyChanged(nameof(CanRunCheck));
            }
        }
    }

    public bool HasTextContent => !string.IsNullOrWhiteSpace(_textContent);

    public string UploadedFilePath
    {
        get => _uploadedFilePath;
        private set
        {
            if (SetProperty(ref _uploadedFilePath, value))
            {
                OnPropertyChanged(nameof(HasUploadedFile));
                OnPropertyChanged(nameof(HasNoUploadedFile));
                OnPropertyChanged(nameof(HasContent));
                OnPropertyChanged(nameof(HasNoContent));
                OnPropertyChanged(nameof(CanRunCheck));
            }
        }
    }

    public string FileName
    {
        get => _fileName;
        private set => SetProperty(ref _fileName, value);
    }

    public string FileSizeFormatted
    {
        get => _fileSizeFormatted;
        private set => SetProperty(ref _fileSizeFormatted, value);
    }

    public bool HasUploadedFile => !string.IsNullOrEmpty(_uploadedFilePath) && _previewImage != null;
    public bool HasNoUploadedFile => !HasUploadedFile;

    public bool HasContent => HasUploadedFile || HasTextContent;
    public bool HasNoContent => !HasContent;

    public ImageSource? PreviewImage
    {
        get => _previewImage;
        private set
        {
            if (SetProperty(ref _previewImage, value))
            {
                OnPropertyChanged(nameof(HasUploadedFile));
                OnPropertyChanged(nameof(HasNoUploadedFile));
                OnPropertyChanged(nameof(HasContent));
                OnPropertyChanged(nameof(HasNoContent));
                OnPropertyChanged(nameof(CanRunCheck));
            }
        }
    }

    public int OriginalPixelWidth
    {
        get => _originalPixelWidth;
        private set => SetProperty(ref _originalPixelWidth, value);
    }

    public int OriginalPixelHeight
    {
        get => _originalPixelHeight;
        private set => SetProperty(ref _originalPixelHeight, value);
    }

    private bool _isInspectionCompleted = false;
    public bool IsInspectionCompleted
    {
        get => _isInspectionCompleted;
        private set
        {
            if (SetProperty(ref _isInspectionCompleted, value))
            {
                OnPropertyChanged(nameof(IsResultsPanelVisible));
                OnPropertyChanged(nameof(HasNoFindings));
            }
        }
    }

    public bool IsResultsPanelVisible => _isInspectionCompleted && !_isAnalyzing;
    public bool HasNoFindings => _isInspectionCompleted && Findings.Count == 0;

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (SetProperty(ref _isAnalyzing, value))
            {
                OnPropertyChanged(nameof(CanRunCheck));
                OnPropertyChanged(nameof(IsResultsPanelVisible));
            }
        }
    }

    public bool CanRunCheck => HasContent && !IsAnalyzing;

    public string AnalysisStageText
    {
        get => _analysisStageText;
        private set => SetProperty(ref _analysisStageText, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            if (SetProperty(ref _zoomLevel, Math.Clamp(value, 0.1, 3.0)))
            {
                OnPropertyChanged(nameof(ZoomText));
            }
        }
    }

    public string ZoomText => $"{Math.Round(ZoomLevel * 100)}%";
    public string DimensionFormatted => OriginalPixelWidth > 0 ? $"{OriginalPixelWidth} × {OriginalPixelHeight} px" : string.Empty;

    public int TotalBoxCount => OverlayBoxes.Count;
    public int AttentionCount => Findings.Count;
    public int CorrectionCount => Findings.Count(f => f.Status == AiCheckJudgmentStatus.Koreksi);
    public int VerificationCount => Findings.Count(f => f.Status == AiCheckJudgmentStatus.PerluVerifikasi);
    public int OcrErrorCount => Findings.Count(f => f.Status == AiCheckJudgmentStatus.OcrError);

    // Snapshot statistics: stable inspection results (immutable when findings are dismissed)
    private int _snapshotBoxCount;
    private int _snapshotFindingCount;
    private int _snapshotCorrectionCount;
    private int _snapshotReviewCount;
    private int _snapshotOcrErrorCount;

    public int SnapshotBoxCount
    {
        get => _snapshotBoxCount;
        private set => SetProperty(ref _snapshotBoxCount, value);
    }

    public int SnapshotFindingCount
    {
        get => _snapshotFindingCount;
        private set => SetProperty(ref _snapshotFindingCount, value);
    }

    public int SnapshotCorrectionCount
    {
        get => _snapshotCorrectionCount;
        private set => SetProperty(ref _snapshotCorrectionCount, value);
    }

    public int SnapshotReviewCount
    {
        get => _snapshotReviewCount;
        private set => SetProperty(ref _snapshotReviewCount, value);
    }

    public int SnapshotOcrErrorCount
    {
        get => _snapshotOcrErrorCount;
        private set => SetProperty(ref _snapshotOcrErrorCount, value);
    }

    public void UpdateSummaryCounts()
    {
        OnPropertyChanged(nameof(TotalBoxCount));
        OnPropertyChanged(nameof(AttentionCount));
        OnPropertyChanged(nameof(CorrectionCount));
        OnPropertyChanged(nameof(VerificationCount));
        OnPropertyChanged(nameof(OcrErrorCount));
        OnPropertyChanged(nameof(HasNoFindings));

        string breakdown = OcrErrorCount > 0
            ? $"{CorrectionCount} Koreksi, {OcrErrorCount} Salah Baca OCR"
            : $"{CorrectionCount} Koreksi";

        SummaryText = TotalBoxCount > 0
            ? $"{TotalBoxCount} Box Diperiksa • {AttentionCount} Perlu Perhatian ({breakdown})"
            : (HasContent ? $"Materi siap dianalisis" : string.Empty);
    }

    public ReferenceDatasetMetadata? SelectedDataset
    {
        get => _selectedDataset;
        set => SetProperty(ref _selectedDataset, value);
    }

    public AiCheckFindingItemViewModel? SelectedFinding
    {
        get => _selectedFinding;
        set => SetProperty(ref _selectedFinding, value);
    }

    public ICommand SelectFileCommand { get; }
    public ICommand PasteContentCommand { get; }
    public ICommand RunCheckCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand CopyCorrectionCommand { get; }
    public ICommand DismissFindingCommand { get; }
    public ICommand JumpToFindingCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ResetZoomCommand { get; }
    public ICommand FitToCanvasCommand { get; }
    public ICommand ToggleProofZoomCommand { get; }
    public Action? RequestFitToCanvasAction { get; set; }
    public Action<int, int, int, int, int>? RequestFocusFindingAction { get; set; }

    public AiCheckViewModel(
        IReferenceDataManager referenceManager,
        IAiCheckJudgeService judgeService,
        INotificationService? notificationService = null,
        ISettingsManagerService? settingsManagerService = null,
        PpOcrEngine? ocrEngine = null,
        Func<PpOcrEngine>? ocrEngineFactory = null)
    {
        _referenceManager = referenceManager ?? throw new ArgumentNullException(nameof(referenceManager));
        _judgeService = judgeService ?? throw new ArgumentNullException(nameof(judgeService));
        _notificationService = notificationService;
        _settingsManagerService = settingsManagerService;
        _ocrEngine = ocrEngine;
        _ocrEngineFactory = ocrEngineFactory;

        SelectFileCommand = new AsyncRelayCommand(ExecuteSelectFileAsync);
        PasteContentCommand = new AsyncRelayCommand(ExecutePasteContentAsync);
        RunCheckCommand = new AsyncRelayCommand(ExecuteRunCheckAsync, () => CanRunCheck);
        ResetCommand = new RelayCommand(ExecuteReset);
        CopyCorrectionCommand = new RelayCommand<AiCheckFindingItemViewModel>(ExecuteCopyCorrection);
        DismissFindingCommand = new RelayCommand<AiCheckFindingItemViewModel>(ExecuteDismissFinding);
        JumpToFindingCommand = new RelayCommand<AiCheckFindingItemViewModel>(ExecuteJumpToFinding);

        ZoomInCommand = new RelayCommand(() => ZoomLevel = Math.Min(3.0, Math.Round(ZoomLevel + 0.15, 2)));
        ZoomOutCommand = new RelayCommand(() => ZoomLevel = Math.Max(0.1, Math.Round(ZoomLevel - 0.15, 2)));
        ResetZoomCommand = new RelayCommand(() => ZoomLevel = 1.0);
        FitToCanvasCommand = new RelayCommand(() => RequestFitToCanvasAction?.Invoke());
        ToggleProofZoomCommand = new RelayCommand(() =>
        {
            if (Math.Abs(ZoomLevel - 1.0) < 0.05)
            {
                RequestFitToCanvasAction?.Invoke();
            }
            else
            {
                ZoomLevel = 1.0;
            }
        });

        _ = LoadAvailableDatasetsAsync();
    }

    public async Task LoadAvailableDatasetsAsync()
    {
        try
        {
            var datasets = await _referenceManager.GetAllDatasetsMetadataAsync();
            AvailableDatasets.Clear();
            int totalEntries = 0;
            foreach (var d in datasets)
            {
                AvailableDatasets.Add(d);
                totalEntries += d.EntryCount;
            }

            if (datasets.Count > 0)
            {
                ActiveReferenceSummaryText = $"Referensi: Otomatis ({datasets.Count} Dataset • {totalEntries} Entri)";
                SelectedDataset = AvailableDatasets.First();
            }
            else
            {
                ActiveReferenceSummaryText = "Referensi: Otomatis Aktif";
            }
        }
        catch
        {
            // Abaikan kegagalan memuat referensi awal
        }
    }

    public async Task LoadFileDirectlyAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        try
        {
            var format = DesignImagePreprocessor.ValidateFile(filePath);
            var fi = new FileInfo(filePath);

            CancelActiveCheck();
            TextContent = string.Empty;
            UploadedFilePath = filePath;
            FileName = fi.Name;
            FileSizeFormatted = $"{fi.Length / (1024.0 * 1024.0):F1} MB ({format})";

            var (width, height, img) = await Task.Run(() =>
            {
                using var bmp = DesignImagePreprocessor.LoadAsBitmap(filePath);
                var src = ConvertBitmapToImageSource(bmp);
                return (bmp.Width, bmp.Height, src);
            });

            OriginalPixelWidth = width;
            OriginalPixelHeight = height;
            PreviewImage = img;
            ZoomLevel = 1.0;
            OnPropertyChanged(nameof(DimensionFormatted));

            OverlayBoxes.Clear();
            Findings.Clear();
            SnapshotBoxCount = 0;
            SnapshotFindingCount = 0;
            SnapshotCorrectionCount = 0;
            SnapshotReviewCount = 0;
            SnapshotOcrErrorCount = 0;
            IsInspectionCompleted = false;

            UpdateSummaryCounts();
            OnPropertyChanged(nameof(HasContent));
            OnPropertyChanged(nameof(HasNoContent));
            OnPropertyChanged(nameof(CanRunCheck));
            RequestFitToCanvasAction?.Invoke();
        }
        catch (Exception ex)
        {
            SummaryText = $"Gagal memuat berkas: {ex.Message}";
        }
    }

    private async Task ExecuteSelectFileAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Pilih Berkas Desain Cetak",
            Filter = "Semua Format Desain (*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.pdf)|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.pdf|" +
                     "JPEG Images (*.jpg;*.jpeg)|*.jpg;*.jpeg|" +
                     "PNG Images (*.png)|*.png|" +
                     "TIFF Images (*.tif;*.tiff)|*.tif;*.tiff|" +
                     "PDF Documents (*.pdf)|*.pdf"
        };

        if (dlg.ShowDialog() == true)
        {
            await LoadFileDirectlyAsync(dlg.FileName);
        }
    }

    private async Task ExecutePasteContentAsync()
    {
        try
        {
            if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                if (image != null)
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "Nexus", "AiCheck");
                    Directory.CreateDirectory(tempDir);
                    string tempPath = Path.Combine(tempDir, $"clipboard_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                    using (var fileStream = new FileStream(tempPath, FileMode.Create))
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(image));
                        encoder.Save(fileStream);
                    }

                    await LoadFileDirectlyAsync(tempPath);
                    FileName = "Tempelan Gambar (Clipboard)";
                    return;
                }
            }
            else if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    CancelActiveCheck();
                    TextContent = text;
                    UploadedFilePath = string.Empty;
                    FileName = "Tempelan Teks (Clipboard)";
                    FileSizeFormatted = $"{text.Length} Karakter";
                    PreviewImage = null;
                    OriginalPixelWidth = 0;
                    OriginalPixelHeight = 0;
                    OverlayBoxes.Clear();
                    Findings.Clear();
                    SnapshotBoxCount = 0;
                    SnapshotFindingCount = 0;
                    SnapshotCorrectionCount = 0;
                    SnapshotReviewCount = 0;
                    SnapshotOcrErrorCount = 0;
                    IsInspectionCompleted = false;
                    UpdateSummaryCounts();
                    return;
                }
            }

            _notificationService?.ShowWarning("Clipboard Kosong", "Tidak ditemukan teks atau gambar di clipboard.");
        }
        catch (Exception ex)
        {
            _notificationService?.ShowError("Gagal Menempel", $"Tidak dapat membaca clipboard: {ex.Message}");
        }
    }

    protected virtual Task<List<OcrBoxModel>> ReadOcrBoxesAsync(string filePath)
    {
        return ReadOcrBoxesCoreAsync(filePath, CancellationToken.None);
    }

    protected virtual Task<List<OcrBoxModel>> ReadOcrBoxesAsync(string filePath, CancellationToken ct)
    {
        var method = GetType().GetMethod(
            nameof(ReadOcrBoxesAsync),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            null,
            [typeof(string)],
            null);
        if (method != null && method.DeclaringType != typeof(AiCheckViewModel))
        {
            return ReadOcrBoxesAsync(filePath);
        }
        return ReadOcrBoxesCoreAsync(filePath, ct);
    }

    private Task<List<OcrBoxModel>> ReadOcrBoxesCoreAsync(string filePath, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var bmp = DesignImagePreprocessor.LoadAsBitmap(filePath);
            ct.ThrowIfCancellationRequested();

            var engine = _ocrEngine ?? _ocrEngineFactory?.Invoke();
            bool ownsTransientEngine = false;
            if (engine == null)
            {
                string? modelDirectory = _settingsManagerService == null
                    ? null
                    : Path.Combine(_settingsManagerService.ModelRootFolder, "AI Workspace", "PP-OCRv6");
                engine = new PpOcrEngine(modelDirectory);
                ownsTransientEngine = true;
            }

            try
            {
                return engine.RunPipeline(bmp, maxSide: 1536, ct: ct);
            }
            finally
            {
                if (ownsTransientEngine)
                {
                    engine.Dispose();
                }
            }
        }, ct);
    }

    private async Task ExecuteRunCheckAsync()
    {
        if (!CanRunCheck) return;

        if (!HasUploadedFile && HasTextContent)
        {
            _notificationService?.ShowWarning("Fitur Teks", "Pemeriksaan teks murni sedang dalam tahap pengembangan. Silakan unggah berkas desain/gambar.");
            return;
        }

        using var operation = new CancellationTokenSource();
        var ct = operation.Token;
        _activeCheck = operation;
        var filePath = UploadedFilePath;
        var fileName = FileName;

        IsInspectionCompleted = false;
        IsAnalyzing = true;
        Findings.Clear();
        OverlayBoxes.Clear();
        var swOverall = Stopwatch.StartNew();

        try
        {
            // ==========================================
            // TAHAP 1: Perception (PP-OCRv6 ONNX)
            // ==========================================
            AnalysisStageText = "Tahap 1/3: Membaca Teks & Posisi Desain (PP-OCRv6)...";

            var ocrBoxes = await ReadOcrBoxesAsync(filePath, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            // ==========================================
            // TAHAP 2: Evidence Engine (Radar & Deduplikasi)
            // ==========================================
            AnalysisStageText = "Tahap 2/3: Menganalisis Bukti Leksikal, Konteks Spasial & Deduplikasi...";

            // Tampilkan seluruh kotak awal di kanvas sebagai Safe (normal)
            foreach (var b in ocrBoxes)
            {
                var overlayBox = new AiCheckOverlayBoxViewModel
                {
                    Id = b.Id,
                    X1 = b.X1,
                    Y1 = b.Y1,
                    X2 = b.X2,
                    Y2 = b.Y2,
                    OriginalText = b.Text,
                    Status = OverlayBoxStatus.Safe,
                    TooltipText = $"[Box {b.Id:D2}] {b.Text} (Det: {b.DetScore:F2}, Conf: {b.RecConfidence:F2})"
                };
                OverlayBoxes.Add(overlayBox);
            }
            UpdateSummaryCounts();

            // Muat 3 Lapisan Referensi Data ke Evidence Engine:
            // Layer 1: Base Language Lexicon 20K kata bawaan sistem (read-only)
            var baseLexicon = _referenceManager.GetBaseLanguageLexicon();

            // Layer 2: User Dictionary (flat list kata tambahan pengguna)
            List<string>? userDictEntries = null;
            try
            {
                var userDict = await _referenceManager.GetDatasetAsync(Nexus.Contracts.ReferenceData.ReferenceConstants.UserDictionaryId, ct).ConfigureAwait(true);
                ct.ThrowIfCancellationRequested();
                if (userDict?.Entries != null && userDict.Entries.Count > 0)
                {
                    userDictEntries = userDict.Entries.Select(e => e.Value).ToList();
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine($"[AiCheck] Peringatan memuat user dictionary: {ex.Message}");
            }

            // Layer 2.5: Domain Lexicon (istilah perdagangan & kuliner dari dataset deklaratif)
            List<string>? domainEntries = null;
            try
            {
                var domainDs = await _referenceManager.GetDatasetAsync(Nexus.Contracts.ReferenceData.ReferenceConstants.DomainFoodRetailId, ct).ConfigureAwait(true);
                ct.ThrowIfCancellationRequested();
                if (domainDs?.Entries != null && domainDs.Entries.Count > 0)
                {
                    domainEntries = domainDs.Entries.Select(e => e.Value).ToList();
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine($"[AiCheck] Peringatan memuat domain lexicon: {ex.Message}");
            }

            // Layer 3: Structured Reference Entities (seluruh dataset referensi selain UserDictionary dan DomainLexicon)
            var structuredPhrases = new List<StructuredReferencePhrase>();
            try
            {
                var allDatasets = await _referenceManager.GetAllDatasetsMetadataAsync(ct).ConfigureAwait(true);
                ct.ThrowIfCancellationRequested();
                foreach (var meta in allDatasets)
                {
                    bool isUserDict = string.Equals(meta.Id, Nexus.Contracts.ReferenceData.ReferenceConstants.UserDictionaryId, StringComparison.OrdinalIgnoreCase);
                    bool isDomainLexicon = string.Equals(meta.Id, Nexus.Contracts.ReferenceData.ReferenceConstants.DomainFoodRetailId, StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(meta.Category, Nexus.Contracts.ReferenceData.ReferenceConstants.DomainFoodRetailCategory, StringComparison.OrdinalIgnoreCase);
                    if (isDomainLexicon) continue; // Dimuat terpisah via LoadDomainLexicon

                    bool isEntityRef = string.Equals(meta.Id, Nexus.Contracts.ReferenceData.ReferenceConstants.EntityReferenceId, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(meta.Category, Nexus.Contracts.ReferenceData.ReferenceConstants.EntityReferenceCategory, StringComparison.OrdinalIgnoreCase);
                    if (isEntityRef) continue; // Dimuat terpisah via LoadEntityReferences

                    var ds = await _referenceManager.GetDatasetAsync(meta.Id, ct).ConfigureAwait(true);
                    ct.ThrowIfCancellationRequested();
                    if (ds?.Entries != null && ds.Entries.Count > 0)
                    {
                        foreach (var entry in ds.Entries)
                        {
                            if (string.IsNullOrWhiteSpace(entry.Value)) continue;

                            // Untuk User Dictionary, masukkan entri frasa multi-kata (>= 2 kata) ke structured phrases
                            // agar terlindungi oleh Exact Phrase Detection dan Near-Match Alignment
                            if (isUserDict)
                            {
                                var tokCount = entry.Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                                if (tokCount < 2) continue;
                            }

                            structuredPhrases.Add(new StructuredReferencePhrase
                            {
                                Text = entry.Value,
                                DatasetId = meta.Id,
                                Category = meta.Category ?? (isUserDict ? "UserDictionary" : string.Empty)
                            });
                        }
                    }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine($"[AiCheck] Peringatan memuat structured references: {ex.Message}");
            }

            // Layer 4: Entity References (entitas resmi lintas 6 kategori: Bank, Operator, E-Wallet, dll)
            var entityItems = new List<EntityReferenceItem>();
            try
            {
                var entityDs = await _referenceManager.GetDatasetAsync(Nexus.Contracts.ReferenceData.ReferenceConstants.EntityReferenceId, ct).ConfigureAwait(true);
                ct.ThrowIfCancellationRequested();
                if (entityDs?.Entries != null && entityDs.Entries.Count > 0)
                {
                    foreach (var entry in entityDs.Entries)
                    {
                        if (string.IsNullOrWhiteSpace(entry.Value)) continue;
                        entityItems.Add(new EntityReferenceItem
                        {
                            Id = entry.Id,
                            Name = entry.Value,
                            Category = entry.Category ?? string.Empty,
                            Aliases = entry.Aliases != null ? new List<string>(entry.Aliases) : new List<string>(),
                            AmbiguousAliases = entry.AmbiguousAliases != null ? new List<string>(entry.AmbiguousAliases) : new List<string>(),
                            Ambiguity = entry.Ambiguity,
                            Active = entry.Active
                        });
                    }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine($"[AiCheck] Peringatan memuat entity references: {ex.Message}");
            }

            // Eksekusi CPU-bound (Loading leksikon & kalkulasi multi-evidence) di dalam Task.Run sinkron murni
            var (evalResults, suspectCandidates) = await Task.Run(() =>
            {
                // Derived indexes belong to this run; only the manager supplies mutable references.
                var evidenceEngine = new EvidenceEngine();
                evidenceEngine.LoadLocations(Nexus.Contracts.ReferenceData.ReferenceConstants.BuiltInAdministrativeKeywords);
                if (baseLexicon != null && baseLexicon.Count > 0)
                {
                    evidenceEngine.LoadBaseLexicon(baseLexicon);
                }
                if (userDictEntries != null && userDictEntries.Count > 0)
                {
                    evidenceEngine.LoadUserDictionary(userDictEntries);
                }
                if (domainEntries != null && domainEntries.Count > 0)
                {
                    evidenceEngine.LoadDomainLexicon(domainEntries);
                }
                if (structuredPhrases.Count > 0)
                {
                    evidenceEngine.LoadStructuredReferences(structuredPhrases);
                }
                if (entityItems.Count > 0)
                {
                    evidenceEngine.LoadEntityReferences(entityItems);
                }

                ct.ThrowIfCancellationRequested();
                var results = evidenceEngine.ProcessBoxes(ocrBoxes);
                var suspects = results.Where(r => r.Action == PipelineAction.SUSPECT).ToList();
                return (results, suspects);
            }, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            // EARLY EXIT: Jika tidak ditemukan kandidat SUSPECT sama sekali
            if (suspectCandidates.Count == 0)
            {
                AnalysisStageText = "Pemeriksaan selesai: Tidak ditemukan anomali teks (Early Exit).";
                swOverall.Stop();
                UpdateSummaryCounts();
                SnapshotBoxCount = OverlayBoxes.Count;
                SnapshotFindingCount = 0;
                SnapshotCorrectionCount = 0;
                SnapshotReviewCount = 0;
                SnapshotOcrErrorCount = 0;
                IsInspectionCompleted = true;
                return;
            }

            // Tandai kandidat awal di kanvas (Review) dan siapkan daftar temuan awal
            foreach (var s in suspectCandidates)
            {
                foreach (var boxId in s.BoxIds)
                {
                    var matchedBox = OverlayBoxes.FirstOrDefault(b => b.Id == boxId);
                    if (matchedBox != null)
                    {
                        matchedBox.Status = OverlayBoxStatus.Review;
                    }
                }

                Findings.Add(new AiCheckFindingItemViewModel
                {
                    CandidateId = s.CandidateId,
                    TokenIndex = s.TokenIndex,
                    BoxId = s.BoxIds.FirstOrDefault(),
                    OriginalText = s.RawToken,
                    RecommendedText = s.PossibleCorrections.FirstOrDefault()?.Word ?? s.RawToken,
                    Status = AiCheckJudgmentStatus.PerluVerifikasi,
                    Reason = NormalizeReason(s.Notes, AiCheckJudgmentStatus.PerluVerifikasi)
                });
            }
            UpdateSummaryCounts();

            // ==========================================
            // TAHAP 3: AI Judge (Gemma 4 E4B - SUSPECT Only)
            // ==========================================
            string docContext = $"{fileName} - Referensi Otomatis";
            var candidateDtos = suspectCandidates.Select(s => new TriageCandidateDto
            {
                CandidateId = s.CandidateId,
                TokenIndex = s.TokenIndex,
                BoxId = s.PrimaryBoxId,
                BoxIds = new List<int>(s.BoxIds),
                OriginalText = s.RawToken,
                PossibleCorrections = new List<string>(s.PossibleCorrectionWords),
                SpatialContext = s.SpatialContext,
                BoxText = s.BoxText,
                EvidenceDescription = s.Notes,
                HasStrongEvidence = s.HasStrongEvidence,
                IsReferenceNearMatch = s.IsReferenceNearMatch,
                LevenshteinDistance = s.LevenshteinDistance,
                Action = s.ActionName,
                DetScore = s.DetScore,
                RecConfidence = s.MinRecConfidence,
                PrimaryScore = s.Top1PrimaryScore,
                FuzzyMargin = s.FuzzyMargin,
                HasFuzzyRunnerUp = s.HasFuzzyRunnerUp,
                TrueFuzzyMargin = s.TrueFuzzyMargin,
                G3LowMargin = s.G3LowMargin,
                Top1Suggestion = s.Top1Suggestion,
                Top1Distance = s.Top1Distance,
                MatchedReferenceContext = s.MatchedReferenceContext,
                IsNameSignal = s.IsNameSignal,
                HasCharacterAnomaly = s.HasCharacterAnomaly,
                HasEntitySignal = s.HasEntitySignal,
                IsStrongPersonContext = s.IsStrongPersonContext,
                IsMixedCase = s.IsMixedCase
            }).ToList();

            int totalCandidates = candidateDtos.Count;
            AnalysisStageText = $"Tahap 3/3: Memverifikasi {totalCandidates} kandidat dengan AI Judge...";

            var judgments = await _judgeService.EvaluateFindingsAsync(docContext, candidateDtos, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            // ==========================================
            // TAHAP 4: Compile & Mapping Layer ke seluruh BoxIds (Batched Update)
            // Menggunakan pemetaan deterministik CandidateId anti-timpa
            // ==========================================
            var overlayMap = OverlayBoxes.ToDictionary(b => b.Id);
            var findingMap = Findings.Where(f => !string.IsNullOrEmpty(f.CandidateId)).ToDictionary(f => f.CandidateId);
            var candidateMap = candidateDtos.Where(c => !string.IsNullOrEmpty(c.CandidateId)).ToDictionary(c => c.CandidateId);
            var findingsToRemove = new List<AiCheckFindingItemViewModel>();
            var boxJudgments = new Dictionary<int, List<AiCheckJudgment>>();

            foreach (var j in judgments)
            {
                var targetBoxIds = j.BoxIds.Count > 0 ? j.BoxIds : new List<int> { j.BoxId };
                foreach (var boxId in targetBoxIds)
                {
                    if (!boxJudgments.TryGetValue(boxId, out var list))
                    {
                        list = new List<AiCheckJudgment>();
                        boxJudgments[boxId] = list;
                    }
                    list.Add(j);
                }

                AiCheckFindingItemViewModel? existingFinding = null;
                if (!string.IsNullOrEmpty(j.CandidateId) && findingMap.TryGetValue(j.CandidateId, out var found))
                {
                    existingFinding = found;
                }
                else if (j.BoxId > 0)
                {
                    existingFinding = Findings.FirstOrDefault(f => f.BoxId == j.BoxId);
                }

                TriageCandidateDto? candidate = null;
                if (!string.IsNullOrEmpty(j.CandidateId))
                {
                    candidateMap.TryGetValue(j.CandidateId, out candidate);
                }
                if (candidate == null && j.BoxId > 0)
                {
                    candidate = candidateDtos.FirstOrDefault(c => c.BoxId == j.BoxId);
                }

                if (j.IsTypo)
                {
                    // TYPO: Tampilkan di panel temuan dan tandai sebagai Koreksi
                    if (existingFinding != null)
                    {
                        existingFinding.RecommendedText = j.RecommendedText;
                        existingFinding.Status = AiCheckJudgmentStatus.Koreksi;
                        existingFinding.Reason = BuildFindingReason(j, candidate);
                        existingFinding.DurationMs = j.DurationMs;
                        existingFinding.NotifyPropertiesChanged();
                    }
                }
                else if (j.IsOcrError)
                {
                    // Salah Baca OCR (Font/Kerapatan Spasi) - Tampil di daftar temuan sebagai info terpisah
                    if (existingFinding != null)
                    {
                        existingFinding.RecommendedText = j.RecommendedText;
                        existingFinding.Status = AiCheckJudgmentStatus.OcrError;
                        existingFinding.Reason = BuildFindingReason(j, candidate);
                        existingFinding.DurationMs = j.DurationMs;
                        existingFinding.NotifyPropertiesChanged();
                    }
                }
                else if (j.IsUnresolved)
                {
                    // UNRESOLVED: Tidak dijawab model / timeout teknis / malformed.
                    // INVARIANT: Unresolved -> REVIEW, RecommendedText = string.Empty, NEVER Safe!
                    if (existingFinding != null)
                    {
                        existingFinding.RecommendedText = string.Empty;
                        existingFinding.Status = AiCheckJudgmentStatus.PerluVerifikasi;
                        existingFinding.Reason = BuildFindingReason(j, candidate);
                        existingFinding.DurationMs = j.DurationMs;
                        existingFinding.NotifyPropertiesChanged();
                    }
                }
                else if (j.Status == AiCheckJudgmentStatus.PerluVerifikasi)
                {
                    // UNCERTAIN: Tetap tampilkan sebagai review
                    if (existingFinding != null)
                    {
                        existingFinding.RecommendedText = j.RecommendedText;
                        existingFinding.Status = AiCheckJudgmentStatus.PerluVerifikasi;
                        existingFinding.Reason = BuildFindingReason(j, candidate);
                        existingFinding.DurationMs = j.DurationMs;
                        existingFinding.NotifyPropertiesChanged();
                    }
                }
                else
                {
                    // VALID / AMAN: Kumpulkan untuk dihapus secara batch dari panel temuan
                    if (existingFinding != null)
                    {
                        findingsToRemove.Add(existingFinding);
                    }
                }
            }

            // Hapus temuan valid secara batch agar tidak memicu re-layout beruntun
            foreach (var toRemove in findingsToRemove.Distinct())
            {
                Findings.Remove(toRemove);
            }

            // AGGREGASI VISUAL STATUS KANVAS PER BOX:
            // Mengikuti aturan prioritas konvergen: TYPO (Correction/Merah) > REVIEW (Review/Kuning) > SAFE (Safe/Hijau)
            // Menggunakan fungsi tunggal ApplyOverlayBoxStatus agar sinkron dengan aksi Abaikan
            foreach (var overlay in OverlayBoxes)
            {
                ApplyOverlayBoxStatus(overlay.Id);
            }

            if (ct.IsCancellationRequested || !ReferenceEquals(_activeCheck, operation))
            {
                if (ReferenceEquals(_activeCheck, operation))
                {
                    OverlayBoxes.Clear();
                    Findings.Clear();
                    IsInspectionCompleted = false;
                    UpdateSummaryCounts();
                }
                return;
            }

            swOverall.Stop();
            UpdateSummaryCounts();
            SnapshotBoxCount = OverlayBoxes.Count;
            SnapshotFindingCount = Findings.Count;
            SnapshotCorrectionCount = Findings.Count(f => f.Status == AiCheckJudgmentStatus.Koreksi);
            SnapshotReviewCount = Findings.Count(f => f.Status == AiCheckJudgmentStatus.PerluVerifikasi);
            SnapshotOcrErrorCount = Findings.Count(f => f.Status == AiCheckJudgmentStatus.OcrError);
            IsInspectionCompleted = true;
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // Obsolete operations must not publish results or alter state of newer runs.
            if (ReferenceEquals(_activeCheck, operation))
            {
                OverlayBoxes.Clear();
                Findings.Clear();
                IsInspectionCompleted = false;
                UpdateSummaryCounts();
            }
        }
        catch (Exception ex)
        {
            IsInspectionCompleted = false;
            SummaryText = $"Pemeriksaan gagal: {ex.Message}";
            _notificationService?.ShowError("Pemeriksaan Gagal", ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_activeCheck, operation))
            {
                _activeCheck = null;
                IsAnalyzing = false;
                AnalysisStageText = string.Empty;
            }
        }
    }

    internal void CancelActiveCheck()
    {
        var operation = _activeCheck;
        if (operation == null) return;
        _activeCheck = null;
        operation.Cancel();
        IsAnalyzing = false;
        AnalysisStageText = string.Empty;
        OverlayBoxes.Clear();
        Findings.Clear();
        IsInspectionCompleted = false;
        UpdateSummaryCounts();
    }

    private void ExecuteReset()
    {
        CancelActiveCheck();
        UploadedFilePath = string.Empty;
        FileName = string.Empty;
        FileSizeFormatted = string.Empty;
        PreviewImage = null;
        OriginalPixelWidth = 0;
        OriginalPixelHeight = 0;
        OverlayBoxes.Clear();
        Findings.Clear();
        TextContent = string.Empty;
        SnapshotBoxCount = 0;
        SnapshotFindingCount = 0;
        SnapshotCorrectionCount = 0;
        SnapshotReviewCount = 0;
        SnapshotOcrErrorCount = 0;
        IsInspectionCompleted = false;
        ZoomLevel = 1.0;
        OnPropertyChanged(nameof(DimensionFormatted));
        UpdateSummaryCounts();
    }

    private void ExecuteCopyCorrection(AiCheckFindingItemViewModel? finding)
    {
        if (finding == null || string.IsNullOrEmpty(finding.RecommendedText))
        {
            return;
        }

        if (NexusClipboard.SetText(finding.RecommendedText))
        {
            finding.ShowCopyFeedback();
        }
        else
        {
            _notificationService?.ShowError("Gagal Menyalin", "Tidak dapat mengakses clipboard. Silakan coba lagi.");
        }
    }

    private void ExecuteDismissFinding(AiCheckFindingItemViewModel? finding)
    {
        if (finding != null)
        {
            finding.IsDismissed = true;
            Findings.Remove(finding);
            ApplyOverlayBoxStatus(finding.BoxId);
            UpdateSummaryCounts();
        }
    }

    private void ExecuteJumpToFinding(AiCheckFindingItemViewModel? finding)
    {
        if (finding == null) return;
        SelectedFinding = finding;

        bool targetExpanded = !finding.IsExpanded;
        foreach (var item in Findings)
        {
            if (!ReferenceEquals(item, finding))
            {
                item.IsExpanded = false;
            }
        }
        finding.IsExpanded = targetExpanded;

        var targetBox = OverlayBoxes.FirstOrDefault(b => b.Id == finding.BoxId);
        if (targetBox != null)
        {
            RequestFocusFindingAction?.Invoke(targetBox.Id, targetBox.X1, targetBox.Y1, targetBox.X2, targetBox.Y2);
        }
    }

    private void ApplyOverlayBoxStatus(int boxId)
    {
        var matchedOverlay = OverlayBoxes.FirstOrDefault(b => b.Id == boxId);
        if (matchedOverlay == null) return;

        var boxFindings = Findings.Where(f => f.BoxId == boxId && !f.IsDismissed).ToList();
        var typo = boxFindings.FirstOrDefault(f => f.Status == AiCheckJudgmentStatus.Koreksi);
        if (typo != null)
        {
            matchedOverlay.RecommendedText = typo.RecommendedText;
            matchedOverlay.Status = OverlayBoxStatus.Correction;
            matchedOverlay.TooltipText = $"[Box {boxId:D2}] {matchedOverlay.OriginalText} -> {typo.RecommendedText} ({typo.Reason})";
            return;
        }

        var ocr = boxFindings.FirstOrDefault(f => f.Status == AiCheckJudgmentStatus.OcrError);
        if (ocr != null)
        {
            matchedOverlay.RecommendedText = ocr.RecommendedText;
            matchedOverlay.Status = OverlayBoxStatus.Review;
            matchedOverlay.TooltipText = $"[Box {boxId:D2}] Kemungkinan salah baca OCR: '{matchedOverlay.OriginalText}' -> '{ocr.RecommendedText}' ({ocr.Reason})";
            return;
        }

        var rev = boxFindings.FirstOrDefault(f => f.Status == AiCheckJudgmentStatus.PerluVerifikasi);
        if (rev != null)
        {
            matchedOverlay.RecommendedText = rev.RecommendedText;
            matchedOverlay.Status = OverlayBoxStatus.Review;
            matchedOverlay.TooltipText = $"[Box {boxId:D2}] {matchedOverlay.OriginalText} (Perlu verifikasi manual: {rev.Reason})";
            return;
        }

        matchedOverlay.RecommendedText = string.Empty;
        matchedOverlay.Status = OverlayBoxStatus.Safe;
        matchedOverlay.TooltipText = $"[Box {boxId:D2}] {matchedOverlay.OriginalText} (Aman)";
    }

    internal static string BuildFindingReason(AiCheckJudgment judgment, TriageCandidateDto? candidate)
    {
        string original = !string.IsNullOrWhiteSpace(judgment.OriginalText)
            ? judgment.OriginalText
            : candidate?.OriginalText ?? string.Empty;

        if (string.IsNullOrWhiteSpace(original))
        {
            original = "Teks";
        }

        string correction = !string.IsNullOrWhiteSpace(judgment.RecommendedText)
            ? judgment.RecommendedText
            : candidate?.Top1Suggestion ?? string.Empty;

        // 1. Kasus Unresolved / Timeout
        if (judgment.IsUnresolved)
        {
            return "Pemeriksaan belum memperoleh cukup informasi untuk menentukan koreksi; teks perlu diverifikasi secara manual.";
        }

        // 2. Kasus OCR Error
        if (judgment.IsOcrError)
        {
            return $"\"{original}\" terindikasi sebagai hasil pembacaan teks yang keliru; bentuk pada desain perlu diperiksa kembali secara visual.";
        }

        // Analisis perbedaan karakter antara original dan correction
        var diff = AnalyzeDiff(original, correction);

        // 3. Kasus Koreksi / Typo
        if (judgment.IsTypo)
        {
            // 3A. Reference / Entity Evidence
            string? reference = !string.IsNullOrWhiteSpace(candidate?.MatchedReferenceContext)
                ? candidate.MatchedReferenceContext
                : (candidate?.HasEntitySignal == true && !string.IsNullOrWhiteSpace(candidate?.BoxText) ? candidate.BoxText : null);

            if (!string.IsNullOrWhiteSpace(reference))
            {
                string categoryLabel = IsLocationOrRegion(reference, candidate?.EvidenceDescription)
                    ? "nama wilayah"
                    : "istilah yang menjadi acuan";

                string diffDesc = diff.Kind switch
                {
                    DiffKind.SingleSubstitution => diff.DifferingToken != null
                        ? $"terdapat perbedaan satu huruf pada \"{diff.DifferingToken}\""
                        : "berbeda satu huruf",
                    DiffKind.SingleDeletion => diff.DifferingToken != null
                        ? $"terdapat satu huruf yang hilang pada \"{diff.DifferingToken}\""
                        : "terdapat satu huruf yang hilang",
                    DiffKind.SingleInsertion => diff.DifferingToken != null
                        ? $"terdapat satu huruf tambahan pada \"{diff.DifferingToken}\""
                        : "terdapat satu huruf tambahan",
                    DiffKind.SpacingDifference => "terdapat perbedaan pemisahan spasi",
                    _ => string.Empty
                };

                if (!string.IsNullOrEmpty(diffDesc))
                {
                    return $"\"{original}\" kemungkinan salah penulisan dari {categoryLabel} \"{reference}\"; {diffDesc}, sehingga bentuk yang disarankan adalah \"{correction}\".";
                }

                return $"\"{original}\" kemungkinan salah penulisan dari {categoryLabel} \"{reference}\"; bentuk yang disarankan adalah \"{correction}\".";
            }

            // 3B. Dictionary / Lexical Evidence
            bool isSupportedByDictionary = false;
            if (!string.IsNullOrWhiteSpace(correction))
            {
                if (candidate?.PossibleCorrections != null &&
                    candidate.PossibleCorrections.Any(w => string.Equals(w, correction, StringComparison.OrdinalIgnoreCase)))
                {
                    isSupportedByDictionary = true;
                }
                else if (!string.IsNullOrWhiteSpace(candidate?.Top1Suggestion) &&
                         string.Equals(candidate.Top1Suggestion, correction, StringComparison.OrdinalIgnoreCase))
                {
                    isSupportedByDictionary = true;
                }
            }

            if (isSupportedByDictionary)
            {
                string diffPhrase = diff.Kind switch
                {
                    DiffKind.SingleSubstitution => diff.DifferingToken != null
                        ? $"berbeda satu huruf pada \"{diff.DifferingToken}\" dari \"{correction}\" yang menjadi acuan"
                        : $"berbeda satu huruf dari \"{correction}\" yang menjadi acuan",
                    DiffKind.SingleInsertion => diff.DifferingToken != null
                        ? $"terdapat satu huruf tambahan pada \"{diff.DifferingToken}\" dibandingkan \"{correction}\" yang menjadi acuan"
                        : $"terdapat satu huruf tambahan dibandingkan \"{correction}\" yang menjadi acuan",
                    DiffKind.SingleDeletion => diff.DifferingToken != null
                        ? $"terdapat satu huruf yang hilang pada \"{diff.DifferingToken}\" dibandingkan \"{correction}\" yang menjadi acuan"
                        : $"terdapat satu huruf yang hilang dibandingkan \"{correction}\" yang menjadi acuan",
                    DiffKind.SpacingDifference => $"memerlukan pemisahan spasi dibandingkan \"{correction}\" yang menjadi acuan",
                    _ => $"mendekati \"{correction}\" yang menjadi acuan ejaan"
                };

                string judgeConfirm = diff.Kind == DiffKind.MultipleChanges
                    ? $"AI Judge mengonfirmasi bahwa bentuk yang disarankan adalah \"{correction}\""
                    : $"AI Judge mengonfirmasi bahwa bentuk yang benar adalah \"{correction}\"";

                return $"\"{original}\" terindikasi salah eja karena {diffPhrase}; {judgeConfirm}.";
            }

            // 3C. Judge correction tanpa evidence tambahan
            if (!string.IsNullOrWhiteSpace(correction))
            {
                return $"\"{original}\" dinilai sebagai kemungkinan salah eja dan AI Judge menyarankan \"{correction}\" sebagai bentuk perbaikannya.";
            }

            return $"\"{original}\" dinilai sebagai kemungkinan salah eja dan memerlukan perbaikan.";
        }

        // 4. Kasus Perlu Verifikasi / Ambigu
        if (judgment.Status == AiCheckJudgmentStatus.PerluVerifikasi)
        {
            if (candidate != null && candidate.IsNameSignal)
            {
                return $"\"{original}\" perlu diverifikasi karena terindikasi sebagai kemungkinan nama atau istilah khusus yang tidak dapat dipastikan secara otomatis.";
            }

            return $"\"{original}\" perlu diverifikasi karena sistem belum memiliki cukup bukti untuk memastikan bentuk ejaannya.";
        }

        // 5. Fallback Default
        return "Ejaan telah diperiksa.";
    }

    private enum DiffKind
    {
        None,
        SingleSubstitution,
        SingleDeletion,
        SingleInsertion,
        SpacingDifference,
        MultipleChanges
    }

    private sealed class DiffResult
    {
        public DiffKind Kind { get; set; } = DiffKind.None;
        public string? DifferingToken { get; set; }
    }

    private static DiffResult AnalyzeDiff(string original, string recommended)
    {
        var result = new DiffResult();

        if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(recommended))
        {
            result.Kind = DiffKind.None;
            return result;
        }

        if (string.Equals(original, recommended, StringComparison.OrdinalIgnoreCase))
        {
            result.Kind = DiffKind.None;
            return result;
        }

        // Periksa apakah hanya perbedaan pemisahan spasi
        string origNoSpaces = original.Replace(" ", "");
        string recNoSpaces = recommended.Replace(" ", "");
        if (string.Equals(origNoSpaces, recNoSpaces, StringComparison.OrdinalIgnoreCase))
        {
            result.Kind = DiffKind.SpacingDifference;
            return result;
        }

        var origTokens = original.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var recTokens = recommended.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (origTokens.Length == recTokens.Length)
        {
            int diffCount = 0;
            int diffIdx = -1;
            for (int i = 0; i < origTokens.Length; i++)
            {
                if (!string.Equals(origTokens[i], recTokens[i], StringComparison.OrdinalIgnoreCase))
                {
                    diffCount++;
                    diffIdx = i;
                }
            }

            if (diffCount == 1 && diffIdx >= 0)
            {
                string tOrig = origTokens[diffIdx];
                string tRec = recTokens[diffIdx];
                string? tokenContext = origTokens.Length > 1 ? tOrig : null;
                result.DifferingToken = tokenContext;

                // 1. Cek substitusi 1 karakter (panjang sama)
                if (tOrig.Length == tRec.Length)
                {
                    int charDiffs = 0;
                    for (int c = 0; c < tOrig.Length; c++)
                    {
                        if (char.ToUpperInvariant(tOrig[c]) != char.ToUpperInvariant(tRec[c]))
                        {
                            charDiffs++;
                        }
                    }

                    if (charDiffs == 1)
                    {
                        result.Kind = DiffKind.SingleSubstitution;
                        return result;
                    }
                }
                // 2. Cek 1 karakter hilang di original (tOrig.Length + 1 == tRec.Length)
                else if (tOrig.Length + 1 == tRec.Length && IsOneCharMissing(tOrig, tRec))
                {
                    result.Kind = DiffKind.SingleDeletion;
                    return result;
                }
                // 3. Cek 1 karakter tambahan di original (tOrig.Length - 1 == tRec.Length)
                else if (tOrig.Length - 1 == tRec.Length && IsOneCharMissing(tRec, tOrig))
                {
                    result.Kind = DiffKind.SingleInsertion;
                    return result;
                }

                result.Kind = DiffKind.MultipleChanges;
                return result;
            }

            result.Kind = DiffKind.MultipleChanges;
            return result;
        }

        result.Kind = DiffKind.MultipleChanges;
        return result;
    }

    private static bool IsOneCharMissing(string shorter, string longer)
    {
        if (shorter.Length + 1 != longer.Length) return false;
        int i = 0, j = 0;
        bool foundDiff = false;
        while (i < shorter.Length && j < longer.Length)
        {
            if (char.ToUpperInvariant(shorter[i]) != char.ToUpperInvariant(longer[j]))
            {
                if (foundDiff) return false;
                foundDiff = true;
                j++;
            }
            else
            {
                i++;
                j++;
            }
        }
        return true;
    }

    private static bool IsLocationOrRegion(string? text, string? evidenceDesc)
    {
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(evidenceDesc))
            return false;

        string combined = $"{text} {evidenceDesc}".ToUpperInvariant();
        string[] locationKeywords =
        {
            "DESA", "KELURAHAN", "KECAMATAN", "KABUPATEN", "KOTA",
            "PROVINSI", "DUSUN", "KAMPUNG", "WILAYAH", "KAB.", "KEC.", "KEL.",
            "TIMUR", "BARAT", "SELATAN", "UTARA", "TENGAH"
        };

        foreach (var kw in locationKeywords)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(combined, $@"\b{System.Text.RegularExpressions.Regex.Escape(kw)}\b"))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeReason(string? rawReason, AiCheckJudgmentStatus status)
    {
        if (string.IsNullOrWhiteSpace(rawReason))
        {
            return status switch
            {
                AiCheckJudgmentStatus.Koreksi => "Ejaan perlu diperbaiki.",
                AiCheckJudgmentStatus.OcrError => "Kemungkinan artefak pembacaan OCR.",
                _ => "Konteks belum cukup untuk memastikan ejaan."
            };
        }

        var trimmed = rawReason.Trim();

        // Saring istilah teknis internal/debug engine
        if (trimmed.Contains("Pass 2 Targeted Hint", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("fuzzy anomaly", StringComparison.OrdinalIgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"\bd\s*=\s*\d+\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return status switch
            {
                AiCheckJudgmentStatus.Koreksi => "Ejaan berbeda dari data referensi.",
                AiCheckJudgmentStatus.OcrError => "Kemungkinan artefak pembacaan OCR.",
                _ => "Konteks belum cukup untuk memastikan ejaan."
            };
        }

        return trimmed;
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    private static BitmapSource ConvertBitmapToImageSource(Bitmap bmp)
    {
        var hBitmap = bmp.GetHbitmap();
        try
        {
            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _activeCheck?.Dispose();
            _activeCheck = null;
        }
    }
}
