using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Nexus.Engine.ImageSubsystem.Diagnostics
{
    public class TensorValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public List<InferenceErrorCode> Errors { get; } = new();
        public List<string> Messages { get; } = new();
        public TensorSnapshot Snapshot { get; }

        public TensorValidationResult(TensorSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public void AddError(InferenceErrorCode code, string message)
        {
            Errors.Add(code);
            Messages.Add(message);
        }
    }

    public class TensorSnapshot
    {
        public string InputName { get; set; } = "";
        public string DType { get; set; } = "";
        public int Rank { get; set; }
        public int[] Shape { get; set; } = Array.Empty<int>();
        public int ElementCount { get; set; }
        public int ExpectedElementCount { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Mean { get; set; }
        public int NanCount { get; set; }
        public int InfinityCount { get; set; }
        public string NormalizationProfile { get; set; } = "";
        public int OriginalWidth { get; set; }
        public int OriginalHeight { get; set; }
        public int ResizedWidth { get; set; }
        public int ResizedHeight { get; set; }

        public string ToDiagnosticString()
        {
            return $"InputName={InputName}, DType={DType}, Rank={Rank}, Shape=[{string.Join(",", Shape)}], " +
                   $"ElementCount={ElementCount}, ExpectedCount={ExpectedElementCount}, " +
                   $"Min={Min:G4}, Max={Max:G4}, Mean={Mean:G4}, " +
                   $"NaN={NanCount}, Inf={InfinityCount}, " +
                   $"Orig={OriginalWidth}x{OriginalHeight}, Resized={ResizedWidth}x{ResizedHeight}, " +
                   $"Norm={NormalizationProfile}";
        }
    }

    public static class TensorValidator
    {
        public static InferenceException CreateTensorException(InferenceErrorCode code, string message,
            string requestId, string modelId, string sessionId)
        {
            return InferenceException.FromTensorError(code, message, requestId, modelId, sessionId);
        }
        public static TensorValidationResult Validate(DenseTensor<float> tensor, string inputName,
            int[] expectedShape, string normalizationProfile,
            int originalWidth = 0, int originalHeight = 0,
            int resizedWidth = 0, int resizedHeight = 0)
        {
            var dims = tensor.Dimensions.ToArray();
            var snapshot = new TensorSnapshot
            {
                InputName = inputName,
                DType = "float32",
                Rank = dims.Length,
                Shape = dims,
                ElementCount = (int)tensor.Length,
                ExpectedElementCount = expectedShape.Aggregate(1, (a, b) => a * b),
                OriginalWidth = originalWidth,
                OriginalHeight = originalHeight,
                ResizedWidth = resizedWidth,
                ResizedHeight = resizedHeight,
                NormalizationProfile = normalizationProfile
            };

            var result = new TensorValidationResult(snapshot);

            if (inputName != snapshot.InputName)
            {
                result.AddError(InferenceErrorCode.INVALID_TENSOR_NAME,
                    $"Expected input name '{inputName}', tensor has no matching name");
            }

            if (dims.Length != expectedShape.Length)
            {
                result.AddError(InferenceErrorCode.INVALID_TENSOR_RANK,
                    $"Tensor rank {dims.Length} != expected {expectedShape.Length}");
                return result;
            }

            for (int i = 0; i < dims.Length; i++)
            {
                if (expectedShape[i] > 0 && dims[i] != expectedShape[i])
                {
                    result.AddError(InferenceErrorCode.INVALID_TENSOR_SHAPE,
                        $"Dimension {i}: {dims[i]} != expected {expectedShape[i]}");
                }
            }

            if (expectedShape.All(d => d > 0) && tensor.Length != snapshot.ExpectedElementCount)
            {
                result.AddError(InferenceErrorCode.INVALID_TENSOR_LENGTH,
                    $"Element count {tensor.Length} != expected {snapshot.ExpectedElementCount}");
            }

            int nanCount = 0, infCount = 0;
            float min = float.MaxValue, max = float.MinValue;
            double sum = 0;

            var span = tensor.Buffer.Span;
            for (int i = 0; i < span.Length; i++)
            {
                var val = span[i];
                if (float.IsNaN(val)) nanCount++;
                else if (float.IsInfinity(val)) infCount++;
                else
                {
                    if (val < min) min = val;
                    if (val > max) max = val;
                    sum += val;
                }
            }

            snapshot.NanCount = nanCount;
            snapshot.InfinityCount = infCount;
            snapshot.Min = min;
            snapshot.Max = max;
            snapshot.Mean = span.Length > 0 ? sum / (span.Length - nanCount - infCount) : 0;

            if (nanCount > 0)
                result.AddError(InferenceErrorCode.TENSOR_CONTAINS_NAN,
                    $"Tensor contains {nanCount} NaN values");

            if (infCount > 0)
                result.AddError(InferenceErrorCode.TENSOR_CONTAINS_INFINITY,
                    $"Tensor contains {infCount} Infinity values");

            return result;
        }
    }
}
