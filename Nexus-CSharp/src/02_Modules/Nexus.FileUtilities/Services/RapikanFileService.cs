using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nexus.FileUtilities.Models;

namespace Nexus.FileUtilities.Services
{
    public class RapikanFileService : IRapikanFileService
    {
        private readonly IFileManifestService _manifestService;

        private static readonly HashSet<string> ExcludedFolderNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Images", "Videos", "Documents", "Applications", "Archives", "Audio", "Font", "Desain", "Others", "Sampah", "RapikanFile_Log"
        };

        private static readonly Regex DesignPdfRegex = new(@"banner|spanduk|poster|brosur|undangan|logo|backdrop|baliho", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DupeNameRegex = new(@"\s*\(\d+\)\s*$|^Copy\s+of\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private struct ExtInfo
        {
            public string Folder;
            public string Category;
        }

        private static readonly Dictionary<string, ExtInfo> Extensions = new(StringComparer.OrdinalIgnoreCase);

        static RapikanFileService()
        {
            void Add(string ext, string folder, string cat) => Extensions[ext] = new ExtInfo { Folder = folder, Category = cat };

            // Images
            foreach (var ext in new[] { ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tiff", ".tif", ".raw", ".cr2", ".nef" }) Add(ext, @"Images\JPG", "Images");
            Add(".png", @"Images\PNG", "Images");

            // Videos
            foreach (var ext in new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".3gp", ".mpeg", ".mpg", ".ts", ".vob" }) Add(ext, "Videos", "Videos");

            // Desain & CAD (CAD extensions categorized under Desain)
            Add(".cdr", @"Desain\CorelDraw", "Desain");
            Add(".ai", @"Desain\Illustrator", "Desain");
            Add(".eps", @"Desain\Illustrator", "Desain");
            Add(".psd", @"Desain\Photoshop", "Desain");
            Add(".psb", @"Desain\Photoshop", "Desain");
            foreach (var ext in new[] { ".dwg", ".dxf", ".dgn", ".step", ".stp", ".iges", ".igs", ".stl", ".sldprt", ".sldasm" }) Add(ext, @"Desain\CAD", "Desain");

            // Documents
            Add(".pdf", @"Documents\PDF", "Documents");
            foreach (var ext in new[] { ".doc", ".docx", ".rtf", ".odt" }) Add(ext, @"Documents\Word", "Documents");
            foreach (var ext in new[] { ".xls", ".xlsx", ".ods", ".csv" }) Add(ext, @"Documents\Excel", "Documents");
            foreach (var ext in new[] { ".ppt", ".pptx", ".odp" }) Add(ext, @"Documents\PowerPoint", "Documents");

            // Font
            Add(".ttf", "Font", "Font");
            Add(".otf", "Font", "Font");

            // Applications
            foreach (var ext in new[] { ".exe", ".msi", ".ps1", ".vbs", ".reg", ".inf", ".jar", ".apk" }) Add(ext, "Applications", "Applications");

            // Archives
            foreach (var ext in new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".cab", ".zst" }) Add(ext, "Archives", "Archives");

            // Audio
            foreach (var ext in new[] { ".mp3", ".wav", ".aac", ".flac", ".ogg", ".wma", ".m4a", ".opus" }) Add(ext, "Audio", "Audio");
        }

        public RapikanFileService(IFileManifestService manifestService)
        {
            _manifestService = manifestService;
        }

        public string GetCategoryDestination(string fileName)
        {
            var ext = Path.GetExtension(fileName);
            if (Extensions.TryGetValue(ext, out var info)) return info.Category;
            return "Others";
        }

        public string? GetLastLogFilePath(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath)) return null;
            var logPath = Path.Combine(rootPath, "RapikanFile_Log", "RapikanFile_Last.log");
            return File.Exists(logPath) ? logPath : null;
        }

        public Task<Dictionary<string, long>> PreScanDirectoryAsync(string rootPath)
        {
            return Task.Run(() =>
            {
                var stats = CreateEmptyStats();
                if (!Directory.Exists(rootPath)) return stats;

                var targetDir = rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var exclPaths = ExcludedFolderNames.Select(name => Path.Combine(rootPath, name) + Path.DirectorySeparatorChar).ToList();

                var dirInfo = new DirectoryInfo(rootPath);
                var files = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                    .Where(f =>
                    {
                        var fp = f.FullName + Path.DirectorySeparatorChar;
                        return !exclPaths.Any(ep => fp.StartsWith(ep, StringComparison.OrdinalIgnoreCase));
                    });

                long totalCount = 0;
                long totalSize = 0;

                foreach (var f in files)
                {
                    totalCount++;
                    totalSize += f.Length;
                    var ext = f.Extension;

                    if (Extensions.TryGetValue(ext, out var info))
                    {
                        if (info.Category == "Images" && f.Length < 102400)
                        {
                            stats["Sampah"]++;
                        }
                        else
                        {
                            stats[info.Category]++;
                        }
                    }
                    else
                    {
                        stats["Others"]++;
                    }
                }

                stats["TotalFile"] = totalCount;
                stats["TotalSize"] = totalSize;
                return stats;
            });
        }

        public Task ExecuteOrganizeAsync(RapikanOptions options) => ExecuteOrganizeAsync(options.RootPath, null, null);

        public async Task ExecuteOrganizeAsync(string rootPath, IProgress<RapikanProgressReport>? progress = null, Action<string, string?>? logCallback = null)
        {
            await Task.Run(async () =>
            {
                var target = rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var logFolder = Path.Combine(rootPath, "RapikanFile_Log");
                Directory.CreateDirectory(logFolder);
                var logFile = Path.Combine(logFolder, "RapikanFile_Last.log");

                using var logWriter = new StreamWriter(logFile, false);
                logWriter.WriteLine("============================================================");
                logWriter.WriteLine($"Dijalankan: {DateTime.Now}");
                logWriter.WriteLine($"Target Folder: {rootPath}");
                logWriter.WriteLine("============================================================");

                var stats = CreateEmptyStats();

                void ReportProgress(string phase, int pct, string status, string currentFile = "-", int moved = 0, int skipped = 0)
                {
                    progress?.Report(new RapikanProgressReport
                    {
                        Phase = phase,
                        ProgressPct = pct,
                        Status = status,
                        CurrentFile = currentFile,
                        MovedCount = moved,
                        SkippedCount = skipped,
                        RealtimeStats = new Dictionary<string, long>(stats)
                    });
                }

                logCallback?.Invoke("Memulai fase 1/3: Mengindeks file...", null);
                ReportProgress("Fase 1/3: Mengindeks...", 0, "Mengindeks file...");

                var exclPaths = ExcludedFolderNames.Select(name => Path.Combine(rootPath, name) + Path.DirectorySeparatorChar).ToList();
                var dirInfo = new DirectoryInfo(rootPath);
                var allFiles = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                    .Where(f =>
                    {
                        var fp = f.FullName + Path.DirectorySeparatorChar;
                        return !exclPaths.Any(ep => fp.StartsWith(ep, StringComparison.OrdinalIgnoreCase));
                    }).ToList();

                var processFiles = new List<FileInfo>();
                foreach (var f in allFiles)
                {
                    var relPath = f.FullName[target.Length..].TrimStart(Path.DirectorySeparatorChar);
                    var idx = relPath.IndexOf(Path.DirectorySeparatorChar);
                    var rootSub = idx > -1 ? relPath[..idx] : relPath;

                    if (!ExcludedFolderNames.Contains(rootSub) &&
                        !f.Name.Equals("RapikanFile.bat", StringComparison.OrdinalIgnoreCase) &&
                        !f.Name.StartsWith("log_", StringComparison.OrdinalIgnoreCase) &&
                        !f.Name.Equals("RapikanFile_Last.log", StringComparison.OrdinalIgnoreCase))
                    {
                        processFiles.Add(f);
                    }
                }

                int totalFiles = processFiles.Count;
                logCallback?.Invoke($"{totalFiles} file akan diproses.", null);

                if (totalFiles == 0)
                {
                    ReportProgress("Selesai", 100, "Tidak ada file untuk diproses.");
                    return;
                }

                // Phase 2: Duplicate pre-scan
                ReportProgress("Fase 2/3: Pre-scan duplikat...", 0, "Smart pre-scan duplikat...");
                logCallback?.Invoke("Memulai fase 2/3: Smart pre-scan duplikat...", null);

                var dupeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var sizeGroups = processFiles.GroupBy(f => f.Length).Where(g => g.Count() > 1).ToList();
                var filesToHash = new List<FileInfo>();

                foreach (var group in sizeGroups)
                {
                    var sorted = group.OrderBy(f => DupeNameRegex.IsMatch(f.Name) ? 1 : 0);
                    filesToHash.AddRange(sorted);
                }

                int hashTotal = filesToHash.Count;
                logCallback?.Invoke($"{hashTotal} file perlu di-hash.", null);

                if (hashTotal > 0)
                {
                    using var sha256 = SHA256.Create();
                    var hashIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    int hashCurrent = 0;

                    foreach (var file in filesToHash)
                    {
                        hashCurrent++;
                        if (hashCurrent % 50 == 0 || hashCurrent == hashTotal)
                        {
                            int pct = (int)Math.Round((double)hashCurrent / hashTotal * 100);
                            ReportProgress("Fase 2/3: Pre-scan duplikat...", pct, $"Hashing ({hashCurrent}/{hashTotal})...");
                        }

                        try
                        {
                            if (!file.Exists) continue;
                            using var fs = file.OpenRead();
                            var hashBytes = sha256.ComputeHash(fs);
                            var hashStr = Convert.ToHexString(hashBytes);

                            if (hashIndex.TryGetValue(hashStr, out var existingPath))
                            {
                                dupeSet.Add(file.FullName);
                            }
                            else
                            {
                                hashIndex[hashStr] = file.FullName;
                            }
                        }
                        catch
                        {
                            // Ignore hash errors
                        }
                    }
                }

                logCallback?.Invoke($"Duplikat terdeteksi: {dupeSet.Count} file.", null);

                // Phase 3: Move files
                ReportProgress("Fase 3/3: Memproses file...", 0, "Memproses file...");
                logCallback?.Invoke("Memulai fase 3/3: Memproses file...", null);

                var existingFilesCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var fileSizeCache = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                var folderCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var f in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    existingFilesCache.Add(f.FullName);
                    fileSizeCache[f.FullName] = f.Length;
                }

                var manifest = new FileManifest();
                int moved = 0;
                int skipped = 0;
                int current = 0;

                foreach (var file in processFiles)
                {
                    current++;
                    if (current % 50 == 0 || current == totalFiles)
                    {
                        int pct = (int)Math.Round((double)current / totalFiles * 100);
                        ReportProgress("Fase 3/3: Memproses file...", pct, $"Memproses ({current}/{totalFiles})...", file.Name, moved, skipped);
                    }

                    if (!File.Exists(file.FullName))
                    {
                        skipped++;
                        continue;
                    }

                    var ext = file.Extension;
                    var subFolder = "Others";
                    var cat = "Others";

                    if (dupeSet.Contains(file.FullName))
                    {
                        subFolder = @"Sampah\Duplikat";
                        cat = "Sampah";
                    }
                    else if (Extensions.TryGetValue(ext, out var info))
                    {
                        subFolder = info.Folder;
                        cat = info.Category;

                        if (cat == "Images" && file.Length < 102400)
                        {
                            subFolder = @"Sampah\Kecil";
                            cat = "Sampah";
                        }
                        else if (cat == "Images")
                        {
                            var name = file.Name;
                            if (name.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase)) subFolder = @"Images\ChatGPT";
                            else if (name.Contains("Gemini", StringComparison.OrdinalIgnoreCase)) subFolder = @"Images\Gemini";
                            else if (name.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)) subFolder = @"Images\WhatsApp";
                            else if (name.Contains("Pngtree", StringComparison.OrdinalIgnoreCase)) subFolder = @"Images\Pngtree";
                        }
                        else if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase) && DesignPdfRegex.IsMatch(file.Name))
                        {
                            subFolder = @"Desain\PDF";
                            cat = "Desain";
                        }
                    }

                    var destDir = Path.Combine(target, subFolder);
                    var destFile = Path.Combine(destDir, file.Name);

                    try
                    {
                        if (!folderCache.Contains(destDir))
                        {
                            Directory.CreateDirectory(destDir);
                            folderCache.Add(destDir);
                        }

                        string finalDestFile = destFile;
                        bool isRenamed = false;
                        string newName = file.Name;

                        if (existingFilesCache.Contains(destFile) && !destFile.Equals(file.FullName, StringComparison.OrdinalIgnoreCase))
                        {
                            isRenamed = true;
                            int count = 1;
                            var baseName = Path.GetFileNameWithoutExtension(file.Name);
                            var testPath = Path.Combine(destDir, $"{baseName}_{count}{ext}");
                            while (existingFilesCache.Contains(testPath))
                            {
                                count++;
                                testPath = Path.Combine(destDir, $"{baseName}_{count}{ext}");
                            }
                            finalDestFile = testPath;
                            newName = Path.GetFileName(testPath);
                        }

                        existingFilesCache.Remove(file.FullName);
                        File.Move(file.FullName, finalDestFile);
                        existingFilesCache.Add(finalDestFile);

                        manifest.OriginalPaths[finalDestFile] = file.FullName;

                        var tagMap = new Dictionary<string, string>
                        {
                            ["Images"] = "[IMAGES]", ["Videos"] = "[VIDEOS]", ["Documents"] = "[DOCUMENTS]",
                            ["Applications"] = "[APP]", ["Archives"] = "[ARCHIVE]", ["Audio"] = "[AUDIO]",
                            ["Font"] = "[FONT]", ["Desain"] = "[DESAIN]", ["Others"] = "[OTH]", ["Sampah"] = "[SAMPAH]"
                        };
                        var tagColorMap = new Dictionary<string, string>
                        {
                            ["Images"] = "#3B82F6", ["Videos"] = "#8B5CF6", ["Documents"] = "#10B981",
                            ["Applications"] = "#F59E0B", ["Archives"] = "#EC4899", ["Audio"] = "#06B6D4",
                            ["Font"] = "#6366F1", ["Desain"] = "#F97316", ["Others"] = "#94A3B8", ["Sampah"] = "#EF4444"
                        };
                        var tag = tagMap.TryGetValue(cat, out var t) ? t : "[LOG]";
                        var tagColor = tagColorMap.TryGetValue(cat, out var tc) ? tc : null;

                        var logMsg = isRenamed
                            ? $"{tag} {file.Name} [RENAME: {newName}] -> {subFolder}"
                            : $"{tag} {file.Name} -> {subFolder}";

                        logWriter.WriteLine(logMsg);
                        logCallback?.Invoke(logMsg, tagColor);

                        stats[cat]++;
                        stats["TotalFile"]++;
                        stats["TotalSize"] += file.Length;
                        moved++;
                    }
                    catch (Exception ex)
                    {
                        var errorMsg = $"[ERROR] {file.Name} | {ex.Message}";
                        logWriter.WriteLine(errorMsg);
                        logCallback?.Invoke(errorMsg, "#EF4444");
                        skipped++;
                    }
                }

                logWriter.WriteLine("============================================================");
                logWriter.WriteLine($"TOTAL - Sukses: {moved} | Skip/Gagal: {skipped}");

                // Save undo manifest
                var manifestPath = Path.Combine(rootPath, "undo_manifest.json");
                await _manifestService.SaveManifestAsync(manifest, manifestPath);

                var finalMsg = $"[LOG] Proses selesai. Total: {moved} dipindahkan, {skipped} gagal/skip.";
                logCallback?.Invoke(finalMsg, null);

                // Summary Section Banner (PowerShell Baseline Parity)
                logCallback?.Invoke("================================", "#2F6BFF");
                logCallback?.Invoke("HASIL PROSES - RAPIKAN FILE", "#2F6BFF");
                logCallback?.Invoke($"  Images      : {stats["Images"]} file", "#22C55E");
                logCallback?.Invoke($"  Videos      : {stats["Videos"]} file", "#A855F7");
                logCallback?.Invoke($"  Documents   : {stats["Documents"]} file", "#F59E0B");
                logCallback?.Invoke($"  Desain      : {stats["Desain"]} file", "#EC4899");
                logCallback?.Invoke($"  Archives    : {stats["Archives"]} file", "#8B5CF6");
                logCallback?.Invoke($"  Audio       : {stats["Audio"]} file", "#06B6D4");
                logCallback?.Invoke($"  Font        : {stats["Font"]} file", "#10B981");
                logCallback?.Invoke($"  Applications: {stats["Applications"]} file", "#3B82F6");
                logCallback?.Invoke($"  Others      : {stats["Others"]} file", "#6B7280");
                logCallback?.Invoke($"  Sampah      : {stats["Sampah"]} file", "#EF4444");
                logCallback?.Invoke("--------------------------------", "#6B7280");
                logCallback?.Invoke($"  Total File  : {stats["TotalFile"]} file", null);
                logCallback?.Invoke($"  Total Size  : {FormatFileSize(stats["TotalSize"])}", null);
                logCallback?.Invoke("================================", "#2F6BFF");

                ReportProgress("Selesai!", 100, "Proses selesai!", "-", moved, skipped);
            });
        }

        public async Task UndoOrganizeAsync(string manifestPath)
        {
            if (!File.Exists(manifestPath)) return;
            var manifest = await _manifestService.LoadManifestAsync(manifestPath);

            await Task.Run(() =>
            {
                foreach (var kvp in manifest.OriginalPaths)
                {
                    var currentPath = kvp.Key;
                    var originalPath = kvp.Value;

                    if (File.Exists(currentPath))
                    {
                        var dir = Path.GetDirectoryName(originalPath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }
                        File.Move(currentPath, originalPath, overwrite: true);
                    }
                }

                File.Delete(manifestPath);
            });
        }

        private static Dictionary<string, long> CreateEmptyStats()
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["Images"] = 0, ["Videos"] = 0, ["Desain"] = 0, ["Documents"] = 0,
                ["Audio"] = 0, ["Font"] = 0, ["Archives"] = 0, ["Applications"] = 0,
                ["Others"] = 0, ["Sampah"] = 0, ["TotalFile"] = 0, ["TotalSize"] = 0
            };
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
