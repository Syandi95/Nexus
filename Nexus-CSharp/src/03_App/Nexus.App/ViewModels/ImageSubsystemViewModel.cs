using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nexus.Contracts;
using Nexus.App.Services;
using Nexus.Core.HomeSettings.Services;
using Nexus.Engine.ColorManagement;
using Nexus.Engine.ImageSubsystem.Ocr;
using Nexus.Engine.ImageSubsystem.Pipelines;
using Nexus.Engine.ImageSubsystem.SavePipeline;
using Nexus.Engine.ImageSubsystem.Services;

namespace Nexus.App.ViewModels
{
    public enum ImageBatchItemState
    {
        Queued,
        Processing,
        Completed,
        Failed,
        Cancelled
    }

    public class ImageBatchItem : ViewModelBase
    {
        private ImageBatchItemState _state = ImageBatchItemState.Queued;
        private string _filePath = string.Empty;
        private string _name = string.Empty;
        private string _sizeText = string.Empty;
        private string _statusText = "Dalam antrean";
        private Brush _statusForeground = Brushes.Gray;
        private BitmapSource? _thumbnailImage;
        private BitmapSource? _resultImage;
        private string? _resultPath;
        private string _errorMessage = string.Empty;

        public string FilePath { get => _filePath; set => SetProperty(ref _filePath, value); }
        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public string SizeText { get => _sizeText; set => SetProperty(ref _sizeText, value); }
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
        public Brush StatusForeground { get => _statusForeground; set => SetProperty(ref _statusForeground, value); }
        public BitmapSource? ThumbnailImage { get => _thumbnailImage; set => SetProperty(ref _thumbnailImage, value); }
        public BitmapSource? ResultImage { get => _resultImage; set => SetProperty(ref _resultImage, value); }
        public string? ResultPath { get => _resultPath; set => SetProperty(ref _resultPath, value); }
        public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
        private bool _isActive;
        public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

        public ImageBatchItemState State
        {
            get => _state;
            set
            {
                if (SetProperty(ref _state, value))
                {
                    OnPropertyChanged(nameof(IsQueued));
                    OnPropertyChanged(nameof(IsProcessing));
                    OnPropertyChanged(nameof(IsCompleted));
                    OnPropertyChanged(nameof(IsFailed));
                    OnPropertyChanged(nameof(IsCancelled));
                    OnPropertyChanged(nameof(CanRemove));
                    OnPropertyChanged(nameof(DisplayImage));
                }
            }
        }

        public bool IsQueued => State == ImageBatchItemState.Queued;
        public bool IsProcessing => State == ImageBatchItemState.Processing;
        public bool IsCompleted => State == ImageBatchItemState.Completed;
        public bool IsFailed => State == ImageBatchItemState.Failed;
        public bool IsCancelled => State == ImageBatchItemState.Cancelled;
        public bool CanRemove => State == ImageBatchItemState.Queued;

        public ImageSource? DisplayImage => ResultImage ?? (ImageSource?)ThumbnailImage;
    }

    public class ImageSubsystemViewModel : ViewModelBase, IModuleLifecycle, IDisposable
    {
        private readonly IImageSubsystemWorkflow _imageWorkflow;
        private readonly IGenericImageEngine _genericImageEngine;
        private readonly ICmykProfileManager _cmykProfileManager;
        private readonly Func<IColorManagementService> _colorManagementFactory;
        private readonly IMobileSamService _mobileSamService;
        private readonly string _modelsRoot;
        private readonly string _slotsFilePath;

        private readonly ObservableCollection<ImageBatchItem> _batchItems = new();

        private string _selectedMode = "RemoveBG";
        private bool _isImageLoaded;
        private string _inputImagePath = string.Empty;
        private string _outputImagePath = string.Empty;
        private BitmapSource? _resultImageSource;
        private byte[]? _baselineRemoveBgAlpha;
        private byte[]? _activeRemoveBgAlpha;
        private int _removeBgWidth;
        private int _removeBgHeight;
        private System.Drawing.Bitmap? _currentResultBmp;
        private readonly Stack<AlphaMaskPatch> _removeBgUndoStack = new();
        private readonly Stack<AlphaMaskPatch> _removeBgRedoStack = new();
        private bool _isRemoveBgRefining;
        private bool _isRemoveBgAddMode = true;
        private bool _isRemoveBgProcessing;
        private bool _isUpscaleProcessing;
        private bool _isInpaintProcessing;
        private string _fileInfoText = "Belum ada gambar";
        private double _zoomLevel = 100.0;
        private double _compareRatio = 0.0;
        private BitmapSource? _removeBgRefineOverlaySource;
        private byte[]? _removeBgRefineBgra;
        private bool _hasResult;
        private int _selectedPreviewBackgroundIndex = 0;
        private int _selectedQualityIndex = 1;
        private double _upscaleScale = 4.0;
        private string _outputPath = string.Empty;
        private bool _userSetOutputPath;
        private string _statusText = "Pilih satu gambar untuk memulai.";
        private string _detailText = "File asli tidak akan diubah.";
        private double _progressValue = 0.0;
        private bool _isProcessing;
        private bool _isModeSwitchOverlayVisible;
        private string _pendingMode = string.Empty;
        private CancellationTokenSource? _cts;

        public ObservableCollection<ImageBatchItem> BatchItems => _batchItems;
        public bool IsBatchMode => !IsInpaintMode && _batchItems.Count > 1;
        public bool IsSingleImageMode => IsImageLoaded && !IsBatchMode;
        public bool HasNoImages => !IsImageLoaded && _batchItems.Count == 0;
        public bool CanAddBatchImage => !IsInpaintMode && _batchItems.Count < 10 && !IsProcessing;
        public string BatchCountText => $"{_batchItems.Count} / 10 gambar";

        public string DropZoneTitle => IsInpaintMode
            ? "Tarik dan lepas satu gambar untuk dihapus objeknya"
            : "Tarik dan lepas satu atau beberapa gambar di sini";

        public string DropZoneSubtitle => IsInpaintMode
            ? "Mendukung format PNG, JPG, JPEG, atau WebP (1 gambar per proses)"
            : "Maksimal 10 gambar (PNG, JPG, JPEG, atau WebP)";

        // Process / Save separation
        private string _workflowState = "Empty"; // Empty | ImageLoaded | Processing | ResultUnsaved | Saved | Error
        private string? _resultTempPath;
        private string? _lastSavedPath;
        private int _saveTargetScale;
        private string _saveModelDir = string.Empty;

        // Processing timer
        private readonly Stopwatch _processStopwatch = new();
        private DispatcherTimer? _processTimer;
        private string _processElapsedText = string.Empty;

        private string _slotLitePath = "PhotosSeg";
        private string _slotBalancedPath = "BiRefNet_lite";
        private string _slotHighQualityPath = "RMBG-2.0";

        private readonly string _inpaintModelsRoot;
        private readonly string _inpaintSlotsFilePath;
        private readonly ImageApplicationModelSelectionPolicy? _modelSelectionPolicy;
        private string _selectedInpaintModel = "LaMa";
        private readonly ObservableCollection<string> _availableInpaintModels = new();

        private IImageToolModule? _currentModule;
        private ImageOutputFormat? _selectedFormat;
        private bool _isRgbColorSpace = true;
        private bool _isCmykColorSpace;
        private string _activeCmykProfileText = string.Empty;
        private readonly INotificationService? _notificationService;

        // Orientation & Crop Entry state
        private double _rotationAngle;
        private double _scaleX = 1.0;
        private double _scaleY = 1.0;
        private bool _isCropActive;

        // Filmstrip state
        private readonly ObservableCollection<ImageBatchItem> _siblingImages = new();
        private bool _isFilmstripVisible;
        private string _currentFilmstripDirectory = string.Empty;
        public string CurrentFilmstripDirectory => _currentFilmstripDirectory;
        private int _filmstripLoadSequence;

        // OCR state (Single Engine: OneOCR)
        private readonly object _oneOcrLock = new();
        private OneOcrEngine? _oneOcrEngine;
        private readonly bool _ownsOneOcrEngine = true;
        private OneOcrDocument? _cachedOcrDocument;
        private string? _cachedOcrSource;
        private DateTime _cachedOcrLastWriteTimeUtc;
        private long _cachedOcrFileLength;
        private readonly ObservableCollection<OneOcrWordViewModel> _ocrWords = new();
        public ObservableCollection<OneOcrWordViewModel> OcrWords => _ocrWords;
        public bool HasOcrWords => _ocrWords.Count > 0;
        public bool HasSelectedOcrWords => _ocrWords.Any(w => w.IsSelected);

        private readonly ObservableCollection<AiCheckOverlayBoxViewModel> _overlayBoxes = new();
        public ObservableCollection<AiCheckOverlayBoxViewModel> OverlayBoxes => _overlayBoxes;
        public bool HasOverlayBoxes => _ocrWords.Count > 0 || _overlayBoxes.Count > 0;
        public bool HasSelectedOcrBoxes => _ocrWords.Any(w => w.IsSelected) || _overlayBoxes.Any(b => b.IsHighlighted);
        private Geometry? _ocrMaskGeometry;
        public Geometry? OcrMaskGeometry
        {
            get => _ocrMaskGeometry;
            private set => SetProperty(ref _ocrMaskGeometry, value);
        }

        private Geometry? _ocrSelectionGeometry;
        public Geometry? OcrSelectionGeometry
        {
            get => _ocrSelectionGeometry;
            private set => SetProperty(ref _ocrSelectionGeometry, value);
        }

        // Derivative tracking
        private string _originalSourcePath = string.Empty;
        public string OriginalSourcePath => _originalSourcePath;
        private string? _currentBeforeCropPath;
        public string? CurrentBeforeCropPath => _currentBeforeCropPath;
        private string? _cropDerivativePath;
        public string? CropDerivativePath => _cropDerivativePath;
        private readonly List<string> _tempCropFiles = new();

        public ImageSubsystemViewModel(
            IImageSubsystemWorkflow imageWorkflow,
            IGenericImageEngine genericImageEngine,
            ICmykProfileManager cmykProfileManager,
            Func<IColorManagementService> colorManagementFactory,
            ISettingsManagerService settingsManagerService,
            IMobileSamService? mobileSamService = null,
            INotificationService? notificationService = null,
            ImageApplicationModelSelectionPolicy? modelSelectionPolicy = null,
            OneOcrEngine? oneOcrEngine = null)
        {
            _imageWorkflow = imageWorkflow ?? throw new ArgumentNullException(nameof(imageWorkflow));
            _genericImageEngine = genericImageEngine ?? throw new ArgumentNullException(nameof(genericImageEngine));
            _cmykProfileManager = cmykProfileManager ?? throw new ArgumentNullException(nameof(cmykProfileManager));
            _colorManagementFactory = colorManagementFactory ?? throw new ArgumentNullException(nameof(colorManagementFactory));
            var settingsManager = settingsManagerService ?? throw new ArgumentNullException(nameof(settingsManagerService));
            _mobileSamService = mobileSamService ?? new MobileSamService(settingsManager.ModelRootFolder);
            _notificationService = notificationService;
            _modelSelectionPolicy = modelSelectionPolicy;
            if (oneOcrEngine != null)
            {
                _oneOcrEngine = oneOcrEngine;
                _ownsOneOcrEngine = false;
            }

            _modelsRoot = Path.Combine(settingsManager.ModelRootFolder, "RemoveBackground");
            _slotsFilePath = Path.Combine(_modelsRoot, "slots_mapping.json");
            _inpaintModelsRoot = Path.Combine(settingsManager.ModelRootFolder, "Inpaint");
            _inpaintSlotsFilePath = Path.Combine(_inpaintModelsRoot, "slots_mapping.json");

            SwitchMode(SelectedMode);

            SelectModeCommand = new RelayCommand<string>(ExecuteSelectMode);
            SelectImageCommand = new RelayCommand(ExecuteSelectImage);
            ClearImageCommand = new RelayCommand(async () => await ExecuteClearImageAsync(), () => IsImageLoaded && !IsProcessing);
            ExecutePrimaryActionCommand = new RelayCommand(async () => await ExecutePrimaryActionAsync(), () => IsImageLoaded && _workflowState != "Empty");
            CancelCommand = new RelayCommand(ExecuteCancel, () => _workflowState == "Processing");
            BrowseOutputFolderCommand = new RelayCommand(ExecuteBrowseOutputFolder);
            OpenOutputFolderCommand = new RelayCommand(ExecuteOpenOutputFolder, () => !string.IsNullOrEmpty(_lastSavedPath));
            ZoomInCommand = new RelayCommand(() => ZoomLevel = GetNextZoomIn(ZoomLevel));
            ZoomOutCommand = new RelayCommand(() => ZoomLevel = GetNextZoomOut(ZoomLevel));
            ToggleCompareCommand = new RelayCommand(() => { if (HasResult) CompareRatio = 0.5; });
            ConfirmModeSwitchCommand = new RelayCommand(async () => await ExecuteConfirmModeSwitchAsync());
            CancelModeSwitchCommand = new RelayCommand(() => IsModeSwitchOverlayVisible = false);
            BrowseSlotLiteCommand = new RelayCommand(async () => await ExecuteBrowseSlotAsync("Lite"));
            BrowseSlotBalancedCommand = new RelayCommand(async () => await ExecuteBrowseSlotAsync("Balanced"));
            BrowseSlotHighQualityCommand = new RelayCommand(async () => await ExecuteBrowseSlotAsync("High Quality"));
            BrowseInpaintModelCommand = new RelayCommand(async () => await ExecuteBrowseInpaintModelAsync());
            RemoveBatchItemCommand = new RelayCommand<ImageBatchItem>(async item => await ExecuteRemoveBatchItemAsync(item), item => !IsProcessing && item != null && item.CanRemove);
            AddMoreBatchImagesCommand = new RelayCommand(ExecuteSelectImage, () => CanAddBatchImage);
            ClearAllBatchCommand = new RelayCommand(async () => await ExecuteClearImageAsync(), () => !IsProcessing);

            UndoMaskCommand = new RelayCommand(() => UndoMaskAction?.Invoke());
            RedoMaskCommand = new RelayCommand(() => RedoMaskAction?.Invoke());
            ClearMaskCommand = new RelayCommand(() => ClearMaskAction?.Invoke());
            SetBrushModeCommand = new RelayCommand(() =>
            {
                IsEraserMode = false;
                IsAutoSelectMode = false;
            });
            SetEraserModeCommand = new RelayCommand(() =>
            {
                IsEraserMode = true;
                IsAutoSelectMode = false;
            });
            SetAutoSelectModeCommand = new RelayCommand(() =>
            {
                IsAutoSelectMode = true;
                IsEraserMode = false;
            }, () => IsAutoSelectAvailable);

            ToggleRemoveBgRefiningCommand = new RelayCommand(() => IsRemoveBgRefining = !IsRemoveBgRefining, () => IsRemoveBgMode && HasResult);
            SetRemoveBgAddModeCommand = new RelayCommand(() => IsRemoveBgAddMode = true);
            SetRemoveBgRemoveModeCommand = new RelayCommand(() => IsRemoveBgAddMode = false);
            UndoRemoveBgRefinementCommand = new RelayCommand(UndoRemoveBgRefinement, () => CanUndoRemoveBgRefinement);
            RedoRemoveBgRefinementCommand = new RelayCommand(RedoRemoveBgRefinement, () => CanRedoRemoveBgRefinement);
            ResetRemoveBgRefinementCommand = new RelayCommand(ResetRemoveBgRefinement, () => CanResetRemoveBgRefinement);

            ToggleCropCommand = new RelayCommand(() =>
            {
                IsCropActive = !IsCropActive;
                if (IsCropActive)
                {
                    IsRemoveBgRefining = false;
                }
            }, () => IsImageLoaded);

            RotateLeftCommand = new RelayCommand(() => RotationAngle = (RotationAngle - 90 + 360) % 360, () => IsImageLoaded);
            RotateRightCommand = new RelayCommand(() => RotationAngle = (RotationAngle + 90) % 360, () => IsImageLoaded);
            FlipHorizontalCommand = new RelayCommand(() => ScaleX = -ScaleX, () => IsImageLoaded);
            FlipVerticalCommand = new RelayCommand(() => ScaleY = -ScaleY, () => IsImageLoaded);
            ResetTransformCommand = new RelayCommand(ExecuteResetTransform, () => IsImageLoaded);

            CopyTextOcrCommand = new RelayCommand(async () => await ExecuteCopyTextOcrAsync(), () => IsImageLoaded);
            ToggleOcrBoxSelectionCommand = new RelayCommand<AiCheckOverlayBoxViewModel>(ExecuteToggleOcrBoxSelection);
            CopySelectedOcrTextCommand = new RelayCommand(ExecuteCopySelectedOcrText, () => HasSelectedOcrBoxes);
            CopyAllOcrTextCommand = new RelayCommand(ExecuteCopyAllOcrText, () => HasOverlayBoxes);
            CloseOcrCommand = new RelayCommand(ExecuteCloseOcr, () => HasOverlayBoxes);
            ToggleFilmstripCommand = new RelayCommand(ExecuteToggleFilmstrip, () => IsImageLoaded);
            SelectSiblingImageCommand = new RelayCommand<ImageBatchItem>(ExecuteSelectSiblingImage);
            NavigatePreviousImageCommand = new RelayCommand(ExecuteNavigatePreviousImage, CanNavigatePreviousImage);
            NavigateNextImageCommand = new RelayCommand(ExecuteNavigateNextImage, CanNavigateNextImage);

            LoadModelSlots();
            RefreshAvailableInpaintModels();
        }

        public Task OnNavigatedToAsync() => Task.CompletedTask;

        public async Task OnNavigatedFromAsync()
        {
            _cts?.Cancel();
            ClearMobileSamSession();
            await _genericImageEngine.UnloadAllModelsAsync();
        }

        private void SwitchMode(string mode)
        {
            _currentModule = mode switch
            {
                "RemoveBG" => new Nexus.Engine.ImageSubsystem.Modules.RemoveBackgroundModule(),
                "Upscale" => new Nexus.Engine.ImageSubsystem.Modules.UpscaleModule(),
                "Inpaint" => new Nexus.Engine.ImageSubsystem.Modules.InpaintModule(),
                _ => _currentModule
            };
            _selectedFormat = mode == "Inpaint" ? ImageOutputFormat.Png : null;
            IsRgbColorSpace = true;
            ValidateSelectedFormat();

            if (mode == "Inpaint")
            {
                _batchItems.Clear();
                if (IsImageLoaded)
                {
                    TriggerMobileSamPrecompute();
                }
            }

            if (!IsImageLoaded)
            {
                StatusText = mode == "Inpaint"
                    ? "Pilih satu gambar untuk memulai penghapusan objek."
                    : "Pilih satu atau beberapa gambar untuk memulai.";
            }

            OnPropertyChanged(nameof(AvailableOutputFormats));
            OnPropertyChanged(nameof(OutputFormat));
            OnPropertyChanged(nameof(PrimaryActionButtonText));
            OnPropertyChanged(nameof(PrimaryActionIcon));
            OnPropertyChanged(nameof(PrimaryActionToolTip));
            OnPropertyChanged(nameof(IsInpaintCanvasVisible));
            OnPropertyChanged(nameof(IsRemoveBgCanvasVisible));
            OnPropertyChanged(nameof(IsMaskCanvasVisible));
            OnPropertyChanged(nameof(DropZoneTitle));
            OnPropertyChanged(nameof(DropZoneSubtitle));
            OnPropertyChanged(nameof(IsBatchMode));
            OnPropertyChanged(nameof(IsSingleImageMode));
            OnPropertyChanged(nameof(HasNoImages));
            OnPropertyChanged(nameof(CanAddBatchImage));
            OnPropertyChanged(nameof(BatchCountText));
        }

        private OutputConfig CurrentOutputConfig
        {
            get
            {
                var colorSpace = _isCmykColorSpace
                    ? BuildCmykColorSpace()
                    : OutputColorSpace.Rgb;
                return _currentModule?.BuildOutput(_selectedFormat, colorSpace) ?? new OutputConfig();
            }
        }

        private OutputColorSpace BuildCmykColorSpace()
        {
            var profile = _cmykProfileManager.GetDefaultProfile();
            return profile != null && File.Exists(profile.FilePath)
                ? OutputColorSpace.Cmyk(profile.FilePath)
                : OutputColorSpace.Rgb;
        }

        private void LoadModelSlots()
        {
            try
            {
                if (File.Exists(_slotsFilePath))
                {
                    var json = File.ReadAllText(_slotsFilePath);
                    var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null)
                    {
                        if (dict.TryGetValue("Lite", out var lite) && !string.IsNullOrWhiteSpace(lite)) SlotLitePath = lite;
                        if (dict.TryGetValue("Balanced", out var balanced) && !string.IsNullOrWhiteSpace(balanced)) SlotBalancedPath = balanced;
                        if (dict.TryGetValue("High Quality", out var hq) && !string.IsNullOrWhiteSpace(hq)) SlotHighQualityPath = hq;
                    }
                }
            }
            catch { }
        }

        private void SaveModelSlots()
        {
            try
            {
                var dict = new Dictionary<string, string>
                {
                    ["Lite"] = SlotLitePath,
                    ["Balanced"] = SlotBalancedPath,
                    ["High Quality"] = SlotHighQualityPath
                };
                var dir = Path.GetDirectoryName(_slotsFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = System.Text.Json.JsonSerializer.Serialize(dict, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_slotsFilePath, json);
            }
            catch { }
        }

        private static BitmapImage? LoadSafeBitmap(string? path, int decodePixelWidth = 0)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                if (decodePixelWidth > 0)
                {
                    bitmap.DecodePixelWidth = decodePixelWidth;
                }
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ImageSubsystem] Failed to load bitmap for '{path}': {ex.Message}");
                return null;
            }
        }

        private static BitmapSource? LoadThumbnailBitmap(string? path, int decodePixelWidth = 360)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Trace.WriteLine($"[ImageSubsystem] LoadThumbnailBitmap skipped: path is null or file does not exist: '{path}'");
                return null;
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.StreamSource = stream;
                if (decodePixelWidth > 0)
                {
                    bitmap.DecodePixelWidth = decodePixelWidth;
                }
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ImageSubsystem] Standard thumbnail decode failed for '{path}', attempting fallback: {ex.Message}");
                // Fallback for CMYK or non-WIC formats: use GDI+ to decode to sRGB and convert to BitmapSource
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var gdiImg = System.Drawing.Image.FromStream(stream);
                    int origW = gdiImg.Width;
                    int origH = gdiImg.Height;
                    int targetW = decodePixelWidth > 0 && decodePixelWidth < origW ? decodePixelWidth : origW;
                    int targetH = Math.Max(1, (int)Math.Round((double)origH * targetW / origW));
                    using var rgbBmp = new System.Drawing.Bitmap(targetW, targetH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (var g = System.Drawing.Graphics.FromImage(rgbBmp))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(gdiImg, 0, 0, targetW, targetH);
                    }
                    var data = rgbBmp.LockBits(
                        new System.Drawing.Rectangle(0, 0, targetW, targetH),
                        System.Drawing.Imaging.ImageLockMode.ReadOnly,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try
                    {
                        var wpfBitmap = BitmapSource.Create(
                            targetW, targetH, 96, 96,
                            PixelFormats.Bgra32, null,
                            data.Scan0, data.Stride * targetH, data.Stride);
                        wpfBitmap.Freeze();
                        return wpfBitmap;
                    }
                    finally
                    {
                        rgbBmp.UnlockBits(data);
                    }
                }
                catch (Exception fallbackEx)
                {
                    Trace.WriteLine($"[ImageSubsystem] Fallback thumbnail decode failed for '{path}': {fallbackEx.Message}");
                    return null;
                }
            }
        }

        public string SelectedMode
        {
            get => _selectedMode;
            set
            {
                if (SetProperty(ref _selectedMode, value))
                {
                    SwitchMode(value);
                    OnPropertyChanged(nameof(IsRemoveBgMode));
                    OnPropertyChanged(nameof(IsGenericProcessingOverlayVisible));
                    OnPropertyChanged(nameof(IsUpscaleMode));
                    OnPropertyChanged(nameof(IsInpaintMode));
                    OnPropertyChanged(nameof(ActiveScannerMode));
                    OnPropertyChanged(nameof(PrimaryActionButtonText));
                    OnPropertyChanged(nameof(PrimaryActionIcon));
                    OnPropertyChanged(nameof(PrimaryActionToolTip));
                    OnPropertyChanged(nameof(DropZoneTitle));
                    OnPropertyChanged(nameof(DropZoneSubtitle));
                    OnPropertyChanged(nameof(IsBatchMode));
                    OnPropertyChanged(nameof(IsSingleImageMode));
                    OnPropertyChanged(nameof(HasNoImages));
                    OnPropertyChanged(nameof(CanAddBatchImage));
                }
            }
        }

        public bool IsRemoveBgMode => SelectedMode == "RemoveBG";
        public bool IsUpscaleMode => SelectedMode == "Upscale";
        public bool IsInpaintMode => SelectedMode == "Inpaint";
        public Controls.ImageScannerMode ActiveScannerMode =>
            IsUpscaleMode ? Controls.ImageScannerMode.Upscale :
            IsInpaintMode ? Controls.ImageScannerMode.ObjectRemoval :
            Controls.ImageScannerMode.RemoveBackground;
        public bool IsInpaintCanvasVisible => IsInpaintMode && !HasResult;

        private bool _isHighDetailMode = true;
        public bool IsHighDetailMode
        {
            get => _isHighDetailMode;
            set => SetProperty(ref _isHighDetailMode, value);
        }

        private double _brushSize = 35.0;
        public double BrushSize
        {
            get => _brushSize;
            set => SetProperty(ref _brushSize, value);
        }

        private double _brushSoftness = 0.25;
        public double BrushSoftness
        {
            get => _brushSoftness;
            set => SetProperty(ref _brushSoftness, value);
        }

        private bool _isEraserMode = false;
        private bool _isAutoSelectMode = false;

        public bool IsEraserMode
        {
            get => _isEraserMode;
            set
            {
                if (SetProperty(ref _isEraserMode, value))
                {
                    if (value)
                    {
                        _isAutoSelectMode = false;
                        OnPropertyChanged(nameof(IsAutoSelectMode));
                    }
                    OnPropertyChanged(nameof(IsBrushMode));
                }
            }
        }

        public bool IsAutoSelectMode
        {
            get => _isAutoSelectMode;
            set
            {
                if (SetProperty(ref _isAutoSelectMode, value))
                {
                    if (value)
                    {
                        _isEraserMode = false;
                        OnPropertyChanged(nameof(IsEraserMode));
                    }
                    OnPropertyChanged(nameof(IsBrushMode));
                }
            }
        }

        public bool IsBrushMode => !_isEraserMode && !_isAutoSelectMode;
        public bool IsAutoSelectAvailable => _mobileSamService.IsModelAvailable;

        private bool _isAutoSelectEncoding;
        private bool _hasAutoMask;
        private BitmapSource? _autoMaskOverlaySource;
        private byte[]? _accumulatedAutoMask;
        private int _autoMaskWidth;
        private int _autoMaskHeight;
        private MobileSamEmbeddingSession? _currentSamSession;
        private string? _samSessionImagePath;
        private CancellationTokenSource? _samEncodeCts;
        private Task<MobileSamEmbeddingSession?>? _samEncodeTask;
        private string? _samEncodeImagePath;
        private readonly object _samSessionLock = new();

        private readonly Stack<byte[]?> _autoMaskUndoStack = new();
        private readonly Stack<byte[]?> _autoMaskRedoStack = new();

        public bool IsAutoSelectEncoding
        {
            get => _isAutoSelectEncoding;
            private set => SetProperty(ref _isAutoSelectEncoding, value);
        }

        public bool HasAutoMask
        {
            get => _hasAutoMask;
            private set
            {
                if (SetProperty(ref _hasAutoMask, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public BitmapSource? AutoMaskOverlaySource
        {
            get => _autoMaskOverlaySource;
            private set => SetProperty(ref _autoMaskOverlaySource, value);
        }

        public byte[]? GetAccumulatedAutoMask() => _accumulatedAutoMask;
        public (int Width, int Height) GetAutoMaskDimensions() => (_autoMaskWidth, _autoMaskHeight);

        private bool _hasMaskStrokes = false;
        public bool HasMaskStrokes
        {
            get => _hasMaskStrokes;
            set
            {
                if (SetProperty(ref _hasMaskStrokes, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public Func<System.Drawing.Bitmap?>? MaskProvider { get; set; }
        public Func<Task<System.Drawing.Bitmap?>>? MaskProviderAsync { get; set; }
        public Action? UndoMaskAction { get; set; }
        public Action? RedoMaskAction { get; set; }
        public Action? ClearMaskAction { get; set; }

        public bool IsImageLoaded
        {
            get => _isImageLoaded;
            set
            {
                if (SetProperty(ref _isImageLoaded, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private BitmapSource? _beforeImageSource;
        public BitmapSource? BeforeImageSource
        {
            get => _beforeImageSource;
            private set => SetProperty(ref _beforeImageSource, value);
        }

        private int _originalImageWidth;
        public int OriginalImageWidth
        {
            get => _originalImageWidth;
            private set => SetProperty(ref _originalImageWidth, value);
        }

        private int _originalImageHeight;
        public int OriginalImageHeight
        {
            get => _originalImageHeight;
            private set => SetProperty(ref _originalImageHeight, value);
        }

        public string InputImagePath
        {
            get => _inputImagePath;
            set
            {
                if (SetProperty(ref _inputImagePath, value))
                {
                    ClearMobileSamSession();
                    BeforeImageSource = LoadSafeBitmap(value);
                    InvalidateOcrCache();
                }
            }
        }

        public string OutputImagePath
        {
            get => _outputImagePath;
            set
            {
                if (SetProperty(ref _outputImagePath, value))
                {
                    ResultImageSource = LoadSafeBitmap(value);
                }
            }
        }

        public BitmapSource? ResultImageSource
        {
            get => _resultImageSource;
            set => SetProperty(ref _resultImageSource, value);
        }

        public bool IsRemoveBgRefining
        {
            get => _isRemoveBgRefining;
            set
            {
                if (SetProperty(ref _isRemoveBgRefining, value))
                {
                    if (value)
                    {
                        IsCropActive = false;
                        UpdateRemoveBgRefineOverlay(null);
                    }
                    else
                    {
                        RemoveBgRefineOverlaySource = null;
                        _removeBgRefineBgra = null;
                        if (_currentResultBmp != null && _activeRemoveBgAlpha != null)
                        {
                            RemoveBackgroundPipeline.UpdateAlpha(_currentResultBmp, _activeRemoveBgAlpha);
                            UpdateResultImageSourceFromBitmap(_currentResultBmp, null);
                            if (!string.IsNullOrEmpty(ResultTempPath))
                            {
                                try
                                {
                                    _currentResultBmp.Save(ResultTempPath, System.Drawing.Imaging.ImageFormat.Png);
                                    InvalidateOcrCache();
                                }
                                catch { }
                            }
                        }
                    }

                    OnPropertyChanged(nameof(IsRemoveBgCanvasVisible));
                    OnPropertyChanged(nameof(IsMaskCanvasVisible));
                    OnPropertyChanged(nameof(IsCompareActive));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public double RotationAngle
        {
            get => _rotationAngle;
            set => SetProperty(ref _rotationAngle, value);
        }

        public double ScaleX
        {
            get => _scaleX;
            set => SetProperty(ref _scaleX, value);
        }

        public double ScaleY
        {
            get => _scaleY;
            set => SetProperty(ref _scaleY, value);
        }

        public bool IsCropActive
        {
            get => _isCropActive;
            set
            {
                if (SetProperty(ref _isCropActive, value))
                {
                    if (value)
                    {
                        IsRemoveBgRefining = false;
                    }
                    OnPropertyChanged(nameof(IsCompareActive));
                }
            }
        }

        public ObservableCollection<ImageBatchItem> SiblingImages => _siblingImages;
        public bool HasMultipleSiblingImages => _siblingImages != null && _siblingImages.Count > 1;

        public bool IsFilmstripVisible
        {
            get => _isFilmstripVisible;
            set => SetProperty(ref _isFilmstripVisible, value);
        }

        public BitmapSource? RemoveBgRefineOverlaySource
        {
            get => _removeBgRefineOverlaySource;
            private set => SetProperty(ref _removeBgRefineOverlaySource, value);
        }

        public bool IsRemoveBgAddMode
        {
            get => _isRemoveBgAddMode;
            set => SetProperty(ref _isRemoveBgAddMode, value);
        }

        public bool IsRemoveBgProcessing
        {
            get => _isRemoveBgProcessing;
            set => SetProperty(ref _isRemoveBgProcessing, value);
        }

        public bool IsUpscaleProcessing
        {
            get => _isUpscaleProcessing;
            set => SetProperty(ref _isUpscaleProcessing, value);
        }

        public bool IsInpaintProcessing
        {
            get => _isInpaintProcessing;
            set => SetProperty(ref _isInpaintProcessing, value);
        }

        public bool CanUndoRemoveBgRefinement => _removeBgUndoStack.Count > 0;
        public bool CanRedoRemoveBgRefinement => _removeBgRedoStack.Count > 0;
        public bool CanResetRemoveBgRefinement => _removeBgUndoStack.Count > 0 || _removeBgRedoStack.Count > 0;

        public bool IsRemoveBgCanvasVisible => IsRemoveBgMode && HasResult && IsRemoveBgRefining;
        public bool IsMaskCanvasVisible => IsInpaintCanvasVisible || IsRemoveBgCanvasVisible;
        public int RemoveBgWidth => _removeBgWidth;
        public int RemoveBgHeight => _removeBgHeight;

        public byte[]? GetActiveRemoveBgAlpha() => _activeRemoveBgAlpha;
        public byte[]? GetBaselineRemoveBgAlpha() => _baselineRemoveBgAlpha;

        public string FileInfoText
        {
            get => _fileInfoText;
            set => SetProperty(ref _fileInfoText, value);
        }

        public double ZoomLevel
        {
            get => _zoomLevel;
            set
            {
                if (SetProperty(ref _zoomLevel, value))
                {
                    OnPropertyChanged(nameof(ZoomText));
                }
            }
        }

        public string ZoomText => $"{Math.Round(ZoomLevel)}%";

        public static readonly double[] StandardZoomTiers =
        {
            50.0, 75.0, 100.0, 125.0, 150.0, 200.0, 250.0, 300.0, 400.0, 500.0, 600.0, 800.0, 1000.0
        };

        public static double GetNextZoomIn(double current)
        {
            foreach (var tier in StandardZoomTiers)
            {
                if (tier > current + 0.5)
                    return tier;
            }
            return 1000.0;
        }

        public static double GetNextZoomOut(double current)
        {
            for (int i = StandardZoomTiers.Length - 1; i >= 0; i--)
            {
                if (StandardZoomTiers[i] < current - 0.5)
                    return StandardZoomTiers[i];
            }
            return 50.0;
        }

        public double CompareRatio
        {
            get => _compareRatio;
            set => SetProperty(ref _compareRatio, value);
        }

        public bool IsCompareActive => _hasResult && !_isCropActive && (!IsRemoveBgMode || !IsRemoveBgRefining);

        public bool HasResult
        {
            get => _hasResult;
            set
            {
                if (SetProperty(ref _hasResult, value))
                {
                    OnPropertyChanged(nameof(IsCompareActive));
                    OnPropertyChanged(nameof(IsInpaintCanvasVisible));
                    OnPropertyChanged(nameof(IsRemoveBgCanvasVisible));
                    OnPropertyChanged(nameof(IsMaskCanvasVisible));
                }
            }
        }

        // Workflow state properties
        public string? ResultTempPath
        {
            get => _resultTempPath;
            set => SetProperty(ref _resultTempPath, value);
        }

        public string? LastSavedPath
        {
            get => _lastSavedPath;
            set
            {
                if (SetProperty(ref _lastSavedPath, value))
                {
                    OnPropertyChanged(nameof(IsOpenFolderEnabled));
                }
            }
        }

        public bool IsOpenFolderEnabled => !string.IsNullOrEmpty(_lastSavedPath) && File.Exists(_lastSavedPath);

        public bool IsSaveEnabled => _workflowState == "ResultUnsaved";

        public string ProcessElapsedText
        {
            get => _processElapsedText;
            set => SetProperty(ref _processElapsedText, value);
        }

        private void SetWorkflowState(string state)
        {
            if (_workflowState == state) return;
            _workflowState = state;
            OnPropertyChanged(nameof(PrimaryActionButtonText));
            OnPropertyChanged(nameof(PrimaryActionIcon));
            OnPropertyChanged(nameof(PrimaryActionToolTip));
            OnPropertyChanged(nameof(IsSaveEnabled));
            OnPropertyChanged(nameof(IsOpenFolderEnabled));
            CommandManager.InvalidateRequerySuggested();
        }

        public int SelectedPreviewBackgroundIndex
        {
            get => _selectedPreviewBackgroundIndex;
            set => SetProperty(ref _selectedPreviewBackgroundIndex, value);
        }

        public int SelectedQualityIndex
        {
            get => _selectedQualityIndex;
            set => SetProperty(ref _selectedQualityIndex, value);
        }

        public double UpscaleScale
        {
            get => _upscaleScale;
            set => SetProperty(ref _upscaleScale, value);
        }

        public string OutputPath
        {
            get => _outputPath;
            set => SetProperty(ref _outputPath, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string DetailText
        {
            get => _detailText;
            set => SetProperty(ref _detailText, value);
        }

        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (SetProperty(ref _isProcessing, value))
                {
                    OnPropertyChanged(nameof(IsGenericProcessingOverlayVisible));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        // Silenced across all single-image AI tools in favor of photo-bounded animated border
        public bool IsGenericProcessingOverlayVisible => false;

        public bool IsModeSwitchOverlayVisible
        {
            get => _isModeSwitchOverlayVisible;
            set => SetProperty(ref _isModeSwitchOverlayVisible, value);
        }

        public string SlotLitePath
        {
            get => _slotLitePath;
            set => SetProperty(ref _slotLitePath, value);
        }

        public string SlotBalancedPath
        {
            get => _slotBalancedPath;
            set => SetProperty(ref _slotBalancedPath, value);
        }

        public string SlotHighQualityPath
        {
            get => _slotHighQualityPath;
            set => SetProperty(ref _slotHighQualityPath, value);
        }

        public ObservableCollection<string> AvailableInpaintModels => _availableInpaintModels;

        public string SelectedInpaintModel
        {
            get => _selectedInpaintModel;
            set
            {
                if (SetProperty(ref _selectedInpaintModel, value))
                {
                    ApplyInpaintModel(value);
                    OnPropertyChanged(nameof(SelectedInpaintModelDisplayName));
                }
            }
        }

        public string SelectedInpaintModelDisplayName => _selectedInpaintModel switch
        {
            "LaMa" => "LaMa (Large Mask Inpainting)",
            "PhotosMager" => "Microsoft Photos Magic Eraser",
            _ => _selectedInpaintModel
        };

        public ICommand BrowseInpaintModelCommand { get; }

        public bool IsRgbColorSpace
        {
            get => _isRgbColorSpace;
            set
            {
                if (SetProperty(ref _isRgbColorSpace, value))
                {
                    if (value)
                    {
                        _isCmykColorSpace = false;
                        OnPropertyChanged(nameof(IsCmykColorSpace));
                        ValidateSelectedFormat();
                        OnPropertyChanged(nameof(AvailableOutputFormats));
                    }
                    OnPropertyChanged(nameof(PrimaryActionButtonText));
                    OnPropertyChanged(nameof(PrimaryActionToolTip));
                }
            }
        }

        public bool IsCmykColorSpace
        {
            get => _isCmykColorSpace;
            set
            {
                if (SetProperty(ref _isCmykColorSpace, value))
                {
                    if (value)
                    {
                        _isRgbColorSpace = false;
                        OnPropertyChanged(nameof(IsRgbColorSpace));
                        var defaultProfile = _cmykProfileManager.GetDefaultProfile();
                        ActiveCmykProfileText = defaultProfile != null
                            ? $"Profil: {defaultProfile.DisplayName}"
                            : "Tidak ada profil CMYK tersedia";
                        ValidateSelectedFormat();
                        OnPropertyChanged(nameof(AvailableOutputFormats));
                    }
                    OnPropertyChanged(nameof(PrimaryActionButtonText));
                    OnPropertyChanged(nameof(PrimaryActionToolTip));
                }
            }
        }

        private void ValidateSelectedFormat()
        {
            var allowed = AvailableOutputFormats.ToList();
            if (allowed.Count > 0 && !allowed.Contains(OutputFormat))
                OutputFormat = allowed.First();
        }

        public string ActiveCmykProfileText
        {
            get => _activeCmykProfileText;
            set => SetProperty(ref _activeCmykProfileText, value);
        }

        public ImageOutputFormat OutputFormat
        {
            get => CurrentOutputConfig.Format;
            set
            {
                if (SetProperty(ref _selectedFormat, value))
                {
                    OnPropertyChanged(nameof(PrimaryActionButtonText));
                }
            }
        }

        public IEnumerable<ImageOutputFormat> AvailableOutputFormats =>
            _currentModule?.GetAllowedFormats(_isCmykColorSpace ? ColorSpaceKind.Cmyk : ColorSpaceKind.Rgb)
                ?? new[] { ImageOutputFormat.Png };

        public string PrimaryActionButtonText
        {
            get
            {
                if (IsBatchMode)
                {
                    switch (_workflowState)
                    {
                        case "Processing": return "Batalkan";
                        case "Saved": return "Buka Folder";
                        case "Error": return "Coba Lagi";
                        default:
                            return IsUpscaleMode
                                ? $"AI Upscale ({_batchItems.Count} Gambar)"
                                : $"Hapus Background ({_batchItems.Count} Gambar)";
                    }
                }

                switch (_workflowState)
                {
                    case "Processing": return "Batalkan";
                    case "ResultUnsaved": return GetSaveButtonText();
                    case "Saved": return "Simpan Lagi";
                    case "Error": return "Coba Lagi";
                    default:
                        if (IsInpaintMode) return "Hapus Objek";
                        return IsRemoveBgMode ? "Hapus Background" : "AI Upscale";
                }
            }
        }

        public string PrimaryActionIcon
        {
            get
            {
                if (IsBatchMode)
                {
                    switch (_workflowState)
                    {
                        case "Processing": return "Sync";
                        case "Saved": return "Folder";
                        case "Error": return "Refresh";
                        default: return IsUpscaleMode ? "Document" : "ImageTools";
                    }
                }

                switch (_workflowState)
                {
                    case "Processing": return "Sync";
                    case "ResultUnsaved": return "Save";
                    case "Saved": return "Save";
                    case "Error": return "Refresh";
                    default:
                        if (IsInpaintMode) return "Clean";
                        return "Document";
                }
            }
        }

        public string PrimaryActionToolTip
        {
            get
            {
                if (IsBatchMode)
                {
                    switch (_workflowState)
                    {
                        case "Processing": return "Batalkan antrean proses";
                        case "Saved": return "Buka folder lokasi hasil disimpan";
                        case "Error": return "Coba proses ulang antrean";
                        default:
                            return IsUpscaleMode
                                ? $"Proses upscale untuk {_batchItems.Count} gambar"
                                : $"Proses hapus background untuk {_batchItems.Count} gambar";
                    }
                }

                switch (_workflowState)
                {
                    case "Processing": return "Batalkan proses";
                    case "ResultUnsaved": return IsCmykColorSpace ? "Simpan hasil ke folder file asli" : "Simpan hasil ke folder file asli";
                    case "Saved": return "Simpan ulang hasil";
                    case "Error": return "Coba lagi";
                    default:
                        if (IsInpaintMode) return "Hapus objek yang dicoret dari gambar";
                        return IsRemoveBgMode ? "Hapus background" : "Upscale gambar";
                }
            }
        }

        private string GetSaveButtonText()
        {
            var cfg = CurrentOutputConfig;
            var formatLabel = cfg.Format switch
            {
                ImageOutputFormat.Jpeg => "JPEG",
                ImageOutputFormat.Png => "PNG",
                ImageOutputFormat.Tiff => "TIFF",
                _ => "Gambar"
            };
            return $"Simpan {formatLabel}";
        }

        // Commands
        public ICommand SelectModeCommand { get; }
        public ICommand SelectImageCommand { get; }
        public ICommand ClearImageCommand { get; }
        public ICommand ExecutePrimaryActionCommand { get; }
        public ICommand BrowseOutputFolderCommand { get; }
        public ICommand OpenOutputFolderCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand ToggleCompareCommand { get; }
        public ICommand ConfirmModeSwitchCommand { get; }
        public ICommand CancelModeSwitchCommand { get; }
        public ICommand BrowseSlotLiteCommand { get; }
        public ICommand BrowseSlotBalancedCommand { get; }
        public ICommand BrowseSlotHighQualityCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RemoveBatchItemCommand { get; }
        public ICommand AddMoreBatchImagesCommand { get; }
        public ICommand ClearAllBatchCommand { get; }
        public ICommand UndoMaskCommand { get; }
        public ICommand RedoMaskCommand { get; }
        public ICommand ClearMaskCommand { get; }
        public ICommand SetBrushModeCommand { get; }
        public ICommand SetEraserModeCommand { get; }
        public ICommand SetAutoSelectModeCommand { get; }
        public ICommand ToggleRemoveBgRefiningCommand { get; }
        public ICommand SetRemoveBgAddModeCommand { get; }
        public ICommand SetRemoveBgRemoveModeCommand { get; }
        public ICommand UndoRemoveBgRefinementCommand { get; }
        public ICommand RedoRemoveBgRefinementCommand { get; }
        public ICommand ResetRemoveBgRefinementCommand { get; }
        public ICommand ToggleCropCommand { get; }
        public ICommand RotateLeftCommand { get; }
        public ICommand RotateRightCommand { get; }
        public ICommand FlipHorizontalCommand { get; }
        public ICommand FlipVerticalCommand { get; }
        public ICommand ResetTransformCommand { get; }
        public ICommand CopyTextOcrCommand { get; }
        public ICommand ToggleOcrBoxSelectionCommand { get; }
        public ICommand CopySelectedOcrTextCommand { get; }
        public ICommand CopyAllOcrTextCommand { get; }
        public ICommand CloseOcrCommand { get; }
        public ICommand ToggleFilmstripCommand { get; }
        public ICommand SelectSiblingImageCommand { get; }
        public ICommand NavigatePreviousImageCommand { get; }
        public ICommand NavigateNextImageCommand { get; }

        public void LoadImage(string filePath, bool isInternalDerivative = false)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return;

            CleanupTemp(preserveCropDerivative: isInternalDerivative);
            ResetProcessTimer();
            _batchItems.Clear();

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (decoder.Frames.Count > 0)
                {
                    OriginalImageWidth = decoder.Frames[0].PixelWidth;
                    OriginalImageHeight = decoder.Frames[0].PixelHeight;
                }
            }
            catch
            {
                OriginalImageWidth = 0;
                OriginalImageHeight = 0;
            }

            InputImagePath = filePath;
            OutputImagePath = string.Empty;
            HasResult = false;
            IsImageLoaded = true;
            RotationAngle = 0;
            ScaleX = 1.0;
            ScaleY = 1.0;
            IsCropActive = false;
            InvalidateOcrCache();

            var fileInfo = new FileInfo(filePath);
            var sizeText = $"{(fileInfo.Length / 1024.0 / 1024.0):F2} MB";

            if (!isInternalDerivative)
            {
                _cropDerivativePath = null;
                _originalSourcePath = filePath;
                FileInfoText = $"{fileInfo.Name} ({sizeText})";
                DetailText = $"{filePath} ({sizeText})";
                if (!_userSetOutputPath)
                {
                    var dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir))
                        OutputPath = dir;
                }
                if (IsFilmstripVisible)
                {
                    RefreshSiblingImages();
                }
            }
            else
            {
                _tempCropFiles.Add(filePath);
                _cropDerivativePath = filePath;
                var originalName = !string.IsNullOrEmpty(_originalSourcePath) ? Path.GetFileName(_originalSourcePath) : fileInfo.Name;
                FileInfoText = $"{originalName} (Dipotong) ({sizeText})";
                DetailText = $"{originalName} (Dipotong dari {filePath}) ({sizeText})";
            }

            UpdateFilmstripActiveItem();
            StatusText = "Gambar berhasil dimuat. Siap diproses.";
            ZoomLevel = 100.0;
            CompareRatio = 0.0;
            SetWorkflowState("ImageLoaded");

            if (IsInpaintMode)
            {
                TriggerMobileSamPrecompute();
            }

            OnPropertyChanged(nameof(IsBatchMode));
            OnPropertyChanged(nameof(IsSingleImageMode));
            OnPropertyChanged(nameof(HasNoImages));
            OnPropertyChanged(nameof(CanAddBatchImage));
            OnPropertyChanged(nameof(BatchCountText));
            OnPropertyChanged(nameof(PrimaryActionButtonText));
            OnPropertyChanged(nameof(PrimaryActionIcon));
            OnPropertyChanged(nameof(PrimaryActionToolTip));
            CommandManager.InvalidateRequerySuggested();
        }

        public void AddFiles(string[]? filePaths)
        {
            if (filePaths == null || filePaths.Length == 0)
                return;

            var validExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".png", ".jpg", ".jpeg", ".webp"
            };

            var validFiles = filePaths
                .Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f) && validExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            if (validFiles.Count == 0)
                return;

            if (IsInpaintMode)
            {
                // Mode Object Removal strictly operates on a single image per session
                _batchItems.Clear();
                OnPropertyChanged(nameof(IsBatchMode));
                OnPropertyChanged(nameof(IsSingleImageMode));
                OnPropertyChanged(nameof(CanAddBatchImage));
                OnPropertyChanged(nameof(BatchCountText));

                var firstFile = validFiles[0];
                LoadImage(firstFile);

                if (validFiles.Count > 1)
                {
                    StatusText = "Mode Hapus Objek hanya mendukung 1 gambar per sesi.";
                    DetailText = $"Hanya gambar pertama '{Path.GetFileName(firstFile)}' yang dimuat. {validFiles.Count - 1} file lainnya diabaikan.";
                }
                return;
            }

            if (validFiles.Count == 1 && _batchItems.Count == 0)
            {
                LoadImage(validFiles[0]);
                return;
            }

            if (_batchItems.Count == 0 && IsImageLoaded && !string.IsNullOrEmpty(InputImagePath) && File.Exists(InputImagePath))
            {
                var existingFi = new FileInfo(InputImagePath);
                var existingThumb = LoadThumbnailBitmap(InputImagePath, 360);
                _batchItems.Add(new ImageBatchItem
                {
                    FilePath = InputImagePath,
                    Name = existingFi.Name,
                    SizeText = $"{(existingFi.Length / 1024.0 / 1024.0):F2} MB",
                    ThumbnailImage = existingThumb,
                    State = ImageBatchItemState.Queued,
                    StatusText = "Dalam antrean",
                    StatusForeground = Brushes.Gray
                });
            }

            int remainingSlots = 10 - _batchItems.Count;
            if (remainingSlots <= 0)
            {
                StatusText = "Antrean penuh (maksimal 10 gambar).";
                DetailText = "Hapus gambar yang ada sebelum menambahkan gambar baru.";
                return;
            }

            var filesToAdd = validFiles.Take(remainingSlots).ToList();
            if (validFiles.Count > remainingSlots)
            {
                StatusText = $"Maksimal 10 gambar (hanya {remainingSlots} gambar pertama yang ditambahkan).";
            }

            foreach (var file in filesToAdd)
            {
                if (_batchItems.Any(b => string.Equals(b.FilePath, file, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var fi = new FileInfo(file);
                var thumb = LoadThumbnailBitmap(file, 360);
                var item = new ImageBatchItem
                {
                    FilePath = file,
                    Name = fi.Name,
                    SizeText = $"{(fi.Length / 1024.0 / 1024.0):F2} MB",
                    ThumbnailImage = thumb,
                    State = ImageBatchItemState.Queued,
                    StatusText = "Dalam antrean",
                    StatusForeground = Brushes.Gray
                };
                _batchItems.Add(item);
            }

            if (_batchItems.Count > 0)
            {
                IsImageLoaded = true;
                InputImagePath = _batchItems[0].FilePath;
                SetWorkflowState("ImageLoaded");
                if (validFiles.Count > remainingSlots)
                {
                    StatusText = $"Maksimal 10 gambar (hanya {remainingSlots} gambar pertama yang ditambahkan).";
                }
                else
                {
                    StatusText = $"{_batchItems.Count} gambar dalam antrean batch.";
                }
                DetailText = IsUpscaleMode
                    ? "Pilih faktor skala lalu klik AI Upscale."
                    : "Pilih kualitas model lalu klik Hapus Background.";
                FileInfoText = $"{_batchItems.Count} file dalam antrean";

                if (!_userSetOutputPath && string.IsNullOrEmpty(_outputPath))
                {
                    var dir = Path.GetDirectoryName(_batchItems[0].FilePath);
                    if (!string.IsNullOrEmpty(dir))
                        OutputPath = dir;
                }
            }

            OnPropertyChanged(nameof(IsBatchMode));
            OnPropertyChanged(nameof(IsSingleImageMode));
            OnPropertyChanged(nameof(HasNoImages));
            OnPropertyChanged(nameof(CanAddBatchImage));
            OnPropertyChanged(nameof(BatchCountText));
            OnPropertyChanged(nameof(PrimaryActionButtonText));
            OnPropertyChanged(nameof(PrimaryActionIcon));
            OnPropertyChanged(nameof(PrimaryActionToolTip));
            CommandManager.InvalidateRequerySuggested();
        }

        private async Task ExecuteRemoveBatchItemAsync(ImageBatchItem? item)
        {
            if (item == null || IsProcessing || !item.CanRemove)
                return;

            _batchItems.Remove(item);

            if (_batchItems.Count == 0)
            {
                await ExecuteClearImageAsync();
                return;
            }

            StatusText = $"{_batchItems.Count} gambar dalam antrean.";
            DetailText = $"Item '{item.Name}' dihapus dari antrean.";
            FileInfoText = $"{_batchItems.Count} file dalam antrean";

            OnPropertyChanged(nameof(IsBatchMode));
            OnPropertyChanged(nameof(IsSingleImageMode));
            OnPropertyChanged(nameof(HasNoImages));
            OnPropertyChanged(nameof(CanAddBatchImage));
            OnPropertyChanged(nameof(BatchCountText));
            OnPropertyChanged(nameof(PrimaryActionButtonText));
            OnPropertyChanged(nameof(PrimaryActionIcon));
            OnPropertyChanged(nameof(PrimaryActionToolTip));
            CommandManager.InvalidateRequerySuggested();
        }

        private void ExecuteSelectMode(string? targetMode)
        {
            if (string.IsNullOrEmpty(targetMode) || targetMode == SelectedMode)
                return;

            if (IsImageLoaded)
            {
                _pendingMode = targetMode;
                IsModeSwitchOverlayVisible = true;
            }
            else
            {
                SelectedMode = targetMode;
            }
        }

        private async Task ExecuteConfirmModeSwitchAsync()
        {
            IsModeSwitchOverlayVisible = false;
            await ExecuteClearImageAsync();
            if (!string.IsNullOrEmpty(_pendingMode))
            {
                SelectedMode = _pendingMode;
                _pendingMode = string.Empty;
            }
        }

        private void ExecuteSelectImage()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "File Gambar (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|Semua File (*.*)|*.*",
                Title = IsInpaintMode ? "Pilih Gambar untuk Hapus Objek" : "Pilih Gambar",
                Multiselect = !IsInpaintMode
            };

            if (dialog.ShowDialog() == true)
            {
                AddFiles(IsInpaintMode ? new[] { dialog.FileName } : dialog.FileNames);
            }
        }

        public Task ExecuteClearImageAsync()
        {
            ClearMobileSamSession();
            CleanupTemp(preserveCropDerivative: false);
            _cropDerivativePath = null;
            _originalSourcePath = string.Empty;
            _currentBeforeCropPath = null;
            ResetProcessTimer();
            _batchItems.Clear();
            _currentResultBmp?.Dispose();
            _currentResultBmp = null;
            OriginalImageWidth = 0;
            OriginalImageHeight = 0;
            LastSavedPath = null;
            InputImagePath = string.Empty;
            OutputImagePath = string.Empty;
            HasResult = false;
            IsImageLoaded = false;
            RotationAngle = 0;
            ScaleX = 1.0;
            ScaleY = 1.0;
            IsCropActive = false;
            IsRemoveBgRefining = false;
            CompareRatio = 0.5;
            Interlocked.Increment(ref _filmstripLoadSequence);
            _currentFilmstripDirectory = string.Empty;
            _siblingImages.Clear();
            IsFilmstripVisible = false;
            InvalidateOcrCache();
            FileInfoText = "Belum ada gambar";
            StatusText = IsInpaintMode
                ? "Pilih satu gambar untuk memulai penghapusan objek."
                : "Pilih satu atau beberapa gambar untuk memulai.";
            DetailText = "File asli tidak akan diubah.";
            ProgressValue = 0;
            ZoomLevel = 100.0;
            SetWorkflowState("Empty");

            OnPropertyChanged(nameof(IsBatchMode));
            OnPropertyChanged(nameof(IsSingleImageMode));
            OnPropertyChanged(nameof(HasNoImages));
            OnPropertyChanged(nameof(CanAddBatchImage));
            OnPropertyChanged(nameof(BatchCountText));
            OnPropertyChanged(nameof(PrimaryActionButtonText));
            OnPropertyChanged(nameof(PrimaryActionIcon));
            OnPropertyChanged(nameof(PrimaryActionToolTip));
            CommandManager.InvalidateRequerySuggested();
            return Task.CompletedTask;
        }

        public void ExecuteCancel()
        {
            if (_workflowState == "Processing" && _cts != null && !_cts.IsCancellationRequested)
            {
                StatusText = "Membatalkan...";
                _cts.Cancel();
            }
        }

        public async Task ExecutePrimaryActionAsync()
        {
            if (IsBatchMode)
            {
                switch (_workflowState)
                {
                    case "ImageLoaded":
                    case "Error":
                        if (IsUpscaleMode)
                        {
                            await ProcessBatchUpscaleAsync();
                        }
                        else
                        {
                            await ProcessBatchRemoveBackgroundAsync();
                        }
                        break;
                    case "Processing":
                        ExecuteCancel();
                        break;
                    case "Saved":
                        ExecuteOpenOutputFolder();
                        break;
                }
            }
            else
            {
                switch (_workflowState)
                {
                    case "ImageLoaded":
                    case "Error":
                        await ProcessAsync();
                        break;
                    case "Processing":
                        ExecuteCancel();
                        break;
                    case "ResultUnsaved":
                        await SaveResultAsync();
                        break;
                    case "Saved":
                        ExecuteOpenOutputFolder();
                        break;
                }
            }
        }

        private async Task ProcessBatchRemoveBackgroundAsync()
        {
            if (_batchItems.Count == 0) return;

            _cts = new CancellationTokenSource();
            IsProcessing = true;
            SetWorkflowState("Processing");
            StartProcessTimer();

            int total = _batchItems.Count;
            int current = 0;
            int successCount = 0;
            int failCount = 0;

            var slotValue = SelectedQualityIndex switch
            {
                0 => SlotLitePath,
                2 => SlotHighQualityPath,
                _ => SlotBalancedPath
            };

            var greenBrush = (Brush)new BrushConverter().ConvertFromString("#10B981")!;
            var accentBrush = (Application.Current?.TryFindResource("NexusAccentPrimary") as Brush)
                ?? (Application.Current?.TryFindResource("AccentPrimaryBrush") as Brush)
                ?? (Brush)new BrushConverter().ConvertFromString("#0067C0")!;
            var redBrush = Brushes.Red;
            var grayBrush = Brushes.Gray;

            try
            {
                foreach (var item in _batchItems)
                {
                    if (item.IsCompleted)
                    {
                        successCount++;
                        continue;
                    }

                    if (_cts.Token.IsCancellationRequested)
                    {
                        item.State = ImageBatchItemState.Cancelled;
                        item.StatusText = "Dibatalkan";
                        item.StatusForeground = grayBrush;
                        continue;
                    }

                    current++;
                    item.State = ImageBatchItemState.Processing;
                    item.StatusText = "Memproses AI...";
                    item.StatusForeground = accentBrush;

                    StatusText = $"Memproses {current} dari {total}: {item.Name}";
                    DetailText = "Menjalankan inferensi model AI...";
                    ProgressValue = ((double)(current - 1) / total) * 100.0;

                    string? tempPath = null;
                    try
                    {
                        if (!File.Exists(item.FilePath) || new FileInfo(item.FilePath).Length == 0)
                        {
                            throw new FileNotFoundException("File tidak ditemukan atau berukuran 0 byte.", item.FilePath);
                        }

                        tempPath = Path.Combine(Path.GetTempPath(), $"nexus-batch-{Guid.NewGuid():N}.png");

                        var inputPath = item.FilePath;
                        var tempOut = tempPath;
                        var resultPath = await Task.Run(async () =>
                        {
                            return await _imageWorkflow.RemoveBackgroundAsync(inputPath, slotValue, tempOut, _cts.Token);
                        }, _cts.Token);

                        var sourceFile = File.Exists(resultPath)
                            ? resultPath
                            : (File.Exists(tempPath) ? tempPath : item.FilePath);

                        var targetDir = !string.IsNullOrEmpty(_outputPath)
                            ? _outputPath
                            : Path.GetDirectoryName(item.FilePath) ?? string.Empty;

                        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }

                        var request = new SaveRequest
                        {
                            SourcePath = sourceFile,
                            DestDir = targetDir,
                            BaseName = Path.GetFileNameWithoutExtension(item.FilePath),
                            FileSuffix = _currentModule?.GetFileSuffix(0) ?? "_removed",
                            Output = CurrentOutputConfig,
                            Encoder = new EncoderOptions { Quality = 95 }
                        };

                        await SaveAsync(request);

                        item.ResultPath = LastSavedPath;
                        item.ResultImage = LoadThumbnailBitmap(sourceFile, 360);
                        item.State = ImageBatchItemState.Completed;
                        item.StatusText = "Selesai";
                        item.StatusForeground = greenBrush;
                        successCount++;
                    }
                    catch (OperationCanceledException)
                    {
                        item.State = ImageBatchItemState.Cancelled;
                        item.StatusText = "Dibatalkan";
                        item.StatusForeground = grayBrush;
                        Trace.WriteLine($"[ImageSubsystem] Batch processing cancelled on item '{item.Name}'");
                        break;
                    }
                    catch (Exception ex)
                    {
                        item.State = ImageBatchItemState.Failed;
                        item.StatusText = "Gagal";
                        item.StatusForeground = redBrush;
                        item.ErrorMessage = ex.Message;
                        failCount++;
                        Trace.WriteLine($"[ImageSubsystem] Error processing item '{item.Name}': {ex}");
                    }
                    finally
                    {
                        if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                        {
                            try { File.Delete(tempPath); }
                            catch (Exception ex) { Trace.WriteLine($"[ImageSubsystem] Failed to delete temp file '{tempPath}': {ex.Message}"); }
                        }
                    }

                    ProgressValue = ((double)current / total) * 100.0;
                }
            }
            finally
            {
                StopProcessTimer();
                _cts?.Dispose();
                _cts = null;
                IsProcessing = false;
            }

            if (successCount > 0)
            {
                StatusText = $"Selesai! {successCount}/{total} gambar berhasil diproses" + (failCount > 0 ? $" ({failCount} gagal)" : ".");
                DetailText = "Hasil tersimpan di folder tujuan.";
                SetWorkflowState("Saved");
                _notificationService?.ShowSuccess("Sukses", "Gambar berhasil disimpan.");
            }
            else if (failCount > 0)
            {
                StatusText = "Batch gagal memproses gambar.";
                DetailText = "Periksa format file dan ketersediaan model AI.";
                SetWorkflowState("Error");
            }
            else
            {
                StatusText = "Batch dibatalkan.";
                DetailText = "Proses dihentikan oleh pengguna.";
                SetWorkflowState("ImageLoaded");
            }

            GC.Collect(2, GCCollectionMode.Optimized);
            CommandManager.InvalidateRequerySuggested();
        }

        private async Task ProcessBatchUpscaleAsync()
        {
            if (_batchItems.Count == 0) return;

            _cts = new CancellationTokenSource();
            IsProcessing = true;
            IsUpscaleProcessing = true;
            SetWorkflowState("Processing");
            StartProcessTimer();

            int total = _batchItems.Count;
            int current = 0;
            int successCount = 0;
            int failCount = 0;

            var targetScale = (int)Math.Round(UpscaleScale);
            _saveTargetScale = targetScale;

            var greenBrush = (Brush)new BrushConverter().ConvertFromString("#10B981")!;
            var accentBrush = (Application.Current?.TryFindResource("NexusAccentPrimary") as Brush)
                ?? (Application.Current?.TryFindResource("AccentPrimaryBrush") as Brush)
                ?? (Brush)new BrushConverter().ConvertFromString("#0067C0")!;
            var redBrush = Brushes.Red;
            var grayBrush = Brushes.Gray;

            try
            {
                foreach (var item in _batchItems)
                {
                    if (item.IsCompleted)
                    {
                        successCount++;
                        continue;
                    }

                    if (_cts.Token.IsCancellationRequested)
                    {
                        item.State = ImageBatchItemState.Cancelled;
                        item.StatusText = "Dibatalkan";
                        item.StatusForeground = grayBrush;
                        continue;
                    }

                    current++;
                    item.State = ImageBatchItemState.Processing;
                    item.StatusText = "Memproses AI...";
                    item.StatusForeground = accentBrush;

                    StatusText = $"Memproses {current} dari {total}: {item.Name}";
                    DetailText = $"Meningkatkan resolusi ({targetScale}x)...";
                    ProgressValue = ((double)(current - 1) / total) * 100.0;

                    string? tempPath = null;
                    try
                    {
                        if (!File.Exists(item.FilePath) || new FileInfo(item.FilePath).Length == 0)
                        {
                            throw new FileNotFoundException("File tidak ditemukan atau berukuran 0 byte.", item.FilePath);
                        }

                        tempPath = Path.Combine(Path.GetTempPath(), $"nexus-batch-upscale-{Guid.NewGuid():N}.png");

                        var inputPath = item.FilePath;
                        var tempOut = tempPath;
                        var resultPath = await Task.Run(async () =>
                        {
                            return await _imageWorkflow.UpscaleAsync(inputPath, targetScale, tempOut, _cts.Token);
                        }, _cts.Token);

                        var sourceFile = File.Exists(resultPath)
                            ? resultPath
                            : (File.Exists(tempPath) ? tempPath : item.FilePath);

                        var targetDir = !string.IsNullOrEmpty(_outputPath)
                            ? _outputPath
                            : Path.GetDirectoryName(item.FilePath) ?? string.Empty;

                        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }

                        var request = new SaveRequest
                        {
                            SourcePath = sourceFile,
                            DestDir = targetDir,
                            BaseName = Path.GetFileNameWithoutExtension(item.FilePath),
                            FileSuffix = _currentModule?.GetFileSuffix(targetScale) ?? $"_upscaled_{targetScale}x",
                            Output = CurrentOutputConfig,
                            Encoder = new EncoderOptions { Quality = 95 }
                        };

                        await SaveAsync(request);

                        item.ResultPath = LastSavedPath;
                        item.ResultImage = LoadThumbnailBitmap(sourceFile, 360);
                        item.State = ImageBatchItemState.Completed;
                        item.StatusText = "Selesai";
                        item.StatusForeground = greenBrush;
                        successCount++;
                    }
                    catch (OperationCanceledException)
                    {
                        item.State = ImageBatchItemState.Cancelled;
                        item.StatusText = "Dibatalkan";
                        item.StatusForeground = grayBrush;
                        Trace.WriteLine($"[ImageSubsystem] Batch upscale cancelled on item '{item.Name}'");
                        break;
                    }
                    catch (Exception ex)
                    {
                        item.State = ImageBatchItemState.Failed;
                        item.StatusText = "Gagal";
                        item.StatusForeground = redBrush;
                        item.ErrorMessage = ex.Message;
                        failCount++;
                        Trace.WriteLine($"[ImageSubsystem] Error processing upscale item '{item.Name}': {ex}");
                    }
                    finally
                    {
                        if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                        {
                            try { File.Delete(tempPath); }
                            catch (Exception ex) { Trace.WriteLine($"[ImageSubsystem] Failed to delete temp file '{tempPath}': {ex.Message}"); }
                        }
                    }

                    ProgressValue = ((double)current / total) * 100.0;
                }
            }
            finally
            {
                StopProcessTimer();
                _cts?.Dispose();
                _cts = null;
                IsProcessing = false;
                IsUpscaleProcessing = false;
            }

            if (successCount > 0)
            {
                StatusText = $"Selesai! {successCount}/{total} gambar berhasil diproses" + (failCount > 0 ? $" ({failCount} gagal)" : ".");
                DetailText = "Hasil tersimpan di folder tujuan.";
                SetWorkflowState("Saved");
                _notificationService?.ShowSuccess("Sukses", "Gambar berhasil disimpan.");
            }
            else if (failCount > 0)
            {
                StatusText = "Batch gagal memproses gambar.";
                DetailText = "Periksa format file dan ketersediaan model AI.";
                SetWorkflowState("Error");
            }
            else
            {
                StatusText = "Batch dibatalkan.";
                DetailText = "Proses dihentikan oleh pengguna.";
                SetWorkflowState("ImageLoaded");
            }

            GC.Collect(2, GCCollectionMode.Optimized);
            CommandManager.InvalidateRequerySuggested();
        }

        private async Task ProcessAsync()
        {
            if (!IsImageLoaded || string.IsNullOrEmpty(InputImagePath) || !File.Exists(InputImagePath))
                return;

            CleanupTemp(preserveCropDerivative: true);
            _cts = new CancellationTokenSource();
            IsProcessing = true;
            SetWorkflowState("Processing");
            ProgressValue = 10.0;
            StatusText = IsRemoveBgMode ? "Menghapus background..." : "Meningkatkan detail...";
            DetailText = "Menjalankan inferensi model AI...";
            StartProcessTimer();

            var activeSource = GetCurrentEditorSourcePath();
            if (RotationAngle != 0 || ScaleX < 0 || ScaleY < 0)
            {
                try
                {
                    using var srcStream = new FileStream(activeSource, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var orientBmp = new System.Drawing.Bitmap(srcStream);
                    var flipType = GetRotateFlipType(RotationAngle, ScaleX < 0, ScaleY < 0);
                    if (flipType != System.Drawing.RotateFlipType.RotateNoneFlipNone)
                    {
                        orientBmp.RotateFlip(flipType);
                    }
                    var orientTempPath = Path.Combine(Path.GetTempPath(), $"nexus_orient_{Guid.NewGuid():N}.png");
                    orientBmp.Save(orientTempPath, System.Drawing.Imaging.ImageFormat.Png);
                    _tempCropFiles.Add(orientTempPath);
                    _cropDerivativePath = orientTempPath;
                    activeSource = orientTempPath;
                    BeforeImageSource = LoadSafeBitmap(orientTempPath);
                    OriginalImageWidth = orientBmp.Width;
                    OriginalImageHeight = orientBmp.Height;
                    RotationAngle = 0;
                    ScaleX = 1.0;
                    ScaleY = 1.0;
                }
                catch (Exception exOrient)
                {
                    Trace.WriteLine($"[ImageSubsystem] Failed to bake pre-inference orientation: {exOrient.Message}");
                }
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"nexus-{Guid.NewGuid():N}.png");

            try
            {
                ProgressValue = 40.0;

                if (IsRemoveBgMode)
                {
                    IsRemoveBgProcessing = true;
                    var slotValue = SelectedQualityIndex switch
                    {
                        0 => SlotLitePath,
                        2 => SlotHighQualityPath,
                        _ => SlotBalancedPath
                    };
                    var swPerfWorkflow = System.Diagnostics.Stopwatch.StartNew();
                    var resultPath = await _imageWorkflow.RemoveBackgroundAsync(
                        activeSource,
                        slotValue,
                        tempPath,
                        _cts.Token);
                    swPerfWorkflow.Stop();
                    IsRemoveBgProcessing = false;
                    ResultTempPath = resultPath;
                    OutputImagePath = resultPath;
                    CompareRatio = 0.5;
                    HasResult = true;
                    InvalidateOcrCache();

                    if (!string.IsNullOrEmpty(resultPath) && File.Exists(resultPath))
                    {
                        var swPerfSetup = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            byte[] baselineAlpha;
                            int bgW, bgH;
                            using (var tempBmp = new System.Drawing.Bitmap(resultPath))
                            {
                                bgW = tempBmp.Width;
                                bgH = tempBmp.Height;
                                baselineAlpha = RemoveBackgroundPipeline.ExtractAlpha(tempBmp);
                            }

                            _removeBgWidth = bgW;
                            _removeBgHeight = bgH;
                            _baselineRemoveBgAlpha = baselineAlpha;
                            _activeRemoveBgAlpha = (byte[])baselineAlpha.Clone();
                            _currentResultBmp?.Dispose();
                            _currentResultBmp = null;

                            // ROOT CAUSE FIX: Load active source (derivative/crop or original) and apply the alpha mask to it.
                            if (!string.IsNullOrEmpty(activeSource) && File.Exists(activeSource))
                            {
                                using var origBmp = new System.Drawing.Bitmap(activeSource);
                                _currentResultBmp = new System.Drawing.Bitmap(origBmp.Width, origBmp.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                                using (var g = System.Drawing.Graphics.FromImage(_currentResultBmp))
                                {
                                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                                    g.DrawImage(origBmp, 0, 0, origBmp.Width, origBmp.Height);
                                }
                                RemoveBackgroundPipeline.UpdateAlpha(_currentResultBmp, _activeRemoveBgAlpha);
                            }
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"[ImageSubsystem] Failed to initialize refinement mask: {ex.Message}");
                        }
                        swPerfSetup.Stop();
                        System.Diagnostics.Trace.WriteLine(
                            $"[PERF_AUDIT] ViewModel: Workflow={swPerfWorkflow.ElapsedMilliseconds}ms " +
                            $"MaskSetup={swPerfSetup.ElapsedMilliseconds}ms");
                    }
                    _removeBgUndoStack.Clear();
                    _removeBgRedoStack.Clear();
                    IsRemoveBgRefining = false;
                    NotifyRemoveBgRefinementStateChanged();

                    StopProcessTimer();
                    ProgressValue = 100.0;
                    StatusText = "Hasil siap disimpan.";
                    DetailText = "Klik Simpan untuk menyimpan ke folder file asli.";
                    SetWorkflowState("ResultUnsaved");
                }
                else if (IsInpaintMode)
                {
                    if (MaskProviderAsync == null && MaskProvider == null)
                    {
                        throw new InvalidOperationException("Lapisan coretan mask belum siap.");
                    }

                    StatusText = "Menyiapkan mask...";
                    DetailText = "Memproses goresan kuas...";
                    ProgressValue = 25.0;

                    // Release dispatcher to render progress and status update immediately
                    await Task.Yield();

                    using var maskBmp = MaskProviderAsync != null
                        ? await MaskProviderAsync().ConfigureAwait(true)
                        : await Task.Run(() => MaskProvider!()).ConfigureAwait(true);

                    if (maskBmp == null)
                    {
                        throw new InvalidOperationException("Silakan pilih atau coret objek yang ingin dihapus terlebih dahulu.");
                    }

                    var tier = IsHighDetailMode
                        ? Nexus.Engine.ImageSubsystem.Pipelines.InpaintExecutionTier.HighDetailDualPass
                        : Nexus.Engine.ImageSubsystem.Pipelines.InpaintExecutionTier.Auto;

                    StatusText = "Menghapus objek...";
                    DetailText = "Menjalankan inferensi model inpainting...";
                    ProgressValue = 40.0;

                    await Task.Yield();

                    IsInpaintProcessing = true;
                    var resultPath = await _imageWorkflow.InpaintAsync(
                        activeSource,
                        maskBmp,
                        tier,
                        tempPath,
                        _cts.Token).ConfigureAwait(true);
                    IsInpaintProcessing = false;

                    ResultTempPath = resultPath;
                    OutputImagePath = resultPath;
                    CompareRatio = 0.5;
                    InvalidateOcrCache();

                    // Automatically clear canvas strokes so Before/After comparison is clean
                    if (System.Windows.Application.Current?.Dispatcher != null &&
                        !System.Windows.Application.Current.Dispatcher.CheckAccess())
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            ClearMaskAction?.Invoke();
                            HasMaskStrokes = false;
                        });
                    }
                    else
                    {
                        ClearMaskAction?.Invoke();
                        HasMaskStrokes = false;
                    }
                    HasResult = true;

                    StopProcessTimer();
                    ProgressValue = 100.0;
                    StatusText = "Objek berhasil dihapus.";
                    DetailText = "Klik Simpan untuk menyimpan ke folder tujuan.";
                    SetWorkflowState("ResultUnsaved");
                }
                else
                {
                    IsUpscaleProcessing = true;
                    var targetScale = (int)Math.Round(UpscaleScale);
                    var resultPath = await _imageWorkflow.UpscaleAsync(
                        activeSource,
                        targetScale,
                        tempPath,
                        _cts.Token);
                    IsUpscaleProcessing = false;
                    ResultTempPath = resultPath;
                    OutputImagePath = resultPath;
                    CompareRatio = 0.5;
                    HasResult = true;
                    InvalidateOcrCache();
                    _saveTargetScale = targetScale;

                    StopProcessTimer();
                    ProgressValue = 100.0;
                    StatusText = "Hasil siap disimpan.";
                    DetailText = "Klik Simpan untuk menyimpan ke folder file asli.";
                    SetWorkflowState("ResultUnsaved");
                }
            }
            catch (OperationCanceledException)
            {
                CleanupTemp(preserveCropDerivative: true);
                ResetProcessTimer();
                StatusText = "Dibatalkan";
                DetailText = "Proses telah dibatalkan. File asli tidak diubah.";
                ProgressValue = 0.0;
                SetWorkflowState("ImageLoaded");
            }
            catch (Exception ex)
            {
                CleanupTemp(preserveCropDerivative: true);
                ResetProcessTimer();
                StatusText = "Gagal memproses gambar.";
                DetailText = ex.Message;
                SetWorkflowState("Error");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                IsProcessing = false;
                IsRemoveBgProcessing = false;
                IsUpscaleProcessing = false;
                IsInpaintProcessing = false;
            }
        }

        private async Task SaveResultAsync()
        {
            if (string.IsNullOrEmpty(ResultTempPath) || !File.Exists(ResultTempPath))
            {
                StatusText = "Hasil tidak tersedia.";
                DetailText = "Jalankan proses AI terlebih dahulu.";
                SetWorkflowState("Error");
                return;
            }

            var sourceDir = !string.IsNullOrEmpty(_outputPath) ? _outputPath : Path.GetDirectoryName(InputImagePath) ?? string.Empty;
            if (string.IsNullOrEmpty(sourceDir))
            {
                StatusText = "Folder file asli tidak valid.";
                DetailText = "Pastikan file asli masih tersedia.";
                return;
            }

            var baseName = Path.GetFileNameWithoutExtension(InputImagePath);
            var scale = _saveTargetScale > 0 ? _saveTargetScale : (int)Math.Round(UpscaleScale);
            var suffix = _currentModule?.GetFileSuffix(scale) ?? "_removed";
            var config = CurrentOutputConfig;

            if (_currentResultBmp != null && !string.IsNullOrEmpty(ResultTempPath))
            {
                try
                {
                    _currentResultBmp.Save(ResultTempPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[ImageSubsystem] Failed to sync refined bitmap to temp path: {ex.Message}");
                }
            }

            var request = new SaveRequest
            {
                SourcePath = ResultTempPath!,
                DestDir = sourceDir,
                BaseName = baseName,
                FileSuffix = suffix,
                Output = config,
                Encoder = new EncoderOptions { Quality = IsInpaintMode ? 98 : 95 }
            };

            try
            {
                StatusText = "Menyimpan...";
                DetailText = "Menyimpan hasil ke folder output.";

                await SaveAsync(request);

                StatusText = "Berhasil disimpan.";
                SetWorkflowState("Saved");
                ProgressValue = 100.0;
                _notificationService?.ShowSuccess("Sukses", "Gambar berhasil disimpan.");
            }
            catch (Exception ex)
            {
                StatusText = "Gagal menyimpan.";
                DetailText = ex.Message;
            }
        }

        private async Task SaveAsync(SaveRequest request)
        {
            var ext = request.Output.Format switch
            {
                ImageOutputFormat.Jpeg => ".jpg",
                ImageOutputFormat.Png => ".png",
                ImageOutputFormat.Tiff => ".tif",
                _ => ".png"
            };

            var finalName = $"{request.BaseName}{request.FileSuffix}{ext}";
            var finalPath = Path.Combine(request.DestDir, finalName);

            var index = 1;
            while (File.Exists(finalPath))
            {
                var nameWithoutExt = Path.GetFileNameWithoutExtension(finalName);
                var dedupName = $"{nameWithoutExt}_{index}{ext}";
                finalPath = Path.Combine(request.DestDir, dedupName);
                index++;
            }

            if (request.Output.ColorSpace.Kind == ColorSpaceKind.Cmyk)
            {
                var iccPath = request.Output.ColorSpace.IccProfilePath;
                if (string.IsNullOrEmpty(iccPath) || !File.Exists(iccPath))
                {
                    StatusText = "Profil CMYK tidak tersedia.";
                    DetailText = "Periksa pengaturan Color Management.";
                    return;
                }

                DetailText = $"Mengonversi ke CMYK...";

                using var cms = _colorManagementFactory();
                var result = cms.ExportToCmyk(request.SourcePath, finalPath, iccPath,
                    request.Output.Format, overwrite: false, _cts?.Token ?? CancellationToken.None);

                LastSavedPath = result.OutputPath;
                var sizeText = result.FileSizeBytes >= 1_000_000
                    ? $"{result.FileSizeBytes / 1_000_000.0:F1} MB"
                    : $"{result.FileSizeBytes / 1_000.0:F0} KB";
                DetailText = $"{Path.GetFileName(result.OutputPath)} ({sizeText}) — CMYK";
            }
            else if (request.Output.Format == ImageOutputFormat.Jpeg)
            {
                DetailText = "Mengompresi ke JPEG...";
                var quality = request.Encoder?.Quality ?? 95;
                await Task.Run(() =>
                {
                    using var fs = new FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    var frame = BitmapFrame.Create(new Uri(request.SourcePath), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var encoder = new JpegBitmapEncoder { QualityLevel = quality };
                    encoder.Frames.Add(frame);
                    encoder.Save(fs);
                });

                LastSavedPath = finalPath;
                var savedSize = new FileInfo(finalPath).Length;
                var sizeText = savedSize >= 1_000_000
                    ? $"{savedSize / 1_000_000.0:F1} MB"
                    : $"{savedSize / 1_000.0:F0} KB";
                DetailText = $"{Path.GetFileName(finalPath)} ({sizeText})";
            }
            else
            {
                await Task.Run(() => File.Copy(request.SourcePath, finalPath, overwrite: false));

                LastSavedPath = finalPath;
                var savedSize = new FileInfo(finalPath).Length;
                var sizeText = savedSize >= 1_000_000
                    ? $"{savedSize / 1_000_000.0:F1} MB"
                    : $"{savedSize / 1_000.0:F0} KB";
                DetailText = $"{Path.GetFileName(finalPath)} ({sizeText})";
            }
        }

        private void StartProcessTimer()
        {
            _processStopwatch.Restart();
            ProcessElapsedText = "(00:00)";
            _processTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _processTimer.Tick += (s, e) =>
            {
                ProcessElapsedText = $"({_processStopwatch.Elapsed:mm\\:ss})";
            };
            _processTimer.Start();
        }

        private void StopProcessTimer()
        {
            if (_processTimer != null)
            {
                _processTimer.Stop();
                _processTimer = null;
            }
            if (_processStopwatch.IsRunning)
            {
                _processStopwatch.Stop();
            }
        }

        private void ResetProcessTimer()
        {
            StopProcessTimer();
            _processStopwatch.Reset();
            ProcessElapsedText = string.Empty;
        }

        private void CleanupTemp(bool preserveCropDerivative = true)
        {
            if (!string.IsNullOrEmpty(_resultTempPath) && File.Exists(_resultTempPath))
            {
                try { File.Delete(_resultTempPath); } catch { }
            }
            ResultTempPath = null;

            var activeCrop = _cropDerivativePath;
            for (int i = _tempCropFiles.Count - 1; i >= 0; i--)
            {
                var cropPath = _tempCropFiles[i];
                if (preserveCropDerivative && string.Equals(cropPath, activeCrop, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (File.Exists(cropPath))
                {
                    try { File.Delete(cropPath); } catch { }
                }
                _tempCropFiles.RemoveAt(i);
            }
            if (!preserveCropDerivative)
            {
                _cropDerivativePath = null;
            }
            _currentBeforeCropPath = null;
            _currentResultBmp?.Dispose();
            _currentResultBmp = null;
            _baselineRemoveBgAlpha = null;
            _activeRemoveBgAlpha = null;
            _removeBgUndoStack.Clear();
            _removeBgRedoStack.Clear();
            _isRemoveBgRefining = false;
            _removeBgRefineOverlaySource = null;
            _removeBgRefineBgra = null;
            OnPropertyChanged(nameof(RemoveBgRefineOverlaySource));
            _isRemoveBgProcessing = false;
            _isUpscaleProcessing = false;
            _isInpaintProcessing = false;
            OnPropertyChanged(nameof(IsRemoveBgProcessing));
            OnPropertyChanged(nameof(IsUpscaleProcessing));
            OnPropertyChanged(nameof(IsInpaintProcessing));
            NotifyRemoveBgRefinementStateChanged();
        }

        private void ExecuteBrowseOutputFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Pilih Folder Output"
            };

            if (dialog.ShowDialog() == true)
            {
                OutputPath = dialog.FolderName;
                _userSetOutputPath = true;
            }
        }

        private void ExecuteOpenOutputFolder()
        {
            if (string.IsNullOrEmpty(_lastSavedPath))
                return;

            var folder = Path.GetDirectoryName(_lastSavedPath);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }

        private async Task ExecuteBrowseSlotAsync(string slotName)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = $"Pilih Model untuk Slot {slotName}",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
                return;

            var chosenPath = dialog.FolderName;

            if (!_imageWorkflow.ValidateModelPackage(chosenPath))
            {
                var onnxFiles = Directory.GetFiles(chosenPath, "*.onnx");
                if (onnxFiles.Length == 0)
                {
                    DetailText = $"Folder tidak mengandung file .onnx.";
                    return;
                }

                var metaPath = Path.Combine(chosenPath, "metadata.json");
                if (!File.Exists(metaPath))
                {
                    try
                    {
                        await _imageWorkflow.GenerateMetadataAsync(onnxFiles[0], chosenPath);
                    }
                    catch
                    {
                        DetailText = $"Gagal generate metadata untuk model.";
                        return;
                    }
                }
            }

            switch (slotName)
            {
                case "Lite": SlotLitePath = ToPortableSlotPath(chosenPath); break;
                case "Balanced": SlotBalancedPath = ToPortableSlotPath(chosenPath); break;
                case "High Quality": SlotHighQualityPath = ToPortableSlotPath(chosenPath); break;
            }

            SaveModelSlots();
            DetailText = $"Slot '{slotName}' → {chosenPath}";
        }

        private string ToPortableSlotPath(string path)
        {
            try
            {
                var relative = Path.GetRelativePath(_modelsRoot, path);
                if (!Path.IsPathRooted(relative) &&
                    !relative.StartsWith("..", StringComparison.Ordinal))
                {
                    return relative;
                }
            }
            catch
            {
            }

            return path;
        }

        public void RefreshAvailableInpaintModels()
        {
            _availableInpaintModels.Clear();
            _availableInpaintModels.Add("LaMa");

            if (Directory.Exists(_inpaintModelsRoot))
            {
                var subdirs = Directory.GetDirectories(_inpaintModelsRoot);
                foreach (var dir in subdirs)
                {
                    var name = Path.GetFileName(dir);
                    if (string.Equals(name, "LaMa", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (_imageWorkflow.ValidateModelPackage(dir))
                    {
                        if (!_availableInpaintModels.Contains(name))
                        {
                            _availableInpaintModels.Add(name);
                        }
                    }
                }
            }

            var selectedFromDisk = "LaMa";
            if (File.Exists(_inpaintSlotsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_inpaintSlotsFilePath);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Selected", out var selElem) &&
                        !string.IsNullOrWhiteSpace(selElem.GetString()))
                    {
                        selectedFromDisk = selElem.GetString()!;
                    }
                }
                catch
                {
                    selectedFromDisk = "LaMa";
                }
            }

            if (_availableInpaintModels.Contains(selectedFromDisk))
            {
                _selectedInpaintModel = selectedFromDisk;
                try
                {
                    _modelSelectionPolicy?.UseInpaintModel(selectedFromDisk);
                }
                catch
                {
                    _selectedInpaintModel = "LaMa";
                    SaveInpaintSlot("LaMa");
                    try { _modelSelectionPolicy?.UseInpaintModel("LaMa"); } catch { }
                }
            }
            else
            {
                _selectedInpaintModel = "LaMa";
                SaveInpaintSlot("LaMa");
                try { _modelSelectionPolicy?.UseInpaintModel("LaMa"); } catch { }
            }

            OnPropertyChanged(nameof(SelectedInpaintModel));
            OnPropertyChanged(nameof(SelectedInpaintModelDisplayName));
        }

        private void ApplyInpaintModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return;

            try
            {
                _modelSelectionPolicy?.UseInpaintModel(modelName);
                SaveInpaintSlot(modelName);
                DetailText = $"Model inpainting aktif: {modelName}";
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ImageSubsystemViewModel] ApplyInpaintModel failed: {ex.Message}");
                try
                {
                    _modelSelectionPolicy?.UseInpaintModel("LaMa");
                    SaveInpaintSlot("LaMa");
                }
                catch { }

                _selectedInpaintModel = "LaMa";
                OnPropertyChanged(nameof(SelectedInpaintModel));
                OnPropertyChanged(nameof(SelectedInpaintModelDisplayName));
                DetailText = $"Gagal memuat model '{modelName}'. Beralih ke default (LaMa).";
            }
        }

        private void SaveInpaintSlot(string modelName)
        {
            try
            {
                var dict = new Dictionary<string, string>
                {
                    ["Selected"] = modelName
                };
                var dir = Path.GetDirectoryName(_inpaintSlotsFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = System.Text.Json.JsonSerializer.Serialize(dict, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_inpaintSlotsFilePath, json);
            }
            catch { }
        }

        private async Task ExecuteBrowseInpaintModelAsync()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Pilih Folder Model Inpainting",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
                return;

            var chosenPath = dialog.FolderName;
            if (string.IsNullOrWhiteSpace(chosenPath) || !Directory.Exists(chosenPath))
                return;

            var onnxFiles = Directory.GetFiles(chosenPath, "*.onnx");
            if (onnxFiles.Length == 0)
            {
                DetailText = "Folder tidak mengandung file .onnx.";
                return;
            }

            var metaPath = Path.Combine(chosenPath, "metadata.json");
            if (!File.Exists(metaPath))
            {
                try
                {
                    await _imageWorkflow.GenerateMetadataAsync(onnxFiles[0], chosenPath, AICapability.InpaintingGeneral);
                }
                catch (Exception ex)
                {
                    DetailText = $"Gagal generate metadata: {ex.Message}";
                    return;
                }
            }

            var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(chosenPath));
            var targetDir = Path.Combine(_inpaintModelsRoot, folderName);

            if (!string.Equals(Path.GetFullPath(chosenPath), Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }
                    foreach (var file in Directory.GetFiles(chosenPath))
                    {
                        var destFile = Path.Combine(targetDir, Path.GetFileName(file));
                        File.Copy(file, destFile, overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    DetailText = $"Gagal menyalin model ke folder inpaint: {ex.Message}";
                    return;
                }
            }

            RefreshAvailableInpaintModels();
            SelectedInpaintModel = folderName;
            DetailText = $"Model inpainting berhasil diimpor: {folderName}";
        }

        private Task<MobileSamEmbeddingSession?> EnsureSamSessionAsync(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath) || !_mobileSamService.IsModelAvailable)
                return Task.FromResult<MobileSamEmbeddingSession?>(null);

            CancellationTokenSource? oldCts = null;
            Task<MobileSamEmbeddingSession?>? task = null;

            lock (_samSessionLock)
            {
                if (_currentSamSession != null && string.Equals(_samSessionImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult<MobileSamEmbeddingSession?>(_currentSamSession);
                }

                if (_samEncodeTask != null && string.Equals(_samEncodeImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
                {
                    return _samEncodeTask;
                }

                oldCts = _samEncodeCts;
                _samEncodeCts = null;

                if (_currentSamSession != null && !string.Equals(_samSessionImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
                {
                    _currentSamSession.Dispose();
                    _currentSamSession = null;
                    _samSessionImagePath = null;
                }

                var cts = new CancellationTokenSource();
                _samEncodeCts = cts;
                _samEncodeImagePath = imagePath;
                IsAutoSelectEncoding = true;

                task = Task.Run<MobileSamEmbeddingSession?>(async () =>
                {
                    MobileSamEmbeddingSession? session = null;
                    try
                    {
                        using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        using var streamBmp = new System.Drawing.Bitmap(fs);
                        using var cloneBmp = new System.Drawing.Bitmap(streamBmp);

                        session = await _mobileSamService.EncodeImageAsync(cloneBmp, cts.Token).ConfigureAwait(false);

                        lock (_samSessionLock)
                        {
                            if (!cts.IsCancellationRequested &&
                                ReferenceEquals(_samEncodeTask, task) &&
                                string.Equals(_samEncodeImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
                            {
                                _currentSamSession?.Dispose();
                                _currentSamSession = session;
                                _samSessionImagePath = imagePath;
                                return session;
                            }
                            else
                            {
                                session?.Dispose();
                                return null;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        session?.Dispose();
                        return null;
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"[ImageSubsystemViewModel] MobileSAM encoding error: {ex.Message}");
                        session?.Dispose();
                        return null;
                    }
                    finally
                    {
                        lock (_samSessionLock)
                        {
                            if (ReferenceEquals(_samEncodeTask, task))
                            {
                                _samEncodeTask = null;
                                _samEncodeImagePath = null;
                                _samEncodeCts = null;
                                IsAutoSelectEncoding = false;
                            }
                        }
                        cts.Dispose();
                    }
                });

                _samEncodeTask = task;
            }

            if (oldCts != null)
            {
                try
                {
                    oldCts.Cancel();
                    oldCts.Dispose();
                }
                catch { }
            }

            return task;
        }

        public void TriggerMobileSamPrecompute()
        {
            if (!IsInpaintMode || !IsImageLoaded || string.IsNullOrEmpty(InputImagePath) || !File.Exists(InputImagePath) || !_mobileSamService.IsModelAvailable)
                return;

            _ = EnsureSamSessionAsync(InputImagePath);
        }

        public async Task<bool> AddAutoSelectPointAsync(System.Drawing.PointF nativePoint)
        {
            if (!_mobileSamService.IsModelAvailable || string.IsNullOrEmpty(InputImagePath) || !File.Exists(InputImagePath))
                return false;

            try
            {
                string imagePath = InputImagePath;
                bool needsEncoding;
                lock (_samSessionLock)
                {
                    needsEncoding = _currentSamSession == null || !string.Equals(_samSessionImagePath, imagePath, StringComparison.OrdinalIgnoreCase);
                }

                if (needsEncoding)
                {
                    StatusText = "Mempersiapkan model auto-select...";
                    DetailText = "Menganalisis gambar...";
                }

                var session = await EnsureSamSessionAsync(imagePath).ConfigureAwait(false);
                if (session == null)
                    return false;

                StatusText = "Mendeteksi objek...";
                DetailText = $"Memproses seleksi di ({Math.Round(nativePoint.X)}, {Math.Round(nativePoint.Y)})...";

                using var pointMaskBmp = await _mobileSamService.SegmentPointAsync(session, nativePoint, CancellationToken.None).ConfigureAwait(false);

                byte[]? maskToRender;
                int w = pointMaskBmp.Width;
                int h = pointMaskBmp.Height;

                lock (_samSessionLock)
                {
                    if (!ReferenceEquals(_currentSamSession, session) || !string.Equals(_samSessionImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    byte[]? undoSnapshot = _accumulatedAutoMask != null ? (byte[])_accumulatedAutoMask.Clone() : null;
                    _autoMaskUndoStack.Push(undoSnapshot);
                    _autoMaskRedoStack.Clear();

                    if (_accumulatedAutoMask == null || _autoMaskWidth != w || _autoMaskHeight != h)
                    {
                        _accumulatedAutoMask = new byte[w * h];
                        _autoMaskWidth = w;
                        _autoMaskHeight = h;
                    }

                    var maskData = pointMaskBmp.LockBits(
                        new System.Drawing.Rectangle(0, 0, w, h),
                        System.Drawing.Imaging.ImageLockMode.ReadOnly,
                        System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    try
                    {
                        int stride = Math.Abs(maskData.Stride);
                        byte[] maskBytes = new byte[stride * h];
                        System.Runtime.InteropServices.Marshal.Copy(maskData.Scan0, maskBytes, 0, maskBytes.Length);

                        for (int y = 0; y < h; y++)
                        {
                            int rowOff = y * stride;
                            int accRowOff = y * w;
                            for (int x = 0; x < w; x++)
                            {
                                byte val = maskBytes[rowOff + (x * 3) + 2]; // R channel
                                if (val > 127)
                                {
                                    _accumulatedAutoMask[accRowOff + x] = 255;
                                }
                            }
                        }
                    }
                    finally
                    {
                        pointMaskBmp.UnlockBits(maskData);
                    }

                    maskToRender = (byte[])_accumulatedAutoMask.Clone();
                }

                await UpdateAutoMaskOverlayAsync(w, h, maskToRender).ConfigureAwait(true);

                lock (_samSessionLock)
                {
                    if (!ReferenceEquals(_currentSamSession, session) || !string.Equals(_samSessionImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
                    {
                        ClearAutoMaskLocked();
                        return false;
                    }

                    HasAutoMask = true;
                    HasMaskStrokes = true;
                    StatusText = "Objek terpilih.";
                    DetailText = "Klik objek lain untuk menambah seleksi, atau gambar dengan Kuas.";
                    return true;
                }
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                StatusText = "Gagal memilih objek.";
                DetailText = ex.Message;
                return false;
            }
        }

        public async Task<bool> UndoAutoMaskAsync()
        {
            if (_autoMaskUndoStack.Count == 0) return false;

            var prev = _autoMaskUndoStack.Pop();
            _autoMaskRedoStack.Push(_accumulatedAutoMask != null ? (byte[])_accumulatedAutoMask.Clone() : null);

            _accumulatedAutoMask = prev;
            if (prev != null)
            {
                await UpdateAutoMaskOverlayAsync(_autoMaskWidth, _autoMaskHeight, prev).ConfigureAwait(true);
                HasAutoMask = true;
            }
            else
            {
                AutoMaskOverlaySource = null;
                HasAutoMask = false;
            }
            return true;
        }

        public async Task<bool> RedoAutoMaskAsync()
        {
            if (_autoMaskRedoStack.Count == 0) return false;

            var next = _autoMaskRedoStack.Pop();
            _autoMaskUndoStack.Push(_accumulatedAutoMask != null ? (byte[])_accumulatedAutoMask.Clone() : null);

            _accumulatedAutoMask = next;
            if (next != null)
            {
                await UpdateAutoMaskOverlayAsync(_autoMaskWidth, _autoMaskHeight, next).ConfigureAwait(true);
                HasAutoMask = true;
            }
            else
            {
                AutoMaskOverlaySource = null;
                HasAutoMask = false;
            }
            return true;
        }

        private void ClearAutoMaskLocked()
        {
            _accumulatedAutoMask = null;
            _autoMaskWidth = 0;
            _autoMaskHeight = 0;
            _autoMaskUndoStack.Clear();
            _autoMaskRedoStack.Clear();
            AutoMaskOverlaySource = null;
            HasAutoMask = false;
        }

        public void ClearAutoMask()
        {
            lock (_samSessionLock)
            {
                ClearAutoMaskLocked();
            }
        }

        public void ClearMobileSamSession()
        {
            CancellationTokenSource? oldCts = null;
            lock (_samSessionLock)
            {
                oldCts = _samEncodeCts;
                _samEncodeCts = null;
                _samEncodeTask = null;
                _samEncodeImagePath = null;

                _currentSamSession?.Dispose();
                _currentSamSession = null;
                _samSessionImagePath = null;

                ClearAutoMaskLocked();
            }

            if (oldCts != null)
            {
                try
                {
                    oldCts.Cancel();
                    oldCts.Dispose();
                }
                catch { }
            }

            IsAutoSelectEncoding = false;
        }

        private async Task UpdateAutoMaskOverlayAsync(int w, int h, byte[]? maskData)
        {
            if (maskData == null || w <= 0 || h <= 0)
            {
                AutoMaskOverlaySource = null;
                return;
            }

            byte[] bgraPixels = await Task.Run(() =>
            {
                byte[] pixels = new byte[w * h * 4];
                for (int i = 0; i < w * h; i++)
                {
                    if (maskData[i] > 127)
                    {
                        int p = i * 4;
                        pixels[p] = 48;     // B
                        pixels[p + 1] = 59;  // G
                        pixels[p + 2] = 255; // R
                        pixels[p + 3] = 140; // A
                    }
                }
                return pixels;
            }).ConfigureAwait(true);

            var wbmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            wbmp.WritePixels(new Int32Rect(0, 0, w, h), bgraPixels, w * 4, 0);
            wbmp.Freeze();
            AutoMaskOverlaySource = wbmp;
        }

        public void ApplyRemoveBgStroke(System.Drawing.Rectangle roi, byte[] localStrokeMask, bool isAdd)
        {
            if (_activeRemoveBgAlpha == null || _removeBgWidth <= 0 || _removeBgHeight <= 0) return;

            var patch = RemoveBackgroundPipeline.ApplyStroke(
                _activeRemoveBgAlpha,
                _removeBgWidth,
                _removeBgHeight,
                roi,
                localStrokeMask,
                isAdd);

            _removeBgUndoStack.Push(patch);
            _removeBgRedoStack.Clear();

            if (_currentResultBmp != null)
            {
                RemoveBackgroundPipeline.UpdateAlpha(_currentResultBmp, _activeRemoveBgAlpha, roi);
                UpdateResultImageSourceFromBitmap(_currentResultBmp, roi);
            }

            if (_isRemoveBgRefining)
            {
                UpdateRemoveBgRefineOverlay(roi);
            }

            NotifyRemoveBgRefinementStateChanged();
        }

        public void UndoRemoveBgRefinement()
        {
            if (_removeBgUndoStack.Count == 0 || _activeRemoveBgAlpha == null) return;

            var patch = _removeBgUndoStack.Pop();
            _removeBgRedoStack.Push(patch);

            RemoveBackgroundPipeline.ApplyPatch(_activeRemoveBgAlpha, _removeBgWidth, _removeBgHeight, patch, isRedo: false);

            if (_currentResultBmp != null)
            {
                RemoveBackgroundPipeline.UpdateAlpha(_currentResultBmp, _activeRemoveBgAlpha, patch.Bounds);
                UpdateResultImageSourceFromBitmap(_currentResultBmp, patch.Bounds);
            }

            if (_isRemoveBgRefining)
            {
                UpdateRemoveBgRefineOverlay(patch.Bounds);
            }

            NotifyRemoveBgRefinementStateChanged();
        }

        public void RedoRemoveBgRefinement()
        {
            if (_removeBgRedoStack.Count == 0 || _activeRemoveBgAlpha == null) return;

            var patch = _removeBgRedoStack.Pop();
            _removeBgUndoStack.Push(patch);

            RemoveBackgroundPipeline.ApplyPatch(_activeRemoveBgAlpha, _removeBgWidth, _removeBgHeight, patch, isRedo: true);

            if (_currentResultBmp != null)
            {
                RemoveBackgroundPipeline.UpdateAlpha(_currentResultBmp, _activeRemoveBgAlpha, patch.Bounds);
                UpdateResultImageSourceFromBitmap(_currentResultBmp, patch.Bounds);
            }

            if (_isRemoveBgRefining)
            {
                UpdateRemoveBgRefineOverlay(patch.Bounds);
            }

            NotifyRemoveBgRefinementStateChanged();
        }

        public void ResetRemoveBgRefinement()
        {
            if (_baselineRemoveBgAlpha == null || _activeRemoveBgAlpha == null) return;

            Array.Copy(_baselineRemoveBgAlpha, _activeRemoveBgAlpha, _baselineRemoveBgAlpha.Length);
            _removeBgUndoStack.Clear();
            _removeBgRedoStack.Clear();

            if (_currentResultBmp != null)
            {
                RemoveBackgroundPipeline.UpdateAlpha(_currentResultBmp, _activeRemoveBgAlpha);
                UpdateResultImageSourceFromBitmap(_currentResultBmp, null);
            }

            if (_isRemoveBgRefining)
            {
                UpdateRemoveBgRefineOverlay(null);
            }

            NotifyRemoveBgRefinementStateChanged();
        }

        private void UpdateRemoveBgRefineOverlay(System.Drawing.Rectangle? roi = null)
        {
            if (_activeRemoveBgAlpha == null || _removeBgWidth <= 0 || _removeBgHeight <= 0)
            {
                RemoveBgRefineOverlaySource = null;
                _removeBgRefineBgra = null;
                return;
            }

            if (System.Windows.Application.Current?.Dispatcher != null &&
                !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => UpdateRemoveBgRefineOverlay(roi));
                return;
            }

            try
            {
                int w = _removeBgWidth;
                int h = _removeBgHeight;

                _removeBgRefineBgra = RemoveBackgroundPipeline.CreateMaskVisualization(
                    _activeRemoveBgAlpha,
                    w,
                    h,
                    roi,
                    _removeBgRefineBgra);

                if (RemoveBgRefineOverlaySource is WriteableBitmap wbmp &&
                    wbmp.PixelWidth == w &&
                    wbmp.PixelHeight == h)
                {
                    var updateRect = roi ?? new System.Drawing.Rectangle(0, 0, w, h);
                    updateRect.Intersect(new System.Drawing.Rectangle(0, 0, w, h));
                    if (updateRect.Width > 0 && updateRect.Height > 0)
                    {
                        int stride = w * 4;
                        int offset = (updateRect.Y * stride) + (updateRect.X * 4);
                        wbmp.WritePixels(
                            new Int32Rect(updateRect.X, updateRect.Y, updateRect.Width, updateRect.Height),
                            _removeBgRefineBgra,
                            stride,
                            offset);
                    }
                }
                else
                {
                    var newWbmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                    newWbmp.WritePixels(
                        new Int32Rect(0, 0, w, h),
                        _removeBgRefineBgra,
                        w * 4,
                        0);
                    RemoveBgRefineOverlaySource = newWbmp;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ImageSubsystem] UpdateRemoveBgRefineOverlay error: {ex.Message}");
            }
        }

        private void NotifyRemoveBgRefinementStateChanged()
        {
            OnPropertyChanged(nameof(CanUndoRemoveBgRefinement));
            OnPropertyChanged(nameof(CanRedoRemoveBgRefinement));
            OnPropertyChanged(nameof(CanResetRemoveBgRefinement));
            CommandManager.InvalidateRequerySuggested();
        }

        private void UpdateResultImageSourceFromBitmap(System.Drawing.Bitmap bmp, System.Drawing.Rectangle? roi)
        {
            if (System.Windows.Application.Current?.Dispatcher != null &&
                !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => UpdateResultImageSourceFromBitmap(bmp, roi));
                return;
            }

            try
            {
                if (ResultImageSource is WriteableBitmap wbmp &&
                    wbmp.PixelWidth == bmp.Width &&
                    wbmp.PixelHeight == bmp.Height)
                {
                    var updateRect = roi ?? new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
                    updateRect.Intersect(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
                    if (updateRect.Width <= 0 || updateRect.Height <= 0) return;

                    var data = bmp.LockBits(
                        updateRect,
                        System.Drawing.Imaging.ImageLockMode.ReadOnly,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try
                    {
                        wbmp.WritePixels(
                            new Int32Rect(updateRect.X, updateRect.Y, updateRect.Width, updateRect.Height),
                            data.Scan0,
                            data.Stride * updateRect.Height,
                            data.Stride);
                    }
                    finally
                    {
                        bmp.UnlockBits(data);
                    }
                }
                else
                {
                    var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
                    var data = bmp.LockBits(
                        rect,
                        System.Drawing.Imaging.ImageLockMode.ReadOnly,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try
                    {
                        var newWbmp = new WriteableBitmap(bmp.Width, bmp.Height, 96, 96, PixelFormats.Bgra32, null);
                        newWbmp.WritePixels(
                            new Int32Rect(0, 0, bmp.Width, bmp.Height),
                            data.Scan0,
                            data.Stride * bmp.Height,
                            data.Stride);
                        ResultImageSource = newWbmp;
                    }
                    finally
                    {
                        bmp.UnlockBits(data);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ImageSubsystem] UpdateResultImageSourceFromBitmap error: {ex.Message}");
            }
        }

        public string GetCurrentEditorSourcePath()
        {
            if (HasResult && !string.IsNullOrEmpty(ResultTempPath) && File.Exists(ResultTempPath))
            {
                SyncActiveResultBitmapToFile();
                return ResultTempPath;
            }
            if (!string.IsNullOrEmpty(_cropDerivativePath) && File.Exists(_cropDerivativePath))
            {
                return _cropDerivativePath;
            }
            return InputImagePath;
        }

        public void SyncActiveResultBitmapToFile()
        {
            if (_currentResultBmp != null && !string.IsNullOrEmpty(ResultTempPath))
            {
                try
                {
                    _currentResultBmp.Save(ResultTempPath, System.Drawing.Imaging.ImageFormat.Png);
                    InvalidateOcrCache();
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[ImageSubsystem] Failed to sync active result bitmap: {ex.Message}");
                }
            }
        }

        public void ApplyCropToActiveResult(string croppedPath, int width, int height, string? croppedBeforePath = null)
        {
            _tempCropFiles.Add(croppedPath);
            ResultTempPath = croppedPath;
            OutputImagePath = croppedPath;
            ResultImageSource = LoadSafeBitmap(croppedPath);
            OriginalImageWidth = width;
            OriginalImageHeight = height;

            if (!string.IsNullOrWhiteSpace(croppedBeforePath) && File.Exists(croppedBeforePath))
            {
                _tempCropFiles.Add(croppedBeforePath);
                _currentBeforeCropPath = croppedBeforePath;
                BeforeImageSource = LoadSafeBitmap(croppedBeforePath);
            }

            if (_currentResultBmp != null)
            {
                _currentResultBmp.Dispose();
                _currentResultBmp = new System.Drawing.Bitmap(croppedPath);
                _removeBgWidth = width;
                _removeBgHeight = height;
                _baselineRemoveBgAlpha = RemoveBackgroundPipeline.ExtractAlpha(_currentResultBmp);
                _activeRemoveBgAlpha = (byte[])_baselineRemoveBgAlpha.Clone();
            }

            IsCropActive = false;
            RotationAngle = 0;
            ScaleX = 1.0;
            ScaleY = 1.0;
            ZoomLevel = 100.0;
            HasResult = true;
            InvalidateOcrCache();
            SetWorkflowState("ResultUnsaved");

            OnPropertyChanged(nameof(ResultImageSource));
            OnPropertyChanged(nameof(BeforeImageSource));
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(IsCompareActive));
            OnPropertyChanged(nameof(OutputImagePath));
            OnPropertyChanged(nameof(ResultTempPath));
        }

        public void ApplyCropToInput(string croppedPath, int width, int height)
        {
            _tempCropFiles.Add(croppedPath);
            _cropDerivativePath = croppedPath;
            BeforeImageSource = LoadSafeBitmap(croppedPath);
            OriginalImageWidth = width;
            OriginalImageHeight = height;

            IsCropActive = false;
            RotationAngle = 0;
            ScaleX = 1.0;
            ScaleY = 1.0;
            ZoomLevel = 100.0;
            HasResult = false;
            InvalidateOcrCache();

            var fileInfo = new FileInfo(croppedPath);
            var sizeText = $"{(fileInfo.Length / 1024.0 / 1024.0):F2} MB";
            var originalName = !string.IsNullOrEmpty(_originalSourcePath) ? Path.GetFileName(_originalSourcePath) : Path.GetFileName(InputImagePath);
            FileInfoText = $"{originalName} (Dipotong) ({sizeText})";
            DetailText = $"{originalName} (Dipotong dari {croppedPath}) ({sizeText})";
            StatusText = "Gambar berhasil dipotong. Siap diproses.";

            SetWorkflowState("ImageLoaded");

            OnPropertyChanged(nameof(BeforeImageSource));
            OnPropertyChanged(nameof(CropDerivativePath));
            OnPropertyChanged(nameof(OriginalImageWidth));
            OnPropertyChanged(nameof(OriginalImageHeight));
            OnPropertyChanged(nameof(FileInfoText));
            OnPropertyChanged(nameof(DetailText));
            OnPropertyChanged(nameof(StatusText));
        }

        internal static System.Drawing.RotateFlipType GetRotateFlipType(double rotationAngle, bool flipX, bool flipY)
        {
            int normAngle = (((int)Math.Round(rotationAngle) % 360) + 360) % 360;

            return (normAngle, flipX, flipY) switch
            {
                (0, false, false) => System.Drawing.RotateFlipType.RotateNoneFlipNone,
                (0, true, false) => System.Drawing.RotateFlipType.RotateNoneFlipX,
                (0, false, true) => System.Drawing.RotateFlipType.RotateNoneFlipY,
                (0, true, true) => System.Drawing.RotateFlipType.RotateNoneFlipXY,

                (90, false, false) => System.Drawing.RotateFlipType.Rotate90FlipNone,
                (90, true, false) => System.Drawing.RotateFlipType.Rotate90FlipX,
                (90, false, true) => System.Drawing.RotateFlipType.Rotate90FlipY,
                (90, true, true) => System.Drawing.RotateFlipType.Rotate90FlipXY,

                (180, false, false) => System.Drawing.RotateFlipType.Rotate180FlipNone,
                (180, true, false) => System.Drawing.RotateFlipType.Rotate180FlipX,
                (180, false, true) => System.Drawing.RotateFlipType.Rotate180FlipY,
                (180, true, true) => System.Drawing.RotateFlipType.Rotate180FlipXY,

                (270, false, false) => System.Drawing.RotateFlipType.Rotate270FlipNone,
                (270, true, false) => System.Drawing.RotateFlipType.Rotate270FlipX,
                (270, false, true) => System.Drawing.RotateFlipType.Rotate270FlipY,
                (270, true, true) => System.Drawing.RotateFlipType.Rotate270FlipXY,

                _ => System.Drawing.RotateFlipType.RotateNoneFlipNone
            };
        }

        private OneOcrEngine GetOrCreateOneOcrEngine()
        {
            if (_oneOcrEngine != null) return _oneOcrEngine;
            lock (_oneOcrLock)
            {
                return _oneOcrEngine ??= new OneOcrEngine();
            }
        }

        public void InvalidateOcrCache()
        {
            _cachedOcrDocument = null;
            _cachedOcrSource = null;
            _cachedOcrLastWriteTimeUtc = default;
            _cachedOcrFileLength = 0;
            _ocrWords.Clear();
            _overlayBoxes.Clear();
            OcrMaskGeometry = null;
            OcrSelectionGeometry = null;
            OnPropertyChanged(nameof(HasOcrWords));
            OnPropertyChanged(nameof(HasOverlayBoxes));
            OnPropertyChanged(nameof(HasSelectedOcrWords));
            OnPropertyChanged(nameof(HasSelectedOcrBoxes));
        }

        private void ExecuteResetTransform()
        {
            RotationAngle = 0;
            ScaleX = 1.0;
            ScaleY = 1.0;
            ZoomLevel = 100.0;
            IsCropActive = false;
            _ocrWords.Clear();
            _overlayBoxes.Clear();
            OcrMaskGeometry = null;
            OcrSelectionGeometry = null;
            OnPropertyChanged(nameof(HasOcrWords));
            OnPropertyChanged(nameof(HasOverlayBoxes));
            OnPropertyChanged(nameof(HasSelectedOcrWords));
            OnPropertyChanged(nameof(HasSelectedOcrBoxes));
        }

        internal async Task ExecuteCopyTextOcrAsync()
        {
            var ocrSource = (HasResult && !string.IsNullOrEmpty(ResultTempPath) && File.Exists(ResultTempPath))
                ? ResultTempPath
                : InputImagePath;

            if (string.IsNullOrWhiteSpace(ocrSource) || !File.Exists(ocrSource))
                return;

            DateTime currentWriteTime = default;
            long currentLength = 0;
            try
            {
                var fi = new FileInfo(ocrSource);
                currentWriteTime = fi.LastWriteTimeUtc;
                currentLength = fi.Length;
            }
            catch
            {
            }

            // Session Cache check: if the exact same image and file content is already scanned, restore instantly without re-inference
            if (_cachedOcrDocument != null &&
                string.Equals(_cachedOcrSource, ocrSource, StringComparison.OrdinalIgnoreCase) &&
                _cachedOcrLastWriteTimeUtc == currentWriteTime &&
                _cachedOcrFileLength == currentLength)
            {
                if (_ocrWords.Count == 0 && _cachedOcrDocument.AllWords != null)
                {
                    foreach (var w in _cachedOcrDocument.AllWords)
                    {
                        _ocrWords.Add(new OneOcrWordViewModel
                        {
                            GlobalIndex = w.GlobalIndex,
                            LineIndex = w.LineIndex,
                            WordIndex = w.WordIndex,
                            Text = w.Text,
                            Confidence = w.Confidence,
                            Quad = w.Quad,
                            PolygonGeometry = OneOcrWordViewModel.CreateQuadGeometry(w.Quad)
                        });
                    }
                }
                else
                {
                    foreach (var w in _ocrWords)
                    {
                        w.IsSelected = false;
                        w.IsHovered = false;
                    }
                }
                RebuildOcrMaskGeometry();
                RebuildOcrSelectionGeometry();
                OnPropertyChanged(nameof(HasOcrWords));
                OnPropertyChanged(nameof(HasOverlayBoxes));
                OnPropertyChanged(nameof(HasSelectedOcrWords));
                OnPropertyChanged(nameof(HasSelectedOcrBoxes));
                StatusText = $"Terdeteksi {_ocrWords.Count} kata teks (dari cache). Klik atau seret untuk memilih kata.";
                return;
            }

            IsProcessing = true;
            StatusText = "Mendeteksi teks (OneOCR)...";
            try
            {
                var doc = await Task.Run(() =>
                {
                    using var bmp = DesignImagePreprocessor.LoadAsBitmap(ocrSource);
                    var engine = GetOrCreateOneOcrEngine();
                    return engine.RunPipeline(bmp);
                }).ConfigureAwait(true);

                _cachedOcrDocument = doc;
                _cachedOcrSource = ocrSource;
                _cachedOcrLastWriteTimeUtc = currentWriteTime;
                _cachedOcrFileLength = currentLength;

                _ocrWords.Clear();
                _overlayBoxes.Clear();

                if (doc.AllWords != null)
                {
                    foreach (var w in doc.AllWords)
                    {
                        _ocrWords.Add(new OneOcrWordViewModel
                        {
                            GlobalIndex = w.GlobalIndex,
                            LineIndex = w.LineIndex,
                            WordIndex = w.WordIndex,
                            Text = w.Text,
                            Confidence = w.Confidence,
                            Quad = w.Quad,
                            PolygonGeometry = OneOcrWordViewModel.CreateQuadGeometry(w.Quad)
                        });
                    }
                }

                RebuildOcrMaskGeometry();
                RebuildOcrSelectionGeometry();

                OnPropertyChanged(nameof(HasOcrWords));
                OnPropertyChanged(nameof(HasOverlayBoxes));
                OnPropertyChanged(nameof(HasSelectedOcrWords));
                OnPropertyChanged(nameof(HasSelectedOcrBoxes));

                if (_ocrWords.Count == 0 || string.IsNullOrWhiteSpace(doc.FullText))
                {
                    _notificationService?.ShowWarning("Pindai Teks", "Tidak ada teks yang terdeteksi pada gambar.");
                    StatusText = "Tidak ada teks terdeteksi.";
                    return;
                }

                StatusText = $"Terdeteksi {_ocrWords.Count} kata ({doc.Lines.Count} baris) dalam {doc.InferenceMs} ms. Klik atau seret untuk memilih kata.";
            }
            catch (OneOcrNotAvailableException ex)
            {
                Trace.WriteLine($"[ImageSubsystem] OneOCR not available: {ex.Message}");
                _notificationService?.ShowError("Pindai Teks", $"OneOCR tidak tersedia: {ex.Message}");
                StatusText = $"OneOCR tidak tersedia: {ex.Message}";
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ImageSubsystem] OneOCR execution failed: {ex.Message}");
                _notificationService?.ShowError("Pindai Teks", $"Gagal memindai teks: {ex.Message}");
                StatusText = $"Gagal membaca teks: {ex.Message}";
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private void RebuildOcrMaskGeometry()
        {
            if (OriginalImageWidth > 0 && OriginalImageHeight > 0 && _ocrWords.Count > 0)
            {
                var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
                group.Children.Add(new RectangleGeometry(new Rect(0, 0, OriginalImageWidth, OriginalImageHeight)));

                var pathGeom = new PathGeometry();
                var byLine = _ocrWords.GroupBy(w => w.LineIndex).OrderBy(g => g.Key);
                foreach (var lineGroup in byLine)
                {
                    var sortedWords = lineGroup.OrderBy(w => w.WordIndex).ToList();
                    var runs = SplitIntoContiguousRuns(sortedWords);
                    foreach (var run in runs)
                    {
                        pathGeom.Figures.Add(OneOcrWordViewModel.CreateContiguousRunFigure(run));
                    }
                }
                group.Children.Add(pathGeom);

                if (group.CanFreeze) group.Freeze();
                OcrMaskGeometry = group;
            }
            else
            {
                OcrMaskGeometry = null;
            }
        }

        public void RebuildOcrSelectionGeometry()
        {
            var selectedWords = _ocrWords.Where(w => w.IsSelected).ToList();
            if (selectedWords.Count == 0)
            {
                OcrSelectionGeometry = null;
                return;
            }

            var pathGeom = new PathGeometry();
            var byLine = selectedWords.GroupBy(w => w.LineIndex).OrderBy(g => g.Key);

            foreach (var lineGroup in byLine)
            {
                var sortedWords = lineGroup.OrderBy(w => w.WordIndex).ToList();
                var runs = SplitIntoContiguousRuns(sortedWords);

                foreach (var run in runs)
                {
                    pathGeom.Figures.Add(OneOcrWordViewModel.CreateContiguousRunFigure(run));
                }
            }

            if (pathGeom.CanFreeze) pathGeom.Freeze();
            OcrSelectionGeometry = pathGeom;
        }

        private static List<List<OneOcrWordViewModel>> SplitIntoContiguousRuns(List<OneOcrWordViewModel> sortedWords)
        {
            var runs = new List<List<OneOcrWordViewModel>>();
            if (sortedWords.Count == 0) return runs;

            var currentRun = new List<OneOcrWordViewModel> { sortedWords[0] };
            for (int i = 1; i < sortedWords.Count; i++)
            {
                var prev = sortedWords[i - 1];
                var curr = sortedWords[i];

                if (curr.WordIndex == prev.WordIndex + 1)
                {
                    currentRun.Add(curr);
                }
                else
                {
                    runs.Add(currentRun);
                    currentRun = new List<OneOcrWordViewModel> { curr };
                }
            }
            runs.Add(currentRun);
            return runs;
        }

        public void SelectWord(OneOcrWordViewModel? word, bool multiSelect = false)
        {
            if (word == null) return;
            if (!multiSelect)
            {
                foreach (var w in _ocrWords)
                {
                    if (w != word) w.IsSelected = false;
                }
            }
            word.IsSelected = !word.IsSelected;
            RebuildOcrSelectionGeometry();
            OnPropertyChanged(nameof(HasSelectedOcrWords));
            OnPropertyChanged(nameof(HasSelectedOcrBoxes));
        }

        public void SelectWordRange(int startGlobalIndex, int endGlobalIndex)
        {
            int min = Math.Min(startGlobalIndex, endGlobalIndex);
            int max = Math.Max(startGlobalIndex, endGlobalIndex);

            foreach (var w in _ocrWords)
            {
                w.IsSelected = (w.GlobalIndex >= min && w.GlobalIndex <= max);
            }
            RebuildOcrSelectionGeometry();
            OnPropertyChanged(nameof(HasSelectedOcrWords));
            OnPropertyChanged(nameof(HasSelectedOcrBoxes));
        }

        public void SelectAllWords()
        {
            foreach (var w in _ocrWords)
            {
                w.IsSelected = true;
            }
            RebuildOcrSelectionGeometry();
            OnPropertyChanged(nameof(HasSelectedOcrWords));
            OnPropertyChanged(nameof(HasSelectedOcrBoxes));
        }

        public void ClearWordSelection()
        {
            foreach (var w in _ocrWords)
            {
                w.IsSelected = false;
            }
            RebuildOcrSelectionGeometry();
            OnPropertyChanged(nameof(HasSelectedOcrWords));
            OnPropertyChanged(nameof(HasSelectedOcrBoxes));
        }

        private void ExecuteToggleOcrBoxSelection(AiCheckOverlayBoxViewModel? box)
        {
            if (box == null) return;
            box.IsHighlighted = !box.IsHighlighted;
            OnPropertyChanged(nameof(HasSelectedOcrBoxes));
        }

        private void ExecuteCopySelectedOcrText()
        {
            var selectedWords = _ocrWords.Where(w => w.IsSelected).OrderBy(w => w.GlobalIndex).ToList();
            if (selectedWords.Count > 0)
            {
                var lines = selectedWords
                    .GroupBy(w => w.LineIndex)
                    .OrderBy(g => g.Key)
                    .Select(g => string.Join(" ", g.OrderBy(w => w.WordIndex).Select(w => w.Text)));
                var text = string.Join(Environment.NewLine, lines);
                bool ok = NexusClipboard.SetPlainTextNative(text);
                if (ok)
                {
                    StatusText = $"{selectedWords.Count} kata disalin.";
                    _notificationService?.ShowSuccess("Salin Teks", $"{selectedWords.Count} kata terpilih disalin ke clipboard.");
                }
                return;
            }

            var selected = _overlayBoxes.Where(b => b.IsHighlighted).ToList();
            if (selected.Count == 0) return;
            var legacyText = string.Join(Environment.NewLine, selected.Select(b => b.OriginalText));
            bool legOk = NexusClipboard.SetPlainTextNative(legacyText);
            if (legOk)
            {
                StatusText = $"{selected.Count} baris teks disalin.";
                _notificationService?.ShowSuccess("Salin Teks", $"{selected.Count} baris teks terpilih disalin ke clipboard.");
            }
        }

        private void ExecuteCopyAllOcrText()
        {
            if (_cachedOcrDocument != null && !string.IsNullOrWhiteSpace(_cachedOcrDocument.FullText))
            {
                bool ok = NexusClipboard.SetPlainTextNative(_cachedOcrDocument.FullText);
                if (ok)
                {
                    StatusText = $"Semua teks ({_ocrWords.Count} kata) disalin.";
                    _notificationService?.ShowSuccess("Salin Teks", $"Semua teks ({_ocrWords.Count} kata) disalin ke clipboard.");
                }
                return;
            }

            if (_ocrWords.Count > 0)
            {
                var lines = _ocrWords
                    .OrderBy(w => w.GlobalIndex)
                    .GroupBy(w => w.LineIndex)
                    .OrderBy(g => g.Key)
                    .Select(g => string.Join(" ", g.OrderBy(w => w.WordIndex).Select(w => w.Text)));
                var text = string.Join(Environment.NewLine, lines);
                bool ok = NexusClipboard.SetPlainTextNative(text);
                if (ok)
                {
                    StatusText = $"Semua teks ({_ocrWords.Count} kata) disalin.";
                    _notificationService?.ShowSuccess("Salin Teks", $"Semua teks ({_ocrWords.Count} kata) disalin ke clipboard.");
                }
                return;
            }

            if (_overlayBoxes.Count == 0) return;
            var boxText = string.Join(Environment.NewLine, _overlayBoxes.Select(b => b.OriginalText));
            bool legOk = NexusClipboard.SetPlainTextNative(boxText);
            if (legOk)
            {
                StatusText = $"Semua teks ({_overlayBoxes.Count} baris) disalin.";
                _notificationService?.ShowSuccess("Salin Teks", $"Semua teks ({_overlayBoxes.Count} baris) disalin ke clipboard.");
            }
        }

        private void ExecuteCloseOcr()
        {
            // Close visual overlay while preserving cached document for instant reopen
            _ocrWords.Clear();
            _overlayBoxes.Clear();
            OcrMaskGeometry = null;
            OnPropertyChanged(nameof(HasOcrWords));
            OnPropertyChanged(nameof(HasOverlayBoxes));
            OnPropertyChanged(nameof(HasSelectedOcrWords));
            OnPropertyChanged(nameof(HasSelectedOcrBoxes));
            StatusText = "Pemindaian teks ditutup.";
        }

        private void ExecuteToggleFilmstrip()
        {
            IsFilmstripVisible = !IsFilmstripVisible;
            if (IsFilmstripVisible)
            {
                RefreshSiblingImages();
            }
        }

        public void RefreshSiblingImages(bool force = false)
        {
            if (string.IsNullOrWhiteSpace(InputImagePath) || !File.Exists(InputImagePath))
            {
                _siblingImages.Clear();
                _currentFilmstripDirectory = string.Empty;
                OnPropertyChanged(nameof(HasMultipleSiblingImages));
                CommandManager.InvalidateRequerySuggested();
                return;
            }

            var dir = Path.GetDirectoryName(InputImagePath);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                _siblingImages.Clear();
                _currentFilmstripDirectory = string.Empty;
                OnPropertyChanged(nameof(HasMultipleSiblingImages));
                CommandManager.InvalidateRequerySuggested();
                return;
            }

            // Same-folder check: if already loaded for this directory and not forced, skip recrawl
            if (!force && string.Equals(dir, _currentFilmstripDirectory, StringComparison.OrdinalIgnoreCase) && _siblingImages.Count > 0)
            {
                return;
            }

            var currentSequence = Interlocked.Increment(ref _filmstripLoadSequence);

            _currentFilmstripDirectory = dir;
            _siblingImages.Clear();

            var validExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };
            var files = new List<string>();
            try
            {
                var dirInfo = new DirectoryInfo(dir);
                files = dirInfo.EnumerateFiles()
                    .Where(fi => validExts.Contains(fi.Extension))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .ThenBy(fi => fi.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(fi => fi.FullName)
                    .ToList();
            }
            catch
            {
                OnPropertyChanged(nameof(HasMultipleSiblingImages));
                CommandManager.InvalidateRequerySuggested();
                return;
            }

            // A. Metadata-first realization: populate immediately with null thumbnail so UI responds instantly
            var items = new List<ImageBatchItem>(files.Count);
            var currentActive = !string.IsNullOrEmpty(_originalSourcePath) ? _originalSourcePath : InputImagePath;
            foreach (var file in files)
            {
                var fi = new FileInfo(file);
                var isItemActive = string.Equals(file, currentActive, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(file, InputImagePath, StringComparison.OrdinalIgnoreCase);
                var item = new ImageBatchItem
                {
                    FilePath = file,
                    Name = fi.Name,
                    SizeText = $"{(fi.Length / 1024.0 / 1024.0):F2} MB",
                    ThumbnailImage = null,
                    IsActive = isItemActive
                };
                _siblingImages.Add(item);
                items.Add(item);
            }
            OnPropertyChanged(nameof(HasMultipleSiblingImages));
            CommandManager.InvalidateRequerySuggested();

            // B. Asynchronous thumbnail decode via Task.Run with sequence check
            _ = Task.Run(() =>
            {
                foreach (var it in items)
                {
                    if (currentSequence != _filmstripLoadSequence)
                        return; // Folder changed, abort stale decode

                    try
                    {
                        if (File.Exists(it.FilePath))
                        {
                            var thumb = LoadThumbnailBitmap(it.FilePath, 120);
                            if (thumb != null)
                            {
                                if (thumb.CanFreeze) thumb.Freeze();
                                if (currentSequence != _filmstripLoadSequence)
                                    return;

                                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
                                {
                                    if (currentSequence == _filmstripLoadSequence)
                                    {
                                        it.ThumbnailImage = thumb;
                                    }
                                });
                            }
                        }
                    }
                    catch { }
                }
            });
        }

        private void ExecuteSelectSiblingImage(ImageBatchItem? item)
        {
            if (item != null && !string.IsNullOrWhiteSpace(item.FilePath))
            {
                if (string.Equals(item.FilePath, InputImagePath, StringComparison.OrdinalIgnoreCase))
                    return; // Same image is no-op!

                LoadImage(item.FilePath);
                UpdateFilmstripActiveItem();
            }
        }

        private void UpdateFilmstripActiveItem()
        {
            var currentActive = !string.IsNullOrEmpty(_originalSourcePath) ? _originalSourcePath : InputImagePath;
            foreach (var s in _siblingImages)
            {
                s.IsActive = string.Equals(s.FilePath, currentActive, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(s.FilePath, InputImagePath, StringComparison.OrdinalIgnoreCase);
            }
            CommandManager.InvalidateRequerySuggested();
        }

        private int GetCurrentSiblingIndex()
        {
            if (_siblingImages == null || _siblingImages.Count == 0) return -1;
            var currentActive = !string.IsNullOrEmpty(_originalSourcePath) ? _originalSourcePath : InputImagePath;
            for (int i = 0; i < _siblingImages.Count; i++)
            {
                if (string.Equals(_siblingImages[i].FilePath, currentActive, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(_siblingImages[i].FilePath, InputImagePath, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        private bool CanNavigatePreviousImage()
        {
            if (!IsSingleImageMode || IsCropActive || IsProcessing) return false;
            var idx = GetCurrentSiblingIndex();
            return idx > 0;
        }

        private void ExecuteNavigatePreviousImage()
        {
            var idx = GetCurrentSiblingIndex();
            if (idx > 0)
            {
                ExecuteSelectSiblingImage(_siblingImages[idx - 1]);
            }
        }

        private bool CanNavigateNextImage()
        {
            if (!IsSingleImageMode || IsCropActive || IsProcessing) return false;
            var idx = GetCurrentSiblingIndex();
            return idx >= 0 && idx < _siblingImages.Count - 1;
        }

        private void ExecuteNavigateNextImage()
        {
            var idx = GetCurrentSiblingIndex();
            if (idx >= 0 && idx < _siblingImages.Count - 1)
            {
                ExecuteSelectSiblingImage(_siblingImages[idx + 1]);
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
                lock (_oneOcrLock)
                {
                    if (_ownsOneOcrEngine && _oneOcrEngine != null)
                    {
                        _oneOcrEngine.Dispose();
                        _oneOcrEngine = null;
                    }
                }
                ClearMobileSamSession();
                _currentResultBmp?.Dispose();
                _currentResultBmp = null;
                _cts?.Dispose();
                _cts = null;
            }
        }
    }
}
