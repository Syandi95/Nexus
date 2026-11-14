using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexus.PdfUtilities;

namespace Nexus.App.ViewModels;

public enum PdfReaderFitMode { FitPage, FitWidth, Custom }

public sealed record PdfAttachmentItem(string FileName, string SizeText);

public sealed class PdfReaderDocumentState
{
    public int PageNumber { get; set; } = 1;
    public int ZoomPercent { get; set; } = 100;
    public PdfReaderFitMode FitMode { get; set; } = PdfReaderFitMode.FitPage;
    public int Rotation { get; set; }
}

public sealed class PdfReaderPageViewModel(int index,PdfPageSize size) : ViewModelBase
{
    private BitmapSource? _image;
    private BitmapSource? _thumbnail;
    private double _width,_height;
    private int _rotation;
    private bool _isCurrentPage;
    private string _status="Memuat halaman...";
    internal bool NeedsRender { get; set; } = true;
    internal bool HasDedicatedThumbnail => _thumbnail != null;
    public int Index { get; } = index;
    public int PageNumber => Index + 1;
    public PdfPageSize Size { get; } = size;
    public string Label => $"Halaman {Index+1}";
    public BitmapSource? Image
    {
        get => _image;
        internal set
        {
            if (SetProperty(ref _image, value))
            {
                OnPropertyChanged(nameof(Thumbnail));
            }
        }
    }
    public BitmapSource? Thumbnail
    {
        get => _thumbnail ?? _image;
        internal set => SetProperty(ref _thumbnail, value);
    }
    public bool IsCurrentPage
    {
        get => _isCurrentPage;
        internal set => SetProperty(ref _isCurrentPage, value);
    }
    public double Width { get => _width; internal set => SetProperty(ref _width,value); }
    public double Height { get => _height; internal set => SetProperty(ref _height,value); }
    public int Rotation { get => _rotation; internal set => SetProperty(ref _rotation,value); }
    public string Status { get => _status; internal set => SetProperty(ref _status,value); }
}

public sealed class PdfReaderViewModel : ViewModelBase
{
    private readonly IPdfEngineService _engine;
    private readonly ReadOnlyObservableCollection<PdfFileItem> _sources;
    private readonly ObservableCollection<PdfReaderPageViewModel> _pages=new();
    private readonly HashSet<Task> _pending=new();
    private readonly HashSet<(int Page,int Version)> _inflight=new();
    private HashSet<int> _visible=new();
    private static readonly SemaphoreSlim NativeSlot=new(1);
    private CancellationTokenSource? _documentCts,_renderCts;
    private PdfFileItem? _activeDocument;
    private int _documentVersion,_renderVersion;
    private bool _closed,_suspended,_handMode;
    private double _viewportWidth=800,_viewportHeight=700,_dpi=1;
    private string _status="Tambahkan file PDF untuk melihat dokumen.";

    // Batch 2: Left Collapsible PDF Reader Sidebar State (iLovePDF Desktop parity)
    private bool _isLeftSidebarExpanded;
    private int _selectedSidebarTabIndex;
    private string _docInfoVersion = "1.4";
    private string _docInfoFileSize = "-";
    private string _docInfoCreationDate = "-";
    private string _docInfoModifiedDate = "-";
    private string _docInfoAuthor = "-";
    private string _docInfoCreator = "-";
    private string _docInfoProducer = "-";
    private string _docInfoTitle = "-";
    private string _docInfoSubject = "-";
    private string _docInfoKeywords = "-";
    private bool _isPdfACompliant;

    // Print Dialog State (iLovePDF Desktop parity)
    private bool _isPrintDialogOpen;
    private string _selectedPrinter = string.Empty;
    private string _selectedPrintTray = "Auto";
    private int _selectedPaperSizeIndex;
    private int _printCopies = 1;
    private int _printPreviewPageNumber = 1;
    private BitmapSource? _printPreviewImage;
    private bool _isPrintAllPages = true;
    private bool _isPrintCurrentPage;
    private bool _isPrintPageRange;
    private string _printPageRangeText = string.Empty;
    private int _selectedPrintSubsetIndex;
    private bool _printInvertPages;
    private bool _isPrintOrientationAuto = true;
    private bool _isPrintOrientationPortrait;
    private bool _isPrintOrientationLandscape;
    private bool _isPrintSizeFit = true;
    private bool _isPrintSizeReal;
    private bool _isPrintSizeCustom;
    private int _printCustomScalePercent = 100;
    private bool _isPrintColor = true;
    private bool _isPrintGrayscale;
    private bool _isPrintDuplexNone = true;
    private bool _isPrintDuplexLongEdge;
    private bool _isPrintDuplexShortEdge;

    public ObservableCollection<PdfAttachmentItem> Attachments { get; } = new();
    public ObservableCollection<string> InstalledPrinters { get; } = new();
    public IReadOnlyList<string> PrintTrays { get; } = new[] { "Auto", "Upper tray", "Lower tray", "Manual feed" };
    public IReadOnlyList<string> PrintPaperSizes { get; } = new[]
    {
        "A4 (210 x 297 mm)",
        "Letter (216 x 279 mm)",
        "Legal (216 x 356 mm)",
        "A3 (297 x 420 mm)",
        "Ukuran Asli PDF"
    };
    public IReadOnlyList<string> PrintSubsetOptions { get; } = new[] { "All pages", "Odd pages only", "Even pages only" };

    public PdfReaderViewModel(IPdfEngineService engine,ReadOnlyObservableCollection<PdfFileItem> sources)
    {
        _engine=engine;_sources=sources;Pages=new(_pages);
        PreviousPageCommand=new RelayCommand(()=>PageNumber--,()=>PageNumber>1);
        NextPageCommand=new RelayCommand(()=>PageNumber++,()=>PageNumber<PageCount);
        ZoomInCommand=new RelayCommand(()=>ZoomPercent+=25,()=>HasDocument&&ZoomPercent<1000);
        ZoomOutCommand=new RelayCommand(()=>ZoomPercent-=25,()=>HasDocument&&ZoomPercent>10);
        RotateViewCommand=new RelayCommand<int>(degrees=>{if(_activeDocument==null)return;_activeDocument.ReaderState.Rotation=(_activeDocument.ReaderState.Rotation+degrees%360+360)%360;Reflow();},_=>HasDocument);
        ToggleHandCommand=new RelayCommand(()=>IsHandMode=!IsHandMode);

        ToggleLeftSidebarCommand=new RelayCommand(()=>IsLeftSidebarExpanded=!IsLeftSidebarExpanded);
        SelectSidebarTabCommand=new RelayCommand<string>(tabParam=>
        {
            int targetIdx = tabParam switch
            {
                "1" or "Attachments" => 1,
                "2" or "Information" => 2,
                _ => 0
            };
            if (IsLeftSidebarExpanded && SelectedSidebarTabIndex == targetIdx)
            {
                IsLeftSidebarExpanded = false;
            }
            else
            {
                SelectedSidebarTabIndex = targetIdx;
                IsLeftSidebarExpanded = true;
            }
        });
        SelectThumbnailPageCommand=new RelayCommand<int>(pageNum=>
        {
            if (pageNum >= 1 && pageNum <= PageCount)
                PageNumber = pageNum;
        });

        OpenPrintDialogCommand=new RelayCommand(OpenPrintDialog,()=>HasDocument&&PageCount>0);
        ClosePrintDialogCommand=new RelayCommand(()=>IsPrintDialogOpen=false);
        PreviousPrintPreviewPageCommand=new RelayCommand(()=>PrintPreviewPageNumber--,()=>PrintPreviewPageNumber>1);
        NextPrintPreviewPageCommand=new RelayCommand(()=>PrintPreviewPageNumber++,()=>PrintPreviewPageNumber<PageCount);
        ExecutePrintCommand=new RelayCommand(async ()=>await ExecutePrintAsync(),()=>HasDocument&&PageCount>0);
    }

    public ReadOnlyObservableCollection<PdfReaderPageViewModel> Pages { get; }
    public ReadOnlyObservableCollection<PdfFileItem> Sources => _sources;
    public PdfFileItem? ActiveDocument => _activeDocument;
    public bool HasDocument => _activeDocument!=null;
    public string DocumentName => ActiveDocument?.Name??"";
    public int PageCount => _pages.Count;
    public string Status { get => _status; private set => SetProperty(ref _status,value); }
    public bool IsHandMode { get => _handMode; set => SetProperty(ref _handMode,value); }

    // Batch 2: Left Collapsible Sidebar Properties
    public bool IsLeftSidebarExpanded
    {
        get => _isLeftSidebarExpanded;
        set
        {
            if (SetProperty(ref _isLeftSidebarExpanded, value))
            {
                OnPropertyChanged(nameof(IsThumbnailsTabExpanded));
                OnPropertyChanged(nameof(IsAttachmentsTabExpanded));
                OnPropertyChanged(nameof(IsInfoTabExpanded));
                if (value && _selectedSidebarTabIndex == 0)
                {
                    _ = EnsureSidebarThumbnailsAsync();
                }
            }
        }
    }
    public int SelectedSidebarTabIndex
    {
        get => _selectedSidebarTabIndex;
        set
        {
            int clamped = Math.Clamp(value, 0, 2);
            if (SetProperty(ref _selectedSidebarTabIndex, clamped))
            {
                OnPropertyChanged(nameof(IsThumbnailsTabActive));
                OnPropertyChanged(nameof(IsAttachmentsTabActive));
                OnPropertyChanged(nameof(IsInfoTabActive));
                OnPropertyChanged(nameof(IsThumbnailsTabExpanded));
                OnPropertyChanged(nameof(IsAttachmentsTabExpanded));
                OnPropertyChanged(nameof(IsInfoTabExpanded));
                if (_isLeftSidebarExpanded && clamped == 0)
                {
                    _ = EnsureSidebarThumbnailsAsync();
                }
            }
        }
    }
    public bool IsThumbnailsTabActive => _selectedSidebarTabIndex == 0;
    public bool IsAttachmentsTabActive => _selectedSidebarTabIndex == 1;
    public bool IsInfoTabActive => _selectedSidebarTabIndex == 2;
    public bool IsThumbnailsTabExpanded => _isLeftSidebarExpanded && _selectedSidebarTabIndex == 0;
    public bool IsAttachmentsTabExpanded => _isLeftSidebarExpanded && _selectedSidebarTabIndex == 1;
    public bool IsInfoTabExpanded => _isLeftSidebarExpanded && _selectedSidebarTabIndex == 2;
    public bool HasNoAttachments => Attachments.Count == 0;

    public string DocInfoFileType => "PDF";
    public string DocInfoVersion { get => _docInfoVersion; private set => SetProperty(ref _docInfoVersion, value); }
    public string DocInfoFileSize { get => _docInfoFileSize; private set => SetProperty(ref _docInfoFileSize, value); }
    public string DocInfoCreationDate { get => _docInfoCreationDate; private set => SetProperty(ref _docInfoCreationDate, value); }
    public string DocInfoModifiedDate { get => _docInfoModifiedDate; private set => SetProperty(ref _docInfoModifiedDate, value); }
    public string DocInfoAuthor { get => _docInfoAuthor; private set => SetProperty(ref _docInfoAuthor, value); }
    public string DocInfoCreator { get => _docInfoCreator; private set => SetProperty(ref _docInfoCreator, value); }
    public string DocInfoProducer { get => _docInfoProducer; private set => SetProperty(ref _docInfoProducer, value); }
    public string DocInfoTitle { get => _docInfoTitle; private set => SetProperty(ref _docInfoTitle, value); }
    public string DocInfoSubject { get => _docInfoSubject; private set => SetProperty(ref _docInfoSubject, value); }
    public string DocInfoKeywords { get => _docInfoKeywords; private set => SetProperty(ref _docInfoKeywords, value); }
    public bool IsPdfACompliant
    {
        get => _isPdfACompliant;
        private set
        {
            if (SetProperty(ref _isPdfACompliant, value))
            {
                OnPropertyChanged(nameof(PdfAComplianceText));
                OnPropertyChanged(nameof(PdfABadgeBackgroundBrush));
                OnPropertyChanged(nameof(PdfABadgeIconBrush));
            }
        }
    }
    public string PdfAComplianceText => _isPdfACompliant
        ? "This PDF is PDF/A compliant."
        : "This PDF is NOT PDF/A compliant.";
    public Brush PdfABadgeBackgroundBrush => _isPdfACompliant
        ? PdfToolDefinition.PdfACompliantBgBrush
        : PdfToolDefinition.PdfANotCompliantBgBrush;
    public Brush PdfABadgeIconBrush => _isPdfACompliant
        ? PdfToolDefinition.ExcelGreenBrush
        : PdfToolDefinition.PdfReaderRedBrush;

    public bool IsPrintDialogOpen
    {
        get => _isPrintDialogOpen;
        set => SetProperty(ref _isPrintDialogOpen, value);
    }
    public string SelectedPrinter
    {
        get => _selectedPrinter;
        set => SetProperty(ref _selectedPrinter, value);
    }
    public string SelectedPrintTray
    {
        get => _selectedPrintTray;
        set => SetProperty(ref _selectedPrintTray, value);
    }
    public int SelectedPaperSizeIndex
    {
        get => _selectedPaperSizeIndex;
        set => SetProperty(ref _selectedPaperSizeIndex, value);
    }
    public int PrintCopies
    {
        get => _printCopies;
        set => SetProperty(ref _printCopies, Math.Clamp(value, 1, 99));
    }
    public int PrintPreviewPageNumber
    {
        get => _printPreviewPageNumber;
        set
        {
            int clamped = Math.Clamp(value, 1, Math.Max(1, PageCount));
            if (SetProperty(ref _printPreviewPageNumber, clamped))
            {
                OnPropertyChanged(nameof(PrintPreviewPageDimensionsMm));
                OnPropertyChanged(nameof(PrintPreviewSourceLabel));
                _ = RefreshPrintPreviewImageAsync();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
    public BitmapSource? PrintPreviewImage
    {
        get => _printPreviewImage;
        private set => SetProperty(ref _printPreviewImage, value);
    }
    public string PrintPreviewPageDimensionsMm
    {
        get
        {
            if (_pages.Count == 0) return "210 x 297 mm";
            var sz = _pages[Math.Clamp(_printPreviewPageNumber - 1, 0, _pages.Count - 1)].Size;
            int wMm = (int)Math.Round(sz.Width * 25.4 / 72.0);
            int hMm = (int)Math.Round(sz.Height * 25.4 / 72.0);
            return $"{wMm} x {hMm} mm";
        }
    }
    public string PrintPreviewSourceLabel => $"Source page ({_printPreviewPageNumber})";

    public bool IsPrintAllPages
    {
        get => _isPrintAllPages;
        set { if (SetProperty(ref _isPrintAllPages, value) && value) { _isPrintCurrentPage = false; _isPrintPageRange = false; OnPropertyChanged(nameof(IsPrintCurrentPage)); OnPropertyChanged(nameof(IsPrintPageRange)); } }
    }
    public bool IsPrintCurrentPage
    {
        get => _isPrintCurrentPage;
        set { if (SetProperty(ref _isPrintCurrentPage, value) && value) { _isPrintAllPages = false; _isPrintPageRange = false; OnPropertyChanged(nameof(IsPrintAllPages)); OnPropertyChanged(nameof(IsPrintPageRange)); } }
    }
    public bool IsPrintPageRange
    {
        get => _isPrintPageRange;
        set { if (SetProperty(ref _isPrintPageRange, value) && value) { _isPrintAllPages = false; _isPrintCurrentPage = false; OnPropertyChanged(nameof(IsPrintAllPages)); OnPropertyChanged(nameof(IsPrintCurrentPage)); } }
    }
    public string PrintPageRangeText
    {
        get => _printPageRangeText;
        set => SetProperty(ref _printPageRangeText, value);
    }
    public int SelectedPrintSubsetIndex
    {
        get => _selectedPrintSubsetIndex;
        set => SetProperty(ref _selectedPrintSubsetIndex, value);
    }
    public bool PrintInvertPages
    {
        get => _printInvertPages;
        set => SetProperty(ref _printInvertPages, value);
    }
    public bool IsPrintOrientationAuto
    {
        get => _isPrintOrientationAuto;
        set => SetProperty(ref _isPrintOrientationAuto, value);
    }
    public bool IsPrintOrientationPortrait
    {
        get => _isPrintOrientationPortrait;
        set => SetProperty(ref _isPrintOrientationPortrait, value);
    }
    public bool IsPrintOrientationLandscape
    {
        get => _isPrintOrientationLandscape;
        set => SetProperty(ref _isPrintOrientationLandscape, value);
    }
    public bool IsPrintSizeFit
    {
        get => _isPrintSizeFit;
        set => SetProperty(ref _isPrintSizeFit, value);
    }
    public bool IsPrintSizeReal
    {
        get => _isPrintSizeReal;
        set => SetProperty(ref _isPrintSizeReal, value);
    }
    public bool IsPrintSizeCustom
    {
        get => _isPrintSizeCustom;
        set => SetProperty(ref _isPrintSizeCustom, value);
    }
    public int PrintCustomScalePercent
    {
        get => _printCustomScalePercent;
        set => SetProperty(ref _printCustomScalePercent, Math.Clamp(value, 10, 400));
    }
    public bool IsPrintColor
    {
        get => _isPrintColor;
        set { if (SetProperty(ref _isPrintColor, value)) { _isPrintGrayscale = !value; OnPropertyChanged(nameof(IsPrintGrayscale)); } }
    }
    public bool IsPrintGrayscale
    {
        get => _isPrintGrayscale;
        set { if (SetProperty(ref _isPrintGrayscale, value)) { _isPrintColor = !value; OnPropertyChanged(nameof(IsPrintColor)); } }
    }
    public bool IsPrintDuplexNone
    {
        get => _isPrintDuplexNone;
        set => SetProperty(ref _isPrintDuplexNone, value);
    }
    public bool IsPrintDuplexLongEdge
    {
        get => _isPrintDuplexLongEdge;
        set => SetProperty(ref _isPrintDuplexLongEdge, value);
    }
    public bool IsPrintDuplexShortEdge
    {
        get => _isPrintDuplexShortEdge;
        set => SetProperty(ref _isPrintDuplexShortEdge, value);
    }

    public int PageNumber
    {
        get => ActiveDocument?.ReaderState.PageNumber??1;
        set
        {
            SetCurrentPageFromViewport(value);
            ScrollToPageRequested?.Invoke(PageNumber-1);
        }
    }
    public PdfReaderFitMode FitMode
    {
        get => ActiveDocument?.ReaderState.FitMode??PdfReaderFitMode.FitPage;
        set { if(ActiveDocument==null||ActiveDocument.ReaderState.FitMode==value)return;ActiveDocument.ReaderState.FitMode=value;Reflow(); }
    }
    public int ZoomPercent
    {
        get => ActiveDocument==null?100:FitMode==PdfReaderFitMode.Custom?ActiveDocument.ReaderState.ZoomPercent
            :_pages.Count==0?100:(int)Math.Round(Scale(_pages[Math.Clamp(PageNumber-1,0,_pages.Count-1)].Size)*100);
        set
        {
            if(ActiveDocument==null)return;
            ActiveDocument.ReaderState.ZoomPercent=Math.Clamp(value,10,1000);ActiveDocument.ReaderState.FitMode=PdfReaderFitMode.Custom;Reflow();
        }
    }
    public string ZoomText
    {
        get => $"{ZoomPercent}%";
        set { if(int.TryParse(value.Trim().TrimEnd('%'),NumberStyles.Integer,CultureInfo.InvariantCulture,out int percent))ZoomPercent=percent;else{Status="Masukkan zoom 10–1000%.";OnPropertyChanged();} }
    }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand RotateViewCommand { get; }
    public ICommand ToggleHandCommand { get; }
    public ICommand ToggleLeftSidebarCommand { get; }
    public ICommand SelectSidebarTabCommand { get; }
    public ICommand SelectThumbnailPageCommand { get; }
    public ICommand OpenPrintDialogCommand { get; }
    public ICommand ClosePrintDialogCommand { get; }
    public ICommand PreviousPrintPreviewPageCommand { get; }
    public ICommand NextPrintPreviewPageCommand { get; }
    public ICommand ExecutePrintCommand { get; }

    public event Action? RenderInvalidated;
    public event Action<int>? ScrollToPageRequested;

    private static BitmapSource CreateScaledThumbnail(BitmapSource source, int maxPixelDimension = 160)
    {
        if (source.PixelWidth <= maxPixelDimension && source.PixelHeight <= maxPixelDimension)
        {
            if (!source.IsFrozen && source.CanFreeze) source.Freeze();
            return source;
        }
        double scale = Math.Min((double)maxPixelDimension / Math.Max(1, source.PixelWidth), (double)maxPixelDimension / Math.Max(1, source.PixelHeight));
        var tb = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        var wb = new WriteableBitmap(tb);
        wb.Freeze();
        return wb;
    }

    private void LoadDocumentMetadataAndAttachments(PdfFileItem? document)
    {
        Attachments.Clear();
        if (document == null || string.IsNullOrWhiteSpace(document.Path) || !File.Exists(document.Path))
        {
            DocInfoVersion = "1.4";
            DocInfoFileSize = "-";
            DocInfoCreationDate = "-";
            DocInfoModifiedDate = "-";
            DocInfoAuthor = "-";
            DocInfoCreator = "-";
            DocInfoProducer = "-";
            DocInfoTitle = "-";
            DocInfoSubject = "-";
            DocInfoKeywords = "-";
            IsPdfACompliant = false;
            OnPropertyChanged(nameof(HasNoAttachments));
            return;
        }

        try
        {
            var fi = new FileInfo(document.Path);
            DocInfoFileSize = fi.Length >= 1024 * 1024
                ? $"{(fi.Length / (1024.0 * 1024.0)).ToString("0.00", CultureInfo.InvariantCulture)} MB"
                : $"{Math.Max(1.0, fi.Length / 1024.0).ToString("0.#", CultureInfo.InvariantCulture)} KB";
            DocInfoCreationDate = fi.CreationTime.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            DocInfoModifiedDate = fi.LastWriteTime.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch
        {
            DocInfoFileSize = "-";
            DocInfoCreationDate = "-";
            DocInfoModifiedDate = "-";
        }

        string version = "1.4";
        string author = "-";
        string creator = "-";
        string producer = "-";
        string title = Path.GetFileNameWithoutExtension(document.Name);
        string subject = "-";
        string keywords = "-";
        bool pdfA = false;

        try
        {
            using var fs = new FileStream(document.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int sampleLen = (int)Math.Min(fs.Length, 256 * 1024);
            byte[] buf = new byte[sampleLen];
            int read = fs.Read(buf, 0, sampleLen);
            string headLatin = Encoding.Latin1.GetString(buf, 0, read);

            var verMatch = Regex.Match(headLatin, @"%PDF-(\d+\.\d+)");
            if (verMatch.Success)
            {
                version = verMatch.Groups[1].Value;
            }
            if (headLatin.Contains("pdfaid:part", StringComparison.OrdinalIgnoreCase) ||
                headLatin.Contains("GTS_PDFA1", StringComparison.OrdinalIgnoreCase))
            {
                pdfA = true;
            }

            if (headLatin.Contains("/EmbeddedFiles", StringComparison.Ordinal) || headLatin.Contains("/Filespec", StringComparison.Ordinal))
            {
                foreach (Match m in Regex.Matches(headLatin, @"/(?:UF|F)\s*\(([^()]{1,120})\)"))
                {
                    string fname = m.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(fname) && !fname.Equals(document.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (Attachments.All(a => !a.FileName.Equals(fname, StringComparison.OrdinalIgnoreCase)))
                            Attachments.Add(new PdfAttachmentItem(fname, "Embedded"));
                    }
                }
            }
        }
        catch { }

        try
        {
            using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(document.Path, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            if (pdf.Version > 0)
            {
                version = $"{pdf.Version / 10}.{pdf.Version % 10}";
            }
            var info = pdf.Info;
            if (!string.IsNullOrWhiteSpace(info.Author)) author = info.Author.Trim();
            if (!string.IsNullOrWhiteSpace(info.Creator)) creator = info.Creator.Trim();
            if (!string.IsNullOrWhiteSpace(info.Producer)) producer = info.Producer.Trim();
            if (!string.IsNullOrWhiteSpace(info.Title)) title = info.Title.Trim();
            if (!string.IsNullOrWhiteSpace(info.Subject)) subject = info.Subject.Trim();
            if (!string.IsNullOrWhiteSpace(info.Keywords)) keywords = info.Keywords.Trim();
            if (info.CreationDate != DateTime.MinValue && info.CreationDate.Year > 1970)
                DocInfoCreationDate = info.CreationDate.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            if (info.ModificationDate != DateTime.MinValue && info.ModificationDate.Year > 1970)
                DocInfoModifiedDate = info.ModificationDate.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch { }

        DocInfoVersion = version;
        DocInfoAuthor = author;
        DocInfoCreator = creator;
        DocInfoProducer = producer;
        DocInfoTitle = string.IsNullOrWhiteSpace(title) ? "-" : title;
        DocInfoSubject = subject;
        DocInfoKeywords = keywords;
        IsPdfACompliant = pdfA;
        OnPropertyChanged(nameof(HasNoAttachments));
    }

    private async Task EnsureSidebarThumbnailsAsync()
    {
        var doc = ActiveDocument;
        if (_closed || _suspended || !_isLeftSidebarExpanded || _selectedSidebarTabIndex != 0 || doc == null) return;
        int docVer = _documentVersion;
        var token = _documentCts?.Token ?? CancellationToken.None;

        var snapshot = _pages.ToArray();
        foreach (var page in snapshot)
        {
            if (_closed || _suspended || !_isLeftSidebarExpanded || _selectedSidebarTabIndex != 0 || docVer != _documentVersion || !ReferenceEquals(doc, ActiveDocument))
                return;
            if (page.HasDedicatedThumbnail) continue;
            if (page.Image != null)
            {
                page.Thumbnail = CreateScaledThumbnail(page.Image, 160);
                continue;
            }
            try
            {
                var res = await NativeAsync(() => _engine.GetPageImage(doc.Path, page.Index, 136, 180), token);
                if (res != null && !_closed && !_suspended && docVer == _documentVersion && ReferenceEquals(doc, ActiveDocument))
                {
                    var bmp = BitmapSource.Create(res.Width, res.Height, 96, 96, PixelFormats.Bgra32, null, res.Pixels, res.Stride);
                    bmp.Freeze();
                    page.Thumbnail = bmp;
                }
            }
            catch { }
        }
    }

    public void OpenPrintDialog()
    {
        if (!HasDocument || _pages.Count == 0) return;
        RefreshInstalledPrinters();
        _printPreviewPageNumber = Math.Clamp(PageNumber, 1, PageCount);
        OnPropertyChanged(nameof(PrintPreviewPageNumber));
        OnPropertyChanged(nameof(PrintPreviewPageDimensionsMm));
        OnPropertyChanged(nameof(PrintPreviewSourceLabel));
        IsPrintDialogOpen = true;
        _ = RefreshPrintPreviewImageAsync();
    }

    private void RefreshInstalledPrinters()
    {
        InstalledPrinters.Clear();
        try
        {
            foreach (string? printer in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
            {
                if (!string.IsNullOrWhiteSpace(printer))
                    InstalledPrinters.Add(printer);
            }
        }
        catch { }

        if (InstalledPrinters.Count == 0)
        {
            InstalledPrinters.Add("Microsoft Print to PDF");
        }

        if (string.IsNullOrWhiteSpace(_selectedPrinter) || !InstalledPrinters.Contains(_selectedPrinter))
        {
            try
            {
                var def = new System.Drawing.Printing.PrinterSettings().PrinterName;
                SelectedPrinter = (!string.IsNullOrWhiteSpace(def) && InstalledPrinters.Contains(def))
                    ? def
                    : InstalledPrinters[0];
            }
            catch
            {
                SelectedPrinter = InstalledPrinters[0];
            }
        }
    }

    private async Task RefreshPrintPreviewImageAsync()
    {
        var doc = ActiveDocument;
        int pageIdx = Math.Clamp(_printPreviewPageNumber - 1, 0, Math.Max(0, _pages.Count - 1));
        if (doc == null || pageIdx >= _pages.Count || _closed) return;

        if (_pages[pageIdx].Image != null)
        {
            PrintPreviewImage = _pages[pageIdx].Image;
            return;
        }
        if (pageIdx == 0 && doc.PreviewImage != null)
        {
            PrintPreviewImage = doc.PreviewImage;
        }

        try
        {
            var token = _documentCts?.Token ?? CancellationToken.None;
            var res = await NativeAsync(() => _engine.GetPageImage(doc.Path, pageIdx, 420, 560), token);
            if (res != null && ReferenceEquals(doc, ActiveDocument) && !_closed)
            {
                var bmp = BitmapSource.Create(res.Width, res.Height, 96, 96, PixelFormats.Bgra32, null, res.Pixels, res.Stride);
                bmp.Freeze();
                PrintPreviewImage = bmp;
            }
        }
        catch { }
    }

    public async Task ExecutePrintAsync()
    {
        var doc = ActiveDocument;
        if (doc == null || _pages.Count == 0 || _closed) return;

        var targetPages = ResolvePagesToPrint();
        if (targetPages.Count == 0) return;

        IsPrintDialogOpen = false;
        string printerName = SelectedPrinter;
        int copies = PrintCopies;
        bool grayscale = IsPrintGrayscale;
        bool autoOrient = IsPrintOrientationAuto;
        bool forceLandscape = IsPrintOrientationLandscape;
        bool fitPage = IsPrintSizeFit;
        bool customScale = IsPrintSizeCustom;
        double customScaleFactor = PrintCustomScalePercent / 100.0;
        var duplexMode = IsPrintDuplexLongEdge ? System.Drawing.Printing.Duplex.Vertical : (IsPrintDuplexShortEdge ? System.Drawing.Printing.Duplex.Horizontal : System.Drawing.Printing.Duplex.Simplex);

        await Task.Run(() =>
        {
            try
            {
                using var printDoc = new System.Drawing.Printing.PrintDocument();
                if (!string.IsNullOrWhiteSpace(printerName))
                    printDoc.PrinterSettings.PrinterName = printerName;
                printDoc.PrinterSettings.Copies = (short)Math.Clamp(copies, 1, 99);
                if (printDoc.PrinterSettings.CanDuplex)
                    printDoc.PrinterSettings.Duplex = duplexMode;
                printDoc.DocumentName = doc.Name;

                int cursor = 0;
                printDoc.QueryPageSettings += (_, qe) =>
                {
                    if (cursor < targetPages.Count)
                    {
                        int pIdx = targetPages[cursor] - 1;
                        var pSize = _pages[Math.Clamp(pIdx, 0, _pages.Count - 1)].Size;
                        qe.PageSettings.Landscape = autoOrient ? (pSize.Width > pSize.Height) : forceLandscape;
                        qe.PageSettings.Color = !grayscale;
                    }
                };

                printDoc.PrintPage += (_, pe) =>
                {
                    if (cursor >= targetPages.Count || pe.Graphics == null)
                    {
                        pe.HasMorePages = false;
                        return;
                    }
                    int pageIdx = targetPages[cursor++] - 1;
                    var imgRes = _engine.GetPageImage(doc.Path, pageIdx, 2480, 3508);
                    if (imgRes != null && imgRes.Width > 0 && imgRes.Height > 0)
                    {
                        byte[] pixels = imgRes.Pixels;
                        if (grayscale)
                        {
                            pixels = (byte[])imgRes.Pixels.Clone();
                            for (int i = 0; i + 3 < pixels.Length; i += 4)
                            {
                                byte lum = (byte)((pixels[i + 2] * 77 + pixels[i + 1] * 150 + pixels[i] * 29) >> 8);
                                pixels[i] = lum;
                                pixels[i + 1] = lum;
                                pixels[i + 2] = lum;
                            }
                        }
                        using var bmp = new System.Drawing.Bitmap(imgRes.Width, imgRes.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        var bmpData = bmp.LockBits(new System.Drawing.Rectangle(0, 0, imgRes.Width, imgRes.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        try
                        {
                            Marshal.Copy(pixels, 0, bmpData.Scan0, Math.Min(pixels.Length, bmpData.Stride * imgRes.Height));
                        }
                        finally
                        {
                            bmp.UnlockBits(bmpData);
                        }

                        var bounds = pe.MarginBounds;
                        if (fitPage)
                        {
                            double ratio = Math.Min((double)bounds.Width / imgRes.Width, (double)bounds.Height / imgRes.Height);
                            int drawW = (int)Math.Round(imgRes.Width * ratio);
                            int drawH = (int)Math.Round(imgRes.Height * ratio);
                            int drawX = bounds.X + (bounds.Width - drawW) / 2;
                            int drawY = bounds.Y + (bounds.Height - drawH) / 2;
                            pe.Graphics.DrawImage(bmp, new System.Drawing.Rectangle(drawX, drawY, drawW, drawH));
                        }
                        else
                        {
                            var pSize = _pages[Math.Clamp(pageIdx, 0, _pages.Count - 1)].Size;
                            double factor = customScale ? customScaleFactor : 1.0;
                            int drawW = (int)Math.Round(pSize.Width * 100.0 / 72.0 * factor);
                            int drawH = (int)Math.Round(pSize.Height * 100.0 / 72.0 * factor);
                            int drawX = bounds.X + (bounds.Width - drawW) / 2;
                            int drawY = bounds.Y + (bounds.Height - drawH) / 2;
                            pe.Graphics.DrawImage(bmp, new System.Drawing.Rectangle(drawX, drawY, drawW, drawH));
                        }
                    }
                    pe.HasMorePages = cursor < targetPages.Count;
                };

                printDoc.Print();
            }
            catch { }
        });
    }

    internal List<int> ResolvePagesToPrint()
    {
        var list = new List<int>();
        if (_pages.Count == 0) return list;

        if (IsPrintCurrentPage)
        {
            list.Add(Math.Clamp(PageNumber, 1, _pages.Count));
        }
        else if (IsPrintPageRange && !string.IsNullOrWhiteSpace(PrintPageRangeText))
        {
            foreach (var part in PrintPageRangeText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var dash = part.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (dash.Length == 1 && int.TryParse(dash[0], out int single) && single >= 1 && single <= _pages.Count)
                {
                    list.Add(single);
                }
                else if (dash.Length == 2 && int.TryParse(dash[0], out int start) && int.TryParse(dash[1], out int end))
                {
                    int s = Math.Clamp(Math.Min(start, end), 1, _pages.Count);
                    int e = Math.Clamp(Math.Max(start, end), 1, _pages.Count);
                    for (int p = s; p <= e; p++) list.Add(p);
                }
            }
        }
        if (list.Count == 0)
        {
            for (int p = 1; p <= _pages.Count; p++) list.Add(p);
        }

        if (SelectedPrintSubsetIndex == 1)
            list = list.Where(p => p % 2 == 1).ToList();
        else if (SelectedPrintSubsetIndex == 2)
            list = list.Where(p => p % 2 == 0).ToList();

        if (PrintInvertPages)
            list.Reverse();

        return list;
    }

    public void SetCurrentPageFromViewport(int pageNumber)
    {
        if(ActiveDocument!=null)ActiveDocument.ReaderState.PageNumber=Math.Clamp(pageNumber,1,Math.Max(1,PageCount));
        int currentIdx = PageNumber - 1;
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].IsCurrentPage = (i == currentIdx);
        }
        OnPropertyChanged(nameof(PageNumber));OnPropertyChanged(nameof(ZoomText));CommandManager.InvalidateRequerySuggested();
    }
    private Task Track(Task task)
    {
        _pending.Add(task);
        return Complete();
        async Task Complete(){try{await task;}catch{}finally{_pending.Remove(task);}}
    }
    public Task OpenDocumentAsync(PdfFileItem? document)
    {
        if(_closed||document!=null&&(!_sources.Contains(document)||!document.IsReady))return Task.CompletedTask;
        if(ReferenceEquals(_activeDocument,document)&&!_suspended)return Task.CompletedTask;
        _suspended=false;++_documentVersion;
        try { _documentCts?.Cancel(); _documentCts?.Dispose(); } catch { }
        _documentCts=new();
        RenewRenderRequests();
        _activeDocument=document;_pages.Clear();_visible.Clear();
        LoadDocumentMetadataAndAttachments(document);
        foreach(var name in new[]{nameof(ActiveDocument),nameof(HasDocument),nameof(DocumentName),nameof(PageCount),nameof(PageNumber)})OnPropertyChanged(name);
        if(document==null){Status="Pilih dokumen PDF.";return Task.CompletedTask;}
        Status="Memuat dokumen...";
        return Track(LoadDocumentAsync(document,_documentVersion,_documentCts.Token));
    }
    private async Task<T> NativeAsync<T>(Func<T> action,CancellationToken token)
    {
        await NativeSlot.WaitAsync(token).ConfigureAwait(false);
        try{return await Task.Run(action,token).ConfigureAwait(false);}finally{NativeSlot.Release();}
    }
    private async Task LoadDocumentAsync(PdfFileItem document,int version,CancellationToken token)
    {
        try
        {
            var sizes=await NativeAsync(()=>_engine.GetPageSizes(document.Path,token),token);
            if(_closed||_suspended||token.IsCancellationRequested||version!=_documentVersion)return;
            if(sizes==null||sizes.Count==0)throw new InvalidOperationException("Metadata halaman tidak tersedia.");
            for(int index=0;index<sizes.Count;index++)_pages.Add(new(index,sizes[index]));
            if (_pages.Count > 0 && document.PreviewImage != null)
            {
                _pages[0].Thumbnail = document.PreviewImage;
            }
            SetCurrentPageFromViewport(document.ReaderState.PageNumber);
            OnPropertyChanged(nameof(PageCount));Status="";Reflow();
            ScrollToPageRequested?.Invoke(PageNumber-1);
            if (_isLeftSidebarExpanded && _selectedSidebarTabIndex == 0)
            {
                _ = EnsureSidebarThumbnailsAsync();
            }
        }
        catch(OperationCanceledException) { }
        catch(ObjectDisposedException) { }
        catch(Exception ex){if(!_closed&&!_suspended&&version==_documentVersion)Status=$"Reader tidak tersedia: {ex.Message}";}
    }
    public void UpdateViewport(double width,double height,double dpi)
    {
        if(!double.IsFinite(width)||!double.IsFinite(height)||width<32||height<32)return;
        dpi=double.IsFinite(dpi)&&dpi>0?dpi:1;
        if(Math.Abs(width-_viewportWidth)<0.5&&Math.Abs(height-_viewportHeight)<0.5&&Math.Abs(dpi-_dpi)<0.01)return;
        _viewportWidth=width;_viewportHeight=height;_dpi=dpi;Reflow();
    }
    private double Scale(PdfPageSize size)
    {
        bool sideways=ActiveDocument?.ReaderState.Rotation is 90 or 270;
        double width=(sideways?size.Height:size.Width)*96.0/72;
        double height=(sideways?size.Width:size.Height)*96.0/72;
        return FitMode switch
        {
            PdfReaderFitMode.FitPage=>Math.Min(Math.Max(32,_viewportWidth-48)/width,Math.Max(32,_viewportHeight-32)/height),
            PdfReaderFitMode.FitWidth=>Math.Max(32,_viewportWidth-48)/width,
            _=>(ActiveDocument?.ReaderState.ZoomPercent??100)/100.0
        };
    }
    private void RenewRenderRequests()
    {
        ++_renderVersion;
        try { _renderCts?.Cancel(); _renderCts?.Dispose(); } catch { }
        try
        {
            _renderCts=CancellationTokenSource.CreateLinkedTokenSource(_documentCts?.Token??CancellationToken.None);
        }
        catch
        {
            _renderCts=new CancellationTokenSource();
        }
    }
    private void Reflow()
    {
        if(_closed||_suspended)return;
        RenewRenderRequests();
        foreach(var page in _pages)
        {
            page.NeedsRender=true;
            if(!_visible.Contains(page.Index))page.Image=null;
            double scale=Scale(page.Size)*96.0/72;page.Rotation=ActiveDocument?.ReaderState.Rotation??0;
            bool sideways=page.Rotation is 90 or 270;
            page.Width=Math.Max(1,(sideways?page.Size.Height:page.Size.Width)*scale);
            page.Height=Math.Max(1,(sideways?page.Size.Width:page.Size.Height)*scale);
        }
        OnPropertyChanged(nameof(FitMode));OnPropertyChanged(nameof(ZoomPercent));OnPropertyChanged(nameof(ZoomText));
        RenderInvalidated?.Invoke();
        ScrollToPageRequested?.Invoke(PageNumber-1);
    }
    public Task SetVisiblePagesAsync(IEnumerable<int> indices)
    {
        if(_closed||_suspended||ActiveDocument==null)return Task.CompletedTask;
        _visible=indices.Where(x=>x>=0&&x<_pages.Count).ToHashSet();
        foreach(var page in _pages)if(!_visible.Contains(page.Index)){page.Image=null;page.NeedsRender=true;}
        return Task.WhenAll(_visible.Order().Select(RenderPageAsync));
    }
    private Task RenderPageAsync(int index)
    {
        if(_closed||_suspended||index<0||index>=_pages.Count)return Task.CompletedTask;
        var page=_pages[index];int version=_renderVersion;
        if((page.Image!=null&&!page.NeedsRender)||!_inflight.Add((index,version)))return Task.CompletedTask;
        return Track(Render());
        async Task Render()
        {
            var document=ActiveDocument;
            var cts=_renderCts;
            if(document==null||cts==null)return;
            CancellationToken token;
            try{token=cts.Token;}catch(ObjectDisposedException){return;}
            try
            {
                if(page.Image==null)page.Status="Memuat halaman...";
                bool sideways=page.Rotation is 90 or 270;
                double hdScale=Math.Max(1.35,_dpi*1.35);
                int width=(int)Math.Clamp(Math.Ceiling((sideways?page.Height:page.Width)*hdScale),1,20000);
                int height=(int)Math.Clamp(Math.Ceiling((sideways?page.Width:page.Height)*hdScale),1,20000);
                var result=await NativeAsync(()=>_engine.GetPageImage(document.Path,index,width,height),token);
                if(_closed||_suspended||token.IsCancellationRequested||version!=_renderVersion||!ReferenceEquals(document,ActiveDocument)||!_visible.Contains(index))return;
                if(result==null)throw new InvalidOperationException("Halaman tidak dapat dirender.");
                long pixels=(long)result.Width*result.Height;
                long resident=_pages.Where(x=>x.Image!=null&&x.Index!=index).Sum(x=>(long)x.Image!.PixelWidth*x.Image.PixelHeight);
                if(pixels>PdfEngineService.MaximumReaderPixels||resident+pixels>24_000_000){page.Image=null;page.Status="Preview dibatasi memori. Sesuaikan zoom atau posisi halaman.";return;}
                var bitmap=BitmapSource.Create(result.Width,result.Height,96,96,PixelFormats.Bgra32,null,result.Pixels,result.Stride);bitmap.Freeze();
                page.Image=bitmap;
                if (!page.HasDedicatedThumbnail)
                {
                    page.Thumbnail = CreateScaledThumbnail(bitmap, 160);
                }
                page.NeedsRender=false;page.Status="";
            }
            catch(OperationCanceledException) { }
            catch(ObjectDisposedException) { }
            catch(Exception ex){if(!_closed&&version==_renderVersion&&ReferenceEquals(document,ActiveDocument)){page.Image=null;page.Status=$"Preview tidak tersedia: {ex.Message}";}}
            finally{_inflight.Remove((index,version));}
        }
    }
    public void Suspend()
    {
        _suspended=true;IsPrintDialogOpen=false;++_documentVersion;++_renderVersion;
        try { _documentCts?.Cancel(); } catch { }
        try { _renderCts?.Cancel(); } catch { }
        foreach(var page in _pages){page.Image=null;page.Thumbnail=null;page.NeedsRender=true;}_visible.Clear();
    }
    public Task ResumeAsync()=>_suspended?OpenDocumentAsync(ActiveDocument):Task.CompletedTask;
    public async Task CloseAsync()
    {
        if(_closed)return;
        _closed=true;Suspend();
        try{await Task.WhenAll(_pending.ToArray()).ConfigureAwait(false);}catch{}
        _pages.Clear();
        try{_documentCts?.Dispose();}catch{}
        try{_renderCts?.Dispose();}catch{}
        _documentCts=null;_renderCts=null;
    }
}
