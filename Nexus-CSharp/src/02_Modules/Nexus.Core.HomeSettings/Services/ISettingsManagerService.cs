using System.Threading.Tasks;

namespace Nexus.Core.HomeSettings.Services
{
    public interface ISettingsManagerService
    {
        string GetTheme();
        string GetDownloadsPath();
        string ModelRootFolder { get; set; }
        string EngineRootFolder { get; set; }
        bool GetConfirmExit();
        bool GetConfirmModule();
        bool SaveSettings(string theme, string downloadsFolder, bool confirmExit, bool confirmModule, string? modelRootFolder = null, string? engineRootFolder = null);
        Task SaveSettingsAsync(string theme, string downloadsFolder, bool confirmExit, bool confirmModule, string? modelRootFolder = null, string? engineRootFolder = null);
        Task ResetToDefaultAsync();
    }
}
