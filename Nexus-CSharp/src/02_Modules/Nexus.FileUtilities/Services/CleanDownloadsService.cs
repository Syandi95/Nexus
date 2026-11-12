using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nexus.FileUtilities.Models;

namespace Nexus.FileUtilities.Services
{
    public class CleanDownloadsService : ICleanDownloadsService
    {
        private readonly IFileManifestService _manifestService;

        private static readonly Regex PdfRegex = new(@"banner|spanduk|poster|brosur|undangan|logo|backdrop|baliho", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private struct ExtInfo
        {
            public string Folder;
            public string Category;
        }

        private static readonly Dictionary<string, ExtInfo> ExtMap = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> ExtColor = new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "#4682B4", [".jpeg"] = "#4682B4", [".png"] = "#FF8C00", [".gif"] = "#B464C8",
            [".webp"] = "#64B464", [".pdf"] = "#DC3C3C", [".doc"] = "#3C6EC8", [".docx"] = "#3C6EC8",
            [".xls"] = "#3C9650", [".xlsx"] = "#3C9650", [".ppt"] = "#DC8228", [".pptx"] = "#DC8228",
            [".zip"] = "#C8AA32", [".rar"] = "#C8AA32", [".7z"] = "#C8AA32", [".mp4"] = "#B450C8",
            [".mp3"] = "#C86496", [".exe"] = "#969696", [".ai"] = "#DC6432", [".psd"] = "#3C6EC8"
        };

        static CleanDownloadsService()
        {
            void Add(string ext, string folder, string cat) => ExtMap[ext] = new ExtInfo { Folder = folder, Category = cat };

            foreach (var ext in new[] { ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tiff" }) Add(ext, @"Images\JPG", "Images");
            Add(".png", @"Images\PNG", "Images");

            Add(".pdf", @"Documents\PDF", "Documents");
            foreach (var ext in new[] { ".doc", ".docx" }) Add(ext, @"Documents\Word", "Documents");
            foreach (var ext in new[] { ".xls", ".xlsx", ".csv" }) Add(ext, @"Documents\Excel", "Documents");
            foreach (var ext in new[] { ".ppt", ".pptx" }) Add(ext, @"Documents\PowerPoint", "Documents");

            foreach (var ext in new[] { ".zip", ".rar", ".7z" }) Add(ext, "Archives", "Archives");
            foreach (var ext in new[] { ".exe", ".msi" }) Add(ext, "Applications", "Applications");
            foreach (var ext in new[] { ".mp4", ".avi", ".mkv" }) Add(ext, "Videos", "Videos");
            foreach (var ext in new[] { ".mp3", ".wav" }) Add(ext, "Audio", "Audio");

            Add(".ai", @"Desain\Illustrator", "Desain");
            Add(".psd", @"Desain\Photoshop", "Desain");
            Add(".cdr", @"Desain\CorelDraw", "Desain");
            Add(".ttf", "Font", "Font");
            Add(".otf", "Font", "Font");
        }

        public CleanDownloadsService(IFileManifestService manifestService)
        {
            _manifestService = manifestService;
        }

        public string GetDestinationSubfolder(string fileName, long length = 0)
        {
            var ext = Path.GetExtension(fileName);
            var lowerName = fileName.ToLower();

            if (!ExtMap.TryGetValue(ext, out var info)) return "Others";

            var folder = info.Folder;
            var cat = info.Category;

            if (cat == "Images" && length > 0 && length < 102400) return @"Sampah\Kecil";
            if (cat == "Images")
            {
                if (lowerName.Contains("chatgpt")) return @"Images\ChatGPT";
                if (lowerName.Contains("gemini")) return @"Images\Gemini";
                if (lowerName.Contains("whatsapp")) return @"Images\WhatsApp";
            }

            if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase) && PdfRegex.IsMatch(fileName))
            {
                return @"Desain\PDF";
            }

            return folder;
        }

        public string? GetDestinationSubfolder(string fileName) => GetDestinationSubfolder(fileName, 0);

        public Task<Dictionary<string, List<string>>> ScanDirectoryAsync(string rootPath)
        {
            return Task.Run(() =>
            {
                var result = new Dictionary<string, List<string>>();
                if (!Directory.Exists(rootPath)) return result;

                var files = Directory.GetFiles(rootPath);
                foreach (var file in files)
                {
                    var info = new FileInfo(file);
                    var folder = GetDestinationSubfolder(info.Name, info.Length);
                    if (!result.ContainsKey(folder)) result[folder] = new List<string>();
                    result[folder].Add(file);
                }
                return result;
            });
        }

        public Task<List<CleanFileItemModel>> ScanDownloadsFolderAsync(string downloadsPath)
        {
            return Task.Run(() =>
            {
                var list = new List<CleanFileItemModel>();
                if (!Directory.Exists(downloadsPath)) return list;

                var dirInfo = new DirectoryInfo(downloadsPath);
                var files = dirInfo.GetFiles("*", SearchOption.TopDirectoryOnly)
                    .Where(f => !f.Name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) &&
                                !f.Name.Equals("session.lock", StringComparison.OrdinalIgnoreCase) &&
                                !f.Name.Equals("crash.log", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTime);

                foreach (var file in files)
                {
                    var ext = file.Extension.ToLower();
                    var badgeHex = ExtColor.TryGetValue(ext, out var c) ? c : "#0078D4";

                    var span = DateTime.Now - file.LastWriteTime;
                    var days = (int)span.TotalDays;
                    var ageText = days == 0 ? "Hari ini" : days == 1 ? "1 hari" : days < 365 ? $"{days} hari" : $"{Math.Floor(days / 365.0)} th";
                    var ageFgHex = days > 30 ? "#DC2626" : days > 7 ? "#CA8A04" : "#16A34A";

                    var destFolder = GetDestinationSubfolder(file.Name, file.Length);

                    list.Add(new CleanFileItemModel
                    {
                        Checked = false,
                        Name = file.Name,
                        Ext = string.IsNullOrEmpty(file.Extension) ? "-" : file.Extension.TrimStart('.').ToUpper(),
                        ExtBgHex = badgeHex,
                        RawSize = file.Length,
                        SizeDisplay = FormatFileSize(file.Length),
                        AgeDisplay = ageText,
                        AgeFgHex = ageFgHex,
                        TargetFolder = destFolder,
                        FullPath = file.FullName,
                        LastWriteTime = file.LastWriteTime
                    });
                }

                return list;
            });
        }

        public async Task<CleanResultReport> ExecuteCleanDownloadsAsync(string downloadsPath, List<CleanFileItemModel> items, IProgress<double>? progress)
        {
            return await Task.Run(async () =>
            {
                var report = new CleanResultReport();
                var startTime = DateTime.Now;
                var logFolder = Path.Combine(downloadsPath, "CleanDownloads_Log");
                Directory.CreateDirectory(logFolder);
                var logFile = Path.Combine(logFolder, $"log_{DateTime.Now:yyyy-MM-dd}.txt");

                using var logWriter = new StreamWriter(logFile, true);
                logWriter.WriteLine("===============================");
                logWriter.WriteLine($"Dijalankan: {DateTime.Now}");
                logWriter.WriteLine("===============================");

                var manifest = new FileManifest();
                int index = 0;
                int total = items.Count;

                foreach (var item in items)
                {
                    index++;
                    progress?.Report((double)index / total * 100);

                    if (!File.Exists(item.FullPath))
                    {
                        report.FailedCount++;
                        continue;
                    }

                    if (item.Checked)
                    {
                        try
                        {
                            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                                item.FullPath,
                                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

                            logWriter.WriteLine($"[HAPUS] {item.FullPath}");
                            report.DeletedCount++;
                            report.ProcessedSize += item.RawSize;
                        }
                        catch (Exception ex)
                        {
                            report.FailedCount++;
                            logWriter.WriteLine($"[GAGAL HAPUS] {item.FullPath} | {ex.Message}");
                        }
                    }
                    else
                    {
                        try
                        {
                            var destFolderRel = item.TargetFolder;
                            var destDir = Path.Combine(downloadsPath, destFolderRel);
                            Directory.CreateDirectory(destDir);

                            var destFile = Path.Combine(destDir, item.Name);
                            if (File.Exists(destFile))
                            {
                                var baseName = Path.GetFileNameWithoutExtension(item.FullPath);
                                var ext = Path.GetExtension(item.FullPath);
                                int i = 1;
                                do
                                {
                                    destFile = Path.Combine(destDir, $"{baseName}_{i}{ext}");
                                    i++;
                                } while (File.Exists(destFile));
                            }

                            File.Move(item.FullPath, destFile);
                            logWriter.WriteLine($"[PINDAH] {item.FullPath} -> {destFile}");
                            manifest.OriginalPaths[destFile] = item.FullPath;
                            report.MovedCount++;
                            report.ProcessedSize += item.RawSize;
                        }
                        catch (Exception ex)
                        {
                            report.FailedCount++;
                            logWriter.WriteLine($"[GAGAL] {item.FullPath} | {ex.Message}");
                        }
                    }
                }

                report.Duration = DateTime.Now - startTime;
                logWriter.WriteLine("===============================");
                logWriter.WriteLine("HASIL PROSES");
                logWriter.WriteLine($"  Hapus  : {report.DeletedCount} file");
                logWriter.WriteLine($"  Pindah : {report.MovedCount} file");
                logWriter.WriteLine($"  Gagal  : {report.FailedCount} file");
                logWriter.WriteLine($"  Ukuran : {FormatFileSize(report.ProcessedSize)}");
                logWriter.WriteLine($"  Durasi : {report.Duration:hh\\:mm\\:ss}");
                logWriter.WriteLine("===============================");

                if (manifest.OriginalPaths.Count > 0)
                {
                    await _manifestService.SaveManifestAsync(manifest, Path.Combine(logFolder, "undo_manifest.json"));
                }

                return report;
            });
        }

        public async Task ExecuteCleanAsync(CleanOptions options)
        {
            var items = await ScanDownloadsFolderAsync(options.RootPath);
            await ExecuteCleanDownloadsAsync(options.RootPath, items, null);
        }

        public async Task<string> UndoCleanDownloadsAsync(string downloadsPath)
        {
            return await Task.Run(async () =>
            {
                var manifestPath = Path.Combine(downloadsPath, "CleanDownloads_Log", "undo_manifest.json");
                if (!File.Exists(manifestPath))
                {
                    manifestPath = Path.Combine(downloadsPath, "undo_manifest.json");
                }

                if (!File.Exists(manifestPath))
                {
                    return "Tidak ada operasi yang bisa di-undo dari sesi terakhir.";
                }

                var manifest = await _manifestService.LoadManifestAsync(manifestPath);
                int successMove = 0;
                int failedMove = 0;

                foreach (var kvp in manifest.OriginalPaths.Reverse())
                {
                    var dest = kvp.Key;
                    var src = kvp.Value;

                    if (File.Exists(dest))
                    {
                        if (File.Exists(src))
                        {
                            failedMove++;
                        }
                        else
                        {
                            try
                            {
                                var dir = Path.GetDirectoryName(src);
                                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                                File.Move(dest, src);
                                successMove++;
                            }
                            catch
                            {
                                failedMove++;
                            }
                        }
                    }
                    else
                    {
                        failedMove++;
                    }
                }

                if (File.Exists(manifestPath)) File.Delete(manifestPath);
                return $"Undo selesai.\n\nPindah: Berhasil {successMove} | Gagal {failedMove}";
            });
        }

        public async Task UndoCleanAsync(string manifestPath)
        {
            await UndoCleanDownloadsAsync(Path.GetDirectoryName(manifestPath) ?? string.Empty);
        }

        public string? GetLatestLogPath(string downloadsPath)
        {
            if (string.IsNullOrWhiteSpace(downloadsPath)) return null;
            var logDir = Path.Combine(downloadsPath, "CleanDownloads_Log");
            if (!Directory.Exists(logDir)) return null;

            var todayFile = Path.Combine(logDir, $"log_{DateTime.Now:yyyy-MM-dd}.txt");
            if (File.Exists(todayFile)) return todayFile;

            var latest = new DirectoryInfo(logDir).GetFiles("log_*.txt").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            return latest?.FullName;
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1073741824) return $"{bytes / 1073741824.0:N1} GB";
            if (bytes >= 1048576) return $"{bytes / 1048576.0:N1} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:N0} KB";
            return $"{bytes} B";
        }
    }
}
