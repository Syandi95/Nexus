using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Nexus.Contracts;
using Nexus.Engine.ImageSubsystem.Pipelines;
using Nexus.Engine.ImageSubsystem.Services;

namespace Nexus.App.Services;

public interface IImageSubsystemWorkflow
{
    Task<string> RemoveBackgroundAsync(
        string inputPath,
        string modelReference,
        string outputPath,
        CancellationToken cancellationToken);

    Task<string> UpscaleAsync(
        string inputPath,
        int targetScale,
        string outputPath,
        CancellationToken cancellationToken);

    Task<string> InpaintAsync(
        string inputPath,
        Bitmap maskBitmap,
        InpaintExecutionTier tier,
        string outputPath,
        CancellationToken cancellationToken);

    bool ValidateModelPackage(string modelDirectory);

    Task GenerateMetadataAsync(string modelFilePath, string modelDirectory);
    Task GenerateMetadataAsync(string modelFilePath, string modelDirectory, AICapability? capability);
}

public sealed class ImageSubsystemWorkflow : IImageSubsystemWorkflow
{
    private readonly ImageApplicationModelSelectionPolicy _modelSelectionPolicy;
    private readonly RemoveBackgroundPipeline _removeBackgroundPipeline;
    private readonly UpscalePipeline _upscalePipeline;
    private readonly InpaintPipeline? _inpaintPipeline;
    private readonly IOnnxModelManager _modelPackageManager;

    public ImageSubsystemWorkflow(
        ImageApplicationModelSelectionPolicy modelSelectionPolicy,
        RemoveBackgroundPipeline removeBackgroundPipeline,
        UpscalePipeline upscalePipeline,
        IOnnxModelManager modelPackageManager,
        InpaintPipeline? inpaintPipeline = null)
    {
        _modelSelectionPolicy = modelSelectionPolicy
            ?? throw new ArgumentNullException(nameof(modelSelectionPolicy));
        _removeBackgroundPipeline = removeBackgroundPipeline
            ?? throw new ArgumentNullException(nameof(removeBackgroundPipeline));
        _upscalePipeline = upscalePipeline
            ?? throw new ArgumentNullException(nameof(upscalePipeline));
        _modelPackageManager = modelPackageManager
            ?? throw new ArgumentNullException(nameof(modelPackageManager));
        _inpaintPipeline = inpaintPipeline;
    }

    public async Task<string> RemoveBackgroundAsync(
        string inputPath,
        string modelReference,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        _modelSelectionPolicy.UseBackgroundRemovalModel(modelReference);

        var swLoad = System.Diagnostics.Stopwatch.StartNew();
        using var input = LoadBitmap(inputPath);
        swLoad.Stop();

        var swPipeline = System.Diagnostics.Stopwatch.StartNew();
        using var output = await _removeBackgroundPipeline
            .ExecuteAsync(input, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        swPipeline.Stop();

        cancellationToken.ThrowIfCancellationRequested();
        var swSave = System.Diagnostics.Stopwatch.StartNew();
        output.Save(outputPath, ImageFormat.Png);
        swSave.Stop();
        swTotal.Stop();

        System.Diagnostics.Trace.WriteLine(
            $"[PERF_AUDIT] Workflow: LoadImage={swLoad.ElapsedMilliseconds}ms " +
            $"Pipeline={swPipeline.ElapsedMilliseconds}ms SavePng={swSave.ElapsedMilliseconds}ms " +
            $"TotalWorkflow={swTotal.ElapsedMilliseconds}ms");

        return outputPath;
    }

    public async Task<string> UpscaleAsync(
        string inputPath,
        int targetScale,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (targetScale is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetScale),
                targetScale,
                "Application upscale target must be between 1x and 10x.");
        }

        using var input = LoadBitmap(inputPath);
        using var nativeOutput = await _upscalePipeline
            .ExecuteAsync(input, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (nativeOutput.Width == input.Width * targetScale &&
            nativeOutput.Height == input.Height * targetScale)
        {
            nativeOutput.Save(outputPath, ImageFormat.Png);
            return outputPath;
        }

        using var transformed = Resize(
            nativeOutput,
            checked(input.Width * targetScale),
            checked(input.Height * targetScale));
        transformed.Save(outputPath, ImageFormat.Png);
        return outputPath;
    }

    public async Task<string> InpaintAsync(
        string inputPath,
        Bitmap maskBitmap,
        InpaintExecutionTier tier,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (_inpaintPipeline == null)
        {
            throw new InvalidOperationException("InpaintPipeline is not configured.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentNullException.ThrowIfNull(maskBitmap);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        return await Task.Run(async () =>
        {
            using var input = LoadBitmap(inputPath);
            using var output = await _inpaintPipeline
                .ExecuteAsync(input, maskBitmap, tier, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var ext = Path.GetExtension(outputPath);
            var format = (string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase))
                ? ImageFormat.Jpeg
                : ImageFormat.Png;
            output.Save(outputPath, format);
            return outputPath;
        }, cancellationToken).ConfigureAwait(false);
    }

    public bool ValidateModelPackage(string modelDirectory) =>
        _modelPackageManager.ValidateModelPackage(modelDirectory);

    public Task GenerateMetadataAsync(string modelFilePath, string modelDirectory) =>
        _modelPackageManager.GenerateMetadataAsync(modelFilePath, modelDirectory, null);

    public Task GenerateMetadataAsync(string modelFilePath, string modelDirectory, AICapability? capability) =>
        _modelPackageManager.GenerateMetadataAsync(modelFilePath, modelDirectory, capability);

    private static Bitmap LoadBitmap(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var source = new Bitmap(path);
        return new Bitmap(source);
    }

    private static Bitmap Resize(Bitmap source, int width, int height)
    {
        var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, 0, 0, width, height);
        return output;
    }
}
