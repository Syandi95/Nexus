using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nexus.FileUtilities.Models;

namespace Nexus.FileUtilities.Services
{
    public class RapikanProgressReport
    {
        public string Phase { get; set; } = string.Empty;
        public int ProgressPct { get; set; }
        public string Status { get; set; } = string.Empty;
        public string CurrentFile { get; set; } = string.Empty;
        public int MovedCount { get; set; }
        public int SkippedCount { get; set; }
        public Dictionary<string, long>? RealtimeStats { get; set; }
    }

    public interface IRapikanFileService
    {
        string GetCategoryDestination(string fileName);
        Task<Dictionary<string, long>> PreScanDirectoryAsync(string rootPath);
        Task ExecuteOrganizeAsync(RapikanOptions options);
        Task ExecuteOrganizeAsync(string rootPath, IProgress<RapikanProgressReport>? progress = null, Action<string, string?>? logCallback = null);
        Task UndoOrganizeAsync(string manifestPath);
        string? GetLastLogFilePath(string rootPath);
    }
}
