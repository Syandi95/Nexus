using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime.Tensors;
using Nexus.Engine.ImageSubsystem.Diagnostics;
using Nexus.Engine.ImageSubsystem.Services;
using Xunit;

namespace Nexus.Engine.ImageSubsystem.Tests
{
    public class InferenceDiagnosticContractTests
    {
        [Fact]
        public void InferenceException_PreservesInnerExceptionChain()
        {
            var inner = new InvalidOperationException("root cause");
            var ex = new InferenceException(
                InferenceErrorCode.ONNX_RUNTIME_FAILURE,
                "User-friendly message",
                "Technical details: root cause",
                "INF-ABCDEF",
                innerException: inner);

            Assert.Equal(InferenceErrorCode.ONNX_RUNTIME_FAILURE, ex.ErrorCode);
            Assert.Equal("User-friendly message", ex.UserMessage);
            Assert.Equal("INF-ABCDEF", ex.RequestId);
            Assert.Same(inner, ex.InnerException);
            Assert.False(ex.IsRecoverable);
        }

        [Fact]
        public void InferenceException_FromTensorError_SetsCorrectProperties()
        {
            var ex = InferenceException.FromTensorError(
                InferenceErrorCode.TENSOR_CONTAINS_NAN,
                "Tensor contains NaN values",
                "INF-999999", "model-v2", "SES-1234");

            Assert.Equal(InferenceErrorCode.TENSOR_CONTAINS_NAN, ex.ErrorCode);
            Assert.True(ex.IsRecoverable);
            Assert.Equal("INF-999999", ex.RequestId);
            Assert.Equal("model-v2", ex.ModelId);
            Assert.Equal("SES-1234", ex.SessionId);
            Assert.Null(ex.InnerException);
        }

        [Fact]
        public void TensorValidator_DetectsNaN()
        {
            var tensor = new DenseTensor<float>(new[] { 1.0f, float.NaN, 3.0f, 4.0f }, new[] { 1, 1, 2, 2 });
            var result = TensorValidator.Validate(tensor, "input", new[] { 1, 1, 2, 2 }, "default");

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e == InferenceErrorCode.TENSOR_CONTAINS_NAN);
            Assert.Equal(1, result.Snapshot.NanCount);
        }

        [Fact]
        public void TensorValidator_DetectsInfinity()
        {
            var tensor = new DenseTensor<float>(new[] { 1.0f, float.PositiveInfinity, 3.0f }, new[] { 1, 1, 1, 3 });
            var result = TensorValidator.Validate(tensor, "input", new[] { 1, 1, 1, 3 }, "default");

            Assert.False(result.IsValid);
            Assert.Contains(InferenceErrorCode.TENSOR_CONTAINS_INFINITY, result.Errors);
            Assert.Equal(1, result.Snapshot.InfinityCount);
        }

        [Fact]
        public void TensorValidator_DetectsInvalidShape()
        {
            var tensor = new DenseTensor<float>(new float[1 * 3 * 4 * 4], new[] { 1, 3, 4, 4 });
            var result = TensorValidator.Validate(tensor, "input", new[] { 1, 3, 1024, 1024 }, "default");

            Assert.False(result.IsValid);
            Assert.Contains(InferenceErrorCode.INVALID_TENSOR_SHAPE, result.Errors);
        }

        [Fact]
        public void TensorValidator_PassesValidTensor()
        {
            var tensor = new DenseTensor<float>(new float[1 * 3 * 224 * 224], new[] { 1, 3, 224, 224 });
            var result = TensorValidator.Validate(tensor, "input", new[] { 1, 3, 224, 224 }, "imagenet",
                originalWidth: 800, originalHeight: 600, resizedWidth: 224, resizedHeight: 224);

            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
            Assert.Equal(224, result.Snapshot.ResizedWidth);
            Assert.Equal(224, result.Snapshot.ResizedHeight);
            Assert.Equal(800, result.Snapshot.OriginalWidth);
            Assert.Equal(600, result.Snapshot.OriginalHeight);
        }

        [Fact]
        public void TensorValidator_DetectsInvalidRank()
        {
            var tensor = new DenseTensor<float>(new float[12], new[] { 12 });
            var result = TensorValidator.Validate(tensor, "input", new[] { 1, 3, 224, 224 }, "default");

            Assert.False(result.IsValid);
            Assert.Contains(InferenceErrorCode.INVALID_TENSOR_RANK, result.Errors);
        }

        [Fact]
        public async Task RemoveBackgroundService_ThrowsInferenceException_WithRequestId()
        {
            using var pipeline = new ImagePipelineService();
            using var modelManager = new OnnxModelManager();
            using var service = new RemoveBackgroundService();

            var tempDir = Path.Combine(Path.GetTempPath(), "DiagTest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var inputPath = Path.Combine(tempDir, "test.png");
                var outPath = Path.Combine(tempDir, "out.png");
                File.WriteAllText(inputPath, "not-a-real-image");

                var ex = await Assert.ThrowsAsync<InferenceException>(() =>
                    service.ExecuteInferenceAsync(pipeline, modelManager, inputPath, tempDir, outPath));
                Assert.False(string.IsNullOrEmpty(ex.RequestId));
                Assert.StartsWith("INF-", ex.RequestId);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }
    }
}
