using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Moq;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.App.Views;
using Nexus.PdfUtilities;

namespace Nexus.Integration.Tests;

[Collection(WpfApplicationCollection.Name)]
public class PdfReaderToolLayoutTests
{
    [Fact]
    public void ReaderHidesCanvasAndConversionChrome_ToolRetainsSettingsAndToolbar() => WpfAppHost.Run(() =>
    {
        ThemeManager.ApplyTheme(ThemeMode.Light);
        var vm=new PdfUtilitiesViewModel(Mock.Of<IPdfConversionService>(),Mock.Of<IPdfEngineService>());
        var view=new PdfUtilitiesView{DataContext=vm};Layout(view);
        var toolbar=(StackPanel)view.FindName("PdfWorkspaceToolbar");
        var root=(Grid)view.FindName("PdfWorkspaceRoot");
        var footer=root.Children.OfType<Border>().Single(x=>Grid.GetRow(x)==2);
        Assert.Equal(Visibility.Collapsed,toolbar.Visibility);Assert.Equal(Visibility.Collapsed,footer.Visibility);
        var reader=(PdfReaderView)view.FindName("PdfReaderWorkspace");
        Assert.Equal(0,((WrapPanel)reader.FindName("ReaderToolbar")).Margin.Right);
        vm.SelectedTool=vm.Tools.Single(x=>x.Id==PdfToolId.ImageExport);Layout(view);
        Assert.Equal(Visibility.Visible,toolbar.Visibility);Assert.Equal(Visibility.Visible,footer.Visibility);
        Assert.DoesNotContain(Descendants<TextBlock>(view),x=>x.Text.Contains("Render halaman menggunakan engine NEXUS",StringComparison.Ordinal));
        var quality=(FrameworkElement)view.FindName("PdfJpegQualityPanel");
        vm.SelectedFormatIndex=1;Layout(view);Assert.Equal(Visibility.Collapsed,quality.Visibility);
        vm.SelectedFormatIndex=0;Layout(view);Assert.Equal(Visibility.Visible,quality.Visibility);
    });

    [Fact]
    public void ReaderUsesHandToggle_AndCategoryChevronAtRight() => WpfAppHost.Run(() =>
    {
        var vm=new PdfUtilitiesViewModel(Mock.Of<IPdfConversionService>(),Mock.Of<IPdfEngineService>());
        var view=new PdfUtilitiesView{DataContext=vm};Layout(view);
        var reader=(PdfReaderView)view.FindName("PdfReaderWorkspace");
        var hand=Assert.IsType<ToggleButton>(reader.FindName("PdfHandToggle"));
        hand.IsChecked=true;Assert.True(vm.Reader.IsHandMode);
        var pages=(ListBox)reader.FindName("PageList");Assert.Equal(Cursors.Hand,pages.Cursor);
        foreach(var category in Descendants<Expander>((Border)view.FindName("PdfToolsPanel")))
        {
            var header=Assert.IsType<ToggleButton>(category.Template.FindName("CategoryHeader",category));header.ApplyTemplate();
            var chevron=Assert.IsType<TextBlock>(header.Template.FindName("CategoryChevron",header));
            Assert.Equal(HorizontalAlignment.Right,chevron.HorizontalAlignment);
            header.IsChecked=false;Assert.False(category.IsExpanded);
        }
    });
    private static void Layout(FrameworkElement view){view.Measure(new Size(1400,850));view.Arrange(new Rect(0,0,1400,850));view.UpdateLayout();}

    [Fact]
    public void EscapeFromHandToggle_ReleasesReaderPan() => WpfAppHost.Run(() =>
    {
        var vm=new PdfUtilitiesViewModel(Mock.Of<IPdfConversionService>(),Mock.Of<IPdfEngineService>());
        var reader=new PdfReaderView{DataContext=vm.Reader};
        var window=new Window{Content=reader,Width=800,Height=600,Left=-10000,ShowActivated=false,ShowInTaskbar=false};
        try
        {
            window.Show();reader.UpdateLayout();
            var hand=(ToggleButton)reader.FindName("PdfHandToggle");hand.IsChecked=true;
            var pan=typeof(PdfReaderView).GetField("_panStart",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            pan.SetValue(reader,new Point(100,100));
            var escape=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(reader),0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent};
            hand.RaiseEvent(escape);
            Assert.Null(pan.GetValue(reader));Assert.True(escape.Handled);
        }
        finally{window.Close();}
    });
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T match)yield return match;foreach(var nested in Descendants<T>(child))yield return nested;}}
}
