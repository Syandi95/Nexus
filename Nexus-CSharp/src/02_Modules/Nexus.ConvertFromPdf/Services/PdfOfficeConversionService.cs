using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nexus.PdfUtilities.OfficeConversion;

namespace Nexus.PdfUtilities
{
    public enum ConversionStatus
    {
        Success = 0,
        NoContent = 1,
        Canceled = 2,
        Failed = 3
    }

    public sealed class PdfOfficeConversionResult
    {
        public bool IsSuccess { get; init; }
        public bool IsCanceled { get; init; }
        public ConversionStatus Status { get; init; }
        public string OutputPath { get; init; } = string.Empty;
        public int PagesProcessed { get; init; }
        public int TablesReconstructed { get; init; }
        public int ImagesProcessed { get; init; }
        public long OutputSizeBytes { get; init; }
        public TimeSpan Duration { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    public sealed class PdfImageExtractionResult
    {
        public bool IsSuccess { get; init; }
        public bool IsCanceled { get; init; }
        public ConversionStatus Status { get; init; }
        public string OutputDirectory { get; init; } = string.Empty;
        public int PagesProcessed { get; init; }
        public IReadOnlyList<string> ExtractedFiles { get; init; } = Array.Empty<string>();
        public TimeSpan Duration { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    public interface IPdfOfficeConversionService
    {
        Task<PdfOfficeConversionResult> ConvertToDocxAsync(
            string inputPdfPath,
            string outputDocxPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);

        Task<PdfOfficeConversionResult> ConvertToXlsxAsync(
            string inputPdfPath,
            string outputXlsxPath,
            bool multipleSheets = true,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);

        Task<PdfOfficeConversionResult> ConvertToPptxAsync(
            string inputPdfPath,
            string outputPptxPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);

        Task<PdfImageExtractionResult> ExtractImagesAsync(
            string inputPdfPath,
            string outputDirectory,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);
    }

    public sealed class PdfOfficeConversionService : IPdfOfficeConversionService
    {
        private readonly INexusHighFidelityConverter _engine;
        private readonly NexusConversionOptions _options;

        public PdfOfficeConversionService()
            : this(new NexusHighFidelityConverter(), new NexusConversionOptions())
        {
        }

        public PdfOfficeConversionService(INexusHighFidelityConverter engine, NexusConversionOptions? options = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _options = options ?? new NexusConversionOptions();
        }

        public async Task<PdfOfficeConversionResult> ConvertToDocxAsync(
            string inputPdfPath,
            string outputDocxPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!TryValidatePaths(inputPdfPath, outputDocxPath, ".docx", out string? errCode, out string? errMsg))
            {
                return CreateFailedOfficeResult(outputDocxPath, errCode!, errMsg!, TimeSpan.Zero);
            }

            var sw = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var res = await _engine.ConvertToDocxAsync(inputPdfPath, outputDocxPath, _options, progress, cancellationToken).ConfigureAwait(false);
                return MapToOfficeResult(res);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new PdfOfficeConversionResult
                {
                    IsSuccess = false,
                    IsCanceled = true,
                    Status = ConversionStatus.Canceled,
                    OutputPath = outputDocxPath,
                    Duration = sw.Elapsed,
                    ErrorCode = "CANCELED",
                    ErrorMessage = "Konversi PDF ke DOCX dibatalkan oleh pengguna."
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return CreateFailedOfficeResult(outputDocxPath, "DOCX_CONVERSION_ERROR", ex.Message, sw.Elapsed);
            }
        }

        public async Task<PdfOfficeConversionResult> ConvertToXlsxAsync(
            string inputPdfPath,
            string outputXlsxPath,
            bool multipleSheets = true,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!TryValidatePaths(inputPdfPath, outputXlsxPath, ".xlsx", out string? errCode, out string? errMsg))
            {
                return CreateFailedOfficeResult(outputXlsxPath, errCode!, errMsg!, TimeSpan.Zero);
            }

            var sw = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var res = await _engine.ConvertToXlsxAsync(inputPdfPath, outputXlsxPath, multipleSheets, _options, progress, cancellationToken).ConfigureAwait(false);
                return MapToOfficeResult(res);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new PdfOfficeConversionResult
                {
                    IsSuccess = false,
                    IsCanceled = true,
                    Status = ConversionStatus.Canceled,
                    OutputPath = outputXlsxPath,
                    Duration = sw.Elapsed,
                    ErrorCode = "CANCELED",
                    ErrorMessage = "Konversi PDF ke XLSX dibatalkan oleh pengguna."
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return CreateFailedOfficeResult(outputXlsxPath, "XLSX_CONVERSION_ERROR", ex.Message, sw.Elapsed);
            }
        }

        public async Task<PdfOfficeConversionResult> ConvertToPptxAsync(
            string inputPdfPath,
            string outputPptxPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!TryValidatePaths(inputPdfPath, outputPptxPath, ".pptx", out string? errCode, out string? errMsg))
            {
                return CreateFailedOfficeResult(outputPptxPath, errCode!, errMsg!, TimeSpan.Zero);
            }

            var sw = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var res = await _engine.ConvertToPptxAsync(inputPdfPath, outputPptxPath, _options, progress, cancellationToken).ConfigureAwait(false);
                return MapToOfficeResult(res);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new PdfOfficeConversionResult
                {
                    IsSuccess = false,
                    IsCanceled = true,
                    Status = ConversionStatus.Canceled,
                    OutputPath = outputPptxPath,
                    Duration = sw.Elapsed,
                    ErrorCode = "CANCELED",
                    ErrorMessage = "Konversi PDF ke PPTX dibatalkan oleh pengguna."
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return CreateFailedOfficeResult(outputPptxPath, "PPTX_CONVERSION_ERROR", ex.Message, sw.Elapsed);
            }
        }

        public async Task<PdfImageExtractionResult> ExtractImagesAsync(
            string inputPdfPath,
            string outputDirectory,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(inputPdfPath) || !File.Exists(inputPdfPath))
            {
                return new PdfImageExtractionResult
                {
                    IsSuccess = false,
                    Status = ConversionStatus.Failed,
                    OutputDirectory = outputDirectory ?? string.Empty,
                    ErrorCode = "INPUT_NOT_FOUND",
                    ErrorMessage = $"File PDF input tidak ditemukan: '{inputPdfPath}'."
                };
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                return new PdfImageExtractionResult
                {
                    IsSuccess = false,
                    Status = ConversionStatus.Failed,
                    OutputDirectory = string.Empty,
                    ErrorCode = "INVALID_OUTPUT_DIR",
                    ErrorMessage = "Direktori output ekstraksi gambar tidak boleh kosong."
                };
            }

            var sw = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var res = await _engine.ExtractImagesAsync(inputPdfPath, outputDirectory, _options, progress, cancellationToken).ConfigureAwait(false);

                // Hanya mengembalikan daftar file yang benar-benar dibuat pada job ini (terurut secara numerik)
                bool hasImages = res.CreatedFiles.Count > 0;
                return new PdfImageExtractionResult
                {
                    IsSuccess = res.IsSuccess,
                    IsCanceled = false,
                    Status = !res.IsSuccess ? ConversionStatus.Failed
                        : hasImages ? ConversionStatus.Success : ConversionStatus.NoContent,
                    OutputDirectory = outputDirectory,
                    PagesProcessed = res.PagesProcessed,
                    ExtractedFiles = res.CreatedFiles,
                    Duration = res.Elapsed,
                    ErrorCode = res.ErrorCode,
                    ErrorMessage = res.ErrorMessage,
                    Warnings = res.Warnings
                };
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new PdfImageExtractionResult
                {
                    IsSuccess = false,
                    IsCanceled = true,
                    Status = ConversionStatus.Canceled,
                    OutputDirectory = outputDirectory,
                    Duration = sw.Elapsed,
                    ErrorCode = "CANCELED",
                    ErrorMessage = "Ekstraksi gambar PDF dibatalkan oleh pengguna."
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new PdfImageExtractionResult
                {
                    IsSuccess = false,
                    Status = ConversionStatus.Failed,
                    OutputDirectory = outputDirectory,
                    Duration = sw.Elapsed,
                    ErrorCode = "IMAGE_EXTRACTION_ERROR",
                    ErrorMessage = ex.Message
                };
            }
        }

        private static bool TryValidatePaths(
            string inputPdfPath,
            string outputPath,
            string expectedExtension,
            out string? errorCode,
            out string? errorMessage)
        {
            errorCode = null;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(inputPdfPath) || !File.Exists(inputPdfPath))
            {
                errorCode = "INPUT_NOT_FOUND";
                errorMessage = $"File PDF input tidak ditemukan: '{inputPdfPath}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                errorCode = "INVALID_OUTPUT_PATH";
                errorMessage = "Path file output tidak boleh kosong.";
                return false;
            }

            string fullIn;
            string fullOut;
            try
            {
                fullIn = Path.GetFullPath(inputPdfPath);
                fullOut = Path.GetFullPath(outputPath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errorCode = "INVALID_OUTPUT_PATH";
                errorMessage = "Path file input atau output tidak valid.";
                return false;
            }

            if (string.Equals(fullIn, fullOut, StringComparison.OrdinalIgnoreCase))
            {
                errorCode = "INPUT_OUTPUT_COLLISION";
                errorMessage = "Path file input dan file output tidak boleh menunjuk ke file yang sama.";
                return false;
            }

            if (!fullOut.EndsWith(expectedExtension, StringComparison.OrdinalIgnoreCase))
            {
                errorCode = "INVALID_OUTPUT_EXTENSION";
                errorMessage = $"Ekstensi file output harus '{expectedExtension}'.";
                return false;
            }

            return true;
        }

        private static PdfOfficeConversionResult MapToOfficeResult(ConversionExecutionResult res)
        {
            return new PdfOfficeConversionResult
            {
                IsSuccess = res.IsSuccess,
                IsCanceled = false,
                Status = res.IsSuccess ? ConversionStatus.Success : ConversionStatus.Failed,
                OutputPath = res.OutputPath,
                PagesProcessed = res.PagesProcessed,
                TablesReconstructed = res.TablesReconstructed,
                ImagesProcessed = res.ImagesProcessed,
                OutputSizeBytes = res.OutputSizeBytes,
                Duration = res.Elapsed,
                ErrorCode = res.ErrorCode,
                ErrorMessage = res.ErrorMessage,
                Warnings = res.Warnings
            };
        }

        private static PdfOfficeConversionResult CreateFailedOfficeResult(
            string outputPath,
            string errorCode,
            string errorMessage,
            TimeSpan elapsed)
        {
            return new PdfOfficeConversionResult
            {
                IsSuccess = false,
                Status = ConversionStatus.Failed,
                OutputPath = outputPath ?? string.Empty,
                Duration = elapsed,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage
            };
        }
    }
}
