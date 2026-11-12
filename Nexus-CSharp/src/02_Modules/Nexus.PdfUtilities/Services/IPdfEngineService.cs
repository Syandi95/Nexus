namespace Nexus.PdfUtilities;

public sealed record PdfPageSize(int Width,int Height);

public class PdfPageImageResult
{
    public byte[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }

    public PdfPageImageResult(byte[] pixels, int width, int height, int stride)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
        Stride = stride;
    }
}

public interface IPdfEngineService
{
    void Initialize();
    int GetPageCount(string pdfPath);
    PdfPageImageResult? GetPageImage(string pdfPath, int maxWidth = 200);
    PdfPageImageResult? GetPageImage(string pdfPath,int pageIndex,int targetWidth,int targetHeight);
    IReadOnlyList<PdfPageSize> GetPageSizes(string pdfPath,System.Threading.CancellationToken token = default);
    System.Threading.Tasks.Task<string> ExtractTextAsync(string pdfPath, System.Threading.CancellationToken ct = default);
}
