using System.Windows.Media;

namespace Nexus.App.ViewModels;

public enum PdfToolId { ImageExport, ToWord, ToExcel, ToPowerPoint, ToPdfA, ImageToPdf, WordToPdf, ExcelToPdf, PowerPointToPdf, Rotate, PageNumbers, Watermark, Edit, Crop }
public enum PdfToolCategory { ConvertToPdf, ConvertFromPdf, EditPdf }

public sealed record PdfToolDefinition(PdfToolId Id, PdfToolCategory Category, string Title, string Glyph, string Badge, string InputKind, bool IsAvailable = false)
{
    // Exact iLovePDF Desktop semantic palette (matched from reference screenshots)
    public static readonly Brush PdfReaderRedBrush = CreateFrozenBrush(0xE5, 0x32, 0x2D);
    public static readonly Brush JpgYellowBrush = CreateFrozenBrush(0xEB, 0xB4, 0x0E);
    public static readonly Brush WordBlueBrush = CreateFrozenBrush(0x2B, 0x6C, 0xB0);
    public static readonly Brush PowerPointCoralBrush = CreateFrozenBrush(0xE0, 0x53, 0x3C);
    public static readonly Brush ExcelGreenBrush = CreateFrozenBrush(0x38, 0xA1, 0x69);
    public static readonly Brush PdfATealBrush = CreateFrozenBrush(0x2B, 0x7A, 0x9B);
    public static readonly Brush EditPlumBrush = CreateFrozenBrush(0x7E, 0x3B, 0x7B);
    public static readonly Brush PdfANotCompliantBgBrush = CreateFrozenAlphaBrush(0x26, 0xE5, 0x32, 0x2D);
    public static readonly Brush PdfACompliantBgBrush = CreateFrozenAlphaBrush(0x26, 0x38, 0xA1, 0x69);

    private static Brush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateFrozenAlphaBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    public PdfToolSettings Settings { get; } = PdfToolSettings.Create(Id);
    public bool AcceptsPdf => InputKind == "PDF";
    public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);
    public bool IsConversionTool => Category is PdfToolCategory.ConvertFromPdf or PdfToolCategory.ConvertToPdf;
    public bool IsEditTool => Category == PdfToolCategory.EditPdf;

    public Brush AccentBrush => Id switch
    {
        PdfToolId.ImageExport or PdfToolId.ImageToPdf => JpgYellowBrush,
        PdfToolId.ToWord or PdfToolId.WordToPdf => WordBlueBrush,
        PdfToolId.ToPowerPoint or PdfToolId.PowerPointToPdf => PowerPointCoralBrush,
        PdfToolId.ToExcel or PdfToolId.ExcelToPdf => ExcelGreenBrush,
        PdfToolId.ToPdfA => PdfATealBrush,
        PdfToolId.Rotate or PdfToolId.PageNumbers or PdfToolId.Watermark or PdfToolId.Edit or PdfToolId.Crop => EditPlumBrush,
        _ => WordBlueBrush
    };

    public Geometry IconGeometry => Geometry.Parse(Id switch
    {
        PdfToolId.Rotate => "M 6,4 H 13 L 17,8 V 20 H 6 Z M 13,4 V 8 H 17 M 11.5,11 A 3,3 0 1 1 9,15.5 M 11.5,9.2 L 13.5,11.2 L 11.2,12.8",
        PdfToolId.PageNumbers => "M 4,4 H 10.5 V 10.5 H 4 Z M 13.5,4 H 20 V 10.5 H 13.5 Z M 4,13.5 H 10.5 V 20 H 4 Z M 13.5,13.5 H 20 V 20 H 13.5 Z",
        PdfToolId.Watermark => "M 6,3 H 14 L 18,7 V 21 H 6 Z M 14,3 V 7 H 18 M 12,10.5 C 12,10.5 9.2,13.8 9.2,15.6 A 2.8,2.8 0 0 0 14.8,15.6 C 14.8,13.8 12,10.5 12,10.5 Z",
        PdfToolId.Edit => "M 15.5,4.5 L 19.5,8.5 L 8.5,19.5 H 4.5 V 15.5 Z M 13.5,6.5 L 17.5,10.5",
        PdfToolId.Crop => "M 7,3 V 17 H 21 M 3,7 H 17 V 21",
        _ => "M 3,2 H 11 L 14,5 V 15 H 3 Z M 11,2 V 5 H 14"
    });

    public string CategoryTitle => Category switch { PdfToolCategory.ConvertToPdf => "Convert to PDF", PdfToolCategory.ConvertFromPdf => "Convert from PDF", _ => "Edit PDF" };
    public string UnavailableReason => IsAvailable ? string.Empty
        : AcceptsPdf ? "Engine tool ini belum terpasang. Pengaturan tersimpan; proses belum tersedia."
        : $"Memerlukan input {InputKind}. Impor format ini dan engine Convert to PDF belum tersedia.";

    public static IReadOnlyList<PdfToolDefinition> CreateCatalog() => new PdfToolDefinition[]
    {
        new(PdfToolId.ImageExport,PdfToolCategory.ConvertFromPdf,"PDF to JPG","Image","JPG","PDF",true),
        new(PdfToolId.ToWord,PdfToolCategory.ConvertFromPdf,"PDF to Word","Document","W","PDF",true),
        new(PdfToolId.ToPowerPoint,PdfToolCategory.ConvertFromPdf,"PDF to PowerPoint","Document","P","PDF",true),
        new(PdfToolId.ToExcel,PdfToolCategory.ConvertFromPdf,"PDF to Excel","Document","X","PDF",true),
        new(PdfToolId.ToPdfA,PdfToolCategory.ConvertFromPdf,"PDF to PDF/A","PDFPage","/A","PDF"),
        new(PdfToolId.Rotate,PdfToolCategory.EditPdf,"Rotate PDF","RotateRight","","PDF"),
        new(PdfToolId.PageNumbers,PdfToolCategory.EditPdf,"Page Numbers","PDFPage","12","PDF"),
        new(PdfToolId.Watermark,PdfToolCategory.EditPdf,"Watermark","Brush","","PDF"),
        new(PdfToolId.Edit,PdfToolCategory.EditPdf,"Edit PDF","Edit","","PDF"),
        new(PdfToolId.Crop,PdfToolCategory.EditPdf,"Crop PDF","Crop","","PDF"),
        new(PdfToolId.ImageToPdf,PdfToolCategory.ConvertToPdf,"JPG / Image to PDF","Image","JPG","image"),
        new(PdfToolId.WordToPdf,PdfToolCategory.ConvertToPdf,"Word to PDF","Document","W","Word"),
        new(PdfToolId.ExcelToPdf,PdfToolCategory.ConvertToPdf,"Excel to PDF","Document","X","Excel"),
        new(PdfToolId.PowerPointToPdf,PdfToolCategory.ConvertToPdf,"PowerPoint to PDF","Document","P","PowerPoint")
    };
}
