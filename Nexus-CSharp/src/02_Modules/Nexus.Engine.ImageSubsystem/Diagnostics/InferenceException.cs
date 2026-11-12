using System;
using Microsoft.ML.OnnxRuntime;

namespace Nexus.Engine.ImageSubsystem.Diagnostics
{
    public enum InferenceErrorCode
    {
        ONNX_RUNTIME_FAILURE,
        OUT_OF_MEMORY,
        MODEL_OR_FILE_ACCESS_FAILURE,
        OPERATION_CANCELLED,
        INVALID_TENSOR_NAME,
        INVALID_TENSOR_TYPE,
        INVALID_TENSOR_RANK,
        INVALID_TENSOR_SHAPE,
        INVALID_TENSOR_LENGTH,
        INVALID_TENSOR_RANGE,
        TENSOR_CONTAINS_NAN,
        TENSOR_CONTAINS_INFINITY,
        INVALID_SESSION,
        SESSION_DISPOSED,
        SESSION_MODEL_MISMATCH,
        MODEL_ACCESS_FAILURE,
        POSTPROCESS_FAILURE,
        GPU_MEMORY_EXHAUSTED,
        BUSY,
        UNKNOWN_RUNTIME_FAILURE
    }

    public class InferenceException : Exception
    {
        public InferenceErrorCode ErrorCode { get; }
        public string UserMessage { get; }
        public string RequestId { get; }
        public string ModelId { get; }
        public string SessionId { get; }
        public bool IsRecoverable { get; }

        public InferenceException(
            InferenceErrorCode errorCode,
            string userMessage,
            string technicalMessage,
            string requestId,
            string modelId = "",
            string sessionId = "",
            bool isRecoverable = false,
            Exception? innerException = null)
            : base(technicalMessage, innerException)
        {
            ErrorCode = errorCode;
            UserMessage = userMessage;
            RequestId = requestId;
            ModelId = modelId;
            SessionId = sessionId;
            IsRecoverable = isRecoverable;
        }

        public static InferenceException FromOnnxRuntime(
            OnnxRuntimeException onnxEx,
            string requestId,
            string modelId = "",
            string sessionId = "")
        {
            var msg = onnxEx.Message;
            var userMsg = "Model gagal dijalankan. Silakan coba ulang.";

            if (msg.Contains("InvalidGraph", StringComparison.Ordinal) ||
                msg.Contains("graph", StringComparison.OrdinalIgnoreCase))
                userMsg = "Model tidak valid. Silakan pilih model lain.";
            else if (msg.Contains("InvalidArgument", StringComparison.Ordinal) ||
                     msg.Contains("shape", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("dimension", StringComparison.OrdinalIgnoreCase))
                userMsg = "Data input tidak cocok dengan model. Silakan coba gambar lain.";

            return new InferenceException(
                InferenceErrorCode.ONNX_RUNTIME_FAILURE,
                userMsg,
                $"OnnxRuntime: {msg}",
                requestId, modelId, sessionId,
                isRecoverable: false,
                innerException: onnxEx);
        }

        public static InferenceException FromTensorError(
            InferenceErrorCode code,
            string details,
            string requestId,
            string modelId = "",
            string sessionId = "")
        {
            var userMsg = code switch
            {
                InferenceErrorCode.TENSOR_CONTAINS_NAN or InferenceErrorCode.TENSOR_CONTAINS_INFINITY
                    => "Data gambar mengandung nilai tidak valid.",
                InferenceErrorCode.INVALID_TENSOR_SHAPE or InferenceErrorCode.INVALID_TENSOR_RANK
                    => "Ukuran gambar tidak sesuai dengan model.",
                InferenceErrorCode.INVALID_TENSOR_NAME => "Nama input model tidak dikenal.",
                InferenceErrorCode.INVALID_TENSOR_TYPE => "Tipe data tidak sesuai.",
                _ => "Data input tidak valid."
            };

            return new InferenceException(
                code, userMsg, details,
                requestId, modelId, sessionId,
                isRecoverable: code is InferenceErrorCode.TENSOR_CONTAINS_NAN or InferenceErrorCode.TENSOR_CONTAINS_INFINITY,
                innerException: null);
        }
    }
}
