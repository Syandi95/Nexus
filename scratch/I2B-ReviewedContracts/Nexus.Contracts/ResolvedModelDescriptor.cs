using System;
using System.Collections.Generic;

namespace Nexus.Contracts;

/// <summary>
/// DTO deskriptor model fisik yang dikembalikan oleh Model Registry
/// setelah menerjemahkan kapabilitas abstrak (AICapability).
/// Didesain immutable (init-only) untuk menjamin keamanan antar-thread.
/// Consumer wajib memperlakukannya sebagai immutable setelah registrasi.
/// </summary>
public class ResolvedModelDescriptor
{
    public string ModelId { get; init; } = string.Empty;
    public string ModelPath { get; init; } = string.Empty;
    public ModelType ModelType { get; init; } = ModelType.Onnx;
    public string? DisplayName { get; init; }
    public string? InputTensorName { get; init; }
    public string? OutputTensorName { get; init; }
    public long[]? InputShape { get; init; }
    public long[]? OutputShape { get; init; }
    public string TensorType { get; init; } = "float32";
    public string? Sha256 { get; init; }

    /// <summary>
    /// Kamus tensor input tambahan untuk model multi-input (misal: inpainting yang membutuhkan image + mask).
    /// Key: nama tensor input, Value: dimensi shape tensor.
    /// </summary>
    public IReadOnlyDictionary<string, long[]>? AdditionalInputs { get; init; }
}
