using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Contracts.BrowserAgent;

// ============================================================================
// LAPIS 0 (00_Contracts): PURE INTERFACES, DTOs & BARRIER CONTRACTS
// Single Source of Truth for Browser Agent, AdGuard MV3, and AI Intent DTOs
// Zero dependency on WPF, XAML, or WebView2 UI controls.
// ============================================================================

public sealed record BrowserElementRef(
    string SnapshotId,
    string RefId,
    string Role,
    string Name,
    string TagName,
    string? Href,
    bool IsPassword,
    bool IsSubmit);

public sealed record BrowserPageState(
    string CurrentUrl,
    string PageTitle,
    string SnapshotId,
    bool IsReady,
    IReadOnlyList<BrowserElementRef> InteractiveElements);

public sealed record BrowserActionOutcome(
    bool IsSuccess,
    string ActionName,
    string UrlBefore,
    string UrlAfter,
    string TitleAfter,
    string SnapshotId,
    string Message,
    string? ErrorCode = null);

public enum BrowserRiskLevel
{
    AutoAllowed,
    RequiresUserConfirmation,
    Blocked
}

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
    string ActionName,
    int? ElementIndex = null,
    string? TextOrUrl = null,
    bool PressEnter = false,
    IReadOnlyList<AgentStepDecision>? QueuedFollowUpActions = null,
    IReadOnlyList<string>? PlanUpdate = null);

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

public sealed class HumanTakeoverControlState
{
    public const string HumanHasControlMessage =
        "A person has control or has been asked to take control. Wait for them to hand it back before acting.";

    public BrowserActionBarrier Barrier { get; } = new();
    public string Holder { get; private set; } = "bot";
    public bool IsHelpRequested { get; private set; }
    public string? HelpReason { get; private set; }
    public string? HelpPageUrl { get; private set; }
    public bool ResumeSnapshotRequired { get; private set; }
    public string? PendingSecretFieldRef { get; private set; }
    public string? PendingSecretLabel { get; private set; }
    public int LastSuppliedSecretLength { get; private set; }

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

public sealed record StagehandActionCacheEntry(
    string Host,
    string ActionName,
    string TargetLabel,
    string ProvenXPath,
    string TagName,
    DateTimeOffset UpdatedAtUtc);

public sealed record DetectedHistoryVariable(
    string Name,
    string OriginalValue,
    string FormatHint);

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

public sealed record WebUiMigrationItem(
    int StageNumber,
    string LegacyFileOrComponent,
    string ReplacementComponent,
    string MigrationAction);
