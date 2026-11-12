using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexus.PdfUtilities;

namespace Nexus.App.ViewModels;

public enum PdfReaderFitMode { FitPage, FitWidth, Custom }
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
    private double _width,_height;
    private int _rotation;
    private string _status="Memuat halaman...";
    public int Index { get; } = index;
    public PdfPageSize Size { get; } = size;
    public string Label => $"Halaman {Index+1}";
    public BitmapSource? Image { get => _image; internal set => SetProperty(ref _image,value); }
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
    public PdfReaderViewModel(IPdfEngineService engine,ReadOnlyObservableCollection<PdfFileItem> sources)
    {
        _engine=engine;_sources=sources;Pages=new(_pages);
        PreviousPageCommand=new RelayCommand(()=>PageNumber--,()=>PageNumber>1);
        NextPageCommand=new RelayCommand(()=>PageNumber++,()=>PageNumber<PageCount);
        ZoomInCommand=new RelayCommand(()=>ZoomPercent+=25,()=>HasDocument&&ZoomPercent<1000);
        ZoomOutCommand=new RelayCommand(()=>ZoomPercent-=25,()=>HasDocument&&ZoomPercent>10);
        RotateViewCommand=new RelayCommand<int>(degrees=>{if(_activeDocument==null)return;_activeDocument.ReaderState.Rotation=(_activeDocument.ReaderState.Rotation+degrees%360+360)%360;Reflow();},_=>HasDocument);
        ToggleHandCommand=new RelayCommand(()=>IsHandMode=!IsHandMode);
    }
    public ReadOnlyObservableCollection<PdfReaderPageViewModel> Pages { get; }
    public ReadOnlyObservableCollection<PdfFileItem> Sources => _sources;
    public PdfFileItem? ActiveDocument => _activeDocument;
    public bool HasDocument => _activeDocument!=null;
    public string DocumentName => ActiveDocument?.Name??"";
    public int PageCount => _pages.Count;
    public string Status { get => _status; private set => SetProperty(ref _status,value); }
    public bool IsHandMode { get => _handMode; set => SetProperty(ref _handMode,value); }
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
    public event Action? RenderInvalidated;
    public event Action<int>? ScrollToPageRequested;
    public void SetCurrentPageFromViewport(int pageNumber)
    {
        if(ActiveDocument!=null)ActiveDocument.ReaderState.PageNumber=Math.Clamp(pageNumber,1,Math.Max(1,PageCount));
        OnPropertyChanged(nameof(PageNumber));OnPropertyChanged(nameof(ZoomText));CommandManager.InvalidateRequerySuggested();
    }
    private Task Track(Task task)
    {
        _pending.Add(task);
        return Complete();
        async Task Complete(){try{await task;}finally{_pending.Remove(task);}}
    }
    public Task OpenDocumentAsync(PdfFileItem? document)
    {
        if(_closed||document!=null&&(!_sources.Contains(document)||!document.IsReady))return Task.CompletedTask;
        if(ReferenceEquals(_activeDocument,document)&&!_suspended)return Task.CompletedTask;
        _suspended=false;++_documentVersion;
        _documentCts?.Cancel();_documentCts?.Dispose();_documentCts=new();
        RenewRenderRequests();
        _activeDocument=document;_pages.Clear();_visible.Clear();
        foreach(var name in new[]{nameof(ActiveDocument),nameof(HasDocument),nameof(DocumentName),nameof(PageCount),nameof(PageNumber)})OnPropertyChanged(name);
        if(document==null){Status="Pilih dokumen PDF.";return Task.CompletedTask;}
        Status="Memuat dokumen...";
        return Track(LoadDocumentAsync(document,_documentVersion,_documentCts.Token));
    }
    private async Task<T> NativeAsync<T>(Func<T> action,CancellationToken token)
    {
        await NativeSlot.WaitAsync(token);
        try{return await Task.Run(action,token);}finally{NativeSlot.Release();}
    }
    private async Task LoadDocumentAsync(PdfFileItem document,int version,CancellationToken token)
    {
        try
        {
            var sizes=await NativeAsync(()=>_engine.GetPageSizes(document.Path,token),token);
            if(_closed||_suspended||token.IsCancellationRequested||version!=_documentVersion)return;
            if(sizes==null||sizes.Count==0)throw new InvalidOperationException("Metadata halaman tidak tersedia.");
            for(int index=0;index<sizes.Count;index++)_pages.Add(new(index,sizes[index]));
            SetCurrentPageFromViewport(document.ReaderState.PageNumber);
            OnPropertyChanged(nameof(PageCount));Status="";Reflow();
            ScrollToPageRequested?.Invoke(PageNumber-1);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
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
        ++_renderVersion;_renderCts?.Cancel();_renderCts?.Dispose();
        _renderCts=CancellationTokenSource.CreateLinkedTokenSource(_documentCts?.Token??CancellationToken.None);
    }
    private void Reflow()
    {
        if(_closed||_suspended)return;
        RenewRenderRequests();
        foreach(var page in _pages)
        {
            page.Image=null;double scale=Scale(page.Size)*96.0/72;page.Rotation=ActiveDocument?.ReaderState.Rotation??0;
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
        foreach(var page in _pages)if(!_visible.Contains(page.Index))page.Image=null;
        return Task.WhenAll(_visible.Order().Select(RenderPageAsync));
    }
    private Task RenderPageAsync(int index)
    {
        var page=_pages[index];int version=_renderVersion;
        if(page.Image!=null||!_inflight.Add((index,version)))return Task.CompletedTask;
        return Track(Render());
        async Task Render()
        {
            var document=ActiveDocument!;var token=_renderCts!.Token;
            try
            {
                page.Status="Memuat halaman...";
                bool sideways=page.Rotation is 90 or 270;
                int width=(int)Math.Clamp(Math.Ceiling((sideways?page.Height:page.Width)*_dpi),1,20000);
                int height=(int)Math.Clamp(Math.Ceiling((sideways?page.Width:page.Height)*_dpi),1,20000);
                var result=await NativeAsync(()=>_engine.GetPageImage(document.Path,index,width,height),token);
                if(_closed||_suspended||token.IsCancellationRequested||version!=_renderVersion||!ReferenceEquals(document,ActiveDocument)||!_visible.Contains(index))return;
                if(result==null)throw new InvalidOperationException("Halaman tidak dapat dirender.");
                long pixels=(long)result.Width*result.Height;
                long resident=_pages.Where(x=>x.Image!=null).Sum(x=>(long)x.Image!.PixelWidth*x.Image.PixelHeight);
                if(pixels>PdfEngineService.MaximumReaderPixels||resident+pixels>8_000_000){page.Status="Preview dibatasi memori. Sesuaikan zoom atau posisi halaman.";return;}
                var bitmap=BitmapSource.Create(result.Width,result.Height,96,96,PixelFormats.Bgra32,null,result.Pixels,result.Stride);bitmap.Freeze();
                page.Image=bitmap;page.Status="";
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested) { }
            catch(Exception ex){if(!_closed&&version==_renderVersion&&ReferenceEquals(document,ActiveDocument))page.Status=$"Preview tidak tersedia: {ex.Message}";}
            finally{_inflight.Remove((index,version));}
        }
    }
    public void Suspend()
    {
        _suspended=true;++_documentVersion;++_renderVersion;_documentCts?.Cancel();_renderCts?.Cancel();
        foreach(var page in _pages)page.Image=null;_visible.Clear();
    }
    public Task ResumeAsync()=>_suspended?OpenDocumentAsync(ActiveDocument):Task.CompletedTask;
    public async Task CloseAsync()
    {
        _closed=true;Suspend();await Task.WhenAll(_pending.ToArray());_pages.Clear();
        _documentCts?.Dispose();_renderCts?.Dispose();_documentCts=null;_renderCts=null;
    }
}
