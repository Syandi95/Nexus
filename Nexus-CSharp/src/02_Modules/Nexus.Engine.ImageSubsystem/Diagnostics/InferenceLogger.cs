using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Nexus.Engine.ImageSubsystem.Diagnostics
{
    public class InferenceLogger : IDisposable
    {
        private readonly string _lifecycleLogPath;
        private readonly string _failureLogPath;
        private readonly long _maxFileSize;
        private readonly ReaderWriterLockSlim _lock = new();

        public InferenceLogger(string logDirectory, long maxFileSize = 10L * 1024 * 1024)
        {
            Directory.CreateDirectory(logDirectory);
            _lifecycleLogPath = Path.Combine(logDirectory, "inference.log");
            _failureLogPath = Path.Combine(logDirectory, "inference_failure.log");
            _maxFileSize = maxFileSize;
        }

        public void LogLifecycle(string requestId, string eventType, string message,
            string modelId = "", string sessionId = "", string executionProvider = "",
            long elapsedMs = 0, int? threadId = null)
        {
            SafeWrite(_lifecycleLogPath, FormatLifecycle(requestId, eventType, message,
                modelId, sessionId, executionProvider, elapsedMs, threadId));
        }

        public void LogFailure(InferenceException ex, string fullDiagnostic)
        {
            SafeWrite(_failureLogPath, FormatFailure(ex, fullDiagnostic));
        }

        public void LogRawFailure(string diagnostic)
        {
            SafeWrite(_failureLogPath, FormatTimestamp() + " " + diagnostic);
        }

        private string FormatLifecycle(string requestId, string eventType, string message,
            string modelId, string sessionId, string executionProvider,
            long elapsedMs, int? threadId)
        {
            var tid = threadId ?? Environment.CurrentManagedThreadId;
            return $"[{FormatTimestamp()}] [{requestId}] [{eventType,-25}] +{elapsedMs,6}ms " +
                   $"Thread={tid} {message}" +
                   (string.IsNullOrEmpty(modelId) ? "" : $" Model={modelId}") +
                   (string.IsNullOrEmpty(sessionId) ? "" : $" Session={sessionId}") +
                   (string.IsNullOrEmpty(executionProvider) ? "" : $" Provider={executionProvider}");
        }

        private string FormatFailure(InferenceException ex, string fullDiagnostic)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== INFERENCE FAILURE ===");
            sb.AppendLine($"Timestamp: {FormatTimestamp()}");
            sb.AppendLine($"ErrorCode: {ex.ErrorCode}");
            sb.AppendLine($"RequestId: {ex.RequestId}");
            sb.AppendLine($"ModelId: {ex.ModelId}");
            sb.AppendLine($"SessionId: {ex.SessionId}");
            sb.AppendLine($"IsRecoverable: {ex.IsRecoverable}");
            sb.AppendLine($"UserMessage: {ex.UserMessage}");
            sb.AppendLine($"TechnicalMessage: {ex.Message}");
            sb.AppendLine();
            sb.AppendLine("--- Original Exception Chain ---");

            var inner = ex.InnerException;
            int level = 0;
            while (inner != null)
            {
                sb.AppendLine($"Level {level}: {inner.GetType().FullName}");
                sb.AppendLine($"  Message: {inner.Message}");
                sb.AppendLine($"  StackTrace: {inner.StackTrace}");
                inner = inner.InnerException;
                level++;
            }

            sb.AppendLine();
            sb.AppendLine("--- Full Diagnostic ---");
            sb.AppendLine(fullDiagnostic);
            sb.AppendLine("================================");
            sb.AppendLine();

            return sb.ToString();
        }

        private void SafeWrite(string path, string content)
        {
            try
            {
                _lock.EnterWriteLock();
                try
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > _maxFileSize)
                    {
                        var backup = path + ".old";
                        try { File.Delete(backup); } catch { }
                        try { File.Move(path, backup); } catch { }
                    }

                    using var writer = new StreamWriter(path, append: true);
                    writer.WriteLine(content);
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
            catch
            {
            }
        }

        private static string FormatTimestamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }

        public void Dispose()
        {
            _lock.Dispose();
        }
    }
}
