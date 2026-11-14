global using Nexus.Contracts.BrowserAgent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return (false, "URL dengan user:password@ diblokir.");

        string host = uri.DnsSafeHost.Trim('[', ']').TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0)
            return (false, "Host kosong.");

        if (host == "localhost" || host == "0.0.0.0" ||
            host.EndsWith(".localhost", StringComparison.Ordinal) ||
            host.EndsWith(".local", StringComparison.Ordinal) ||
            host.EndsWith(".internal", StringComparison.Ordinal) ||
            host.EndsWith(".lan", StringComparison.Ordinal) ||
            host.EndsWith(".home.arpa", StringComparison.Ordinal) ||
            host.EndsWith(".intranet", StringComparison.Ordinal))
            return (false, $"Akses ke host lokal/internal '{host}' diblokir (SSRF Protection).");

        if (IPAddress.TryParse(host, out var ip))
        {
            if (IsPrivateOrReservedAddress(ip))
                return (false, $"Akses ke IP privat/khusus '{ip}' diblokir (SSRF Protection).");
        }
        else if (!host.Contains('.'))
        {
            return (false, $"Host tanpa domain '{host}' diblokir (nama intranet).");
        }

        // CATATAN: hostname publik yang di-resolve DNS ke IP privat (DNS rebinding) belum dicek di sini;
        // perlu pengecekan pada lapisan jaringan/proxy.
        return (true, "OK");
    }

    internal static bool IsPrivateOrReservedAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip))
            return true;
        byte[] b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6Multicast || ip.Equals(IPAddress.IPv6Any);
        return b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 192 && b[1] == 0 && b[2] == 0)
            || (b[0] == 198 && (b[1] == 18 || b[1] == 19));
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
