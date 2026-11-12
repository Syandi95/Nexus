using System;
using System.IO;
using System.Threading.Tasks;
using Nexus.Foundation.Config;

namespace Nexus.Core.HomeSettings.Services
{
    public class SettingsManagerService : ISettingsManagerService
    {
        private const string DefaultModelRootFolder = @".\Models";
        private const string DefaultEngineRootFolder = @".\Engine";
        private readonly IConfigService _configService;

        public SettingsManagerService(IConfigService configService)
        {
            _configService = configService;
        }

        public string GetTheme()
        {
            return _configService.GetConfig().Theme;
        }

        public string GetDownloadsPath()
        {
            return _configService.GetConfig().DownloadsFolder;
        }

        public string ModelRootFolder
        {
            get
            {
                var value = _configService.GetConfig().ModelRootFolder;
                if (string.IsNullOrWhiteSpace(value))
                {
                    value = DefaultModelRootFolder;
                }

                return ResolveModelDirectory(value);
            }
            set
            {
                var config = _configService.GetConfig();
                config.ModelRootFolder = value;
                _configService.SaveConfig(config);
            }
        }

        public string EngineRootFolder
        {
            get
            {
                var value = _configService.GetConfig().EngineRootFolder;
                if (string.IsNullOrWhiteSpace(value))
                {
                    value = DefaultEngineRootFolder;
                }

                return ResolveEngineDirectory(value);
            }
            set
            {
                var config = _configService.GetConfig();
                config.EngineRootFolder = value;
                _configService.SaveConfig(config);
            }
        }

        private static string ResolveEngineDirectory(string configuredValue)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 1. Check if configured directory exists and contains the converter engine
            if (!string.IsNullOrWhiteSpace(configuredValue))
            {
                string configuredFull = Path.GetFullPath(configuredValue, baseDir);
                if (Directory.Exists(configuredFull) &&
                    File.Exists(Path.Combine(configuredFull, "Convert from PDF", "NexusConverterEngine.dll")))
                {
                    return configuredFull;
                }
            }

            // 2. Fallback to <AppBaseDir>\Engine if it contains the converter engine
            string appBaseEngine = Path.Combine(baseDir, "Engine");
            if (Directory.Exists(appBaseEngine) &&
                File.Exists(Path.Combine(appBaseEngine, "Convert from PDF", "NexusConverterEngine.dll")))
            {
                return appBaseEngine;
            }

            // 3. Fallback: if running inside a build folder (bin\Debug or bin\Release), walk up parent directory looking for <NexusRoot>\Engine
            if (baseDir.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var dir = new DirectoryInfo(baseDir);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "Engine");
                    if (Directory.Exists(candidate) &&
                        File.Exists(Path.Combine(candidate, "Convert from PDF", "NexusConverterEngine.dll")))
                    {
                        return candidate;
                    }
                    dir = dir.Parent;
                }
            }

            // Default fallback relative to baseDir
            string fallback = string.IsNullOrWhiteSpace(configuredValue) ? DefaultEngineRootFolder : configuredValue;
            return Path.GetFullPath(fallback, baseDir);
        }

        private static string ResolveModelDirectory(string configuredValue)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 1. Check if configured directory exists
            if (!string.IsNullOrWhiteSpace(configuredValue))
            {
                string configuredFull = Path.GetFullPath(configuredValue, baseDir);
                if (Directory.Exists(configuredFull))
                {
                    return configuredFull;
                }
            }

            // 2. Fallback to <AppBaseDir>\Models
            string appBaseModels = Path.Combine(baseDir, "Models");
            if (Directory.Exists(appBaseModels))
            {
                return appBaseModels;
            }

            // 3. Fallback: if running inside a build folder (bin\Debug or bin\Release), walk up parent directory looking for <NexusRoot>\Models
            if (baseDir.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var dir = new DirectoryInfo(baseDir);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "Models");
                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }
                    dir = dir.Parent;
                }
            }

            // Default fallback relative to baseDir
            string fallback = string.IsNullOrWhiteSpace(configuredValue) ? DefaultModelRootFolder : configuredValue;
            return Path.GetFullPath(fallback, baseDir);
        }

        public bool GetConfirmExit()
        {
            return _configService.GetConfig().ConfirmExit;
        }

        public bool GetConfirmModule()
        {
            return _configService.GetConfig().ConfirmModule;
        }

        public bool SaveSettings(string theme, string downloadsFolder, bool confirmExit, bool confirmModule, string? modelRootFolder = null, string? engineRootFolder = null)
        {
            var config = _configService.GetConfig();
            config.Theme = theme;
            config.DownloadsFolder = downloadsFolder;
            config.ConfirmExit = confirmExit;
            config.ConfirmModule = confirmModule;
            if (modelRootFolder != null)
            {
                config.ModelRootFolder = modelRootFolder;
            }
            if (engineRootFolder != null)
            {
                config.EngineRootFolder = engineRootFolder;
            }
            if (!_configService.SaveConfig(config))
            {
                throw new IOException("Failed to persist settings configuration to disk.");
            }
            return true;
        }

        public Task SaveSettingsAsync(string theme, string downloadsFolder, bool confirmExit, bool confirmModule, string? modelRootFolder = null, string? engineRootFolder = null)
        {
            SaveSettings(theme, downloadsFolder, confirmExit, confirmModule, modelRootFolder, engineRootFolder);
            return Task.CompletedTask;
        }

        public Task ResetToDefaultAsync()
        {
            var config = new AppConfigRecord();
            if (!_configService.SaveConfig(config))
            {
                throw new IOException("Failed to persist default configuration to disk.");
            }
            return Task.CompletedTask;
        }
    }
}
