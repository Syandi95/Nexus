using System;
using System.IO;
using Xunit;
using Nexus.Foundation.Config;

namespace Nexus.Foundation.Tests
{
    [Collection("ConfigServiceTests")]
    public class ConfigServiceTests
    {
        private static readonly string ConfigPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nexus_data", "config.json");

        private static readonly string NexusDataDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nexus_data");

        public ConfigServiceTests()
        {
            CleanConfig();
        }

        private static void CleanConfig()
        {
            if (File.Exists(ConfigPath))
                File.Delete(ConfigPath);
            if (Directory.Exists(NexusDataDir) && !Directory.GetFiles(NexusDataDir).Any())
                Directory.Delete(NexusDataDir);
        }

        [Fact]
        public void LoadConfig_Fallback_WhenFileDoesNotExist()
        {
            var service = new ConfigService();
            var config = service.GetConfig();
            Assert.NotNull(config);
        }

        [Fact]
        public void SaveConfig_SavesSuccessfully()
        {
            var service = new ConfigService();
            var config = new AppConfigRecord();
            service.SaveConfig(config);

            Assert.True(File.Exists(ConfigPath));
        }

        [Fact]
        public void SaveLoad_Roundtrip_PreservesTheme()
        {
            var saved = new AppConfigRecord { Theme = "Light" };
            var service = new ConfigService();
            service.SaveConfig(saved);

            var reloaded = new ConfigService().GetConfig();
            Assert.Equal("Light", reloaded.Theme);
        }

        [Fact]
        public void SaveLoad_Roundtrip_PreservesAllSettings()
        {
            var saved = new AppConfigRecord
            {
                Theme = "Light",
                DownloadsFolder = @"C:\Users\Test\Downloads",
                ConfirmExit = false,
                ConfirmModule = true,
                DefaultCmykProfilePath = @"C:\Profiles\test.icc"
            };
            var service = new ConfigService();
            service.SaveConfig(saved);

            var reloaded = new ConfigService().GetConfig();

            Assert.Equal(saved.Theme, reloaded.Theme);
            Assert.Equal(saved.DownloadsFolder, reloaded.DownloadsFolder);
            Assert.Equal(saved.ConfirmExit, reloaded.ConfirmExit);
            Assert.Equal(saved.ConfirmModule, reloaded.ConfirmModule);
            Assert.Equal(saved.DefaultCmykProfilePath, reloaded.DefaultCmykProfilePath);
        }

        [Fact]
        public void SaveLoad_Roundtrip_PreservesDashboardFields()
        {
            var saved = new AppConfigRecord
            {
                LastActivityModule = "ImageTools",
                LastActivityFileCount = 5,
                LastActivityTotalSize = 1024000,
                LastActivityTimestamp = "2026-07-26 12:00:00",
                EngineStatus = "Running",
                EngineState = "Active",
                ActiveFolderPath = @"C:\Projects"
            };
            var service = new ConfigService();
            service.SaveConfig(saved);

            var reloaded = new ConfigService().GetConfig();

            Assert.Equal(saved.LastActivityModule, reloaded.LastActivityModule);
            Assert.Equal(saved.LastActivityFileCount, reloaded.LastActivityFileCount);
            Assert.Equal(saved.LastActivityTotalSize, reloaded.LastActivityTotalSize);
            Assert.Equal(saved.LastActivityTimestamp, reloaded.LastActivityTimestamp);
            Assert.Equal(saved.EngineStatus, reloaded.EngineStatus);
            Assert.Equal(saved.EngineState, reloaded.EngineState);
            Assert.Equal(saved.ActiveFolderPath, reloaded.ActiveFolderPath);
        }

        [Fact]
        public void GetConfig_ReturnsCachedInstance_NotFileRead()
        {
            CleanConfig();
            var service = new ConfigService();
            var first = service.GetConfig();
            var second = service.GetConfig();
            Assert.Same(first, second);
        }

        [Fact]
        public void AlternatingSaves_NoThemeDrift()
        {
            CleanConfig();
            var service = new ConfigService();

            service.SaveConfig(new AppConfigRecord { Theme = "Light" });
            Assert.Equal("Light", service.GetConfig().Theme);

            service.SaveConfig(new AppConfigRecord { Theme = "Dark" });
            Assert.Equal("Dark", service.GetConfig().Theme);

            service.SaveConfig(new AppConfigRecord { Theme = "Light" });
            Assert.Equal("Light", service.GetConfig().Theme);
        }

        [Fact]
        public void Restart_ReadsSavedTheme()
        {
            CleanConfig();
            var saved = new AppConfigRecord { Theme = "Light" };
            new ConfigService().SaveConfig(saved);

            var restarted = new ConfigService();
            Assert.Equal("Light", restarted.GetConfig().Theme);
        }

        [Fact]
        public void SaveLoad_Roundtrip_PreservesDefaultValues()
        {
            var saved = new AppConfigRecord();
            var service = new ConfigService();
            service.SaveConfig(saved);

            var reloaded = new ConfigService().GetConfig();

            Assert.Equal("Dark", reloaded.Theme);
            Assert.Equal(string.Empty, reloaded.DownloadsFolder);
            Assert.True(reloaded.ConfirmExit);
            Assert.True(reloaded.ConfirmModule);
            Assert.Equal(string.Empty, reloaded.DefaultCmykProfilePath);
            Assert.Equal(string.Empty, reloaded.LastActivityModule);
            Assert.Equal(0, reloaded.LastActivityFileCount);
            Assert.Equal(0L, reloaded.LastActivityTotalSize);
            Assert.Equal(string.Empty, reloaded.LastActivityTimestamp);
            Assert.Equal("Standby", reloaded.EngineStatus);
            Assert.Equal("READY", reloaded.EngineState);
            Assert.Equal("Downloads", reloaded.ActiveFolderPath);
        }

        [Fact]
        public void LoadConfig_WithTrailingNullBytes_DeserializesSuccessfully()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"nexus_test_null_{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(tempFile, "{\"Theme\":\"Light\"}\0\0\0\0");
                var service = new ConfigService(tempFile);
                var config = service.GetConfig();

                Assert.Equal("Light", config.Theme);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Fact]
        public void LoadConfig_CorruptedJson_ReturnsDefaultAndPreservesFile()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"nexus_test_corrupt_{Guid.NewGuid():N}.json");
            const string corruptedContent = "{\"Theme\": \"Light\", corrupted_json_without_closing_bracket";
            try
            {
                File.WriteAllText(tempFile, corruptedContent);
                var service = new ConfigService(tempFile);
                var config = service.GetConfig();

                Assert.Equal("Dark", config.Theme);
                Assert.True(File.Exists(tempFile));
                Assert.Equal(corruptedContent, File.ReadAllText(tempFile));
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Fact]
        public void LoadConfig_EmptyOrWhitespace_ReturnsDefaultConfig()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"nexus_test_empty_{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(tempFile, "   \r\n\t   ");
                var service = new ConfigService(tempFile);
                var config = service.GetConfig();

                Assert.Equal("Dark", config.Theme);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Fact]
        public void SaveConfig_WriteError_LogsWithoutThrowing()
        {
            // Create a file and then treat it as a directory to force an IOException on Directory.CreateDirectory or File.WriteAllText
            var blockerFile = Path.Combine(Path.GetTempPath(), $"nexus_blocker_{Guid.NewGuid():N}");
            File.WriteAllText(blockerFile, "blocker");
            var invalidPath = Path.Combine(blockerFile, "config.json");

            try
            {
                var service = new ConfigService(invalidPath);
                var ex = Record.Exception(() => service.SaveConfig(new AppConfigRecord { Theme = "Light" }));
                Assert.Null(ex);
            }
            finally
            {
                if (File.Exists(blockerFile))
                {
                    File.Delete(blockerFile);
                }
            }
        }
    }
}
