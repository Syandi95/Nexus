using System.Threading.Tasks;

namespace Nexus.Core.HomeSettings.Services
{
    public interface IHomeDashboardService
    {
        Task UpdateActivityAsync(string module, int fileCount, long totalSize);
        (string Module, int FileCount, long TotalSize, string Timestamp) GetLastActivity();
        void SetEngineStatus(string status, string state);
        void SetFolderStatus(string path);
        (string Status, string State, string FolderPath) GetDashboardStatus();
    }
}
