using System;
using Nexus.Core.HomeSettings.Services;

namespace Nexus.App.ViewModels
{
    public class HomeViewModel : ViewModelBase
    {
        private readonly IHomeDashboardService _homeDashboardService;

        public HomeViewModel(IHomeDashboardService homeDashboardService)
        {
            _homeDashboardService = homeDashboardService;
            Refresh();
        }

        public string ActiveModule { get; private set; } = "-";
        public string ActivityDetail { get; private set; } = "Belum ada riwayat aktivitas terbaru.";
        public string TotalFiles { get; private set; } = "0";
        public string TotalSizeFormatted { get; private set; } = "0 KB";

        public string SystemStatus { get; private set; } = "READY";
        public string EngineState { get; private set; } = "Standby";
        public string ActiveFolder { get; private set; } = "Downloads";

        public void Refresh()
        {
            var activity = _homeDashboardService.GetLastActivity();
            if (!string.IsNullOrEmpty(activity.Module) && activity.Module != "Belum ada" && activity.Module != "-")
            {
                ActiveModule = activity.Module;
                TotalFiles = activity.FileCount.ToString();
                TotalSizeFormatted = FormatFileSize(activity.TotalSize);
                var timestamp = string.IsNullOrEmpty(activity.Timestamp)
                    ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    : activity.Timestamp;
                ActivityDetail = $"{timestamp} | {activity.FileCount} file | {TotalSizeFormatted}";
            }
            else
            {
                ActiveModule = "-";
                TotalFiles = "0";
                TotalSizeFormatted = "0 KB";
                ActivityDetail = "Belum ada riwayat aktivitas terbaru.";
            }

            var status = _homeDashboardService.GetDashboardStatus();
            EngineState = status.Status;
            SystemStatus = status.State;
            ActiveFolder = string.IsNullOrEmpty(status.FolderPath) ? "Downloads" : status.FolderPath;

            OnPropertyChanged(nameof(ActiveModule));
            OnPropertyChanged(nameof(ActivityDetail));
            OnPropertyChanged(nameof(TotalFiles));
            OnPropertyChanged(nameof(TotalSizeFormatted));
            OnPropertyChanged(nameof(SystemStatus));
            OnPropertyChanged(nameof(EngineState));
            OnPropertyChanged(nameof(ActiveFolder));
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes <= 0) return "0 KB";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < units.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {units[order]}";
        }
    }
}
