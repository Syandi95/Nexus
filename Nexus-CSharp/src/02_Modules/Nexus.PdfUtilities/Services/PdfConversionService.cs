using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Docnet.Core;
using Docnet.Core.Models;
using Docnet.Core.Readers;
using SkiaSharp;

namespace Nexus.PdfUtilities
{
    public class PdfConversionService : IPdfConversionService
    {
        private readonly record struct PageRasterEstimate(int PageNumber, int WidthPixels, int HeightPixels, long PixelCount, long EstimatedPeakBytes);

        public Task<PdfPreflightResult> PreflightAsync(
            string pdfPath,
            string format,
            int dpi,
            IReadOnlyList<int>? pageNumbers,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(pdfPath))
                throw new ArgumentException("PDF path cannot be empty", nameof(pdfPath));
            if (string.IsNullOrWhiteSpace(format))
                throw new ArgumentException("Output format cannot be empty", nameof(format));
            if (dpi <= 0)
                throw new ArgumentOutOfRangeException(nameof(dpi));
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException("PDF file not found", pdfPath);

            return Task.Run(() => PreflightCore(pdfPath, format, dpi, pageNumbers, cancellationToken), cancellationToken);
        }

        private static PdfPreflightResult PreflightCore(
            string pdfPath,
            string format,
            int dpi,
            IReadOnlyList<int>? pageNumbers,
            CancellationToken cancellationToken)
        {
            double scale = dpi / 72.0;
            var estimates = new List<PageRasterEstimate>();
            int pageCount;

            using (var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(scale)))
            {
                pageCount = docReader.GetPageCount();
                if (pageCount <= 0)
                    return Failure(dpi, format, PdfPreflightFailureReason.PageDimensionsUnavailable);

                IEnumerable<int> pagesToCheck = pageNumbers is null
                    ? Enumerable.Range(1, pageCount)
                    : pageNumbers;
                if (pageNumbers?.Count == 0)
                    return Failure(dpi, format, PdfPreflightFailureReason.NoPagesSelected);

                bool isPng = format.Equals("png", StringComparison.OrdinalIgnoreCase);
                long multiplier = isPng ? 3L : 4L;

                foreach (int pageNumber in pagesToCheck)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int pageIndex = pageNumber - 1;
                    if (pageIndex < 0 || pageIndex >= pageCount)
                        return Failure(dpi, format, PdfPreflightFailureReason.PageDimensionsUnavailable);

                    int width;
                    int height;
                    using (var pageReader = docReader.GetPageReader(pageIndex))
                    {
                        // Page dimensions are available at the selected DPI without calling GetImage().
                        width = pageReader.GetPageWidth();
                        height = pageReader.GetPageHeight();
                    }

                    if (width <= 0 || height <= 0)
                        return Failure(dpi, format, PdfPreflightFailureReason.PageDimensionsUnavailable);

                    try
                    {
                        long pixelCount = checked((long)width * height);
                        long rasterBytes = checked(pixelCount * 4L);
                        long estimatedPeakBytes = checked(rasterBytes * multiplier);
                        estimates.Add(new PageRasterEstimate(pageNumber, width, height, pixelCount, estimatedPeakBytes));
                    }
                    catch (OverflowException)
                    {
                        return Failure(dpi, format, PdfPreflightFailureReason.EstimateOverflow);
                    }
                }
            }

            if (estimates.Count == 0)
                return Failure(dpi, format, PdfPreflightFailureReason.NoPagesSelected);

            var highestRisk = estimates.MaxBy(estimate => estimate.EstimatedPeakBytes)!;
            var memoryInfo = GC.GetGCMemoryInfo();
            long totalAvailable = memoryInfo.TotalAvailableMemoryBytes;
            long highLoadThreshold = memoryInfo.HighMemoryLoadThresholdBytes;
            long workingSet = Environment.WorkingSet;

            if (totalAvailable <= 0 || highLoadThreshold <= 0 || workingSet < 0)
            {
                return CreateResult(
                    isSafe: false,
                    highestRisk,
                    dpi,
                    format,
                    recommendedMaxDpi: null,
                    PdfPreflightFailureReason.MemoryHeadroomUnavailable);
            }

            long headroomThreshold = Math.Min(totalAvailable, highLoadThreshold);
            long availableHeadroom;
            try
            {
                availableHeadroom = checked(headroomThreshold - workingSet);
            }
            catch (OverflowException)
            {
                availableHeadroom = long.MinValue;
            }

            if (availableHeadroom <= 0)
            {
                return CreateResult(
                    isSafe: false,
                    highestRisk,
                    dpi,
                    format,
                    recommendedMaxDpi: null,
                    PdfPreflightFailureReason.MemoryHeadroomUnavailable);
            }

            bool isSafe = estimates.All(estimate => estimate.EstimatedPeakBytes <= availableHeadroom);
            int? recommendedDpi = isSafe
                ? dpi
                : FindHighestSafeDpi(estimates, dpi, availableHeadroom, format.Equals("png", StringComparison.OrdinalIgnoreCase));

            return CreateResult(
                isSafe,
                highestRisk,
                dpi,
                format,
                recommendedDpi,
                PdfPreflightFailureReason.None);
        }

        private static int? FindHighestSafeDpi(
            IReadOnlyList<PageRasterEstimate> estimates,
            int selectedDpi,
            long availableHeadroom,
            bool isPng)
        {
            int multiplier = isPng ? 3 : 4;
            int low = 1;
            int high = selectedDpi - 1;
            int best = 0;

            while (low <= high)
            {
                int candidate = low + (high - low) / 2;
                bool candidateSafe = true;

                foreach (var estimate in estimates)
                {
                    long width = (long)Math.Ceiling((double)estimate.WidthPixels * candidate / selectedDpi);
                    long height = (long)Math.Ceiling((double)estimate.HeightPixels * candidate / selectedDpi);
                    long estimatedPeak;
                    try
                    {
                        estimatedPeak = checked(checked(checked(width * height) * 4L) * multiplier);
                    }
                    catch (OverflowException)
                    {
                        candidateSafe = false;
                        break;
                    }

                    if (estimatedPeak > availableHeadroom)
                    {
                        candidateSafe = false;
                        break;
                    }
                }

                if (candidateSafe)
                {
                    best = candidate;
                    low = candidate + 1;
                }
                else
                {
                    high = candidate - 1;
                }
            }

            return best > 0 ? best : null;
        }

        private static PdfPreflightResult CreateResult(
            bool isSafe,
            PageRasterEstimate highestRisk,
            int dpi,
            string format,
            int? recommendedMaxDpi,
            PdfPreflightFailureReason failureReason)
        {
            return new PdfPreflightResult
            {
                IsSafe = isSafe,
                HighestRiskPage = highestRisk.PageNumber,
                WidthPixels = highestRisk.WidthPixels,
                HeightPixels = highestRisk.HeightPixels,
                EstimatedMegapixels = highestRisk.PixelCount / 1_000_000.0,
                EstimatedMemoryBytes = highestRisk.EstimatedPeakBytes,
                SelectedDpi = dpi,
                RecommendedMaxDpi = recommendedMaxDpi,
                OutputFormat = format,
                FailureReason = failureReason
            };
        }

        private static PdfPreflightResult Failure(int dpi, string format, PdfPreflightFailureReason reason)
        {
            return new PdfPreflightResult
            {
                IsSafe = false,
                SelectedDpi = dpi,
                OutputFormat = format,
                FailureReason = reason
            };
        }

        public Task ConvertAsync(
            string pdfPath,
            string outputDir,
            string format,
            int dpi,
            int quality,
            CancellationToken cancellationToken)
        {
            return ConvertAsync(pdfPath, outputDir, format, dpi, quality, 0, null, null, cancellationToken);
        }

        public Task ConvertAsync(
            string pdfPath,
            string outputDir,
            string format,
            int dpi,
            int quality,
            int namingOption,
            IReadOnlyList<int>? pageNumbers,
            IProgress<PdfConversionProgressReport>? progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(pdfPath))
                throw new ArgumentException("PDF path cannot be empty", nameof(pdfPath));
            if (string.IsNullOrWhiteSpace(outputDir))
                throw new ArgumentException("Output dir cannot be empty", nameof(outputDir));

            if (!File.Exists(pdfPath))
                throw new FileNotFoundException("PDF file not found", pdfPath);

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            return Task.Run(() =>
            {
                var renderFlags = RenderFlags.RenderAnnotations;
                double scale = dpi / 72.0;

                using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(scale));
                int pageCount = docReader.GetPageCount();

                var isPng = format.Equals("png", StringComparison.OrdinalIgnoreCase);
                var skFormat = isPng ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
                var ext = isPng ? "png" : "jpg";
                var prefix = Path.GetFileNameWithoutExtension(pdfPath);
                var pagesToConvert = pageNumbers ?? Enumerable.Range(1, pageCount).ToArray();

                foreach (int pageNum in pagesToConvert)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int i = pageNum - 1;
                    progress?.Report(new PdfConversionProgressReport
                    {
                        CurrentPage = pageNum,
                        TotalPages = pageCount,
                        PhaseText = $"Render halaman {pageNum} dari {pageCount}..."
                    });

                    byte[] rawBytes;
                    int width;
                    int height;

                    using (var pageReader = docReader.GetPageReader(i))
                    {
                        rawBytes = pageReader.GetImage(renderFlags);
                        width = pageReader.GetPageWidth();
                        height = pageReader.GetPageHeight();
                    }

                    // Non-native CPU/Image processing & Disk I/O runs outside native Docnet calls
                    using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                    var handle = GCHandle.Alloc(rawBytes, GCHandleType.Pinned);
                    bool pixelsInstalled=false;

                    try
                    {
                        pixelsInstalled=bitmap.InstallPixels(bitmap.Info, handle.AddrOfPinnedObject(), bitmap.RowBytes, delegate { if(handle.IsAllocated)handle.Free(); });
                        if(!pixelsInstalled)throw new InvalidOperationException("Image buffer could not be installed.");

                        string fileName = namingOption switch
                        {
                            0 => $"{prefix}_{pageNum}.{ext}",
                            1 => pageCount == 1 ? $"{prefix}.{ext}" : $"{prefix}_{pageNum}.{ext}",
                            2 => $"{pageNum}.{ext}",
                            3 => $"PDF_{prefix}_{pageNum}.{ext}",
                            _ => $"{prefix}_{pageNum}.{ext}"
                        };

                        var outPath = Path.Combine(outputDir, fileName);
                        using var jpegBitmap = isPng
                            ? null
                            : new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                        if (jpegBitmap != null)
                        {
                            using var canvas = new SKCanvas(jpegBitmap);
                            canvas.Clear(SKColors.White);
                            canvas.DrawBitmap(bitmap, 0, 0, new SKSamplingOptions());
                        }

                        using var image = SKImage.FromBitmap(jpegBitmap ?? bitmap);
                        using var data = image.Encode(skFormat, quality);
                        var temporaryPath=outPath+"."+Guid.NewGuid().ToString("N")+".partial";
                        bool ownsTemporary=false;
                        try
                        {
                            using(var stream=new FileStream(temporaryPath,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                            {ownsTemporary=true;data.SaveTo(stream);stream.Flush(true);}
                            cancellationToken.ThrowIfCancellationRequested();
                            File.Move(temporaryPath,outPath,overwrite:true);
                        }
                        finally{if(ownsTemporary&&File.Exists(temporaryPath))File.Delete(temporaryPath);}

                        progress?.Report(new PdfConversionProgressReport
                        {
                            CurrentPage = pageNum,
                            TotalPages = pageCount,
                            PhaseText = $"Render halaman {pageNum} dari {pageCount}...",
                            OutputFilePath = outPath
                        });
                    }
                    catch
                    {
                        if (!pixelsInstalled && handle.IsAllocated)
                            handle.Free();
                        throw;
                    }
                }

                progress?.Report(new PdfConversionProgressReport
                {
                    CurrentPage = pageCount,
                    TotalPages = pageCount,
                    PhaseText = "Selesai"
                });
            }, cancellationToken);
        }
    }
}
