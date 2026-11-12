using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.PdfUtilities;
using Xunit;

namespace Nexus.Integration.Tests
{
    public class PdfUtilitiesViewModelTests
    {
        [Fact]
        public void SharedStatusBrush_IsFrozenForImportAndCardThreads()
        {
            var field=typeof(PdfUtilitiesViewModel).GetField("GreenBrush",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
            Assert.True(((System.Windows.Media.Brush)field.GetValue(null)!).IsFrozen);
        }

        [Fact]
        public void PdfFileItem_PageCount_StoresAndRetrievesNumericValue()
        {
            var item = new PdfFileItem();
            item.PageCount = 12;
            item.PageCountText = "12 halaman";

            Assert.Equal(12, item.PageCount);
            Assert.Equal("12 halaman", item.PageCountText);
        }

        [Fact]
        public async Task Convert_SinglePdf10Pages_ProgressReaches100WithoutDoubleScaling()
        {
            var mockEngine = new Mock<IPdfEngineService>();
            var mockConversion = new Mock<IPdfConversionService>();

            mockConversion
                .Setup(c => c.PreflightAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<IReadOnlyList<int>?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PdfPreflightResult { IsSafe = true });

            var reportedOverall = new List<double>();

            mockConversion
                .Setup(c => c.ConvertAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<IReadOnlyList<int>?>(),
                    It.IsAny<IProgress<PdfConversionProgressReport>?>(),
                    It.IsAny<CancellationToken>()))
                .Returns<string, string, string, int, int, int, IReadOnlyList<int>?, IProgress<PdfConversionProgressReport>?, CancellationToken>(
                    (pdfPath, outDir, fmt, dpi, q, naming, pages, progress, ct) =>
                    {
                        for (int page = 1; page <= 10; page++)
                        {
                            progress?.Report(new PdfConversionProgressReport
                            {
                                CurrentPage = page,
                                TotalPages = 10,
                                OutputFilePath = Path.Combine(outDir, $"page_{page}.png"),
                                PhaseText = $"Render halaman {page} dari 10..."
                            });
                        }
                        return Task.CompletedTask;
                    });

            var vm = new PdfUtilitiesViewModel(mockConversion.Object, mockEngine.Object);
            vm.SelectedTool=vm.Tools.Single(x=>x.Id==PdfToolId.ImageExport);

            // Add item directly
            var fileItem = new PdfFileItem
            {
                Path = @"C:\Test\doc.pdf",
                Name = "doc.pdf",
                PageCount = 10,
                PageCountText = "10 halaman"
            };

            // Use reflection to add to _pdfFiles or execute conversion
            var pdfFilesField = typeof(PdfUtilitiesViewModel).GetField("_pdfFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(pdfFilesField);
            var files = (System.Collections.ObjectModel.ObservableCollection<PdfFileItem>)pdfFilesField!.GetValue(vm)!;
            files.Add(fileItem);

            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PdfUtilitiesViewModel.ProgressValue))
                {
                    reportedOverall.Add(vm.ProgressValue);
                }
            };

            vm.OutputDirPath = @"C:\Test\Output";
            await (Task)typeof(PdfUtilitiesViewModel).GetMethod("ConvertPdfAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(vm, null)!;

            Assert.Equal(100.0, vm.ProgressValue);
            Assert.Equal("100%", vm.ProgressPctText);
            Assert.NotEmpty(reportedOverall);
            // Verify progress reached beyond 10% during conversion (in the old broken code, maximum progress during loop was 10%)
            Assert.Contains(reportedOverall, p => p >= 50.0);
        }

        [Fact]
        public async Task Convert_MultiPdf_OverallProgressCalculatedCorrectlyAcrossAllFiles()
        {
            var mockEngine = new Mock<IPdfEngineService>();
            var mockConversion = new Mock<IPdfConversionService>();

            mockConversion
                .Setup(c => c.PreflightAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<IReadOnlyList<int>?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PdfPreflightResult { IsSafe = true });

            mockConversion
                .Setup(c => c.ConvertAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<IReadOnlyList<int>?>(),
                    It.IsAny<IProgress<PdfConversionProgressReport>?>(),
                    It.IsAny<CancellationToken>()))
                .Returns<string, string, string, int, int, int, IReadOnlyList<int>?, IProgress<PdfConversionProgressReport>?, CancellationToken>(
                    (pdfPath, outDir, fmt, dpi, q, naming, pages, progress, ct) =>
                    {
                        for (int page = 1; page <= 5; page++)
                        {
                            progress?.Report(new PdfConversionProgressReport
                            {
                                CurrentPage = page,
                                TotalPages = 5,
                                OutputFilePath = Path.Combine(outDir, $"page_{page}.png"),
                                PhaseText = $"Render halaman {page} dari 5..."
                            });
                        }
                        return Task.CompletedTask;
                    });

            var vm = new PdfUtilitiesViewModel(mockConversion.Object, mockEngine.Object);

            var pdfFilesField = typeof(PdfUtilitiesViewModel).GetField("_pdfFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            vm.SelectedTool=vm.Tools.Single(x=>x.Id==PdfToolId.ImageExport);
            var files = (System.Collections.ObjectModel.ObservableCollection<PdfFileItem>)pdfFilesField!.GetValue(vm)!;

            // 2 files with 5 pages each = 10 pages total
            files.Add(new PdfFileItem { Path = @"C:\Test\doc1.pdf", Name = "doc1.pdf", PageCount = 5, PageCountText = "5 halaman" });
            files.Add(new PdfFileItem { Path = @"C:\Test\doc2.pdf", Name = "doc2.pdf", PageCount = 5, PageCountText = "5 halaman" });

            vm.OutputDirPath = @"C:\Test\Output";
            await (Task)typeof(PdfUtilitiesViewModel).GetMethod("ConvertPdfAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(vm, null)!;

            Assert.Equal(100.0, vm.ProgressValue);
            Assert.Equal("100%", vm.ProgressPctText);
        }

        [Fact]
        public async Task Convert_UnsafePreflight_ShowsWarningAndDoesNotStartConversion()
        {
            var mockEngine = new Mock<IPdfEngineService>();
            var mockConversion = new Mock<IPdfConversionService>();
            var mockNotifications = new Mock<INotificationService>();
            mockConversion
                .Setup(c => c.PreflightAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<IReadOnlyList<int>?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PdfPreflightResult
                {
                    IsSafe = false,
                    EstimatedMemoryBytes = 10_000,
                    SelectedDpi = 300,
                    RecommendedMaxDpi = 144,
                    OutputFormat = "Png"
                });

            var vm = new PdfUtilitiesViewModel(mockConversion.Object, mockEngine.Object, mockNotifications.Object);
            vm.SelectedTool=vm.Tools.Single(x=>x.Id==PdfToolId.ImageExport);
            var pdfFilesField = typeof(PdfUtilitiesViewModel).GetField("_pdfFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var files = (System.Collections.ObjectModel.ObservableCollection<PdfFileItem>)pdfFilesField!.GetValue(vm)!;
            files.Add(new PdfFileItem { Path = @"C:\Test\unsafe.pdf", Name = "unsafe.pdf", PageCount = 1 });

            await (Task)typeof(PdfUtilitiesViewModel).GetMethod("ConvertPdfAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(vm, null)!;

            mockConversion.Verify(c => c.ConvertAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<IReadOnlyList<int>?>(), It.IsAny<IProgress<PdfConversionProgressReport>?>(),
                It.IsAny<CancellationToken>()), Times.Never);
            mockNotifications.Verify(n => n.ShowWarning(
                "Resolusi PDF terlalu tinggi",
                "Resolusi PDF terlalu tinggi untuk diekspor dengan aman pada pengaturan saat ini. Disarankan menggunakan maksimal 144 DPI.",
                It.IsAny<TimeSpan?>()), Times.Once);
            Assert.Equal(300, vm.SelectedDpi);
            Assert.False(vm.IsConverting);
        }
    }
}
