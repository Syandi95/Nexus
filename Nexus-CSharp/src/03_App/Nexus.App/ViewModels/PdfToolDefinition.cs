namespace Nexus.App.ViewModels;

public enum PdfToolId { ImageExport, ToWord, ToExcel, ToPowerPoint, ToPdfA, ImageToPdf, WordToPdf, ExcelToPdf, PowerPointToPdf, Rotate, PageNumbers, Watermark, Edit, Crop }
public enum PdfToolCategory { ConvertToPdf, ConvertFromPdf, EditPdf }

public sealed record PdfToolDefinition(PdfToolId Id, PdfToolCategory Category, string Title, string Glyph, string Badge, string InputKind, bool IsAvailable = false)
{
    public PdfToolSettings Settings { get; } = PdfToolSettings.Create(Id);
    public bool AcceptsPdf => InputKind == "PDF";
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
        new(PdfToolId.ToPdfA,PdfToolCategory.ConvertFromPdf,"PDF to PDF/A","PDFPage","A","PDF"),
        new(PdfToolId.Rotate,PdfToolCategory.EditPdf,"Rotate PDF","Undo","","PDF"),
        new(PdfToolId.PageNumbers,PdfToolCategory.EditPdf,"Page Numbers","PDFPage","#","PDF"),
        new(PdfToolId.Watermark,PdfToolCategory.EditPdf,"Watermark","Brush","","PDF"),
        new(PdfToolId.Edit,PdfToolCategory.EditPdf,"Edit PDF","Edit","","PDF"),
        new(PdfToolId.Crop,PdfToolCategory.EditPdf,"Crop PDF","Crop","","PDF"),
        new(PdfToolId.ImageToPdf,PdfToolCategory.ConvertToPdf,"JPG / Image to PDF","Image","PDF","image"),
        new(PdfToolId.WordToPdf,PdfToolCategory.ConvertToPdf,"Word to PDF","Document","W","Word"),
        new(PdfToolId.ExcelToPdf,PdfToolCategory.ConvertToPdf,"Excel to PDF","Document","X","Excel"),
        new(PdfToolId.PowerPointToPdf,PdfToolCategory.ConvertToPdf,"PowerPoint to PDF","Document","P","PowerPoint")
    };
}
