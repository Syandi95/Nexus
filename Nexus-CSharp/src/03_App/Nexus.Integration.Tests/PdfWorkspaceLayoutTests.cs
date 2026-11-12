using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Moq;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.App.Views;
using Nexus.PdfUtilities;

namespace Nexus.Integration.Tests;

[Collection(WpfApplicationCollection.Name)]
public class PdfWorkspaceLayoutTests
{
    [Fact]
    public void FocusOutline_JoinsVerticalEdges_AndEndsOnTheStripBaseline()
    {
        WpfAppHost.Run(() =>
        {
            ThemeManager.ApplyTheme(ThemeMode.Light);
            var shell = new PdfWorkspaceViewModel(Mock.Of<IPdfConversionService>(), Mock.Of<IPdfEngineService>());
            var host = new PdfWorkspaceView { DataContext = shell };
            host.Measure(new Size(1400,850)); host.Arrange(new Rect(0,0,1400,850)); host.UpdateLayout();
            var tabs = (TabControl)host.FindName("PdfWorkspaceTabs");
            var tab = (TabItem)tabs.ItemContainerGenerator.ContainerFromIndex(0);
            var chrome = (Border)tab.Template.FindName("TabChrome",tab);
            chrome.SetResourceReference(Border.BorderBrushProperty,"NexusStrokeFocus");
            host.UpdateLayout();
            var focus = Assert.IsType<SolidColorBrush>(chrome.BorderBrush).Color;
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1400,850,96,96,PixelFormats.Pbgra32);
            bitmap.Render(host);
            var origin = tab.TranslatePoint(new Point(),host);
            double bottom = origin.Y+tab.ActualHeight;
            (double Coverage,double Center) StrokeProfile(double x,double y)
            {
                double coverage = 0, weighted = 0;
                for (int offset=-1;offset<=1;offset++)
                {
                    var pixel = new byte[4];
                    bitmap.CopyPixels(new Int32Rect((int)x+offset,(int)y,1,1),pixel,4,0);
                    double weight = Math.Max(0,(pixel[0]-pixel[2]-2.0)/(focus.B-focus.R-2.0));
                    coverage += weight; weighted += weight*offset;
                }
                return (coverage,weighted/Math.Max(coverage,0.001));
            }
            void AssertContinuousJoin(double x)
            {
                var stem = StrokeProfile(x,bottom-10);
                var curve = StrokeProfile(x,bottom-8);
                // Arc antialiasing spreads a one-pixel stroke; its coverage and center must stay continuous.
                Assert.InRange(curve.Coverage,0.8,1.2);
                Assert.InRange(Math.Abs(curve.Center-stem.Center),0,0.25);
            }
            void AssertSolidOutline(double x,double y)
            {
                var pixel = new byte[4];
                bitmap.CopyPixels(new Int32Rect((int)x,(int)y,1,1),pixel,4,0);
                Assert.InRange(Math.Abs(pixel[0]-focus.B),0,30);
                Assert.InRange(Math.Abs(pixel[1]-focus.G),0,30);
                Assert.InRange(Math.Abs(pixel[2]-focus.R),0,30);
            }
            AssertContinuousJoin(origin.X+8);
            AssertContinuousJoin(origin.X+tab.ActualWidth-9);
            AssertSolidOutline(origin.X,bottom-1);
            AssertSolidOutline(origin.X+tab.ActualWidth-1,bottom-1);
        });
    }

    [Fact]
    public void ActiveTab_JoinsContentWithoutSeparator_AndHasCurvedLowerShoulders()
    {
        WpfAppHost.Run(() =>
        {
            foreach(var theme in new[]{ThemeMode.Light,ThemeMode.Dark,ThemeMode.HighContrast})
            {
                ThemeManager.ApplyTheme(theme);
                var shell=new PdfWorkspaceViewModel(Mock.Of<IPdfConversionService>(),Mock.Of<IPdfEngineService>());
                shell.AddTabCommand.Execute(null);
                var host=new PdfWorkspaceView{DataContext=shell};
                host.SetResourceReference(Control.BackgroundProperty,"NexusSurfaceBase");
                host.Measure(new Size(1400,850));host.Arrange(new Rect(0,0,1400,850));host.UpdateLayout();
                var tabs=(TabControl)host.FindName("PdfWorkspaceTabs");
                var active=(TabItem)tabs.ItemContainerGenerator.ContainerFromIndex(1);
                var origin=active.TranslatePoint(new Point(),host);
                var paper=Assert.IsType<SolidColorBrush>(host.FindResource("NexusSurfaceBase")).Color;
                var strip=Assert.IsType<SolidColorBrush>(host.FindResource("NexusPdfTabStripBackground")).Color;
                foreach(var scale in new[]{1.0,1.5})
                {
                    var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)(1400*scale),(int)(850*scale),96*scale,96*scale,PixelFormats.Pbgra32);
                    bitmap.Render(host);
                    Color Pixel(double x,double y)
                    {
                        var bytes=new byte[4];
                        bitmap.CopyPixels(new Int32Rect((int)(x*scale),(int)(y*scale),1,1),bytes,4,0);
                        return Color.FromArgb(bytes[3],bytes[2],bytes[1],bytes[0]);
                    }
                    double bottom=origin.Y+active.ActualHeight;
                    double center=origin.X+active.ActualWidth/2;
                    Assert.Equal(paper,Pixel(center,bottom-1));
                    Assert.Equal(paper,Pixel(center,bottom));
                    Assert.Equal(paper,Pixel(center,bottom+1));
                    Assert.Equal(strip,Pixel(origin.X+3,bottom-5));
                    Assert.Equal(paper,Pixel(origin.X+7,bottom-1));
                    Assert.Equal(paper,Pixel(origin.X+8,bottom-2));
                    Assert.Equal(paper,Pixel(origin.X+active.ActualWidth-9,bottom-2));
                }
            }
        });
    }

    [Fact]
    public void AdjacentTabBodies_Touch_AndHoverKeepsCurvedShoulders()
    {
        WpfAppHost.Run(() =>
        {
            var shell = new PdfWorkspaceViewModel(Mock.Of<IPdfConversionService>(), Mock.Of<IPdfEngineService>());
            shell.AddTabCommand.Execute(null);
            var host = new PdfWorkspaceView { DataContext = shell };
            host.Measure(new Size(1400,850)); host.Arrange(new Rect(0,0,1400,850)); host.UpdateLayout();
            var tabs = (TabControl)host.FindName("PdfWorkspaceTabs");
            var first = (TabItem)tabs.ItemContainerGenerator.ContainerFromIndex(0);
            var second = (TabItem)tabs.ItemContainerGenerator.ContainerFromIndex(1);
            var firstBody = (Border)first.Template.FindName("TabChrome",first);
            var secondBody = (Border)second.Template.FindName("TabChrome",second);
            var right = firstBody.TranslatePoint(new Point(firstBody.ActualWidth,0),host).X;
            var left = secondBody.TranslatePoint(new Point(),host).X;
            Assert.InRange(left-right,-1,1);
            var hover = first.Template.Triggers.OfType<Trigger>().Single(t => t.Property == UIElement.IsMouseOverProperty);
            Assert.Contains(hover.Setters.OfType<Setter>(), s => s.TargetName == "LowerShoulders"
                && s.Property == UIElement.VisibilityProperty && Equals(s.Value,Visibility.Visible));
        });
    }

    [Fact]
    public void SelectedWorkspace_FillsAvailableSpace_AddFollowsTabs_AndSettingsReflows()
    {
        WpfAppHost.Run(() =>
        {
            var shell = new PdfWorkspaceViewModel(Mock.Of<IPdfConversionService>(), Mock.Of<IPdfEngineService>());
            shell.SelectedWorkspace!.SelectedTool=shell.SelectedWorkspace.Tools.Single(x=>x.Id==PdfToolId.ImageExport);
            var host = new PdfWorkspaceView { DataContext=shell };
            host.Measure(new Size(1400,850)); host.Arrange(new Rect(0,0,1400,850)); host.UpdateLayout();
            var tabs = (TabControl)host.FindName("PdfWorkspaceTabs");
            var view = Descendants<PdfUtilitiesView>(host).Single();
            Assert.InRange(view.ActualWidth,1398,1400);
            Assert.True(view.ActualHeight>750);
            var firstHeader = (TabItem)tabs.ItemContainerGenerator.ContainerFromIndex(0);
            var add = (Button)tabs.Template.FindName("AddPdfWorkspaceButton",tabs);
            double tabRight = firstHeader.TranslatePoint(new Point(firstHeader.ActualWidth,0),host).X;
            double addLeft = add.TranslatePoint(new Point(),host).X;
            Assert.InRange(addLeft-tabRight,0,14);
            var canvas = (Border)view.FindName("PdfDocumentArea");
            var settings = (Border)view.FindName("PdfSettingsPanel");
            var grid = (Grid)view.FindName("PdfContentGrid");
            Assert.Equal(310,settings.ActualWidth,1);
            Assert.Equal(new Thickness(20),settings.Padding);
            Assert.Equal(18,settings.Margin.Left);
            Assert.Equal(28,grid.Margin.Right);
            Assert.Equal(18,settings.CornerRadius.TopRight);
            double expanded=canvas.ActualWidth;
            shell.SelectedWorkspace!.ToggleSettingsCommand.Execute(null); host.UpdateLayout();
            Assert.Equal(Visibility.Collapsed,settings.Visibility);
            Assert.Equal(0,grid.ColumnDefinitions[1].ActualWidth);
            Assert.True(canvas.ActualWidth>expanded+300);
            shell.SelectedWorkspace.ToggleSettingsCommand.Execute(null); host.UpdateLayout();
            Assert.Equal(expanded,canvas.ActualWidth,1);
        });
    }

    [Fact]
    public void TransparentPreview_UsesWhiteBackingExactlyAtImageBounds_InEveryTheme()
    {
        WpfAppHost.Run(() =>
        {
            var vm = new PdfUtilitiesViewModel(Mock.Of<IPdfConversionService>(),Mock.Of<IPdfEngineService>());
            var pixels = new byte[200*100*4];
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(200,100,96,96,PixelFormats.Bgra32,null,pixels,200*4);
            bitmap.Freeze(); vm.IsFullScreenVisible=true;
            var view = new PdfUtilitiesView {DataContext=vm};
            view.Measure(new Size(1200,800));view.Arrange(new Rect(0,0,1200,800));view.UpdateLayout();
            var viewport=(Grid)view.FindName("PdfPreviewViewport");
            Assert.InRange(viewport.ActualWidth,1043,1044);
            Assert.InRange(viewport.ActualHeight,683,684);
            var position=viewport.TranslatePoint(new Point(),view);
            Assert.Equal(600,position.X+viewport.ActualWidth/2,1);
            Assert.Equal(400,position.Y+viewport.ActualHeight/2,1);
            vm.FullScreenPreviewImage=bitmap;
            foreach(var theme in new[]{ThemeMode.Light,ThemeMode.Dark,ThemeMode.HighContrast})
            {
                ThemeManager.ApplyTheme(theme);
                view.Measure(new Size(1200,800));view.Arrange(new Rect(0,0,1200,800));view.UpdateLayout();
                var image=(Image)view.FindName("PdfFullscreenPreviewImage");
                var backing=Assert.IsType<Border>(image.Parent);
                Assert.Equal(Colors.White,Assert.IsType<SolidColorBrush>(backing.Background).Color);
                Assert.Equal(new Thickness(0),backing.Padding);
                Assert.Equal(new Thickness(0),image.Margin);
                Assert.Equal(image.ActualWidth,backing.ActualWidth,1);
                Assert.Equal(image.ActualHeight,backing.ActualHeight,1);
                Assert.Equal(Stretch.Uniform,image.Stretch);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ReopenedPreview_RejectsOlderImageAndError(bool oldRequestFails) => WpfAppHost.RunAsync(async () =>
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release=new ManualResetEventSlim();
        int calls=0;
        var engine=new Mock<IPdfEngineService>();
        engine.Setup(x=>x.GetPageImage(It.IsAny<string>(),It.IsAny<int>())).Returns(() =>
        {
            if(Interlocked.Increment(ref calls)==1)
            {
                entered.SetResult();
                if(!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                if(oldRequestFails) throw new InvalidOperationException("older render failed");
                return new PdfPageImageResult(new byte[4],1,1,4);
            }
            return new PdfPageImageResult(new byte[8],2,1,8);
        });
        var vm=new PdfUtilitiesViewModel(Mock.Of<IPdfConversionService>(),engine.Object);
        var item=new PdfFileItem{Path=@"C:\same.pdf"};
        vm.FullScreenItem=item;vm.IsFullScreenVisible=true;
        var load=typeof(PdfUtilitiesViewModel).GetMethod("LoadFullScreenPreviewAsync",
            System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        var older=(Task)load.Invoke(vm,new object[]{item,0})!;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.CloseFullScreenCommand.Execute(null);
            vm.FullScreenItem=item;vm.IsFullScreenVisible=true;
            await (Task)load.Invoke(vm,new object[]{item,1})!;
        }
        finally{release.Set();await older;}
        Assert.Equal(2,vm.FullScreenPreviewImage!.PixelWidth);
        Assert.Equal(string.Empty,vm.PreviewStatusText);
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is T match)yield return match;
            foreach(var descendant in Descendants<T>(child))yield return descendant;
        }
    }
}
