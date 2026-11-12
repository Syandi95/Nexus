using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nexus.FileUtilities.Models;

namespace Nexus.FileUtilities.Services
{
    public class CleanFileItemModel
    {
        public bool Checked { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Ext { get; set; } = string.Empty;
        public string ExtBgHex { get; set; } = "#0078D4";
        public long RawSize { get; set; }
        public string SizeDisplay { get; set; } = "0 B";
        public string AgeDisplay { get; set; } = string.Empty;
        public string AgeFgHex { get; set; } = "#16A34A";
        public string TargetFolder { get; set; } = string.Empty;
        public string StatusText => Checked ? "HAPUS" : "SIMPAN";
        public string StatusFgHex => Checked ? "#DC2626" : "#16A34A";
        public string FullPath { get; set; } = string.Empty;
        public DateTime LastWriteTime { get; set; }
    }

    public class CleanResultReport
    {
        public int DeletedCount { get; set; }
        public int MovedCount { get; set; }
        public int FailedCount { get; set; }
        public long ProcessedSize { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public interface ICleanDownloadsService
    {
        string GetDestinationSubfolder(string fileName, long length = 0);
        Task<List<CleanFileItemModel>> ScanDownloadsFolderAsync(string downloadsPath);
        Task<CleanResultReport> ExecuteCleanDownloadsAsync(string downloadsPath, List<CleanFileItemModel> items, IProgress<double>? progress);
        Task<string> UndoCleanDownloadsAsync(string downloadsPath);
        string? GetLatestLogPath(string downloadsPath);
    }
}
