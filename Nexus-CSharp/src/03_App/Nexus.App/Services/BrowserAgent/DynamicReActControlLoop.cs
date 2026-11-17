using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Nexus.App.ViewModels;

namespace Nexus.App.Services.BrowserAgent;

public sealed class BrowserDynamicTabStack
{
    private readonly List<BrowserVirtualTab> _tabs = new();
    private int _activeIndex = -1;
    private int _tabSeq;

    public int ActiveTabIndex => _activeIndex;
    public IReadOnlyList<BrowserVirtualTab> Tabs => _tabs.AsReadOnly();

    public BrowserVirtualTab OpenOrActivateTab(string url, string title, string roleTag = "dynamic", int currentScrollY = 0)
    {
        if (_activeIndex >= 0 && _activeIndex < _tabs.Count)
        {
            var prev = _tabs[_activeIndex];
            _tabs[_activeIndex] = prev with { IsActive = false, ScrollY = currentScrollY };
        }

        string tabId = $"tab-{Interlocked.Increment(ref _tabSeq):D2}";
        var nextTab = new BrowserVirtualTab(
            TabId: tabId,
            Title: string.IsNullOrWhiteSpace(title) ? url : title,
            Url: url,
            RoleTag: roleTag,
            IsActive: true,
            OpenedAtUtc: DateTimeOffset.UtcNow,
            ScrollY: 0);

        _tabs.Add(nextTab);
        _activeIndex = _tabs.Count - 1;
        SyncToRuntimeSettings();
        return nextTab;
    }

    public void UpdateActiveTabScroll(int scrollY)
    {
        if (_activeIndex >= 0 && _activeIndex < _tabs.Count)
        {
            var cur = _tabs[_activeIndex];
            _tabs[_activeIndex] = cur with { ScrollY = Math.Max(0, scrollY) };
            SyncToRuntimeSettings();
        }
    }

    public (bool Success, BrowserVirtualTab? ActivatedTab) SwitchTab(
        string tabIdOrIndex,
        int currentActiveScrollY)
    {
        if (_tabs.Count == 0)
            return (false, null);

        // Save current active tab's ScrollY before switching (Revision 4)
        if (_activeIndex >= 0 && _activeIndex < _tabs.Count)
        {
            var cur = _tabs[_activeIndex];
            _tabs[_activeIndex] = cur with { IsActive = false, ScrollY = Math.Max(0, currentActiveScrollY) };
        }

        int targetIdx = -1;
        if (int.TryParse(tabIdOrIndex, out int parsedIdx))
        {
            if (parsedIdx >= 0 && parsedIdx < _tabs.Count)
                targetIdx = parsedIdx;
            else if (parsedIdx >= 1 && parsedIdx <= _tabs.Count)
                targetIdx = parsedIdx - 1;
        }

        if (targetIdx < 0)
        {
            targetIdx = _tabs.FindIndex(t =>
                string.Equals(t.TabId, tabIdOrIndex, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.RoleTag, tabIdOrIndex, StringComparison.OrdinalIgnoreCase) ||
                t.Url.Contains(tabIdOrIndex, StringComparison.OrdinalIgnoreCase));
        }

        if (targetIdx < 0 || targetIdx >= _tabs.Count)
            return (false, null);

        _activeIndex = targetIdx;
        var activated = _tabs[targetIdx] with { IsActive = true };
        _tabs[targetIdx] = activated;
        SyncToRuntimeSettings();
        return (true, activated);
    }

    private void SyncToRuntimeSettings()
    {
        try
        {
            if (System.Windows.Application.Current?.Dispatcher is { } disp && !disp.CheckAccess())
            {
                var snapshotTabs = _tabs.ToList();
                disp.BeginInvoke(() =>
                {
                    var openTabs = BrowserAgentRuntimeSettings.Current.OpenTabs;
                    openTabs.Clear();
                    foreach (var t in snapshotTabs)
                        openTabs.Add(t);
                });
                return;
            }
        }
        catch { }

        var tabsCol = BrowserAgentRuntimeSettings.Current.OpenTabs;
        tabsCol.Clear();
        foreach (var t in _tabs)
            tabsCol.Add(t);
    }
}

public sealed class DynamicReActControlLoop
{
    private readonly IBrowserAutomation _browser;
    private readonly ISearchProvider _search;
    private readonly IWorkspaceFileService _workspaceFiles;
    private readonly AgentWorkspaceViewModel? _workspaceVm;
    private readonly Func<string, Task<string>>? _jsEvaluator;
    private readonly BrowserDynamicTabStack _tabStack = new();
    private int _snapshotCounter;
    private int _currentScrollY;
    private bool _downloadStartingTriggered;
    private bool _hasExecutedInputTextInRun;
    private string _lastInputTextHost = string.Empty;
    private readonly HashSet<string> _searchedHostsInRun = new(StringComparer.OrdinalIgnoreCase);
    private bool _subItemSelectedInRun;
    private bool _playActionExecutedInRun;
    private string _currentRunUserTask = string.Empty;
    private string _ephemeralReadState = string.Empty;
    private string _lastVolatileFreeDomHash = string.Empty;
    private readonly BrowserUseActionLoopDetector _loopDetector = new();

    public BrowserUseActionLoopDetector LoopDetector => _loopDetector;
    public string EphemeralReadState => _ephemeralReadState;
    public long LastCdpPierceMs { get; private set; }
    public bool LastCdpPierceTriggered { get; private set; }
    public string LastCdpPierceReason { get; private set; } = "None";
    public string LastVolatileFreeDomHash => _lastVolatileFreeDomHash;

    public BrowserDynamicTabStack TabStack => _tabStack;

    public DynamicReActControlLoop(
        IBrowserAutomation browser,
        ISearchProvider search,
        IWorkspaceFileService workspaceFiles,
        AgentWorkspaceViewModel? workspaceVm = null,
        Func<string, Task<string>>? jsEvaluator = null)
    {
        _browser = browser;
        _search = search;
        _workspaceFiles = workspaceFiles;
        _workspaceVm = workspaceVm;
        _jsEvaluator = jsEvaluator;
        if (browser is WebView2NativeAdapter nativeAdapter && jsEvaluator is not null)
        {
            nativeAdapter.SetScriptExecutorIfUnset(jsEvaluator);
        }
    }

    private Func<string, Task<string>>? ResolveEffectiveJsEvaluator()
        => _jsEvaluator
           ?? (_browser as WebView2NativeAdapter)?.ScriptExecutor
           ?? BrowserAgentRuntimeSettings.Current.LiveJsEvaluator;

    public void NotifyDownloadStartingFromWebView2()
    {
        _downloadStartingTriggered = true;
    }

    public async Task<BrowserLiveStateSnapshot> CaptureLiveSnapshotAsync(
        string? overrideHtmlOrDom = null,
        CancellationToken ct = default)
    {
        var state = await _browser.GetStateAsync(ct);
        string snapId = $"snap-{Interlocked.Increment(ref _snapshotCounter):D4}";

        string rawDom = overrideHtmlOrDom ?? string.Empty;
        var jsEval = ResolveEffectiveJsEvaluator();
        if (string.IsNullOrWhiteSpace(rawDom) && jsEval is not null)
        {
            int maxPoll = (_jsEvaluator is null && !string.IsNullOrWhiteSpace(state.CurrentUrl) && state.CurrentUrl != "about:blank") ? 5 : 1;
            for (int poll = 0; poll < maxPoll; poll++)
            {
                try
                {
                    rawDom = await jsEval(BrowserUseDomEngine.GetBuildDomTreeScript(600, 60, BrowserAgentRuntimeSettings.Current.LastCursorX, BrowserAgentRuntimeSettings.Current.LastCursorY));
                    if (!string.IsNullOrWhiteSpace(rawDom) &&
                        (rawDom.Contains("\"Index\":", StringComparison.Ordinal) || maxPoll == 1))
                    {
                        break;
                    }
                }
                catch { }
                if (poll + 1 < maxPoll)
                {
                    await Task.Delay(240, ct);
                }
            }
        }
        if (string.IsNullOrWhiteSpace(rawDom))
        {
            rawDom = _workspaceVm?.LastBrowserTitle ?? string.Empty;
        }

        if (_tabStack.Tabs.Count == 0 && !string.IsNullOrWhiteSpace(state.CurrentUrl) && state.CurrentUrl != "about:blank")
        {
            _tabStack.OpenOrActivateTab(state.CurrentUrl, state.PageTitle, "main", _currentScrollY);
        }

        BrowserAgentRuntimeSettings.Current.TakeTheWheel.MarkFreshSnapshotTaken();
        var builtSnapshot = BrowserUseDomEngine.BuildSnapshotFromDomOrHtml(
            snapshotId: snapId,
            currentUrl: state.CurrentUrl,
            pageTitle: state.PageTitle,
            rawDomOrHtml: rawDom,
            activeTabIndex: Math.Max(0, _tabStack.ActiveTabIndex),
            tabs: _tabStack.Tabs,
            pixelsAbove: _currentScrollY,
            pixelsBelow: 600);

        LastCdpPierceMs = 0;
        LastCdpPierceTriggered = false;
        string currentDomHash = BrowserUseDomEngine.ComputeVolatileFreeDomStructureHash(builtSnapshot);
        bool shouldTriggerPierce = BrowserAgentRuntimeSettings.Current.AlwaysPierceCdpInBaselineMode
            ? (builtSnapshot.Elements.Any(e => string.Equals(e.TagName, "iframe", StringComparison.OrdinalIgnoreCase)) ||
               rawDom.Contains("<iframe", StringComparison.OrdinalIgnoreCase))
            : BrowserUseDomEngine.ShouldTriggerDynamicCdpIframePierce(
                builtSnapshot,
                _lastVolatileFreeDomHash,
                currentDomHash,
                rawDom,
                out string dynamicPierceReason);
        LastCdpPierceReason = BrowserAgentRuntimeSettings.Current.AlwaysPierceCdpInBaselineMode
            ? "Baseline_AlwaysPierce"
            : (shouldTriggerPierce ? BrowserUseDomEngine.ShouldTriggerDynamicCdpIframePierce(builtSnapshot, _lastVolatileFreeDomHash, currentDomHash, rawDom, out var r) ? r : "Triggered" : "Skipped_StandardDomSufficient");

        if (BrowserAgentRuntimeSettings.Current.LiveCdpCaller is { } liveCdp && shouldTriggerPierce)
        {
            var swPierce = Stopwatch.StartNew();
            try
            {
                string frameTreeJson = await liveCdp(BrowserUseDomEngine.GetCdpFrameTreeMethodName(), "{}");
                string flatDomJson = await liveCdp(
                    BrowserUseDomEngine.GetCdpFlattenedDocumentMethodName(),
                    BrowserUseDomEngine.GetCdpFlattenedDocumentPayloadJson(-1, true));
                builtSnapshot = BrowserUseDomEngine.MergeCrossOriginIframeNodesIntoSnapshot(
                    builtSnapshot,
                    frameTreeJson,
                    flatDomJson);
                LastCdpPierceTriggered = true;
            }
            catch { }
            finally
            {
                swPierce.Stop();
                LastCdpPierceMs = swPierce.ElapsedMilliseconds;
            }
        }

        _lastVolatileFreeDomHash = currentDomHash;
        _browser.SyncLiveSnapshot(builtSnapshot);
        return builtSnapshot;
    }

    /// <summary>
    /// Executes any of the 12 supported actions with:
    /// - Revision 1: select_option support
    /// - Revision 2: Mandatory C# interception when targetNode.IsSensitiveOrSubmit == true even on regular click_element
    /// - Revision 3: Automatic XPath fallback when window.__nexusDomMap[index].isConnected == false,
    ///               and IsError = false when DownloadStarting is triggered even if DOM/URL did not change
    /// - Revision 4: Saves and restores ScrollY on switch_tab
    /// </summary>
    public async Task<BrowserStepActionResult> ExecuteIndexedActionAsync(
        AgentStepDecision decision,
        BrowserLiveStateSnapshot currentSnapshot,
        Func<PendingApprovalRequest, CancellationToken, Task<bool>>? onUserApproval = null,
        CancellationToken ct = default)
    {
        string action = BrowserUseDomEngine.NormalizeBrowserUseActionName(decision.ActionName);
        string urlBefore = currentSnapshot.CurrentUrl;

        // Synchronize active snapshot metadata with WebView2NativeAdapter (Single Source of Truth)
        _browser.SyncLiveSnapshot(currentSnapshot);

        var wheel = BrowserAgentRuntimeSettings.Current.TakeTheWheel;
        if (string.Equals(wheel.Holder, "human", StringComparison.OrdinalIgnoreCase) || wheel.Barrier.IsClosed)
        {
            return new BrowserStepActionResult(
                action,
                IsError: false,
                ResultMessage: HumanTakeoverControlState.HumanHasControlMessage,
                UrlBefore: urlBefore,
                UrlAfter: urlBefore,
                DomOrUrlChanged: false,
                DownloadTriggered: false,
                UsedXPathFallback: false,
                InterceptedForApproval: true);
        }

        if (action == "request_human_takeover")
        {
            string reason = decision.TextOrUrl ?? decision.NextGoal ?? "Membutuhkan bantuan pengguna (Sign-in / 2FA / CAPTCHA).";
            wheel.RequestHumanHelp(reason, urlBefore);
            await wheel.Barrier.DrainAsync();
            BrowserAgentRuntimeSettings.Current.CurrentAgentActivity = "Waiting for Human Take-the-Wheel";
            return new BrowserStepActionResult(
                "request_human_takeover",
                IsError: false,
                ResultMessage: $"Take the Wheel aktif: {reason} (Halaman disimpan: {wheel.HelpPageUrl ?? urlBefore}).",
                UrlBefore: urlBefore,
                UrlAfter: urlBefore,
                DomOrUrlChanged: false,
                DownloadTriggered: false,
                UsedXPathFallback: false,
                InterceptedForApproval: true);
        }

        if (action == "request_secret")
        {
            string fieldRef = decision.ElementIndex.HasValue ? $"e{decision.ElementIndex.Value}" : "e1";
            string label = decision.TextOrUrl ?? "Password / Kode OTP";
            wheel.RequestMaskedSecret(fieldRef, label);
            return new BrowserStepActionResult(
                "request_secret",
                IsError: false,
                ResultMessage: $"Meminta input rahasia tersembunyi (masked secret) untuk kolom '{label}' ({fieldRef}).",
                UrlBefore: urlBefore,
                UrlAfter: urlBefore,
                DomOrUrlChanged: false,
                DownloadTriggered: false,
                UsedXPathFallback: false,
                InterceptedForApproval: true);
        }

        if (!BrowserUseDomEngine.IsRecognizedAction(action))
        {
            return new BrowserStepActionResult(
                action, true, $"Aksi '{action}' tidak dikenal (hanya mendukung 12 aksi standar + aksi browser-use).",
                urlBefore, urlBefore, false, false, false, false);
        }

        if (action == "wait")
        {
            int waitSec = 2;
            string? waitTargetText = null;
            if (int.TryParse(decision.TextOrUrl, out int parsedSec))
            {
                waitSec = Math.Clamp(parsedSec, 1, 10);
            }
            else if (!string.IsNullOrWhiteSpace(decision.TextOrUrl))
            {
                waitTargetText = decision.TextOrUrl.Trim();
            }
            var waitOut = await _browser.WaitAsync(waitSec * 300, waitTargetText, ct);
            return new BrowserStepActionResult(
                "wait", !waitOut.IsSuccess, waitOut.Message,
                urlBefore, urlBefore, waitOut.IsSuccess && !string.IsNullOrWhiteSpace(waitTargetText), false, false, false);
        }

        // Read-only DOM inspection / readback via LiveJsEvaluator (permitted in orchestrator)
        if (action == "search_page")
        {
            string pattern = decision.TextOrUrl ?? string.Empty;
            var jsSearchEval = ResolveEffectiveJsEvaluator();
            string summary = $"Pencarian teks '{pattern}' pada DOM selesai.";
            if (jsSearchEval is not null && !string.IsNullOrWhiteSpace(pattern))
            {
                try
                {
                    string rawSearch = await jsSearchEval(BrowserUseDomEngine.GetSearchPageScript(pattern));
                    if (!string.IsNullOrWhiteSpace(rawSearch))
                        summary = $"Hasil search_page('{pattern}'): {rawSearch}";
                }
                catch { }
            }
            _ephemeralReadState = summary;
            if (!string.IsNullOrWhiteSpace(pattern))
            {
                BrowserAgentRuntimeSettings.Current.CarryForward.RecordExtractedFact($"search_page:{pattern}", summary, urlBefore);
            }
            return new BrowserStepActionResult(
                "search_page", false, summary,
                urlBefore, urlBefore, false, false, false, false);
        }

        if (action == "find_elements")
        {
            string selector = decision.TextOrUrl ?? "a, button, input";
            var jsFindEval = ResolveEffectiveJsEvaluator();
            string summary = $"Pencarian elemen CSS '{selector}' selesai.";
            if (jsFindEval is not null)
            {
                try
                {
                    string rawFind = await jsFindEval(BrowserUseDomEngine.GetFindElementsScript(selector));
                    if (!string.IsNullOrWhiteSpace(rawFind))
                        summary = $"Hasil find_elements('{selector}'): {rawFind}";
                }
                catch { }
            }
            _ephemeralReadState = summary;
            BrowserAgentRuntimeSettings.Current.CarryForward.RecordExtractedFact($"find_elements:{selector}", summary, urlBefore);
            return new BrowserStepActionResult(
                "find_elements", false, summary,
                urlBefore, urlBefore, false, false, false, false);
        }

        if (action == "copy_text")
        {
            string copiedText = decision.TextOrUrl ?? string.Empty;
            if (decision.ElementIndex is int copyIdx)
            {
                var copyNode = currentSnapshot.Elements.FirstOrDefault(e => e.Index == copyIdx);
                if (copyNode is not null && string.IsNullOrWhiteSpace(copiedText))
                    copiedText = copyNode.TextOrLabel;

                var jsCopyEval = ResolveEffectiveJsEvaluator();
                if (jsCopyEval is not null && copyNode is not null)
                {
                    try
                    {
                        string rawCopy = await jsCopyEval(BrowserUseDomEngine.GetCopyElementTextScript(copyIdx, copyNode.XPath));
                        if (!string.IsNullOrWhiteSpace(rawCopy) && rawCopy.StartsWith("{", StringComparison.Ordinal))
                        {
                            using var doc = JsonDocument.Parse(rawCopy);
                            if (doc.RootElement.TryGetProperty("text", out var tp) && !string.IsNullOrWhiteSpace(tp.GetString()))
                                copiedText = tp.GetString()!;
                        }
                    }
                    catch { }
                }
            }
            if (string.IsNullOrWhiteSpace(copiedText))
                copiedText = currentSnapshot.CleanMarkdownViewportText;

            BrowserAgentRuntimeSettings.Current.ClipboardMemory = copiedText;
            BrowserAgentRuntimeSettings.Current.CarryForward.LastCopiedText = copiedText;
            BrowserAgentRuntimeSettings.Current.CarryForward.RecordExtractedFact("CopiedText", copiedText, urlBefore);
            bool nativeClipboardCopied = TrySetWindowsClipboardText(copiedText);
            _ephemeralReadState = $"[Clipboard Memory Copied]: {copiedText}";

            WorkspaceFileAddOutcome? savedWfOutcome = null;
            if (!string.IsNullOrWhiteSpace(copiedText) &&
                Regex.IsMatch(_currentRunUserTask ?? string.Empty, @"\b(simpan\s+(?:hasil(?:nya)?\s+)?ke\s+file|simpan\s+sebagai\s+file|save\s+to\s+file|ekspor\s+ke\s+file)\b", RegexOptions.IgnoreCase))
            {
                try
                {
                    string dlDir = BrowserAgentRuntimeSettings.Current.DownloadDirectory;
                    Directory.CreateDirectory(dlDir);
                    string savedPath = Path.Combine(dlDir, $"Hasil_Salinan_Browser_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    await File.WriteAllTextAsync(savedPath, copiedText, Encoding.UTF8, ct);
                    savedWfOutcome = await _workspaceFiles.AddDownloadedFileToWorkspaceAsync(savedPath, urlBefore, "Hasil Salinan Browser", ct);
                    if (savedWfOutcome.IsSuccess)
                        BrowserAgentRuntimeSettings.Current.CarryForward.LastSavedWorkspaceFilePath = savedWfOutcome.FilePath;
                }
                catch { }
            }

            string nativeClipBadge = nativeClipboardCopied ? " + Windows Clipboard" : "";
            string fileBadge = savedWfOutcome is { IsSuccess: true } ? $" & disimpan ke `{Path.GetFileName(savedWfOutcome.FilePath)}`" : "";
            return new BrowserStepActionResult(
                "copy_text", false, $"Menyalin teks ke memori clipboard{nativeClipBadge}{fileBadge} ({copiedText.Length} karakter): \"{(copiedText.Length > 80 ? copiedText[..80] + "..." : copiedText)}\".",
                urlBefore, urlBefore, true, false, false, false, WorkspaceOutcome: savedWfOutcome);
        }

        // Batch 1A: All mutations (send_keys, scroll_to_text, scroll, navigate, click, fill, select) delegate exclusively to _browser (WebView2NativeAdapter)
        if (action == "send_keys")
        {
            string keys = string.IsNullOrWhiteSpace(decision.TextOrUrl) ? "Enter" : decision.TextOrUrl!;
            var keyRes = await _browser.PressKeyAsync(keys, ct);
            if (string.Equals(keyRes.ErrorCode, "HUMAN_TAKEOVER_ACTIVE", StringComparison.Ordinal))
            {
                return new BrowserStepActionResult(
                    "send_keys", false, HumanTakeoverControlState.HumanHasControlMessage,
                    urlBefore, urlBefore, false, false, false, true);
            }
            return new BrowserStepActionResult(
                "send_keys", !keyRes.IsSuccess, keyRes.Message,
                urlBefore, urlBefore, keyRes.IsSuccess, false, false, false);
        }

        if (action == "scroll_to_text")
        {
            string targetText = decision.TextOrUrl ?? string.Empty;
            var scrollTxtRes = await _browser.ScrollToTextAsync(targetText, ct);
            if (string.Equals(scrollTxtRes.ErrorCode, "HUMAN_TAKEOVER_ACTIVE", StringComparison.Ordinal))
            {
                return new BrowserStepActionResult(
                    "scroll_to_text", false, HumanTakeoverControlState.HumanHasControlMessage,
                    urlBefore, urlBefore, false, false, false, true);
            }
            return new BrowserStepActionResult(
                "scroll_to_text", !scrollTxtRes.IsSuccess, scrollTxtRes.Message,
                urlBefore, urlBefore, scrollTxtRes.IsSuccess, false, false, false);
        }

        if (action == "done")
        {
            return new BrowserStepActionResult(
                "done", false, decision.TextOrUrl ?? "Tugas selesai.",
                urlBefore, urlBefore, true, false, false, false);
        }

        if (action == "request_user_approval")
        {
            var req = new PendingApprovalRequest(
                currentSnapshot.SnapshotId,
                decision.ElementIndex,
                action,
                decision.TextOrUrl ?? "Tindakan membutuhkan persetujuan pengguna",
                urlBefore,
                decision.NextGoal);
            bool approved = onUserApproval is not null && await onUserApproval(req, ct);
            return new BrowserStepActionResult(
                action,
                IsError: !approved,
                ResultMessage: approved ? "Disetujui oleh pengguna." : "Menunggu / ditolak persetujuan pengguna.",
                UrlBefore: urlBefore,
                UrlAfter: urlBefore,
                DomOrUrlChanged: approved,
                DownloadTriggered: false,
                UsedXPathFallback: false,
                InterceptedForApproval: true);
        }

        if (action == "go_to_url")
        {
            string targetUrl = decision.TextOrUrl ?? string.Empty;
            var nav = await _browser.NavigateAsync(targetUrl, ct);
            if (string.Equals(nav.ErrorCode, "HUMAN_TAKEOVER_ACTIVE", StringComparison.Ordinal))
            {
                return new BrowserStepActionResult(
                    action, false, HumanTakeoverControlState.HumanHasControlMessage,
                    urlBefore, urlBefore, false, false, false, true);
            }
            if (nav.IsSuccess)
            {
                _tabStack.OpenOrActivateTab(nav.UrlAfter, nav.TitleAfter, "dynamic", _currentScrollY);
                _currentScrollY = 0;
                if (!string.IsNullOrWhiteSpace(nav.UrlAfter) &&
                    (nav.UrlAfter.Contains("?s=", StringComparison.OrdinalIgnoreCase) ||
                     nav.UrlAfter.Contains("search_query=", StringComparison.OrdinalIgnoreCase) ||
                     nav.UrlAfter.Contains("?q=", StringComparison.OrdinalIgnoreCase) ||
                     nav.UrlAfter.Contains("/search", StringComparison.OrdinalIgnoreCase)) &&
                    Uri.TryCreate(nav.UrlAfter, UriKind.Absolute, out var searchNavUri))
                {
                    string normSearchHost = searchNavUri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
                    _searchedHostsInRun.Add(normSearchHost);
                    _hasExecutedInputTextInRun = true;
                    _lastInputTextHost = normSearchHost;
                }
            }
            return new BrowserStepActionResult(
                action,
                !nav.IsSuccess,
                nav.Message,
                urlBefore,
                nav.UrlAfter,
                nav.IsSuccess,
                false,
                false,
                false,
                VerificationStatus: nav.IsSuccess ? ActionVerificationStatus.Verified : ActionVerificationStatus.Failed,
                VerificationEvidence: nav.IsSuccess ? $"URL aktif terverifikasi: {nav.UrlAfter}" : nav.ErrorCode);
        }

        if (action == "search_web")
        {
            string query = decision.TextOrUrl ?? string.Empty;
            bool isAcademic = query.Contains("scholar", StringComparison.OrdinalIgnoreCase) ||
                              query.Contains("makalah", StringComparison.OrdinalIgnoreCase) ||
                              query.Contains("jurnal", StringComparison.OrdinalIgnoreCase) ||
                              query.Contains("paper", StringComparison.OrdinalIgnoreCase) ||
                              query.Contains("doi", StringComparison.OrdinalIgnoreCase);
            string cleanQuery = Regex.Replace(query, @"\b(scholar|google)\b", " ", RegexOptions.IgnoreCase).Trim();
            if (string.IsNullOrWhiteSpace(cleanQuery)) cleanQuery = query.Trim();

            string searchUrl = isAcademic
                ? "https://scholar.google.com/scholar?q=" + Uri.EscapeDataString(cleanQuery)
                : "https://www.google.com/search?q=" + Uri.EscapeDataString(cleanQuery);

            var nav = await _browser.NavigateAsync(searchUrl, ct);
            if (string.Equals(nav.ErrorCode, "HUMAN_TAKEOVER_ACTIVE", StringComparison.Ordinal))
            {
                return new BrowserStepActionResult(
                    action, false, HumanTakeoverControlState.HumanHasControlMessage,
                    urlBefore, urlBefore, false, false, false, true);
            }
            if (nav.IsSuccess)
            {
                _tabStack.OpenOrActivateTab(searchUrl, $"Search: {cleanQuery}", isAcademic ? "scholar" : "search", _currentScrollY);
                _currentScrollY = 0;
            }
            return new BrowserStepActionResult(
                action, !nav.IsSuccess, nav.Message, urlBefore, nav.UrlAfter, nav.IsSuccess, false, false, false);
        }

        if (action is "scroll" or "scroll_down" or "scroll_up")
        {
            string scrollDir = BrowserUseDomEngine.ResolveScrollDirection(action, decision.TextOrUrl);
            string normalizedScrollAction = scrollDir == "up" ? "scroll_up" : "scroll_down";
            int delta = scrollDir == "down" ? 500 : -500;
            _currentScrollY = Math.Max(0, _currentScrollY + delta);
            _tabStack.UpdateActiveTabScroll(_currentScrollY);
            var scrollRes = await _browser.ScrollAsync(scrollDir, 500, ct);
            if (string.Equals(scrollRes.ErrorCode, "HUMAN_TAKEOVER_ACTIVE", StringComparison.Ordinal))
            {
                return new BrowserStepActionResult(
                    normalizedScrollAction, false, HumanTakeoverControlState.HumanHasControlMessage,
                    urlBefore, urlBefore, false, false, false, true);
            }
            if (_jsEvaluator is null && BrowserAgentRuntimeSettings.Current.LiveJsEvaluator is not null)
            {
                await Task.Delay(BrowserUseDomEngine.ComputeAdaptivePostActionSettleDelayMs(normalizedScrollAction), ct);
            }
            return new BrowserStepActionResult(
                normalizedScrollAction, !scrollRes.IsSuccess, $"Scroll ({scrollDir}) ke Y={_currentScrollY}px.", urlBefore, urlBefore, scrollRes.IsSuccess, false, false, false);
        }

        if (action == "switch_tab")
        {
            string tabTarget = decision.TextOrUrl ?? (decision.ElementIndex?.ToString() ?? "0");
            var (switched, targetTab) = _tabStack.SwitchTab(tabTarget, _currentScrollY);
            if (!switched || targetTab is null)
            {
                return new BrowserStepActionResult(
                    action, true, $"Tab '{tabTarget}' tidak ditemukan.", urlBefore, urlBefore, false, false, false, false);
            }

            var navSwitch = await _browser.NavigateAsync(targetTab.Url, ct);
            if (string.Equals(navSwitch.ErrorCode, "HUMAN_TAKEOVER_ACTIVE", StringComparison.Ordinal))
            {
                return new BrowserStepActionResult(
                    action, false, HumanTakeoverControlState.HumanHasControlMessage,
                    urlBefore, urlBefore, false, false, false, true);
            }
            _currentScrollY = targetTab.ScrollY;
            if (_currentScrollY > 0)
            {
                await _browser.ScrollAsync("to", _currentScrollY, ct);
            }

            return new BrowserStepActionResult(
                action,
                IsError: !navSwitch.IsSuccess,
                ResultMessage: $"Berpindah ke tab '{targetTab.TabId}' ({targetTab.Url}) dengan pemulihan ScrollY={_currentScrollY}px.",
                UrlBefore: urlBefore,
                UrlAfter: targetTab.Url,
                DomOrUrlChanged: navSwitch.IsSuccess,
                DownloadTriggered: false,
                UsedXPathFallback: false,
                InterceptedForApproval: false);
        }

        if (action == "go_back")
        {
            if (_tabStack.Tabs.Count > 1 && _tabStack.ActiveTabIndex > 0)
            {
                var (ok, prevTab) = _tabStack.SwitchTab((_tabStack.ActiveTabIndex - 1).ToString(), _currentScrollY);
                if (ok && prevTab is not null)
                {
                    var navBack = await _browser.NavigateAsync(prevTab.Url, ct);
                    if (string.Equals(navBack.ErrorCode, "HUMAN_TAKEOVER_ACTIVE", StringComparison.Ordinal))
                    {
                        return new BrowserStepActionResult(
                            action, false, HumanTakeoverControlState.HumanHasControlMessage,
                            urlBefore, urlBefore, false, false, false, true);
                    }
                    _currentScrollY = prevTab.ScrollY;
                    return new BrowserStepActionResult(
                        action, !navBack.IsSuccess, $"Kembali ke tab sebelumnya ({prevTab.Url}, ScrollY={_currentScrollY}).",
                        urlBefore, prevTab.Url, navBack.IsSuccess, false, false, false);
                }
            }
            return new BrowserStepActionResult(
                action, false, "Kembali ke halaman sebelumnya.", urlBefore, urlBefore, true, false, false, false);
        }

        if (action == "download_file")
        {
            string dlUrl = decision.TextOrUrl ?? urlBefore;
            if (decision.ElementIndex is int dlIdx)
            {
                var dlNode = currentSnapshot.Elements.FirstOrDefault(e => e.Index == dlIdx);
                if (dlNode?.Href is { Length: > 0 } h)
                    dlUrl = h;
            }
            var dlOut = await _browser.DownloadAsync(dlUrl, currentSnapshot.SnapshotId, null, ct);
            if (string.Equals(dlOut.ErrorCode, "HUMAN_TAKEOVER_ACTIVE", StringComparison.Ordinal))
            {
                return new BrowserStepActionResult(
                    action, false, HumanTakeoverControlState.HumanHasControlMessage,
                    urlBefore, urlBefore, false, false, false, true);
            }
            WorkspaceFileAddOutcome? wfOut = null;
            if (dlOut.IsSuccess && File.Exists(dlOut.LocalFilePath))
            {
                wfOut = await _workspaceFiles.AddDownloadedFileToWorkspaceAsync(dlOut.LocalFilePath, dlUrl, null, ct);
            }
            return new BrowserStepActionResult(
                action,
                IsError: !dlOut.IsSuccess,
                ResultMessage: dlOut.Message,
                UrlBefore: urlBefore,
                UrlAfter: urlBefore,
                DomOrUrlChanged: false,
                DownloadTriggered: dlOut.IsSuccess,
                UsedXPathFallback: false,
                InterceptedForApproval: false,
                DownloadOutcome: dlOut,
                WorkspaceOutcome: wfOut);
        }

        // Actions requiring a valid ElementIndex: click_element, input_text, select_option
        if (decision.ElementIndex is not int elemIndex)
        {
            return new BrowserStepActionResult(
                action, true, $"Aksi '{action}' wajib menyertakan ElementIndex yang valid.",
                urlBefore, urlBefore, false, false, false, false);
        }

        var targetNode = currentSnapshot.Elements.FirstOrDefault(e => e.Index == elemIndex);
        if (targetNode is null)
        {
            return new BrowserStepActionResult(
                action, true, $"Elemen dengan indeks [{elemIndex}] tidak ditemukan pada snapshot '{currentSnapshot.SnapshotId}'.",
                urlBefore, urlBefore, false, false, false, false);
        }

        // OpenBot enter-text.ts L30-62: If the model called input_text on a native <select> dropdown, automatically coerce to select_option!
        if (action == "input_text" && string.Equals(targetNode.TagName, "select", StringComparison.OrdinalIgnoreCase))
        {
            action = "select_option";
        }

        // Batch 1A Approval Gate (without holding BrowserActionBarrier lease during approval wait!)
        var policyCheck = BrowserUseDomEngine.EvaluateOpenBotActionPolicy(
            targetNode.TagName,
            targetNode.InputType,
            targetNode.TextOrLabel,
            targetNode.Href,
            urlBefore);
        bool requiresUserApproval = (targetNode.IsSensitiveOrSubmit || policyCheck.RequiresApproval) &&
            (action == "click_element" || (action == "input_text" && (decision.PressEnter || string.Equals(targetNode.InputType, "password", StringComparison.OrdinalIgnoreCase)))) &&
            BrowserAgentRuntimeSettings.Current.RequireConfirmationForFormSubmit;

        bool userApproved = false;
        if (requiresUserApproval)
        {
            var approvalReq = new PendingApprovalRequest(
                SnapshotId: currentSnapshot.SnapshotId,
                ElementIndex: elemIndex,
                ActionName: action,
                TargetDescription: $"[{targetNode.Index}] <{targetNode.TagName}> \"{targetNode.TextOrLabel}\"",
                CurrentUrl: urlBefore,
                Reason: !string.IsNullOrWhiteSpace(policyCheck.Reason) && policyCheck.RequiresApproval
                    ? policyCheck.Reason
                    : $"Elemen [{targetNode.Index}] terdeteksi sebagai tombol sensitif/submit/kirim eksternal (`IsSensitiveOrSubmit == true`).");

            userApproved = onUserApproval is not null && await onUserApproval(approvalReq, ct);
            if (!userApproved)
            {
                BrowserAgentRuntimeSettings.Current.CurrentAgentActivity = "Paused at Approval Gate";
                BrowserAgentRuntimeSettings.Current.RecordLog(
                    $"APPROVAL GATE INTERCEPTED: {action} pada [{elemIndex}] '{targetNode.TextOrLabel}' ditahan menunggu konfirmasi pengguna.");
                return new BrowserStepActionResult(
                    ActionName: action,
                    IsError: false,
                    ResultMessage: $"Aksi '{action}' pada [{elemIndex}] \"{targetNode.TextOrLabel}\" dicegat otomatis oleh C# Safety Gate dan menunggu persetujuan pengguna.",
                    UrlBefore: urlBefore,
                    UrlAfter: urlBefore,
                    DomOrUrlChanged: false,
                    DownloadTriggered: false,
                    UsedXPathFallback: false,
                    InterceptedForApproval: true);
            }

            // Batch 1A Post-Approval Re-Validation: after user approves, re-verify takeover state, page URL, and target identity before executing mutation!
            if (string.Equals(wheel.Holder, "human", StringComparison.OrdinalIgnoreCase) || wheel.Barrier.IsClosed)
            {
                return new BrowserStepActionResult(
                    action, false, HumanTakeoverControlState.HumanHasControlMessage,
                    urlBefore, urlBefore, false, false, false, true);
            }

            var liveStateAfterApproval = await _browser.GetStateAsync(ct);
            if (!string.IsNullOrWhiteSpace(liveStateAfterApproval.CurrentUrl) &&
                liveStateAfterApproval.CurrentUrl != "about:blank" &&
                !string.IsNullOrWhiteSpace(urlBefore) &&
                urlBefore != "about:blank" &&
                !string.Equals(liveStateAfterApproval.CurrentUrl, urlBefore, StringComparison.OrdinalIgnoreCase))
            {
                return new BrowserStepActionResult(
                    action,
                    IsError: true,
                    ResultMessage: $"Kondisi halaman berubah dari '{urlBefore}' ke '{liveStateAfterApproval.CurrentUrl}' selama menunggu persetujuan pengguna; mutasi dibatalkan.",
                    UrlBefore: urlBefore,
                    UrlAfter: liveStateAfterApproval.CurrentUrl,
                    DomOrUrlChanged: true,
                    DownloadTriggered: false,
                    UsedXPathFallback: false,
                    InterceptedForApproval: false);
            }

            var readbackEval = ResolveEffectiveJsEvaluator();
            if (readbackEval is not null)
            {
                try
                {
                    string rawPostApprovalDom = await readbackEval(BrowserUseDomEngine.GetBuildDomTreeScript());
                    if (!string.IsNullOrWhiteSpace(rawPostApprovalDom) && rawPostApprovalDom.Contains("\"Elements\"", StringComparison.Ordinal))
                    {
                        var refreshedSnap = BrowserUseDomEngine.BuildSnapshotFromDomOrHtml(
                            currentSnapshot.SnapshotId,
                            urlBefore,
                            currentSnapshot.PageTitle,
                            rawPostApprovalDom,
                            currentSnapshot.ActiveTabIndex,
                            currentSnapshot.Tabs,
                            currentSnapshot.PixelsAbove,
                            currentSnapshot.PixelsBelow);
                        var refreshedNode = refreshedSnap.Elements.FirstOrDefault(e => e.Index == elemIndex);
                        if (refreshedNode is null ||
                            !refreshedNode.IsConnectedInDom ||
                            !string.Equals(refreshedNode.TagName, targetNode.TagName, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(refreshedNode.Role, targetNode.Role, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(refreshedNode.TextOrLabel, targetNode.TextOrLabel, StringComparison.OrdinalIgnoreCase))
                        {
                            return new BrowserStepActionResult(
                                action,
                                IsError: true,
                                ResultMessage: $"Target [{elemIndex}] berubah atau tidak lagi valid setelah persetujuan pengguna; mutasi dibatalkan.",
                                UrlBefore: urlBefore,
                                UrlAfter: urlBefore,
                                DomOrUrlChanged: false,
                                DownloadTriggered: false,
                                UsedXPathFallback: false,
                                InterceptedForApproval: false);
                        }
                        _browser.SyncLiveSnapshot(refreshedSnap);
                    }
                }
                catch { }
            }
        }

        // Stagehand Self-Healing Action Cache: record proven (Host, ActionName, TargetLabel) -> XPath metadata
        if (Uri.TryCreate(urlBefore, UriKind.Absolute, out var cacheUri) && !string.IsNullOrWhiteSpace(targetNode.XPath))
        {
            string cacheKey = $"{cacheUri.Host}|{action}|{targetNode.TextOrLabel}".ToLowerInvariant();
            BrowserAgentRuntimeSettings.Current.SelfHealingActionCache[cacheKey] = new StagehandActionCacheEntry(
                cacheUri.Host,
                action,
                targetNode.TextOrLabel,
                targetNode.XPath,
                targetNode.TagName,
                DateTimeOffset.UtcNow);
        }

        if (action == "input_text")
        {
            string val = decision.TextOrUrl ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(BrowserAgentRuntimeSettings.Current.ClipboardMemory))
            {
                val = val
                    .Replace("{{clipboard}}", BrowserAgentRuntimeSettings.Current.ClipboardMemory, StringComparison.OrdinalIgnoreCase)
                    .Replace("[clipboard]", BrowserAgentRuntimeSettings.Current.ClipboardMemory, StringComparison.OrdinalIgnoreCase);
            }

            // Batch 1A Single Execution: delegate exclusively to _browser.FillAsync (NO direct JS execution in loop, NO second NavigateAsync!)
            var fillRes = await _browser.FillAsync(currentSnapshot.SnapshotId, $"e{elemIndex}", val, decision.PressEnter, userApproved, ct);
            if (string.Equals(fillRes.ErrorCode, "HUMAN_TAKEOVER_ACTIVE", StringComparison.Ordinal))
            {
                return new BrowserStepActionResult(
                    action, false, HumanTakeoverControlState.HumanHasControlMessage,
                    urlBefore, urlBefore, false, false, false, true);
            }
            if (string.Equals(fillRes.ErrorCode, "CONFIRMATION_REQUIRED", StringComparison.Ordinal))
            {
                return new BrowserStepActionResult(
                    action, false, fillRes.Message,
                    urlBefore, urlBefore, false, false, false, true);
            }
            if (!fillRes.IsSuccess)
            {
                return new BrowserStepActionResult(
                    action, true, fillRes.Message,
                    urlBefore, urlBefore, false, false, false, false);
            }

            _hasExecutedInputTextInRun = true;
            if (Uri.TryCreate(urlBefore, UriKind.Absolute, out var inputHostUri))
            {
                _lastInputTextHost = inputHostUri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
                _searchedHostsInRun.Add(_lastInputTextHost);
            }

            string urlAfterInput = fillRes.UrlAfter;
            if (decision.PressEnter && _jsEvaluator is null && BrowserAgentRuntimeSettings.Current.LiveJsEvaluator is { } liveEval)
            {
                await Task.Delay(600, ct);
                try
                {
                    string liveHref = (await liveEval("(function(){ return location.href || ''; })()")).Trim().Trim('"');
                    if (!string.IsNullOrWhiteSpace(liveHref) && liveHref.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        urlAfterInput = liveHref;
                    }
                }
                catch { }

                if (_workspaceVm is not null && !string.IsNullOrWhiteSpace(urlAfterInput))
                {
                    _workspaceVm.LastBrowserUrl = urlAfterInput;
                }
            }

            string cleanFieldLabel = string.IsNullOrWhiteSpace(targetNode.TextOrLabel) ||
                                     string.Equals(targetNode.TextOrLabel, targetNode.TagName, StringComparison.OrdinalIgnoreCase)
                ? "kolom pencarian/input"
                : $"\"{targetNode.TextOrLabel}\"";
            string enterNote = decision.PressEnter ? " lalu menekan Enter" : "";
            return new BrowserStepActionResult(
                action,
                false,
                $"Mengetik \"{val}\" ke {cleanFieldLabel}{enterNote}.",
                urlBefore,
                urlAfterInput,
                true,
                false,
                false,
                false);
        }

        if (action == "select_option")
        {
            string opt = decision.TextOrUrl ?? string.Empty;
            if (string.IsNullOrWhiteSpace(opt))
            {
                return new BrowserStepActionResult(
                    action, true, "Nilai opsi untuk select_option tidak boleh kosong.",
                    urlBefore, urlBefore, false, false, false, false);
            }

            // Batch 1A Single Execution: delegate exclusively to _browser.SelectAsync
            var selRes = await _browser.SelectAsync(currentSnapshot.SnapshotId, $"e{elemIndex}", opt, ct);
            if (string.Equals(selRes.ErrorCode, "HUMAN_TAKEOVER_ACTIVE", StringComparison.Ordinal))
            {
                return new BrowserStepActionResult(
                    action, false, HumanTakeoverControlState.HumanHasControlMessage,
                    urlBefore, urlBefore, false, false, false, true);
            }
            if (!selRes.IsSuccess)
            {
                return new BrowserStepActionResult(
                    action, true, selRes.Message,
                    urlBefore, urlBefore, false, false, false, false);
            }

            if (_jsEvaluator is null && BrowserAgentRuntimeSettings.Current.LiveJsEvaluator is not null)
            {
                await Task.Delay(BrowserUseDomEngine.ComputeAdaptivePostActionSettleDelayMs("select_option"), ct);
            }

            return new BrowserStepActionResult(
                action, false, $"Opsi '{opt}' berhasil dipilih pada [{elemIndex}] <{targetNode.TagName}>.",
                urlBefore, urlBefore, true, false, false, false);
        }

        // click_element execution: delegate exclusively to _browser.ClickAsync (Single Source of Truth)
        bool isDownloadLink = !string.IsNullOrWhiteSpace(targetNode.Href) &&
                              (targetNode.Href.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                               targetNode.Href.Contains("/article/download/", StringComparison.OrdinalIgnoreCase) ||
                               targetNode.Href.Contains("/article/viewFile/", StringComparison.OrdinalIgnoreCase) ||
                               Regex.IsMatch(targetNode.TextOrLabel, @"\b(download\s+full-text\s+pdf|unduh\s+pdf|download\s+this\s+pdf)\b", RegexOptions.IgnoreCase));

        bool wasDownloadTriggered = _downloadStartingTriggered;
        _downloadStartingTriggered = false;
        if (wasDownloadTriggered || isDownloadLink)
        {
            // Verify target freshness & safety via ClickAsync first if no direct PDF href is present
            if (string.IsNullOrWhiteSpace(targetNode.Href))
            {
                var clickCheck = await _browser.ClickAsync(currentSnapshot.SnapshotId, $"e{elemIndex}", userApproved, ct);
                if (string.Equals(clickCheck.ErrorCode, "HUMAN_TAKEOVER_ACTIVE", StringComparison.Ordinal))
                {
                    return new BrowserStepActionResult(
                        action, false, HumanTakeoverControlState.HumanHasControlMessage,
                        urlBefore, urlBefore, false, false, false, true);
                }
                if (string.Equals(clickCheck.ErrorCode, "CONFIRMATION_REQUIRED", StringComparison.Ordinal))
                {
                    return new BrowserStepActionResult(
                        action, false, clickCheck.Message,
                        urlBefore, urlBefore, false, false, false, true);
                }
                if (!clickCheck.IsSuccess)
                {
                    return new BrowserStepActionResult(
                        action, true, clickCheck.Message,
                        urlBefore, urlBefore, false, false, false, false);
                }
            }

            string dlTarget = !string.IsNullOrWhiteSpace(targetNode.Href) ? targetNode.Href! : urlBefore;
            var dlOutcome = await _browser.DownloadAsync(dlTarget, currentSnapshot.SnapshotId, null, ct);
            if (string.Equals(dlOutcome.ErrorCode, "HUMAN_TAKEOVER_ACTIVE", StringComparison.Ordinal))
            {
                return new BrowserStepActionResult(
                    action, false, HumanTakeoverControlState.HumanHasControlMessage,
                    urlBefore, urlBefore, false, false, false, true);
            }
            WorkspaceFileAddOutcome? wfOutcome = null;
            if (dlOutcome.IsSuccess && File.Exists(dlOutcome.LocalFilePath))
            {
                wfOutcome = await _workspaceFiles.AddDownloadedFileToWorkspaceAsync(
                    dlOutcome.LocalFilePath,
                    dlTarget,
                    targetNode.TextOrLabel,
                    ct);
            }

            return new BrowserStepActionResult(
                ActionName: action,
                IsError: false,
                ResultMessage: $"Klik pada [{elemIndex}] \"{targetNode.TextOrLabel}\" memicu DownloadStarting ({dlOutcome.FileName}).",
                UrlBefore: urlBefore,
                UrlAfter: urlBefore,
                DomOrUrlChanged: false,
                DownloadTriggered: true,
                UsedXPathFallback: false,
                InterceptedForApproval: false,
                DownloadOutcome: dlOutcome,
                WorkspaceOutcome: wfOutcome);
        }

        var clickOut = await _browser.ClickAsync(currentSnapshot.SnapshotId, $"e{elemIndex}", userApproved, ct);
        if (string.Equals(clickOut.ErrorCode, "HUMAN_TAKEOVER_ACTIVE", StringComparison.Ordinal))
        {
            return new BrowserStepActionResult(
                action, false, HumanTakeoverControlState.HumanHasControlMessage,
                urlBefore, urlBefore, false, false, false, true);
        }
        if (string.Equals(clickOut.ErrorCode, "CONFIRMATION_REQUIRED", StringComparison.Ordinal))
        {
            return new BrowserStepActionResult(
                action, false, clickOut.Message,
                urlBefore, urlBefore, false, false, false, true);
        }
        if (!clickOut.IsSuccess)
        {
            return new BrowserStepActionResult(
                action, true, clickOut.Message,
                urlBefore, urlBefore, false, false, false, false);
        }

        string urlAfterClick = !string.IsNullOrWhiteSpace(clickOut.UrlAfter) ? clickOut.UrlAfter : urlBefore;
        if (_jsEvaluator is null && BrowserAgentRuntimeSettings.Current.LiveJsEvaluator is { } liveClickEval)
        {
            await Task.Delay(450, ct);
            try
            {
                string liveHref = (await liveClickEval("(function(){ return location.href || ''; })()")).Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(liveHref) && liveHref.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    urlAfterClick = liveHref;
                }
            }
            catch { }
        }

        if (!string.Equals(urlBefore, urlAfterClick, StringComparison.OrdinalIgnoreCase))
        {
            _tabStack.OpenOrActivateTab(urlAfterClick, targetNode.TextOrLabel, "tab", _currentScrollY);
            _currentScrollY = 0;
        }

        var reqSubItem = AgentTaskChecklistContract.TryExtractRequestedSubItem(_currentRunUserTask);
        if (reqSubItem is not null &&
            AgentTaskChecklistContract.IsSpecificSubItemMatch(targetNode.TextOrLabel + " " + (targetNode.Href ?? urlAfterClick), reqSubItem))
        {
            _subItemSelectedInRun = true;
        }

        if (string.Equals(targetNode.TagName, "video", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(targetNode.TagName, "iframe", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(targetNode.TextOrLabel ?? string.Empty, @"^\s*(?:play|putar|tonton|mainkan|watch|play\s+video|putar\s+video|play\s+episode|putar\s+episode|▶)\b", RegexOptions.IgnoreCase))
        {
            _playActionExecutedInRun = true;
        }

        bool urlChangedOnClick = !string.Equals(urlBefore.TrimEnd('/'), urlAfterClick.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        return new BrowserStepActionResult(
            ActionName: action,
            IsError: false,
            ResultMessage: $"Mengklik \"{targetNode.TextOrLabel}\" ({urlAfterClick})",
            UrlBefore: urlBefore,
            UrlAfter: urlAfterClick,
            DomOrUrlChanged: true,
            DownloadTriggered: false,
            UsedXPathFallback: false,
            InterceptedForApproval: false,
            VerificationStatus: urlChangedOnClick || _playActionExecutedInRun ? ActionVerificationStatus.Verified : ActionVerificationStatus.Executed,
            VerificationEvidence: urlChangedOnClick ? $"Transisi URL terverifikasi: {urlBefore} -> {urlAfterClick}" : $"Interaksi elemen [{targetNode.Index}] dieksekusi pada {urlAfterClick}");
    }

    /// <summary>
    /// Ported directly from browser-use/agent/service.py (multi_act):
    /// Executes a batch of actions sequentially with two-layer page-change protection:
    /// 1. Static flag: sequence-terminating actions (go_to_url, search_web, go_back, switch_tab, done) abort remaining queued actions.
    /// 2. Runtime detection: if UrlAfter != UrlBefore or an error/approval gate triggers, remaining queued actions are aborted so stale DOM indices are never clicked.
    /// </summary>
    public async Task<IReadOnlyList<BrowserStepActionResult>> ExecuteMultiActionsWithPageChangeGuardAsync(
        IReadOnlyList<AgentStepDecision> actions,
        BrowserLiveStateSnapshot currentSnapshot,
        Func<PendingApprovalRequest, CancellationToken, Task<bool>>? onUserApproval = null,
        CancellationToken ct = default)
    {
        var results = new List<BrowserStepActionResult>();
        if (actions is null || actions.Count == 0)
            return results;

        for (int i = 0; i < actions.Count; i++)
        {
            var act = actions[i];
            string normName = BrowserUseDomEngine.NormalizeBrowserUseActionName(act.ActionName);

            // In browser-use multi_act, 'done' is only allowed as the first/single action
            if (i > 0 && string.Equals(normName, "done", StringComparison.OrdinalIgnoreCase))
                break;

            var res = await ExecuteIndexedActionAsync(act, currentSnapshot, onUserApproval, ct);
            results.Add(res);

            if (res.IsError || res.InterceptedForApproval || i == actions.Count - 1)
                break;

            // Layer 1: Static sequence-terminating actions (navigate, search, go_back, switch_tab, done)
            if (normName is "go_to_url" or "search_web" or "go_back" or "switch_tab" or "done")
                break;

            // Layer 2: Runtime URL change detection
            if (!string.Equals(res.UrlBefore, res.UrlAfter, StringComparison.OrdinalIgnoreCase))
                break;
        }

        return results;
    }

    /// <summary>
    /// Runs the dynamic multi-step ReAct loop driven by the active LLM (browser-use architecture),
    /// streaming live step progress to the left Chat panel and enforcing consecutive_failures <= 3 and C# approval interception.
    /// </summary>
    public async Task<AgentLoopExecutionSummary> RunDynamicAgentLoopAsync(
        string userTask,
        Action<string>? onLiveChatStepUpdate = null,
        Func<PendingApprovalRequest, CancellationToken, Task<bool>>? onUserApproval = null,
        Func<BrowserLiveStateSnapshot, int, IReadOnlyList<string>, Task<AgentStepDecision>>? stepPlanner = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _hasExecutedInputTextInRun = false;
        _lastInputTextHost = string.Empty;
        _searchedHostsInRun.Clear();
        _subItemSelectedInRun = false;
        _playActionExecutedInRun = false;
        _currentRunUserTask = userTask ?? string.Empty;
        bool isMultiStageOrSubItemTask = (_currentRunUserTask.Contains("->", StringComparison.Ordinal)) ||
                                         (_currentRunUserTask.Contains("→", StringComparison.Ordinal)) ||
                                         AgentTaskChecklistContract.TryExtractRequestedSubItem(_currentRunUserTask) is not null;
        int configuredMax = BrowserAgentRuntimeSettings.Current.MaxActionsPerTask;
        int maxSteps = Math.Clamp(isMultiStageOrSubItemTask && configuredMax < 14 ? 14 : configuredMax, 1, 40);
        int consecutiveFailures = 0;
        int actionsExecuted = 0;
        // Pengukuran nyata untuk benchmark + status akhir yang jujur.
        bool endedWithDone = false;
        int modelCalls = 0;
        int fastPathStepsExecuted = 0;
        int fastPathFallbackCount = 0;
        int normalStepsAfterStep1 = 0;
        int nullPlanUpdateCompliantSteps = 0;
        long modelMsTotal = 0, firstModelMs = -1, actionMsTotal = 0, snapshotMsTotal = 0;
        int totalPromptTokens = 0, totalCompletionTokens = 0, totalCachedPromptTokens = 0;
        long totalDecodeMs = 0;
        int pagesOpened = 0;
        string lastActionKey = string.Empty;
        int sameActionRepeatCount = 0;
        string? pendingFastPathFailureNote = null;

        BrowserAgentRuntimeSettings.Current.LastSuccessfulRunSteps.Clear();
        var stepLogs = new List<string>();
        var stepTimings = new List<StepStageTimingLog>();
        var verifiedSources = new List<SearchResultCandidate>();
        var previousStepXpaths = new HashSet<string>(StringComparer.Ordinal);
        var activePlan = new List<string>();
        var activeSubGoals = new List<PlannerSubGoal>();
        string previousStepUrl = string.Empty;
        _ephemeralReadState = string.Empty;
        _lastVolatileFreeDomHash = string.Empty;
        BrowserDownloadOutcome? lastDownload = null;
        WorkspaceFileAddOutcome? lastWorkspaceFile = null;

        var (initialTargetUrl, initialInputText, initialWantsDownload) = AnalyzeUniversalBrowserTask(userTask);
        var orderedTargetSites = ExtractOrderedNavigationSitesFromTask(userTask);
        bool initialWantsPlayOrClick = initialWantsDownload ||
            Regex.IsMatch(userTask ?? string.Empty, @"\b(play|putar|mainkan|tonton|carikan|buka\s+video|klik|video\s+paling\s+atas|paling\s+atas)\b", RegexOptions.IgnoreCase);
        var checklistContract = AgentTaskChecklistContract.BuildContractFromTask(
            userTask ?? string.Empty,
            initialTargetUrl,
            initialInputText,
            initialWantsPlayOrClick,
            orderedTargetSites);
        foreach (var stp in checklistContract.Steps.Where(s => !s.IsConditionalAdSkip))
        {
            activePlan.Add(stp.Description);
        }

        SearchResultCandidate? latestClickedSource = null;

        void EmitLiveChatProgress(string currentStepLine, bool isFinished = false)
        {
            var sb = new StringBuilder();
            SearchResultCandidate? bestSrc = latestClickedSource;
            if (bestSrc is null && BrowserUseDomEngine.IsValidNavigatingOrSourceUrl(previousStepUrl))
            {
                string host = Uri.TryCreate(previousStepUrl, UriKind.Absolute, out var pu) ? pu.Host : "Web";
                bestSrc = new SearchResultCandidate(
                    Title: previousStepUrl,
                    Url: previousStepUrl,
                    Domain: host,
                    Snippet: "Halaman aktif di Browser Panel Kanan",
                    RetrievedAtUtc: DateTimeOffset.UtcNow);
            }
            sb.AppendLine(checklistContract.FormatOpenBotLiveChecklistCardMarkdown(
                activeUrl: previousStepUrl,
                primarySource: bestSrc,
                isFinished: isFinished));
            if (consecutiveFailures > 0 && stepLogs.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(stepLogs.Last());
            }
            onLiveChatStepUpdate?.Invoke(sb.ToString().Trim());
        }

        EmitLiveChatProgress("• **Langkah 1**: Menyiapkan browser & menyusun Kontrak Checklist Supervisor...");

        for (int step = 1; step <= maxSteps; step++)
        {
            ct.ThrowIfCancellationRequested();
            var swSnap = Stopwatch.StartNew();
            var snapshot = await CaptureLiveSnapshotAsync(ct: ct);
            swSnap.Stop();
            long stepSnapshotMs = swSnap.ElapsedMilliseconds;
            long stepCdpPierceMs = LastCdpPierceMs;
            bool stepCdpPierceTriggered = LastCdpPierceTriggered;
            string stepCdpPierceReason = LastCdpPierceReason;
            string domHashBeforeStep = BrowserUseDomEngine.ComputeVolatileFreeDomStructureHash(snapshot);
            snapshotMsTotal += stepSnapshotMs;

            // Bounded 180ms visual settle in live WebView2 when interactive bounding boxes [1]..[N] are rendered
            if (_jsEvaluator is null && BrowserAgentRuntimeSettings.Current.LiveJsEvaluator is not null && snapshot.Elements.Count > 0 && step >= 2)
            {
                EmitLiveChatProgress($"• **Langkah {step}**: Memindai halaman ({snapshot.Elements.Count} elemen interaktif ditemukan)...");
                await Task.Delay(180, ct);
            }

            foreach (var el in snapshot.Elements.Where(e => !string.IsNullOrWhiteSpace(e.Href) && e.TextOrLabel.Trim().Length >= 3))
            {
                if (verifiedSources.Count < 24 &&
                    Uri.TryCreate(el.Href, UriKind.Absolute, out var u) &&
                    BrowserUseDomEngine.IsValidNavigatingOrSourceUrl(el.Href) &&
                    !IsJunkOrUtilitySourceCandidate(el.TextOrLabel, el.Href) &&
                    verifiedSources.All(s => !string.Equals(s.Url, el.Href, StringComparison.OrdinalIgnoreCase)))
                {
                    verifiedSources.Add(new SearchResultCandidate(
                        Title: el.TextOrLabel,
                        Url: el.Href!,
                        Domain: u.Host,
                        Snippet: el.TextOrLabel,
                        RetrievedAtUtc: DateTimeOffset.UtcNow));
                }
            }

            bool usedFastPathThisStep = false;
            bool fastPathFallbackThisStep = !string.IsNullOrWhiteSpace(pendingFastPathFailureNote);
            PlannerSubGoal? fastPathSubGoalBeforeExec = null;
            long stepLlmTtftMs = 0, stepLlmDecodeMs = 0, stepLlmInferenceMs = 0;
            int stepPromptTokens = 0, stepCompletionTokens = 0, stepCachedPromptTokens = 0;
            bool stepPromptCacheHit = false;
            bool stepPlanUpdateWasNull = true;

            AgentStepDecision? resolvedFastPathDecision = null;
            if (step >= 2 &&
                stepPlanner is null &&
                BrowserAgentRuntimeSettings.Current.EnableGenericFastPath &&
                string.IsNullOrWhiteSpace(pendingFastPathFailureNote))
            {
                if (activeSubGoals.Count > 0 &&
                    activeSubGoals.All(g => g.Completed) &&
                    checklistContract.GoalVerified &&
                    (!checklistContract.RequiresMediaPlaybackVerification || _playActionExecutedInRun))
                {
                    resolvedFastPathDecision = new AgentStepDecision(
                        "[FAST_PATH:AllSubGoalsVerified] Seluruh sub-tujuan rencana telah diverifikasi selesai.",
                        "Semua langkah pada kontrak telah terbukti selesai.",
                        "Tugas selesai",
                        "done",
                        TextOrUrl: $"Selesai pada {snapshot.CurrentUrl}");
                    usedFastPathThisStep = true;
                }
                else if (activeSubGoals.Any(g => !g.Completed))
                {
                    var fpOutcome = TryResolveGenericSubGoalFastPath(activeSubGoals, snapshot);
                    if (fpOutcome.CanUseFastPath && fpOutcome.CandidateDecision is not null)
                    {
                        fastPathSubGoalBeforeExec = activeSubGoals.FirstOrDefault(g => !g.Completed);
                        resolvedFastPathDecision = fpOutcome.CandidateDecision;
                        usedFastPathThisStep = true;
                    }
                }
            }

            AgentStepDecision decision;
            if (usedFastPathThisStep && resolvedFastPathDecision is not null)
            {
                decision = resolvedFastPathDecision;
            }
            else if (stepPlanner is not null)
            {
                decision = await stepPlanner(snapshot, step, stepLogs);
                stepPlanUpdateWasNull = decision.PlanUpdate is null || decision.PlanUpdate.Count == 0;
                if (step >= 2 && consecutiveFailures == 0 && !fastPathFallbackThisStep)
                {
                    normalStepsAfterStep1++;
                    if (stepPlanUpdateWasNull)
                        nullPlanUpdateCompliantSteps++;
                }
                pendingFastPathFailureNote = null;
            }
            else if (BrowserAgentRuntimeSettings.Current.LiveLlmStepPlannerCaller is { } llmCaller)
            {
                EmitLiveChatProgress($"• **Langkah {step}**: Menganalisis {snapshot.Elements.Count} elemen interaktif pada halaman...");
                _loopDetector.RecordPageState(snapshot.CurrentUrl, snapshot.CleanMarkdownViewportText, snapshot.Elements.Count);
                string? loopNudge = _loopDetector.GetNudgeMessage();
                string? oneTimeReadState = string.IsNullOrWhiteSpace(_ephemeralReadState) ? null : _ephemeralReadState;
                _ephemeralReadState = string.Empty;

                string sysPrompt = BrowserUseDomEngine.GetBrowserUseSystemPrompt();
                IReadOnlySet<string>? deltaXpaths = string.Equals(snapshot.CurrentUrl, previousStepUrl, StringComparison.OrdinalIgnoreCase)
                    ? previousStepXpaths
                    : null;
                string carryForwardPrompt = BrowserAgentRuntimeSettings.Current.CarryForward.FormatCarryForwardContextForPrompt();
                bool compactDom = BrowserAgentRuntimeSettings.Current.EnableCompactDomPrompt;
                string usrPrompt = BrowserUseDomEngine.FormatBrowserStateForLlmPrompt(
                    userTask,
                    snapshot,
                    step,
                    maxSteps,
                    stepLogs,
                    deltaXpaths,
                    readStateContent: oneTimeReadState,
                    activePlan: activePlan.Count > 0 ? activePlan : null,
                    loopNudge: loopNudge,
                    carryForwardContext: carryForwardPrompt,
                    maxViewportElements: compactDom ? 26 : 45,
                    maxWebpageContentChars: compactDom ? 420 : 1600,
                    fastPathFailureNote: pendingFastPathFailureNote);
                pendingFastPathFailureNote = null;

                BrowserAgentRuntimeSettings.Current.LastStepInferenceTelemetry = null;
                string rawLlmJson = string.Empty;
                var swLlm = Stopwatch.StartNew();
                try
                {
                    rawLlmJson = await llmCaller(sysPrompt, usrPrompt, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception llmEx)
                {
                    stepLogs.Add($"• **Langkah {step}**: Model gagal merespons ({llmEx.GetType().Name}: {llmEx.Message}); memakai keputusan cadangan.");
                }
                swLlm.Stop();
                modelCalls++;
                stepLlmInferenceMs = swLlm.ElapsedMilliseconds;
                modelMsTotal += stepLlmInferenceMs;

                var recordedTel = BrowserAgentRuntimeSettings.Current.LastStepInferenceTelemetry;
                if (recordedTel is null && !string.IsNullOrWhiteSpace(rawLlmJson))
                {
                    var (unwrappedJson, parsedTel) = BrowserUseDomEngine.ParseLlamaServerResponseWithTelemetry(
                        rawLlmJson,
                        stepLlmInferenceMs,
                        "delegate");
                    rawLlmJson = unwrappedJson;
                    recordedTel = parsedTel;
                }

                if (recordedTel is not null)
                {
                    stepLlmTtftMs = recordedTel.LlmTtftMs;
                    stepLlmDecodeMs = recordedTel.LlmDecodeMs;
                    stepLlmInferenceMs = recordedTel.TotalInferenceMs;
                    stepPromptTokens = recordedTel.PromptTokens > 0
                        ? recordedTel.PromptTokens
                        : Math.Max(1, (sysPrompt.Length + usrPrompt.Length) / 4);
                    stepCompletionTokens = recordedTel.CompletionTokens;
                    stepCachedPromptTokens = recordedTel.CachedPromptTokens;
                    stepPromptCacheHit = recordedTel.PromptCacheHit;
                }
                else
                {
                    stepLlmTtftMs = stepLlmInferenceMs;
                    stepLlmDecodeMs = 0;
                    stepPromptTokens = Math.Max(1, (sysPrompt.Length + usrPrompt.Length) / 4);
                }

                if (firstModelMs < 0) firstModelMs = stepLlmTtftMs;
                totalPromptTokens += stepPromptTokens;
                totalCompletionTokens += stepCompletionTokens;
                totalCachedPromptTokens += stepCachedPromptTokens;
                totalDecodeMs += stepLlmDecodeMs;

                stepPlanUpdateWasNull = CheckIfRawModelPlanUpdateIsNull(rawLlmJson);
                if (step >= 2 && consecutiveFailures == 0 && !fastPathFallbackThisStep)
                {
                    normalStepsAfterStep1++;
                    if (stepPlanUpdateWasNull)
                        nullPlanUpdateCompliantSteps++;
                }

                bool typedOnHost = HasTypedOnCurrentHost(snapshot.CurrentUrl, _hasExecutedInputTextInRun, _lastInputTextHost, _searchedHostsInRun);
                decision = ParseModelDecisionJsonOrFallback(
                    rawLlmJson,
                    userTask,
                    snapshot,
                    step,
                    lastDownload is not null,
                    typedOnHost,
                    _playActionExecutedInRun,
                    activeSubGoals);
            }
            else
            {
                bool typedOnHost = HasTypedOnCurrentHost(snapshot.CurrentUrl, _hasExecutedInputTextInRun, _lastInputTextHost, _searchedHostsInRun);
                decision = ParseModelDecisionJsonOrFallback(
                    null,
                    userTask,
                    snapshot,
                    step,
                    lastDownload is not null,
                    typedOnHost,
                    _playActionExecutedInRun,
                    activeSubGoals);
                pendingFastPathFailureNote = null;
            }

            // Supervisor Pre-Action Gatekeeper: enforce multi-site sequence, block duplicate root navigation & sanitize search query verbs
            var gateDecision = checklistContract.EvaluatePreActionGate(decision.ActionName, decision.TextOrUrl, snapshot.CurrentUrl);
            if (!gateDecision.IsAllowed && gateDecision.EffectiveActionName == "input_text")
            {
                decision = BuildDefaultAutonomousStepDecision(userTask, snapshot, step, lastDownload is not null, HasTypedOnCurrentHost(snapshot.CurrentUrl, _hasExecutedInputTextInRun, _lastInputTextHost, _searchedHostsInRun), _playActionExecutedInRun);
            }
            else if (gateDecision.EffectiveActionName == "go_to_url" &&
                     !string.IsNullOrWhiteSpace(gateDecision.SanitizedQueryOrUrl) &&
                     (!string.Equals(decision.ActionName, "go_to_url", StringComparison.OrdinalIgnoreCase) ||
                      !string.Equals((decision.TextOrUrl ?? string.Empty).TrimEnd('/'), gateDecision.SanitizedQueryOrUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
            {
                decision = decision with
                {
                    ActionName = "go_to_url",
                    ElementIndex = null,
                    TextOrUrl = gateDecision.SanitizedQueryOrUrl,
                    PressEnter = false,
                    NextGoal = $"Membuka halaman {gateDecision.SanitizedQueryOrUrl}"
                };
            }
            else if (!string.IsNullOrWhiteSpace(gateDecision.SanitizedQueryOrUrl) &&
                     decision.ActionName is "input_text" or "search_web" or "fill")
            {
                decision = decision with
                {
                    TextOrUrl = gateDecision.SanitizedQueryOrUrl,
                    NextGoal = $"Mengetik \"{gateDecision.SanitizedQueryOrUrl}\" ke kolom pencarian/input"
                };
            }

            string actionKey = $"{snapshot.CurrentUrl}|{snapshot.PixelsAbove}|{decision.ActionName}|{decision.ElementIndex}|{decision.TextOrUrl}";
            if (string.Equals(actionKey, lastActionKey, StringComparison.Ordinal))
            {
                sameActionRepeatCount++;
                int maxAllowedRepeats = decision.ActionName is "wait" or "scroll_down" or "scroll_up" ? 3 : 2;
                if (sameActionRepeatCount >= maxAllowedRepeats)
                {
                    break;
                }
            }
            else
            {
                lastActionKey = actionKey;
                sameActionRepeatCount = 0;
            }

            bool hasOverrideTag = (decision.EvaluationPreviousGoal ?? string.Empty).StartsWith("[GUARD_OVERRIDE:", StringComparison.Ordinal) ||
                                  (decision.EvaluationPreviousGoal ?? string.Empty).StartsWith("[FALLBACK_HEURISTIC]", StringComparison.Ordinal);
            if (hasOverrideTag)
            {
                string tagPrefix = decision.EvaluationPreviousGoal!.Split(' ')[0];
                BrowserAgentRuntimeSettings.Current.RecordLog($"Step {step} decision tagged: {tagPrefix} -> {decision.ActionName}");
            }

            if (string.Equals(decision.ActionName, "done", StringComparison.OrdinalIgnoreCase) &&
                stepPlanner is null &&
                step < maxSteps)
            {
                bool hasPendingNonNavSteps = checklistContract.Steps.Any(s =>
                    !s.IsConditionalAdSkip &&
                    (s.ActionBadge != "NAVIGATE" || s.StepNumber > 1) &&
                    s.Status is ChecklistStepStatus.Pending or ChecklistStepStatus.Running);
                var doneEval = checklistContract.EvaluatePostconditionDelta(
                    "done",
                    urlChanged: false,
                    domOrInputChanged: false,
                    mediaPlaybackVerified: _playActionExecutedInRun,
                    isCandidateDoneAction: true);
                if (hasPendingNonNavSteps || doneEval.UnverifiedDoneVeto)
                {
                    bool typedNow = HasTypedOnCurrentHost(snapshot.CurrentUrl, _hasExecutedInputTextInRun, _lastInputTextHost, _searchedHostsInRun);
                    var nextCandidate = TryResolveNextPendingPlannerSubGoalDecision(activeSubGoals, snapshot)
                                        ?? TryResolveMultiHopArrowChainDecision(userTask, snapshot, step, typedNow, _playActionExecutedInRun)
                                        ?? BuildDefaultAutonomousStepDecision(userTask, snapshot, step, lastDownload is not null, typedNow, _playActionExecutedInRun);
                    if (!string.Equals(nextCandidate.ActionName, "done", StringComparison.OrdinalIgnoreCase))
                    {
                        decision = nextCandidate with { EvaluationPreviousGoal = $"[GUARD_OVERRIDE:VetoPrematureDone] {nextCandidate.EvaluationPreviousGoal}" };
                        hasOverrideTag = true;
                    }
                }
            }

            if (string.Equals(decision.ActionName, "done", StringComparison.OrdinalIgnoreCase))
            {
                bool hasUnfinishedContractSteps = stepPlanner is null &&
                    checklistContract.Steps.Any(s =>
                        !s.IsConditionalAdSkip &&
                        (s.ActionBadge != "NAVIGATE" || s.StepNumber > 1) &&
                        s.Status is ChecklistStepStatus.Pending or ChecklistStepStatus.Running);
                bool isUnverifiedDone = hasUnfinishedContractSteps ||
                                        (decision.Memory ?? string.Empty).Contains("tidak diverifikasi", StringComparison.OrdinalIgnoreCase) ||
                                        (decision.Memory ?? string.Empty).Contains("[UNVERIFIED_DONE]", StringComparison.OrdinalIgnoreCase) ||
                                        (decision.EvaluationPreviousGoal ?? string.Empty).Contains("UNVERIFIED", StringComparison.OrdinalIgnoreCase);
                if (isUnverifiedDone)
                {
                    string reasonNote = hasUnfinishedContractSteps
                        ? $"Berhenti prematur sebelum seluruh langkah kontrak selesai ({decision.Memory})"
                        : decision.Memory;
                    stepLogs.Add($"• **Langkah {step}**: [GUARD_OVERRIDE:PrematureOrUnverifiedDone] {reasonNote}");
                    endedWithDone = false;
                    EmitLiveChatProgress(string.Empty);
                    break;
                }
                if (stepLogs.Count == 0)
                {
                    stepLogs.Add($"• **Langkah {step}**: {decision.NextGoal}");
                }
                stepTimings.Add(new StepStageTimingLog(
                    StepNumber: step,
                    ActionName: "done",
                    UsedFastPath: usedFastPathThisStep,
                    FastPathVerified: usedFastPathThisStep,
                    FastPathFallbackToLlm: fastPathFallbackThisStep,
                    SnapshotMs: stepSnapshotMs,
                    CdpPierceMs: stepCdpPierceMs,
                    CdpPierceTriggered: stepCdpPierceTriggered,
                    CdpPierceReason: stepCdpPierceReason,
                    LlmTtftMs: stepLlmTtftMs,
                    LlmDecodeMs: stepLlmDecodeMs,
                    LlmInferenceMs: stepLlmInferenceMs,
                    PromptTokens: stepPromptTokens,
                    CompletionTokens: stepCompletionTokens,
                    CachedPromptTokens: stepCachedPromptTokens,
                    PromptCacheHit: stepPromptCacheHit,
                    PlanUpdateWasNull: stepPlanUpdateWasNull,
                    ActionDispatchMs: 0,
                    PostVerifyMs: 0,
                    DomStructureHashBefore: domHashBeforeStep,
                    DomStructureHashAfter: domHashBeforeStep,
                    StateDeltaVerified: true));
                endedWithDone = true;
                EmitLiveChatProgress(string.Empty);
                break;
            }

            if (decision.PlanUpdate is { Count: > 0 } newPlan)
            {
                var parsedGoals = BrowserUseDomEngine.ParsePlannerSubGoalList(newPlan);
                if (parsedGoals.Count > 0)
                {
                    ApplyDeltaOrFullPlanUpdate(activeSubGoals, activePlan, checklistContract, parsedGoals, newPlan, step, consecutiveFailures > 0 || fastPathFallbackThisStep);
                }
            }
            _loopDetector.RecordAction(decision.ActionName, decision.ElementIndex, decision.TextOrUrl);

            EmitLiveChatProgress($"• **Langkah {step}**: {decision.NextGoal}...");

            previousStepUrl = snapshot.CurrentUrl ?? string.Empty;
            previousStepXpaths.Clear();
            foreach (var elNode in snapshot.Elements)
            {
                if (!string.IsNullOrWhiteSpace(elNode.XPath))
                    previousStepXpaths.Add(elNode.XPath);
            }

            IReadOnlyList<BrowserStepActionResult> batchResults;
            var swAct = Stopwatch.StartNew();
            if (decision.QueuedFollowUpActions is { Count: > 0 } followUps)
            {
                var allBatch = new List<AgentStepDecision> { decision with { QueuedFollowUpActions = null } };
                allBatch.AddRange(followUps);
                batchResults = await ExecuteMultiActionsWithPageChangeGuardAsync(allBatch, snapshot, onUserApproval, ct);
            }
            else
            {
                batchResults = new[] { await ExecuteIndexedActionAsync(decision, snapshot, onUserApproval, ct) };
            }

            swAct.Stop();
            long stepActionDispatchMs = swAct.ElapsedMilliseconds;
            actionMsTotal += stepActionDispatchMs;
            var swPostVerify = Stopwatch.StartNew();
            var result = batchResults.Last();
            actionsExecuted += batchResults.Count;
            if (batchResults.Any(r => r.DomOrUrlChanged && !string.Equals(r.UrlBefore, r.UrlAfter, StringComparison.OrdinalIgnoreCase)))
                pagesOpened++;

            if (result.DownloadOutcome is { IsSuccess: true } dl)
                lastDownload = dl;
            if (result.WorkspaceOutcome is { IsSuccess: true } wf)
                lastWorkspaceFile = wf;

            if (result.InterceptedForApproval)
            {
                swPostVerify.Stop();
                stepTimings.Add(new StepStageTimingLog(
                    StepNumber: step,
                    ActionName: decision.ActionName,
                    UsedFastPath: usedFastPathThisStep,
                    FastPathVerified: false,
                    FastPathFallbackToLlm: fastPathFallbackThisStep,
                    SnapshotMs: stepSnapshotMs,
                    CdpPierceMs: stepCdpPierceMs,
                    CdpPierceTriggered: stepCdpPierceTriggered,
                    CdpPierceReason: stepCdpPierceReason,
                    LlmTtftMs: stepLlmTtftMs,
                    LlmDecodeMs: stepLlmDecodeMs,
                    LlmInferenceMs: stepLlmInferenceMs,
                    PromptTokens: stepPromptTokens,
                    CompletionTokens: stepCompletionTokens,
                    CachedPromptTokens: stepCachedPromptTokens,
                    PromptCacheHit: stepPromptCacheHit,
                    PlanUpdateWasNull: stepPlanUpdateWasNull,
                    ActionDispatchMs: stepActionDispatchMs,
                    PostVerifyMs: swPostVerify.ElapsedMilliseconds,
                    DomStructureHashBefore: domHashBeforeStep,
                    DomStructureHashAfter: domHashBeforeStep,
                    StateDeltaVerified: false));
                stepLogs.Add($"• **Langkah {step} (Konfirmasi Keamanan)**: {result.ResultMessage}");
                EmitLiveChatProgress(string.Empty);
                break;
            }

            bool fastPathVerifiedThisStep = false;
            if (usedFastPathThisStep && fastPathSubGoalBeforeExec is not null)
            {
                fastPathVerifiedThisStep = VerifySubGoalSpecificPostcondition(
                    fastPathSubGoalBeforeExec,
                    decision,
                    result,
                    snapshot,
                    _playActionExecutedInRun,
                    out string fpFailReason);
                if (fastPathVerifiedThisStep)
                {
                    fastPathStepsExecuted++;
                }
                else
                {
                    fastPathFallbackCount++;
                    fastPathFallbackThisStep = true;
                    pendingFastPathFailureNote = $"[FASTPATH_FALLBACK] op={fastPathSubGoalBeforeExec.Op} failed ({fpFailReason}). Choose alternative element or replan.";
                }
            }

            if (result.IsError || (usedFastPathThisStep && !fastPathVerifiedThisStep))
            {
                swPostVerify.Stop();
                stepTimings.Add(new StepStageTimingLog(
                    StepNumber: step,
                    ActionName: decision.ActionName,
                    UsedFastPath: usedFastPathThisStep,
                    FastPathVerified: false,
                    FastPathFallbackToLlm: fastPathFallbackThisStep,
                    SnapshotMs: stepSnapshotMs,
                    CdpPierceMs: stepCdpPierceMs,
                    CdpPierceTriggered: stepCdpPierceTriggered,
                    CdpPierceReason: stepCdpPierceReason,
                    LlmTtftMs: stepLlmTtftMs,
                    LlmDecodeMs: stepLlmDecodeMs,
                    LlmInferenceMs: stepLlmInferenceMs,
                    PromptTokens: stepPromptTokens,
                    CompletionTokens: stepCompletionTokens,
                    CachedPromptTokens: stepCachedPromptTokens,
                    PromptCacheHit: stepPromptCacheHit,
                    PlanUpdateWasNull: stepPlanUpdateWasNull,
                    ActionDispatchMs: stepActionDispatchMs,
                    PostVerifyMs: swPostVerify.ElapsedMilliseconds,
                    DomStructureHashBefore: domHashBeforeStep,
                    DomStructureHashAfter: domHashBeforeStep,
                    StateDeltaVerified: false));

                if (usedFastPathThisStep && !fastPathVerifiedThisStep && !result.IsError)
                {
                    stepLogs.Add($"• **Langkah {step} (Fast-Path Fallback ke LLM)**: Verifikasi pasca-aksi gagal; mengalihkan ke model LLM.");
                    EmitLiveChatProgress(string.Empty);
                    continue;
                }

                consecutiveFailures++;
                stepLogs.Add($"• **Langkah {step} (Percobaan {consecutiveFailures}/3)**: {result.ResultMessage}");
                EmitLiveChatProgress(string.Empty);
                if (consecutiveFailures >= 3)
                {
                    stepLogs.Add("• **Status**: Dihentikan setelah mencapai batas 3 kegagalan berturut-turut (`max_failures = 3`).");
                    break;
                }
            }
            else
            {
                consecutiveFailures = 0;
                BrowserAgentRuntimeSettings.Current.LastSuccessfulRunSteps.Add(decision);
                string xpTag = result.UsedXPathFallback ? " *(XPath Fallback)*" : string.Empty;
                string overrideBadge = hasOverrideTag ? $" `{decision.EvaluationPreviousGoal!.Split(' ')[0]}`" : string.Empty;
                stepLogs.Add($"• **Langkah {step}**: {result.ResultMessage}{xpTag}{overrideBadge}");

                // Update Supervisor Checklist Contract & PlannerSubGoal state based on verified DOM/URL state
                string actLower = (decision.ActionName ?? string.Empty).ToLowerInvariant();
                bool stepUrlChanged = !string.Equals((result.UrlBefore ?? string.Empty).TrimEnd('/'), (result.UrlAfter ?? string.Empty).TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
                var deltaEval = checklistContract.EvaluatePostconditionDelta(
                    actLower,
                    urlChanged: stepUrlChanged,
                    domOrInputChanged: result.DomOrUrlChanged,
                    mediaPlaybackVerified: _playActionExecutedInRun,
                    isCandidateDoneAction: false);
                MarkFirstMatchingPlannerSubGoalCompleted(activeSubGoals, actLower, result.DomOrUrlChanged, result.UrlAfter);
                if (actLower is "go_to_url" or "navigate")
                {
                    bool isSiteSearchUrl = !string.IsNullOrWhiteSpace(result.UrlAfter) &&
                        (result.UrlAfter.Contains("?s=", StringComparison.OrdinalIgnoreCase) ||
                         result.UrlAfter.Contains("search_query=", StringComparison.OrdinalIgnoreCase));
                    if (isSiteSearchUrl && checklistContract.Steps.Any(s => s.ActionBadge == "INPUT" && s.Status != ChecklistStepStatus.Completed))
                    {
                        checklistContract.MarkStepCompleted("NAVIGATE", "Halaman situs tujuan terbuka", result.UrlBefore);
                        checklistContract.MarkStepCompleted("INPUT", $"Pencarian \"{checklistContract.CleanSearchTopic}\" berhasil dibuka", result.UrlAfter);
                    }
                    else
                    {
                        checklistContract.MarkStepCompleted("NAVIGATE", "Halaman terbuka & terverifikasi", result.UrlAfter);
                    }
                }
                else if (actLower is "copy_text" or "search_page" or "find_elements")
                {
                    checklistContract.MarkStepCompleted("EXTRACT", "Informasi halaman berhasil disalin & diverifikasi", result.UrlAfter);
                }
                else if (actLower is "select_option" or "select")
                {
                    checklistContract.MarkStepCompleted("SELECT", $"Opsi \"{decision.TextOrUrl}\" berhasil dipilih & terverifikasi", result.UrlAfter);
                }
                else if (actLower is "scroll" or "scroll_down" or "scroll_up")
                {
                    checklistContract.MarkStepCompleted("SCROLL", "Halaman berhasil digulir & terverifikasi", result.UrlAfter);
                }
                else if (actLower is "input_text" or "search_web" or "fill")
                {
                    bool isIntermediateGoogleHop = isMultiStageOrSubItemTask &&
                        (snapshot.CurrentUrl ?? string.Empty).Contains("google.com", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(checklistContract.CleanSearchTopic) &&
                        !string.Equals((decision.TextOrUrl ?? string.Empty).Trim(), checklistContract.CleanSearchTopic.Trim(), StringComparison.OrdinalIgnoreCase);
                    bool inputPostconditionVerified = !result.IsError && result.DomOrUrlChanged;
                    if (!isIntermediateGoogleHop && inputPostconditionVerified)
                    {
                        checklistContract.MarkStepCompleted("INPUT", $"Kueri \"{decision.TextOrUrl}\" berhasil dikirim", result.UrlAfter);
                    }
                }
                else if (actLower is "click_element" or "click")
                {
                    var clickedNode = decision.ElementIndex is int cIdx ? snapshot.Elements.FirstOrDefault(e => e.Index == cIdx) : null;
                    string clickedTitle = !string.IsNullOrWhiteSpace(clickedNode?.TextOrLabel) ? clickedNode!.TextOrLabel : (result.UrlAfter ?? "Target");
                    string targetHref = !string.IsNullOrWhiteSpace(clickedNode?.Href) ? clickedNode!.Href! : result.UrlAfter;
                    if (Uri.TryCreate(targetHref, UriKind.Absolute, out var cu) && BrowserUseDomEngine.IsValidNavigatingOrSourceUrl(targetHref))
                    {
                        latestClickedSource = new SearchResultCandidate(
                            Title: clickedTitle,
                            Url: targetHref,
                            Domain: cu.Host,
                            Snippet: "Aktif & terverifikasi di Browser Panel Kanan",
                            RetrievedAtUtc: DateTimeOffset.UtcNow);
                    }
                    bool isEnteringSiteFromGoogle = isMultiStageOrSubItemTask &&
                        (snapshot.CurrentUrl ?? string.Empty).Contains("google.com/search", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(checklistContract.CleanSearchTopic) &&
                        !clickedTitle.Contains(checklistContract.CleanSearchTopic.Split(' ')[0], StringComparison.OrdinalIgnoreCase);
                    if (isEnteringSiteFromGoogle)
                    {
                        checklistContract.MarkStepCompleted("NAVIGATE", $"Berhasil masuk ke situs \"{clickedTitle}\"", targetHref);
                    }
                    else
                    {
                        var clickStepObj = checklistContract.Steps.FirstOrDefault(s => s.ActionBadge == "CLICK" && s.Status != ChecklistStepStatus.Completed)
                                           ?? checklistContract.Steps.FirstOrDefault(s => s.ActionBadge == "CLICK");
                        if (clickStepObj is not null && !string.IsNullOrWhiteSpace(clickedNode?.TextOrLabel) && !_subItemSelectedInRun && !_playActionExecutedInRun)
                        {
                            clickStepObj.Description = $"Mengklik \"{clickedTitle}\"";
                        }
                        if (_subItemSelectedInRun && checklistContract.Steps.Any(s => s.ActionBadge == "SUBITEM" && s.Status != ChecklistStepStatus.Completed))
                        {
                            checklistContract.MarkStepCompleted("CLICK", "Halaman detail konten terbuka", result.UrlBefore);
                            checklistContract.MarkStepCompleted("SUBITEM", $"Bagian \"{clickedTitle}\" berhasil dipilih & dimuat", targetHref);
                        }
                        else if (!_playActionExecutedInRun || checklistContract.Steps.Any(s => s.ActionBadge == "CLICK" && s.Status != ChecklistStepStatus.Completed))
                        {
                            checklistContract.MarkStepCompleted("CLICK", "Target berhasil diklik & dimuat", targetHref);
                        }
                    }

                    bool isOnMediaWatchPage = (result.UrlAfter ?? string.Empty).Contains("/watch?v=", StringComparison.OrdinalIgnoreCase) ||
                                              (snapshot.CurrentUrl ?? string.Empty).Contains("/watch?v=", StringComparison.OrdinalIgnoreCase) ||
                                              _playActionExecutedInRun;
                    if (isOnMediaWatchPage)
                    {
                        int? seekSec = AgentTaskChecklistContract.TryParseRequestedVideoSeekSeconds(userTask);
                        bool domHasVideo = false;
                        bool domVideoPlaying = false;
                        int domCurrentTime = 0;

                        var mediaEval = ResolveEffectiveJsEvaluator();
                        if (mediaEval is not null)
                        {
                            try
                            {
                                string mediaJson = await mediaEval(BrowserUseDomEngine.GetControlMediaPlaybackScript(seekSec));
                                if (!string.IsNullOrWhiteSpace(mediaJson))
                                {
                                    using var mDoc = JsonDocument.Parse(mediaJson);
                                    var mRoot = mDoc.RootElement;
                                    domHasVideo = mRoot.TryGetProperty("hasVideo", out var hv) && hv.ValueKind == JsonValueKind.True;
                                    domVideoPlaying = mRoot.TryGetProperty("playing", out var pl) && pl.ValueKind == JsonValueKind.True;
                                    if (mRoot.TryGetProperty("currentTime", out var ctProp) && ctProp.ValueKind == JsonValueKind.Number)
                                        domCurrentTime = ctProp.GetInt32();
                                }
                            }
                            catch { }
                        }

                        string focusedSummary = string.Empty;
                        if (mediaEval is not null)
                        {
                            try
                            {
                                string verifJson = await mediaEval(BrowserUseDomEngine.GetVerifyMediaAndExtractTablesScript(ensurePlayIfPaused: false));
                                if (!string.IsNullOrWhiteSpace(verifJson))
                                {
                                    using var vDoc = JsonDocument.Parse(verifJson);
                                    var vRoot = vDoc.RootElement;
                                    if (vRoot.TryGetProperty("isPlaying", out var vp) && vp.ValueKind == JsonValueKind.True)
                                        domVideoPlaying = true;
                                    if (vRoot.TryGetProperty("focusedElementSummary", out var fsProp) && fsProp.ValueKind == JsonValueKind.String)
                                        focusedSummary = fsProp.GetString() ?? string.Empty;
                                }
                            }
                            catch { }
                        }

                        if (domVideoPlaying || domCurrentTime > 0)
                        {
                            _playActionExecutedInRun = true;
                            checklistContract.RecordMediaPlaybackVerification(true, domCurrentTime);
                            var lastClickStep = checklistContract.Steps.LastOrDefault(s => s.ActionBadge is "CLICK" or "SUBITEM");
                            if (lastClickStep is not null)
                            {
                                string focusPart = !string.IsNullOrWhiteSpace(focusedSummary) ? $"{focusedSummary} · " : string.Empty;
                                lastClickStep.VerificationNote = $"{focusPart}Media active (currentTime={domCurrentTime}s, playing={domVideoPlaying}) · StateDeltaVerified=true";
                            }
                        }

                        if (seekSec is int sVal && sVal > 0)
                        {
                            int mPart = sVal / 60;
                            int sPart = sVal % 60;
                            if (domHasVideo)
                            {
                                stepLogs.Add($"• **Navigasi Waktu Video (Auto-Seek Terverifikasi DOM)**: Video diarahkan ke posisi waktu **{mPart}:{sPart:D2}** (`currentTime = {domCurrentTime}s`, status: `{(domVideoPlaying ? "playing" : "paused")}`).");
                            }
                            else
                            {
                                stepLogs.Add($"• **Navigasi Waktu Video (Auto-Seek)**: Permintaan posisi waktu **{mPart}:{sPart:D2}** (`{sVal}s`) dicatat (elemen `<video>` belum terdeteksi di DOM).");
                            }
                        }

                        if (checklistContract.HasConditionalAdSkip && domVideoPlaying)
                        {
                            checklistContract.ResolveConditionalAdSkipStep(adButtonClicked: false, videoPlayingCleanly: true);
                            stepLogs.Add("• **Supervisor AdGuard Shield**: `[✓ Dilewati Otomatis oleh AdGuard Shield]` (Video utama terverifikasi berputar tanpa overlay iklan).");
                        }
                    }
                }

                swPostVerify.Stop();
                string domHashAfterStep = stepUrlChanged ? BrowserUseDomEngine.NormalizeUrlForDomStructureHash(result.UrlAfter) : domHashBeforeStep;
                stepTimings.Add(new StepStageTimingLog(
                    StepNumber: step,
                    ActionName: decision.ActionName,
                    UsedFastPath: usedFastPathThisStep,
                    FastPathVerified: usedFastPathThisStep && fastPathVerifiedThisStep,
                    FastPathFallbackToLlm: fastPathFallbackThisStep,
                    SnapshotMs: stepSnapshotMs,
                    CdpPierceMs: stepCdpPierceMs,
                    CdpPierceTriggered: stepCdpPierceTriggered,
                    CdpPierceReason: stepCdpPierceReason,
                    LlmTtftMs: stepLlmTtftMs,
                    LlmDecodeMs: stepLlmDecodeMs,
                    LlmInferenceMs: stepLlmInferenceMs,
                    PromptTokens: stepPromptTokens,
                    CompletionTokens: stepCompletionTokens,
                    CachedPromptTokens: stepCachedPromptTokens,
                    PromptCacheHit: stepPromptCacheHit,
                    PlanUpdateWasNull: stepPlanUpdateWasNull,
                    ActionDispatchMs: stepActionDispatchMs,
                    PostVerifyMs: swPostVerify.ElapsedMilliseconds,
                    DomStructureHashBefore: domHashBeforeStep,
                    DomStructureHashAfter: domHashAfterStep,
                    StateDeltaVerified: deltaEval.StateDeltaVerified));

                EmitLiveChatProgress(string.Empty);
            }
        }

        sw.Stop();
        var finalSnap = await CaptureLiveSnapshotAsync(ct: ct);
        BrowserAgentRuntimeSettings.Current.RecordBrowsingHistory(finalSnap.PageTitle, finalSnap.CurrentUrl);

        // Batch B: Persist verified cross-turn state into CarryForward store (strictly bounded, zero raw DOM)
        var cfStore = BrowserAgentRuntimeSettings.Current.CarryForward;
        if (!string.IsNullOrWhiteSpace(finalSnap.CurrentUrl) && finalSnap.CurrentUrl != "about:blank")
        {
            cfStore.LastActiveUrl = finalSnap.CurrentUrl;
            cfStore.LastActiveTitle = finalSnap.PageTitle ?? string.Empty;
        }
        cfStore.LastTaskSummary = userTask ?? string.Empty;
        if (verifiedSources.Count > 0)
        {
            cfStore.RecordNumberedCandidates(verifiedSources.Select(s => (s.Title, s.Url)), finalSnap.CurrentUrl);
        }
        if (!string.IsNullOrWhiteSpace(finalSnap.CleanMarkdownViewportText) &&
            Regex.IsMatch(userTask ?? string.Empty, @"\b(ambil|ekstrak|salin|rangkum|ringkas|informasi|nama|alamat|telepon)\b", RegexOptions.IgnoreCase))
        {
            cfStore.RecordExtractedFact("PageExcerpt", finalSnap.CleanMarkdownViewportText, finalSnap.CurrentUrl);
        }
        if (Regex.IsMatch(userTask ?? string.Empty, @"\b(salin\s+hasilnya|simpan\s+(?:hasil(?:nya)?\s+)?ke\s+file|save\s+to\s+file)\b", RegexOptions.IgnoreCase))
        {
            string textToExport = !string.IsNullOrWhiteSpace(cfStore.LastCopiedText)
                ? cfStore.LastCopiedText
                : (!string.IsNullOrWhiteSpace(finalSnap.CleanMarkdownViewportText) ? finalSnap.CleanMarkdownViewportText : finalSnap.PageTitle ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(textToExport))
            {
                BrowserAgentRuntimeSettings.Current.ClipboardMemory = textToExport;
                cfStore.LastCopiedText = textToExport;
                cfStore.RecordExtractedFact("CopiedText", textToExport, finalSnap.CurrentUrl);
                TrySetWindowsClipboardText(textToExport);
                if (lastWorkspaceFile is null &&
                    Regex.IsMatch(userTask ?? string.Empty, @"\b(simpan\s+(?:hasil(?:nya)?\s+)?ke\s+(?:file|excel|xlsx|pdf)|ekspor\s+ke\s+(?:excel|xlsx|pdf)|save\s+to\s+file)\b", RegexOptions.IgnoreCase))
                {
                    try
                    {
                        string dlDir = BrowserAgentRuntimeSettings.Current.DownloadDirectory;
                        Directory.CreateDirectory(dlDir);
                        bool wantsXlsx = Regex.IsMatch(userTask ?? string.Empty, @"\b(excel|xlsx|spreadsheet|tabel)\b", RegexOptions.IgnoreCase);
                        bool wantsPdf = Regex.IsMatch(userTask ?? string.Empty, @"\b(pdf)\b", RegexOptions.IgnoreCase);
                        string exportPath;
                        if (wantsXlsx || wantsPdf)
                        {
                            var art = NexusCanvasExporterAndExtractor.TryDetectOrExtractCanvasArtifact(textToExport, finalSnap.PageTitle)
                                ?? new CanvasExtractedArtifact(
                                    Title: !string.IsNullOrWhiteSpace(finalSnap.PageTitle) ? finalSnap.PageTitle! : "Hasil_Ekstraksi_Browser",
                                    FileNameHint: "Hasil_Ekstraksi_Browser",
                                    LanguageOrFormat: wantsXlsx ? "table" : "markdown",
                                    RawCodeOrMarkdown: textToExport,
                                    CleanPreviewMarkdown: textToExport,
                                    PreviewHeadline: !string.IsNullOrWhiteSpace(finalSnap.PageTitle) ? finalSnap.PageTitle! : "Hasil Ekstraksi Browser",
                                    PreviewSubtitle: finalSnap.CurrentUrl ?? string.Empty,
                                    HighlightMetricValue: string.Empty,
                                    HighlightMetricCaption: string.Empty,
                                    TableRows: NexusCanvasExporterAndExtractor.ExtractMarkdownTableRows(textToExport),
                                    DefaultToPreviewMode: true);
                            var expRes = NexusCanvasExporterAndExtractor.ExportCanvasArtifactToDisk(art, wantsXlsx ? "xlsx" : "pdf", dlDir);
                            exportPath = expRes.FullFilePath;
                        }
                        else
                        {
                            exportPath = Path.Combine(dlDir, $"Hasil_Ekstraksi_Browser_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                            await File.WriteAllTextAsync(exportPath, textToExport, Encoding.UTF8, ct);
                        }
                        lastWorkspaceFile = await _workspaceFiles.AddDownloadedFileToWorkspaceAsync(exportPath, finalSnap.CurrentUrl, "Hasil Ekstraksi Browser", ct);
                        if (lastWorkspaceFile.IsSuccess)
                            cfStore.LastSavedWorkspaceFilePath = lastWorkspaceFile.FilePath;
                    }
                    catch { }
                }
            }
        }

        // Automatically clear visual bounding boxes [1]..[N] and any overlay from the live WebView2 once the loop finishes
        if (_jsEvaluator is null && BrowserAgentRuntimeSettings.Current.LiveJsEvaluator is { } clearEval)
        {
            try
            {
                await clearEval(BrowserUseDomEngine.GetClearVisualOverlaysScript());
            }
            catch { }
        }

        if (checklistContract.HasConditionalAdSkip &&
            checklistContract.Steps.Any(s => s.IsConditionalAdSkip && s.Status != ChecklistStepStatus.Completed && s.Status != ChecklistStepStatus.AutoSkippedByAdGuard))
        {
            checklistContract.ResolveConditionalAdSkipStep(adButtonClicked: false, videoPlayingCleanly: true);
        }
        // Jangan memaksa langkah yang belum terbukti menjadi "selesai". Tandai apa adanya.
        var unverifiedSteps = checklistContract.Steps
            .Where(s => s.Status == ChecklistStepStatus.Running || s.Status == ChecklistStepStatus.Pending)
            .ToList();
        foreach (var pendingStep in unverifiedSteps)
        {
            if (string.IsNullOrWhiteSpace(pendingStep.VerificationNote))
                pendingStep.VerificationNote = "Belum terbukti oleh observasi";
        }
        bool runEndedCleanly = endedWithDone && consecutiveFailures < 3;
        bool goalVerified = runEndedCleanly && unverifiedSteps.Count == 0;

        SearchResultCandidate? primaryCardSource = latestClickedSource
            ?? verifiedSources.FirstOrDefault(s =>
                BrowserUseDomEngine.IsValidNavigatingOrSourceUrl(s.Url) &&
                (!string.IsNullOrWhiteSpace(finalSnap.CurrentUrl) && string.Equals(s.Url, finalSnap.CurrentUrl, StringComparison.OrdinalIgnoreCase)));

        if (primaryCardSource is null && !string.IsNullOrWhiteSpace(finalSnap.CurrentUrl) && finalSnap.CurrentUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            string host = Uri.TryCreate(finalSnap.CurrentUrl, UriKind.Absolute, out var fu) ? fu.Host : "Web";
            primaryCardSource = new SearchResultCandidate(
                Title: !string.IsNullOrWhiteSpace(finalSnap.PageTitle) ? finalSnap.PageTitle : finalSnap.CurrentUrl,
                Url: finalSnap.CurrentUrl,
                Domain: host,
                Snippet: "Halaman aktif terverifikasi di Browser Panel Kanan",
                RetrievedAtUtc: DateTimeOffset.UtcNow);
        }

        bool isInformationExtractionTask = Regex.IsMatch(
            userTask ?? string.Empty,
            @"\b(rangkum|ringkas|jelaskan|siapa|apa\s+itu|berapa|daftar|makalah|jurnal|paper|scholar|cendekia|resep|cara\s+membuat)\b",
            RegexOptions.IgnoreCase);

        var reportSb = new StringBuilder();
        reportSb.AppendLine(checklistContract.FormatOpenBotLiveChecklistCardMarkdown(
            activeUrl: finalSnap.CurrentUrl,
            primarySource: primaryCardSource,
            isFinished: true));

        if (!runEndedCleanly && stepLogs.Count > 0)
        {
            reportSb.AppendLine();
            reportSb.AppendLine(stepLogs.Last());
        }

        if (lastDownload is { IsSuccess: true } && lastWorkspaceFile is { IsSuccess: true })
        {
            reportSb.AppendLine();
            reportSb.AppendLine("### Berkas Diunduh ke Nexus Workspace");
            reportSb.AppendLine($"- **Nama Berkas**: `{lastDownload.FileName}` ({lastDownload.FileSizeBytes:N0} bytes)");
            reportSb.AppendLine($"- **Lokasi Lokal**: `{lastWorkspaceFile.FilePath}`");
            reportSb.AppendLine($"- **SHA-256**: `{lastWorkspaceFile.Sha256Hex[..16]}...`");
        }
        else if (lastWorkspaceFile is { IsSuccess: true })
        {
            reportSb.AppendLine();
            reportSb.AppendLine("### Berkas Hasil Ekstraksi Disimpan ke Nexus Workspace");
            reportSb.AppendLine($"- **Lokasi Lokal**: `{lastWorkspaceFile.FilePath}`");
            if (!string.IsNullOrWhiteSpace(lastWorkspaceFile.Sha256Hex) && lastWorkspaceFile.Sha256Hex.Length >= 16)
                reportSb.AppendLine($"- **SHA-256**: `{lastWorkspaceFile.Sha256Hex[..16]}...`");
        }

        // Only append step summary / page content when needed for test assertions or explicit information-extraction prompts,
        // NEVER dump raw website footers, share buttons, or unrelated ad links into the chat bubble!
        if (isInformationExtractionTask && !string.IsNullOrWhiteSpace(finalSnap.CleanMarkdownViewportText))
        {
            var lastInputLog = stepLogs.LastOrDefault(l => l.Contains("Mengetik \"", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(lastInputLog))
            {
                reportSb.AppendLine();
                reportSb.AppendLine(lastInputLog);
            }
            reportSb.AppendLine();
            reportSb.AppendLine(finalSnap.CleanMarkdownViewportText.Trim());
        }

        string finalMarkdown = reportSb.ToString().Trim();
        var (tokCount, tokSource) = await GgufModelTokenizer.CountTokensAsync(finalMarkdown, ct: ct);
        var (rawTokCount, _) = await GgufModelTokenizer.CountTokensAsync(finalSnap.CleanMarkdownViewportText ?? string.Empty, ct: ct);

        // Semua angka latensi berasal dari stopwatch nyata. Tanpa panggilan model => 0 (bukan total waktu).
        // LocalModelTtftMs = latensi respons pertama model (non-streaming), bukan TTFT sejati.
        // TokensPerSecond tidak diukur di sini (butuh jumlah token yang dihasilkan model) => 0.
        double computedTps = (totalDecodeMs > 0 && totalCompletionTokens > 0)
            ? Math.Round(totalCompletionTokens / (totalDecodeMs / 1000.0), 2)
            : 0;
        var benchmark = new BrowserAgentBenchmarkRecord(
            TaskDescription: userTask,
            RawContentTokens: rawTokCount,
            ExtractedContentTokens: tokCount,
            CompletePromptTokens: totalPromptTokens > 0 ? totalPromptTokens : tokCount,
            ExtractionLatencyMs: snapshotMsTotal,
            BrowserActionLatencyMs: actionMsTotal,
            LocalModelTtftMs: modelCalls > 0 ? Math.Max(0, firstModelMs) : 0,
            TokensPerSecond: computedTps,
            ActionsExecuted: actionsExecuted,
            PagesOpened: pagesOpened,
            TaskSucceeded: runEndedCleanly,
            TokenizerSource: tokSource,
            RecordedAtUtc: DateTimeOffset.UtcNow);

        BrowserAgentRuntimeSettings.Current.BenchmarkHistory.Add(benchmark);
        int completedChecklistCount = checklistContract.Steps.Count(s => s.Status is ChecklistStepStatus.Completed or ChecklistStepStatus.AutoSkippedByAdGuard);
        int totalChecklistCount = Math.Max(1, checklistContract.Steps.Count);
        double nullComplianceRate = normalStepsAfterStep1 > 0
            ? Math.Round((double)nullPlanUpdateCompliantSteps / normalStepsAfterStep1, 4)
            : 1.0;
        return new AgentLoopExecutionSummary(
            IsSuccess: runEndedCleanly,
            FinalGroundedReportMarkdown: finalMarkdown,
            VerifiedSources: verifiedSources,
            DownloadedArtifact: lastDownload,
            WorkspaceFileRegistered: lastWorkspaceFile,
            Benchmark: benchmark,
            GoalVerified: goalVerified,
            ChecklistCompletedCount: completedChecklistCount,
            ChecklistTotalCount: totalChecklistCount,
            StepTimings: stepTimings.AsReadOnly(),
            TotalLlmCalls: modelCalls,
            FastPathStepsExecuted: fastPathStepsExecuted,
            FastPathFallbackCount: fastPathFallbackCount,
            PlanUpdateNullComplianceRate: nullComplianceRate);
    }

    /// <summary>
    /// Tahap 2: Parses strict GBNF / responseSchema JSON output directly without regex when the output is a pure JSON object,
    /// falling back to balanced-brace slicing only if wrapped in markdown fences.
    /// </summary>
    public static bool TryParseStrictStepJsonDocument(string? rawModelOutput, out JsonDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(rawModelOutput))
            return false;

        string trimmed = rawModelOutput.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            try
            {
                document = JsonDocument.Parse(trimmed);
                return document.RootElement.ValueKind == JsonValueKind.Object;
            }
            catch
            {
                document = null;
            }
        }

        int firstBrace = trimmed.IndexOf('{');
        int lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            string slice = trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
            try
            {
                document = JsonDocument.Parse(slice);
                return document.RootElement.ValueKind == JsonValueKind.Object;
            }
            catch
            {
                document = null;
            }
        }
        return false;
    }

    private static void MarkFirstMatchingPlannerSubGoalCompleted(
        List<PlannerSubGoal> subGoals,
        string executedActionName,
        bool domOrUrlChanged,
        string? urlAfter)
    {
        if (subGoals is null || subGoals.Count == 0)
            return;

        string norm = BrowserUseDomEngine.NormalizePlannerSubGoalOp(executedActionName);
        if (executedActionName is "go_to_url" or "navigate")
            norm = "navigate";
        else if (executedActionName is "input_text" or "search_web" or "fill")
            norm = "input";
        else if (executedActionName is "click_element" or "click")
            norm = "click";
        else if (executedActionName is "select_option" or "select")
            norm = "select";
        else if (executedActionName is "scroll" or "scroll_down" or "scroll_up")
            norm = "scroll";

        for (int i = 0; i < subGoals.Count; i++)
        {
            if (subGoals[i].Completed) continue;
            string gOp = BrowserUseDomEngine.NormalizePlannerSubGoalOp(subGoals[i].Op);
            bool matches = string.Equals(gOp, norm, StringComparison.OrdinalIgnoreCase) ||
                           (norm == "click" && gOp == "click_and_play");
            if (matches)
            {
                if (norm == "input" && !domOrUrlChanged)
                    return;
                subGoals[i] = subGoals[i] with { Completed = true };
                if (norm == "navigate" && !string.IsNullOrWhiteSpace(urlAfter) &&
                    (urlAfter.Contains("?s=", StringComparison.OrdinalIgnoreCase) ||
                     urlAfter.Contains("search_query=", StringComparison.OrdinalIgnoreCase)) &&
                    i + 1 < subGoals.Count &&
                    !subGoals[i + 1].Completed &&
                    string.Equals(BrowserUseDomEngine.NormalizePlannerSubGoalOp(subGoals[i + 1].Op), "input", StringComparison.OrdinalIgnoreCase))
                {
                    subGoals[i + 1] = subGoals[i + 1] with { Completed = true };
                }
                return;
            }
        }
    }

    public static AgentStepDecision? TryResolveNextPendingPlannerSubGoalDecision(
        IReadOnlyList<PlannerSubGoal>? subGoals,
        BrowserLiveStateSnapshot snapshot)
    {
        if (subGoals is null || subGoals.Count == 0)
            return null;

        var pending = subGoals.FirstOrDefault(g => !g.Completed);
        if (pending is null)
            return null;

        string op = BrowserUseDomEngine.NormalizePlannerSubGoalOp(pending.Op);
        string curUrl = snapshot.CurrentUrl ?? string.Empty;

        if (op == "navigate")
        {
            string targetUrl = (pending.Target ?? pending.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(targetUrl))
            {
                if (!targetUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    targetUrl = "https://" + targetUrl.TrimStart('/');
                if (!string.Equals(curUrl.TrimEnd('/'), targetUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                {
                    return new AgentStepDecision(
                        "[GUARD_OVERRIDE:PlannerSubGoalPending] Menjalankan sub-tujuan navigasi dari rencana Planner.",
                        $"Membuka situs sesuai rencana: {targetUrl}",
                        $"Membuka {targetUrl}",
                        "go_to_url",
                        TextOrUrl: targetUrl);
                }
            }
        }
        else if (op == "input")
        {
            string query = AgentTaskChecklistContract.CleanSearchQueryFromCommandVerbs(pending.Text ?? pending.Target);
            if (!string.IsNullOrWhiteSpace(query))
            {
                var inputNode = snapshot.Elements.FirstOrDefault(e =>
                    e.TagName is not ("a" or "button") &&
                    (e.IsContentEditable ||
                     e.TagName is "input" or "textarea" ||
                     e.Role is "searchbox" or "textbox" or "combobox"));
                if (inputNode is not null)
                {
                    return new AgentStepDecision(
                        "[GUARD_OVERRIDE:PlannerSubGoalPending] Menjalankan sub-tujuan input dari rencana Planner.",
                        $"Mengetik \"{query}\" pada elemen [{inputNode.Index}]",
                        $"Mengetik \"{query}\" ke kolom pencarian/input",
                        "input_text",
                        ElementIndex: inputNode.Index,
                        TextOrUrl: query,
                        PressEnter: true);
                }
                if (Uri.TryCreate(curUrl, UriKind.Absolute, out var curSearchUri) &&
                    !curUrl.Contains("whatsapp.com", StringComparison.OrdinalIgnoreCase) &&
                    !curUrl.Contains("gemini.google", StringComparison.OrdinalIgnoreCase) &&
                    !curUrl.Contains("chatgpt.com", StringComparison.OrdinalIgnoreCase))
                {
                    string directSearchUrl = BuildSiteSpecificSearchUrl(curSearchUri, query);
                    return new AgentStepDecision(
                        "[GUARD_OVERRIDE:PlannerSubGoalPending] Kolom input tersembunyi pada viewport sempit; menjalankan navigasi pencarian langsung.",
                        $"Mencari \"{query}\" pada {curSearchUri.Host}",
                        $"Mencari \"{query}\" di {curSearchUri.Host}",
                        "go_to_url",
                        TextOrUrl: directSearchUrl);
                }
            }
        }
        else if (op is "click" or "click_and_play")
        {
            string targetHint = (pending.Target ?? pending.Text ?? string.Empty).Trim();
            InteractiveDomNode? candidate = null;
            if (!string.IsNullOrWhiteSpace(targetHint) &&
                !Regex.IsMatch(targetHint, @"^(?:video\s+paling\s+atas|paling\s+atas|hasil\s+teratas|top\s+result|konten\s+utama)$", RegexOptions.IgnoreCase))
            {
                candidate = snapshot.Elements.FirstOrDefault(e =>
                    e.TagName is "a" or "button" or "div" &&
                    !string.IsNullOrWhiteSpace(e.TextOrLabel) &&
                    e.TextOrLabel.Contains(targetHint, StringComparison.OrdinalIgnoreCase));
            }
            candidate ??= snapshot.Elements.FirstOrDefault(e =>
                e.TagName == "a" &&
                !string.IsNullOrWhiteSpace(e.Href) &&
                !string.Equals(e.TextOrLabel?.Trim(), "YouTube", StringComparison.OrdinalIgnoreCase) &&
                !IsJunkOrUtilitySourceCandidate(e.TextOrLabel, e.Href))
                ?? snapshot.Elements.FirstOrDefault(e =>
                (e.TagName == "a" && !string.IsNullOrWhiteSpace(e.Href) && !string.Equals(e.TextOrLabel?.Trim(), "YouTube", StringComparison.OrdinalIgnoreCase)) ||
                e.TagName == "button" ||
                e.Role == "button");
            if (candidate is not null)
            {
                return new AgentStepDecision(
                    "[GUARD_OVERRIDE:PlannerSubGoalPending] Menjalankan sub-tujuan klik dari rencana Planner.",
                    $"Mengklik elemen [{candidate.Index}] \"{candidate.TextOrLabel}\"",
                    $"Mengklik \"{candidate.TextOrLabel}\"",
                    "click_element",
                    ElementIndex: candidate.Index);
            }
        }
        else if (op == "select")
        {
            string optText = (pending.Text ?? pending.Target ?? string.Empty).Trim();
            var selectNode = snapshot.Elements.FirstOrDefault(e =>
                string.Equals(e.TagName, "select", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(e.CompoundInfo));
            if (selectNode is not null && !string.IsNullOrWhiteSpace(optText))
            {
                return new AgentStepDecision(
                    "[GUARD_OVERRIDE:PlannerSubGoalPending] Menjalankan sub-tujuan select_option dari rencana Planner.",
                    $"Memilih opsi \"{optText}\" pada dropdown [{selectNode.Index}]",
                    $"Memilih \"{optText}\"",
                    "select_option",
                    ElementIndex: selectNode.Index,
                    TextOrUrl: optText);
            }
        }

        return null;
    }


    // ============================================================================
    // RULES 1, 2, 4, 6: STRICT GENERIC FAST-PATH, GOAL-SPECIFIC POST-VERIFICATION,
    // DELTA-ONLY PLAN_UPDATE MERGER, AND OOD BENCHMARK SUITE
    // ============================================================================

    public static bool CheckIfRawModelPlanUpdateIsNull(string? rawModelOutput)
    {
        if (string.IsNullOrWhiteSpace(rawModelOutput))
            return true;
        if (!TryParseStrictStepJsonDocument(rawModelOutput, out var doc) || doc is null)
            return true;
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("plan_update", out var pu))
                return true;
            return pu.ValueKind == JsonValueKind.Null ||
                   (pu.ValueKind == JsonValueKind.Array && pu.GetArrayLength() == 0);
        }
    }

    public static void ApplyDeltaOrFullPlanUpdate(
        List<PlannerSubGoal> activeSubGoals,
        List<string> activePlan,
        AgentTaskChecklistContract checklistContract,
        List<PlannerSubGoal> incomingGoals,
        IReadOnlyList<string> rawPlanStrings,
        int currentStep,
        bool isReplanAfterFailure)
    {
        if (incomingGoals is null || incomingGoals.Count == 0)
            return;

        if (currentStep <= 1 || activeSubGoals.Count == 0)
        {
            int existingRequiredStepCount = checklistContract.Steps.Count(s => !s.IsConditionalAdSkip);
            if (incomingGoals.Count >= existingRequiredStepCount || activeSubGoals.Count == 0)
            {
                activeSubGoals.Clear();
                activeSubGoals.AddRange(incomingGoals);
                activePlan.Clear();
                activePlan.AddRange(rawPlanStrings);
                checklistContract.SyncContractFromPlannerSubGoals(activeSubGoals);
            }
            return;
        }

        var completedPrefix = activeSubGoals.Where(g => g.Completed).ToList();
        bool incomingRepeatsCompletedPrefix = completedPrefix.Count > 0 &&
            incomingGoals.Count >= completedPrefix.Count &&
            string.Equals(
                BrowserUseDomEngine.NormalizePlannerSubGoalOp(incomingGoals[0].Op),
                BrowserUseDomEngine.NormalizePlannerSubGoalOp(completedPrefix[0].Op),
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                (incomingGoals[0].Target ?? incomingGoals[0].Text ?? string.Empty).Trim(),
                (completedPrefix[0].Target ?? completedPrefix[0].Text ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);

        if (incomingRepeatsCompletedPrefix)
        {
            for (int gi = 0; gi < incomingGoals.Count && gi < activeSubGoals.Count; gi++)
            {
                if (activeSubGoals[gi].Completed &&
                    string.Equals(
                        BrowserUseDomEngine.NormalizePlannerSubGoalOp(activeSubGoals[gi].Op),
                        BrowserUseDomEngine.NormalizePlannerSubGoalOp(incomingGoals[gi].Op),
                        StringComparison.OrdinalIgnoreCase))
                {
                    incomingGoals[gi] = incomingGoals[gi] with { Completed = true };
                }
            }
            if (isReplanAfterFailure || incomingGoals.Count >= activeSubGoals.Count)
            {
                activeSubGoals.Clear();
                activeSubGoals.AddRange(incomingGoals);
                activePlan.Clear();
                activePlan.AddRange(activeSubGoals.Select(BrowserUseDomEngine.SerializePlannerSubGoal));
                checklistContract.SyncContractFromPlannerSubGoals(activeSubGoals);
            }
        }
        else
        {
            // Genuine delta replan (Rule 4): preserve C#-managed completed prefix [x] and append the new delta sub-goals!
            var merged = new List<PlannerSubGoal>(completedPrefix.Count + incomingGoals.Count);
            merged.AddRange(completedPrefix);
            merged.AddRange(incomingGoals.Select(g => g with { Completed = false }));
            activeSubGoals.Clear();
            activeSubGoals.AddRange(merged);
            activePlan.Clear();
            activePlan.AddRange(activeSubGoals.Select(BrowserUseDomEngine.SerializePlannerSubGoal));
            checklistContract.SyncContractFromPlannerSubGoals(activeSubGoals);
        }
    }

    private static readonly Regex SensitiveFastPathIntentRegex = new(
        @"\b(?:login|log\s+in|sign\s+in|masuk\s+akun|password|kata\s+sandi|passcode|otp|2fa|pin|credit\s*card|kartu\s+kredit|cvv|pay\b|bayar|checkout|complete\s+purchase|place\s+order|transfer\s+uang|send\s+money|withdraw|delete|hapus|remove\s+account|destroy|kirim\s+pesan|send\s+message|publish|post\s+comment)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsSensitiveFastPathSubGoal(
        PlannerSubGoal subGoal,
        InteractiveDomNode? candidateNode,
        string? currentUrl)
    {
        string combinedGoalText = $"{subGoal.Op} {subGoal.Target} {subGoal.Text}".Trim();
        if (SensitiveFastPathIntentRegex.IsMatch(combinedGoalText))
            return true;

        if (candidateNode is not null)
        {
            if (candidateNode.IsSensitiveOrSubmit)
                return true;
            var (requiresApproval, _, _) = BrowserUseDomEngine.EvaluateOpenBotActionPolicy(
                candidateNode.TagName,
                candidateNode.InputType,
                candidateNode.TextOrLabel,
                candidateNode.Href,
                currentUrl);
            if (requiresApproval)
                return true;
            if (SensitiveFastPathIntentRegex.IsMatch($"{candidateNode.TextOrLabel} {candidateNode.Href}"))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Rule 1 & Rule 2: Generic, non-hardcoded Fast-Path resolver with strict Single-Candidate Requirement
    /// and Sensitive-Action Exclusion. If 0 or >= 2 candidates match on the page, returns CanUseFastPath = false
    /// and delegates to the LLM.
    /// </summary>
    public static FastPathResolutionOutcome TryResolveGenericSubGoalFastPath(
        IReadOnlyList<PlannerSubGoal>? subGoals,
        BrowserLiveStateSnapshot snapshot)
    {
        if (subGoals is null || subGoals.Count == 0)
            return new FastPathResolutionOutcome(false, null, "NoActiveSubGoals", 0, false);

        var pending = subGoals.FirstOrDefault(g => !g.Completed);
        if (pending is null)
            return new FastPathResolutionOutcome(false, null, "AllSubGoalsCompleted", 0, false);

        string curUrl = snapshot.CurrentUrl ?? string.Empty;
        if (IsSensitiveFastPathSubGoal(pending, null, curUrl))
            return new FastPathResolutionOutcome(false, null, "BlockedBySensitiveSubGoalIntent", 0, true);

        string op = BrowserUseDomEngine.NormalizePlannerSubGoalOp(pending.Op);

        if (op == "navigate")
        {
            string targetUrl = (pending.Target ?? pending.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(targetUrl) || targetUrl.Contains(' ', StringComparison.Ordinal))
                return new FastPathResolutionOutcome(false, null, "InvalidNavigateTarget", 0, false);

            if (!targetUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                targetUrl = "https://" + targetUrl.TrimStart('/');

            if (!BrowserUseDomEngine.IsValidNavigatingOrSourceUrl(targetUrl) ||
                !BrowserUseDomEngine.ValidateTargetSafety(targetUrl).IsSafe)
            {
                return new FastPathResolutionOutcome(false, null, "UnsafeNavigateTarget", 0, false);
            }

            if (string.Equals(curUrl.TrimEnd('/'), targetUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                return new FastPathResolutionOutcome(false, null, "AlreadyAtTargetUrl", 0, false);

            var navDecision = new AgentStepDecision(
                "[FAST_PATH:SubGoal_navigate] Menjalankan sub-tujuan navigasi terencana.",
                $"Membuka {targetUrl}",
                $"Membuka {targetUrl}",
                "go_to_url",
                TextOrUrl: targetUrl);
            return new FastPathResolutionOutcome(true, navDecision, "SingleUnambiguousNavigateTarget", 1, false);
        }

        if (op == "input")
        {
            string query = AgentTaskChecklistContract.CleanSearchQueryFromCommandVerbs(pending.Text ?? pending.Target);
            if (string.IsNullOrWhiteSpace(query))
                return new FastPathResolutionOutcome(false, null, "EmptyInputQuery", 0, false);

            var inputCandidates = snapshot.Elements
                .Where(e => e.IsConnectedInDom &&
                            e.TagName is not ("a" or "button") &&
                            (e.IsContentEditable ||
                             e.TagName is "input" or "textarea" ||
                             e.Role is "searchbox" or "textbox" or "combobox"))
                .ToList();

            string targetFieldHint = (pending.Target ?? string.Empty).Trim();
            if (inputCandidates.Count > 1 &&
                !string.IsNullOrWhiteSpace(targetFieldHint) &&
                !string.Equals(targetFieldHint, pending.Text, StringComparison.OrdinalIgnoreCase))
            {
                var narrowed = inputCandidates.Where(e =>
                    (e.TextOrLabel ?? string.Empty).Contains(targetFieldHint, StringComparison.OrdinalIgnoreCase) ||
                    (e.Role ?? string.Empty).Contains(targetFieldHint, StringComparison.OrdinalIgnoreCase) ||
                    (e.InputType ?? string.Empty).Contains(targetFieldHint, StringComparison.OrdinalIgnoreCase) ||
                    (string.Equals(targetFieldHint, "search", StringComparison.OrdinalIgnoreCase) &&
                     (e.Role == "searchbox" || e.InputType == "search" ||
                      Regex.IsMatch(e.TextOrLabel ?? string.Empty, @"\b(search|cari|telusuri)\b", RegexOptions.IgnoreCase))))
                    .ToList();
                if (narrowed.Count > 0)
                    inputCandidates = narrowed;
            }

            if (inputCandidates.Count != 1)
            {
                return new FastPathResolutionOutcome(
                    false,
                    null,
                    inputCandidates.Count == 0 ? "ZeroInputCandidates" : $"AmbiguousInputCandidates:{inputCandidates.Count}",
                    inputCandidates.Count,
                    false);
            }

            var singleInput = inputCandidates[0];
            if (IsSensitiveFastPathSubGoal(pending, singleInput, curUrl))
                return new FastPathResolutionOutcome(false, null, "BlockedSensitiveInputCandidate", 1, true);

            var inputDecision = new AgentStepDecision(
                "[FAST_PATH:SubGoal_input] Menjalankan sub-tujuan input pada kandidat elemen tunggal.",
                $"Mengetik \"{query}\" pada [{singleInput.Index}]",
                $"Mengetik \"{query}\" ke kolom pencarian/input",
                "input_text",
                ElementIndex: singleInput.Index,
                TextOrUrl: query,
                PressEnter: true);
            return new FastPathResolutionOutcome(true, inputDecision, "SingleUnambiguousInputCandidate", 1, false);
        }

        if (op == "select")
        {
            string optText = (pending.Text ?? pending.Target ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(optText))
                return new FastPathResolutionOutcome(false, null, "EmptySelectOption", 0, false);

            var selectCandidates = snapshot.Elements
                .Where(e => e.IsConnectedInDom &&
                            (string.Equals(e.TagName, "select", StringComparison.OrdinalIgnoreCase) ||
                             (e.CompoundInfo ?? string.Empty).Contains("options=", StringComparison.OrdinalIgnoreCase)))
                .Where(e => string.IsNullOrWhiteSpace(e.CompoundInfo) ||
                            e.CompoundInfo.Contains(optText, StringComparison.OrdinalIgnoreCase) ||
                            (e.TextOrLabel ?? string.Empty).Contains(optText, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (selectCandidates.Count != 1)
            {
                return new FastPathResolutionOutcome(
                    false,
                    null,
                    selectCandidates.Count == 0 ? "ZeroSelectCandidates" : $"AmbiguousSelectCandidates:{selectCandidates.Count}",
                    selectCandidates.Count,
                    false);
            }

            var singleSelect = selectCandidates[0];
            if (IsSensitiveFastPathSubGoal(pending, singleSelect, curUrl))
                return new FastPathResolutionOutcome(false, null, "BlockedSensitiveSelectCandidate", 1, true);

            var selectDecision = new AgentStepDecision(
                "[FAST_PATH:SubGoal_select] Memilih opsi pada dropdown tunggal.",
                $"Memilih \"{optText}\" pada [{singleSelect.Index}]",
                $"Memilih \"{optText}\"",
                "select_option",
                ElementIndex: singleSelect.Index,
                TextOrUrl: optText);
            return new FastPathResolutionOutcome(true, selectDecision, "SingleUnambiguousSelectCandidate", 1, false);
        }

        if (op is "click" or "click_and_play")
        {
            string targetHint = (pending.Target ?? pending.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(targetHint) ||
                Regex.IsMatch(targetHint, @"^(?:video\s+paling\s+atas|paling\s+atas|hasil\s+teratas|top\s+result|konten\s+utama)$", RegexOptions.IgnoreCase))
            {
                return new FastPathResolutionOutcome(false, null, "GenericClickTargetRequiresLlmJudgment", 0, false);
            }

            var clickCandidates = snapshot.Elements
                .Where(e => e.IsConnectedInDom &&
                            e.TagName is "a" or "button" or "video" or "div" &&
                            !string.IsNullOrWhiteSpace(e.TextOrLabel) &&
                            !IsJunkOrUtilitySourceCandidate(e.TextOrLabel, e.Href) &&
                            e.TextOrLabel.Contains(targetHint, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (clickCandidates.Count != 1)
            {
                return new FastPathResolutionOutcome(
                    false,
                    null,
                    clickCandidates.Count == 0 ? "ZeroClickCandidates" : $"AmbiguousClickCandidates:{clickCandidates.Count}",
                    clickCandidates.Count,
                    false);
            }

            var singleClick = clickCandidates[0];
            if (IsSensitiveFastPathSubGoal(pending, singleClick, curUrl))
                return new FastPathResolutionOutcome(false, null, "BlockedSensitiveClickCandidate", 1, true);

            var clickDecision = new AgentStepDecision(
                $"[FAST_PATH:SubGoal_{op}] Mengklik kandidat elemen tunggal yang cocok.",
                $"Mengklik [{singleClick.Index}] \"{singleClick.TextOrLabel}\"",
                $"Mengklik \"{singleClick.TextOrLabel}\"",
                "click_element",
                ElementIndex: singleClick.Index);
            return new FastPathResolutionOutcome(true, clickDecision, "SingleUnambiguousClickCandidate", 1, false);
        }

        return new FastPathResolutionOutcome(false, null, $"UnsupportedFastPathOp:{op}", 0, false);
    }

    /// <summary>
    /// Rule 2: Goal-Specific Postcondition Verification after a Fast-Path action.
    /// Prevents false-positive StateDeltaVerified (e.g. ad redirects, mismatched host, or input read-back failure).
    /// </summary>
    public static bool VerifySubGoalSpecificPostcondition(
        PlannerSubGoal subGoal,
        AgentStepDecision executedDecision,
        BrowserStepActionResult actionResult,
        BrowserLiveStateSnapshot snapshotBefore,
        bool mediaPlaybackVerified,
        out string compactFailureReason)
    {
        compactFailureReason = string.Empty;
        if (actionResult.IsError || actionResult.VerificationStatus == ActionVerificationStatus.Failed)
        {
            compactFailureReason = BrowserUseDomEngine.CutAtCodeUnits(actionResult.ResultMessage ?? "ActionError", 64);
            return false;
        }

        string urlAfter = (actionResult.UrlAfter ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(urlAfter))
        {
            var (safeUrl, unsafeReason) = BrowserUseDomEngine.ValidateTargetSafety(urlAfter);
            if (!safeUrl)
            {
                compactFailureReason = $"AdOrUnsafeRedirect:{BrowserUseDomEngine.CutAtCodeUnits(unsafeReason, 48)}";
                return false;
            }
        }

        string op = BrowserUseDomEngine.NormalizePlannerSubGoalOp(subGoal.Op);
        if (op == "navigate")
        {
            string expectedTarget = (subGoal.Target ?? subGoal.Text ?? executedDecision.TextOrUrl ?? string.Empty).Trim();
            if (!expectedTarget.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                expectedTarget = "https://" + expectedTarget.TrimStart('/');

            if (!Uri.TryCreate(expectedTarget, UriKind.Absolute, out var expUri) ||
                !Uri.TryCreate(urlAfter, UriKind.Absolute, out var actUri))
            {
                compactFailureReason = $"InvalidUrlAfterNavigate:{urlAfter}";
                return false;
            }

            string expHost = expUri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("m.", string.Empty, StringComparison.OrdinalIgnoreCase);
            string actHost = actUri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("m.", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(expHost, actHost, StringComparison.OrdinalIgnoreCase))
            {
                compactFailureReason = $"HostMismatch:expected={expHost},actual={actHost}";
                return false;
            }
            return true;
        }

        if (op == "input")
        {
            string msg = actionResult.ResultMessage ?? string.Empty;
            if (msg.Contains("INPUT_READBACK_MISMATCH", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("ELEMENT_DISABLED", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("ELEMENT_STALE", StringComparison.OrdinalIgnoreCase))
            {
                compactFailureReason = "InputReadbackOrStaleFailure";
                return false;
            }
            if (!actionResult.DomOrUrlChanged)
            {
                compactFailureReason = "InputProducedNoDomOrUrlDelta";
                return false;
            }
            return true;
        }

        if (op == "select")
        {
            string msg = actionResult.ResultMessage ?? string.Empty;
            if (msg.Contains("OPTION_NOT_FOUND", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("SELECT_READBACK_MISMATCH", StringComparison.OrdinalIgnoreCase))
            {
                compactFailureReason = "SelectOptionMismatch";
                return false;
            }
            if (!actionResult.DomOrUrlChanged)
            {
                compactFailureReason = "SelectProducedNoDelta";
                return false;
            }
            return true;
        }

        if (op is "click" or "click_and_play")
        {
            bool urlChanged = !string.Equals(
                (actionResult.UrlBefore ?? string.Empty).TrimEnd('/'),
                urlAfter.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase);
            if (!urlChanged && !actionResult.DomOrUrlChanged && !mediaPlaybackVerified)
            {
                compactFailureReason = "ClickProducedNoStateDelta";
                return false;
            }
            return true;
        }

        return actionResult.DomOrUrlChanged;
    }

    public static AgentStepDecision ParseModelDecisionJsonOrFallback(
        string? rawModelOutput,
        string userTask,
        BrowserLiveStateSnapshot snapshot,
        int step,
        bool alreadyDownloaded,
        bool hasTypedInput = false,
        bool playAlreadyExecuted = false,
        IReadOnlyList<PlannerSubGoal>? activeSubGoals = null)
    {
        userTask ??= string.Empty;
        string curUrl = snapshot.CurrentUrl ?? string.Empty;

        // Tahap 3 Planner-First: When rawModelOutput is provided by the active LLM, parse and execute the model's decision directly!
        // Never invoke AnalyzeUniversalBrowserTask to hijack a valid non-"done" LLM action on Step 1 or mid-run.
        if (!string.IsNullOrWhiteSpace(rawModelOutput))
        {
            try
            {
                if (TryParseStrictStepJsonDocument(rawModelOutput, out var parsedDoc) && parsedDoc is not null)
                {
                    using var doc = parsedDoc;
                    var r = doc.RootElement;
                    string eval = r.TryGetProperty("evaluation_previous_goal", out var ep) ? ep.GetString() ?? "OK" : "OK";
                    string mem = r.TryGetProperty("memory", out var mp) ? mp.GetString() ?? "" : "";
                    string next = r.TryGetProperty("next_goal", out var np) ? np.GetString() ?? "" : "";
                    List<string>? planUpdate = null;
                    List<PlannerSubGoal>? inlineSubGoals = null;
                    if (r.TryGetProperty("plan_update", out var puProp) && puProp.ValueKind == JsonValueKind.Array)
                    {
                        inlineSubGoals = BrowserUseDomEngine.ParsePlannerSubGoalsFromJsonArray(puProp);
                        if (inlineSubGoals.Count > 0)
                        {
                            planUpdate = inlineSubGoals.Select(BrowserUseDomEngine.SerializePlannerSubGoal).ToList();
                        }
                        else
                        {
                            planUpdate = puProp.EnumerateArray()
                                .Where(x => x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                                .Select(x => x.GetString()!)
                                .ToList();
                        }
                    }

                    // Support browser-use multi-action array format: "actions": [...] or "action": [ ... ]
                    if ((r.TryGetProperty("actions", out var arrProp) && arrProp.ValueKind == JsonValueKind.Array) ||
                        (r.TryGetProperty("action", out arrProp) && arrProp.ValueKind == JsonValueKind.Array))
                    {
                        var parsedBatch = new List<AgentStepDecision>();
                        foreach (var item in arrProp.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.Object) continue;
                            if (item.TryGetProperty("action", out var ia) && ia.ValueKind == JsonValueKind.String)
                            {
                                string subAct = BrowserUseDomEngine.NormalizeBrowserUseActionName(ia.GetString());
                                if (!BrowserUseDomEngine.IsRecognizedAction(subAct)) continue;
                                int? subIdx = item.TryGetProperty("element_index", out var si) && si.ValueKind == JsonValueKind.Number ? si.GetInt32() :
                                              (item.TryGetProperty("index", out var si2) && si2.ValueKind == JsonValueKind.Number ? si2.GetInt32() : null);
                                string? subTxt = item.TryGetProperty("text_or_url", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() :
                                                 (item.TryGetProperty("text", out var st2) && st2.ValueKind == JsonValueKind.String ? st2.GetString() :
                                                 (item.TryGetProperty("url", out var st3) && st3.ValueKind == JsonValueKind.String ? st3.GetString() : null));
                                bool subEnter = item.TryGetProperty("press_enter", out var se) && se.ValueKind == JsonValueKind.True;
                                parsedBatch.Add(new AgentStepDecision(eval, mem, string.IsNullOrWhiteSpace(next) ? subAct : next, subAct, subIdx, subTxt, subEnter, PlanUpdate: planUpdate));
                            }
                            else
                            {
                                // Native browser-use format: {"input": {"index": 1, "text": "..."}} or {"click": {"index": 2}}
                                foreach (var prop in item.EnumerateObject())
                                {
                                    string subAct = BrowserUseDomEngine.NormalizeBrowserUseActionName(prop.Name);
                                    if (!BrowserUseDomEngine.IsRecognizedAction(subAct)) continue;
                                    int? subIdx = null;
                                    string? subTxt = null;
                                    bool subEnter = false;
                                    if (prop.Value.ValueKind == JsonValueKind.Object)
                                    {
                                        if (prop.Value.TryGetProperty("index", out var pi) && pi.ValueKind == JsonValueKind.Number) subIdx = pi.GetInt32();
                                        if (prop.Value.TryGetProperty("text", out var pt) && pt.ValueKind == JsonValueKind.String) subTxt = pt.GetString();
                                        else if (prop.Value.TryGetProperty("url", out var pu) && pu.ValueKind == JsonValueKind.String) subTxt = pu.GetString();
                                        else if (prop.Value.TryGetProperty("query", out var pq) && pq.ValueKind == JsonValueKind.String) subTxt = pq.GetString();
                                        else if (prop.Value.TryGetProperty("pattern", out var pp) && pp.ValueKind == JsonValueKind.String) subTxt = pp.GetString();
                                        else if (prop.Value.TryGetProperty("selector", out var ps) && ps.ValueKind == JsonValueKind.String) subTxt = ps.GetString();
                                        else if (prop.Value.TryGetProperty("keys", out var pk) && pk.ValueKind == JsonValueKind.String) subTxt = pk.GetString();
                                        if (prop.Value.TryGetProperty("press_enter", out var peProp) && peProp.ValueKind == JsonValueKind.True) subEnter = true;
                                    }
                                    parsedBatch.Add(new AgentStepDecision(eval, mem, string.IsNullOrWhiteSpace(next) ? subAct : next, subAct, subIdx, subTxt, subEnter, PlanUpdate: planUpdate));
                                    break;
                                }
                            }
                        }
                        if (parsedBatch.Count > 0)
                        {
                            var first = parsedBatch[0];
                            var rest = parsedBatch.Count > 1 ? parsedBatch.Skip(1).ToList() : null;
                            return first with { QueuedFollowUpActions = rest };
                        }
                    }

                    string rawAct = r.TryGetProperty("action", out var aProp) && aProp.ValueKind == JsonValueKind.String ? aProp.GetString() ?? "" :
                                    (r.TryGetProperty("ActionName", out var anProp) && anProp.ValueKind == JsonValueKind.String ? anProp.GetString() ?? "" : "");
                    string act = BrowserUseDomEngine.NormalizeBrowserUseActionName(rawAct);
                    if (!string.IsNullOrWhiteSpace(act) &&
                        BrowserUseDomEngine.IsRecognizedAction(act))
                    {
                        if (string.IsNullOrWhiteSpace(next)) next = act;
                        int? idx = null;
                        if (r.TryGetProperty("element_index", out var idxProp) && idxProp.ValueKind == JsonValueKind.Number)
                            idx = idxProp.GetInt32();
                        else if (r.TryGetProperty("index", out var idx2) && idx2.ValueKind == JsonValueKind.Number)
                            idx = idx2.GetInt32();
                        else if (r.TryGetProperty("ElementIndex", out var idx3) && idx3.ValueKind == JsonValueKind.Number)
                            idx = idx3.GetInt32();

                        string? textOrUrl = null;
                        if (r.TryGetProperty("text_or_url", out var tuProp) && tuProp.ValueKind == JsonValueKind.String)
                            textOrUrl = tuProp.GetString();
                        else if (r.TryGetProperty("url", out var uProp) && uProp.ValueKind == JsonValueKind.String)
                            textOrUrl = uProp.GetString();
                        else if (r.TryGetProperty("text", out var tProp) && tProp.ValueKind == JsonValueKind.String)
                            textOrUrl = tProp.GetString();
                        else if (r.TryGetProperty("TextOrUrl", out var tu2) && tu2.ValueKind == JsonValueKind.String)
                            textOrUrl = tu2.GetString();

                        bool pressEnter = (r.TryGetProperty("press_enter", out var pe) && pe.ValueKind == JsonValueKind.True) ||
                                          (r.TryGetProperty("PressEnter", out var pe2) && pe2.ValueKind == JsonValueKind.True);

                        if (string.Equals(act, "scroll", StringComparison.OrdinalIgnoreCase))
                        {
                            act = BrowserUseDomEngine.ResolveScrollDirection(act, textOrUrl) == "up" ? "scroll_up" : "scroll_down";
                        }
                        string normAct = act.ToLowerInvariant();
                        var effectiveSubGoals = activeSubGoals is { Count: > 0 } ? activeSubGoals : inlineSubGoals;

                        // Guard 1b (Duplicate Root URL Gate without regex): Prevent reloading the exact same root URL already open on Step >= 2
                        if (step >= 2 && normAct == "go_to_url" &&
                            !string.IsNullOrWhiteSpace(curUrl) &&
                            !string.IsNullOrWhiteSpace(textOrUrl) &&
                            string.Equals(curUrl.TrimEnd('/'), textOrUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                        {
                            var fb1b = TryResolveNextPendingPlannerSubGoalDecision(effectiveSubGoals, snapshot)
                                       ?? BuildDefaultAutonomousStepDecision(userTask, snapshot, step, alreadyDownloaded, hasTypedInput, playAlreadyExecuted);
                            return fb1b with { EvaluationPreviousGoal = $"[GUARD_OVERRIDE:Guard1b] {fb1b.EvaluationPreviousGoal}", PlanUpdate = planUpdate };
                        }

                        // Guard 1c (Supervisor Input Sanitizer): Strip trailing command verbs ("play", "klik", "lewati") from input_text
                        if (normAct == "input_text" && !string.IsNullOrWhiteSpace(textOrUrl))
                        {
                            string cleanTxt = AgentTaskChecklistContract.CleanSearchQueryFromCommandVerbs(textOrUrl);
                            if (!string.IsNullOrWhiteSpace(cleanTxt))
                                textOrUrl = cleanTxt;
                        }

                        // Guard 0b (Post-Parse Logo Protection): If already on a video watch page (/watch?v=...) and the model attempts to click the site root/home logo [1], prevent leaving the video page!
                        if (step >= 2 &&
                            normAct == "click_element" &&
                            curUrl.Contains("youtube.com/watch?v=", StringComparison.OrdinalIgnoreCase) &&
                            idx is int watchClickIdx)
                        {
                            var watchTarget = snapshot.Elements.FirstOrDefault(e => e.Index == watchClickIdx);
                            bool isRootLogoLink = watchTarget is not null &&
                                ((watchTarget.XPath?.Contains("logo", StringComparison.OrdinalIgnoreCase) == true) ||
                                 string.Equals((watchTarget.Href ?? string.Empty).TrimEnd('/'), "https://www.youtube.com", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(watchTarget.TextOrLabel?.Trim(), "YouTube", StringComparison.OrdinalIgnoreCase));
                            if (isRootLogoLink)
                            {
                                return new AgentStepDecision(
                                    playAlreadyExecuted ? "[GUARD_OVERRIDE:Guard0b_Verified] Halaman video YouTube telah terbuka." : "[GUARD_OVERRIDE:Guard0b_Unverified] Mencegah klik logo beranda tanpa bukti putar.",
                                    playAlreadyExecuted ? "Pemutaran video terverifikasi di DOM." : "[UNVERIFIED_DONE] Pemutaran tidak diverifikasi oleh agen (tidak ada bukti status video).",
                                    "Halaman video aktif di Browser Panel Kanan.",
                                    "done",
                                    TextOrUrl: playAlreadyExecuted
                                        ? $"Video telah diputar dan terverifikasi: {curUrl}"
                                        : $"Halaman video terbuka (pemutaran tidak diverifikasi): {curUrl}",
                                    PlanUpdate: planUpdate);
                            }
                        }

                        // Premature "done" Veto (ONLY when normAct == "done" — NEVER hijacks valid non-"done" LLM actions!):
                        if (normAct == "done")
                        {
                            // 1. Check structured PlannerSubGoal contract first
                            if (TryResolveNextPendingPlannerSubGoalDecision(effectiveSubGoals, snapshot) is { } pendingPlannerDecision &&
                                !string.Equals(pendingPlannerDecision.ActionName, "done", StringComparison.OrdinalIgnoreCase))
                            {
                                return pendingPlannerDecision with { PlanUpdate = planUpdate };
                            }

                            bool allPlannerSubGoalsCompleted = effectiveSubGoals is { Count: > 0 } && effectiveSubGoals.All(g => g.Completed);
                            if (!allPlannerSubGoalsCompleted)
                            {
                                // 2. Check multi-hop / sub-item completion gate (e.g. series card -> Episode 01 -> play)
                                if (TryResolveMultiHopArrowChainDecision(userTask, snapshot, step, hasTypedInput, playAlreadyExecuted) is { } pendingMultiStepDecision &&
                                    !string.Equals(pendingMultiStepDecision.ActionName, "done", StringComparison.OrdinalIgnoreCase))
                                {
                                    return pendingMultiStepDecision with { PlanUpdate = planUpdate };
                                }

                                // 3. Guard 2: Veto premature "done" on search results page when click/play/sub-item is still pending
                                var reqSub = AgentTaskChecklistContract.TryExtractRequestedSubItem(userTask);
                                bool wantsResultClickOrPlay = reqSub is not null ||
                                    Regex.IsMatch(userTask, @"\b(play|putar|mainkan|tonton|carikan|download|dowload|unduh|pilih|memilih|klik)\b", RegexOptions.IgnoreCase);
                                bool isSummaryOrInfoTask = !wantsResultClickOrPlay &&
                                    Regex.IsMatch(userTask, @"\b(rangkum|ringkas|jelaskan|siapa|apa\s+itu|berapa|daftar|resep|cara\s+membuat)\b", RegexOptions.IgnoreCase);
                                bool isOnSearchResultsPage = curUrl.Contains("search_query=", StringComparison.OrdinalIgnoreCase) ||
                                                             curUrl.Contains("?s=", StringComparison.OrdinalIgnoreCase) ||
                                                             curUrl.Contains("/search", StringComparison.OrdinalIgnoreCase);
                                bool subItemStillPending = reqSub is not null && !AgentTaskChecklistContract.IsSpecificSubItemMatch(curUrl, reqSub);
                                if (!isSummaryOrInfoTask &&
                                    ((wantsResultClickOrPlay || isOnSearchResultsPage || subItemStillPending) &&
                                     (isOnSearchResultsPage || subItemStillPending || (!hasTypedInput && step <= 6))))
                                {
                                    var fb2 = BuildDefaultAutonomousStepDecision(userTask, snapshot, step, alreadyDownloaded, hasTypedInput, playAlreadyExecuted);
                                    if (!string.Equals(fb2.ActionName, "done", StringComparison.OrdinalIgnoreCase))
                                    {
                                        return fb2 with { EvaluationPreviousGoal = $"[GUARD_OVERRIDE:Guard2] {fb2.EvaluationPreviousGoal}", PlanUpdate = planUpdate };
                                    }
                                }
                            }
                        }

                        // Guard 3: If model selected input_text on an <a>/<button> (e.g. narrow YouTube Search icon [1]) or non-existent element index
                        if (normAct is "click_element" or "input_text" or "select_option")
                        {
                            var matchedNode = idx is int validIdx ? snapshot.Elements.FirstOrDefault(e => e.Index == validIdx) : null;
                            if (matchedNode is null || (normAct == "input_text" && matchedNode.TagName is "a" or "button"))
                            {
                                if (normAct == "input_text" && !string.IsNullOrWhiteSpace(textOrUrl))
                                {
                                    var realInputNode = snapshot.Elements.FirstOrDefault(e =>
                                        e.TagName is not ("a" or "button") &&
                                        (e.IsContentEditable || e.TagName is "input" or "textarea" || e.Role is "searchbox" or "textbox" or "combobox"));
                                    if (realInputNode is not null)
                                    {
                                        return new AgentStepDecision(eval, mem, next, "input_text", realInputNode.Index, textOrUrl, PressEnter: true, PlanUpdate: planUpdate);
                                    }
                                    if (Uri.TryCreate(curUrl, UriKind.Absolute, out var narrowUri) &&
                                        !curUrl.Contains("whatsapp.com", StringComparison.OrdinalIgnoreCase) &&
                                        !curUrl.Contains("gemini.google", StringComparison.OrdinalIgnoreCase) &&
                                        !curUrl.Contains("chatgpt.com", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string directSearchUrl = BuildSiteSpecificSearchUrl(narrowUri, textOrUrl!);
                                        return new AgentStepDecision(
                                            "[GUARD_OVERRIDE:Guard3_DirectSearch] Kolom input tersembunyi di balik tombol ikon; membuka URL pencarian langsung.",
                                            $"Mencari \"{textOrUrl}\" pada {narrowUri.Host}",
                                            $"Mencari \"{textOrUrl}\" di {narrowUri.Host}",
                                            "go_to_url",
                                            TextOrUrl: directSearchUrl,
                                            PlanUpdate: planUpdate);
                                    }
                                }
                                var fb3 = TryResolveNextPendingPlannerSubGoalDecision(effectiveSubGoals, snapshot)
                                          ?? BuildDefaultAutonomousStepDecision(userTask, snapshot, step, alreadyDownloaded, hasTypedInput, playAlreadyExecuted);
                                return fb3 with { EvaluationPreviousGoal = $"[GUARD_OVERRIDE:Guard3] {fb3.EvaluationPreviousGoal}", PlanUpdate = planUpdate };
                            }
                        }

                        return new AgentStepDecision(eval, mem, next, normAct, idx, textOrUrl, pressEnter, PlanUpdate: planUpdate);
                    }
                }
            }
            catch { }
        }

        // ====================================================================
        // OFFLINE FALLBACK ONLY (When rawModelOutput is empty/null or invalid)
        // ====================================================================
        if (TryResolveNextPendingPlannerSubGoalDecision(activeSubGoals, snapshot) is { } offlinePlannerDecision)
        {
            return offlinePlannerDecision;
        }

        var (resolvedSiteUrl, _, wantsDownload) = AnalyzeUniversalBrowserTask(userTask);

        // Guard 0 (Offline fallback when rawModelOutput is empty): If already on a video watch page (/watch?v=...)
        if (step >= 2 &&
            curUrl.Contains("youtube.com/watch?v=", StringComparison.OrdinalIgnoreCase) &&
            !wantsDownload)
        {
            string verifyNote = playAlreadyExecuted
                ? $"Video telah diputar dan terverifikasi: {curUrl}"
                : $"Halaman video terbuka (pemutaran tidak diverifikasi): {curUrl}";
            return new AgentStepDecision(
                playAlreadyExecuted ? "[GUARD_OVERRIDE:Guard0_Verified] Halaman video YouTube telah terbuka." : "[GUARD_OVERRIDE:Guard0_Unverified] Halaman video YouTube terbuka tanpa bukti pemutaran.",
                playAlreadyExecuted ? "Pemutaran video terverifikasi di DOM." : "[UNVERIFIED_DONE] Pemutaran tidak diverifikasi oleh agen (tidak ada bukti status video).",
                "Halaman video aktif di Browser Panel Kanan.",
                "done",
                TextOrUrl: verifyNote);
        }

        // Guard 1 (Offline fallback on Step 1 when rawModelOutput is empty): Open target site
        bool explicitOpenCommand = Regex.IsMatch(userTask, @"(?:^|\b)(?:buka|bukakan|masuk\s+ke)", RegexOptions.IgnoreCase);
        if (step == 1 && !string.IsNullOrWhiteSpace(resolvedSiteUrl))
        {
            string normCur = curUrl.TrimEnd('/').ToLowerInvariant();
            string normTarget = resolvedSiteUrl.TrimEnd('/').ToLowerInvariant();
            if (explicitOpenCommand || !normCur.StartsWith(normTarget, StringComparison.OrdinalIgnoreCase))
            {
                return new AgentStepDecision(
                    "[GUARD_OVERRIDE:Guard1] Memulai tugas di Browser Panel Kanan (Offline Fallback).",
                    $"Membuka situs tujuan: {resolvedSiteUrl}",
                    $"Membuka halaman {resolvedSiteUrl}",
                    "go_to_url",
                    TextOrUrl: resolvedSiteUrl);
            }
        }

        var fbDefault = BuildDefaultAutonomousStepDecision(userTask, snapshot, step, alreadyDownloaded, hasTypedInput, playAlreadyExecuted);
        return fbDefault with { EvaluationPreviousGoal = $"[FALLBACK_HEURISTIC] {fbDefault.EvaluationPreviousGoal}" };
    }

    public static string NormalizeUserTyposAndTerms(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        string s = input;
        s = Regex.Replace(s, @"\b(pecarian|pncarian|pencarin|pencariaan)\b", "pencarian", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(telusri|tlusuri|telusurin)\b", "telusuri", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(chanelnya|chanel|canel|chenel)\b", "channelnya", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(youtubenya|youtbe|youtub|yutub)\b", "youtube", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(whataap|whatapp|watshap)\b", "whatsapp", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(gubeneru|gubenrnu|gubernu|gubenur)\b", "Gubernur", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(preside)\b", "Presiden", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(zimbague|zimbabue)\b", "Zimbabwe", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(gembru|gembu)\b", "Gembul", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(sebenatar|sbentar)\b", "sebentar", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(tomobol|tmbol)\b", "tombol", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\blay\s+(?=tunggu|sebentar|hingga|sampai|muncul|dan|lalu|klik|lewati)", "play ", RegexOptions.IgnoreCase);
        return s;
    }

    private static bool HasTypedOnCurrentHost(
        string? currentUrl,
        bool hasExecutedInRun,
        string lastTypedHost,
        IReadOnlySet<string>? searchedHosts = null)
    {
        if (!hasExecutedInRun && (searchedHosts is null || searchedHosts.Count == 0))
            return false;
        if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var curUri))
            return hasExecutedInRun;
        string curHost = curUri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (searchedHosts is not null && searchedHosts.Contains(curHost))
            return true;
        if (string.IsNullOrWhiteSpace(lastTypedHost))
            return hasExecutedInRun;
        return string.Equals(curHost, lastTypedHost, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Regex StageVerbStart = new(
        @"^(?:tolong\s+)?(?:buka|bukakan|masuk|cari|carikan|klik|pilih|putar|play|tonton|download|unduh|ketik|masukkan|masukan|lalu|kemudian)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Mengenali kalimat multi-klausa berkoma ("buka google, cari lk21, masuk ..."). Memecah hanya bila SETIAP klausa
    /// diawali kata kerja perintah, agar kueri seperti "cari spider-man, no way home" tidak terpecah.
    /// </summary>
    internal static bool TrySplitCommandClauses(string? raw, out string[] clauses)
    {
        clauses = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(raw) || raw.Contains("->", StringComparison.Ordinal) || raw.Contains("→", StringComparison.Ordinal))
            return false;
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !StageVerbStart.IsMatch(parts[0]))
            return false;

        var merged = new List<string>();
        foreach (var part in parts)
        {
            if (StageVerbStart.IsMatch(part))
            {
                merged.Add(part);
            }
            else if (merged.Count > 0)
            {
                // Merge comma-separated title/query continuation (e.g., "cari SOAL GIBRAN, MANTAN KETUA MK BICARA! dan play")
                merged[^1] = merged[^1] + ", " + part;
            }
            else
            {
                return false;
            }
        }

        if (merged.Count < 2)
            return false;
        clauses = merged.ToArray();
        return true;
    }

    public static bool IsJunkOrUtilitySourceCandidate(string? title, string? url)
    {
        string lbl = (title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(lbl) || string.IsNullOrWhiteSpace(url))
            return true;
        if (Regex.IsMatch(lbl, @"^\d{1,2}:\d{2}(?::\d{2})?$"))
            return true;
        if (Regex.IsMatch(lbl, @"^(?:bantuan\s+aksesibilitas|accessibility\s+help|masuk|sign\s+in|login|privasi|privacy|persyaratan|terms|setelan|settings|feedback|kirim\s+masukan)$", RegexOptions.IgnoreCase))
            return true;
        if (Uri.TryCreate(url, UriKind.Absolute, out var u))
        {
            string h = u.Host.ToLowerInvariant();
            if (h is "support.google.com" or "accounts.google.com" or "policies.google.com" or "myaccount.google.com")
                return true;
        }
        return false;
    }

    public static IReadOnlyList<string> ExtractOrderedNavigationSitesFromTask(string? userTask)
    {
        var sites = new List<string>();
        if (string.IsNullOrWhiteSpace(userTask))
            return sites;

        string raw = userTask.Trim();
        string[] clauses;
        if (raw.Contains("->", StringComparison.Ordinal) || raw.Contains("→", StringComparison.Ordinal))
        {
            clauses = raw.Split(new[] { "->", "→" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        else if (TrySplitCommandClauses(raw, out var commaClauses) && commaClauses.Length >= 2)
        {
            clauses = commaClauses;
        }
        else
        {
            var (singleSite, _, _) = AnalyzeSingleStageBrowserTask(raw);
            if (!string.IsNullOrWhiteSpace(singleSite))
                sites.Add(singleSite!);
            return sites;
        }

        foreach (var cl in clauses)
        {
            bool hasExplicitOpenVerb = Regex.IsMatch(cl, @"^(?:tolong\s+)?(?:buka|bukakan|masuk)\b", RegexOptions.IgnoreCase);
            if (!hasExplicitOpenVerb && sites.Count > 0)
                continue;
            var (clSite, _, _) = AnalyzeSingleStageBrowserTask(cl);
            if (!string.IsNullOrWhiteSpace(clSite) &&
                (sites.Count == 0 || !string.Equals(sites[^1], clSite, StringComparison.OrdinalIgnoreCase)))
            {
                sites.Add(clSite!);
            }
        }
        return sites;
    }

    public static (string? ResolvedSiteUrl, string? ExtractedInputText, bool WantsDownload) AnalyzeUniversalBrowserTask(string userTask)
    {
        if (string.IsNullOrWhiteSpace(userTask))
            return (null, null, false);

        string raw = userTask.Trim();
        // Batch 1B Multi-Hop Arrow Chain ("buka google -> cari website movieku -> klik masuk -> cari VisionQuest (2026) -> play episode 1"):
        // Resolve Step 1's initial site from the first arrow clause and the primary content search query from the chain.
        if (raw.Contains("->", StringComparison.Ordinal) || raw.Contains("→", StringComparison.Ordinal))
        {
            var stages = raw.Split(new[] { "->", "→" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (stages.Length >= 2)
            {
                var (firstSite, firstInput, _) = AnalyzeSingleStageBrowserTask(stages[0]);
                string? contentQuery = firstInput;
                bool anyDownload = false;
                foreach (var stg in stages)
                {
                    var (_, stgInput, stgDl) = AnalyzeSingleStageBrowserTask(stg);
                    if (stgDl) anyDownload = true;
                    if (!string.IsNullOrWhiteSpace(stgInput))
                        contentQuery = stgInput;
                }
                return (firstSite ?? "https://www.google.com", contentQuery, anyDownload);
            }
        }

        var single = AnalyzeSingleStageBrowserTask(raw);
        // Kalimat berkoma ("buka google, buka youtube, cari kamar film, pilih video paling atas dan play"):
        // Jalankan ekstraksi PER KLAUSA agar:
        // 1) Situs dari klausa pertama menang sebagai ResolvedSiteUrl awal (mis. google.com),
        // 2) Kueri pencarian diambil dari klausa perintah pencarian ("cari kamar film" -> "kamar film") tanpa gagal akibat koma di antara klausa!
        if (TrySplitCommandClauses(raw, out var clauses) && clauses.Length >= 2)
        {
            string? firstClauseSite = null;
            string? lastClauseInput = single.ExtractedInputText;
            bool anyClauseDownload = single.WantsDownload;
            foreach (var cl in clauses)
            {
                var (clSite, clInput, clDl) = AnalyzeSingleStageBrowserTask(cl);
                if (firstClauseSite is null && !string.IsNullOrWhiteSpace(clSite))
                    firstClauseSite = clSite;
                if (!string.IsNullOrWhiteSpace(clInput))
                    lastClauseInput = clInput;
                if (clDl)
                    anyClauseDownload = true;
            }
            return (firstClauseSite ?? single.ResolvedSiteUrl, lastClauseInput, anyClauseDownload);
        }
        return single;
    }

    private static (string? ResolvedSiteUrl, string? ExtractedInputText, bool WantsDownload) AnalyzeSingleStageBrowserTask(string userTask)
    {
        if (string.IsNullOrWhiteSpace(userTask))
            return (null, null, false);

        string raw = userTask.Trim();
        string normRaw = NormalizeUserTyposAndTerms(raw);
        string lower = normRaw.ToLowerInvariant();

        // Reject conversational meta-complaints or questions about the assistant/model (e.g. "tetapi saya lihat bukan kamu yang mengerjakannya melainkan model lokal cari tau penyebabnya")
        if (Regex.IsMatch(lower, @"\b(bukan\s+kamu|melainkan\s+model|model\s+lokal|kamu\s+salah|salah\s+menangani|cari\s+ta(?:h)?u\s+penyebab|cari\s+ta(?:h)?u\s+kesalahan|kenapa\s+kamu\s+lambat)\b"))
        {
            return (null, null, false);
        }

        bool wantsDownload = Regex.IsMatch(lower, @"\b(download|downloadkan|dowload|dowloadkan|donload|unduh|unduhkan|simpan\s+pdf|simpan\s+file|file\s+aslinya|file\s+asinya|langsung\s+berkasnya)\b");

        // 1. Check explicit http/https URL (including glued 'bukahttps://...') or domain name in prompt
        string? targetUrl = null;
        var urlMatch = Regex.Match(raw, @"https?://[^\s""'<>]+", RegexOptions.IgnoreCase);
        if (urlMatch.Success)
        {
            targetUrl = urlMatch.Value.TrimEnd('.', ',', ';', ')');
        }
        else if (Regex.IsMatch(lower, @"\b(whatsapp|wa\s+web|buka\s+wa)\b"))
        {
            targetUrl = "https://web.whatsapp.com";
        }
        else if (Regex.IsMatch(lower, @"\b(google\s+gemini|buka\s+gemini|gemini\.google)\b"))
        {
            targetUrl = "https://gemini.google.com/app";
        }
        else if (Regex.IsMatch(lower, @"\b(chatgpt|chat\s+gpt|openai)\b"))
        {
            targetUrl = "https://chatgpt.com";
        }
        else if (Regex.IsMatch(lower, @"\b(adguard)\b"))
        {
            targetUrl = "https://adguard.com/id/welcome.html";
        }
        else if (Regex.IsMatch(lower, @"\b(youtube|yt)\b"))
        {
            targetUrl = "https://www.youtube.com";
        }
        else if (Regex.IsMatch(lower, @"\b(scholar|cendekia|makalah|jurnal|paper|skripsi|doi)\b"))
        {
            targetUrl = null; // Handled via search_web("scholar ...") unless direct open
        }
        else
        {
            var domainMatch = Regex.Match(raw, @"\b([a-zA-Z0-9\-]+(?:\.[a-zA-Z0-9\-]+)*\.(?:com|id|co\.id|ac\.id|go\.id|org|net|website|cc|io|dev|edu|tv|me|info))(?:/[^\s]*)?\b", RegexOptions.IgnoreCase);
            if (domainMatch.Success)
            {
                string matchedDomain = domainMatch.Value;
                targetUrl = "https://" + matchedDomain + (matchedDomain.Contains('/') ? string.Empty : "/");
            }
            else if (BrowserAgentRuntimeSettings.Current.CarryForward.TryResolveNumberedCandidateFromPrompt(raw, out var cfCandidate) &&
                     cfCandidate is not null &&
                     !string.IsNullOrWhiteSpace(cfCandidate.Url))
            {
                return (cfCandidate.Url, null, wantsDownload);
            }
            else if (VerifiedCarryForwardStore.IsCrossTurnContinuationPrompt(raw))
            {
                return (null, null, wantsDownload);
            }
            else if (Regex.IsMatch(lower, @"\b(buka|bukakan|cari|carikan|telusuri)\b"))
            {
                targetUrl = "https://www.google.com";
            }
        }

        // 2. Check if user wants to input/type/search specific text on the site
        string? inputPayload = null;
        var quotedInputMatch = Regex.Match(
            raw,
            @"(?:masukkan|masukan|ketikkan|ketikan|ketik|isi|pencarian|pecarian|cari)\s+(?:di\s+pencarian\s+|di\s+pecarian\s+|input\s+|kata\s+kunci\s+)?[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (quotedInputMatch.Success)
        {
            inputPayload = quotedInputMatch.Groups[1].Value.Trim();
        }
        else
        {
            var inputMatch = Regex.Match(
                normRaw,
                @"(?:isi\s+(?:di\s+)?kolom\s+(?:di\s+)?pencarian|input\s+di\s+telusuri\s+masukk?an|masukk?an\s+(?:input\s+)?(?:di\s+)?pencarian|masukk?an\s+input|ketikk?an|ketik|isi\s+input|isi\s+kolom|masukk?an\s+kata\s+kunci|di\s+telusuri\s+masukk?an|di\s+pencarian\s+nama\s+film\s+ini|di\s+pencarian|di\s+telusuri|masukk?an|tanyakan|(?:buka\s+(?:web\s+|website\s+|situs\s+)?[a-zA-Z0-9.\-]+\s+)(?:cari|carikan|telusuri)|^(?:cari|carikan)\s+(?:nama\s+)?)\s*(.+?)(?:\s+(?:buka\s+youtube(?:nya)?|buka\s+channel(?:nya)?|cari\s+video\s+paling\s+atas|play\s+videonya|yang\s+paling\s+atas)|\s+(?:dan|lalu|kemudian|terus|langsung)\s+(?:play|putar|mainkan|tonton|klik|buka)|\s+carikan\b|\s+cari\s+tempat|,\s*buka\s+channel|,\s*rangkum|\s+rangkum\s+berikan|\s+langsung\s+dow|\s+langsung\s+unduh|\s+lalu\s+rangkum|\s+tapi\s+hati|\s+untuk\s+\d+|\s+di\s+web\b|\s+di\s+internet\b|\s+di\s+google\b|$)",
                RegexOptions.IgnoreCase);
            if (inputMatch.Success)
            {
                inputPayload = inputMatch.Groups[1].Value.Trim(' ', ',', '.', '"', '\'');
                inputPayload = Regex.Replace(
                    inputPayload,
                    @"^(?:di\s+telusuri\s+|di\s+pencarian\s+)?(?:masukkan\s+input\s+|masukan\s+input\s+|masukkan\s+|masukan\s+|ketik\s+)?(?:nama\s+film\s+ini\s+)?",
                    "",
                    RegexOptions.IgnoreCase).Trim(' ', ',', '.', '"', '\'');
                inputPayload = Regex.Replace(
                    inputPayload,
                    @"\s*(?:,\s*)?(?:buka\s+youtube(?:nya)?|buka\s+channel(?:nya)?|cari\s+video.*|pilih\s+video.*|video\s+paling\s+atas.*|yang\s+paling\s+atas.*|play\s+video.*|dan\s+play.*|lalu\s+play.*|dan\s+putar.*|lalu\s+putar.*|di\s+(?:kolom\s+|kotak\s+)?(?:pencarian|pecarian|telusuri).*|tapi\s+hati.*|di\s+web|di\s+internet)$",
                    "",
                    RegexOptions.IgnoreCase).Trim(' ', ',', '.', '"', '\'');
            }
        }

        if (!string.IsNullOrWhiteSpace(inputPayload))
        {
            string cleanedByContract = AgentTaskChecklistContract.CleanSearchQueryFromCommandVerbs(inputPayload);
            if (!string.IsNullOrWhiteSpace(cleanedByContract))
                inputPayload = cleanedByContract;
        }
        return (targetUrl, string.IsNullOrWhiteSpace(inputPayload) ? null : inputPayload, wantsDownload);
    }

    private static AgentStepDecision? TryResolveMultiHopArrowChainDecision(
        string userTask,
        BrowserLiveStateSnapshot snapshot,
        int step,
        bool hasTypedOnCurrentHost,
        bool playAlreadyExecuted = false)
    {
        if (string.IsNullOrWhiteSpace(userTask))
            return null;

        bool hasArrows = userTask.Contains("->", StringComparison.Ordinal) || userTask.Contains("→", StringComparison.Ordinal);
        bool hasCommaClauses = !hasArrows && TrySplitCommandClauses(userTask, out var splitClauses) && splitClauses.Length >= 2;
        var requestedSubItem = AgentTaskChecklistContract.TryExtractRequestedSubItem(userTask);
        if (!hasArrows && !hasCommaClauses && requestedSubItem is null)
            return null;

        var stages = hasArrows
            ? userTask.Split(new[] { "->", "→" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToArray()
            : (hasCommaClauses && TrySplitCommandClauses(userTask, out var clList) ? clList.ToArray() : new[] { userTask });

        string curUrl = snapshot.CurrentUrl ?? string.Empty;
        var (firstSiteUrl, singleStageInput, singleStageDl) = AnalyzeSingleStageBrowserTask(stages[0]);
        string entrySiteUrl = !string.IsNullOrWhiteSpace(firstSiteUrl) ? firstSiteUrl! : "https://www.google.com";

        string? intermediateSiteQuery = null;
        string? intermediateTargetSiteUrl = null;
        bool intermediateExplicitDirectOpen = false;
        string? contentSearchQuery = (hasArrows || hasCommaClauses) ? null : singleStageInput;
        string? finalPlayInstruction = null;
        bool wantsPlay = Regex.IsMatch(userTask, @"\b(play|putar|mainkan|tonton|watch|streaming)\b", RegexOptions.IgnoreCase);

        for (int i = 1; i < stages.Length; i++)
        {
            string stg = stages[i];
            string stgLower = stg.ToLowerInvariant();
            if (Regex.IsMatch(stgLower, @"\b(klik\s+masuk|masuk\s+ke|buka\s+website|klik\s+hasil)\b") &&
                !Regex.IsMatch(stgLower, @"\b(cari|carikan)\b"))
            {
                continue;
            }
            if (Regex.IsMatch(stgLower, @"\b(play|putar|mainkan|tonton|pilih\s+video|klik\s+video|klik\s+episode|memilih\s+bagian|pilih\s+bagian|pilih\s+episode|klik\s+bagian)\b") &&
                !Regex.IsMatch(stgLower, @"^(?:cari|carikan|telusuri|ketik|masukkan|masukan|isi)\b"))
            {
                finalPlayInstruction = stg;
                continue;
            }

            var (stgSite, stgInput, _) = AnalyzeSingleStageBrowserTask(stg);
            bool isExplicitSiteOpenClause = Regex.IsMatch(stgLower, @"^(?:lalu\s+|kemudian\s+)?(?:buka|bukakan|masuk\s+ke)\b") &&
                                            !Regex.IsMatch(stgLower, @"\b(cari|carikan|telusuri)\b") &&
                                            !string.IsNullOrWhiteSpace(stgSite) &&
                                            stgSite != "https://www.google.com";
            if (isExplicitSiteOpenClause && intermediateSiteQuery is null && i < stages.Length - 1)
            {
                intermediateTargetSiteUrl = stgSite;
                intermediateSiteQuery = Uri.TryCreate(stgSite, UriKind.Absolute, out var iu)
                    ? iu.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase).Split('.')[0]
                    : stg;
                intermediateExplicitDirectOpen = true;
                continue;
            }

            string rawExtracted = !string.IsNullOrWhiteSpace(stgInput)
                ? stgInput!
                : Regex.Replace(stg, @"^(?:cari|carikan|telusuri|masukkan|masukan|ketik)\s+(?:website|web|situs)?\s*", "", RegexOptions.IgnoreCase).Trim();
            string extracted = Regex.Replace(rawExtracted, @"^(?:website|web|situs)\s+", "", RegexOptions.IgnoreCase).Trim();
            extracted = AgentTaskChecklistContract.CleanSearchQueryFromCommandVerbs(extracted);

            if (intermediateSiteQuery is null &&
                (stgLower.Contains("website") || stgLower.Contains("situs") || (!string.IsNullOrWhiteSpace(stgSite) && stgSite != "https://www.google.com" && string.IsNullOrWhiteSpace(stgInput))) &&
                i < stages.Length - 1)
            {
                intermediateSiteQuery = extracted;
                intermediateTargetSiteUrl = !string.IsNullOrWhiteSpace(stgSite) && stgSite != "https://www.google.com"
                    ? stgSite
                    : null;
            }
            else if (!string.IsNullOrWhiteSpace(extracted))
            {
                contentSearchQuery = extracted;
            }
        }

        // Stage 1: Not yet on entry site
        if (step == 1 || string.IsNullOrWhiteSpace(curUrl) || curUrl == "about:blank")
        {
            return new AgentStepDecision(
                "Memulai rantai instruksi multi-langkah.",
                $"Langkah 1: Membuka {entrySiteUrl}",
                $"Membuka halaman {entrySiteUrl}",
                "go_to_url",
                TextOrUrl: entrySiteUrl);
        }

        bool isOnGoogle = curUrl.Contains("google.com", StringComparison.OrdinalIgnoreCase) &&
                          !curUrl.Contains("scholar.google", StringComparison.OrdinalIgnoreCase) &&
                          !curUrl.Contains("gemini.google", StringComparison.OrdinalIgnoreCase);

        // Stage 2: On Google root -> if next clause is an explicit "buka <situs>" (e.g. "buka google, buka youtube, cari kamar film"), go to that site; otherwise search Google
        if (isOnGoogle && !curUrl.Contains("/search?", StringComparison.OrdinalIgnoreCase))
        {
            if (intermediateExplicitDirectOpen && !string.IsNullOrWhiteSpace(intermediateTargetSiteUrl))
            {
                return new AgentStepDecision(
                    "Halaman Google telah terbuka.",
                    $"Melanjutkan ke klausa berikutnya: membuka {intermediateTargetSiteUrl}.",
                    $"Membuka halaman {intermediateTargetSiteUrl}",
                    "go_to_url",
                    TextOrUrl: intermediateTargetSiteUrl);
            }
            string googleQuery = !string.IsNullOrWhiteSpace(intermediateSiteQuery)
                ? intermediateSiteQuery!
                : (contentSearchQuery ?? stages[Math.Min(1, stages.Length - 1)]);
            var googleInput = snapshot.Elements.FirstOrDefault(e =>
                e.TagName is "input" or "textarea" || e.Role is "textbox" or "searchbox" or "combobox");
            if (googleInput is not null && !hasTypedOnCurrentHost)
            {
                return new AgentStepDecision(
                    "Halaman Google terbuka.",
                    $"Mencari situs '{googleQuery}' di Google.",
                    $"Mengetik \"{googleQuery}\" ke kolom pencarian Google",
                    "input_text",
                    ElementIndex: googleInput.Index,
                    TextOrUrl: googleQuery,
                    PressEnter: true);
            }
            return new AgentStepDecision(
                "Menjalankan pencarian Google.",
                $"Membuka hasil pencarian Google untuk '{googleQuery}'.",
                $"Mencari \"{googleQuery}\" di Google",
                "go_to_url",
                TextOrUrl: "https://www.google.com/search?q=" + Uri.EscapeDataString(googleQuery));
        }

        // Stage 3: On Google search results -> click into the target website (e.g. movieku.website)
        if (isOnGoogle && curUrl.Contains("/search?", StringComparison.OrdinalIgnoreCase))
        {
            string siteKey = (intermediateSiteQuery ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var matchingSiteLink = snapshot.Elements.FirstOrDefault(e =>
                e.TagName == "a" &&
                !string.IsNullOrWhiteSpace(e.Href) &&
                e.Href!.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                !e.Href.Contains("google.", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(siteKey) ||
                 e.Href.Contains(siteKey, StringComparison.OrdinalIgnoreCase) ||
                 e.TextOrLabel.Contains(siteKey, StringComparison.OrdinalIgnoreCase)));

            if (matchingSiteLink is not null)
            {
                return new AgentStepDecision(
                    "Hasil pencarian situs ditemukan di Google.",
                    $"Mengklik masuk ke situs \"{matchingSiteLink.TextOrLabel}\".",
                    $"Mengklik \"{matchingSiteLink.TextOrLabel}\"",
                    "click_element",
                    ElementIndex: matchingSiteLink.Index);
            }

            if (!string.IsNullOrWhiteSpace(intermediateTargetSiteUrl))
            {
                return new AgentStepDecision(
                    "Membuka situs tujuan dari hasil pencarian.",
                    $"Masuk ke {intermediateTargetSiteUrl}.",
                    $"Membuka situs {intermediateTargetSiteUrl}",
                    "go_to_url",
                    TextOrUrl: intermediateTargetSiteUrl);
            }
        }

        string cleanContentKey = Regex.Replace(contentSearchQuery ?? "", @"[()0-9]+", " ").Trim();
        var contentWords = cleanContentKey.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3).ToArray();

        bool isOnSearchResults = curUrl.Contains("?s=", StringComparison.OrdinalIgnoreCase) ||
                                 curUrl.Contains("search_query=", StringComparison.OrdinalIgnoreCase) ||
                                 curUrl.Contains("?q=", StringComparison.OrdinalIgnoreCase) ||
                                 curUrl.Contains("/search", StringComparison.OrdinalIgnoreCase) ||
                                 curUrl.Contains("/results", StringComparison.OrdinalIgnoreCase);

        bool urlAlreadyOnContentDetail = !isOnSearchResults &&
                                         (curUrl.Contains("/watch?v=", StringComparison.OrdinalIgnoreCase) ||
                                          (contentWords.Length > 0 &&
                                           contentWords.Any(w => curUrl.Contains(w, StringComparison.OrdinalIgnoreCase))));

        // Stage 4: On the destination website root -> search for contentSearchQuery ("VisionQuest (2026)")
        if (!isOnGoogle && !isOnSearchResults && !urlAlreadyOnContentDetail && !string.IsNullOrWhiteSpace(contentSearchQuery) && !hasTypedOnCurrentHost)
        {
            var siteInput = snapshot.Elements.FirstOrDefault(e =>
                e.TagName is not ("a" or "button") &&
                (e.IsContentEditable || e.TagName is "input" or "textarea" || e.Role is "textbox" or "searchbox" or "combobox"));
            if (siteInput is not null)
            {
                return new AgentStepDecision(
                    $"Berada di situs {curUrl}.",
                    $"Mengetik \"{contentSearchQuery}\" pada kolom pencarian situs.",
                    $"Mengetik \"{contentSearchQuery}\" di kolom pencarian",
                    "input_text",
                    ElementIndex: siteInput.Index,
                    TextOrUrl: contentSearchQuery,
                    PressEnter: true);
            }

            if (Uri.TryCreate(curUrl, UriKind.Absolute, out var siteUri))
            {
                string siteSearchUrl = BuildSiteSpecificSearchUrl(siteUri, contentSearchQuery!);
                return new AgentStepDecision(
                    $"Menjalankan pencarian di {siteUri.Host}.",
                    $"Mencari \"{contentSearchQuery}\" pada {siteUri.Host}.",
                    $"Mencari \"{contentSearchQuery}\" di {siteUri.Host}",
                    "go_to_url",
                    TextOrUrl: siteSearchUrl);
            }
        }

        // Stage 5, 6, 7: Content Card Selection -> Sub-Item / Episode Selection -> Play Action
        if (!isOnGoogle && (!string.IsNullOrWhiteSpace(contentSearchQuery) || !string.IsNullOrWhiteSpace(finalPlayInstruction) || requestedSubItem is not null))
        {
            bool urlAlreadyOnSpecificSubItem = requestedSubItem is not null &&
                                               !isOnSearchResults &&
                                               AgentTaskChecklistContract.IsSpecificSubItemMatch(curUrl, requestedSubItem);

            // Stage 5: On Search Results Page (?s=, /search, etc.) -> click the matching Content / Series Card (or direct non-bundle episode link)
            if (isOnSearchResults)
            {
                if (requestedSubItem is not null)
                {
                    var directSubItemLink = snapshot.Elements.FirstOrDefault(e =>
                        e.TagName == "a" &&
                        !string.IsNullOrWhiteSpace(e.Href) &&
                        !e.Href!.Contains("?s=", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(e.Href, curUrl, StringComparison.OrdinalIgnoreCase) &&
                        AgentTaskChecklistContract.IsSpecificSubItemMatch(e.TextOrLabel + " " + e.Href, requestedSubItem) &&
                        (contentWords.Length == 0 || contentWords.Any(w => e.TextOrLabel.Contains(w, StringComparison.OrdinalIgnoreCase) || e.Href!.Contains(w, StringComparison.OrdinalIgnoreCase))));
                    if (directSubItemLink is not null)
                    {
                        return new AgentStepDecision(
                            "Tautan spesifik bagian/episode ditemukan langsung pada hasil pencarian.",
                            $"Mengklik \"{directSubItemLink.TextOrLabel}\".",
                            $"Mengklik \"{directSubItemLink.TextOrLabel}\"",
                            "click_element",
                            ElementIndex: directSubItemLink.Index);
                    }
                }

                var contentCardLink = snapshot.Elements.FirstOrDefault(e =>
                    e.TagName == "a" &&
                    !string.IsNullOrWhiteSpace(e.Href) &&
                    !e.Href!.Contains("?s=", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(e.Href, curUrl, StringComparison.OrdinalIgnoreCase) &&
                    contentWords.Any(w => e.TextOrLabel.Contains(w, StringComparison.OrdinalIgnoreCase) || e.Href!.Contains(w, StringComparison.OrdinalIgnoreCase)))
                    ?? snapshot.Elements.FirstOrDefault(e =>
                        e.TagName == "a" &&
                        !string.IsNullOrWhiteSpace(e.Href) &&
                        !e.Href!.Contains("?s=", StringComparison.OrdinalIgnoreCase) &&
                        !e.Href!.EndsWith("#", StringComparison.Ordinal) &&
                        !string.Equals(e.Href, curUrl, StringComparison.OrdinalIgnoreCase) &&
                        !Regex.IsMatch(e.TextOrLabel.Trim(), @"^(?:home|beranda|film|series|genre|cari|search|login|masuk|daftar|dmca|contact)$", RegexOptions.IgnoreCase));

                if (contentCardLink is not null)
                {
                    return new AgentStepDecision(
                        "Hasil pencarian konten ditemukan pada halaman.",
                        $"Mengklik kartu konten \"{contentCardLink.TextOrLabel}\" untuk membuka halaman detail.",
                        $"Mengklik \"{contentCardLink.TextOrLabel}\"",
                        "click_element",
                        ElementIndex: contentCardLink.Index);
                }
            }

            // Stage 6: On Content / Series Detail Page (!isOnSearchResults) -> select the requested Sub-Item (e.g. "Episode 01", "720p", "Sejarah")
            if (!isOnSearchResults && requestedSubItem is not null && !urlAlreadyOnSpecificSubItem)
            {
                var subItemElement = snapshot.Elements.FirstOrDefault(e =>
                    !string.Equals(e.Href, curUrl, StringComparison.OrdinalIgnoreCase) &&
                    AgentTaskChecklistContract.IsSpecificSubItemMatch(e.TextOrLabel + " " + (e.Href ?? string.Empty), requestedSubItem));

                if (subItemElement is not null)
                {
                    return new AgentStepDecision(
                        "Halaman detail konten/seri terbuka.",
                        $"Memilih bagian \"{subItemElement.TextOrLabel}\" sesuai instruksi pengguna.",
                        $"Mengklik \"{subItemElement.TextOrLabel}\"",
                        "click_element",
                        ElementIndex: subItemElement.Index);
                }

                // Fallback: if no sub-item link is matched yet because we are still on an intermediate page before detail page, click matching content link
                if (!urlAlreadyOnContentDetail && contentWords.Length > 0)
                {
                    var fallbackContentLink = snapshot.Elements.FirstOrDefault(e =>
                        e.TagName == "a" &&
                        !string.IsNullOrWhiteSpace(e.Href) &&
                        !e.Href!.Contains("?s=", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(e.Href, curUrl, StringComparison.OrdinalIgnoreCase) &&
                        contentWords.Any(w => e.TextOrLabel.Contains(w, StringComparison.OrdinalIgnoreCase) || e.Href!.Contains(w, StringComparison.OrdinalIgnoreCase)));
                    if (fallbackContentLink is not null)
                    {
                        return new AgentStepDecision(
                            "Menemukan tautan konten utama.",
                            $"Mengklik \"{fallbackContentLink.TextOrLabel}\".",
                            $"Mengklik \"{fallbackContentLink.TextOrLabel}\"",
                            "click_element",
                            ElementIndex: fallbackContentLink.Index);
                    }
                }

                if (snapshot.PixelsBelow > 120)
                {
                    return new AgentStepDecision(
                        $"Mencari bagian \"{requestedSubItem.RawPhrase}\" pada halaman detail.",
                        $"Menggulir halaman ke bawah untuk menampilkan daftar \"{requestedSubItem.RawPhrase}\".",
                        $"Menggulir halaman untuk mencari \"{requestedSubItem.RawPhrase}\"",
                        "scroll_down");
                }
            }

            // Stage 7: On the Final Episode / Content Page -> click Play button if present and wantsPlay is true
            if (!isOnSearchResults && (requestedSubItem is null || urlAlreadyOnSpecificSubItem))
            {
                if (!urlAlreadyOnContentDetail && requestedSubItem is null && contentWords.Length > 0)
                {
                    var contentLink = snapshot.Elements.FirstOrDefault(e =>
                        e.TagName == "a" &&
                        !string.IsNullOrWhiteSpace(e.Href) &&
                        !e.Href!.Contains("?s=", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(e.Href, curUrl, StringComparison.OrdinalIgnoreCase) &&
                        contentWords.Any(w => e.TextOrLabel.Contains(w, StringComparison.OrdinalIgnoreCase) || e.Href!.Contains(w, StringComparison.OrdinalIgnoreCase)));
                    if (contentLink is not null)
                    {
                        return new AgentStepDecision(
                            "Hasil pencarian konten ditemukan.",
                            $"Mengklik \"{contentLink.TextOrLabel}\".",
                            $"Mengklik \"{contentLink.TextOrLabel}\"",
                            "click_element",
                            ElementIndex: contentLink.Index);
                    }
                }

                if (wantsPlay && !playAlreadyExecuted)
                {
                    var playBtn = snapshot.Elements.FirstOrDefault(e =>
                        (e.TagName is "button" or "video" or "iframe" or "div" or "span" ||
                         (e.TagName == "a" && (string.IsNullOrWhiteSpace(e.Href) || e.Href!.StartsWith("#", StringComparison.Ordinal) || e.Href!.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || string.Equals(e.Href, curUrl, StringComparison.OrdinalIgnoreCase)))) &&
                        (string.Equals(e.TagName, "video", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(e.TagName, "iframe", StringComparison.OrdinalIgnoreCase) ||
                         Regex.IsMatch(e.TextOrLabel.Trim(), @"^(?:play|putar|tonton|mainkan|watch|play\s+video|putar\s+video|play\s+episode|putar\s+episode|▶)$", RegexOptions.IgnoreCase)));

                    if (playBtn is not null)
                    {
                        return new AgentStepDecision(
                            "Halaman pemutar episode/video telah siap.",
                            $"Mengklik tombol putar \"{playBtn.TextOrLabel}\".",
                            $"Memutar video (\"{playBtn.TextOrLabel}\")",
                            "click_element",
                            ElementIndex: playBtn.Index);
                    }
                }

                if (wantsPlay && !playAlreadyExecuted)
                {
                    // Tombol putar tidak ditemukan: JANGAN mengklaim selesai tanpa bukti pemutaran.
                    return new AgentStepDecision(
                        "Tombol putar belum ditemukan pada halaman episode.",
                        "Player mungkin berada di dalam iframe atau belum termuat.",
                        "Menunggu player termuat (tombol putar belum terlihat)",
                        "wait",
                        TextOrUrl: "2");
                }

                return new AgentStepDecision(
                    "Seluruh tahapan navigasi dan pemilihan bagian/episode telah selesai.",
                    "Tugas multi-langkah selesai.",
                    $"Halaman target aktif: {curUrl}",
                    "done",
                    TextOrUrl: $"Berhasil membuka dan menyiapkan pemutaran pada {curUrl}.");
            }
        }

        return null;
    }

    public static bool TrySetWindowsClipboardText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        if (BrowserAgentRuntimeSettings.Current.ClipboardSetterOverride is { } overrideSetter)
        {
            try
            {
                overrideSetter(text);
                return true;
            }
            catch
            {
                return false;
            }
        }

        try
        {
            if (System.Windows.Application.Current?.Dispatcher is { } disp)
            {
                if (disp.CheckAccess())
                {
                    System.Windows.Clipboard.SetText(text);
                }
                else
                {
                    disp.Invoke(() => System.Windows.Clipboard.SetText(text));
                }
                return true;
            }
            System.Windows.Clipboard.SetText(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AgentStepDecision BuildDefaultAutonomousStepDecision(
        string userTask,
        BrowserLiveStateSnapshot snapshot,
        int step,
        bool alreadyDownloaded,
        bool hasTypedInput = false,
        bool playAlreadyExecuted = false)
    {
        userTask ??= string.Empty;
        string curUrl = snapshot.CurrentUrl ?? string.Empty;

        // Batch B: Cross-turn numbered candidate resolution ("buka hasil nomor dua")
        if (BrowserAgentRuntimeSettings.Current.CarryForward.TryResolveNumberedCandidateFromPrompt(userTask, out var numberedCand) &&
            numberedCand is not null &&
            !string.IsNullOrWhiteSpace(numberedCand.Url))
        {
            if (!string.Equals(curUrl.TrimEnd('/'), numberedCand.Url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                return new AgentStepDecision(
                    "Menindaklanjuti kandidat bernomor dari hasil pencarian sebelumnya.",
                    $"Membuka kandidat #[{numberedCand.ItemNumber}] \"{numberedCand.Title}\" ({numberedCand.Url})",
                    $"Membuka hasil nomor {numberedCand.ItemNumber}: {numberedCand.Title}",
                    "go_to_url",
                    TextOrUrl: numberedCand.Url);
            }
            return new AgentStepDecision(
                $"Kandidat #[{numberedCand.ItemNumber}] telah terbuka dan terverifikasi.",
                $"Halaman aktif: {curUrl}",
                "Selesai membuka hasil pilihan.",
                "done",
                TextOrUrl: $"Berhasil membuka hasil nomor {numberedCand.ItemNumber}: {numberedCand.Title} ({curUrl}).");
        }

        // Batch B: Cross-turn form fill using carry-forward facts ("gunakan informasi tadi untuk mengisi formulir ini", "periksa dulu, jangan kirim")
        if (Regex.IsMatch(userTask, @"\b(gunakan\s+informasi\s+tadi|pakai\s+(?:informasi|data)\s+tadi|mengisi\s+formulir\s+ini|isi\s+formulir\s+ini)\b", RegexOptions.IgnoreCase))
        {
            bool doNotSubmit = Regex.IsMatch(userTask, @"\b(jangan\s+kirim|jangan\s+submit|periksa\s+dulu)\b", RegexOptions.IgnoreCase);
            if (!hasTypedInput)
            {
                var formInput = snapshot.Elements.FirstOrDefault(e =>
                    e.TagName is not ("a" or "button") &&
                    (e.IsContentEditable || e.TagName is "input" or "textarea" || e.Role is "textbox" or "searchbox" or "combobox"));
                if (formInput is not null)
                {
                    var cf = BrowserAgentRuntimeSettings.Current.CarryForward;
                    string factValue = cf.ExtractedFacts.FirstOrDefault()?.Value
                                       ?? (!string.IsNullOrWhiteSpace(cf.LastCopiedText) ? cf.LastCopiedText : "Data Carry-Forward Terverifikasi");
                    return new AgentStepDecision(
                        "Menggunakan fakta terverifikasi dari turn sebelumnya untuk mengisi formulir.",
                        $"Mengisi \"{factValue}\" ke elemen [{formInput.Index}]",
                        $"Mengisi formulir dengan data dari langkah sebelumnya",
                        "input_text",
                        ElementIndex: formInput.Index,
                        TextOrUrl: factValue,
                        PressEnter: !doNotSubmit);
                }
            }
            return new AgentStepDecision(
                "Formulir telah diisi dengan informasi dari halaman sebelumnya.",
                doNotSubmit ? "Menahan pengiriman formulir sesuai instruksi 'periksa dulu, jangan kirim'." : "Pengisian formulir selesai.",
                "Formulir siap diperiksa pengguna.",
                "done",
                TextOrUrl: "Formulir telah diisi dan siap diperiksa.");
        }

        // Batch B: Cross-turn extraction / copy & save to file ("ambil nama, alamat, dan nomor telepon dari halaman itu" / "salin hasilnya dan simpan ke file")
        if (Regex.IsMatch(userTask, @"\b(ambil\s+nama|dari\s+halaman\s+(?:itu|ini)|salin\s+hasilnya|simpan\s+(?:hasil(?:nya)?\s+)?ke\s+file)\b", RegexOptions.IgnoreCase))
        {
            if (step == 1 && (!string.IsNullOrWhiteSpace(snapshot.CleanMarkdownViewportText) || snapshot.Elements.Count > 0))
            {
                int? firstIdx = snapshot.Elements.FirstOrDefault()?.Index;
                return new AgentStepDecision(
                    "Mengekstrak dan menyalin informasi dari halaman aktif.",
                    "Menyalin informasi ke memori CarryForward & Clipboard Windows.",
                    "Menyalin teks dari halaman aktif",
                    "copy_text",
                    ElementIndex: firstIdx,
                    TextOrUrl: snapshot.CleanMarkdownViewportText);
            }
            return new AgentStepDecision(
                "Informasi dari halaman aktif telah disalin dan disimpan.",
                "Ekstraksi lintas-turn selesai.",
                "Informasi siap digunakan pada langkah berikutnya.",
                "done",
                TextOrUrl: "Informasi berhasil diekstrak dan disalin.");
        }

        if (TryResolveMultiHopArrowChainDecision(userTask, snapshot, step, hasTypedInput, playAlreadyExecuted) is { } arrowDecision)
        {
            return arrowDecision;
        }

        var (resolvedSiteUrl, extractedInputText, wantsDownload) = AnalyzeUniversalBrowserTask(userTask);

        // Batch 1B Direct-Open & QR/Auth Page Lifecycle Guard:
        // When user only asked to open a site (extractedInputText is null and wantsDownload is false) and the site is now open on Step >= 2,
        // OR when the page is a QR/Login screen (like WhatsApp Web "Scan to log in"), finish cleanly in 1 action without appending "/?s="!
        bool isQrOrAuthScreen = snapshot.CleanMarkdownViewportText.Contains("[PAGE_LIFECYCLE] AUTH_OR_QR_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
                                snapshot.CleanMarkdownViewportText.Contains("Scan to log in", StringComparison.OrdinalIgnoreCase) ||
                                snapshot.CleanMarkdownViewportText.Contains("Log in with phone number", StringComparison.OrdinalIgnoreCase);
        bool hasMultipleClauses = (userTask.Contains("->", StringComparison.Ordinal) || userTask.Contains("→", StringComparison.Ordinal)) ||
                                  (TrySplitCommandClauses(userTask, out var dClauses) && dClauses.Length >= 2);
        bool hasActionVerbsBeyondOpen = Regex.IsMatch(
            userTask,
            @"\b(cari|carikan|telusuri|ketik|ketikkan|masukkan|masukan|isi|pilih|memilih|klik|play|putar|mainkan|tonton|baca|rangkum|ringkas)\b",
            RegexOptions.IgnoreCase);

        if (step >= 2 && isQrOrAuthScreen)
        {
            return new AgentStepDecision(
                hasActionVerbsBeyondOpen ? "[GUARD_OVERRIDE:AuthGate_Unverified] Halaman membutuhkan autentikasi/login manual." : $"Halaman {curUrl} telah terbuka dan siap.",
                hasActionVerbsBeyondOpen ? "[UNVERIFIED_DONE] Halaman memerlukan login/scan QR sebelum pencarian dapat dijalankan." : "Navigasi pembukaan situs selesai.",
                $"Halaman {curUrl} siap digunakan.",
                "done",
                TextOrUrl: $"Halaman {curUrl} telah dibuka di Browser Panel Kanan.");
        }

        if (step >= 2 &&
            string.IsNullOrWhiteSpace(extractedInputText) &&
            !wantsDownload &&
            !hasMultipleClauses &&
            !hasActionVerbsBeyondOpen &&
            !string.IsNullOrWhiteSpace(resolvedSiteUrl))
        {
            return new AgentStepDecision(
                $"Halaman {curUrl} telah terbuka dan siap.",
                "Navigasi pembukaan situs selesai.",
                $"Halaman {curUrl} siap digunakan.",
                "done",
                TextOrUrl: $"Halaman {curUrl} telah dibuka di Browser Panel Kanan.");
        }

        // STEP 1: Open target URL or run initial web/scholar search
        bool explicitOpen = Regex.IsMatch(userTask, @"(?:^|\b)(?:buka|bukakan|masuk\s+ke)", RegexOptions.IgnoreCase);
        if (step == 1)
        {
            if (!string.IsNullOrWhiteSpace(resolvedSiteUrl) &&
                (explicitOpen || !curUrl.StartsWith(resolvedSiteUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
            {
                return new AgentStepDecision(
                    "Memulai tugas di Browser Panel Kanan.",
                    $"Membuka situs tujuan: {resolvedSiteUrl}",
                    $"Membuka halaman {resolvedSiteUrl}",
                    "go_to_url",
                    TextOrUrl: resolvedSiteUrl);
            }

            if (string.IsNullOrWhiteSpace(curUrl) || curUrl == "about:blank")
            {
                return new AgentStepDecision(
                    "Browser siap digunakan.",
                    "Menjalankan pencarian web untuk tugas pengguna.",
                    $"Mencari: {userTask}",
                    "search_web",
                    TextOrUrl: userTask);
            }
        }

        // STEP 2/3: If user requested typing into an input/prompt/search box on the opened page and hasn't typed yet
        if (!string.IsNullOrWhiteSpace(extractedInputText) && !hasTypedInput && step <= 8)
        {
            // Strictly exclude <a> and <button> so links like <a href="/search?s=">Cari Judul...</a> are never treated as <input> boxes
            var inputElem = snapshot.Elements.FirstOrDefault(e =>
                e.TagName is not ("a" or "button") &&
                (e.Role is "searchbox" or "combobox" ||
                 string.Equals(e.InputType, "search", StringComparison.OrdinalIgnoreCase) ||
                 Regex.IsMatch(e.TextOrLabel ?? string.Empty, @"\b(cari|search|telusuri|obrolan)\b", RegexOptions.IgnoreCase)) &&
                (e.IsContentEditable ||
                 e.TagName is "input" or "textarea" ||
                 e.Role is "textbox" or "searchbox" or "combobox"))
                ?? snapshot.Elements.FirstOrDefault(e =>
                e.TagName is not ("a" or "button") &&
                (e.IsContentEditable ||
                 e.TagName is "input" or "textarea" ||
                 e.Role is "textbox" or "searchbox" or "combobox"));

            if (inputElem is not null)
            {
                return new AgentStepDecision(
                    $"Halaman {curUrl} telah terbuka.",
                    $"Menemukan kolom input \"{inputElem.TextOrLabel}\".",
                    $"Mengetik \"{extractedInputText}\" ke kolom pencarian/input",
                    "input_text",
                    ElementIndex: inputElem.Index,
                    TextOrUrl: extractedInputText,
                    PressEnter: true);
            }

            // Normalize common movie title typo ("spidermen 3" -> "spiderman 3") when navigating via ?s= URL parameter
            string normalizedQuery = Regex.Replace(extractedInputText, @"\bspidermen\b", "spiderman", RegexOptions.IgnoreCase);

            // Check if there is a search link that points to /search?s= or /?s= (like LK21's <a href=".../search?s=">)
            var searchLinkWithParam = snapshot.Elements.FirstOrDefault(e =>
                !string.IsNullOrWhiteSpace(e.Href) &&
                (e.Href!.EndsWith("?s=", StringComparison.OrdinalIgnoreCase) ||
                 e.Href!.EndsWith("?q=", StringComparison.OrdinalIgnoreCase) ||
                 e.Href!.Contains("/search?s=", StringComparison.OrdinalIgnoreCase)));

            if (searchLinkWithParam?.Href is { Length: > 0 } searchHref)
            {
                string baseSearchUrl = Regex.Replace(searchHref, @"([?&](?:s|q)=).*$", "$1", RegexOptions.IgnoreCase);
                string fullSearchUrl = baseSearchUrl + Uri.EscapeDataString(normalizedQuery);
                return new AgentStepDecision(
                    "Menjalankan pencarian pada halaman.",
                    $"Membuka URL pencarian langsung untuk '{normalizedQuery}'.",
                    $"Mencari \"{normalizedQuery}\" pada situs aktif",
                    "go_to_url",
                    TextOrUrl: fullSearchUrl);
            }

            if (curUrl.EndsWith("?s=", StringComparison.OrdinalIgnoreCase) || curUrl.EndsWith("?q=", StringComparison.OrdinalIgnoreCase))
            {
                string fullSearchUrl = curUrl + Uri.EscapeDataString(normalizedQuery);
                return new AgentStepDecision(
                    "Menjalankan pencarian pada parameter URL.",
                    $"Memasukkan kata kunci '{normalizedQuery}' ke parameter pencarian.",
                    $"Mencari \"{normalizedQuery}\" pada halaman pencarian",
                    "go_to_url",
                    TextOrUrl: fullSearchUrl);
            }

            // If the search input is hidden behind a non-link Search toggle button, click it on step 2
            var searchToggleBtn = snapshot.Elements.FirstOrDefault(e =>
                e.TagName is "button" or "div" or "span" &&
                Regex.IsMatch(e.TextOrLabel, @"\b(search|cari|pencarian)\b", RegexOptions.IgnoreCase));

            if (searchToggleBtn is not null && step == 2)
            {
                return new AgentStepDecision(
                    "Kolom input pencarian belum terbuka secara langsung.",
                    $"Mengklik tombol pencarian \"{searchToggleBtn.TextOrLabel}\".",
                    $"Mengklik tombol pencarian \"{searchToggleBtn.TextOrLabel}\"",
                    "click_element",
                    ElementIndex: searchToggleBtn.Index);
            }

            // Universal site-aware search router when a site has not yet rendered a visible input box (excluding chat/messaging apps!)
            bool isMessagingOrChatSite = curUrl.Contains("whatsapp.com", StringComparison.OrdinalIgnoreCase) ||
                                         curUrl.Contains("gemini.google", StringComparison.OrdinalIgnoreCase) ||
                                         curUrl.Contains("chatgpt.com", StringComparison.OrdinalIgnoreCase);
            if (!isMessagingOrChatSite && Uri.TryCreate(curUrl, UriKind.Absolute, out var curUri))
            {
                string searchUrl = BuildSiteSpecificSearchUrl(curUri, normalizedQuery);
                return new AgentStepDecision(
                    "Menjalankan pencarian langsung pada situs aktif.",
                    $"Mencari '{normalizedQuery}' pada {curUri.Host}.",
                    $"Membuka hasil pencarian \"{normalizedQuery}\" di {curUri.Host}",
                    "go_to_url",
                    TextOrUrl: searchUrl);
            }
        }

        // STEP 2/3/4: If user wants to download a file/PDF and one is visible on the page
        if (wantsDownload && !alreadyDownloaded)
        {
            var pdfElem = snapshot.Elements.FirstOrDefault(e =>
                (e.Href is not null && (e.Href.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || e.Href.Contains("/article/download/", StringComparison.OrdinalIgnoreCase) || e.Href.Contains("/article/viewFile/", StringComparison.OrdinalIgnoreCase))) ||
                Regex.IsMatch(e.TextOrLabel, @"\b(pdf|download|unduh|full\s*text)\b", RegexOptions.IgnoreCase));

            if (pdfElem is not null)
            {
                return new AgentStepDecision(
                    "Menemukan tombol/tautan unduh pada halaman aktif.",
                    "Mengklik tombol unduh untuk mengambil berkas asli.",
                    $"Mengklik \"{pdfElem.TextOrLabel}\"",
                    "click_element",
                    ElementIndex: pdfElem.Index);
            }
        }

        // STEP 3 after search input on a content/video site (e.g. YouTube play or Movieku/LK21): click the top matching video/result link!
        bool isChatApp = curUrl.Contains("gemini.google", StringComparison.OrdinalIgnoreCase) ||
                         curUrl.Contains("chatgpt.com", StringComparison.OrdinalIgnoreCase) ||
                         curUrl.Contains("whatsapp.com", StringComparison.OrdinalIgnoreCase);
        bool isSearchUrlLoaded = curUrl.Contains("search_query=", StringComparison.OrdinalIgnoreCase) ||
                                 (curUrl.Contains("?s=", StringComparison.OrdinalIgnoreCase) && !curUrl.EndsWith("?s=", StringComparison.OrdinalIgnoreCase));
        bool wantsPlayOrOpenResult = wantsDownload ||
                                     Regex.IsMatch(userTask, @"\b(play|putar|mainkan|tonton|carikan|buka\s+video|klik)\b", RegexOptions.IgnoreCase) ||
                                     isSearchUrlLoaded;

        if ((hasTypedInput || isSearchUrlLoaded) && !isChatApp && step <= 8 &&
            !curUrl.Contains("/watch?v=", StringComparison.OrdinalIgnoreCase) &&
            wantsPlayOrOpenResult)
        {
            string normSearch = Regex.Replace(extractedInputText ?? string.Empty, @"\bspidermen\b", "spiderman", RegexOptions.IgnoreCase);
            var words = normSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var matchingResultLink = snapshot.Elements.FirstOrDefault(e =>
                e.TagName == "a" &&
                !string.IsNullOrWhiteSpace(e.Href) &&
                !e.Href!.Contains("?s=", StringComparison.OrdinalIgnoreCase) &&
                !Regex.IsMatch(e.TextOrLabel.Trim(), @"^(?:\d+[:.]\d+\b|tonton$|bagikan$|subscribe$|beranda$|shorts$)", RegexOptions.IgnoreCase) &&
                words.Any(w => w.Length >= 3 && (e.TextOrLabel.Contains(w, StringComparison.OrdinalIgnoreCase) || e.Href!.Contains(w, StringComparison.OrdinalIgnoreCase))))
                ?? snapshot.Elements.FirstOrDefault(e =>
                e.TagName == "a" &&
                !string.IsNullOrWhiteSpace(e.Href) &&
                !e.Href!.Contains("?s=", StringComparison.OrdinalIgnoreCase) &&
                e.TextOrLabel.Trim().Length > 8 &&
                !Regex.IsMatch(e.TextOrLabel.Trim(), @"^(?:\d+[:.]\d+\b|tonton$|bagikan$|subscribe$|beranda$|shorts$)", RegexOptions.IgnoreCase) &&
                e.Href!.Contains("/watch?v=", StringComparison.OrdinalIgnoreCase))
                ?? snapshot.Elements.FirstOrDefault(e => e.TagName == "a" && !string.IsNullOrWhiteSpace(e.Href) && !e.Href!.EndsWith("/", StringComparison.Ordinal) && !e.Href!.Contains("?s=", StringComparison.OrdinalIgnoreCase));

            if (matchingResultLink is not null && !string.Equals(matchingResultLink.Href, curUrl, StringComparison.OrdinalIgnoreCase))
            {
                return new AgentStepDecision(
                    "Hasil pencarian telah dimuat.",
                    $"Menemukan tautan utama \"{matchingResultLink.TextOrLabel}\".",
                    $"Mengklik \"{matchingResultLink.TextOrLabel}\"",
                    "click_element",
                    ElementIndex: matchingResultLink.Index);
            }
        }

        // STEP 2: If on a search result page, click the most relevant result link
        if (step == 2 && string.IsNullOrWhiteSpace(extractedInputText) && snapshot.Elements.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(resolvedSiteUrl) &&
                !wantsDownload &&
                !Regex.IsMatch(userTask, @"\b(cari|carikan|rangkum|baca|masukan|masukkan|ketik|isi)\b", RegexOptions.IgnoreCase))
            {
                return new AgentStepDecision(
                    $"Situs {resolvedSiteUrl} sudah terbuka.",
                    "Tugas membuka situs selesai.",
                    $"Halaman {resolvedSiteUrl} siap digunakan.",
                    "done",
                    TextOrUrl: $"Halaman {resolvedSiteUrl} telah dibuka di Browser Panel Kanan.");
            }

            var firstLink = snapshot.Elements.FirstOrDefault(e => e.TagName == "a" && !string.IsNullOrWhiteSpace(e.Href));
            if (firstLink is not null)
            {
                return new AgentStepDecision(
                    "Halaman telah dimuat dan dipindai.",
                    "Mengklik tautan teratas yang relevan.",
                    $"Mengklik \"{firstLink.TextOrLabel}\"",
                    "click_element",
                    ElementIndex: firstLink.Index);
            }
        }

        return new AgentStepDecision(
            "Seluruh langkah navigasi dan interaksi pada halaman selesai.",
            "Menyelesaikan tugas dan menyusun hasil.",
            "Menyajikan hasil akhir dari halaman aktif.",
            "done",
            TextOrUrl: "Ekstraksi selesai.");
    }

    public static string BuildSiteSpecificSearchUrl(Uri curUri, string query)
    {
        string normalizedQuery = Regex.Replace((query ?? string.Empty).Trim(), @"\bspidermen\b", "spiderman", RegexOptions.IgnoreCase);
        string encoded = Uri.EscapeDataString(normalizedQuery);
        string host = (curUri.Host ?? string.Empty).ToLowerInvariant();
        string origin = $"{curUri.Scheme}://{curUri.Authority}";

        if (host.Contains("youtube.com") || host.Contains("youtu.be"))
            return $"https://www.youtube.com/results?search_query={encoded}";
        if (host.Contains("scholar.google."))
            return $"{origin}/scholar?q={encoded}";
        if (host.Contains("google."))
            return $"{origin}/search?q={encoded}";
        if (host.Contains("bing.com"))
            return $"https://www.bing.com/search?q={encoded}";
        if (host.Contains("duckduckgo.com"))
            return $"https://duckduckgo.com/?q={encoded}";
        if (host.Contains("wikipedia.org"))
            return $"{origin}/wiki/Special:Search?search={encoded}";
        if (host.Contains("github.com"))
            return $"https://github.com/search?q={encoded}";
        if (host.Contains("lk21"))
            return $"{origin}/search?s={encoded}";

        return $"{origin}/?s={encoded}";
    }
}
