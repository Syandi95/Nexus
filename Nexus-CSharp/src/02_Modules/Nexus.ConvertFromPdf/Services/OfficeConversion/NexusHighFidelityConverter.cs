using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;
using W = DocumentFormat.OpenXml.Wordprocessing;
using M = DocumentFormat.OpenXml.Math;
using P = DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Nexus.PdfUtilities.OfficeConversion
{
    public enum ConversionLayoutMode
    {
        Editable = 0,
        VisualFidelity = 1
    }

    public sealed record OcrRecognizedWord(
        string Text,
        double LeftPt,
        double BottomPt,
        double RightPt,
        double TopPt,
        double Confidence = 1.0,
        int LineIndex = -1);

    public interface INexusOcrBackend
    {
        IReadOnlyList<OcrRecognizedWord> RecognizePage(SKBitmap pageBitmap, double pageWidthPt, double pageHeightPt, int pageNo);
    }

    public sealed class NexusConversionOptions
    {
        public ConversionLayoutMode LayoutMode { get; set; } = ConversionLayoutMode.Editable;
        public bool UseEmptyParagraphsForLargeGaps { get; set; } = true;
        public bool EnableOcrLogoCropping { get; set; } = true;
        public bool ValidateOpenXmlOutput { get; set; } = true;
        /// <summary>Actual target renderer resolution; 600 DPI matches the verified Microsoft Print to PDF environment.
        /// Workbook print-quality settings do not force a driver to use this resolution; other environments need validation.</summary>
        public int TargetExcelPrintDpi { get; set; } = 600;
        public bool EnableScannedPageOcr { get; set; } = true;
        public bool EnableCurveTextOcr { get; set; } = true;
        public string OcrLanguage { get; set; } = "ind+eng";
        public string? OneOcrDirectory { get; set; }
        public string? PpOcrModelOrExecutablePath { get; set; }
        public string? TesseractExecutablePath { get; set; }
        public INexusOcrBackend? OcrBackend { get; set; }
        public Func<SKBitmap, double, double, int, IReadOnlyList<OcrRecognizedWord>>? CustomOcrProvider { get; set; }
        public bool ReconstructExcelFormulas { get; set; } = true;
        public bool ExtractVectorIllustrationsAsPng { get; set; } = true;
        public bool EmitFontFallbackTable { get; set; } = true;
    }

    public sealed class ConversionExecutionResult
    {
        public bool IsSuccess { get; init; }
        public string OutputPath { get; init; } = string.Empty;
        public int PagesProcessed { get; init; }
        public int TablesReconstructed { get; init; }
        public int ImagesProcessed { get; init; }
        public long OutputSizeBytes { get; init; }
        public TimeSpan Elapsed { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public IReadOnlyList<string> CreatedFiles { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    public interface INexusHighFidelityConverter
    {
        Task<ConversionExecutionResult> ConvertToDocxAsync(
            string inputPdfPath,
            string outputDocxPath,
            NexusConversionOptions? options = null,
            IProgress<double>? progress = null,
            CancellationToken ct = default);

        Task<ConversionExecutionResult> ConvertToXlsxAsync(
            string inputPdfPath,
            string outputXlsxPath,
            bool multipleSheets = true,
            NexusConversionOptions? options = null,
            IProgress<double>? progress = null,
            CancellationToken ct = default);

        Task<ConversionExecutionResult> ConvertToPptxAsync(
            string inputPdfPath,
            string outputPptxPath,
            NexusConversionOptions? options = null,
            IProgress<double>? progress = null,
            CancellationToken ct = default);

        Task<ConversionExecutionResult> ExtractImagesAsync(
            string inputPdfPath,
            string outputDirectory,
            NexusConversionOptions? options = null,
            IProgress<double>? progress = null,
            CancellationToken ct = default);
    }

    public sealed class NexusHighFidelityConverter : INexusHighFidelityConverter
    {
        private static readonly Regex BulletPrefixRegex = new(
            @"^([\u2022\u25CF\u25AA\u25E6\u00AB\u00BB\*\u2023\u2043\u2219\uF0B7\uF0A7\u00B7])\s*(.*)$",
            RegexOptions.Compiled);

        private static readonly Regex NumberedListRegex = new(
            @"^(\d+[\.\)]|[a-zA-Z][\.\)])\s+(.*)$",
            RegexOptions.Compiled);

        private static readonly Regex KeyValueMetadataRegex = new(
            @"^[\p{L}\p{N}][\p{L}\p{N}\s\-\/\._]{0,28}:\s*\S+",
            RegexOptions.Compiled);

        private static readonly Regex PageFractionRegex = new(
            @"^\d+/\d+$",
            RegexOptions.Compiled);

        private static readonly Regex UriSchemeRegex = new(
            @"^(https?:/+|ftp://|www\.)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public NexusHighFidelityConverter()
        {
        }

        public Task<ConversionExecutionResult> ConvertToDocxAsync(
            string inputPdfPath,
            string outputDocxPath,
            CancellationToken ct)
            => ConvertToDocxAsync(inputPdfPath, outputDocxPath, null, null, ct);

        public Task<ConversionExecutionResult> ConvertToXlsxAsync(
            string inputPdfPath,
            string outputXlsxPath,
            bool multipleSheets,
            CancellationToken ct)
            => ConvertToXlsxAsync(inputPdfPath, outputXlsxPath, multipleSheets, null, null, ct);

        public Task<ConversionExecutionResult> ConvertToPptxAsync(
            string inputPdfPath,
            string outputPptxPath,
            CancellationToken ct)
            => ConvertToPptxAsync(inputPdfPath, outputPptxPath, null, null, ct);

        public Task<ConversionExecutionResult> ExtractImagesAsync(
            string inputPdfPath,
            string outputDirectory,
            CancellationToken ct)
            => ExtractImagesAsync(inputPdfPath, outputDirectory, null, null, ct);

        // =====================================================================
        // 1. PDF -> WORD (.DOCX) — UNIFIED DIGITAL & OCR 1:1 ENGINE
        // =====================================================================
        public Task<ConversionExecutionResult> ConvertToDocxAsync(
            string inputPdfPath,
            string outputDocxPath,
            NexusConversionOptions? options = null,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                options ??= new NexusConversionOptions();
                var warnings = new List<string>();

                EnsureDirectory(outputDocxPath);
                string tempPath = CreateStagingFilePath(outputDocxPath);

                int tablesCount = 0;
                int imagesCount = 0;
                int totalPages = 0;

                try
                {
                    using var pdf = PdfDocument.Open(inputPdfPath, new ParsingOptions { UseLenientParsing = true });
                    totalPages = pdf.NumberOfPages;

                    using (var wordDoc = WordprocessingDocument.Create(tempPath, WordprocessingDocumentType.Document))
                    {
                        wordDoc.PackageProperties.Creator = pdf.Information.Creator ?? "NEXUS High-Fidelity Engine";
                        wordDoc.PackageProperties.Title = pdf.Information.Title ?? Path.GetFileNameWithoutExtension(inputPdfPath);

                        var mainPart = wordDoc.AddMainDocumentPart();
                        mainPart.Document = new W.Document();
                        var body = mainPart.Document.AppendChild(new W.Body());

                        uint docPrId = 1U;
                        bool prevPageEndedWithList = false;
                        string? dominantDocumentOcrSerifFont = null;

                        for (int pageNo = 1; pageNo <= totalPages; pageNo++)
                        {
                            ct.ThrowIfCancellationRequested();
                            var page = pdf.GetPage(pageNo);

                            var rawWords = page.GetWords(NearestNeighbourWordExtractor.Instance).ToList();
                            var pageImages = page.GetImages().ToList();
                            bool hasVisibleText = page.Letters.Any(letter => letter.RenderingMode != TextRenderingMode.Neither
                                && letter.RenderingMode != TextRenderingMode.NeitherClip);

                            SKBitmap? fullPageBitmap = null;
                            double pageBottomMarginPt = 14;
                            var standaloneImages = new List<StandaloneImageItem>();
                            double pageLeftMarginPt = 42.0;
                            double pageRightMarginPt = 42.0;
                            double pageTopMarginPt = 16.0;

                            foreach (var img in pageImages)
                            {
                                double wPt = img.Bounds.Width > 0 ? img.Bounds.Width : img.WidthInSamples * 0.75;
                                double hPt = img.Bounds.Height > 0 ? img.Bounds.Height : img.HeightInSamples * 0.75;
                                // A decorative background behind visible text is not an OCR scan.
                                bool isFullPageBackdrop = (wPt >= page.Width * 0.75 && hPt >= page.Height * 0.75) && rawWords.Count > 5 && !hasVisibleText;

                                byte[]? pngBytes = DecodePdfImageToPng(img, pdf, warnings);
                                if (pngBytes == null || pngBytes.Length == 0) continue;

                                if (isFullPageBackdrop && fullPageBitmap == null)
                                {
                                    fullPageBitmap = SKBitmap.Decode(pngBytes);
                                }
                                else
                                {
                                    standaloneImages.Add(new StandaloneImageItem(
                                        pngBytes,
                                        img.Bounds.Top,
                                        img.Bounds.Bottom,
                                        img.Bounds.Left,
                                        img.Bounds.Right,
                                        wPt,
                                        hPt));
                                }
                            }

                            rawWords = rawWords.Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
                            string initialPageFont = CleanFontName(rawWords.FirstOrDefault()?.FontName, false);
                            var pagePatterns = ExtractPagePatternShadings(pdf, page);
                            var wordArt = options.LayoutMode == ConversionLayoutMode.Editable
                                ? RecoverTaggedWordArt(pdf, page, string.IsNullOrWhiteSpace(initialPageFont) || initialPageFont == "Calibri" ? "Arial" : initialPageFont)
                                : new List<TaggedWordArt>();
                            var verticalWordShapes = fullPageBitmap == null
                                ? ExtractVerticalRotatedTextShapes(ref rawWords, page, pagePatterns)
                                : new List<VerticalRotatedTextItem>();

                            List<RichWord>? ocrRecognizedWords = null;
                            var consumedCurveGlyphPaths = new HashSet<UglyToad.PdfPig.Graphics.PdfPath>();
                            if (rawWords.Count == 0 && wordArt.Count == 0 && verticalWordShapes.Count == 0 && pageImages.Count > 0)
                            {
                                if (fullPageBitmap == null)
                                {
                                    var largestImg = standaloneImages.OrderByDescending(x => x.WidthPt * x.HeightPt).FirstOrDefault();
                                    if (largestImg != null && largestImg.WidthPt >= page.Width * 0.70 && largestImg.HeightPt >= page.Height * 0.70)
                                    {
                                        using var candidateBmp = SKBitmap.Decode(largestImg.PngBytes);
                                        if (candidateBmp != null)
                                        {
                                            ocrRecognizedWords = TryRecognizeScannedPageWords(candidateBmp, page.Width, page.Height, pageNo, options, warnings);
                                            if (ocrRecognizedWords != null && ocrRecognizedWords.Count > 0)
                                            {
                                                fullPageBitmap = candidateBmp.Copy();
                                                standaloneImages.Remove(largestImg);
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    ocrRecognizedWords = TryRecognizeScannedPageWords(fullPageBitmap, page.Width, page.Height, pageNo, options, warnings);
                                }

                                if (ocrRecognizedWords != null && ocrRecognizedWords.Count > 0)
                                {
                                    string pageOcrFont = ocrRecognizedWords[0].FontName;
                                    if (pageOcrFont == "Bookman Old Style")
                                    {
                                        dominantDocumentOcrSerifFont = "Bookman Old Style";
                                    }
                                    else if (dominantDocumentOcrSerifFont == null && (pageOcrFont == "Times New Roman" || pageOcrFont == "Georgia"))
                                    {
                                        dominantDocumentOcrSerifFont = pageOcrFont;
                                    }

                                    if (dominantDocumentOcrSerifFont != null
                                        && (pageOcrFont == "Times New Roman" || pageOcrFont == "Bookman Old Style" || pageOcrFont == "Georgia")
                                        && pageOcrFont != dominantDocumentOcrSerifFont)
                                    {
                                        ocrRecognizedWords = ocrRecognizedWords
                                            .Select(w => w with { FontName = dominantDocumentOcrSerifFont })
                                            .ToList();
                                    }
                                }
                            }

                            // Also run curve-text OCR if the page has outlined curve text and is not already a tagged WordArt page
                            if ((ocrRecognizedWords == null || ocrRecognizedWords.Count == 0)
                                && rawWords.Count == 0
                                && wordArt.Count == 0
                                && verticalWordShapes.Count == 0
                                && options.LayoutMode == ConversionLayoutMode.Editable
                                && options.EnableCurveTextOcr
                                && (options.CustomOcrProvider != null || options.OcrBackend != null || page.ExperimentalAccess.Paths.Count >= 80))
                            {
                                ocrRecognizedWords = TryRecognizeCurveTextPageWords(page, pageNo, options, warnings, consumedCurveGlyphPaths);
                            }

                            if (rawWords.Count == 0 && wordArt.Count == 0 && verticalWordShapes.Count == 0 && pageImages.Count > 0 && (ocrRecognizedWords == null || ocrRecognizedWords.Count == 0))
                            {
                                warnings.Add($"Halaman {pageNo} hanya berisi gambar tanpa lapisan teks.");
                            }

                            try
                            {
                                var styleRuleRects = ExtractInlineTextDecorationRects(page);
                                var pageInlinePills = ExtractPageInlineBackgroundPills(page, consumedCurveGlyphPaths);
                                bool isCurveOcrPage = ocrRecognizedWords != null && ocrRecognizedWords.Count > 0 && consumedCurveGlyphPaths.Count > 0;
                                var lines = ocrRecognizedWords != null && ocrRecognizedWords.Count > 0
                                    ? BuildVisualLinesFromRichWords(ocrRecognizedWords, page.Width, page.Height, true, pageNo, totalPages)
                                    : BuildVisualLines(rawWords, fullPageBitmap, page.Width, page.Height, pageNo, totalPages, warnings, styleRuleRects, pageInlinePills);
                                var vectorRects = ExtractVectorGridBounds(page);
                                if (fullPageBitmap != null && vectorRects.Count == 0)
                                {
                                    vectorRects.AddRange(ExtractBitmapTableGridRects(fullPageBitmap, page.Width, page.Height));
                                }
                                var dividerPaths = page.ExperimentalAccess.Paths
                                    .SelectMany(path => path.Select(sub => (Bounds: sub.GetBoundingRectangle(), Path: path)))
                                    .Where(item => item.Bounds.HasValue && item.Bounds.Value.Width > page.Width / 2 && item.Bounds.Value.Height <= 4).ToList();

                                var bodyLines = lines.Where(l => l.Bottom > 35.0 && l.Top < page.Height - 30.0).ToList();
                                pageLeftMarginPt = bodyLines.Count > 0
                                    ? Math.Clamp(bodyLines.Min(l => l.Left), 24.0, 72.0)
                                    : 42.0;
                                double pageRightLimitPt = bodyLines.Count > 0
                                    ? Math.Clamp(bodyLines.Max(l => l.Right), page.Width - 72.0, page.Width - 24.0)
                                    : page.Width - 42.0;
                                double usableWidthPt = Math.Max(240.0, pageRightLimitPt - pageLeftMarginPt);
                                pageRightMarginPt = page.Width - pageRightLimitPt;

                                var regions = SegmentLinesIntoRegions(lines, vectorRects, useVectorGrid: true);
                                if (isCurveOcrPage)
                                {
                                    // On curve-OCR pages, only keep tables that have explicit vector grid rules so formula/code lines are never misclassified as borderless tables.
                                    foreach (var r in regions)
                                    {
                                        if (r.IsTable && r.RuledRows.Count == 0)
                                        {
                                            r.IsTable = false;
                                        }
                                    }
                                }
                                bool ruledPage = regions.Any(r => r.RuledRows.Count > 0);
                                // Digital line geometry already contains leading; adding Word's default leading accumulates page drift.
                                bool vectorArtworkPage = lines.Count == 0 && page.ExperimentalAccess.Paths.Any(path => path.IsFilled || path.IsStroked);
                                bool positionedPage = ruledPage || (fullPageBitmap == null && (lines.Count > 0 || standaloneImages.Count > 1 || vectorArtworkPage));
                                var recoveredPaths = wordArt.SelectMany(a => a.Paths).Concat(consumedCurveGlyphPaths).ToHashSet();
                                var clippedPagePaths = GetClippedPagePaths(page, recoveredPaths);
                                bool diagramVectorPage = !isCurveOcrPage && positionedPage && !vectorArtworkPage && !ruledPage
                                    && (pagePatterns.Count > 0 || verticalWordShapes.Count > 0 || CountPairedFilledStrokedShapes(clippedPagePaths) >= 3);

                                // On flowchart/diagram pages (e.g. Puskesmas), bind text lines & vertical rotated labels directly into their enclosing vector shapes as native Word TextBoxes (<v:textbox><w:txbxContent>)
                                var shapeHorizontalLines = new Dictionary<UglyToad.PdfPig.Graphics.PdfPath, List<VisualLine>>();
                                var shapeVerticalTexts = new Dictionary<UglyToad.PdfPig.Graphics.PdfPath, List<VerticalRotatedTextItem>>();
                                if (diagramVectorPage)
                                {
                                    var diagramContainers = clippedPagePaths
                                        .Where(item => item.Path.IsFilled
                                            && !IsPureWhiteCanvasBackdrop(item.Path, item.EffectiveBounds, page.Width, page.Height)
                                            && item.EffectiveBounds.Width >= 18.0 && item.EffectiveBounds.Height >= 16.0
                                            && item.EffectiveBounds.Width <= page.Width * 0.85 && item.EffectiveBounds.Height <= page.Height * 0.85)
                                        .OrderBy(item => item.EffectiveBounds.Width * item.EffectiveBounds.Height)
                                        .ToList();

                                    var consumedDiagramLines = new HashSet<VisualLine>();
                                    foreach (var dLine in lines)
                                    {
                                        double cx = (dLine.Left + dLine.Right) * 0.5;
                                        double cy = (dLine.Bottom + dLine.Top) * 0.5;
                                        var matchShape = diagramContainers.FirstOrDefault(c =>
                                            cx >= c.EffectiveBounds.Left && cx <= c.EffectiveBounds.Right
                                            && cy >= c.EffectiveBounds.Bottom && cy <= c.EffectiveBounds.Top
                                            && dLine.Left >= c.EffectiveBounds.Left - 6.0 && dLine.Right <= c.EffectiveBounds.Right + 6.0
                                            && dLine.Bottom >= c.EffectiveBounds.Bottom - 6.0 && dLine.Top <= c.EffectiveBounds.Top + 6.0);
                                        if (matchShape?.Path != null)
                                        {
                                            if (!shapeHorizontalLines.TryGetValue(matchShape.Path, out var list))
                                            {
                                                list = new List<VisualLine>();
                                                shapeHorizontalLines[matchShape.Path] = list;
                                            }
                                            list.Add(dLine);
                                            consumedDiagramLines.Add(dLine);
                                        }
                                        else if (dLine.Segments.Count > 1)
                                        {
                                            var matchedSegs = new List<(WordSegment Seg, ClippedVectorPathItem Container)>();
                                            foreach (var seg in dLine.Segments)
                                            {
                                                double scx = (seg.Left + seg.Right) * 0.5;
                                                double scy = (seg.Bottom + seg.Top) * 0.5;
                                                var cMatch = diagramContainers.FirstOrDefault(c =>
                                                    scx >= c.EffectiveBounds.Left && scx <= c.EffectiveBounds.Right
                                                    && scy >= c.EffectiveBounds.Bottom && scy <= c.EffectiveBounds.Top
                                                    && seg.Left >= c.EffectiveBounds.Left - 8.0 && seg.Right <= c.EffectiveBounds.Right + 8.0
                                                    && seg.Bottom >= c.EffectiveBounds.Bottom - 8.0 && seg.Top <= c.EffectiveBounds.Top + 8.0);
                                                if (cMatch?.Path != null)
                                                {
                                                    matchedSegs.Add((seg, cMatch));
                                                }
                                            }
                                            if (matchedSegs.Count == dLine.Segments.Count)
                                            {
                                                foreach (var (seg, cont) in matchedSegs)
                                                {
                                                    if (!shapeHorizontalLines.TryGetValue(cont.Path, out var list))
                                                    {
                                                        list = new List<VisualLine>();
                                                        shapeHorizontalLines[cont.Path] = list;
                                                    }
                                                    var subLine = new VisualLine(
                                                        seg.Words,
                                                        seg.Left,
                                                        seg.Right,
                                                        dLine.Bottom,
                                                        dLine.Top,
                                                        dLine.FontSizePt,
                                                        seg.Words.Count(w => w.IsBold) > seg.Words.Count / 2,
                                                        new List<WordSegment> { seg });
                                                    list.Add(subLine);
                                                }
                                                consumedDiagramLines.Add(dLine);
                                            }
                                        }
                                    }

                                    if (consumedDiagramLines.Count > 0)
                                    {
                                        lines.RemoveAll(consumedDiagramLines.Contains);
                                        foreach (var r in regions)
                                        {
                                            r.Lines.RemoveAll(consumedDiagramLines.Contains);
                                        }
                                        regions.RemoveAll(r => !r.IsTable && r.Lines.Count == 0);
                                    }

                                    foreach (var vItem in verticalWordShapes.ToList())
                                    {
                                        double vcx = (vItem.Bounds.Left + vItem.Bounds.Right) * 0.5;
                                        double vcy = (vItem.Bounds.Bottom + vItem.Bounds.Top) * 0.5;
                                        var matchShape = diagramContainers.FirstOrDefault(c =>
                                            vcx >= c.EffectiveBounds.Left - 4.0 && vcx <= c.EffectiveBounds.Right + 4.0
                                            && vcy >= c.EffectiveBounds.Bottom - 4.0 && vcy <= c.EffectiveBounds.Top + 4.0);
                                        if (matchShape?.Path != null)
                                        {
                                            if (!shapeVerticalTexts.TryGetValue(matchShape.Path, out var vList))
                                            {
                                                vList = new List<VerticalRotatedTextItem>();
                                                shapeVerticalTexts[matchShape.Path] = vList;
                                            }
                                            vList.Add(vItem);
                                            verticalWordShapes.Remove(vItem);
                                        }
                                    }
                                }

                                if (positionedPage && regions.Count > 0) pageTopMarginPt = Math.Max(0, page.Height - regions[0].Top - 1.5);
                                if (positionedPage && lines.Count > 0)
                                {
                                    pageBottomMarginPt = Math.Max(0, Math.Min(pageBottomMarginPt, lines.Min(line => line.Bottom - line.FontSizePt)));
                                    // Headers/footers can extend beyond body margins; reserve their full source width.
                                    pageRightMarginPt = Math.Max(0, Math.Min(pageRightMarginPt, page.Width - lines.Max(line => line.Right)));
                                }
                                double inlineWidthPt = Math.Max(1, page.Width - pageLeftMarginPt - pageRightMarginPt);
                                double inlineHeightPt = Math.Max(1, page.Height - pageTopMarginPt - pageBottomMarginPt);
                                double prevBottomPt = double.NaN;
                                var pendingStandalone = standaloneImages.OrderByDescending(x => x.TopPt).ToList();
                                var floatingImages = pendingStandalone.Where(img => positionedPage || lines.Any(l => l.Top > img.BottomPt && l.Bottom < img.TopPt)).ToList();
                                pendingStandalone.RemoveAll(floatingImages.Contains);
                                // Inferred tables have no source borders and can expand over independently positioned artwork.
                                bool imageLayoutConflict = floatingImages.Any(img => regions.Any(r => r.IsTable && r.RuledRows.Count == 0
                                    && r.Top > img.BottomPt && r.Bottom < img.TopPt));
                                int pageBodyStart = body.ChildElements.Count;
                                string defaultPageFont = lines.FirstOrDefault()?.Words.FirstOrDefault()?.FontName ?? "Calibri";
                                bool useEnterGaps = options.LayoutMode == ConversionLayoutMode.Editable && options.UseEmptyParagraphsForLargeGaps;

                                // Collect canonical X tab/column anchors across multi-segment lines so nearby header/address columns snap cleanly
                                var canonicalTabPositionsPt = lines
                                    .SelectMany(l => l.Segments.Skip(1).Select(s => s.Left))
                                    .OrderBy(x => x)
                                    .Aggregate(new List<double>(), (acc, x) =>
                                    {
                                        if (acc.Count == 0 || x - acc[^1] > 4.0) acc.Add(x);
                                        return acc;
                                    });

                                var croppedLogoRects = new List<SKRectI>();
                                if (fullPageBitmap != null && pageNo == 1 && lines.Count > 0
                                    && TryExtractScannedHeaderEmblem(fullPageBitmap, page.Width, page.Height, lines[0].Top, croppedLogoRects,
                                        out byte[]? emblemPng, out double emblemW, out double emblemH, out double emblemBottomPt))
                                {
                                    var emblemPara = new W.Paragraph(
                                        new W.ParagraphProperties(
                                            new W.SpacingBetweenLines { Before = "0", After = "120", Line = "240", LineRule = W.LineSpacingRuleValues.Auto },
                                            new W.Justification { Val = W.JustificationValues.Center }),
                                        new W.Run(BuildInlineDrawingElement(mainPart, emblemPng!, emblemW, emblemH, docPrId++, inlineWidthPt, inlineHeightPt)));
                                    body.Append(emblemPara);
                                    prevBottomPt = emblemBottomPt;
                                    imagesCount++;
                                }

                                if (fullPageBitmap != null && options.EnableOcrLogoCropping && lines.Count > 0)
                                {
                                    ExtractScannedWetStampsAsFloatingImages(
                                        fullPageBitmap,
                                        page.Width,
                                        page.Height,
                                        lines,
                                        croppedLogoRects,
                                        floatingImages);
                                }

                                foreach (var region in regions)
                                {
                                    bool formTabs = region.RuledRows.Count == 0 && region.Lines.Count > 0
                                        && region.Lines.All(l => KeyValueMetadataRegex.IsMatch(l.FullText)
                                            || (l.Segments.Count >= 2 && l.Segments[1].Text.TrimStart().StartsWith(':')));
                                    bool bodyList = region.RuledRows.Count == 0 && HasAnyBodyListItems(region.Lines, prevPageEndedWithList);
                                    bool emitAsTable = region.IsTable
                                        && (region.RuledRows.Count >= 1 || (region.TableRows.Count >= 2 && region.ColumnAnchors.Count >= 2))
                                        && !((imageLayoutConflict || formTabs || bodyList || isCurveOcrPage) && region.RuledRows.Count == 0);

                                    if (emitAsTable)
                                    {
                                        // Pre-exclude standalone images that belong inside this ruled table so they are not emitted above or as page-floating anchors
                                        if (region.RuledRows.Count > 0 && region.GridColumns.Count >= 2)
                                        {
                                            double tblLeft = region.GridColumns[0] - 2.0;
                                            double tblRight = region.GridColumns[^1] + 2.0;
                                            double tblBottom = region.Bottom - 2.0;
                                            double tblTop = region.Top + 2.0;
                                            pendingStandalone.RemoveAll(img =>
                                            {
                                                double icx = (img.LeftPt + img.RightPt) * 0.5;
                                                double icy = (img.BottomPt + img.TopPt) * 0.5;
                                                return icx >= tblLeft && icx <= tblRight && icy >= tblBottom && icy <= tblTop;
                                            });
                                        }

                                        // Emit standalone images situated above this table
                                        while (pendingStandalone.Count > 0 && pendingStandalone[0].BottomPt >= region.Top - 4.0)
                                        {
                                            var sImg = pendingStandalone[0];
                                            pendingStandalone.RemoveAt(0);
                                            double imgGapPt = !double.IsNaN(prevBottomPt) ? Math.Max(0.0, prevBottomPt - sImg.TopPt) : 0.0;
                                            if (useEnterGaps)
                                            {
                                                AppendEmptyEnterParagraphsForGap(body, ref imgGapPt, defaultPageFont);
                                            }

                                            int indentDxa = (int)Math.Round(Math.Max(0, sImg.LeftPt - pageLeftMarginPt) * 20.0);
                                            body.Append(CreateInlineImageParagraph(mainPart, sImg.PngBytes, sImg.WidthPt, sImg.HeightPt, docPrId++, indentDxa, imgGapPt,
                                                maxWidthPt: Math.Max(1, inlineWidthPt - indentDxa / 20.0), maxHeightPt: inlineHeightPt));
                                            prevBottomPt = sImg.BottomPt;
                                            imagesCount++;
                                        }

                                        double tblGapPt = !double.IsNaN(prevBottomPt) ? Math.Max(0.0, prevBottomPt - region.Top) : 0.0;
                                        if (useEnterGaps && !isCurveOcrPage && !diagramVectorPage && tblGapPt >= 13.5)
                                        {
                                            AppendEmptyEnterParagraphsForGap(body, ref tblGapPt, defaultPageFont);
                                        }
                                        if (tblGapPt > 1.0)
                                        {
                                            body.Append(new W.Paragraph(new W.ParagraphProperties(new W.SpacingBetweenLines
                                            { Before = "0", After = "0", Line = Math.Round(tblGapPt * 20).ToString(CultureInfo.InvariantCulture), LineRule = W.LineSpacingRuleValues.Exact })));
                                        }

                                        var wordTable = BuildOpenXmlTable(region, pageLeftMarginPt, usableWidthPt, page, mainPart, standaloneImages, pendingStandalone, floatingImages, ref docPrId, ref imagesCount, isCurveOcrPage);
                                        body.Append(wordTable);
                                        tablesCount++;
                                        prevBottomPt = region.Bottom;
                                        prevPageEndedWithList = false;
                                        continue;
                                    }

                                    // Single Unified Paragraph & Graphics Pipeline
                                    bool allowEnterGapsInRegion = useEnterGaps && !isCurveOcrPage && !diagramVectorPage && !bodyList && !prevPageEndedWithList;
                                    for (int i = 0; i < region.Lines.Count; i++)
                                    {
                                        var line = region.Lines[i];

                                        // Interleave any pending standalone image situated above this line
                                        while (pendingStandalone.Count > 0 && pendingStandalone[0].BottomPt >= line.Top - 2.0)
                                        {
                                            var sImg = pendingStandalone[0];
                                            pendingStandalone.RemoveAt(0);
                                            double imgGapPt = !double.IsNaN(prevBottomPt) ? Math.Max(0.0, prevBottomPt - sImg.TopPt) : 0.0;
                                            if (useEnterGaps)
                                            {
                                                AppendEmptyEnterParagraphsForGap(body, ref imgGapPt, defaultPageFont);
                                            }
                                            int indentDxa = (int)Math.Round(Math.Max(0, sImg.LeftPt - pageLeftMarginPt) * 20.0);
                                            body.Append(CreateInlineImageParagraph(mainPart, sImg.PngBytes, sImg.WidthPt, sImg.HeightPt, docPrId++, indentDxa, imgGapPt,
                                                maxWidthPt: Math.Max(1, inlineWidthPt - indentDxa / 20.0), maxHeightPt: inlineHeightPt));
                                            prevBottomPt = sImg.BottomPt;
                                            imagesCount++;
                                        }

                                        double gap = double.IsNaN(prevBottomPt) ? 0 : Math.Max(0, prevBottomPt - line.Top);
                                        bool hasTopDivider = fullPageBitmap != null
                                            && !double.IsNaN(prevBottomPt)
                                            && gap >= 2.0 && gap <= 65.0
                                            && HasHorizontalDividerBetween(fullPageBitmap, vectorRects, page.Width, page.Height, prevBottomPt, line.Top, pageLeftMarginPt, page.Width - pageRightLimitPt);

                                        // Optional OCR bitmap logo cropping when fullPageBitmap is present (deduplicated via croppedLogoRects)
                                        if (fullPageBitmap != null && options.EnableOcrLogoCropping && TryDetectAndCropGraphicLogo(
                                                fullPageBitmap, page.Width, page.Height, line,
                                                out byte[]? logoPng, out double logoWidthPt, out double logoHeightPt, out double logoLeftPt,
                                                out List<WordSegment> remainingRightSegments, croppedLogoRects, lines))
                                        {
                                            double logoGapPt = gap;
                                            if (allowEnterGapsInRegion && !hasTopDivider && logoGapPt >= 13.5)
                                            {
                                                AppendEmptyEnterParagraphsForGap(body, ref logoGapPt, defaultPageFont, line.FontSizePt);
                                            }
                                            if (remainingRightSegments.Count > 0)
                                            {
                                                body.Append(CreateLogoWithRemainingSegmentsParagraph(
                                                    mainPart, logoPng!, logoWidthPt, logoHeightPt, remainingRightSegments,
                                                    pageLeftMarginPt, pageRightLimitPt, usableWidthPt, logoGapPt, hasTopDivider,
                                                    docPrId++, inlineWidthPt, inlineHeightPt));
                                            }
                                            else
                                            {
                                                int indentDxa = (int)Math.Round(Math.Max(0, logoLeftPt - pageLeftMarginPt) * 20.0);
                                                body.Append(CreateInlineImageParagraph(
                                                    mainPart, logoPng!, logoWidthPt, logoHeightPt, docPrId++, indentDxa, logoGapPt,
                                                    hasTopDivider, Math.Max(1, inlineWidthPt - indentDxa / 20.0), inlineHeightPt));
                                            }
                                            imagesCount++;
                                            prevBottomPt = line.Bottom;
                                            prevPageEndedWithList = false;
                                            continue;
                                        }

                                        int pageLineIdx = lines.IndexOf(line);
                                        bool listMarker = pageLineIdx >= 0
                                            ? IsLinePartOfBodyList(lines, pageLineIdx, prevPageEndedWithList)
                                            : IsLinePartOfBodyList(region.Lines, i, prevPageEndedWithList);

                                        if (allowEnterGapsInRegion && !hasTopDivider && !listMarker && gap >= 13.5)
                                        {
                                            AppendEmptyEnterParagraphsForGap(body, ref gap, defaultPageFont, line.FontSizePt);
                                        }

                                        // 1. Native Microsoft Word Equation (<m:oMathPara><m:oMath>) for fraction-bar & display formulas
                                        var mathPara = TryBuildMathEquationParagraph(region.Lines, i, page, consumedCurveGlyphPaths, pageLeftMarginPt, page.Width - pageRightMarginPt, gap, out int mathLineCount, out double mathBottomPt);
                                        if (mathPara != null)
                                        {
                                            body.Append(mathPara);
                                            prevBottomPt = mathBottomPt;
                                            i += mathLineCount - 1;
                                            prevPageEndedWithList = false;
                                            continue;
                                        }

                                        // 2. Table of Contents Dot-Leader Paragraph (e.g., "DAFTAR ISI ...... i")
                                        var tocPara = TryBuildTocDotLeaderParagraph(line, region.Lines, i, pageLeftMarginPt, page.Width - pageRightMarginPt, gap, out double tocNextBottomPt);
                                        if (tocPara != null)
                                        {
                                            body.Append(tocPara);
                                            prevBottomPt = tocNextBottomPt;
                                            prevPageEndedWithList = false;
                                            continue;
                                        }

                                        // 3. Multi-Line Justified Paragraph (with FirstLine / Hanging / Left / Right indent)
                                        var justified = TryBuildJustifiedParagraph(region.Lines, i, pageLeftMarginPt, page.Width - pageRightMarginPt,
                                            gap, out int lineCount, out double leading, allowEnterGapsInRegion);
                                        if (justified != null)
                                        {
                                            body.Append(justified);
                                            var lastJustifiedLine = region.Lines[i + lineCount - 1];
                                            prevBottomPt = allowEnterGapsInRegion ? lastJustifiedLine.Bottom : (line.Top - lineCount * leading);
                                            i += lineCount - 1;
                                            prevPageEndedWithList = false;
                                            continue;
                                        }

                                        var mergedWords = new List<RichWord>(line.Words);
                                        int lastMergedLineIdx = i;
                                        if (listMarker)
                                        {
                                            double itemTextLeft = line.Words[1].Left;
                                            double curLineTop = line.Top;
                                            double curLineBottom = line.Bottom;
                                            while (lastMergedLineIdx + 1 < region.Lines.Count)
                                            {
                                                var nextCandidate = region.Lines[lastMergedLineIdx + 1];
                                                if (!IsWrappedListContinuationVisualLine(line, nextCandidate, curLineTop, curLineBottom, itemTextLeft))
                                                    break;
                                                mergedWords.AddRange(nextCandidate.Words);
                                                curLineTop = nextCandidate.Top;
                                                curLineBottom = nextCandidate.Bottom;
                                                lastMergedLineIdx++;
                                            }
                                        }
                                        else if (line.Segments.Count <= 1)
                                        {
                                            // 4. Multi-Line Flowing Paragraph Merging (Left-aligned, Indented, Sub-Bullets, Wrapped Headings)
                                            TryMergeFlowingParagraphLines(region.Lines, i, pageLeftMarginPt, page.Width - pageRightMarginPt, mergedWords, ref lastMergedLineIdx);
                                        }
                                        else
                                        {
                                            // 5. Hanging Labeled Paragraph Merging (e.g. "Menimbang : a. bahwa...", "Mengingat : 1. ...", "KESATU : Menetapkan...")
                                            TryMergeHangingLabeledParagraphLines(region.Lines, i, pageLeftMarginPt, page.Width - pageRightMarginPt, ref line, mergedWords, ref lastMergedLineIdx);
                                        }

                                        bool hasWrappedContinuation = lastMergedLineIdx > i;
                                        bool isHangingLabeledMultiLine = hasWrappedContinuation && line.Segments.Count > 1 && !listMarker;
                                        var lastItemLine = region.Lines[lastMergedLineIdx];
                                        double height = hasWrappedContinuation && lastMergedLineIdx > i
                                            ? Math.Clamp((line.Top - lastItemLine.Top) / (lastMergedLineIdx - i), line.FontSizePt * 1.05, line.FontSizePt * 1.65)
                                            : line.FontSizePt * 1.1;
                                        if (!hasWrappedContinuation && lastMergedLineIdx + 1 < region.Lines.Count)
                                        {
                                            var nextRegionLine = region.Lines[lastMergedLineIdx + 1];
                                            double deltaY = lastItemLine.Top - nextRegionLine.Top;
                                            bool horizontallyDisjoint = lastItemLine.Right < nextRegionLine.Left - 12.0
                                                || nextRegionLine.Right < lastItemLine.Left - 12.0;
                                            if (horizontallyDisjoint && deltaY >= 1.0 && deltaY < height)
                                            {
                                                height = deltaY;
                                            }
                                        }
                                        double after = lastMergedLineIdx + 1 < region.Lines.Count
                                            ? Math.Max(0, lastItemLine.Top - region.Lines[lastMergedLineIdx + 1].Top - height)
                                            : 0;
                                        bool convertAfterToEnterGap = allowEnterGapsInRegion && !listMarker && after >= 13.5;
                                        double emittedAfter = convertAfterToEnterGap ? 0.0 : after;

                                        bool literalMarkerTab = !listMarker && line.Segments.Count <= 1 && HasListMarkerToken(line, out _)
                                            && (line.Words[1].Left - line.Words[0].Right) >= 4.0;
                                        double rightLimit = page.Width - pageRightMarginPt;
                                        var paragraph = line.Segments.Count > 1 && !listMarker
                                            ? BuildGeometricTabStopParagraph(line, page.Width, pageLeftMarginPt, rightLimit, rightLimit - pageLeftMarginPt, gap, hasTopDivider, canonicalTabPositionsPt, region.Lines)
                                            : new W.Paragraph(new W.ParagraphProperties());
                                        if (hasTopDivider && paragraph.ParagraphProperties!.ParagraphBorders == null)
                                        {
                                            paragraph.ParagraphProperties.ParagraphBorders = new W.ParagraphBorders(
                                                new W.TopBorder { Val = W.BorderValues.Single, Size = 6, Space = 4, Color = "D0D0D0" });
                                        }
                                        paragraph.ParagraphProperties!.SpacingBetweenLines = new W.SpacingBetweenLines
                                        {
                                            Before = Math.Round(gap * 20).ToString(CultureInfo.InvariantCulture),
                                            After = Math.Round(emittedAfter * 20).ToString(CultureInfo.InvariantCulture),
                                            Line = Math.Round(Math.Max(height < line.FontSizePt ? 2.0 : line.FontSizePt, height) * 20).ToString(CultureInfo.InvariantCulture),
                                            LineRule = W.LineSpacingRuleValues.Exact
                                        };

                                        // Compute accurate Left / FirstLine / Hanging indentation for multi-line paragraphs
                                        double bodyIndentLeftPt = hasWrappedContinuation && !listMarker
                                            ? Math.Min(line.Left, region.Lines[i + 1].Left)
                                            : line.Left;
                                        if (!hasWrappedContinuation && !listMarker && canonicalTabPositionsPt.Count > 0)
                                        {
                                            foreach (double tabX in canonicalTabPositionsPt)
                                            {
                                                if (Math.Abs(bodyIndentLeftPt - tabX) <= 3.5)
                                                {
                                                    bodyIndentLeftPt = tabX;
                                                    break;
                                                }
                                            }
                                        }
                                        var indentObj = new W.Indentation
                                        {
                                            Left = Math.Round(Math.Max(0, bodyIndentLeftPt - pageLeftMarginPt) * 20).ToString(CultureInfo.InvariantCulture)
                                        };
                                        if (isHangingLabeledMultiLine)
                                        {
                                            double contLeftPt = region.Lines[i + 1].Left;
                                            double hangPt = Math.Max(0, contLeftPt - line.Left);
                                            int contLeftDxa = (int)Math.Round(Math.Max(0, contLeftPt - pageLeftMarginPt) * 20);
                                            indentObj.Left = contLeftDxa.ToString(CultureInfo.InvariantCulture);
                                            if (hangPt >= 6.0)
                                            {
                                                indentObj.Hanging = Math.Round(hangPt * 20).ToString(CultureInfo.InvariantCulture);
                                            }
                                            var lastTab = paragraph.ParagraphProperties.Tabs?.Elements<W.TabStop>().LastOrDefault();
                                            if (lastTab != null)
                                            {
                                                lastTab.Val = W.TabStopValues.Left;
                                                lastTab.Position = contLeftDxa;
                                            }
                                            paragraph.ParagraphProperties.Justification = new W.Justification { Val = W.JustificationValues.Both };
                                        }
                                        else if (hasWrappedContinuation && !listMarker)
                                        {
                                            double firstVsSecondPt = line.Left - region.Lines[i + 1].Left;
                                            if (firstVsSecondPt >= 8.0 && firstVsSecondPt <= 56.0)
                                            {
                                                indentObj.FirstLine = Math.Round(firstVsSecondPt * 20).ToString(CultureInfo.InvariantCulture);
                                            }
                                            else if (firstVsSecondPt <= -8.0 && firstVsSecondPt >= -220.0)
                                            {
                                                indentObj.Left = Math.Round(Math.Max(0, region.Lines[i + 1].Left - pageLeftMarginPt) * 20).ToString(CultureInfo.InvariantCulture);
                                                indentObj.Hanging = Math.Round((-firstVsSecondPt) * 20).ToString(CultureInfo.InvariantCulture);
                                            }
                                        }
                                        paragraph.ParagraphProperties.Indentation = indentObj;

                                        if (literalMarkerTab)
                                        {
                                            int tabPosDxa = hasWrappedContinuation
                                                ? (int)Math.Round(Math.Max(0, region.Lines[i + 1].Left - pageLeftMarginPt) * 20)
                                                : (int)Math.Round(Math.Max(0, line.Words[1].Left - pageLeftMarginPt) * 20);
                                            paragraph.ParagraphProperties.Tabs = new W.Tabs(new W.TabStop
                                            {
                                                Val = W.TabStopValues.Left,
                                                Position = tabPosDxa
                                            });
                                            if (hasWrappedContinuation)
                                            {
                                                paragraph.ParagraphProperties.Justification = new W.Justification { Val = W.JustificationValues.Both };
                                            }
                                            AppendConsolidatedRuns(paragraph, new[] { mergedWords[0] }, 0);
                                            string halfPt = ((int)Math.Round(Math.Clamp(line.FontSizePt, 6.0, 72.0) * 2.0)).ToString(CultureInfo.InvariantCulture);
                                            paragraph.Append(new W.Run(new W.RunProperties(new W.FontSize { Val = halfPt }, new W.FontSizeComplexScript { Val = halfPt }), new W.TabChar()));
                                            AppendConsolidatedRuns(paragraph, mergedWords.Skip(1).ToList(), 0);
                                        }
                                        else if (line.Segments.Count <= 1 || listMarker)
                                        {
                                            AppendConsolidatedRuns(paragraph, mergedWords, 0);
                                        }

                                        if (listMarker)
                                        {
                                            paragraph.ParagraphProperties.Tabs = new W.Tabs(new W.TabStop
                                            {
                                                Val = W.TabStopValues.Left,
                                                Position = (int)Math.Round(Math.Max(0, line.Words[1].Left - pageLeftMarginPt) * 20)
                                            });
                                            if (hasWrappedContinuation && line.Right >= pageLeftMarginPt + (rightLimit - pageLeftMarginPt) * 0.74)
                                            {
                                                paragraph.ParagraphProperties.Justification = new W.Justification { Val = W.JustificationValues.Both };
                                            }
                                        }
                                        else if (line.Segments.Count <= 1 && !literalMarkerTab)
                                        {
                                            double tolerance = Math.Max(1.5, line.FontSizePt / 5);
                                            double centerTol = Math.Max(tolerance, 18.0);
                                            double lineMid = (line.Left + line.Right) * 0.5;
                                            double marginMid = (pageLeftMarginPt + rightLimit) * 0.5;
                                            double pageMid = page.Width * 0.5;
                                            var alignment = W.JustificationValues.Left;
                                            if (hasWrappedContinuation && (line.Right >= rightLimit - 28.0 || (line.Right - bodyIndentLeftPt) >= (rightLimit - bodyIndentLeftPt) * 0.74))
                                            {
                                                alignment = W.JustificationValues.Both;
                                            }
                                            else if (!hasWrappedContinuation
                                                && (Math.Abs(lineMid - marginMid) <= tolerance
                                                    || (line.Left > pageLeftMarginPt + 18.0 && (Math.Abs(lineMid - marginMid) <= centerTol || Math.Abs(lineMid - pageMid) <= centerTol)))
                                                && line.Right - line.Left < (rightLimit - pageLeftMarginPt) * .86)
                                            {
                                                alignment = W.JustificationValues.Center;
                                                if (fullPageBitmap != null)
                                                {
                                                    paragraph.ParagraphProperties.Indentation = new W.Indentation { Left = "0", Right = "0" };
                                                }
                                                else
                                                {
                                                    double axisOffset = line.Left + line.Right - pageLeftMarginPt - rightLimit;
                                                    paragraph.ParagraphProperties.Indentation = new W.Indentation {
                                                        Left = Math.Round(Math.Max(0, axisOffset) * 20).ToString(CultureInfo.InvariantCulture),
                                                        Right = Math.Round(Math.Max(0, -axisOffset) * 20).ToString(CultureInfo.InvariantCulture) };
                                                }
                                            }
                                            else if (!hasWrappedContinuation
                                                && Math.Abs(line.Right - rightLimit) <= Math.Max(tolerance, 6.0)
                                                && line.Left > pageLeftMarginPt + (rightLimit - pageLeftMarginPt) * 0.45)
                                            {
                                                alignment = W.JustificationValues.Right;
                                                paragraph.ParagraphProperties.Indentation = new W.Indentation { Left = "0", Right = Math.Round(Math.Max(0, rightLimit - line.Right) * 20).ToString(CultureInfo.InvariantCulture) };
                                            }
                                            paragraph.ParagraphProperties.Justification = new W.Justification { Val = alignment };
                                        }
                                        if (!hasWrappedContinuation && !isCurveOcrPage && fullPageBitmap == null && hasVisibleText
                                            && paragraph.Elements<W.Run>().Count() == 1)
                                        {
                                            int available = (int)(Math.Round(page.Width * 20) - Math.Round(pageLeftMarginPt * 20)
                                                - Math.Round(pageRightMarginPt * 20) - Math.Round((line.Left - pageLeftMarginPt) * 20));
                                            int sourceWidth = (int)Math.Round((line.Right - line.Left) * 20);
                                            if (available > 0 && sourceWidth > 0 && sourceWidth >= available - 1)
                                                paragraph.GetFirstChild<W.Run>()!.RunProperties!.Append(new W.FitText
                                                { Val = (uint)Math.Min(sourceWidth, available), Id = (int)docPrId++ });
                                        }
                                        body.Append(paragraph);
                                        prevBottomPt = convertAfterToEnterGap
                                            ? (lastItemLine.Top - height)
                                            : (lastItemLine.Top - height - after);
                                        i = lastMergedLineIdx;
                                        prevPageEndedWithList = listMarker;
                                    }
                                }

                                // Jika masih ada gambar mandiri di bagian paling bawah setelah seluruh teks selesai
                                while (pendingStandalone.Count > 0)
                                {
                                    var sImg = pendingStandalone[0];
                                    pendingStandalone.RemoveAt(0);
                                    double imgGapPt = !double.IsNaN(prevBottomPt) ? Math.Max(0.0, prevBottomPt - sImg.TopPt) : 0.0;
                                    if (useEnterGaps)
                                    {
                                        AppendEmptyEnterParagraphsForGap(body, ref imgGapPt, defaultPageFont);
                                    }

                                    int indentDxa = (int)Math.Round(Math.Max(0, sImg.LeftPt - pageLeftMarginPt) * 20.0);
                                    body.Append(CreateInlineImageParagraph(mainPart, sImg.PngBytes, sImg.WidthPt, sImg.HeightPt, docPrId++, indentDxa, imgGapPt,
                                        maxWidthPt: Math.Max(1, inlineWidthPt - indentDxa / 20.0), maxHeightPt: inlineHeightPt));
                                    prevBottomPt = sImg.BottomPt;
                                    imagesCount++;
                                }
                                // Page rules are independent of paragraph grouping; table borders are already emitted by the table.
                                var independentDividers = dividerPaths.Where(item => positionedPage && !regions.Any(region => region.RuledRows.Count > 0
                                    && region.GridColumns.Count >= 2
                                    && item.Bounds!.Value.Left >= region.GridColumns[0] - 2 && item.Bounds.Value.Right <= region.GridColumns[^1] + 2
                                    && item.Bounds.Value.Bottom >= region.Bottom - 2 && item.Bounds.Value.Top <= region.Top + 2)).ToList();
                                // Filled round markers can be PDF paths rather than text glyphs.
                                var vectorBullets = page.ExperimentalAccess.Paths.Where(path => positionedPage && path.IsFilled)
                                    .SelectMany(path => path.Where(sub => sub.Commands.OfType<PdfSubpath.CubicBezierCurve>().Any() && !sub.Commands.OfType<PdfSubpath.Line>().Any())
                                        .Select(sub => (Bounds: sub.GetBoundingRectangle(), Color: path.FillColor, Curves: sub.Commands.OfType<PdfSubpath.CubicBezierCurve>().ToList())))
                                    .Where(item => item.Bounds.HasValue && lines.Any(line =>
                                    {
                                        var b = item.Bounds.Value;
                                        bool OnCircle(PdfPoint point) => Math.Abs(Math.Sqrt(Math.Pow(point.X - (b.Left + b.Right) / 2, 2)
                                            + Math.Pow(point.Y - (b.Top + b.Bottom) / 2, 2)) - b.Width / 2) <= .15;
                                        return b.Width >= 1.5 && b.Width <= line.FontSizePt * .6 && Math.Abs(b.Width - b.Height) <= .25
                                            && b.Right <= line.Left && b.Left >= line.Left - line.FontSizePt * 2
                                            && b.Top >= line.Bottom && b.Bottom <= line.Top
                                            && item.Curves.All(curve => OnCircle(curve.StartPoint) && OnCircle(curve.EndPoint)
                                                && OnCircle(new PdfPoint((curve.StartPoint.X + 3 * curve.FirstControlPoint.X + 3 * curve.SecondControlPoint.X + curve.EndPoint.X) / 8,
                                                    (curve.StartPoint.Y + 3 * curve.FirstControlPoint.Y + 3 * curve.SecondControlPoint.Y + curve.EndPoint.Y) / 8)));
                                    })).ToList();
                                independentDividers.RemoveAll(d => consumedCurveGlyphPaths.Contains(d.Path));
                                if (isCurveOcrPage || diagramVectorPage)
                                {
                                    EnsurePageSectionFitsSinglePage(body, pageBodyStart, page.Width, page.Height, pageLeftMarginPt, pageRightMarginPt, pageTopMarginPt, pageBottomMarginPt);
                                }
                                var vectorArtwork = clippedPagePaths.Where(item =>
                                {
                                    var path = item.Path;
                                    var bounds = item.EffectiveBounds;
                                    if (IsPureWhiteCanvasBackdrop(path, bounds, page.Width, page.Height)) return false;
                                    if (vectorArtworkPage && (path.IsFilled || path.IsStroked))
                                        return true;
                                    if (!positionedPage || independentDividers.Any(d => ReferenceEquals(d.Path, path))) return false;
                                    if (standaloneImages.Any(img => bounds.Left < img.RightPt && bounds.Right > img.LeftPt
                                        && bounds.Bottom < img.TopPt && bounds.Top > img.BottomPt)) return false;
                                    if (regions.Any(r => r.RuledRows.Count > 0
                                        && ((bounds.Left >= r.GridColumns[0] - 6 && bounds.Right <= r.GridColumns[^1] + 6
                                             && bounds.Bottom >= r.Bottom - 6 && bounds.Top <= r.Top + 6)
                                            || ComputeOverlapRatio(bounds, new PdfRectangle(r.GridColumns[0], r.Bottom, r.GridColumns[^1], r.Top)) >= 0.45))) return false;

                                    if (diagramVectorPage)
                                    {
                                        if (path.IsStroked && !path.IsFilled)
                                            return Math.Max(bounds.Width, bounds.Height) > 2.0;
                                        if (path.IsFilled)
                                            return bounds.Width > 1.2 && bounds.Height > 1.2 && Math.Max(bounds.Width, bounds.Height) > 2.5;
                                        return false;
                                    }

                                    if (bounds.Width <= 0.5 || bounds.Height <= 0.5) return false;
                                    if (!path.IsFilled) return false;
                                    if (!path.All(sub => sub.Commands.All(c => c is PdfSubpath.Move or PdfSubpath.Line or PdfSubpath.Close)
                                        && sub.GetBoundingRectangle() is PdfRectangle subBounds && subBounds.Width > 2 && subBounds.Height > 2)) return false;
                                    return true;
                                }).ToList();
                                if (floatingImages.Count > 0 || independentDividers.Count > 0 || vectorBullets.Count > 0 || vectorArtwork.Count > 0 || wordArt.Count > 0 || verticalWordShapes.Count > 0)
                                {
                                    var anchorParagraph = body.ChildElements.Skip(pageBodyStart).OfType<W.Paragraph>().FirstOrDefault()
                                        ?? body.ChildElements.Skip(pageBodyStart).SelectMany(element => element.Descendants<W.Paragraph>()).FirstOrDefault();
                                    if (anchorParagraph == null)
                                    {
                                        anchorParagraph = new W.Paragraph(new W.ParagraphProperties(
                                            new W.SpacingBetweenLines { Before = "0", After = "0", Line = "20", LineRule = W.LineSpacingRuleValues.Exact }));
                                        body.InsertAt(anchorParagraph, pageBodyStart);
                                    }
                                    foreach (var art in wordArt)
                                        anchorParagraph.Append(new W.Run(new W.Picture(BuildEditableWordArt(art, page.Height, docPrId++))));
                                    foreach (var vText in verticalWordShapes)
                                        anchorParagraph.Append(new W.Run(new W.Picture(BuildVerticalRotatedWordArtShape(vText, page.Height, docPrId++))));
                                    if (wordArt.Count > 0) warnings.Add($"Halaman {pageNo}: {wordArt.Count} WordArt dipulihkan dari tag PDF; teks dapat diedit, tetapi font dan efek asli tidak tersimpan dalam PDF. Mode VisualFidelity mempertahankan kurva asli.");
                                    if (vectorArtwork.Count > 0)
                                    {
                                        for (int vIdx = 0; vIdx < vectorArtwork.Count; vIdx++)
                                        {
                                            var item = vectorArtwork[vIdx];
                                            UglyToad.PdfPig.Graphics.PdfPath? pairedStroke = null;
                                            if (diagramVectorPage && item.Path.IsFilled && !item.Path.IsStroked && vIdx + 1 < vectorArtwork.Count)
                                            {
                                                var nextItem = vectorArtwork[vIdx + 1];
                                                if (!nextItem.Path.IsFilled && nextItem.Path.IsStroked && AreSameBoxBounds(item.EffectiveBounds, nextItem.EffectiveBounds, 0.8))
                                                {
                                                    pairedStroke = nextItem.Path;
                                                    vIdx++;
                                                }
                                            }
                                            shapeHorizontalLines.TryGetValue(item.Path, out var boxLines);
                                            shapeVerticalTexts.TryGetValue(item.Path, out var boxVertTexts);
                                            var vShape = BuildWordVectorShape(item.Path, page.Width, page.Height, docPrId++, item.ClipRect, pagePatterns, pairedStroke, vectorArtworkPage, boxLines, boxVertTexts);
                                            if (vShape != null)
                                                anchorParagraph.Append(new W.Run(new W.Picture(vShape)));
                                        }
                                        if (vectorArtworkPage) warnings.Add($"Halaman {pageNo}: tulisan berbentuk kurva dipertahankan sebagai objek vektor, bukan teks yang dapat diketik ulang.");
                                    }
                                    foreach (var marker in vectorBullets)
                                    {
                                        var bounds = marker.Bounds!.Value;
                                        var (red, green, blue) = SafeToRgbValues(marker.Color);
                                        var shape = new DocumentFormat.OpenXml.Vml.Oval(
                                            new DocumentFormat.OpenXml.Vml.Wordprocessing.TextWrap { Type = DocumentFormat.OpenXml.Vml.Wordprocessing.WrapValues.None })
                                        {
                                            Id = $"Bullet_{docPrId++}", FillColor = $"#{(int)Math.Round(red * 255):X2}{(int)Math.Round(green * 255):X2}{(int)Math.Round(blue * 255):X2}", Stroked = false,
                                            Style = FormattableString.Invariant($"position:absolute;margin-left:{bounds.Left:0.###}pt;margin-top:{page.Height - bounds.Top:0.###}pt;width:{bounds.Width:0.###}pt;height:{bounds.Height:0.###}pt;z-index:-1;mso-position-horizontal-relative:page;mso-position-vertical-relative:page")
                                        };
                                        anchorParagraph.Append(new W.Run(new W.Picture(shape)));
                                    }
                                    foreach (var divider in independentDividers)
                                    {
                                        var bounds = divider.Bounds!.Value;
                                        double thickness = bounds.Height > 0 ? bounds.Height : divider.Path.LineWidth;
                                        if (thickness <= 0) continue;
                                        double top = bounds.Height > 0 ? bounds.Top : bounds.Top + thickness / 2;
                                        var color = divider.Path.IsFilled ? divider.Path.FillColor : divider.Path.StrokeColor;
                                        var (red, green, blue) = SafeToRgbValues(color);
                                        string hex = $"#{(int)Math.Round(red * 255):X2}{(int)Math.Round(green * 255):X2}{(int)Math.Round(blue * 255):X2}";
                                        var shape = new DocumentFormat.OpenXml.Vml.Rectangle(
                                            new DocumentFormat.OpenXml.Vml.Wordprocessing.TextWrap { Type = DocumentFormat.OpenXml.Vml.Wordprocessing.WrapValues.None })
                                        {
                                            Id = $"Divider_{docPrId++}", FillColor = hex, Stroked = false,
                                            Style = FormattableString.Invariant($"position:absolute;margin-left:{bounds.Left:0.###}pt;margin-top:{page.Height - top:0.###}pt;width:{bounds.Width:0.###}pt;height:{thickness:0.###}pt;z-index:-1;mso-position-horizontal-relative:page;mso-position-vertical-relative:page")
                                        };
                                        anchorParagraph.Append(new W.Run(new W.Picture(shape)));
                                    }
                                    foreach (var img in floatingImages)
                                    {
                                        var drawing = BuildInlineDrawingElement(mainPart, img.PngBytes, img.WidthPt, img.HeightPt, docPrId++);
                                        var inline = drawing.GetFirstChild<DW.Inline>()!;
                                        // A page-anchored image keeps PDF dimensions; inline width limits do not apply.
                                        long width = (long)Math.Round(img.WidthPt * 12700), height = (long)Math.Round(img.HeightPt * 12700);
                                        inline.Extent!.Cx = width; inline.Extent.Cy = height;
                                        foreach (var extent in inline.Graphic!.Descendants<A.Extents>()) { extent.Cx = width; extent.Cy = height; }
                                        var anchor = new DW.Anchor(
                                            new DW.SimplePosition { X = 0, Y = 0 },
                                            new DW.HorizontalPosition(new DW.PositionOffset(Math.Round(img.LeftPt * 12700).ToString(CultureInfo.InvariantCulture))) { RelativeFrom = DW.HorizontalRelativePositionValues.Page },
                                            new DW.VerticalPosition(new DW.PositionOffset(Math.Round((page.Height - img.TopPt) * 12700).ToString(CultureInfo.InvariantCulture))) { RelativeFrom = DW.VerticalRelativePositionValues.Page },
                                            inline.Extent!.CloneNode(true), inline.EffectExtent!.CloneNode(true), new DW.WrapNone(),
                                            inline.DocProperties!.CloneNode(true), inline.NonVisualGraphicFrameDrawingProperties!.CloneNode(true), inline.Graphic!.CloneNode(true))
                                        { SimplePos = false, RelativeHeight = img.ForceInFrontOfText ? 25165824U : 0U,
                                            // Keep non-overlapping images and transparent wet stamps in front of text; leave opaque overlapping backdrops behind text.
                                            BehindDoc = !img.ForceInFrontOfText && lines.SelectMany(line => line.Words).Any(word => word.Left < img.RightPt && word.Right > img.LeftPt
                                                && word.Bottom < img.TopPt && word.Top > img.BottomPt),
                                            Locked = false, LayoutInCell = false, AllowOverlap = true,
                                            DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U };
                                        drawing.RemoveAllChildren(); drawing.Append(anchor);
                                        anchorParagraph.Append(new W.Run(drawing));
                                        imagesCount++;
                                    }
                                }
                            }
                            finally
                            {
                                fullPageBitmap?.Dispose();
                            }

                            var sectPr = CreateSectionProperties(page.Width, page.Height, pageNo < totalPages,
                                pageLeftMarginPt, pageRightMarginPt, pageTopMarginPt, pageBottomMarginPt);
                            if (pageNo < totalPages)
                            {
                                if (body.LastChild is W.Paragraph lastParagraph)
                                    (lastParagraph.ParagraphProperties ??= new W.ParagraphProperties()).SectionProperties = sectPr;
                                else
                                    body.Append(new W.Paragraph(new W.ParagraphProperties(
                                        new W.SpacingBetweenLines { Before = "0", After = "0", Line = "1", LineRule = W.LineSpacingRuleValues.Exact }, sectPr)));
                            }
                            else
                                body.Append(sectPr);

                            progress?.Report((double)pageNo / totalPages * 100.0);
                        }

                        if (dominantDocumentOcrSerifFont == "Bookman Old Style")
                        {
                            foreach (var rf in body.Descendants<W.RunFonts>())
                            {
                                if (rf.Ascii?.Value == "Times New Roman" || rf.Ascii?.Value == "Georgia")
                                {
                                    rf.Ascii = "Bookman Old Style";
                                    rf.HighAnsi = "Bookman Old Style";
                                    rf.ComplexScript = "Bookman Old Style";
                                }
                            }
                        }

                        ApplyEditableNumbering(mainPart);
                        if (options.EmitFontFallbackTable)
                        {
                            EnsureWordFontTable(mainPart);
                        }
                        EnsureModernWordSettings(mainPart);
                        mainPart.Document.Save();
                    }

                    if (options.ValidateOpenXmlOutput)
                    {
                        using var verifyDoc = WordprocessingDocument.Open(tempPath, false);
                        var validator = new OpenXmlValidator();
                        var schemaErrors = validator.Validate(verifyDoc).ToList();
                        if (schemaErrors.Count > 0)
                        {
                            throw new InvalidDataException($"Validasi skema DOCX gagal dengan {schemaErrors.Count} error: {schemaErrors[0].Description}");
                        }
                    }

                    CommitStagingFile(tempPath, outputDocxPath);
                }
                catch
                {
                    CleanupStagingFile(tempPath);
                    throw;
                }

                sw.Stop();
                var fi = new FileInfo(outputDocxPath);
                return new ConversionExecutionResult
                {
                    IsSuccess = fi.Exists && fi.Length > 0,
                    OutputPath = outputDocxPath,
                    PagesProcessed = totalPages,
                    TablesReconstructed = tablesCount,
                    ImagesProcessed = imagesCount,
                    OutputSizeBytes = fi.Exists ? fi.Length : 0,
                    Elapsed = sw.Elapsed,
                    CreatedFiles = new[] { outputDocxPath },
                    Warnings = warnings
                };
            }, ct);
        }

        // =====================================================================
        // 2. PDF -> EXCEL (.XLSX)
        // =====================================================================
        private static double ExcelColumnWidthForPixels(int normalPixels, double screenMdw)
        {
            if (normalPixels < 1 || !double.IsFinite(screenMdw) || screenMdw < 1)
                throw new ArgumentOutOfRangeException(nameof(normalPixels), "Excel columns require positive pixel widths and font metrics.");
            double rawWidth = Math.Ceiling(normalPixels * 256.0 / screenMdw - Math.Floor(128 / screenMdw)) / 256;
            // ClosedXML 0.104.2 saves six decimals; round upward to retain the ECMA pixel boundary.
            rawWidth = Math.Ceiling(rawWidth * 1_000_000) / 1_000_000;
            if (rawWidth > 255)
                throw new ArgumentOutOfRangeException(nameof(normalPixels), "Column exceeds Excel's serialized width limit.");
            // ClosedXML 0.104.2 WorksheetPartWriter adds ColumnWidthOffset, even for sub-character widths.
            return rawWidth - 0.710625;
        }

        private static double[] QuantizeExcelRowHeights(IReadOnlyList<double> heights, double standardHeight,
            double cellDots, int printDpi)
        {
            if (!double.IsFinite(standardHeight) || standardHeight <= 0 || !double.IsFinite(cellDots) || cellDots <= 0 ||
                printDpi <= 0 || heights.Any(h => !double.IsFinite(h) || h <= 0))
                throw new ArgumentException("Invalid Excel row geometry or printer metrics.");
            double RowDots(int pixels) => Math.Floor(pixels * .75 / standardHeight * cellDots);
            var result = new double[heights.Count];
            double targetPt = 0, actualDots = 0;
            for (int row = 0; row < heights.Count; row++)
            {
                targetPt += heights[row];
                double remaining = targetPt * printDpi / 72 - actualDots;
                double idealPixels = remaining * standardHeight / (.75 * cellDots);
                if (idealPixels > 546.5)
                    throw new ArgumentException("A printer-adjusted Excel row exceeds the 409.5pt row-height limit.");
                int lower = Math.Clamp((int)Math.Floor(Math.Max(1, idealPixels)), 1, 546);
                int upper = Math.Min(546, lower + 1);
                int pixels = Math.Abs(RowDots(lower) - remaining) <= Math.Abs(RowDots(upper) - remaining) ? lower : upper;
                result[row] = pixels * .75;
                actualDots += RowDots(pixels); // Keep minimum-row debt in the cumulative boundary.
            }
            return result;
        }

        private static (double Dots, bool Native) GetExcelRowCellDots(IXLFont font, int printDpi)
        {
            // shortcut: printer cell padding verified at600DPI on Microsoft Print to PDF; other drivers need validation.
            if (OperatingSystem.IsWindows() && ExcelGdi.TryGetHeight(font, printDpi, out int height))
                return (height + Math.Ceiling(printDpi / 96.0) + 1, true);
            return (GetSkiaExcelRowCellDots(font, printDpi), false);
        }

        private static double GetSkiaExcelRowCellDots(IXLFont font, int printDpi)
        {
            string resolvedFamily = CleanFontName(font.FontName);
            using var face = SKTypeface.FromFamilyName(resolvedFamily,
                new SKFontStyle(font.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                    SKFontStyleWidth.Normal, font.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright));
            int emHeight = (int)Math.Round(font.FontSize * printDpi / 72.0, MidpointRounding.AwayFromZero);
            if (face != null && TryGetTrueTypeWinMetricsHeight(face, emHeight, out int winHeight))
            {
                return Math.Max(1, winHeight) + Math.Ceiling(printDpi / 96.0) + 1;
            }
            using var skFont = new SKFont(face, emHeight);
            var m = skFont.Metrics;
            return Math.Max(1, Math.Round(-m.Ascent) + Math.Round(m.Descent) + Math.Round(Math.Max(0, m.Leading)))
                + Math.Ceiling(printDpi / 96.0) + 1;
        }

        private static bool TryGetTrueTypeWinMetricsHeight(SKTypeface face, int ppem, out int heightDots)
        {
            heightDots = 0;
            try
            {
                int upem = face.UnitsPerEm;
                if (upem <= 0 || ppem <= 0) return false;
                uint os2Tag = ('O' << 24) | ('S' << 16) | ('/' << 8) | '2';
                uint hheaTag = ('h' << 24) | ('h' << 16) | ('e' << 8) | 'a';
                byte[]? os2 = face.GetTableData(os2Tag);
                if (os2 != null && os2.Length >= 78)
                {
                    ushort winAscent = (ushort)((os2[74] << 8) | os2[75]);
                    ushort winDescent = (ushort)((os2[76] << 8) | os2[77]);
                    short typoLineGap = (short)((os2[72] << 8) | os2[73]);
                    if (winAscent > 0 || winDescent > 0)
                    {
                        int ascDots = (int)Math.Round((double)winAscent * ppem / upem, MidpointRounding.AwayFromZero);
                        int descDots = (int)Math.Round((double)winDescent * ppem / upem, MidpointRounding.AwayFromZero);
                        int extLeading = typoLineGap > 0 ? (int)Math.Round((double)typoLineGap * ppem / upem, MidpointRounding.AwayFromZero) : 0;
                        heightDots = ascDots + descDots + extLeading;
                        return heightDots > 0;
                    }
                }
                byte[]? hhea = face.GetTableData(hheaTag);
                if (hhea != null && hhea.Length >= 10)
                {
                    short ascender = (short)((hhea[4] << 8) | hhea[5]);
                    short descender = (short)((hhea[6] << 8) | hhea[7]);
                    short lineGap = (short)((hhea[8] << 8) | hhea[9]);
                    int ascDots = (int)Math.Round(Math.Abs((double)ascender) * ppem / upem, MidpointRounding.AwayFromZero);
                    int descDots = (int)Math.Round(Math.Abs((double)descender) * ppem / upem, MidpointRounding.AwayFromZero);
                    int gapDots = lineGap > 0 ? (int)Math.Round((double)lineGap * ppem / upem, MidpointRounding.AwayFromZero) : 0;
                    heightDots = ascDots + descDots + gapDots;
                    return heightDots > 0;
                }
            }
            catch { }
            return false;
        }

        private static class ExcelGdi
        {
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct TextMetric
            {
                public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AverageWidth, MaximumWidth,
                    Weight, Overhang, AspectX, AspectY;
                public char First, Last, Default, Break;
                public byte Italic, Underlined, Struck, Pitch, CharacterSet;
            }
            [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
            [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
            private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight,
                uint italic, uint underline, uint strike, uint charSet, uint outputPrecision, uint clipPrecision,
                uint quality, uint pitch, string face);
            [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
            [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
            private static extern bool GetTextMetricsW(IntPtr dc, out TextMetric metric);
            [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
            [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

            internal static bool TryGetHeight(IXLFont font, int dpi, out int height)
            {
                height = 0;
                IntPtr dc = IntPtr.Zero, handle = IntPtr.Zero, previous = IntPtr.Zero;
                try
                {
                    dc = CreateCompatibleDC(IntPtr.Zero);
                    if (dc == IntPtr.Zero) return false;
                    handle = CreateFontW(-checked((int)Math.Round(font.FontSize * dpi / 72, MidpointRounding.AwayFromZero)),
                        0, 0, 0, font.Bold ? 700 : 400, font.Italic ? 1u : 0u, 0, 0, 1, 0, 0, 0, 0, font.FontName);
                    if (handle == IntPtr.Zero) return false;
                    previous = SelectObject(dc, handle);
                    if (previous == IntPtr.Zero || previous == new IntPtr(-1)) return false;
                    if (!GetTextMetricsW(dc, out var metric)) return false;
                    height = metric.Height + metric.ExternalLeading;
                    return height > 0;
                }
                catch (DllNotFoundException) { return false; }
                catch (EntryPointNotFoundException) { return false; }
                finally
                {
                    if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(dc, previous);
                    if (handle != IntPtr.Zero) DeleteObject(handle);
                    if (dc != IntPtr.Zero) DeleteDC(dc);
                }
            }
        }

        private static List<double> BuildExcelMasterGrid(IEnumerable<LayoutRegion> regions)
        {
            var all = regions.ToList();
            var columns = all.Where(r => r.RuledRows.Count > 0).SelectMany(r => r.GridColumns).ToList();
            var text = all.Where(r => r.RuledRows.Count == 0).SelectMany(r => r.Lines).SelectMany(l => l.Segments).ToList();
            // Independent text needs its own anchors, otherwise two segments can overwrite one master cell.
            return MergeGridCoordinates(columns.Concat(text.Select(s => s.Left))
                .Append(Math.Max(columns.Max(), text.Select(s => s.Right).DefaultIfEmpty(columns.Max()).Max())));
        }

        private static double[] QuantizeExcelGrid(IReadOnlyList<double> boundaries, double screenMdw,
            double printerMdw, int printDpi, double budgetReductionPt)
        {
            if (boundaries.Count < 2 || boundaries.Any(x => !double.IsFinite(x)) ||
                !double.IsFinite(screenMdw) || screenMdw < 1 || !double.IsFinite(printerMdw) || printerMdw < 1 || printDpi <= 0 ||
                !double.IsFinite(budgetReductionPt) || budgetReductionPt < 0)
                throw new ArgumentException("Invalid Excel print geometry or font metrics.");
            double width = boundaries[^1] - boundaries[0];
            if (width <= budgetReductionPt || Enumerable.Range(1, boundaries.Count - 1).Any(i => boundaries[i] <= boundaries[i - 1]))
                throw new ArgumentException("Excel grid boundaries must increase within a positive print budget.");
            double dotsPerPixel = printerMdw / screenMdw;
            double PixelDots(int pixels) => Math.Round(pixels * dotsPerPixel, MidpointRounding.AwayFromZero);
            double minDots = PixelDots(1);
            double budgetDots = Math.Floor((width - budgetReductionPt) * printDpi / 72);
            int count = boundaries.Count - 1;
            if (minDots < 1 || budgetDots < count * minDots)
                throw new ArgumentException("Excel grid cannot fit positive pixel widths in the available print budget.");
            var widths = new double[count];
            double actualDots = 0;
            for (int col = 0; col < count; col++)
            {
                double targetDots = Math.Round((boundaries[col + 1] - boundaries[0]) * (width - budgetReductionPt) / width * printDpi / 72,
                    MidpointRounding.AwayFromZero);
                // Reserve positive pixels for trailing narrow columns; carry every minimum-width dot forward.
                double remaining = Math.Min(targetDots - actualDots, budgetDots - actualDots - (count - col - 1) * minDots);
                int pixels = Math.Max(1, checked((int)Math.Ceiling((remaining + .5) / dotsPerPixel)) - 1);
                while (pixels > 1 && PixelDots(pixels) > remaining) pixels--;
                actualDots += PixelDots(pixels);
                widths[col] = ExcelColumnWidthForPixels(pixels, screenMdw);
            }
            return widths;
        }

        public Task<ConversionExecutionResult> ConvertToXlsxAsync(
            string inputPdfPath,
            string outputXlsxPath,
            bool multipleSheets = true,
            NexusConversionOptions? options = null,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                options ??= new NexusConversionOptions();
                var warnings = new List<string>();

                if (options.TargetExcelPrintDpi <= 0)
                    throw new ArgumentOutOfRangeException(nameof(options.TargetExcelPrintDpi), "Excel print DPI must be positive.");
                EnsureDirectory(outputXlsxPath);
                string tempPath = CreateStagingFilePath(outputXlsxPath);

                int tablesCount = 0;
                int totalPages = 0;

                try
                {
                    using var pdf = PdfDocument.Open(inputPdfPath, new ParsingOptions { UseLenientParsing = true });
                    totalPages = pdf.NumberOfPages;

                    using (var workbook = new XLWorkbook())
                    {
                        int printDpi = options.TargetExcelPrintDpi;
                        double outlineMdw = ClosedXML.Graphics.DefaultGraphicEngine.Instance.Value.GetMaxDigitWidth(workbook.Style.Font, 96);
                        double screenMdw = Math.Round(outlineMdw, MidpointRounding.AwayFromZero);
                        double printerEm = Math.Round(workbook.Style.Font.FontSize * printDpi / 72, MidpointRounding.AwayFromZero);
                        double printerMdw = Math.Round(outlineMdw / (workbook.Style.Font.FontSize * 96 / 72) * printerEm, MidpointRounding.AwayFromZero);
                        // shortcut: pagination allowance verified on Microsoft Print to PDF; validate before targeting another driver/DPI.
                        double clearancePt = Math.Ceiling((Math.Ceiling(printDpi / 96.0) + 1) * 96 / printDpi) * 72 / 96;
                        IXLWorksheet? singleSheet = multipleSheets ? null : workbook.Worksheets.Add("PDF_Data");
                        int currentRow = 1;
                        bool hasRuledTables = false;

                        (Page Page, List<VisualLine> Lines, List<PdfRectangle> Vectors, List<LayoutRegion> Regions) ReadExcelPage(int pageNo)
                        {
                            ct.ThrowIfCancellationRequested();
                            var page = pdf.GetPage(pageNo);
                            var pageWords = page.GetWords(NearestNeighbourWordExtractor.Instance).Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
                            List<VisualLine> lines;
                            if (pageWords.Count == 0)
                            {
                                var scanImg = page.GetImages().OrderByDescending(i => i.WidthInSamples * i.HeightInSamples).FirstOrDefault();
                                byte[]? scanPng = scanImg != null ? DecodePdfImageToPng(scanImg, pdf, warnings) : null;
                                using var scanBmp = scanPng != null ? SKBitmap.Decode(scanPng) : null;
                                var ocrWords = scanBmp != null
                                    ? TryRecognizeScannedPageWords(scanBmp, page.Width, page.Height, pageNo, options, warnings)
                                    : null;
                                if ((ocrWords == null || ocrWords.Count == 0) && options.EnableCurveTextOcr
                                    && (options.CustomOcrProvider != null || options.OcrBackend != null || page.ExperimentalAccess.Paths.Count >= 80))
                                {
                                    ocrWords = TryRecognizeCurveTextPageWords(page, pageNo, options, warnings, new HashSet<UglyToad.PdfPig.Graphics.PdfPath>());
                                }
                                lines = ocrWords != null && ocrWords.Count > 0
                                    ? BuildVisualLinesFromRichWords(ocrWords, page.Width, page.Height, true, pageNo, totalPages)
                                    : new List<VisualLine>();
                            }
                            else
                            {
                                lines = BuildVisualLines(pageWords, null, page.Width, page.Height, pageNo, totalPages, warnings);
                            }
                            var vectors = ExtractVectorGridBounds(page);
                            return (page, lines, vectors, SegmentLinesIntoRegions(lines, vectors, useVectorGrid: true));
                        }
                        // One worksheet has one set of columns, including tables from later PDF pages.
                        var sheetPages = multipleSheets ? null : Enumerable.Range(1, totalPages).Select(ReadExcelPage).ToList();
                        var sheetTables = sheetPages?.SelectMany(p => p.Regions).Where(r => r.RuledRows.Count > 0).ToList();
                        var sheetGrid = sheetTables?.Count > 1 ? BuildExcelMasterGrid(sheetPages!.SelectMany(p => p.Regions)) : null;
                        double? sheetTopMargin = sheetGrid == null ? null : sheetPages!.Min(p => p.Page.Height - Math.Max(
                            p.Regions.Where(r => r.RuledRows.Count > 0).Select(r => r.Top).DefaultIfEmpty(0).Max(),
                            p.Lines.Count == 0 ? 0 : ExcelLineTop(p.Lines[0], singleSheet!.Cell(1, 1))));

                        for (int pageNo = 1; pageNo <= totalPages; pageNo++)
                        {
                            ct.ThrowIfCancellationRequested();
                            var (page, lines, vectorRects, regions) = sheetPages == null ? ReadExcelPage(pageNo) : sheetPages[pageNo - 1];
                            var pageGrid = regions.Where(r => r.RuledRows.Count > 0).OrderByDescending(r => r.GridColumns.Count).FirstOrDefault();
                            var ruledTables = regions.Where(r => r.RuledRows.Count > 0).ToList();
                            var masterGrid = sheetGrid ?? (ruledTables.Count > 1 ? BuildExcelMasterGrid(regions) : null);
                            var printGrid = masterGrid ?? pageGrid?.GridColumns;
                            int GridBoundary(double x) => masterGrid!.FindIndex(boundary => Math.Abs(boundary - x) <= 2);

                            var ws = multipleSheets ? workbook.Worksheets.Add($"Page_{pageNo}") : singleSheet!;
                            if (multipleSheets) currentRow = 1;
                            int pageStartRow = currentRow;
                            double pageOrigin = pageGrid == null ? page.Height : Math.Max(pageGrid.Top,
                                ExcelLineTop(lines[0], ws.Cell(currentRow, 1)));
                            if (masterGrid != null && ruledTables.Count > 0)
                                pageOrigin = Math.Max(ruledTables.Max(r => r.Top), ExcelLineTop(lines[0], ws.Cell(currentRow, 1)));
                            double flowBottom = pageOrigin;
                            bool previousWasTable = false;
                            double rawRightPt = printGrid == null ? 0 : Math.Max(0, page.Width - printGrid[^1]);
                            double gridBudgetReductionPt = Math.Max(0, clearancePt - rawRightPt);
                            double RowHeight(double top, double bottom) => masterGrid != null ? Math.Max(.01, top - bottom)
                                : Math.Max(.75, (Math.Round((pageOrigin - bottom) / .75) - Math.Round((pageOrigin - top) / .75)) * .75);
                            if (printGrid != null && (multipleSheets || pageNo == 1))
                            {
                                ws.PageSetup.Margins.Left = Math.Max(0, printGrid[0]) / 72;
                                ws.PageSetup.Margins.Right = Math.Max(0, rawRightPt - clearancePt) / 72;
                                ws.PageSetup.Margins.Top = Math.Max(0, sheetTopMargin ?? page.Height - pageOrigin) / 72;
                                ws.PageSetup.Margins.Bottom = 0;
                            }
                            if (masterGrid != null && (multipleSheets || pageNo == 1))
                            {
                                var widths = QuantizeExcelGrid(masterGrid, screenMdw, printerMdw, printDpi, gridBudgetReductionPt);
                                for (int col = 0; col < widths.Length; col++) ws.Column(col + 1).Width = widths[col];
                            }
                            if (sheetGrid != null)
                            {
                                if (pageNo > 1 && currentRow > 1) ws.PageSetup.AddHorizontalPageBreak(currentRow - 1);
                                double printableTop = page.Height - ws.PageSetup.Margins.Top * 72;
                                if (printableTop - pageOrigin >= .75) ws.Row(currentRow++).Height = RowHeight(printableTop, pageOrigin);
                            }

                            foreach (var region in regions)
                            {
                                if (pageGrid != null)
                                {
                                    double top = region.IsTable && (masterGrid == null || region.RuledRows.Count > 0)
                                        ? region.Top : ExcelLineTop(region.Lines[0], ws.Cell(currentRow, 1));
                                    if (flowBottom - top >= .75)
                                    {
                                        double gapHeight = RowHeight(flowBottom, top);
                                        if (previousWasTable) ws.Row(currentRow++).Height = gapHeight;
                                        else if (currentRow > 1) ws.Row(currentRow - 1).Height += gapHeight;
                                    }
                                    flowBottom = top;
                                }
                                if (region.RuledRows.Count > 0)
                                {
                                    tablesCount++;
                                    hasRuledTables = true;
                                    int tableStartRow = currentRow;
                                    var fills = page.ExperimentalAccess.Paths.Where(p => p.IsFilled).SelectMany(p => p.Select(sub => (Bounds: sub.GetBoundingRectangle(), Color: p.FillColor)))
                                        .Where(p => p.Bounds.HasValue && p.Bounds.Value.Width > 4 && p.Bounds.Value.Height > 4).ToList();
                                    if (masterGrid == null)
                                    {
                                        var columnWidths = QuantizeExcelGrid(region.GridColumns, screenMdw, printerMdw, printDpi, gridBudgetReductionPt);
                                        for (int col = 0; col < columnWidths.Length; col++) ws.Column(col + 1).Width = columnWidths[col];
                                    }
                                    foreach (var row in region.RuledRows)
                                    {
                                        // Excel rounds heights to pixels; round shared boundaries to avoid cumulative drift.
                                        ws.Row(currentRow).Height = RowHeight(row.Top, row.Bottom);
                                        foreach (var source in row.Cells)
                                        {
                                            int startCol = masterGrid == null ? source.Column : GridBoundary(region.GridColumns[source.Column]);
                                            int endCol = masterGrid == null ? source.Column + source.Span : GridBoundary(region.GridColumns[source.Column + source.Span]);
                                            var range = ws.Range(currentRow, startCol + 1, currentRow, endCol);
                                            if (endCol - startCol > 1) range.Merge();
                                            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                                            range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                                            range.Style.Alignment.WrapText = true;
                                            var cell = range.FirstCell();
                                            double midX = (region.GridColumns[source.Column] + region.GridColumns[source.Column + source.Span]) / 2;
                                            double midY = (row.Top + row.Bottom) / 2;
                                            var fill = fills.LastOrDefault(f => f.Bounds!.Value.Left <= midX && f.Bounds.Value.Right >= midX && f.Bounds.Value.Bottom <= midY && f.Bounds.Value.Top >= midY);
                                            if (fill.Color != null)
                                            {
                                                var (red, green, blue) = SafeToRgbValues(fill.Color);
                                                range.Style.Fill.BackgroundColor = XLColor.FromArgb((int)Math.Round(red * 255), (int)Math.Round(green * 255), (int)Math.Round(blue * 255));
                                            }
                                            WriteTypedExcelCellValue(cell, string.Join("\n", source.Lines.Select(l => l.FullText)));
                                            var first = source.Lines.FirstOrDefault()?.Words.FirstOrDefault();
                                            if (first != null)
                                            {
                                                cell.Style.Font.FontName = first.FontName;
                                                cell.Style.Font.FontSize = first.FontSizePt;
                                                cell.Style.Font.Bold = first.IsBold;
                                                cell.Style.Font.Italic = first.IsItalic;
                                                cell.Style.Font.FontColor = XLColor.FromHtml("#" + first.HexColor);
                                                cell.Style.Alignment.Horizontal = ExcelAlignment(source.Lines[0], region.GridColumns[source.Column], region.GridColumns[source.Column + source.Span]);
                                            }
                                        }
                                        currentRow++;
                                    }
                                    for (int col = 0; col < region.GridColumns.Count - 1; col++)
                                    {
                                        double midX = (region.GridColumns[col] + region.GridColumns[col + 1]) / 2;
                                        int start = 0;
                                        for (int r = 1; r <= region.RuledRows.Count; r++)
                                        {
                                            bool boundary = r == region.RuledRows.Count || vectorRects.Any(edge => edge.Height <= 2 && edge.Left <= midX && edge.Right >= midX
                                                && Math.Abs((edge.Top + edge.Bottom) / 2 - region.RuledRows[r].Top) <= 2);
                                            if (!boundary) continue;
                                            if (r - start > 1)
                                            {
                                                int firstCol = masterGrid == null ? col + 1 : GridBoundary(region.GridColumns[col]) + 1;
                                                int lastCol = masterGrid == null ? col + 1 : GridBoundary(region.GridColumns[col + 1]);
                                                var merged = ws.Range(tableStartRow + start, firstCol, tableStartRow + r - 1, lastCol);
                                                var existing = masterGrid == null ? new List<IXLRange>() : ws.MergedRanges.Where(m => m.Intersects(merged)).ToList();
                                                bool canMerge = masterGrid == null ? !merged.Cells().Any(c => c.IsMerged())
                                                    : existing.All(m => merged.Contains(m) && m.FirstColumn().ColumnNumber() == firstCol && m.LastColumn().ColumnNumber() == lastCol);
                                                if (canMerge)
                                                {
                                                    string text = string.Join("\n", merged.Cells().Select(c => c.GetString()).Where(s => s.Length > 0));
                                                    foreach (var horizontal in existing) horizontal.Unmerge();
                                                    merged.Merge(); WriteTypedExcelCellValue(merged.FirstCell(), text);
                                                }
                                            }
                                            start = r;
                                        }
                                    }
                                    if (options.ReconstructExcelFormulas && currentRow - tableStartRow >= 3)
                                    {
                                        int firstTblCol = masterGrid == null ? 1 : GridBoundary(region.GridColumns[0]) + 1;
                                        int lastTblCol = masterGrid == null ? region.GridColumns.Count - 1 : GridBoundary(region.GridColumns[^1]);
                                        TryReconstructExcelTableFormulas(ws, tableStartRow, currentRow - 1, Math.Max(1, firstTblCol), Math.Max(1, lastTblCol));
                                    }
                                    flowBottom = region.Bottom;
                                    previousWasTable = true;
                                    continue;
                                }
                                if (masterGrid != null)
                                {
                                    if (region.IsTable) tablesCount++;
                                    for (int lineIndex = 0; lineIndex < region.Lines.Count; lineIndex++)
                                    {
                                        var line = region.Lines[lineIndex];
                                        double top = ExcelLineTop(line, ws.Cell(currentRow, 1));
                                        for (int s = 0; s < line.Segments.Count; s++)
                                        {
                                            var segment = line.Segments[s];
                                            int first = Math.Clamp(masterGrid.FindLastIndex(x => x <= segment.Left + 2), 0, masterGrid.Count - 2);
                                            int end = s + 1 < line.Segments.Count
                                                ? Math.Clamp(masterGrid.FindLastIndex(x => x <= line.Segments[s + 1].Left + 2), first + 1, masterGrid.Count - 1)
                                                : masterGrid.Count - 1;
                                            var alignment = line.Segments.Count == 1 ? ExcelAlignment(line, masterGrid[0], masterGrid[^1]) : XLAlignmentHorizontalValues.Left;
                                            if (alignment != XLAlignmentHorizontalValues.Left) first = 0;
                                            var range = ws.Range(currentRow, first + 1, currentRow, end);
                                            if (end - first > 1) range.Merge();
                                            var cell = range.FirstCell();
                                            ExcelLineTop(line with { Words = segment.Words }, cell);
                                            WriteTypedExcelCellValue(cell, segment.Text.Trim());
                                            range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                                            range.Style.Alignment.Horizontal = alignment;
                                        }
                                        double bottom = lineIndex + 1 < region.Lines.Count
                                            ? ExcelLineTop(region.Lines[lineIndex + 1], ws.Cell(currentRow + 1, 1))
                                            : top - ClosedXML.Graphics.DefaultGraphicEngine.Instance.Value.GetTextHeight(ws.Cell(currentRow, 1).Style.Font, 72);
                                        ws.Row(currentRow).Height = RowHeight(top, bottom);
                                        flowBottom = bottom;
                                        previousWasTable = region.IsTable;
                                        currentRow++;
                                    }
                                    continue;
                                }
                                if (region.IsTable && region.TableRows.Count >= 2 && region.ColumnAnchors.Count >= 2)
                                {
                                    tablesCount++;
                                    int unruledStartRow = currentRow;
                                    for (int rIdx = 0; rIdx < region.TableRows.Count; rIdx++)
                                    {
                                        var rowCells = region.TableRows[rIdx];
                                        bool isHeaderRow = rIdx == 0 && region.Lines[rIdx].IsBold;

                                        for (int cIdx = 0; cIdx < rowCells.Length; cIdx++)
                                        {
                                            string val = rowCells[cIdx].Trim();
                                            if (string.IsNullOrEmpty(val)) continue;

                                            var cell = ws.Cell(currentRow, cIdx + 1);
                                            WriteTypedExcelCellValue(cell, val);

                                            if (isHeaderRow || region.Lines[rIdx].IsBold)
                                            {
                                                cell.Style.Font.Bold = true;
                                            }
                                        }
                                        currentRow++;
                                    }
                                    if (options.ReconstructExcelFormulas && currentRow - unruledStartRow >= 3)
                                    {
                                        TryReconstructExcelTableFormulas(ws, unruledStartRow, currentRow - 1, 1, region.ColumnAnchors.Count);
                                    }
                                }
                                else
                                {
                                    for (int lineIndex = 0; lineIndex < region.Lines.Count; lineIndex++)
                                    {
                                        var line = region.Lines[lineIndex];
                                        if (pageGrid != null && line.Segments.Count == 1)
                                        {
                                            var alignment = ExcelAlignment(line, pageGrid.GridColumns[0], pageGrid.GridColumns[^1]);
                                            int last = Math.Clamp(pageGrid.GridColumns.FindIndex(x => x >= line.Right), 1, pageGrid.GridColumns.Count - 1);
                                            if (line.Right > pageGrid.GridColumns[^1]) last = pageGrid.GridColumns.Count - 1;
                                            if (alignment != XLAlignmentHorizontalValues.Left) last = pageGrid.GridColumns.Count - 1;
                                            var range = ws.Range(currentRow, 1, currentRow, last);
                                            if (last > 1) range.Merge();
                                            range.FirstCell().Value = line.FullText;
                                            double top = ExcelLineTop(line, range.FirstCell());
                                            double bottom = lineIndex + 1 < region.Lines.Count
                                                ? ExcelLineTop(region.Lines[lineIndex + 1], ws.Cell(currentRow + 1, 1))
                                                : top - ClosedXML.Graphics.DefaultGraphicEngine.Instance.Value.GetTextHeight(range.FirstCell().Style.Font, 72);
                                            ws.Row(currentRow).Height = RowHeight(top, bottom);
                                            range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                                            range.Style.Alignment.Horizontal = alignment;
                                            flowBottom = bottom;
                                            previousWasTable = false;
                                            currentRow++;
                                            continue;
                                        }
                                        for (int sIdx = 0; sIdx < line.Segments.Count; sIdx++)
                                        {
                                            string val = line.Segments[sIdx].Text.Trim();
                                            if (string.IsNullOrEmpty(val)) continue;

                                            var cell = ws.Cell(currentRow, sIdx + 1);
                                            WriteTypedExcelCellValue(cell, val);
                                            if (line.IsBold) cell.Style.Font.Bold = true;
                                        }
                                        currentRow++;
                                    }
                                }
                            }

                            if (masterGrid != null && currentRow > pageStartRow)
                            {
                                var (cellDots, native) = GetExcelRowCellDots(workbook.Style.Font, printDpi);
                                if (!native && !warnings.Contains("Excel row heights use Skia fallback; native print fidelity is unverified on this platform."))
                                    warnings.Add("Excel row heights use Skia fallback; native print fidelity is unverified on this platform.");
                                var rows = ws.Rows(pageStartRow, currentRow - 1).ToList();
                                var heights = QuantizeExcelRowHeights(rows.Select(r => r.Height).ToArray(), ws.RowHeight, cellDots, printDpi);
                                for (int row = 0; row < rows.Count; row++) rows[row].Height = heights[row];
                            }
                            if (multipleSheets && !regions.Any(r => r.RuledRows.Count > 0))
                            {
                                ws.Columns().AdjustToContents();
                            }
                            if (pageGrid != null)
                            {
                                ws.PageSetup.HorizontalDpi = printDpi;
                                ws.PageSetup.VerticalDpi = printDpi;
                                ws.PageSetup.PageOrientation = page.Width > page.Height ? XLPageOrientation.Landscape : XLPageOrientation.Portrait;
                                // Match dimensions before fitting; printer defaults can shrink an A3 table onto A4.
                                foreach (var (paper, width, height) in new[] {
                                    (XLPaperSize.A3Paper, 841.89, 1190.55), (XLPaperSize.A4Paper, 595.28, 841.89),
                                    (XLPaperSize.A5Paper, 419.53, 595.28), (XLPaperSize.LetterPaper, 612.0, 792.0),
                                    (XLPaperSize.LegalPaper, 612.0, 1008.0), (XLPaperSize.TabloidPaper, 792.0, 1224.0) })
                                {
                                    if (Math.Abs(Math.Min(page.Width, page.Height) - width) > 2 ||
                                        Math.Abs(Math.Max(page.Width, page.Height) - height) > 2) continue;
                                    ws.PageSetup.PaperSize = paper;
                                    break;
                                }
                                ws.PageSetup.FitToPages(1, 0);
                            }

                            progress?.Report((double)pageNo / totalPages * 100.0);
                        }

                        if (!multipleSheets && singleSheet != null)
                        {
                            if (!hasRuledTables) singleSheet.Columns().AdjustToContents();
                        }

                        workbook.SaveAs(tempPath);
                    }

                    CommitStagingFile(tempPath, outputXlsxPath);
                }
                catch
                {
                    CleanupStagingFile(tempPath);
                    throw;
                }

                sw.Stop();
                var fi = new FileInfo(outputXlsxPath);
                return new ConversionExecutionResult
                {
                    IsSuccess = fi.Exists && fi.Length > 0,
                    OutputPath = outputXlsxPath,
                    PagesProcessed = totalPages,
                    TablesReconstructed = tablesCount,
                    OutputSizeBytes = fi.Exists ? fi.Length : 0,
                    Elapsed = sw.Elapsed,
                    CreatedFiles = new[] { outputXlsxPath },
                    Warnings = warnings
                };
            }, ct);
        }

        // =====================================================================
        // 3. PDF -> POWERPOINT (.PPTX)
        // =====================================================================
        public Task<ConversionExecutionResult> ConvertToPptxAsync(
            string inputPdfPath,
            string outputPptxPath,
            NexusConversionOptions? options = null,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                options ??= new NexusConversionOptions();
                var warnings = new List<string>();

                EnsureDirectory(outputPptxPath);
                string tempPath = CreateStagingFilePath(outputPptxPath);

                int totalImages = 0;
                int totalPages = 0;

                try
                {
                    using var pdf = PdfDocument.Open(inputPdfPath, new ParsingOptions { UseLenientParsing = true });
                    totalPages = pdf.NumberOfPages;

                    using (var pptDoc = PresentationDocument.Create(tempPath, PresentationDocumentType.Presentation))
                    {
                        var presentationPart = pptDoc.AddPresentationPart();
                        presentationPart.Presentation = new P.Presentation();

                        var slideMasterPart = CreateValidSlideMaster(presentationPart);
                        var slideLayoutPart = slideMasterPart.SlideLayoutParts.First();

                        var slideMasterIdList = new P.SlideMasterIdList(
                            new P.SlideMasterId { Id = 2147483648U, RelationshipId = presentationPart.GetIdOfPart(slideMasterPart) });
                        var slideIdList = new P.SlideIdList();

                        uint slideId = 256U;
                        double slideWidthPt = 720.0;
                        double slideHeightPt = 540.0;

                        for (int pageNo = 1; pageNo <= totalPages; pageNo++)
                        {
                            ct.ThrowIfCancellationRequested();
                            var page = pdf.GetPage(pageNo);
                            if (pageNo == 1 && page.Width > 0 && page.Height > 0)
                            {
                                slideWidthPt = page.Width;
                                slideHeightPt = page.Height;
                            }

                            double scale = Math.Min(slideWidthPt / page.Width, slideHeightPt / page.Height);
                            double offsetX = (slideWidthPt - page.Width * scale) / 2.0;
                            double offsetY = (slideHeightPt - page.Height * scale) / 2.0;

                            string relId = $"rIdSlide{pageNo}";
                            var slidePart = presentationPart.AddNewPart<SlidePart>(relId);
                            slidePart.AddPart(slideLayoutPart);

                            var shapeTree = new P.ShapeTree(
                                new P.NonVisualGroupShapeProperties(
                                    new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                                    new P.NonVisualGroupShapeDrawingProperties(),
                                    new P.ApplicationNonVisualDrawingProperties()),
                                new P.GroupShapeProperties(new A.TransformGroup()));

                            uint shapeId = 2U;

                            foreach (var pdfImg in page.GetImages())
                            {
                                byte[]? pngBytes = DecodePdfImageToPng(pdfImg, pdf, warnings);
                                if (pngBytes == null || pngBytes.Length == 0) continue;

                                double imgW = pdfImg.Bounds.Width > 0 ? pdfImg.Bounds.Width : Math.Min(slideWidthPt, pdfImg.WidthInSamples * 0.75);
                                double imgH = pdfImg.Bounds.Height > 0 ? pdfImg.Bounds.Height : Math.Min(slideHeightPt, pdfImg.HeightInSamples * 0.75);

                                long xEmu = (long)Math.Round((offsetX + pdfImg.Bounds.Left * scale) * 12700.0);
                                long yEmu = (long)Math.Round((offsetY + (page.Height - pdfImg.Bounds.Top) * scale) * 12700.0);
                                long wEmu = (long)Math.Round(Math.Max(0.01, imgW * scale) * 12700.0);
                                long hEmu = (long)Math.Round(Math.Max(0.01, imgH * scale) * 12700.0);

                                var imgPart = slidePart.AddImagePart(ImagePartType.Png);
                                using (var ms = new MemoryStream(pngBytes))
                                {
                                    imgPart.FeedData(ms);
                                }
                                string imgRelId = slidePart.GetIdOfPart(imgPart);

                                var pic = new P.Picture(
                                    new P.NonVisualPictureProperties(
                                        new P.NonVisualDrawingProperties { Id = shapeId, Name = $"SlideImage_{shapeId}" },
                                        new P.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true }),
                                        new P.ApplicationNonVisualDrawingProperties()),
                                    new P.BlipFill(
                                        new A.Blip { Embed = imgRelId },
                                        new A.Stretch(new A.FillRectangle())),
                                    new P.ShapeProperties(
                                        new A.Transform2D(
                                            new A.Offset { X = xEmu, Y = yEmu },
                                            new A.Extents { Cx = wEmu, Cy = hEmu }),
                                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));

                                shapeTree.Append(pic);
                                shapeId++;
                                totalImages++;
                            }

                            var words = page.GetWords(NearestNeighbourWordExtractor.Instance).Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
                            var slideWordArt = options.LayoutMode == ConversionLayoutMode.Editable
                                ? RecoverTaggedWordArt(pdf, page, "Arial")
                                : new List<TaggedWordArt>();
                            var slideConsumedGlyphPaths = new HashSet<UglyToad.PdfPig.Graphics.PdfPath>();
                            List<RichWord>? slideOcrWords = null;
                            if (words.Count == 0 && slideWordArt.Count == 0 && options.LayoutMode == ConversionLayoutMode.Editable && options.EnableCurveTextOcr
                                && (options.CustomOcrProvider != null || options.OcrBackend != null || page.ExperimentalAccess.Paths.Count >= 80))
                            {
                                slideOcrWords = TryRecognizeCurveTextPageWords(page, pageNo, options, warnings, slideConsumedGlyphPaths);
                            }
                            var slideRecoveredPaths = slideWordArt.SelectMany(a => a.Paths).Concat(slideConsumedGlyphPaths).ToHashSet();
                            var slideVectorPaths = GetClippedPagePaths(page, slideRecoveredPaths)
                                .Where(item => (item.Path.IsFilled || item.Path.IsStroked)
                                    && item.EffectiveBounds.Width > 0.5 && item.EffectiveBounds.Height > 0.5
                                    && !IsPureWhiteCanvasBackdrop(item.Path, item.EffectiveBounds, page.Width, page.Height))
                                .ToList();

                            foreach (var vecShape in BuildPptxGroupedVectorShapes(slideVectorPaths, page.Height, scale, offsetX, offsetY, ref shapeId))
                            {
                                shapeTree.Append(vecShape);
                            }

                            foreach (var art in slideWordArt)
                            {
                                shapeTree.Append(BuildPptxWordArtShape(art, page.Height, scale, offsetX, offsetY, shapeId++));
                            }

                            if (slideOcrWords != null && slideOcrWords.Count > 0)
                            {
                                var ocrLines = BuildVisualLinesFromRichWords(slideOcrWords, page.Width, page.Height, true, pageNo, totalPages);
                                var verticalGridRules = ExtractVectorGridBounds(page)
                                    .Where(r => r.Width <= 2.5 && r.Height >= 12.0)
                                    .ToList();

                                foreach (var ocrLine in ocrLines)
                                {
                                    // Split line into positioned segments at table vertical borders or horizontal column gaps (>= 10pt)
                                    var lineChunks = SplitOcrLineIntoPositionedChunks(ocrLine, verticalGridRules);
                                    foreach (var chunk in lineChunks)
                                    {
                                        double chunkLeft = chunk.Min(w => w.Left);
                                        double chunkRight = chunk.Max(w => w.Right);
                                        long lxEmu = (long)Math.Round((offsetX + chunkLeft * scale) * 12700.0);
                                        long lyEmu = (long)Math.Round((offsetY + (page.Height - ocrLine.Top) * scale) * 12700.0);
                                        long lwEmu = (long)Math.Round(Math.Max(12.0, (chunkRight - chunkLeft + 10.0) * scale) * 12700.0);
                                        long lhEmu = (long)Math.Round(Math.Max(8.0, (ocrLine.Top - ocrLine.Bottom + 4.0) * scale) * 12700.0);
                                        var tb = new P.TextBody(new A.BodyProperties
                                        {
                                            Wrap = A.TextWrappingValues.None,
                                            LeftInset = 0, RightInset = 0, TopInset = 0, BottomInset = 0
                                        }, new A.ListStyle());
                                        var aPara = new A.Paragraph();
                                        for (int wIdx = 0; wIdx < chunk.Count; wIdx++)
                                        {
                                            var rw = chunk[wIdx];
                                            var rPr = new A.RunProperties
                                            {
                                                Language = "en-US",
                                                FontSize = (int)Math.Round(Math.Clamp(rw.FontSizePt * scale, 1.0, 400.0) * 100.0),
                                                Bold = rw.IsBold,
                                                Italic = rw.IsItalic
                                            };
                                            rPr.Append(new A.SolidFill(new A.RgbColorModelHex { Val = rw.HexColor }));
                                            if (!string.IsNullOrEmpty(rw.BackgroundFillHex) || rw.IsHighlighted)
                                            {
                                                rPr.Append(new A.Highlight(new A.RgbColorModelHex { Val = rw.BackgroundFillHex ?? "F1F5F9" }));
                                            }
                                            rPr.Append(new A.LatinFont { Typeface = rw.FontName });
                                            string txt = wIdx < chunk.Count - 1 ? rw.Text + " " : rw.Text;
                                            aPara.Append(new A.Run(rPr, new A.Text(txt)));
                                        }
                                        tb.Append(aPara);
                                        shapeTree.Append(new P.Shape(
                                            new P.NonVisualShapeProperties(
                                                new P.NonVisualDrawingProperties { Id = shapeId, Name = $"OcrLine_{shapeId}" },
                                                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                                                new P.ApplicationNonVisualDrawingProperties()),
                                            new P.ShapeProperties(
                                                new A.Transform2D(
                                                    new A.Offset { X = lxEmu, Y = lyEmu },
                                                    new A.Extents { Cx = lwEmu, Cy = lhEmu }),
                                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                                                new A.NoFill()),
                                            tb));
                                        shapeId++;
                                    }
                                }
                            }

                            var blocks = words.Count > 0
                                ? DocstrumBoundingBoxes.Instance.GetBlocks(words)
                                : Array.Empty<TextBlock>();

                            foreach (var block in blocks)
                            {
                                long xEmu = (long)Math.Round((offsetX + block.BoundingBox.Left * scale) * 12700.0);
                                long yEmu = (long)Math.Round((offsetY + (page.Height - block.BoundingBox.Top) * scale) * 12700.0);
                                long wEmu = (long)Math.Round(Math.Max(0.01, (block.BoundingBox.Width + 12.0) * scale) * 12700.0);
                                long hEmu = (long)Math.Round(Math.Max(0.01, (block.BoundingBox.Height + 6.0) * scale) * 12700.0);

                                var textBody = new P.TextBody(new A.BodyProperties
                                {
                                    Wrap = A.TextWrappingValues.None,
                                    LeftInset = 0, RightInset = 0, TopInset = 0, BottomInset = 0
                                }, new A.ListStyle());
                                foreach (var line in block.TextLines)
                                {
                                    var aPara = new A.Paragraph();
                                    foreach (var (w, wIdx) in line.Words.Select((w, idx) => (w, idx)))
                                    {
                                        double pt = w.Letters.Count > 0 ? w.Letters.Average(l => l.PointSize > 0 ? l.PointSize : 12.0) : 12.0;
                                        string fontName = CleanFontName(w.FontName);
                                        bool isBold = w.FontName != null && (w.FontName.Contains("Bold", StringComparison.OrdinalIgnoreCase) || w.FontName.Contains("Black", StringComparison.OrdinalIgnoreCase));
                                        bool isItalic = w.FontName != null && (w.FontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) || w.FontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase));
                                        string hexColor = ExtractWordHexColor(w, null, page.Width, page.Height);

                                        var rPr = new A.RunProperties
                                        {
                                            Language = "en-US",
                                            FontSize = (int)Math.Round(Math.Clamp(pt * scale, 1.0, 400.0) * 100.0),
                                            Bold = isBold,
                                            Italic = isItalic
                                        };
                                        rPr.Append(new A.SolidFill(new A.RgbColorModelHex { Val = hexColor }));
                                        rPr.Append(new A.LatinFont { Typeface = fontName });

                                        string txt = wIdx < line.Words.Count - 1 ? w.Text + " " : w.Text;
                                        aPara.Append(new A.Run(rPr, new A.Text(txt)));
                                    }
                                    textBody.Append(aPara);
                                }

                                var shape = new P.Shape(
                                    new P.NonVisualShapeProperties(
                                        new P.NonVisualDrawingProperties { Id = shapeId, Name = $"Block_{shapeId}" },
                                        new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                                        new P.ApplicationNonVisualDrawingProperties()),
                                    new P.ShapeProperties(
                                        new A.Transform2D(
                                            new A.Offset { X = xEmu, Y = yEmu },
                                            new A.Extents { Cx = wEmu, Cy = hEmu }),
                                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }),
                                    textBody);

                                shapeTree.Append(shape);
                                shapeId++;
                            }

                            slidePart.Slide = new P.Slide(
                                new P.CommonSlideData(shapeTree),
                                new P.ColorMapOverride(new A.MasterColorMapping()));
                            slidePart.Slide.Save();

                            slideIdList.Append(new P.SlideId { Id = slideId++, RelationshipId = relId });
                            progress?.Report((double)pageNo / totalPages * 100.0);
                        }

                        presentationPart.Presentation.Append(
                            slideMasterIdList,
                            slideIdList,
                            new P.SlideSize
                            {
                                Cx = (int)Math.Round(slideWidthPt * 12700.0),
                                Cy = (int)Math.Round(slideHeightPt * 12700.0)
                            },
                            new P.NotesSize { Cx = 6858000, Cy = 9144000 });

                        presentationPart.Presentation.Save();
                    }

                    if (options.ValidateOpenXmlOutput)
                    {
                        using var verifyPpt = PresentationDocument.Open(tempPath, false);
                        var validator = new OpenXmlValidator();
                        var schemaErrors = validator.Validate(verifyPpt).ToList();
                        if (schemaErrors.Count > 0)
                        {
                            throw new InvalidDataException($"Validasi skema PPTX gagal dengan {schemaErrors.Count} error: {schemaErrors[0].Description}");
                        }
                    }

                    CommitStagingFile(tempPath, outputPptxPath);
                }
                catch
                {
                    CleanupStagingFile(tempPath);
                    throw;
                }

                sw.Stop();
                var fi = new FileInfo(outputPptxPath);
                return new ConversionExecutionResult
                {
                    IsSuccess = fi.Exists && fi.Length > 0,
                    OutputPath = outputPptxPath,
                    PagesProcessed = totalPages,
                    ImagesProcessed = totalImages,
                    OutputSizeBytes = fi.Exists ? fi.Length : 0,
                    Elapsed = sw.Elapsed,
                    CreatedFiles = new[] { outputPptxPath },
                    Warnings = warnings
                };
            }, ct);
        }

        // =====================================================================
        // 4. PDF -> EXTRACT IMAGES
        // =====================================================================
        public Task<ConversionExecutionResult> ExtractImagesAsync(
            string inputPdfPath,
            string outputDirectory,
            NexusConversionOptions? options = null,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                Directory.CreateDirectory(outputDirectory);

                var createdFiles = new List<string>();
                var warnings = new List<string>();
                int count = 0;
                long totalSize = 0;

                using var pdf = PdfDocument.Open(inputPdfPath, new ParsingOptions { UseLenientParsing = true });
                int totalPages = pdf.NumberOfPages;

                options ??= new NexusConversionOptions();
                for (int pageNo = 1; pageNo <= totalPages; pageNo++)
                {
                    ct.ThrowIfCancellationRequested();
                    var page = pdf.GetPage(pageNo);
                    int pageRasterCount = 0;
                    foreach (var pdfImg in page.GetImages())
                    {
                        byte[]? pngBytes = DecodePdfImageToPng(pdfImg, pdf, warnings);
                        if (pngBytes != null && pngBytes.Length > 0)
                        {
                            count++;
                            pageRasterCount++;
                            string outFile = Path.Combine(outputDirectory, $"img_{count}.png");
                            string tempFile = CreateStagingFilePath(outFile);
                            try
                            {
                                File.WriteAllBytes(tempFile, pngBytes);
                                CommitStagingFile(tempFile, outFile);
                                createdFiles.Add(outFile);
                                totalSize += pngBytes.Length;
                            }
                            catch
                            {
                                CleanupStagingFile(tempFile);
                                throw;
                            }
                        }
                    }

                    if (pageRasterCount == 0 && options.ExtractVectorIllustrationsAsPng)
                    {
                        var clippedPaths = GetClippedPagePaths(page, null)
                            .Where(item => (item.Path.IsFilled || item.Path.IsStroked)
                                && item.EffectiveBounds.Width > 1.5 && item.EffectiveBounds.Height > 1.5
                                && !IsPureWhiteCanvasBackdrop(item.Path, item.EffectiveBounds, page.Width, page.Height))
                            .ToList();
                        bool hasCurvedOrRichVectorArtwork = clippedPaths.Count >= 8
                            && clippedPaths.Any(item => item.Path.Any(sub => sub.Commands.Any(c => c is PdfSubpath.CubicBezierCurve or PdfSubpath.QuadraticBezierCurve)));
                        if (hasCurvedOrRichVectorArtwork)
                        {
                            byte[]? vecPng = RenderClippedVectorPathsToPng(clippedPaths, page.Width, page.Height, dpi: 150);
                            if (vecPng != null && vecPng.Length > 0)
                            {
                                count++;
                                string outFile = Path.Combine(outputDirectory, $"img_{count}.png");
                                string tempFile = CreateStagingFilePath(outFile);
                                try
                                {
                                    File.WriteAllBytes(tempFile, vecPng);
                                    CommitStagingFile(tempFile, outFile);
                                    createdFiles.Add(outFile);
                                    totalSize += vecPng.Length;
                                }
                                catch
                                {
                                    CleanupStagingFile(tempFile);
                                    throw;
                                }
                            }
                        }
                    }

                    progress?.Report((double)pageNo / totalPages * 100.0);
                }

                if (count == 0)
                {
                    warnings.Add("Tidak ada objek gambar tertanam yang ditemukan di dalam dokumen PDF.");
                }

                sw.Stop();
                return new ConversionExecutionResult
                {
                    IsSuccess = true,
                    OutputPath = outputDirectory,
                    PagesProcessed = totalPages,
                    ImagesProcessed = count,
                    OutputSizeBytes = totalSize,
                    Elapsed = sw.Elapsed,
                    CreatedFiles = createdFiles,
                    Warnings = warnings
                };
            }, ct);
        }

        // =====================================================================
        // MODEL INTERNAL & FUNGSI PEMROSESAN UNIVERSAL (DIGITAL + OCR)
        // =====================================================================
        private sealed record StandaloneImageItem(
            byte[] PngBytes,
            double TopPt,
            double BottomPt,
            double LeftPt,
            double RightPt,
            double WidthPt,
            double HeightPt,
            bool ForceInFrontOfText = false);

        private sealed record RichWord(
            string Text,
            double Left,
            double Right,
            double Bottom,
            double Top,
            double FontSizePt,
            string FontName,
            string HexColor,
            bool IsBold,
            bool IsItalic,
            double Baseline,
            bool IsUnderline = false,
            bool IsStrike = false,
            bool IsHighlighted = false)
        {
            public RichWord(
                string Text,
                double Left,
                double Right,
                double Bottom,
                double Top,
                double FontSizePt,
                string FontName,
                string HexColor,
                bool IsBold,
                bool IsItalic,
                double Baseline,
                bool IsUnderline,
                bool IsStrike)
                : this(Text, Left, Right, Bottom, Top, FontSizePt, FontName, HexColor, IsBold, IsItalic, Baseline, IsUnderline, IsStrike, false)
            {
            }

            public string? BackgroundFillHex { get; init; }
            public string? VerticalAlign { get; init; }
        }

        private sealed record WordSegment(
            IReadOnlyList<RichWord> Words,
            double Left,
            double Right,
            double Bottom,
            double Top)
        {
            public string Text => string.Join(" ", Words.Select(w => w.Text));
        }

        private sealed record VisualLine(
            IReadOnlyList<RichWord> Words,
            double Left,
            double Right,
            double Bottom,
            double Top,
            double FontSizePt,
            bool IsBold,
            IReadOnlyList<WordSegment> Segments)
        {
            public string FullText => string.Join(" ", Words.Select(w => w.Text));
        }

        private sealed class LayoutRegion
        {
            public bool IsTable { get; set; }
            public List<VisualLine> Lines { get; } = new();
            public List<double> ColumnAnchors { get; set; } = new();
            public List<string[]> TableRows { get; set; } = new();
            public List<double> GridColumns { get; set; } = new();
            public List<RuledRow> RuledRows { get; set; } = new();
            public double Top => RuledRows.Count > 0 ? RuledRows[0].Top : Lines.Count > 0 ? Lines.Max(l => l.Top) : 0;
            public double Bottom => RuledRows.Count > 0 ? RuledRows[^1].Bottom : Lines.Count > 0 ? Lines.Min(l => l.Bottom) : 0;
            public double Left => Lines.Count > 0 ? Lines.Min(l => l.Left) : 0;
            public double Right => Lines.Count > 0 ? Lines.Max(l => l.Right) : 0;
        }

        private sealed record RuledCell(int Column, int Span, List<VisualLine> Lines, W.MergedCellValues? VerticalMerge = null);
        private sealed record RuledRow(double Top, double Bottom, List<RuledCell> Cells);

        private static string CreateStagingFilePath(string finalOutputPath)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(finalOutputPath)) ?? AppContext.BaseDirectory;
            string name = Path.GetFileName(finalOutputPath);
            // ClosedXML chooses its writer from the file extension, including staged files.
            return Path.Combine(dir, $".~{name}.{Guid.NewGuid():N}.tmp{Path.GetExtension(name)}");
        }

        private static void CommitStagingFile(string stagingPath, string finalOutputPath)
        {
            File.Move(stagingPath, finalOutputPath, overwrite: true);
        }

        private static void CleanupStagingFile(string stagingPath)
        {
            try
            {
                if (File.Exists(stagingPath)) File.Delete(stagingPath);
            }
            catch { }
        }

        private static W.SectionProperties CreateSectionProperties(double pageWidthPt, double pageHeightPt,
            bool isNextPageSectionBreak, double leftMarginPt, double rightMarginPt, double topMarginPt, double bottomMarginPt)
        {
            uint widthDxa = (uint)Math.Round(Math.Max(144.0, pageWidthPt) * 20.0);
            uint heightDxa = (uint)Math.Round(Math.Max(144.0, pageHeightPt) * 20.0);

            var sectPr = new W.SectionProperties();
            if (isNextPageSectionBreak)
            {
                sectPr.Append(new W.SectionType { Val = W.SectionMarkValues.NextPage });
            }

            sectPr.Append(new W.PageSize
            {
                Width = widthDxa,
                Height = heightDxa,
                Orient = pageWidthPt > pageHeightPt ? W.PageOrientationValues.Landscape : W.PageOrientationValues.Portrait
            });
            sectPr.Append(new W.PageMargin
            {
                Top = (int)Math.Round(topMarginPt * 20), Bottom = (int)Math.Round(bottomMarginPt * 20),
                Left = (uint)Math.Round(leftMarginPt * 20.0),
                Right = (uint)Math.Round(rightMarginPt * 20.0)
            });
            return sectPr;
        }

        private static void WriteTypedExcelCellValue(IXLCell cell, string rawText)
        {
            string trimmed = rawText?.Trim() ?? string.Empty;
            if (trimmed.Length > 1 && trimmed.StartsWith('='))
            {
                try
                {
                    cell.FormulaA1 = trimmed[1..].Trim();
                    return;
                }
                catch { }
            }

            if (TryParseLocaleAwareNumber(rawText ?? string.Empty, out double numericValue, out bool hasDecimals))
            {
                cell.Value = numericValue;
                if (hasDecimals)
                {
                    string numPart = trimmed;
                    if (numPart.StartsWith("Rp", StringComparison.OrdinalIgnoreCase)) numPart = numPart[2..].Trim();
                    else if (numPart.Length > 0 && (numPart[0] is '$' or '€' or '£' or '¥')) numPart = numPart[1..].Trim();
                    numPart = Regex.Replace(numPart, @"(?<=\d[.,])\s+(?=\d)", "");
                    int sepIdx = Math.Max(numPart.LastIndexOf('.'), numPart.LastIndexOf(','));
                    int decimalPlaces = sepIdx >= 0 ? Math.Clamp(numPart.Length - sepIdx - 1, 1, 10) : 2;
                    cell.Style.NumberFormat.Format = "#,##0." + new string('0', decimalPlaces);
                }
            }
            else if (TryParseExtendedExcelNumber(trimmed, out double extValue, out string? numberFormat))
            {
                cell.Value = extValue;
                if (!string.IsNullOrEmpty(numberFormat))
                {
                    cell.Style.NumberFormat.Format = numberFormat;
                }
            }
            else
            {
                cell.Value = rawText ?? string.Empty;
            }
        }

        private static bool TryParseExtendedExcelNumber(string trimmed, out double value, out string? numberFormat)
        {
            value = 0;
            numberFormat = null;
            if (string.IsNullOrWhiteSpace(trimmed)) return false;

            if (trimmed.EndsWith('%') && trimmed.Length > 1)
            {
                string inner = trimmed[..^1].Trim();
                if (TryParseLocaleAwareNumber(inner, out double pctVal, out bool pctDecimals))
                {
                    value = pctVal / 100.0;
                    int sepIdx = Math.Max(inner.LastIndexOf('.'), inner.LastIndexOf(','));
                    int decs = pctDecimals && sepIdx >= 0 ? Math.Clamp(inner.Length - sepIdx - 1, 1, 6) : 0;
                    numberFormat = decs > 0 ? "0." + new string('0', decs) + "%" : "0%";
                    return true;
                }
            }

            string s = trimmed;
            if (s.StartsWith("Rp", StringComparison.OrdinalIgnoreCase)) s = s[2..].Trim();
            else if (s.Length > 0 && (s[0] is '$' or '€' or '£' or '¥')) s = s[1..].Trim();
            if (s.Length >= 3 && s[0] == '(' && s[^1] == ')')
            {
                string inner = s[1..^1].Trim();
                if (TryParseLocaleAwareNumber(inner, out double posVal, out bool accDecimals))
                {
                    value = -Math.Abs(posVal);
                    int sepIdx = Math.Max(inner.LastIndexOf('.'), inner.LastIndexOf(','));
                    int decs = accDecimals && sepIdx >= 0 ? Math.Clamp(inner.Length - sepIdx - 1, 1, 6) : 0;
                    numberFormat = decs > 0
                        ? "#,##0." + new string('0', decs) + ";(#,##0." + new string('0', decs) + ")"
                        : "#,##0;(#,##0)";
                    return true;
                }
            }

            return false;
        }

        private static readonly Regex ExcelSummaryRowLabelRegex = new(
            @"^(?:Total|Jumlah|Sub\s*-?\s*total|Grand\s+Total|Nilai\s+Total|Rata\s*-?\s*rata|Average)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static void TryReconstructExcelTableFormulas(IXLWorksheet ws, int startRow, int endRow, int startCol, int endCol)
        {
            if (endRow - startRow < 2 || endCol < startCol) return;
            for (int r = startRow + 2; r <= endRow; r++)
            {
                string rowLabel = string.Empty;
                for (int c = startCol; c <= endCol; c++)
                {
                    var cell = ws.Cell(r, c);
                    if (cell.DataType == XLDataType.Text && !string.IsNullOrWhiteSpace(cell.GetString()))
                    {
                        rowLabel = cell.GetString().Trim();
                        break;
                    }
                }
                if (!ExcelSummaryRowLabelRegex.IsMatch(rowLabel)) continue;
                bool isAverage = rowLabel.StartsWith("Rata", StringComparison.OrdinalIgnoreCase)
                    || rowLabel.StartsWith("Average", StringComparison.OrdinalIgnoreCase);

                for (int c = startCol; c <= endCol; c++)
                {
                    var targetCell = ws.Cell(r, c);
                    if (targetCell.HasFormula || targetCell.DataType != XLDataType.Number) continue;
                    double targetVal = targetCell.GetDouble();
                    int runStart = r - 1;
                    var values = new List<double>();
                    while (runStart >= startRow)
                    {
                        var above = ws.Cell(runStart, c);
                        if (above.DataType == XLDataType.Number && !above.HasFormula)
                        {
                            values.Add(above.GetDouble());
                            runStart--;
                        }
                        else break;
                    }
                    if (values.Count < 2) continue;
                    double expected = isAverage ? values.Average() : values.Sum();
                    if (Math.Abs(expected - targetVal) <= Math.Max(1e-5, Math.Abs(targetVal) * 1e-6))
                    {
                        string colLetter = ws.Column(c).ColumnLetter();
                        string fn = isAverage ? "AVERAGE" : "SUM";
                        targetCell.FormulaA1 = $"{fn}({colLetter}{runStart + 1}:{colLetter}{r - 1})";
                    }
                }
            }
        }

        public static bool TryParseLocaleAwareNumber(string rawText, out double result, out bool hasDecimals)
        {
            result = 0;
            hasDecimals = false;
            if (string.IsNullOrWhiteSpace(rawText)) return false;

            string s = rawText.Trim();
            if (s.StartsWith("Rp", StringComparison.OrdinalIgnoreCase)) s = s[2..].Trim();
            else if (s.StartsWith('$') || s.StartsWith('€') || s.StartsWith('£') || s.StartsWith('¥')) s = s[1..].Trim();

            // Strip spurious OCR/PDF space immediately following a number separator before digits (e.g., "1. 234,5678" -> "1.234,5678")
            if (s.Contains(' '))
            {
                s = Regex.Replace(s, @"(?<=\d[.,])\s+(?=\d)", "");
            }

            if (string.IsNullOrEmpty(s)) return false;

            string digitsCheck = s.TrimStart('-', '+');
            if (digitsCheck.Length > 1 && digitsCheck[0] == '0' && char.IsDigit(digitsCheck[1]))
            {
                return false;
            }

            if (!Regex.IsMatch(s, @"^[+-]?\d(?:[\d.,]*\d)?$"))
            {
                return false;
            }
            // Excel only preserves 15 significant digits; identifiers must stay text.
            if (new string(s.Where(char.IsDigit).ToArray()).TrimStart('0').Length > 15)
                return false;

            int lastDot = s.LastIndexOf('.');
            int lastComma = s.LastIndexOf(',');
            int dotCount = s.Count(c => c == '.');
            int commaCount = s.Count(c => c == ',');

            string normalized;
            if (dotCount > 0 && commaCount > 0)
            {
                char groupSeparator = lastComma > lastDot ? '.' : ',';
                char decimalSeparator = lastComma > lastDot ? ',' : '.';
                string groupedPattern = @"^[+-]?\d{1,3}(?:" + Regex.Escape(groupSeparator.ToString())
                    + @"\d{3})+" + Regex.Escape(decimalSeparator.ToString()) + @"\d+$";
                if (!Regex.IsMatch(s, groupedPattern)) return false;
                if (lastComma > lastDot)
                {
                    normalized = s.Replace(".", "").Replace(',', '.');
                    hasDecimals = true;
                }
                else
                {
                    normalized = s.Replace(",", "");
                    hasDecimals = true;
                }
            }
            else if (commaCount > 0)
            {
                if (commaCount == 1)
                {
                    int digitsAfterComma = s.Length - lastComma - 1;
                    if (digitsAfterComma != 3 || digitsCheck.StartsWith("0,", StringComparison.Ordinal))
                    {
                        normalized = s.Replace(',', '.');
                        hasDecimals = true;
                    }
                    else
                    {
                        if (!Regex.IsMatch(s, @"^[+-]?\d{1,3},\d{3}$")) return false;
                        normalized = s.Replace(",", "");
                    }
                }
                else
                {
                    if (!Regex.IsMatch(s, @"^[+-]?\d{1,3}(,\d{3})+$")) return false;
                    normalized = s.Replace(",", "");
                }
            }
            else if (dotCount > 0)
            {
                if (dotCount == 1)
                {
                    normalized = s;
                    hasDecimals = true;
                }
                else
                {
                    if (!Regex.IsMatch(s, @"^[+-]?\d{1,3}(\.\d{3})+$")) return false;
                    normalized = s.Replace(".", "");
                }
            }
            else
            {
                normalized = s;
            }

            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                && double.IsFinite(result);
        }

        private static readonly Regex GluedListMarkerWordRegex = new(
            @"^(\(\d{1,3}\)|\([a-zA-Z]\)|\([ivxlcdmIVXLCDM]{1,6}\)|\d{1,3}[.)]|[ivxlcdmIVXLCDM]{1,6}[.)]|[a-zA-Z][.)])(\s*\S.*)$",
            RegexOptions.Compiled);

        private static List<PdfRectangle> ExtractInlineTextDecorationRects(Page page)
        {
            var rules = new List<PdfRectangle>();
            try
            {
                foreach (var p in page.ExperimentalAccess.Paths)
                {
                    if (!p.IsFilled && !p.IsStroked) continue;
                    foreach (var sub in p)
                    {
                        var b = sub.GetBoundingRectangle();
                        if (b.HasValue && b.Value.Width >= 3.0 && b.Value.Width <= page.Width * 0.45 && b.Value.Height <= 2.2)
                        {
                            rules.Add(b.Value);
                        }
                    }
                }
            }
            catch { }
            return rules;
        }

        private static List<(PdfRectangle Bounds, string FillHex)> ExtractPageInlineBackgroundPills(
            Page page,
            HashSet<UglyToad.PdfPig.Graphics.PdfPath> consumedPaths)
        {
            var pills = new List<(PdfRectangle Bounds, string FillHex)>();
            try
            {
                var clipped = GetClippedPagePaths(page, null);
                foreach (var item in clipped)
                {
                    if (TryExtractInlineBackgroundPill(item, page.Width, page.Height, out string fillHex))
                    {
                        pills.Add((item.EffectiveBounds, fillHex));
                        consumedPaths.Add(item.Path);
                    }
                }
            }
            catch { }
            return pills;
        }

        private static List<VisualLine> BuildVisualLines(
            List<Word> rawWords,
            SKBitmap? pageBitmap,
            double pageWidthPt,
            double pageHeightPt,
            int pageNo,
            int totalPages,
            List<string> warnings,
            List<PdfRectangle>? decorationRects = null,
            List<(PdfRectangle Bounds, string FillHex)>? inlinePills = null)
        {
            bool isOcrPage = pageBitmap != null;
            var initialWords = rawWords.SelectMany(w =>
            {
                double letterPt = w.Letters.Count > 0 ? w.Letters.Average(l => l.PointSize > 0 ? l.PointSize : 10.5) : 10.5;
                double rawEstPt = EstimateWordPointSizeGeneral(w.Text, w.BoundingBox.Height, letterPt, isOcrPage);
                string font = CleanFontName(w.FontName, isOcrPage);
                bool bold = (w.FontName != null && (w.FontName.Contains("Bold", StringComparison.OrdinalIgnoreCase) || w.FontName.Contains("Black", StringComparison.OrdinalIgnoreCase)))
                            || (isOcrPage && IsWordBoldInBitmap(w, pageBitmap!, pageWidthPt, pageHeightPt));
                bool italic = w.FontName != null && (w.FontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) || w.FontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase));
                string color = ExtractWordHexColor(w, pageBitmap, pageWidthPt, pageHeightPt);
                double baseline = w.Letters.Count > 0 ? w.Letters.Average(l => l.StartBaseLine.Y) : w.BoundingBox.Bottom;

                bool isUnderline = false;
                bool isStrike = false;
                if (decorationRects != null && decorationRects.Count > 0 && w.BoundingBox.Width >= 3.0)
                {
                    double midY = (w.BoundingBox.Bottom + w.BoundingBox.Top) * 0.5;
                    isUnderline = decorationRects.Any(r =>
                        r.Left <= w.BoundingBox.Left + 2.0 && r.Right >= w.BoundingBox.Right - 2.0
                        && Math.Abs((r.Top + r.Bottom) * 0.5 - baseline) <= Math.Max(2.2, rawEstPt * 0.22));
                    isStrike = !isUnderline && decorationRects.Any(r =>
                        r.Left <= w.BoundingBox.Left + 2.0 && r.Right >= w.BoundingBox.Right - 2.0
                        && Math.Abs((r.Top + r.Bottom) * 0.5 - midY) <= Math.Max(1.8, rawEstPt * 0.16));
                }

                string? bgHex = null;
                if (inlinePills != null && inlinePills.Count > 0)
                {
                    double wMidX = (w.BoundingBox.Left + w.BoundingBox.Right) * 0.5;
                    double wMidY = (w.BoundingBox.Bottom + w.BoundingBox.Top) * 0.5;
                    var matchedPill = inlinePills.LastOrDefault(p =>
                        wMidX >= p.Bounds.Left - 2.0 && wMidX <= p.Bounds.Right + 2.0
                        && wMidY >= p.Bounds.Bottom - 2.0 && wMidY <= p.Bounds.Top + 2.0);
                    if (!string.IsNullOrEmpty(matchedPill.FillHex))
                    {
                        bgHex = matchedPill.FillHex;
                    }
                }
                bool isHighlighted = !string.IsNullOrEmpty(bgHex);

                string cleanedText = isOcrPage
                    ? CleanOcrTokenGeneral(w.Text, w.BoundingBox.Bottom, w.BoundingBox.Right, pageWidthPt, pageNo, totalPages, warnings)
                    : w.Text;

                var gm = !isOcrPage ? GluedListMarkerWordRegex.Match(cleanedText) : Match.Empty;
                // Only split glued list markers when the remainder does NOT start with a digit (so numbers like "1.234" are never split!)
                if (gm.Success && w.Letters.Count > gm.Groups[1].Value.Length
                    && !char.IsDigit(gm.Groups[2].Value.TrimStart().FirstOrDefault()))
                {
                    string markerStr = gm.Groups[1].Value;
                    string prefixLetters = string.Concat(w.Letters.Take(markerStr.Length).Select(l => l.Value));
                    if (prefixLetters == markerStr)
                    {
                        var markerLet = w.Letters.Take(markerStr.Length).ToList();
                        var restLet = w.Letters.Skip(markerStr.Length).SkipWhile(l => string.IsNullOrWhiteSpace(l.Value)).ToList();
                        double mRight = markerLet.Max(l => l.GlyphRectangle.Right);
                        double rLeft = restLet.Count > 0 ? restLet.Min(l => l.GlyphRectangle.Left) : mRight + 4.0;
                        if (rLeft >= mRight - 0.5 && !rawWords.Any(other => !ReferenceEquals(other, w)
                            && Math.Abs(other.BoundingBox.Bottom - w.BoundingBox.Bottom) <= 4.0
                            && other.BoundingBox.Left < w.BoundingBox.Left - 1.0))
                        {
                            return new[]
                            {
                                new RichWord(markerStr, w.BoundingBox.Left, mRight, w.BoundingBox.Bottom, w.BoundingBox.Top,
                                    rawEstPt, font, color, bold, italic, baseline, isUnderline, isStrike, isHighlighted) { BackgroundFillHex = bgHex },
                                new RichWord(gm.Groups[2].Value.TrimStart(), Math.Max(mRight + 1.0, rLeft), w.BoundingBox.Right,
                                    w.BoundingBox.Bottom, w.BoundingBox.Top, rawEstPt, font, color, bold, italic, baseline, isUnderline, isStrike, isHighlighted) { BackgroundFillHex = bgHex }
                            };
                        }
                    }
                }

                return new[]
                {
                    new RichWord(
                        cleanedText,
                        w.BoundingBox.Left,
                        w.BoundingBox.Right,
                        w.BoundingBox.Bottom,
                        w.BoundingBox.Top,
                        rawEstPt,
                        font,
                        color,
                        bold,
                        italic,
                        baseline,
                        isUnderline,
                        isStrike,
                        isHighlighted)
                    {
                        BackgroundFillHex = bgHex
                    }
                };
            })
            .Where(rw => !string.IsNullOrWhiteSpace(rw.Text))
            .ToList();

            return BuildVisualLinesFromRichWords(initialWords, pageWidthPt, pageHeightPt, isOcrPage, pageNo, totalPages);
        }

        private static List<VisualLine> BuildVisualLinesFromRichWords(
            List<RichWord> initialWords,
            double pageWidthPt,
            double pageHeightPt,
            bool isOcrPage,
            int pageNo,
            int totalPages)
        {
            var wordsList = initialWords
                .Where(rw => !string.IsNullOrWhiteSpace(rw.Text))
                .ToList();

            // Attach inline superscript/subscript tokens (e.g., footnote numbers 1..28 at 8pt raised by 3.5..6.5pt) to their adjacent host word's baseline
            for (int i = 0; i < wordsList.Count; i++)
            {
                var cand = wordsList[i];
                if (cand.Text.Length > 4 || cand.FontSizePt > 9.5) continue;
                RichWord? host = null;
                double bestDistX = double.MaxValue;
                for (int j = 0; j < wordsList.Count; j++)
                {
                    if (i == j) continue;
                    var other = wordsList[j];
                    if (other.FontSizePt < cand.FontSizePt * 1.18) continue;
                    double dy = cand.Bottom - other.Bottom;
                    if (dy < -other.FontSizePt * 0.45 || dy > other.FontSizePt * 0.65) continue;
                    double dx = cand.Left >= other.Right - 2.0
                        ? cand.Left - other.Right
                        : (other.Left >= cand.Right - 2.0 ? other.Left - cand.Right : 0.0);
                    if (dx <= Math.Max(8.0, other.FontSizePt * 0.75) && dx < bestDistX)
                    {
                        bestDistX = dx;
                        host = other;
                    }
                }
                if (host != null)
                {
                    double dy = cand.Bottom - host.Bottom;
                    string? vAlign = dy >= host.FontSizePt * 0.18
                        ? "superscript"
                        : (dy <= -host.FontSizePt * 0.15 ? "subscript" : null);
                    if (vAlign != null)
                    {
                        wordsList[i] = cand with
                        {
                            Bottom = host.Bottom,
                            Top = host.Top,
                            Baseline = host.Baseline,
                            FontSizePt = host.FontSizePt,
                            VerticalAlign = vAlign
                        };
                    }
                }
            }

            var sorted = wordsList
                .OrderByDescending(w => isOcrPage ? (w.Bottom + w.Top) * 0.5 : w.Bottom)
                .ThenBy(w => w.Left)
                .ToList();

            var result = new List<VisualLine>();
            var current = new List<RichWord>();
            double currentY = double.NaN;
            const double yTol = 4.0;

            foreach (var rw in sorted)
            {
                bool sameLine;
                if (!isOcrPage)
                {
                    sameLine = double.IsNaN(currentY) || Math.Abs(rw.Bottom - currentY) <= yTol;
                }
                else if (current.Count == 0)
                {
                    sameLine = true;
                }
                else
                {
                    double rwMidY = (rw.Bottom + rw.Top) * 0.5;
                    double curMidY = current.Average(c => (c.Bottom + c.Top) * 0.5);
                    double avgH = current.Average(c => Math.Max(4.0, c.Top - c.Bottom));
                    bool vMatch = Math.Abs(rw.Bottom - currentY) <= yTol
                        || Math.Abs(rwMidY - curMidY) <= Math.Max(5.2, avgH * 0.52);
                    bool hOverlap = current.Any(c => Math.Min(rw.Right, c.Right) - Math.Max(rw.Left, c.Left) > 2.5);
                    sameLine = vMatch && !hOverlap;
                }

                if (sameLine)
                {
                    current.Add(rw);
                    if (double.IsNaN(currentY)) currentY = rw.Bottom;
                }
                else
                {
                    result.Add(FinalizeVisualLine(current, pageWidthPt, pageHeightPt, isOcrPage, pageNo, totalPages));
                    current = new List<RichWord> { rw };
                    currentY = rw.Bottom;
                }
            }
            if (current.Count > 0)
            {
                result.Add(FinalizeVisualLine(current, pageWidthPt, pageHeightPt, isOcrPage, pageNo, totalPages));
            }

            if (isOcrPage && result.Count >= 3)
            {
                var bodySizeCandidates = result
                    .Where(l => l.Words.Count >= 3)
                    .Select(l => l.FontSizePt)
                    .OrderBy(x => x)
                    .ToList();
                if (bodySizeCandidates.Count >= 2)
                {
                    double medBodyPt = bodySizeCandidates[bodySizeCandidates.Count / 2];
                    double snappedBodyPt = medBodyPt >= 11.0 && medBodyPt <= 13.6
                        ? 12.0
                        : (medBodyPt >= 10.3 && medBodyPt < 11.0
                            ? 11.0
                            : (medBodyPt >= 9.2 && medBodyPt < 10.3 ? 10.0 : Math.Round(medBodyPt)));

                    for (int i = 0; i < result.Count; i++)
                    {
                        var vLine = result[i];
                        if (Math.Abs(vLine.FontSizePt - snappedBodyPt) <= 2.2)
                        {
                            var normWords = vLine.Words
                                .Select(w => w.VerticalAlign != null ? w : (w with { FontSizePt = snappedBodyPt }))
                                .ToList();
                            var normSegs = vLine.Segments
                                .Select(s => s with
                                {
                                    Words = s.Words.Select(w => w.VerticalAlign != null ? w : (w with { FontSizePt = snappedBodyPt })).ToList()
                                })
                                .ToList();
                            result[i] = vLine with
                            {
                                FontSizePt = snappedBodyPt,
                                Words = normWords,
                                Segments = normSegs
                            };
                        }
                    }
                }
            }

            return result;
        }

        private static double EstimateWordPointSizeGeneral(string text, double bboxHeightPt, double pdfPointSize, bool isOcrPage)
        {
            if (!isOcrPage)
            {
                // Pada PDF Digital (seperti Chrome Print), jika PointSize ==Height bbox penuh, skalakan ke ukuran poin tipografi standar
                double basePt = pdfPointSize > 1.0 ? pdfPointSize : bboxHeightPt;
                return Math.Clamp(Math.Round(basePt * 2.0) / 2.0, 6.0, 72.0);
            }

            bool hasAscenderOrCap = false;
            bool hasDescender = false;
            foreach (char c in text)
            {
                if (char.IsUpper(c) || char.IsDigit(c) || "bdfhklt?!()/\\[]{}".IndexOf(c) >= 0)
                    hasAscenderOrCap = true;
                if ("gjpqy,;()<>".IndexOf(c) >= 0)
                    hasDescender = true;
            }

            double estimatedCapPt;
            if (hasAscenderOrCap && !hasDescender)
            {
                estimatedCapPt = bboxHeightPt * 0.96;
            }
            else if (hasAscenderOrCap && hasDescender)
            {
                estimatedCapPt = bboxHeightPt * 0.78;
            }
            else if (!hasAscenderOrCap && hasDescender)
            {
                estimatedCapPt = bboxHeightPt * 0.92;
            }
            else
            {
                estimatedCapPt = bboxHeightPt * 1.28;
            }

            return Math.Clamp(Math.Round(estimatedCapPt * 2.0) / 2.0, 6.0, 72.0);
        }

        private static double CalibrateUniformLineFontSizePt(List<RichWord> lineWords, bool isOcrPage)
        {
            if (lineWords.Count == 0) return 10.0;
            if (!isOcrPage)
            {
                var pdfSizes = lineWords.Select(w => w.FontSizePt).OrderBy(x => x).ToList();
                return Math.Clamp(Math.Round(pdfSizes[pdfSizes.Count / 2] * 2.0) / 2.0, 6.0, 72.0);
            }

            var capWords = lineWords
                .Where(w => w.Text.Any(c => char.IsUpper(c) || char.IsDigit(c) || "bdfhklt".IndexOf(c) >= 0))
                .Select(w => w.FontSizePt)
                .OrderBy(x => x)
                .ToList();

            var pool = capWords.Count > 0
                ? capWords
                : lineWords.Select(w => w.FontSizePt).OrderBy(x => x).ToList();

            double medianPt = pool[pool.Count / 2];
            return Math.Clamp(Math.Round(medianPt * 2.0) / 2.0, 6.0, 72.0);
        }

        private static VisualLine FinalizeVisualLine(
            List<RichWord> words,
            double pageWidthPt,
            double pageHeightPt,
            bool isOcrPage,
            int pageNo,
            int totalPages)
        {
            var ordered = words.OrderBy(w => w.Left).ToList();
            double lineBottom = ordered.Average(w => w.Bottom);
            double lineTop = ordered.Max(w => w.Top);

            double uniformLinePt = CalibrateUniformLineFontSizePt(ordered, isOcrPage);
            if (isOcrPage)
            {
                ordered = ordered.Select(w => w with { FontSizePt = uniformLinePt }).ToList();
            }

            // Rekonstruksi URL yang terpecah pada baris footer (berlaku untuk OCR maupun Digital jika URL memiliki token nomor halaman di kanan)
            if (UriSchemeRegex.IsMatch(ordered[0].Text) && ordered.Count > 1)
            {
                var mergedList = new List<RichWord>();
                var urlBuilder = new StringBuilder( isOcrPage ? NormalizeUriSchemePrefix(ordered[0].Text) : ordered[0].Text );
                double urlRight = ordered[0].Right;
                int i = 1;

                while (i < ordered.Count)
                {
                    var candidate = ordered[i];
                    double gap = candidate.Left - urlRight;

                    bool isRightPageNum = PageFractionRegex.IsMatch(candidate.Text)
                        || candidate.Text == $"{pageNo}/{totalPages}"
                        || (Regex.IsMatch(candidate.Text, @"^\d+$") && candidate.Left > pageWidthPt * 0.85);

                    if (isRightPageNum || gap > 12.0)
                        break;

                    string part = candidate.Text.Trim();
                    urlBuilder.Append(part);

                    urlRight = candidate.Right;
                    i++;
                }

                string finalUrl = urlBuilder.ToString();

                var first = ordered[0];
                mergedList.Add(first with { Text = finalUrl, Right = urlRight });
                for (; i < ordered.Count; i++)
                {
                    var remWord = ordered[i];
                    mergedList.Add(remWord);
                }
                ordered = mergedList;
            }

            // Rejoin split numeric tokens where PDF/OCR inserted a small space after a thousands/decimal separator (e.g., "1." + "234,5678")
            if (ordered.Count >= 2)
            {
                var mergedNums = new List<RichWord>(ordered.Count);
                int idx = 0;
                while (idx < ordered.Count)
                {
                    var w = ordered[idx];
                    if (idx + 1 < ordered.Count
                        && Regex.IsMatch(w.Text, @"^[+-]?\d{1,3}[.,]$")
                        && Regex.IsMatch(ordered[idx + 1].Text, @"^\d{3}(?:[.,]\d+)*$")
                        && (ordered[idx + 1].Left - w.Right) <= Math.Max(4.5, w.FontSizePt * 0.55))
                    {
                        var nextW = ordered[idx + 1];
                        mergedNums.Add(w with
                        {
                            Text = w.Text + nextW.Text,
                            Right = nextW.Right,
                            Bottom = Math.Min(w.Bottom, nextW.Bottom),
                            Top = Math.Max(w.Top, nextW.Top)
                        });
                        idx += 2;
                        continue;
                    }
                    mergedNums.Add(w);
                    idx++;
                }
                ordered = mergedNums;
            }

            // Normalize OCR numbered list token where scanner lost the period after a leading single digit (e.g. "2 Peraturan Pemerintah...")
            if (isOcrPage && ordered.Count >= 4
                && Regex.IsMatch(ordered[0].Text, @"^[1-9]$")
                && char.IsUpper(ordered[1].Text[0])
                && ordered[1].Text.Length >= 4
                && (ordered[1].Left - ordered[0].Right) >= 6.0
                && ordered[0].Left >= pageWidthPt * 0.14)
            {
                ordered[0] = ordered[0] with { Text = ordered[0].Text + "." };
            }

            var segments = new List<WordSegment>();
            var curWords = new List<RichWord> { ordered[0] };
            double prevRight = ordered[0].Right;

            bool isShortCenteredHeadingLine = ordered.Count <= 6
                && ordered[0].Left >= pageWidthPt * 0.27
                && ordered[^1].Right <= pageWidthPt * 0.73
                && Math.Abs((ordered[0].Left + ordered[^1].Right) * 0.5 - pageWidthPt * 0.5) <= 28.0;

            for (int i = 1; i < ordered.Count; i++)
            {
                var w = ordered[i];
                double gap = w.Left - prevRight;

                // PERBAIKAN PENTING: Jika kata terakhir di kanan adalah nomor halaman (misal "1/2" atau "2/2") dan baris dimulai dengan URL/Footer,
                // selalu pisahkan menjadi segmen tersendiri walaupun jaraknya < 16pt agar otomatis diberi Right Tab-Stop!
                bool isTrailingPageNumber = (i == ordered.Count - 1)
                    && PageFractionRegex.IsMatch(w.Text)
                    && w.Right > pageWidthPt * 0.82
                    && gap >= 4.0;

                bool reachesRightEdge = ordered[^1].Right >= pageWidthPt * 0.74 && ordered.Count >= 4;
                bool curIsOnlyColonOrMarker = (curWords.Count == 1 && (curWords[0].Text == ":" || Regex.IsMatch(curWords[0].Text, @"^(?:[a-zA-Z0-9]{1,3}[.)])$")))
                    || (curWords.Count == 2 && curWords[0].Text == ":" && Regex.IsMatch(curWords[1].Text, @"^(?:[a-zA-Z0-9]{1,3}[.)])$"));
                bool curIsLegalKeywordPrefix = segments.Count == 0
                    && curWords.Count == 1
                    && Regex.IsMatch(curWords[0].Text, @"^(?:Menimbang|Mengingat|Menetapkan|KESATU|KEDUA|KETIGA|KEEMPAT|KELIMA|KEENAM|KETUJUH|PERTAMA):?$");

                bool isAfterStructuralPrefix = prevRight < pageWidthPt * 0.42
                    && gap >= 4.5
                    && ((segments.Count >= 1 && curIsOnlyColonOrMarker)
                        || (curIsLegalKeywordPrefix && (w.Text == ":" || gap >= 10.0))
                        || (segments.Count == 0 && curWords.Count == 2 && curWords[1].Text == ":" && Regex.IsMatch(curWords[0].Text, @"^(?:Menimbang|Mengingat|Menetapkan|KESATU|KEDUA|KETIGA|KEEMPAT|KELIMA|KEENAM|KETUJUH|PERTAMA)$")));

                double maxProseGap = (ordered[0].Left >= pageWidthPt * 0.18 && !curIsOnlyColonOrMarker && !curIsLegalKeywordPrefix)
                    ? 60.0
                    : 34.0;

                bool insideProseClause = !isAfterStructuralPrefix
                    && reachesRightEdge
                    && gap < maxProseGap
                    && !curIsOnlyColonOrMarker
                    && !curIsLegalKeywordPrefix
                    && (curWords.Count >= 2
                        || curWords[0].Left >= pageWidthPt * 0.18
                        || (segments.Count >= 1 && curWords[0].Text != ":" && !Regex.IsMatch(curWords[0].Text, @"^(?:[a-zA-Z0-9]{1,3}[.)]|:)$")));

                bool insideShortCenteredLine = isShortCenteredHeadingLine && gap < 38.0;

                if (((gap >= 16.0 || isAfterStructuralPrefix) && !insideProseClause && !insideShortCenteredLine) || isTrailingPageNumber)
                {
                    segments.Add(new WordSegment(
                        curWords.ToList(),
                        curWords.Min(x => x.Left),
                        prevRight,
                        curWords.Min(x => x.Bottom),
                        curWords.Max(x => x.Top)));
                    curWords.Clear();
                }
                curWords.Add(w);
                prevRight = w.Right;
            }
            if (curWords.Count > 0)
            {
                segments.Add(new WordSegment(
                    curWords.ToList(),
                    curWords.Min(x => x.Left),
                    prevRight,
                    curWords.Min(x => x.Bottom),
                    curWords.Max(x => x.Top)));
            }

            return new VisualLine(
                ordered,
                ordered.Min(w => w.Left),
                ordered.Max(w => w.Right),
                lineBottom,
                lineTop,
                uniformLinePt,
                ordered.Count(w => w.IsBold) > ordered.Count / 2,
                segments);
        }

        private static string NormalizeUriSchemePrefix(string text)
        {
            if (text.StartsWith("https:/", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return "https://" + text.Substring("https:/".Length);
            if (text.StartsWith("http:/", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                return "http://" + text.Substring("http:/".Length);
            return text;
        }

        private sealed record TaggedWordArt(
            string Text,
            List<UglyToad.PdfPig.Graphics.PdfPath> Paths,
            PdfRectangle Bounds,
            UglyToad.PdfPig.Graphics.PdfPath Ink,
            IReadOnlyList<UglyToad.PdfPig.Graphics.PdfPath>? InkGlyphs = null,
            string FontFamily = "Arial",
            UglyToad.PdfPig.Graphics.Colors.IColor? ShadowColor = null,
            double ShadowOffsetXPt = 0,
            double ShadowOffsetYPt = 0,
            bool IsBold = false,
            bool IsItalic = false);

        private sealed record PdfPatternShadingInfo(
            string Name,
            PdfRectangle? CoordsBox,
            (double R, double G, double B) AverageRgb,
            string HexColor);

        private sealed record VerticalRotatedTextItem(
            string Text,
            PdfRectangle Bounds,
            string FontFamily,
            double FontSizePt,
            bool IsBold,
            string FillHex,
            bool BottomToTop);

        private sealed record ClippedVectorPathItem(
            UglyToad.PdfPig.Graphics.PdfPath Path,
            PdfRectangle RawBounds,
            PdfRectangle EffectiveBounds,
            PdfRectangle? ClipRect);

        private static List<IReadOnlyList<RichWord>> SplitOcrLineIntoPositionedChunks(
            VisualLine ocrLine,
            IReadOnlyList<PdfRectangle> verticalGridRules)
        {
            var chunks = new List<IReadOnlyList<RichWord>>();
            if (ocrLine.Words.Count == 0) return chunks;
            var current = new List<RichWord> { ocrLine.Words[0] };
            for (int i = 1; i < ocrLine.Words.Count; i++)
            {
                var prev = ocrLine.Words[i - 1];
                var cur = ocrLine.Words[i];
                double gap = cur.Left - prev.Right;
                double lineMidY = (ocrLine.Bottom + ocrLine.Top) * 0.5;
                bool crossedVerticalGrid = verticalGridRules.Any(r =>
                    r.Bottom <= lineMidY + 4.0 && r.Top >= lineMidY - 4.0
                    && (r.Left + r.Right) * 0.5 >= prev.Right - 2.0
                    && (r.Left + r.Right) * 0.5 <= cur.Left + 2.0);
                if (crossedVerticalGrid || gap >= Math.Max(10.0, ocrLine.FontSizePt * 0.95))
                {
                    chunks.Add(current);
                    current = new List<RichWord>();
                }
                current.Add(cur);
            }
            if (current.Count > 0) chunks.Add(current);
            return chunks;
        }

        private static bool TryGetColorRgb(UglyToad.PdfPig.Graphics.Colors.IColor? color, out (double R, double G, double B) rgb)
        {
            rgb = (0d, 0d, 0d);
            if (color == null) return false;
            try
            {
                rgb = color.ToRGBValues();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static (double R, double G, double B) SafeToRgbValues(UglyToad.PdfPig.Graphics.Colors.IColor? color)
            => TryGetColorRgb(color, out var rgb) ? rgb : (0d, 0d, 0d);

        private static List<PdfPatternShadingInfo> ExtractPagePatternShadings(PdfDocument pdf, Page page)
        {
            var result = new List<PdfPatternShadingInfo>();
            try
            {
                IToken Resolve(IToken token) => token is IndirectReferenceToken r ? pdf.Structure.GetObject(r.Data).Data : token;
                if (!page.Dictionary.TryGet(NameToken.Create("Resources"), out var resTok) || Resolve(resTok) is not DictionaryToken resDict)
                    return result;
                if (!resDict.TryGet(NameToken.Create("Pattern"), out var patTok) || Resolve(patTok) is not DictionaryToken patDict)
                    return result;

                foreach (var kv in patDict.Data)
                {
                    string patName = kv.Key?.ToString() ?? string.Empty;
                    var visited = new HashSet<IndirectReference>();
                    PdfRectangle? coordsBox = null;
                    var rgbSamples = new List<(double R, double G, double B)>();

                    void Walk(IToken tok, int depth)
                    {
                        if (depth > 20 || (tok is IndirectReferenceToken r && !visited.Add(r.Data))) return;
                        tok = Resolve(tok);
                        if (tok is DictionaryToken d)
                        {
                            if (d.TryGet(NameToken.Create("Coords"), out var cTok) && Resolve(cTok) is ArrayToken cArr && cArr.Data.Count >= 4)
                            {
                                var nums = cArr.Data.Select(Resolve).OfType<NumericToken>().Select(n => n.Double).ToList();
                                if (nums.Count >= 4)
                                {
                                    coordsBox = new PdfRectangle(
                                        Math.Min(nums[0], nums[2]),
                                        Math.Min(nums[1], nums[3]),
                                        Math.Max(nums[0], nums[2]),
                                        Math.Max(nums[1], nums[3]));
                                }
                            }
                            foreach (string ck in new[] { "C0", "C1" })
                            {
                                if (d.TryGet(NameToken.Create(ck), out var colTok) && Resolve(colTok) is ArrayToken colArr && colArr.Data.Count >= 3)
                                {
                                    var cNums = colArr.Data.Select(Resolve).OfType<NumericToken>().Select(n => n.Double).ToList();
                                    if (cNums.Count >= 3)
                                        rgbSamples.Add((Math.Clamp(cNums[0], 0, 1), Math.Clamp(cNums[1], 0, 1), Math.Clamp(cNums[2], 0, 1)));
                                }
                            }
                            foreach (var child in d.Data.Values) Walk(child, depth + 1);
                        }
                        else if (tok is ArrayToken arr)
                        {
                            foreach (var item in arr.Data) Walk(item, depth + 1);
                        }
                    }

                    Walk(kv.Value, 0);
                    if (rgbSamples.Count > 0)
                    {
                        double r = rgbSamples.Average(x => x.R);
                        double g = rgbSamples.Average(x => x.G);
                        double b = rgbSamples.Average(x => x.B);
                        string hex = $"#{(int)Math.Round(r * 255):X2}{(int)Math.Round(g * 255):X2}{(int)Math.Round(b * 255):X2}";
                        result.Add(new PdfPatternShadingInfo(patName, coordsBox, (r, g, b), hex));
                    }
                }
            }
            catch { }
            return result;
        }

        private static string? ResolvePdfColorHex(
            UglyToad.PdfPig.Graphics.Colors.IColor? color,
            PdfRectangle? pathBounds,
            IReadOnlyList<PdfPatternShadingInfo>? pagePatterns)
        {
            if (color == null) return "#000000";
            if (TryGetColorRgb(color, out var rgb))
            {
                return $"#{(int)Math.Round(rgb.R * 255):X2}{(int)Math.Round(rgb.G * 255):X2}{(int)Math.Round(rgb.B * 255):X2}";
            }
            if (pagePatterns != null && pagePatterns.Count > 0)
            {
                if (pathBounds.HasValue)
                {
                    var b = pathBounds.Value;
                    var insideMatch = pagePatterns
                        .Where(p => p.CoordsBox.HasValue)
                        .Select(p =>
                        {
                            var cb = p.CoordsBox!.Value;
                            double cx = (cb.Left + cb.Right) * 0.5;
                            double cy = (cb.Bottom + cb.Top) * 0.5;
                            double dx = cx < b.Left ? b.Left - cx : (cx > b.Right ? cx - b.Right : 0);
                            double dy = cy < b.Bottom ? b.Bottom - cy : (cy > b.Top ? cy - b.Top : 0);
                            return (Pattern: p, Dist: Math.Sqrt(dx * dx + dy * dy));
                        })
                        .OrderBy(x => x.Dist)
                        .FirstOrDefault();
                    if (insideMatch.Pattern != null && insideMatch.Dist <= 24.0)
                    {
                        return insideMatch.Pattern.HexColor;
                    }
                }
                return pagePatterns[0].HexColor;
            }
            return null;
        }

        private static bool AreSameBoxBounds(PdfRectangle a, PdfRectangle b, double tol = 0.8)
            => Math.Abs(a.Left - b.Left) <= tol
            && Math.Abs(a.Right - b.Right) <= tol
            && Math.Abs(a.Bottom - b.Bottom) <= tol
            && Math.Abs(a.Top - b.Top) <= tol;

        private static int CountPairedFilledStrokedShapes(IReadOnlyList<ClippedVectorPathItem> items)
        {
            int count = 0;
            for (int i = 0; i + 1 < items.Count; i++)
            {
                if (items[i].Path.IsFilled && !items[i].Path.IsStroked
                    && !items[i + 1].Path.IsFilled && items[i + 1].Path.IsStroked
                    && items[i].EffectiveBounds.Width >= 8.0 && items[i].EffectiveBounds.Height >= 8.0
                    && AreSameBoxBounds(items[i].EffectiveBounds, items[i + 1].EffectiveBounds, 0.8))
                {
                    count++;
                    i++;
                }
            }
            return count;
        }

        private static List<VerticalRotatedTextItem> ExtractVerticalRotatedTextShapes(
            ref List<Word> rawWords,
            Page page,
            IReadOnlyList<PdfPatternShadingInfo> pagePatterns)
        {
            var result = new List<VerticalRotatedTextItem>();
            if (rawWords.Count == 0) return result;

            static bool IsVerticalWord(Word w)
            {
                if (w.Letters.Count == 0) return false;
                var l0 = w.Letters[0];
                var lN = w.Letters[^1];
                double dx = Math.Abs(lN.EndBaseLine.X - l0.StartBaseLine.X);
                double dy = Math.Abs(lN.EndBaseLine.Y - l0.StartBaseLine.Y);
                if (dy > dx * 2.0 && dy >= 4.0) return true;
                double ldx = Math.Abs(l0.EndBaseLine.X - l0.StartBaseLine.X);
                double ldy = Math.Abs(l0.EndBaseLine.Y - l0.StartBaseLine.Y);
                return ldy > ldx * 2.0 && ldy >= 2.0;
            }

            var vertWords = rawWords.Where(IsVerticalWord).ToList();
            if (vertWords.Count == 0) return result;
            rawWords = rawWords.Where(w => !IsVerticalWord(w)).ToList();

            var groups = new List<List<Word>>();
            foreach (var vw in vertWords.OrderBy(w => w.Letters.Average(l => l.StartBaseLine.X)).ThenBy(w => w.Letters.Min(l => Math.Min(l.StartBaseLine.Y, l.EndBaseLine.Y))))
            {
                double baseX = vw.Letters.Average(l => l.StartBaseLine.X);
                var existing = groups.FirstOrDefault(g =>
                    Math.Abs(g.Average(x => x.Letters.Average(l => l.StartBaseLine.X)) - baseX) <= 8.0);
                if (existing != null) existing.Add(vw);
                else groups.Add(new List<Word> { vw });
            }

            foreach (var grp in groups)
            {
                var firstLet = grp[0].Letters[0];
                bool bottomToTop = firstLet.EndBaseLine.Y >= firstLet.StartBaseLine.Y;
                var orderedWords = bottomToTop
                    ? grp.OrderBy(w => w.Letters.Min(l => Math.Min(l.StartBaseLine.Y, l.EndBaseLine.Y))).ToList()
                    : grp.OrderByDescending(w => w.Letters.Max(l => Math.Max(l.StartBaseLine.Y, l.EndBaseLine.Y))).ToList();

                string fullText = string.Join(" ", orderedWords.Select(w => w.Text.Trim()).Where(t => t.Length > 0));
                if (string.IsNullOrWhiteSpace(fullText)) continue;

                var allLetters = orderedWords.SelectMany(w => w.Letters).ToList();
                var allPts = allLetters.SelectMany(l => new[]
                {
                    l.GlyphRectangle.BottomLeft,
                    l.GlyphRectangle.BottomRight,
                    l.GlyphRectangle.TopLeft,
                    l.GlyphRectangle.TopRight,
                    l.StartBaseLine,
                    l.EndBaseLine
                }).ToList();

                double minX = allPts.Min(p => p.X);
                double maxX = allPts.Max(p => p.X);
                double minY = allPts.Min(p => p.Y);
                double maxY = allPts.Max(p => p.Y);
                double ptSize = allLetters.Average(l => l.PointSize > 0 ? l.PointSize : 12.0);
                double baseX = allLetters.Average(l => l.StartBaseLine.X);
                if (maxX - minX < ptSize * 0.75)
                {
                    minX = Math.Min(minX, baseX - ptSize * 0.80);
                    maxX = Math.Max(maxX, baseX + ptSize * 0.15);
                }

                var (r, g, b) = SafeToRgbValues(firstLet.Color);
                string fillHex = $"#{(int)Math.Round(r * 255):X2}{(int)Math.Round(g * 255):X2}{(int)Math.Round(b * 255):X2}";
                string? fontName = orderedWords[0].FontName;
                string font = CleanFontName(fontName, false);
                bool bold = fontName != null && (fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase) || fontName.Contains("Black", StringComparison.OrdinalIgnoreCase));

                result.Add(new VerticalRotatedTextItem(
                    fullText,
                    new PdfRectangle(minX, minY, maxX, maxY),
                    string.IsNullOrWhiteSpace(font) ? "Calibri" : font,
                    ptSize,
                    bold,
                    fillHex,
                    bottomToTop));
            }

            return result;
        }

        private static DocumentFormat.OpenXml.Vml.Shape BuildVerticalRotatedWordArtShape(VerticalRotatedTextItem item, double pageHeight, uint id)
        {
            var b = item.Bounds;
            string vmlPath = item.BottomToTop
                ? "m0,21600l0,0m21600,21600l21600,0e"
                : "m21600,0l21600,21600m0,0l0,21600e";
            string weightStyle = item.IsBold ? ";font-weight:bold" : string.Empty;
            return new DocumentFormat.OpenXml.Vml.Shape(
                new DocumentFormat.OpenXml.Vml.Path { Value = vmlPath, AllowTextPath = true },
                new DocumentFormat.OpenXml.Vml.TextPath
                {
                    On = true,
                    FitShape = true,
                    Trim = true,
                    String = item.Text,
                    Style = $"font-family:{item.FontFamily}{weightStyle};v-text-kern:t"
                },
                new DocumentFormat.OpenXml.Vml.Wordprocessing.TextWrap { Type = DocumentFormat.OpenXml.Vml.Wordprocessing.WrapValues.None })
            {
                Id = $"VertText_{id}",
                CoordinateSize = "21600,21600",
                Filled = true,
                Stroked = false,
                FillColor = item.FillHex,
                Style = FormattableString.Invariant($"position:absolute;margin-left:{b.Left:0.###}pt;margin-top:{pageHeight - b.Top:0.###}pt;width:{Math.Max(6.0, b.Width):0.###}pt;height:{Math.Max(12.0, b.Height):0.###}pt;z-index:1;mso-position-horizontal-relative:page;mso-position-vertical-relative:page")
            };
        }

        private static (string FontFamily, bool IsBold, bool IsItalic) InferWordArtTypography(
            string text,
            IReadOnlyList<UglyToad.PdfPig.Graphics.PdfPath> ink,
            PdfRectangle bounds,
            bool hasShadowLayer,
            string fallbackFont)
        {
            string font = string.IsNullOrWhiteSpace(fallbackFont) || fallbackFont == "Calibri" ? "Arial" : fallbackFont;
            if (ink.Count == 0 || bounds.Width <= 0 || bounds.Height <= 0)
                return (font, false, false);

            int verticalStems = 0;
            var botStemRatios = new List<double>();
            double maxHorizToHeight = 0;

            foreach (var glyphPath in ink)
            {
                var subRects = glyphPath.Select(s => s.GetBoundingRectangle()).Where(b => b.HasValue).Select(b => b!.Value).ToList();
                if (subRects.Count == 0) continue;
                double gLeft = subRects.Min(b => b.Left);
                double gRight = subRects.Max(b => b.Right);
                double gBottom = subRects.Min(b => b.Bottom);
                double gTop = subRects.Max(b => b.Top);
                double gw = gRight - gLeft;
                double gh = gTop - gBottom;
                if (gw < 1.0 || gh < 3.0) continue;

                double maxBotSeg = 0;
                foreach (var cmd in glyphPath.SelectMany(s => s.Commands))
                {
                    PdfPoint? p0 = null, p1 = null;
                    if (cmd is PdfSubpath.Line ln) { p0 = ln.From; p1 = ln.To; }
                    else if (cmd is PdfSubpath.CubicBezierCurve cb) { p0 = cb.StartPoint; p1 = cb.EndPoint; }
                    if (!p0.HasValue || !p1.HasValue) continue;

                    double dx = Math.Abs(p1.Value.X - p0.Value.X);
                    double dy = Math.Abs(p1.Value.Y - p0.Value.Y);
                    if (dx <= 0.05 && dy >= gh * 0.25)
                    {
                        verticalStems++;
                    }
                    if (dy <= 0.25 && Math.Abs(p0.Value.Y - gBottom) <= 0.35 && dx > 0.3)
                    {
                        if (dx < gw * 0.72)
                        {
                            maxBotSeg = Math.Max(maxBotSeg, dx / gw);
                        }
                        maxHorizToHeight = Math.Max(maxHorizToHeight, dx / gh);
                    }
                }
                if (maxBotSeg > 0)
                {
                    botStemRatios.Add(maxBotSeg);
                }
            }

            double avgBotRatio = botStemRatios.Count > 0 ? botStemRatios.Average() : 0;
            // Chancery / cursive script WordArt (e.g. Monotype Corsiva): zero vertical stems, pointed script terminals (low bottom stem ratio), and shadow layer
            if (verticalStems == 0 && ink.Count >= 5 && (hasShadowLayer || (avgBotRatio > 0 && avgBotRatio < 0.11)))
            {
                return ("Monotype Corsiva", false, false);
            }

            int nonSpaceChars = Math.Max(1, text.EnumerateRunes().Count(r => !Rune.IsWhiteSpace(r)));
            double charAspect = (bounds.Width / nonSpaceChars) / Math.Max(1.0, bounds.Height);
            // Wide classical serif display WordArt (e.g. Baskerville Old Face Bold): wide character aspect with fine bracketed serif base ratio
            if (charAspect > 1.2 && avgBotRatio >= 0.11 && avgBotRatio < 0.16)
            {
                return ("Baskerville Old Face", true, false);
            }

            // Heavy display sans-serif WordArt (e.g. Arial Black): wide bottom stem ratio >= 0.302
            if (avgBotRatio >= 0.302)
            {
                return ("Arial Black", false, false);
            }

            bool isBold = avgBotRatio >= 0.242 || maxHorizToHeight >= 0.25 || (charAspect < 0.46 && avgBotRatio >= 0.16);
            return (font, isBold, false);
        }

        private static List<TaggedWordArt> RecoverTaggedWordArt(PdfDocument pdf, Page page, string defaultFontFamily = "Arial")
        {
            var labels = new Dictionary<int, string>();
            var visited = new HashSet<IndirectReference>();
            IToken Resolve(IToken token) => token is IndirectReferenceToken reference ? pdf.Structure.GetObject(reference.Data).Data : token;
            void Read(IToken token, IToken? pageToken, string? label, int depth)
            {
                if (depth > 128 || (token is IndirectReferenceToken reference && !visited.Add(reference.Data))) return;
                token = Resolve(token);
                if (token is ArrayToken array) { foreach (var child in array.Data) Read(child, pageToken, label, depth + 1); return; }
                if (token is NumericToken number && pageToken != null && !string.IsNullOrWhiteSpace(label))
                {
                    if (Resolve(pageToken) is DictionaryToken taggedPage && taggedPage.TryGet(NameToken.Contents, out var contents)
                        && page.Dictionary.TryGet(NameToken.Contents, out var current) && contents.Equals(current)) labels[number.Int] = label.Trim();
                    return;
                }
                if (token is not DictionaryToken dictionary) return;
                if (dictionary.TryGet(NameToken.Create("Pg"), out var pg)) pageToken = pg;
                if (dictionary.TryGet(NameToken.Create("ActualText"), out var actual) && Resolve(actual) is StringToken actualString) label = actualString.Data;
                else if (dictionary.TryGet(NameToken.Create("Alt"), out var alt) && Resolve(alt) is StringToken altString) label = altString.Data;
                if (dictionary.TryGet(NameToken.Create("K"), out var kids)) Read(kids, pageToken, label, depth + 1);
            }
            if (pdf.Structure.Catalog.CatalogDictionary.TryGet(NameToken.Create("StructTreeRoot"), out var root)) Read(root, null, null, 0);
            var result = new List<TaggedWordArt>();
            var pending = new Queue<MarkedContentElement>(page.GetMarkedContents());
            string inferredFont = string.IsNullOrWhiteSpace(defaultFontFamily) || defaultFontFamily == "Calibri" || defaultFontFamily == "Segoe UI" ? "Arial" : defaultFontFamily;
            while (pending.TryDequeue(out var marked))
            {
                foreach (var child in marked.Children) pending.Enqueue(child);
                if (!labels.TryGetValue(marked.MarkedContentIdentifier, out var rawText) || marked.Images.Count > 0 || marked.Letters.Count > 0) continue;
                string text = rawText.Trim();
                int glyphs = text.EnumerateRunes().Count(r => !Rune.IsWhiteSpace(r));
                var paths = marked.Paths.Where(p => p.IsFilled || p.IsStroked).ToList();
                var matchingLayers = paths.Where(p => p.IsFilled && TryGetColorRgb(p.FillColor, out _))
                    .GroupBy(p => SafeToRgbValues(p.FillColor))
                    .Where(g => g.Count() == glyphs && glyphs > 0).ToList();
                var ink = matchingLayers.LastOrDefault()?.ToList();
                if (ink == null) continue;
                var boxes = ink.SelectMany(p => p.Select(s => s.GetBoundingRectangle())).Where(b => b.HasValue).Select(b => b!.Value).ToList();
                if (boxes.Count == 0) continue;
                var bounds = new PdfRectangle(boxes.Min(b => b.Left), boxes.Min(b => b.Bottom), boxes.Max(b => b.Right), boxes.Max(b => b.Top));
                if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.Left >= page.Width || bounds.Bottom >= page.Height || bounds.Right <= 0 || bounds.Top <= 0) continue;

                UglyToad.PdfPig.Graphics.Colors.IColor? shadowColor = null;
                double shadowDx = 0, shadowDy = 0;
                if (matchingLayers.Count >= 2)
                {
                    var shadowLayer = matchingLayers[^2].ToList();
                    var shBoxes = shadowLayer.SelectMany(p => p.Select(s => s.GetBoundingRectangle())).Where(b => b.HasValue).Select(b => b!.Value).ToList();
                    if (shBoxes.Count > 0)
                    {
                        var shBounds = new PdfRectangle(shBoxes.Min(b => b.Left), shBoxes.Min(b => b.Bottom), shBoxes.Max(b => b.Right), shBoxes.Max(b => b.Top));
                        shadowColor = shadowLayer[0].FillColor;
                        shadowDx = shBounds.Left - bounds.Left;
                        shadowDy = bounds.Top - shBounds.Top;
                    }
                }

                var (typoFont, isBold, isItalic) = InferWordArtTypography(text, ink, bounds, shadowColor != null, inferredFont);
                result.Add(new TaggedWordArt(text, paths, bounds, ink[0], ink, typoFont, shadowColor, shadowDx, shadowDy, isBold, isItalic));
            }
            return result;
        }

        private static string BuildWordArtTextPathGeometry(TaggedWordArt art)
        {
            const string straightPath = "m0,0l21600,0m0,21600l21600,21600e";
            if (art.InkGlyphs == null || art.InkGlyphs.Count < 5 || art.Bounds.Width < 24 || art.Bounds.Height < 8)
                return straightPath;

            var glyphRects = art.InkGlyphs
                .Select(p =>
                {
                    var subBoxes = p.Select(s => s.GetBoundingRectangle()).Where(b => b.HasValue).Select(b => b!.Value).ToList();
                    return subBoxes.Count == 0
                        ? (PdfRectangle?)null
                        : new PdfRectangle(subBoxes.Min(b => b.Left), subBoxes.Min(b => b.Bottom), subBoxes.Max(b => b.Right), subBoxes.Max(b => b.Top));
                })
                .Where(b => b.HasValue)
                .Select(b => b!.Value)
                .OrderBy(b => (b.Left + b.Right) * 0.5)
                .ToList();

            if (glyphRects.Count < 5) return straightPath;
            double maxGlyphH = glyphRects.Max(g => g.Height);
            // Filter out punctuation ('.', '-'), lowercase x-height letters, and descender-skewed glyphs so only full-height glyphs define the envelope
            var fullGlyphs = glyphRects.Where(g => g.Height >= maxGlyphH * 0.78).ToList();
            if (fullGlyphs.Count < 5) return straightPath;

            var first = fullGlyphs[0];
            var last = fullGlyphs[^1];
            var mid = fullGlyphs[fullGlyphs.Count / 2];

            double topTol = Math.Max(2.0, art.Bounds.Height * 0.12);
            double linearTop = (first.Top + last.Top) * 0.5;
            double linearBottom = (first.Bottom + last.Bottom) * 0.5;
            bool flatTop = Math.Abs(mid.Top - linearTop) <= topTol && Math.Abs(last.Top - first.Top) <= topTol;
            bool flatBottom = Math.Abs(mid.Bottom - linearBottom) <= topTol && Math.Abs(last.Bottom - first.Bottom) <= topTol;
            // If either the cap-line or the baseline of full-height glyphs is flat, the WordArt is straight horizontal!
            if (flatTop || flatBottom)
            {
                return straightPath;
            }

            int NormY(double pdfY) => Math.Clamp((int)Math.Round((art.Bounds.Top - pdfY) / art.Bounds.Height * 21600.0), 0, 21600);
            int topY0 = NormY(first.Top);
            int topY1 = NormY(last.Top);
            int topMidY = NormY(mid.Top);
            int botY0 = NormY(first.Bottom);
            int botY1 = NormY(last.Bottom);
            int botMidY = NormY(mid.Bottom);

            int topCtrlY = Math.Clamp((int)Math.Round((4 * topMidY - topY0 - topY1) / 2.0), -10800, 16000);
            int botCtrlY = Math.Clamp((int)Math.Round((4 * botMidY - botY0 - botY1) / 2.0), 5600, 32400);
            if (botCtrlY - topCtrlY < 6000) return straightPath;

            return FormattableString.Invariant(
                $"m0,{topY0}c7200,{topCtrlY},14400,{topCtrlY},21600,{topY1}m0,{botY0}c7200,{botCtrlY},14400,{botCtrlY},21600,{botY1}e");
        }

        private static DocumentFormat.OpenXml.Vml.Shape BuildEditableWordArt(TaggedWordArt art, double pageHeight, uint id)
        {
            var b = art.Bounds;
            var (red, green, blue) = SafeToRgbValues(art.Ink.FillColor);
            var stroke = art.Paths.FirstOrDefault(p => p.IsStroked);
            var (sr, sg, sb) = SafeToRgbValues(stroke?.StrokeColor);
            string vmlPath = BuildWordArtTextPathGeometry(art);
            string fontFamily = string.IsNullOrWhiteSpace(art.FontFamily) ? "Arial" : art.FontFamily;
            var styleBuilder = new StringBuilder($"font-family:{fontFamily}");
            if (art.IsBold) styleBuilder.Append(";font-weight:bold");
            if (art.IsItalic) styleBuilder.Append(";font-style:italic");
            styleBuilder.Append(";v-text-kern:t");

            var shape = new DocumentFormat.OpenXml.Vml.Shape(
                new DocumentFormat.OpenXml.Vml.Path { Value = vmlPath, AllowTextPath = true },
                new DocumentFormat.OpenXml.Vml.TextPath { On = true, FitShape = true, Trim = true, String = art.Text, Style = styleBuilder.ToString() },
                new DocumentFormat.OpenXml.Vml.Wordprocessing.TextWrap { Type = DocumentFormat.OpenXml.Vml.Wordprocessing.WrapValues.None })
            {
                Id = $"WordArt_{id}", CoordinateSize = "21600,21600", Filled = true, Stroked = stroke != null,
                FillColor = $"#{(int)Math.Round(red * 255):X2}{(int)Math.Round(green * 255):X2}{(int)Math.Round(blue * 255):X2}",
                StrokeColor = $"#{(int)Math.Round(sr * 255):X2}{(int)Math.Round(sg * 255):X2}{(int)Math.Round(sb * 255):X2}",
                StrokeWeight = FormattableString.Invariant($"{stroke?.LineWidth ?? 0:0.###}pt"),
                Style = FormattableString.Invariant($"position:absolute;margin-left:{b.Left:0.###}pt;margin-top:{pageHeight - b.Top:0.###}pt;width:{b.Width:0.###}pt;height:{b.Height:0.###}pt;z-index:1;mso-position-horizontal-relative:page;mso-position-vertical-relative:page")
            };
            if (art.ShadowColor != null && TryGetColorRgb(art.ShadowColor, out var shRgb) && (Math.Abs(art.ShadowOffsetXPt) > 0.1 || Math.Abs(art.ShadowOffsetYPt) > 0.1))
            {
                string shHex = $"#{(int)Math.Round(shRgb.R * 255):X2}{(int)Math.Round(shRgb.G * 255):X2}{(int)Math.Round(shRgb.B * 255):X2}";
                shape.Append(new DocumentFormat.OpenXml.Vml.Shadow
                {
                    On = true,
                    Color = shHex,
                    Offset = FormattableString.Invariant($"{art.ShadowOffsetXPt:0.##}pt,{art.ShadowOffsetYPt:0.##}pt")
                });
            }
            return shape;
        }

        private static PdfRectangle? GetPathBoundingRectangle(UglyToad.PdfPig.Graphics.PdfPath path)
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            bool any = false;
            foreach (var sub in path)
            {
                var b = sub.GetBoundingRectangle();
                if (!b.HasValue) continue;
                if (b.Value.Left < minX) minX = b.Value.Left;
                if (b.Value.Bottom < minY) minY = b.Value.Bottom;
                if (b.Value.Right > maxX) maxX = b.Value.Right;
                if (b.Value.Top > maxY) maxY = b.Value.Top;
                any = true;
            }
            return any ? new PdfRectangle(minX, minY, maxX, maxY) : null;
        }

        private static PdfRectangle IntersectRectangles(PdfRectangle a, PdfRectangle b)
        {
            double left = Math.Max(a.Left, b.Left);
            double bottom = Math.Max(a.Bottom, b.Bottom);
            double right = Math.Min(a.Right, b.Right);
            double top = Math.Min(a.Top, b.Top);
            return right > left && top > bottom
                ? new PdfRectangle(left, bottom, right, top)
                : new PdfRectangle(left, bottom, left, bottom);
        }

        private static double ComputeOverlapRatio(PdfRectangle candidate, PdfRectangle target)
        {
            double candArea = candidate.Width * candidate.Height;
            if (candArea <= 0) return 0;
            var inter = IntersectRectangles(candidate, target);
            if (inter.Width <= 0 || inter.Height <= 0) return 0;
            return (inter.Width * inter.Height) / candArea;
        }

        private static bool IsPureWhiteCanvasBackdrop(UglyToad.PdfPig.Graphics.PdfPath path, PdfRectangle bounds, double pageWidth, double pageHeight)
        {
            if (!path.IsFilled || path.IsStroked) return false;
            if (bounds.Width < pageWidth * 0.65 || bounds.Height < pageHeight * 0.45) return false;
            var (r, g, b) = SafeToRgbValues(path.FillColor);
            return r >= 0.985 && g >= 0.985 && b >= 0.985;
        }

        private static List<ClippedVectorPathItem> GetClippedPagePaths(
            Page page,
            HashSet<UglyToad.PdfPig.Graphics.PdfPath>? excludedPaths)
        {
            var result = new List<ClippedVectorPathItem>();
            var pageBox = new PdfRectangle(0, 0, page.Width, page.Height);
            PdfRectangle? activeClip = null;

            foreach (var path in page.ExperimentalAccess.Paths)
            {
                var rawBounds = GetPathBoundingRectangle(path);
                if (path.IsClipping)
                {
                    if (rawBounds.HasValue && rawBounds.Value.Width > 0 && rawBounds.Value.Height > 0)
                    {
                        var clippedToPage = IntersectRectangles(rawBounds.Value, pageBox);
                        if (clippedToPage.Width >= page.Width * 0.98 && clippedToPage.Height >= page.Height * 0.98)
                        {
                            activeClip = null;
                        }
                        else if (activeClip.HasValue && ComputeOverlapRatio(clippedToPage, activeClip.Value) > 0.1)
                        {
                            activeClip = IntersectRectangles(activeClip.Value, clippedToPage);
                        }
                        else
                        {
                            activeClip = clippedToPage;
                        }
                    }
                    if (!path.IsFilled && !path.IsStroked) continue;
                }

                if (excludedPaths != null && excludedPaths.Contains(path)) continue;
                if (!rawBounds.HasValue) continue;

                PdfRectangle? clipForPath = null;
                var effective = IntersectRectangles(rawBounds.Value, pageBox);
                if (activeClip.HasValue)
                {
                    var candClip = IntersectRectangles(effective, activeClip.Value);
                    if (candClip.Width > 0.05 && candClip.Height > 0.05)
                    {
                        effective = candClip;
                        if (rawBounds.Value.Left < activeClip.Value.Left - 0.5
                            || rawBounds.Value.Right > activeClip.Value.Right + 0.5
                            || rawBounds.Value.Bottom < activeClip.Value.Bottom - 0.5
                            || rawBounds.Value.Top > activeClip.Value.Top + 0.5)
                        {
                            clipForPath = activeClip.Value;
                        }
                    }
                    else
                    {
                        // Path lies completely outside activeClip -> PDF graphics state 'Q' popped the clip
                        activeClip = null;
                    }
                }

                result.Add(new ClippedVectorPathItem(path, rawBounds.Value, effective, clipForPath));
            }
            return result;
        }

        private static DocumentFormat.OpenXml.Vml.Shape BuildWordVectorShape(UglyToad.PdfPig.Graphics.PdfPath path,
            double pageWidth, double pageHeight, uint id, PdfRectangle? clipRect = null,
            IReadOnlyList<PdfPatternShadingInfo>? pagePatterns = null,
            UglyToad.PdfPig.Graphics.PdfPath? pairedStrokePath = null,
            bool usePageCoordinateSpace = false,
            IReadOnlyList<VisualLine>? attachedLines = null,
            IReadOnlyList<VerticalRotatedTextItem>? attachedVerticalTexts = null)
        {
            PdfPoint ClampPoint(PdfPoint pt) => clipRect.HasValue
                ? new PdfPoint(
                    Math.Clamp(pt.X, clipRect.Value.Left, clipRect.Value.Right),
                    Math.Clamp(pt.Y, clipRect.Value.Bottom, clipRect.Value.Top))
                : pt;
            string Point(PdfPoint point)
            {
                var p = ClampPoint(point);
                return FormattableString.Invariant($"{Math.Round(p.X * 1000)},{Math.Round((pageHeight - p.Y) * 1000)}");
            }
            var data = new StringBuilder();
            foreach (var command in path.SelectMany(subpath => subpath.Commands))
                switch (command)
                {
                    case PdfSubpath.Move move: data.Append("m").Append(Point(move.Location)); break;
                    case PdfSubpath.Line line: data.Append("l").Append(Point(line.To)); break;
                    case PdfSubpath.CubicBezierCurve curve:
                        data.Append("c").Append(Point(curve.FirstControlPoint)).Append(',').Append(Point(curve.SecondControlPoint)).Append(',').Append(Point(curve.EndPoint)); break;
                    case PdfSubpath.QuadraticBezierCurve curve:
                        var first = new PdfPoint(curve.StartPoint.X + (curve.ControlPoint.X - curve.StartPoint.X) * 2 / 3,
                            curve.StartPoint.Y + (curve.ControlPoint.Y - curve.StartPoint.Y) * 2 / 3);
                        var second = new PdfPoint(curve.EndPoint.X + (curve.ControlPoint.X - curve.EndPoint.X) * 2 / 3,
                            curve.EndPoint.Y + (curve.ControlPoint.Y - curve.EndPoint.Y) * 2 / 3);
                        data.Append("c").Append(Point(first)).Append(',').Append(Point(second)).Append(',').Append(Point(curve.EndPoint)); break;
                    case PdfSubpath.Close: data.Append('x'); break;
                    default: throw new NotSupportedException($"Perintah vektor PDF tidak didukung: {command.GetType().Name}");
                }
            data.Append('e');

            var rawBounds = GetPathBoundingRectangle(path);
            var bounds = rawBounds.HasValue && clipRect.HasValue
                ? IntersectRectangles(rawBounds.Value, clipRect.Value)
                : rawBounds;

            var strokeSource = pairedStrokePath ?? (path.IsStroked ? path : null);
            string? fillColor = path.IsFilled ? ResolvePdfColorHex(path.FillColor, rawBounds, pagePatterns) : null;
            string? strokeColor = strokeSource != null ? ResolvePdfColorHex(strokeSource.StrokeColor, rawBounds, pagePatterns) : null;
            double strokeLineWidth = strokeSource?.LineWidth ?? path.LineWidth;

            bool hasTextBox = (attachedLines != null && attachedLines.Count > 0) || (attachedVerticalTexts != null && attachedVerticalTexts.Count > 0);
            bool useTightBounds = !usePageCoordinateSpace && bounds.HasValue && Math.Max(bounds.Value.Width, bounds.Value.Height) > 0.5
                && (bounds.Value.Width < pageWidth * 0.96 || bounds.Value.Height < pageHeight * 0.96);
            double boxLeft = useTightBounds ? bounds!.Value.Left : 0;
            double boxTop = useTightBounds ? pageHeight - bounds!.Value.Top : 0;
            double boxWidth = useTightBounds ? Math.Max(1.0, bounds!.Value.Width) : pageWidth;
            double boxHeight = useTightBounds ? Math.Max(1.0, bounds!.Value.Height) : pageHeight;
            int zIndex = hasTextBox ? 1 : -2;
            string anchorStyle = hasTextBox ? ";v-text-anchor:middle" : string.Empty;

            var shape = new DocumentFormat.OpenXml.Vml.Shape(
                new DocumentFormat.OpenXml.Vml.Path { Value = data.ToString() },
                new DocumentFormat.OpenXml.Vml.Wordprocessing.TextWrap { Type = DocumentFormat.OpenXml.Vml.Wordprocessing.WrapValues.None })
            {
                Id = $"Artwork_{id}",
                CoordinateOrigin = FormattableString.Invariant($"{Math.Round(boxLeft * 1000)},{Math.Round(boxTop * 1000)}"),
                CoordinateSize = FormattableString.Invariant($"{Math.Max(1000, Math.Round(boxWidth * 1000))},{Math.Max(1000, Math.Round(boxHeight * 1000))}"),
                Filled = fillColor != null, Stroked = strokeColor != null,
                FillColor = fillColor, StrokeColor = strokeColor,
                StrokeWeight = FormattableString.Invariant($"{strokeLineWidth:0.###}pt"),
                Style = FormattableString.Invariant($"position:absolute;margin-left:{boxLeft:0.###}pt;margin-top:{boxTop:0.###}pt;width:{boxWidth:0.###}pt;height:{boxHeight:0.###}pt;z-index:{zIndex};mso-position-horizontal-relative:page;mso-position-vertical-relative:page{anchorStyle}")
            };

            if (hasTextBox)
            {
                var txbxContent = new W.TextBoxContent();
                var textBox = new DocumentFormat.OpenXml.Vml.TextBox { Inset = "1.5pt,1.5pt,1.5pt,1.5pt" };
                if (attachedVerticalTexts != null && attachedVerticalTexts.Count > 0)
                {
                    var vItem = attachedVerticalTexts[0];
                    textBox.Style = vItem.BottomToTop
                        ? "layout-flow:vertical;mso-layout-flow-alt:bottom-to-top"
                        : "layout-flow:vertical";
                    string halfPt = ((int)Math.Round(Math.Clamp(vItem.FontSizePt, 6.0, 72.0) * 2.0)).ToString(CultureInfo.InvariantCulture);
                    string cleanHex = vItem.FillHex.TrimStart('#');
                    var rPr = new W.RunProperties(
                        new W.RunFonts { Ascii = vItem.FontFamily, HighAnsi = vItem.FontFamily, ComplexScript = vItem.FontFamily },
                        new W.Color { Val = string.IsNullOrWhiteSpace(cleanHex) ? "000000" : cleanHex },
                        new W.FontSize { Val = halfPt },
                        new W.FontSizeComplexScript { Val = halfPt });
                    if (vItem.IsBold) rPr.InsertAt(new W.Bold(), 1);
                    var vPara = new W.Paragraph(
                        new W.ParagraphProperties(
                            new W.SpacingBetweenLines { Before = "0", After = "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto },
                            new W.Justification { Val = W.JustificationValues.Center }),
                        new W.Run(rPr, new W.Text(vItem.Text) { Space = SpaceProcessingModeValues.Preserve }));
                    txbxContent.Append(vPara);
                }
                else if (attachedLines != null && attachedLines.Count > 0)
                {
                    var orderedLines = attachedLines.OrderByDescending(l => l.Top).ToList();
                    var combinedWords = orderedLines.SelectMany(l => l.Words).ToList();
                    var hPara = new W.Paragraph(
                        new W.ParagraphProperties(
                            new W.SpacingBetweenLines { Before = "0", After = "0", Line = "220", LineRule = W.LineSpacingRuleValues.Auto },
                            new W.Justification { Val = W.JustificationValues.Center }));
                    AppendConsolidatedRuns(hPara, combinedWords, 0);
                    txbxContent.Append(hPara);
                }
                textBox.Append(txbxContent);
                shape.Append(textBox);
            }

            return shape;
        }

        private static List<P.Shape> BuildPptxGroupedVectorShapes(
            IReadOnlyList<ClippedVectorPathItem> items,
            double pageHeight,
            double scale,
            double offsetX,
            double offsetY,
            ref uint shapeId)
        {
            var shapes = new List<P.Shape>();
            if (items.Count == 0) return shapes;

            static string StyleKey(UglyToad.PdfPig.Graphics.PdfPath p)
            {
                var (fr, fg, fb) = SafeToRgbValues(p.FillColor);
                var (sr, sg, sb) = SafeToRgbValues(p.StrokeColor);
                return FormattableString.Invariant(
                    $"{p.IsFilled}:{(int)Math.Round(fr * 255):X2}{(int)Math.Round(fg * 255):X2}{(int)Math.Round(fb * 255):X2}|{p.IsStroked}:{(int)Math.Round(sr * 255):X2}{(int)Math.Round(sg * 255):X2}{(int)Math.Round(sb * 255):X2}:{p.LineWidth:0.##}");
            }

            static bool IsGlyphLike(ClippedVectorPathItem item)
                => item.Path.IsFilled && !item.Path.IsStroked
                && item.EffectiveBounds.Width <= 28.0 && item.EffectiveBounds.Height <= 36.0;

            var batch = new List<ClippedVectorPathItem>();
            string? batchKey = null;
            double batchMidY = double.NaN;
            uint localShapeId = shapeId;

            void FlushBatch()
            {
                if (batch.Count == 0) return;
                if (batch.Count == 1)
                {
                    var single = BuildPptxVectorShape(batch[0].Path, batch[0].ClipRect, batch[0].EffectiveBounds, pageHeight, scale, offsetX, offsetY, localShapeId);
                    if (single != null)
                    {
                        shapes.Add(single);
                        localShapeId++;
                    }
                }
                else
                {
                    var compound = BuildPptxCompoundVectorShape(batch, pageHeight, scale, offsetX, offsetY, localShapeId);
                    if (compound != null)
                    {
                        shapes.Add(compound);
                        localShapeId++;
                    }
                }
                batch.Clear();
                batchKey = null;
                batchMidY = double.NaN;
            }

            foreach (var item in items)
            {
                if (!IsGlyphLike(item))
                {
                    FlushBatch();
                    var s = BuildPptxVectorShape(item.Path, item.ClipRect, item.EffectiveBounds, pageHeight, scale, offsetX, offsetY, localShapeId);
                    if (s != null)
                    {
                        shapes.Add(s);
                        localShapeId++;
                    }
                    continue;
                }

                string key = StyleKey(item.Path);
                double midY = (item.EffectiveBounds.Top + item.EffectiveBounds.Bottom) * 0.5;
                if (batch.Count > 0 && (key != batchKey || Math.Abs(midY - batchMidY) > 8.0 || batch.Count >= 48))
                {
                    FlushBatch();
                }
                batch.Add(item);
                batchKey = key;
                if (double.IsNaN(batchMidY)) batchMidY = midY;
            }
            FlushBatch();
            shapeId = localShapeId;
            return shapes;
        }

        private static P.Shape? BuildPptxCompoundVectorShape(
            IReadOnlyList<ClippedVectorPathItem> group,
            double pageHeight,
            double scale,
            double offsetX,
            double offsetY,
            uint shapeId)
        {
            double minLeft = group.Min(g => g.EffectiveBounds.Left);
            double minBottom = group.Min(g => g.EffectiveBounds.Bottom);
            double maxRight = group.Max(g => g.EffectiveBounds.Right);
            double maxTop = group.Max(g => g.EffectiveBounds.Top);
            var unionBounds = new PdfRectangle(minLeft, minBottom, maxRight, maxTop);
            if (unionBounds.Width <= 0.05 || unionBounds.Height <= 0.05) return null;

            long xEmu = (long)Math.Round((offsetX + unionBounds.Left * scale) * 12700.0);
            long yEmu = (long)Math.Round((offsetY + (pageHeight - unionBounds.Top) * scale) * 12700.0);
            long wEmu = Math.Max(1270L, (long)Math.Round(unionBounds.Width * scale * 12700.0));
            long hEmu = Math.Max(1270L, (long)Math.Round(unionBounds.Height * scale * 12700.0));

            var aPath = new A.Path { Width = wEmu, Height = hEmu };
            int commandCount = 0;

            foreach (var item in group)
            {
                var clipRect = item.ClipRect;
                A.Point ToLocalPoint(PdfPoint pt)
                {
                    double px = clipRect.HasValue ? Math.Clamp(pt.X, clipRect.Value.Left, clipRect.Value.Right) : pt.X;
                    double py = clipRect.HasValue ? Math.Clamp(pt.Y, clipRect.Value.Bottom, clipRect.Value.Top) : pt.Y;
                    long lx = (long)Math.Clamp(Math.Round((px - unionBounds.Left) * scale * 12700.0), 0L, wEmu);
                    long ly = (long)Math.Clamp(Math.Round((unionBounds.Top - py) * scale * 12700.0), 0L, hEmu);
                    return new A.Point
                    {
                        X = lx.ToString(CultureInfo.InvariantCulture),
                        Y = ly.ToString(CultureInfo.InvariantCulture)
                    };
                }

                foreach (var command in item.Path.SelectMany(sub => sub.Commands))
                {
                    switch (command)
                    {
                        case PdfSubpath.Move move:
                            aPath.Append(new A.MoveTo(ToLocalPoint(move.Location)));
                            commandCount++;
                            break;
                        case PdfSubpath.Line line:
                            aPath.Append(new A.LineTo(ToLocalPoint(line.To)));
                            commandCount++;
                            break;
                        case PdfSubpath.CubicBezierCurve cubic:
                            aPath.Append(new A.CubicBezierCurveTo(
                                ToLocalPoint(cubic.FirstControlPoint),
                                ToLocalPoint(cubic.SecondControlPoint),
                                ToLocalPoint(cubic.EndPoint)));
                            commandCount++;
                            break;
                        case PdfSubpath.QuadraticBezierCurve quad:
                            var c1 = new PdfPoint(
                                quad.StartPoint.X + (quad.ControlPoint.X - quad.StartPoint.X) * 2.0 / 3.0,
                                quad.StartPoint.Y + (quad.ControlPoint.Y - quad.StartPoint.Y) * 2.0 / 3.0);
                            var c2 = new PdfPoint(
                                quad.EndPoint.X + (quad.ControlPoint.X - quad.EndPoint.X) * 2.0 / 3.0,
                                quad.EndPoint.Y + (quad.ControlPoint.Y - quad.EndPoint.Y) * 2.0 / 3.0);
                            aPath.Append(new A.CubicBezierCurveTo(
                                ToLocalPoint(c1),
                                ToLocalPoint(c2),
                                ToLocalPoint(quad.EndPoint)));
                            commandCount++;
                            break;
                        case PdfSubpath.Close:
                            aPath.Append(new A.CloseShapePath());
                            break;
                    }
                }
            }
            if (commandCount == 0) return null;

            var firstPath = group[0].Path;
            var spPr = new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = xEmu, Y = yEmu },
                    new A.Extents { Cx = wEmu, Cy = hEmu }),
                new A.CustomGeometry(
                    new A.AdjustValueList(),
                    new A.PathList(aPath)));

            if (firstPath.IsFilled)
            {
                var (r, g, b) = SafeToRgbValues(firstPath.FillColor);
                string fillHex = $"{(int)Math.Round(r * 255):X2}{(int)Math.Round(g * 255):X2}{(int)Math.Round(b * 255):X2}";
                spPr.Append(new A.SolidFill(new A.RgbColorModelHex { Val = fillHex }));
            }
            else
            {
                spPr.Append(new A.NoFill());
            }

            if (firstPath.IsStroked)
            {
                var (sr, sg, sb) = SafeToRgbValues(firstPath.StrokeColor);
                string strokeHex = $"{(int)Math.Round(sr * 255):X2}{(int)Math.Round(sg * 255):X2}{(int)Math.Round(sb * 255):X2}";
                int widthEmu = (int)Math.Clamp(Math.Round(Math.Max(0.5, firstPath.LineWidth) * scale * 12700.0), 3175, 254000);
                spPr.Append(new A.Outline(new A.SolidFill(new A.RgbColorModelHex { Val = strokeHex })) { Width = widthEmu });
            }

            return new P.Shape(
                new P.NonVisualShapeProperties(
                    new P.NonVisualDrawingProperties { Id = shapeId, Name = $"VectorGlyphCluster_{shapeId}" },
                    new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                    new P.ApplicationNonVisualDrawingProperties()),
                spPr,
                new P.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph()));
        }

        private static P.Shape? BuildPptxVectorShape(
            UglyToad.PdfPig.Graphics.PdfPath path,
            PdfRectangle? clipRect,
            PdfRectangle effectiveBounds,
            double pageHeight,
            double scale,
            double offsetX,
            double offsetY,
            uint shapeId)
        {
            if (effectiveBounds.Width <= 0.05 || effectiveBounds.Height <= 0.05) return null;
            long xEmu = (long)Math.Round((offsetX + effectiveBounds.Left * scale) * 12700.0);
            long yEmu = (long)Math.Round((offsetY + (pageHeight - effectiveBounds.Top) * scale) * 12700.0);
            long wEmu = Math.Max(1270L, (long)Math.Round(effectiveBounds.Width * scale * 12700.0));
            long hEmu = Math.Max(1270L, (long)Math.Round(effectiveBounds.Height * scale * 12700.0));

            A.Point ToLocalPoint(PdfPoint pt)
            {
                double px = clipRect.HasValue ? Math.Clamp(pt.X, clipRect.Value.Left, clipRect.Value.Right) : pt.X;
                double py = clipRect.HasValue ? Math.Clamp(pt.Y, clipRect.Value.Bottom, clipRect.Value.Top) : pt.Y;
                long lx = (long)Math.Clamp(Math.Round((px - effectiveBounds.Left) * scale * 12700.0), 0L, wEmu);
                long ly = (long)Math.Clamp(Math.Round((effectiveBounds.Top - py) * scale * 12700.0), 0L, hEmu);
                return new A.Point
                {
                    X = lx.ToString(CultureInfo.InvariantCulture),
                    Y = ly.ToString(CultureInfo.InvariantCulture)
                };
            }

            var aPath = new A.Path { Width = wEmu, Height = hEmu };
            int commandCount = 0;
            foreach (var command in path.SelectMany(sub => sub.Commands))
            {
                switch (command)
                {
                    case PdfSubpath.Move move:
                        aPath.Append(new A.MoveTo(ToLocalPoint(move.Location)));
                        commandCount++;
                        break;
                    case PdfSubpath.Line line:
                        aPath.Append(new A.LineTo(ToLocalPoint(line.To)));
                        commandCount++;
                        break;
                    case PdfSubpath.CubicBezierCurve cubic:
                        aPath.Append(new A.CubicBezierCurveTo(
                            ToLocalPoint(cubic.FirstControlPoint),
                            ToLocalPoint(cubic.SecondControlPoint),
                            ToLocalPoint(cubic.EndPoint)));
                        commandCount++;
                        break;
                    case PdfSubpath.QuadraticBezierCurve quad:
                        var c1 = new PdfPoint(
                            quad.StartPoint.X + (quad.ControlPoint.X - quad.StartPoint.X) * 2.0 / 3.0,
                            quad.StartPoint.Y + (quad.ControlPoint.Y - quad.StartPoint.Y) * 2.0 / 3.0);
                        var c2 = new PdfPoint(
                            quad.EndPoint.X + (quad.ControlPoint.X - quad.EndPoint.X) * 2.0 / 3.0,
                            quad.EndPoint.Y + (quad.ControlPoint.Y - quad.EndPoint.Y) * 2.0 / 3.0);
                        aPath.Append(new A.CubicBezierCurveTo(
                            ToLocalPoint(c1),
                            ToLocalPoint(c2),
                            ToLocalPoint(quad.EndPoint)));
                        commandCount++;
                        break;
                    case PdfSubpath.Close:
                        aPath.Append(new A.CloseShapePath());
                        break;
                }
            }
            if (commandCount == 0) return null;

            var spPr = new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = xEmu, Y = yEmu },
                    new A.Extents { Cx = wEmu, Cy = hEmu }),
                new A.CustomGeometry(
                    new A.AdjustValueList(),
                    new A.PathList(aPath)));

            if (path.IsFilled)
            {
                var (r, g, b) = SafeToRgbValues(path.FillColor);
                string fillHex = $"{(int)Math.Round(r * 255):X2}{(int)Math.Round(g * 255):X2}{(int)Math.Round(b * 255):X2}";
                spPr.Append(new A.SolidFill(new A.RgbColorModelHex { Val = fillHex }));
            }
            else
            {
                spPr.Append(new A.NoFill());
            }

            if (path.IsStroked)
            {
                var (sr, sg, sb) = SafeToRgbValues(path.StrokeColor);
                string strokeHex = $"{(int)Math.Round(sr * 255):X2}{(int)Math.Round(sg * 255):X2}{(int)Math.Round(sb * 255):X2}";
                int widthEmu = (int)Math.Clamp(Math.Round(Math.Max(0.5, path.LineWidth) * scale * 12700.0), 3175, 254000);
                spPr.Append(new A.Outline(new A.SolidFill(new A.RgbColorModelHex { Val = strokeHex })) { Width = widthEmu });
            }

            return new P.Shape(
                new P.NonVisualShapeProperties(
                    new P.NonVisualDrawingProperties { Id = shapeId, Name = $"VectorShape_{shapeId}" },
                    new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                    new P.ApplicationNonVisualDrawingProperties()),
                spPr,
                new P.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph()));
        }

        private static P.Shape BuildPptxWordArtShape(TaggedWordArt art, double pageHeight, double scale, double offsetX, double offsetY, uint shapeId)
        {
            var b = art.Bounds;
            long xEmu = (long)Math.Round((offsetX + b.Left * scale) * 12700.0);
            long yEmu = (long)Math.Round((offsetY + (pageHeight - b.Top) * scale) * 12700.0);
            long wEmu = Math.Max(12700L, (long)Math.Round(b.Width * scale * 12700.0));
            long hEmu = Math.Max(12700L, (long)Math.Round(b.Height * scale * 12700.0));

            var (r, g, bl) = SafeToRgbValues(art.Ink.FillColor);
            string hexColor = $"{(int)Math.Round(r * 255):X2}{(int)Math.Round(g * 255):X2}{(int)Math.Round(bl * 255):X2}";
            int fontSizeCentipoints = (int)Math.Round(Math.Clamp(b.Height * 0.78 * scale, 6.0, 400.0) * 100.0);

            var rPr = new A.RunProperties
            {
                Language = "en-US",
                FontSize = fontSizeCentipoints,
                Bold = true
            };
            rPr.Append(new A.SolidFill(new A.RgbColorModelHex { Val = hexColor }));
            rPr.Append(new A.LatinFont { Typeface = string.IsNullOrWhiteSpace(art.FontFamily) ? "Arial" : art.FontFamily });

            var aPara = new A.Paragraph(
                new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Center },
                new A.Run(rPr, new A.Text(art.Text)));

            var textBody = new P.TextBody(
                new A.BodyProperties
                {
                    Wrap = A.TextWrappingValues.None,
                    Anchor = A.TextAnchoringTypeValues.Center,
                    LeftInset = 0, RightInset = 0, TopInset = 0, BottomInset = 0
                },
                new A.ListStyle(),
                aPara);

            return new P.Shape(
                new P.NonVisualShapeProperties(
                    new P.NonVisualDrawingProperties { Id = shapeId, Name = $"WordArt_{shapeId}" },
                    new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.ShapeProperties(
                    new A.Transform2D(
                        new A.Offset { X = xEmu, Y = yEmu },
                        new A.Extents { Cx = wEmu, Cy = hEmu }),
                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                    new A.NoFill()),
                textBody);
        }

        private static SKBitmap? RenderClippedVectorPathsToBitmap(
            IReadOnlyList<ClippedVectorPathItem> items,
            double pageWidthPt,
            double pageHeightPt,
            int dpi,
            SKColor backgroundColor)
        {
            if (items.Count == 0 || pageWidthPt <= 0 || pageHeightPt <= 0) return null;
            float scale = (float)(dpi / 72.0);
            int widthPx = Math.Clamp((int)Math.Round(pageWidthPt * scale), 16, 4096);
            int heightPx = Math.Clamp((int)Math.Round(pageHeightPt * scale), 16, 4096);
            float sx = (float)(widthPx / pageWidthPt);
            float sy = (float)(heightPx / pageHeightPt);

            var bitmap = new SKBitmap(widthPx, heightPx, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(backgroundColor);

            foreach (var item in items)
            {
                var path = item.Path;
                if (!path.IsFilled && !path.IsStroked) continue;

                int saveCount = canvas.Save();
                if (item.ClipRect.HasValue)
                {
                    var cr = item.ClipRect.Value;
                    canvas.ClipRect(new SKRect(
                        (float)(cr.Left * sx),
                        (float)((pageHeightPt - cr.Top) * sy),
                        (float)(cr.Right * sx),
                        (float)((pageHeightPt - cr.Bottom) * sy)));
                }

                using var skPath = new SKPath();
                foreach (var sub in path)
                {
                    foreach (var cmd in sub.Commands)
                    {
                        switch (cmd)
                        {
                            case PdfSubpath.Move m:
                                skPath.MoveTo((float)(m.Location.X * sx), (float)((pageHeightPt - m.Location.Y) * sy));
                                break;
                            case PdfSubpath.Line l:
                                skPath.LineTo((float)(l.To.X * sx), (float)((pageHeightPt - l.To.Y) * sy));
                                break;
                            case PdfSubpath.CubicBezierCurve c:
                                skPath.CubicTo(
                                    (float)(c.FirstControlPoint.X * sx), (float)((pageHeightPt - c.FirstControlPoint.Y) * sy),
                                    (float)(c.SecondControlPoint.X * sx), (float)((pageHeightPt - c.SecondControlPoint.Y) * sy),
                                    (float)(c.EndPoint.X * sx), (float)((pageHeightPt - c.EndPoint.Y) * sy));
                                break;
                            case PdfSubpath.QuadraticBezierCurve q:
                                skPath.QuadTo(
                                    (float)(q.ControlPoint.X * sx), (float)((pageHeightPt - q.ControlPoint.Y) * sy),
                                    (float)(q.EndPoint.X * sx), (float)((pageHeightPt - q.EndPoint.Y) * sy));
                                break;
                            case PdfSubpath.Close:
                                skPath.Close();
                                break;
                        }
                    }
                }

                if (path.IsFilled)
                {
                    var (r, g, b) = SafeToRgbValues(path.FillColor);
                    using var fillPaint = new SKPaint
                    {
                        IsAntialias = true,
                        Style = SKPaintStyle.Fill,
                        Color = new SKColor(
                            (byte)Math.Clamp((int)Math.Round(r * 255), 0, 255),
                            (byte)Math.Clamp((int)Math.Round(g * 255), 0, 255),
                            (byte)Math.Clamp((int)Math.Round(b * 255), 0, 255))
                    };
                    canvas.DrawPath(skPath, fillPaint);
                }
                if (path.IsStroked)
                {
                    var (sr, sg, sb) = SafeToRgbValues(path.StrokeColor);
                    using var strokePaint = new SKPaint
                    {
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = Math.Max(1f, (float)(path.LineWidth * sx)),
                        Color = new SKColor(
                            (byte)Math.Clamp((int)Math.Round(sr * 255), 0, 255),
                            (byte)Math.Clamp((int)Math.Round(sg * 255), 0, 255),
                            (byte)Math.Clamp((int)Math.Round(sb * 255), 0, 255))
                    };
                    canvas.DrawPath(skPath, strokePaint);
                }
                canvas.RestoreToCount(saveCount);
            }
            return bitmap;
        }

        private static byte[]? RenderClippedVectorPathsToPng(
            IReadOnlyList<ClippedVectorPathItem> items,
            double pageWidthPt,
            double pageHeightPt,
            int dpi = 150)
        {
            using var bitmap = RenderClippedVectorPathsToBitmap(items, pageWidthPt, pageHeightPt, dpi, SKColors.Transparent);
            if (bitmap == null) return null;
            using var skImage = SKImage.FromBitmap(bitmap);
            using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        private static List<PdfRectangle> ExtractVectorGridBounds(Page page)
        {
            var rects = new List<PdfRectangle>();
            try
            {
                var paths = page.ExperimentalAccess.Paths;
                if (paths == null) return rects;

                foreach (var p in paths)
                {
                    foreach (var sub in p)
                    {
                        var bbox = sub.GetBoundingRectangle();
                        if (bbox.HasValue && (bbox.Value.Width >= 1.5 || bbox.Value.Height >= 1.5))
                        {
                            rects.Add(bbox.Value);
                        }
                    }
                }
            }
            catch { }
            return rects;
        }

        private static List<LayoutRegion> SegmentLinesIntoRegions(List<VisualLine> lines, List<PdfRectangle> vectorPaths, bool useVectorGrid = false)
        {
            if (useVectorGrid)
            {
                var tables = FindRuledTables(lines, vectorPaths);
                if (tables.Count > 0)
                {
                    var remaining = lines.Except(tables.SelectMany(t => t.Lines)).ToList();
                    // Keep paragraphs on opposite sides of a table in separate regions.
                    return remaining.GroupBy(l => tables.Count(t => t.Top > l.Top))
                        .SelectMany(group => SegmentLinesIntoRegions(group.ToList(), vectorPaths)).Concat(tables)
                        .OrderByDescending(r => r.Top).ToList();
                }
            }
            var regions = new List<LayoutRegion>();
            var candidate = new List<VisualLine>();
            var normalLines = new List<VisualLine>();

            foreach (var line in lines)
            {
                if (line.Segments.Count >= 2)
                {
                    if (normalLines.Count > 0)
                    {
                        var r = new LayoutRegion { IsTable = false };
                        r.Lines.AddRange(normalLines);
                        regions.Add(r);
                        normalLines.Clear();
                    }
                    candidate.Add(line);
                }
                else
                {
                    FlushTableCandidate(regions, candidate);
                    normalLines.Add(line);
                }
            }

            FlushTableCandidate(regions, candidate);
            if (normalLines.Count > 0)
            {
                var r = new LayoutRegion { IsTable = false };
                r.Lines.AddRange(normalLines);
                regions.Add(r);
            }

            return regions;
        }

        private static List<LayoutRegion> FindRuledTables(List<VisualLine> lines, List<PdfRectangle> paths)
        {
            var pending = paths.Where(p => (p.Width <= 2 && p.Height >= 8) || (p.Height <= 2 && p.Width >= 8)).ToList();
            var tables = new List<LayoutRegion>();
            while (pending.Count > 0)
            {
                var edges = new List<PdfRectangle> { pending[0] };
                pending.RemoveAt(0);
                bool expanded;
                do
                {
                    expanded = false;
                    for (int i = pending.Count - 1; i >= 0; i--)
                    {
                        var edge = pending[i];
                        if (!edges.Any(existing => edge.Left <= existing.Right + 2 && edge.Right >= existing.Left - 2
                            && edge.Bottom <= existing.Top + 2 && edge.Top >= existing.Bottom - 2)) continue;
                        edges.Add(edge);
                        pending.RemoveAt(i);
                        expanded = true;
                    }
                } while (expanded);

                var verticals = edges.Where(e => e.Width <= 2 && e.Height >= 8).ToList();
                var xs = MergeGridCoordinates(verticals.Select(e => (e.Left + e.Right) / 2));
                // shortcut: partial horizontal rules do not yet reconstruct vertically merged cells.
                var ys = MergeGridCoordinates(edges.Where(e => e.Height <= 2 && e.Width >= 8).Select(e => (e.Top + e.Bottom) / 2));
                ys.Reverse();
                if (xs.Count < 3 || ys.Count < 2) continue;
                var horizontals = edges.Where(e => e.Height <= 2 && e.Width >= 8).ToList();
                if (!CoversInterval(horizontals.Where(e => Math.Abs((e.Top + e.Bottom) / 2 - ys[0]) <= 2).Select(e => (e.Left, e.Right)), xs[0], xs[^1])
                    || !CoversInterval(horizontals.Where(e => Math.Abs((e.Top + e.Bottom) / 2 - ys[^1]) <= 2).Select(e => (e.Left, e.Right)), xs[0], xs[^1])) continue;
                bool VerticalCovers(double x, double bottom, double top) => CoversInterval(
                    verticals.Where(e => Math.Abs((e.Left + e.Right) / 2 - x) <= 2).Select(e => (e.Bottom, e.Top)), bottom, top);
                bool HorizontalCovers(double y, double left, double right) => CoversInterval(
                    horizontals.Where(e => Math.Abs((e.Top + e.Bottom) / 2 - y) <= 2).Select(e => (e.Left, e.Right)), left, right);
                // An interrupted outer border is still a grid when every row contains a closed cell.
                if (!(VerticalCovers(xs[0], ys[^1], ys[0]) && VerticalCovers(xs[^1], ys[^1], ys[0]))
                    && !Enumerable.Range(0, ys.Count - 1).All(row => Enumerable.Range(0, xs.Count - 1).Any(col =>
                        VerticalCovers(xs[col], ys[row + 1], ys[row]) && VerticalCovers(xs[col + 1], ys[row + 1], ys[row])
                        && HorizontalCovers(ys[row], xs[col], xs[col + 1]) && HorizontalCovers(ys[row + 1], xs[col], xs[col + 1])))) continue;
                var selected = lines.Where(l => l.Left >= xs[0] - 2 && l.Right <= xs[^1] + 2
                    && (l.Top + l.Bottom) / 2 < ys[0] && (l.Top + l.Bottom) / 2 > ys[^1]).ToList();
                bool isSingleRowMultiColContinuation = ys.Count == 2 && xs.Count >= 5 && (ys[0] - ys[^1]) >= 14.0;
                if (ys.Count < 3 && !isSingleRowMultiColContinuation) continue;
                if (selected.Count < 2 && !(isSingleRowMultiColContinuation && (selected.Count >= 1 || xs.Count >= 6))) continue;
                var table = new LayoutRegion { IsTable = true, GridColumns = xs, ColumnAnchors = xs.Take(xs.Count - 1).ToList() };
                table.Lines.AddRange(selected);
                for (int row = 0; row < ys.Count - 1; row++)
                {
                    var rowLines = selected.Where(l => (l.Top + l.Bottom) / 2 <= ys[row] && (l.Top + l.Bottom) / 2 > ys[row + 1]).ToList();
                    var cells = new List<RuledCell>();
                    int start = 0;
                    for (int col = 1; col < xs.Count; col++)
                    {
                        double midY = (ys[row] + ys[row + 1]) / 2;
                        bool boundary = col == xs.Count - 1 || verticals.Any(e => Math.Abs((e.Left + e.Right) / 2 - xs[col]) <= 2 && e.Top >= midY && e.Bottom <= midY);
                        if (!boundary) continue;
                        var cellLines = new List<VisualLine>();
                        foreach (var line in rowLines)
                        {
                            var words = line.Words.Where(w => (w.Left + w.Right) / 2 >= xs[start] && (w.Left + w.Right) / 2 < xs[col]).ToList();
                            if (words.Count > 0) cellLines.Add(line with { Words = words, Left = words.Min(w => w.Left), Right = words.Max(w => w.Right) });
                        }
                        cells.Add(new RuledCell(start, col - start, cellLines));
                        start = col;
                    }
                    table.RuledRows.Add(new RuledRow(ys[row], ys[row + 1], cells));
                    var values = Enumerable.Repeat(string.Empty, xs.Count - 1).ToArray();
                    foreach (var cell in cells) values[cell.Column] = string.Join("\n", cell.Lines.Select(l => l.FullText));
                    table.TableRows.Add(values);
                }
                for (int r = 0; r < table.RuledRows.Count - 1; r++)
                {
                    double sharedBoundaryY = table.RuledRows[r].Bottom;
                    for (int cIdx = 0; cIdx < table.RuledRows[r].Cells.Count; cIdx++)
                    {
                        var upperCell = table.RuledRows[r].Cells[cIdx];
                        int lowerIdx = table.RuledRows[r + 1].Cells.FindIndex(c => c.Column == upperCell.Column && c.Span == upperCell.Span);
                        if (lowerIdx < 0) continue;
                        double spanLeft = xs[upperCell.Column];
                        double spanRight = xs[upperCell.Column + upperCell.Span];
                        double midX = (spanLeft + spanRight) * 0.5;
                        bool hasHorizontalRule = horizontals.Any(e =>
                            Math.Abs((e.Top + e.Bottom) * 0.5 - sharedBoundaryY) <= 2.0
                            && e.Left <= midX && e.Right >= midX);
                        if (!hasHorizontalRule)
                        {
                            if (upperCell.VerticalMerge == null)
                            {
                                table.RuledRows[r].Cells[cIdx] = upperCell with { VerticalMerge = W.MergedCellValues.Restart };
                            }
                            var lowerCell = table.RuledRows[r + 1].Cells[lowerIdx];
                            table.RuledRows[r + 1].Cells[lowerIdx] = lowerCell with { VerticalMerge = W.MergedCellValues.Continue };
                        }
                    }
                }
                tables.Add(table);
            }
            return tables;
        }

        private static bool CoversInterval(IEnumerable<(double Start, double End)> segments, double start, double end)
        {
            double covered = start;
            foreach (var segment in segments.OrderBy(s => s.Start))
            {
                if (segment.Start > covered + 2) return false;
                covered = Math.Max(covered, segment.End);
                if (covered >= end - 2) return true;
            }
            return false;
        }

        private static List<double> MergeGridCoordinates(IEnumerable<double> coordinates)
        {
            var result = new List<double>();
            foreach (double coordinate in coordinates.OrderBy(x => x))
                if (result.Count == 0 || coordinate - result[^1] > 2) result.Add(coordinate);
            return result;
        }

        private static void FlushTableCandidate(List<LayoutRegion> regions, List<VisualLine> candidate)
        {
            if (candidate.Count == 0) return;

            if (candidate.Count >= 2)
            {
                var (anchors, rows) = BuildTableGridForLines(candidate);

                bool isValidTable = false;
                if (candidate.Count >= 3 && anchors.Count >= 3)
                {
                    isValidTable = true;
                }
                else if (candidate.Count == 2 && anchors.Count >= 3 && candidate[0].IsBold && !candidate[1].IsBold)
                {
                    isValidTable = true;
                }
                else if (candidate.Count >= 3 && anchors.Count == 2)
                {
                    var col2Lefts = candidate.Where(l => l.Segments.Count >= 2).Select(l => l.Segments[1].Left).ToList();
                    double maxDrift = col2Lefts.Count > 0 ? col2Lefts.Max() - col2Lefts.Min() : 100.0;
                    bool paragraphColumns = candidate.All(l => l.Segments.Count == 2 && l.Segments.All(s => s.Words.Count >= 4));
                    bool colonKeyValues = candidate.All(l => KeyValueMetadataRegex.IsMatch(l.FullText)
                        || (l.Segments.Count >= 2 && l.Segments[1].Text.TrimStart().StartsWith(':')));
                    if (maxDrift <= 6.0 && !paragraphColumns && !colonKeyValues)
                    {
                        isValidTable = true;
                    }
                }

                if (isValidTable)
                {
                    var tbl = new LayoutRegion
                    {
                        IsTable = true,
                        ColumnAnchors = anchors,
                        TableRows = rows
                    };
                    tbl.Lines.AddRange(candidate);
                    regions.Add(tbl);
                    candidate.Clear();
                    return;
                }
            }

            var nonTable = new LayoutRegion { IsTable = false };
            nonTable.Lines.AddRange(candidate);
            regions.Add(nonTable);
            candidate.Clear();
        }

        private static W.Paragraph CreateEmptyEnterParagraph(string fontName = "Calibri", double fontSizePt = 10.0)
        {
            string halfPt = ((int)Math.Round(Math.Clamp(fontSizePt, 6.0, 24.0) * 2.0)).ToString(CultureInfo.InvariantCulture);
            var pPr = new W.ParagraphProperties(
                new W.SpacingBetweenLines
                {
                    Before = "0",
                    After = "0",
                    Line = "240",
                    LineRule = W.LineSpacingRuleValues.Auto
                },
                new W.ParagraphMarkRunProperties(
                    new W.RunFonts { Ascii = fontName, HighAnsi = fontName, ComplexScript = fontName },
                    new W.FontSize { Val = halfPt },
                    new W.FontSizeComplexScript { Val = halfPt }));

            return new W.Paragraph(pPr);
        }

        private static void AppendEmptyEnterParagraphsForGap(
            OpenXmlCompositeElement container,
            ref double rawGapBeforePt,
            string fontName,
            double fontSizePt = 10.0)
        {
            var enters = GenerateEmptyEnterParagraphsForGap(ref rawGapBeforePt, fontName, fontSizePt);
            foreach (var p in enters)
            {
                container.Append(p);
            }
        }

        private static List<W.Paragraph> GenerateEmptyEnterParagraphsForGap(
            ref double rawGapBeforePt,
            string fontName,
            double fontSizePt = 10.0)
        {
            var list = new List<W.Paragraph>();
            double singleEnterPt = Math.Clamp(fontSizePt * 1.18, 11.5, 14.5);

            if (rawGapBeforePt >= 13.5)
            {
                double effectiveGapPt = rawGapBeforePt > 60.0 ? rawGapBeforePt * 0.90 : rawGapBeforePt;
                int enterCount = Math.Clamp((int)Math.Round(effectiveGapPt / singleEnterPt), 1, 44);

                for (int i = 0; i < enterCount; i++)
                {
                    list.Add(CreateEmptyEnterParagraph(fontName, fontSizePt));
                }

                rawGapBeforePt = 0.0;
            }

            return list;
        }

        private static List<PdfRectangle> ExtractBitmapTableGridRects(SKBitmap bmp, double pageWidthPt, double pageHeightPt)
        {
            var rects = new List<PdfRectangle>();
            if (bmp.Width < 100 || bmp.Height < 100 || pageWidthPt <= 0 || pageHeightPt <= 0) return rects;

            double scaleX = bmp.Width / pageWidthPt;
            double scaleY = bmp.Height / pageHeightPt;
            int minHLinePx = Math.Max(40, (int)Math.Round(110.0 * scaleX));
            int maxGapPx = Math.Max(3, (int)Math.Round(1.5 * scaleX));
            var rawHRules = new List<(int Y, int X0, int X1)>();

            for (int y = 0; y < bmp.Height; y++)
            {
                int runStart = -1;
                int gapPx = 0;
                for (int x = 0; x < bmp.Width; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    bool dark = (c.Red + c.Green + c.Blue) / 3 < 200;
                    if (dark)
                    {
                        if (runStart < 0) runStart = x;
                        gapPx = 0;
                    }
                    else if (runStart >= 0)
                    {
                        gapPx++;
                        if (gapPx > maxGapPx)
                        {
                            int runEnd = x - gapPx;
                            if (runEnd - runStart >= minHLinePx) rawHRules.Add((y, runStart, runEnd));
                            runStart = -1;
                            gapPx = 0;
                        }
                    }
                }
                if (runStart >= 0 && bmp.Width - runStart >= minHLinePx)
                {
                    rawHRules.Add((y, runStart, bmp.Width - 1));
                }
            }

            if (rawHRules.Count < 2) return rects;

            int maxClusterGapY = Math.Max(5, (int)Math.Round(2.0 * scaleY));
            var hClusters = new List<List<(int Y, int X0, int X1)>>();
            foreach (var r in rawHRules)
            {
                if (hClusters.Count > 0 && r.Y - hClusters[^1][^1].Y <= maxClusterGapY)
                    hClusters[^1].Add(r);
                else
                    hClusters.Add(new List<(int Y, int X0, int X1)> { r });
            }

            var hLines = hClusters
                .Select(c => (
                    Y: (int)Math.Round(c.Average(x => x.Y)),
                    X0: c.Min(x => x.X0),
                    X1: c.Max(x => x.X1)))
                .ToList();

            // Group consecutive horizontal rules into table bands where vertical column lines connect them
            int winX = Math.Max(2, (int)Math.Round(0.85 * scaleX));
            int maxClusterGapX = Math.Max(5, (int)Math.Round(2.0 * scaleX));
            var bandCols = new List<(int TopRuleIdx, List<double> ColXsPt)>();
            for (int i = 0; i + 1 < hLines.Count; i++)
            {
                int y1 = hLines[i].Y + 2;
                int y2 = hLines[i + 1].Y - 2;
                int bandH = y2 - y1;
                if (bandH < 6 || bandH > (int)Math.Round(65.0 * scaleY)) continue;

                int xMin = Math.Max(winX, (int)Math.Round(45.0 * scaleX));
                int xMax = Math.Min(bmp.Width - winX - 1, (int)Math.Round((pageWidthPt - 30.0) * scaleX));
                var foundXs = new List<int>();
                for (int x = xMin; x <= xMax; x++)
                {
                    int darkRows = 0;
                    for (int y = y1; y < y2; y++)
                    {
                        bool anyDark = false;
                        for (int dx = -winX; dx <= winX; dx++)
                        {
                            var cp = bmp.GetPixel(x + dx, y);
                            if ((cp.Red + cp.Green + cp.Blue) / 3 < 202)
                            {
                                anyDark = true;
                                break;
                            }
                        }
                        if (anyDark) darkRows++;
                    }
                    if (darkRows >= bandH * 0.65) foundXs.Add(x);
                }

                var xClusters = new List<List<int>>();
                foreach (int fx in foundXs)
                {
                    if (xClusters.Count > 0 && fx - xClusters[^1][^1] <= maxClusterGapX)
                        xClusters[^1].Add(fx);
                    else
                        xClusters.Add(new List<int> { fx });
                }

                var colsPt = xClusters
                    .Where(xc => xc.Count <= Math.Max(12, (int)Math.Round(9.0 * scaleX)))
                    .Select(xc => xc.Average() / scaleX)
                    .ToList();

                if (colsPt.Count >= 3)
                {
                    bandCols.Add((i, colsPt));
                }
            }

            // Group contiguous bands into tables and emit aligned horizontal & vertical PdfRectangles
            int bIdx = 0;
            while (bIdx < bandCols.Count)
            {
                int startBand = bIdx;
                while (bIdx + 1 < bandCols.Count && bandCols[bIdx + 1].TopRuleIdx == bandCols[bIdx].TopRuleIdx + 1)
                {
                    bIdx++;
                }
                int firstRule = bandCols[startBand].TopRuleIdx;
                int lastRule = bandCols[bIdx].TopRuleIdx + 1;
                int rowCount = lastRule - firstRule;

                var allColCandidates = new List<double>();
                for (int k = startBand; k <= bIdx; k++) allColCandidates.AddRange(bandCols[k].ColXsPt);
                allColCandidates.Sort();

                var mergedColGroups = new List<List<double>>();
                foreach (double cx in allColCandidates)
                {
                    if (mergedColGroups.Count > 0 && cx - mergedColGroups[^1][^1] <= 6.0)
                        mergedColGroups[^1].Add(cx);
                    else
                        mergedColGroups.Add(new List<double> { cx });
                }

                int minRequiredRows = Math.Max(1, (int)Math.Ceiling(rowCount * 0.55));
                var canonicalCols = mergedColGroups
                    .Where(g => g.Count >= minRequiredRows)
                    .Select(g => g.Average())
                    .OrderBy(x => x)
                    .ToList();

                if (canonicalCols.Count >= 3)
                {
                    double leftX = canonicalCols[0];
                    double rightX = canonicalCols[^1];
                    double topY = pageHeightPt - (hLines[firstRule].Y / scaleY);
                    double botY = pageHeightPt - (hLines[lastRule].Y / scaleY);

                    for (int r = firstRule; r <= lastRule; r++)
                    {
                        double yPt = pageHeightPt - (hLines[r].Y / scaleY);
                        rects.Add(new PdfRectangle(leftX, yPt - 0.5, rightX, yPt + 0.5));
                    }
                    foreach (double cx in canonicalCols)
                    {
                        rects.Add(new PdfRectangle(cx - 0.5, botY, cx + 0.5, topY));
                    }
                }
                bIdx++;
            }

            return rects;
        }

        private static bool HasHorizontalDividerBetween(
            SKBitmap? fullPageBitmap,
            List<PdfRectangle>? vectorRects,
            double pageWidthPt,
            double pageHeightPt,
            double upperBottomPt,
            double lowerTopPt,
            double pageLeftMarginPt,
            double pageRightMarginPt)
        {
            bool hasVectorLine = vectorRects != null && vectorRects.Any(r =>
                r.Width >= pageWidthPt * 0.45 &&
                r.Height <= 4.0 &&
                r.Top <= upperBottomPt + 3.0 &&
                r.Bottom >= lowerTopPt - 3.0);

            if (hasVectorLine) return true;

            if (fullPageBitmap != null)
            {
                return DetectHorizontalDividerInBitmap(fullPageBitmap, pageWidthPt, pageHeightPt, upperBottomPt, lowerTopPt, pageLeftMarginPt, pageRightMarginPt);
            }

            return false;
        }

        private static bool TryExtractScannedHeaderEmblem(
            SKBitmap pageBitmap,
            double pageWidthPt,
            double pageHeightPt,
            double firstLineTopPt,
            List<SKRectI> croppedLogoRects,
            out byte[]? croppedPng,
            out double widthPt,
            out double heightPt,
            out double bottomPt)
        {
            croppedPng = null;
            widthPt = heightPt = bottomPt = 0;
            double maxTopZonePt = pageHeightPt - firstLineTopPt - 4.0;
            if (maxTopZonePt < 36.0 || maxTopZonePt > pageHeightPt * 0.35) return false;

            double scaleX = pageBitmap.Width / pageWidthPt;
            double scaleY = pageBitmap.Height / pageHeightPt;
            int x1 = Math.Clamp((int)Math.Round(pageWidthPt * 0.25 * scaleX), 0, pageBitmap.Width - 1);
            int x2 = Math.Clamp((int)Math.Round(pageWidthPt * 0.75 * scaleX), x1 + 1, pageBitmap.Width);
            int y1 = Math.Clamp((int)Math.Round(10.0 * scaleY), 0, pageBitmap.Height - 1);
            int y2 = Math.Clamp((int)Math.Round((pageHeightPt - firstLineTopPt - 4.0) * scaleY), y1 + 1, pageBitmap.Height);

            int minX = x2, maxX = x1, minY = y2, maxY = y1, inkCount = 0;
            for (int y = y1; y < y2; y++)
            {
                for (int x = x1; x < x2; x++)
                {
                    var c = pageBitmap.GetPixel(x, y);
                    if (c.Red < 232 || c.Green < 232 || c.Blue < 232)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                        inkCount++;
                    }
                }
            }

            if (inkCount < 450 || maxX <= minX || maxY <= minY) return false;
            double wPt = (maxX - minX) / scaleX;
            double hPt = (maxY - minY) / scaleY;
            double centerXPt = ((minX + maxX) * 0.5) / scaleX;
            if (wPt < 28.0 || wPt > 180.0 || hPt < 28.0 || hPt > 155.0) return false;
            if (Math.Abs(centerXPt - pageWidthPt * 0.5) > 42.0) return false;

            int pad = 4;
            var rect = new SKRectI(
                Math.Max(0, minX - pad),
                Math.Max(0, minY - pad),
                Math.Min(pageBitmap.Width, maxX + pad),
                Math.Min(pageBitmap.Height, maxY + pad));
            using var subset = new SKBitmap();
            if (!pageBitmap.ExtractSubset(subset, rect)) return false;

            using var skImg = SKImage.FromBitmap(subset);
            using var encoded = skImg.Encode(SKEncodedImageFormat.Png, 100);
            croppedPng = encoded.ToArray();
            widthPt = wPt;
            heightPt = hPt;
            bottomPt = pageHeightPt - (rect.Bottom / scaleY);
            croppedLogoRects.Add(rect);
            return true;
        }

        private static void ExtractScannedWetStampsAsFloatingImages(
            SKBitmap pageBitmap,
            double pageWidthPt,
            double pageHeightPt,
            IReadOnlyList<VisualLine> lines,
            List<SKRectI> croppedLogoRects,
            List<StandaloneImageItem> floatingImages)
        {
            if (pageBitmap.Width < 120 || pageBitmap.Height < 120 || pageWidthPt <= 0 || pageHeightPt <= 0)
                return;

            double scaleX = pageBitmap.Width / pageWidthPt;
            double scaleY = pageBitmap.Height / pageHeightPt;

            var colColoredCounts = new int[pageBitmap.Width];
            for (int x = 0; x < pageBitmap.Width; x++)
            {
                int count = 0;
                for (int y = 0; y < pageBitmap.Height; y += 2)
                {
                    if (croppedLogoRects.Any(r => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom))
                        continue;
                    var c = pageBitmap.GetPixel(x, y);
                    int maxC = Math.Max(c.Red, Math.Max(c.Green, c.Blue));
                    int minC = Math.Min(c.Red, Math.Min(c.Green, c.Blue));
                    int sat = maxC - minC;
                    int lum = (c.Red + c.Green + c.Blue) / 3;
                    if (sat >= 26 && lum >= 25 && lum < 230)
                        count += 2;
                }
                colColoredCounts[x] = count;
            }

            var activeXsPt = new List<double>();
            for (int x = 0; x < pageBitmap.Width; x++)
            {
                if (colColoredCounts[x] >= 8)
                    activeXsPt.Add(x / scaleX);
            }
            if (activeXsPt.Count < 15) return;

            var xClusters = new List<List<double>>();
            foreach (double xPt in activeXsPt)
            {
                if (xClusters.Count > 0 && xPt - xClusters[^1][^1] <= 12.0)
                    xClusters[^1].Add(xPt);
                else
                    xClusters.Add(new List<double> { xPt });
            }

            foreach (var xc in xClusters)
            {
                double x0Pt = xc.Min();
                double x1Pt = xc.Max();
                double wPt = x1Pt - x0Pt;
                if (wPt < 36.0 || wPt > 250.0) continue;

                int ix0 = Math.Clamp((int)Math.Floor(x0Pt * scaleX), 0, pageBitmap.Width - 1);
                int ix1 = Math.Clamp((int)Math.Ceiling(x1Pt * scaleX), ix0 + 1, pageBitmap.Width);

                int minY = pageBitmap.Height, maxY = 0, coloredTotal = 0;
                for (int y = 0; y < pageBitmap.Height; y++)
                {
                    int rowColored = 0;
                    for (int x = ix0; x < ix1; x += 2)
                    {
                        if (croppedLogoRects.Any(r => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom))
                            continue;
                        var c = pageBitmap.GetPixel(x, y);
                        int maxC = Math.Max(c.Red, Math.Max(c.Green, c.Blue));
                        int minC = Math.Min(c.Red, Math.Min(c.Green, c.Blue));
                        int sat = maxC - minC;
                        int lum = (c.Red + c.Green + c.Blue) / 3;
                        if (sat >= 26 && lum >= 25 && lum < 230)
                            rowColored += 2;
                    }
                    if (rowColored >= 4)
                    {
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                        coloredTotal += rowColored;
                    }
                }

                if (coloredTotal < 220 || maxY <= minY) continue;
                double topPt = pageHeightPt - (minY / scaleY);
                double bottomPt = pageHeightPt - (maxY / scaleY);
                double hPt = topPt - bottomPt;
                if (hPt < 34.0 || hPt > 195.0) continue;

                // Must overlap or be adjacent to text lines (signature/stamp zone), not an isolated top header logo
                bool overlapsOrNearText = lines.Any(l =>
                    l.Right >= x0Pt - 25.0 && l.Left <= x1Pt + 25.0
                    && l.Top >= bottomPt - 18.0 && l.Bottom <= topPt + 18.0);
                if (!overlapsOrNearText) continue;

                int padX = Math.Max(4, (int)Math.Round(6.0 * scaleX));
                int padY = Math.Max(4, (int)Math.Round(6.0 * scaleY));
                var cropRect = new SKRectI(
                    Math.Max(0, ix0 - padX),
                    Math.Max(0, minY - padY),
                    Math.Min(pageBitmap.Width, ix1 + padX),
                    Math.Min(pageBitmap.Height, maxY + padY));

                croppedLogoRects.Add(cropRect);

                int cropW = cropRect.Width;
                int cropH = cropRect.Height;
                if (cropW < 16 || cropH < 16) continue;

                using var transBmp = new SKBitmap(cropW, cropH, SKColorType.Bgra8888, SKAlphaType.Premul);
                transBmp.Erase(SKColors.Transparent);

                // Precollect word boxes inside this cropRect so neutral black laser-printed text is not duplicated in the stamp overlay
                var localWordRects = new List<SKRectI>();
                foreach (var w in lines.SelectMany(l => l.Words))
                {
                    int wx1 = (int)Math.Floor(w.Left * scaleX) - cropRect.Left - 1;
                    int wx2 = (int)Math.Ceiling(w.Right * scaleX) - cropRect.Left + 1;
                    int wy1 = (int)Math.Floor((pageHeightPt - w.Top) * scaleY) - cropRect.Top - 1;
                    int wy2 = (int)Math.Ceiling((pageHeightPt - w.Bottom) * scaleY) - cropRect.Top + 1;
                    if (wx2 > 0 && wx1 < cropW && wy2 > 0 && wy1 < cropH)
                    {
                        localWordRects.Add(new SKRectI(Math.Max(0, wx1), Math.Max(0, wy1), Math.Min(cropW, wx2), Math.Min(cropH, wy2)));
                    }
                }

                int visibleStampPx = 0;
                for (int py = 0; py < cropH; py++)
                {
                    int srcY = cropRect.Top + py;
                    for (int px = 0; px < cropW; px++)
                    {
                        int srcX = cropRect.Left + px;
                        var c = pageBitmap.GetPixel(srcX, srcY);
                        int maxC = Math.Max(c.Red, Math.Max(c.Green, c.Blue));
                        int minC = Math.Min(c.Red, Math.Min(c.Green, c.Blue));
                        int sat = maxC - minC;
                        int lum = (c.Red + c.Green + c.Blue) / 3;

                        // Paper background -> transparent
                        if (lum >= 225 && sat < 22) continue;

                        // Neutral dark pixel inside a recognized text word -> transparent so editable text underneath stays crisp
                        if (sat < 22 && lum < 168 && localWordRects.Any(r => px >= r.Left && px < r.Right && py >= r.Top && py < r.Bottom))
                            continue;

                        byte alpha = (byte)Math.Clamp((245 - lum) * 2, 75, 220);
                        transBmp.SetPixel(px, py, new SKColor(c.Red, c.Green, c.Blue, alpha));
                        visibleStampPx++;
                    }
                }

                if (visibleStampPx < 150) continue;

                using var skImg = SKImage.FromBitmap(transBmp);
                using var encoded = skImg.Encode(SKEncodedImageFormat.Png, 100);
                byte[] pngBytes = encoded.ToArray();

                double finalLeftPt = cropRect.Left / scaleX;
                double finalRightPt = cropRect.Right / scaleX;
                double finalTopPt = pageHeightPt - (cropRect.Top / scaleY);
                double finalBottomPt = pageHeightPt - (cropRect.Bottom / scaleY);

                floatingImages.Add(new StandaloneImageItem(
                    pngBytes,
                    finalTopPt,
                    finalBottomPt,
                    finalLeftPt,
                    finalRightPt,
                    finalRightPt - finalLeftPt,
                    finalTopPt - finalBottomPt,
                    ForceInFrontOfText: true));
            }
        }

        private static bool TryDetectAndCropGraphicLogo(
            SKBitmap pageBitmap,
            double pageWidthPt,
            double pageHeightPt,
            VisualLine line,
            out byte[]? croppedPng,
            out double widthPt,
            out double heightPt,
            out double leftPt,
            out List<WordSegment> remainingRightSegments,
            List<SKRectI>? croppedLogoRects = null,
            IReadOnlyList<VisualLine>? allPageLines = null)
        {
            croppedPng = null;
            widthPt = heightPt = leftPt = 0;
            remainingRightSegments = new List<WordSegment>();

            var logoSegments = new List<WordSegment> { line.Segments[0] };
            int segIdx = 1;
            while (segIdx < line.Segments.Count)
            {
                var prevSeg = logoSegments.Last();
                var curSeg = line.Segments[segIdx];
                if ((curSeg.Left - prevSeg.Right) <= 32.0 && (curSeg.Right - logoSegments[0].Left) <= 180.0)
                {
                    logoSegments.Add(curSeg);
                    segIdx++;
                }
                else
                {
                    break;
                }
            }

            int totalLogoWords = logoSegments.Sum(s => s.Words.Count);
            double clusterLeftPt = logoSegments.Min(s => s.Left);
            double clusterRightPt = logoSegments.Max(s => s.Right);
            double clusterBottomPt = logoSegments.Min(s => s.Bottom);
            double clusterTopPt = logoSegments.Max(s => s.Top);

            if (totalLogoWords > 3 || (clusterRightPt - clusterLeftPt) > 190.0)
                return false;

            if (logoSegments.SelectMany(s => s.Words).Any(w => w.Text.Count(char.IsLetter) >= 4 && w.Text.All(c => !char.IsLetter(c) || char.IsUpper(c))))
                return false;

            double scaleX = pageBitmap.Width / pageWidthPt;
            double scaleY = pageBitmap.Height / pageHeightPt;

            int seedX1 = Math.Clamp((int)Math.Floor(Math.Max(12.0, clusterLeftPt - 42.0) * scaleX), 0, pageBitmap.Width - 1);
            int seedX2 = Math.Clamp((int)Math.Ceiling(Math.Min(pageWidthPt - 12.0, clusterRightPt + 12.0) * scaleX), seedX1 + 1, pageBitmap.Width);
            int seedY1 = Math.Clamp((int)Math.Floor((pageHeightPt - Math.Min(pageHeightPt - 8.0, clusterTopPt + 8.0)) * scaleY), 0, pageBitmap.Height - 1);
            int seedY2 = Math.Clamp((int)Math.Ceiling((pageHeightPt - Math.Max(8.0, clusterBottomPt - 8.0)) * scaleY), seedY1 + 1, pageBitmap.Height);

            int[] hueBins = new int[6];
            for (int y = seedY1; y < seedY2; y++)
            {
                for (int x = seedX1; x < seedX2; x++)
                {
                    var c = pageBitmap.GetPixel(x, y);
                    c.ToHsv(out float h, out float s, out float v);
                    if (s >= 28f && v >= 25f && v <= 248f)
                    {
                        int bin = Math.Clamp((int)(h / 60f), 0, 5);
                        hueBins[bin]++;
                    }
                }
            }

            if (hueBins.Count(count => count >= 12) < 2)
                return false;

            double maxAllowedRightPt = segIdx < line.Segments.Count
                ? line.Segments[segIdx].Left - 12.0
                : Math.Min(pageWidthPt - 20.0, clusterRightPt + 85.0);

            int limitLeftPx = Math.Clamp((int)Math.Floor(10.0 * scaleX), 0, seedX1);
            int limitRightPx = Math.Clamp((int)Math.Ceiling(maxAllowedRightPt * scaleX), seedX2, pageBitmap.Width);
            int limitTopPx = Math.Clamp(seedY1 - (int)(14.0 * scaleY), 0, seedY1);
            int limitBottomPx = Math.Clamp(seedY2 + (int)(14.0 * scaleY), seedY2, pageBitmap.Height);

            int expandY1 = seedY1;
            int whiteRowsUp = 0;
            for (int y = seedY1; y >= limitTopPx; y--)
            {
                if (RowHasInk(pageBitmap, seedX1, seedX2, y))
                {
                    expandY1 = y;
                    whiteRowsUp = 0;
                }
                else
                {
                    whiteRowsUp++;
                    if (whiteRowsUp >= 6) break;
                }
            }

            int expandY2 = seedY2;
            int whiteRowsDown = 0;
            for (int y = seedY2; y < limitBottomPx; y++)
            {
                if (RowHasInk(pageBitmap, seedX1, seedX2, y))
                {
                    expandY2 = y;
                    whiteRowsDown = 0;
                }
                else
                {
                    whiteRowsDown++;
                    if (whiteRowsDown >= 6) break;
                }
            }

            int expandX1 = seedX1;
            int whiteColsLeft = 0;
            for (int x = seedX1; x >= limitLeftPx; x--)
            {
                if (ColHasInk(pageBitmap, expandY1, expandY2, x))
                {
                    expandX1 = x;
                    whiteColsLeft = 0;
                }
                else
                {
                    whiteColsLeft++;
                    if (whiteColsLeft >= 14) break;
                }
            }

            int expandX2 = seedX2;
            int whiteColsRight = 0;
            for (int x = seedX1 + (seedX2 - seedX1) / 2; x < limitRightPx; x++)
            {
                if (ColHasInk(pageBitmap, expandY1, expandY2, x))
                {
                    if (x > expandX2) expandX2 = x;
                    whiteColsRight = 0;
                }
                else
                {
                    whiteColsRight++;
                    if (x > seedX2 && whiteColsRight >= 16) break;
                }
            }

            for (int y = expandY2; y < limitBottomPx; y++)
            {
                if (RowHasInk(pageBitmap, expandX1, expandX2, y)) expandY2 = y;
                else if (y > expandY2 + 4) break;
            }
            for (int y = expandY1; y >= limitTopPx; y--)
            {
                if (RowHasInk(pageBitmap, expandX1, expandX2, y)) expandY1 = y;
                else if (y < expandY1 - 4) break;
            }

            int minX = expandX2, maxX = expandX1, minY = expandY2, maxY = expandY1;
            bool foundPixel = false;
            for (int y = expandY1; y <= expandY2 && y < pageBitmap.Height; y++)
            {
                for (int x = expandX1; x <= expandX2 && x < pageBitmap.Width; x++)
                {
                    var c = pageBitmap.GetPixel(x, y);
                    if (c.Red < 244 || c.Green < 244 || c.Blue < 244)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                        foundPixel = true;
                    }
                }
            }

            if (!foundPixel || (maxX - minX) < 12 || (maxY - minY) < 8)
                return false;

            int pad = 6;
            minX = Math.Max(0, minX - pad);
            minY = Math.Max(0, minY - pad);
            maxX = Math.Min(pageBitmap.Width, maxX + pad);
            maxY = Math.Min(pageBitmap.Height, maxY + pad);

            var cropRect = new SKRectI(minX, minY, maxX, maxY);
            if (croppedLogoRects != null)
            {
                bool alreadyCropped = croppedLogoRects.Any(existing =>
                {
                    int interL = Math.Max(existing.Left, cropRect.Left);
                    int interT = Math.Max(existing.Top, cropRect.Top);
                    int interR = Math.Min(existing.Right, cropRect.Right);
                    int interB = Math.Min(existing.Bottom, cropRect.Bottom);
                    if (interR <= interL || interB <= interT) return false;
                    double interArea = (double)(interR - interL) * (interB - interT);
                    double minArea = Math.Max(1.0, Math.Min((double)existing.Width * existing.Height, (double)cropRect.Width * cropRect.Height));
                    return interArea / minArea >= 0.35;
                });
                if (alreadyCropped) return false;
                croppedLogoRects.Add(cropRect);
            }

            using var subset = new SKBitmap();
            if (!pageBitmap.ExtractSubset(subset, cropRect))
                return false;

            // Mask out neutral black-toner printed words inside the cropped stamp/logo so editable text is not duplicated inside the image
            if (allPageLines != null)
            {
                using var cleanSubset = subset.Copy();
                foreach (var w in allPageLines.SelectMany(l => l.Words))
                {
                    int wx1 = Math.Clamp((int)Math.Floor(w.Left * scaleX) - cropRect.Left - 1, 0, cleanSubset.Width - 1);
                    int wx2 = Math.Clamp((int)Math.Ceiling(w.Right * scaleX) - cropRect.Left + 1, wx1 + 1, cleanSubset.Width);
                    int wy1 = Math.Clamp((int)Math.Floor((pageHeightPt - w.Top) * scaleY) - cropRect.Top - 1, 0, cleanSubset.Height - 1);
                    int wy2 = Math.Clamp((int)Math.Ceiling((pageHeightPt - w.Bottom) * scaleY) - cropRect.Top + 1, wy1 + 1, cleanSubset.Height);
                    for (int py = wy1; py < wy2; py++)
                    {
                        for (int px = wx1; px < wx2; px++)
                        {
                            var pc = cleanSubset.GetPixel(px, py);
                            int maxC = Math.Max(pc.Red, Math.Max(pc.Green, pc.Blue));
                            int minC = Math.Min(pc.Red, Math.Min(pc.Green, pc.Blue));
                            int lum = (pc.Red + pc.Green + pc.Blue) / 3;
                            if (lum < 155 && (maxC - minC) < 26)
                            {
                                cleanSubset.SetPixel(px, py, SKColors.White);
                            }
                        }
                    }
                }
                using var skImg = SKImage.FromBitmap(cleanSubset);
                using var encoded = skImg.Encode(SKEncodedImageFormat.Png, 100);
                croppedPng = encoded.ToArray();
            }
            else
            {
                using var skImg = SKImage.FromBitmap(subset);
                using var encoded = skImg.Encode(SKEncodedImageFormat.Png, 100);
                croppedPng = encoded.ToArray();
            }

            leftPt = minX / scaleX;
            widthPt = (maxX - minX) / scaleX;
            heightPt = (maxY - minY) / scaleY;

            for (int i = segIdx; i < line.Segments.Count; i++)
            {
                remainingRightSegments.Add(line.Segments[i]);
            }

            return true;
        }

        private static bool RowHasInk(SKBitmap bmp, int x1, int x2, int y)
        {
            if (y < 0 || y >= bmp.Height) return false;
            int start = Math.Clamp(x1, 0, bmp.Width - 1);
            int end = Math.Clamp(x2, start, bmp.Width - 1);
            for (int x = start; x <= end; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Red < 242 || c.Green < 242 || c.Blue < 242) return true;
            }
            return false;
        }

        private static bool ColHasInk(SKBitmap bmp, int y1, int y2, int x)
        {
            if (x < 0 || x >= bmp.Width) return false;
            int start = Math.Clamp(y1, 0, bmp.Height - 1);
            int end = Math.Clamp(y2, start, bmp.Height - 1);
            for (int y = start; y <= end; y++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Red < 242 || c.Green < 242 || c.Blue < 242) return true;
            }
            return false;
        }

        private static int ComputeSpacingBeforeDxa(double gapPt)
        {
            if (gapPt <= 1.2) return 0;
            if (gapPt <= 45.0) return (int)Math.Round(gapPt * 19.0);
            return (int)Math.Round(Math.Clamp(gapPt * 18.5, 800.0, 11800.0));
        }

        private static readonly Regex TocDotLeaderLineRegex = new(
            @"^(.+?)\s*([.·•]{5,}|[-_]{5,})\s*([ivxlcdmIVXLCDM]{1,6}|\d{1,4})\s*$",
            RegexOptions.Compiled);

        private static W.Paragraph? TryBuildTocDotLeaderParagraph(
            VisualLine line,
            IReadOnlyList<VisualLine> regionLines,
            int lineIdx,
            double leftMargin,
            double rightLimit,
            double gap,
            out double nextPrevBottomPt)
        {
            nextPrevBottomPt = 0;
            if (line.Right < leftMargin + (rightLimit - leftMargin) * 0.65) return null;
            var m = TocDotLeaderLineRegex.Match(line.FullText);
            if (!m.Success) return null;

            string leaderChars = m.Groups[2].Value;
            var leaderVal = leaderChars[0] switch
            {
                '-' => W.TabStopLeaderCharValues.Hyphen,
                '_' => W.TabStopLeaderCharValues.Underscore,
                _ => W.TabStopLeaderCharValues.Dot
            };

            var titleWords = new List<RichWord>();
            var pageWords = new List<RichWord>();
            bool seenDots = false;
            for (int wIdx = 0; wIdx < line.Words.Count; wIdx++)
            {
                var rw = line.Words[wIdx];
                string wt = rw.Text.Trim();
                if (!seenDots)
                {
                    if (Regex.IsMatch(wt, @"^[.·•\-_]{3,}$"))
                    {
                        seenDots = true;
                        continue;
                    }
                    var stripTrailingDots = Regex.Match(wt, @"^(.+?)[.·•\-_]{3,}$");
                    if (stripTrailingDots.Success)
                    {
                        titleWords.Add(rw with { Text = stripTrailingDots.Groups[1].Value.TrimEnd() });
                        seenDots = true;
                        continue;
                    }
                    titleWords.Add(rw);
                }
                else
                {
                    if (Regex.IsMatch(wt, @"^[.·•\-_]+$")) continue;
                    string cleanPage = Regex.Replace(wt, @"^[.·•\-_]+", "");
                    if (!string.IsNullOrEmpty(cleanPage))
                    {
                        pageWords.Add(rw with { Text = cleanPage });
                    }
                }
            }
            if (titleWords.Count == 0 || pageWords.Count == 0) return null;

            double height = line.FontSizePt * 1.1;
            double after = lineIdx + 1 < regionLines.Count
                ? Math.Max(0, line.Top - regionLines[lineIdx + 1].Top - height)
                : 0;
            nextPrevBottomPt = line.Top - height - after;

            int leftIndentDxa = (int)Math.Round(Math.Max(0, titleWords[0].Left - leftMargin) * 20);
            int rightTabDxa = (int)Math.Round(Math.Max(leftIndentDxa + 720, (line.Right - leftMargin) * 20));

            var pPr = new W.ParagraphProperties(
                new W.Tabs(new W.TabStop
                {
                    Val = W.TabStopValues.Right,
                    Leader = leaderVal,
                    Position = rightTabDxa
                }),
                new W.SpacingBetweenLines
                {
                    Before = Math.Round(gap * 20).ToString(CultureInfo.InvariantCulture),
                    After = Math.Round(after * 20).ToString(CultureInfo.InvariantCulture),
                    Line = Math.Round(Math.Max(line.FontSizePt, height) * 20).ToString(CultureInfo.InvariantCulture),
                    LineRule = W.LineSpacingRuleValues.Exact
                },
                new W.Indentation { Left = leftIndentDxa.ToString(CultureInfo.InvariantCulture) });

            var para = new W.Paragraph(pPr);
            AppendConsolidatedRuns(para, titleWords, 0);
            string halfPt = ((int)Math.Round(Math.Clamp(line.FontSizePt, 6.0, 72.0) * 2.0)).ToString(CultureInfo.InvariantCulture);
            para.Append(new W.Run(new W.RunProperties(new W.FontSize { Val = halfPt }, new W.FontSizeComplexScript { Val = halfPt }), new W.TabChar()));
            AppendConsolidatedRuns(para, pageWords, 0);
            return para;
        }

        private static M.Run CreateMathRunElement(string text, double fontSizePt = 10.5, string hexColor = "000000", bool italic = true)
        {
            string halfPt = ((int)Math.Round(Math.Clamp(fontSizePt, 6.0, 72.0) * 2.0)).ToString(CultureInfo.InvariantCulture);
            var mRPr = new M.RunProperties();
            if (!italic)
            {
                mRPr.Append(new M.NormalText());
            }
            var wRPr = new W.RunProperties(
                new W.RunFonts { Ascii = "Cambria Math", HighAnsi = "Cambria Math", ComplexScript = "Cambria Math" },
                new W.Color { Val = hexColor },
                new W.FontSize { Val = halfPt },
                new W.FontSizeComplexScript { Val = halfPt });
            if (italic) wRPr.Append(new W.Italic());
            return new M.Run(mRPr, wRPr, new M.Text(text));
        }

        private static W.Paragraph? TryBuildMathEquationParagraph(
            IReadOnlyList<VisualLine> regionLines,
            int startIdx,
            Page page,
            HashSet<UglyToad.PdfPig.Graphics.PdfPath> consumedPaths,
            double leftMargin,
            double rightLimit,
            double gap,
            out int consumedLineCount,
            out double nextPrevBottomPt)
        {
            consumedLineCount = 0;
            nextPrevBottomPt = 0;
            if (startIdx >= regionLines.Count) return null;
            var line = regionLines[startIdx];
            double usableWidth = Math.Max(200.0, rightLimit - leftMargin);

            // 1. Check for a horizontal fraction bar directly below numerator words on `line` with denominator words on `regionLines[startIdx + 1]`
            if (startIdx + 1 < regionLines.Count)
            {
                var nextLine = regionLines[startIdx + 1];
                var fractionBarCandidate = page.ExperimentalAccess.Paths
                    .Select(p => (Path: p, Bounds: GetPathBoundingRectangle(p)))
                    .Where(x => x.Bounds.HasValue
                        && x.Bounds.Value.Width >= 14.0
                        && x.Bounds.Value.Width <= usableWidth * 0.72
                        && x.Bounds.Value.Height <= 2.4
                        && x.Bounds.Value.Top <= line.Top + 1.5
                        && x.Bounds.Value.Bottom >= nextLine.Bottom - 1.5
                        && x.Bounds.Value.Left >= line.Left - 4.0
                        && x.Bounds.Value.Right <= line.Right + 12.0
                        && nextLine.Left >= x.Bounds.Value.Left - 10.0
                        && nextLine.Right <= x.Bounds.Value.Right + 10.0)
                    .OrderByDescending(x => x.Bounds!.Value.Width)
                    .FirstOrDefault();

                if (fractionBarCandidate.Bounds.HasValue)
                {
                    var bar = fractionBarCandidate.Bounds.Value;
                    var prefixWords = line.Words.Where(w => (w.Left + w.Right) * 0.5 < bar.Left - 1.5).ToList();
                    var numWords = line.Words.Where(w => (w.Left + w.Right) * 0.5 >= bar.Left - 1.5 && (w.Left + w.Right) * 0.5 <= bar.Right + 1.5).ToList();
                    var suffixWords = line.Words.Where(w => (w.Left + w.Right) * 0.5 > bar.Right + 1.5).ToList();
                    var denWords = nextLine.Words.ToList();

                    string candPrefixStr = string.Join(" ", prefixWords.Select(w => w.Text));
                    string candNumStr = string.Join(" ", numWords.Select(w => w.Text));
                    string candDenStr = string.Join(" ", denWords.Select(w => w.Text));
                    string candSuffixStr = string.Join(" ", suffixWords.Select(w => w.Text));
                    bool hasEqualityContext = candPrefixStr.Contains('=') || candSuffixStr.Contains('=');
                    bool numHasMathTokens = Regex.IsMatch(candNumStr, @"(?:[+−×÷=≤≥≈∑∏√()]|\s-\s|\d)");
                    bool denHasMathTokens = Regex.IsMatch(candDenStr, @"(?:[+−×÷=≤≥≈∑∏√()]|\s-\s|\d)");
                    bool hasMathSyntax = hasEqualityContext || (numHasMathTokens && denHasMathTokens);
                    if (numWords.Count > 0 && denWords.Count > 0 && hasMathSyntax)
                    {
                        consumedPaths.Add(fractionBarCandidate.Path);
                        string prefixStr = candPrefixStr;
                        string numStr = candNumStr;
                        string denStr = candDenStr;
                        string suffixStr = candSuffixStr;

                        int openParens = prefixStr.Count(c => c == '(') - prefixStr.Count(c => c == ')');
                        if (openParens > 0 && string.IsNullOrEmpty(suffixStr))
                        {
                            if (numStr.EndsWith(')'))
                            {
                                numStr = numStr[..^1].TrimEnd();
                            }
                            suffixStr = new string(')', openParens);
                        }

                        consumedLineCount = 2;
                        double height = (line.Top - nextLine.Bottom) + 4.0;
                        double after = startIdx + 2 < regionLines.Count
                            ? Math.Max(0, nextLine.Bottom - regionLines[startIdx + 2].Top - 2.0)
                            : 0;
                        nextPrevBottomPt = line.Top - height - after;

                        var oMath = new M.OfficeMath();
                        double pt = line.FontSizePt;
                        string col = line.Words[0].HexColor;
                        if (!string.IsNullOrEmpty(prefixStr))
                        {
                            oMath.Append(CreateMathRunElement(prefixStr + " ", pt, col, italic: false));
                        }
                        var frac = new M.Fraction(
                            new M.FractionProperties(new M.FractionType { Val = M.FractionTypeValues.Bar }),
                            new M.Numerator(CreateMathRunElement(numStr, pt, col, italic: false)),
                            new M.Denominator(CreateMathRunElement(denStr, pt, col, italic: false)));
                        oMath.Append(frac);
                        if (!string.IsNullOrEmpty(suffixStr))
                        {
                            oMath.Append(CreateMathRunElement(suffixStr, pt, col, italic: false));
                        }

                        var wPara = new W.Paragraph(
                            new W.ParagraphProperties(
                                new W.SpacingBetweenLines
                                {
                                    Before = Math.Round(gap * 20).ToString(CultureInfo.InvariantCulture),
                                    After = Math.Round(after * 20).ToString(CultureInfo.InvariantCulture),
                                    Line = Math.Round(Math.Max(line.FontSizePt * 1.3, height) * 20).ToString(CultureInfo.InvariantCulture),
                                    LineRule = W.LineSpacingRuleValues.AtLeast
                                },
                                new W.Indentation
                                {
                                    Left = Math.Round(Math.Max(0, line.Left - leftMargin) * 20).ToString(CultureInfo.InvariantCulture)
                                },
                                new W.Justification { Val = W.JustificationValues.Center }),
                            oMath);
                        return wPara;
                    }
                }
            }

            // 2. Check for standalone centered display equation line (e.g., "isRot270 = (|shapeRotDeg - 270.0| ≤ 1.0) ∨ isVert270")
            string fullText = line.FullText.Trim();
            bool isCenteredDisplayLine = line.Segments.Count <= 1
                && line.Left >= leftMargin + 85.0
                && (line.Right - line.Left) <= usableWidth * 0.78
                && fullText.Contains('=')
                && !fullText.StartsWith('<')
                && !fullText.EndsWith(':')
                && !fullText.EndsWith('.')
                && (fullText.IndexOfAny(new[] { '≤', '≥', '≈', '∨', '∧', '+', '-', '|', '×' }) >= 0
                    || Regex.IsMatch(fullText, @"^[A-Za-z_]\w*\s*=\s*[A-Za-z0-9_(|]"));

            if (isCenteredDisplayLine)
            {
                consumedLineCount = 1;
                double height = line.FontSizePt * 1.15;
                double after = startIdx + 1 < regionLines.Count
                    ? Math.Max(0, line.Top - regionLines[startIdx + 1].Top - height)
                    : 0;
                nextPrevBottomPt = line.Top - height - after;

                var oMath = new M.OfficeMath(CreateMathRunElement(fullText, line.FontSizePt, line.Words[0].HexColor, italic: false));

                return new W.Paragraph(
                    new W.ParagraphProperties(
                        new W.SpacingBetweenLines
                        {
                            Before = Math.Round(gap * 20).ToString(CultureInfo.InvariantCulture),
                            After = Math.Round(after * 20).ToString(CultureInfo.InvariantCulture),
                            Line = Math.Round(Math.Max(line.FontSizePt, height) * 20).ToString(CultureInfo.InvariantCulture),
                            LineRule = W.LineSpacingRuleValues.Exact
                        },
                        new W.Indentation
                        {
                            Left = Math.Round(Math.Max(0, line.Left - leftMargin) * 20).ToString(CultureInfo.InvariantCulture)
                        },
                        new W.Justification { Val = W.JustificationValues.Center }),
                    oMath);
            }

            return null;
        }

        private static bool IsHorizontallyCenteredLine(VisualLine line, double leftMargin, double rightLimit, double pageWidthPt)
        {
            double usableWidth = Math.Max(180.0, rightLimit - leftMargin);
            double lineSpan = line.Right - line.Left;
            if (lineSpan <= 0 || lineSpan >= usableWidth * 0.86) return false;
            if (line.Left <= leftMargin + 18.0 || line.Right >= rightLimit - 18.0) return false;
            double lineMid = (line.Left + line.Right) * 0.5;
            double marginMid = (leftMargin + rightLimit) * 0.5;
            double pageMid = pageWidthPt * 0.5;
            return Math.Abs(lineMid - marginMid) <= 18.0 || Math.Abs(lineMid - pageMid) <= 18.0;
        }

        private static void TryMergeFlowingParagraphLines(
            IReadOnlyList<VisualLine> lines,
            int startIdx,
            double leftMargin,
            double rightLimit,
            List<RichWord> mergedWords,
            ref int lastMergedLineIdx)
        {
            var firstLine = lines[startIdx];
            string firstText = firstLine.FullText.Trim();
            if (string.IsNullOrEmpty(firstText)) return;
            if (KeyValueMetadataRegex.IsMatch(firstText)) return;
            if (TocDotLeaderLineRegex.IsMatch(firstText)) return;
            if (IsHorizontallyCenteredLine(firstLine, leftMargin, rightLimit, leftMargin + rightLimit)) return;

            bool firstHasStandaloneMarker = HasListMarkerToken(firstLine, out _);
            bool firstIsBullet = BulletPrefixRegex.IsMatch(firstText) || firstHasStandaloneMarker;
            double expectedContLeft = firstIsBullet && firstLine.Words.Count >= 2
                ? firstLine.Words[1].Left
                : firstLine.Left;

            double curTop = firstLine.Top;
            double curBottom = firstLine.Bottom;
            double curRight = firstLine.Right;
            double curLeft = firstLine.Left;

            while (lastMergedLineIdx + 1 < lines.Count)
            {
                var prevLine = lines[lastMergedLineIdx];
                var nextLine = lines[lastMergedLineIdx + 1];
                string prevText = prevLine.FullText.Trim();
                string nextText = nextLine.FullText.Trim();

                if (string.IsNullOrEmpty(nextText)) break;
                if (nextLine.Segments.Count > 1) break;
                if (HasListMarkerToken(nextLine, out _)) break;
                if (BulletPrefixRegex.IsMatch(nextText)) break;
                if (KeyValueMetadataRegex.IsMatch(nextText)) break;
                if (TocDotLeaderLineRegex.IsMatch(nextText)) break;
                // Do not merge a new numbered heading (e.g. "2.1.", "3.3.", "4.") into preceding paragraph
                if (Regex.IsMatch(nextText, @"^\d+(\.\d+)+\b")) break;
                // Do not merge standalone display formula into preceding prose paragraph
                if (nextLine.Left >= leftMargin + 85.0 && nextText.Contains('=')) break;
                // Do not merge after a line that ends with ':' (introduces a list or formula block)
                if (prevText.EndsWith(':')) break;

                // Font style compatibility (allow prose lines that contain mixed inline bold/code phrases to merge, while keeping pure-bold headings separate from body prose)
                if (Math.Abs(nextLine.FontSizePt - firstLine.FontSizePt) > 1.8) break;
                bool firstAllBold = firstLine.Words.Count > 0 && firstLine.Words.All(w => w.IsBold);
                bool nextAllBold = nextLine.Words.Count > 0 && nextLine.Words.All(w => w.IsBold);
                if (firstAllBold != nextAllBold) break;

                // Vertical spacing check: support 1.0x to 1.65x intra-paragraph line spacing
                double vGap = curBottom - nextLine.Top;
                double lineStep = curTop - nextLine.Top;
                if (vGap < -2.5 || lineStep <= 0 || vGap > firstLine.FontSizePt * 0.95 || lineStep > firstLine.FontSizePt * 1.95)
                    break;

                // Previous line must have reached the right wrapping zone (at least 64% of column span from curLeft to rightLimit)
                double colSpan = Math.Max(120.0, rightLimit - curLeft);
                if ((curRight - curLeft) < colSpan * 0.64) break;

                // Left alignment check:
                // - Either aligned with expectedContLeft (within 6.5pt)
                // - Or on line 2 of a first-line-indented paragraph (firstLine.Left > nextLine.Left by 10..52pt)
                // - Or on line 2 of a hanging-indent bibliography entry (nextLine.Left > firstLine.Left by 10..52pt)
                bool leftAligned = Math.Abs(nextLine.Left - expectedContLeft) <= 6.5;
                if (!leftAligned && lastMergedLineIdx == startIdx && !firstIsBullet)
                {
                    double indentDelta = firstLine.Left - nextLine.Left;
                    if (Math.Abs(indentDelta) >= 10.0 && Math.Abs(indentDelta) <= 52.0 && nextLine.Left >= leftMargin - 4.0)
                    {
                        leftAligned = true;
                        expectedContLeft = nextLine.Left;
                    }
                }
                if (!leftAligned) break;

                // Do not merge a new capitalized label-colon item (e.g. "Label: description") into the preceding paragraph
                if (Regex.IsMatch(nextText, @"^[\p{Lu}][\p{L}]{2,15}(?:\s+[\p{Lu}][\p{L}]{2,15})?\s*:\s+\S"))
                    break;

                if (IsHorizontallyCenteredLine(nextLine, leftMargin, rightLimit, leftMargin + rightLimit))
                    break;

                mergedWords.AddRange(nextLine.Words.Select(w => w.VerticalAlign != null ? w : (w with { FontSizePt = firstLine.FontSizePt })));
                curTop = nextLine.Top;
                curBottom = nextLine.Bottom;
                curRight = nextLine.Right;
                curLeft = nextLine.Left;
                lastMergedLineIdx++;
            }
        }

        private static void TryMergeHangingLabeledParagraphLines(
            IReadOnlyList<VisualLine> lines,
            int startIdx,
            double leftMargin,
            double rightLimit,
            ref VisualLine firstLine,
            List<RichWord> mergedWords,
            ref int lastMergedLineIdx)
        {
            if (startIdx + 1 >= lines.Count || firstLine.Segments.Count < 2) return;
            var lastSeg = firstLine.Segments[^1];
            if (lastSeg.Words.Count < 3) return;

            double usableSpan = Math.Max(180.0, rightLimit - leftMargin);
            if (firstLine.Right < leftMargin + usableSpan * 0.62) return;

            // Determine the expected left X of wrapped continuation lines (either lastSeg.Left, or after leading ':' / ': a.' in lastSeg)
            double expectedContLeft = lastSeg.Left;
            int prefixWordsInLastSeg = 0;
            if (lastSeg.Words[0].Text == ":" && lastSeg.Words.Count >= 3 && Regex.IsMatch(lastSeg.Words[1].Text, @"^(?:[a-zA-Z0-9]{1,3}[.)])$"))
            {
                expectedContLeft = lastSeg.Words[2].Left;
                prefixWordsInLastSeg = 2;
            }
            else if (lastSeg.Words[0].Text == ":" && lastSeg.Words.Count >= 2)
            {
                expectedContLeft = lastSeg.Words[1].Left;
                prefixWordsInLastSeg = 1;
            }
            else if (Regex.IsMatch(lastSeg.Words[0].Text, @"^(?:[a-zA-Z0-9]{1,3}[.)])$") && lastSeg.Words.Count >= 2
                && Math.Abs(lines[startIdx + 1].Left - lastSeg.Words[1].Left) < Math.Abs(lines[startIdx + 1].Left - lastSeg.Left))
            {
                expectedContLeft = lastSeg.Words[1].Left;
                prefixWordsInLastSeg = 1;
            }

            if (expectedContLeft <= firstLine.Left + 10.0) return;

            var appendedWords = new List<RichWord>();
            double curTop = firstLine.Top;
            double curBottom = firstLine.Bottom;
            double curRight = firstLine.Right;
            double targetFontSizePt = firstLine.FontSizePt;

            while (lastMergedLineIdx + 1 < lines.Count)
            {
                var nextLine = lines[lastMergedLineIdx + 1];
                string nextText = nextLine.FullText.Trim();
                if (string.IsNullOrEmpty(nextText)) break;
                if (nextLine.Segments.Count > 1) break;
                if (HasListMarkerToken(nextLine, out _) || BulletPrefixRegex.IsMatch(nextText)) break;
                if (Math.Abs(nextLine.FontSizePt - firstLine.FontSizePt) > 2.2) break;

                double vGap = curBottom - nextLine.Top;
                double lineStep = curTop - nextLine.Top;
                if (vGap < -2.5 || lineStep <= 0 || vGap > firstLine.FontSizePt * 1.05 || lineStep > firstLine.FontSizePt * 2.05)
                    break;

                if ((curRight - expectedContLeft) < (rightLimit - expectedContLeft) * 0.55)
                    break;

                if (Math.Abs(nextLine.Left - expectedContLeft) > 16.0)
                    break;

                var normalizedNextWords = nextLine.Words
                    .Select(w => w.VerticalAlign != null ? w : (w with { FontSizePt = targetFontSizePt }))
                    .ToList();
                appendedWords.AddRange(normalizedNextWords);
                mergedWords.AddRange(normalizedNextWords);
                curTop = nextLine.Top;
                curBottom = nextLine.Bottom;
                curRight = nextLine.Right;
                lastMergedLineIdx++;
            }

            if (appendedWords.Count > 0)
            {
                var updatedSegments = new List<WordSegment>(firstLine.Segments.Take(firstLine.Segments.Count - 1));
                for (int p = 0; p < prefixWordsInLastSeg; p++)
                {
                    var pw = lastSeg.Words[p];
                    updatedSegments.Add(new WordSegment(new List<RichWord> { pw }, pw.Left, pw.Right, pw.Bottom, pw.Top));
                }
                var bodyWords = lastSeg.Words.Skip(prefixWordsInLastSeg).Concat(appendedWords).ToList();
                updatedSegments.Add(new WordSegment(bodyWords, bodyWords[0].Left, bodyWords.Max(w => w.Right), bodyWords.Min(w => w.Bottom), bodyWords.Max(w => w.Top)));
                firstLine = firstLine with { Segments = updatedSegments };
            }
        }

        private static void EnsurePageSectionFitsSinglePage(
            W.Body body,
            int pageBodyStart,
            double pageWidthPt,
            double pageHeightPt,
            double leftMarginPt,
            double rightMarginPt,
            double topMarginPt,
            double bottomMarginPt)
        {
            var paras = body.ChildElements.Skip(pageBodyStart).OfType<W.Paragraph>().ToList();
            if (paras.Count < 4) return;

            double availHeightDxa = Math.Max(2000.0, (pageHeightPt - topMarginPt - bottomMarginPt) * 20.0);
            double availWidthPt = Math.Max(180.0, pageWidthPt - leftMarginPt - rightMarginPt);

            double totalEstDxa = 0;
            foreach (var p in paras)
            {
                var sp = p.ParagraphProperties?.SpacingBetweenLines;
                double before = sp?.Before?.Value != null && double.TryParse(sp.Before.Value, out double b) ? b : 0;
                double after = sp?.After?.Value != null && double.TryParse(sp.After.Value, out double a) ? a : 0;
                double lineDxa = sp?.Line?.Value != null && double.TryParse(sp.Line.Value, out double l) ? l : 240;

                int leftIndDxa = p.ParagraphProperties?.Indentation?.Left?.Value != null && int.TryParse(p.ParagraphProperties.Indentation.Left.Value, out int li) ? li : 0;
                double paraAvailWidthPt = Math.Max(120.0, availWidthPt - leftIndDxa / 20.0);
                double fontSizePt = p.Descendants<W.FontSize>().FirstOrDefault()?.Val?.Value != null
                    && double.TryParse(p.Descendants<W.FontSize>().First().Val!.Value, out double halfPt)
                    ? halfPt / 2.0 : 10.0;
                string txt = p.InnerText;
                int explicitBreaks = p.Descendants<W.Break>().Count();
                int estLines = Math.Max(1 + explicitBreaks, (int)Math.Ceiling((txt.Length * fontSizePt * 0.51) / paraAvailWidthPt));
                totalEstDxa += before + after + estLines * lineDxa;
            }

            double budgetDxa = availHeightDxa * 0.94;
            if (totalEstDxa > budgetDxa && totalEstDxa > 0)
            {
                double ratio = Math.Clamp(budgetDxa / totalEstDxa, 0.72, 0.95);
                foreach (var p in paras)
                {
                    var sp = p.ParagraphProperties?.SpacingBetweenLines;
                    if (sp == null) continue;
                    if (sp.Before?.Value != null && double.TryParse(sp.Before.Value, out double b) && b > 0)
                        sp.Before = Math.Round(b * ratio * 0.85).ToString(CultureInfo.InvariantCulture);
                    if (sp.After?.Value != null && double.TryParse(sp.After.Value, out double a) && a > 0)
                        sp.After = Math.Round(a * ratio * 0.85).ToString(CultureInfo.InvariantCulture);
                    if (sp.Line?.Value != null && double.TryParse(sp.Line.Value, out double l) && l > 180)
                        sp.Line = Math.Round(Math.Max(180, l * Math.Max(0.88, ratio))).ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        private static W.Paragraph? TryBuildJustifiedParagraph(IReadOnlyList<VisualLine> lines, int start,
            double leftMargin, double rightLimit, double gap, out int count, out double leading, bool allowEnterGaps = false)
        {
            count = 0; leading = 0;
            if (start + 1 >= lines.Count) return null;
            var first = lines[start]; var second = lines[start + 1];
            if (IsHorizontallyCenteredLine(first, leftMargin, rightLimit, leftMargin + rightLimit)
                || IsHorizontallyCenteredLine(second, leftMargin, rightLimit, leftMargin + rightLimit))
                return null;
            double strictTol = Math.Max(.5, first.FontSizePt / 10);
            double left = second.Left, right = first.Right;
            double Baseline(VisualLine line) => line.Words.Average(w => w.Baseline);
            leading = Baseline(first) - Baseline(second);

            bool StrictEligible(VisualLine line, int minWords = 5) => line.Segments.Count <= 1 && line.Words.Count >= minWords
                && Math.Abs(line.FontSizePt - first.FontSizePt) < .5 && line.IsBold == first.IsBold
                && line.Words[0].FontName == first.Words[0].FontName && !BodyListMarkerRegex.IsMatch(line.FullText)
                && !KeyValueMetadataRegex.IsMatch(line.FullText) && !line.FullText.EndsWith(':');

            // 1. Exact baseline justified paragraph check (continuous runs without artificial <w:br/>)
            if (leading >= first.FontSizePt && leading <= first.FontSizePt * 2
                && first.Left >= left - strictTol && first.Left - left <= first.FontSizePt * 4
                && Math.Abs(right - rightLimit) <= strictTol && StrictEligible(first))
            {
                if (second.Right < right - first.FontSizePt)
                {
                    bool hasThird = start + 2 < lines.Count
                        && Math.Abs(Baseline(second) - Baseline(lines[start + 2]) - leading) <= strictTol;
                    if (!hasThird && first.Words.Count >= 6 && StrictEligible(second, minWords: 2)
                        && Math.Abs(second.Left - left) <= strictTol && Math.Abs(left - leftMargin) <= strictTol * 2)
                    {
                        count = 2;
                        var twoLinePara = new W.Paragraph(new W.ParagraphProperties(
                            new W.SpacingBetweenLines { Before = Math.Round(gap * 20).ToString(CultureInfo.InvariantCulture), After = "0",
                                Line = Math.Round(leading * 20).ToString(CultureInfo.InvariantCulture), LineRule = W.LineSpacingRuleValues.Exact },
                            new W.Indentation { Left = Math.Round(Math.Max(0, left - leftMargin) * 20).ToString(CultureInfo.InvariantCulture),
                                Right = Math.Round(Math.Max(0, rightLimit - right) * 20).ToString(CultureInfo.InvariantCulture),
                                FirstLine = Math.Round(Math.Max(0, first.Left - left) * 20).ToString(CultureInfo.InvariantCulture) },
                            new W.Justification { Val = W.JustificationValues.Both }));
                        var combinedTwoLineWords = new List<RichWord>(first.Words.Count + second.Words.Count);
                        combinedTwoLineWords.AddRange(first.Words);
                        combinedTwoLineWords.AddRange(second.Words);
                        AppendConsolidatedRuns(twoLinePara, combinedTwoLineWords, 0);
                        return twoLinePara;
                    }
                }
                else if (start + 2 < lines.Count && Math.Abs(second.Right - right) <= strictTol)
                {
                    int endStrict = start + 1;
                    bool validStrict = true;
                    for (; endStrict < lines.Count; endStrict++)
                    {
                        var current = lines[endStrict];
                        if (!StrictEligible(current) || Math.Abs(current.Left - left) > strictTol
                            || Math.Abs(Baseline(lines[endStrict - 1]) - Baseline(current) - leading) > strictTol)
                        {
                            validStrict = false;
                            break;
                        }
                        if (current.Right < right - first.FontSizePt)
                        {
                            if (endStrict < start + 2) validStrict = false;
                            break;
                        }
                        if (Math.Abs(current.Right - right) > strictTol)
                        {
                            validStrict = false;
                            break;
                        }
                    }
                    if (validStrict && endStrict < lines.Count)
                    {
                        count = endStrict - start + 1;
                        var strictPara = new W.Paragraph(new W.ParagraphProperties(
                            new W.SpacingBetweenLines { Before = Math.Round(gap * 20).ToString(CultureInfo.InvariantCulture), After = "0",
                                Line = Math.Round(leading * 20).ToString(CultureInfo.InvariantCulture), LineRule = W.LineSpacingRuleValues.Exact },
                            new W.Indentation { Left = Math.Round((left - leftMargin) * 20).ToString(CultureInfo.InvariantCulture),
                                Right = Math.Round((rightLimit - right) * 20).ToString(CultureInfo.InvariantCulture),
                                FirstLine = Math.Round((first.Left - left) * 20).ToString(CultureInfo.InvariantCulture) },
                            new W.Justification { Val = W.JustificationValues.Both }));
                        if (lines.Count <= 6 && first.Left >= left + 4.0)
                        {
                            for (int i = start; i <= endStrict; i++)
                            {
                                if (i > start) strictPara.Append(new W.Run(new W.Break()));
                                AppendConsolidatedRuns(strictPara, lines[i].Words, 0);
                            }
                        }
                        else
                        {
                            var combinedStrictWords = new List<RichWord>();
                            for (int i = start; i <= endStrict; i++)
                            {
                                combinedStrictWords.AddRange(lines[i].Words);
                            }
                            AppendConsolidatedRuns(strictPara, combinedStrictWords, 0);
                        }
                        return strictPara;
                    }
                }
            }

            // 2. General Real-World Justified Paragraph (supports 1.15x-1.65x spacing, inline code/bold runs, first-line indent, hanging indent)
            double rightTol = Math.Max(8.5, first.FontSizePt * 0.85);
            double leftTol = Math.Max(4.5, first.FontSizePt * 0.45);
            double leadTol = Math.Max(4.0, first.FontSizePt * 0.38);
            double usableSpan = Math.Max(200.0, rightLimit - leftMargin);

            if (leading < first.FontSizePt * 0.95 || leading > first.FontSizePt * 2.25) return null;
            if (right < leftMargin + usableSpan * 0.72 || right > rightLimit + rightTol) return null;
            if (Math.Abs(first.Left - left) > 54.0) return null;

            bool firstAllBold = first.Words.Count > 0 && first.Words.All(w => w.IsBold);
            bool GeneralEligible(VisualLine line, int minWords = 4)
            {
                if (line.Segments.Count > 1 || line.Words.Count < minWords) return false;
                if (Math.Abs(line.FontSizePt - first.FontSizePt) > 1.8) return false;
                bool lineAllBold = line.Words.Count > 0 && line.Words.All(w => w.IsBold);
                if (lineAllBold != firstAllBold) return false;
                string txt = line.FullText.Trim();
                if (BodyListMarkerRegex.IsMatch(txt) || BulletPrefixRegex.IsMatch(txt)) return false;
                if (KeyValueMetadataRegex.IsMatch(txt) || TocDotLeaderLineRegex.IsMatch(txt)) return false;
                if (Regex.IsMatch(txt, @"^\d+(\.\d+)+\b")) return false;
                if (Regex.IsMatch(txt, @"^[\p{Lu}][\p{L}]{2,15}(?:\s+[\p{Lu}][\p{L}]{2,15})?\s*:\s+\S")) return false;
                return !txt.EndsWith(':');
            }

            if (!GeneralEligible(first, minWords: 5)) return null;
            if (Math.Abs(second.Right - right) > rightTol) return null;

            int end = start + 1;
            for (; end < lines.Count; end++)
            {
                var current = lines[end];
                if (!GeneralEligible(current, minWords: 1) || Math.Abs(current.Left - left) > leftTol
                    || Math.Abs(Baseline(lines[end - 1]) - Baseline(current) - leading) > leadTol)
                    break;
                if (current.Right < right - first.FontSizePt * 1.2)
                {
                    if (end >= start + 2)
                    {
                        count = end - start + 1;
                        break;
                    }
                    return null;
                }
                if (Math.Abs(current.Right - right) > rightTol)
                    break;
                if (end == lines.Count - 1 && end >= start + 2)
                {
                    count = end - start + 1;
                    break;
                }
            }

            if (count < 3) return null;
            int lastIdx = start + count - 1;
            var lastLine = lines[lastIdx];
            double rawAfterPt = lastIdx + 1 < lines.Count
                ? Math.Max(0, lastLine.Top - lines[lastIdx + 1].Top - leading)
                : 0;
            double afterDxa = (allowEnterGaps && rawAfterPt >= 13.5) ? 0 : Math.Round(rawAfterPt * 20);

            var ind = new W.Indentation
            {
                Left = Math.Round(Math.Max(0, left - leftMargin) * 20).ToString(CultureInfo.InvariantCulture),
                Right = Math.Round(Math.Max(0, rightLimit - right) * 20).ToString(CultureInfo.InvariantCulture)
            };
            if (first.Left - left >= 6.0)
            {
                ind.FirstLine = Math.Round((first.Left - left) * 20).ToString(CultureInfo.InvariantCulture);
            }
            else if (left - first.Left >= 6.0)
            {
                ind.Hanging = Math.Round((left - first.Left) * 20).ToString(CultureInfo.InvariantCulture);
            }

            var realJustifiedPara = new W.Paragraph(new W.ParagraphProperties(
                new W.SpacingBetweenLines
                {
                    Before = Math.Round(gap * 20).ToString(CultureInfo.InvariantCulture),
                    After = afterDxa.ToString(CultureInfo.InvariantCulture),
                    Line = Math.Round(leading * 20).ToString(CultureInfo.InvariantCulture),
                    LineRule = W.LineSpacingRuleValues.Exact
                },
                ind,
                new W.Justification { Val = W.JustificationValues.Both }));

            var allWords = new List<RichWord>();
            for (int i = start; i <= lastIdx; i++)
            {
                allWords.AddRange(lines[i].Words);
            }
            AppendConsolidatedRuns(realJustifiedPara, allWords, 0);
            return realJustifiedPara;
        }

        private static W.Paragraph BuildGeometricTabStopParagraph(
            VisualLine line,
            double pageWidthPt,
            double pageLeftMarginPt,
            double pageRightLimitPt,
            double usableWidthPt,
            double remainderGapBeforePt,
            bool hasTopDivider,
            IReadOnlyList<double>? canonicalTabPositionsPt = null,
            IReadOnlyList<VisualLine>? regionLines = null)
        {
            int spacingBeforeDxa = ComputeSpacingBeforeDxa(remainderGapBeforePt);

            var tabs = new W.Tabs();
            for (int s = 1; s < line.Segments.Count; s++)
            {
                var seg = line.Segments[s];
                double segLeftPt = seg.Left;
                if (canonicalTabPositionsPt != null)
                {
                    foreach (double canonX in canonicalTabPositionsPt)
                    {
                        if (Math.Abs(segLeftPt - canonX) <= 3.5)
                        {
                            segLeftPt = canonX;
                            break;
                        }
                    }
                }

                // Check if multiple lines share this same left-aligned column anchor (e.g. address block on right side of page)
                bool isSharedLeftAlignedColumn = regionLines != null
                    && regionLines.Count(other => other.Segments.Skip(1).Any(os => Math.Abs(os.Left - segLeftPt) <= 4.0)) >= 2;
                double distToRightLimit = Math.Abs(pageRightLimitPt - seg.Right);

                bool isShortRightSideToken = seg.Words.Count <= 3
                    && (seg.Right - seg.Left) <= usableWidthPt * 0.38
                    && seg.Left >= pageLeftMarginPt + usableWidthPt * 0.55;
                if (s == line.Segments.Count - 1 && !isSharedLeftAlignedColumn && ((distToRightLimit <= 32.0 && isShortRightSideToken) || PageFractionRegex.IsMatch(seg.Text)))
                {
                    int rightTabPosDxa = (int)Math.Round(Math.Max(180.0 * s, (seg.Right - pageLeftMarginPt) * 20.0));
                    tabs.Append(new W.TabStop { Val = W.TabStopValues.Right, Position = rightTabPosDxa });
                }
                else
                {
                    int leftPosDxa = (int)Math.Round(Math.Max(180.0 * s, (segLeftPt - pageLeftMarginPt) * 20.0));
                    tabs.Append(new W.TabStop { Val = W.TabStopValues.Left, Position = leftPosDxa });
                }
            }

            var pPr = new W.ParagraphProperties();
            if (hasTopDivider)
            {
                pPr.Append(new W.ParagraphBorders(
                    new W.TopBorder { Val = W.BorderValues.Single, Size = 6, Space = 4, Color = "D0D0D0" }));
            }
            pPr.Append(tabs);

            int indentLeftDxa = (int)Math.Round(Math.Max(0, line.Segments[0].Left - pageLeftMarginPt) * 20.0);
            pPr.Append(new W.SpacingBetweenLines
            {
                Before = spacingBeforeDxa.ToString(CultureInfo.InvariantCulture),
                After = "0",
                Line = "240",
                LineRule = W.LineSpacingRuleValues.Auto
            });

            if (indentLeftDxa >= 80 && indentLeftDxa <= 1800)
            {
                pPr.Append(new W.Indentation { Left = indentLeftDxa.ToString(CultureInfo.InvariantCulture) });
            }

            var para = new W.Paragraph(pPr);
            for (int s = 0; s < line.Segments.Count; s++)
            {
                if (s > 0)
                {
                    string halfPt = ((int)Math.Round(Math.Clamp(line.FontSizePt, 6.0, 72.0) * 2.0)).ToString(CultureInfo.InvariantCulture);
                    para.Append(new W.Run(new W.RunProperties(new W.FontSize { Val = halfPt }, new W.FontSizeComplexScript { Val = halfPt }), new W.TabChar()));
                }
                AppendConsolidatedRuns(para, line.Segments[s].Words, verticalPositionHalfPt: 0);
            }

            return para;
        }

        private static W.Paragraph CreateLogoWithRemainingSegmentsParagraph(
            MainDocumentPart mainPart,
            byte[] pngBytes,
            double widthPt,
            double heightPt,
            List<WordSegment> rightSegments,
            double pageLeftMarginPt,
            double pageRightLimitPt,
            double usableWidthPt,
            double remainderGapBeforePt,
            bool hasTopDivider,
            uint docPrId,
            double maxWidthPt = 468.0,
            double maxHeightPt = 648.0)
        {
            int rightTabPosDxa = (int)Math.Round(usableWidthPt * 20.0);
            int spacingBeforeDxa = ComputeSpacingBeforeDxa(remainderGapBeforePt);

            var tabs = new W.Tabs();
            for (int s = 0; s < rightSegments.Count; s++)
            {
                var seg = rightSegments[s];
                double distToRightLimit = Math.Abs(pageRightLimitPt - seg.Right);
                if (s == rightSegments.Count - 1 && distToRightLimit <= 32.0)
                {
                    tabs.Append(new W.TabStop { Val = W.TabStopValues.Right, Position = rightTabPosDxa });
                }
                else
                {
                    int leftPosDxa = (int)Math.Round(Math.Max(720.0, (seg.Left - pageLeftMarginPt) * 20.0));
                    tabs.Append(new W.TabStop { Val = W.TabStopValues.Left, Position = leftPosDxa });
                }
            }

            var pPr = new W.ParagraphProperties();
            if (hasTopDivider)
            {
                pPr.Append(new W.ParagraphBorders(
                    new W.TopBorder { Val = W.BorderValues.Single, Size = 6, Space = 4, Color = "D0D0D0" }));
            }
            pPr.Append(tabs);
            pPr.Append(new W.SpacingBetweenLines
            {
                Before = spacingBeforeDxa.ToString(CultureInfo.InvariantCulture),
                After = "40"
            });

            var para = new W.Paragraph(pPr);
            var drawing = BuildInlineDrawingElement(mainPart, pngBytes, widthPt, heightPt, docPrId, maxWidthPt, maxHeightPt);
            para.Append(new W.Run(drawing));

            int textRaiseHalfPt = (int)Math.Round(Math.Clamp((heightPt - 10.0) * 0.70, 0.0, 14.0));
            foreach (var seg in rightSegments)
            {
                string halfPt = ((int)Math.Round(Math.Clamp(seg.Words.FirstOrDefault()?.FontSizePt ?? 10.0, 6.0, 72.0) * 2.0)).ToString(CultureInfo.InvariantCulture);
                para.Append(new W.Run(new W.RunProperties(new W.FontSize { Val = halfPt }, new W.FontSizeComplexScript { Val = halfPt }), new W.TabChar()));
                AppendConsolidatedRuns(para, seg.Words, textRaiseHalfPt);
            }

            return para;
        }



        private static void AppendConsolidatedRuns(W.Paragraph para, IReadOnlyList<RichWord> words, int verticalPositionHalfPt)
        {
            if (words.Count == 0) return;

            var sb = new StringBuilder();
            RichWord currentStyle = words[0];

            for (int i = 0; i < words.Count; i++)
            {
                var w = words[i];
                bool sameRunStyle = w.FontName == currentStyle.FontName
                    && Math.Abs(w.FontSizePt - currentStyle.FontSizePt) <= 0.4
                    && w.HexColor == currentStyle.HexColor
                    && w.IsBold == currentStyle.IsBold
                    && w.IsItalic == currentStyle.IsItalic
                    && w.IsUnderline == currentStyle.IsUnderline
                    && w.IsStrike == currentStyle.IsStrike
                    && w.IsHighlighted == currentStyle.IsHighlighted
                    && w.BackgroundFillHex == currentStyle.BackgroundFillHex
                    && w.VerticalAlign == currentStyle.VerticalAlign;

                if (!sameRunStyle && sb.Length > 0)
                {
                    FlushRun(para, currentStyle, sb.ToString(), verticalPositionHalfPt);
                    sb.Clear();
                    currentStyle = w;
                }

                sb.Append(w.Text);
                if (i < words.Count - 1 && w.VerticalAlign == null && words[i + 1].VerticalAlign != "superscript" && words[i + 1].VerticalAlign != "subscript")
                {
                    sb.Append(' ');
                }
                else if (i < words.Count - 1 && w.VerticalAlign != null && words[i + 1].VerticalAlign == null)
                {
                    sb.Append(' ');
                }
            }

            if (sb.Length > 0)
            {
                FlushRun(para, currentStyle, sb.ToString(), verticalPositionHalfPt);
            }
        }

        private static void FlushRun(W.Paragraph para, RichWord style, string text, int verticalPositionHalfPt)
        {
            string halfPt = ((int)Math.Round(Math.Clamp(style.FontSizePt, 6.0, 72.0) * 2.0)).ToString(CultureInfo.InvariantCulture);
            var rPr = CreateRunProperties(
                style.FontName,
                style.HexColor,
                halfPt,
                style.IsBold,
                style.IsItalic,
                verticalPositionHalfPt,
                style.IsUnderline,
                style.IsStrike,
                ContainsRtlScript(text),
                style.IsHighlighted,
                style.BackgroundFillHex,
                style.VerticalAlign);
            para.Append(new W.Run(rPr, new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        }

        private static bool ContainsRtlScript(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                if ((c >= '\u0590' && c <= '\u08FF') || (c >= '\uFB1D' && c <= '\uFDFF') || (c >= '\uFE70' && c <= '\uFEFF'))
                    return true;
            }
            return false;
        }

        private static bool TryMapExactWordHighlightColor(string hex, out W.HighlightColorValues highlightColor)
        {
            highlightColor = W.HighlightColorValues.None;
            if (string.IsNullOrEmpty(hex) || hex.Length != 6) return false;
            if (!int.TryParse(hex[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int r)
                || !int.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int g)
                || !int.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int b))
                return false;

            (W.HighlightColorValues Val, int R, int G, int B)[] palette =
            {
                (W.HighlightColorValues.Yellow, 255, 255, 0),
                (W.HighlightColorValues.Green, 0, 255, 0),
                (W.HighlightColorValues.Cyan, 0, 255, 255),
                (W.HighlightColorValues.Magenta, 255, 0, 255),
                (W.HighlightColorValues.Blue, 0, 0, 255),
                (W.HighlightColorValues.Red, 255, 0, 0),
                (W.HighlightColorValues.DarkBlue, 0, 0, 128),
                (W.HighlightColorValues.DarkCyan, 0, 128, 128),
                (W.HighlightColorValues.DarkGreen, 0, 128, 0),
                (W.HighlightColorValues.DarkMagenta, 128, 0, 128),
                (W.HighlightColorValues.DarkRed, 128, 0, 0),
                (W.HighlightColorValues.DarkYellow, 128, 128, 0),
                (W.HighlightColorValues.DarkGray, 128, 128, 128),
                (W.HighlightColorValues.LightGray, 192, 192, 192),
                (W.HighlightColorValues.Black, 0, 0, 0)
            };

            foreach (var p in palette)
            {
                int distSq = (r - p.R) * (r - p.R) + (g - p.G) * (g - p.G) + (b - p.B) * (b - p.B);
                if (distSq <= 256)
                {
                    highlightColor = p.Val;
                    return true;
                }
            }
            return false;
        }

        private static W.RunProperties CreateRunProperties(
            string fontName,
            string hexColor,
            string halfPt,
            bool isBold,
            bool isItalic,
            int verticalPositionHalfPt,
            bool isUnderline = false,
            bool isStrike = false,
            bool isRtl = false,
            bool isHighlighted = false,
            string? backgroundFillHex = null,
            string? verticalAlign = null)
        {
            var rPr = new W.RunProperties(new W.RunFonts { Ascii = fontName, HighAnsi = fontName, ComplexScript = fontName });
            if (isBold) rPr.Append(new W.Bold());
            if (isItalic) rPr.Append(new W.Italic());
            if (isStrike) rPr.Append(new W.Strike());
            rPr.Append(new W.Color { Val = hexColor });
            if (verticalPositionHalfPt != 0)
            {
                rPr.Append(new W.Position { Val = verticalPositionHalfPt.ToString(CultureInfo.InvariantCulture) });
            }
            rPr.Append(new W.FontSize { Val = halfPt });
            rPr.Append(new W.FontSizeComplexScript { Val = halfPt });
            if (!string.IsNullOrEmpty(backgroundFillHex))
            {
                if (TryMapExactWordHighlightColor(backgroundFillHex, out var exactHl))
                {
                    rPr.Append(new W.Highlight { Val = exactHl });
                }
                else
                {
                    // Preserve exact 1:1 RGB background fill from the source PDF (e.g. #F1F5F9, #FEF3C7, etc.) without hardcoding gray!
                    rPr.Append(new W.Shading
                    {
                        Val = W.ShadingPatternValues.Clear,
                        Color = "auto",
                        Fill = backgroundFillHex
                    });
                }
            }
            else if (isHighlighted)
            {
                rPr.Append(new W.Highlight { Val = W.HighlightColorValues.LightGray });
            }
            if (isUnderline) rPr.Append(new W.Underline { Val = W.UnderlineValues.Single });
            if (verticalAlign == "superscript")
            {
                rPr.Append(new W.VerticalTextAlignment { Val = W.VerticalPositionValues.Superscript });
            }
            else if (verticalAlign == "subscript")
            {
                rPr.Append(new W.VerticalTextAlignment { Val = W.VerticalPositionValues.Subscript });
            }
            if (isRtl) rPr.Append(new W.RightToLeftText());
            return rPr;
        }

        private static W.Paragraph BuildBulletParagraph(List<RichWord> words, double remainderGapBeforePt)
        {
            int spacingBeforeDxa = ComputeSpacingBeforeDxa(remainderGapBeforePt);

            var pPr = new W.ParagraphProperties(
                new W.Tabs(new W.TabStop { Val = W.TabStopValues.Left, Position = 640 }),
                new W.SpacingBetweenLines
                {
                    Before = spacingBeforeDxa.ToString(CultureInfo.InvariantCulture),
                    After = "0",
                    Line = "260",
                    LineRule = W.LineSpacingRuleValues.Auto
                },
                new W.Indentation { Left = "640", Hanging = "280" });

            var para = new W.Paragraph(pPr);
            var firstWord = words.FirstOrDefault();
            string font = firstWord?.FontName ?? "Calibri";
            string color = firstWord?.HexColor ?? "000000";
            string halfPt = ((int)Math.Round((firstWord?.FontSizePt ?? 10.0) * 2.0)).ToString(CultureInfo.InvariantCulture);

            var bulletRPr = CreateRunProperties(font, color, halfPt, false, false, 0);
            para.Append(new W.Run(bulletRPr, new W.Text("\u2022"), new W.TabChar()));

            AppendConsolidatedRuns(para, words, verticalPositionHalfPt: 0);
            return para;
        }

        private static bool IsWordBoldInBitmap(Word w, SKBitmap pageBitmap, double pageWidthPt, double pageHeightPt)
        {
            if (pageWidthPt <= 0 || pageHeightPt <= 0 || w.Text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return false;

            double scaleX = pageBitmap.Width / pageWidthPt;
            double scaleY = pageBitmap.Height / pageHeightPt;

            int x1 = Math.Clamp((int)(w.BoundingBox.Left * scaleX), 0, pageBitmap.Width - 1);
            int x2 = Math.Clamp((int)(w.BoundingBox.Right * scaleX), x1 + 1, pageBitmap.Width);
            int y1 = Math.Clamp((int)((pageHeightPt - w.BoundingBox.Top) * scaleY), 0, pageBitmap.Height - 1);
            int y2 = Math.Clamp((int)((pageHeightPt - w.BoundingBox.Bottom) * scaleY), y1 + 1, pageBitmap.Height);

            int deepBlackPixels = 0;
            int totalInkPixels = 0;
            for (int y = y1; y < y2; y++)
            {
                for (int x = x1; x < x2; x++)
                {
                    var c = pageBitmap.GetPixel(x, y);
                    int lum = (c.Red + c.Green + c.Blue) / 3;
                    if (lum < 200)
                    {
                        totalInkPixels++;
                        if (lum < 48)
                        {
                            deepBlackPixels++;
                        }
                    }
                }
            }

            if (totalInkPixels < 8) return false;
            double darkCoreRatio = (double)deepBlackPixels / totalInkPixels;
            int area = Math.Max(1, (x2 - x1) * (y2 - y1));
            double fillDensity = (double)deepBlackPixels / area;

            return darkCoreRatio >= 0.54 && fillDensity >= 0.17;
        }

        private static bool DetectHorizontalDividerInBitmap(
            SKBitmap pageBitmap,
            double pageWidthPt,
            double pageHeightPt,
            double upperBottomPt,
            double lowerTopPt,
            double pageLeftMarginPt,
            double pageRightMarginPt)
        {
            double scaleX = pageBitmap.Width / pageWidthPt;
            double scaleY = pageBitmap.Height / pageHeightPt;

            int yStart = Math.Clamp((int)Math.Floor((pageHeightPt - upperBottomPt) * scaleY) + 1, 0, pageBitmap.Height - 1);
            int yEnd = Math.Clamp((int)Math.Ceiling((pageHeightPt - lowerTopPt) * scaleY) - 1, yStart, pageBitmap.Height - 1);
            int xStart = Math.Clamp((int)(pageLeftMarginPt * scaleX), 0, pageBitmap.Width - 1);
            int xEnd = Math.Clamp((int)((pageWidthPt - pageRightMarginPt) * scaleX), xStart + 1, pageBitmap.Width - 1);

            for (int y = yStart; y <= yEnd; y++)
            {
                int grayPixels = 0;
                int totalChecked = 0;
                for (int x = xStart; x < xEnd; x += 4)
                {
                    var c = pageBitmap.GetPixel(x, y);
                    totalChecked++;
                    if (c.Red < 245 && c.Green < 245 && c.Blue < 245)
                    {
                        grayPixels++;
                    }
                }
                if (totalChecked > 10 && (double)grayPixels / totalChecked >= 0.55)
                {
                    return true;
                }
            }
            return false;
        }

        private static string ExtractWordHexColor(Word w, SKBitmap? pageBitmap, double pageWidthPt, double pageHeightPt)
        {
            try
            {
                var firstLetter = w.Letters.FirstOrDefault();
                if (firstLetter?.Color != null)
                {
                    var (r, g, b) = SafeToRgbValues(firstLetter.Color);
                    byte rb = (byte)Math.Clamp((int)Math.Round(r * 255.0), 0, 255);
                    byte gb = (byte)Math.Clamp((int)Math.Round(g * 255.0), 0, 255);
                    byte bb = (byte)Math.Clamp((int)Math.Round(b * 255.0), 0, 255);
                    if (pageBitmap == null || rb != 0 || gb != 0 || bb != 0)
                    {
                        return $"{rb:X2}{gb:X2}{bb:X2}";
                    }
                }
            }
            catch { }

            if (pageBitmap != null)
            {
                double scaleX = pageBitmap.Width / pageWidthPt;
                double scaleY = pageBitmap.Height / pageHeightPt;

                int x1 = Math.Clamp((int)(w.BoundingBox.Left * scaleX), 0, pageBitmap.Width - 1);
                int x2 = Math.Clamp((int)(w.BoundingBox.Right * scaleX), x1 + 1, pageBitmap.Width);
                int y1 = Math.Clamp((int)((pageHeightPt - w.BoundingBox.Top) * scaleY), 0, pageBitmap.Height - 1);
                int y2 = Math.Clamp((int)((pageHeightPt - w.BoundingBox.Bottom) * scaleY), y1 + 1, pageBitmap.Height);

                var inkColors = new Dictionary<uint, int>();
                var background = new List<int>();
                int left = Math.Max(0, x1 - 1), right = Math.Min(pageBitmap.Width - 1, x2);
                int top = Math.Max(0, y1 - 1), bottom = Math.Min(pageBitmap.Height - 1, y2);
                void SampleBackground(int x, int y)
                {
                    var pixel = pageBitmap.GetPixel(x, y);
                    background.Add((pixel.Red + pixel.Green + pixel.Blue) / 3);
                }
                for (int x = left; x <= right; x++) { SampleBackground(x, top); SampleBackground(x, bottom); }
                for (int y = top + 1; y < bottom; y++) { SampleBackground(left, y); SampleBackground(right, y); }
                background.Sort();
                // The lower paper decile rejects repeated background noise while tolerating sparse dark border contamination.
                int inkThreshold = background[background.Count / 10];

                for (int y = y1; y < y2; y++)
                {
                    for (int x = x1; x < x2; x++)
                    {
                        var c = pageBitmap.GetPixel(x, y);
                        int lum = (c.Red + c.Green + c.Blue) / 3;
                        if (lum < inkThreshold)
                        {
                            uint rgb = ((uint)c.Red << 16) | ((uint)c.Green << 8) | c.Blue;
                            inkColors.TryGetValue(rgb, out int count);
                            inkColors[rgb] = count + 1;
                        }
                    }
                }

                if (inkColors.Count > 0)
                {
                    // Repeated ink beats isolated noise; prefer the darker core when edge shades tie.
                    // shortcut: noisy scans can fragment RGB counts; validate clustering before extending this estimator.
                    uint rgb = inkColors.OrderByDescending(pair => pair.Value)
                        .ThenBy(pair => (pair.Key >> 16) + ((pair.Key >> 8) & 255) + (pair.Key & 255))
                        .ThenBy(pair => pair.Key).First().Key;
                    return rgb.ToString("X6", CultureInfo.InvariantCulture);
                }
            }

            return "000000";
        }

        private static string CleanOcrTokenGeneral(
            string text,
            double bottomPt,
            double rightPt,
            double pageWidthPt,
            int pageNo,
            int totalPages,
            List<string> warnings)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string t = text.Trim();
            if (Regex.IsMatch(t, @"^\(I[a-z]{3,}"))
            {
                t = "(|" + t[2..];
            }
            if (t.StartsWith('<') && t.Contains(':'))
            {
                t = Regex.Replace(t, @"(?<=[a-z])1(?=[A-Z]>?\.?$)", "l");
            }
            if (Regex.IsMatch(t, @"^\d{1,2}[A-Z][a-z]{2,}$"))
            {
                t = Regex.Replace(t, @"^(\d{1,2})([A-Z][a-z]{2,})$", "$1 $2");
            }

            return NormalizeUriSchemePrefix(t);
        }

        private static W.Paragraph CreateInlineImageParagraph(
            MainDocumentPart mainPart,
            byte[] pngBytes,
            double widthPt,
            double heightPt,
            uint docPrId,
            int leftIndentDxa,
            double remainderGapBeforePt = 4.0,
            bool hasTopDivider = false,
            double maxWidthPt = 468.0,
            double maxHeightPt = 648.0)
        {
            int spacingBeforeDxa = ComputeSpacingBeforeDxa(remainderGapBeforePt);
            var pPr = new W.ParagraphProperties();
            if (hasTopDivider)
            {
                pPr.Append(new W.ParagraphBorders(
                    new W.TopBorder { Val = W.BorderValues.Single, Size = 6, Space = 4, Color = "D0D0D0" }));
            }
            pPr.Append(new W.SpacingBetweenLines
            {
                Before = spacingBeforeDxa.ToString(CultureInfo.InvariantCulture),
                After = "0"
            });
            if (leftIndentDxa > 0)
            {
                pPr.Append(new W.Indentation { Left = leftIndentDxa.ToString(CultureInfo.InvariantCulture) });
            }

            var drawing = BuildInlineDrawingElement(mainPart, pngBytes, widthPt, heightPt, docPrId, maxWidthPt, maxHeightPt);
            return new W.Paragraph(pPr, new W.Run(drawing));
        }

        private static W.Drawing BuildInlineDrawingElement(
            MainDocumentPart mainPart,
            byte[] pngBytes,
            double widthPt,
            double heightPt,
            uint docPrId,
            double maxWidthPt = 468.0,
            double maxHeightPt = 648.0)
        {
            var imagePart = mainPart.AddImagePart(ImagePartType.Png);
            using (var ms = new MemoryStream(pngBytes))
            {
                imagePart.FeedData(ms);
            }

            string relId = mainPart.GetIdOfPart(imagePart);

            double safeW = Math.Max(8.0, widthPt);
            double safeH = Math.Max(8.0, heightPt);
            double scale = Math.Min(1.0, Math.Min(maxWidthPt / safeW, maxHeightPt / safeH));
            long cxEmu = (long)Math.Round(safeW * scale * 12700.0);
            long cyEmu = (long)Math.Round(safeH * scale * 12700.0);

            return new W.Drawing(
                new DW.Inline(
                    new DW.Extent { Cx = cxEmu, Cy = cyEmu },
                    new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                    new DW.DocProperties { Id = docPrId, Name = $"Picture_{docPrId}" },
                    new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                    new A.Graphic(
                        new A.GraphicData(
                            new PIC.Picture(
                                new PIC.NonVisualPictureProperties(
                                    new PIC.NonVisualDrawingProperties { Id = docPrId, Name = $"Image_{docPrId}.png" },
                                    new PIC.NonVisualPictureDrawingProperties()),
                                new PIC.BlipFill(
                                    new A.Blip { Embed = relId },
                                    new A.Stretch(new A.FillRectangle())),
                                new PIC.ShapeProperties(
                                    new A.Transform2D(
                                        new A.Offset { X = 0L, Y = 0L },
                                        new A.Extents { Cx = cxEmu, Cy = cyEmu }),
                                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })));
        }

        private static (List<double> Anchors, List<string[]> Rows) BuildTableGridForLines(List<VisualLine> lines)
        {
            var anchors = new List<double>();
            double occupiedRight = double.NegativeInfinity;
            // Overlapping spans share a column, including right-aligned values and sparse rows.
            foreach (var segment in lines.SelectMany(l => l.Segments).OrderBy(s => s.Left))
            {
                if (segment.Left > occupiedRight) anchors.Add(segment.Left);
                occupiedRight = Math.Max(occupiedRight, segment.Right);
            }

            var rows = new List<string[]>();
            foreach (var line in lines)
            {
                var row = new string[Math.Max(1, anchors.Count)];
                Array.Fill(row, string.Empty);

                for (int segmentIndex = 0; segmentIndex < line.Segments.Count; segmentIndex++)
                {
                    var seg = line.Segments[segmentIndex];
                    int col = GetTableColumn(line, segmentIndex, anchors);
                    row[col] = string.IsNullOrEmpty(row[col]) ? seg.Text : row[col] + " " + seg.Text;
                }
                rows.Add(row);
            }

            return (anchors, rows);
        }

        private static int GetTableColumn(VisualLine line, int segmentIndex, List<double> anchors)
        {
            if (anchors.Count <= 1) return 0;
            var seg = line.Segments[segmentIndex];
            int idx = anchors.FindLastIndex(a => a <= seg.Left + 6.0);
            if (idx >= 0) return idx;
            return 0;
        }

        private static double ExcelLineTop(VisualLine line, IXLCell cell)
        {
            var word = line.Words[0];
            var font = cell.Style.Font;
            font.FontName = word.FontName;
            font.FontSize = word.FontSizePt;
            font.Bold = word.IsBold;
            font.Italic = word.IsItalic;
            font.FontColor = XLColor.FromHtml("#" + word.HexColor);
            var metrics = ClosedXML.Graphics.DefaultGraphicEngine.Instance.Value;
            return word.Baseline + metrics.GetTextHeight(font, 72) - metrics.GetDescent(font, 72);
        }

        private static XLAlignmentHorizontalValues ExcelAlignment(VisualLine line, double left, double right)
        {
            double tolerance = line.FontSizePt / 2;
            if (Math.Abs((line.Left + line.Right - left - right) / 2) <= tolerance) return XLAlignmentHorizontalValues.Center;
            if (Math.Abs(right - line.Right) <= tolerance && line.Left - left > tolerance) return XLAlignmentHorizontalValues.Right;
            return XLAlignmentHorizontalValues.Left;
        }

        private static W.Table BuildOpenXmlTable(
            LayoutRegion tableRegion,
            double pageLeftMarginPt,
            double availableWidthPt,
            Page page,
            MainDocumentPart? mainPart,
            List<StandaloneImageItem>? standaloneImages,
            List<StandaloneImageItem>? pendingStandalone,
            List<StandaloneImageItem>? floatingImages,
            ref uint docPrId,
            ref int imagesCount,
            bool isCurveOcrPage = false)
        {
            if (tableRegion.RuledRows.Count > 0)
            {
                return BuildRuledWordTable(tableRegion, pageLeftMarginPt, page, mainPart, standaloneImages, pendingStandalone, floatingImages, ref docPrId, ref imagesCount, isCurveOcrPage);
            }
            var anchors = tableRegion.ColumnAnchors;
            int colCount = anchors.Count;
            double actualSpanPt = Math.Max(144.0, tableRegion.Right - anchors[0] + 24.0);
            double effectiveWidthPt = actualSpanPt < availableWidthPt * 0.82
                ? Math.Clamp(actualSpanPt, 144.0, availableWidthPt)
                : Math.Max(288.0, availableWidthPt);
            int totalWidthDxa = (int)Math.Round(effectiveWidthPt * 20.0);

            int[] colWidthsDxa = new int[colCount];
            double tableRightEdgePt = Math.Max(anchors.Last() + 48.0, anchors[0] + effectiveWidthPt);
            int sumDxa = 0;
            for (int c = 0; c < colCount; c++)
            {
                double startX = anchors[c];
                double endX = (c < colCount - 1) ? anchors[c + 1] : tableRightEdgePt;
                int wDxa = Math.Max(720, (int)Math.Round((endX - startX) * 20.0));
                colWidthsDxa[c] = wDxa;
                sumDxa += wDxa;
            }

            if (sumDxa > 0 && sumDxa != totalWidthDxa)
            {
                double scale = (double)totalWidthDxa / sumDxa;
                for (int c = 0; c < colCount; c++)
                {
                    colWidthsDxa[c] = Math.Max(720, (int)Math.Round(colWidthsDxa[c] * scale));
                }
            }

            var table = new W.Table();
            var tblPr = new W.TableProperties(
                new W.TableWidth { Width = totalWidthDxa.ToString(CultureInfo.InvariantCulture), Type = W.TableWidthUnitValues.Dxa });
            int tableIndentDxa = (int)Math.Round(Math.Max(0, anchors[0] - pageLeftMarginPt) * 20.0);
            if (tableIndentDxa >= 40)
            {
                tblPr.Append(new W.TableIndentation { Width = tableIndentDxa, Type = W.TableWidthUnitValues.Dxa });
            }
            tblPr.Append(new W.TableBorders(
                new W.TopBorder { Val = W.BorderValues.Single, Size = 4, Space = 0, Color = "BFBFBF" },
                new W.LeftBorder { Val = W.BorderValues.Single, Size = 4, Space = 0, Color = "BFBFBF" },
                new W.BottomBorder { Val = W.BorderValues.Single, Size = 4, Space = 0, Color = "BFBFBF" },
                new W.RightBorder { Val = W.BorderValues.Single, Size = 4, Space = 0, Color = "BFBFBF" },
                new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4, Space = 0, Color = "D9D9D9" },
                new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4, Space = 0, Color = "D9D9D9" }),
                new W.TableCellMarginDefault(
                    new W.TopMargin { Width = "20", Type = W.TableWidthUnitValues.Dxa },
                    new W.TableCellLeftMargin { Width = 100, Type = W.TableWidthValues.Dxa },
                    new W.BottomMargin { Width = "20", Type = W.TableWidthUnitValues.Dxa },
                    new W.TableCellRightMargin { Width = 100, Type = W.TableWidthValues.Dxa }));
            table.AppendChild(tblPr);

            var tblGrid = new W.TableGrid();
            for (int c = 0; c < colCount; c++)
            {
                tblGrid.Append(new W.GridColumn { Width = colWidthsDxa[c].ToString(CultureInfo.InvariantCulture) });
            }
            table.AppendChild(tblGrid);

            for (int r = 0; r < tableRegion.TableRows.Count; r++)
            {
                var visualLine = tableRegion.Lines[r];
                bool isHeader = r == 0 && visualLine.IsBold;

                var tr = new W.TableRow();
                for (int c = 0; c < colCount; c++)
                {
                    var tc = new W.TableCell();
                    var tcPr = new W.TableCellProperties(
                        new W.TableCellWidth { Width = colWidthsDxa[c].ToString(CultureInfo.InvariantCulture), Type = W.TableWidthUnitValues.Dxa });

                    if (isHeader)
                    {
                        tcPr.Append(new W.Shading { Val = W.ShadingPatternValues.Clear, Color = "auto", Fill = "F2F2F2" });
                    }
                    tc.Append(tcPr);

                    var cellWords = visualLine.Segments
                        .Where((segment, index) => GetTableColumn(visualLine, index, anchors) == c)
                        .SelectMany(segment => segment.Words).ToList();
                    double colStartX = anchors[c];
                    double colEndX = (c < colCount - 1) ? anchors[c + 1] : tableRightEdgePt;
                    double colSpanW = Math.Max(20.0, colEndX - colStartX);
                    var cellJc = W.JustificationValues.Left;
                    if (cellWords.Count > 0)
                    {
                        double wLeft = cellWords.Min(w => w.Left);
                        double wRight = cellWords.Max(w => w.Right);
                        double lPad = Math.Max(0, wLeft - colStartX);
                        double rPad = Math.Max(0, colEndX - wRight);
                        if (Math.Abs(lPad - rPad) <= Math.Max(6.5, colSpanW * 0.10) && lPad >= 3.5)
                            cellJc = W.JustificationValues.Center;
                        else if (rPad <= 8.0 && lPad >= Math.Max(16.0, colSpanW * 0.22))
                            cellJc = W.JustificationValues.Right;
                    }

                    var cellPara = new W.Paragraph(
                        new W.ParagraphProperties(
                            new W.SpacingBetweenLines { Before = "40", After = "40" },
                            new W.Justification { Val = cellJc }));
                    AppendConsolidatedRuns(cellPara, cellWords, 0);

                    tc.Append(cellPara);
                    tr.Append(tc);
                }
                table.Append(tr);
            }

            return table;
        }

        private static void ApplyEditableNumbering(MainDocumentPart mainPart)
        {
            foreach (var cell in mainPart.Document.Descendants<W.TableCell>())
            {
                var items = cell.Elements<W.Paragraph>().Select(p => (Paragraph: p, Match: BodyListMarkerRegex.Match(p.InnerText)))
                    .Where(x => x.Match.Success).ToList();
                if (items.Count < 2) continue;
                var markers = items.Select((item, idx) =>
                    BodyListMarker(
                        item.Match.Groups[1].Value,
                        idx + 1 < items.Count ? items[idx + 1].Match.Groups[1].Value : null,
                        idx > 0 ? items[idx - 1].Match.Groups[1].Value : null)).ToList();
                var first = markers[0];
                if (markers.Any(m => m.Format != first.Format || m.Suffix != first.Suffix)
                    || (first.Format != W.NumberFormatValues.Bullet
                        && markers.Select((m, index) => m.Value - index).Distinct().Count() != 1)) continue;
                var part = mainPart.NumberingDefinitionsPart ?? mainPart.AddNewPart<NumberingDefinitionsPart>();
                part.Numbering ??= new W.Numbering();
                int id = part.Numbering.Elements<W.NumberingInstance>().Select(n => n.NumberID!.Value).DefaultIfEmpty(0).Max() + 1;
                string lvlText = first.Format == W.NumberFormatValues.Bullet
                    ? first.Suffix
                    : (first.Suffix == "()" ? "(%1)" : "%1" + first.Suffix);
                var level = new W.Level(new W.StartNumberingValue { Val = first.Value },
                    new W.NumberingFormat { Val = first.Format }, new W.LevelText { Val = lvlText },
                    new W.LevelJustification { Val = W.LevelJustificationValues.Left }) { LevelIndex = 0 };
                var markerStyle = items[0].Paragraph.Elements<W.Run>().FirstOrDefault()?.RunProperties;
                var markerProperties = new W.NumberingSymbolRunProperties();
                if (markerStyle?.RunFonts != null) markerProperties.Append(markerStyle.RunFonts.CloneNode(true));
                if (markerStyle?.FontSize != null) markerProperties.Append(markerStyle.FontSize.CloneNode(true));
                level.Append(markerProperties);
                var definition = new W.AbstractNum(level) { AbstractNumberId = id };
                part.Numbering.InsertBefore(definition, part.Numbering.Elements<W.NumberingInstance>().FirstOrDefault());
                part.Numbering.Append(new W.NumberingInstance(new W.AbstractNumId { Val = id }) { NumberID = id });
                var cellParas = cell.Elements<W.Paragraph>().ToList();
                bool anyJustifiedInList = items.Any(it => it.Paragraph.ParagraphProperties?.Justification?.Val?.Value == W.JustificationValues.Both);
                for (int itemIdx = 0; itemIdx < items.Count; itemIdx++)
                {
                    var item = items[itemIdx];
                    var nextItemPara = itemIdx + 1 < items.Count ? items[itemIdx + 1].Paragraph : null;
                    int startP = cellParas.IndexOf(item.Paragraph);
                    int itemMarkerLeft = int.TryParse(item.Paragraph.ParagraphProperties?.Indentation?.Left?.Value, out int iml) ? iml : 0;
                    var cellConts = new List<W.Paragraph>();
                    for (int pIdx = startP + 1; pIdx < cellParas.Count; pIdx++)
                    {
                        var cp = cellParas[pIdx];
                        if (ReferenceEquals(cp, nextItemPara) || BodyListMarkerRegex.IsMatch(cp.InnerText)) break;
                        int cpLeft = int.TryParse(cp.ParagraphProperties?.Indentation?.Left?.Value, out int cpl) ? cpl : 0;
                        if (nextItemPara != null || cpLeft >= itemMarkerLeft + 80)
                        {
                            if (cp.ParagraphProperties?.Justification?.Val?.Value == W.JustificationValues.Both)
                                anyJustifiedInList = true;
                            cellConts.Add(cp);
                        }
                        else break;
                    }
                    MergeContinuationParagraphsIntoListItem(item.Paragraph, cellConts);
                }
                foreach (var item in items)
                {
                    int remove = item.Match.Length;
                    foreach (var text in item.Paragraph.Descendants<W.Text>())
                    {
                        int count = Math.Min(remove, text.Text.Length); text.Text = text.Text[count..]; remove -= count;
                        if (remove == 0) break;
                    }
                    var properties = item.Paragraph.ParagraphProperties ??= new W.ParagraphProperties();
                    properties.NumberingProperties = new W.NumberingProperties(new W.NumberingLevelReference { Val = 0 }, new W.NumberingId { Val = id });
                    int left = int.TryParse(properties.Indentation?.Left?.Value, out int offset) ? offset : 0;
                    properties.Indentation = new W.Indentation { Left = (left + 280).ToString(CultureInfo.InvariantCulture), Hanging = "280" };
                    if (anyJustifiedInList)
                    {
                        properties.Justification = new W.Justification { Val = W.JustificationValues.Both };
                    }
                }
                part.Numbering.Save();
            }
            ApplyEditableBodyNumbering(mainPart);
        }

        private static readonly Regex BodyListMarkerRegex = new(
            @"^(\(\d{1,3}\)|\([a-zA-Z]\)|\([ivxlcdmIVXLCDM]{1,6}\)|\d{1,3}[.)]|[ivxlcdmIVXLCDM]{1,6}[.)]|[a-zA-Z][.)]|[•●▪◦‣⁃∙·])\s+",
            RegexOptions.Compiled);

        private static bool TryParseRomanNumeral(string token, out int value, out bool isUpper)
        {
            value = 0;
            isUpper = false;
            if (string.IsNullOrEmpty(token)) return false;
            isUpper = char.IsUpper(token[0]);
            for (int i = 0; i < token.Length; i++)
            {
                if (char.IsUpper(token[i]) != isUpper) return false;
            }
            string upper = token.ToUpperInvariant();
            if (!Regex.IsMatch(upper, @"^M{0,3}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$")) return false;
            int ValueOf(char c) => c switch
            {
                'I' => 1, 'V' => 5, 'X' => 10, 'L' => 50, 'C' => 100, 'D' => 500, 'M' => 1000, _ => 0
            };
            int total = 0;
            for (int i = 0; i < upper.Length; i++)
            {
                int v = ValueOf(upper[i]);
                if (v == 0) return false;
                if (i + 1 < upper.Length && ValueOf(upper[i + 1]) > v) total -= v;
                else total += v;
            }
            value = total;
            return value > 0;
        }

        private static (W.NumberFormatValues Format, int Value, string Suffix) BodyListMarker(
            string text,
            string? nextMarkerText = null,
            string? prevMarkerText = null)
        {
            bool parenthesized = text.Length >= 3 && text[0] == '(' && text[^1] == ')';
            string core = parenthesized ? text[1..^1] : text.Length > 1 && (text[^1] is '.' or ')') ? text[..^1] : text;
            string suffix = parenthesized ? "()" : text.Length > 1 && (text[^1] is '.' or ')') ? text[^1..] : text;

            if (core.Length > 0 && core.All(char.IsDigit))
            {
                return (W.NumberFormatValues.Decimal, int.Parse(core, CultureInfo.InvariantCulture), suffix);
            }

            if (TryParseRomanNumeral(core, out int romanVal, out bool romanUpper))
            {
                if (core.Length >= 2)
                {
                    return (romanUpper ? W.NumberFormatValues.UpperRoman : W.NumberFormatValues.LowerRoman, romanVal, suffix);
                }

                string? nextCore = !string.IsNullOrEmpty(nextMarkerText) ? nextMarkerText.Trim('(', ')', '.', ' ') : null;
                string? prevCore = !string.IsNullOrEmpty(prevMarkerText) ? prevMarkerText.Trim('(', ')', '.', ' ') : null;

                if (core.Equals("i", StringComparison.OrdinalIgnoreCase))
                {
                    bool partOfAlphabeticalHtoJ =
                        (prevCore != null && prevCore.Equals("h", StringComparison.OrdinalIgnoreCase) && char.IsUpper(prevCore[0]) == romanUpper)
                        || (nextCore != null && nextCore.Equals("j", StringComparison.OrdinalIgnoreCase) && char.IsUpper(nextCore[0]) == romanUpper);
                    if (!partOfAlphabeticalHtoJ)
                    {
                        return (romanUpper ? W.NumberFormatValues.UpperRoman : W.NumberFormatValues.LowerRoman, 1, suffix);
                    }
                }
                else
                {
                    bool nextIsRoman = nextCore != null && TryParseRomanNumeral(nextCore, out int nextRom, out bool nextUp)
                        && nextUp == romanUpper && nextRom == romanVal + 1;
                    bool prevIsRoman = prevCore != null && TryParseRomanNumeral(prevCore, out int prevRom, out bool prevUp)
                        && prevUp == romanUpper && prevRom == romanVal - 1;
                    if (nextIsRoman || prevIsRoman)
                    {
                        return (romanUpper ? W.NumberFormatValues.UpperRoman : W.NumberFormatValues.LowerRoman, romanVal, suffix);
                    }
                }
            }

            if (core.Length == 1 && char.IsAsciiLetter(core[0]))
            {
                return (char.IsUpper(core[0]) ? W.NumberFormatValues.UpperLetter : W.NumberFormatValues.LowerLetter,
                    char.ToLowerInvariant(core[0]) - 'a' + 1, suffix);
            }

            return (W.NumberFormatValues.Bullet, 1, text);
        }

        private static bool HasListMarkerToken(VisualLine line, out string markerToken)
        {
            markerToken = string.Empty;
            if (line.Words.Count < 2 || line.Segments.Count > 2) return false;
            var m = BodyListMarkerRegex.Match(line.FullText);
            if (!m.Success || line.Words[0].Text != m.Groups[1].Value) return false;
            markerToken = m.Groups[1].Value;
            return true;
        }

        private static bool IsWrappedListContinuationVisualLine(
            VisualLine markerLine,
            VisualLine candidateLine,
            double prevSubLineTop,
            double prevSubLineBottom,
            double markerTextLeft)
        {
            if (HasListMarkerToken(candidateLine, out _)) return false;
            if (BulletPrefixRegex.IsMatch(candidateLine.FullText)) return false;
            if (KeyValueMetadataRegex.IsMatch(candidateLine.FullText)) return false;
            if (candidateLine.Segments.Count > 1) return false;
            if (Math.Abs(candidateLine.FontSizePt - markerLine.FontSizePt) > 1.5) return false;
            double vGap = prevSubLineBottom - candidateLine.Top;
            double lineStep = prevSubLineTop - candidateLine.Top;
            if (vGap < -3.0 || lineStep <= 0 || lineStep > markerLine.FontSizePt * 2.2) return false;
            return candidateLine.Left >= Math.Min(markerLine.Left, 24.0) - 4.0 && candidateLine.Left <= markerTextLeft + 36.0;
        }

        private static bool AreCompatibleListNeighborLines(VisualLine first, VisualLine second, string firstToken, string secondToken)
        {
            double indentDiff = second.Left - first.Left;
            var m1 = BodyListMarker(firstToken, secondToken, null);
            var m2 = BodyListMarker(secondToken, null, firstToken);

            if (Math.Abs(indentDiff) <= 4.0 || (Math.Abs(indentDiff) <= 10.0 && Math.Abs(second.Words[1].Left - first.Words[1].Left) <= 4.0))
            {
                if (m1.Format != m2.Format || m1.Suffix != m2.Suffix) return false;
                if (m1.Format != W.NumberFormatValues.Bullet && m2.Value != m1.Value + 1) return false;
                double maxTextShift = m1.Format == W.NumberFormatValues.Bullet ? 2.0 : Math.Max(14.0, first.FontSizePt * 1.6);
                return Math.Abs(second.Words[1].Left - first.Words[1].Left) <= maxTextShift;
            }

            if (indentDiff > 4.0 && indentDiff <= 72.0)
            {
                return m2.Format == W.NumberFormatValues.Bullet || m2.Value == 1;
            }

            return false;
        }

        private static bool IsLinePartOfBodyList(IReadOnlyList<VisualLine> lines, int lineIndex, bool prevPageEndedWithList = false)
        {
            if (lineIndex < 0 || lineIndex >= lines.Count) return false;
            var line = lines[lineIndex];
            if (!HasListMarkerToken(line, out string curToken)) return false;

            for (int p = lineIndex - 1; p >= 0; p--)
            {
                var prevLine = lines[p];
                if (HasListMarkerToken(prevLine, out string prevToken))
                {
                    if (prevLine.Left > line.Left + 4.0)
                    {
                        continue;
                    }
                    if (AreCompatibleListNeighborLines(prevLine, line, prevToken, curToken))
                        return true;
                    break;
                }
                if (KeyValueMetadataRegex.IsMatch(prevLine.FullText) || prevLine.Segments.Count > 1)
                    break;
            }

            double prevTop = line.Top;
            double prevBottom = line.Bottom;
            double textLeft = line.Words[1].Left;
            for (int n = lineIndex + 1; n < lines.Count; n++)
            {
                var nextLine = lines[n];
                if (HasListMarkerToken(nextLine, out string nextToken))
                {
                    if (AreCompatibleListNeighborLines(line, nextLine, curToken, nextToken))
                        return true;
                    if (nextLine.Left > line.Left + 4.0)
                        continue;
                    break;
                }
                if (IsWrappedListContinuationVisualLine(line, nextLine, prevTop, prevBottom, textLeft))
                {
                    prevTop = nextLine.Top;
                    prevBottom = nextLine.Bottom;
                    continue;
                }
                break;
            }

            if (prevPageEndedWithList && lineIndex == 0)
            {
                var m = BodyListMarker(curToken);
                if (m.Format == W.NumberFormatValues.Bullet || m.Value >= 2)
                    return true;
            }

            return false;
        }

        private static bool HasAnyBodyListItems(IReadOnlyList<VisualLine> lines, bool prevPageEndedWithList = false)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (IsLinePartOfBodyList(lines, i, prevPageEndedWithList)) return true;
            }
            return false;
        }


        private static void ApplyEditableBodyNumbering(MainDocumentPart mainPart)
        {
            var group = new List<(W.Paragraph Paragraph, Match Match, List<W.Paragraph> Continuations)>();
            int? lastCrossPageNumId = null;
            (W.NumberFormatValues Format, int LastVal, string Suffix, int MarkerLeft, int TextLeft)? lastCrossPageState = null;

            string FormatLevelText(int levelIdx, (W.NumberFormatValues Format, int Value, string Suffix) marker)
            {
                if (marker.Format == W.NumberFormatValues.Bullet) return marker.Suffix;
                string token = $"%{levelIdx + 1}";
                return marker.Suffix == "()" ? $"({token})" : token + marker.Suffix;
            }

            void Flush(bool endedAtPageBreak = false)
            {
                if (group.Count == 1 && lastCrossPageNumId.HasValue && lastCrossPageState.HasValue)
                {
                    var only = group[0];
                    var current = BodyListMarker(only.Match.Groups[1].Value);
                    var prev = lastCrossPageState.Value;
                    int markerLeft = int.TryParse(only.Paragraph.ParagraphProperties?.Indentation?.Left?.Value, out int ml) ? ml : 0;
                    if (current.Format == prev.Format && current.Suffix == prev.Suffix
                        && ((current.Format != W.NumberFormatValues.Bullet && current.Value == prev.LastVal + 1)
                            || (current.Format == W.NumberFormatValues.Bullet && Math.Abs(markerLeft - prev.MarkerLeft) <= 100)))
                    {
                        MergeContinuationParagraphsIntoListItem(only.Paragraph, only.Continuations);
                        StripMarkerAndApplyNumbering(only.Paragraph, only.Match.Length, lastCrossPageNumId.Value, 0);
                        lastCrossPageState = (current.Format, current.Value, current.Suffix, markerLeft, prev.TextLeft);
                        if (!endedAtPageBreak) { lastCrossPageNumId = null; lastCrossPageState = null; }
                        group.Clear();
                        return;
                    }
                }

                if (group.Count >= 2)
                {
                    var distinctLefts = group
                        .Select(item => int.TryParse(item.Paragraph.ParagraphProperties?.Indentation?.Left?.Value, out int l) ? l : 0)
                        .OrderBy(x => x).ToList();
                    var levelIndents = new List<int>();
                    foreach (int x in distinctLefts)
                    {
                        if (levelIndents.Count == 0 || x - levelIndents[^1] > 100) levelIndents.Add(x);
                    }
                    int LevelOf(W.Paragraph p)
                    {
                        int left = int.TryParse(p.ParagraphProperties?.Indentation?.Left?.Value, out int l) ? l : 0;
                        return Math.Clamp(levelIndents.FindLastIndex(a => a <= left + 60), 0, 8);
                    }

                    string? secondAtLvl0Raw = group.Skip(1).FirstOrDefault(g => LevelOf(g.Paragraph) == 0).Match?.Groups[1].Value;
                    var firstMarker = BodyListMarker(group[0].Match.Groups[1].Value, secondAtLvl0Raw, null);
                    int firstLeft = int.TryParse(group[0].Paragraph.ParagraphProperties?.Indentation?.Left?.Value, out int fl) ? fl : 0;
                    int firstTextLeft = group[0].Paragraph.ParagraphProperties?.Tabs?.Elements<W.TabStop>().FirstOrDefault()?.Position?.Value ?? (firstLeft + 320);

                    int id;
                    var part = mainPart.NumberingDefinitionsPart ?? mainPart.AddNewPart<NumberingDefinitionsPart>();
                    part.Numbering ??= new W.Numbering();

                    if (lastCrossPageNumId.HasValue && lastCrossPageState.HasValue
                        && LevelOf(group[0].Paragraph) == 0
                        && firstMarker.Format == lastCrossPageState.Value.Format
                        && firstMarker.Suffix == lastCrossPageState.Value.Suffix
                        && ((firstMarker.Format != W.NumberFormatValues.Bullet && firstMarker.Value == lastCrossPageState.Value.LastVal + 1)
                            || (firstMarker.Format == W.NumberFormatValues.Bullet && Math.Abs(firstLeft - lastCrossPageState.Value.MarkerLeft) <= 100)))
                    {
                        id = lastCrossPageNumId.Value;
                    }
                    else
                    {
                        id = part.Numbering.Elements<W.NumberingInstance>().Select(n => n.NumberID!.Value).DefaultIfEmpty(0).Max() + 1;
                        var abstractNum = new W.AbstractNum { AbstractNumberId = id };
                        for (int lvlIdx = 0; lvlIdx < Math.Min(9, levelIndents.Count); lvlIdx++)
                        {
                            int firstIdxAtLevel = group.FindIndex(g => LevelOf(g.Paragraph) == lvlIdx);
                            if (firstIdxAtLevel < 0) firstIdxAtLevel = 0;
                            string? nextSameLvlRaw = group.Skip(firstIdxAtLevel + 1)
                                .FirstOrDefault(g => LevelOf(g.Paragraph) == lvlIdx).Match?.Groups[1].Value;
                            var lvlMarker = BodyListMarker(group[firstIdxAtLevel].Match.Groups[1].Value, nextSameLvlRaw, null);
                            var level = new W.Level(
                                new W.StartNumberingValue { Val = lvlMarker.Value },
                                new W.NumberingFormat { Val = lvlMarker.Format },
                                new W.LevelText { Val = FormatLevelText(lvlIdx, lvlMarker) },
                                new W.LevelJustification { Val = W.LevelJustificationValues.Left }) { LevelIndex = lvlIdx };
                            var style = group[firstIdxAtLevel].Paragraph.Elements<W.Run>().FirstOrDefault()?.RunProperties;
                            var markerStyle = new W.NumberingSymbolRunProperties();
                            if (style?.RunFonts != null) markerStyle.Append(style.RunFonts.CloneNode(true));
                            if (style?.FontSize != null) markerStyle.Append(style.FontSize.CloneNode(true));
                            level.Append(markerStyle);
                            abstractNum.Append(level);
                        }
                        part.Numbering.InsertBefore(abstractNum, part.Numbering.Elements<W.NumberingInstance>().FirstOrDefault());
                        part.Numbering.Append(new W.NumberingInstance(new W.AbstractNumId { Val = id }) { NumberID = id });
                    }

                    for (int i = 0; i < group.Count; i++)
                    {
                        var item = group[i];
                        int lvl = LevelOf(item.Paragraph);
                        MergeContinuationParagraphsIntoListItem(item.Paragraph, item.Continuations);
                        StripMarkerAndApplyNumbering(item.Paragraph, item.Match.Length, id, lvl);
                        if (lvl == 0)
                        {
                            string? nextSameRaw = group.Skip(i + 1).FirstOrDefault(g => LevelOf(g.Paragraph) == 0).Match?.Groups[1].Value;
                            string? prevSameRaw = group.Take(i).LastOrDefault(g => LevelOf(g.Paragraph) == 0).Match?.Groups[1].Value;
                            var m = BodyListMarker(item.Match.Groups[1].Value, nextSameRaw, prevSameRaw);
                            lastCrossPageState = (m.Format, m.Value, m.Suffix, firstLeft, firstTextLeft);
                        }
                    }
                    part.Numbering.Save();
                    lastCrossPageNumId = endedAtPageBreak ? id : null;
                    if (!endedAtPageBreak) lastCrossPageState = null;
                }
                else if (!endedAtPageBreak)
                {
                    lastCrossPageNumId = null;
                    lastCrossPageState = null;
                }
                group.Clear();
            }

            var bodyChildren = mainPart.Document.Body!.ChildElements.ToList();
            for (int idx = 0; idx < bodyChildren.Count; idx++)
            {
                var child = bodyChildren[idx];
                if (child is not W.Paragraph paragraph)
                {
                    Flush(false);
                    continue;
                }
                bool hasPageBreak = paragraph.ParagraphProperties?.SectionProperties != null;
                bool hasSingleTabStop = paragraph.ParagraphProperties?.Tabs?.Elements<W.TabStop>().Count() == 1
                    && !paragraph.Descendants<W.TabChar>().Any()
                    && paragraph.ParagraphProperties.NumberingProperties == null;

                var match = BodyListMarkerRegex.Match(paragraph.InnerText);
                if (!hasSingleTabStop && match.Success && group.Count == 0 && lastCrossPageNumId.HasValue && lastCrossPageState.HasValue
                    && !paragraph.Descendants<W.TabChar>().Any() && paragraph.ParagraphProperties?.NumberingProperties == null)
                {
                    var candMarker = BodyListMarker(match.Groups[1].Value);
                    int candLeft = int.TryParse(paragraph.ParagraphProperties?.Indentation?.Left?.Value, out int cl) ? cl : 0;
                    if (candMarker.Format == lastCrossPageState.Value.Format && candMarker.Suffix == lastCrossPageState.Value.Suffix
                        && ((candMarker.Format != W.NumberFormatValues.Bullet && candMarker.Value == lastCrossPageState.Value.LastVal + 1)
                            || (candMarker.Format == W.NumberFormatValues.Bullet && Math.Abs(candLeft - lastCrossPageState.Value.MarkerLeft) <= 100)))
                    {
                        (paragraph.ParagraphProperties ??= new W.ParagraphProperties()).Tabs = new W.Tabs(
                            new W.TabStop { Val = W.TabStopValues.Left, Position = lastCrossPageState.Value.TextLeft });
                        hasSingleTabStop = true;
                    }
                }

                if (!hasSingleTabStop || !match.Success)
                {
                    var lastParaInItem = group.Count > 0 ? (group[^1].Continuations.LastOrDefault() ?? group[^1].Paragraph) : null;
                    if (group.Count > 0 && lastParaInItem != null && IsWrappedContinuationOfListParagraph(group[^1].Paragraph, lastParaInItem, paragraph))
                    {
                        group[^1].Continuations.Add(paragraph);
                        if (hasPageBreak) Flush(true);
                        continue;
                    }
                    Flush(hasPageBreak);
                    continue;
                }

                int curLeft = int.TryParse(paragraph.ParagraphProperties?.Indentation?.Left?.Value, out int curL) ? curL : 0;
                int curTextLeft = paragraph.ParagraphProperties?.Tabs?.Elements<W.TabStop>().FirstOrDefault()?.Position?.Value ?? -1;
                bool SameIndentationLevel(W.Paragraph p)
                {
                    int pLeft = int.TryParse(p.ParagraphProperties?.Indentation?.Left?.Value, out int pl) ? pl : 0;
                    int pTextLeft = p.ParagraphProperties?.Tabs?.Elements<W.TabStop>().FirstOrDefault()?.Position?.Value ?? -1;
                    return Math.Abs(pLeft - curLeft) <= 100 || (curTextLeft >= 0 && pTextLeft >= 0 && Math.Abs(pTextLeft - curTextLeft) <= 60);
                }
                int sameLevelIdx = group.FindLastIndex(g => SameIndentationLevel(g.Paragraph));

                string? lookaheadRaw = null;
                for (int nextIdx = idx + 1; nextIdx < bodyChildren.Count && nextIdx <= idx + 4; nextIdx++)
                {
                    if (bodyChildren[nextIdx] is W.Paragraph nextP)
                    {
                        int nextLeft = int.TryParse(nextP.ParagraphProperties?.Indentation?.Left?.Value, out int nl) ? nl : 0;
                        var nm = BodyListMarkerRegex.Match(nextP.InnerText);
                        if (nm.Success && Math.Abs(nextLeft - curLeft) <= 60)
                        {
                            lookaheadRaw = nm.Groups[1].Value;
                            break;
                        }
                    }
                }
                string? sameLevelPrevRaw = sameLevelIdx >= 0 ? group[sameLevelIdx].Match.Groups[1].Value : null;
                var current = BodyListMarker(match.Groups[1].Value, lookaheadRaw, sameLevelPrevRaw);

                if (group.Count > 0)
                {
                    if (sameLevelIdx >= 0)
                    {
                        int prevSameIdx = group.Take(sameLevelIdx).ToList().FindLastIndex(g => SameIndentationLevel(g.Paragraph));
                        string? priorRaw = prevSameIdx >= 0 ? group[prevSameIdx].Match.Groups[1].Value : null;
                        var previous = BodyListMarker(group[sameLevelIdx].Match.Groups[1].Value, match.Groups[1].Value, priorRaw);
                        if (current.Format != previous.Format || current.Suffix != previous.Suffix
                            || (current.Format != W.NumberFormatValues.Bullet && current.Value != previous.Value + 1))
                        {
                            Flush(false);
                            current = BodyListMarker(match.Groups[1].Value, lookaheadRaw, null);
                        }
                    }
                    else
                    {
                        int baseLeft = int.TryParse(group[0].Paragraph.ParagraphProperties?.Indentation?.Left?.Value, out int bl) ? bl : 0;
                        if (curLeft < baseLeft - 40 || (current.Format != W.NumberFormatValues.Bullet && current.Value != 1))
                        {
                            Flush(false);
                        }
                    }
                }

                group.Add((paragraph, match, new List<W.Paragraph>()));
                if (hasPageBreak)
                {
                    Flush(true);
                }
            }
            Flush(false);
        }

        private static bool IsWrappedContinuationOfListParagraph(W.Paragraph listParagraph, W.Paragraph prevParagraph, W.Paragraph candidate)
        {
            if (candidate.ParagraphProperties?.NumberingProperties != null) return false;
            string candText = candidate.InnerText.Trim();
            if (string.IsNullOrWhiteSpace(candText)) return false;
            if (BodyListMarkerRegex.IsMatch(candText)) return false;
            if (Regex.IsMatch(candText, @"^\d+(\.\d+)+\b")) return false;

            int prevAfterDxa = int.TryParse(prevParagraph.ParagraphProperties?.SpacingBetweenLines?.After?.Value, out int aDxa) ? aDxa : 0;
            int beforeDxa = int.TryParse(candidate.ParagraphProperties?.SpacingBetweenLines?.Before?.Value, out int bDxa) ? bDxa : 0;
            if (prevAfterDxa > 160 || beforeDxa > 240) return false;

            string? listSz = listParagraph.Descendants<W.FontSize>().FirstOrDefault()?.Val?.Value;
            string? candSz = candidate.Descendants<W.FontSize>().FirstOrDefault()?.Val?.Value;
            if (int.TryParse(listSz, out int lHalfPt) && int.TryParse(candSz, out int cHalfPt) && Math.Abs(cHalfPt - lHalfPt) > 3)
                return false;
            bool listFirstBold = listParagraph.Elements<W.Run>().FirstOrDefault()?.RunProperties?.Bold != null;
            bool candAllBold = candidate.Elements<W.Run>().Any() && candidate.Elements<W.Run>().All(r => r.RunProperties?.Bold != null);
            if (candAllBold && !listFirstBold) return false;

            int markerLeft = int.TryParse(listParagraph.ParagraphProperties?.Indentation?.Left?.Value, out int mLeft) ? mLeft : 0;
            int textLeft = listParagraph.ParagraphProperties?.Tabs?.Elements<W.TabStop>().FirstOrDefault()?.Position?.Value ?? (markerLeft + 320);
            int candLeft = int.TryParse(candidate.ParagraphProperties?.Indentation?.Left?.Value, out int cLeft) ? cLeft : 0;
            return candLeft >= Math.Max(0, markerLeft - 80) && candLeft <= textLeft + 480;
        }

        private static void MergeContinuationParagraphsIntoListItem(W.Paragraph listParagraph, List<W.Paragraph> continuations)
        {
            if (continuations.Count == 0) return;
            foreach (var cont in continuations)
            {
                listParagraph.Append(new W.Run(new W.Text(" ") { Space = SpaceProcessingModeValues.Preserve }));
                foreach (var run in cont.Elements<W.Run>().ToList())
                {
                    var cloned = (W.Run)run.CloneNode(true);
                    cloned.RunProperties?.RemoveAllChildren<W.FitText>();
                    listParagraph.Append(cloned);
                }
                if (cont.ParagraphProperties?.SectionProperties != null && listParagraph.ParagraphProperties != null)
                {
                    listParagraph.ParagraphProperties.SectionProperties = (W.SectionProperties)cont.ParagraphProperties.SectionProperties.CloneNode(true);
                }
                cont.Remove();
            }
            foreach (var fitted in listParagraph.Descendants<W.FitText>().ToList())
            {
                fitted.Remove();
            }
        }

        private static void StripMarkerAndApplyNumbering(W.Paragraph paragraph, int removeLength, int numId, int levelIndex)
        {
            int remove = removeLength;
            foreach (var text in paragraph.Descendants<W.Text>())
            {
                int count = Math.Min(remove, text.Text.Length);
                text.Text = text.Text[count..];
                remove -= count;
                if (remove == 0) break;
            }
            var properties = paragraph.ParagraphProperties!;
            int markerLeft = int.TryParse(properties.Indentation?.Left?.Value, out int ml) ? ml : 0;
            int textLeft = properties.Tabs?.Elements<W.TabStop>().FirstOrDefault()?.Position?.Value ?? (markerLeft + 320);
            foreach (var fitted in paragraph.Descendants<W.FitText>())
            {
                fitted.Val = (uint)Math.Max(1, (long)fitted.Val!.Value - Math.Max(0, textLeft - markerLeft));
            }
            properties.Indentation = new W.Indentation
            {
                Left = textLeft.ToString(CultureInfo.InvariantCulture),
                Hanging = Math.Max(0, textLeft - markerLeft).ToString(CultureInfo.InvariantCulture)
            };
            properties.NumberingProperties = new W.NumberingProperties(
                new W.NumberingLevelReference { Val = levelIndex },
                new W.NumberingId { Val = numId });
        }

        private static W.Table BuildRuledWordTable(
            LayoutRegion region,
            double pageLeftMarginPt,
            Page page,
            MainDocumentPart? mainPart,
            List<StandaloneImageItem>? standaloneImages,
            List<StandaloneImageItem>? pendingStandalone,
            List<StandaloneImageItem>? floatingImages,
            ref uint docPrId,
            ref int imagesCount,
            bool isCurveOcrPage = false)
        {
            var xs = region.GridColumns;
            double cellPadPt = isCurveOcrPage ? 0.0 : 5.0;
            short cellPadDxa = (short)Math.Round(cellPadPt * 20.0);
            var fills = page.ExperimentalAccess.Paths.Where(path => path.IsFilled && TryGetColorRgb(path.FillColor, out _))
                .SelectMany(path => path.Select(sub => (Bounds: sub.GetBoundingRectangle(), Color: path.FillColor)))
                .Where(fill => fill.Bounds.HasValue && fill.Bounds.Value.Width > 4 && fill.Bounds.Value.Height > 4).ToList();
            var table = new W.Table(new W.TableProperties(
                new W.TableWidth { Type = W.TableWidthUnitValues.Dxa, Width = Math.Round((xs[^1] - xs[0]) * 20).ToString(CultureInfo.InvariantCulture) },
                new W.TableIndentation { Type = W.TableWidthUnitValues.Dxa, Width = (int)Math.Round((xs[0] - pageLeftMarginPt) * 20) },
                new W.TableBorders(
                    new W.TopBorder { Val = W.BorderValues.Single, Size = 4 }, new W.LeftBorder { Val = W.BorderValues.Single, Size = 4 },
                    new W.BottomBorder { Val = W.BorderValues.Single, Size = 4 }, new W.RightBorder { Val = W.BorderValues.Single, Size = 4 },
                    new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4 }, new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4 }),
                new W.TableLayout { Type = W.TableLayoutValues.Fixed },
                new W.TableCellMarginDefault(
                    new W.TopMargin { Width = "0", Type = W.TableWidthUnitValues.Dxa },
                    new W.TableCellLeftMargin { Width = cellPadDxa, Type = W.TableWidthValues.Dxa },
                    new W.BottomMargin { Width = "0", Type = W.TableWidthUnitValues.Dxa },
                    new W.TableCellRightMargin { Width = cellPadDxa, Type = W.TableWidthValues.Dxa })));
            table.Append(new W.TableGrid(Enumerable.Range(0, xs.Count - 1)
                .Select(c => new W.GridColumn { Width = Math.Round((xs[c + 1] - xs[c]) * 20).ToString(CultureInfo.InvariantCulture) })));

            foreach (var row in region.RuledRows)
            {
                double borderHeight = table.GetFirstChild<W.TableProperties>()!.TableBorders!.InsideHorizontalBorder!.Size!.Value / 8.0;
                var tr = new W.TableRow(new W.TableRowProperties(new W.TableRowHeight
                { Val = (uint)Math.Max(1, Math.Round((row.Top - row.Bottom - borderHeight) * 20)), HeightType = W.HeightRuleValues.AtLeast }));
                foreach (var cell in row.Cells)
                {
                    double colLeft = xs[cell.Column];
                    double colRight = xs[cell.Column + cell.Span];
                    double colWidth = Math.Max(10.0, colRight - colLeft);

                    var properties = new W.TableCellProperties(new W.TableCellWidth { Type = W.TableWidthUnitValues.Dxa,
                        Width = Math.Round(colWidth * 20).ToString(CultureInfo.InvariantCulture) });
                    if (cell.Span > 1) properties.Append(new W.GridSpan { Val = cell.Span });
                    if (cell.VerticalMerge.HasValue) properties.Append(new W.VerticalMerge { Val = cell.VerticalMerge.Value });
                    var fill = fills.LastOrDefault(f => f.Bounds!.Value.Left <= colLeft + borderHeight
                        && f.Bounds.Value.Right >= colRight - borderHeight
                        && f.Bounds.Value.Bottom <= row.Bottom + borderHeight && f.Bounds.Value.Top >= row.Top - borderHeight);
                    if (fill.Color != null)
                    {
                        var (red, green, blue) = SafeToRgbValues(fill.Color);
                        properties.Append(new W.Shading { Val = W.ShadingPatternValues.Clear,
                            Fill = $"{(int)Math.Round(red * 255):X2}{(int)Math.Round(green * 255):X2}{(int)Math.Round(blue * 255):X2}" });
                    }
                    var tc = new W.TableCell(properties);

                    // Collect any images whose center falls inside this cell (e.g., documentation photos in Lidia KKN tables)
                    var cellImages = new List<StandaloneImageItem>();
                    if (mainPart != null && standaloneImages != null && standaloneImages.Count > 0)
                    {
                        cellImages = standaloneImages
                            .Where(img =>
                            {
                                double icx = (img.LeftPt + img.RightPt) * 0.5;
                                double icy = (img.BottomPt + img.TopPt) * 0.5;
                                return icx >= colLeft - 2.0 && icx <= colRight + 2.0
                                    && icy >= row.Bottom - 2.0 && icy <= row.Top + 2.0;
                            })
                            .OrderByDescending(img => img.TopPt)
                            .ThenBy(img => img.LeftPt)
                            .ToList();
                        if (cellImages.Count > 0)
                        {
                            standaloneImages.RemoveAll(cellImages.Contains);
                            pendingStandalone?.RemoveAll(cellImages.Contains);
                            floatingImages?.RemoveAll(cellImages.Contains);
                        }
                    }

                    // Group cellImages into horizontal rows (photos side-by-side in the same cell share a Y row)
                    var imageRows = new List<List<StandaloneImageItem>>();
                    foreach (var cImg in cellImages)
                    {
                        double cMidY = (cImg.TopPt + cImg.BottomPt) * 0.5;
                        var existingRow = imageRows.FirstOrDefault(r => Math.Abs(((r[0].TopPt + r[0].BottomPt) * 0.5) - cMidY) <= 18.0);
                        if (existingRow != null) existingRow.Add(cImg);
                        else imageRows.Add(new List<StandaloneImageItem> { cImg });
                    }
                    foreach (var imgRow in imageRows)
                    {
                        imgRow.Sort((a, b) => a.LeftPt.CompareTo(b.LeftPt));
                    }

                    void EmitImageRow(List<StandaloneImageItem> imgRow, ref uint localDocPrId, ref int localImagesCount)
                    {
                        if (mainPart == null || imgRow.Count == 0) return;
                        double usableCellW = Math.Max(24.0, colWidth - cellPadPt * 2.0 - 4.0);
                        double maxPerImgW = Math.Max(20.0, (usableCellW - (imgRow.Count - 1) * 4.0) / imgRow.Count);
                        var imgPara = new W.Paragraph(new W.ParagraphProperties(
                            new W.SpacingBetweenLines { Before = "20", After = "20", Line = "240", LineRule = W.LineSpacingRuleValues.Auto },
                            new W.Justification { Val = W.JustificationValues.Center }));
                        for (int m = 0; m < imgRow.Count; m++)
                        {
                            var im = imgRow[m];
                            double wPt = im.WidthPt;
                            double hPt = im.HeightPt;
                            if (wPt > maxPerImgW && wPt > 0)
                            {
                                double s = maxPerImgW / wPt;
                                wPt *= s;
                                hPt *= s;
                            }
                            if (m > 0)
                            {
                                imgPara.Append(new W.Run(new W.Text(" ") { Space = SpaceProcessingModeValues.Preserve }));
                            }
                            imgPara.Append(new W.Run(BuildInlineDrawingElement(mainPart, im.PngBytes, wPt, hPt, localDocPrId++)));
                            localImagesCount++;
                        }
                        tc.Append(imgPara);
                    }

                    properties.Append(new W.TableCellVerticalAlignment { Val = W.TableVerticalAlignmentValues.Center });

                    // Deduplicate identical consecutive lines inside the same table cell on OCR pages (e.g., double-rendered "≤ 11.50")
                    var effectiveCellLines = new List<VisualLine>(cell.Lines.Count);
                    foreach (var cl in cell.Lines)
                    {
                        if (isCurveOcrPage && effectiveCellLines.Count > 0)
                        {
                            string prevNorm = Regex.Replace(effectiveCellLines[^1].FullText, @"\s+", "");
                            string curNorm = Regex.Replace(cl.FullText, @"\s+", "");
                            if (!string.IsNullOrEmpty(curNorm) && string.Equals(prevNorm, curNorm, StringComparison.OrdinalIgnoreCase))
                                continue;
                        }
                        effectiveCellLines.Add(cl);
                    }

                    int nextImgRowIdx = 0;
                    bool cellHasList = effectiveCellLines.Count(l => BodyListMarkerRegex.IsMatch(l.FullText)) >= 2;
                    bool cellReachesRight = effectiveCellLines.Count >= 2 && colWidth >= 85.0
                        && effectiveCellLines.Any(l => (colRight - l.Right) <= Math.Max(18.0, colWidth * 0.12) && (l.Left - colLeft) <= Math.Max(28.0, colWidth * 0.18));

                    for (int i = 0; i < effectiveCellLines.Count; i++)
                    {
                        var line = effectiveCellLines[i];
                        while (nextImgRowIdx < imageRows.Count && imageRows[nextImgRowIdx][0].BottomPt >= line.Top - 4.0)
                        {
                            EmitImageRow(imageRows[nextImgRowIdx++], ref docPrId, ref imagesCount);
                        }

                        // Merge wrapped lines of a non-list prose paragraph inside the cell
                        var paraLines = new List<VisualLine> { line };
                        bool lineIsListItem = BodyListMarkerRegex.IsMatch(line.FullText) || BulletPrefixRegex.IsMatch(line.FullText);
                        if (!isCurveOcrPage && !cellHasList && !lineIsListItem)
                        {
                            while (i + 1 < effectiveCellLines.Count)
                            {
                                var prevL = paraLines[^1];
                                var nextL = effectiveCellLines[i + 1];
                                if (BodyListMarkerRegex.IsMatch(nextL.FullText) || BulletPrefixRegex.IsMatch(nextL.FullText)) break;
                                if (Math.Abs(nextL.FontSizePt - line.FontSizePt) > 1.6) break;
                                bool prevAllBold = prevL.Words.All(w => w.IsBold);
                                bool nextAllBold = nextL.Words.All(w => w.IsBold);
                                if (prevAllBold != nextAllBold) break;
                                double vGap = prevL.Bottom - nextL.Top;
                                if (vGap < -3.0 || vGap > Math.Max(5.5, line.FontSizePt * 0.72)) break;
                                if (prevL.FullText.TrimEnd().EndsWith(':') && prevL.Right < colLeft + colWidth * 0.65) break;
                                bool sameLeft = Math.Abs(nextL.Left - line.Left) <= 14.0 && prevL.Right >= colLeft + colWidth * 0.55;
                                double prevLeftPad = prevL.Left - colLeft, prevRightPad = colRight - prevL.Right;
                                double nextLeftPad = nextL.Left - colLeft, nextRightPad = colRight - nextL.Right;
                                bool bothCentered = Math.Abs(prevLeftPad - prevRightPad) <= Math.Max(8.0, colWidth * 0.14)
                                    && Math.Abs(nextLeftPad - nextRightPad) <= Math.Max(8.0, colWidth * 0.14)
                                    && prevLeftPad >= 3.5 && nextLeftPad >= 3.5;
                                if (!sameLeft && !bothCentered) break;
                                paraLines.Add(nextL);
                                i++;
                            }
                        }

                        var lastLineInPara = paraLines[^1];
                        double lineHeight = paraLines.Count > 1
                            ? Math.Clamp((line.Top - lastLineInPara.Top) / (paraLines.Count - 1), line.FontSizePt * 1.05, line.FontSizePt * 1.45)
                            : (i + 1 < effectiveCellLines.Count ? Math.Min(line.FontSizePt * 1.35, line.Bottom - effectiveCellLines[i + 1].Bottom) : line.FontSizePt * 1.1);
                        double afterPt = paraLines.Count > 1 && i + 1 < effectiveCellLines.Count
                            ? Math.Max(0, lastLineInPara.Bottom - effectiveCellLines[i + 1].Top - 2.0)
                            : 0;

                        double minLeft = paraLines.Min(l => l.Left);
                        double maxRight = paraLines.Max(l => l.Right);
                        double leftPad = Math.Max(0, minLeft - colLeft);
                        double rightPad = Math.Max(0, colRight - maxRight);

                        // 100% Native Table Alignment (Both / Center / Right / Left) without faking alignment via Indentation.Left
                        var cellJc = W.JustificationValues.Left;
                        if ((paraLines.Count >= 2 || cellHasList || cellReachesRight)
                            && colWidth >= 85.0
                            && (rightPad <= Math.Max(18.0, colWidth * 0.12) || (cellHasList && cellReachesRight))
                            && leftPad <= Math.Max(28.0, colWidth * 0.18))
                        {
                            cellJc = W.JustificationValues.Both;
                        }
                        else if (!cellHasList && !lineIsListItem
                            && ((Math.Abs(leftPad - rightPad) <= Math.Max(8.5, colWidth * 0.15) && leftPad >= 3.0)
                                || (leftPad >= Math.Max(6.5, colWidth * 0.10) && rightPad >= 4.0 && (maxRight - minLeft) <= colWidth * 0.86)))
                        {
                            cellJc = W.JustificationValues.Center;
                        }
                        else if (!cellHasList && rightPad < 4.0 && leftPad >= Math.Max(18.0, colWidth * 0.30))
                        {
                            cellJc = W.JustificationValues.Right;
                        }

                        // Only preserve marker indentation on list cells so ApplyEditableNumbering can compute hanging indent; all other table cells use Left="0"
                        int leftIndDxa = cellHasList
                            ? (int)Math.Round(Math.Max(0, line.Left - colLeft - Math.Min(cellPadPt, 2.0)) * 20)
                            : 0;

                        var paragraph = new W.Paragraph(new W.ParagraphProperties(
                            new W.SpacingBetweenLines
                            {
                                Before = "0",
                                After = Math.Round(afterPt * 20).ToString(CultureInfo.InvariantCulture),
                                Line = Math.Round(Math.Max(line.FontSizePt, lineHeight) * 20).ToString(CultureInfo.InvariantCulture),
                                LineRule = W.LineSpacingRuleValues.Exact
                            },
                            new W.Indentation { Left = leftIndDxa.ToString(CultureInfo.InvariantCulture) },
                            new W.Justification { Val = cellJc }));
                        var combinedWords = paraLines.SelectMany(l => l.Words).ToList();
                        AppendConsolidatedRuns(paragraph, combinedWords, 0);
                        tc.Append(paragraph);
                    }

                    while (nextImgRowIdx < imageRows.Count)
                    {
                        EmitImageRow(imageRows[nextImgRowIdx++], ref docPrId, ref imagesCount);
                    }

                    // Word's default empty paragraph can exceed a short PDF row's minimum height.
                    if (!tc.Elements<W.Paragraph>().Any())
                    {
                        tc.Append(new W.Paragraph(new W.ParagraphProperties(
                            new W.SpacingBetweenLines { Before = "0", After = "0", Line = "1", LineRule = W.LineSpacingRuleValues.Exact })));
                    }
                    tr.Append(tc);
                }
                table.Append(tr);
            }
            return table;
        }

        private static SlideMasterPart CreateValidSlideMaster(PresentationPart presentationPart)
        {
            var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>("rIdMaster1");
            var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>("rIdLayout1");
            // PowerPoint requires the reverse layout relationship and a resolvable master theme.
            slideLayoutPart.AddPart(slideMasterPart);
            var themePart = slideMasterPart.AddNewPart<ThemePart>();
            themePart.Theme = new A.Theme(new A.ThemeElements(
                new A.ColorScheme(
                    new A.Dark1Color(new A.RgbColorModelHex { Val = "000000" }),
                    new A.Light1Color(new A.RgbColorModelHex { Val = "FFFFFF" }),
                    new A.Dark2Color(new A.RgbColorModelHex { Val = "222222" }),
                    new A.Light2Color(new A.RgbColorModelHex { Val = "EEEEEE" }),
                    new A.Accent1Color(new A.RgbColorModelHex { Val = "4472C4" }),
                    new A.Accent2Color(new A.RgbColorModelHex { Val = "ED7D31" }),
                    new A.Accent3Color(new A.RgbColorModelHex { Val = "A5A5A5" }),
                    new A.Accent4Color(new A.RgbColorModelHex { Val = "FFC000" }),
                    new A.Accent5Color(new A.RgbColorModelHex { Val = "5B9BD5" }),
                    new A.Accent6Color(new A.RgbColorModelHex { Val = "70AD47" }),
                    new A.Hyperlink(new A.RgbColorModelHex { Val = "0000FF" }),
                    new A.FollowedHyperlinkColor(new A.RgbColorModelHex { Val = "800080" })) { Name = "NEXUS" },
                new A.FontScheme(
                    new A.MajorFont(new A.LatinFont { Typeface = "Arial" }, new A.EastAsianFont { Typeface = "" }, new A.ComplexScriptFont { Typeface = "" }),
                    new A.MinorFont(new A.LatinFont { Typeface = "Arial" }, new A.EastAsianFont { Typeface = "" }, new A.ComplexScriptFont { Typeface = "" })) { Name = "NEXUS" },
                new A.FormatScheme(
                    new A.FillStyleList(Enumerable.Range(0, 3).Select(_ => new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }))),
                    new A.LineStyleList(Enumerable.Range(0, 3).Select(_ => new A.Outline(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })) { Width = 12700 })),
                    new A.EffectStyleList(Enumerable.Range(0, 3).Select(_ => new A.EffectStyle(new A.EffectList()))),
                    new A.BackgroundFillStyleList(Enumerable.Range(0, 3).Select(_ => new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })))) { Name = "NEXUS" })) { Name = "NEXUS" };
            themePart.Theme.Save();

            slideLayoutPart.SlideLayout = new P.SlideLayout(
                new P.CommonSlideData(
                    new P.ShapeTree(
                        new P.NonVisualGroupShapeProperties(
                            new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                            new P.NonVisualGroupShapeDrawingProperties(),
                            new P.ApplicationNonVisualDrawingProperties()),
                        new P.GroupShapeProperties(new A.TransformGroup()))),
                new P.ColorMapOverride(new A.MasterColorMapping()));
            slideLayoutPart.SlideLayout.Save();

            slideMasterPart.SlideMaster = new P.SlideMaster(
                new P.CommonSlideData(
                    new P.ShapeTree(
                        new P.NonVisualGroupShapeProperties(
                            new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                            new P.NonVisualGroupShapeDrawingProperties(),
                            new P.ApplicationNonVisualDrawingProperties()),
                        new P.GroupShapeProperties(new A.TransformGroup()))),
                new P.ColorMap
                {
                    Background1 = A.ColorSchemeIndexValues.Light1,
                    Text1 = A.ColorSchemeIndexValues.Dark1,
                    Background2 = A.ColorSchemeIndexValues.Light2,
                    Text2 = A.ColorSchemeIndexValues.Dark2,
                    Accent1 = A.ColorSchemeIndexValues.Accent1,
                    Accent2 = A.ColorSchemeIndexValues.Accent2,
                    Accent3 = A.ColorSchemeIndexValues.Accent3,
                    Accent4 = A.ColorSchemeIndexValues.Accent4,
                    Accent5 = A.ColorSchemeIndexValues.Accent5,
                    Accent6 = A.ColorSchemeIndexValues.Accent6,
                    Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
                    FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink
                },
                new P.SlideLayoutIdList(
                    new P.SlideLayoutId { Id = 2147483649U, RelationshipId = "rIdLayout1" }));
            slideMasterPart.SlideMaster.Save();

            return slideMasterPart;
        }

        // PERBAIKAN PENTING: Dekoding gambar PDF Digital & pembersihan latar belakang hitam akibat transparansi /SMask PDF
        private static byte[]? DecodePdfImageToPng(IPdfImage pdfImage, PdfDocument pdf, List<string>? warnings = null)
        {
            byte[]? rawResult = null;
            if (pdfImage.TryGetPng(out var png) && png != null && png.Length > 0)
            {
                rawResult = png;
            }
            else if (pdfImage.TryGetBytesAsMemory(out var mem) && !mem.IsEmpty)
            {
                rawResult = EncodeBytesWithSkia(mem.ToArray(), pdfImage.WidthInSamples, pdfImage.HeightInSamples, pdfImage.BitsPerComponent);
            }
            else
            {
                byte[] raw = pdfImage.RawBytes.ToArray();
                if (raw.Length > 0)
                {
                    rawResult = EncodeBytesWithSkia(raw, pdfImage.WidthInSamples, pdfImage.HeightInSamples, pdfImage.BitsPerComponent);
                }
            }

            if (rawResult == null || rawResult.Length == 0)
            {
                warnings?.Add($"Gagal mendekode objek gambar PDF berukuran {pdfImage.WidthInSamples}x{pdfImage.HeightInSamples}.");
                return null;
            }

            try
            {
                return ApplyPdfImageMask(rawResult, pdfImage, pdf);
            }
            catch (Exception ex)
            {
                warnings?.Add($"Transparansi gambar {pdfImage.WidthInSamples}x{pdfImage.HeightInSamples} tidak dapat diterapkan: {ex.Message}");
                return rawResult;
            }
        }

        private static byte[] ApplyPdfImageMask(byte[] pngBytes, IPdfImage pdfImage, PdfDocument pdf)
        {
            bool softMask = pdfImage.ImageDictionary.TryGet(NameToken.Smask, out IToken maskToken)
                && maskToken is not NameToken;
            if (!softMask && !pdfImage.ImageDictionary.TryGet(NameToken.Mask, out maskToken)) return pngBytes;
            if (maskToken is NameToken) return pngBytes;
            if (maskToken is IndirectReferenceToken reference) maskToken = pdf.Structure.GetObject(reference.Data).Data;
            if (!softMask && maskToken is ArrayToken colorKeyMask)
            {
                return ApplyColorKeyMaskArray(pngBytes, pdfImage, colorKeyMask);
            }
            if (maskToken is not StreamToken mask) throw new NotSupportedException("Jenis /Mask bukan stream gambar.");
            var dictionary = mask.StreamDictionary;
            if (!dictionary.TryGet<NumericToken>(NameToken.Width, out var width)
                || !dictionary.TryGet<NumericToken>(NameToken.Height, out var height)
                || width.Int <= 0 || height.Int <= 0) throw new InvalidDataException("Ukuran mask tidak valid.");
            int bits = dictionary.TryGet<NumericToken>(NameToken.BitsPerComponent, out var bpc) ? bpc.Int : 1;
            if (bits is not (1 or 2 or 4 or 8 or 16)) throw new NotSupportedException("Bit depth mask tidak didukung.");
            if (softMask)
            {
                bool isGrayMask = false;
                if (dictionary.TryGet(NameToken.ColorSpace, out var csToken))
                {
                    if (csToken is IndirectReferenceToken csRef) csToken = pdf.Structure.GetObject(csRef.Data).Data;
                    if (csToken is NameToken csName && (csName == NameToken.Devicegray || csName.Data.Equals("G", StringComparison.OrdinalIgnoreCase)))
                        isGrayMask = true;
                    else if (csToken is ArrayToken csArr && csArr.Data.FirstOrDefault() is NameToken arrName
                        && (arrName == NameToken.Devicegray || arrName.Data.Equals("CalGray", StringComparison.OrdinalIgnoreCase) || arrName.Data.Equals("ICCBased", StringComparison.OrdinalIgnoreCase)))
                        isGrayMask = true;
                }
                if (!isGrayMask) throw new NotSupportedException("Soft mask harus DeviceGray.");
            }
            var samples = mask.Decode(DefaultFilterProvider.Instance).Span;
            long stride = ((long)width.Int * bits + 7) / 8;
            if (stride * height.Int > samples.Length) throw new InvalidDataException("Data mask tidak lengkap.");
            double decodeMin = 0, decodeMax = 1;
            if (dictionary.TryGet<ArrayToken>(NameToken.Decode, out var decode))
            {
                if (decode.Length != 2 || decode[0] is not NumericToken d0 || decode[1] is not NumericToken d1)
                    throw new InvalidDataException("Decode mask tidak valid.");
                decodeMin = d0.Double;
                decodeMax = d1.Double;
            }
            double[]? matte = null;
            if (softMask && dictionary.TryGet<ArrayToken>(NameToken.Matte, out var matteToken))
            {
                matte = matteToken.Data.OfType<NumericToken>().Select(n => n.Double * 255).ToArray();
                if (!pdfImage.ImageDictionary.TryGet<NameToken>(NameToken.ColorSpace, out var sourceSpace)
                    || !((sourceSpace == NameToken.Devicergb && matte.Length == 3)
                        || (sourceSpace == NameToken.Devicegray && matte.Length == 1)))
                    throw new NotSupportedException("Matte hanya didukung untuk DeviceRGB/DeviceGray.");
            }
            using var decoded = SKBitmap.Decode(pngBytes);
            if (decoded == null) throw new InvalidDataException("Gambar dasar tidak dapat didekode.");
            using var bitmap = new SKBitmap(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            for (int y = 0; y < decoded.Height; y++)
            {
                int maskY = (int)((long)y * height.Int / decoded.Height);
                for (int x = 0; x < decoded.Width; x++)
                {
                    int maskX = (int)((long)x * width.Int / decoded.Width);
                    long bitOffset = (long)maskX * bits;
                    int index = checked((int)(maskY * stride + bitOffset / 8));
                    int sample = bits == 16 ? (samples[index] << 8) | samples[index + 1]
                        : (samples[index] >> (8 - bits - (int)(bitOffset % 8))) & ((1 << bits) - 1);
                    double opacity = Math.Clamp(decodeMin + (decodeMax - decodeMin) * sample / ((1 << bits) - 1), 0, 1);
                    if (!softMask) opacity = 1 - opacity;
                    var color = decoded.GetPixel(x, y);
                    byte Component(byte value, int component) => matte == null || opacity == 0 ? value
                        : (byte)Math.Clamp(Math.Round((value - (1 - opacity) * matte[matte.Length == 1 ? 0 : component]) / opacity), 0, 255);
                    // PDF /Matte stores preblended colors; undo that before assigning PNG alpha.
                    bitmap.SetPixel(x, y, new SKColor(Component(color.Red, 0), Component(color.Green, 1),
                        Component(color.Blue, 2), (byte)Math.Round(color.Alpha * opacity)));
                }
            }
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }

        private static byte[]? EncodeBytesWithSkia(byte[] bytes, int width, int height, int bitsPerComponent)
        {
            using var decoded = SKBitmap.Decode(bytes);
            if (decoded != null)
            {
                using var img = SKImage.FromBitmap(decoded);
                using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                return data.ToArray();
            }

            if (width > 0 && height > 0 && bitsPerComponent == 8)
            {
                int pixelCount = width * height;
                if (bytes.Length == pixelCount * 3)
                {
                    using var bmp = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
                    int idx = 0;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            bmp.SetPixel(x, y, new SKColor(bytes[idx], bytes[idx + 1], bytes[idx + 2]));
                            idx += 3;
                        }
                    }
                    using var img = SKImage.FromBitmap(bmp);
                    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                    return data.ToArray();
                }

                if (bytes.Length == pixelCount)
                {
                    using var bmp = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
                    int idx = 0;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            byte g = bytes[idx++];
                            bmp.SetPixel(x, y, new SKColor(g, g, g));
                        }
                    }
                    using var img = SKImage.FromBitmap(bmp);
                    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                    return data.ToArray();
                }
            }
            return null;
        }

        private static byte[] ApplyColorKeyMaskArray(byte[] pngBytes, IPdfImage pdfImage, ArrayToken colorKeyMask)
        {
            var numbers = colorKeyMask.Data.OfType<NumericToken>().Select(n => n.Int).ToArray();
            if (numbers.Length < 2 || numbers.Length % 2 != 0)
                throw new InvalidDataException("Array /Mask tidak valid.");

            int componentCount = numbers.Length / 2;
            int bits = Math.Max(1, pdfImage.BitsPerComponent);
            int maxSample = (1 << Math.Clamp(bits, 1, 16)) - 1;
            int ScaleToByte(int v) => maxSample == 255 ? Math.Clamp(v, 0, 255) : Math.Clamp((int)Math.Round(v * 255.0 / maxSample), 0, 255);

            var ranges = new (int Min, int Max)[componentCount];
            for (int c = 0; c < componentCount; c++)
            {
                int a = ScaleToByte(numbers[c * 2]);
                int b = ScaleToByte(numbers[c * 2 + 1]);
                ranges[c] = (Math.Min(a, b), Math.Max(a, b));
            }

            using var decoded = SKBitmap.Decode(pngBytes);
            if (decoded == null) throw new InvalidDataException("Gambar dasar tidak dapat didekode.");
            using var bitmap = new SKBitmap(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            for (int y = 0; y < decoded.Height; y++)
            {
                for (int x = 0; x < decoded.Width; x++)
                {
                    var px = decoded.GetPixel(x, y);
                    bool masked = false;
                    if (componentCount == 1)
                    {
                        int lum = (px.Red + px.Green + px.Blue) / 3;
                        masked = lum >= ranges[0].Min && lum <= ranges[0].Max;
                    }
                    else if (componentCount >= 3)
                    {
                        masked = px.Red >= ranges[0].Min && px.Red <= ranges[0].Max
                              && px.Green >= ranges[1].Min && px.Green <= ranges[1].Max
                              && px.Blue >= ranges[2].Min && px.Blue <= ranges[2].Max;
                    }
                    bitmap.SetPixel(x, y, masked ? new SKColor(px.Red, px.Green, px.Blue, 0) : px);
                }
            }
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }

        private static readonly Lazy<Dictionary<string, string>> InstalledFontFamilies = new(() =>
            SKFontManager.Default.FontFamilies.GroupBy(FontFamilyKey).ToDictionary(group => group.Key, group => group.First()));

        private static string FontFamilyKey(string name) => name.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();

        private static readonly Dictionary<string, (string CanonicalName, string AlternateFallback, string FamilyClass)> KnownFontCatalog = new(StringComparer.OrdinalIgnoreCase)
        {
            ["times"] = ("Times New Roman", "Times New Roman", "roman"),
            ["timesnewroman"] = ("Times New Roman", "Times New Roman", "roman"),
            ["timesnewromanps"] = ("Times New Roman", "Times New Roman", "roman"),
            ["timesnewromanpsmt"] = ("Times New Roman", "Times New Roman", "roman"),
            ["timesroman"] = ("Times New Roman", "Times New Roman", "roman"),
            ["nimbusromno9l"] = ("Times New Roman", "Times New Roman", "roman"),
            ["liberationsans"] = ("Liberation Sans", "Arial", "swiss"),
            ["liberationserif"] = ("Liberation Serif", "Times New Roman", "roman"),
            ["liberationmono"] = ("Liberation Mono", "Courier New", "modern"),
            ["courier"] = ("Courier New", "Courier New", "modern"),
            ["couriernew"] = ("Courier New", "Courier New", "modern"),
            ["couriernewps"] = ("Courier New", "Courier New", "modern"),
            ["couriernewpsmt"] = ("Courier New", "Courier New", "modern"),
            ["courierstd"] = ("Courier New", "Courier New", "modern"),
            ["helvetica"] = ("Arial", "Arial", "swiss"),
            ["helveticaneue"] = ("Helvetica Neue", "Arial", "swiss"),
            ["nimbussanl"] = ("Arial", "Arial", "swiss"),
            ["arial"] = ("Arial", "Arial", "swiss"),
            ["arialmt"] = ("Arial", "Arial", "swiss"),
            ["arialnarrow"] = ("Arial Narrow", "Arial", "swiss"),
            ["arialnarrowmt"] = ("Arial Narrow", "Arial", "swiss"),
            ["arialblack"] = ("Arial Black", "Arial", "swiss"),
            ["arialunicodems"] = ("Arial Unicode MS", "Arial", "swiss"),
            ["calibri"] = ("Calibri", "Arial", "swiss"),
            ["calibrilight"] = ("Calibri Light", "Calibri", "swiss"),
            ["carlito"] = ("Carlito", "Calibri", "swiss"),
            ["cambria"] = ("Cambria", "Times New Roman", "roman"),
            ["cambriamath"] = ("Cambria Math", "Cambria", "roman"),
            ["caladea"] = ("Caladea", "Cambria", "roman"),
            ["aptos"] = ("Aptos", "Calibri", "swiss"),
            ["aptosdisplay"] = ("Aptos Display", "Calibri", "swiss"),
            ["aptosnarrow"] = ("Aptos Narrow", "Arial Narrow", "swiss"),
            ["aptosmono"] = ("Aptos Mono", "Consolas", "modern"),
            ["segoeui"] = ("Segoe UI", "Calibri", "swiss"),
            ["segoeuilight"] = ("Segoe UI Light", "Segoe UI", "swiss"),
            ["segoeuibold"] = ("Segoe UI", "Calibri", "swiss"),
            ["segoeuibisemibold"] = ("Segoe UI Semibold", "Segoe UI", "swiss"),
            ["tahoma"] = ("Tahoma", "Arial", "swiss"),
            ["verdana"] = ("Verdana", "Arial", "swiss"),
            ["trebuchetms"] = ("Trebuchet MS", "Arial", "swiss"),
            ["georgia"] = ("Georgia", "Times New Roman", "roman"),
            ["baskervilleoldface"] = ("Baskerville Old Face", "Georgia", "roman"),
            ["monotypecorsiva"] = ("Monotype Corsiva", "Gabriola", "script"),
            ["garamond"] = ("Garamond", "Times New Roman", "roman"),
            ["palatinolinotype"] = ("Palatino Linotype", "Times New Roman", "roman"),
            ["bookantiqua"] = ("Book Antiqua", "Times New Roman", "roman"),
            ["bookmanoldstyle"] = ("Bookman Old Style", "Georgia", "roman"),
            ["bookman"] = ("Bookman Old Style", "Georgia", "roman"),
            ["centurygothic"] = ("Century Gothic", "Arial", "swiss"),
            ["franklingothicmedium"] = ("Franklin Gothic Medium", "Arial", "swiss"),
            ["consolas"] = ("Consolas", "Courier New", "modern"),
            ["lucidaconsole"] = ("Lucida Console", "Courier New", "modern"),
            ["candara"] = ("Candara", "Calibri", "swiss"),
            ["corbel"] = ("Corbel", "Calibri", "swiss"),
            ["constantia"] = ("Constantia", "Times New Roman", "roman"),
            ["comicsansms"] = ("Comic Sans MS", "Arial", "script"),
            ["impact"] = ("Impact", "Arial Black", "swiss"),
            ["roboto"] = ("Roboto", "Arial", "swiss"),
            ["opensans"] = ("Open Sans", "Segoe UI", "swiss"),
            ["lato"] = ("Lato", "Calibri", "swiss"),
            ["montserrat"] = ("Montserrat", "Century Gothic", "swiss"),
            ["poppins"] = ("Poppins", "Segoe UI", "swiss"),
            ["inter"] = ("Inter", "Segoe UI", "swiss"),
            ["notosans"] = ("Noto Sans", "Arial", "swiss"),
            ["notoserif"] = ("Noto Serif", "Times New Roman", "roman"),
            ["dejavusans"] = ("DejaVu Sans", "Arial", "swiss"),
            ["dejavuserif"] = ("DejaVu Serif", "Times New Roman", "roman"),
            ["dejavusansmono"] = ("DejaVu Sans Mono", "Courier New", "modern"),
            ["myriadpro"] = ("Myriad Pro", "Segoe UI", "swiss"),
            ["minionpro"] = ("Minion Pro", "Times New Roman", "roman")
        };

        private static string CleanFontName(string? rawFont, bool isOcrPage = false)
        {
            if (string.IsNullOrWhiteSpace(rawFont))
                return isOcrPage ? "Arial" : "Calibri";

            string clean = Regex.Replace(rawFont, @"^[A-Z]{6}\+", "");
            if (FontFamilyKey(clean) == "arialblack") return "Arial Black";
            if (InstalledFontFamilies.Value.TryGetValue(FontFamilyKey(clean), out var installedFamily)) return installedFamily;
            clean = Regex.Replace(clean, @"[-,](?:Regular|Roman|Bold(?:Italic|Oblique)?|Italic|Oblique|Light|Medium|SemiBold|DemiBold|Black)(?:-?MT)?$", "", RegexOptions.IgnoreCase);
            if (InstalledFontFamilies.Value.TryGetValue(FontFamilyKey(clean), out installedFamily)) return installedFamily;
            string familyCandidate = Regex.Replace(clean, @"(?:Bold(?:Italic|Oblique)?|Italic|Oblique|Regular)$", "", RegexOptions.IgnoreCase);
            if (familyCandidate != clean && InstalledFontFamilies.Value.TryGetValue(FontFamilyKey(familyCandidate), out installedFamily)) return installedFamily;

            if (clean.Contains("GlyphLess", StringComparison.OrdinalIgnoreCase)
                || clean.Contains("CIDFont", StringComparison.OrdinalIgnoreCase)
                || clean.Contains("Identity", StringComparison.OrdinalIgnoreCase))
            {
                return "Arial";
            }

            string key = FontFamilyKey(familyCandidate != clean && !string.IsNullOrWhiteSpace(familyCandidate) ? familyCandidate : clean);
            if (KnownFontCatalog.TryGetValue(key, out var entry))
            {
                if (InstalledFontFamilies.Value.TryGetValue(FontFamilyKey(entry.CanonicalName), out var canonicalInstalled))
                    return canonicalInstalled;
                return entry.CanonicalName;
            }

            return string.IsNullOrWhiteSpace(clean) ? "Calibri" : clean;
        }

        private static void EnsureWordFontTable(MainDocumentPart mainPart)
        {
            var usedFonts = mainPart.Document.Descendants<W.RunFonts>()
                .Select(rf => rf.Ascii?.Value)
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (usedFonts.Count == 0) return;

            var fontTablePart = mainPart.FontTablePart ?? mainPart.AddNewPart<FontTablePart>();
            var fonts = new W.Fonts();
            foreach (string fontName in usedFonts)
            {
                string key = FontFamilyKey(fontName);
                string altName = "Calibri";
                var familyVal = W.FontFamilyValues.Swiss;
                var pitchVal = W.FontPitchValues.Variable;

                if (KnownFontCatalog.TryGetValue(key, out var catalogInfo))
                {
                    altName = catalogInfo.AlternateFallback;
                    familyVal = catalogInfo.FamilyClass switch
                    {
                        "roman" => W.FontFamilyValues.Roman,
                        "modern" => W.FontFamilyValues.Modern,
                        "script" => W.FontFamilyValues.Script,
                        _ => W.FontFamilyValues.Swiss
                    };
                    if (catalogInfo.FamilyClass == "modern") pitchVal = W.FontPitchValues.Fixed;
                }
                else if (fontName.Contains("Serif", StringComparison.OrdinalIgnoreCase) || fontName.Contains("Times", StringComparison.OrdinalIgnoreCase))
                {
                    altName = "Times New Roman";
                    familyVal = W.FontFamilyValues.Roman;
                }
                else if (fontName.Contains("Mono", StringComparison.OrdinalIgnoreCase) || fontName.Contains("Courier", StringComparison.OrdinalIgnoreCase))
                {
                    altName = "Courier New";
                    familyVal = W.FontFamilyValues.Modern;
                    pitchVal = W.FontPitchValues.Fixed;
                }

                var fontElem = new W.Font { Name = fontName };
                if (!string.Equals(fontName, altName, StringComparison.OrdinalIgnoreCase))
                {
                    fontElem.Append(new W.AltName { Val = altName });
                }
                fontElem.Append(new W.FontCharSet { Val = "00" });
                fontElem.Append(new W.FontFamily { Val = familyVal });
                fontElem.Append(new W.Pitch { Val = pitchVal });
                fonts.Append(fontElem);
            }
            fontTablePart.Fonts = fonts;
            fontTablePart.Fonts.Save();
        }

        private static void EnsureModernWordSettings(MainDocumentPart mainPart)
        {
            var settingsPart = mainPart.DocumentSettingsPart ?? mainPart.AddNewPart<DocumentSettingsPart>();
            var settings = settingsPart.Settings ?? new W.Settings();
            var compat = settings.GetFirstChild<W.Compatibility>() ?? settings.AppendChild(new W.Compatibility());
            compat.RemoveAllChildren<W.CompatibilitySetting>();
            compat.Append(new W.CompatibilitySetting
            {
                Name = W.CompatSettingNameValues.CompatibilityMode,
                Uri = "http://schemas.microsoft.com/office/word",
                Val = "15"
            });
            settingsPart.Settings = settings;
            settingsPart.Settings.Save();
        }

        // =====================================================================
        // UNIFIED OCR PIPELINE: OneOCR + PP-OCRv6 ONNX + Curve-Text Refinement
        // =====================================================================
        private static bool TryExtractInlineBackgroundPill(
            ClippedVectorPathItem item,
            double pageWidthPt,
            double pageHeightPt,
            out string fillHex)
        {
            fillHex = string.Empty;
            var path = item.Path;
            var b = item.EffectiveBounds;
            if (!path.IsFilled || path.IsStroked || path.FillColor == null) return false;
            if (path.Count > 2) return false;
            if (b.Height < 7.5 || b.Height > 22.5 || b.Width < 5.5 || b.Width > pageWidthPt * 0.72) return false;
            if (IsPureWhiteCanvasBackdrop(path, b, pageWidthPt, pageHeightPt)) return false;

            if (!TryGetColorRgb(path.FillColor, out var rgbVal)) return false;
            var (r, g, bl) = rgbVal;
            int rb = Math.Clamp((int)Math.Round(r * 255.0), 0, 255);
            int gb = Math.Clamp((int)Math.Round(g * 255.0), 0, 255);
            int bb = Math.Clamp((int)Math.Round(bl * 255.0), 0, 255);
            if (rb >= 250 && gb >= 250 && bb >= 250) return false;
            int avgLum = (rb + gb + bb) / 3;
            if (avgLum < 145) return false;

            fillHex = $"{rb:X2}{gb:X2}{bb:X2}";
            return true;
        }

        private static bool IsVisibleInkGlyphPath(ClippedVectorPathItem item, double pageWidthPt, double pageHeightPt)
        {
            var path = item.Path;
            var b = item.EffectiveBounds;
            if (!path.IsFilled || path.IsStroked) return false;
            if (b.Width < 0.35 || b.Width > 42.0 || b.Height < 0.25 || b.Height > 36.0) return false;
            if (IsPureWhiteCanvasBackdrop(path, b, pageWidthPt, pageHeightPt)) return false;
            if (TryExtractInlineBackgroundPill(item, pageWidthPt, pageHeightPt, out _)) return false;
            if (!TryGetColorRgb(path.FillColor, out var rgbVal)) return false;
            var (r, g, bl) = rgbVal;
            double lum = (r + g + bl) / 3.0;
            return lum < 0.82;
        }

        private static List<RichWord>? TryRecognizeCurveTextPageWords(
            Page page,
            int pageNo,
            NexusConversionOptions options,
            List<string> warnings,
            HashSet<UglyToad.PdfPig.Graphics.PdfPath> consumedGlyphPaths)
        {
            var clippedPaths = GetClippedPagePaths(page, null);
            var inlineHighlightPills = new List<(ClippedVectorPathItem Item, string FillHex)>();
            foreach (var cp in clippedPaths)
            {
                if (TryExtractInlineBackgroundPill(cp, page.Width, page.Height, out string pHex))
                {
                    inlineHighlightPills.Add((cp, pHex));
                }
            }
            var glyphCandidates = clippedPaths.Where(item => IsVisibleInkGlyphPath(item, page.Width, page.Height)).ToList();

            if (glyphCandidates.Count < 8) return null;

            using var glyphBitmap = RenderClippedVectorPathsToBitmap(glyphCandidates, page.Width, page.Height, dpi: 288, SKColors.White);
            if (glyphBitmap == null) return null;

            var rawOcr = RecognizePageWordsRaw(glyphBitmap, page.Width, page.Height, pageNo, options, warnings);
            if (rawOcr == null || rawOcr.Count == 0) return null;

            foreach (var pill in inlineHighlightPills)
            {
                consumedGlyphPaths.Add(pill.Item.Path);
            }

            // Group raw OCR words into visual lines using OneOCR LineIndex or vertical center overlap
            var lineClusters = new List<List<OcrRecognizedWord>>();
            foreach (var ow in rawOcr.Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .OrderByDescending(w => (w.BottomPt + w.TopPt) * 0.5)
                .ThenBy(w => w.LeftPt))
            {
                double owMidY = (ow.BottomPt + ow.TopPt) * 0.5;
                double owHeight = Math.Max(4.0, ow.TopPt - ow.BottomPt);
                List<OcrRecognizedWord>? targetCluster = null;

                // Find best matching line cluster that vertically overlaps without horizontal word collision
                foreach (var cluster in lineClusters)
                {
                    double cBottom = cluster.Min(c => c.BottomPt);
                    double cTop = cluster.Max(c => c.TopPt);
                    double cMidY = cluster.Average(c => (c.BottomPt + c.TopPt) * 0.5);
                    double cAvgH = cluster.Average(c => Math.Max(4.0, c.TopPt - c.BottomPt));
                    double vOverlap = Math.Min(ow.TopPt, cTop) - Math.Max(ow.BottomPt, cBottom);
                    bool sameOcrLine = ow.LineIndex >= 0 && cluster.Any(c => c.LineIndex == ow.LineIndex);
                    bool verticallyAligned = sameOcrLine
                        || Math.Abs(owMidY - cMidY) <= Math.Max(4.5, Math.Min(owHeight, cAvgH) * 0.55)
                        || vOverlap >= Math.Min(owHeight, cAvgH) * 0.35;
                    if (!verticallyAligned) continue;

                    bool horizontalCollision = !sameOcrLine && cluster.Any(c =>
                    {
                        double hOverlap = Math.Min(ow.RightPt, c.RightPt) - Math.Max(ow.LeftPt, c.LeftPt);
                        double minW = Math.Max(1.0, Math.Min(ow.RightPt - ow.LeftPt, c.RightPt - c.LeftPt));
                        return hOverlap > minW * 0.55;
                    });
                    if (!horizontalCollision)
                    {
                        targetCluster = cluster;
                        break;
                    }
                }

                if (targetCluster == null)
                {
                    targetCluster = new List<OcrRecognizedWord>();
                    lineClusters.Add(targetCluster);
                }
                targetCluster.Add(ow);
            }

            var refinedWords = new List<RichWord>();
            foreach (var cluster in lineClusters.OrderByDescending(c => c.Average(w => (w.BottomPt + w.TopPt) * 0.5)))
            {
                var orderedCluster = cluster.OrderBy(w => w.LeftPt).ToList();
                var lineGlyphHeights = new List<double>();
                var lineBaselines = new List<double>();
                var lineTops = new List<double>();

                var wordMatches = new List<(OcrRecognizedWord Word, List<ClippedVectorPathItem> Glyphs)>(orderedCluster.Count);
                foreach (var ow in orderedCluster)
                {
                    var matching = glyphCandidates.Where(g =>
                    {
                        var gb = g.EffectiveBounds;
                        double midX = (gb.Left + gb.Right) * 0.5;
                        double midY = (gb.Bottom + gb.Top) * 0.5;
                        return midX >= ow.LeftPt - 2.5 && midX <= ow.RightPt + 2.5
                            && midY >= ow.BottomPt - 3.0 && midY <= ow.TopPt + 3.0;
                    }).ToList();

                    wordMatches.Add((ow, matching));
                    foreach (var mg in matching)
                    {
                        consumedGlyphPaths.Add(mg.Path);
                        if (mg.EffectiveBounds.Height >= 3.2)
                        {
                            lineGlyphHeights.Add(mg.EffectiveBounds.Height);
                        }
                    }

                    bool hasDescender = ow.Text.IndexOfAny(new[] { 'g', 'j', 'p', 'q', 'y', '(', ')', '[', ']', '{', '}', ',', ';', '_' }) >= 0;
                    if (matching.Count > 0)
                    {
                        double wBot = matching.Min(g => g.EffectiveBounds.Bottom);
                        double wTop = matching.Max(g => g.EffectiveBounds.Top);
                        if (!hasDescender && ow.Text.Any(char.IsLetterOrDigit))
                        {
                            lineBaselines.Add(wBot);
                        }
                        if (ow.Text.Any(c => char.IsUpper(c) || char.IsDigit(c) || "bdfhklt".IndexOf(c) >= 0))
                        {
                            lineTops.Add(wTop);
                        }
                    }
                }

                double uniformBaseline = lineBaselines.Count > 0
                    ? lineBaselines.OrderBy(x => x).ElementAt(lineBaselines.Count / 2)
                    : orderedCluster.Min(w => w.BottomPt);
                double uniformTop = lineTops.Count > 0
                    ? lineTops.OrderBy(x => x).ElementAt(lineTops.Count / 2)
                    : orderedCluster.Max(w => w.TopPt);
                if (uniformTop <= uniformBaseline + 3.0)
                {
                    uniformTop = uniformBaseline + Math.Max(8.0, orderedCluster.Average(w => w.TopPt - w.BottomPt));
                }

                // Calibrate uniform line font size from vector cap-height (65th percentile of glyph heights / 0.75) so 10pt text is never inflated to 11pt
                double calibratedLinePt;
                if (lineGlyphHeights.Count > 0)
                {
                    lineGlyphHeights.Sort();
                    double p65 = lineGlyphHeights[(int)Math.Min(lineGlyphHeights.Count - 1, Math.Floor(lineGlyphHeights.Count * 0.65))];
                    calibratedLinePt = Math.Clamp(Math.Round((p65 / 0.75) * 2.0) / 2.0, 6.5, 36.0);
                }
                else
                {
                    calibratedLinePt = EstimateWordPointSizeGeneral("Cap", uniformTop - uniformBaseline, uniformTop - uniformBaseline, isOcrPage: true);
                }

                var clusterWords = new List<RichWord>(wordMatches.Count + 4);
                for (int wIdx = 0; wIdx < wordMatches.Count; wIdx++)
                {
                    var (ow, matching) = wordMatches[wIdx];
                    string cleaned = CleanOcrTokenGeneral(ow.Text, uniformBaseline, ow.RightPt, page.Width, pageNo, pageNo, warnings);
                    if (string.IsNullOrWhiteSpace(cleaned)) continue;



                    double exactLeft = matching.Count > 0 ? matching.Min(g => g.EffectiveBounds.Left) : Math.Clamp(ow.LeftPt, 0, page.Width);
                    double exactRight = matching.Count > 0 ? matching.Max(g => g.EffectiveBounds.Right) : Math.Clamp(ow.RightPt, exactLeft + 1.0, page.Width);

                    // Normalize floating round/square bullet glyph at index 0 recognized as "." or "·" into "•"
                    if (wIdx == 0 && (cleaned == "." || cleaned == "·" || cleaned == "-") && matching.Count == 1)
                    {
                        var mb = matching[0].EffectiveBounds;
                        if (mb.Width <= 6.5 && mb.Height <= 6.5 && mb.Bottom >= uniformBaseline + 1.5)
                        {
                            cleaned = "\u2022";
                        }
                    }

                    string dominantColor = matching
                        .Where(g => g.Path.FillColor != null)
                        .Select(g => SafeToRgbValues(g.Path.FillColor))
                        .GroupBy(c => $"{(int)Math.Round(c.R * 255):X2}{(int)Math.Round(c.G * 255):X2}{(int)Math.Round(c.B * 255):X2}")
                        .OrderByDescending(grp => grp.Count())
                        .FirstOrDefault()?.Key ?? "000000";

                    var wordBox = new PdfRectangle(exactLeft, uniformBaseline, Math.Max(exactLeft + 1.0, exactRight), uniformTop);
                    bool isBold = IsWordBoldInBitmap(wordBox, glyphBitmap, page.Width, page.Height);

                    double wordMidX = (exactLeft + exactRight) * 0.5;
                    double wordMidY = (uniformBaseline + uniformTop) * 0.5;
                    var matchedPill = inlineHighlightPills.LastOrDefault(pill =>
                        wordMidX >= pill.Item.EffectiveBounds.Left - 2.0 && wordMidX <= pill.Item.EffectiveBounds.Right + 2.0
                        && wordMidY >= pill.Item.EffectiveBounds.Bottom - 2.0 && wordMidY <= pill.Item.EffectiveBounds.Top + 2.0);
                    string? pillHex = matchedPill.FillHex;
                    bool isHighlighted = !string.IsNullOrEmpty(pillHex);

                    clusterWords.Add(new RichWord(
                        cleaned,
                        exactLeft,
                        Math.Max(exactLeft + 1.0, exactRight),
                        uniformBaseline,
                        uniformTop,
                        calibratedLinePt,
                        isHighlighted ? "Consolas" : "Segoe UI",
                        dominantColor,
                        isBold,
                        false,
                        uniformBaseline,
                        false,
                        false,
                        isHighlighted)
                    {
                        BackgroundFillHex = pillHex
                    });
                }

                // Recover thin horizontal minus-bar glyphs (width 3.2..11.0pt, height 0.25..2.2pt) between adjacent words on this line that OCR skipped
                if (clusterWords.Count >= 2)
                {
                    var withMinus = new List<RichWord>(clusterWords.Count + 4) { clusterWords[0] };
                    for (int k = 0; k < clusterWords.Count - 1; k++)
                    {
                        var leftW = clusterWords[k];
                        var rightW = clusterWords[k + 1];
                        double gapX = rightW.Left - leftW.Right;
                        if (gapX >= 2.0 && gapX <= 22.0
                            && !leftW.Text.EndsWith('-') && !leftW.Text.EndsWith('−')
                            && !rightW.Text.StartsWith('-') && !rightW.Text.StartsWith('−'))
                        {
                            var minusGlyph = glyphCandidates.FirstOrDefault(g =>
                            {
                                var gb = g.EffectiveBounds;
                                return gb.Width >= 3.2 && gb.Width <= 11.0
                                    && gb.Height >= 0.25 && gb.Height <= 2.2
                                    && gb.Left >= leftW.Right - 1.5 && gb.Right <= rightW.Left + 1.5
                                    && gb.Bottom >= uniformBaseline + (uniformTop - uniformBaseline) * 0.15
                                    && gb.Top <= uniformTop - (uniformTop - uniformBaseline) * 0.10;
                            });
                            if (minusGlyph != null)
                            {
                                consumedGlyphPaths.Add(minusGlyph.Path);
                                var mb = minusGlyph.EffectiveBounds;
                                withMinus.Add(new RichWord(
                                    "-",
                                    mb.Left,
                                    mb.Right,
                                    uniformBaseline,
                                    uniformTop,
                                    calibratedLinePt,
                                    leftW.FontName,
                                    leftW.HexColor,
                                    leftW.IsBold,
                                    false,
                                    uniformBaseline));
                            }
                        }
                        withMinus.Add(rightW);
                    }
                    clusterWords = withMinus;
                }

                refinedWords.AddRange(clusterWords);
            }

            if (refinedWords.Count >= 3)
            {
                // Consume remaining tiny diacritics, math dots, and punctuation belonging to recognized text lines
                foreach (var gc in glyphCandidates)
                {
                    var gb = gc.EffectiveBounds;
                    if (refinedWords.Any(w => gb.Bottom >= w.Bottom - 5.0 && gb.Top <= w.Top + 5.0
                        && gb.Left >= w.Left - 10.0 && gb.Right <= w.Right + 10.0))
                    {
                        consumedGlyphPaths.Add(gc.Path);
                    }
                }
            }
            return refinedWords;
        }

        private static bool IsWordBoldInBitmap(PdfRectangle boxPt, SKBitmap pageBitmap, double pageWidthPt, double pageHeightPt)
        {
            if (pageWidthPt <= 0 || pageHeightPt <= 0) return false;
            double scaleX = pageBitmap.Width / pageWidthPt;
            double scaleY = pageBitmap.Height / pageHeightPt;
            int x1 = Math.Clamp((int)(boxPt.Left * scaleX), 0, pageBitmap.Width - 1);
            int x2 = Math.Clamp((int)(boxPt.Right * scaleX), x1 + 1, pageBitmap.Width);
            int y1 = Math.Clamp((int)((pageHeightPt - boxPt.Top) * scaleY), 0, pageBitmap.Height - 1);
            int y2 = Math.Clamp((int)((pageHeightPt - boxPt.Bottom) * scaleY), y1 + 1, pageBitmap.Height);
            int boxH = Math.Max(1, y2 - y1);
            int deepInk = 0, totalInk = 0;
            for (int y = y1; y < y2; y++)
            {
                for (int x = x1; x < x2; x++)
                {
                    var c = pageBitmap.GetPixel(x, y);
                    int lum = (c.Red + c.Green + c.Blue) / 3;
                    if (lum < 210)
                    {
                        totalInk++;
                        if (lum < 90) deepInk++;
                    }
                }
            }
            if (totalInk < 8) return false;
            int area = Math.Max(1, (x2 - x1) * boxH);
            if ((double)deepInk / totalInk < 0.55 || (double)deepInk / area < 0.22)
                return false;

            // Measure horizontal stem thickness across the middle band (25%..75% height) so compact regular words ("ini", "di", "dan") are not misclassified as bold
            int midY1 = y1 + boxH / 4;
            int midY2 = Math.Max(midY1 + 1, y2 - boxH / 4);
            int maxStemPx = Math.Max(3, (int)Math.Round(boxH * 0.65));
            int stemRunSum = 0, stemRunCount = 0;
            for (int y = midY1; y < midY2; y++)
            {
                int run = 0;
                for (int x = x1; x < x2; x++)
                {
                    var c = pageBitmap.GetPixel(x, y);
                    int lum = (c.Red + c.Green + c.Blue) / 3;
                    if (lum < 145)
                    {
                        run++;
                    }
                    else if (run > 0)
                    {
                        if (run <= maxStemPx) { stemRunSum += run; stemRunCount++; }
                        run = 0;
                    }
                }
                if (run > 0 && run <= maxStemPx) { stemRunSum += run; stemRunCount++; }
            }
            if (stemRunCount == 0) return false;
            double avgStemRatio = ((double)stemRunSum / stemRunCount) / boxH;
            return avgStemRatio >= 0.138;
        }

        private static IReadOnlyList<OcrRecognizedWord>? RecognizePageWordsRaw(
            SKBitmap pageBitmap,
            double pageWidthPt,
            double pageHeightPt,
            int pageNo,
            NexusConversionOptions options,
            List<string> warnings)
        {
            IReadOnlyList<OcrRecognizedWord>? rawOcrWords = null;
            if (options.OcrBackend != null)
            {
                try
                {
                    rawOcrWords = options.OcrBackend.RecognizePage(pageBitmap, pageWidthPt, pageHeightPt, pageNo);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Halaman {pageNo}: OcrBackend gagal dijalankan ({ex.Message}).");
                }
            }
            if ((rawOcrWords == null || rawOcrWords.Count == 0) && options.CustomOcrProvider != null)
            {
                try
                {
                    rawOcrWords = options.CustomOcrProvider(pageBitmap, pageWidthPt, pageHeightPt, pageNo);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Halaman {pageNo}: CustomOcrProvider gagal dijalankan ({ex.Message}).");
                }
            }
            if ((rawOcrWords == null || rawOcrWords.Count == 0) && (options.EnableScannedPageOcr || options.EnableCurveTextOcr))
            {
                rawOcrWords = TryRunNativeOneOcr(pageBitmap, pageWidthPt, pageHeightPt, options);
                if (rawOcrWords == null || rawOcrWords.Count == 0)
                {
                    rawOcrWords = TryRunPpOcrV6Bridge(pageBitmap, pageWidthPt, pageHeightPt, options);
                }
                if (rawOcrWords == null || rawOcrWords.Count == 0)
                {
                    rawOcrWords = TryRunTesseractCliOcr(pageBitmap, pageWidthPt, pageHeightPt, options);
                }
            }
            return rawOcrWords;
        }

        private static string InferScannedPageFontFamily(
            SKBitmap pageBitmap,
            IReadOnlyList<OcrRecognizedWord> rawOcrWords,
            double pageWidthPt,
            double pageHeightPt)
        {
            if (pageBitmap.Width < 60 || pageBitmap.Height < 60 || rawOcrWords.Count == 0)
                return "Arial";

            double scaleX = pageBitmap.Width / pageWidthPt;
            double scaleY = pageBitmap.Height / pageHeightPt;

            var serifFlareSamples = new List<double>();
            var strokeContrastSamples = new List<double>();
            var charAspectSamples = new List<double>();
            var lowerHeights = new List<double>();
            var upperHeights = new List<double>();
            var narrowCharWidths = new List<double>();
            var wideCharWidths = new List<double>();

            foreach (var ow in rawOcrWords)
            {
                string txt = ow.Text.Trim();
                if (txt.Length < 3) continue;
                double wPt = ow.RightPt - ow.LeftPt;
                double hPt = ow.TopPt - ow.BottomPt;
                if (wPt < 6.0 || hPt < 5.5 || hPt > 24.0) continue;

                int letterCount = txt.Count(char.IsLetter);
                if (letterCount < 3) continue;

                bool allLowerNoDesc = txt.All(c => char.IsLower(c) && "gjpqybdfhklt".IndexOf(c) < 0);
                bool allUpper = txt.All(c => char.IsUpper(c));
                if (allLowerNoDesc) lowerHeights.Add(hPt);
                else if (allUpper && txt.Length >= 4) upperHeights.Add(hPt);

                if (txt.Count(char.IsLower) >= txt.Length - 1)
                {
                    charAspectSamples.Add((wPt / txt.Length) / hPt);
                }
                if (txt.Any(c => "il1I".IndexOf(c) >= 0) && !txt.Any(c => "mwMW".IndexOf(c) >= 0))
                    narrowCharWidths.Add(wPt / txt.Length);
                else if (txt.Any(c => "mwMW".IndexOf(c) >= 0) && !txt.Any(c => "il1I".IndexOf(c) >= 0))
                    wideCharWidths.Add(wPt / txt.Length);

                int x1 = Math.Clamp((int)Math.Round(ow.LeftPt * scaleX), 0, pageBitmap.Width - 1);
                int x2 = Math.Clamp((int)Math.Round(ow.RightPt * scaleX), x1 + 1, pageBitmap.Width);
                int y1 = Math.Clamp((int)Math.Round((pageHeightPt - ow.TopPt) * scaleY), 0, pageBitmap.Height - 1);
                int y2 = Math.Clamp((int)Math.Round((pageHeightPt - ow.BottomPt) * scaleY), y1 + 1, pageBitmap.Height);
                int boxH = y2 - y1, boxW = x2 - x1;
                if (boxH < 10 || boxW < 12) continue;

                // 1. Row ink profile for Serif Foot Flare (baseline/cap serif projections vs mid-stem)
                var rowInk = new int[boxH];
                var hRuns = new List<int>();
                for (int r = 0; r < boxH; r++)
                {
                    int run = 0;
                    for (int x = x1; x < x2; x++)
                    {
                        var c = pageBitmap.GetPixel(x, y1 + r);
                        if ((c.Red + c.Green + c.Blue) / 3 < 185)
                        {
                            rowInk[r]++;
                            run++;
                        }
                        else if (run > 0)
                        {
                            if (r >= boxH / 3 && r <= (boxH * 2) / 3 && run <= 10) hRuns.Add(run);
                            run = 0;
                        }
                    }
                    if (run > 0 && r >= boxH / 3 && r <= (boxH * 2) / 3 && run <= 10) hRuns.Add(run);
                }

                int midStart = Math.Max(1, (int)(boxH * 0.35));
                int midEnd = Math.Max(midStart + 1, (int)(boxH * 0.65));
                double midAvg = 0;
                for (int r = midStart; r < midEnd; r++) midAvg += rowInk[r];
                midAvg /= (midEnd - midStart);

                int botPeak = 0, topPeak = 0;
                for (int r = (int)(boxH * 0.65); r < boxH; r++) if (rowInk[r] > botPeak) botPeak = rowInk[r];
                for (int r = 0; r < Math.Max(1, (int)(boxH * 0.35)); r++) if (rowInk[r] > topPeak) topPeak = rowInk[r];
                if (midAvg >= 3.0)
                {
                    serifFlareSamples.Add(Math.Max(botPeak, topPeak) / midAvg);
                }

                // 2. Vertical run lengths (horizontal bar/curve thickness) vs horizontal stem thickness
                var vRuns = new List<int>();
                for (int x = x1; x < x2; x += 2)
                {
                    int run = 0;
                    for (int y = y1; y < y2; y++)
                    {
                        var c = pageBitmap.GetPixel(x, y);
                        if ((c.Red + c.Green + c.Blue) / 3 < 185) run++;
                        else if (run > 0)
                        {
                            if (run <= 8) vRuns.Add(run);
                            run = 0;
                        }
                    }
                    if (run > 0 && run <= 8) vRuns.Add(run);
                }
                if (hRuns.Count >= 3 && vRuns.Count >= 3)
                {
                    strokeContrastSamples.Add(vRuns.Average() / hRuns.Average());
                }
            }

            if (serifFlareSamples.Count == 0) return "Arial";
            serifFlareSamples.Sort();
            double medSerifFlare = serifFlareSamples[serifFlareSamples.Count / 2];

            strokeContrastSamples.Sort();
            double medStrokeContrast = strokeContrastSamples.Count > 0
                ? strokeContrastSamples[strokeContrastSamples.Count / 2]
                : 0.70;

            charAspectSamples.Sort();
            double medCharAspect = charAspectSamples.Count > 0
                ? charAspectSamples[charAspectSamples.Count / 2]
                : 0.54;

            double xHeightRatio = 0.68;
            if (lowerHeights.Count >= 3 && upperHeights.Count >= 3)
            {
                lowerHeights.Sort();
                upperHeights.Sort();
                xHeightRatio = lowerHeights[lowerHeights.Count / 2] / Math.Max(1.0, upperHeights[upperHeights.Count / 2]);
            }

            // Ciri 5: Monospace check
            if (narrowCharWidths.Count >= 3 && wideCharWidths.Count >= 3)
            {
                double narrowAvg = narrowCharWidths.Average();
                double wideAvg = wideCharWidths.Average();
                if (wideAvg > 0 && narrowAvg / wideAvg >= 0.84)
                {
                    return medSerifFlare >= 1.18 ? "Courier New" : "Consolas";
                }
            }

            // Ciri 1..4: Serif vs Sans-Serif & Bookman Old Style vs Times New Roman
            if (medSerifFlare >= 1.15)
            {
                // Bookman Old Style: sturdy low-contrast Old-Style Serif with wide bowls / tall x-height (calibrated for both 200 DPI JPEG and 400-600 DPI TIFF scans)
                if ((medStrokeContrast >= 0.45 && (medCharAspect >= 0.515 || xHeightRatio >= 0.68))
                    || (medStrokeContrast >= 0.52 && medCharAspect >= 0.49))
                {
                    return "Bookman Old Style";
                }
                if (medCharAspect >= 0.58)
                {
                    return "Georgia";
                }
                return "Times New Roman";
            }

            return medCharAspect <= 0.51 ? "Calibri" : "Arial";
        }

        private static double MeasureWordStemRatioInBitmap(PdfRectangle boxPt, SKBitmap pageBitmap, double pageWidthPt, double pageHeightPt)
        {
            if (pageWidthPt <= 0 || pageHeightPt <= 0) return 0.0;
            double scaleX = pageBitmap.Width / pageWidthPt;
            double scaleY = pageBitmap.Height / pageHeightPt;
            int x1 = Math.Clamp((int)(boxPt.Left * scaleX), 0, pageBitmap.Width - 1);
            int x2 = Math.Clamp((int)(boxPt.Right * scaleX), x1 + 1, pageBitmap.Width);
            int y1 = Math.Clamp((int)((pageHeightPt - boxPt.Top) * scaleY), 0, pageBitmap.Height - 1);
            int y2 = Math.Clamp((int)((pageHeightPt - boxPt.Bottom) * scaleY), y1 + 1, pageBitmap.Height);
            int boxH = Math.Max(1, y2 - y1);
            int midY1 = y1 + boxH / 4;
            int midY2 = Math.Max(midY1 + 1, y2 - boxH / 4);
            int maxStemPx = Math.Max(3, (int)Math.Round(boxH * 0.65));
            int stemRunSum = 0, stemRunCount = 0;
            for (int y = midY1; y < midY2; y++)
            {
                int run = 0;
                for (int x = x1; x < x2; x++)
                {
                    var c = pageBitmap.GetPixel(x, y);
                    int lum = (c.Red + c.Green + c.Blue) / 3;
                    if (lum < 145) run++;
                    else if (run > 0)
                    {
                        if (run <= maxStemPx) { stemRunSum += run; stemRunCount++; }
                        run = 0;
                    }
                }
                if (run > 0 && run <= maxStemPx) { stemRunSum += run; stemRunCount++; }
            }
            return stemRunCount > 0 ? ((double)stemRunSum / stemRunCount) / boxH : 0.0;
        }

        private static bool IsPureStampNoiseInBitmap(
            PdfRectangle boxPt,
            string cleanedText,
            SKBitmap pageBitmap,
            double pageWidthPt,
            double pageHeightPt,
            out bool isItalic)
        {
            isItalic = false;
            if (pageWidthPt <= 0 || pageHeightPt <= 0) return false;
            double scaleX = pageBitmap.Width / pageWidthPt;
            double scaleY = pageBitmap.Height / pageHeightPt;

            int x1 = Math.Clamp((int)Math.Round(boxPt.Left * scaleX), 0, pageBitmap.Width - 1);
            int x2 = Math.Clamp((int)Math.Round(boxPt.Right * scaleX), x1 + 1, pageBitmap.Width);
            int y1 = Math.Clamp((int)Math.Round((pageHeightPt - boxPt.Top) * scaleY), 0, pageBitmap.Height - 1);
            int y2 = Math.Clamp((int)Math.Round((pageHeightPt - boxPt.Bottom) * scaleY), y1 + 1, pageBitmap.Height);
            int boxH = Math.Max(1, y2 - y1);
            int boxW = Math.Max(1, x2 - x1);

            int blackTonerPx = 0;
            int coloredStampPx = 0;
            int blueRedCastInkPx = 0;
            int faintGrayPx = 0;
            double topXSum = 0, botXSum = 0;
            int topCount = 0, botCount = 0;

            for (int y = y1; y < y2; y++)
            {
                bool inTopHalf = (y - y1) < boxH / 2;
                for (int x = x1; x < x2; x++)
                {
                    var c = pageBitmap.GetPixel(x, y);
                    int maxC = Math.Max(c.Red, Math.Max(c.Green, c.Blue));
                    int minC = Math.Min(c.Red, Math.Min(c.Green, c.Blue));
                    int sat = maxC - minC;
                    int lum = (c.Red + c.Green + c.Blue) / 3;

                    if (lum < 228 && sat >= 24)
                    {
                        coloredStampPx++;
                    }
                    else if (lum < 175 && Math.Abs(c.Blue - c.Red) >= 16)
                    {
                        blueRedCastInkPx++;
                    }
                    else if (lum < 140 && sat < 20)
                    {
                        blackTonerPx++;
                        if (inTopHalf) { topXSum += x; topCount++; }
                        else { botXSum += x; botCount++; }
                    }
                    else if (lum >= 140 && lum < 218)
                    {
                        faintGrayPx++;
                    }
                }
            }

            if (topCount >= 12 && botCount >= 12 && boxH >= 14 && cleanedText.Length >= 5)
            {
                double topMeanX = topXSum / topCount;
                double botMeanX = botXSum / botCount;
                double shear = (topMeanX - botMeanX) / (boxH * 0.5);
                if (shear >= 0.22 && shear <= 0.46)
                {
                    isItalic = true;
                }
            }

            int area = boxW * boxH;
            int totalStampInk = coloredStampPx + blueRedCastInkPx;
            // Filter curved wet-stamp text (blue/purple/faint circular stamp letters that have almost no black laser toner)
            if (totalStampInk >= 14 && blackTonerPx < Math.Max(10, area * 0.045))
                return true;
            if (cleanedText.Length <= 4 && totalStampInk > blackTonerPx && totalStampInk >= 12)
                return true;
            if (faintGrayPx > blackTonerPx * 4 && blackTonerPx < Math.Max(8, area * 0.025) && totalStampInk + faintGrayPx >= 20)
                return true;
            if (cleanedText.Length <= 2 && !char.IsLetterOrDigit(cleanedText[0]) && totalStampInk >= 6)
                return true;

            return false;
        }

        private static List<RichWord>? TryRecognizeScannedPageWords(
            SKBitmap pageBitmap,
            double pageWidthPt,
            double pageHeightPt,
            int pageNo,
            NexusConversionOptions options,
            List<string> warnings)
        {
            if (pageWidthPt <= 0 || pageHeightPt <= 0) return null;
            var rawOcrWords = RecognizePageWordsRaw(pageBitmap, pageWidthPt, pageHeightPt, pageNo, options, warnings);
            if (rawOcrWords == null || rawOcrWords.Count == 0) return null;

            string dominantPageFont = InferScannedPageFontFamily(pageBitmap, rawOcrWords, pageWidthPt, pageHeightPt);

            // Compute page-level median stem thickness ratio so sturdy serif fonts (like Bookman Old Style) are not falsely marked bold on random words
            var stemSamples = new List<double>(rawOcrWords.Count);
            foreach (var ow in rawOcrWords)
            {
                if (ow.Text.Length < 3) continue;
                var rPt = new PdfRectangle(
                    Math.Clamp(ow.LeftPt, 0, pageWidthPt),
                    Math.Clamp(ow.BottomPt, 0, pageHeightPt),
                    Math.Clamp(Math.Max(ow.LeftPt + 1.0, ow.RightPt), 0, pageWidthPt),
                    Math.Clamp(Math.Max(ow.BottomPt + 1.0, ow.TopPt), 0, pageHeightPt));
                double sr = MeasureWordStemRatioInBitmap(rPt, pageBitmap, pageWidthPt, pageHeightPt);
                if (sr > 0.04) stemSamples.Add(sr);
            }
            stemSamples.Sort();
            double pageMedianStemRatio = stemSamples.Count > 0 ? stemSamples[stemSamples.Count / 2] : 0.125;
            double relativeBoldStemThreshold = Math.Max(0.168, pageMedianStemRatio * 1.28);

            var tempWords = new List<(RichWord Word, int LineIndex)>(rawOcrWords.Count);
            foreach (var ow in rawOcrWords)
            {
                if (string.IsNullOrWhiteSpace(ow.Text)) continue;
                double left = Math.Clamp(ow.LeftPt, 0, pageWidthPt);
                double right = Math.Clamp(Math.Max(left + 1.0, ow.RightPt), 0, pageWidthPt);
                double bottom = Math.Clamp(ow.BottomPt, 0, pageHeightPt);
                double top = Math.Clamp(Math.Max(bottom + 1.0, ow.TopPt), 0, pageHeightPt);
                double bboxHeight = top - bottom;
                double estPt = EstimateWordPointSizeGeneral(ow.Text, bboxHeight, bboxHeight, isOcrPage: true);
                string cleaned = CleanOcrTokenGeneral(ow.Text, bottom, right, pageWidthPt, pageNo, pageNo, warnings);
                if (string.IsNullOrWhiteSpace(cleaned)) continue;
                var rectPt = new PdfRectangle(left, bottom, right, top);
                if (IsPureStampNoiseInBitmap(rectPt, cleaned, pageBitmap, pageWidthPt, pageHeightPt, out bool isItalic))
                    continue;
                bool isBold = IsWordBoldInBitmap(rectPt, pageBitmap, pageWidthPt, pageHeightPt)
                    && MeasureWordStemRatioInBitmap(rectPt, pageBitmap, pageWidthPt, pageHeightPt) >= relativeBoldStemThreshold;

                tempWords.Add((new RichWord(
                    cleaned,
                    left,
                    right,
                    bottom,
                    top,
                    estPt,
                    dominantPageFont,
                    "000000",
                    isBold,
                    isItalic,
                    bottom), ow.LineIndex));
            }

            // Smooth bold and italic across each scanned line so single-word scanner noise never creates random bold/italic spikes inside plain clauses
            var richWords = new List<RichWord>(tempWords.Count);
            foreach (var grp in tempWords.GroupBy(x => x.LineIndex >= 0 ? x.LineIndex : (int)Math.Round(x.Word.Bottom / 6.0)))
            {
                var lineList = grp.Select(x => x.Word).ToList();
                int count = lineList.Count;
                int boldCount = lineList.Count(w => w.IsBold);
                int italicCount = lineList.Count(w => w.IsItalic);
                bool allowLineBold = boldCount * 2 >= count && boldCount >= 2;
                bool allowLineItalic = italicCount * 2 >= count && italicCount >= 2;
                foreach (var w in lineList)
                {
                    richWords.Add(w with
                    {
                        IsBold = allowLineBold && w.IsBold,
                        IsItalic = allowLineItalic && w.IsItalic
                    });
                }
            }
            return richWords.Count > 0 ? richWords : null;
        }

        // ---------------------------------------------------------------------
        // Native OneOCR (oneocr.dll + oneocr.onemodel) In-Process P/Invoke Bridge
        // ---------------------------------------------------------------------
        private static class NativeOneOcrInterop
        {
            [StructLayout(LayoutKind.Sequential)]
            internal struct OneOcrImg
            {
                public int Type;
                public int Width;
                public int Height;
                public long Step;
                public IntPtr Data;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct OneOcrBoundingBox
            {
                public float X1, Y1, X2, Y2, X3, Y3, X4, Y4;
            }

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate long CreateOcrInitOptionsDelegate(out long ctx);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate long OcrInitOptionsSetUseModelDelayLoadDelegate(long ctx, byte flag);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] internal delegate long CreateOcrPipelineDelegate(string modelPath, string key, long ctx, out long pipeline);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate long CreateOcrProcessOptionsDelegate(out long opt);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate long OcrProcessOptionsSetMaxRecognitionLineCountDelegate(long opt, long maxLines);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate long RunOcrPipelineDelegate(long pipeline, ref OneOcrImg img, long opt, out long instance);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate long GetOcrLineCountDelegate(long instance, out long count);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate long GetOcrLineDelegate(long instance, long index, out long line);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate long GetOcrLineWordCountDelegate(long line, out long count);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate long GetOcrWordDelegate(long line, long index, out long word);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate long GetOcrWordContentDelegate(long word, out IntPtr contentPtr);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate long GetOcrWordBoundingBoxDelegate(long word, out IntPtr bboxPtr);
        }

        private static IReadOnlyList<OcrRecognizedWord>? TryRunNativeOneOcr(
            SKBitmap pageBitmap,
            double pageWidthPt,
            double pageHeightPt,
            NexusConversionOptions options)
        {
            if (!OperatingSystem.IsWindows()) return null;
            var (dllPath, modelPath) = ResolveOneOcrPaths(options.OneOcrDirectory);
            if (string.IsNullOrEmpty(dllPath) || string.IsNullOrEmpty(modelPath)) return null;

            IntPtr libHandle = IntPtr.Zero;
            try
            {
                string dllDir = Path.GetDirectoryName(dllPath)!;
                string onnxPath = Path.Combine(dllDir, "onnxruntime.dll");
                if (File.Exists(onnxPath)) NativeLibrary.TryLoad(onnxPath, out _);
                if (!NativeLibrary.TryLoad(dllPath, out libHandle)) return null;

                T GetExport<T>(string name) where T : Delegate
                    => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(libHandle, name));

                var createInit = GetExport<NativeOneOcrInterop.CreateOcrInitOptionsDelegate>("CreateOcrInitOptions");
                var setDelay = GetExport<NativeOneOcrInterop.OcrInitOptionsSetUseModelDelayLoadDelegate>("OcrInitOptionsSetUseModelDelayLoad");
                var createPipe = GetExport<NativeOneOcrInterop.CreateOcrPipelineDelegate>("CreateOcrPipeline");
                var createOpt = GetExport<NativeOneOcrInterop.CreateOcrProcessOptionsDelegate>("CreateOcrProcessOptions");
                var setMaxLines = GetExport<NativeOneOcrInterop.OcrProcessOptionsSetMaxRecognitionLineCountDelegate>("OcrProcessOptionsSetMaxRecognitionLineCount");
                var runPipe = GetExport<NativeOneOcrInterop.RunOcrPipelineDelegate>("RunOcrPipeline");
                var getLineCount = GetExport<NativeOneOcrInterop.GetOcrLineCountDelegate>("GetOcrLineCount");
                var getLine = GetExport<NativeOneOcrInterop.GetOcrLineDelegate>("GetOcrLine");
                var getWordCount = GetExport<NativeOneOcrInterop.GetOcrLineWordCountDelegate>("GetOcrLineWordCount");
                var getWord = GetExport<NativeOneOcrInterop.GetOcrWordDelegate>("GetOcrWord");
                var getWordContent = GetExport<NativeOneOcrInterop.GetOcrWordContentDelegate>("GetOcrWordContent");
                var getWordBbox = GetExport<NativeOneOcrInterop.GetOcrWordBoundingBoxDelegate>("GetOcrWordBoundingBox");

                if (createInit(out long ctx) != 0) return null;
                setDelay(ctx, 0);
                const string oneOcrKey = "kj)TGtrK>f]b[Piow.gU+nC@s\"\"\"\"\"\"4";
                if (createPipe(modelPath, oneOcrKey, ctx, out long pipeline) != 0) return null;
                if (createOpt(out long opt) != 0) return null;
                setMaxLines(opt, 1000);

                using var bgraBitmap = new SKBitmap(pageBitmap.Width, pageBitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var canvas = new SKCanvas(bgraBitmap))
                {
                    canvas.Clear(SKColors.White);
                    canvas.DrawBitmap(pageBitmap, 0, 0);
                }

                var imgStruct = new NativeOneOcrInterop.OneOcrImg
                {
                    Type = 3, // BGRA8888
                    Width = bgraBitmap.Width,
                    Height = bgraBitmap.Height,
                    Step = bgraBitmap.RowBytes,
                    Data = bgraBitmap.GetPixels()
                };

                if (runPipe(pipeline, ref imgStruct, opt, out long instance) != 0) return null;
                if (getLineCount(instance, out long lineCount) != 0 || lineCount <= 0) return null;

                double scaleX = pageWidthPt / Math.Max(1, bgraBitmap.Width);
                double scaleY = pageHeightPt / Math.Max(1, bgraBitmap.Height);
                var words = new List<OcrRecognizedWord>();

                for (long l = 0; l < lineCount; l++)
                {
                    if (getLine(instance, l, out long lineHandle) != 0 || lineHandle == 0) continue;
                    if (getWordCount(lineHandle, out long wordCount) != 0 || wordCount <= 0) continue;
                    for (long w = 0; w < wordCount; w++)
                    {
                        if (getWord(lineHandle, w, out long wordHandle) != 0 || wordHandle == 0) continue;
                        if (getWordContent(wordHandle, out IntPtr textPtr) != 0 || textPtr == IntPtr.Zero) continue;
                        if (getWordBbox(wordHandle, out IntPtr bboxPtr) != 0 || bboxPtr == IntPtr.Zero) continue;

                        string? wordText = Marshal.PtrToStringUTF8(textPtr)?.Trim();
                        if (string.IsNullOrEmpty(wordText)) continue;
                        var bbox = Marshal.PtrToStructure<NativeOneOcrInterop.OneOcrBoundingBox>(bboxPtr);
                        float minX = Math.Min(Math.Min(bbox.X1, bbox.X2), Math.Min(bbox.X3, bbox.X4));
                        float maxX = Math.Max(Math.Max(bbox.X1, bbox.X2), Math.Max(bbox.X3, bbox.X4));
                        float minY = Math.Min(Math.Min(bbox.Y1, bbox.Y2), Math.Min(bbox.Y3, bbox.Y4));
                        float maxY = Math.Max(Math.Max(bbox.Y1, bbox.Y2), Math.Max(bbox.Y3, bbox.Y4));
                        if (maxX <= minX || maxY <= minY) continue;

                        double leftPt = minX * scaleX;
                        double rightPt = maxX * scaleX;
                        double topPt = pageHeightPt - (minY * scaleY);
                        double bottomPt = pageHeightPt - (maxY * scaleY);
                        words.Add(new OcrRecognizedWord(wordText, leftPt, bottomPt, rightPt, topPt, 0.98, (int)l));
                    }
                }
                return words;
            }
            catch
            {
                return null;
            }
        }

        private static (string? DllPath, string? ModelPath) ResolveOneOcrPaths(string? configuredDir)
        {
            var candidateDirs = new List<string>();
            if (!string.IsNullOrWhiteSpace(configuredDir)) candidateDirs.Add(configuredDir);
            string? envDir = Environment.GetEnvironmentVariable("NEXUS_ONEOCR_DIR") ?? Environment.GetEnvironmentVariable("ONEOCR_DIR");
            if (!string.IsNullOrWhiteSpace(envDir)) candidateDirs.Add(envDir);
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localAppData))
                {
                    candidateDirs.Add(Path.Combine(localAppData, "Nexus", "OneOcr"));
                }
            }
            catch { }
            candidateDirs.Add(AppContext.BaseDirectory);
            candidateDirs.Add(Path.Combine(AppContext.BaseDirectory, "oneocr"));
            candidateDirs.Add(Path.Combine(AppContext.BaseDirectory, "ocr"));

            foreach (var dir in candidateDirs.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string directDll = Path.Combine(dir, "oneocr.dll");
                string directModel = Path.Combine(dir, "oneocr.onemodel");
                if (File.Exists(directDll) && File.Exists(directModel))
                    return (directDll, directModel);
            }

            try
            {
                if (Directory.Exists(@"D:\NEXUS"))
                {
                    var foundModel = Directory.EnumerateFiles(@"D:\NEXUS", "oneocr.onemodel", SearchOption.AllDirectories).FirstOrDefault();
                    if (!string.IsNullOrEmpty(foundModel))
                    {
                        string modelDir = Path.GetDirectoryName(foundModel)!;
                        string dll = Path.Combine(modelDir, "oneocr.dll");
                        if (File.Exists(dll)) return (dll, foundModel);
                    }
                }
            }
            catch { }

            return (null, null);
        }

        // ---------------------------------------------------------------------
        // PP-OCRv6 Bridge: Nexus.Engine.ImageSubsystem.Ocr.PpOcrEngine + ONNX + CLI
        // ---------------------------------------------------------------------
        private static IReadOnlyList<OcrRecognizedWord>? TryRunPpOcrV6Bridge(
            SKBitmap pageBitmap,
            double pageWidthPt,
            double pageHeightPt,
            NexusConversionOptions options)
        {
            string? configuredPath = options.PpOcrModelOrExecutablePath
                ?? Environment.GetEnvironmentVariable("NEXUS_PPOCR_PATH")
                ?? Environment.GetEnvironmentVariable("PPOCR_PATH");

            string? modelDir = null;
            if (!string.IsNullOrWhiteSpace(configuredPath) && Directory.Exists(configuredPath))
            {
                modelDir = configuredPath;
            }
            else if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath) && configuredPath.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
            {
                modelDir = Path.GetDirectoryName(configuredPath);
            }
            else if (Directory.Exists(@"D:\NEXUS\Models\AI Workspace\PP-OCRv6"))
            {
                modelDir = @"D:\NEXUS\Models\AI Workspace\PP-OCRv6";
            }

            // 1. Try invoking Nexus.Engine.ImageSubsystem.Ocr.PpOcrEngine directly in-process via reflection
            var viaSubsystem = TryInvokeNexusImageSubsystemPpOcr(pageBitmap, pageWidthPt, pageHeightPt, modelDir);
            if (viaSubsystem != null && viaSubsystem.Count > 0) return viaSubsystem;

            // 2. Try CLI executable if configuredPath is an .exe
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath) && configuredPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                string tempImg = Path.Combine(Path.GetTempPath(), $"nexus_ppocr_{Guid.NewGuid():N}.png");
                try
                {
                    using (var img = SKImage.FromBitmap(pageBitmap))
                    using (var encoded = img.Encode(SKEncodedImageFormat.Png, 100))
                    using (var fs = File.Create(tempImg))
                    {
                        encoded.SaveTo(fs);
                    }

                    var psi = new ProcessStartInfo
                    {
                        FileName = configuredPath,
                        Arguments = $"\"{tempImg}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        string output = proc.StandardOutput.ReadToEnd();
                        if (proc.WaitForExit(30000) && proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                        {
                            var parsed = ParseTabOrJsonOcrOutput(output, pageBitmap.Width, pageBitmap.Height, pageWidthPt, pageHeightPt);
                            if (parsed.Count > 0) return parsed;
                        }
                    }
                }
                catch { }
                finally
                {
                    CleanupStagingFile(tempImg);
                }
            }
            return null;
        }

        private static IReadOnlyList<OcrRecognizedWord>? TryInvokeNexusImageSubsystemPpOcr(
            SKBitmap pageBitmap,
            double pageWidthPt,
            double pageHeightPt,
            string? modelDir)
        {
            try
            {
                var targetType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                    .FirstOrDefault(t => t.Name.Equals("PpOcrEngine", StringComparison.OrdinalIgnoreCase));

                if (targetType == null && Directory.Exists(@"D:\NEXUS\Nexus-CSharp"))
                {
                    var dllFile = Directory.EnumerateFiles(@"D:\NEXUS\Nexus-CSharp", "Nexus.Engine.ImageSubsystem.dll", SearchOption.AllDirectories)
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
                    if (!string.IsNullOrEmpty(dllFile))
                    {
                        var asm = System.Reflection.Assembly.LoadFrom(dllFile);
                        targetType = asm.GetTypes().FirstOrDefault(t => t.Name.Equals("PpOcrEngine", StringComparison.OrdinalIgnoreCase));
                    }
                }
                if (targetType == null) return null;

                object? engineInstance = null;
                foreach (var ctor in targetType.GetConstructors().OrderBy(c => c.GetParameters().Length))
                {
                    var prms = ctor.GetParameters();
                    if (prms.Length == 0)
                    {
                        engineInstance = ctor.Invoke(null);
                        break;
                    }
                    if (prms.Length == 1 && prms[0].ParameterType == typeof(string) && !string.IsNullOrEmpty(modelDir))
                    {
                        engineInstance = ctor.Invoke(new object[] { modelDir });
                        break;
                    }
                }
                if (engineInstance == null) return null;

                var methods = targetType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var recMethod = methods.FirstOrDefault(m =>
                    (m.Name.Contains("Recognize", StringComparison.OrdinalIgnoreCase)
                     || m.Name.Contains("Detect", StringComparison.OrdinalIgnoreCase)
                     || m.Name.Contains("Run", StringComparison.OrdinalIgnoreCase)
                     || m.Name.Contains("Process", StringComparison.OrdinalIgnoreCase))
                    && m.GetParameters().Length >= 1);
                if (recMethod == null) return null;

                object? arg0 = null;
                var pType = recMethod.GetParameters()[0].ParameterType;
                string? tempFile = null;
                try
                {
                    if (pType == typeof(SKBitmap)) arg0 = pageBitmap;
                    else if (pType == typeof(byte[]))
                    {
                        using var img = SKImage.FromBitmap(pageBitmap);
                        using var enc = img.Encode(SKEncodedImageFormat.Png, 100);
                        arg0 = enc.ToArray();
                    }
                    else if (pType == typeof(string))
                    {
                        tempFile = Path.Combine(Path.GetTempPath(), $"nexus_ppocr_in_{Guid.NewGuid():N}.png");
                        using var img = SKImage.FromBitmap(pageBitmap);
                        using var enc = img.Encode(SKEncodedImageFormat.Png, 100);
                        using var fs = File.Create(tempFile);
                        enc.SaveTo(fs);
                        arg0 = tempFile;
                    }
                    if (arg0 == null) return null;

                    var callArgs = new object?[recMethod.GetParameters().Length];
                    callArgs[0] = arg0;
                    for (int i = 1; i < callArgs.Length; i++)
                    {
                        callArgs[i] = recMethod.GetParameters()[i].HasDefaultValue ? recMethod.GetParameters()[i].DefaultValue : null;
                    }

                    object? rawResult = recMethod.Invoke(engineInstance, callArgs);
                    if (rawResult is Task taskResult)
                    {
                        taskResult.GetAwaiter().GetResult();
                        rawResult = taskResult.GetType().GetProperty("Result")?.GetValue(taskResult);
                    }
                    if (rawResult is System.Collections.IEnumerable enumerable and not string)
                    {
                        double scaleX = pageWidthPt / Math.Max(1, pageBitmap.Width);
                        double scaleY = pageHeightPt / Math.Max(1, pageBitmap.Height);
                        var words = new List<OcrRecognizedWord>();
                        int idx = 0;
                        foreach (var item in enumerable)
                        {
                            if (item == null) continue;
                            var itType = item.GetType();
                            string? txt = (itType.GetProperty("Text") ?? itType.GetProperty("Content") ?? itType.GetProperty("Label"))?.GetValue(item)?.ToString()?.Trim();
                            if (string.IsNullOrEmpty(txt)) continue;
                            double x1 = Convert.ToDouble((itType.GetProperty("Left") ?? itType.GetProperty("X"))?.GetValue(item) ?? 0);
                            double y1 = Convert.ToDouble((itType.GetProperty("Top") ?? itType.GetProperty("Y"))?.GetValue(item) ?? 0);
                            double w = Convert.ToDouble(itType.GetProperty("Width")?.GetValue(item) ?? 0);
                            double h = Convert.ToDouble(itType.GetProperty("Height")?.GetValue(item) ?? 0);
                            if (w > 0 && h > 0)
                            {
                                words.Add(new OcrRecognizedWord(
                                    txt,
                                    x1 * scaleX,
                                    pageHeightPt - (y1 + h) * scaleY,
                                    (x1 + w) * scaleX,
                                    pageHeightPt - y1 * scaleY,
                                    0.95,
                                    idx++));
                            }
                        }
                        if (words.Count > 0) return words;
                    }
                }
                finally
                {
                    if (tempFile != null) CleanupStagingFile(tempFile);
                    if (engineInstance is IDisposable disp) disp.Dispose();
                }
            }
            catch { }
            return null;
        }

        private static List<OcrRecognizedWord> ParseTabOrJsonOcrOutput(
            string output,
            int bmpWidth,
            int bmpHeight,
            double pageWidthPt,
            double pageHeightPt)
        {
            var results = new List<OcrRecognizedWord>();
            double scaleX = pageWidthPt / Math.Max(1, bmpWidth);
            double scaleY = pageHeightPt / Math.Max(1, bmpHeight);
            foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = rawLine.Split('\t');
                if (parts.Length >= 5
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x1)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y1)
                    && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double x2)
                    && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double y2))
                {
                    string txt = parts[4].Trim();
                    if (!string.IsNullOrEmpty(txt) && x2 > x1 && y2 > y1)
                    {
                        results.Add(new OcrRecognizedWord(
                            txt,
                            x1 * scaleX,
                            pageHeightPt - y2 * scaleY,
                            x2 * scaleX,
                            pageHeightPt - y1 * scaleY,
                            0.95));
                    }
                }
            }
            return results;
        }

        private static IReadOnlyList<OcrRecognizedWord>? TryRunTesseractCliOcr(
            SKBitmap pageBitmap,
            double pageWidthPt,
            double pageHeightPt,
            NexusConversionOptions options)
        {
            string? exe = ResolveTesseractExecutable(options.TesseractExecutablePath);
            if (string.IsNullOrEmpty(exe)) return null;

            string tempImg = Path.Combine(Path.GetTempPath(), $"nexus_ocr_{Guid.NewGuid():N}.png");
            try
            {
                using (var img = SKImage.FromBitmap(pageBitmap))
                using (var encoded = img.Encode(SKEncodedImageFormat.Png, 100))
                using (var fs = File.Create(tempImg))
                {
                    encoded.SaveTo(fs);
                }

                string lang = string.IsNullOrWhiteSpace(options.OcrLanguage) ? "eng" : options.OcrLanguage;
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"\"{tempImg}\" stdout -l {lang} tsv",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                string tsv = proc.StandardOutput.ReadToEnd();
                if (!proc.WaitForExit(30000) || proc.ExitCode != 0 || string.IsNullOrWhiteSpace(tsv)) return null;

                double scaleX = pageWidthPt / Math.Max(1, pageBitmap.Width);
                double scaleY = pageHeightPt / Math.Max(1, pageBitmap.Height);
                var results = new List<OcrRecognizedWord>();
                var lines = tsv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 1; i < lines.Length; i++)
                {
                    var cols = lines[i].Split('\t');
                    if (cols.Length < 12) continue;
                    if (!int.TryParse(cols[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) || level != 5) continue;
                    string wordText = cols[11].Trim();
                    if (string.IsNullOrEmpty(wordText)) continue;
                    if (!int.TryParse(cols[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lineNum)) lineNum = -1;
                    if (!int.TryParse(cols[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pxLeft)
                        || !int.TryParse(cols[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pxTop)
                        || !int.TryParse(cols[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pxWidth)
                        || !int.TryParse(cols[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pxHeight)) continue;
                    if (pxWidth <= 0 || pxHeight <= 0) continue;
                    double.TryParse(cols[10], NumberStyles.Float, CultureInfo.InvariantCulture, out double conf);
                    if (conf >= 0 && conf < 15.0) continue;

                    double leftPt = pxLeft * scaleX;
                    double rightPt = (pxLeft + pxWidth) * scaleX;
                    double topPt = pageHeightPt - (pxTop * scaleY);
                    double bottomPt = pageHeightPt - ((pxTop + pxHeight) * scaleY);
                    results.Add(new OcrRecognizedWord(wordText, leftPt, bottomPt, rightPt, topPt, conf / 100.0, lineNum));
                }
                return results;
            }
            catch
            {
                return null;
            }
            finally
            {
                CleanupStagingFile(tempImg);
            }
        }

        private static string? ResolveTesseractExecutable(string? configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath)) return configuredPath;
            string? envPath = Environment.GetEnvironmentVariable("NEXUS_TESSERACT_PATH")
                ?? Environment.GetEnvironmentVariable("TESSERACT_PATH");
            if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath)) return envPath;

            if (OperatingSystem.IsWindows())
            {
                string[] winCandidates =
                {
                    @"C:\Program Files\Tesseract-OCR\tesseract.exe",
                    @"C:\Program Files (x86)\Tesseract-OCR\tesseract.exe",
                    Path.Combine(AppContext.BaseDirectory, "tesseract", "tesseract.exe"),
                    Path.Combine(AppContext.BaseDirectory, "tesseract.exe")
                };
                foreach (var c in winCandidates)
                {
                    if (File.Exists(c)) return c;
                }
            }
            else
            {
                string[] unixCandidates = { "/usr/bin/tesseract", "/usr/local/bin/tesseract" };
                foreach (var c in unixCandidates)
                {
                    if (File.Exists(c)) return c;
                }
            }
            return null;
        }

        private static void EnsureDirectory(string filePath)
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
    }
}