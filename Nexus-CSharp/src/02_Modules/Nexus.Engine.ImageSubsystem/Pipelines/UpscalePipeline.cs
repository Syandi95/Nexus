using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Nexus.Contracts;
using Nexus.Foundation.Registry;

namespace Nexus.Engine.ImageSubsystem.Pipelines;

/// <summary>
/// Pipeline headless untuk inferensi upscale RGB pada resolusi native model.
/// </summary>
public sealed class UpscalePipeline
{
    private readonly IModelSelectionPolicy _modelSelectionPolicy;
    private readonly IGenericImageEngine _imageEngine;

    public UpscalePipeline(
        IModelSelectionPolicy modelSelectionPolicy,
        IGenericImageEngine imageEngine)
    {
        _modelSelectionPolicy = modelSelectionPolicy
            ?? throw new ArgumentNullException(nameof(modelSelectionPolicy));
        _imageEngine = imageEngine
            ?? throw new ArgumentNullException(nameof(imageEngine));
    }

    /// <summary>
    /// Menghasilkan bitmap upscale baru. Bitmap input tetap dimiliki caller.
    /// </summary>
    public async Task<Bitmap> ExecuteAsync(
        Bitmap input,
        EngineSessionOptions? sessionOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var jobScope = new PipelineJobScope(cancellationToken);
        var jobToken = jobScope.CancellationToken;
        jobToken.ThrowIfCancellationRequested();

        var options = sessionOptions ?? new EngineSessionOptions();
        var descriptor = _modelSelectionPolicy.SelectModel(
            AICapability.UpscaleGeneral,
            options);
        ValidateDescriptor(descriptor);

        var tensor = await Task.Run(
            () => CreateRgbNchw(input),
            jobToken).ConfigureAwait(false);
        var sessionKey = await _imageEngine
            .LoadModelAsync(descriptor, options)
            .ConfigureAwait(false);
        try
        {
            jobToken.ThrowIfCancellationRequested();

            var outputs = await _imageEngine
                .ExecuteInferenceAsync(
                    sessionKey,
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        [descriptor.InputTensorName!] = tensor
                    })
                .ConfigureAwait(false);
            jobToken.ThrowIfCancellationRequested();

            if (!outputs.TryGetValue(descriptor.OutputTensorName!, out var output) ||
                output is not float[,,,] imageTensor)
            {
                throw new InvalidDataException(
                    $"Output tensor '{descriptor.OutputTensorName}' was not a float32 NCHW tensor.");
            }

            return await Task.Run(
                () => CreateBitmap(imageTensor),
                jobToken).ConfigureAwait(false);
        }
        finally
        {
            await _imageEngine
                .UnloadModelAsync(sessionKey)
                .ConfigureAwait(false);
        }
    }

    private static void ValidateDescriptor(ResolvedModelDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.ModelType != ModelType.Onnx)
        {
            throw new InvalidDataException(
                "Upscale requires an ONNX model descriptor.");
        }

        if (!string.Equals(descriptor.TensorType, "float32", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Tensor type '{descriptor.TensorType}' is not supported.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.InputTensorName) ||
            string.IsNullOrWhiteSpace(descriptor.OutputTensorName))
        {
            throw new InvalidDataException(
                "The model descriptor must define input and output tensor names.");
        }

        if (descriptor.InputShape is not { Length: 4 } inputShape ||
            inputShape[1] != 3 ||
            inputShape.Any(dimension => dimension == 0 || dimension < -1))
        {
            throw new InvalidDataException(
                "Upscale requires an NCHW input shape [N,3,H,W] with positive or dynamic dimensions.");
        }
    }

    private static float[,,,] CreateRgbNchw(Bitmap input)
    {
        var width = input.Width;
        var height = input.Height;
        using var rgb = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(rgb))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(input, 0, 0);
        }

        var tensor = new float[1, 3, height, width];
        var rectangle = new Rectangle(0, 0, width, height);
        var data = rgb.LockBits(
            rectangle,
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var pixels = new byte[stride * height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            for (var y = 0; y < height; y++)
            {
                var row = data.Stride >= 0
                    ? y * stride
                    : (height - 1 - y) * stride;
                for (var x = 0; x < width; x++)
                {
                    var pixel = row + (x * 3);
                    tensor[0, 0, y, x] = pixels[pixel + 2] / 255f;
                    tensor[0, 1, y, x] = pixels[pixel + 1] / 255f;
                    tensor[0, 2, y, x] = pixels[pixel] / 255f;
                }
            }
        }
        finally
        {
            rgb.UnlockBits(data);
        }

        return tensor;
    }

    private static Bitmap CreateBitmap(float[,,,] tensor)
    {
        if (tensor.GetLength(0) != 1 ||
            tensor.GetLength(1) != 3 ||
            tensor.GetLength(2) <= 0 ||
            tensor.GetLength(3) <= 0)
        {
            throw new InvalidDataException(
                "Upscale requires an NCHW output shape [1,3,H,W].");
        }

        var height = tensor.GetLength(2);
        var width = tensor.GetLength(3);
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            var rectangle = new Rectangle(0, 0, width, height);
            var data = result.LockBits(
                rectangle,
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                var stride = Math.Abs(data.Stride);
                var pixels = new byte[stride * height];
                for (var y = 0; y < height; y++)
                {
                    var row = data.Stride >= 0
                        ? y * stride
                        : (height - 1 - y) * stride;
                    for (var x = 0; x < width; x++)
                    {
                        var pixel = row + (x * 4);
                        pixels[pixel] = ToByte(tensor[0, 2, y, x]);
                        pixels[pixel + 1] = ToByte(tensor[0, 1, y, x]);
                        pixels[pixel + 2] = ToByte(tensor[0, 0, y, x]);
                        pixels[pixel + 3] = 255;
                    }
                }

                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally
            {
                result.UnlockBits(data);
            }

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static byte ToByte(float value)
    {
        if (float.IsNaN(value))
        {
            return 0;
        }

        return (byte)Math.Clamp(
            (int)Math.Round(Math.Clamp(value, 0f, 1f) * 255f),
            0,
            255);
    }
}
