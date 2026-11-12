using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using Nexus.Engine.ImageSubsystem.Ocr;
using Nexus.Engine.ImageSubsystem.Services;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Engine.ImageSubsystem.Tests;

[Collection("CPU timing")]
public class PpOcrEngineTests
{
    private readonly ITestOutputHelper _output;

    private static readonly string Sample1Path = File.Exists(@"D:\NEXUS\PILKADES SUKAPINDAH UK. 100 X 150 CM = 3 LEMBAR FINISHING.jpg")
        ? @"D:\NEXUS\PILKADES SUKAPINDAH UK. 100 X 150 CM = 3 LEMBAR FINISHING.jpg"
        : @"D:\NEXUS\test\PILKADES SUKAPINDAH UK. 100 X 150 CM = 3 LEMBAR FINISHING.jpg";
    private static readonly string Sample2Path = File.Exists(@"d:\NEXUS\ChatGPT Image 5 Sep 2026, 15.40.22.png")
        ? @"d:\NEXUS\ChatGPT Image 5 Sep 2026, 15.40.22.png"
        : @"d:\NEXUS\test\ChatGPT Image 5 Sep 2026, 15.40.22.png";

    public PpOcrEngineTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Test_Vocabulary_LoadsSuccessfully()
    {
        using var engine = new PpOcrEngine();
        _output.WriteLine($"Loaded vocabulary count: {engine.VocabCount}");
        Assert.True(engine.VocabCount > 18000, $"Vocabulary count should exceed 18,000 characters, got {engine.VocabCount}");
    }

    [Fact]
    public void Test_MagicBytes_Validation()
    {
        if (File.Exists(Sample1Path))
        {
            var format1 = DesignImagePreprocessor.ValidateFile(Sample1Path);
            Assert.Equal(SupportedDesignFormat.Jpeg, format1);
        }

        if (File.Exists(Sample2Path))
        {
            var format2 = DesignImagePreprocessor.ValidateFile(Sample2Path);
            Assert.Equal(SupportedDesignFormat.Png, format2);
        }
    }

    [Fact]
    public void Gate1_Empirical_Sample1_PilkadesOku()
    {
        Assert.True(File.Exists(Sample1Path), $"Sample 1 not found at {Sample1Path}");

        _output.WriteLine("=== RE-VERIFIKASI EMPIRIS GATE 1: SAMPEL 1 (PILKADES OKU) ===");
        var swTotal = Stopwatch.StartNew();

        using var bmp = DesignImagePreprocessor.LoadAsBitmap(Sample1Path);
        int origW = bmp.Width;
        int origH = bmp.Height;
        _output.WriteLine($"Image loaded: {origW}x{origH}, load elapsed: {swTotal.ElapsedMilliseconds} ms");

        using var engine = new PpOcrEngine();
        long detMs;
        var swDetRec = Stopwatch.StartNew();
        var boxes = engine.Detect(bmp, maxSide: 1536, thresh: 0.20f, boxThresh: 0.45f, unclipRatio: 1.40f, out detMs);
        _output.WriteLine($"Detection finished: {boxes.Count} boxes in {detMs} ms");

        long totalRecMs = 0;
        for (int i = 0; i < boxes.Count; i++)
        {
            engine.RecognizeBox(bmp, boxes[i]);
            boxes[i].Id = i + 1;
            totalRecMs += boxes[i].RecMs;
        }
        swDetRec.Stop();
        swTotal.Stop();

        _output.WriteLine($"Recognition finished in {totalRecMs} ms. Total pipeline: {swDetRec.ElapsedMilliseconds} ms ({swDetRec.ElapsedMilliseconds / 1000.0:F2}s)");

        // Cetak semua box ke output test
        for (int i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            _output.WriteLine(b.ToString());
        }

        // 1. Verifikasi Jumlah Box: Baseline spike = 37-39 box, toleransi 36 - 40 box
        Assert.InRange(boxes.Count, 36, 40);

        // 2. Verifikasi Batas Koordinat Pixel Asli
        foreach (var b in boxes)
        {
            Assert.True(b.X1 >= 0 && b.X1 < origW, $"X1 ({b.X1}) out of bounds [0, {origW})");
            Assert.True(b.Y1 >= 0 && b.Y1 < origH, $"Y1 ({b.Y1}) out of bounds [0, {origH})");
            Assert.True(b.X2 > b.X1 && b.X2 <= origW, $"X2 ({b.X2}) must be > X1 ({b.X1}) and <= {origW}");
            Assert.True(b.Y2 > b.Y1 && b.Y2 <= origH, $"Y2 ({b.Y2}) must be > Y1 ({b.Y1}) and <= {origH}");
        }

        // 3. Verifikasi Teks Kunci Terbaca
        bool hasPendaftaran = boxes.Any(b => b.Text.Contains("PENDAFTARAN") || b.Text.Contains("KEPALA DESA"));
        bool hasSukapindah = boxes.Any(b => b.Text.Contains("SUKAPINDAH"));
        Assert.True(hasPendaftaran, "Teks 'PENDAFTARAN' / 'KEPALA DESA' harus terdeteksi.");
        Assert.True(hasSukapindah, "Teks 'SUKAPINDAH' harus terdeteksi.");

        // 4. Verifikasi Waktu Eksekusi: Baseline spike = 5.58s, batas Gate 1 <= 6.5s
        Assert.True(swDetRec.ElapsedMilliseconds <= 6500,
            $"Waktu inferensi ({swDetRec.ElapsedMilliseconds} ms) melebihi batas Gate 1 (6500 ms).");
    }

    [Fact]
    public void Gate1_Empirical_Sample2_PaninBank()
    {
        Assert.True(File.Exists(Sample2Path), $"Sample 2 not found at {Sample2Path}");

        _output.WriteLine("=== RE-VERIFIKASI EMPIRIS GATE 1: SAMPEL 2 (PANIN BANK) ===");
        var swTotal = Stopwatch.StartNew();

        using var bmp = DesignImagePreprocessor.LoadAsBitmap(Sample2Path);
        int origW = bmp.Width;
        int origH = bmp.Height;
        _output.WriteLine($"Image loaded: {origW}x{origH}, load elapsed: {swTotal.ElapsedMilliseconds} ms");

        using var engine = new PpOcrEngine();
        long detMs;
        var swDetRec = Stopwatch.StartNew();
        var boxes = engine.Detect(bmp, maxSide: 1280, thresh: 0.20f, boxThresh: 0.45f, unclipRatio: 1.40f, out detMs);
        _output.WriteLine($"Detection finished: {boxes.Count} boxes in {detMs} ms");

        long totalRecMs = 0;
        for (int i = 0; i < boxes.Count; i++)
        {
            engine.RecognizeBox(bmp, boxes[i]);
            boxes[i].Id = i + 1;
            totalRecMs += boxes[i].RecMs;
        }
        swDetRec.Stop();
        swTotal.Stop();

        _output.WriteLine($"Recognition finished in {totalRecMs} ms. Total pipeline: {swDetRec.ElapsedMilliseconds} ms ({swDetRec.ElapsedMilliseconds / 1000.0:F2}s)");

        for (int i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            _output.WriteLine(b.ToString());
        }

        // 1. Verifikasi Jumlah Box: Baseline spike = 35 box, toleransi 34 - 36 box
        Assert.InRange(boxes.Count, 34, 36);

        // 2. Verifikasi Batas Koordinat Pixel Asli
        foreach (var b in boxes)
        {
            Assert.True(b.X1 >= 0 && b.X1 < origW, $"X1 ({b.X1}) out of bounds [0, {origW})");
            Assert.True(b.Y1 >= 0 && b.Y1 < origH, $"Y1 ({b.Y1}) out of bounds [0, {origH})");
            Assert.True(b.X2 > b.X1 && b.X2 <= origW, $"X2 ({b.X2}) must be > X1 ({b.X1}) and <= {origW}");
            Assert.True(b.Y2 > b.Y1 && b.Y2 <= origH, $"Y2 ({b.Y2}) must be > Y1 ({b.Y1}) and <= {origH}");
        }

        // 3. Verifikasi Teks Target Typo 'ljin' Terbaca
        bool hasLjin = boxes.Any(b => b.Text.Contains("ljin") || b.Text.Contains("Surat ljin"));
        Assert.True(hasLjin, "Teks 'ljin' / 'Surat ljin' harus terdeteksi untuk target triage Layer 2.");

        // 4. Verifikasi Waktu Eksekusi: Baseline spike = 4.78s, batas Gate 1 <= 6.0s
        Assert.True(swDetRec.ElapsedMilliseconds <= 6000,
            $"Waktu inferensi ({swDetRec.ElapsedMilliseconds} ms) melebihi batas Gate 1 (6000 ms).");
    }
}
