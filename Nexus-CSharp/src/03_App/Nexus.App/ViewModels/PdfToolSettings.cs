namespace Nexus.App.ViewModels;

// Form values are workspace-local UI state; unavailable tools have no executable adapter.
public abstract class PdfToolSetting(string key,string label) : ViewModelBase
{
    private bool _isVisible = true;
    private bool _isEnabled = true;
    public string Key { get; } = key;
    public string Label { get; } = label;
    public bool IsVisible { get => _isVisible; set => SetProperty(ref _isVisible,value); }
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled,value); }
}

public sealed class PdfChoiceSetting(string key,string label,string[] choices,int selected=0) : PdfToolSetting(key,label)
{
    private string _value = choices[selected];
    public IReadOnlyList<string> Choices { get; } = choices;
    public string Value { get => _value; set { if (Choices.Contains(value)) SetProperty(ref _value,value); } }
}
public sealed class PdfToggleSetting(string key,string label,bool initial=false) : PdfToolSetting(key,label)
{
    private bool _value = initial;
    public bool Value { get => _value; set => SetProperty(ref _value,value); }
}
public sealed class PdfTextSetting(string key,string label,string initial="") : PdfToolSetting(key,label)
{
    private string _value = initial;
    public string Value { get => _value; set => SetProperty(ref _value,value); }
}
public sealed class PdfNumberSetting(string key,string label,int initial,int minimum,int maximum) : PdfToolSetting(key,label)
{
    private int _value = initial;
    public int Minimum { get; } = minimum;
    private int _maximum = maximum;
    public int Maximum { get => _maximum; internal set { if(SetProperty(ref _maximum,value)) Value=_value; } }
    public int Value { get => _value; set => SetProperty(ref _value,Math.Clamp(value,Minimum,Maximum)); }
}

public sealed class PdfToolSettings : ViewModelBase
{
    private readonly PdfToolId _tool;
    private int _pageCount;
    private int _fileCount;
    private PdfToolSettings(PdfToolId tool,IReadOnlyList<PdfToolSetting> fields,string note)
    {
        _tool=tool; Fields=fields; Note=note;
        foreach(var field in Fields) field.PropertyChanged+=(_,e)=>{if(e.PropertyName=="Value")RefreshConditions();};
        RefreshConditions();
    }
    public IReadOnlyList<PdfToolSetting> Fields { get; }
    public string Note { get; }
    public PdfToolSetting? Find(string key) => Fields.FirstOrDefault(x=>x.Key==key);
    public void SetInput(int fileCount,int pageCount)
    {
        bool firstMetadata=_pageCount==0&&pageCount>0;
        _fileCount=fileCount; _pageCount=pageCount;
        foreach(var key in new[]{"from","to"})if(Find(key) is PdfNumberSetting field)
        {field.Maximum=Math.Max(1,pageCount);if(firstMetadata&&key=="to")field.Value=field.Maximum;}
        RefreshConditions();
    }
    private string Choice(string key) => (Find(key) as PdfChoiceSetting)?.Value ?? "";
    private void Show(string key,bool visible) { if(Find(key) is {} field) field.IsVisible=visible; }
    private void RefreshConditions()
    {
        if(_tool==PdfToolId.Rotate)Show("filter",_fileCount>1);
        if(_tool==PdfToolId.ImageToPdf)
        {
            if(Find("orientation") is {} orientation) orientation.IsEnabled=Choice("paper")!="Fit. Same page size as image";
            Show("merge",_fileCount>1);
        }
        if(_tool==PdfToolId.PageNumbers)
        {
            Show("cover",Choice("pageType")=="Double-sided"); Show("customText",Choice("text")=="Custom");
            Show("from",_fileCount==1&&_pageCount>1); Show("to",_fileCount==1&&_pageCount>1);
            Show("excludeFirst",_fileCount>1); Show("excludeLast",_fileCount>1);
        }
        if(_tool==PdfToolId.Watermark)
        {
            bool text=Choice("mode")=="Place Text";
            foreach(var key in new[]{"text","font","fontSize","bold","italic","underline","color"}) Show(key,text);
            Show("image",!text); Show("position",!(Find("mosaic") as PdfToggleSetting)!.Value);
            Show("from",_fileCount==1); Show("to",_fileCount==1);
        }
    }
    public static PdfToolSettings Create(PdfToolId tool)
    {
        var fields=new List<PdfToolSetting>(); string note="";
        void C(string id,string label,string[] values,int selected=0)=>fields.Add(new PdfChoiceSetting(id,label,values,selected));
        void B(string id,string label,bool value=false)=>fields.Add(new PdfToggleSetting(id,label,value));
        void T(string id,string label,string value="")=>fields.Add(new PdfTextSetting(id,label,value));
        void N(string id,string label,int value,int min,int max)=>fields.Add(new PdfNumberSetting(id,label,value,min,max));
        void Font() { T("font","Font","Segoe UI"); N("fontSize","Size",14,1,100); B("bold","Bold");B("italic","Italic");B("underline","Underline");T("color","Color","#000000"); }
        string[] positions={"Top left","Top center","Top right","Middle left","Center","Middle right","Bottom left","Bottom center","Bottom right"};
        switch(tool)
        {
            case PdfToolId.ImageExport: break;
            case PdfToolId.ToWord: C("ocr","Text recognition",new[]{"Otomatis (Disarankan)","OCR","No OCR"}); fields[^1].IsEnabled=true; note="Seluruh halaman PDF dikonversi ke Word dengan preservasi layout dan font tingkat tinggi."; break;
            case PdfToolId.ToExcel: C("ocr","Text recognition",new[]{"Otomatis (Disarankan)","OCR","No OCR"}); fields[^1].IsEnabled=true; C("layout","Layout",new[]{"One Sheet","Multiple Sheets"},1); note="Seluruh halaman PDF dikonversi ke Excel dengan preservasi tabel."; break;
            case PdfToolId.ToPowerPoint: note="Seluruh halaman PDF dikonversi menjadi slide PowerPoint."; break;
            case PdfToolId.ToPdfA: C("conformance","PDF/A conformance",new[]{"PDF/A-1b","PDF/A-1a","PDF/A-2b","PDF/A-2u","PDF/A-2a","PDF/A-3b","PDF/A-3u","PDF/A-3a"},2); B("downgrade","Allow Downgrade"); note="Validasi conformance menunggu engine PDF/A."; break;
            case PdfToolId.ImageToPdf:
                C("orientation","Orientation",new[]{"Portrait","Landscape"}); C("paper","Page size",new[]{"Fit. Same page size as image","A4 (210x297 mm)","US Letter (215x279.4 mm)"},1);
                C("margin","Margin",new[]{"No margin","Small","Big"});B("merge","Merge all images into one PDF file",true);note="Import image/Office belum didukung. Orientasi awal Portrait merupakan pilihan NEXUS; default referensi belum pasti.";break;
            case PdfToolId.WordToPdf: case PdfToolId.ExcelToPdf: case PdfToolId.PowerPointToPdf:
                note="Referensi memiliki opsi Convert online. NEXUS tetap lokal; upload online dan engine konversi ini belum tersedia.";break;
            case PdfToolId.Rotate: C("filter","Selection filter",new[]{"All","Portrait","Landscape"});C("angle","Rotate",new[]{"0°","90° right","180°","90° left"});note="Berbeda dari rotasi tampilan reader. Menulis PDF hasil rotasi memerlukan engine.";break;
            case PdfToolId.PageNumbers:
                C("pageType","Page Type",new[]{"Single page","Double-sided"});B("cover","First page is cover page");C("position","Position",new[]{positions[0],positions[1],positions[2],positions[6],positions[7],positions[8]},5);
                C("margin","Margin",new[]{"Small","Recommended","Big"},1);C("text","Text",new[]{"Insert page number only","Page {n}","Page {n} of {p}","Custom"});T("customText","Custom text","Page {n} of {p}");Font();N("firstNumber","First number",1,1,1000000);
                N("from","From page",1,1,int.MaxValue);N("to","To page",1,1,int.MaxValue);N("excludeFirst","Exclude first pages",0,0,5);N("excludeLast","Exclude last pages",0,0,5);break;
            case PdfToolId.Watermark:
                C("mode","Watermark",new[]{"Place Text","Place Image"});T("text","Text");T("image","Image path");Font();C("position","Position",positions);B("mosaic","Mosaic");
                C("transparency","Transparency",new[]{"No transparency","75%","50%","25%"});C("rotation","Rotate",new[]{"No rotation","45°","90°","180°","270°","-45°"});
                N("from","From page",1,1,int.MaxValue);N("to","To page",1,1,int.MaxValue);C("layer","Layer",new[]{"Over the PDF content","Below the PDF content"});break;
            case PdfToolId.Crop: C("pages","Apply to",new[]{"All pages","Selected pages"});note="Pemilihan rectangle crop menunggu engine editor; tidak ada crop yang diterapkan ke sumber.";break;
            case PdfToolId.Edit: note="Annotate dan Edit isi asli memerlukan engine berbeda. Elements dan properti seleksi tersedia setelah engine editor dipasang.";break;
        }
        return new(tool,fields,note);
    }
}
