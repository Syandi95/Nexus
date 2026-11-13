global using Nexus.Contracts.BrowserAgent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.App.Services.BrowserAgent;

public static class BrowserSafetyGuard
{
    private static readonly Regex PromptInjectionPattern = new(
        @"(?:ignore\s+(?:all\s+)?(?:previous|prior)\s+instructions|system\s*prompt\s*:|<\|im_start\|>|<\|endoftext\|>|you\s+are\s+now\s+in\s+developer\s+mode|abaikan\s+instruksi\s+sebelumnya)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static (bool IsAllowed, string Reason) ValidateNavigationUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            return (false, "URL kosong.");

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
            return (false, "Format URL tidak valid.");

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"Protokol '{uri.Scheme}:' diblokir oleh BrowserSafetyGuard.");
        }

        string host = uri.DnsSafeHost.ToLowerInvariant();
        if (host is "localhost" or "0.0.0.0" || host.EndsWith(".local", StringComparison.Ordinal))
            return (false, $"Akses ke host lokal '{host}' diblokir (SSRF Protection).");

        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.IsLoopback(ip))
                return (false, $"Akses ke loopback IP '{ip}' diblokir (SSRF Protection).");

            byte[] bytes = ip.GetAddressBytes();
            if (bytes.Length == 4)
            {
                if (bytes[0] == 10 ||
                    bytes[0] == 127 ||
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168) ||
                    (bytes[0] == 169 && bytes[1] == 254))
                {
                    return (false, $"Akses ke IP privat '{ip}' diblokir (SSRF Protection).");
                }
            }
        }

        return (true, "OK");
    }

    public static (BrowserRiskLevel Risk, string Reason) EvaluateElementAction(
        string actionType,
        BrowserElementRef element,
        string activeSnapshotId)
    {
        if (!string.Equals(element.SnapshotId, activeSnapshotId, StringComparison.Ordinal))
            return (BrowserRiskLevel.Blocked, $"Snapshot ID '{element.SnapshotId}' sudah kadaluarsa (aktif: '{activeSnapshotId}'). Ambil snapshot baru terlebih dahulu.");

        if (element.IsPassword)
            return (BrowserRiskLevel.Blocked, "Ditolak: Agen dilarang mengisi atau membaca field password/kredensial.");

        if (string.Equals(actionType, "submit", StringComparison.OrdinalIgnoreCase) || element.IsSubmit)
            return (BrowserRiskLevel.RequiresUserConfirmation, "Submit formulir memerlukan konfirmasi eksplisit pengguna.");

        return (BrowserRiskLevel.AutoAllowed, "OK");
    }

    public static string SanitizeUntrustedWebContent(string? rawWebText, out bool injectionDetected)
    {
        injectionDetected = false;
        if (string.IsNullOrWhiteSpace(rawWebText))
            return string.Empty;

        if (PromptInjectionPattern.IsMatch(rawWebText))
        {
            injectionDetected = true;
            return PromptInjectionPattern.Replace(rawWebText, "[REDACTED_PROMPT_INJECTION_ATTEMPT]");
        }

        return rawWebText;
    }
}
