using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Nexus.Foundation.Config
{
    public class ConfigService : IConfigService
    {
        private readonly string _configFilePath;
        private AppConfigRecord _currentConfig;

        public ConfigService(string? configFilePath = null)
        {
            _configFilePath = configFilePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nexus_data", "config.json");
            _currentConfig = LoadConfig();
        }

        public AppConfigRecord GetConfig()
        {
            return _currentConfig;
        }

        public bool SaveConfig(AppConfigRecord config)
        {
            try
            {
                var directory = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
                _currentConfig = config;
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ConfigService] Error saving config: {ex.Message}");
                return false;
            }
        }

        private AppConfigRecord LoadConfig()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath).Trim('\0');
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        Trace.WriteLine("[ConfigService] Config file is empty or whitespace. Using default config.");
                        return new AppConfigRecord();
                    }

                    var config = JsonSerializer.Deserialize<AppConfigRecord>(json);
                    if (config != null)
                    {
                        return config;
                    }
                }
            }
            catch (JsonException ex)
            {
                Trace.WriteLine($"[ConfigService] Config file corrupted or invalid JSON: {ex.Message}. Using default config without overwriting file.");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ConfigService] Error reading config: {ex.Message}. Using default config.");
            }

            return new AppConfigRecord(); // Fallback to default
        }
    }
}
