using System;
using System.IO;

namespace Nexus.Foundation.Logging
{
    public class CrashLoggerService : ILoggerService
    {
        private readonly string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diagnostic_crash.log");
        private readonly object _lockObject = new object();

        public void LogInfo(string category, string message)
        {
            Log("INFO", category, message);
        }

        public void LogWarning(string category, string message)
        {
            Log("WARNING", category, message);
        }

        public void LogError(string category, string message)
        {
            Log("ERROR", category, message);
        }

        private void Log(string level, string category, string message)
        {
            string formattedMessage = $"[{DateTime.UtcNow:O}] [{level}] [{category}] {message}";
            
            // Console output
            Console.WriteLine(formattedMessage);

            // File output
            lock (_lockObject)
            {
                try
                {
                    File.AppendAllText(_logFilePath, formattedMessage + Environment.NewLine);
                }
                catch
                {
                    // Ignore file write errors for crash logger
                }
            }
        }
    }
}
