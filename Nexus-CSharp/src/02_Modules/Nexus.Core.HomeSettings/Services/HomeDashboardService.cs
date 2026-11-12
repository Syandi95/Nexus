using System;
using System.Threading.Tasks;
using Nexus.Foundation.Config;

namespace Nexus.Core.HomeSettings.Services
{
    public class HomeDashboardService : IHomeDashboardService
    {
        private readonly IConfigService _configService;

        public HomeDashboardService(IConfigService configService)
        {
            _configService = configService;
        }

        public Task UpdateActivityAsync(string module, int fileCount, long totalSize)
        {
            var config = _configService.GetConfig();
            config.LastActivityModule = module;
            config.LastActivityFileCount = fileCount;
            config.LastActivityTotalSize = totalSize;
            config.LastActivityTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _configService.SaveConfig(config);
            return Task.CompletedTask;
        }

        public (string Module, int FileCount, long TotalSize, string Timestamp) GetLastActivity()
        {
            var config = _configService.GetConfig();
            return (config.LastActivityModule, config.LastActivityFileCount, config.LastActivityTotalSize, config.LastActivityTimestamp);
        }

        public void SetEngineStatus(string status, string state)
        {
            var config = _configService.GetConfig();
            config.EngineStatus = status;
            config.EngineState = state;
            _configService.SaveConfig(config);
        }

        public void SetFolderStatus(string path)
        {
            var config = _configService.GetConfig();
            config.ActiveFolderPath = path;
            _configService.SaveConfig(config);
        }

        public (string Status, string State, string FolderPath) GetDashboardStatus()
        {
            var config = _configService.GetConfig();
            var status = string.IsNullOrEmpty(config.EngineStatus) ? "Standby" : config.EngineStatus;
            var state = string.IsNullOrEmpty(config.EngineState) ? "READY" : config.EngineState;
            var folder = string.IsNullOrEmpty(config.ActiveFolderPath) ? "Downloads" : config.ActiveFolderPath;
            return (status, state, folder);
        }
    }
}
