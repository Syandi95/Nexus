using System.Windows.Input;
using System.Windows.Media;
using Nexus.App.Controls.InfoBar;

namespace Nexus.App.ViewModels;

public partial class PdfUtilitiesViewModel
{
    private static readonly Geometry DefaultReaderPageGeometry = CreateFrozenGeometry("M 7,2 H 15 L 19,6 V 18 H 7 Z M 15,2 V 6 H 19 M 4,5 V 21 H 16");
    private static Geometry CreateFrozenGeometry(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    internal event Action<PdfUtilitiesViewModel,PdfToolId>? ToolRequested;
    public bool IsReaderWorkspace => !HasSelectedTool;
    public string WorkspaceGlyph => SelectedTool?.Glyph??"PDFPage";
    public string WorkspaceBadge => SelectedTool?.Badge??"";
    public bool HasWorkspaceBadge => !string.IsNullOrWhiteSpace(WorkspaceBadge);
    public bool IsConversionToolWorkspace => SelectedTool?.IsConversionTool == true;
    public bool IsEditToolWorkspace => SelectedTool?.IsEditTool == true;
    public Brush WorkspaceAccentBrush => SelectedTool?.AccentBrush ?? PdfToolDefinition.PdfReaderRedBrush;
    public Geometry WorkspaceIconGeometry => SelectedTool?.IconGeometry ?? DefaultReaderPageGeometry;
    private PdfToolDefinition? _selectedTool;
    private bool _convertFromExpanded=true,_editExpanded=true,_convertToExpanded=true;
    public bool IsConvertFromExpanded { get=>_convertFromExpanded;set=>SetProperty(ref _convertFromExpanded,value); }
    public bool IsEditExpanded { get=>_editExpanded;set=>SetProperty(ref _editExpanded,value); }
    public bool IsConvertToExpanded { get=>_convertToExpanded;set=>SetProperty(ref _convertToExpanded,value); }
    private bool _toolsExpanded = true;
    private bool _isDocumentView = true;
    public bool IsDocumentView { get => _isDocumentView; set => SetProperty(ref _isDocumentView,value); }
    public double RightPanelWidth => HasSelectedTool ? (IsSettingsVisible ? 328 : 0) : (ToolsExpanded ? 338 : 90);
    public ICommand ToggleDocumentViewCommand { get; private set; } = null!;
    public ICommand SelectDocumentCommand { get; private set; } = null!;
    public IReadOnlyList<PdfToolDefinition> Tools { get; } = PdfToolDefinition.CreateCatalog();
    public IEnumerable<PdfToolDefinition> ConvertFromTools => Tools.Where(x=>x.Category==PdfToolCategory.ConvertFromPdf);
    public IEnumerable<PdfToolDefinition> EditTools => Tools.Where(x=>x.Category==PdfToolCategory.EditPdf);
    public IEnumerable<PdfToolDefinition> ConvertToTools => Tools.Where(x=>x.Category==PdfToolCategory.ConvertToPdf);
    public PdfToolDefinition? SelectedTool
    {
        get => _selectedTool;
        set
        {
            if(value!=null&&!Tools.Any(x=>ReferenceEquals(x,value))) return;
            if(!SetProperty(ref _selectedTool,value)) return;
            IsSettingsVisible=true;
            IsDocumentView=value==null;
            foreach(var name in new[]{nameof(HasSelectedTool),nameof(IsImageToolSelected),nameof(IsPageImageMode),nameof(SelectedToolSettings),nameof(CanConvert),nameof(ConvertButtonText),nameof(RightPanelWidth),nameof(SettingsToggleText),nameof(IsReaderWorkspace),nameof(WorkspaceTitle),nameof(WorkspaceGlyph),nameof(WorkspaceBadge),nameof(HasWorkspaceBadge),nameof(IsConversionToolWorkspace),nameof(IsEditToolWorkspace),nameof(WorkspaceAccentBrush),nameof(WorkspaceIconGeometry)}) OnPropertyChanged(name);
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public PdfToolSettings? SelectedToolSettings => SelectedTool?.Settings;
    public bool HasSelectedTool => SelectedTool!=null;
    public bool IsImageToolSelected => SelectedTool?.Id==PdfToolId.ImageExport;
    private bool _extractEmbeddedImages;
    public bool ExtractEmbeddedImages
    {
        get => _extractEmbeddedImages;
        set
        {
            if(!SetProperty(ref _extractEmbeddedImages,value))return;
            foreach(var name in new[]{nameof(RenderPdfPages),nameof(IsPageImageMode),nameof(CanConvert)})OnPropertyChanged(name);
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool RenderPdfPages { get => !ExtractEmbeddedImages; set { if(value)ExtractEmbeddedImages=false; } }
    public bool IsPageImageMode => IsImageToolSelected && !ExtractEmbeddedImages;
    public bool ToolsExpanded { get => _toolsExpanded; set { if(SetProperty(ref _toolsExpanded,value)) { OnPropertyChanged(nameof(RightPanelWidth));OnPropertyChanged(nameof(SettingsToggleText)); } } }
    public ICommand SelectToolCommand { get; private set; } = null!;
    public ICommand ShowToolsCommand { get; private set; } = null!;
    public ICommand ToggleToolsCommand { get; private set; } = null!;
    private void InitializeTools()
    {
        ToggleDocumentViewCommand=new RelayCommand(()=>IsDocumentView=!IsDocumentView);
        SelectDocumentCommand=new RelayCommand<PdfFileItem>(async item=>{if(item!=null&&item.IsReady){if(HasSelectedTool){ShowFullScreen(item);return;}IsDocumentView=true;await Reader.OpenDocumentAsync(item);}});
        SelectToolCommand=new RelayCommand<PdfToolDefinition>(tool=>{if(tool!=null)ToolRequested?.Invoke(this,tool.Id);},tool=>!_isClosed&&!HasSelectedTool&&tool!=null&&_pdfFiles.Any(x=>x.IsReady)&&!_pdfFiles.Any(x=>x.IsLoading));
        ShowToolsCommand=new RelayCommand(()=>SelectedTool=null,()=>!_isClosed);
        ToggleToolsCommand=new RelayCommand(()=>ToolsExpanded=!ToolsExpanded);
    }
    private void RefreshToolInput()
    {
        foreach(var tool in Tools) tool.Settings.SetInput(_pdfFiles.Count,_pdfFiles.Count==1?_pdfFiles[0].PageCount:0);
        CanConvert=!IsConverting&&_pdfFiles.Any(x=>x.IsReady)&&!_pdfFiles.Any(x=>x.IsLoading);
        CommandManager.InvalidateRequerySuggested();
    }
    private bool ValidateSelectedTool()
    {
        if(SelectedTool?.IsAvailable==true)
        {
            if(IsPageImageMode)return true;
            if(_officeTools?.IsAvailable!=true)
            {ShowInfoBar("Engine belum tersedia","Periksa modul Convert from PDF di folder Engine NEXUS.",InfoBarSeverity.Warning);return false;}

            return true;
        }
        ShowInfoBar("Tool belum tersedia",SelectedTool?.UnavailableReason??"Pilih tool untuk memproses file.",InfoBarSeverity.Informational);
        return false;
    }
}
