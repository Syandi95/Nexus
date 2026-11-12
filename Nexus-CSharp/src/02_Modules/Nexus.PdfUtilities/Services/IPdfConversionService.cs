using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.PdfUtilities
{
    public enum PdfPreflightFailureReason
    {
        None,
        PageDimensionsUnavailable,
        MemoryHeadroomUnavailable,
        NoPagesSelected,
        EstimateOverflow
    }

    public sealed class PdfPreflightResult
    {
        public bool IsSafe { get; init; }
        public int HighestRiskPage { get; init; }
        public int WidthPixels { get; init; }
        public int HeightPixels { get; init; }
        public double EstimatedMegapixels { get; init; }
        public long EstimatedMemoryBytes { get; init; }
        public int SelectedDpi { get; init; }
        public int? RecommendedMaxDpi { get; init; }
        public string OutputFormat { get; init; } = string.Empty;
        public PdfPreflightFailureReason FailureReason { get; init; }
    }

    public class PdfConversionProgressReport
    {
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public string PhaseText { get; set; } = string.Empty;
        public string? OutputFilePath { get; set; }
        public double Percent => TotalPages > 0 ? (double)CurrentPage / TotalPages * 100 : 0;
    }

    public interface IPdfConversionService
    {
        Task<PdfPreflightResult> PreflightAsync(
            string pdfPath,
            string format,
            int dpi,
            IReadOnlyList<int>? pageNumbers,
            CancellationToken cancellationToken);

        Task ConvertAsync(
            string pdfPath,
            string outputDir,
            string format,
            int dpi,
            int quality,
            int namingOption,
            IReadOnlyList<int>? pageNumbers,
            IProgress<PdfConversionProgressReport>? progress,
            CancellationToken cancellationToken);

        Task ConvertAsync(
            string pdfPath,
            string outputDir,
            string format,
            int dpi,
            int quality,
            CancellationToken cancellationToken);
    }
}
