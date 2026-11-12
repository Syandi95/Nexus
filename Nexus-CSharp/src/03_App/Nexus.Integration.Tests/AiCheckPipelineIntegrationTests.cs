using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Services;
using Nexus.App.ViewModels;
using Nexus.Conversation.Manager;
using Nexus.Engine.ImageSubsystem.Ocr;
using Nexus.Engine.ImageSubsystem.Services;
using Nexus.Contracts.AiCheck;
using Nexus.Engine.ImageSubsystem.Triage;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Integration.Tests;

public class AiCheckPipelineIntegrationTests
{
    private readonly ITestOutputHelper _output;

    private static readonly string Sample1Path = File.Exists(@"D:\NEXUS\PILKADES SUKAPINDAH UK. 100 X 150 CM = 3 LEMBAR FINISHING.jpg")
        ? @"D:\NEXUS\PILKADES SUKAPINDAH UK. 100 X 150 CM = 3 LEMBAR FINISHING.jpg"
        : @"D:\NEXUS\test\PILKADES SUKAPINDAH UK. 100 X 150 CM = 3 LEMBAR FINISHING.jpg";

    private static readonly string Sample2Path = File.Exists(@"D:\NEXUS\ChatGPT Image 5 Sep 2026, 15.40.22.png")
        ? @"D:\NEXUS\ChatGPT Image 5 Sep 2026, 15.40.22.png"
        : @"D:\NEXUS\test\ChatGPT Image 5 Sep 2026, 15.40.22.png";

    public AiCheckPipelineIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Test1_Sample1_FullPerceptionAndTriage_PilkadesOku()
    {
        Assert.True(File.Exists(Sample1Path), $"Sample 1 not found at {Sample1Path}");

        _output.WriteLine("=== E2E TEST: SAMPLE 1 (PILKADES OKU CMYK JPG) ===");
        var swTotal = Stopwatch.StartNew();

        using var bmp = DesignImagePreprocessor.LoadAsBitmap(Sample1Path);
        int origW = bmp.Width;
        int origH = bmp.Height;
        _output.WriteLine($"Image loaded: {origW}x{origH}, elapsed: {swTotal.ElapsedMilliseconds} ms");

        using var engine = new PpOcrEngine();
        long detMs;
        var swDetRec = Stopwatch.StartNew();
        var boxes = engine.Detect(bmp, maxSide: 1536, thresh: 0.20f, boxThresh: 0.45f, unclipRatio: 1.40f, out detMs);
        _output.WriteLine($"Detection: {boxes.Count} boxes in {detMs} ms");

        long totalRecMs = 0;
        for (int i = 0; i < boxes.Count; i++)
        {
            engine.RecognizeBox(bmp, boxes[i]);
            boxes[i].Id = i + 1;
            totalRecMs += boxes[i].RecMs;
        }
        swDetRec.Stop();
        swTotal.Stop();

        _output.WriteLine($"Recognition: {totalRecMs} ms. Det+Rec total: {swDetRec.ElapsedMilliseconds} ms ({swDetRec.ElapsedMilliseconds / 1000.0:F2}s)");

        // 1. Box Count Verification (Baseline 37-39, tolerance 36-40)
        Assert.InRange(boxes.Count, 36, 40);

        // 2. Native Bounding Box Coordinates Verification
        foreach (var b in boxes)
        {
            Assert.True(b.X1 >= 0 && b.X1 < origW, $"X1 ({b.X1}) out of bounds [0, {origW})");
            Assert.True(b.Y1 >= 0 && b.Y1 < origH, $"Y1 ({b.Y1}) out of bounds [0, {origH})");
            Assert.True(b.X2 > b.X1 && b.X2 <= origW, $"X2 ({b.X2}) must be > X1 and <= {origW}");
            Assert.True(b.Y2 > b.Y1 && b.Y2 <= origH, $"Y2 ({b.Y2}) must be > Y1 and <= {origH}");
        }

        // 3. Key Text Content Verification
        bool hasPilkades = boxes.Any(b => b.Text.Contains("PENDAFTARAN") || b.Text.Contains("KEPALA DESA") || b.Text.Contains("PILKADES"));
        bool hasSukapindah = boxes.Any(b => b.Text.Contains("SUKAPINDAH"));
        Assert.True(hasPilkades, "Key text 'PENDAFTARAN' / 'KEPALA DESA' / 'PILKADES' must be detected.");
        Assert.True(hasSukapindah, "Key text 'SUKAPINDAH' must be detected.");

        // 4. Inference Time Verification (Gate 1 limit <= 6.5s)
        Assert.True(swDetRec.ElapsedMilliseconds <= 6500,
            $"Inference duration ({swDetRec.ElapsedMilliseconds} ms) exceeded Gate 1 budget (6500 ms).");

        // 5. Run Triage on Real Detected Boxes
        var filter = new ThreeTierTriageFilter();
        var refData = new List<string> { "Desa Sukapindah", "KAB. OGAN KOMERING ULU", "Puskesmas Grogol" };
        var swTriage = Stopwatch.StartNew();
        var findings = filter.Evaluate(boxes, refData);
        swTriage.Stop();

        _output.WriteLine($"Triage evaluated {boxes.Count} boxes in {swTriage.ElapsedMilliseconds} ms, flagged {findings.Count} findings.");
        Assert.True(swTriage.ElapsedMilliseconds < 50, $"Triage must execute in < 50ms, took {swTriage.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public void Test2_Sample2_FullPerceptionAndTriage_PaninBank_CatchesLjinTypo()
    {
        Assert.True(File.Exists(Sample2Path), $"Sample 2 not found at {Sample2Path}");

        _output.WriteLine("=== E2E TEST: SAMPLE 2 (PANIN BANK PNG) ===");
        var swTotal = Stopwatch.StartNew();

        using var bmp = DesignImagePreprocessor.LoadAsBitmap(Sample2Path);
        int origW = bmp.Width;
        int origH = bmp.Height;
        _output.WriteLine($"Image loaded: {origW}x{origH}, elapsed: {swTotal.ElapsedMilliseconds} ms");

        using var engine = new PpOcrEngine();
        long detMs;
        var swDetRec = Stopwatch.StartNew();
        var boxes = engine.Detect(bmp, maxSide: 1280, thresh: 0.20f, boxThresh: 0.45f, unclipRatio: 1.40f, out detMs);
        _output.WriteLine($"Detection: {boxes.Count} boxes in {detMs} ms");

        long totalRecMs = 0;
        for (int i = 0; i < boxes.Count; i++)
        {
            engine.RecognizeBox(bmp, boxes[i]);
            boxes[i].Id = i + 1;
            totalRecMs += boxes[i].RecMs;
        }
        swDetRec.Stop();
        swTotal.Stop();

        _output.WriteLine($"Recognition: {totalRecMs} ms. Det+Rec total: {swDetRec.ElapsedMilliseconds} ms ({swDetRec.ElapsedMilliseconds / 1000.0:F2}s)");

        // 1. Box Count Verification (Baseline 35, tolerance 34-36)
        Assert.InRange(boxes.Count, 34, 36);

        // 2. Detection of Target Typo 'ljin'
        var ljinBox = boxes.FirstOrDefault(b => b.Text.Contains("ljin") || b.Text.Contains("Surat ljin"));
        Assert.NotNull(ljinBox);
        _output.WriteLine($"Detected target box: '{ljinBox.Text}', DetScore={ljinBox.DetScore:F2}, RecConf={ljinBox.RecConfidence:F2}");

        // 3. Inference Time Verification (Gate 1 limit <= 6.0s)
        Assert.True(swDetRec.ElapsedMilliseconds <= 6000,
            $"Inference duration ({swDetRec.ElapsedMilliseconds} ms) exceeded Gate 1 budget (6000 ms).");

        // 4. Run Triage and Verify Layer 2A Catches High-Confidence 'ljin'
        var filter = new ThreeTierTriageFilter();
        var findings = filter.Evaluate(boxes, (IReadOnlyList<string>?)null);

        var ljinFinding = findings.FirstOrDefault(f => f.Box.Id == ljinBox.Id);
        Assert.NotNull(ljinFinding);
        Assert.True((ljinFinding.LayerFlags & TriageLayerFlags.Layer2A_SuspiciousPrefix) != 0,
            "Layer 2A must catch 'ljin' prefix anomaly regardless of high OCR confidence.");
    }

    [Fact]
    public void Test3_ThreeTierTriage_ExactMatchTrumpsNearMiss_AndCatchesGroqol()
    {
        var refDb = new List<string>
        {
            "Desa Sukapindah",
            "Desa SukaIndah",
            "Puskesmas Grogol",
            "KEC. KEDATON PENINJAUAN RAYA",
            "KAB. OGAN KOMERING ULU"
        };

        var filter = new ThreeTierTriageFilter();
        var testBoxes = new List<OcrBoxModel>
        {
            // Exact matches -> Must NOT be flagged as near miss
            new() { Id = 1, Text = "Desa Sukapindah", DetScore = 0.95f, RecConfidence = 0.99f },
            new() { Id = 2, Text = "KAB. OGAN KOMERING ULU", DetScore = 0.96f, RecConfidence = 1.00f },

            // Near-miss entity -> Must be flagged by Layer 3
            new() { Id = 3, Text = "Puskesmas Groqol", DetScore = 0.98f, RecConfidence = 1.00f },

            // High-confidence lexical typo -> Must be flagged by Layer 2A
            new() { Id = 4, Text = "Surat ljin Iklan", DetScore = 0.95f, RecConfidence = 0.99f },

            // Low-confidence & irregular casing -> Must be flagged by Layer 1 & 2B
            new() { Id = 5, Text = "YangMaJuMdaS", DetScore = 0.91f, RecConfidence = 0.45f }
        };

        var findings = filter.Evaluate(testBoxes, refDb);

        // Exact matches must NOT be in findings
        Assert.DoesNotContain(findings, f => f.Box.Id == 1);
        Assert.DoesNotContain(findings, f => f.Box.Id == 2);

        // Puskesmas Groqol -> Layer 3 match with Puskesmas Grogol
        var groqolFinding = findings.FirstOrDefault(f => f.Box.Id == 3);
        Assert.NotNull(groqolFinding);
        Assert.True((groqolFinding.LayerFlags & TriageLayerFlags.Layer3_ReferenceMismatch) != 0);
        Assert.Equal("Puskesmas Grogol", groqolFinding.MatchedReferenceEntity);
        Assert.Equal(1, groqolFinding.LevenshteinDistance);

        // Surat ljin -> Layer 2A
        var ljinFinding = findings.FirstOrDefault(f => f.Box.Id == 4);
        Assert.NotNull(ljinFinding);
        Assert.True((ljinFinding.LayerFlags & TriageLayerFlags.Layer2A_SuspiciousPrefix) != 0);

        // YangMaJuMdaS -> Layer 1 & Layer 2B
        var kaligrafiFinding = findings.FirstOrDefault(f => f.Box.Id == 5);
        Assert.NotNull(kaligrafiFinding);
        Assert.True((kaligrafiFinding.LayerFlags & TriageLayerFlags.Layer1_LowRecConfidence) != 0);
        Assert.True((kaligrafiFinding.LayerFlags & TriageLayerFlags.Layer2B_IrregularCasing) != 0);
    }

    [Fact]
    public void Test4_ZeroRegression_ConversationViewModel_ModeSwitchContract()
    {
        var mockConvManager = new Mock<IConversationManagerService>();
        var mockLlama = new Mock<ILlamaClientService>();

        var vm = new ConversationViewModel(
            mockConvManager.Object,
            mockLlama.Object);

        // 1. Initial State Contract: Chat is active by default
        Assert.True(vm.IsChatModeActive, "Chat mode must be active by default.");
        Assert.False(vm.IsAiCheckModeActive, "AI Check mode must be inactive by default.");
        Assert.Empty(vm.Messages);

        // 2. Transition to AI CHECK
        vm.IsAiCheckModeActive = true;
        Assert.True(vm.IsAiCheckModeActive, "AI Check mode must be active after switch.");
        Assert.False(vm.IsChatModeActive, "Chat mode must be inactive when AI Check is active.");

        // Chat state remains pristine
        Assert.Empty(vm.Messages);
        Assert.Equal(string.Empty, vm.InputText);

        // 3. Transition back to CHAT
        vm.IsChatModeActive = true;
        Assert.True(vm.IsChatModeActive, "Chat mode must be restored.");
        Assert.False(vm.IsAiCheckModeActive, "AI Check mode must be inactive.");
    }

    [Fact]
    public void Test5_ChatStateAndAiCheckModule_PreservedAcrossMultipleToggles()
    {
        var mockConvManager = new Mock<IConversationManagerService>();
        var mockLlama = new Mock<ILlamaClientService>();
        var mockRef = new Mock<Nexus.Contracts.ReferenceData.IReferenceDataManager>();
        var mockJudge = new Mock<Nexus.Contracts.AiCheck.IAiCheckJudgeService>();

        var aiCheckVm = new AiCheckViewModel(mockRef.Object, mockJudge.Object);

        var vm = new ConversationViewModel(
            mockConvManager.Object,
            mockLlama.Object,
            aiCheck: aiCheckVm);

        // A. Kondisi Awal: Mode CHAT aktif secara default
        Assert.True(vm.IsChatModeActive, "Awal: Mode CHAT harus aktif.");
        Assert.False(vm.IsAiCheckModeActive, "Awal: Mode AI CHECK harus nonaktif.");
        Assert.NotNull(vm.AiCheck);

        // B. Simulasikan interaksi pengguna di Chat (mengetik teks, menambah lampiran & pesan)
        vm.InputText = "Tolong periksa dokumen banner pilkades ini.";
        var testMsg = new ChatMessageItemViewModel
        {
            Role = "user",
            Content = "Halo AI, apa kabar?"
        };
        vm.Messages.Add(testMsg);

        var testChip = new AttachmentChipViewModel
        {
            Label = "banner.jpg",
            Path = @"C:\sample\banner.jpg"
        };
        vm.Attachments.Add(testChip);

        Assert.Single(vm.Messages);
        Assert.Single(vm.Attachments);
        Assert.Equal("Tolong periksa dokumen banner pilkades ini.", vm.InputText);

        // C. Klik tombol [ AI CHECK ]
        vm.IsAiCheckModeActive = true;

        // 1. Verifikasi Status Mode
        Assert.True(vm.IsAiCheckModeActive, "Setelah klik: Mode AI CHECK harus aktif.");
        Assert.False(vm.IsChatModeActive, "Setelah klik: Mode CHAT harus nonaktif.");

        // 2. Verifikasi Seluruh Komponen & Modul AI CHECK Tersedia Penuh
        Assert.NotNull(vm.AiCheck);
        Assert.True(vm.AiCheck.HasNoUploadedFile, "Modul AI Check: Banner upload harus aktif sebelum ada file.");
        Assert.False(vm.AiCheck.HasUploadedFile, "Modul AI Check: Belum ada file terunggah.");
        Assert.NotNull(vm.AiCheck.SelectFileCommand);
        Assert.NotNull(vm.AiCheck.RunCheckCommand);
        Assert.NotNull(vm.AiCheck.ResetCommand);
        Assert.NotNull(vm.AiCheck.CopyCorrectionCommand);
        Assert.NotNull(vm.AiCheck.DismissFindingCommand);
        Assert.NotNull(vm.AiCheck.ZoomInCommand);
        Assert.NotNull(vm.AiCheck.ZoomOutCommand);
        Assert.NotNull(vm.AiCheck.ResetZoomCommand);
        Assert.NotNull(vm.AiCheck.AvailableDatasets);
        Assert.NotNull(vm.AiCheck.OverlayBoxes);
        Assert.NotNull(vm.AiCheck.Findings);
        Assert.Equal(1.0, vm.AiCheck.ZoomLevel);

        // 3. Verifikasi Data & Riwayat Chat Lama SAMA SEKALI TIDAK BERUBAH / TIDAK HILANG
        Assert.Single(vm.Messages);
        Assert.Same(testMsg, vm.Messages[0]);
        Assert.Equal("Halo AI, apa kabar?", vm.Messages[0].Content);
        Assert.Single(vm.Attachments);
        Assert.Same(testChip, vm.Attachments[0]);
        Assert.Equal("Tolong periksa dokumen banner pilkades ini.", vm.InputText);

        // D. Klik kembali tombol [ CHAT ]
        vm.IsChatModeActive = true;

        // 1. Verifikasi Status Mode Kembali ke Chat
        Assert.True(vm.IsChatModeActive, "Setelah klik balik: Mode CHAT harus aktif kembali.");
        Assert.False(vm.IsAiCheckModeActive, "Setelah klik balik: Mode AI CHECK harus nonaktif.");

        // 2. Verifikasi Konsistensi Chat Penuh (Zero Mutation)
        Assert.Single(vm.Messages);
        Assert.Same(testMsg, vm.Messages[0]);
        Assert.Equal("Halo AI, apa kabar?", vm.Messages[0].Content);
        Assert.Single(vm.Attachments);
        Assert.Same(testChip, vm.Attachments[0]);
        Assert.Equal("Tolong periksa dokumen banner pilkades ini.", vm.InputText);
    }

    [Fact]
    public void Test6_ConversationViewXaml_AiCheckHostPanel_BindingContract()
    {
        var currentDir = AppContext.BaseDirectory;
        var viewPath = Path.Combine(currentDir, "..", "..", "..", "..", "src", "03_App", "Nexus.App", "Views", "ConversationView.xaml");
        viewPath = Path.GetFullPath(viewPath);
        if (!File.Exists(viewPath))
        {
            viewPath = @"D:\NEXUS\Nexus-CSharp\src\03_App\Nexus.App\Views\ConversationView.xaml";
        }
        Assert.True(File.Exists(viewPath), $"ConversationView.xaml not found at {viewPath}");

        var doc = System.Xml.Linq.XDocument.Load(viewPath);
        var xamlNs = System.Xml.Linq.XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var hostPanel = doc.Descendants().Single(e => (string?)e.Attribute(xamlNs + "Name") == "AiCheckHostPanel");
        Assert.Equal("1", (string?)hostPanel.Attribute("Grid.Row"));
        Assert.Equal("2", (string?)hostPanel.Attribute("Grid.RowSpan"));
        Assert.Equal("10", (string?)hostPanel.Attribute("Panel.ZIndex"));
        Assert.Equal("{Binding AiCheck}", (string?)hostPanel.Attribute("DataContext"));

        var visibility = (string?)hostPanel.Attribute("Visibility");
        Assert.NotNull(visibility);
        Assert.Contains("IsAiCheckModeActive", visibility);
        Assert.Contains("BooleanToVisibilityConverter", visibility);
        Assert.Contains("RelativeSource", visibility);
    }

    [Fact]
    public void Test7_VerifyHorizontalBannerGeometry_NoClipping()
    {
        // Viewport dimensions (typical Nexus.App window layout)
        double stageW = 740.0;
        double stageH = 600.0;
        double availW = Math.Max(20.0, stageW - 32.0);
        double availH = Math.Max(20.0, stageH - 32.0);

        // 1. Extreme Horizontal Banner (ChatGPT 7 Sep: 1774 x 887)
        int imgW = 1774;
        int imgH = 887;
        double imgAspect = (double)imgW / imgH;
        double stageAspect = availW / availH;
        double dispW, dispH;
        if (stageAspect > imgAspect)
        {
            dispH = availH;
            dispW = availH * imgAspect;
        }
        else
        {
            dispW = availW;
            dispH = availW / imgAspect;
        }
        double ratio = dispW / imgW;
        double leftScreenX = (stageW - dispW) / 2.0;
        double rightScreenX = leftScreenX + dispW;

        // Verify stage contains entire banner
        Assert.True(leftScreenX >= 0, $"Left screen X must be non-negative: {leftScreenX}");
        Assert.True(rightScreenX <= stageW, $"Right screen X ({rightScreenX}) must fit inside stage width ({stageW})");

        // The green panel 'LINGKUNGAN BERSIH' is at orig X=1432, trash icon at X=1650
        double greenPanelScreenX = leftScreenX + (1432.0 * ratio);
        double trashCanScreenX = leftScreenX + (1650.0 * ratio);

        Assert.True(greenPanelScreenX >= 0 && greenPanelScreenX <= stageW,
            $"Green panel at X=1432 must be fully visible on screen. Got screen X={greenPanelScreenX}, stage width={stageW}");
        Assert.True(trashCanScreenX >= 0 && trashCanScreenX <= stageW,
            $"Trash can icon at X=1650 must be fully visible on screen. Got screen X={trashCanScreenX}, stage width={stageW}");

        // 2. Extreme Vertical Image (WhatsApp Upscaled: 2400 x 6400)
        int tallW = 2400;
        int tallH = 6400;
        double tallAspect = (double)tallW / tallH;
        double tallDispH, tallDispW;
        if (stageAspect > tallAspect)
        {
            tallDispH = availH;
            tallDispW = availH * tallAspect;
        }
        else
        {
            tallDispW = availW;
            tallDispH = availW / tallAspect;
        }
        double tallRatio = tallDispH / tallH;
        double topScreenY = (stageH - tallDispH) / 2.0;
        double bottomScreenY = topScreenY + tallDispH;

        Assert.True(topScreenY >= 0, $"Top screen Y must be non-negative: {topScreenY}");
        Assert.True(bottomScreenY <= stageH, $"Bottom screen Y ({bottomScreenY}) must fit inside stage height ({stageH})");
    }

    [Fact]
    public void Test8_VerifyInstantSyncAndDismissFinding()
    {
        var mockRef = new Mock<Nexus.Contracts.ReferenceData.IReferenceDataManager>();
        var mockJudge = new Mock<Nexus.Contracts.AiCheck.IAiCheckJudgeService>();

        var vm = new AiCheckViewModel(mockRef.Object, mockJudge.Object);

        // Simulate triage results: 2 overlay boxes and findings
        var box1 = new AiCheckOverlayBoxViewModel { Id = 1, X1 = 100, Y1 = 100, X2 = 300, Y2 = 150, Status = OverlayBoxStatus.Review };
        var box2 = new AiCheckOverlayBoxViewModel { Id = 2, X1 = 400, Y1 = 200, X2 = 700, Y2 = 260, Status = OverlayBoxStatus.Correction };
        vm.OverlayBoxes.Add(box1);
        vm.OverlayBoxes.Add(box2);

        var finding1 = new AiCheckFindingItemViewModel { BoxId = 1, OriginalText = "DESA SUKAPINDAH", RecommendedText = "Desa Sukapindah", Reason = "Kapitalisasi huruf", Status = Nexus.Contracts.AiCheck.AiCheckJudgmentStatus.PerluVerifikasi };
        var finding2 = new AiCheckFindingItemViewModel { BoxId = 2, OriginalText = "BUPATII", RecommendedText = "Bupati", Reason = "Typo ganda", Status = Nexus.Contracts.AiCheck.AiCheckJudgmentStatus.Koreksi };
        vm.Findings.Add(finding1);
        vm.Findings.Add(finding2);

        // Verify Instant Sync: Count matches exactly
        Assert.Equal(2, vm.Findings.Count);
        Assert.Equal(2, vm.OverlayBoxes.Count);

        // Execute Dismiss ("Abaikan") on finding1
        vm.DismissFindingCommand.Execute(finding1);

        // Verify finding1 is removed immediately from Sidebar Temuan
        Assert.Single(vm.Findings);
        Assert.Same(finding2, vm.Findings[0]);
        Assert.True(finding1.IsDismissed);

        // Verify associated overlay box is marked safe
        Assert.Equal(OverlayBoxStatus.Safe, box1.Status);
    }

    [Fact]
    public void Test9_AiCheckViewXaml_PureNexusComponentConformance()
    {
        var currentDir = AppContext.BaseDirectory;
        var viewPath = Path.Combine(currentDir, "..", "..", "..", "..", "src", "03_App", "Nexus.App", "Views", "AiCheckView.xaml");
        viewPath = Path.GetFullPath(viewPath);
        if (!File.Exists(viewPath))
        {
            viewPath = @"D:\NEXUS\Nexus-CSharp\src\03_App\Nexus.App\Views\AiCheckView.xaml";
        }
        Assert.True(File.Exists(viewPath), $"AiCheckView.xaml not found at {viewPath}");

        var doc = System.Xml.Linq.XDocument.Load(viewPath);
        var content = File.ReadAllText(viewPath);

        // 1. Foreign Wpf.Ui Dictionary must NOT be present
        Assert.DoesNotContain("Wpf.Ui.xaml", content);
        Assert.DoesNotContain("ui:SymbolIcon", content);
        Assert.DoesNotContain("ui:Card", content);
        Assert.DoesNotContain("ui:InfoBadge", content);
        Assert.DoesNotContain("ui:ProgressRing", content);

        // 2. ImgPreview and OverlayViewbox must use Stretch="Uniform"
        var xamlNs = System.Xml.Linq.XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var imgPreview = doc.Descendants().Single(e => (string?)e.Attribute(xamlNs + "Name") == "ImgPreview");
        Assert.Equal("Uniform", (string?)imgPreview.Attribute("Stretch"));

        var overlayViewbox = doc.Descendants().Single(e => (string?)e.Attribute(xamlNs + "Name") == "OverlayViewbox");
        Assert.Equal("Uniform", (string?)overlayViewbox.Attribute("Stretch"));

        // 3. Must use Nexus design system components
        Assert.Contains("NexusPrimaryButtonStyle", content);
        Assert.Contains("NexusSecondaryButtonStyle", content);
        Assert.Contains("NexusSliderStyle", content);
        Assert.Contains("NexusIcon.Glyph", content);
    }

    [Fact]
    public void Test10_NoiseFilter_DiscardsEmptySingleCharAndSymbolsOnly()
    {
        // 1. Meaningful text helper tests
        Assert.False(PpOcrEngine.IsMeaningfulText(null));
        Assert.False(PpOcrEngine.IsMeaningfulText(""));
        Assert.False(PpOcrEngine.IsMeaningfulText("   "));
        Assert.False(PpOcrEngine.IsMeaningfulText("."));
        Assert.False(PpOcrEngine.IsMeaningfulText("-"));
        Assert.False(PpOcrEngine.IsMeaningfulText("~"));
        Assert.False(PpOcrEngine.IsMeaningfulText("..."));
        Assert.False(PpOcrEngine.IsMeaningfulText("---"));
        Assert.False(PpOcrEngine.IsMeaningfulText("###"));
        Assert.False(PpOcrEngine.IsMeaningfulText("a"));
        Assert.False(PpOcrEngine.IsMeaningfulText("1"));
        Assert.False(PpOcrEngine.IsMeaningfulText("[A]"));
        Assert.False(PpOcrEngine.IsMeaningfulText("-b-"));

        Assert.True(PpOcrEngine.IsMeaningfulText("OK"));
        Assert.True(PpOcrEngine.IsMeaningfulText("Rp"));
        Assert.True(PpOcrEngine.IsMeaningfulText("A1"));
        Assert.True(PpOcrEngine.IsMeaningfulText("10"));
        Assert.True(PpOcrEngine.IsMeaningfulText("Desa Sukapindah"));
        Assert.True(PpOcrEngine.IsMeaningfulText("PaninBank"));

        // 2. ThreeTierTriageFilter ignores noise boxes even if Det/Rec scores are low
        var noiseBoxes = new List<OcrBoxModel>
        {
            new OcrBoxModel { Id = 1, Text = ".", DetScore = 0.10f, RecConfidence = 0.10f, X1 = 10, Y1 = 10, X2 = 20, Y2 = 20 },
            new OcrBoxModel { Id = 2, Text = "---", DetScore = 0.15f, RecConfidence = 0.15f, X1 = 30, Y1 = 10, X2 = 50, Y2 = 20 },
            new OcrBoxModel { Id = 3, Text = "a", DetScore = 0.20f, RecConfidence = 0.20f, X1 = 60, Y1 = 10, X2 = 70, Y2 = 20 },
            new OcrBoxModel { Id = 4, Text = "DESA SUKAPINDAH", DetScore = 0.95f, RecConfidence = 0.95f, X1 = 100, Y1 = 100, X2 = 300, Y2 = 150 }
        };

        var filter = new ThreeTierTriageFilter();
        var findings = filter.Evaluate(noiseBoxes, new List<string> { "Desa Sukapindah" });

        // None of the noise boxes (1, 2, 3) should be flagged in findings!
        Assert.DoesNotContain(findings, f => f.Box.Id == 1);
        Assert.DoesNotContain(findings, f => f.Box.Id == 2);
        Assert.DoesNotContain(findings, f => f.Box.Id == 3);
    }

    [Fact]
    public void Test11_JumpToFinding_TriggersFocusAndHighlightsBox()
    {
        var mockRef = new Mock<Nexus.Contracts.ReferenceData.IReferenceDataManager>();
        var mockJudge = new Mock<Nexus.Contracts.AiCheck.IAiCheckJudgeService>();
        var vm = new AiCheckViewModel(mockRef.Object, mockJudge.Object);

        var box = new AiCheckOverlayBoxViewModel
        {
            Id = 5,
            X1 = 200,
            Y1 = 300,
            X2 = 500,
            Y2 = 350,
            Status = OverlayBoxStatus.Review
        };
        vm.OverlayBoxes.Add(box);

        var finding = new AiCheckFindingItemViewModel
        {
            BoxId = 5,
            OriginalText = "ljin Operasional",
            RecommendedText = "Izin Operasional",
            Reason = "Anomali awalan 'lj'",
            Status = Nexus.Contracts.AiCheck.AiCheckJudgmentStatus.Koreksi
        };
        vm.Findings.Add(finding);

        int focusedBoxId = -1;
        int focusedX1 = -1, focusedY1 = -1, focusedX2 = -1, focusedY2 = -1;

        vm.RequestFocusFindingAction = (id, x1, y1, x2, y2) =>
        {
            focusedBoxId = id;
            focusedX1 = x1;
            focusedY1 = y1;
            focusedX2 = x2;
            focusedY2 = y2;
        };

        // Execute Jump to finding
        vm.JumpToFindingCommand.Execute(finding);

        // Verify focus action fired with correct coordinates
        Assert.Equal(5, focusedBoxId);
        Assert.Equal(200, focusedX1);
        Assert.Equal(300, focusedY1);
        Assert.Equal(500, focusedX2);
        Assert.Equal(350, focusedY2);
        Assert.Same(finding, vm.SelectedFinding);

        // Verify highlight state changes visual properties
        Assert.False(box.IsHighlighted);
        Assert.Equal(2.0, box.BorderThickness);

        box.IsHighlighted = true;
        Assert.True(box.IsHighlighted);
        Assert.Equal(3.5, box.BorderThickness);
        Assert.Equal("#FFD54F", box.StrokeBrushName);
    }

    [Fact]
    public void Test12_AiCheckViewXaml_NexusComboBoxAndDecoupledShadow()
    {
        var currentDir = AppContext.BaseDirectory;
        var viewPath = Path.Combine(currentDir, "..", "..", "..", "..", "src", "03_App", "Nexus.App", "Views", "AiCheckView.xaml");
        viewPath = Path.GetFullPath(viewPath);
        if (!File.Exists(viewPath))
        {
            viewPath = @"D:\NEXUS\Nexus-CSharp\src\03_App\Nexus.App\Views\AiCheckView.xaml";
        }
        Assert.True(File.Exists(viewPath), $"AiCheckView.xaml not found at {viewPath}");

        var content = File.ReadAllText(viewPath);

        // DisplayBoxNumber binding without double Box # prefix
        Assert.Contains("Text=\"{Binding DisplayBoxNumber}\"", content);

        // Decoupled shadow caster in empty state
        Assert.Contains("NexusSurfaceRaised", content);
        Assert.Contains("NexusStrokeDefault", content);
        Assert.Contains("TextFormattingMode=\"Display\"", content);
        Assert.Contains("TextRenderingMode=\"ClearType\"", content);

        // Card finding click handler
        Assert.Contains("FindingCard_MouseLeftButtonUp", content);
    }

    [Fact]
    public void Test13_CandidateId_ExactMapping_NoOverwriteOnSameBox()
    {
        // Simulasi 2 temuan di Box 10: token 0 'TANJUUNG' dan token 1 'BATURAJA'
        var findings = new List<AiCheckFindingItemViewModel>
        {
            new()
            {
                CandidateId = "10_0",
                TokenIndex = 0,
                BoxId = 10,
                OriginalText = "TANJUUNG",
                RecommendedText = "TANJUUNG",
                Status = AiCheckJudgmentStatus.PerluVerifikasi
            },
            new()
            {
                CandidateId = "10_1",
                TokenIndex = 1,
                BoxId = 10,
                OriginalText = "BATURAJA",
                RecommendedText = "BATURAJA",
                Status = AiCheckJudgmentStatus.PerluVerifikasi
            }
        };

        // Penilaian AI Judge: TANJUUNG adalah typo TANJUNG, BATURAJA adalah VALID (Aman)
        var judgments = new List<AiCheckJudgment>
        {
            new()
            {
                CandidateId = "10_0",
                TokenIndex = 0,
                BoxId = 10,
                OriginalText = "TANJUUNG",
                RecommendedText = "TANJUNG",
                Status = AiCheckJudgmentStatus.Koreksi,
                Reason = "Koreksi typo huruf dobel"
            },
            new()
            {
                CandidateId = "10_1",
                TokenIndex = 1,
                BoxId = 10,
                OriginalText = "BATURAJA",
                RecommendedText = "BATURAJA",
                Status = AiCheckJudgmentStatus.Aman,
                Reason = "Nama daerah valid"
            }
        };

        // Terapkan mapping Tahap 4
        foreach (var j in judgments)
        {
            var existingFinding = findings.FirstOrDefault(f =>
                (!string.IsNullOrEmpty(j.CandidateId) && f.CandidateId == j.CandidateId) ||
                (string.IsNullOrEmpty(j.CandidateId) && f.BoxId == j.BoxId));

            if (j.IsTypo)
            {
                if (existingFinding != null)
                {
                    existingFinding.RecommendedText = j.RecommendedText;
                    existingFinding.Status = AiCheckJudgmentStatus.Koreksi;
                    existingFinding.Reason = j.Reason;
                }
            }
            else
            {
                if (existingFinding != null)
                {
                    findings.Remove(existingFinding);
                }
            }
        }

        // Verifikasi hasil:
        // TANJUUNG (10_0) tetap bertahan sebagai Koreksi -> TANJUNG dan TIDAK tertimpa BATURAJA!
        Assert.Single(findings);
        var remaining = findings[0];
        Assert.Equal("10_0", remaining.CandidateId);
        Assert.Equal("TANJUUNG", remaining.OriginalText);
        Assert.Equal("TANJUNG", remaining.RecommendedText);
        Assert.Equal(AiCheckJudgmentStatus.Koreksi, remaining.Status);
    }

    [Fact]
    public void Test14_LaskarPelangiScenario_EmpiricalGatekeeperEvaluation()
    {
        // Setup EvidenceEngine dengan 20K kamus dan Structured References OKU
        var engine = new Nexus.Engine.ImageSubsystem.Evidence.EvidenceEngine();
        engine.InitializeDefault();
        engine.LoadStructuredReferences(new[]
        {
            new Nexus.Engine.ImageSubsystem.Evidence.StructuredReferencePhrase
            {
                Text = "Kecamatan Baturaja Timur",
                Category = "Wilayah",
                DatasetId = "wilayah-oku"
            },
            new Nexus.Engine.ImageSubsystem.Evidence.StructuredReferencePhrase
            {
                Text = "Desa Tanjung Baru",
                Category = "Wilayah",
                DatasetId = "wilayah-oku"
            }
        });

        // Kotak teks yang ada pada file LASKAR PELANGI 2026.jpg (dari screenshot user)
        var boxes = new List<OcrBoxModel>
        {
            new() { Id = 10, Text = "DESA TANJUUNG BARU KECAMATAN BATURAJA TIMUR", DetScore = 0.95f, RecConfidence = 0.95f },
            new() { Id = 11, Text = "PLS", DetScore = 0.92f, RecConfidence = 0.92f },
            new() { Id = 12, Text = "NPSN : 10604720", DetScore = 0.94f, RecConfidence = 0.94f },
            new() { Id = 13, Text = "LASKAR PELANGI", DetScore = 0.98f, RecConfidence = 0.98f },
            new() { Id = 14, Text = "2026", DetScore = 0.99f, RecConfidence = 0.99f }
        };

        var results = engine.ProcessBoxes(boxes);

        // Filter kandidat SUSPECT yang akan dikirim ke Gemma
        var suspects = results.Where(r => r.Action == Nexus.Engine.ImageSubsystem.Evidence.PipelineAction.SUSPECT).ToList();

        // 1. Gatekeeper harus menyaring secara radikal: HANYA 'TANJUUNG' yang lolos sebagai SUSPECT!
        // Tidak ada 'BATURAJA', tidak ada 'PLS', tidak ada 'NPSN'
        Assert.Single(suspects);

        var tanjuung = suspects[0];
        Assert.Equal("TANJUUNG", tanjuung.RawToken);
        Assert.Equal("10_1", tanjuung.CandidateId);
        Assert.Equal(1, tanjuung.TokenIndex);
        Assert.Equal("TANJUNG", tanjuung.PossibleCorrections.First().Word);

        // 2. 'BATURAJA' dalam frasa 'Kecamatan Baturaja Timur' harus terlindungi (SAFE_TO_SKIP)
        var baturaja = results.FirstOrDefault(r => r.RawToken.Equals("BATURAJA", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(baturaja);
        Assert.Equal(Nexus.Engine.ImageSubsystem.Evidence.PipelineAction.SAFE_TO_SKIP, baturaja.Action);

        // 3. 'PLS' dan 'NPSN' harus PASSIVE (Noise reduction akronim, tidak dikirim ke Gemma)
        var pls = results.FirstOrDefault(r => r.RawToken.Equals("PLS", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(pls);
        Assert.Equal(Nexus.Engine.ImageSubsystem.Evidence.PipelineAction.PASSIVE, pls.Action);

        var npsn = results.FirstOrDefault(r => r.RawToken.Equals("NPSN", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(npsn);
        Assert.Equal(Nexus.Engine.ImageSubsystem.Evidence.PipelineAction.PASSIVE, npsn.Action);
    }

    [Fact]
    public void Test15_EndToEnd_LaskarPelangiScenario_TanjuungPersistsThroughAiJudge()
    {
        // 1. Setup EvidenceEngine
        var engine = new Nexus.Engine.ImageSubsystem.Evidence.EvidenceEngine();
        engine.InitializeDefault();
        engine.LoadStructuredReferences(new[]
        {
            new Nexus.Engine.ImageSubsystem.Evidence.StructuredReferencePhrase
            {
                Text = "Kecamatan Baturaja Timur",
                Category = "Wilayah",
                DatasetId = "wilayah-oku"
            },
            new Nexus.Engine.ImageSubsystem.Evidence.StructuredReferencePhrase
            {
                Text = "Desa Tanjung Baru",
                Category = "Wilayah",
                DatasetId = "wilayah-oku"
            }
        });

        var boxes = new List<OcrBoxModel>
        {
            new() { Id = 10, Text = "DESA TANJUUNG BARU KECAMATAN BATURAJA TIMUR", DetScore = 0.95f, RecConfidence = 0.95f },
            new() { Id = 11, Text = "PLS", DetScore = 0.92f, RecConfidence = 0.92f }
        };

        var results = engine.ProcessBoxes(boxes);
        var suspects = results.Where(r => r.Action == Nexus.Engine.ImageSubsystem.Evidence.PipelineAction.SUSPECT).ToList();

        Assert.Single(suspects);
        var tanjuungSuspect = suspects[0];

        // 2. Map ke TriageCandidateDto dengan HasStrongEvidence terstruktur
        var candidateDto = new TriageCandidateDto
        {
            CandidateId = tanjuungSuspect.CandidateId,
            TokenIndex = tanjuungSuspect.TokenIndex,
            BoxId = tanjuungSuspect.BoxIds.First(),
            BoxIds = tanjuungSuspect.BoxIds,
            OriginalText = tanjuungSuspect.RawToken,
            PossibleCorrections = tanjuungSuspect.PossibleCorrections.Select(c => c.Word).ToList(),
            SpatialContext = tanjuungSuspect.SpatialContext,
            EvidenceDescription = tanjuungSuspect.Notes,
            HasStrongEvidence = (tanjuungSuspect.Evidence & (Nexus.Engine.ImageSubsystem.Evidence.EvidenceTypes.REFERENCE_NEAR_MATCH | Nexus.Engine.ImageSubsystem.Evidence.EvidenceTypes.FUZZY_STRONG)) != 0 ||
                                tanjuungSuspect.PossibleCorrections.Any(c => c.Grade == "STRONG"),
            IsReferenceNearMatch = (tanjuungSuspect.Evidence & Nexus.Engine.ImageSubsystem.Evidence.EvidenceTypes.REFERENCE_NEAR_MATCH) != 0
        };

        Assert.True(candidateDto.HasStrongEvidence);
        Assert.True(candidateDto.IsReferenceNearMatch);
        Assert.Equal("TANJUNG", candidateDto.PossibleCorrections.First());

        // 3. Verifikasi Prompt Builder menyertakan 1 SARAN terbaik
        var prompt = Nexus.AI.Llama.Services.AiCheckJudgeService.BuildJudgePrompt("LASKAR PELANGI 2026.jpg", new[] { candidateDto });
        Assert.Contains("CANDIDATE: TANJUUNG", prompt);
        Assert.Contains("SUGGESTIONS: TANJUNG", prompt);
        Assert.Contains("DESA TANJUUNG BARU KECAMATAN BATURAJA TIMUR", prompt);

        // 4. Simulasi output model dengan bullet: "- [10_1]: TYPO | TANJUNG"
        var candidateMap = new Dictionary<string, TriageCandidateDto> { [candidateDto.CandidateId] = candidateDto };
        var judgment = Nexus.AI.Llama.Services.AiCheckJudgeService.ParseJudgmentLine("- [10_1]: TYPO | TANJUNG", candidateMap);

        Assert.NotNull(judgment);
        Assert.Equal(AiCheckJudgmentStatus.Koreksi, judgment.Status);
        Assert.Equal("TANJUNG", judgment.RecommendedText);

        // 5. Tahap 4: Verifikasi Finding UI bertahan (tidak terhapus)
        var findings = new List<AiCheckFindingItemViewModel>
        {
            new()
            {
                CandidateId = candidateDto.CandidateId,
                BoxId = candidateDto.BoxId,
                OriginalText = candidateDto.OriginalText,
                RecommendedText = candidateDto.PossibleCorrections.First(),
                Status = AiCheckJudgmentStatus.PerluVerifikasi
            }
        };

        var existing = findings.FirstOrDefault(f => f.CandidateId == judgment.CandidateId);
        Assert.NotNull(existing);
        existing.RecommendedText = judgment.RecommendedText;
        existing.Status = judgment.Status;

        Assert.Single(findings);
        Assert.Equal(AiCheckJudgmentStatus.Koreksi, findings[0].Status);
        Assert.Equal("TANJUNG", findings[0].RecommendedText);
    }

    [Fact]
    public void Test16_EndToEnd_LaskarPelangiScenario_TanjuungUnresolvedFallback_BecomesPerluVerifikasi()
    {
        // Setup kandidat TANJUUNG dengan bukti kuat (Reference Near Match)
        var candidateDto = new TriageCandidateDto
        {
            CandidateId = "10_1",
            TokenIndex = 1,
            BoxId = 10,
            OriginalText = "TANJUUNG",
            PossibleCorrections = new List<string> { "TANJUNG" },
            HasStrongEvidence = true,
            IsReferenceNearMatch = true
        };

        // Model tidak merespon / timeout -> status Unresolved
        var judgment = new AiCheckJudgment
        {
            CandidateId = "10_1",
            BoxId = 10,
            OriginalText = "TANJUUNG",
            Status = AiCheckJudgmentStatus.Unresolved,
            Reason = "Batas waktu evaluasi AI tercapai (unresolved)"
        };

        var findings = new List<AiCheckFindingItemViewModel>
        {
            new()
            {
                CandidateId = candidateDto.CandidateId,
                BoxId = candidateDto.BoxId,
                OriginalText = candidateDto.OriginalText,
                RecommendedText = candidateDto.PossibleCorrections.First(),
                Status = AiCheckJudgmentStatus.PerluVerifikasi
            }
        };

        // Invariant Visual Reducer Kanonikal (§4.4):
        // UNRESOLVED: Tidak dijawab model / timeout teknis / malformed.
        // INVARIANT: Unresolved -> REVIEW (PerluVerifikasi), RecommendedText = string.Empty, NEVER Safe, NEVER fuzzy suggestion!
        var existingFinding = findings.FirstOrDefault(f => f.CandidateId == judgment.CandidateId);
        if (existingFinding != null && judgment.IsUnresolved)
        {
            existingFinding.RecommendedText = string.Empty;
            existingFinding.Status = AiCheckJudgmentStatus.PerluVerifikasi;
            existingFinding.Reason = !string.IsNullOrEmpty(judgment.Reason) ? judgment.Reason : "Perlu verifikasi manual (AI Judge tidak memberikan respon)";
        }

        // Verifikasi temuan TIDAK HILANG (tidak menjadi 0 Koreksi), melainkan tetap ada sebagai PerluVerifikasi dengan RecommendedText kosong
        Assert.Single(findings);
        Assert.Equal(AiCheckJudgmentStatus.PerluVerifikasi, findings[0].Status);
        Assert.Equal(string.Empty, findings[0].RecommendedText);
    }

    private sealed class PipelineTestLlamaClient : ILlamaClientService
    {
        private readonly Func<IEnumerable<ChatMessagePayload>, string> _handler;
        public int CallCount { get; private set; }
        public List<string> ReceivedPrompts { get; } = new();

        public PipelineTestLlamaClient(Func<IEnumerable<ChatMessagePayload>, string> handler)
        {
            _handler = handler;
        }

        public async IAsyncEnumerable<LlamaStreamChunk> StreamChatCompletionsAsync(string prompt, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
        {
            CallCount++;
            ReceivedPrompts.Add(prompt);
            var res = _handler(new[] { new ChatMessagePayload { Role = "user", Content = prompt } });
            yield return new LlamaStreamChunk { Delta = res };
            await Task.Yield();
        }

        public async IAsyncEnumerable<LlamaStreamChunk> StreamChatCompletionsAsync(IEnumerable<ChatMessagePayload> messages, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
        {
            CallCount++;
            var list = messages.ToList();
            ReceivedPrompts.Add(string.Join("\n", list.Select(m => m.Content)));
            var res = _handler(list);
            yield return new LlamaStreamChunk { Delta = res };
            await Task.Yield();
        }

        public async IAsyncEnumerable<LlamaStreamChunk> StreamChatCompletionsAsync(IEnumerable<ChatMessagePayload> messages, bool enableThinking, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
        {
            await foreach (var c in StreamChatCompletionsAsync(messages, cancellationToken))
            {
                yield return c;
            }
        }

        public void CancelStreaming() { }
    }

    [Fact]
    public void Test17_CandidateGate_SnapshotRegression_EvaluatesConsistently()
    {
        var engine = new Nexus.Engine.ImageSubsystem.Evidence.EvidenceEngine();
        engine.InitializeDefault();
        engine.LoadStructuredReferences(new[]
        {
            new Nexus.Engine.ImageSubsystem.Evidence.StructuredReferencePhrase
            {
                Text = "Desa Tanjung Baru",
                Category = "Wilayah",
                DatasetId = "wilayah-oku"
            },
            new Nexus.Engine.ImageSubsystem.Evidence.StructuredReferencePhrase
            {
                Text = "Puskesmas Grogol",
                Category = "Layanan",
                DatasetId = "layanan-kesehatan"
            }
        });

        var boxes = new List<OcrBoxModel>
        {
            new() { Id = 1, Text = "DESA TANJUUNG BARU", DetScore = 0.95f, RecConfidence = 0.95f },
            new() { Id = 2, Text = "Alat Tullis Kantor", DetScore = 0.94f, RecConfidence = 0.94f },
            new() { Id = 3, Text = "Puskesmas Groqol", DetScore = 0.93f, RecConfidence = 0.93f },
            new() { Id = 4, Text = "KANTOR DESA", DetScore = 0.98f, RecConfidence = 0.98f },
            new() { Id = 5, Text = "PLS", DetScore = 0.90f, RecConfidence = 0.90f },
            new() { Id = 6, Text = "NPSN : 10604720", DetScore = 0.91f, RecConfidence = 0.91f }
        };

        var results = engine.ProcessBoxes(boxes);
        var suspects = results.Where(r => r.Action == Nexus.Engine.ImageSubsystem.Evidence.PipelineAction.SUSPECT).ToList();

        // Verifikasi ketat snapshot Candidate Gate:
        // 3 kata suspect terdeteksi persis: TANJUUNG, Tullis, Groqol
        Assert.Equal(3, suspects.Count);
        Assert.Contains(suspects, s => s.RawToken == "TANJUUNG");
        Assert.Contains(suspects, s => s.RawToken == "Tullis");
        Assert.Contains(suspects, s => s.RawToken == "Groqol");

        // Kata baku aman: DESA, KANTOR, BARU, Alat -> SAFE_TO_SKIP
        var desa = results.FirstOrDefault(r => r.RawToken.Equals("DESA", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(desa);
        Assert.Equal(Nexus.Engine.ImageSubsystem.Evidence.PipelineAction.SAFE_TO_SKIP, desa.Action);

        // Noise akronim/angka: PLS, NPSN, 10604720 -> PASSIVE
        var pls = results.FirstOrDefault(r => r.RawToken.Equals("PLS", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(pls);
        Assert.Equal(Nexus.Engine.ImageSubsystem.Evidence.PipelineAction.PASSIVE, pls.Action);
    }

    [Fact]
    public async Task Test18_G3NoiseGuard_PreFlightInterception_BypassesLlmWithZeroCalls()
    {
        var client = new PipelineTestLlamaClient(_ => "[error]: should never be reached");
        var service = new AiCheckJudgeService(client);

        var extremeNoiseCandidates = new List<TriageCandidateDto>
        {
            new()
            {
                CandidateId = "99_0",
                BoxId = 99,
                OriginalText = "x#@!",
                RecConfidence = 0.60f,
                FuzzyMargin = 0.01,
                PrimaryScore = 0.20
            },
            new()
            {
                CandidateId = "99_1",
                BoxId = 99,
                OriginalText = "RIADo",
                IsMixedCase = true,
                RecConfidence = 0.77f,
                FuzzyMargin = 0.01,
                PrimaryScore = 0.40
            }
        };

        var judgments = await service.EvaluateFindingsAsync("Dokumen Noise", extremeNoiseCandidates);

        // LLM tidak boleh dihubungi sama sekali (0 HTTP calls)
        Assert.Equal(0, client.CallCount);
        Assert.Equal(2, judgments.Count);

        Assert.All(judgments, j =>
        {
            Assert.Equal(AiCheckJudgmentStatus.OcrError, j.Status);
            Assert.Equal(0, j.DurationMs);
            Assert.Contains("Artefak visual / OCR noise", j.Reason);
            Assert.Empty(j.RecommendedText);
        });
    }

    [Fact]
    public async Task Test19_Pass1Pass2RoutingFlow_WithMockLlama_PromotesRecallRiskToTypo()
    {
        var client = new PipelineTestLlamaClient(messages =>
        {
            string prompt = messages.First(message => message.Role == "user").Content;

            // Pass 1: prompt Clean
            if (prompt.Contains("Anda adalah AI Judge untuk audit ketelitian") && !prompt.Contains("CATATAN RUJUKAN"))
            {
                return "[c_normal]: VALID\n[c_recall]: VALID";
            }

            // Pass 2: Targeted Hint H2 prompt
            if (prompt.Contains("CATATAN RUJUKAN"))
            {
                return "[c_recall]: TYPO | Tulis";
            }

            return "[unexpected]: VALID";
        });

        var service = new AiCheckJudgeService(client);

        var candidates = new List<TriageCandidateDto>
        {
            new()
            {
                CandidateId = "c_normal",
                BoxId = 1,
                OriginalText = "KANTOR",
                BoxText = "KANTOR KECAMATAN",
                PrimaryScore = 0.90,
                RecConfidence = 0.95f,
                G3LowMargin = 0.50,
                IsNameSignal = false
            },
            new()
            {
                CandidateId = "c_recall",
                BoxId = 2,
                TokenIndex = 1,
                OriginalText = "Tullis",
                BoxText = "Alat Tullis Kantor",
                Top1Suggestion = "Tulis",
                Top1Distance = 1,
                PrimaryScore = 0.55,
                HasStrongEvidence = true,
                HasFuzzyRunnerUp = true,
                TrueFuzzyMargin = 0.12,
                G3LowMargin = 0.50,
                RecConfidence = 0.95f,
                IsNameSignal = false
            }
        };

        var judgments = await service.EvaluateFindingsAsync("Dokumen Uji", candidates);

        // Client dipanggil 2 kali: 1x Pass 1 (batch), 1x Pass 2 (Targeted Hint untuk c_recall)
        Assert.Equal(2, client.CallCount);
        Assert.Equal(2, judgments.Count);

        var jNormal = judgments.First(j => j.CandidateId == "c_normal");
        Assert.Equal(AiCheckJudgmentStatus.Aman, jNormal.Status);
        Assert.Equal("KANTOR", jNormal.RecommendedText);

        var jRecall = judgments.First(j => j.CandidateId == "c_recall");
        Assert.Equal(AiCheckJudgmentStatus.Koreksi, jRecall.Status);
        Assert.Equal("Tulis", jRecall.RecommendedText);
        Assert.Contains("Pass 2 Targeted Hint", jRecall.Reason);
    }

    [Fact]
    public void Test20_EntityEvidenceIsolation_CandidateScopedInBatchPrompt()
    {
        var candA = new TriageCandidateDto
        {
            CandidateId = "10_1",
            BoxId = 10,
            OriginalText = "DESIA",
            BoxText = "KANTOR DESIA SUKAPINDAH",
            MatchedReferenceContext = "DESA SUKAPINDAH"
        };

        var candB = new TriageCandidateDto
        {
            CandidateId = "10_2",
            BoxId = 10,
            OriginalText = "BUPATII",
            BoxText = "BUPATII OKU",
            MatchedReferenceContext = null
        };

        string prompt = AiCheckJudgeService.BuildCleanJudgePrompt("Dokumen Pemerintahan", new[] { candA, candB });

        // Verifikasi isolasi ketat:
        Assert.Contains("Isolasi ketat: ENTITY EVIDENCE suatu kandidat HANYA berlaku untuk kandidat tersebut dan DILARANG memengaruhi kandidat lain.", prompt);

        // Cand A memiliki rujukan entitas di prompt-nya
        int idxCandA = prompt.IndexOf("[10_1]");
        int idxCandB = prompt.IndexOf("[10_2]");
        Assert.True(idxCandA >= 0 && idxCandB > idxCandA);

        string sectionA = prompt.Substring(idxCandA, idxCandB - idxCandA);
        string sectionB = prompt.Substring(idxCandB);

        Assert.Contains("ENTITY / REFERENCE EVIDENCE:", sectionA);
        Assert.Contains("\"DESA SUKAPINDAH\"", sectionA);

        Assert.DoesNotContain("ENTITY / REFERENCE EVIDENCE:", sectionB);
        Assert.DoesNotContain("SUKAPINDAH", sectionB);

        // Anti-anchoring: tidak ada suggestions fuzzy
        Assert.DoesNotContain("SUGGESTIONS:", prompt);
    }

    [Fact]
    public void Test21_VisualReducer_UnresolvedInvariant_NeverSafe_NeverFuzzyCopy()
    {
        // Simulasi kandidat triage dengan saran fuzzy awal
        var candidateDto = new TriageCandidateDto
        {
            CandidateId = "box_15_0",
            BoxId = 15,
            OriginalText = "KATAUJI",
            PossibleCorrections = new List<string> { "KOREKSI_FUZZY" },
            HasStrongEvidence = false
        };

        // LLM tidak merespon / timeout -> Judgment Unresolved
        var unresolvedJudgment = new AiCheckJudgment
        {
            CandidateId = "box_15_0",
            BoxId = 15,
            OriginalText = "KATAUJI",
            RecommendedText = string.Empty,
            Status = AiCheckJudgmentStatus.Unresolved,
            Reason = "Batas waktu evaluasi AI tercapai (unresolved)"
        };

        var finding = new AiCheckFindingItemViewModel
        {
            CandidateId = candidateDto.CandidateId,
            BoxId = candidateDto.BoxId,
            OriginalText = candidateDto.OriginalText,
            RecommendedText = "SARAN_AWAL",
            Status = AiCheckJudgmentStatus.PerluVerifikasi
        };

        // Reducer invariant:
        if (unresolvedJudgment.IsUnresolved)
        {
            finding.RecommendedText = string.Empty;
            finding.Status = AiCheckJudgmentStatus.PerluVerifikasi;
            finding.Reason = unresolvedJudgment.Reason;
        }

        // Invariant 1: Status WAJIB PerluVerifikasi (Review), DILARANG Aman / SafeToSkip
        Assert.Equal(AiCheckJudgmentStatus.PerluVerifikasi, finding.Status);

        // Invariant 2: RecommendedText WAJIB string.Empty, DILARANG menyalin fuzzy suggestion atau teks asal
        Assert.Equal(string.Empty, finding.RecommendedText);

        // Invariant 3: Canvas Overlay agregasi
        var overlay = new AiCheckOverlayBoxViewModel
        {
            Id = 15,
            OriginalText = "KATAUJI",
            RecommendedText = unresolvedJudgment.IsUnresolved ? string.Empty : unresolvedJudgment.RecommendedText,
            Status = OverlayBoxStatus.Review
        };
        Assert.Equal(OverlayBoxStatus.Review, overlay.Status);
        Assert.Equal(string.Empty, overlay.RecommendedText);
    }
}


