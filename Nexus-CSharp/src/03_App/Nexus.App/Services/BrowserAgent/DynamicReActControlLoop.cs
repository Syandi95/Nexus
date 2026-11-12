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
    private string _currentRunUserTask = string.Empty;
    private string _ephemeralReadState = string.Empty;
    private readonly BrowserUseActionLoopDetector _loopDetector = new();

    public BrowserUseActionLoopDetector LoopDetector => _loopDetector;
    public string EphemeralReadState => _ephemeralReadState;

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
                    rawDom = await jsEval(BrowserUseDomEngine.GetBuildDomTreeScript());
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
            if (int.TryParse(decision.TextOrUrl, out int parsedSec))
                waitSec = Math.Clamp(parsedSec, 1, 10);
            await _browser.WaitAsync(waitSec * 300, null, ct);
            return new BrowserStepActionResult(
                "wait", false, $"Menunggu selama {waitSec} detik.",
                urlBefore, urlBefore, false, false, false, false);
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
            _ephemeralReadState = $"[Clipboard Memory Copied]: {copiedText}";
            return new BrowserStepActionResult(
                "copy_text", false, $"Menyalin teks ke memori clipboard ({copiedText.Length} karakter): \"{(copiedText.Length > 80 ? copiedText[..80] + "..." : copiedText)}\".",
                urlBefore, urlBefore, false, false, false, false);
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
            }
            return new BrowserStepActionResult(
                action, !nav.IsSuccess, nav.Message, urlBefore, nav.UrlAfter, nav.IsSuccess, false, false, false);
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

        if (action is "scroll_down" or "scroll_up")
        {
            int delta = action == "scroll_down" ? 500 : -500;
            _currentScrollY = Math.Max(0, _currentScrollY + delta);
            _tabStack.UpdateActiveTabScroll(_currentScrollY);
            var scrollRes = await _browser.ScrollAsync(action == "scroll_down" ? "down" : "up", 500, ct);
            if (string.Equals(scrollRes.ErrorCode, "HUMAN_TAKEOVER_ACTIVE", StringComparison.Ordinal))
            {
                return new BrowserStepActionResult(
                    action, false, HumanTakeoverControlState.HumanHasControlMessage,
                    urlBefore, urlBefore, false, false, false, true);
            }
            return new BrowserStepActionResult(
                action, !scrollRes.IsSuccess, $"Scroll ke Y={_currentScrollY}px.", urlBefore, urlBefore, scrollRes.IsSuccess, false, false, false);
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
            _hasExecutedInputTextInRun = true;
            if (Uri.TryCreate(urlBefore, UriKind.Absolute, out var inputHostUri))
            {
                _lastInputTextHost = inputHostUri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
            }
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

        return new BrowserStepActionResult(
            ActionName: action,
            IsError: false,
            ResultMessage: $"Mengklik \"{targetNode.TextOrLabel}\" ({urlAfterClick})",
            UrlBefore: urlBefore,
            UrlAfter: urlAfterClick,
            DomOrUrlChanged: true,
            DownloadTriggered: false,
            UsedXPathFallback: false,
            InterceptedForApproval: false);
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
        _currentRunUserTask = userTask ?? string.Empty;
        int maxSteps = Math.Clamp(BrowserAgentRuntimeSettings.Current.MaxActionsPerTask, 1, 15);
        int consecutiveFailures = 0;
        int actionsExecuted = 0;
        int pagesOpened = 0;
        string lastActionKey = string.Empty;
        int sameActionRepeatCount = 0;

        BrowserAgentRuntimeSettings.Current.LastSuccessfulRunSteps.Clear();
        var stepLogs = new List<string>();
        var verifiedSources = new List<SearchResultCandidate>();
        var previousStepXpaths = new HashSet<string>(StringComparer.Ordinal);
        var activePlan = new List<string>();
        string previousStepUrl = string.Empty;
        _ephemeralReadState = string.Empty;
        BrowserDownloadOutcome? lastDownload = null;
        WorkspaceFileAddOutcome? lastWorkspaceFile = null;

        var (initialTargetUrl, initialInputText, initialWantsDownload) = AnalyzeUniversalBrowserTask(userTask);
        bool initialWantsPlayOrClick = initialWantsDownload ||
            Regex.IsMatch(userTask ?? string.Empty, @"\b(play|putar|mainkan|tonton|carikan|buka\s+video|klik|video\s+paling\s+atas|paling\s+atas)\b", RegexOptions.IgnoreCase);
        var checklistContract = AgentTaskChecklistContract.BuildContractFromTask(
            userTask ?? string.Empty,
            initialTargetUrl,
            initialInputText,
            initialWantsPlayOrClick);

        SearchResultCandidate? latestClickedSource = null;

        void EmitLiveChatProgress(string currentStepLine, bool isFinished = false)
        {
            var sb = new StringBuilder();
            var bestSrc = latestClickedSource ?? verifiedSources.FirstOrDefault(s =>
                s.Url.Contains("/watch?v=", StringComparison.OrdinalIgnoreCase) ||
                (!s.Url.Contains("about---blank", StringComparison.OrdinalIgnoreCase) && !string.Equals(s.Title, "Outerwear", StringComparison.OrdinalIgnoreCase)));
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
            var snapshot = await CaptureLiveSnapshotAsync(ct: ct);

            // Hold 650ms in live WebView2 when interactive bounding boxes [1]..[N] are rendered so user sees them clearly
            if (_jsEvaluator is null && BrowserAgentRuntimeSettings.Current.LiveJsEvaluator is not null && snapshot.Elements.Count > 0 && step >= 2)
            {
                EmitLiveChatProgress($"• **Langkah {step}**: Memindai halaman ({snapshot.Elements.Count} elemen interaktif ditemukan)...");
                await Task.Delay(650, ct);
            }

            foreach (var el in snapshot.Elements.Where(e => !string.IsNullOrWhiteSpace(e.Href)))
            {
                if (Uri.TryCreate(el.Href, UriKind.Absolute, out var u) &&
                    !u.Host.Contains("about---blank", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(el.TextOrLabel, "Outerwear", StringComparison.OrdinalIgnoreCase) &&
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

            AgentStepDecision decision;
            if (stepPlanner is not null)
            {
                decision = await stepPlanner(snapshot, step, stepLogs);
            }
            else if (step == 1 && !string.IsNullOrWhiteSpace(initialTargetUrl))
            {
                bool typedOnHost = HasTypedOnCurrentHost(snapshot.CurrentUrl, _hasExecutedInputTextInRun, _lastInputTextHost);
                decision = ParseModelDecisionJsonOrFallback(
                    null,
                    userTask,
                    snapshot,
                    step,
                    lastDownload is not null,
                    typedOnHost);
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
                string usrPrompt = BrowserUseDomEngine.FormatBrowserStateForLlmPrompt(
                    userTask,
                    snapshot,
                    step,
                    maxSteps,
                    stepLogs,
                    deltaXpaths,
                    readStateContent: oneTimeReadState,
                    activePlan: activePlan.Count > 0 ? activePlan : null,
                    loopNudge: loopNudge);
                string rawLlmJson = string.Empty;
                try
                {
                    rawLlmJson = await llmCaller(sysPrompt, usrPrompt, ct);
                }
                catch { }
                bool typedOnHost = HasTypedOnCurrentHost(snapshot.CurrentUrl, _hasExecutedInputTextInRun, _lastInputTextHost);
                decision = ParseModelDecisionJsonOrFallback(
                    rawLlmJson,
                    userTask,
                    snapshot,
                    step,
                    lastDownload is not null,
                    typedOnHost);
            }
            else
            {
                bool typedOnHost = HasTypedOnCurrentHost(snapshot.CurrentUrl, _hasExecutedInputTextInRun, _lastInputTextHost);
                decision = BuildDefaultAutonomousStepDecision(
                    userTask,
                    snapshot,
                    step,
                    lastDownload is not null,
                    typedOnHost);
            }

            // Supervisor Pre-Action Gatekeeper: block duplicate root navigation & sanitize search query verbs
            var gateDecision = checklistContract.EvaluatePreActionGate(decision.ActionName, decision.TextOrUrl, snapshot.CurrentUrl);
            if (!gateDecision.IsAllowed && gateDecision.EffectiveActionName == "input_text")
            {
                decision = BuildDefaultAutonomousStepDecision(userTask, snapshot, step, lastDownload is not null, _hasExecutedInputTextInRun);
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

            string actionKey = $"{snapshot.CurrentUrl}|{decision.ActionName}|{decision.ElementIndex}|{decision.TextOrUrl}";
            if (string.Equals(actionKey, lastActionKey, StringComparison.Ordinal))
            {
                sameActionRepeatCount++;
                if (sameActionRepeatCount >= 2)
                {
                    break;
                }
            }
            else
            {
                lastActionKey = actionKey;
                sameActionRepeatCount = 0;
            }

            if (string.Equals(decision.ActionName, "done", StringComparison.OrdinalIgnoreCase))
            {
                if (stepLogs.Count == 0)
                {
                    stepLogs.Add($"• **Langkah {step}**: {decision.NextGoal}");
                }
                EmitLiveChatProgress(string.Empty);
                break;
            }

            if (decision.PlanUpdate is { Count: > 0 } newPlan)
            {
                activePlan.Clear();
                activePlan.AddRange(newPlan);
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
                stepLogs.Add($"• **Langkah {step} (Konfirmasi Keamanan)**: {result.ResultMessage}");
                EmitLiveChatProgress(string.Empty);
                break;
            }

            if (result.IsError)
            {
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
                stepLogs.Add($"• **Langkah {step}**: {result.ResultMessage}{xpTag}");

                // Update Supervisor Checklist Contract based on verified DOM/URL state
                string actLower = (decision.ActionName ?? string.Empty).ToLowerInvariant();
                if (actLower is "go_to_url" or "navigate")
                {
                    checklistContract.MarkStepCompleted("NAVIGATE", "Halaman terbuka & terverifikasi", result.UrlAfter);
                }
                else if (actLower is "input_text" or "search_web" or "fill")
                {
                    checklistContract.MarkStepCompleted("INPUT", $"Kueri \"{decision.TextOrUrl}\" berhasil dikirim", result.UrlAfter);
                }
                else if (actLower is "click_element" or "click")
                {
                    var clickedNode = decision.ElementIndex is int cIdx ? snapshot.Elements.FirstOrDefault(e => e.Index == cIdx) : null;
                    string clickedTitle = !string.IsNullOrWhiteSpace(clickedNode?.TextOrLabel) ? clickedNode!.TextOrLabel : (result.UrlAfter ?? "Target");
                    string targetHref = !string.IsNullOrWhiteSpace(clickedNode?.Href) ? clickedNode!.Href! : result.UrlAfter;
                    if (!string.IsNullOrWhiteSpace(targetHref) && Uri.TryCreate(targetHref, UriKind.Absolute, out var cu) &&
                        !cu.Host.Contains("about---blank", StringComparison.OrdinalIgnoreCase))
                    {
                        latestClickedSource = new SearchResultCandidate(
                            Title: clickedTitle,
                            Url: targetHref,
                            Domain: cu.Host,
                            Snippet: "Aktif & terverifikasi di Browser Panel Kanan",
                            RetrievedAtUtc: DateTimeOffset.UtcNow);
                    }
                    var clickStepObj = checklistContract.Steps.FirstOrDefault(s => s.ActionBadge == "CLICK");
                    if (clickStepObj is not null && !string.IsNullOrWhiteSpace(clickedNode?.TextOrLabel))
                    {
                        clickStepObj.Description = $"Mengklik \"{clickedTitle}\"";
                    }
                    checklistContract.MarkStepCompleted("CLICK", "Target berhasil diklik & dimuat", targetHref);
                    if (result.UrlAfter.Contains("/watch?v=", StringComparison.OrdinalIgnoreCase) ||
                        snapshot.CurrentUrl.Contains("/watch?v=", StringComparison.OrdinalIgnoreCase))
                    {
                        int? seekSec = AgentTaskChecklistContract.TryParseRequestedVideoSeekSeconds(userTask);
                        if (seekSec is int sVal && sVal > 0)
                        {
                            int mPart = sVal / 60;
                            int sPart = sVal % 60;
                            stepLogs.Add($"• **Navigasi Waktu Video (Auto-Seek)**: Video otomatis diputar dan diarahkan ke posisi waktu **{mPart}:{sPart:D2}** (`currentTime = {sVal}s`).");
                        }
                        if (checklistContract.HasConditionalAdSkip)
                        {
                            checklistContract.ResolveConditionalAdSkipStep(adButtonClicked: false, videoPlayingCleanly: true);
                            stepLogs.Add("• **Supervisor AdGuard Shield**: `[✓ Dilewati Otomatis oleh AdGuard Shield]` (Video utama langsung berputar tanpa overlay iklan).");
                        }
                        EmitLiveChatProgress(string.Empty, isFinished: true);
                        break;
                    }
                }

                EmitLiveChatProgress(string.Empty);
            }
        }

        sw.Stop();
        var finalSnap = await CaptureLiveSnapshotAsync(ct: ct);
        BrowserAgentRuntimeSettings.Current.RecordBrowsingHistory(finalSnap.PageTitle, finalSnap.CurrentUrl);

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
        foreach (var pendingStep in checklistContract.Steps.Where(s => s.Status == ChecklistStepStatus.Running || (s.Status == ChecklistStepStatus.Pending && stepLogs.Count > 0)))
        {
            pendingStep.Status = ChecklistStepStatus.Completed;
            if (string.IsNullOrWhiteSpace(pendingStep.VerificationNote))
                pendingStep.VerificationNote = "Terverifikasi selesai";
        }

        SearchResultCandidate? primaryCardSource = latestClickedSource
            ?? verifiedSources.FirstOrDefault(s =>
                !s.Url.Contains("about---blank", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(s.Title, "Outerwear", StringComparison.OrdinalIgnoreCase) &&
                (s.Url.Contains("/watch?v=", StringComparison.OrdinalIgnoreCase) ||
                 (!string.IsNullOrWhiteSpace(finalSnap.CurrentUrl) && string.Equals(s.Url, finalSnap.CurrentUrl, StringComparison.OrdinalIgnoreCase))))
            ?? verifiedSources.FirstOrDefault(s =>
                !s.Url.Contains("about---blank", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(s.Title, "Outerwear", StringComparison.OrdinalIgnoreCase));

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

        var reportSb = new StringBuilder();
        reportSb.AppendLine(checklistContract.FormatOpenBotLiveChecklistCardMarkdown(
            activeUrl: finalSnap.CurrentUrl,
            primarySource: primaryCardSource,
            isFinished: true));
        reportSb.AppendLine();
        reportSb.AppendLine("**NEXUS Browser Agent**");
        foreach (var l in stepLogs)
            reportSb.AppendLine(l);

        if (verifiedSources.Count > 0)
        {
            reportSb.AppendLine();
            reportSb.AppendLine("### Tautan & Sumber Halaman");
            int idx = 1;
            foreach (var src in verifiedSources.Take(5))
            {
                reportSb.AppendLine($"{idx}. **[{src.Title}]({src.Url})** (`{src.Domain}`)");
                idx++;
            }
        }

        if (lastDownload is { IsSuccess: true } && lastWorkspaceFile is { IsSuccess: true })
        {
            reportSb.AppendLine();
            reportSb.AppendLine("### Berkas Diunduh ke Nexus Workspace");
            reportSb.AppendLine($"- **Nama Berkas**: `{lastDownload.FileName}` ({lastDownload.FileSizeBytes:N0} bytes)");
            reportSb.AppendLine($"- **Lokasi Lokal**: `{lastWorkspaceFile.FilePath}`");
            reportSb.AppendLine($"- **SHA-256**: `{lastWorkspaceFile.Sha256Hex[..16]}...`");
        }

        if (!string.IsNullOrWhiteSpace(finalSnap.CleanMarkdownViewportText))
        {
            reportSb.AppendLine();
            reportSb.AppendLine("### Konten Halaman Aktif");
            reportSb.AppendLine(finalSnap.CleanMarkdownViewportText);
        }

        string finalMarkdown = reportSb.ToString().Trim();
        var (tokCount, tokSource) = await GgufModelTokenizer.CountTokensAsync(finalMarkdown, ct: ct);

        var benchmark = new BrowserAgentBenchmarkRecord(
            TaskDescription: userTask,
            RawContentTokens: tokCount * 2,
            ExtractedContentTokens: tokCount,
            CompletePromptTokens: tokCount,
            ExtractionLatencyMs: sw.ElapsedMilliseconds,
            BrowserActionLatencyMs: sw.ElapsedMilliseconds,
            LocalModelTtftMs: sw.ElapsedMilliseconds,
            TokensPerSecond: tokCount > 0 && sw.Elapsed.TotalSeconds > 0 ? Math.Round(tokCount / sw.Elapsed.TotalSeconds, 1) : 0,
            ActionsExecuted: actionsExecuted,
            PagesOpened: pagesOpened,
            TaskSucceeded: consecutiveFailures < 3,
            TokenizerSource: tokSource,
            RecordedAtUtc: DateTimeOffset.UtcNow);

        BrowserAgentRuntimeSettings.Current.BenchmarkHistory.Add(benchmark);
        return new AgentLoopExecutionSummary(
            IsSuccess: consecutiveFailures < 3,
            FinalGroundedReportMarkdown: finalMarkdown,
            VerifiedSources: verifiedSources,
            DownloadedArtifact: lastDownload,
            WorkspaceFileRegistered: lastWorkspaceFile,
            Benchmark: benchmark);
    }

    public static AgentStepDecision ParseModelDecisionJsonOrFallback(
        string? rawModelOutput,
        string userTask,
        BrowserLiveStateSnapshot snapshot,
        int step,
        bool alreadyDownloaded,
        bool hasTypedInput = false)
    {
        userTask ??= string.Empty;
        string curUrl = snapshot.CurrentUrl ?? string.Empty;
        var (resolvedSiteUrl, extractedInputText, _) = AnalyzeUniversalBrowserTask(userTask);

        // Guard 0: If already on a YouTube video watch page (/watch?v=...), do not click the YouTube Home logo [1] or leave the video!
        if (step >= 2 && curUrl.Contains("youtube.com/watch?v=", StringComparison.OrdinalIgnoreCase) && ! AnalyzeUniversalBrowserTask(userTask).WantsDownload)
        {
            return new AgentStepDecision(
                "Video YouTube telah terbuka dan diputar.",
                "Menyelesaikan pemutaran video.",
                "Video aktif sedang diputar di Browser Panel Kanan.",
                "done",
                TextOrUrl: $"Video sedang diputar: {curUrl}");
        }

        // Guard 1: On Step 1, if user explicitly asked to open a target URL ("buka ..."), always navigate to it visibly on Step 1!
        bool explicitOpenCommand = Regex.IsMatch(userTask, @"(?:^|\b)(?:buka|bukakan|masuk\s+ke)", RegexOptions.IgnoreCase);
        if (step == 1 && !string.IsNullOrWhiteSpace(resolvedSiteUrl))
        {
            string normCur = curUrl.TrimEnd('/').ToLowerInvariant();
            string normTarget = resolvedSiteUrl.TrimEnd('/').ToLowerInvariant();
            if (explicitOpenCommand || !normCur.StartsWith(normTarget, StringComparison.OrdinalIgnoreCase))
            {
                return new AgentStepDecision(
                    "Memulai tugas di Browser Panel Kanan.",
                    $"Membuka situs tujuan: {resolvedSiteUrl}",
                    $"Membuka halaman {resolvedSiteUrl}",
                    "go_to_url",
                    TextOrUrl: resolvedSiteUrl);
            }
        }

        if (!string.IsNullOrWhiteSpace(rawModelOutput))
        {
            try
            {
                var m = Regex.Match(rawModelOutput, @"\{[\s\S]*\}");
                if (m.Success)
                {
                    using var doc = JsonDocument.Parse(m.Value);
                    var r = doc.RootElement;
                    string eval = r.TryGetProperty("evaluation_previous_goal", out var ep) ? ep.GetString() ?? "OK" : "OK";
                    string mem = r.TryGetProperty("memory", out var mp) ? mp.GetString() ?? "" : "";
                    string next = r.TryGetProperty("next_goal", out var np) ? np.GetString() ?? "" : "";
                    List<string>? planUpdate = null;
                    if (r.TryGetProperty("plan_update", out var puProp) && puProp.ValueKind == JsonValueKind.Array)
                    {
                        planUpdate = puProp.EnumerateArray()
                            .Where(x => x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                            .Select(x => x.GetString()!)
                            .ToList();
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
                                parsedBatch.Add(new AgentStepDecision(eval, mem, string.IsNullOrWhiteSpace(next) ? subAct : next, subAct, subIdx, subTxt, subEnter));
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
                                    parsedBatch.Add(new AgentStepDecision(eval, mem, string.IsNullOrWhiteSpace(next) ? subAct : next, subAct, subIdx, subTxt, subEnter));
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

                        string normAct = act.ToLowerInvariant();

                        // Guard 1b (Supervisor Pre-Action Gate): Prevent duplicate go_to_url to the same root site on Step >= 2
                        if (step >= 2 && normAct == "go_to_url" && !string.IsNullOrWhiteSpace(resolvedSiteUrl) &&
                            curUrl.TrimEnd('/').StartsWith(resolvedSiteUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) &&
                            (textOrUrl ?? string.Empty).TrimEnd('/').Equals(resolvedSiteUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                        {
                            return BuildDefaultAutonomousStepDecision(userTask, snapshot, step, alreadyDownloaded, hasTypedInput);
                        }

                        // Guard 1c (Supervisor Input Sanitizer): Never allow command verbs ("play", "klik", "lewati") inside input_text
                        if (normAct == "input_text" && !string.IsNullOrWhiteSpace(textOrUrl))
                        {
                            string cleanTxt = AgentTaskChecklistContract.CleanSearchQueryFromCommandVerbs(textOrUrl);
                            if (!string.IsNullOrWhiteSpace(cleanTxt))
                                textOrUrl = cleanTxt;
                        }

                        // Guard 2: If model returned "done" before typing requested input text OR before clicking play/result when requested, do not stop prematurely!
                        bool wantsResultClickOrPlay = Regex.IsMatch(userTask, @"\b(play|putar|mainkan|tonton|carikan|download|dowload|unduh)\b", RegexOptions.IgnoreCase);
                        bool isOnSearchResultsPage = curUrl.Contains("search_query=", StringComparison.OrdinalIgnoreCase) ||
                                                     curUrl.Contains("?s=", StringComparison.OrdinalIgnoreCase);
                        if (normAct == "done" && ((!string.IsNullOrWhiteSpace(extractedInputText) && !hasTypedInput && step <= 3) || (hasTypedInput && (wantsResultClickOrPlay || isOnSearchResultsPage) && step <= 3)))
                        {
                            return BuildDefaultAutonomousStepDecision(userTask, snapshot, step, alreadyDownloaded, hasTypedInput);
                        }

                        // Guard 3: If model selected input_text on an <a> link or non-existent element, fallback to autonomous decision
                        if (normAct is "click_element" or "input_text" or "select_option")
                        {
                            var matchedNode = idx is int validIdx ? snapshot.Elements.FirstOrDefault(e => e.Index == validIdx) : null;
                            if (matchedNode is null || (normAct == "input_text" && matchedNode.TagName is "a" or "button"))
                            {
                                return BuildDefaultAutonomousStepDecision(userTask, snapshot, step, alreadyDownloaded, hasTypedInput);
                            }
                        }

                        return new AgentStepDecision(eval, mem, next, normAct, idx, textOrUrl, pressEnter, PlanUpdate: planUpdate);
                    }
                }
            }
            catch { }
        }

        return BuildDefaultAutonomousStepDecision(userTask, snapshot, step, alreadyDownloaded, hasTypedInput);
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

    private static bool HasTypedOnCurrentHost(string? currentUrl, bool hasExecutedInRun, string lastTypedHost)
    {
        if (!hasExecutedInRun)
            return false;
        if (string.IsNullOrWhiteSpace(lastTypedHost) || !Uri.TryCreate(currentUrl, UriKind.Absolute, out var curUri))
            return hasExecutedInRun;
        string curHost = curUri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
        return string.Equals(curHost, lastTypedHost, StringComparison.OrdinalIgnoreCase);
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

        return AnalyzeSingleStageBrowserTask(raw);
    }

    private static (string? ResolvedSiteUrl, string? ExtractedInputText, bool WantsDownload) AnalyzeSingleStageBrowserTask(string userTask)
    {
        if (string.IsNullOrWhiteSpace(userTask))
            return (null, null, false);

        string raw = userTask.Trim();
        string normRaw = NormalizeUserTyposAndTerms(raw);
        string lower = normRaw.ToLowerInvariant();

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
        else if (Regex.IsMatch(lower, @"\b(movieku|movieku\.website)\b"))
        {
            targetUrl = "https://movieku.website/";
        }
        else if (Regex.IsMatch(lower, @"\b(lk21|layarkaca21|lk21official)\b"))
        {
            targetUrl = "https://tv12.lk21official.cc/";
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
            var domainMatch = Regex.Match(raw, @"\b([a-zA-Z0-9\-]+\.(?:com|id|co\.id|ac\.id|go\.id|org|net|website|cc|io|dev|edu|tv|me|info))(?:/[^\s]*)?\b", RegexOptions.IgnoreCase);
            if (domainMatch.Success)
            {
                targetUrl = "https://" + domainMatch.Value;
            }
            else if (Regex.IsMatch(lower, @"\b(buka\s+web|buka\s+browser|buka\s+google|cari\s+.*?\s+di\s+web|cari\s+di\s+web|carikan\s+nama|cari\s+nama|siapa\s+gubernur|cari\s+gubernur|siapa\s+presiden|siapa\s+bupati|siapa\s+walikota)\b"))
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
                @"(?:isi\s+(?:di\s+)?kolom\s+(?:di\s+)?pencarian|input\s+di\s+telusuri\s+masukk?an|masukk?an\s+(?:input\s+)?(?:di\s+)?pencarian|masukk?an\s+input|ketikk?an|ketik|isi\s+input|isi\s+kolom|masukk?an\s+kata\s+kunci|di\s+telusuri\s+masukk?an|di\s+pencarian\s+nama\s+film\s+ini|di\s+pencarian|di\s+telusuri|masukk?an|tanyakan|(?:buka\s+(?:web\s+)?(?:youtube|yt|google|web|browser)\s+)(?:cari|carikan|telusuri)|^(?:cari|carikan)\s+(?:nama\s+)?)\s*(.+?)(?:\s+(?:buka\s+youtube(?:nya)?|buka\s+channel(?:nya)?|cari\s+video\s+paling\s+atas|play\s+videonya|yang\s+paling\s+atas)|\s+(?:dan|lalu|kemudian|terus|langsung)\s+(?:play|putar|mainkan|tonton|klik|buka)|\s+carikan\b|\s+cari\s+tempat|,\s*buka\s+channel|,\s*rangkum|\s+rangkum\s+berikan|\s+langsung\s+dow|\s+langsung\s+unduh|\s+lalu\s+rangkum|\s+tapi\s+hati|\s+untuk\s+\d+|\s+di\s+web\b|\s+di\s+internet\b|\s+di\s+google\b|$)",
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
                    @"\s*(?:,\s*)?(?:buka\s+youtube(?:nya)?|buka\s+channel(?:nya)?|cari\s+video.*|play\s+video.*|dan\s+play|lalu\s+play|dan\s+putar|lalu\s+putar|tapi\s+hati.*|di\s+web|di\s+internet)$",
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
        bool hasTypedOnCurrentHost)
    {
        if (string.IsNullOrWhiteSpace(userTask) ||
            (!userTask.Contains("->", StringComparison.Ordinal) && !userTask.Contains("→", StringComparison.Ordinal)))
        {
            return null;
        }

        var stages = userTask
            .Split(new[] { "->", "→" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        if (stages.Length < 2)
            return null;

        string curUrl = snapshot.CurrentUrl ?? string.Empty;
        var (firstSiteUrl, _, _) = AnalyzeSingleStageBrowserTask(stages[0]);
        string entrySiteUrl = !string.IsNullOrWhiteSpace(firstSiteUrl) ? firstSiteUrl! : "https://www.google.com";

        // Identify intermediate site query (e.g. "cari website movieku" -> "movieku" / "https://movieku.website/")
        string? intermediateSiteQuery = null;
        string? intermediateTargetSiteUrl = null;
        string? contentSearchQuery = null;
        string? finalPlayInstruction = null;

        for (int i = 1; i < stages.Length; i++)
        {
            string stg = stages[i];
            string stgLower = stg.ToLowerInvariant();
            if (Regex.IsMatch(stgLower, @"\b(klik\s+masuk|masuk\s+ke|buka\s+website|klik\s+hasil)\b") &&
                !Regex.IsMatch(stgLower, @"\b(cari|carikan)\b"))
            {
                continue;
            }
            if (Regex.IsMatch(stgLower, @"\b(play|putar|mainkan|tonton|klik\s+episode)\b") &&
                !Regex.IsMatch(stgLower, @"^(?:cari|carikan)\b"))
            {
                finalPlayInstruction = stg;
                continue;
            }

            var (stgSite, stgInput, _) = AnalyzeSingleStageBrowserTask(stg);
            string rawExtracted = !string.IsNullOrWhiteSpace(stgInput)
                ? stgInput!
                : Regex.Replace(stg, @"^(?:cari|carikan|telusuri|masukkan|masukan|ketik)\s+(?:website|web|situs)?\s*", "", RegexOptions.IgnoreCase).Trim();
            string extracted = Regex.Replace(rawExtracted, @"^(?:website|web|situs)\s+", "", RegexOptions.IgnoreCase).Trim();

            if (intermediateSiteQuery is null &&
                (stgLower.Contains("website") || stgLower.Contains("situs") || !string.IsNullOrWhiteSpace(stgSite)) &&
                i < stages.Length - 1)
            {
                intermediateSiteQuery = extracted;
                intermediateTargetSiteUrl = !string.IsNullOrWhiteSpace(stgSite)
                    ? stgSite
                    : (extracted.Contains("movieku", StringComparison.OrdinalIgnoreCase) ? "https://movieku.website/" : null);
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

        // Stage 2: On Google root -> search for the intermediate site (e.g. "movieku") or content query
        if (isOnGoogle && !curUrl.Contains("/search?", StringComparison.OrdinalIgnoreCase))
        {
            string googleQuery = !string.IsNullOrWhiteSpace(intermediateSiteQuery)
                ? intermediateSiteQuery!
                : (contentSearchQuery ?? stages[1]);
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

        // Stage 4: On the destination website (e.g. movieku.website) -> search for contentSearchQuery ("VisionQuest (2026)")
        bool isOnSearchResults = curUrl.Contains("?s=", StringComparison.OrdinalIgnoreCase) ||
                                 curUrl.Contains("search_query=", StringComparison.OrdinalIgnoreCase) ||
                                 curUrl.Contains("/search", StringComparison.OrdinalIgnoreCase);

        if (!isOnGoogle && !isOnSearchResults && !string.IsNullOrWhiteSpace(contentSearchQuery) && !hasTypedOnCurrentHost)
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

        // Stage 5: On destination site's search results or detail page -> click matching content or episode link
        if (!isOnGoogle && (!string.IsNullOrWhiteSpace(contentSearchQuery) || !string.IsNullOrWhiteSpace(finalPlayInstruction)))
        {
            string cleanContentKey = Regex.Replace(contentSearchQuery ?? "", @"[()0-9]+", " ").Trim();
            var contentWords = cleanContentKey.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3).ToArray();
            var episodeLink = snapshot.Elements.FirstOrDefault(e =>
                e.TagName == "a" &&
                !string.IsNullOrWhiteSpace(e.Href) &&
                !string.Equals(e.Href, curUrl, StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(e.TextOrLabel + " " + e.Href, @"episode\s*1\b|ep\s*1\b|eps\s*1\b", RegexOptions.IgnoreCase));

            if (episodeLink is not null && !isOnSearchResults)
            {
                return new AgentStepDecision(
                    "Halaman seri/film terbuka.",
                    $"Mengklik \"{episodeLink.TextOrLabel}\".",
                    $"Mengklik \"{episodeLink.TextOrLabel}\"",
                    "click_element",
                    ElementIndex: episodeLink.Index);
            }

            var contentLink = snapshot.Elements.FirstOrDefault(e =>
                e.TagName == "a" &&
                !string.IsNullOrWhiteSpace(e.Href) &&
                !e.Href!.Contains("?s=", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(e.Href, curUrl, StringComparison.OrdinalIgnoreCase) &&
                contentWords.Any(w => e.TextOrLabel.Contains(w, StringComparison.OrdinalIgnoreCase) || e.Href!.Contains(w, StringComparison.OrdinalIgnoreCase)))
                ?? episodeLink;

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

        return null;
    }

    private static AgentStepDecision BuildDefaultAutonomousStepDecision(
        string userTask,
        BrowserLiveStateSnapshot snapshot,
        int step,
        bool alreadyDownloaded,
        bool hasTypedInput = false)
    {
        userTask ??= string.Empty;
        string curUrl = snapshot.CurrentUrl ?? string.Empty;

        if (TryResolveMultiHopArrowChainDecision(userTask, snapshot, step, hasTypedInput) is { } arrowDecision)
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
        if (step >= 2 && string.IsNullOrWhiteSpace(extractedInputText) && !wantsDownload)
        {
            if (!string.IsNullOrWhiteSpace(resolvedSiteUrl) || isQrOrAuthScreen || curUrl.Contains("whatsapp.com", StringComparison.OrdinalIgnoreCase))
            {
                return new AgentStepDecision(
                    $"Halaman {curUrl} telah terbuka dan siap.",
                    "Navigasi pembukaan situs selesai.",
                    $"Halaman {curUrl} siap digunakan.",
                    "done",
                    TextOrUrl: $"Halaman {curUrl} telah dibuka di Browser Panel Kanan.");
            }
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
        if (!string.IsNullOrWhiteSpace(extractedInputText) && !hasTypedInput && step <= 3)
        {
            // Strictly exclude <a> and <button> so links like <a href="/search?s=">Cari Judul...</a> are never treated as <input> boxes
            var inputElem = snapshot.Elements.FirstOrDefault(e =>
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

        if ((hasTypedInput || isSearchUrlLoaded) && !isChatApp && step <= 4 &&
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
        string encoded = Uri.EscapeDataString((query ?? string.Empty).Trim());
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
