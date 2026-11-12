using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.App.Views;
using Nexus.PdfUtilities;

namespace Nexus.Integration.Tests;

[Collection(WpfApplicationCollection.Name)]
public class PdfReaderWorkflowUiTests
{
    [Fact]
    public Task NativeReader_ToolsCardsTabsAndConversion_KeepOriginalSources() => WpfAppHost.RunAsync(async () =>
    {
        ThemeManager.ApplyTheme(ThemeMode.Light);
        var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var source=Path.Combine(root,"reader-sample.pdf");
        using(var document=new PdfSharp.Pdf.PdfDocument())
        {
            for(int i=0;i<3;i++)
            {
                var page=document.AddPage();page.Width=PdfSharp.Drawing.XUnit.FromPoint(i==1?700:500);page.Height=PdfSharp.Drawing.XUnit.FromPoint(i==1?350:700);
                using var graphics=PdfSharp.Drawing.XGraphics.FromPdfPage(page);
                graphics.DrawRectangle(PdfSharp.Drawing.XBrushes.SteelBlue,40,40,180+i*20,100);
                graphics.DrawRectangle(PdfSharp.Drawing.XBrushes.Orange,80,220,220,70);
            }
            document.Save(source);
        }
        var visualOutput=Environment.GetEnvironmentVariable("NEXUS_PDF_VISUAL_OUTPUT");
        if(!string.IsNullOrWhiteSpace(visualOutput)){Directory.CreateDirectory(visualOutput);File.Copy(source,Path.Combine(visualOutput,"visual-source.pdf"));}
        var identity=SHA256.HashData(await File.ReadAllBytesAsync(source));
        var shell=new PdfWorkspaceViewModel(new PdfConversionService(),new PdfEngineService());
        var view=new PdfWorkspaceView{DataContext=shell};
        var window=new Window{Content=view,Width=1500,Height=900,Left=-10000,Top=0,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None};
        try
        {
            window.Show();view.UpdateLayout();Save(view,"after-01-add-file");
            var vm=shell.SelectedWorkspace!;vm.AddFiles(new[]{source});await vm.ImportCompletion;
            var documentView=Descendants<PdfUtilitiesView>(view).Single();
            var reader=(PdfReaderView)documentView.FindName("PdfReaderWorkspace");
            await WaitUntil(()=>vm.Reader.Pages.Any(x=>x.Image!=null));
            Assert.Equal(Visibility.Visible,reader.Visibility);Assert.Equal(3,vm.Reader.PageCount);
            Assert.Contains(Descendants<TextBlock>(reader),x=>x.Text==vm.PdfFiles[0].Name);
            Assert.DoesNotContain(Descendants<TextBlock>(reader),x=>x.Text.StartsWith("Nexus.App.ViewModels",StringComparison.Ordinal));
            Assert.Same(vm.PdfFiles,vm.Reader.Sources);Assert.Same(vm.PdfFiles[0],vm.Reader.ActiveDocument);
            Assert.Equal(Visibility.Collapsed,((StackPanel)documentView.FindName("PdfWorkspaceToolbar")).Visibility);
            Assert.Equal(Visibility.Collapsed,((Border)documentView.FindName("PdfConversionFooter")).Visibility);
            Save(view,"after-02-reader-tools");
            foreach(var theme in new[]{ThemeMode.Light,ThemeMode.Dark,ThemeMode.HighContrast})
            {
                ThemeManager.ApplyTheme(theme);view.UpdateLayout();
                Save(view,$"reader-{theme}-150",1.5);
            }
            ThemeManager.ApplyTheme(ThemeMode.Light);
            double expanded=((Border)documentView.FindName("PdfDocumentArea")).ActualWidth;
            vm.ToggleSettingsCommand.Execute(null);view.UpdateLayout();
            Assert.Equal(90,((Grid)documentView.FindName("PdfContentGrid")).ColumnDefinitions[1].ActualWidth);
            Assert.True(((Border)documentView.FindName("PdfDocumentArea")).ActualWidth>expanded+200);
            await WaitUntil(()=>vm.Reader.Pages.Any(x=>x.Image!=null));Save(view,"after-03-tools-rail");
            vm.Reader.ZoomPercent=10;vm.Reader.PageNumber=2;
            await Task.Delay(200);Assert.Equal(2,vm.Reader.PageNumber);
            vm.Reader.ZoomPercent=125;
            await WaitUntil(()=>vm.Reader.Pages[1].Image!=null);
            Assert.Equal(125,vm.Reader.ZoomPercent);
            vm.Reader.RotateViewCommand.Execute(90);
            await WaitUntil(()=>vm.Reader.Pages[1].Image!=null);
            Assert.Equal(90,vm.PdfFiles[0].ReaderState.Rotation);
            vm.ToggleDocumentViewCommand.Execute(null);view.UpdateLayout();
            Assert.Equal(Visibility.Visible,((ScrollViewer)documentView.FindName("PdfFileScrollViewer")).Visibility);
            Assert.All(vm.Reader.Pages,x=>Assert.Null(x.Image));Save(view,"after-04-cards");
            vm.SelectDocumentCommand.Execute(vm.PdfFiles[0]);view.UpdateLayout();
            vm.SelectToolCommand.Execute(vm.Tools.Single(x=>x.Id==PdfToolId.ImageExport));
            var tool=shell.SelectedWorkspace!;Assert.NotSame(vm,tool);await tool.ImportCompletion;view.UpdateLayout();
            documentView=Descendants<PdfUtilitiesView>(view).Single();
            Assert.Null(vm.SelectedTool);Assert.False(tool.IsDocumentView);Assert.Equal("Image",tool.WorkspaceGlyph);
            Assert.Equal("PDF to JPG",tool.WorkspaceTitle);Assert.Equal(2,shell.Workspaces.Count);
            Assert.Equal(125,vm.Reader.ZoomPercent);Assert.Equal(2,vm.Reader.PageNumber);
            Assert.Equal(Visibility.Visible,((ScrollViewer)documentView.FindName("PdfFileScrollViewer")).Visibility);
            tool.SelectedDpiIndex=0;tool.SelectedFormatIndex=1;tool.OutputDirPath=Path.Combine(root,"output");
            Directory.CreateDirectory(tool.OutputDirPath);
            var unrelated=Path.Combine(tool.OutputDirPath,"unrelated.png");await File.WriteAllTextAsync(unrelated,"do not include in results");
            view.UpdateLayout();Assert.True(tool.CanConvert);Assert.True(tool.ConvertCommand.CanExecute(null));
            var processButton=(Button)documentView.FindName("PdfProcessButton");
            await WaitUntil(()=>processButton.IsEnabled);Save(view,"after-06-image-settings");
            tool.ToggleSettingsCommand.Execute(null);view.UpdateLayout();
            Assert.Equal(0,((Grid)documentView.FindName("PdfContentGrid")).ColumnDefinitions[1].ActualWidth);
            tool.ToggleSettingsCommand.Execute(null);view.UpdateLayout();
            Assert.True(processButton.IsEnabled);processButton.Command.Execute(null);await tool.StartConversionAsync();
            var result=Assert.Single(tool.JobResults);Assert.Equal("Berhasil",result.Status);Assert.Equal(3,result.Outputs.Count);Assert.True(tool.HasConverted);
            Assert.All(result.Outputs,path=>{Assert.True(File.Exists(path));Assert.Equal(tool.OutputDirPath,Path.GetDirectoryName(path));});
            Assert.DoesNotContain(unrelated,result.Outputs);Assert.Single(tool.PdfFiles);Save(view,"after-07-conversion");
            var firstPaths=result.Outputs.ToArray();
            int firstWidth=ReadPixelWidth(firstPaths[0]);
            tool.SelectedDpiIndex=1;await tool.StartConversionAsync();
            Assert.Equal(firstPaths,Assert.Single(tool.JobResults).Outputs);
            Assert.Equal(firstWidth*2,ReadPixelWidth(firstPaths[0]));
            Assert.Equal("do not include in results",await File.ReadAllTextAsync(unrelated));
            Assert.Empty(Directory.GetFiles(tool.OutputDirPath,"*.partial"));
            var secondDirectory=Path.Combine(root,"second");Directory.CreateDirectory(secondDirectory);
            var secondSource=Path.Combine(secondDirectory,"second-sample.pdf");File.Copy(source,secondSource);
            tool.AddFiles(new[]{secondSource});await tool.ImportCompletion;
            await tool.StartConversionAsync();
            Assert.Equal(2,tool.JobResults.Count);Assert.All(tool.JobResults,x=>Assert.Equal(3,x.Outputs.Count));
            Assert.Equal(6,tool.JobResults.SelectMany(x=>x.Outputs).Distinct().Count());
            Assert.Equal(tool.OutputDirPath,tool.LastOutputDirectory);
            tool.NamingIndex=2;await tool.StartConversionAsync();
            Assert.Empty(tool.JobResults[0].Outputs);Assert.Contains("ditimpa",tool.JobResults[0].Status);
            Assert.Equal(3,tool.JobResults[1].Outputs.Count);
            Assert.All(result.Outputs,path=>Assert.True(File.Exists(path)));
            shell.AddTabCommand.Execute(null);view.UpdateLayout();Assert.Empty(shell.SelectedWorkspace!.PdfFiles);
            shell.SelectedWorkspace=vm;view.UpdateLayout();Assert.Single(vm.PdfFiles);
            Assert.Equal(125,vm.Reader.ZoomPercent);
            Assert.Equal(2,vm.Reader.PageNumber);
            vm.SelectToolCommand.Execute(vm.Tools.Single(x=>x.Id==PdfToolId.ToWord));
            var word=shell.SelectedWorkspace!;await word.ImportCompletion;view.UpdateLayout();
            Assert.False(word.ConvertCommand.CanExecute(null));Assert.Equal("Document",word.WorkspaceGlyph);Save(view,"after-05-word-settings");
            await shell.CloseWorkspaceAsync(tool);Assert.True(File.Exists(source));
            await shell.CloseWorkspaceAsync(vm);Assert.True(File.Exists(source));
            Assert.Equal(identity,SHA256.HashData(await File.ReadAllBytesAsync(source)));
        }
        finally{window.Close();foreach(var workspace in shell.Workspaces.ToArray())await shell.CloseWorkspaceAsync(workspace);Directory.Delete(root,true);}
    });
    private static int ReadPixelWidth(string path)
    {
        using var stream=File.OpenRead(path);
        return BitmapDecoder.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad).Frames[0].PixelWidth;
    }
    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline=DateTime.UtcNow.AddSeconds(8);
        while(!condition()&&DateTime.UtcNow<deadline)await Task.Delay(50);
        Assert.True(condition(),"Reader tidak menghasilkan halaman terlihat dalam batas waktu.");
    }
    private static void Save(FrameworkElement view,string name,double scale=1)
    {
        var output=Environment.GetEnvironmentVariable("NEXUS_PDF_VISUAL_OUTPUT");if(string.IsNullOrWhiteSpace(output))return;
        Directory.CreateDirectory(output);view.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)(view.ActualWidth*scale),(int)(view.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(view);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name+".png"));encoder.Save(stream);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T match)yield return match;foreach(var nested in Descendants<T>(child))yield return nested;}}
}
