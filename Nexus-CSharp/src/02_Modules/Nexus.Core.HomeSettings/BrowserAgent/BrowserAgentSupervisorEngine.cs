global using Nexus.Contracts.BrowserAgent;
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



public static class GgufModelTokenizer
{
    private static readonly HttpClient SharedHttp = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromMilliseconds(180)
    })
    {
        Timeout = TimeSpan.FromMilliseconds(450)
    };

    private static int _lastWorkingPort;
    private static long _allPortsOfflineUntilTicks;

    public static async Task<(int TokenCount, string Source)> CountTokensAsync(
        string? text,
        int llamaServerPort = 8080,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text))
            return (0, "empty");

        long nowTicks = DateTimeOffset.UtcNow.UtcTicks;
        if (nowTicks >= Interlocked.Read(ref _allPortsOfflineUntilTicks))
        {
            int cachedPort = Volatile.Read(ref _lastWorkingPort);
            int[] candidatePorts = cachedPort > 0
                ? new[] { cachedPort, llamaServerPort, 8080, 8081, 11434 }
                : new[] { llamaServerPort, 8080, 8081, 11434 };

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
                            Volatile.Write(ref _lastWorkingPort, port);
                            return (tokArr.GetArrayLength(), $"gguf-llama-server:{port}");
                        }
                    }
                }
                catch
                {
                    // Server not listening on this port; try next
                }
            }

            // Cache offline state for 30s so Windows TCP SYN timeouts never stall browser completion or test suites
            Volatile.Write(ref _lastWorkingPort, 0);
            Interlocked.Exchange(ref _allPortsOfflineUntilTicks, DateTimeOffset.UtcNow.AddSeconds(30).UtcTicks);
        }

        // Deterministic SentencePiece/WordPiece subword tokenizer fallback when llama-server process is offline
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
    public const int PageRows = Nexus.Conversation.Windowing.TranscriptPageWindowEngine.PageRows;
    public const int LoadMarginPx = Nexus.Conversation.Windowing.TranscriptPageWindowEngine.LoadMarginPx;

    public static int OpeningFrom(int count) =>
        Nexus.Conversation.Windowing.TranscriptPageWindowEngine.OpeningFrom(count);

    public static int EarlierFrom(int from) =>
        Nexus.Conversation.Windowing.TranscriptPageWindowEngine.EarlierFrom(from);

    public static bool ShouldDrawEarlier(int from, double scrollTop, double scrollHeight) =>
        Nexus.Conversation.Windowing.TranscriptPageWindowEngine.ShouldDrawEarlier(from, scrollTop, scrollHeight);
}


/// <summary>
/// Pre-Flight Checklist Contract & Strict State Machine Gatekeeper (v32):
/// Combines browser-use PlanItem/Judge verification with OpenBot-main tool-activity.tsx, browser-activity.tsx, and research-results.tsx.
/// </summary>
public sealed record PlannerSubGoal(
    string Op,
    string? Target = null,
    string? Text = null,
    bool Completed = false);

public sealed class AgentTaskChecklistContract
{
    public string OriginalUserTask { get; }
    public string CleanSearchTopic { get; private set; }
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
            @"\s*(?:,\s*)?(?:memilih\s+bagian\b.*|pilih\s+bagian\b.*|klik\s+bagian\b.*|buka\s+bagian\b.*|pilih\s+episode\b.*|klik\s+episode\b.*|memilih\s+episode\b.*|pilih\s+video\b.*|klik\s+video\b.*|video\s+paling\s+atas\b.*|yang\s+paling\s+atas\b.*|di\s+(?:kolom\s+|kotak\s+)?(?:pencarian|pecarian|telusuri)\b.*|play\b.*|putar\b.*|mainkan\b.*|tonton\b.*|tunggu\s+(?:hingga|sampai|sebentar|beberapa).*|hingga\s+muncul.*|sampai\s+muncul.*|klik\s+lewati.*|lewati\s+iklan.*|klik\s+videonya.*|ke\s+menit\s+\d+.*|di\s+menit\s+\d+.*|dan\s+klik.*|lalu\s+klik.*|dan\s+play\b.*|lalu\s+play\b.*)$",
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
        bool wantsPlayOrClickTopResult,
        IReadOnlyList<string>? orderedTargetSites = null)
    {
        string cleanQuery = !string.IsNullOrWhiteSpace(rawSearchTopic)
            ? CleanSearchQueryFromCommandVerbs(rawSearchTopic)
            : (string.IsNullOrWhiteSpace(targetSiteUrl) ? CleanSearchQueryFromCommandVerbs(rawTask) : string.Empty);
        bool hasAdSkip = DetectsConditionalAdSkipIntent(rawTask);

        if (VerifiedCarryForwardStore.IsCrossTurnContinuationPrompt(rawTask) &&
            !VerifiedCarryForwardStore.TryParseCandidateOrdinalFromPrompt(rawTask, out _) &&
            string.IsNullOrWhiteSpace(targetSiteUrl))
        {
            var contContract = new AgentTaskChecklistContract(rawTask, rawTask, false);
            bool isFormFillCont = Regex.IsMatch(rawTask, @"\b(gunakan\s+informasi\s+tadi|pakai\s+(?:informasi|data)\s+tadi|mengisi\s+formulir\s+ini|isi\s+formulir\s+ini)\b", RegexOptions.IgnoreCase);
            contContract.Steps.Add(new ChecklistStepItem
            {
                StepNumber = 1,
                ActionBadge = isFormFillCont ? "INPUT" : "EXTRACT",
                Description = isFormFillCont
                    ? "Mengisi formulir dengan informasi terverifikasi dari halaman sebelumnya"
                    : "Mengekstrak & menyalin informasi dari halaman aktif",
                ExpectedTarget = rawTask,
                Status = ChecklistStepStatus.Running
            });
            return contContract;
        }

        var contract = new AgentTaskChecklistContract(rawTask, cleanQuery, hasAdSkip);
        int stepNo = 1;

        if (orderedTargetSites is { Count: > 1 })
        {
            for (int si = 0; si < orderedTargetSites.Count; si++)
            {
                string sUrl = orderedTargetSites[si];
                if (string.IsNullOrWhiteSpace(sUrl)) continue;
                contract.Steps.Add(new ChecklistStepItem
                {
                    StepNumber = stepNo++,
                    ActionBadge = "NAVIGATE",
                    Description = $"Membuka halaman utama `{sUrl}`",
                    ExpectedTarget = sUrl,
                    Status = stepNo == 2 ? ChecklistStepStatus.Running : ChecklistStepStatus.Pending
                });
            }
        }
        else
        {
            string siteLabel = !string.IsNullOrWhiteSpace(targetSiteUrl) ? targetSiteUrl! : "https://www.google.com";
            contract.Steps.Add(new ChecklistStepItem
            {
                StepNumber = stepNo++,
                ActionBadge = "NAVIGATE",
                Description = $"Membuka halaman utama `{siteLabel}`",
                ExpectedTarget = siteLabel,
                Status = ChecklistStepStatus.Running
            });
        }

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

    public static AgentTaskChecklistContract BuildContractFromPlannerSubGoals(
        string rawTask,
        IReadOnlyList<PlannerSubGoal> subGoals)
    {
        bool hasAdSkip = DetectsConditionalAdSkipIntent(rawTask);
        string firstInputTopic = subGoals?
            .FirstOrDefault(g => string.Equals(BrowserUseDomEngine.NormalizePlannerSubGoalOp(g.Op), "input", StringComparison.OrdinalIgnoreCase))
            ?.Text ?? string.Empty;
        var contract = new AgentTaskChecklistContract(rawTask, CleanSearchQueryFromCommandVerbs(firstInputTopic), hasAdSkip);
        contract.SyncContractFromPlannerSubGoals(subGoals);
        return contract;
    }

    public void SyncContractFromPlannerSubGoals(IReadOnlyList<PlannerSubGoal>? subGoals)
    {
        if (subGoals is null || subGoals.Count == 0)
            return;

        var completedSnapshots = Steps
            .Where(s => s.Status == ChecklistStepStatus.Completed)
            .Select(s => (s.ActionBadge, s.ExpectedTarget, s.VerificationNote, s.VerifiedUrl))
            .ToList();

        Steps.Clear();
        int stepNo = 1;
        int usedCompletedCount = 0;

        foreach (var rawGoal in subGoals)
        {
            string op = BrowserUseDomEngine.NormalizePlannerSubGoalOp(rawGoal.Op);
            string badge;
            string expected;
            string desc;

            if (op == "navigate")
            {
                badge = "NAVIGATE";
                expected = !string.IsNullOrWhiteSpace(rawGoal.Target) ? rawGoal.Target! : (rawGoal.Text ?? "https://www.google.com");
                desc = $"Membuka halaman utama `{expected}`";
            }
            else if (op == "input")
            {
                badge = "INPUT";
                string cleanTxt = CleanSearchQueryFromCommandVerbs(rawGoal.Text ?? rawGoal.Target);
                if (!string.IsNullOrWhiteSpace(cleanTxt))
                    CleanSearchTopic = cleanTxt;
                expected = !string.IsNullOrWhiteSpace(cleanTxt) ? cleanTxt : (rawGoal.Text ?? rawGoal.Target ?? string.Empty);
                desc = $"Mengetik \"{expected}\" di kolom pencarian/input & menekan Enter";
            }
            else if (op is "click" or "click_and_play")
            {
                badge = "CLICK";
                expected = rawGoal.Target ?? rawGoal.Text ?? CleanSearchTopic;
                desc = op == "click_and_play"
                    ? $"Mengklik & memutar \"{(string.IsNullOrWhiteSpace(expected) ? "konten utama" : expected)}\" serta memverifikasi halaman detail terbuka"
                    : $"Mengklik hasil/konten \"{(string.IsNullOrWhiteSpace(expected) ? "utama" : expected)}\" & memverifikasi halaman detail terbuka";
            }
            else if (op == "select")
            {
                badge = "SELECT";
                expected = rawGoal.Text ?? rawGoal.Target ?? string.Empty;
                desc = $"Memilih opsi \"{expected}\" pada dropdown";
            }
            else if (op == "scroll")
            {
                badge = "SCROLL";
                expected = rawGoal.Target ?? rawGoal.Text ?? "down";
                desc = $"Menggulir halaman ({expected})";
            }
            else
            {
                badge = "EXTRACT";
                expected = rawGoal.Target ?? rawGoal.Text ?? string.Empty;
                desc = "Mengekstrak & merangkum informasi dari halaman";
            }

            bool isAlreadyDone = rawGoal.Completed;
            string? verNote = null;
            string? verUrl = null;
            if (!isAlreadyDone && usedCompletedCount < completedSnapshots.Count &&
                string.Equals(completedSnapshots[usedCompletedCount].ActionBadge, badge, StringComparison.OrdinalIgnoreCase))
            {
                isAlreadyDone = true;
                verNote = completedSnapshots[usedCompletedCount].VerificationNote;
                verUrl = completedSnapshots[usedCompletedCount].VerifiedUrl;
                usedCompletedCount++;
            }

            Steps.Add(new ChecklistStepItem
            {
                StepNumber = stepNo++,
                ActionBadge = badge,
                Description = desc,
                ExpectedTarget = expected,
                Status = isAlreadyDone ? ChecklistStepStatus.Completed : ChecklistStepStatus.Pending,
                VerificationNote = verNote ?? string.Empty,
                VerifiedUrl = verUrl ?? string.Empty
            });
        }

        if (HasConditionalAdSkip)
        {
            Steps.Add(new ChecklistStepItem
            {
                StepNumber = stepNo++,
                ActionBadge = "SHIELD",
                Description = "Memantau tombol \"Lewati Iklan\" (Skip Ad) & memastikan video berputar tanpa gangguan",
                ExpectedTarget = "skip_ad",
                IsConditionalAdSkip = true,
                Status = ChecklistStepStatus.Pending
            });
        }

        var firstPending = Steps.FirstOrDefault(s => s.Status == ChecklistStepStatus.Pending && !s.IsConditionalAdSkip);
        if (firstPending is not null)
        {
            firstPending.Status = ChecklistStepStatus.Running;
        }
    }

    public SupervisorGateDecision EvaluatePreActionGate(
        string proposedAction,
        string? targetUrlOrText,
        string? currentBrowserUrl)
    {
        string normAction = (proposedAction ?? string.Empty).Trim().ToLowerInvariant();
        string curUrl = (currentBrowserUrl ?? string.Empty).Trim();

        var allNavSteps = Steps.Where(s => s.ActionBadge == "NAVIGATE").ToList();
        var pendingNavSteps = allNavSteps.Where(s => s.Status != ChecklistStepStatus.Completed).ToList();
        var inputStep = Steps.FirstOrDefault(s => s.ActionBadge == "INPUT");

        // Gate 0 (Sequential Multi-Site Contract Enforcement):
        // When the user explicitly requested visiting multiple sites in order (e.g. "buka google, buka youtube, cari ..."),
        // ensure each pending NAVIGATE step is visited in sequence before skipping ahead to a later site or typing on the earlier site!
        if (allNavSteps.Count >= 2 && pendingNavSteps.Count > 0)
        {
            string expectedSite = pendingNavSteps[0].ExpectedTarget;
            if (normAction is "go_to_url" or "navigate")
            {
                if (!string.IsNullOrWhiteSpace(targetUrlOrText) &&
                    !IsSameRootHost(expectedSite, targetUrlOrText!) &&
                    pendingNavSteps.Skip(1).Any(s => IsSameRootHost(s.ExpectedTarget, targetUrlOrText!)))
                {
                    return new SupervisorGateDecision(
                        IsAllowed: true,
                        EffectiveActionName: "go_to_url",
                        SanitizedQueryOrUrl: expectedSite,
                        BlockReason: $"Menjalankan urutan navigasi kontrak: membuka {expectedSite} terlebih dahulu.");
                }
            }
            else if (normAction is "input_text" or "search_web" or "fill")
            {
                if (!string.IsNullOrWhiteSpace(expectedSite) && !IsSameRootHost(curUrl, expectedSite))
                {
                    return new SupervisorGateDecision(
                        IsAllowed: false,
                        EffectiveActionName: "go_to_url",
                        SanitizedQueryOrUrl: expectedSite,
                        BlockReason: $"Menjalankan urutan navigasi kontrak: membuka {expectedSite} sebelum mengetik pencarian.");
                }
            }
        }

        // Gate 1: Prevent duplicate navigation to the exact same root site when its NAVIGATE step is already completed!
        if (normAction is "go_to_url" or "navigate")
        {
            if (allNavSteps.Any(s => s.Status == ChecklistStepStatus.Completed) &&
                !string.IsNullOrWhiteSpace(curUrl) &&
                !string.IsNullOrWhiteSpace(targetUrlOrText) &&
                IsSameRootHost(curUrl, targetUrlOrText!) &&
                Uri.TryCreate(targetUrlOrText, UriKind.Absolute, out var dupTargetUri) &&
                (string.IsNullOrEmpty(dupTargetUri.PathAndQuery) || dupTargetUri.PathAndQuery == "/"))
            {
                if (pendingNavSteps.Count > 0 && !IsSameRootHost(curUrl, pendingNavSteps[0].ExpectedTarget))
                {
                    return new SupervisorGateDecision(
                        IsAllowed: true,
                        EffectiveActionName: "go_to_url",
                        SanitizedQueryOrUrl: pendingNavSteps[0].ExpectedTarget,
                        BlockReason: $"Melanjutkan ke situs berikutnya pada kontrak: {pendingNavSteps[0].ExpectedTarget}.");
                }
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

    public bool StateDeltaVerified { get; private set; }
    public bool RequiresMediaPlaybackVerification =>
        Regex.IsMatch(OriginalUserTask ?? string.Empty, @"\b(play|putar|tonton|nonton|dengarkan|video\s+paling\s+atas)\b", RegexOptions.IgnoreCase) ||
        Steps.Any(s => s.Description.Contains("memutar", StringComparison.OrdinalIgnoreCase));
    public bool MediaPlaybackVerified { get; private set; }
    public double VerifiedMediaCurrentTimeSeconds { get; private set; }
    public bool GoalVerified =>
        Steps.Count > 0 &&
        Steps.Where(s => !s.IsConditionalAdSkip).All(s => s.Status == ChecklistStepStatus.Completed);
    public bool UnverifiedDoneVeto { get; private set; }
    public int UnverifiedDoneVetoCount { get; private set; }

    public void RecordMediaPlaybackVerification(bool isPlayingOrAdvanced, double currentTimeSeconds)
    {
        if (isPlayingOrAdvanced || currentTimeSeconds > 0)
        {
            MediaPlaybackVerified = true;
            if (currentTimeSeconds > VerifiedMediaCurrentTimeSeconds)
                VerifiedMediaCurrentTimeSeconds = currentTimeSeconds;
        }
    }

    public static string MapActionToStagehandPrimitive(string? actionName)
    {
        string norm = (actionName ?? string.Empty).Trim().ToLowerInvariant();
        return norm switch
        {
            "copy_text" or "search_page" or "find_elements" or "extract" => "extract",
            "wait" or "done" or "observe" => "observe",
            _ => "act"
        };
    }

    public PostconditionDeltaEvaluation EvaluatePostconditionDelta(
        string? actionName,
        bool urlChanged,
        bool domOrInputChanged,
        bool mediaPlaybackVerified = false,
        bool isCandidateDoneAction = false)
    {
        if (mediaPlaybackVerified)
        {
            MediaPlaybackVerified = true;
        }

        string primitive = MapActionToStagehandPrimitive(actionName);
        bool effectiveMediaOk = mediaPlaybackVerified || MediaPlaybackVerified;
        bool deltaOk = urlChanged || domOrInputChanged || effectiveMediaOk || primitive == "extract";
        StateDeltaVerified = deltaOk;

        bool allRequiredStepsDone = GoalVerified;
        bool vetoDone = isCandidateDoneAction && !allRequiredStepsDone;
        UnverifiedDoneVeto = vetoDone;
        if (vetoDone)
        {
            UnverifiedDoneVetoCount++;
        }

        string evidence = deltaOk
            ? $"[{primitive.ToUpperInvariant()}] StateDeltaVerified=true (urlChanged={urlChanged}, domOrInput={domOrInputChanged}, media={effectiveMediaOk}, currentTime={VerifiedMediaCurrentTimeSeconds:0.##}s)"
            : $"[{primitive.ToUpperInvariant()}] StateDeltaVerified=false (tidak ada perubahan state teramati)";
        return new PostconditionDeltaEvaluation(
            StagehandPrimitive: primitive,
            StateDeltaVerified: deltaOk,
            GoalVerified: allRequiredStepsDone,
            UnverifiedDoneVeto: vetoDone,
            EvidenceSummary: evidence);
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
    /// and the verified Source/Media Card without any duplicate bullet wall or malformed host items.
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

        bool stoppedPrematurely = isFinished && completedCount < totalCount;
        string headerIcon = stoppedPrematurely ? "⚠" : "✓";
        string badge = completedCount >= totalCount
            ? $"`{completedCount}/{totalCount} selesai ✓`"
            : (stoppedPrematurely ? $"`⚠ {completedCount}/{totalCount} berhenti prematur`" : $"`{completedCount}/{totalCount} berjalan ●`");

        sb.AppendLine($"> **{headerIcon} {taskTitle}** · {badge}");
        sb.AppendLine(">");

        bool runningStepEmitted = false;
        foreach (var step in Steps)
        {
            if (step.Status == ChecklistStepStatus.AutoSkippedByAdGuard)
            {
                sb.AppendLine($"> 🛡️ ~~{step.Description}~~ · **`[✓ Dilewati Otomatis oleh AdGuard Shield]`**");
            }
            else if (step.Status == ChecklistStepStatus.Completed)
            {
                string linkPart = BrowserUseDomEngine.IsValidNavigatingOrSourceUrl(step.VerifiedUrl)
                    ? $" · [Buka]({step.VerifiedUrl})"
                    : string.Empty;
                string cleanNote = (step.VerificationNote ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("‹", "[").Replace("›", "]").Trim();
                string notePart = !string.IsNullOrWhiteSpace(cleanNote)
                    ? $" · ‹{cleanNote}›"
                    : string.Empty;
                sb.AppendLine($"> ✓ ~~{step.Description}~~{linkPart}{notePart}");
            }
            else if (step.Status == ChecklistStepStatus.Running && !runningStepEmitted)
            {
                runningStepEmitted = true;
                string linkPart = BrowserUseDomEngine.IsValidNavigatingOrSourceUrl(step.VerifiedUrl)
                    ? $" · [Buka]({step.VerifiedUrl})"
                    : string.Empty;
                sb.AppendLine($"> ● **{step.Description}**{linkPart}");
            }
            else if (isFinished && !step.IsConditionalAdSkip)
            {
                sb.AppendLine($"> ○ {step.Description} *(belum dijalankan)*");
            }
        }

        if (primarySource is not null &&
            BrowserUseDomEngine.IsValidNavigatingOrSourceUrl(primarySource.Url))
        {
            string domain = "Web";
            if (Uri.TryCreate(primarySource.Url, UriKind.Absolute, out var u))
                domain = u.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);

            sb.AppendLine();
            sb.AppendLine($"**[{primarySource.Title}]({primarySource.Url})** (`{domain}`) · [Buka di Browser]({primarySource.Url})");
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

        if (primarySource is not null && BrowserUseDomEngine.IsValidNavigatingOrSourceUrl(primarySource.Url))
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

public sealed record PostconditionDeltaEvaluation(
    string StagehandPrimitive,
    bool StateDeltaVerified,
    bool GoalVerified,
    bool UnverifiedDoneVeto,
    string EvidenceSummary);

public sealed record CarryForwardFactItem(
    string KeyOrLabel,
    string Value,
    string SourceUrl,
    DateTimeOffset VerifiedAtUtc);

public sealed record CarryForwardCandidateLink(
    int ItemNumber,
    string Title,
    string Url,
    string SourcePageUrl);

/// <summary>
/// Batch B (v55 Rev 24): Cross-Turn Continuity Store (VerifiedCarryForwardStore).
/// Stores compact, provenance-backed facts, numbered candidate links ([1]..[10]), and active URL/title across chat turns
/// without retaining raw DOM trees. Bounded to <= 750 chars when serialized into <carry_forward_context>.
/// </summary>
public sealed class VerifiedCarryForwardStore
{
    private readonly object _sync = new();
    private readonly List<CarryForwardFactItem> _extractedFacts = new();
    private readonly List<CarryForwardCandidateLink> _numberedCandidates = new();

    public string LastActiveUrl { get; set; } = string.Empty;
    public string LastActiveTitle { get; set; } = string.Empty;
    public string LastTaskSummary { get; set; } = string.Empty;
    public string LastCopiedText { get; set; } = string.Empty;
    public string LastSavedWorkspaceFilePath { get; set; } = string.Empty;

    public IReadOnlyList<CarryForwardFactItem> ExtractedFacts
    {
        get
        {
            lock (_sync)
            {
                return _extractedFacts.ToList().AsReadOnly();
            }
        }
    }

    public IReadOnlyList<CarryForwardCandidateLink> NumberedCandidates
    {
        get
        {
            lock (_sync)
            {
                return _numberedCandidates.ToList().AsReadOnly();
            }
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            LastActiveUrl = string.Empty;
            LastActiveTitle = string.Empty;
            LastTaskSummary = string.Empty;
            LastCopiedText = string.Empty;
            LastSavedWorkspaceFilePath = string.Empty;
            _extractedFacts.Clear();
            _numberedCandidates.Clear();
        }
    }

    public void RecordExtractedFact(string keyOrLabel, string value, string? sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        string cleanKey = string.IsNullOrWhiteSpace(keyOrLabel) ? "Fact" : keyOrLabel.Trim();
        if (cleanKey.Length > 48)
            cleanKey = cleanKey[..48];

        string cleanVal = Regex.Replace(value.Trim(), @"\s+", " ");
        if (cleanVal.Length > 240)
            cleanVal = cleanVal[..240] + "...";

        string cleanSrc = (sourceUrl ?? LastActiveUrl ?? string.Empty).Trim();

        lock (_sync)
        {
            _extractedFacts.RemoveAll(f =>
                string.Equals(f.KeyOrLabel, cleanKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.Value, cleanVal, StringComparison.OrdinalIgnoreCase));
            _extractedFacts.Insert(0, new CarryForwardFactItem(
                cleanKey,
                cleanVal,
                cleanSrc,
                DateTimeOffset.UtcNow));
            while (_extractedFacts.Count > 12)
                _extractedFacts.RemoveAt(_extractedFacts.Count - 1);
        }
    }

    public void RecordNumberedCandidates(IEnumerable<(string Title, string Url)>? items, string? sourcePageUrl)
    {
        if (items is null)
            return;

        string srcPage = (sourcePageUrl ?? LastActiveUrl ?? string.Empty).Trim();
        lock (_sync)
        {
            _numberedCandidates.Clear();
            int num = 1;
            foreach (var (title, url) in items)
            {
                if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (_numberedCandidates.Any(c => string.Equals(c.Url, url, StringComparison.OrdinalIgnoreCase)))
                    continue;

                string cleanTitle = Regex.Replace(title ?? url, @"\s+", " ").Trim();
                if (cleanTitle.Length > 110)
                    cleanTitle = cleanTitle[..110] + "...";

                _numberedCandidates.Add(new CarryForwardCandidateLink(num++, cleanTitle, url.Trim(), srcPage));
                if (num > 10)
                    break;
            }
        }
    }

    public static bool TryParseCandidateOrdinalFromPrompt(string? userTask, out int ordinalNumber)
    {
        ordinalNumber = 0;
        if (string.IsNullOrWhiteSpace(userTask))
            return false;

        string lower = userTask.Trim().ToLowerInvariant();
        // Exclude episode / chapter / minute references so "episode 2" is never confused with "hasil nomor 2"
        if (Regex.IsMatch(lower, @"\b(episode|eps|ep|bab|part|season|menit|detik)\s*0*\d+\b"))
            return false;

        var m = Regex.Match(
            lower,
            @"\b(?:hasil|nomor|no\.?|urutan|tautan|link|item|pilihan|video|artikel|baris|opsi)\s*(?:nomor|no\.?|ke-|ke|urutan)?\s*(10|[1-9]|satu|pertama|kesatu|dua|kedua|tiga|ketiga|empat|keempat|lima|kelima|enam|keenam|tujuh|ketujuh|delapan|kedelapan|sembilan|kesembilan|sepuluh|kesepuluh)\b|\byang\s+(pertama|kesatu|kedua|ketiga|keempat|kelima|ke-(?:10|[1-9]))\b",
            RegexOptions.IgnoreCase);
        if (!m.Success)
            return false;

        string token = (!string.IsNullOrWhiteSpace(m.Groups[1].Value) ? m.Groups[1].Value : m.Groups[2].Value)
            .Trim()
            .ToLowerInvariant()
            .Replace("ke-", string.Empty, StringComparison.Ordinal);

        if (int.TryParse(token, out int n) && n >= 1 && n <= 10)
        {
            ordinalNumber = n;
            return true;
        }

        ordinalNumber = token switch
        {
            "satu" or "pertama" or "kesatu" => 1,
            "dua" or "kedua" => 2,
            "tiga" or "ketiga" => 3,
            "empat" or "keempat" => 4,
            "lima" or "kelima" => 5,
            "enam" or "keenam" => 6,
            "tujuh" or "ketujuh" => 7,
            "delapan" or "kedelapan" => 8,
            "sembilan" or "kesembilan" => 9,
            "sepuluh" or "kesepuluh" => 10,
            _ => 0
        };
        return ordinalNumber >= 1 && ordinalNumber <= 10;
    }

    public bool TryResolveNumberedCandidateFromPrompt(string? userTask, out CarryForwardCandidateLink? matched)
    {
        matched = null;
        if (!TryParseCandidateOrdinalFromPrompt(userTask, out int num))
            return false;

        lock (_sync)
        {
            matched = _numberedCandidates.FirstOrDefault(c => c.ItemNumber == num);
            return matched is not null;
        }
    }

    public static bool IsCrossTurnContinuationPrompt(string? userTask)
    {
        if (string.IsNullOrWhiteSpace(userTask))
            return false;

        string lower = userTask.Trim().ToLowerInvariant();
        if (TryParseCandidateOrdinalFromPrompt(lower, out _))
            return true;

        return Regex.IsMatch(
            lower,
            @"\b(dari\s+halaman\s+(?:itu|ini|tersebut|tadi)|di\s+halaman\s+(?:itu|ini|tersebut|tadi)|gunakan\s+informasi\s+tadi|pakai\s+(?:informasi|data)\s+tadi|mengisi\s+formulir\s+ini|isi\s+formulir\s+ini|periksa\s+dulu\s*,?\s*jangan\s+kirim|jangan\s+kirim\s+dulu|jangan\s+submit\s+dulu|salin\s+hasilnya|simpan\s+(?:hasilnya\s+)?ke\s+file|ambil\s+nama\s*,?\s*alamat)\b",
            RegexOptions.IgnoreCase);
    }

    public string FormatCarryForwardContextForPrompt(int maxChars = 750)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(LastActiveUrl) &&
                _extractedFacts.Count == 0 &&
                _numberedCandidates.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.AppendLine("<carry_forward_context>");
            if (!string.IsNullOrWhiteSpace(LastActiveUrl))
            {
                string titlePart = !string.IsNullOrWhiteSpace(LastActiveTitle) ? $" ({LastActiveTitle})" : string.Empty;
                sb.AppendLine($"LastActivePage: {LastActiveUrl}{titlePart}");
            }
            if (!string.IsNullOrWhiteSpace(LastTaskSummary))
            {
                string shortTask = LastTaskSummary.Length > 90 ? LastTaskSummary[..90] + "..." : LastTaskSummary;
                sb.AppendLine($"PreviousTurnTask: {shortTask}");
            }
            if (_extractedFacts.Count > 0)
            {
                sb.AppendLine("VerifiedFacts:");
                foreach (var f in _extractedFacts.Take(5))
                {
                    string v = f.Value.Length > 90 ? f.Value[..90] + "..." : f.Value;
                    sb.AppendLine($"- {f.KeyOrLabel}: {v}");
                }
            }
            if (_numberedCandidates.Count > 0)
            {
                sb.AppendLine("NumberedCandidates:");
                foreach (var c in _numberedCandidates.Take(5))
                {
                    string t = c.Title.Length > 55 ? c.Title[..55] + "..." : c.Title;
                    sb.AppendLine($"- #[{c.ItemNumber}] {t} -> {c.Url}");
                }
            }
            sb.Append("</carry_forward_context>");

            string built = sb.ToString();
            if (built.Length <= maxChars)
                return built;

            int bodyBudget = Math.Max(64, maxChars - "</carry_forward_context>".Length - 6);
            return built[..bodyBudget].TrimEnd() + "\r\n...\r\n</carry_forward_context>";
        }
    }
}


public sealed record CanvasExtractedArtifact(
    string Title,
    string FileNameHint,
    string LanguageOrFormat,
    string RawCodeOrMarkdown,
    string CleanPreviewMarkdown,
    string PreviewHeadline,
    string PreviewSubtitle,
    string HighlightMetricValue,
    string HighlightMetricCaption,
    IReadOnlyList<IReadOnlyList<string>> TableRows,
    bool DefaultToPreviewMode);

public sealed record CanvasExportResult(
    bool IsSuccess,
    string Format,
    string FullFilePath,
    string FileName,
    long FileSizeBytes,
    string Sha256Hex,
    string SummaryMessage);

/// <summary>
/// Batch E.2: Zero-Overhead Local & Browser Canvas Extractor and Multi-Format Exporter (.pdf, .xlsx, .md/.cmd/.cs/.txt).
/// Parses structured reports, web tables, and code blocks in < 2ms without extra LLM token generation.
/// </summary>
public static class NexusCanvasExporterAndExtractor
{
    public static bool IsBrowserAgentStatusMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string trimmed = text.TrimStart();
        return trimmed.StartsWith("> **✓ NEXUS Browser Agent", StringComparison.Ordinal) ||
               trimmed.StartsWith("> **● NEXUS Browser Agent", StringComparison.Ordinal) ||
               trimmed.StartsWith("> **▸ NEXUS Browser Agent", StringComparison.Ordinal) ||
               trimmed.StartsWith("> **⚠ NEXUS Browser Agent", StringComparison.Ordinal) ||
               trimmed.Contains("### Kartu Sumber Terverifikasi", StringComparison.Ordinal);
    }

    public static string StripBrowserAgentStatusAndSourceTable(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;
        var kept = new List<string>();
        bool inSourceCardSection = false;
        foreach (var line in rawText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            string t = line.TrimStart();
            if (t.StartsWith(">", StringComparison.Ordinal))
                continue;
            if (t.StartsWith("### Kartu Sumber Terverifikasi", StringComparison.OrdinalIgnoreCase))
            {
                inSourceCardSection = true;
                continue;
            }
            if (inSourceCardSection)
            {
                if (t.StartsWith("|", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(t))
                    continue;
                inSourceCardSection = false;
            }
            if (t.StartsWith("• **Langkah ", StringComparison.Ordinal) ||
                t.StartsWith("• **Supervisor ", StringComparison.Ordinal) ||
                t.StartsWith("• **Navigasi Waktu Video", StringComparison.Ordinal))
            {
                continue;
            }
            kept.Add(line);
        }
        return string.Join(Environment.NewLine, kept).Trim();
    }

    /// <summary>
    /// Single-Surface Rule (Anti-Dobel): Removes the fenced code block or structured report body from the chat bubble
    /// whenever HasCanvasCard is active so the code/document is rendered ONLY ONCE inside the Canvas card.
    /// </summary>
    public static string StripCanvasBodyFromChatMarkdown(string? rawMarkdown, CanvasExtractedArtifact? artifact)
    {
        if (string.IsNullOrWhiteSpace(rawMarkdown) || artifact is null)
            return rawMarkdown ?? string.Empty;

        string text = rawMarkdown.Trim();

        // 1. If the message has a closed or unclosed code fence, strip the fence from the chat bubble and keep only the intro/outro prose
        var closedFence = Regex.Match(text, @"```[a-zA-Z0-9_+#.-]*\s*\r?\n[\s\S]*?```", RegexOptions.Multiline);
        if (closedFence.Success)
        {
            string withoutFence = (text[..closedFence.Index] + "\n" + text[(closedFence.Index + closedFence.Length)..]).Trim();
            return string.IsNullOrWhiteSpace(withoutFence)
                ? $"Berikut berkas **{artifact.Title}** yang telah disusun di Canvas:"
                : withoutFence;
        }

        var openFence = Regex.Match(text, @"```[a-zA-Z0-9_+#.-]*\s*\r?\n[\s\S]*$", RegexOptions.Multiline);
        if (openFence.Success)
        {
            string beforeFence = text[..openFence.Index].Trim();
            return string.IsNullOrWhiteSpace(beforeFence)
                ? $"Sedang menyusun **{artifact.Title}** di Canvas..."
                : beforeFence;
        }

        // 2. If it's a full-message structured report/PRD/table, keep only the first introductory paragraph or a concise 1-line summary
        var paragraphs = text.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        var introPara = paragraphs.FirstOrDefault(p =>
            !p.StartsWith("#", StringComparison.Ordinal) &&
            !p.StartsWith("|", StringComparison.Ordinal) &&
            !p.StartsWith("-", StringComparison.Ordinal) &&
            !p.StartsWith("*", StringComparison.Ordinal) &&
            p.Length <= 260);
        if (!string.IsNullOrWhiteSpace(introPara) && paragraphs.Count >= 2)
        {
            return introPara!;
        }

        return $"Berikut dokumen **{artifact.Title}** yang telah disusun di Canvas:";
    }

    public static CanvasExtractedArtifact? TryDetectOrExtractCanvasArtifact(
        string? rawMarkdownOrDomText,
        string? fallbackTitle = null,
        bool allowStreamingPartial = false)
    {
        if (string.IsNullOrWhiteSpace(rawMarkdownOrDomText))
            return null;

        string text = rawMarkdownOrDomText.Trim();

        // Rule: NEVER trigger a Canvas card on NEXUS Browser Agent status / Kartu Sumber Terverifikasi cards!
        if (IsBrowserAgentStatusMessage(text))
        {
            text = StripBrowserAgentStatusAndSourceTable(text);
            if (string.IsNullOrWhiteSpace(text))
                return null;
        }

        if (!allowStreamingPartial && text.Length < 110 && !text.Contains("```", StringComparison.Ordinal) && !text.Contains("|", StringComparison.Ordinal))
            return null;

        // 1. Check for a closed fenced code/script block (or an open streaming code fence when allowStreamingPartial == true)
        var fenceMatch = Regex.Match(
            text,
            @"```([a-zA-Z0-9_+#.-]*)\s*\r?\n([\s\S]*?)```",
            RegexOptions.Multiline);
        if (!fenceMatch.Success && allowStreamingPartial)
        {
            fenceMatch = Regex.Match(
                text,
                @"```([a-zA-Z0-9_+#.-]*)\s*\r?\n([\s\S]+)$",
                RegexOptions.Multiline);
        }
        if (fenceMatch.Success)
        {
            string lang = fenceMatch.Groups[1].Value.Trim().ToLowerInvariant();
            string codeBody = fenceMatch.Groups[2].Value.Trim('\r', '\n');
            int lineCount = codeBody.Split('\n').Length;
            if ((allowStreamingPartial && lineCount >= 2) || lineCount >= 3 || codeBody.Length >= 80)
            {
                string ext = lang switch
                {
                    "cmd" or "bat" or "batch" => ".cmd",
                    "ps1" or "powershell" => ".ps1",
                    "cs" or "csharp" or "c#" => ".cs",
                    "py" or "python" => ".py",
                    "js" or "javascript" => ".js",
                    "ts" or "typescript" => ".ts",
                    "json" => ".json",
                    "html" or "htm" => ".html",
                    "xml" or "xaml" => ".xaml",
                    "sql" => ".sql",
                    "md" or "markdown" => ".md",
                    _ => ".txt"
                };

                string detectedFileName = TryExtractFileNameHint(text, codeBody, ext, fallbackTitle);
                bool isDocFence = lang is "md" or "markdown";
                var tableRowsFromFence = ExtractMarkdownTableRows(codeBody);
                var (headline, subtitle, metricVal, metricCap, bodyPreview) = ParseEditorialPreviewFields(
                    isDocFence ? codeBody : text,
                    detectedFileName,
                    lineCount,
                    tableRowsFromFence.Count);

                return new CanvasExtractedArtifact(
                    Title: detectedFileName,
                    FileNameHint: SanitizeFileName(detectedFileName, ext),
                    LanguageOrFormat: string.IsNullOrWhiteSpace(lang) ? "code" : lang,
                    RawCodeOrMarkdown: codeBody,
                    CleanPreviewMarkdown: bodyPreview,
                    PreviewHeadline: headline,
                    PreviewSubtitle: subtitle,
                    HighlightMetricValue: metricVal,
                    HighlightMetricCaption: metricCap,
                    TableRows: tableRowsFromFence,
                    DefaultToPreviewMode: isDocFence);
            }
        }

        // 2. Check for structured Markdown report / PRD / article / extracted multi-row web table (require >= 3 table rows so 2-row source cards never match)
        var tableRows = ExtractMarkdownTableRows(text);
        bool hasHeadings = Regex.IsMatch(text, @"^\s*#{1,3}\s+\S+", RegexOptions.Multiline);
        bool hasBulletList = Regex.Matches(text, @"^\s*[-*•]\s+\S+", RegexOptions.Multiline).Count >= 3;

        if (tableRows.Count >= 3 || (hasHeadings && (hasBulletList || text.Length >= 220)))
        {
            int lineCount = text.Split('\n').Length;
            string docTitle = TryExtractFirstHeadingTitle(text) ?? fallbackTitle ?? "Laporan & Ekstraksi Terstruktur NEXUS";
            var (headline, subtitle, metricVal, metricCap, bodyPreview) = ParseEditorialPreviewFields(
                text,
                docTitle,
                lineCount,
                tableRows.Count);

            string ext = tableRows.Count >= 3 ? ".xlsx" : ".pdf";
            return new CanvasExtractedArtifact(
                Title: docTitle,
                FileNameHint: SanitizeFileName(docTitle, ext),
                LanguageOrFormat: tableRows.Count >= 3 ? "table" : "markdown",
                RawCodeOrMarkdown: text,
                CleanPreviewMarkdown: bodyPreview,
                PreviewHeadline: headline,
                PreviewSubtitle: subtitle,
                HighlightMetricValue: metricVal,
                HighlightMetricCaption: metricCap,
                TableRows: tableRows,
                DefaultToPreviewMode: true);
        }

        return null;
    }

    public sealed record CodexTimelineStepRecord(
        string IconBadge,
        string StepTitle,
        string StepDetailText,
        bool IsCompleted,
        string? FaviconImageUrl = null);

    public static string? ExtractOrReconstructHeaderSummaryFromBrowserCard(string? rawBrowserCardMarkdown)
    {
        if (string.IsNullOrWhiteSpace(rawBrowserCardMarkdown) || !IsBrowserAgentStatusMessage(rawBrowserCardMarkdown))
            return null;

        int completedSteps = 0;
        int totalSteps = 0;
        foreach (var rawLine in rawBrowserCardMarkdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith(">", StringComparison.Ordinal))
                continue;
            string body = line.TrimStart('>', ' ').Trim();
            if (body.Contains("NEXUS Browser Agent", StringComparison.Ordinal))
            {
                int hdrIdx = body.IndexOf("‹HEADER:", StringComparison.Ordinal);
                if (hdrIdx >= 0)
                {
                    int endIdx = body.IndexOf('›', hdrIdx + 8);
                    if (endIdx > hdrIdx + 8)
                        return body.Substring(hdrIdx + 8, endIdx - (hdrIdx + 8)).Trim();
                }
                var mCount = Regex.Match(body, @"`(\d+)/(\d+)\s+selesai");
                if (mCount.Success)
                {
                    int.TryParse(mCount.Groups[1].Value, out completedSteps);
                    int.TryParse(mCount.Groups[2].Value, out totalSteps);
                }
                continue;
            }
            if (body.StartsWith("✓", StringComparison.Ordinal) || body.StartsWith("🛡️", StringComparison.Ordinal))
            {
                completedSteps++;
                totalSteps++;
            }
            else if (body.StartsWith("●", StringComparison.Ordinal) || body.StartsWith("○", StringComparison.Ordinal) || body.StartsWith("⚠", StringComparison.Ordinal))
            {
                totalSteps++;
            }
        }
        if (totalSteps > 0)
        {
            int done = Math.Max(1, completedSteps);
            return $"✓ Ran {done} browser actions · {done}/{Math.Max(done, totalSteps)} done";
        }
        return "✓ Ran browser actions";
    }

    public static IReadOnlyList<CodexTimelineStepRecord> ExtractCodexTimelineStepsFromBrowserCard(string? rawBrowserCardMarkdown)
    {
        var list = new List<CodexTimelineStepRecord>();
        if (string.IsNullOrWhiteSpace(rawBrowserCardMarkdown) || !IsBrowserAgentStatusMessage(rawBrowserCardMarkdown))
            return list;

        list.Add(new CodexTimelineStepRecord(
            "🖥️",
            "Memeriksa browser yang tersedia",
            "Selected Browser: NEXUS Built-in WebView2 Chromium (CDP Active)",
            true,
            FaviconImageUrl: null));

        string lastActiveHost = string.Empty;
        foreach (var rawLine in rawBrowserCardMarkdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith(">", StringComparison.Ordinal))
                continue;
            string body = line.TrimStart('>', ' ').Trim();
            if (string.IsNullOrWhiteSpace(body) || body.Contains("NEXUS Browser Agent", StringComparison.Ordinal))
                continue;

            // Sequential Step Rule: Never show pending (○) steps before the previous step has completed!
            if (body.StartsWith("○", StringComparison.Ordinal))
                continue;

            bool isDone = body.StartsWith("✓", StringComparison.Ordinal) || body.StartsWith("🛡️", StringComparison.Ordinal);
            string cleanDesc = Regex.Replace(body, @"^[✓●○⚠🛡️\s]+", string.Empty)
                .Replace("~~", string.Empty)
                .Replace("**", string.Empty)
                .Replace("`", string.Empty)
                .Replace("*(sedang dikerjakan...)*", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("*(belum dijalankan)*", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("*(belum tercapai — berhenti prematur)*", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

            string verifNote = string.Empty;
            int noteStart = cleanDesc.IndexOf(" · ‹", StringComparison.Ordinal);
            if (noteStart >= 0)
            {
                int noteEnd = cleanDesc.LastIndexOf('›');
                if (noteEnd > noteStart + 4)
                {
                    verifNote = cleanDesc.Substring(noteStart + 4, noteEnd - (noteStart + 4)).Trim();
                }
                cleanDesc = cleanDesc[..noteStart].Trim();
            }

            int bukaIdx = cleanDesc.IndexOf(" · [Buka](", StringComparison.Ordinal);
            string stepUrl = string.Empty;
            if (bukaIdx >= 0)
            {
                int endParen = cleanDesc.IndexOf(')', bukaIdx + 10);
                if (endParen > bukaIdx + 10)
                    stepUrl = cleanDesc.Substring(bukaIdx + 10, endParen - (bukaIdx + 10));
                cleanDesc = cleanDesc[..bukaIdx].Trim();
            }

            string? faviconUrl = ResolveGenericWebsiteFaviconUrl(cleanDesc, stepUrl, ref lastActiveHost);
            string iconBadge = string.IsNullOrWhiteSpace(lastActiveHost)
                ? "🌐"
                : lastActiveHost.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase)[..1].ToUpperInvariant();

            string conciseTitle = FormatHumanFriendlyCodexStepTitle(cleanDesc, stepUrl, lastActiveHost);

            var detailLines = new List<string>();
            if (!string.IsNullOrWhiteSpace(stepUrl))
                detailLines.Add($"Browser tab: 1, URL: \"{stepUrl}\"");
            detailLines.Add($"Action: {cleanDesc}");
            if (!string.IsNullOrWhiteSpace(verifNote))
                detailLines.Add(verifNote);

            list.Add(new CodexTimelineStepRecord(
                iconBadge,
                conciseTitle,
                string.Join("\n", detailLines),
                isDone,
                FaviconImageUrl: faviconUrl));

            if (isDone &&
                !string.IsNullOrWhiteSpace(verifNote) &&
                (verifNote.Contains("movie_player", StringComparison.OrdinalIgnoreCase) ||
                 verifNote.Contains("Media active", StringComparison.OrdinalIgnoreCase) ||
                 verifNote.Contains("playing=True", StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(new CodexTimelineStepRecord(
                    iconBadge,
                    "Memastikan video sedang diputar",
                    verifNote,
                    true,
                    FaviconImageUrl: faviconUrl));
            }
        }

        return list;
    }

    public static string FormatCleanSiteDisplayName(string? hostOrUrl)
    {
        if (string.IsNullOrWhiteSpace(hostOrUrl))
            return "halaman web";

        string host = hostOrUrl.Trim();
        if (Uri.TryCreate(host, UriKind.Absolute, out var u) && !string.IsNullOrWhiteSpace(u.Host))
            host = u.Host;

        host = Regex.Replace(host, @"^(?:www\.|m\.|id\.)", string.Empty, RegexOptions.IgnoreCase);
        string[] parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return hostOrUrl;

        string root = parts[0];
        if (parts.Length >= 2 && string.Equals(parts[0], "scholar", StringComparison.OrdinalIgnoreCase))
            return "Google Scholar";
        if (string.Equals(root, "youtube", StringComparison.OrdinalIgnoreCase) || string.Equals(root, "youtu", StringComparison.OrdinalIgnoreCase))
            return "YouTube";
        if (string.Equals(root, "github", StringComparison.OrdinalIgnoreCase))
            return "GitHub";
        if (string.Equals(root, "whatsapp", StringComparison.OrdinalIgnoreCase) || (parts.Length >= 2 && string.Equals(parts[1], "whatsapp", StringComparison.OrdinalIgnoreCase)))
            return "WhatsApp";
        if (string.Equals(root, "chatgpt", StringComparison.OrdinalIgnoreCase))
            return "ChatGPT";

        return char.ToUpperInvariant(root[0]) + (root.Length > 1 ? root[1..].ToLowerInvariant() : string.Empty);
    }

    public static string FormatHumanFriendlyCodexStepTitle(string cleanDesc, string stepUrl, string activeHost)
    {
        if (string.IsNullOrWhiteSpace(cleanDesc))
            return "Menjalankan aksi browser";

        string siteName = FormatCleanSiteDisplayName(!string.IsNullOrWhiteSpace(stepUrl) ? stepUrl : activeHost);

        // 1. Navigation step ("Membuka halaman utama https://www.google.com" -> "Membuka Google")
        if (cleanDesc.StartsWith("Membuka ", StringComparison.OrdinalIgnoreCase))
        {
            if (cleanDesc.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                cleanDesc.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
                cleanDesc.StartsWith("Membuka halaman", StringComparison.OrdinalIgnoreCase) ||
                cleanDesc.StartsWith("Membuka situs", StringComparison.OrdinalIgnoreCase))
            {
                return $"Membuka {siteName}";
            }
        }

        // 2. Input / Search step ("Mengetik "..." di kolom pencarian/input & menekan Enter" -> "Mencari di YouTube" / "Mencari frasa di Google")
        if (cleanDesc.StartsWith("Mengetik ", StringComparison.OrdinalIgnoreCase) ||
            cleanDesc.StartsWith("Mencari ", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(activeHost))
            {
                return $"Mencari di {siteName}";
            }
            return "Mencari di halaman";
        }

        // 3. Click step ("Mengklik "SOAL GIBRAN, MANTAN KETUA MK BICARA!..."" -> "Membuka video yang paling cocok" / "Membuka hasil yang paling cocok")
        if (cleanDesc.StartsWith("Mengklik ", StringComparison.OrdinalIgnoreCase))
        {
            string combinedUrl = (stepUrl + " " + activeHost).ToLowerInvariant();
            if (combinedUrl.Contains("/watch", StringComparison.Ordinal) ||
                combinedUrl.Contains("youtube.com", StringComparison.Ordinal) ||
                combinedUrl.Contains("youtu.be", StringComparison.Ordinal) ||
                cleanDesc.Contains("minutes", StringComparison.OrdinalIgnoreCase) ||
                cleanDesc.Contains("video", StringComparison.OrdinalIgnoreCase) ||
                cleanDesc.Contains("episode", StringComparison.OrdinalIgnoreCase))
            {
                if (combinedUrl.Contains("/@", StringComparison.Ordinal) ||
                    combinedUrl.Contains("/channel/", StringComparison.Ordinal) ||
                    combinedUrl.Contains("/c/", StringComparison.Ordinal))
                {
                    return $"Membuka kanal di {siteName}";
                }
                return "Membuka video yang paling cocok";
            }
            return "Membuka hasil yang paling cocok";
        }

        return cleanDesc.Length > 52 ? cleanDesc[..49].Trim() + "..." : cleanDesc;
    }

    /// <summary>
    /// 100% Generic Website Favicon Resolver based purely on Uri.Host (zero hardcoded website names).
    /// Extracts the domain from stepUrl or any URL inside cleanDesc, caches/CARRIES forward the active host
    /// for in-page actions (like typing or clicking on that same website), and returns its high-DPI favicon endpoint.
    /// </summary>
    public static string? ResolveGenericWebsiteFaviconUrl(string cleanDesc, string stepUrl, ref string lastActiveHost)
    {
        string candidateUrl = stepUrl;
        if (string.IsNullOrWhiteSpace(candidateUrl))
        {
            var m = Regex.Match(cleanDesc ?? string.Empty, @"https?://[^\s""'<>)]+", RegexOptions.IgnoreCase);
            if (m.Success)
                candidateUrl = m.Value;
        }
        if (!string.IsNullOrWhiteSpace(candidateUrl) &&
            Uri.TryCreate(candidateUrl, UriKind.Absolute, out var u) &&
            !string.IsNullOrWhiteSpace(u.Host) &&
            u.Scheme is "http" or "https")
        {
            lastActiveHost = u.Host;
        }
        if (!string.IsNullOrWhiteSpace(lastActiveHost))
        {
            return $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(lastActiveHost)}&sz=64";
        }
        return null;
    }

    public static IReadOnlyList<IReadOnlyList<string>> ExtractMarkdownTableRows(string? markdown)
    {
        var rows = new List<IReadOnlyList<string>>();
        if (string.IsNullOrWhiteSpace(markdown))
            return rows;

        foreach (var rawLine in markdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith("|", StringComparison.Ordinal) || !line.EndsWith("|", StringComparison.Ordinal))
                continue;

            var cells = line.Trim('|')
                .Split('|')
                .Select(c => c.Trim())
                .ToList();
            if (cells.Count == 0)
                continue;

            // Skip separator row | :--- | :--- |
            if (cells.All(c => Regex.IsMatch(c, @"^[:\-]+$")))
                continue;

            rows.Add(cells);
        }
        return rows;
    }

    private static (string Headline, string Subtitle, string MetricVal, string MetricCaption, string BodyText) ParseEditorialPreviewFields(
        string docText,
        string fallbackTitle,
        int totalLines,
        int tableRowCount)
    {
        var lines = docText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        string headline = fallbackTitle;
        string subtitle = string.Empty;
        var bodyLines = new List<string>();

        foreach (var l in lines)
        {
            if (l.StartsWith("# ", StringComparison.Ordinal) && headline == fallbackTitle)
            {
                headline = l[2..].Trim().Trim('*');
                continue;
            }
            if (l.StartsWith("## ", StringComparison.Ordinal) && headline == fallbackTitle)
            {
                headline = l[3..].Trim().Trim('*');
                continue;
            }
            if (string.IsNullOrEmpty(subtitle) && !l.StartsWith("#", StringComparison.Ordinal) && !l.StartsWith("|", StringComparison.Ordinal) && !l.StartsWith("-", StringComparison.Ordinal) && !l.StartsWith("*", StringComparison.Ordinal) && l.Length >= 25)
            {
                subtitle = l.Trim('*');
                continue;
            }
            bodyLines.Add(l.Replace("**", string.Empty));
        }

        if (string.IsNullOrWhiteSpace(subtitle))
        {
            subtitle = tableRowCount >= 2
                ? $"Dokumen ekstraksi terstruktur memuat {tableRowCount} baris tabel siap ekspor ke Excel (.xlsx) atau PDF (.pdf)."
                : $"Dokumen pratinjau interaktif NEXUS Canvas ({totalLines} baris terverifikasi).";
        }

        string metricVal = string.Empty;
        string metricCap = string.Empty;
        var numMatch = Regex.Match(headline + " " + docText, @"\b(\d+\s+(?:Bug|Tahap|Lapis|Tes|Item|Baris|Poin|Fitur|Langkah))\b", RegexOptions.IgnoreCase);
        if (numMatch.Success)
        {
            metricVal = numMatch.Groups[1].Value;
            metricCap = "Metrik utama teridentifikasi pada dokumen & siap diekspor.";
        }
        else if (tableRowCount >= 2)
        {
            metricVal = $"{tableRowCount} Baris";
            metricCap = "Tabel data terstruktur siap diekspor ke .xlsx / .pdf.";
        }

        string cleanBody = bodyLines.Count > 0
            ? string.Join(Environment.NewLine + Environment.NewLine, bodyLines.Take(18))
            : docText;

        return (headline, subtitle, metricVal, metricCap, cleanBody);
    }

    private static string? TryExtractFirstHeadingTitle(string markdown)
    {
        var m = Regex.Match(markdown, @"^\s*#{1,3}\s+(.+)$", RegexOptions.Multiline);
        if (m.Success)
        {
            string t = m.Groups[1].Value.Trim().Trim('*', '`', '#').Trim();
            if (!string.IsNullOrWhiteSpace(t))
                return t.Length > 72 ? t[..72] + "..." : t;
        }
        return null;
    }

    private static string TryExtractFileNameHint(string surroundingText, string codeBody, string defaultExt, string? fallbackTitle)
    {
        var fileMatch = Regex.Match(
            surroundingText + "\n" + codeBody,
            @"\b([A-Za-z0-9_.\-]+\.(?:cmd|bat|ps1|cs|py|js|ts|json|html|xaml|xml|sql|md|xlsx|pdf|txt))\b",
            RegexOptions.IgnoreCase);
        if (fileMatch.Success)
            return fileMatch.Groups[1].Value;

        var titleMatch = Regex.Match(codeBody, @"^\s*(?:title|#)\s+(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (titleMatch.Success)
        {
            string rawTitle = titleMatch.Groups[1].Value.Trim();
            return rawTitle.Length > 56 ? rawTitle[..56] : rawTitle;
        }

        if (!string.IsNullOrWhiteSpace(fallbackTitle))
            return fallbackTitle!;

        return "Nexus_Canvas_Artifact" + defaultExt;
    }

    public static string SanitizeFileName(string titleOrName, string desiredExtension)
    {
        string ext = desiredExtension.StartsWith(".", StringComparison.Ordinal) ? desiredExtension : "." + desiredExtension;
        string baseName = Path.GetFileNameWithoutExtension(titleOrName ?? "Nexus_Canvas");
        baseName = Regex.Replace(baseName, @"[^A-Za-z0-9_\-]+", "_").Trim('_');
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Nexus_Canvas";
        if (baseName.Length > 48)
            baseName = baseName[..48].TrimEnd('_');
        return baseName + ext;
    }

    /// <summary>
    /// Generates a 100% standards-compliant OpenXML .xlsx workbook archive in memory without requiring external NuGet binaries.
    /// </summary>
    public static byte[] BuildRealOpenXmlXlsxBytes(
        string sheetTitle,
        IReadOnlyList<IReadOnlyList<string>>? rows,
        string? fallbackMarkdown = null)
    {
        var effectiveRows = (rows is { Count: > 0 })
            ? rows.ToList()
            : ExtractMarkdownTableRows(fallbackMarkdown).ToList();

        if (effectiveRows.Count == 0 && !string.IsNullOrWhiteSpace(fallbackMarkdown))
        {
            effectiveRows.Add(new[] { "No", "Bagian / Baris Dokumen" });
            int idx = 1;
            foreach (var line in fallbackMarkdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    effectiveRows.Add(new[] { (idx++).ToString(), trimmed });
                }
            }
        }
        if (effectiveRows.Count == 0)
        {
            effectiveRows.Add(new[] { "Title", sheetTitle ?? "NEXUS Canvas Export" });
        }

        using var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                "</Types>");

            WriteZipEntry(archive, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");

            string safeSheet = Regex.Replace(sheetTitle ?? "NexusData", @"[\[\]\*\/\\\?\:]", " ").Trim();
            if (string.IsNullOrWhiteSpace(safeSheet)) safeSheet = "NexusData";
            if (safeSheet.Length > 31) safeSheet = safeSheet[..31];

            WriteZipEntry(archive, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                $"<sheets><sheet name=\"{EscapeXml(safeSheet)}\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                "</workbook>");

            WriteZipEntry(archive, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                "</Relationships>");

            WriteZipEntry(archive, "xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Segoe UI\"/></font><font><b/><sz val=\"11\"/><name val=\"Segoe UI\"/></font></fonts>" +
                "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
                "</styleSheet>");

            var sheetSb = new StringBuilder();
            sheetSb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sheetSb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            for (int rIdx = 0; rIdx < effectiveRows.Count; rIdx++)
            {
                int rowNum = rIdx + 1;
                sheetSb.Append($"<row r=\"{rowNum}\">");
                var row = effectiveRows[rIdx];
                for (int cIdx = 0; cIdx < row.Count; cIdx++)
                {
                    string colRef = GetExcelColumnName(cIdx + 1) + rowNum;
                    string styleAttr = rIdx == 0 ? " s=\"1\"" : string.Empty;
                    string cellVal = row[cIdx] ?? string.Empty;
                    sheetSb.Append($"<c r=\"{colRef}\" t=\"inlineStr\"{styleAttr}><is><t>{EscapeXml(cellVal)}</t></is></c>");
                }
                sheetSb.Append("</row>");
            }
            sheetSb.Append("</sheetData></worksheet>");
            WriteZipEntry(archive, "xl/worksheets/sheet1.xml", sheetSb.ToString());
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Generates a valid multi-line %PDF-1.4 document with Helvetica-Bold title/headings and formatted content/table rows.
    /// </summary>
    public static byte[] BuildRealFormattedPdfBytes(
        string documentTitle,
        string markdownOrCodeBody,
        IReadOnlyList<IReadOnlyList<string>>? tableRows = null)
    {
        string cleanTitle = EscapePdfText(string.IsNullOrWhiteSpace(documentTitle) ? "NEXUS Canvas Document" : documentTitle);
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(markdownOrCodeBody))
        {
            foreach (var raw in markdownOrCodeBody.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string trimmed = raw.TrimEnd();
                if (trimmed.Length > 92)
                {
                    for (int i = 0; i < trimmed.Length; i += 92)
                    {
                        lines.Add(trimmed.Substring(i, Math.Min(92, trimmed.Length - i)));
                    }
                }
                else
                {
                    lines.Add(trimmed);
                }
                if (lines.Count >= 44) break;
            }
        }
        if (tableRows is { Count: > 0 } && lines.Count < 40)
        {
            lines.Add(string.Empty);
            foreach (var r in tableRows.Take(20))
            {
                lines.Add(string.Join(" | ", r));
            }
        }

        var contentSb = new StringBuilder();
        contentSb.AppendLine("BT");
        contentSb.AppendLine("/F2 15 Tf");
        contentSb.AppendLine("48 790 Td");
        contentSb.AppendLine($"({cleanTitle}) Tj");
        contentSb.AppendLine("/F1 9.5 Tf");
        contentSb.AppendLine("0 -18 Td");
        contentSb.AppendLine("(Diekspor dari NEXUS Interactive Canvas) Tj");
        contentSb.AppendLine("0 -10 Td");
        contentSb.AppendLine("(________________________________________________________________________________) Tj");
        contentSb.AppendLine("0 -18 Td");

        foreach (var line in lines.Take(42))
        {
            bool isHeader = line.StartsWith("#", StringComparison.Ordinal);
            string displayLine = isHeader ? line.TrimStart('#', ' ') : line;
            contentSb.AppendLine(isHeader ? "/F2 11 Tf" : "/F1 9.5 Tf");
            contentSb.AppendLine($"({EscapePdfText(displayLine)}) Tj");
            contentSb.AppendLine("0 -14 Td");
        }
        contentSb.AppendLine("ET");

        byte[] streamBytes = Encoding.ASCII.GetBytes(contentSb.ToString());
        using var ms = new MemoryStream();
        void WriteAscii(string s)
        {
            var b = Encoding.ASCII.GetBytes(s);
            ms.Write(b, 0, b.Length);
        }

        WriteAscii("%PDF-1.4\n");
        var offsets = new List<long> { 0 };

        offsets.Add(ms.Position);
        WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets.Add(ms.Position);
        WriteAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        offsets.Add(ms.Position);
        WriteAscii("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>\nendobj\n");

        offsets.Add(ms.Position);
        WriteAscii("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        offsets.Add(ms.Position);
        WriteAscii("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>\nendobj\n");

        offsets.Add(ms.Position);
        WriteAscii($"6 0 obj\n<< /Length {streamBytes.Length} >>\nstream\n");
        ms.Write(streamBytes, 0, streamBytes.Length);
        WriteAscii("\nendstream\nendobj\n");

        long xrefPos = ms.Position;
        WriteAscii($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        for (int i = 1; i < offsets.Count; i++)
        {
            WriteAscii($"{offsets[i]:D10} 00000 n \n");
        }
        WriteAscii($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF");
        return ms.ToArray();
    }

    public static CanvasExportResult ExportCanvasArtifactToDisk(
        CanvasExtractedArtifact artifact,
        string format,
        string? targetDirectory = null)
    {
        string normFmt = (format ?? "pdf").Trim().ToLowerInvariant();
        string outDir = !string.IsNullOrWhiteSpace(targetDirectory)
            ? targetDirectory!
            : (Directory.Exists(@"D:\NEXUS") ? @"D:\NEXUS\nexus_data\downloads" : Path.Combine(Path.GetTempPath(), "nexus_canvas_exports"));
        Directory.CreateDirectory(outDir);

        string ext;
        byte[] payload;
        if (normFmt is "xlsx" or "excel")
        {
            ext = ".xlsx";
            payload = BuildRealOpenXmlXlsxBytes(artifact.Title, artifact.TableRows, artifact.RawCodeOrMarkdown);
        }
        else if (normFmt is "pdf")
        {
            ext = ".pdf";
            payload = BuildRealFormattedPdfBytes(artifact.Title, artifact.RawCodeOrMarkdown, artifact.TableRows);
        }
        else
        {
            ext = Path.GetExtension(artifact.FileNameHint);
            if (string.IsNullOrWhiteSpace(ext) || ext is ".xlsx" or ".pdf")
                ext = ".md";
            payload = Encoding.UTF8.GetBytes(artifact.RawCodeOrMarkdown ?? string.Empty);
        }

        string fileName = SanitizeFileName(artifact.Title, ext);
        string fullPath = Path.Combine(outDir, fileName);
        File.WriteAllBytes(fullPath, payload);

        byte[] shaBytes = System.Security.Cryptography.SHA256.HashData(payload);
        string shaHex = Convert.ToHexString(shaBytes).ToLowerInvariant();

        return new CanvasExportResult(
            IsSuccess: true,
            Format: ext.TrimStart('.'),
            FullFilePath: fullPath,
            FileName: fileName,
            FileSizeBytes: payload.Length,
            Sha256Hex: shaHex,
            SummaryMessage: $"✓ Tersimpan ke {fileName} ({payload.Length:N0} bytes)");
    }

    private static void WriteZipEntry(System.IO.Compression.ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Fastest);
        using var s = entry.Open();
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }

    private static string GetExcelColumnName(int colNumber)
    {
        string colName = string.Empty;
        while (colNumber > 0)
        {
            int rem = (colNumber - 1) % 26;
            colName = (char)('A' + rem) + colName;
            colNumber = (colNumber - 1) / 26;
        }
        return colName;
    }

    private static string EscapeXml(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }

    private static string EscapePdfText(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (c is '(' or ')' or '\\')
            {
                sb.Append('\\').Append(c);
            }
            else if (c >= 32 && c <= 126)
            {
                sb.Append(c);
            }
            else
            {
                sb.Append(' ');
            }
        }
        return sb.ToString();
    }
}
