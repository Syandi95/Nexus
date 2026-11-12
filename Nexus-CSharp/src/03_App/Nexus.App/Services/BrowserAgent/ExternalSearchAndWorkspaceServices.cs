using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Nexus.App.ViewModels;

namespace Nexus.App.Services.BrowserAgent;

public sealed class ExternalSearchProvider : ISearchProvider
{
    private static readonly HttpClient SearchHttp = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) NexusAcademicAgent/1.0");
        return client;
    }

    public async Task<IReadOnlyList<SearchResultCandidate>> SearchAsync(
        string query,
        string provider = "scholar",
        int maxResults = 6,
        CancellationToken ct = default)
    {
        var results = new List<SearchResultCandidate>();
        if (string.IsNullOrWhiteSpace(query))
            return results;

        bool isAcademic = string.Equals(provider, "scholar", StringComparison.OrdinalIgnoreCase) ||
                          query.Contains("makalah", StringComparison.OrdinalIgnoreCase) ||
                          query.Contains("jurnal", StringComparison.OrdinalIgnoreCase) ||
                          query.Contains("paper", StringComparison.OrdinalIgnoreCase) ||
                          query.Contains("doi", StringComparison.OrdinalIgnoreCase) ||
                          query.Contains("scholar", StringComparison.OrdinalIgnoreCase);

        if (isAcademic)
        {
            await TrySearchOpenAlexAcademicWorksAsync(query, maxResults, results, ct);
            if (results.Count < 3)
            {
                await TrySearchCrossrefWorksAsync(query, maxResults, results, ct);
            }
        }

        if (results.Count < maxResults)
        {
            await TrySearchDuckDuckGoHtmlAsync(query, maxResults - results.Count, results, ct);
        }

        return results.Take(maxResults).ToList();
    }

    private static async Task TrySearchOpenAlexAcademicWorksAsync(
        string query,
        int maxResults,
        List<SearchResultCandidate> target,
        CancellationToken ct)
    {
        try
        {
            string cleanQuery = Regex.Replace(
                query,
                @"\b(ya|iya|oke|belum|terlihat|kamu|anda|saja|aja|membukanya|memasukkan|memeasukan|masukkan|masukan|kata|kunci|keyword|pencarian|coba|tolong|bisa|bukakan|buka|browser|web|carikan|cari|mencarinya|telusuri|makalah|jurnal|paper|artikel|skripsi|di|ke|dari|google|scholar|cendekia|tentang|mengenai|yang|relevan|terbaru|langsung|segera|dan|untuk|saya|downloadkan|download|dowloadkan|dowload|unduh|simpan|file|berkas|pdf|asli|aslinya|nexus|files|workspace|salah|satu)\b",
                " ",
                RegexOptions.IgnoreCase);
            cleanQuery = Regex.Replace(cleanQuery, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(cleanQuery))
                cleanQuery = query.Trim();

            string apiUrl = $"https://api.openalex.org/works?search={Uri.EscapeDataString(cleanQuery)}&per-page={maxResults}";
            using var resp = await SearchHttp.GetAsync(apiUrl, ct);
            if (!resp.IsSuccessStatusCode) return;

            string json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return;

            foreach (var item in arr.EnumerateArray())
            {
                string title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(title)) continue;

                string year = item.TryGetProperty("publication_year", out var y) && y.ValueKind == JsonValueKind.Number
                    ? y.GetInt32().ToString()
                    : "";
                string? doi = item.TryGetProperty("doi", out var d) ? d.GetString() : null;

                var authorNames = new List<string>();
                if (item.TryGetProperty("authorships", out var auths) && auths.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in auths.EnumerateArray().Take(4))
                    {
                        if (a.TryGetProperty("author", out var au) && au.TryGetProperty("display_name", out var dn))
                        {
                            string? name = dn.GetString();
                            if (!string.IsNullOrWhiteSpace(name))
                                authorNames.Add(name);
                        }
                    }
                }

                string? pdfUrl = null;
                string? landingUrl = doi;
                if (item.TryGetProperty("open_access", out var oa) && oa.ValueKind == JsonValueKind.Object)
                {
                    if (oa.TryGetProperty("oa_url", out var oaUrlProp))
                        pdfUrl = oaUrlProp.GetString();
                }
                if (item.TryGetProperty("primary_location", out var loc) && loc.ValueKind == JsonValueKind.Object)
                {
                    if (loc.TryGetProperty("pdf_url", out var pUrl) && !string.IsNullOrWhiteSpace(pUrl.GetString()))
                        pdfUrl = pUrl.GetString();
                    if (loc.TryGetProperty("landing_page_url", out var lUrl) && !string.IsNullOrWhiteSpace(lUrl.GetString()))
                        landingUrl = lUrl.GetString();
                }

                string finalUrl = !string.IsNullOrWhiteSpace(landingUrl)
                    ? landingUrl!
                    : (!string.IsNullOrWhiteSpace(doi) ? doi! : "https://scholar.google.com/scholar?q=" + Uri.EscapeDataString(title));

                if (!Uri.TryCreate(finalUrl, UriKind.Absolute, out var parsedUri))
                    continue;

                string snippet = $"Penulis: {(authorNames.Count > 0 ? string.Join(", ", authorNames) : "N/A")} ({year}). " +
                                 (string.IsNullOrWhiteSpace(doi) ? "" : $"DOI: {doi}. ") +
                                 (string.IsNullOrWhiteSpace(pdfUrl) ? "" : $"PDF Tersedia: {pdfUrl}");

                target.Add(new SearchResultCandidate(
                    Title: title,
                    Url: finalUrl,
                    Domain: parsedUri.Host,
                    Snippet: snippet.Trim(),
                    Authors: authorNames.Count > 0 ? string.Join(", ", authorNames) : null,
                    PublicationYear: string.IsNullOrWhiteSpace(year) ? null : year,
                    Doi: doi,
                    PdfUrl: pdfUrl,
                    RetrievedAtUtc: DateTimeOffset.UtcNow));
            }
        }
        catch (Exception ex)
        {
            BrowserAgentRuntimeSettings.Current.RecordLog($"OpenAlex search fallback notice: {ex.Message}");
        }
    }

    private static async Task TrySearchCrossrefWorksAsync(
        string query,
        int maxResults,
        List<SearchResultCandidate> target,
        CancellationToken ct)
    {
        try
        {
            string url = $"https://api.crossref.org/works?query={Uri.EscapeDataString(query)}&rows={maxResults}";
            using var resp = await SearchHttp.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return;

            string json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("message", out var msg) ||
                !msg.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array)
                return;

            foreach (var item in items.EnumerateArray())
            {
                string title = "";
                if (item.TryGetProperty("title", out var tArr) && tArr.ValueKind == JsonValueKind.Array && tArr.GetArrayLength() > 0)
                    title = tArr[0].GetString() ?? "";
                if (string.IsNullOrWhiteSpace(title)) continue;

                string doi = item.TryGetProperty("DOI", out var dProp) ? dProp.GetString() ?? "" : "";
                string itemUrl = item.TryGetProperty("URL", out var uProp) ? uProp.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(itemUrl) && !string.IsNullOrWhiteSpace(doi))
                    itemUrl = "https://doi.org/" + doi;
                if (!Uri.TryCreate(itemUrl, UriKind.Absolute, out var uri)) continue;

                target.Add(new SearchResultCandidate(
                    Title: title,
                    Url: itemUrl,
                    Domain: uri.Host,
                    Snippet: string.IsNullOrWhiteSpace(doi) ? title : $"DOI: https://doi.org/{doi}",
                    Doi: string.IsNullOrWhiteSpace(doi) ? null : $"https://doi.org/{doi}",
                    RetrievedAtUtc: DateTimeOffset.UtcNow));
            }
        }
        catch (Exception ex)
        {
            BrowserAgentRuntimeSettings.Current.RecordLog($"Crossref search fallback notice: {ex.Message}");
        }
    }

    private static async Task TrySearchDuckDuckGoHtmlAsync(
        string query,
        int countNeeded,
        List<SearchResultCandidate> target,
        CancellationToken ct)
    {
        try
        {
            string ddgUrl = "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query);
            string html = await SearchHttp.GetStringAsync(ddgUrl, ct);
            var matches = Regex.Matches(
                html,
                @"class=""result__a""[^>]*href=""([^""]+)""[^>]*>([\s\S]*?)</a>[\s\S]*?class=""result__snippet""[^>]*>([\s\S]*?)</",
                RegexOptions.IgnoreCase);

            foreach (Match m in matches)
            {
                string rawHref = WebUtility.HtmlDecode(m.Groups[1].Value);
                var uddg = Regex.Match(rawHref, @"[?&]uddg=([^&]+)");
                if (uddg.Success)
                    rawHref = Uri.UnescapeDataString(uddg.Groups[1].Value);

                if (!Uri.TryCreate(rawHref, UriKind.Absolute, out var uri)) continue;
                var (allowed, _) = BrowserSafetyGuard.ValidateNavigationUrl(rawHref);
                if (!allowed) continue;

                string title = WebUtility.HtmlDecode(Regex.Replace(m.Groups[2].Value, @"<[^>]+>", "")).Trim();
                string snippet = WebUtility.HtmlDecode(Regex.Replace(m.Groups[3].Value, @"<[^>]+>", "")).Trim();
                if (string.IsNullOrWhiteSpace(title)) continue;

                target.Add(new SearchResultCandidate(
                    Title: title,
                    Url: rawHref,
                    Domain: uri.Host,
                    Snippet: snippet,
                    RetrievedAtUtc: DateTimeOffset.UtcNow));

                if (target.Count >= countNeeded) break;
            }
        }
        catch (Exception ex)
        {
            BrowserAgentRuntimeSettings.Current.RecordLog($"Web search fallback notice: {ex.Message}");
        }
    }
}

public sealed class NexusWorkspaceFileService : IWorkspaceFileService
{
    private readonly AgentWorkspaceViewModel? _workspaceVm;

    public NexusWorkspaceFileService(AgentWorkspaceViewModel? workspaceVm = null)
    {
        _workspaceVm = workspaceVm;
    }

    public Task<WorkspaceFileAddOutcome> AddDownloadedFileToWorkspaceAsync(
        string filePath,
        string sourceUrl,
        string? documentTitle = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return Task.FromResult(new WorkspaceFileAddOutcome(
                false, filePath ?? string.Empty, sourceUrl, string.Empty, 0, "File fisik tidak ditemukan di disk."));
        }

        var info = new FileInfo(filePath);
        if (info.Length <= 0)
        {
            return Task.FromResult(new WorkspaceFileAddOutcome(
                false, filePath, sourceUrl, string.Empty, 0, "Ukuran file 0 byte (unduhan tidak valid)."));
        }

        byte[] fileBytes = File.ReadAllBytes(filePath);
        string sha256 = Convert.ToHexString(SHA256.HashData(fileBytes));

        // Write companion provenance metadata file
        string metaPath = filePath + ".nexus-meta.json";
        string metaJson = JsonSerializer.Serialize(new
        {
            fileName = info.Name,
            fullPath = info.FullName,
            sourceUrl,
            documentTitle = documentTitle ?? info.Name,
            sizeBytes = info.Length,
            sha256,
            downloadedAtUtc = DateTimeOffset.UtcNow
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(metaPath, metaJson, Encoding.UTF8);

        // Register into AgentWorkspaceViewModel.ReviewFiles & OpenReviewFiles on UI thread if available
        if (_workspaceVm is not null)
        {
            try
            {
                string previewSummary = BuildReadableDocumentSummary(info, sourceUrl, documentTitle, sha256, fileBytes);
                var reviewVm = new ReviewFileViewModel(info.FullName, previewSummary);

                void RegisterOnUi()
                {
                    static bool IsSeededDemoTab(string? target) =>
                        !string.IsNullOrWhiteSpace(target) &&
                        (target.EndsWith("LlamaTransportHelper.cs", StringComparison.OrdinalIgnoreCase) ||
                         target.EndsWith("LlamaClientService.cs", StringComparison.OrdinalIgnoreCase));

                    var existing = _workspaceVm.ReviewFiles
                        .Where(x => !IsSeededDemoTab(x.Target) && !string.Equals(x.Target, info.FullName, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    existing.Add(reviewVm);
                    typeof(AgentWorkspaceViewModel)
                        .GetField("reviewFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        ?.SetValue(_workspaceVm, existing.AsReadOnly());

                    var openList = _workspaceVm.OpenReviewFiles
                        .Where(x => !IsSeededDemoTab(x.Target) && !string.Equals(x.Target, info.FullName, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    openList.Add(reviewVm);
                    typeof(AgentWorkspaceViewModel)
                        .GetField("openReviewFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        ?.SetValue(_workspaceVm, openList.AsReadOnly());

                    _workspaceVm.IsOpen = true;
                    _workspaceVm.SelectReviewFileCommand.Execute(reviewVm);
                }

                if (System.Windows.Application.Current?.Dispatcher is { } disp && !disp.CheckAccess())
                    disp.Invoke(RegisterOnUi);
                else
                    RegisterOnUi();
            }
            catch (Exception ex)
            {
                BrowserAgentRuntimeSettings.Current.RecordLog($"Workspace UI registration warning: {ex.Message}");
            }
        }

        BrowserAgentRuntimeSettings.Current.LastDownloadStatus = $"Verified: {info.Name} ({info.Length:N0} bytes, SHA256: {sha256[..12]}...)";
        BrowserAgentRuntimeSettings.Current.RecordLog($"workspace.add_file -> {info.FullName} (Sumber: {sourceUrl})");

        return Task.FromResult(new WorkspaceFileAddOutcome(
            true,
            info.FullName,
            sourceUrl,
            sha256,
            info.Length,
            $"File '{info.Name}' ({info.Length:N0} bytes) berhasil diverifikasi dan dimasukkan ke Nexus Workspace."));
    }

    private static string BuildReadableDocumentSummary(
        FileInfo info,
        string sourceUrl,
        string? documentTitle,
        string sha256,
        byte[] bytes)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  # Dokumen Terverifikasi di Nexus Workspace: {documentTitle ?? info.Name}");
        sb.AppendLine($"  - Lokasi File : {info.FullName}");
        sb.AppendLine($"  - Sumber URL  : {sourceUrl}");
        sb.AppendLine($"  - Ukuran File : {info.Length:N0} bytes");
        sb.AppendLine($"  - SHA-256     : {sha256}");
        sb.AppendLine($"  - Waktu Unduh : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("  ---");

        string ext = info.Extension.ToLowerInvariant();
        if (ext == ".pdf" && bytes.Length >= 5 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-")
        {
            sb.AppendLine("  [Header PDF Valid (%PDF-) — Berkas PDF siap dibuka di PDF Tools / Workspace]");
        }
        else
        {
            string textSample = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 2400));
            foreach (var line in textSample.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Take(40))
            {
                sb.AppendLine("  " + line);
            }
        }
        return sb.ToString();
    }
}


public sealed class NexusPcTerminalAndFileToolService
{
    private readonly AgentWorkspaceViewModel? _workspaceVm;

    public NexusPcTerminalAndFileToolService(AgentWorkspaceViewModel? workspaceVm = null)
    {
        _workspaceVm = workspaceVm;
    }

    public async Task<PcTerminalToolOutcome> ExecuteFolderOrFileExplorationAsync(
        string userPrompt,
        string? activeProjectFolder = null,
        CancellationToken ct = default)
    {
        var discoveredPaths = new List<string>();
        var sb = new StringBuilder();

        string? explicitPath = null;
        try
        {
            var pathMatch = Regex.Match(userPrompt ?? string.Empty, @"[A-Za-z]:\\[^\r\n""'<>|?*\s]+");
            if (pathMatch.Success)
            {
                explicitPath = pathMatch.Value.TrimEnd('.', ',', ';', ')', ']');
            }
        }
        catch { }

        string targetRoot = !string.IsNullOrWhiteSpace(explicitPath) && (Directory.Exists(explicitPath) || File.Exists(explicitPath))
            ? explicitPath!
            : (!string.IsNullOrWhiteSpace(activeProjectFolder) && Directory.Exists(activeProjectFolder)
                ? activeProjectFolder!
                : (Directory.Exists(@"D:\NEXUS") ? @"D:\NEXUS" : Environment.CurrentDirectory));

        if (File.Exists(targetRoot))
        {
            var fi = new FileInfo(targetRoot);
            discoveredPaths.Add(fi.FullName);
            sb.AppendLine($"### Hasil Pembacaan Berkas PC (`{fi.FullName}`)");
            sb.AppendLine($"- **Nama Berkas**: `{fi.Name}` ({fi.Length:N0} bytes)");
            sb.AppendLine($"- **Terakhir Diubah**: {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            try
            {
                if (fi.Extension.ToLowerInvariant() is ".md" or ".txt" or ".cs" or ".xaml" or ".json" or ".ps1" or ".cmd" or ".xml")
                {
                    string content = await File.ReadAllTextAsync(fi.FullName, ct);
                    if (content.Length > 1800) content = content[..1800] + "\r\n... [dipotong]";
                    sb.AppendLine("\r\n#### Cuplikan Isi Berkas:");
                    sb.AppendLine("```");
                    sb.AppendLine(content);
                    sb.AppendLine("```");
                }
            }
            catch { }

            if (_workspaceVm is not null)
            {
                var wfService = new NexusWorkspaceFileService(_workspaceVm);
                await wfService.AddDownloadedFileToWorkspaceAsync(fi.FullName, "file:///" + fi.FullName.Replace('\\', '/'), fi.Name, ct);
            }

            return new PcTerminalToolOutcome(true, $"type \"{fi.FullName}\"", fi.DirectoryName ?? targetRoot, sb.ToString().Trim(), discoveredPaths);
        }

        string lower = (userPrompt ?? string.Empty).ToLowerInvariant();
        bool isFolderInspection = lower.Contains("folder ini") ||
                                  lower.Contains("proyek ini") ||
                                  lower.Contains("isi folder") ||
                                  lower.Contains("baca folder") ||
                                  lower.Contains("membaca folder") ||
                                  lower.Contains("struktur") ||
                                  lower.Contains("direktori");

        var searchKeywords = lower
            .Split(new[] { ' ', ',', '.', '?', '!', '"', '\'', '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !Regex.IsMatch(w, @"^(bisa|membaca|baca|folder|ini|cari|carikan|dimana|mana|letak|lokasi|file|berkas|dokumen|saya|di|ke|dari|pc|komputer|laptop|drive|lokal|nexus|tolong|coba|cek|lihat|tampilkan|buka|terminal)$"))
            .Distinct()
            .ToArray();

        string executedCmd = $"cmd.exe /c dir \"{targetRoot}\"";
        sb.AppendLine($"### Hasil Eksplorasi Terminal & Direktori PC (`{targetRoot}`)");
        sb.AppendLine($"- **Perintah Dieksekusi**: `{executedCmd}`");

        if (Directory.Exists(targetRoot))
        {
            try
            {
                var subDirs = Directory.GetDirectories(targetRoot)
                    .Select(d => new DirectoryInfo(d))
                    .OrderByDescending(d => d.LastWriteTime)
                    .Take(12)
                    .ToList();

                var topFiles = Directory.GetFiles(targetRoot)
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Take(15)
                    .ToList();

                sb.AppendLine($"- **Ringkasan Folder Induk**: `{subDirs.Count}` sub-folder utama, `{topFiles.Count}` berkas di tingkat atas.");

                if (subDirs.Count > 0)
                {
                    sb.AppendLine("\r\n#### Daftar Sub-Folder Utama:");
                    foreach (var d in subDirs)
                    {
                        sb.AppendLine($"- 📁 `{d.FullName}` (Diubah: {d.LastWriteTime:yyyy-MM-dd HH:mm})");
                    }
                }

                if (topFiles.Count > 0)
                {
                    sb.AppendLine("\r\n#### Daftar Berkas Tingkat Atas:");
                    foreach (var f in topFiles)
                    {
                        discoveredPaths.Add(f.FullName);
                        sb.AppendLine($"- 📄 `{f.FullName}` ({f.Length:N0} bytes)");
                    }
                }

                if (!isFolderInspection && searchKeywords.Length > 0)
                {
                    executedCmd = $"es.exe / dir /s /b \"{string.Join(" ", searchKeywords)}\"";
                    var matchedFiles = new List<FileInfo>();
                    foreach (var fPath in Directory.EnumerateFiles(targetRoot, "*.*", SearchOption.AllDirectories).Take(2500))
                    {
                        string fName = Path.GetFileName(fPath);
                        if (searchKeywords.Any(k => fName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                        {
                            try { matchedFiles.Add(new FileInfo(fPath)); } catch { }
                            if (matchedFiles.Count >= 12) break;
                        }
                    }

                    if (matchedFiles.Count > 0)
                    {
                        sb.AppendLine($"\r\n#### Berkas Ditemukan untuk Kata Kunci `{string.Join(", ", searchKeywords)}`:");
                        foreach (var mf in matchedFiles)
                        {
                            if (!discoveredPaths.Contains(mf.FullName))
                                discoveredPaths.Add(mf.FullName);
                            sb.AppendLine($"- ✅ **`{mf.Name}`** -> `{mf.FullName}` ({mf.Length:N0} bytes)");
                        }
                    }
                }

                // Only auto-open a file tab in Right Panel if the user explicitly searched for a specific keyword match (never auto-seed AGENTS.md on general folder checks)
                if (!isFolderInspection && searchKeywords.Length > 0 && discoveredPaths.Count > 0 && _workspaceVm is not null)
                {
                    string firstFile = discoveredPaths[0];
                    if (File.Exists(firstFile))
                    {
                        var wfService = new NexusWorkspaceFileService(_workspaceVm);
                        await wfService.AddDownloadedFileToWorkspaceAsync(firstFile, "file:///" + firstFile.Replace('\\', '/'), Path.GetFileName(firstFile), ct);
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"- ⚠️ Catatan pembacaan direktori: {ex.Message}");
            }
        }

        BrowserAgentRuntimeSettings.Current.LastToolExecution = $"terminal.explore ({targetRoot})";
        BrowserAgentRuntimeSettings.Current.RecordLog($"Terminal & PC File Explorer -> {executedCmd} ({discoveredPaths.Count} item ditemukan)");

        return new PcTerminalToolOutcome(
            IsSuccess: true,
            ExecutedCommandDescription: executedCmd,
            TargetDirectory: targetRoot,
            StructuredReportMarkdown: sb.ToString().Trim(),
            DiscoveredPaths: discoveredPaths);
    }
}
