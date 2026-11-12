namespace Nexus.Contracts;

/// <summary>
/// Menerjemahkan kapabilitas AI abstrak yang diminta oleh Pipeline Layer,
/// tanpa memikat Pipeline ke file fisik model ONNX, GGUF, atau engine spesifik.
/// </summary>
public enum AICapability
{
    BackgroundRemovalGeneral,
    BackgroundRemovalFast,
    UpscaleGeneral,
    InpaintingGeneral,
    SegmentAnythingEncoder,
    SegmentAnythingDecoder,
    ChatCompletion,
    TextGeneration
}
