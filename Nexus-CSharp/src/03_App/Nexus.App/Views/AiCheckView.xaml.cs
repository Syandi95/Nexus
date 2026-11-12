using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nexus.App.ViewModels;
using Nexus.Contracts.ReferenceData;

namespace Nexus.App.Views;

public partial class AiCheckView : UserControl
{
    private bool _isPanning = false;
    private Point _panStart;
    private double _panStartX = 0;
    private double _panStartY = 0;
    private double _panX = 0;
    private double _panY = 0;

    public AiCheckView()
    {
        InitializeComponent();
        DataContextChanged += AiCheckView_DataContextChanged;
    }

    private void AiCheckView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm)
        {
            oldVm.PropertyChanged -= ViewModel_PropertyChanged;
        }

        if (e.NewValue is AiCheckViewModel newVm)
        {
            newVm.PropertyChanged += ViewModel_PropertyChanged;
            newVm.RequestFitToCanvasAction = FitToCanvas;
            newVm.RequestFocusFindingAction = FocusFinding;
            SyncPreviewGeometry();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiCheckViewModel.PreviewImage) ||
            e.PropertyName == nameof(AiCheckViewModel.UploadedFilePath))
        {
            _panX = 0;
            _panY = 0;
            _isPanning = false;
            if (CanvasPreviewStage.IsMouseCaptured)
                CanvasPreviewStage.ReleaseMouseCapture();

            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Render,
                new Action(FitToCanvas));
            return;
        }

        if (e.PropertyName == nameof(AiCheckViewModel.ZoomLevel) ||
            e.PropertyName == nameof(AiCheckViewModel.IsAnalyzing))
        {
            LimitPan();
            SyncPreviewGeometry();
        }
    }

    private Rect GetImageDisplayRect()
    {
        if (DataContext is not AiCheckViewModel vm || vm.OriginalPixelWidth <= 0 || vm.OriginalPixelHeight <= 0 || CanvasPreviewStage == null)
            return new Rect(0, 0, Math.Max(0, CanvasPreviewStage?.ActualWidth ?? 0), Math.Max(0, CanvasPreviewStage?.ActualHeight ?? 0));

        double stageW = CanvasPreviewStage.ActualWidth;
        double stageH = CanvasPreviewStage.ActualHeight;
        if (stageW <= 0 || stageH <= 0) return Rect.Empty;

        // Leave a 32px breathing room so the image never touches the viewport border tightly
        double availW = Math.Max(20, stageW - 32);
        double availH = Math.Max(20, stageH - 32);

        double imgAspect = (double)vm.OriginalPixelWidth / vm.OriginalPixelHeight;
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

        return new Rect(0, 0, dispW, dispH);
    }

    private void FitToCanvas()
    {
        if (DataContext is not AiCheckViewModel vm) return;
        _panX = 0;
        _panY = 0;
        vm.ZoomLevel = 1.0;
        SyncPreviewGeometry();
    }

    private (bool CanPan, double MaxX, double MaxY) GetPanBounds()
    {
        if (DataContext is not AiCheckViewModel vm || vm.PreviewImage == null || CanvasPreviewStage == null || CanvasTransformStage == null)
            return (false, 0.0, 0.0);

        var dispRect = GetImageDisplayRect();
        if (dispRect.Width <= 0 || dispRect.Height <= 0) return (false, 0.0, 0.0);

        var zoom = vm.ZoomLevel;
        if (zoom <= 1.0) return (false, 0.0, 0.0);

        var scaledWidth = Math.Max(0, dispRect.Width * zoom);
        var scaledHeight = Math.Max(0, dispRect.Height * zoom);

        var maxX = Math.Max(0, (scaledWidth - CanvasPreviewStage.ActualWidth) / 2.0);
        var maxY = Math.Max(0, (scaledHeight - CanvasPreviewStage.ActualHeight) / 2.0);

        return (maxX > 0.01 || maxY > 0.01, maxX, maxY);
    }

    private void LimitPan()
    {
        var bounds = GetPanBounds();
        if (!bounds.CanPan)
        {
            _panX = 0;
            _panY = 0;
            return;
        }
        _panX = Math.Max(-bounds.MaxX, Math.Min(bounds.MaxX, _panX));
        _panY = Math.Max(-bounds.MaxY, Math.Min(bounds.MaxY, _panY));
    }

    private void UpdatePanCursor()
    {
        if (CanvasPreviewStage == null) return;
        var bounds = GetPanBounds();
        if (_isPanning && bounds.CanPan)
            CanvasPreviewStage.Cursor = Cursors.SizeAll;
        else if (bounds.CanPan)
            CanvasPreviewStage.Cursor = Cursors.Hand;
        else
            CanvasPreviewStage.Cursor = Cursors.Arrow;
    }

    private void SyncPreviewGeometry()
    {
        if (DataContext is not AiCheckViewModel vm || CanvasTransformStage == null || CanvasPreviewStage == null) return;

        var dispRect = GetImageDisplayRect();
        if (dispRect.Width > 0 && dispRect.Height > 0)
        {
            CanvasTransformStage.Width = dispRect.Width;
            CanvasTransformStage.Height = dispRect.Height;
            if (OverlayViewbox != null)
            {
                OverlayViewbox.Width = dispRect.Width;
                OverlayViewbox.Height = dispRect.Height;
            }
            if (AiCheckScanner != null)
            {
                AiCheckScanner.Width = dispRect.Width;
                AiCheckScanner.Height = dispRect.Height;
            }
        }

        var zoom = vm.ZoomLevel;
        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(new ScaleTransform(zoom, zoom));
        transformGroup.Children.Add(new TranslateTransform(_panX, _panY));
        CanvasTransformStage.RenderTransform = transformGroup;
        CanvasTransformStage.RenderTransformOrigin = new Point(0.5, 0.5);

        UpdatePanCursor();
    }

    private void Preview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            if (DataContext is AiCheckViewModel vm)
            {
                if (Math.Abs(vm.ZoomLevel - 1.0) < 0.05)
                {
                    vm.ZoomLevel = 2.0;
                }
                else
                {
                    FitToCanvas();
                }
            }
            e.Handled = true;
            return;
        }

        var bounds = GetPanBounds();
        if (!bounds.CanPan || _isPanning) return;

        _isPanning = true;
        _panStart = e.GetPosition(CanvasPreviewStage);
        _panStartX = _panX;
        _panStartY = _panY;
        CanvasPreviewStage.CaptureMouse();
        UpdatePanCursor();
        e.Handled = true;
    }

    private void Preview_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;

        if (e.LeftButton != MouseButtonState.Pressed &&
            e.MiddleButton != MouseButtonState.Pressed &&
            e.RightButton != MouseButtonState.Pressed)
        {
            _isPanning = false;
            if (CanvasPreviewStage.IsMouseCaptured)
                CanvasPreviewStage.ReleaseMouseCapture();
            UpdatePanCursor();
            return;
        }

        var pos = e.GetPosition(CanvasPreviewStage);
        _panX = _panStartX + (pos.X - _panStart.X);
        _panY = _panStartY + (pos.Y - _panStart.Y);
        LimitPan();
        SyncPreviewGeometry();
        e.Handled = true;
    }

    private void Preview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        if (CanvasPreviewStage.IsMouseCaptured)
            CanvasPreviewStage.ReleaseMouseCapture();
        UpdatePanCursor();
        e.Handled = true;
    }

    private void Preview_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _isPanning = false;
        UpdatePanCursor();
    }

    private void Preview_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right)
        {
            var bounds = GetPanBounds();
            if (!bounds.CanPan || _isPanning) return;

            _isPanning = true;
            _panStart = e.GetPosition(CanvasPreviewStage);
            _panStartX = _panX;
            _panStartY = _panY;
            CanvasPreviewStage.CaptureMouse();
            UpdatePanCursor();
            e.Handled = true;
        }
    }

    private void Preview_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right))
        {
            _isPanning = false;
            if (CanvasPreviewStage.IsMouseCaptured)
                CanvasPreviewStage.ReleaseMouseCapture();
            UpdatePanCursor();
            e.Handled = true;
        }
    }

    private void Preview_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_isPanning || DataContext is not AiCheckViewModel vm || !vm.HasUploadedFile || vm.IsAnalyzing)
            return;

        var delta = e.Delta;
        if (delta == 0) return;

        var oldZoom = vm.ZoomLevel;
        var notches = Math.Max(1.0, Math.Round(Math.Abs((double)delta) / 120.0));
        var step = delta > 0 ? 0.15 * notches : -0.15 * notches;
        var nextZoom = Math.Clamp(Math.Round(oldZoom + step, 2), 0.2, 3.0);

        if (Math.Abs(nextZoom - oldZoom) < 0.005) return;

        var pos = e.GetPosition(CanvasPreviewStage);
        var cursorX = pos.X - (CanvasPreviewStage.ActualWidth / 2.0);
        var cursorY = pos.Y - (CanvasPreviewStage.ActualHeight / 2.0);
        var scaleRatio = nextZoom / oldZoom;

        _panX = cursorX - ((cursorX - _panX) * scaleRatio);
        _panY = cursorY - ((cursorY - _panY) * scaleRatio);

        vm.ZoomLevel = nextZoom;
        LimitPan();
        SyncPreviewGeometry();
        e.Handled = true;
    }

    private void Preview_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SyncPreviewGeometry();
    }

    private void View_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void View_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                if (DataContext is AiCheckViewModel vm)
                {
                    await vm.LoadFileDirectlyAsync(files[0]);
                }
            }
        }
    }

    private async void BtnManageRefData_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            var refManager = app.Services.GetRequiredService<IReferenceDataManager>();
            var dlg = new ReferenceDataManagerDialog(refManager)
            {
                Owner = Application.Current.MainWindow
            };

            dlg.ShowDialog();

            if (DataContext is AiCheckViewModel vm)
            {
                await vm.LoadAvailableDatasetsAsync();
            }
        }
    }

    private DispatcherTimer? _highlightTimer;
    private DispatcherTimer? _panAnimTimer;

    private void FocusFinding(int boxId, int x1, int y1, int x2, int y2)
    {
        if (DataContext is not AiCheckViewModel vm) return;

        // 1. Highlight target overlay box
        _highlightTimer?.Stop();
        foreach (var b in vm.OverlayBoxes)
        {
            b.IsHighlighted = (b.Id == boxId);
        }

        var targetBox = vm.OverlayBoxes.FirstOrDefault(b => b.Id == boxId);
        if (targetBox != null)
        {
            _highlightTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
            _highlightTimer.Tick += (s, e) =>
            {
                _highlightTimer.Stop();
                targetBox.IsHighlighted = false;
            };
            _highlightTimer.Start();
        }

        // 2. Center and Zoom to target box coordinates
        var dispRect = GetImageDisplayRect();
        if (dispRect.Width <= 0 || dispRect.Height <= 0 || vm.OriginalPixelWidth <= 0 || vm.OriginalPixelHeight <= 0) return;

        // Pastikan zoom cukup detail untuk melihat teks (min 1.8x)
        double targetZoom = Math.Max(vm.ZoomLevel, 1.8);
        vm.ZoomLevel = targetZoom;

        double boxCenterX = (x1 + x2) / 2.0;
        double boxCenterY = (y1 + y2) / 2.0;
        double origCenterX = vm.OriginalPixelWidth / 2.0;
        double origCenterY = vm.OriginalPixelHeight / 2.0;

        double dx = boxCenterX - origCenterX;
        double dy = boxCenterY - origCenterY;

        double ratio = dispRect.Width / (double)vm.OriginalPixelWidth;
        double dispDx = dx * ratio;
        double dispDy = dy * ratio;

        double targetPanX = -dispDx * targetZoom;
        double targetPanY = -dispDy * targetZoom;

        // Batasi target pan sesuai batas yang diizinkan pada zoom ini
        var scaledW = dispRect.Width * targetZoom;
        var scaledH = dispRect.Height * targetZoom;
        double stageW = CanvasPreviewStage?.ActualWidth ?? 0;
        double stageH = CanvasPreviewStage?.ActualHeight ?? 0;

        double maxX = Math.Max(0, (scaledW - stageW) / 2.0);
        double maxY = Math.Max(0, (scaledH - stageH) / 2.0);

        if (maxX > 0)
            targetPanX = Math.Max(-maxX, Math.Min(maxX, targetPanX));
        else
            targetPanX = 0;

        if (maxY > 0)
            targetPanY = Math.Max(-maxY, Math.Min(maxY, targetPanY));
        else
            targetPanY = 0;

        AnimatePanTo(targetPanX, targetPanY);
    }

    private void AnimatePanTo(double targetX, double targetY)
    {
        _panAnimTimer?.Stop();

        double startX = _panX;
        double startY = _panY;

        if (Math.Abs(startX - targetX) < 1.0 && Math.Abs(startY - targetY) < 1.0)
        {
            _panX = targetX;
            _panY = targetY;
            SyncPreviewGeometry();
            return;
        }

        var sw = Stopwatch.StartNew();
        const double durationMs = 280.0;

        _panAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _panAnimTimer.Tick += (s, e) =>
        {
            double elapsed = sw.Elapsed.TotalMilliseconds;
            double progress = Math.Min(1.0, elapsed / durationMs);
            // EaseInOutQuad
            double ease = progress < 0.5 
                ? 2 * progress * progress 
                : 1 - Math.Pow(-2 * progress + 2, 2) / 2;

            _panX = startX + (targetX - startX) * ease;
            _panY = startY + (targetY - startY) * ease;
            SyncPreviewGeometry();

            if (progress >= 1.0)
            {
                _panAnimTimer?.Stop();
                _panX = targetX;
                _panY = targetY;
                SyncPreviewGeometry();
            }
        };
        _panAnimTimer.Start();
    }

    private void FindingCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        if (sender is FrameworkElement fe && fe.DataContext is AiCheckFindingItemViewModel finding)
        {
            if (DataContext is AiCheckViewModel vm)
            {
                vm.JumpToFindingCommand.Execute(finding);
                e.Handled = true;
            }
        }
    }
}
