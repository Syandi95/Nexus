using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Nexus.App.ViewModels;
using Nexus.Contracts.AiCheck;
using Nexus.Contracts.ReferenceData;
using Moq;
using Xunit;

namespace Nexus.Integration.Tests
{
    public class AiCheckStateWorkflowTests
    {
        private (AiCheckViewModel vm, Mock<IReferenceDataManager> mockRef, Mock<IAiCheckJudgeService> mockJudge) CreateViewModel()
        {
            var mockRef = new Mock<IReferenceDataManager>();
            mockRef.Setup(r => r.GetAllDatasetsMetadataAsync(It.IsAny<System.Threading.CancellationToken>()))
                   .ReturnsAsync(new List<ReferenceDatasetMetadata>());

            var mockJudge = new Mock<IAiCheckJudgeService>();
            var vm = new AiCheckViewModel(mockRef.Object, mockJudge.Object);
            return (vm, mockRef, mockJudge);
        }

        [Fact]
        public void EmptyState_Initially_HasNoContent_AndResultsPanelNotVisible()
        {
            var (vm, _, _) = CreateViewModel();

            Assert.True(vm.HasNoContent);
            Assert.False(vm.HasContent);
            Assert.False(vm.HasUploadedFile);
            Assert.False(vm.HasTextContent);
            Assert.False(vm.IsResultsPanelVisible);
            Assert.False(vm.IsInspectionCompleted);
            Assert.False(vm.CanRunCheck);
        }

        [Fact]
        public void MaterialReady_WhenTextLoaded_HasContentIsTrue_AndResultsPanelRemainsCollapsed()
        {
            var (vm, _, _) = CreateViewModel();

            // Set TextContent via private reflection (or paste text simulation)
            var textProp = typeof(AiCheckViewModel).GetProperty(nameof(AiCheckViewModel.TextContent));
            textProp?.SetValue(vm, "Draf materi undangan resmi desa.");

            Assert.True(vm.HasContent);
            Assert.True(vm.HasTextContent);
            Assert.False(vm.HasNoContent);
            Assert.True(vm.CanRunCheck);
            Assert.False(vm.IsResultsPanelVisible);
            Assert.False(vm.IsInspectionCompleted);
        }

        [Fact]
        public void ResultsState_WhenInspectionCompletedSuccessfully_IsResultsPanelVisibleIsTrue()
        {
            var (vm, _, _) = CreateViewModel();

            var isCompletedProp = typeof(AiCheckViewModel).GetProperty(nameof(AiCheckViewModel.IsInspectionCompleted));
            isCompletedProp?.SetValue(vm, true);

            Assert.True(vm.IsInspectionCompleted);
            Assert.False(vm.IsAnalyzing);
            Assert.True(vm.IsResultsPanelVisible);
        }

        [Fact]
        public void ResultsState_WhenZeroFindings_HasNoFindingsIsTrue_AndResultsPanelVisible()
        {
            var (vm, _, _) = CreateViewModel();

            var isCompletedProp = typeof(AiCheckViewModel).GetProperty(nameof(AiCheckViewModel.IsInspectionCompleted));
            isCompletedProp?.SetValue(vm, true);

            Assert.Empty(vm.Findings);
            Assert.True(vm.HasNoFindings);
            Assert.True(vm.IsResultsPanelVisible);
        }

        [Fact]
        public void DismissFinding_WhenMultipleFindingsOnSameBox_OverlayStaysActiveUntilAllDismissed()
        {
            var (vm, _, _) = CreateViewModel();

            // Setup box 10 with two findings: one Koreksi and one PerluVerifikasi
            var overlay = new AiCheckOverlayBoxViewModel
            {
                Id = 10,
                OriginalText = "Test",
                Status = OverlayBoxStatus.Correction
            };
            vm.OverlayBoxes.Add(overlay);

            var finding1 = new AiCheckFindingItemViewModel
            {
                BoxId = 10,
                OriginalText = "Test",
                RecommendedText = "Teks",
                Status = AiCheckJudgmentStatus.Koreksi,
                Reason = "Ejaan perlu diperbaiki."
            };
            var finding2 = new AiCheckFindingItemViewModel
            {
                BoxId = 10,
                OriginalText = "Test",
                RecommendedText = "Tes",
                Status = AiCheckJudgmentStatus.PerluVerifikasi,
                Reason = "Konteks belum cukup untuk memastikan ejaan."
            };

            vm.Findings.Add(finding1);
            vm.Findings.Add(finding2);

            // Apply initial status
            var applyMethod = typeof(AiCheckViewModel).GetMethod("ApplyOverlayBoxStatus", BindingFlags.NonPublic | BindingFlags.Instance);
            applyMethod?.Invoke(vm, new object[] { 10 });
            Assert.Equal(OverlayBoxStatus.Correction, overlay.Status);

            // Dismiss finding1 (Koreksi) -> Box should fall back to Review (since finding2 remains)
            vm.DismissFindingCommand.Execute(finding1);
            Assert.Single(vm.Findings);
            Assert.Equal(OverlayBoxStatus.Review, overlay.Status);

            // Dismiss finding2 (PerluVerifikasi) -> Box should now be Safe
            vm.DismissFindingCommand.Execute(finding2);
            Assert.Empty(vm.Findings);
            Assert.Equal(OverlayBoxStatus.Safe, overlay.Status);
        }

        [Fact]
        public void DismissFinding_KeepsSnapshotStatisticsStable()
        {
            var (vm, _, _) = CreateViewModel();

            var boxProp = typeof(AiCheckViewModel).GetProperty(nameof(AiCheckViewModel.SnapshotBoxCount));
            var findingProp = typeof(AiCheckViewModel).GetProperty(nameof(AiCheckViewModel.SnapshotFindingCount));
            var corrProp = typeof(AiCheckViewModel).GetProperty(nameof(AiCheckViewModel.SnapshotCorrectionCount));

            boxProp?.SetValue(vm, 5);
            findingProp?.SetValue(vm, 2);
            corrProp?.SetValue(vm, 2);

            var finding = new AiCheckFindingItemViewModel
            {
                BoxId = 1,
                OriginalText = "Salah",
                RecommendedText = "Benar",
                Status = AiCheckJudgmentStatus.Koreksi
            };
            vm.Findings.Add(finding);

            // Dismiss finding
            vm.DismissFindingCommand.Execute(finding);

            // Snapshot stats remain unchanged
            Assert.Equal(5, vm.SnapshotBoxCount);
            Assert.Equal(2, vm.SnapshotFindingCount);
            Assert.Equal(2, vm.SnapshotCorrectionCount);

            // But active findings are empty
            Assert.Empty(vm.Findings);
            Assert.Equal(0, vm.AttentionCount);
        }

        [Fact]
        public void Reset_RestoresEmptyState_ClearsAllStateAndSnapshots()
        {
            var (vm, _, _) = CreateViewModel();

            var textProp = typeof(AiCheckViewModel).GetProperty(nameof(AiCheckViewModel.TextContent));
            textProp?.SetValue(vm, "Some text");

            var isCompletedProp = typeof(AiCheckViewModel).GetProperty(nameof(AiCheckViewModel.IsInspectionCompleted));
            isCompletedProp?.SetValue(vm, true);

            var findingProp = typeof(AiCheckViewModel).GetProperty(nameof(AiCheckViewModel.SnapshotFindingCount));
            findingProp?.SetValue(vm, 3);

            Assert.True(vm.HasContent);
            Assert.True(vm.IsResultsPanelVisible);

            // Reset
            vm.ResetCommand.Execute(null);

            Assert.True(vm.HasNoContent);
            Assert.False(vm.HasContent);
            Assert.False(vm.IsInspectionCompleted);
            Assert.False(vm.IsResultsPanelVisible);
            Assert.Equal(0, vm.SnapshotFindingCount);
            Assert.Equal(0, vm.SnapshotBoxCount);
            Assert.Empty(vm.TextContent);
        }

        [Theory]
        [InlineData("Pass 2 Targeted Hint (d=1, fuzzy anomaly)", AiCheckJudgmentStatus.Koreksi, "Ejaan berbeda dari data referensi.")]
        [InlineData("fuzzy anomaly detected", AiCheckJudgmentStatus.PerluVerifikasi, "Konteks belum cukup untuk memastikan ejaan.")]
        [InlineData("d=2 mismatch", AiCheckJudgmentStatus.OcrError, "Kemungkinan artefak pembacaan OCR.")]
        [InlineData("", AiCheckJudgmentStatus.Koreksi, "Ejaan perlu diperbaiki.")]
        [InlineData("Ejaan kata tidak sesuai konteks kalimat.", AiCheckJudgmentStatus.Koreksi, "Ejaan kata tidak sesuai konteks kalimat.")]
        public void NormalizeReason_SanitizesDebugPhrases_AndProducesHumanizedReason(string rawReason, AiCheckJudgmentStatus status, string expected)
        {
            var normalizeMethod = typeof(AiCheckViewModel).GetMethod("NormalizeReason", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(normalizeMethod);

            var result = (string?)normalizeMethod.Invoke(null, new object[] { rawReason, status });
            Assert.Equal(expected, result);
        }

        [Fact]
        public void BuildFindingReason_EntityReferenceEvidence_ProducesContextualReferenceReason()
        {
            var method = typeof(AiCheckViewModel).GetMethod("BuildFindingReason", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var judgment = new AiCheckJudgment
            {
                CandidateId = "4_0",
                OriginalText = "DESIA",
                RecommendedText = "DESA",
                Status = AiCheckJudgmentStatus.Koreksi
            };

            var candidate = new TriageCandidateDto
            {
                CandidateId = "4_0",
                OriginalText = "DESIA",
                MatchedReferenceContext = "DESA SUKAPINDAH",
                PossibleCorrections = new List<string> { "DESA" }
            };

            var reason = (string?)method.Invoke(null, new object[] { judgment, candidate });
            Assert.Equal("\"DESIA\" kemungkinan salah penulisan dari nama wilayah \"DESA SUKAPINDAH\"; terdapat satu huruf tambahan, sehingga bentuk yang disarankan adalah \"DESA\".", reason);
        }

        [Fact]
        public void BuildFindingReason_ReferenceTypo_BaturajaTmur_ExplainsMissingLetterInRegion()
        {
            var method = typeof(AiCheckViewModel).GetMethod("BuildFindingReason", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var judgment = new AiCheckJudgment
            {
                CandidateId = "8_0",
                OriginalText = "Baturaja Tmur",
                RecommendedText = "Baturaja Timur",
                Status = AiCheckJudgmentStatus.Koreksi
            };

            var candidate = new TriageCandidateDto
            {
                CandidateId = "8_0",
                OriginalText = "Baturaja Tmur",
                MatchedReferenceContext = "Baturaja Timur"
            };

            var reason = (string?)method.Invoke(null, new object[] { judgment, candidate });
            Assert.Equal("\"Baturaja Tmur\" kemungkinan salah penulisan dari nama wilayah \"Baturaja Timur\"; terdapat satu huruf yang hilang pada \"Tmur\", sehingga bentuk yang disarankan adalah \"Baturaja Timur\".", reason);
        }

        [Theory]
        [InlineData("Menyadiakan", "Menyediakan", new[] { "MENYEDIAKAN" }, "\"Menyadiakan\" terindikasi salah eja karena berbeda satu huruf dari \"Menyediakan\" yang menjadi acuan; AI Judge mengonfirmasi bahwa bentuk yang benar adalah \"Menyediakan\".")]
        [InlineData("PEMIGLIHAN", "PEMILIHAN", new[] { "PEMILIHAN" }, "\"PEMIGLIHAN\" terindikasi salah eja karena terdapat satu huruf tambahan dibandingkan \"PEMILIHAN\" yang menjadi acuan; AI Judge mengonfirmasi bahwa bentuk yang benar adalah \"PEMILIHAN\".")]
        [InlineData("PERIOHDE", "PERIODE", new[] { "PERIODE" }, "\"PERIOHDE\" terindikasi salah eja karena terdapat satu huruf tambahan dibandingkan \"PERIODE\" yang menjadi acuan; AI Judge mengonfirmasi bahwa bentuk yang benar adalah \"PERIODE\".")]
        [InlineData("PANITIHA", "PANITIA", new[] { "PANITIA" }, "\"PANITIHA\" terindikasi salah eja karena terdapat satu huruf tambahan dibandingkan \"PANITIA\" yang menjadi acuan; AI Judge mengonfirmasi bahwa bentuk yang benar adalah \"PANITIA\".")]
        [InlineData("Terjangkauh", "Terjangkau", new[] { "Terjangkau" }, "\"Terjangkauh\" terindikasi salah eja karena terdapat satu huruf tambahan dibandingkan \"Terjangkau\" yang menjadi acuan; AI Judge mengonfirmasi bahwa bentuk yang benar adalah \"Terjangkau\".")]
        [InlineData("Andah", "Anda", new[] { "Anda" }, "\"Andah\" terindikasi salah eja karena terdapat satu huruf tambahan dibandingkan \"Anda\" yang menjadi acuan; AI Judge mengonfirmasi bahwa bentuk yang benar adalah \"Anda\".")]
        [InlineData("Tullis", "Tulis", new[] { "Tulis" }, "\"Tullis\" terindikasi salah eja karena terdapat satu huruf tambahan dibandingkan \"Tulis\" yang menjadi acuan; AI Judge mengonfirmasi bahwa bentuk yang benar adalah \"Tulis\".")]
        public void BuildFindingReason_DictionaryLexicalEvidence_ProducesContextualReason(
            string orig, string rec, string[] dictSuggestions, string expected)
        {
            var method = typeof(AiCheckViewModel).GetMethod("BuildFindingReason", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var judgment = new AiCheckJudgment
            {
                CandidateId = "1_0",
                OriginalText = orig,
                RecommendedText = rec,
                Status = AiCheckJudgmentStatus.Koreksi
            };

            var candidate = new TriageCandidateDto
            {
                CandidateId = "1_0",
                OriginalText = orig,
                PossibleCorrections = new List<string>(dictSuggestions),
                Top1Suggestion = dictSuggestions.FirstOrDefault()
            };

            var reason = (string?)method.Invoke(null, new object[] { judgment, candidate });
            Assert.Equal(expected, reason);
        }

        [Fact]
        public void BuildFindingReason_PhraseTypo_ComplexChange_DoesNotFalselyClaimSingleLetter()
        {
            var method = typeof(AiCheckViewModel).GetMethod("BuildFindingReason", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var judgment = new AiCheckJudgment
            {
                CandidateId = "9_0",
                OriginalText = "TANGAN TANGAN",
                RecommendedText = "TANDA TANGAN",
                Status = AiCheckJudgmentStatus.Koreksi
            };

            var candidate = new TriageCandidateDto
            {
                CandidateId = "9_0",
                OriginalText = "TANGAN TANGAN",
                PossibleCorrections = new List<string> { "TANDA TANGAN" },
                Top1Suggestion = "TANDA TANGAN"
            };

            var reason = (string?)method.Invoke(null, new object[] { judgment, candidate });
            Assert.NotNull(reason);
            // Memastikan tidak ada klaim keliru bahwa ini sekadar satu huruf
            Assert.DoesNotContain("satu huruf", reason);
            Assert.Equal("\"TANGAN TANGAN\" terindikasi salah eja karena mendekati \"TANDA TANGAN\" yang menjadi acuan ejaan; AI Judge mengonfirmasi bahwa bentuk yang disarankan adalah \"TANDA TANGAN\".", reason);
        }

        [Fact]
        public void BuildFindingReason_TullisWithoutDictionaryEvidence_ProducesJudgeSuggestedCorrectionReason()
        {
            var method = typeof(AiCheckViewModel).GetMethod("BuildFindingReason", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            // Model merekomendasikan "Tulisan" tetapi kamus hanya menyarankan "Tulis"
            var judgment = new AiCheckJudgment
            {
                CandidateId = "2_0",
                OriginalText = "Tullis",
                RecommendedText = "Tulisan",
                Status = AiCheckJudgmentStatus.Koreksi
            };

            var candidate = new TriageCandidateDto
            {
                CandidateId = "2_0",
                OriginalText = "Tullis",
                PossibleCorrections = new List<string> { "Tulis" },
                Top1Suggestion = "Tulis"
            };

            var reason = (string?)method.Invoke(null, new object[] { judgment, candidate });
            // Karena "Tulisan" tidak ada dalam evidence kamus, jujur bahwa ini rekomendasi AI Judge
            Assert.Equal("\"Tullis\" dinilai sebagai kemungkinan salah eja dan AI Judge menyarankan \"Tulisan\" sebagai bentuk perbaikannya.", reason);
        }

        [Fact]
        public void BuildFindingReason_OcrError_ProducesOcrIndicatedReason()
        {
            var method = typeof(AiCheckViewModel).GetMethod("BuildFindingReason", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var judgment = new AiCheckJudgment
            {
                CandidateId = "3_0",
                OriginalText = "TUL1S",
                RecommendedText = "TULIS",
                Status = AiCheckJudgmentStatus.OcrError
            };

            var reason = (string?)method.Invoke(null, new object?[] { judgment, null });
            Assert.Equal("\"TUL1S\" terindikasi sebagai hasil pembacaan teks yang keliru; bentuk pada desain perlu diperiksa kembali secara visual.", reason);
        }

        [Fact]
        public void BuildFindingReason_PerluVerifikasi_WithNameSignal_ProducesNameSignalReason()
        {
            var method = typeof(AiCheckViewModel).GetMethod("BuildFindingReason", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var judgment = new AiCheckJudgment
            {
                CandidateId = "5_0",
                OriginalText = "Suryanto",
                Status = AiCheckJudgmentStatus.PerluVerifikasi
            };

            var candidate = new TriageCandidateDto
            {
                CandidateId = "5_0",
                OriginalText = "Suryanto",
                IsNameSignal = true
            };

            var reason = (string?)method.Invoke(null, new object[] { judgment, candidate });
            Assert.Equal("\"Suryanto\" perlu diverifikasi karena terindikasi sebagai kemungkinan nama atau istilah khusus yang tidak dapat dipastikan secara otomatis.", reason);
        }

        [Fact]
        public void BuildFindingReason_PerluVerifikasi_WithoutNameSignal_ProducesKonteksBelumCukupReason()
        {
            var method = typeof(AiCheckViewModel).GetMethod("BuildFindingReason", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var judgment = new AiCheckJudgment
            {
                CandidateId = "6_0",
                OriginalText = "xyzabc",
                Status = AiCheckJudgmentStatus.PerluVerifikasi
            };

            var candidate = new TriageCandidateDto
            {
                CandidateId = "6_0",
                OriginalText = "xyzabc",
                IsNameSignal = false
            };

            var reason = (string?)method.Invoke(null, new object[] { judgment, candidate });
            Assert.Equal("\"xyzabc\" perlu diverifikasi karena sistem belum memiliki cukup bukti untuk memastikan bentuk ejaannya.", reason);
        }

        [Fact]
        public void BuildFindingReason_Unresolved_ProducesUnresolvedReason()
        {
            var method = typeof(AiCheckViewModel).GetMethod("BuildFindingReason", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var judgment = new AiCheckJudgment
            {
                CandidateId = "7_0",
                OriginalText = "unanswered",
                Status = AiCheckJudgmentStatus.Unresolved
            };

            var reason = (string?)method.Invoke(null, new object?[] { judgment, null });
            Assert.Equal("Pemeriksaan belum memperoleh cukup informasi untuk menentukan koreksi; teks perlu diverifikasi secara manual.", reason);
        }

        [Fact]
        public void BuildFindingReason_NegativeTest_NeverContainsInternalEngineTerms()
        {
            var method = typeof(AiCheckViewModel).GetMethod("BuildFindingReason", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var forbiddenTerms = new[]
            {
                "data leksikal",
                "lexical",
                "entity evidence",
                "reference engine",
                "candidate",
                "r3d",
                "candidate gate",
                "ai pipeline"
            };

            var judgments = new[]
            {
                new AiCheckJudgment { OriginalText = "Menyadiakan", RecommendedText = "Menyediakan", Status = AiCheckJudgmentStatus.Koreksi },
                new AiCheckJudgment { OriginalText = "Baturaja Tmur", RecommendedText = "Baturaja Timur", Status = AiCheckJudgmentStatus.Koreksi },
                new AiCheckJudgment { OriginalText = "Tullis", RecommendedText = "Tulisan", Status = AiCheckJudgmentStatus.Koreksi },
                new AiCheckJudgment { OriginalText = "TUL1S", RecommendedText = "TULIS", Status = AiCheckJudgmentStatus.OcrError },
                new AiCheckJudgment { OriginalText = "Suryanto", Status = AiCheckJudgmentStatus.PerluVerifikasi },
                new AiCheckJudgment { OriginalText = "xyz", Status = AiCheckJudgmentStatus.PerluVerifikasi },
                new AiCheckJudgment { OriginalText = "unanswered", Status = AiCheckJudgmentStatus.Unresolved }
            };

            var candidate = new TriageCandidateDto
            {
                OriginalText = "Test",
                PossibleCorrections = new List<string> { "Menyediakan" },
                MatchedReferenceContext = "Baturaja Timur",
                IsNameSignal = true
            };

            foreach (var j in judgments)
            {
                var reason = (string?)method.Invoke(null, new object[] { j, candidate });
                Assert.NotNull(reason);
                foreach (var term in forbiddenTerms)
                {
                    Assert.DoesNotContain(term, reason, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }
}
