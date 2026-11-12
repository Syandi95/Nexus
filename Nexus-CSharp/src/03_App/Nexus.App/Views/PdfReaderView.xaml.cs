using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nexus.App.ViewModels;

namespace Nexus.App.Views;

public partial class PdfReaderView : UserControl
{
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private PdfReaderViewModel? _reader;
    private ScrollViewer? _scroll;
    private Point? _panStart;
    private int? _navigationPage;
    private int _scrollVersion;
    private bool _pendingScroll;
    private double _panX,_panY;
    public PdfReaderView()
    {
        InitializeComponent();
        _refresh.Tick+=async (_,_)=>{_refresh.Stop();await RefreshVisibleAsync();};
    }
    private void Reader_DataContextChanged(object sender,DependencyPropertyChangedEventArgs e)
    {
        Detach();_reader=e.NewValue as PdfReaderViewModel;
        if(IsLoaded) Attach();
    }
    private void Attach()
    {
        if(_reader==null)return;
        _reader.RenderInvalidated-=Schedule;_reader.ScrollToPageRequested-=ScrollToPage;
        _reader.RenderInvalidated+=Schedule;_reader.ScrollToPageRequested+=ScrollToPage;
        _scroll=FindScroll(PageList);Schedule();
    }
    private void Detach()
    {
        ReleasePan();_refresh.Stop();++_scrollVersion;_pendingScroll=false;
        if(_reader==null)return;
        _reader.RenderInvalidated-=Schedule;_reader.ScrollToPageRequested-=ScrollToPage;
        _reader.Suspend();
    }
    private async void Reader_Loaded(object sender,RoutedEventArgs e){Attach();if(_reader!=null)await _reader.ResumeAsync();Schedule();}
    private async void Reader_IsVisibleChanged(object sender,DependencyPropertyChangedEventArgs e)
    {if(!IsLoaded||_reader==null)return;if(IsVisible){await _reader.ResumeAsync();Schedule();}else{ReleasePan();_refresh.Stop();_reader.Suspend();}}
    private void Reader_Unloaded(object sender,RoutedEventArgs e)=>Detach();
    private void Schedule(){if(IsLoaded){_refresh.Stop();_refresh.Start();}}
    private void ScrollToPage(int index)
    {
        if(_reader==null||index<0||index>=_reader.Pages.Count)return;
        int request=++_scrollVersion;var reader=_reader;var document=reader.ActiveDocument;
        _navigationPage=index;_pendingScroll=true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>
        {
            if(request!=_scrollVersion)return;
            if(!IsVisible||!ReferenceEquals(reader,_reader)||!ReferenceEquals(document,reader.ActiveDocument)||index>=reader.Pages.Count){_pendingScroll=false;return;}
            _navigationPage=index;
            PageList.UpdateLayout();PageList.ScrollIntoView(_reader.Pages[index]);PageList.UpdateLayout();
            _scroll??=FindScroll(PageList);
            if(_scroll!=null&&PageList.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement item)
                _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset+item.TranslatePoint(new Point(),PageList).Y);
            _pendingScroll=false;Schedule();
        }));
    }
    private void Pages_SizeChanged(object sender,SizeChangedEventArgs e)
    {
        _reader?.UpdateViewport(PageList.ActualWidth,PageList.ActualHeight,VisualTreeHelper.GetDpi(this).DpiScaleX);Schedule();
    }
    private void Pages_ScrollChanged(object sender,ScrollChangedEventArgs e)=>Schedule();
    private async Task RefreshVisibleAsync()
    {
        if(_reader==null||!IsVisible)return;
        _scroll??=FindScroll(PageList);
        _reader.UpdateViewport(PageList.ActualWidth,PageList.ActualHeight,VisualTreeHelper.GetDpi(this).DpiScaleX);
        if(_pendingScroll)return;
        var visible=new List<int>();
        for(int i=0;i<_reader.Pages.Count;i++)
            if(PageList.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement item)
            {
                var bounds=item.TransformToAncestor(PageList).TransformBounds(new Rect(item.RenderSize));
                if(bounds.Bottom>0&&bounds.Top<PageList.ActualHeight)visible.Add(i);
            }
        if(visible.Count>0)
        {
            if(_navigationPage is int target&&visible.Contains(target))_reader.SetCurrentPageFromViewport(target+1);
            else{_navigationPage=null;_reader.SetCurrentPageFromViewport(visible[0]+1);}
        }
        await _reader.SetVisiblePagesAsync(visible);
    }
    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        if(root is ScrollViewer scroll)return scroll;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(FindScroll(VisualTreeHelper.GetChild(root,i)) is {} found)return found;
        return null;
    }
    private async void Document_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {if(_reader!=null&&(sender as ComboBox)?.SelectedItem is PdfFileItem document)await _reader.OpenDocumentAsync(document);}
    private void RotateLeft_Click(object sender,RoutedEventArgs e)=>_reader?.RotateViewCommand.Execute(-90);
    private void RotateRight_Click(object sender,RoutedEventArgs e)=>_reader?.RotateViewCommand.Execute(90);
    private void ShowCards_Click(object sender,RoutedEventArgs e)
    {if(Parent is FrameworkElement parent&&parent.DataContext is PdfUtilitiesViewModel workspace)workspace.ToggleDocumentViewCommand.Execute(null);}
    private void Pages_MouseWheel(object sender,MouseWheelEventArgs e){_navigationPage=null;}
    private void Pages_KeyDown(object sender,KeyEventArgs e)=>_navigationPage=null;
    private void Reader_PreviewKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Escape&&_panStart!=null){ReleasePan();e.Handled=true;}}
    private void Pages_MouseDown(object sender,MouseButtonEventArgs e)
    {
        _navigationPage=null;
        if(_reader?.IsHandMode!=true||_scroll==null)return;
        for(var hit=e.OriginalSource as DependencyObject;hit!=null;hit=VisualTreeHelper.GetParent(hit))
            if(hit is System.Windows.Controls.Primitives.ScrollBar)return;
        _panStart=e.GetPosition(this);_panX=_scroll.HorizontalOffset;_panY=_scroll.VerticalOffset;PageList.CaptureMouse();e.Handled=true;
    }
    private void Pages_MouseMove(object sender,MouseEventArgs e)
    {if(_panStart is Point start&&_scroll!=null){var point=e.GetPosition(this);_scroll.ScrollToHorizontalOffset(_panX+start.X-point.X);_scroll.ScrollToVerticalOffset(_panY+start.Y-point.Y);}}
    private void ReleasePan(){_panStart=null;if(PageList.IsMouseCaptured)PageList.ReleaseMouseCapture();}
    private void Pages_LostMouseCapture(object sender,MouseEventArgs e)=>_panStart=null;
    private void Pages_MouseUp(object sender,MouseButtonEventArgs e)=>ReleasePan();
}
