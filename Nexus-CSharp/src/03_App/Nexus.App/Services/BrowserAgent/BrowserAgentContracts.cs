using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.App.Services.BrowserAgent;

public sealed record SearchResultCandidate(
    string Title,
    string Url,
    string Domain,
    string Snippet,
    string? Authors = null,
    string? PublicationYear = null,
    string? Doi = null,
    string? PdfUrl = null,
    DateTimeOffset RetrievedAtUtc = default);

public sealed record BrowserExtractedContent(
    string Url,
    string Title,
    string SnapshotId,
    string CleanSemanticText,
    string AriaSnapshotYaml,
    IReadOnlyList<BrowserElementRef> InteractiveElements,
    IReadOnlyList<SearchResultCandidate> ExtractedCitations,
    int RawCharCount,
    int ExtractedCharCount,
    int RawTokenCount,
    int ExtractedTokenCount,
    long ExtractionLatencyMs,
    bool PromptInjectionNeutralized);

public sealed record BrowserDownloadOutcome(
    bool IsSuccess,
    string SourceUrl,
    string LocalFilePath,
    string FileName,
    long FileSizeBytes,
    string ContentType,
    string Sha256Hex,
    bool IsDuplicate,
    string Message,
    string? ErrorCode = null);

public sealed record WorkspaceFileAddOutcome(
    bool IsSuccess,
    string FilePath,
    string SourceUrl,
    string Sha256Hex,
    long FileSizeBytes,
    string Message);

public sealed record BrowserToolCallRequest(
    string ToolName,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record BrowserToolCallResult(
    string ToolName,
    bool IsSuccess,
    string StructuredOutput,
    string? ErrorMessage,
    long LatencyMs,
    string CurrentUrl,
    string CurrentTitle,
    string SnapshotId);

public sealed record BrowserAgentBenchmarkRecord(
    string TaskDescription,
    int RawContentTokens,
    int ExtractedContentTokens,
    int CompletePromptTokens,
    long ExtractionLatencyMs,
    long BrowserActionLatencyMs,
    long LocalModelTtftMs,
    double TokensPerSecond,
    int ActionsExecuted,
    int PagesOpened,
    bool TaskSucceeded,
    string TokenizerSource,
    DateTimeOffset RecordedAtUtc);


// ============================================================================
// v23 DYNAMIC BROWSER-USE & HERMES CONTROL LOOP CONTRACTS (Revisions 1-4 + LLM StepPlanner)
// ============================================================================

public sealed record BrowserVirtualTab(
    string TabId,
    string Title,
    string Url,
    string RoleTag,
    bool IsActive,
    DateTimeOffset OpenedAtUtc,
    int ScrollY = 0);

public sealed record InteractiveDomNode(
    int Index,
    string TagName,
    string Role,
    string TextOrLabel,
    string? InputType,
    string? Href,
    bool IsContentEditable,
    bool IsSensitiveOrSubmit,
    string XPath,
    bool IsConnectedInDom = true,
    bool IsNewElement = false,
    string? CompoundInfo = null);

public sealed record BrowserLiveStateSnapshot(
    string SnapshotId,
    string CurrentUrl,
    string PageTitle,
    int ActiveTabIndex,
    IReadOnlyList<BrowserVirtualTab> Tabs,
    IReadOnlyList<InteractiveDomNode> Elements,
    string CleanMarkdownViewportText,
    int PixelsAbove,
    int PixelsBelow);

public sealed record AgentStepDecision(
    string EvaluationPreviousGoal,
    string Memory,
    string NextGoal,
    string ActionName, // 12 Core Actions + browser-use Extended Actions (search_page, find_elements, send_keys, scroll_to_text, wait, copy_text)
    int? ElementIndex = null,
    string? TextOrUrl = null,
    bool PressEnter = false,
    IReadOnlyList<AgentStepDecision>? QueuedFollowUpActions = null,
    IReadOnlyList<string>? PlanUpdate = null);

/// <summary>
/// Ported from browser-use/agent/views.py (PageFingerprint):
/// Lightweight fingerprint of the browser page state (URL + element count + 16-char SHA-256 of DOM text).
/// </summary>
public sealed record PageFingerprint(
    string Url,
    int ElementCount,
    string TextHash)
{
    public static PageFingerprint FromBrowserState(string? url, string? domText, int elementCount)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(domText ?? string.Empty);
        string hashHex = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (hashHex.Length > 16)
            hashHex = hashHex[..16];
        return new PageFingerprint(url ?? string.Empty, elementCount, hashHex);
    }
}

/// <summary>
/// Ported from browser-use/agent/views.py (ActionLoopDetector):
/// Tracks normalized action hashes and PageFingerprints across a rolling window to generate escalating awareness nudges.
/// </summary>
public sealed class BrowserUseActionLoopDetector
{
    private readonly int _windowSize;
    private readonly List<string> _recentActionHashes = new();
    private readonly List<PageFingerprint> _recentFingerprints = new();

    public int MaxRepetitionCount { get; private set; }
    public int ConsecutiveStagnantPages { get; private set; }

    public BrowserUseActionLoopDetector(int windowSize = 20)
    {
        _windowSize = Math.Max(5, windowSize);
    }

    public static string ComputeNormalizedActionHash(string? actionName, int? elementIndex, string? textOrUrl)
    {
        string normAct = BrowserUseDomEngine.NormalizeBrowserUseActionName(actionName);
        if (normAct is "wait" or "done" or "go_back")
            return string.Empty;

        string normalizedPayload;
        if (normAct == "search_web")
        {
            var tokens = System.Text.RegularExpressions.Regex
                .Replace((textOrUrl ?? string.Empty).ToLowerInvariant(), @"[^\w\s]", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal);
            normalizedPayload = $"search_web|{string.Join("|", tokens)}";
        }
        else if (normAct == "input_text")
        {
            normalizedPayload = $"input_text|{elementIndex}|{(textOrUrl ?? string.Empty).Trim().ToLowerInvariant()}";
        }
        else if (normAct == "click_element")
        {
            normalizedPayload = $"click_element|{elementIndex}";
        }
        else
        {
            normalizedPayload = $"{normAct}|{elementIndex}|{(textOrUrl ?? string.Empty).Trim()}";
        }

        string hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPayload))).ToLowerInvariant();
        return hex.Length > 12 ? hex[..12] : hex;
    }

    public void RecordAction(string? actionName, int? elementIndex, string? textOrUrl)
    {
        string h = ComputeNormalizedActionHash(actionName, elementIndex, textOrUrl);
        if (string.IsNullOrEmpty(h))
            return;

        _recentActionHashes.Add(h);
        while (_recentActionHashes.Count > _windowSize)
            _recentActionHashes.RemoveAt(0);

        MaxRepetitionCount = _recentActionHashes
            .GroupBy(x => x, StringComparer.Ordinal)
            .Select(g => g.Count())
            .DefaultIfEmpty(0)
            .Max();
    }

    public void RecordPageState(string? url, string? domText, int elementCount)
    {
        var fp = PageFingerprint.FromBrowserState(url, domText, elementCount);
        if (_recentFingerprints.Count > 0 && _recentFingerprints[^1] == fp)
            ConsecutiveStagnantPages++;
        else
            ConsecutiveStagnantPages = 0;

        _recentFingerprints.Add(fp);
        while (_recentFingerprints.Count > 5)
            _recentFingerprints.RemoveAt(0);
    }

    public string? GetNudgeMessage()
    {
        var msgs = new List<string>();
        if (MaxRepetitionCount >= 5)
        {
            msgs.Add($"PERINGATAN LOOP (browser-use detector): Anda telah mengulangi aksi serupa sebanyak {MaxRepetitionCount} kali dalam {_recentActionHashes.Count} langkah terakhir. Jangan ulangi elemen/aksi yang sama; gunakan pendekatan alternatif (mis. search_page, scroll_down, atau go_to_url).");
        }
        if (ConsecutiveStagnantPages >= 3)
        {
            msgs.Add($"PERINGATAN STAGNASI HALAMAN: Konten halaman tidak berubah selama {ConsecutiveStagnantPages} langkah berturut-turut. Coba elemen lain atau navigasi langsung.");
        }
        return msgs.Count > 0 ? string.Join("\r\n", msgs) : null;
    }
}

public sealed record PendingApprovalRequest(
    string SnapshotId,
    int? ElementIndex,
    string ActionName,
    string TargetDescription,
    string CurrentUrl,
    string Reason);

public enum ActionVerificationStatus
{
    Attempted,
    Executed,
    Verified,
    Failed
}

public sealed record RequestedSubItemDescriptor(
    string SubItemKind,
    string RawPhrase,
    int? NumberValue,
    string KeywordToken);

public sealed record BrowserStepActionResult(
    string ActionName,
    bool IsError,
    string ResultMessage,
    string UrlBefore,
    string UrlAfter,
    bool DomOrUrlChanged,
    bool DownloadTriggered,
    bool UsedXPathFallback,
    bool InterceptedForApproval,
    BrowserDownloadOutcome? DownloadOutcome = null,
    WorkspaceFileAddOutcome? WorkspaceOutcome = null,
    ActionVerificationStatus VerificationStatus = ActionVerificationStatus.Verified,
    string? VerificationEvidence = null);

public sealed record HermesProceduralSkill(
    string SkillId,
    string Title,
    string Description,
    IReadOnlyList<string> TriggerKeywords,
    IReadOnlyList<string> OrderedSteps,
    string SkillFilePath);

public sealed record InstalledBrowserExtensionInfo(
    string Id,
    string Name,
    string Version,
    bool IsEnabled,
    string FolderPath,
    string SourceKind);

public sealed record UnifiedAiIntentDecision(
    string Mode,
    string? TargetUrl,
    string? CleanInputText,
    string? FollowUpGoal,
    string? TerminalOrPathQuery,
    int? RequestedItemNumber);

public sealed record BrowsingHistoryItem(
    string Title,
    string Url,
    string Domain,
    string TimeLabel,
    string DateGroupLabel,
    DateTime VisitedAtUtc = default);

public sealed record BrowserTabSessionItem(
    string TabId,
    string Title,
    string Url,
    string IconKind,
    bool IsInternalPage,
    string? InternalPageKind = null);

public sealed class AdGuardPersistentConfig
{
    public string Theme { get; set; } = "System default";
    public bool BlockSearchAds { get; set; } = true;
    public bool SendAnonStats { get; set; } = false;
    public bool FilterBaseEnabled { get; set; } = true;
    public bool FilterPrivacyEnabled { get; set; } = true;
    public bool FilterSocialEnabled { get; set; } = true;
    public bool FilterSecurityEnabled { get; set; } = true;
    public bool FilterIndoEnabled { get; set; } = true;
    public bool StripTrackingParams { get; set; } = true;
    public bool HideSearchReferrer { get; set; } = true;
    public bool SendDntHeader { get; set; } = true;
    public bool BlockWebRtcIp { get; set; } = false;
    public bool ShowBadgeCount { get; set; } = true;
    public bool ContextMenuEnabled { get; set; } = true;
    public bool UnlockRightClickCopy { get; set; } = true;
    public string AllowlistText { get; set; } = "";
    public string CustomUserRulesText { get; set; } = "! AdGuard MV3 Custom Rules\n||doubleclick.net^\n||googlesyndication.com^\nyoutube.com##.ytp-ad-module, .video-ads";
    public bool ShowFullUrlInAddressBar { get; set; } = true;
    public bool OfferToSavePasswords { get; set; } = true;
    public bool AutoCreatePasskeys { get; set; } = true;
    public bool SignInAutomatically { get; set; } = true;
    public bool UseWindowsHelloForPasswords { get; set; } = false;
    public bool DeveloperModeExtensions { get; set; } = false;
    public bool PinNewExtensionsToToolbar { get; set; } = true;
    public int TotalBlockedAdsCount { get; set; } = 0;
    public bool BlockSearchAdsAndSelfPromo { get => BlockSearchAds; set => BlockSearchAds = value; }
    public bool AutoActivateLanguageFilters { get; set; } = true;
    public bool StealthModeEnabled { get; set; } = true;
    public bool HideSearchQueries { get => HideSearchReferrer; set => HideSearchReferrer = value; }
    public bool SendDoNotTrackHeader { get => SendDntHeader; set => SendDntHeader = value; }
    public bool FilterAdBlocking { get => FilterBaseEnabled; set => FilterBaseEnabled = value; }
    public bool FilterPrivacy { get => FilterPrivacyEnabled; set => FilterPrivacyEnabled = value; }
    public bool FilterSocialWidgets { get => FilterSocialEnabled; set => FilterSocialEnabled = value; }
    public bool FilterAnnoyances { get; set; } = true;
    public bool FilterSecurity { get => FilterSecurityEnabled; set => FilterSecurityEnabled = value; }
    public bool FilterLanguageSpecific { get => FilterIndoEnabled; set => FilterIndoEnabled = value; }
    public List<string> AllowlistDomains { get; set; } = new();
    public List<string> CustomUserRules { get; set; } = new() { "! AdGuard MV3 Custom Rules", "||doubleclick.net^", "||googlesyndication.com^", "youtube.com##.ytp-ad-module, .video-ads" };

}

public sealed record PcTerminalToolOutcome(
    bool IsSuccess,
    string ExecutedCommandDescription,
    string TargetDirectory,
    string StructuredReportMarkdown,
    IReadOnlyList<string> DiscoveredPaths);

public sealed class BrowserAgentRuntimeSettings
{
    public static BrowserAgentRuntimeSettings Current { get; } = new();

    public string AdapterMode { get; set; } = "WebView2Native"; // "WebView2Native" or "PlaywrightCdp"
    public bool AutoAllowReadAndNavigate { get; set; } = true;
    public bool RequireConfirmationForDownload { get; set; } = false;
    public bool RequireConfirmationForFormSubmit { get; set; } = true;
    public int MaxActionsPerTask { get; set; } = 8;
    public int MaxPagesPerTask { get; set; } = 5;
    public int MaxContextTokens { get; set; } = 1200;
    public int NavigationTimeoutSeconds { get; set; } = 15;
    public int TotalTaskTimeoutSeconds { get; set; } = 90;
    public int MaxRetries { get; set; } = 2;
    public string DefaultSearchProvider { get; set; } = "scholar";
    public string DownloadDirectory { get; set; } = Directory.Exists(@"D:\NEXUS")
        ? @"D:\NEXUS\nexus_data\downloads"
        : Path.Combine(Path.GetTempPath(), "nexus_downloads");

    // Live Bridges to WebView2 UI Thread & Active LLM StepPlanner (v23)
    public Func<string, Task<string>>? LiveJsEvaluator { get; set; }
    public Func<string, string, CancellationToken, Task<string>>? LiveLlmStepPlannerCaller { get; set; }
    public Action<string>? OpenUrlInRightPanelBrowser { get; set; }
    public Func<bool, Task>? ClearBrowserSessionHistoryAsync { get; set; }
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

public interface IBrowserAutomation
{
    Task<BrowserPageState> GetStateAsync(CancellationToken ct = default);
    Task<BrowserActionOutcome> NavigateAsync(string url, CancellationToken ct = default);
    Task<BrowserPageState> GetSnapshotAsync(int maxElements = 40, CancellationToken ct = default);
    Task<BrowserExtractedContent> ExtractContentAsync(string? queryFocus = null, int maxChars = 2400, CancellationToken ct = default);
    Task<BrowserActionOutcome> ClickAsync(string snapshotId, string elementRef, CancellationToken ct = default);
    Task<BrowserActionOutcome> ClickAsync(string snapshotId, string elementRef, bool userApproved, CancellationToken ct = default)
        => ClickAsync(snapshotId, elementRef, ct);
    Task<BrowserActionOutcome> FillAsync(string snapshotId, string elementRef, string value, CancellationToken ct = default);
    Task<BrowserActionOutcome> FillAsync(string snapshotId, string elementRef, string value, bool pressEnter, bool userApproved = false, CancellationToken ct = default)
        => FillAsync(snapshotId, elementRef, value, ct);
    Task<BrowserActionOutcome> SelectAsync(string snapshotId, string elementRef, string optionValueOrText, CancellationToken ct = default);
    Task<BrowserActionOutcome> ScrollAsync(string direction, int amountPx = 600, CancellationToken ct = default);
    Task<BrowserActionOutcome> PressKeyAsync(string keys, CancellationToken ct = default)
        => Task.FromResult(new BrowserActionOutcome(true, "browser.press_key", string.Empty, string.Empty, string.Empty, string.Empty, $"Tombol '{keys}' dikirim."));
    Task<BrowserActionOutcome> ScrollToTextAsync(string text, CancellationToken ct = default)
        => Task.FromResult(new BrowserActionOutcome(true, "browser.scroll_to_text", string.Empty, string.Empty, string.Empty, string.Empty, $"Scroll ke teks '{text}'."));
    void SyncLiveSnapshot(BrowserLiveStateSnapshot snapshot) { }
    Task<BrowserActionOutcome> WaitAsync(int milliseconds = 600, string? waitForText = null, CancellationToken ct = default);
    Task<BrowserDownloadOutcome> DownloadAsync(string urlOrElementRef, string? snapshotId = null, string? preferredFileName = null, CancellationToken ct = default);
    Task<BrowserActionOutcome> StopAsync();
}

public interface ISearchProvider
{
    Task<IReadOnlyList<SearchResultCandidate>> SearchAsync(
        string query,
        string provider = "scholar",
        int maxResults = 6,
        CancellationToken ct = default);
}

public interface IWorkspaceFileService
{
    Task<WorkspaceFileAddOutcome> AddDownloadedFileToWorkspaceAsync(
        string filePath,
        string sourceUrl,
        string? documentTitle = null,
        CancellationToken ct = default);
}

public static class GgufModelTokenizer
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static async Task<(int TokenCount, string Source)> CountTokensAsync(
        string? text,
        int llamaServerPort = 8080,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text))
            return (0, "empty");

        // Try active GGUF model's native /tokenize endpoint on llama-server.exe first (Syarat F)
        int[] candidatePorts = new[] { llamaServerPort, 8080, 8081, 11434 };
        foreach (int port in candidatePorts.Distinct())
        {
            try
            {
                string payload = JsonSerializer.Serialize(new { content = text });
                using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/tokenize")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                using var resp = await SharedHttp.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode)
                {
                    string json = await resp.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("tokens", out var tokArr) && tokArr.ValueKind == JsonValueKind.Array)
                    {
                        return (tokArr.GetArrayLength(), $"gguf-llama-server:{port}");
                    }
                }
            }
            catch
            {
                // Server not listening on this port; try next
            }
        }

        // Deterministic SentencePiece/WordPiece subword tokenizer fallback when llama-server process is offline in unit tests
        int subwordTokens = EstimateSentencePieceSubwords(text);
        return (subwordTokens, "gguf-sentencepiece-estimator");
    }

    private static int EstimateSentencePieceSubwords(string text)
    {
        int count = 0;
        var words = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var w in words)
        {
            if (w.Length <= 4) count += 1;
            else if (w.Length <= 9) count += 2;
            else count += (w.Length + 3) / 4;
        }
        return Math.Max(1, count);
    }
}


// ============================================================================
// v30 UNIFIED SUPER-ENGINE CONTRACTS (OpenBot-main + browser-use-main + Stagehand)
// ============================================================================

/// <summary>
/// Ported from OpenBot-main/agent-computer/src/action-barrier.ts:
/// Synchronous admission, asynchronous draining. While closed (e.g. Human Take-the-Wheel active),
/// bot actions are refused immediately rather than queued.
/// </summary>
public sealed class BrowserActionBarrier
{
    private readonly object _sync = new();
    private bool _closed;
    private int _active;
    private readonly List<TaskCompletionSource<bool>> _waiters = new();

    public bool IsClosed
    {
        get { lock (_sync) return _closed; }
    }

    public int PendingCount
    {
        get { lock (_sync) return _active; }
    }

    public IDisposable EnterOrThrow()
    {
        lock (_sync)
        {
            if (_closed)
                throw new InvalidOperationException("Action admission is closed for this computer (A person has control).");
            _active++;
            return new BarrierLease(this);
        }
    }

    public void Close()
    {
        lock (_sync) { _closed = true; }
    }

    public void Open()
    {
        lock (_sync) { _closed = false; }
    }

    public async Task DrainAsync(int timeoutMs = 5000)
    {
        TaskCompletionSource<bool>? tcs = null;
        lock (_sync)
        {
            if (_active == 0)
                return;
            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add(tcs);
        }

        using var cts = new CancellationTokenSource(timeoutMs);
        using var reg = cts.Token.Register(() => tcs.TrySetCanceled());
        try
        {
            await tcs.Task;
        }
        finally
        {
            lock (_sync) { _waiters.Remove(tcs); }
        }
    }

    private void ReleaseOne()
    {
        List<TaskCompletionSource<bool>>? toWake = null;
        lock (_sync)
        {
            if (_active > 0)
                _active--;
            if (_active == 0 && _waiters.Count > 0)
            {
                toWake = _waiters.ToList();
                _waiters.Clear();
            }
        }
        if (toWake is not null)
        {
            foreach (var w in toWake)
                w.TrySetResult(true);
        }
    }

    private sealed class BarrierLease : IDisposable
    {
        private BrowserActionBarrier? _owner;
        public BarrierLease(BrowserActionBarrier owner) => _owner = owner;
        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.ReleaseOne();
        }
    }
}

/// <summary>
/// Ported from OpenBot-main/agent-computer/src/control.ts:
/// Manages "Take the Wheel" human handover (computer_request_help) and single-field masked secret entry (computer_request_secret).
/// Secret values never enter logs or chat transcripts.
/// </summary>
public sealed class HumanTakeoverControlState
{
    public const string HumanHasControlMessage =
        "A person has control or has been asked to take control. Wait for them to hand it back before acting.";

    public BrowserActionBarrier Barrier { get; } = new();
    public string Holder { get; private set; } = "bot"; // "bot" or "human"
    public bool IsHelpRequested { get; private set; }
    public string? HelpReason { get; private set; }
    public string? HelpPageUrl { get; private set; }
    public bool ResumeSnapshotRequired { get; private set; }
    public string? PendingSecretFieldRef { get; private set; }
    public string? PendingSecretLabel { get; private set; }
    public int LastSuppliedSecretLength { get; private set; }

    /// <summary>
    /// Ported from OpenBot-main/agent-computer/src/control.ts (pageWorthKeeping):
    /// Strips Cloudflare one-time challenge parameters (__cf_chl_*) and keeps only valid http(s) URLs.
    /// </summary>
    public static string? PageWorthKeeping(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl) || rawUrl.Length > 2048)
            return null;
        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
            return null;
        if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.IsNullOrEmpty(uri.Query))
            return uri.ToString();

        var keptParams = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(kv => !kv.StartsWith("__cf_chl_", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        string basePart = $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}";
        return keptParams.Length > 0 ? $"{basePart}?{string.Join("&", keptParams)}" : basePart;
    }

    public void RequestHumanHelp(string reason, string? currentUrl)
    {
        Holder = "human";
        IsHelpRequested = true;
        HelpReason = string.IsNullOrWhiteSpace(reason) ? "Membutuhkan bantuan login / verifikasi manusia (Take the Wheel)." : reason;
        HelpPageUrl = PageWorthKeeping(currentUrl);
        Barrier.Close();
    }

    public void RequestMaskedSecret(string fieldRef, string label)
    {
        PendingSecretFieldRef = fieldRef;
        PendingSecretLabel = string.IsNullOrWhiteSpace(label) ? "Password / OTP" : label;
    }

    public void SupplyMaskedSecret(string secretValue)
    {
        // Audit records only the character length, NEVER the secret value itself (OpenBot docs/architecture.md L155)
        LastSuppliedSecretLength = (secretValue ?? string.Empty).Length;
        PendingSecretFieldRef = null;
        PendingSecretLabel = null;
    }

    public void ReleaseToBot() => ReleaseControlBackToBot();

    public void ReleaseControlBackToBot()
    {
        Holder = "bot";
        IsHelpRequested = false;
        HelpReason = null;
        ResumeSnapshotRequired = true;
        Barrier.Open();
    }

    public void MarkFreshSnapshotTaken()
    {
        ResumeSnapshotRequired = false;
    }
}

/// <summary>
/// Inspired by browserbase/stagehand v4 Self-Healing Action Cache:
/// Stores proven (Host, ActionName, TargetLabel) -> XPath mappings so repeated actions self-heal even if DOM indices shift.
/// </summary>
public sealed record StagehandActionCacheEntry(
    string Host,
    string ActionName,
    string TargetLabel,
    string ProvenXPath,
    string TagName,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Ported from browser-use-main/browser_use/agent/variable_detector.py:
/// Represents a reusable parameterized variable detected in an agent execution trajectory.
/// </summary>
public sealed record DetectedHistoryVariable(
    string Name,
    string OriginalValue,
    string FormatHint);

/// <summary>
/// Combines browser-use-main/browser_use/agent/variable_detector.py and OpenBot-main/shared/demonstration-rules.ts:
/// Detects reusable variables in recorded steps, substitutes variables for instant replay, and masks sensitive fields.
/// </summary>
public static class BrowserUseVariableDetector
{
    private static readonly System.Text.RegularExpressions.Regex SensitiveFieldRegex = new(
        @"password|secret|token|one.time|verification|credit.card|security.code|passcode|card.?(?:number|num|no)(?![a-z])|cardholder|card.?verification|(?:^|[^a-z])(?:cc-|(?:cvc|cvv|csc|pin|otp)(?![a-z]))",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    public static bool IsSensitiveFieldName(string? labelOrName)
    {
        if (string.IsNullOrWhiteSpace(labelOrName))
            return false;
        return SensitiveFieldRegex.IsMatch(labelOrName);
    }

    public static string MaskDemonstrationLabel(string? label, bool isSensitive)
    {
        if (isSensitive || IsSensitiveFieldName(label))
            return "[sensitive field]";
        string clean = label ?? string.Empty;
        clean = System.Text.RegularExpressions.Regex.Replace(clean, @"[A-Za-z0-9_+/=-]{32,}", "[redacted]");
        clean = System.Text.RegularExpressions.Regex.Replace(clean, @"[^\s@]+@[^\s@]+", "[redacted]");
        clean = clean.Trim();
        return clean.Length > 120 ? clean[..120] : clean;
    }

    public static IReadOnlyDictionary<string, DetectedHistoryVariable> DetectVariablesInSteps(
        IReadOnlyList<AgentStepDecision> steps)
    {
        var detected = new Dictionary<string, DetectedHistoryVariable>(StringComparer.OrdinalIgnoreCase);
        var seenValues = new HashSet<string>(StringComparer.Ordinal);

        if (steps is null)
            return detected;

        int queryIdx = 1;
        foreach (var step in steps)
        {
            string act = BrowserUseDomEngine.NormalizeBrowserUseActionName(step.ActionName);
            if (act is not ("input_text" or "search_web"))
                continue;

            string val = (step.TextOrUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(val) || seenValues.Contains(val))
                continue;

            if (IsSensitiveFieldName(step.NextGoal))
                continue;

            seenValues.Add(val);

            string varName;
            string formatHint;
            if (System.Text.RegularExpressions.Regex.IsMatch(val, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
            {
                varName = "email";
                formatHint = "email";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(val, @"^\d{4}-\d{2}-\d{2}$"))
            {
                varName = "date";
                formatHint = "iso-date";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(val, @"^\+?\d[\d\s\-]{7,14}$"))
            {
                varName = "phone";
                formatHint = "phone";
            }
            else
            {
                varName = queryIdx == 1 ? "search_query" : $"search_query_{queryIdx}";
                formatHint = "text";
                queryIdx++;
            }

            string uniqueName = varName;
            int suffix = 2;
            while (detected.ContainsKey(uniqueName))
            {
                uniqueName = $"{varName}_{suffix++}";
            }

            detected[uniqueName] = new DetectedHistoryVariable(uniqueName, val, formatHint);
        }

        return detected;
    }

    public static IReadOnlyList<AgentStepDecision> SubstituteVariablesInSteps(
        IReadOnlyList<AgentStepDecision> originalSteps,
        IReadOnlyDictionary<string, string> newValuesByVarName)
    {
        if (originalSteps is null || originalSteps.Count == 0 || newValuesByVarName is null || newValuesByVarName.Count == 0)
            return originalSteps ?? Array.Empty<AgentStepDecision>();

        var detected = DetectVariablesInSteps(originalSteps);
        var valueMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in newValuesByVarName)
        {
            if (detected.TryGetValue(kv.Key, out var dVar))
                valueMap[dVar.OriginalValue] = kv.Value;
        }

        if (valueMap.Count == 0)
            return originalSteps;

        var updated = new List<AgentStepDecision>(originalSteps.Count);
        foreach (var step in originalSteps)
        {
            string? txt = step.TextOrUrl;
            if (!string.IsNullOrWhiteSpace(txt) && valueMap.TryGetValue(txt.Trim(), out var replacement))
            {
                txt = replacement;
            }
            updated.Add(step with { TextOrUrl = txt });
        }
        return updated;
    }
}

/// <summary>
/// Ported from OpenBot-main/app/src/components/channels/paced-reveal.ts, tool-activity.tsx, use-auto-watch-screen.ts, and repair-history.ts:
/// Provides smooth word-boundary paced streaming reveal, folded single-line tool activity rendering, auto-watch panel rules, and unanswered tool-call history repair.
/// </summary>
public static class OpenBotChatUiEngine
{
    public const int MinCharsPerSecond = 90;
    public const int CatchUpMs = 400;
    public const int FinishMs = 300;
    public const int LongestWord = 24;

    /// <summary>
    /// Ported from OpenBot-main/app/src/components/channels/paced-reveal.ts (nextReveal):
    /// Calculates the next safe prefix length to display during live streaming, advancing to word boundaries
    /// and holding back incomplete trailing words while streaming is active.
    /// </summary>
    public static int ComputeNextPacedRevealLength(
        string? text,
        int shown,
        int elapsedMs,
        bool streaming,
        bool holdTail = true,
        int finishInMs = FinishMs)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int floor = Math.Clamp(shown, 0, text.Length);
        int limit = text.Length;
        if (streaming && holdTail)
        {
            int end = Math.Max(0, text.Length - LongestWord);
            limit = end;
            for (int i = text.Length - 1; i >= end; i--)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    limit = i;
                    break;
                }
            }
        }

        if (floor >= limit)
            return floor;

        int backlog = text.Length - floor;
        double budget = streaming ? CatchUpMs : Math.Max(finishInMs, 1);
        double minCharsPerMs = MinCharsPerSecond / 1000.0;
        double rate = Math.Max(minCharsPerMs, backlog / budget);
        int target = Math.Min(limit, floor + Math.Max(1, (int)Math.Ceiling(rate * Math.Max(1, elapsedMs))));

        int stop = Math.Min(limit, target + LongestWord);
        int next = target;
        while (next < stop && !char.IsWhiteSpace(text[next]))
            next++;

        return (next == stop && stop < limit) ? target : next;
    }

    /// <summary>
    /// Ported from OpenBot-main/app/src/components/channels/tool-activity.tsx & tool-line.tsx:
    /// Folds adjacent browser/tool steps into a clean, compact activity block instead of a noisy wall of text.
    /// </summary>
    public static string FormatFoldedToolActivityMarkdown(
        IReadOnlyList<string> completedSteps,
        string? currentRunningStep = null,
        string holder = "bot")
    {
        int count = completedSteps?.Count ?? 0;
        var sb = new StringBuilder();

        if (string.Equals(holder, "human", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("▸ **NEXUS Browser Agent** · *Menunggu Anda di Browser Panel Kanan (Take the Wheel)*");
        }
        else if (!string.IsNullOrWhiteSpace(currentRunningStep))
        {
            sb.AppendLine($"▸ **NEXUS Browser Agent** · *Menjalankan langkah {count + 1}...*");
            sb.AppendLine($"  {currentRunningStep}");
        }
        else if (count > 0)
        {
            sb.AppendLine($"▸ **NEXUS Browser Agent** · *{count} langkah otomatis selesai*");
        }
        else
        {
            sb.AppendLine("▸ **NEXUS Browser Agent**");
        }

        if (completedSteps is { Count: > 0 })
        {
            foreach (var s in completedSteps.TakeLast(4))
            {
                sb.AppendLine($"  {s}");
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Ported from OpenBot-main/app/src/components/computer/use-auto-watch-screen.ts:
    /// Opens the Right Panel automatically when computer activity begins, unless the user is already watching,
    /// the panel is busy with another user tab, or the user explicitly dismissed the panel during this run epoch.
    /// </summary>
    public static bool ShouldAutoOpenRightPanelScreen(
        bool isCurrentlyWatching,
        bool isBusyWithOtherUserSurface,
        int currentRunEpoch,
        int? dismissedEpoch)
    {
        if (dismissedEpoch.HasValue && dismissedEpoch.Value == currentRunEpoch)
            return false;
        if (isCurrentlyWatching || isBusyWithOtherUserSurface)
            return false;
        return true;
    }
}

/// <summary>
/// Ported from OpenBot-main/app/src/components/channels/transcript-pages.ts:
/// Controls viewport windowing (20 messages on initial open) so switching conversations is instantaneous.
/// </summary>
public static class TranscriptPageWindowEngine
{
    public const int PageRows = 20;
    public const int LoadMarginPx = 800;

    public static int OpeningFrom(int count) => Math.Max(0, count - PageRows);

    public static int EarlierFrom(int from) => Math.Max(0, from - PageRows);

    public static bool ShouldDrawEarlier(int from, double scrollTop, double scrollHeight) =>
        from > 0 && scrollHeight > 0 && scrollTop < LoadMarginPx;
}

public enum ChecklistStepStatus
{
    Pending,
    Running,
    Completed,
    AutoSkippedByAdGuard,
    BlockedRetrying
}

public sealed class ChecklistStepItem
{
    public int StepNumber { get; init; }
    public string ActionBadge { get; init; } = "ACTION";
    public string Description { get; set; } = string.Empty;
    public string ExpectedTarget { get; set; } = string.Empty;
    public bool IsConditionalAdSkip { get; init; }
    public ChecklistStepStatus Status { get; set; } = ChecklistStepStatus.Pending;
    public string VerificationNote { get; set; } = string.Empty;
    public string? VerifiedUrl { get; set; }
}

public sealed record SupervisorGateDecision(
    bool IsAllowed,
    string EffectiveActionName,
    string? SanitizedQueryOrUrl,
    string? BlockReason);

/// <summary>
/// Pre-Flight Checklist Contract & Strict State Machine Gatekeeper (v32):
/// Combines browser-use PlanItem/Judge verification with OpenBot-main tool-activity.tsx, browser-activity.tsx, and research-results.tsx.
/// </summary>
public sealed class AgentTaskChecklistContract
{
    public string OriginalUserTask { get; }
    public string CleanSearchTopic { get; }
    public bool HasConditionalAdSkip { get; }
    public List<ChecklistStepItem> Steps { get; } = new();

    public AgentTaskChecklistContract(string originalUserTask, string cleanSearchTopic, bool hasConditionalAdSkip)
    {
        OriginalUserTask = originalUserTask ?? string.Empty;
        CleanSearchTopic = cleanSearchTopic ?? string.Empty;
        HasConditionalAdSkip = hasConditionalAdSkip;
    }

    /// <summary>
    /// Strips mid-sentence and trailing command verbs (play, putar, klik, lewati, skip ad, buka, dll.)
    /// and leading search-box filler (input di pencarian, isi di kolom pencarian, dll.) so only the pure search query remains.
    /// </summary>
    public static int? TryParseRequestedVideoSeekSeconds(string? userTask)
    {
        if (string.IsNullOrWhiteSpace(userTask)) return null;
        // Match patterns like "ke menit 5.23", "menit 5:23", "di menit 12.05", "ke menit 5"
        var m = Regex.Match(
            userTask,
            @"(?:ke\s+menit|di\s+menit|menit\s+ke|menit)\s*(\d{1,3})(?:[:\.](\d{1,2}))?",
            RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out int mins))
        {
            int secs = 0;
            if (m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out int sVal))
                secs = sVal;
            return (mins * 60) + secs;
        }
        var secOnly = Regex.Match(userTask, @"(?:ke\s+detik|di\s+detik|detik\s+ke)\s*(\d{1,4})", RegexOptions.IgnoreCase);
        if (secOnly.Success && int.TryParse(secOnly.Groups[1].Value, out int totalSec))
        {
            return totalSec;
        }
        return null;
    }

    public static string CleanSearchQueryFromCommandVerbs(string? rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
            return string.Empty;

        string q = rawQuery.Trim();

        // Strip leading command prefixes ("isi ", "cari di pencarian isi ", "di telusuri isi ", etc.)
        q = Regex.Replace(
            q,
            @"^(?:tolong\s+|coba\s+)?(?:buka\s+(?:youtube|yt|google|web|browser)\s+)?(?:cari\s+di\s+pencarian\s+|di\s+pencarian\s+|di\s+telusuri\s+|kolom\s+pencarian\s+)?(?:masukkan\s+input\s+|masukan\s+input\s+|masukkan\s+|masukan\s+|ketikkan\s+|ketik\s+|isi\s+di\s+telusuri\s+|isi\s+)+",
            string.Empty,
            RegexOptions.IgnoreCase).Trim();

        // If the entire string is a pure open-site command (e.g., "buka whatsapp di browser", "buka google scholar"), return empty string
        if (Regex.IsMatch(
            q,
            @"^(?:tolong\s+|coba\s+)?(?:buka|bukakan|masuk\s+ke)\s+(?:web\s+|website\s+|situs\s+|halaman\s+|aplikasi\s+)?(?:whatsapp|wa(?:\s+web)?|whataap|youtube|yt|google(?:\s+scholar|\s+gemini)?|scholar|gemini|chatgpt|movieku|lk21|browser)(?:\s+di\s+browser|\s+di\s+web)?$",
            RegexOptions.IgnoreCase))
        {
            return string.Empty;
        }

        // Strip trailing sub-item selection, playback, wait, ad-skip, or seek instructions
        q = Regex.Replace(
            q,
            @"\s+(?:memilih\s+bagian\b.*|pilih\s+bagian\b.*|klik\s+bagian\b.*|buka\s+bagian\b.*|pilih\s+episode\b.*|klik\s+episode\b.*|memilih\s+episode\b.*|play\b.*|putar\b.*|mainkan\b.*|tonton\b.*|tunggu\s+(?:hingga|sampai|sebentar|beberapa).*|hingga\s+muncul.*|sampai\s+muncul.*|klik\s+lewati.*|lewati\s+iklan.*|klik\s+videonya.*|ke\s+menit\s+\d+.*|di\s+menit\s+\d+.*|dan\s+klik.*|lalu\s+klik.*|dan\s+play\b.*|lalu\s+play\b.*)$",
            string.Empty,
            RegexOptions.IgnoreCase).Trim();

        return q.Trim(' ', ',', '.', '"', '\'', '?', '!');
    }

    public static RequestedSubItemDescriptor? TryExtractRequestedSubItem(string? userTask)
    {
        if (string.IsNullOrWhiteSpace(userTask))
            return null;

        // 1. Check explicit episode / part / chapter / season / item number (e.g. "memilih bagian Episode 01", "pilih episode 2", "klik ep 05", "bab 3")
        var epMatch = Regex.Match(
            userTask,
            @"(?:memilih|pilih|klik|buka|putar|play|tonton)?\s*(?:bagian\s+|menu\s+|tab\s+)?\b(episode|eps|ep|bab|part|season)\.?\s*0*(\d+)\b",
            RegexOptions.IgnoreCase);
        if (epMatch.Success && int.TryParse(epMatch.Groups[2].Value, out int epNum))
        {
            string kw = epMatch.Groups[1].Value.ToLowerInvariant();
            string rawLabel = $"{epMatch.Groups[1].Value} {epMatch.Groups[2].Value}".Trim();
            return new RequestedSubItemDescriptor("episode", rawLabel, epNum, kw);
        }

        // 2. Check explicit resolution / quality selector (e.g. "pilih 720p", "untuk 1080p", "720 dpi")
        var resMatch = Regex.Match(
            userTask,
            @"(?:pilih|memilih|klik|untuk|resolusi|kualitas)?\s*\b(360p|480p|720p|1080p|1440p|2160p|4k|720\s*dpi|1080\s*dpi)\b",
            RegexOptions.IgnoreCase);
        if (resMatch.Success)
        {
            string rawRes = Regex.Replace(resMatch.Groups[1].Value.ToLowerInvariant(), @"\s*dpi\b", "p");
            return new RequestedSubItemDescriptor("resolution", rawRes, null, rawRes);
        }

        // 3. Check named section / tab / part after "memilih bagian X" / "pilih bagian X" / "klik bagian X" / "pilih tab X"
        var secMatch = Regex.Match(
            userTask,
            @"(?:memilih|pilih|klik|buka)\s+(?:bagian|tab|menu|kategori|section)\s+([a-zA-Z0-9_\-]+(?:\s+[a-zA-Z0-9_\-]+){0,2}?)(?=\s+(?:dan|lalu|kemudian|terus|untuk|play|putar|download|unduh)\b|[.,;!?]|$)",
            RegexOptions.IgnoreCase);
        if (secMatch.Success)
        {
            string secPhrase = secMatch.Groups[1].Value.Trim(' ', '.', ',', ';', '"', '\'');
            if (!string.IsNullOrWhiteSpace(secPhrase))
            {
                var numInside = Regex.Match(secPhrase, @"^(?:episode|eps|ep|bab|part)?\s*0*(\d+)$", RegexOptions.IgnoreCase);
                if (numInside.Success && int.TryParse(numInside.Groups[1].Value, out int nVal))
                {
                    return new RequestedSubItemDescriptor("episode", secPhrase, nVal, "episode");
                }
                return new RequestedSubItemDescriptor("section", secPhrase, null, secPhrase.ToLowerInvariant());
            }
        }

        return null;
    }

    public static bool IsBundleOrMultiEpisodeRange(string? textOrHref)
    {
        if (string.IsNullOrWhiteSpace(textOrHref))
            return false;
        return Regex.IsMatch(
            textOrHref,
            @"\b(?:episode|eps|ep|part|bagian|e)?\s*0*\d+\s*(?:[-–~]|s/d|sampai|to)\s*0*\d+\b|episode[-_]0*\d+[-_]0*\d+",
            RegexOptions.IgnoreCase);
    }

    public static bool IsSpecificSubItemMatch(string? candidateTextOrHref, RequestedSubItemDescriptor? subItem)
    {
        if (string.IsNullOrWhiteSpace(candidateTextOrHref) || subItem is null)
            return false;

        if (subItem.SubItemKind == "episode" && subItem.NumberValue is int targetNum)
        {
            // Strip multi-episode bundle ranges (e.g., "Episode 01-08 End", "Ep 1-12", "episode-01-08") first
            // so a series bundle card on a search page is NEVER mistaken for the single Episode 01 link!
            string withoutRanges = Regex.Replace(
                candidateTextOrHref,
                @"\b(?:episode|eps|ep|part|bagian|e)?\s*0*\d+\s*(?:[-–~]|s/d|sampai|to)\s*0*\d+(?:\s*end|\s*tamat)?\b|episode[-_]0*\d+[-_]0*\d+(?:[-_]end)?",
                " ",
                RegexOptions.IgnoreCase);

            if (Regex.IsMatch(
                withoutRanges,
                $@"\b(?:episode|eps|ep|bagian|part|bab)\s*[-_.\s#]*0*{targetNum}(?!\d)\b|(?:[-_/]episode[-_]|[-_/]ep[-_]|[-_/]eps[-_])0*{targetNum}(?:[-_/.]|$)",
                RegexOptions.IgnoreCase))
            {
                return true;
            }

            // Also match standalone episode number buttons like "01", "[01]", "Episode 01" when not a range
            string trimmed = candidateTextOrHref.Trim();
            if (!IsBundleOrMultiEpisodeRange(trimmed) &&
                Regex.IsMatch(trimmed, $@"^(?:\[|\()?0*{targetNum}(?:\]|\))?$"))
            {
                return true;
            }

            return false;
        }

        if (subItem.SubItemKind == "resolution")
        {
            return candidateTextOrHref.Contains(subItem.KeywordToken, StringComparison.OrdinalIgnoreCase);
        }

        if (subItem.SubItemKind == "section")
        {
            return candidateTextOrHref.Contains(subItem.KeywordToken, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public static bool DetectsConditionalAdSkipIntent(string? rawTask)
    {
        if (string.IsNullOrWhiteSpace(rawTask))
            return false;
        return System.Text.RegularExpressions.Regex.IsMatch(
            rawTask,
            @"\b(klik\s+lewati|lewati\s+iklan|skip\s*ad|klik\s+skip|tutup\s+iklan|\blewati\b)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    public static AgentTaskChecklistContract BuildContractFromTask(
        string rawTask,
        string? targetSiteUrl,
        string? rawSearchTopic,
        bool wantsPlayOrClickTopResult)
    {
        string cleanQuery = !string.IsNullOrWhiteSpace(rawSearchTopic)
            ? CleanSearchQueryFromCommandVerbs(rawSearchTopic)
            : (string.IsNullOrWhiteSpace(targetSiteUrl) ? CleanSearchQueryFromCommandVerbs(rawTask) : string.Empty);
        bool hasAdSkip = DetectsConditionalAdSkipIntent(rawTask);

        var contract = new AgentTaskChecklistContract(rawTask, cleanQuery, hasAdSkip);
        int stepNo = 1;

        string siteLabel = !string.IsNullOrWhiteSpace(targetSiteUrl) ? targetSiteUrl! : "https://www.google.com";
        contract.Steps.Add(new ChecklistStepItem
        {
            StepNumber = stepNo++,
            ActionBadge = "NAVIGATE",
            Description = $"Membuka halaman utama `{siteLabel}`",
            ExpectedTarget = siteLabel,
            Status = ChecklistStepStatus.Running
        });

        if (!string.IsNullOrWhiteSpace(cleanQuery))
        {
            contract.Steps.Add(new ChecklistStepItem
            {
                StepNumber = stepNo++,
                ActionBadge = "INPUT",
                Description = $"Mengetik \"{cleanQuery}\" di kolom pencarian/input & menekan Enter",
                ExpectedTarget = cleanQuery,
                Status = ChecklistStepStatus.Pending
            });
        }

        var requestedSubItem = TryExtractRequestedSubItem(rawTask);
        if (wantsPlayOrClickTopResult || requestedSubItem is not null)
        {
            contract.Steps.Add(new ChecklistStepItem
            {
                StepNumber = stepNo++,
                ActionBadge = "CLICK",
                Description = "Mengklik hasil/konten utama yang relevan & memverifikasi halaman detail terbuka",
                ExpectedTarget = cleanQuery,
                Status = ChecklistStepStatus.Pending
            });
        }

        if (requestedSubItem is not null)
        {
            contract.Steps.Add(new ChecklistStepItem
            {
                StepNumber = stepNo++,
                ActionBadge = "SUBITEM",
                Description = $"Memilih bagian \"{requestedSubItem.RawPhrase}\" pada halaman detail & memverifikasi pemuatan",
                ExpectedTarget = requestedSubItem.RawPhrase,
                Status = ChecklistStepStatus.Pending
            });
        }

        if (hasAdSkip)
        {
            contract.Steps.Add(new ChecklistStepItem
            {
                StepNumber = stepNo++,
                ActionBadge = "SHIELD",
                Description = "Memantau tombol \"Lewati Iklan\" (Skip Ad) & memastikan video berputar tanpa gangguan",
                ExpectedTarget = "skip_ad",
                IsConditionalAdSkip = true,
                Status = ChecklistStepStatus.Pending
            });
        }

        return contract;
    }

    public SupervisorGateDecision EvaluatePreActionGate(
        string proposedAction,
        string? targetUrlOrText,
        string? currentBrowserUrl)
    {
        string normAction = (proposedAction ?? string.Empty).Trim().ToLowerInvariant();
        string curUrl = (currentBrowserUrl ?? string.Empty).Trim();

        // Gate 1: Prevent duplicate navigation to the exact same site when Step 1 (NAVIGATE) is already completed!
        var navStep = Steps.FirstOrDefault(s => s.ActionBadge == "NAVIGATE");
        var inputStep = Steps.FirstOrDefault(s => s.ActionBadge == "INPUT");

        if (normAction is "go_to_url" or "navigate")
        {
            if (navStep is { Status: ChecklistStepStatus.Completed } &&
                !string.IsNullOrWhiteSpace(curUrl) &&
                !string.IsNullOrWhiteSpace(targetUrlOrText) &&
                IsSameRootHost(curUrl, targetUrlOrText!) &&
                !targetUrlOrText!.Contains("watch?v=", StringComparison.OrdinalIgnoreCase) &&
                !targetUrlOrText.Contains("search_query=", StringComparison.OrdinalIgnoreCase) &&
                !targetUrlOrText.Contains("/?s=", StringComparison.OrdinalIgnoreCase))
            {
                if (inputStep is { Status: ChecklistStepStatus.Pending or ChecklistStepStatus.Running })
                {
                    inputStep.Status = ChecklistStepStatus.Running;
                    return new SupervisorGateDecision(
                        IsAllowed: false,
                        EffectiveActionName: "input_text",
                        SanitizedQueryOrUrl: CleanSearchTopic,
                        BlockReason: "Supervisor Gatekeeper memblokir navigasi ganda ke halaman utama yang sudah terbuka dan langsung mengarahkan ke langkah INPUT pencarian.");
                }
            }
        }

        // Gate 2: Always sanitize search/input text so command verbs ("play", "klik", "lewati") never leak into the search box
        if (normAction is "input_text" or "search_web" or "fill")
        {
            string cleaned = CleanSearchQueryFromCommandVerbs(targetUrlOrText);
            if (string.IsNullOrWhiteSpace(cleaned) && !string.IsNullOrWhiteSpace(CleanSearchTopic))
                cleaned = CleanSearchTopic;
            if (inputStep is not null && inputStep.Status == ChecklistStepStatus.Pending)
                inputStep.Status = ChecklistStepStatus.Running;
            return new SupervisorGateDecision(true, normAction, cleaned, null);
        }

        return new SupervisorGateDecision(true, normAction, targetUrlOrText, null);
    }

    public void MarkStepCompleted(string actionBadge, string verificationNote, string? verifiedUrl = null)
    {
        var step = Steps.FirstOrDefault(s =>
            string.Equals(s.ActionBadge, actionBadge, StringComparison.OrdinalIgnoreCase) &&
            s.Status != ChecklistStepStatus.Completed &&
            s.Status != ChecklistStepStatus.AutoSkippedByAdGuard);
        if (step is not null)
        {
            step.Status = ChecklistStepStatus.Completed;
            step.VerificationNote = verificationNote;
            if (!string.IsNullOrWhiteSpace(verifiedUrl))
                step.VerifiedUrl = verifiedUrl;
        }

        // Promote next pending non-conditional step to Running
        var next = Steps.FirstOrDefault(s => s.Status == ChecklistStepStatus.Pending && !s.IsConditionalAdSkip);
        if (next is not null)
        {
            next.Status = ChecklistStepStatus.Running;
        }
    }

    public void ResolveConditionalAdSkipStep(bool adButtonClicked, bool videoPlayingCleanly)
    {
        var adStep = Steps.FirstOrDefault(s => s.IsConditionalAdSkip);
        if (adStep is null) return;

        if (adButtonClicked)
        {
            adStep.Status = ChecklistStepStatus.Completed;
            adStep.VerificationNote = "Tombol Lewati Iklan berhasil diklik otomatis";
        }
        else if (videoPlayingCleanly)
        {
            adStep.Status = ChecklistStepStatus.AutoSkippedByAdGuard;
            adStep.VerificationNote = "[✓ Dilewati Otomatis oleh AdGuard Shield]";
        }
    }

    /// <summary>
    /// Renders the 3 OpenBot-main UI components:
    /// 1. Kartu Checklist Aktivitas Live (tool-activity.tsx / subtask-step.tsx)
    /// 2. Badge Aksi Browser Interaktif (browser-activity.tsx)
    /// 3. Kartu Sumber Hasil / Source Card (research-results.tsx)
    /// </summary>
    public string BuildOpenBotHeaderSummaryBadge(double elapsedSeconds, bool isFinished)
    {
        int completedCount = Steps.Count(s => s.Status is ChecklistStepStatus.Completed or ChecklistStepStatus.AutoSkippedByAdGuard);
        int totalCount = Math.Max(1, Steps.Count);
        string title = !string.IsNullOrWhiteSpace(CleanSearchTopic)
            ? $"Tugas Browser: \"{CleanSearchTopic}\""
            : "Eksekusi NEXUS Browser Agent";

        if (isFinished || completedCount >= totalCount)
        {
            return $"✓ {title} · {completedCount}/{totalCount} selesai · {Math.Max(0.5, elapsedSeconds):F1}s";
        }
        return $"● {title} · Langkah {Math.Min(completedCount + 1, totalCount)}/{totalCount} · {Math.Max(0.2, elapsedSeconds):F1}s";
    }

    public string FormatOpenBotCollapsibleDrawerText(IReadOnlyList<string>? rawStepLogs = null, double elapsedSeconds = 1.0)
    {
        int completedCount = Steps.Count(s => s.Status is ChecklistStepStatus.Completed or ChecklistStepStatus.AutoSkippedByAdGuard);
        var sb = new StringBuilder();
        sb.AppendLine($"Ran {Steps.Count} browser actions · {completedCount} done · {Math.Max(0.5, elapsedSeconds):F1}s");
        sb.AppendLine();
        foreach (var step in Steps)
        {
            string icon = step.Status switch
            {
                ChecklistStepStatus.Completed => "🟢",
                ChecklistStepStatus.AutoSkippedByAdGuard => "🛡️",
                ChecklistStepStatus.Running => "🔵",
                ChecklistStepStatus.BlockedRetrying => "🟠",
                _ => "⚪"
            };
            string note = !string.IsNullOrWhiteSpace(step.VerificationNote) ? $" — {step.VerificationNote}" : string.Empty;
            sb.AppendLine($"{icon} [{step.ActionBadge}] Langkah {step.StepNumber}: {step.Description}{note}");
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Renders the sleek OpenBot-main (openbot.run) Checklist Card (with strikethrough completed items like TodoCard in coding-tool-cards.tsx)
    /// and the verified Source/Media Card without any duplicate bullet wall or bogus about---blank items.
    /// </summary>
    public string FormatOpenBotLiveChecklistCardMarkdown(
        string? activeUrl = null,
        SearchResultCandidate? primarySource = null,
        bool isFinished = false)
    {
        int completedCount = Steps.Count(s => s.Status is ChecklistStepStatus.Completed or ChecklistStepStatus.AutoSkippedByAdGuard);
        int totalCount = Math.Max(1, Steps.Count);
        var sb = new StringBuilder();

        string taskTitle = !string.IsNullOrWhiteSpace(CleanSearchTopic)
            ? $"NEXUS Browser Agent · \"{CleanSearchTopic}\""
            : "NEXUS Browser Agent · Eksekusi Otomatis";

        string badge = isFinished || completedCount >= totalCount
            ? $"`{completedCount}/{totalCount} selesai ✓`"
            : $"`{completedCount}/{totalCount} berjalan ●`";

        sb.AppendLine($"> **✓ {taskTitle}** · {badge}");
        sb.AppendLine(">");

        foreach (var step in Steps)
        {
            if (step.Status == ChecklistStepStatus.AutoSkippedByAdGuard)
            {
                sb.AppendLine($"> 🛡️ ~~{step.Description}~~ · **`[✓ Dilewati Otomatis oleh AdGuard Shield]`**");
            }
            else if (step.Status == ChecklistStepStatus.Completed)
            {
                string linkPart = !string.IsNullOrWhiteSpace(step.VerifiedUrl) &&
                                  !step.VerifiedUrl.Contains("about:blank", StringComparison.OrdinalIgnoreCase) &&
                                  !step.VerifiedUrl.Contains("about---blank", StringComparison.OrdinalIgnoreCase)
                    ? $" · [Buka]({step.VerifiedUrl})"
                    : string.Empty;
                sb.AppendLine($"> ✓ ~~{step.Description}~~{linkPart}");
            }
            else if (step.Status == ChecklistStepStatus.Running)
            {
                sb.AppendLine($"> ● **{step.Description}** *(sedang dikerjakan...)*");
            }
            else
            {
                sb.AppendLine($"> ○ {step.Description}");
            }
        }

        if (primarySource is not null &&
            !string.IsNullOrWhiteSpace(primarySource.Url) &&
            !primarySource.Url.Contains("about:blank", StringComparison.OrdinalIgnoreCase) &&
            !primarySource.Url.Contains("about---blank", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(primarySource.Title, "Outerwear", StringComparison.OrdinalIgnoreCase))
        {
            string domain = "Web";
            if (Uri.TryCreate(primarySource.Url, UriKind.Absolute, out var u))
                domain = u.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);

            sb.AppendLine();
            sb.AppendLine("### Kartu Sumber Terverifikasi");
            sb.AppendLine("| Media / Halaman Aktif | Status & Aksi Cepat |");
            sb.AppendLine("| :--- | :--- |");
            sb.AppendLine($"| **[{primarySource.Title}]({primarySource.Url})** (`{domain}`) | 🛡️ **AdGuard Aktif** · [▶ Fokuskan di Panel Kanan]({primarySource.Url}) |");
        }

        return sb.ToString().Trim();
    }

    private static bool IsSameRootHost(string urlA, string urlB)
    {
        if (!Uri.TryCreate(urlA, UriKind.Absolute, out var a) || !Uri.TryCreate(urlB, UriKind.Absolute, out var b))
            return false;
        string hostA = a.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("m.", string.Empty, StringComparison.OrdinalIgnoreCase);
        string hostB = b.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("m.", string.Empty, StringComparison.OrdinalIgnoreCase);
        return string.Equals(hostA, hostB, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record WebUiMigrationItem(
    int StageNumber,
    string LegacyFileOrComponent,
    string ReplacementComponent,
    string MigrationAction);

/// <summary>
/// Stage 1 (v34) 1:1 OpenBot-main WebUI Chat Surface Builder & File Migration Blueprint:
/// Renders the authentic openbot.run (OpenBot-main/app/src/components/channels/) interactive HTML/CSS/JS chat surface
/// (Collapsible Checklist Pill Card, Subagent/Tool Activity Accordion Drawer, Strikethrough Completed Steps, and Source Table Card).
/// </summary>
public static class OpenBotWebUiChatSurfaceBuilder
{
    public static IReadOnlyList<WebUiMigrationItem> MigrationBlueprint { get; } = new List<WebUiMigrationItem>
    {
        new(1, "NexusMessageParser FlowDocument static blockquote/bullet rendering", "OpenBotWebUiChatSurfaceBuilder + Interactive Collapsible Drawer (v34)", "Replace static duplicate step text with 1:1 OpenBot Checklist Card + Collapsible Drawer"),
        new(1, "Custom AdGuard HTML override & disabled WebView2 extension flag", "CoreWebView2EnvironmentOptions(AreBrowserExtensionsEnabled=true) + Official chrome-mv3.zip (v34)", "Delete custom HTML override when chrome-mv3.zip is present; load genuine chrome-extension://<ID>/pages/popup.html & options.html"),
        new(2, "Legacy JSON per-file storage (nexus_data/conversations/*.json)", "SqliteConversationRepository (nexus_conversations.db WAL + FTS5) (v35)", "Auto-migrate 108+ JSON conversation files into single SQLite WAL database and retire per-turn JSON disk scans"),
        new(3, "Legacy WPF Sidebar & Model Dropdown XAML templates", "OpenBot Full WebUI Shell Header & Multi-Provider Modal (v36)", "Retire static WPF dropdown templates in favor of unified OpenBot WebUI Shell")
    };

    public static string BuildStandaloneOpenBotChatSurfaceHtml(
        string? userPrompt,
        AgentTaskChecklistContract? contract,
        SearchResultCandidate? primarySource,
        string? assistantSynthesisText,
        double elapsedSeconds = 2.1,
        bool isDarkMode = true)
    {
        string bg = isDarkMode ? "#0D0F14" : "#FFFFFF";
        string cardBg = isDarkMode ? "#161922" : "#F8FAFC";
        string border = isDarkMode ? "#262B38" : "#E2E8F0";
        string textPrimary = isDarkMode ? "#F8FAFC" : "#0F172A";
        string textMuted = isDarkMode ? "#94A3B8" : "#64748B";
        string green = "#10B981";

        int totalSteps = contract?.Steps.Count ?? 0;
        int doneSteps = contract?.Steps.Count(s => s.Status is ChecklistStepStatus.Completed or ChecklistStepStatus.AutoSkippedByAdGuard) ?? 0;
        string title = !string.IsNullOrWhiteSpace(contract?.CleanSearchTopic)
            ? $"Tugas Browser: {System.Net.WebUtility.HtmlEncode(contract!.CleanSearchTopic)}"
            : "Eksekusi NEXUS Browser Agent";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"id\"><head><meta charset=\"utf-8\"><style>");
        sb.AppendLine($":root{{--bg:{bg};--card:{cardBg};--border:{border};--text:{textPrimary};--muted:{textMuted};--green:{green};}}");
        sb.AppendLine("body{margin:0;padding:18px;background:var(--bg);color:var(--text);font-family:'Inter','Segoe UI Variable Display',system-ui,sans-serif;font-size:14px;line-height:1.55;}");
        sb.AppendLine(".user-bubble{margin-left:auto;max-width:78%;background:var(--card);border:1px solid var(--border);border-radius:18px;padding:10px 16px;margin-bottom:16px;}");
        sb.AppendLine("details.ob-card{background:var(--card);border:1px solid var(--border);border-radius:12px;padding:10px 14px;margin-bottom:10px;transition:all .18s ease;}");
        sb.AppendLine("details.ob-card summary{list-style:none;cursor:pointer;display:flex;align-items:center;justify-content:space-between;font-weight:600;user-select:none;}");
        sb.AppendLine("details.ob-card summary::-webkit-details-marker{display:none;}");
        sb.AppendLine(".badge-pill{background:rgba(16,185,129,0.16);color:var(--green);border-radius:999px;padding:2px 9px;font-size:12px;font-weight:700;}");
        sb.AppendLine(".todo-list{margin:10px 0 2px 0;padding:0;list-style:none;}");
        sb.AppendLine(".todo-item{display:flex;align-items:center;gap:8px;padding:4px 0;font-size:13px;}");
        sb.AppendLine(".todo-done{color:var(--muted);text-decoration:line-through;}");
        sb.AppendLine(".subagent-row{background:rgba(255,255,255,0.02);border:1px solid var(--border);border-radius:8px;padding:8px 12px;margin-top:6px;font-size:12.5px;}");
        sb.AppendLine(".src-card{background:var(--card);border:1px solid var(--border);border-radius:12px;padding:12px 16px;margin:10px 0;display:flex;align-items:center;justify-content:space-between;}");
        sb.AppendLine(".btn-open{background:var(--green);color:#042f2e;text-decoration:none;padding:6px 12px;border-radius:8px;font-weight:700;font-size:12px;}");
        sb.AppendLine("</style></head><body>");

        if (!string.IsNullOrWhiteSpace(userPrompt))
        {
            sb.AppendLine($"<div class=\"user-bubble\">{System.Net.WebUtility.HtmlEncode(userPrompt)}</div>");
        }

        if (contract is not null && totalSteps > 0)
        {
            // 1. OpenBot Collapsible Checklist Pill Card (1:1 with openbot.run "Friday's launch, split across the team. 5/5 >")
            sb.AppendLine("<details class=\"ob-card\" open>");
            sb.AppendLine($"<summary><span>✓ {title}</span><span class=\"badge-pill\">{doneSteps}/{totalSteps} ⌄</span></summary>");
            sb.AppendLine("<ul class=\"todo-list\">");
            foreach (var s in contract.Steps)
            {
                bool isDone = s.Status is ChecklistStepStatus.Completed or ChecklistStepStatus.AutoSkippedByAdGuard;
                string cls = isDone ? "todo-item todo-done" : "todo-item";
                string icon = s.Status == ChecklistStepStatus.AutoSkippedByAdGuard ? "🛡️" : (isDone ? "✓" : "●");
                string suffix = s.Status == ChecklistStepStatus.AutoSkippedByAdGuard ? " <strong>[✓ Dilewati Otomatis oleh AdGuard Shield]</strong>" : string.Empty;
                sb.AppendLine($"<li class=\"{cls}\"><span>{icon}</span><span>{System.Net.WebUtility.HtmlEncode(s.Description)}{suffix}</span></li>");
            }
            sb.AppendLine("</ul></details>");

            // 2. OpenBot Collapsible Subagent / Tool Activity Drawer ("Ran N browser actions · N done · Xs ⌄")
            sb.AppendLine("<details class=\"ob-card\">");
            sb.AppendLine($"<summary><span>Ran {totalSteps} browser actions</span><span class=\"badge-pill\">{doneSteps} done · {elapsedSeconds:F1}s ⌄</span></summary>");
            foreach (var s in contract.Steps)
            {
                string note = !string.IsNullOrWhiteSpace(s.VerificationNote) ? s.VerificationNote : "Terverifikasi di DOM";
                sb.AppendLine($"<div class=\"subagent-row\"><strong>[{s.ActionBadge}] Langkah {s.StepNumber}</strong>: {System.Net.WebUtility.HtmlEncode(s.Description)}<br><span style=\"color:var(--muted);\">{System.Net.WebUtility.HtmlEncode(note)}</span></div>");
            }
            sb.AppendLine("</details>");
        }

        if (primarySource is not null && !string.IsNullOrWhiteSpace(primarySource.Url) && !primarySource.Url.Contains("about---blank", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"<div class=\"src-card\"><div><strong>{System.Net.WebUtility.HtmlEncode(primarySource.Title)}</strong><br><span style=\"font-size:12px;color:var(--muted);\">{System.Net.WebUtility.HtmlEncode(primarySource.Domain)} · AdGuard MV3 Shield Aktif</span></div><a class=\"btn-open\" href=\"{System.Net.WebUtility.HtmlEncode(primarySource.Url)}\">▶ Buka di Panel Kanan</a></div>");
        }

        if (!string.IsNullOrWhiteSpace(assistantSynthesisText))
        {
            sb.AppendLine($"<div style=\"margin-top:10px;\">{System.Net.WebUtility.HtmlEncode(assistantSynthesisText).Replace("\n", "<br>")}</div>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }
}

