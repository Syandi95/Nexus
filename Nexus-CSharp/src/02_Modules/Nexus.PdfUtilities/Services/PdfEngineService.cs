using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Docnet.Core;
using Docnet.Core.Models;

namespace Nexus.PdfUtilities;

public class PdfEngineService : IPdfEngineService
{
    public const int MaximumReaderPixels = 12_000_000;

    private static readonly ConcurrentDictionary<string, (long LastWriteTicks, long Length, IReadOnlyList<PdfPageSize> Sizes)> PageSizeCache =
        new(StringComparer.OrdinalIgnoreCase);

    public void Initialize()
    {
    }

    public int GetPageCount(string pdfPath)
    {
        if (string.IsNullOrWhiteSpace(pdfPath))
            throw new ArgumentException("Path cannot be null or empty", nameof(pdfPath));
            
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException("PDF file not found", pdfPath);

        using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions());
        return docReader.GetPageCount();
    }

    public PdfPageImageResult? GetPageImage(string pdfPath, int maxWidth = 200)
    {
        return GetPageImage(pdfPath, maxWidth, int.MaxValue);
    }

    public PdfPageImageResult? GetPageImage(string pdfPath, int targetWidth, int targetHeight)
        => RenderPage(pdfPath,0,targetWidth,targetHeight,false);

    public PdfPageImageResult? GetPageImage(string pdfPath,int pageIndex,int targetWidth,int targetHeight)
        => RenderPage(pdfPath,pageIndex,targetWidth,targetHeight,true);

    public IReadOnlyList<PdfPageSize> GetPageSizes(string pdfPath,CancellationToken token = default)
    {
        if (TryGetCachedPageSizes(pdfPath, out var cached))
        {
            token.ThrowIfCancellationRequested();
            return cached;
        }

        using var document=DocLib.Instance.GetDocReader(pdfPath,new PageDimensions(1.0));
        var sizes=new List<PdfPageSize>(document.GetPageCount());
        for(int index=0;index<document.GetPageCount();index++)
        {
            token.ThrowIfCancellationRequested();
            using var page=document.GetPageReader(index);
            int width=page.GetPageWidth(),height=page.GetPageHeight();
            if(width<=0||height<=0) throw new InvalidDataException("Dimensi halaman PDF tidak valid.");
            sizes.Add(new(width,height));
        }

        StoreCachedPageSizes(pdfPath, sizes);
        return sizes;
    }

    private static bool TryGetCachedPageSizes(string pdfPath, out IReadOnlyList<PdfPageSize> sizes)
    {
        sizes = Array.Empty<PdfPageSize>();
        try
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return false;
            var fi = new FileInfo(pdfPath);
            if (PageSizeCache.TryGetValue(pdfPath, out var entry) &&
                entry.LastWriteTicks == fi.LastWriteTimeUtc.Ticks &&
                entry.Length == fi.Length &&
                entry.Sizes.Count > 0)
            {
                sizes = entry.Sizes;
                return true;
            }
        }
        catch { }
        return false;
    }

    private static void StoreCachedPageSizes(string pdfPath, IReadOnlyList<PdfPageSize> sizes)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || sizes.Count == 0 || !File.Exists(pdfPath))
                return;
            var fi = new FileInfo(pdfPath);
            if (PageSizeCache.Count > 64)
                PageSizeCache.Clear();
            PageSizeCache[pdfPath] = (fi.LastWriteTimeUtc.Ticks, fi.Length, sizes);
        }
        catch { }
    }

    private PdfPageImageResult? RenderPage(string pdfPath,int pageIndex,int targetWidth,int targetHeight,bool boundReader)
    {
        try
        {
            if (pageIndex < 0 || targetWidth <= 0 || targetHeight <= 0)
                return null;

            int sourceWidth;
            int sourceHeight;

            if (TryGetCachedPageSizes(pdfPath, out var cachedSizes))
            {
                if (pageIndex >= cachedSizes.Count) return null;
                sourceWidth = cachedSizes[pageIndex].Width;
                sourceHeight = cachedSizes[pageIndex].Height;
            }
            else
            {
                using (var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(1.0)))
                {
                    int pageCount = docReader.GetPageCount();
                    if (pageIndex >= pageCount) return null;

                    using var pageReader = docReader.GetPageReader(pageIndex);
                    sourceWidth = pageReader.GetPageWidth();
                    sourceHeight = pageReader.GetPageHeight();
                }
            }

            if (sourceWidth <= 0 || sourceHeight <= 0)
                return null;

            double scale = Math.Min((double)targetWidth / sourceWidth, (double)targetHeight / sourceHeight);
            if(boundReader)
                scale=Math.Min(scale,Math.Sqrt((double)MaximumReaderPixels/((sourceWidth+1.0)*(sourceHeight+1.0))));

            // Ask Docnet/PDFium to rasterize at the preview dimensions. No full-size
            // image buffer or post-render resize is created for the thumbnail.
            using var targetDocReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(scale));
            using var targetPageReader = targetDocReader.GetPageReader(pageIndex);
            byte[] pixels = targetPageReader.GetImage(RenderFlags.RenderAnnotations);
            int width = targetPageReader.GetPageWidth();
            int height = targetPageReader.GetPageHeight();

            if (width <= 0 || height <= 0)
                return null;

            return new PdfPageImageResult(pixels, width, height, width * 4);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> ExtractTextAsync(string pdfPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pdfPath))
            throw new ArgumentException("Path cannot be null or empty", nameof(pdfPath));

        if (!File.Exists(pdfPath))
            throw new FileNotFoundException("PDF file not found", pdfPath);

        return await Task.Run(() =>
        {
            using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions());
            int pageCount = docReader.GetPageCount();
            if (pageCount <= 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < pageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                using var pageReader = docReader.GetPageReader(i);
                string? pageText = pageReader.GetText();
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    sb.AppendLine(pageText);
                }
            }
            return sb.ToString();
        }, ct).ConfigureAwait(false);
    }
}
