using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Nexus.Engine.ImageSubsystem.Ocr;
using Nexus.Engine.ImageSubsystem.Triage;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Engine.ImageSubsystem.Tests;

[Collection("CPU timing")]
public class ThreeTierTriageFilterTests
{
    private readonly ITestOutputHelper _output;

    public ThreeTierTriageFilterTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Test_Layer1_LowConfidence_Triggered()
    {
        var filter = new ThreeTierTriageFilter();
        var boxes = new List<OcrBoxModel>
        {
            new() { Id = 1, Text = "Bersih", DetScore = 0.95f, RecConfidence = 0.98f },
            new() { Id = 2, Text = "Buram", DetScore = 0.55f, RecConfidence = 0.95f },
            new() { Id = 3, Text = "Rusak", DetScore = 0.92f, RecConfidence = 0.45f }
        };

        var findings = filter.Evaluate(boxes, (IReadOnlyList<string>?)null);

        Assert.Equal(2, findings.Count);
        Assert.True((findings[0].LayerFlags & TriageLayerFlags.Layer1_LowDetScore) != 0);
        Assert.True((findings[1].LayerFlags & TriageLayerFlags.Layer1_LowRecConfidence) != 0);
    }

    [Fact]
    public void Test_Layer2A_CatchesHighConfidence_Ljin_Typo()
    {
        var filter = new ThreeTierTriageFilter();
        var boxes = new List<OcrBoxModel>
        {
            new()
            {
                Id = 32,
                Text = "Surat ljin Iklan: NOMOR : 771/5.5/PI.02/08/2026",
                DetScore = 0.95f,
                RecConfidence = 0.99f // Confidence sangat tinggi!
            }
        };

        var findings = filter.Evaluate(boxes, (IReadOnlyList<string>?)null);

        Assert.Single(findings);
        Assert.True((findings[0].LayerFlags & TriageLayerFlags.Layer2A_SuspiciousPrefix) != 0);
        Assert.Contains("lj", findings[0].Reasons.First());
    }

    [Fact]
    public void Test_Layer2B_CatchesCamelCaseConcatenatedText()
    {
        var filter = new ThreeTierTriageFilter();
        var boxes = new List<OcrBoxModel>
        {
            new()
            {
                Id = 1,
                Text = "YangMaJuMdaS",
                DetScore = 0.91f,
                RecConfidence = 0.45f
            }
        };

        var findings = filter.Evaluate(boxes, (IReadOnlyList<string>?)null);

        Assert.Single(findings);
        Assert.True((findings[0].LayerFlags & TriageLayerFlags.Layer2B_IrregularCasing) != 0);
        Assert.True((findings[0].LayerFlags & TriageLayerFlags.Layer1_LowRecConfidence) != 0);
    }

    [Fact]
    public void Gate2_Empirical_ThreeTierTriage_518ScaleAndExactMatchTrumpsNearMiss()
    {
        _output.WriteLine("=== RE-VERIFIKASI EMPIRIS GATE 2: THREE-TIER TRIAGE 518 ENTRI ===");
        var refDb = Build518RealisticReferenceEntities();
        _output.WriteLine($"Database referensi siap: {refDb.Count} entri.");

        var filter = new ThreeTierTriageFilter();

        // Siapkan simulasi kotak teks nyata Pilkades & Panin dengan injected typo
        var testBoxes = new List<OcrBoxModel>
        {
            // Kasus 1: Exact match entitas (tidak boleh kena near-miss Desa SukaIndah)
            new() { Id = 1, Text = "Kepala Desa Sukapindah", DetScore = 0.94f, RecConfidence = 1.00f },
            new() { Id = 2, Text = "Desa Sukapindah", DetScore = 0.93f, RecConfidence = 0.99f },
            new() { Id = 3, Text = "PANITIA PEMILIHAN KEPALA DESA", DetScore = 0.96f, RecConfidence = 1.00f },
            new() { Id = 4, Text = "KAB. OGAN KOMERING ULU", DetScore = 0.94f, RecConfidence = 1.00f },

            // Kasus 2: Injected typo pada nama faskes (Puskesmas Groqol vs Puskesmas Grogol)
            new() { Id = 5, Text = "Puskesmas Groqol", DetScore = 0.98f, RecConfidence = 1.00f },

            // Kasus 3: Injected typo pada nama kecamatan (Kecamatan Kedaton Peninjauan Rava vs Raya)
            new() { Id = 6, Text = "KEC. KEDATON PENINJAUAN RAVA", DetScore = 0.95f, RecConfidence = 1.00f },

            // Kasus 4: Typo leksikal ber-confidence tinggi dari sampel Panin
            new() { Id = 7, Text = "Surat ljin Iklan", DetScore = 0.95f, RecConfidence = 0.99f },

            // Kasus 5: Teks kaligrafi buram dari sampel Pilkades
            new() { Id = 8, Text = "YangMaJuMdaS", DetScore = 0.91f, RecConfidence = 0.45f },

            // Kasus 6: Teks bersih umum (disclaimer hukum, tanggal, nominal uang)
            new() { Id = 9, Text = "PT. Bank Panin, Tbk berizin dan diawasi oleh Otoritas Jasa Keuangan", DetScore = 0.96f, RecConfidence = 1.00f },
            new() { Id = 10, Text = "30 SEPTEMBER 2026", DetScore = 0.95f, RecConfidence = 1.00f },
            new() { Id = 11, Text = "Rp100 juta", DetScore = 0.97f, RecConfidence = 1.00f }
        };

        // JIT Warmup
        _ = filter.Evaluate(testBoxes, refDb);

        var samples = new List<double>();
        var findings = filter.Evaluate(testBoxes, refDb);
        for (int run = 0; run < 20; run++)
        {
            var sw = Stopwatch.StartNew();
            var current = filter.Evaluate(testBoxes, refDb);
            sw.Stop();
            Assert.Equal(findings.Select(f => f.Box.Id), current.Select(f => f.Box.Id));
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }
        var sorted = samples.Order().ToArray();
        var p95 = sorted[18];
        var mean = samples.Average();
        _output.WriteLine("GATE_A_TRIAGE_TIMING " + System.Text.Json.JsonSerializer.Serialize(new
        {
            samples, mean, median = (sorted[9] + sorted[10]) / 2, p95,
            min = sorted[0], max = sorted[^1],
            sampleStdDev = Math.Sqrt(samples.Sum(x => (x - mean) * (x - mean)) / (samples.Count - 1))
        }));

        _output.WriteLine($"Triage warm P95 selesai dalam {p95:F3} ms.");
        _output.WriteLine($"Total box dievaluasi: {testBoxes.Count}, Flagged: {findings.Count}");

        foreach (var f in findings)
        {
            _output.WriteLine($"[FLAGGED] Box {f.Box.Id:D2} '{f.Box.Text}' -> {string.Join("; ", f.Reasons)}");
        }

        // ==========================================
        // VALIDASI GATE 2
        // ==========================================

        // 1. Waktu eksekusi harus < 100 ms untuk 518 entri
        Assert.True(p95 < 100,
            $"Triage warm P95 ({p95:F3} ms) exceeded Gate 2 requirement (100 ms).");

        // 2. Exact Match Trumps Near-Miss: Box 1, 2, 3, 4, 9, 10, 11 TIDAK boleh ter-flag
        var safeBoxIds = new[] { 1, 2, 3, 4, 9, 10, 11 };
        foreach (int id in safeBoxIds)
        {
            Assert.DoesNotContain(findings, f => f.Box.Id == id);
        }

        // 3. Typo Reference Puskesmas Groqol HARUS tertangkap oleh Layer 3
        var groqolFinding = findings.FirstOrDefault(f => f.Box.Id == 5);
        Assert.NotNull(groqolFinding);
        Assert.True((groqolFinding.LayerFlags & TriageLayerFlags.Layer3_ReferenceMismatch) != 0);
        Assert.Equal("Puskesmas Grogol", groqolFinding.MatchedReferenceEntity);
        Assert.Equal(1, groqolFinding.LevenshteinDistance);

        // 4. Typo Reference Kecamatan ... Rava HARUS tertangkap oleh Layer 3
        var ravaFinding = findings.FirstOrDefault(f => f.Box.Id == 6);
        Assert.NotNull(ravaFinding);
        Assert.True((ravaFinding.LayerFlags & TriageLayerFlags.Layer3_ReferenceMismatch) != 0);
        Assert.Equal("KEC. KEDATON PENINJAUAN RAYA", ravaFinding.MatchedReferenceEntity);

        // 5. Typo Leksikal 'Surat ljin Iklan' HARUS tertangkap oleh Layer 2A
        var ljinFinding = findings.FirstOrDefault(f => f.Box.Id == 7);
        Assert.NotNull(ljinFinding);
        Assert.True((ljinFinding.LayerFlags & TriageLayerFlags.Layer2A_SuspiciousPrefix) != 0);

        // 6. Kaligrafi 'YangMaJuMdaS' HARUS tertangkap oleh Layer 1 & 2B
        var kaligrafiFinding = findings.FirstOrDefault(f => f.Box.Id == 8);
        Assert.NotNull(kaligrafiFinding);
        Assert.True((kaligrafiFinding.LayerFlags & TriageLayerFlags.Layer1_LowRecConfidence) != 0);
        Assert.True((kaligrafiFinding.LayerFlags & TriageLayerFlags.Layer2B_IrregularCasing) != 0);
    }

    private static List<string> Build518RealisticReferenceEntities()
    {
        var list = new List<string>
        {
            // Entri kunci yang dites
            "Desa Sukapindah",
            "Desa SukaIndah",
            "Puskesmas Grogol",
            "KEC. KEDATON PENINJAUAN RAYA",
            "KAB. OGAN KOMERING ULU",
            "OGAN KOMERING ULU",
            "Tamimi Anwar",
            "Ria Anggara",
            "Sebimbing Sekundang",
            "Baturaja"
        };

        // 143 Desa realistis pola Indonesia/Sumsel
        string[] prefixes = { "Desa", "Kelurahan" };
        string[] bases = { "Sukamaju", "Sukaraja", "Tanjung", "Lubuk", "Bumi", "Rantau", "Gunung", "Batu", "Pagar", "Kota" };
        string[] suffixes = { "Harapan", "Kencana", "Makmur", "Agung", "Batang", "Panjang", "Merak", "Mekar", "Baru", "Jaya" };

        int id = 1;
        for (int b = 0; b < bases.Length; b++)
        {
            for (int s = 0; s < suffixes.Length; s++)
            {
                list.Add($"{prefixes[b % 2]} {bases[b]} {suffixes[s]}");
                id++;
                if (list.Count >= 150) break;
            }
            if (list.Count >= 150) break;
        }

        // 20 Kecamatan
        string[] kecs = {
            "Baturaja Barat", "Baturaja Timur", "Kedaton Peninjauan Raya", "Kecamatan Lubuk Batang",
            "Kecamatan Lubuk Raja", "Kecamatan Lengkiti", "Kecamatan Pengandonan", "Kecamatan Semidang Aji",
            "Kecamatan Sosoh Buay Rayap", "Kecamatan Sinar Peninjauan", "Kecamatan Peninjauan", "Kecamatan Ulu Ogan",
            "Kecamatan Muara Jaya", "Kecamatan Baturaja", "Kecamatan Prabumulih", "Kecamatan Muara Enim",
            "Kecamatan Lahat", "Kecamatan OKU Selatan", "Kecamatan OKU Timur", "Kecamatan Palembang"
        };
        list.AddRange(kecs);

        // 35 Puskesmas / Faskes
        for (int i = 1; i <= 35; i++)
        {
            list.Add($"Puskesmas {bases[i % bases.Length]} {i}");
        }

        // 50 Pejabat & Gelar
        string[] names = { "Budi Santoso", "Agus Setiawan", "Ahmad Fauzi", "Bambang Supriyanto", "Dedi Haryanto" };
        string[] titles = { "S.E.", "S.Pd.", "S.T.", "S.Sos.", "M.Si.", "M.M.", "S.Kep." };
        for (int i = 0; i < 50; i++)
        {
            list.Add($"H. {names[i % names.Length]} {titles[i % titles.Length]}");
        }

        // Semboyan & Istilah Administrasi Resmi
        string[] mottos = {
            "Sebimbing Sekundang", "Bhinneka Tunggal Ika", "Pancasila", "Pemilihan Kepala Desa",
            "Pemerintah Kabupaten Ogan Komering Ulu", "Dinas Kesehatan", "Dinas Pendidikan",
            "Sekretariat Panitia Pemilihan", "Kementerian Dalam Negeri", "Republik Indonesia"
        };
        list.AddRange(mottos);

        // Tambah entri variatif hingga tepat 518 entri
        int padId = 1;
        while (list.Count < 518)
        {
            list.Add($"Entri Referensi Cadangan OKU {padId++}");
        }

        return list;
    }
}
