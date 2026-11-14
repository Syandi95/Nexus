using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Nexus.App.Controls.InfoBar;
using Nexus.App.Services;
using Nexus.PdfUtilities;

namespace Nexus.App.ViewModels
{
    public class PdfFileItem : ViewModelBase
    {
        private string _path = string.Empty;
        private string _name = string.Empty;
        private string _sizeText = string.Empty;
        private string _pageCountText = string.Empty;
        private string _dimensionsCmText = string.Empty;
        private int _pageCount;
        private string _dateText = string.Empty;
        private string _statusText = string.Empty;
        private Brush _statusForeground = System.Windows.Media.Brushes.Gray;
        private BitmapSource? _previewImage;
        private bool _hasPreviewFailed;
        private bool _isLoading;
        private string _importError = string.Empty;
        public string Path { get => _path; set => SetProperty(ref _path, value); }
        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public string SizeText { get => _sizeText; set => SetProperty(ref _sizeText, value); }
        public string PageCountText { get => _pageCountText; set => SetProperty(ref _pageCountText, value); }
        public string DimensionsCmText
        {
            get => _dimensionsCmText;
            set
            {
                if (SetProperty(ref _dimensionsCmText, value))
                    OnPropertyChanged(nameof(HasDimensionsCm));
            }
        }
        public bool HasDimensionsCm => !string.IsNullOrWhiteSpace(_dimensionsCmText);

        public static string FormatCmValue(double cm)
        {
            var idCulture = System.Globalization.CultureInfo.GetCultureInfo("id-ID");
            return Math.Abs(cm - Math.Round(cm)) < 0.05
                ? Math.Round(cm).ToString("0", idCulture)
                : cm.ToString("0.0", idCulture);
        }
        public int PageCount { get => _pageCount; set { if(SetProperty(ref _pageCount,value)) OnPropertyChanged(nameof(IsReady)); } }
        public bool IsLoading { get => _isLoading; set { if(SetProperty(ref _isLoading,value)) OnPropertyChanged(nameof(IsReady)); } }
        public string ImportError { get => _importError; set { if(SetProperty(ref _importError,value)) OnPropertyChanged(nameof(IsReady)); } }
        public bool IsReady => !IsLoading && PageCount>0 && string.IsNullOrEmpty(ImportError);
        public PdfReaderDocumentState ReaderState { get; } = new();
        public string DateText { get => _dateText; set => SetProperty(ref _dateText, value); }
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
        public Brush StatusForeground { get => _statusForeground; set => SetProperty(ref _statusForeground, value); }
        public BitmapSource? PreviewImage { get => _previewImage; set => SetProperty(ref _previewImage, value); }

        public bool HasPreviewFailed
        {
            get => _hasPreviewFailed;
            set
            {
                if (SetProperty(ref _hasPreviewFailed, value))
                    OnPropertyChanged(nameof(PreviewAvailable));
            }
        }

        public bool PreviewAvailable => !HasPreviewFailed;
    }

    public partial class PdfUtilitiesViewModel : ViewModelBase
    {
        private readonly IPdfConversionService _pdfConversionService;
        private readonly IPdfEngineService _pdfEngineService;
        private readonly INotificationService? _notificationService;
        private readonly IPdfOfficeToolService? _officeTools;

        private bool _isInfoBarOpen;
        private string _infoBarTitle = string.Empty;
        private string _infoBarMessage = string.Empty;
        private InfoBarSeverity _infoBarSeverity = InfoBarSeverity.Informational;

        private static readonly Brush GreenBrush = (Brush)new BrushConverter().ConvertFromString("#10B981")!;
        static PdfUtilitiesViewModel() => GreenBrush.Freeze();
        private static readonly Brush RedBrush = System.Windows.Media.Brushes.Red;
        private static readonly Brush GrayBrush = System.Windows.Media.Brushes.Gray;

        private readonly ObservableCollection<PdfFileItem> _pdfFiles = new();
        private int _totalPages;

        private int _selectedFormatIndex;
        private bool _convertAllPages = true;
        private string _pageRangeText = string.Empty;
        private const string CustomDpiPlaceholder = "Kustom...";
        private readonly ObservableCollection<string> _dpiItems = new()
        {
            "72 DPI", "144 DPI", "216 DPI", "300 DPI", "600 DPI", CustomDpiPlaceholder
        };
        private int _selectedDpiIndex = 3;
        private int _selectedDpi = 300;
        private string _customDpiText = "300";
        private bool _isCustomDpiVisible;
        private string _customDpiButtonText = "Custom";

        private int _qualityValue = 80;
        private string _qualityText = "80%";

        private string _outputDirPath = string.Empty;
        private int _namingIndex;

        private bool _canConvert;
        private bool _isConverting;
        private double _progressValue;
        private string _progressPctText = "0%";
        private bool _isProgressIndeterminate;
        private string _statusText = "Belum ada proses konversi";
        private string _combinedStatusText = "Belum ada file dipilih";
        private bool _hasConverted;

        private PdfFileItem? _fullScreenItem;
        private bool _isFullScreenVisible;
        private BitmapSource? _fullScreenPreviewImage;

        private CancellationTokenSource? _conversionCts;
        private Task _conversionTask = Task.CompletedTask;
        private Task _metadataTask = Task.CompletedTask;
        internal Task ImportCompletion => _metadataTask;
        private readonly CancellationTokenSource _workspaceCts = new();
        private bool _isClosed;
        private int _previewRequestVersion;
        private bool _isSettingsVisible = true;
        private string _previewStatusText = string.Empty;
        private static readonly SemaphoreSlim PreviewSemaphore = new(3);
        // Tabs may target identical output filenames; keep each conversion batch together.
        private static readonly SemaphoreSlim ConversionSemaphore = new(1);

        private string _workspaceTitle = "Tambahkan file";
        public string WorkspaceTitle { get => SelectedTool?.Title??_workspaceTitle; init => _workspaceTitle=value; }
        public bool IsSettingsVisible
        {
            get => _isSettingsVisible;
            set
            {
                if (SetProperty(ref _isSettingsVisible, value))
                    OnPropertyChanged(nameof(SettingsToggleText));
                    OnPropertyChanged(nameof(RightPanelWidth));
            }
        }
        public string SettingsToggleText => HasSelectedTool ? (IsSettingsVisible ? "Sembunyikan pengaturan" : "Tampilkan pengaturan") : (ToolsExpanded ? "Ringkas Tools" : "Tampilkan Tools");
        public string PreviewStatusText { get => _previewStatusText; private set => SetProperty(ref _previewStatusText, value); }
        public ICommand ToggleSettingsCommand { get; }

        private readonly ObservableCollection<PdfJobResult> _jobResults=new();
        public ReadOnlyObservableCollection<PdfJobResult> JobResults { get; }
        public string LastOutputDirectory { get; private set; } = "";
        public ICommand CancelConversionCommand { get; }
        public ICommand OpenResultCommand { get; }
        public ReadOnlyObservableCollection<PdfFileItem> PdfFiles { get; }
        public PdfReaderViewModel Reader { get; }
        public ReadOnlyObservableCollection<string> DpiItems { get; }

        public bool HasFiles => _pdfFiles.Count > 0;
        public bool HasNoFiles => _pdfFiles.Count == 0;
        public string FileCountText => $"{_pdfFiles.Count} file{(_pdfFiles.Count != 1 ? "s" : "")}";
        public string ConvertButtonText => IsConverting ? "Memproses..." : IsImageToolSelected ? "CONVERT" : SelectedTool?.Title ?? "Pilih tool";

        public int SelectedFormatIndex
        {
            get => _selectedFormatIndex;
            set => SetProperty(ref _selectedFormatIndex, value);
        }

        public string SelectedFormat => SelectedFormatIndex == 0 ? "Jpg" : "Png";

        public bool ConvertAllPages
        {
            get => _convertAllPages;
            set
            {
                if (SetProperty(ref _convertAllPages, value))
                    OnPropertyChanged(nameof(ConvertSpecificPages));
            }
        }

        public bool ConvertSpecificPages
        {
            get => !ConvertAllPages;
            set
            {
                if (value)
                    ConvertAllPages = false;
            }
        }

        public bool CanSelectSpecificPages => _pdfFiles.Count == 1;
        public bool HasMultipleFiles => _pdfFiles.Count > 1;

        public string PageRangeText
        {
            get => _pageRangeText;
            set => SetProperty(ref _pageRangeText, value);
        }

        public int SelectedDpiIndex
        {
            get => _selectedDpiIndex;
            set
            {
                if (SetProperty(ref _selectedDpiIndex, value))
                {
                    int[] dpiValues = { 72, 144, 216, 300, 600, -1 };
                    if (value >= 0 && value < dpiValues.Length)
                    {
                        if (_dpiItems[5] != CustomDpiPlaceholder)
                            _dpiItems[5] = CustomDpiPlaceholder;
                        SelectDpi(dpiValues[value]);
                    }
                }
            }
        }

        public int SelectedDpi
        {
            get => _selectedDpi;
            set => SetProperty(ref _selectedDpi, value);
        }

        public string CustomDpiText
        {
            get => _customDpiText;
            set => SetProperty(ref _customDpiText, value);
        }

        public bool IsCustomDpiVisible
        {
            get => _isCustomDpiVisible;
            set => SetProperty(ref _isCustomDpiVisible, value);
        }

        public string CustomDpiButtonText
        {
            get => _customDpiButtonText;
            set => SetProperty(ref _customDpiButtonText, value);
        }

        public int QualityValue
        {
            get => _qualityValue;
            set
            {
                if (SetProperty(ref _qualityValue, value))
                {
                    QualityText = $"{value}%";
                }
            }
        }

        public string QualityText
        {
            get => _qualityText;
            set => SetProperty(ref _qualityText, value);
        }

        public string OutputDirPath
        {
            get => _outputDirPath;
            set => SetProperty(ref _outputDirPath, value);
        }

        public int NamingIndex
        {
            get => _namingIndex;
            set => SetProperty(ref _namingIndex, value);
        }

        public bool CanConvert
        {
            get => _canConvert && SelectedTool?.IsAvailable == true && (IsPageImageMode || _officeTools?.IsAvailable == true);
            set => SetProperty(ref _canConvert, value);
        }

        public bool IsConverting
        {
            get => _isConverting;
            set
            {
                if (SetProperty(ref _isConverting, value))
                    OnPropertyChanged(nameof(ConvertButtonText));
            }
        }

        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        public string ProgressPctText
        {
            get => _progressPctText;
            set => SetProperty(ref _progressPctText, value);
        }

        public bool IsProgressIndeterminate
        {
            get => _isProgressIndeterminate;
            set => SetProperty(ref _isProgressIndeterminate, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string CombinedStatusText
        {
            get => _combinedStatusText;
            set => SetProperty(ref _combinedStatusText, value);
        }

        public bool HasConverted
        {
            get => _hasConverted;
            set
            {
                if (SetProperty(ref _hasConverted, value))
                    OnPropertyChanged(nameof(ShouldShowJobResults));
            }
        }

        public bool ShouldShowJobResults =>
            HasConverted && (_jobResults.Count > 1 || _jobResults.Any(r => r.Status != "Berhasil"));

        public PdfFileItem? FullScreenItem
        {
            get => _fullScreenItem;
            set
            {
                if (SetProperty(ref _fullScreenItem, value))
                    OnPropertyChanged(nameof(IsFullScreenVisible));
            }
        }

        public bool IsFullScreenVisible
        {
            get => _isFullScreenVisible;
            set => SetProperty(ref _isFullScreenVisible, value);
        }

        public BitmapSource? FullScreenPreviewImage
        {
            get => _fullScreenPreviewImage;
            set => SetProperty(ref _fullScreenPreviewImage, value);
        }

        private string _statusIconGlyph = "Info";
        public string StatusIconGlyph
        {
            get => _statusIconGlyph;
            set => SetProperty(ref _statusIconGlyph, value);
        }

        private bool _isStatusIconSpinning;
        public bool IsStatusIconSpinning
        {
            get => _isStatusIconSpinning;
            set => SetProperty(ref _isStatusIconSpinning, value);
        }

        public ICommand SelectFileCommand { get; }
        public ICommand RemoveFileCommand { get; }
        public ICommand SelectDpiCommand { get; }
        public ICommand ApplyCustomDpiCommand { get; }
        public ICommand BrowseOutputDirCommand { get; }
        public ICommand ConvertCommand { get; }
        public ICommand OpenOutputDirCommand { get; }
        public ICommand ShowFullScreenCommand { get; }
        public ICommand CloseFullScreenCommand { get; }

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

        public PdfUtilitiesViewModel(
            IPdfConversionService pdfConversionService,
            IPdfEngineService pdfEngineService)
            : this(pdfConversionService, pdfEngineService, null)
        {
        }

        public PdfUtilitiesViewModel(
            IPdfConversionService pdfConversionService,
            IPdfEngineService pdfEngineService,
            INotificationService? notificationService,
            IPdfOfficeToolService? officeTools = null)
        {
            _pdfConversionService = pdfConversionService;
            _pdfEngineService = pdfEngineService;
            _notificationService = notificationService;
            _officeTools = officeTools;
            InitializeTools();

            PdfFiles = new ReadOnlyObservableCollection<PdfFileItem>(_pdfFiles);
            Reader=new PdfReaderViewModel(_pdfEngineService,PdfFiles);
            Reader.PropertyChanged+=(_,e)=>
            {
                if(e.PropertyName==nameof(PdfReaderViewModel.ActiveDocument)&&Reader.ActiveDocument is {} active)
                { _workspaceTitle=active.Name;OnPropertyChanged(nameof(WorkspaceTitle)); }
            };
            DpiItems = new ReadOnlyObservableCollection<string>(_dpiItems);

            SelectFileCommand = new RelayCommand(SelectFile, () => !_isClosed && !IsConverting);
            RemoveFileCommand = new RelayCommand<PdfFileItem>(RemoveFile, _ => !_isClosed && !IsConverting);
            SelectDpiCommand = new RelayCommand<int>(SelectDpi);
            ApplyCustomDpiCommand = new RelayCommand(ApplyCustomDpi);
            BrowseOutputDirCommand = new RelayCommand(BrowseOutputDir);
            ConvertCommand = new AsyncRelayCommand(StartConversionAsync, () => !_isClosed && !IsConverting && CanConvert);
            OpenOutputDirCommand = new RelayCommand(OpenOutputDir);
            ShowFullScreenCommand = new RelayCommand<PdfFileItem>(ShowFullScreen);
            CloseFullScreenCommand = new RelayCommand(CloseFullScreen);
            JobResults=new(_jobResults);
            CancelConversionCommand=new RelayCommand(()=>_conversionCts?.Cancel(),()=>IsConverting);
            OpenResultCommand=new RelayCommand<string>(path=>
            {
                if(path==null||!_jobResults.SelectMany(x=>x.Outputs).Contains(path,StringComparer.OrdinalIgnoreCase)||!File.Exists(path))return;
                try{Process.Start(new ProcessStartInfo{FileName=path,UseShellExecute=true});}
                catch(Exception ex){ShowInfoBar("Hasil tidak dapat dibuka",ex.Message,InfoBarSeverity.Error);}
            });
            ToggleSettingsCommand = new RelayCommand(() => { if(HasSelectedTool) IsSettingsVisible = !IsSettingsVisible; else ToolsExpanded = !ToolsExpanded; });
        }

        public void AddFiles(string[] paths)
        {
            if (_isClosed || IsConverting) return;
            int addedCount = 0;
            var knownPaths = _pdfFiles.Select(x=>x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rejected = new List<string>();
            foreach (var path in paths)
            {
                string canonical;
                try { canonical=Path.GetFullPath(path); }
                catch (Exception ex) when(ex is ArgumentException or IOException or NotSupportedException)
                { rejected.Add("Path file tidak valid."); continue; }
                if (!File.Exists(canonical)) { rejected.Add($"File tidak ditemukan: {Path.GetFileName(canonical)}"); continue; }
                if (!Path.GetExtension(canonical).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                { rejected.Add($"{Path.GetFileName(canonical)}: format belum didukung. Tambahkan PDF; image/Office memerlukan engine berikutnya."); continue; }
                if (!knownPaths.Add(canonical)) { rejected.Add($"{Path.GetFileName(canonical)} sudah ada dalam workspace."); continue; }

                var item = new PdfFileItem
                {
                    Path = canonical,
                    Name = Path.GetFileName(canonical),
                    IsLoading = true,
                    StatusText = "Memuat...",
                    StatusForeground = GrayBrush
                };
                _pdfFiles.Add(item);
                addedCount++;
            }
            if(rejected.Count>0) ShowInfoBar("Impor file",string.Join(Environment.NewLine,rejected.Take(5)),InfoBarSeverity.Warning);

            if (addedCount == 0) return;

            _totalPages = 0;
            UpdateCombinedStatus();
            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(HasNoFiles));
            OnPropertyChanged(nameof(FileCountText));
            OnPropertyChanged(nameof(CanSelectSpecificPages));
            OnPropertyChanged(nameof(HasMultipleFiles));

            if (_pdfFiles.Count != 1)
                ConvertAllPages = true;

            if (string.IsNullOrWhiteSpace(OutputDirPath) && _pdfFiles.Count > 0)
            {
                OutputDirPath = Path.Combine(Path.GetDirectoryName(_pdfFiles[0].Path)??string.Empty,"PDF_Output");
            }

            if(_pdfFiles.Count==addedCount)
            {
                _workspaceTitle=_pdfFiles[0].Name;
                OnPropertyChanged(nameof(WorkspaceTitle));
            }
            RefreshToolInput();

            var newItems = _pdfFiles.TakeLast(addedCount).ToList();
            _metadataTask = Task.WhenAll(_metadataTask,LoadFilesAsync(newItems));
        }

        private async Task LoadFilesAsync(List<PdfFileItem> items)
        {
            var tasks = items.Select(item => LoadFileAsync(item));
            await Task.WhenAll(tasks);

            int totalPages = 0;
            foreach (var item in _pdfFiles)
            {
                totalPages += item.PageCount;
            }
            _totalPages = totalPages;
            UpdateCombinedStatus();
            RefreshToolInput();
            if(!_isClosed&&IsReaderWorkspace&&Reader.ActiveDocument==null&&_pdfFiles.FirstOrDefault(x=>x.IsReady) is {} first)
                await Reader.OpenDocumentAsync(first);
        }

        private async Task LoadFileAsync(PdfFileItem item)
        {
            bool ownsSlot=false;
            try
            {
                await PreviewSemaphore.WaitAsync(_workspaceCts.Token);
                ownsSlot=true;
                if (_isClosed) return;
                var fi = new FileInfo(item.Path);

                int pageCount = await Task.Run(() => _pdfEngineService.GetPageCount(item.Path));
                if (_isClosed || !_pdfFiles.Contains(item)) return;
                if(pageCount<=0) throw new InvalidDataException("PDF tidak memiliki halaman yang dapat dibaca.");

                string dimensionsCm = await Task.Run(() =>
                {
                    try
                    {
                        var sizes = _pdfEngineService.GetPageSizes(item.Path, _workspaceCts.Token);
                        if (sizes != null && sizes.Count > 0 && sizes[0].Width > 0 && sizes[0].Height > 0)
                        {
                            double wCm = sizes[0].Width * 2.54 / 72.0;
                            double hCm = sizes[0].Height * 2.54 / 72.0;
                            return $"{PdfFileItem.FormatCmValue(wCm)} × {PdfFileItem.FormatCmValue(hCm)} cm";
                        }
                    }
                    catch { }
                    return string.Empty;
                });
                if (_isClosed || !_pdfFiles.Contains(item)) return;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    item.SizeText = FormatFileSize(fi.Length);
                    item.PageCount = pageCount;
                    item.PageCountText = $"{pageCount} halaman";
                    item.DimensionsCmText = dimensionsCm;
                    item.DateText = fi.LastWriteTime.ToString("dd/MM/yyyy");
                }).Task;

                var targetSize = await GetPreviewPixelSizeAsync(
                    "PdfCardPreviewImage", item, fallbackWidth: 360, fallbackHeight: 320);
                var result = await Task.Run(() => RenderPreview(item.Path, targetSize));

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_isClosed || !_pdfFiles.Contains(item)) return;
                    if (result != null)
                    {
                        var bmp = BitmapSource.Create(
                            result.Width, result.Height,
                            96, 96,
                            PixelFormats.Bgra32, null,
                            result.Pixels, result.Stride);
                        bmp.Freeze();
                        item.PreviewImage = bmp;
                        item.HasPreviewFailed = false;
                        item.StatusText = "Siap";
                        item.StatusForeground = GreenBrush;
                    }
                    else
                    {
                        item.HasPreviewFailed = true;
                        item.StatusText = "Preview tidak tersedia";
                        item.StatusForeground = GrayBrush;
                    }
                }).Task;
            }
            catch (OperationCanceledException) when(_workspaceCts.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (_isClosed || !_pdfFiles.Contains(item)) return;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    item.HasPreviewFailed = true;
                    item.ImportError = $"PDF tidak dapat dibaca: {ex.Message}";
                    item.StatusText = "Gagal membaca PDF";
                    item.StatusForeground = RedBrush;
                }).Task;
            }
            finally
            {
                item.IsLoading=false;
                if(ownsSlot) PreviewSemaphore.Release();
            }
        }

        private void SelectFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*",
                Title = "Pilih File PDF",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                AddFiles(dialog.FileNames);
            }
        }

        private async void RemoveFile(PdfFileItem? item)
        {
            if (item == null || _isClosed || IsConverting) return;

            if (IsFullScreenVisible && FullScreenItem == item)
                CloseFullScreen();

            _totalPages -= item.PageCount;

            _pdfFiles.Remove(item);
            item.PreviewImage=null;
            if(ReferenceEquals(Reader.ActiveDocument,item))await Reader.OpenDocumentAsync(_pdfFiles.FirstOrDefault(x=>x.IsReady));
            if(_pdfFiles.Count==0){_workspaceTitle="Tambahkan file";OnPropertyChanged(nameof(WorkspaceTitle));}
            RefreshToolInput();
            UpdateCombinedStatus();
            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(HasNoFiles));
            OnPropertyChanged(nameof(CanConvert));
            OnPropertyChanged(nameof(FileCountText));
            OnPropertyChanged(nameof(CanSelectSpecificPages));
            OnPropertyChanged(nameof(HasMultipleFiles));
            ConvertAllPages = true;

            if (_pdfFiles.Count == 0)
            {
                HasConverted = false;
                IsConverting = false;
                IsProgressIndeterminate = false;
                ProgressValue = 0;
                ProgressPctText = "0%";
                StatusText = "Belum ada proses konversi";
                StatusIconGlyph = "Info";
                IsStatusIconSpinning = false;
                CanConvert = false;
            }
        }

        private void SelectDpi(int dpi)
        {
            if (dpi == -1)
            {
                IsCustomDpiVisible = true;
                CustomDpiButtonText = "Custom";
                SelectedDpi = -1;
            }
            else
            {
                IsCustomDpiVisible = false;
                CustomDpiButtonText = "Custom";
                SelectedDpi = dpi;
            }
        }

        private void ApplyCustomDpi()
        {
            if (int.TryParse(CustomDpiText, out int value) && value >= 72 && value <= 2400)
            {
                SelectedDpi = value;
                CustomDpiButtonText = $"Custom ({value})";
                IsCustomDpiVisible = false;
                _dpiItems[5] = $"{value} DPI";
                _selectedDpiIndex = 5;
                OnPropertyChanged(nameof(SelectedDpiIndex));
                HideInfoBar();
            }
            else
            {
                ShowInfoBar("DPI Tidak Valid", "DPI harus berada pada rentang 72 sampai 2400.", InfoBarSeverity.Warning);
                _notificationService?.ShowWarning("DPI Tidak Valid", "DPI harus berada pada rentang 72 sampai 2400.");
            }
        }

        private void BrowseOutputDir()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Pilih Folder Output",
                InitialDirectory = string.IsNullOrWhiteSpace(OutputDirPath) ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) : OutputDirPath
            };

            if (dialog.ShowDialog() == true)
            {
                OutputDirPath = dialog.FolderName;
            }
        }

        private void ShowFullScreen(PdfFileItem? item)
        {
            if (item == null || _isClosed) return;
            FullScreenItem = item;
            FullScreenPreviewImage = null;
            PreviewStatusText = "Memuat preview...";
            IsFullScreenVisible = true;
            _ = LoadFullScreenPreviewAsync(item, ++_previewRequestVersion);
        }

        private async Task LoadFullScreenPreviewAsync(PdfFileItem item, int requestVersion)
        {
            await PreviewSemaphore.WaitAsync();
            try
            {
                if (_isClosed || requestVersion != _previewRequestVersion || !IsFullScreenVisible || !ReferenceEquals(FullScreenItem, item)) return;
                var targetSize = await GetPreviewPixelSizeAsync(
                    "PdfPreviewViewport", this, fallbackWidth: 1200, fallbackHeight: 800);
                var result = await Task.Run(() => RenderPreview(item.Path, targetSize));

                if (_isClosed || requestVersion != _previewRequestVersion || !IsFullScreenVisible || !ReferenceEquals(FullScreenItem, item))
                    return;
                if (result == null)
                {
                    PreviewStatusText = "Preview tidak tersedia. File mungkin rusak atau tidak dapat dibaca.";
                    return;
                }

                var bitmap = BitmapSource.Create(
                    result.Width, result.Height,
                    96, 96,
                    PixelFormats.Bgra32, null,
                    result.Pixels, result.Stride);
                bitmap.Freeze();
                FullScreenPreviewImage = bitmap;
                PreviewStatusText = string.Empty;
            }
            catch (Exception)
            {
                if (!_isClosed && requestVersion == _previewRequestVersion && IsFullScreenVisible && ReferenceEquals(FullScreenItem, item))
                    PreviewStatusText = "Preview tidak tersedia. File mungkin rusak atau tidak dapat dibaca.";
            }
            finally
            {
                PreviewSemaphore.Release();
            }
        }

        private void CloseFullScreen()
        {
            ++_previewRequestVersion;
            IsFullScreenVisible = false;
            FullScreenItem = null;
            FullScreenPreviewImage = null;
            PreviewStatusText = string.Empty;
        }

        private PdfPageImageResult? RenderPreview(string path, Size targetSize)
        {
            int targetWidth = Math.Max(1, (int)targetSize.Width);
            int targetHeight = Math.Max(1, (int)targetSize.Height);

            if (_pdfEngineService is PdfEngineService pdfEngine)
                return pdfEngine.GetPageImage(path, targetWidth, targetHeight);

            // Preserve compatibility with alternate IPdfEngineService implementations.
            return _pdfEngineService.GetPageImage(path, targetWidth);
        }

        private static Task<Size> GetPreviewPixelSizeAsync(
            string elementName,
            object? item,
            double fallbackWidth,
            double fallbackHeight)
        {
            return Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var root = Application.Current.MainWindow;
                var element = root == null ? null : FindVisualElement(root, elementName, item);
                var dpiTarget = (Visual?)element ?? root;
                double scaleX = dpiTarget == null ? 1.0 : VisualTreeHelper.GetDpi(dpiTarget).DpiScaleX;
                double scaleY = dpiTarget == null ? 1.0 : VisualTreeHelper.GetDpi(dpiTarget).DpiScaleY;
                double width = element?.ActualWidth > 0 ? element.ActualWidth : fallbackWidth;
                double height = element?.ActualHeight > 0 ? element.ActualHeight : fallbackHeight;

                return new Size(
                    Math.Max(1, Math.Ceiling(width * scaleX)),
                    Math.Max(1, Math.Ceiling(height * scaleY)));
            }, DispatcherPriority.Render).Task;
        }

        private static FrameworkElement? FindVisualElement(
            DependencyObject parent,
            string elementName,
            object? item)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement element &&
                    element.Name == elementName &&
                    (item == null || ReferenceEquals(element.DataContext, item)))
                {
                    return element;
                }

                var descendant = FindVisualElement(child, elementName, item);
                if (descendant != null)
                    return descendant;
            }

            return null;
        }

        internal Task StartConversionAsync()
        {
            if (_isClosed || IsConverting) return _conversionTask;
            if(!ValidateSelectedTool()) return Task.CompletedTask;
            if(_pdfFiles.Any(x=>x.IsLoading)||!_pdfFiles.Any(x=>x.IsReady))
            {
                ShowInfoBar("File belum siap","Tunggu validasi PDF selesai dan pilih file yang dapat dibaca.",InfoBarSeverity.Informational);
                return Task.CompletedTask;
            }
            return _conversionTask = IsPageImageMode ? ConvertPdfAsync() : ConvertOfficeToolAsync();
        }

        internal async Task CloseWorkspaceAsync()
        {
            _isClosed = true;
            _workspaceCts.Cancel();
            _conversionCts?.Cancel();
            await _conversionTask;
            await _metadataTask;
            await Reader.CloseAsync();
            CloseFullScreen();
            foreach (var item in _pdfFiles) item.PreviewImage = null;
            _pdfFiles.Clear();
        }

        private async Task ConvertPdfAsync()
        {
            var sources=_pdfFiles.Where(x=>x.IsReady).Select(x=>(Item:x,Path:x.Path,Name:x.Name,Pages:x.PageCount)).ToArray();
            if(sources.Length==0)return;
            IReadOnlyList<int>? selectedPages=null;
            if(ConvertSpecificPages&&sources.Length==1&&!TryParsePageRange(PageRangeText,sources[0].Pages,out selectedPages,out string rangeError))
            {ShowInfoBar("Pilihan Halaman Tidak Valid",rangeError,InfoBarSeverity.Warning);return;}
            int dpi=SelectedDpi;
            if(dpi==-1&&!int.TryParse(CustomDpiText,out dpi)||dpi<=0)
            {ShowInfoBar("Resolusi tidak valid","Masukkan DPI positif.",InfoBarSeverity.Warning);return;}
            string format=SelectedFormat;
            int quality=QualityValue,naming=NamingIndex;
            string outputRoot=string.IsNullOrWhiteSpace(OutputDirPath)?Path.Combine(Path.GetDirectoryName(sources[0].Path)??"","PDF_Output"):OutputDirPath;
            string outputDirectory;
            try{outputDirectory=Path.GetFullPath(outputRoot);}
            catch(Exception ex){ShowInfoBar("Folder tidak valid",ex.Message,InfoBarSeverity.Warning);return;}
            var pageSnapshot=selectedPages==null?null:Array.AsReadOnly(selectedPages.ToArray());
            var results=sources.Select(source=>new PdfJobResult(source.Path,source.Name,outputDirectory)).ToArray();
            _jobResults.Clear();foreach(var result in results)_jobResults.Add(result);
            LastOutputDirectory=outputDirectory;
            HideInfoBar();CanConvert=false;IsConverting=true;HasConverted=false;ProgressValue=0;ProgressPctText="0%";
            StatusIconGlyph="Sync";IsStatusIconSpinning=true;
            int totalPages=pageSnapshot?.Count??sources.Sum(x=>x.Pages);
            IsProgressIndeterminate=totalPages<=1;
            _conversionCts=new();var jobCts=_conversionCts;var token=jobCts.Token;
            bool ownsSlot=false;
            try
            {
                StatusText="Menunggu antrean konversi...";
                await ConversionSemaphore.WaitAsync(token);ownsSlot=true;
                PdfPreflightResult? highestRisk=null;
                StatusText="Memeriksa keamanan resolusi...";
                try
                {
                    foreach(var source in sources)
                    {
                        var preflight=await _pdfConversionService.PreflightAsync(source.Path,format,dpi,pageSnapshot,token);
                        token.ThrowIfCancellationRequested();
                        if(preflight.FailureReason==PdfPreflightFailureReason.PageDimensionsUnavailable)throw new InvalidOperationException("Ukuran halaman PDF tidak dapat ditentukan dengan aman.");
                        if(!preflight.IsSafe&&(highestRisk==null||preflight.EstimatedMemoryBytes>highestRisk.EstimatedMemoryBytes))highestRisk=preflight;
                    }
                }
                catch(OperationCanceledException){throw;}
                catch(Exception ex){foreach(var result in results)result.Status="Belum diproses: "+ex.Message;StopAfterPreflightFailure("Ukuran halaman PDF tidak dapat ditentukan dengan aman. Ekspor dibatalkan.",true);return;}
                if(highestRisk!=null)
                {
                    string message="Resolusi PDF terlalu tinggi untuk diekspor dengan aman pada pengaturan saat ini.";
                    if(highestRisk.RecommendedMaxDpi is int safe)message+=$" Disarankan menggunakan maksimal {safe} DPI.";
                    foreach(var result in results)result.Status="Belum diproses: "+message;
                    StopAfterPreflightFailure(message,false);return;
                }
                int completed=0,failed=0;
                for(int i=0;i<sources.Length;i++)
                {
                    token.ThrowIfCancellationRequested();
                    var source=sources[i];var result=results[i];
                    int count=pageSnapshot?.Count??source.Pages;
                    source.Item.StatusText=result.Status="Mengonversi...";source.Item.StatusForeground=GrayBrush;
                    int before=completed;
                    var progress=new PdfJobProgress(report=>
                    {
                        if(token.IsCancellationRequested||!IsConverting||!ReferenceEquals(_conversionCts,jobCts))return;
                        int done=report.PhaseText=="Selesai"?count:pageSnapshot==null
                            ?Math.Max(0,report.CurrentPage-(string.IsNullOrEmpty(report.OutputFilePath)?1:0))
                            :pageSnapshot.Count(x=>x<report.CurrentPage||x==report.CurrentPage&&!string.IsNullOrEmpty(report.OutputFilePath));
                        ProgressValue=Math.Clamp(100.0*(before+Math.Clamp(done,0,count))/Math.Max(1,totalPages),0,100);
                        ProgressPctText=$"{(int)ProgressValue}%";StatusText=$"{source.Name}: {report.PhaseText}";
                    });
                    try
                    {
                        await _pdfConversionService.ConvertAsync(source.Path,result.OutputDirectory,format,dpi,quality,naming,pageSnapshot,progress,token);
                        token.ThrowIfCancellationRequested();
                        source.Item.StatusText=result.Status="Berhasil";source.Item.StatusForeground=GreenBrush;
                    }
                    catch(OperationCanceledException){source.Item.StatusText=result.Status="Dibatalkan";throw;}
                    catch(Exception ex){failed++;source.Item.StatusText="Gagal";result.Status="Gagal: "+ex.Message;source.Item.StatusForeground=RedBrush;}
                    finally
                    {
                        result.Outputs=Array.AsReadOnly(progress.Outputs.Where(path=>File.Exists(path)&&
                            string.Equals(Path.GetDirectoryName(path),outputDirectory,StringComparison.OrdinalIgnoreCase))
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
                        foreach(var earlier in results.Take(i))
                        {
                            var retained=earlier.Outputs.Except(result.Outputs,StringComparer.OrdinalIgnoreCase).ToArray();
                            if(retained.Length==earlier.Outputs.Count)continue;
                            earlier.Outputs=Array.AsReadOnly(retained);
                            earlier.Status+="; hasil dengan nama sama ditimpa file berikutnya";
                        }
                    }
                    completed+=count;
                }
                ProgressValue=100;ProgressPctText="100%";
                StatusText=failed==0?"Selesai! Semua file tersimpan.":$"Selesai dengan {failed} file gagal. Periksa hasil per file.";
                StatusIconGlyph=failed==0?"Check":"Error";
            }
            catch(OperationCanceledException)
            {StatusText="Konversi dibatalkan. Hasil halaman yang sudah selesai tetap tersedia.";StatusIconGlyph="Info";foreach(var result in results.Where(x=>x.Status=="Menunggu"))result.Status="Belum diproses — pekerjaan dibatalkan";}
            catch(Exception ex)
            {StatusText=$"Error: {ex.Message}";StatusIconGlyph="Error";foreach(var result in results.Where(x=>x.Status=="Menunggu"))result.Status="Belum diproses: "+ex.Message;}
            finally
            {
                if(ownsSlot)ConversionSemaphore.Release();
                _conversionCts.Dispose();_conversionCts=null;
                HasConverted=results.Any(x=>x.HasOutputs);IsConverting=false;IsProgressIndeterminate=false;IsStatusIconSpinning=false;
                RefreshToolInput();CommandManager.InvalidateRequerySuggested();
            }
        }

        // Capture committed pages synchronously; UI progress is dispatched and may arrive after cancellation.
        private sealed class PdfJobProgress(Action<PdfConversionProgressReport> callback) : Progress<PdfConversionProgressReport>(callback)
        {
            public ConcurrentQueue<string> Outputs { get; } = new();
            protected override void OnReport(PdfConversionProgressReport report)
            {
                if(!string.IsNullOrEmpty(report.OutputFilePath))Outputs.Enqueue(report.OutputFilePath);
                base.OnReport(report);
            }
        }

        private void StopAfterPreflightFailure(string message, bool isError)
        {
            IsProgressIndeterminate = false;
            StatusText = message;
            StatusIconGlyph = isError ? "Error" : "Info";
            IsStatusIconSpinning = false;

            foreach (var file in _pdfFiles.Where(file => file.StatusText == "Mengonversi..."))
            {
                file.StatusText = "Siap";
                file.StatusForeground = GrayBrush;
            }

            if (isError)
                _notificationService?.ShowError("Ekspor dibatalkan", message);
            else
                _notificationService?.ShowWarning("Resolusi PDF terlalu tinggi", message);
        }

        private static bool TryParsePageRange(
            string text,
            int pageCount,
            out IReadOnlyList<int>? pages,
            out string errorMessage)
        {
            pages = null;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                errorMessage = "Masukkan halaman, misalnya 1-3,5,8.";
                return false;
            }

            var selectedPages = new SortedSet<int>();
            foreach (string rawPart in text.Split(','))
            {
                string part = rawPart.Trim();
                if (part.Length == 0)
                {
                    errorMessage = "Format halaman tidak valid. Gunakan format seperti 1-3,5,8.";
                    return false;
                }

                string[] range = part.Split('-');
                if (range.Length == 1)
                {
                    if (!TryValidatePage(range[0], pageCount, out int page, out errorMessage))
                        return false;

                    selectedPages.Add(page);
                    continue;
                }

                if (range.Length != 2 ||
                    !TryValidatePage(range[0], pageCount, out int start, out errorMessage) ||
                    !TryValidatePage(range[1], pageCount, out int end, out errorMessage))
                {
                    if (string.IsNullOrEmpty(errorMessage))
                        errorMessage = "Format halaman tidak valid. Gunakan format seperti 1-3,5,8.";
                    return false;
                }

                if (start > end)
                {
                    errorMessage = $"Rentang {start}-{end} tidak valid karena halaman awal melebihi halaman akhir.";
                    return false;
                }

                for (int page = start; page <= end; page++)
                    selectedPages.Add(page);
            }

            pages = selectedPages.ToList();
            return true;
        }

        private static bool TryValidatePage(string text, int pageCount, out int page, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!int.TryParse(text.Trim(), out page))
            {
                errorMessage = "Format halaman tidak valid. Gunakan angka dan rentang seperti 1-3,5,8.";
                return false;
            }

            if (page <= 0)
            {
                errorMessage = "Nomor halaman harus dimulai dari 1.";
                return false;
            }

            if (page > pageCount)
            {
                errorMessage = $"Halaman {page} melebihi jumlah halaman PDF ({pageCount}).";
                return false;
            }

            return true;
        }

        private void OpenOutputDir()
        {
            if (!string.IsNullOrWhiteSpace(LastOutputDirectory) && Directory.Exists(LastOutputDirectory))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = LastOutputDirectory,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    _notificationService?.ShowError("Error", $"Gagal membuka folder output: {ex.Message}");
                }
            }
        }

        private void UpdateCombinedStatus()
        {
            int totalFiles = _pdfFiles.Count;
            int readyCount = _pdfFiles.Count(f => f.StatusText == "Siap");
            int errorCount = _pdfFiles.Count(f => f.StatusText == "Gagal" || f.StatusText.StartsWith("Gagal"));

            if (totalFiles == 0)
                CombinedStatusText = "Belum ada file dipilih";
            else if (errorCount > 0)
                CombinedStatusText = $"{totalFiles} file, {errorCount} error";
            else
                CombinedStatusText = $"{totalFiles} file siap dikonversi";
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1048576) return $"{bytes / 1048576.0:N1} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:N0} KB";
            return $"{bytes} B";
        }
    }
}
