using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexus.App.Services.BrowserAgent;
using Nexus.App.ViewModels;
using Xunit;

namespace Nexus.Integration.Tests;

public class BrowserAgentStage3To5IntegrationTests
{
    [Fact]
    public void Stage3_All13BrowserTools_AreRegisteredInDispatcher()
    {
        var expected = new[]
        {
            "browser.get_state",
            "browser.navigate",
            "browser.search",
            "browser.get_snapshot",
            "browser.extract_content",
            "browser.click",
            "browser.fill",
            "browser.select",
            "browser.scroll",
            "browser.wait",
            "browser.download",
            "workspace.add_file",
            "browser.stop"
        };

        foreach (var tool in expected)
        {
            Assert.Contains(tool, BrowserToolDispatcher.RegisteredToolNames);
        }
        Assert.Equal(13, BrowserToolDispatcher.RegisteredToolNames.Count);
    }

    [Fact]
    public async Task Stage3And4_BrowserInteractionAndSnapshotValidation_WorksOnSameRightPanelInstance()
    {
        var workspaceVm = new AgentWorkspaceViewModel();
        var navigatedUrls = new List<string>();
        workspaceVm.BrowserNavigateRequested += url =>
        {
            navigatedUrls.Add(url);
            workspaceVm.LastBrowserTitle =
                "[PAGE_TITLE] Google Scholar - Ekonomi Makro\n" +
                "[PAGE_URL] " + url + "\n" +
                "[RAW_CHAR_COUNT] 8420\n" +
                "[SOURCES]\n" +
                "[1] Macroeconomic Policy and Inflation Dynamics in Indonesia | https://doi.org/10.1016/j.econmod.2025.01.004\n" +
                "[2] Fiscal Sustainability and Macroeconomic Stability | https://doi.org/10.1007/s10663-024-09612-x\n" +
                "[3] Monetary Transmission Mechanism in Emerging Markets | https://doi.org/10.1080/00074918.2025.21045\n" +
                "[SEMANTIC_CONTENT]\n" +
                "## Macroeconomic Policy and Inflation Dynamics in Indonesia (2025)\n" +
                "- Abstrak: Studi ini menganalisis kebijakan moneter dan inflasi makroekonomi Indonesia menggunakan model SVAR.\n" +
                "- Penulis: Pratama, A., Wijaya, R. (2025). DOI: 10.1016/j.econmod.2025.01.004.";
            return Task.FromResult(workspaceVm.LastBrowserTitle);
        };

        var adapter = new WebView2NativeAdapter(workspaceVm);
        var search = new ExternalSearchProvider();
        var fileService = new NexusWorkspaceFileService(workspaceVm);
        var dispatcher = new BrowserToolDispatcher(adapter, search, fileService);

        // 1. Navigate via dispatcher
        var navRes = await dispatcher.DispatchAsync(new BrowserToolCallRequest(
            "browser.navigate",
            new Dictionary<string, string> { ["url"] = "https://scholar.google.com/scholar?q=Ekonomi+Makro" }));

        Assert.True(navRes.IsSuccess);
        Assert.True(workspaceVm.IsOpen);
        Assert.Single(navigatedUrls);
        Assert.NotEmpty(navRes.SnapshotId);

        // 2. Fill search input (must NOT auto-submit!)
        var fillRes = await dispatcher.DispatchAsync(new BrowserToolCallRequest(
            "browser.fill",
            new Dictionary<string, string>
            {
                ["snapshot_id"] = navRes.SnapshotId,
                ["ref"] = "e1",
                ["value"] = "Ekonomi Makro Indonesia"
            }));
        Assert.True(fillRes.IsSuccess);
        Assert.Single(navigatedUrls); // Proves fill did NOT trigger a hidden submit navigation

        // 3. Click element [e1] using valid SnapshotId -> navigates to the DOI URL on the same right panel
        var clickRes = await dispatcher.DispatchAsync(new BrowserToolCallRequest(
            "browser.click",
            new Dictionary<string, string>
            {
                ["snapshot_id"] = navRes.SnapshotId,
                ["ref"] = "e1"
            }));
        Assert.True(clickRes.IsSuccess);
        Assert.Equal("https://doi.org/10.1016/j.econmod.2025.01.004", clickRes.CurrentUrl);
        Assert.Equal(2, navigatedUrls.Count);
    }

    [Fact]
    public async Task Stage5_WorkspaceFileService_VerifiesPdfHeaderSha256AndRegistersInRightPanel()
    {
        var workspaceVm = new AgentWorkspaceViewModel();
        var fileService = new NexusWorkspaceFileService(workspaceVm);

        string tempDir = Path.Combine(Path.GetTempPath(), "nexus_stage5_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string pdfPath = Path.Combine(tempDir, "Makalah_Ekonomi_Makro_2025.pdf");

        try
        {
            byte[] validPdfBytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<< /Title (Makalah Ekonomi Makro) >>\nendobj\n%%EOF");
            await File.WriteAllBytesAsync(pdfPath, validPdfBytes);

            var outcome = await fileService.AddDownloadedFileToWorkspaceAsync(
                pdfPath,
                "https://doi.org/10.1016/j.econmod.2025.01.004",
                "Macroeconomic Policy and Inflation Dynamics in Indonesia");

            Assert.True(outcome.IsSuccess);
            Assert.True(outcome.FileSizeBytes > 0);
            Assert.NotEmpty(outcome.Sha256Hex);
            Assert.True(File.Exists(pdfPath + ".nexus-meta.json"));
            Assert.Contains(workspaceVm.ReviewFiles, f => string.Equals(f.Target, pdfPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Stage4And6_CancellationAndBenchmarkTelemetry_RecordActualTokenCounts()
    {
        var workspaceVm = new AgentWorkspaceViewModel();
        workspaceVm.LastBrowserTitle =
            "[PAGE_TITLE] Riset Ekonomi Makro\n" +
            "[PAGE_URL] https://scholar.google.com/scholar?q=Ekonomi+Makro\n" +
            "[RAW_CHAR_COUNT] 4200\n" +
            "[SOURCES]\n" +
            "[1] Kebijakan Fiskal dan Moneter Indonesia | https://doi.org/10.1234/ekonomi.2025.01\n" +
            "[SEMANTIC_CONTENT]\n" +
            "## Kebijakan Fiskal dan Moneter Indonesia\n" +
            "- Analisis pertumbuhan PDB, inflasi, dan suku bunga acuan Bank Indonesia.";

        var adapter = new WebView2NativeAdapter(workspaceVm);
        var extracted = await adapter.ExtractContentAsync("Ekonomi Makro", 1200);

        Assert.True(extracted.RawTokenCount > 0);
        Assert.True(extracted.ExtractedTokenCount > 0);
        Assert.Single(extracted.ExtractedCitations);

        // Test immediate cancellation via CancellationToken
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var search = new ExternalSearchProvider();
        var fileService = new NexusWorkspaceFileService(workspaceVm);
        var dispatcher = new BrowserToolDispatcher(adapter, search, fileService);

        var waitRes = await dispatcher.DispatchAsync(
            new BrowserToolCallRequest("browser.wait", new Dictionary<string, string> { ["ms"] = "5000" }),
            cts.Token);
        Assert.False(waitRes.IsSuccess);
    }
}
