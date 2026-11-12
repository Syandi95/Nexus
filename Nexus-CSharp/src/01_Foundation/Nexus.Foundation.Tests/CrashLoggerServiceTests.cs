using System;
using System.IO;
using Xunit;
using Nexus.Foundation.Logging;

namespace Nexus.Foundation.Tests
{
    public class CrashLoggerServiceTests
    {
        [Fact]
        public void LogInfo_WritesToFile()
        {
            var logPath = "diagnostic_crash.log";
            if (File.Exists(logPath)) File.Delete(logPath);

            var logger = new CrashLoggerService();
            logger.LogInfo("TestCategory", "TestMessage");

            Assert.True(File.Exists(logPath));
            var content = File.ReadAllText(logPath);
            Assert.Contains("[INFO] [TestCategory] TestMessage", content);

            // Clean up
            if (File.Exists(logPath)) File.Delete(logPath);
        }
        
        [Fact]
        public void LogError_WritesToFile()
        {
            var logPath = "diagnostic_crash.log";
            if (File.Exists(logPath)) File.Delete(logPath);

            var logger = new CrashLoggerService();
            logger.LogError("TestCategory", "ErrorMessage");

            Assert.True(File.Exists(logPath));
            var content = File.ReadAllText(logPath);
            Assert.Contains("[ERROR] [TestCategory] ErrorMessage", content);

            // Clean up
            if (File.Exists(logPath)) File.Delete(logPath);
        }
    }
}
