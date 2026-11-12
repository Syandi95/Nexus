using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.Reflection;
using System.IO;
using Moq;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.App.Views;
using Nexus.PdfUtilities;

namespace Nexus.Integration.Tests;

[Collection(WpfApplicationCollection.Name)]
public class PdfFromPdfIntegrationTests
{
    [Fact]
    public void ImageModeCards_ShowFullContentWithEqualHeightsAndVisibleRadioIndicators() => WpfAppHost.Run(() =>
    {
        foreach(var theme in new[]{ThemeMode.Light,ThemeMode.Dark,ThemeMode.HighContrast})
        {
            ThemeManager.ApplyTheme(theme);
            var vm=new PdfUtilitiesViewModel(Mock.Of<IPdfConversionService>(),Mock.Of<IPdfEngineService>());
            vm.SelectedTool=vm.Tools.Single(x=>x.Id==PdfToolId.ImageExport);
            var view=new PdfUtilitiesView{DataContext=vm};
            var window=new Window{Content=view,Width=1100,Height=850,ShowActivated=false,ShowInTaskbar=false,Left=-10000,Top=-10000};
            try
            {
            window.Show();
            view.Measure(new Size(1100,850));view.Arrange(new Rect(0,0,1100,850));view.UpdateLayout();
            var page=(RadioButton)view.FindName("PdfPageExportMode");
            var extract=(RadioButton)view.FindName("PdfExtractImagesMode");
            foreach(bool extraction in new[]{false,true})
            {
                vm.ExtractEmbeddedImages=extraction;view.UpdateLayout();
                string? evidence=Environment.GetEnvironmentVariable("NEXUS_PDF_UI_EVIDENCE");
                if(!string.IsNullOrWhiteSpace(evidence))
                {
                    var panel=(FrameworkElement)view.FindName("PdfSettingsPanel");
                    var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth),
                        (int)Math.Ceiling(panel.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);
                    var drawing=new System.Windows.Media.DrawingVisual();
                    using(var context=drawing.RenderOpen())context.DrawRectangle(new System.Windows.Media.VisualBrush(panel),null,new Rect(panel.RenderSize));
                    bitmap.Render(drawing);
                    var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    Directory.CreateDirectory(evidence);
                    using var stream=File.Create(Path.Combine(evidence,$"{theme}-{(extraction?"extract":"pages")}.png"));encoder.Save(stream);
                }
                Assert.InRange(Math.Abs(page.ActualHeight-extract.ActualHeight),0,0.1);
                foreach(var card in new[]{page,extract})
                {
                    var content=Assert.IsAssignableFrom<FrameworkElement>(card.Content);
                    var bounds=content.TransformToAncestor(card).TransformBounds(new Rect(content.RenderSize));
                    Assert.True(bounds.Top>=0&&bounds.Bottom<=card.ActualHeight,
                        $"{theme}: content {bounds} exceeds card height {card.ActualHeight}.");
                    Assert.InRange(content.DesiredSize.Height,0,card.ActualHeight-card.Padding.Top-card.Padding.Bottom);
                    var indicator=Assert.IsType<System.Windows.Shapes.Ellipse>(card.Template.FindName("RadioRing",card));
                    Assert.True(indicator.IsVisible);Assert.InRange(indicator.ActualWidth,16,24);
                }
                Assert.Equal(extraction,extract.IsChecked);Assert.Equal(!extraction,page.IsChecked);
            }
            }
            finally{window.Content=null;window.Close();}
        }
    });

    [Fact]
    public void PageExportRemainsDefault_ExtractionIsSeparateAndBelowIt() => WpfAppHost.Run(() =>
    {
        var vm=new PdfUtilitiesViewModel(Mock.Of<IPdfConversionService>(),Mock.Of<IPdfEngineService>());
        vm.SelectedTool=vm.Tools.Single(x=>x.Id==PdfToolId.ImageExport);
        var mode=typeof(PdfUtilitiesViewModel).GetProperty("ExtractEmbeddedImages");
        Assert.NotNull(mode);Assert.False((bool)mode!.GetValue(vm)!);
        Assert.Equal(0,vm.SelectedFormatIndex);Assert.Equal(300,vm.SelectedDpi);Assert.Equal(80,vm.QualityValue);
        var view=new PdfUtilitiesView{DataContext=vm};
        view.Measure(new Size(1400,850));view.Arrange(new Rect(0,0,1400,850));view.UpdateLayout();
        var page=Assert.IsType<RadioButton>(view.FindName("PdfPageExportMode"));
        var extract=Assert.IsType<RadioButton>(view.FindName("PdfExtractImagesMode"));
        Assert.True(page.IsChecked);Assert.False(extract.IsChecked);
        Assert.True(page.TranslatePoint(new Point(),view).Y<extract.TranslatePoint(new Point(),view).Y);
        extract.IsChecked=true;view.UpdateLayout();
        Assert.True((bool)mode.GetValue(vm)!);Assert.False(page.IsChecked);
        Assert.Equal(0,vm.SelectedFormatIndex);Assert.Equal(300,vm.SelectedDpi);Assert.Equal(80,vm.QualityValue);
        page.IsChecked=true;Assert.False((bool)mode.GetValue(vm)!);
    });

    [Fact]
    public void OfficeToolsAreSupported_ConvertToAndEditRemainUnavailable()
    {
        var tools=PdfToolDefinition.CreateCatalog();
        foreach(var id in new[]{PdfToolId.ToWord,PdfToolId.ToExcel,PdfToolId.ToPowerPoint})
            Assert.True(tools.Single(x=>x.Id==id).IsAvailable);
        Assert.All(tools.Where(x=>x.Category!=PdfToolCategory.ConvertFromPdf),x=>Assert.False(x.IsAvailable));
        Assert.False(tools.Single(x=>x.Id==PdfToolId.ToPdfA).IsAvailable);
    }

    [Theory]
    [InlineData(PdfToolId.ToWord,"docx")]
    [InlineData(PdfToolId.ToExcel,"xlsx")]
    [InlineData(PdfToolId.ToPowerPoint,"pptx")]
    [InlineData(PdfToolId.ImageExport,"images")]
    public Task NewModesUseOfficeAdapter_WithoutCallingPageRenderer(PdfToolId id,string format) => WpfAppHost.RunAsync(async()=>
    {
        var root=Path.Combine(Path.GetTempPath(),"Nexus-office-test-"+Guid.NewGuid().ToString("N"));
        var pageRenderer=new Mock<IPdfConversionService>(MockBehavior.Strict);
        var adapter=new Mock<IPdfOfficeToolService>();adapter.SetupGet(x=>x.IsAvailable).Returns(true);
        adapter.Setup(x=>x.ConvertAsync(It.IsAny<string>(),root,format,It.IsAny<bool>(),It.IsAny<CancellationToken>()))
            .Returns(async(string input,string destination,string mode,bool sheets,CancellationToken ct)=>
            {
                Directory.CreateDirectory(destination);var file=Path.Combine(destination,mode=="images"?"source_img_1.png":"source."+mode);
                await File.WriteAllTextAsync(file,"completed",ct);return new PdfOfficeToolResult(new[]{file},Array.Empty<string>());
            });
        var vm=new PdfUtilitiesViewModel(pageRenderer.Object,Mock.Of<IPdfEngineService>(),null,adapter.Object);
        vm.SelectedTool=vm.Tools.Single(x=>x.Id==id);vm.ExtractEmbeddedImages=id==PdfToolId.ImageExport;vm.OutputDirPath=root;
        if(vm.SelectedToolSettings!.Find("layout") is PdfChoiceSetting layout)layout.Value="One Sheet";
        Files(vm).Add(new(){Path=@"C:\input\source.pdf",Name="source.pdf",PageCount=2});vm.CanConvert=true;
        try
        {
            Assert.True(vm.CanConvert);await vm.StartConversionAsync();
            adapter.Verify(x=>x.ConvertAsync(@"C:\input\source.pdf",root,format,id!=PdfToolId.ToExcel,It.IsAny<CancellationToken>()),Times.Once);
            pageRenderer.VerifyNoOtherCalls();Assert.False(vm.IsConverting);Assert.Single(vm.JobResults);
            Assert.Single(vm.JobResults[0].Outputs);Assert.True(vm.HasConverted);
        }
        finally {await vm.CloseWorkspaceAsync();if(Directory.Exists(root))Directory.Delete(root,true);}
    });

    [Fact]
    public Task OfficeJobCapturesSettingsAndCancelsWithoutStartingNextFile() => WpfAppHost.RunAsync(async()=>
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter=new Mock<IPdfOfficeToolService>();adapter.SetupGet(x=>x.IsAvailable).Returns(true);
        adapter.Setup(x=>x.ConvertAsync(It.IsAny<string>(),It.IsAny<string>(),"xlsx",false,It.IsAny<CancellationToken>()))
            .Returns(async(string input,string destination,string mode,bool sheets,CancellationToken ct)=>
            {entered.SetResult();await Task.Delay(Timeout.Infinite,ct);return new PdfOfficeToolResult(Array.Empty<string>(),Array.Empty<string>());});
        var renderer=new Mock<IPdfConversionService>(MockBehavior.Strict);
        var vm=new PdfUtilitiesViewModel(renderer.Object,Mock.Of<IPdfEngineService>(),null,adapter.Object);
        vm.SelectedTool=vm.Tools.Single(x=>x.Id==PdfToolId.ToExcel);
        ((PdfChoiceSetting)vm.SelectedToolSettings!.Find("layout")!).Value="One Sheet";
        Files(vm).Add(new(){Path=@"C:\input\a.pdf",Name="a.pdf",PageCount=2});
        Files(vm).Add(new(){Path=@"C:\input\b.pdf",Name="b.pdf",PageCount=2});
        var job=vm.StartConversionAsync();await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ((PdfChoiceSetting)vm.SelectedToolSettings.Find("layout")!).Value="Multiple Sheets";
        vm.CancelConversionCommand.Execute(null);await job;
        Assert.Equal("Dibatalkan",vm.JobResults[0].Status);Assert.StartsWith("Belum diproses",vm.JobResults[1].Status);
        adapter.Verify(x=>x.ConvertAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<bool>(),It.IsAny<CancellationToken>()),Times.Once);
        renderer.VerifyNoOtherCalls();await vm.CloseWorkspaceAsync();
    });

    private static ObservableCollection<PdfFileItem> Files(PdfUtilitiesViewModel vm)=>(ObservableCollection<PdfFileItem>)
        typeof(PdfUtilitiesViewModel).GetField("_pdfFiles",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(vm)!;

    [Fact]
    public async Task RealModuleExtractsEmbeddedPngWithoutPageRasterization()
    {
        var ancestor=new DirectoryInfo(AppContext.BaseDirectory);
        while(ancestor!=null&&!Directory.Exists(Path.Combine(ancestor.FullName,"Nexus-CSharp","src")))ancestor=ancestor.Parent;
        Assert.NotNull(ancestor);
        string dll=Path.Combine(ancestor!.FullName,"Nexus-CSharp","src","02_Modules","Nexus.ConvertFromPdf","bin","Release","net8.0-windows","NexusConverterEngine.dll");
        var service=new PdfOfficeToolService(()=>dll);Assert.True(service.IsAvailable);
        var root=Path.Combine(Path.GetTempPath(),"Nexus-native-extract-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string source=Path.Combine(root,"embedded.pdf");
            using(var bitmap=new SkiaSharp.SKBitmap(20,20))
            {
                bitmap.Erase(SkiaSharp.SKColors.Black);bitmap.SetPixel(8,8,SkiaSharp.SKColors.Red);
                using var png=bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);
                using var stream=new MemoryStream(png.ToArray());
                using var image=PdfSharp.Drawing.XImage.FromStream(stream);
                using var pdf=new PdfSharp.Pdf.PdfDocument();var page=pdf.AddPage();
                using(var graphics=PdfSharp.Drawing.XGraphics.FromPdfPage(page))graphics.DrawImage(image,50,50,100,100);
                pdf.Save(source);
            }
            var before=await File.ReadAllBytesAsync(source);
            var converted=await service.ConvertAsync(source,Path.Combine(root,"out"),"images",true,CancellationToken.None);
            string file=Assert.Single(converted.Files);Assert.Equal("embedded_img_1.png",Path.GetFileName(file));
            using var decoded=SkiaSharp.SKBitmap.Decode(file);Assert.Equal(20,decoded.Width);Assert.Equal(20,decoded.Height);
            Assert.Equal(SkiaSharp.SKColors.Black,decoded.GetPixel(0,0));Assert.Equal(SkiaSharp.SKColors.Red,decoded.GetPixel(8,8));
            Assert.Equal(before,await File.ReadAllBytesAsync(source));
            using(var blank=new PdfSharp.Pdf.PdfDocument()){blank.AddPage();blank.Save(Path.Combine(root,"blank.pdf"));}
            var empty=await service.ConvertAsync(Path.Combine(root,"blank.pdf"),Path.Combine(root,"empty"),"images",true,CancellationToken.None);
            Assert.Empty(empty.Files);Assert.NotEmpty(empty.Warnings);
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public async Task MissingModuleAndPreCancelledJobsDoNotCreateOutputs()
    {
        var root=Path.Combine(Path.GetTempPath(),"Nexus-no-module-"+Guid.NewGuid().ToString("N"));
        var service=new PdfOfficeToolService(()=>Path.Combine(root,"missing.dll"));Assert.False(service.IsAvailable);
        await Assert.ThrowsAsync<FileNotFoundException>(()=>service.ConvertAsync("missing.pdf",root,"docx",true,CancellationToken.None));
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.ConvertAsync("missing.pdf",root,"images",true,cancel.Token));
        Assert.False(Directory.Exists(root));
    }
}
