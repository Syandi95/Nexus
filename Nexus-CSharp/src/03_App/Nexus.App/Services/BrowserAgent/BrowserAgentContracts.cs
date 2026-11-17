global using Nexus.Contracts.BrowserAgent;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexus.App.Services.BrowserAgent;

// ============================================================================
// LAPIS 3 (03_App): UI-BOUND RUNTIME SETTINGS & WPF DISPATCHER OBSERVABILITY
// Pure contracts reside in 00_Contracts (Nexus.Contracts.BrowserAgent).
// Pure Browser Agent engines reside in 02_Modules (Nexus.Core.HomeSettings/BrowserAgent).
// ============================================================================

public sealed class BrowserAgentRuntimeSettings
{
    public static BrowserAgentRuntimeSettings Current { get; } = new();

    public string AdapterMode { get; set; } = "WebView2Native"; // "WebView2Native" or "PlaywrightCdp"
    public bool AutoAllowReadAndNavigate { get; set; } = true;
    public bool RequireConfirmationForDownload { get; set; } = false;
    public bool RequireConfirmationForFormSubmit { get; set; } = true;
    public int MaxActionsPerTask { get; set; } = 25;
    public int MaxPagesPerTask { get; set; } = 12;
    public int MaxContextTokens { get; set; } = 1200;
    public int NavigationTimeoutSeconds { get; set; } = 25;
    public int TotalTaskTimeoutSeconds { get; set; } = 180;
    public int MaxRetries { get; set; } = 2;
    public string DefaultSearchProvider { get; set; } = "scholar";
    public string DownloadDirectory { get; set; } = Directory.Exists(@"D:\NEXUS")
        ? @"D:\NEXUS\nexus_data\downloads"
        : Path.Combine(Path.GetTempPath(), "nexus_downloads");

    // Live Bridges to WebView2 UI Thread & Active LLM StepPlanner (v23 + Batch B)
    public Func<string, Task<string>>? LiveJsEvaluator { get; set; }
    public Func<string, string, Task<string>>? LiveCdpCaller { get; set; }
    public Func<string, string, CancellationToken, Task<string>>? LiveLlmStepPlannerCaller { get; set; }
    public Action<string>? OpenUrlInRightPanelBrowser { get; set; }
    public Func<bool, Task>? ClearBrowserSessionHistoryAsync { get; set; }
    public VerifiedCarryForwardStore CarryForward { get; } = new();
    public Action<string>? ClipboardSetterOverride { get; set; }
    public Action? CancelActiveAgentRunCallback { get; set; }
    public Func<Task<bool>>? StopLiveWebView2NavigationCallback { get; set; }
    public bool AutoClearHistoryOnNewChat { get; set; } = false;
    public string AdGuardExtensionId { get; set; } = string.Empty;
    public string AdGuardPopupExtensionUrl { get; set; } = string.Empty;
    public string AdGuardOptionsExtensionUrl { get; set; } = string.Empty;
    public bool IsAdGuardProtectionEnabled { get; set; } = true;
    public ObservableCollection<string> AdGuardWhitelistedDomains { get; } = new();

    public bool IsDomainWhitelisted(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        string clean = domain.Trim().ToLowerInvariant();
        return AdGuardWhitelistedDomains.Any(d => string.Equals(d, clean, StringComparison.OrdinalIgnoreCase)) ||
               AdGuardConfig.AllowlistDomains.Any(d => string.Equals(d, clean, StringComparison.OrdinalIgnoreCase));
    }
    public Func<string, Task>? OpenAdGuardExtensionPageInBrowserAsync { get; set; }
    public List<BrowsingHistoryItem> BrowsingHistoryEntries { get; } = new();
    public List<BrowserTabSessionItem> OpenBrowserTabs { get; } = new();
    public string ActiveBrowserTabId { get; set; } = string.Empty;
    public AdGuardPersistentConfig AdGuardConfig { get; private set; } = new();
    public string HomePageUrl { get; set; } = "https://www.google.com";
    public double LastCursorX { get; set; } = 420;
    public double LastCursorY { get; set; } = 220;
    public int ActiveLlamaServerPort { get; set; } = 8080;
    public bool EnableGenericFastPath { get; set; } = true;
    public bool EnableCompactDomPrompt { get; set; } = true;
    public bool EnablePromptCache { get; set; } = true;
    public bool EnableLocalModelThinking { get; set; } = false;
    public bool AlwaysPierceCdpInBaselineMode { get; set; } = false;
    public LlmInferenceTelemetry? LastStepInferenceTelemetry { get; set; }
    private bool _historyLoadedFromDisk;
    private bool _adGuardConfigLoadedFromDisk;

    private static string GetPersistentHistoryFilePath()
    {
        string dir = Directory.Exists(@"D:\NEXUS")
            ? @"D:\NEXUS\nexus_data"
            : Path.Combine(Path.GetTempPath(), "nexus_data");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "browsing_history.json");
    }

    private static string GetPersistentAdGuardConfigFilePath()
    {
        string dir = Directory.Exists(@"D:\NEXUS")
            ? @"D:\NEXUS\nexus_data"
            : Path.Combine(Path.GetTempPath(), "nexus_data");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "adguard_settings.json");
    }

    public void EnsureHistoryLoadedFromDisk()
    {
        if (_historyLoadedFromDisk) return;
        _historyLoadedFromDisk = true;
        try
        {
            string path = GetPersistentHistoryFilePath();
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<List<BrowsingHistoryItem>>(File.ReadAllText(path, Encoding.UTF8));
                if (loaded is not null)
                {
                    BrowsingHistoryEntries.Clear();
                    BrowsingHistoryEntries.AddRange(loaded);
                }
            }
        }
        catch { }
    }

    public void SaveHistoryToDisk()
    {
        try
        {
            string path = GetPersistentHistoryFilePath();
            string json = JsonSerializer.Serialize(BrowsingHistoryEntries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, Encoding.UTF8);
        }
        catch { }
    }

    public void EnsureAdGuardConfigLoadedFromDisk()
    {
        if (_adGuardConfigLoadedFromDisk) return;
        _adGuardConfigLoadedFromDisk = true;
        try
        {
            string path = GetPersistentAdGuardConfigFilePath();
            if (File.Exists(path))
            {
                var cfg = JsonSerializer.Deserialize<AdGuardPersistentConfig>(File.ReadAllText(path, Encoding.UTF8));
                if (cfg is not null)
                {
                    AdGuardConfig = cfg;
                    SyncAllowlistFromConfig();
                }
            }
        }
        catch { }
    }

    public void IncrementBlockedAdsCount(int delta = 1)
    {
        if (delta <= 0) return;
        EnsureAdGuardConfigLoadedFromDisk();
        AdGuardConfig.TotalBlockedAdsCount += delta;
        SaveAdGuardConfigToDisk();
    }

    public void SaveAdGuardConfigToDisk()
    {
        try
        {
            SyncAllowlistFromConfig();
            string path = GetPersistentAdGuardConfigFilePath();
            string json = JsonSerializer.Serialize(AdGuardConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, Encoding.UTF8);
        }
        catch { }
    }

    private void SyncAllowlistFromConfig()
    {
        try
        {
            AdGuardWhitelistedDomains.Clear();
            if (!string.IsNullOrWhiteSpace(AdGuardConfig.AllowlistText))
            {
                foreach (var line in AdGuardConfig.AllowlistText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string d = line.Trim().ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(d) && !AdGuardWhitelistedDomains.Contains(d))
                        AdGuardWhitelistedDomains.Add(d);
                }
            }
        }
        catch { }
    }

    public void RecordBrowsingHistory(string? title, string? url)
    {
        EnsureHistoryLoadedFromDisk();
        if (string.IsNullOrWhiteSpace(url) ||
            url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("nexus/settings/", StringComparison.OrdinalIgnoreCase))
            return;

        string cleanUrl = url.Trim();
        string domain = cleanUrl;
        if (cleanUrl.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
        {
            domain = "bgnkhhnnamicmpeenaelnjfhikgbkllg";
        }
        else if (Uri.TryCreate(cleanUrl, UriKind.Absolute, out var u) && !string.IsNullOrWhiteSpace(u.Host))
        {
            domain = u.Host.ToLowerInvariant();
        }
        string cleanTitle = !string.IsNullOrWhiteSpace(title) ? title.Trim() : cleanUrl;
        DateTime nowUtc = DateTime.UtcNow;
        DateTime nowLocal = DateTime.Now;

        // Avoid duplicate consecutive entry within the same minute
        BrowsingHistoryEntries.RemoveAll(x => string.Equals(x.Url, cleanUrl, StringComparison.OrdinalIgnoreCase) &&
                                              x.TimeLabel == nowLocal.ToString("HH:mm"));
        BrowsingHistoryEntries.Insert(0, new BrowsingHistoryItem(
            cleanTitle,
            cleanUrl,
            domain,
            nowLocal.ToString("HH:mm"),
            nowLocal.ToString("d MMM yyyy"),
            nowUtc));
        while (BrowsingHistoryEntries.Count > 250)
            BrowsingHistoryEntries.RemoveAt(BrowsingHistoryEntries.Count - 1);
        SaveHistoryToDisk();
    }

    public int DeleteBrowsingHistoryByTimeRange(string timeRange, bool deleteHistory = true, bool deleteDownloads = false)
    {
        EnsureHistoryLoadedFromDisk();
        int removed = 0;
        if (deleteHistory)
        {
            DateTime nowUtc = DateTime.UtcNow;
            TimeSpan? window = timeRange switch
            {
                "1h" or "Last hour" => TimeSpan.FromHours(1),
                "24h" or "Last 24 hours" => TimeSpan.FromHours(24),
                "7d" or "Last 7 days" => TimeSpan.FromDays(7),
                "4w" or "Last 4 weeks" => TimeSpan.FromDays(28),
                _ => null
            };

            if (window is null)
            {
                removed = BrowsingHistoryEntries.Count;
                BrowsingHistoryEntries.Clear();
            }
            else
            {
                DateTime cutoff = nowUtc - window.Value;
                removed = BrowsingHistoryEntries.RemoveAll(x => x.VisitedAtUtc == default || x.VisitedAtUtc >= cutoff);
            }
            SaveHistoryToDisk();
        }
        return removed;
    }

    public bool DeleteSingleHistoryItem(string url, string timeLabel)
    {
        EnsureHistoryLoadedFromDisk();
        int rem = BrowsingHistoryEntries.RemoveAll(x =>
            string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrEmpty(timeLabel) || string.Equals(x.TimeLabel, timeLabel, StringComparison.OrdinalIgnoreCase)));
        if (rem > 0) SaveHistoryToDisk();
        return rem > 0;
    }

    public static bool IsUserVisibleDownloadFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        return !fileName.EndsWith(".nexus-meta.json", StringComparison.OrdinalIgnoreCase) &&
               !fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) &&
               !fileName.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) &&
               !fileName.StartsWith(".", StringComparison.Ordinal);
    }

    public bool DeleteSingleDownloadedFile(string fileName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            string safeName = Path.GetFileName(fileName);
            string fullPath = Path.Combine(DownloadDirectory, safeName);
            string metaPath = fullPath + ".nexus-meta.json";
            bool deleted = false;
            if (File.Exists(fullPath)) { File.Delete(fullPath); deleted = true; }
            if (File.Exists(metaPath)) { File.Delete(metaPath); }
            return deleted;
        }
        catch
        {
            return false;
        }
    }

    public int ClearAllDownloadedFiles()
    {
        int count = 0;
        try
        {
            if (!Directory.Exists(DownloadDirectory)) return 0;
            foreach (var f in Directory.GetFiles(DownloadDirectory))
            {
                try { File.Delete(f); count++; } catch { }
            }
        }
        catch { }
        return count;
    }

    public int ImportRealHistoryFromLocalBrowsers()
    {
        int imported = 0;
        try
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] candidates = new[]
            {
                Path.Combine(localApp, "Microsoft", "Edge", "User Data", "Default", "History"),
                Path.Combine(localApp, "Google", "Chrome", "User Data", "Default", "History")
            };

            foreach (string histFile in candidates)
            {
                if (!File.Exists(histFile)) continue;
                string tmpCopy = Path.Combine(Path.GetTempPath(), "nexus_hist_import_" + Guid.NewGuid().ToString("N") + ".db");
                try
                {
                    File.Copy(histFile, tmpCopy, overwrite: true);
                    byte[] rawBytes = File.ReadAllBytes(tmpCopy);
                    string ascii = Encoding.UTF8.GetString(rawBytes);
                    var matches = Regex.Matches(ascii, @"https?://[a-zA-Z0-9\.\-_/\?\=&%#]{6,140}");
                    foreach (Match m in matches)
                    {
                        string u = m.Value;
                        if (u.Contains("google.com/complete") || u.Contains("gstatic.com") || u.Contains("googleapis.com") || u.Contains("bing.com/fd/"))
                            continue;
                        if (Uri.TryCreate(u, UriKind.Absolute, out var parsed))
                        {
                            RecordBrowsingHistory(parsed.Host, u);
                            imported++;
                            if (imported >= 25) break;
                        }
                    }
                }
                catch { }
                finally
                {
                    try { File.Delete(tmpCopy); } catch { }
                }
                if (imported > 0) break;
            }
        }
        catch { }
        return imported;
    }
    public string ClipboardMemory { get; set; } = string.Empty;
    public ObservableCollection<string> ClosedPopupMessages { get; } = new();
    public HumanTakeoverControlState TakeTheWheel { get; } = new();
    public Dictionary<string, StagehandActionCacheEntry> SelfHealingActionCache { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<AgentStepDecision> LastSuccessfulRunSteps { get; } = new();

    // Live Observability State for UI Settings (Section 11)
    public string BrowserConnectionState { get; set; } = "Connected (WebView2 Right Panel)";
    public string CurrentUrl { get; set; } = string.Empty;
    public string CurrentPageTitle { get; set; } = string.Empty;
    public string CurrentAgentActivity { get; set; } = "Idle";
    public string LastToolExecution { get; set; } = "None";
    public string LastDownloadStatus { get; set; } = "None";
    public string LastErrorState { get; set; } = "None";
    public ObservableCollection<string> ActivityLogs { get; } = new();
    public ObservableCollection<BrowserVirtualTab> OpenTabs { get; } = new();
    public List<HermesProceduralSkill> ProceduralSkills { get; } = new();
    public List<BrowserAgentBenchmarkRecord> BenchmarkHistory { get; } = new();

    public void RecordLog(string entry)
    {
        string stamped = $"[{DateTime.Now:HH:mm:ss}] {entry}";
        try
        {
            if (System.Windows.Application.Current?.Dispatcher is { } disp && !disp.CheckAccess())
            {
                disp.BeginInvoke(() =>
                {
                    ActivityLogs.Insert(0, stamped);
                    while (ActivityLogs.Count > 100) ActivityLogs.RemoveAt(ActivityLogs.Count - 1);
                });
                return;
            }
        }
        catch { }

        ActivityLogs.Insert(0, stamped);
        while (ActivityLogs.Count > 100) ActivityLogs.RemoveAt(ActivityLogs.Count - 1);
    }
}


