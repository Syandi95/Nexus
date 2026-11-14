using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexus.App.ViewModels;
using Nexus.Engine.ImageSubsystem.Pipelines;
using Nexus.Engine.ImageSubsystem.Services;

namespace Nexus.App.Views
{
    public partial class ImageSubsystemView : UserControl
    {
        private enum MaskActionType
        {
            BrushStroke,
            AutoSelect
        }

        private bool _isPanning = false;
        private Point _panStart;
        private double _panStartX = 0;
        private double _panStartY = 0;
        private double _panX = 0;
        private double _panY = 0;
        private bool _isSpacebarHolding = false;
        private double _savedCompareRatio = 0.5;

        private readonly Stack<Stroke> _undoStack = new();
        private readonly Stack<Stroke> _redoStack = new();
        private readonly Stack<MaskActionType> _actionHistory = new();
        private readonly Stack<MaskActionType> _redoHistory = new();

        public ImageSubsystemView()
        {
            InitializeComponent();
            DataContextChanged += ImageSubsystemView_DataContextChanged;
            Loaded += (_, _) =>
            {
                if (DataContext is ImageSubsystemViewModel vm)
                    SyncCanvasBackgroundState(vm);
            };
            ImgRemoveBackgroundBefore.SizeChanged += (_, _) => InvokePreviewGeometrySync();
        }

        private void ImgRemoveBackgroundBefore_TargetUpdated(object? sender, DataTransferEventArgs e)
        {
            InvokePreviewGeometrySync();
        }

        /// <summary>
        /// Rendered width is ActualWidth * zoom. Use this in DragDelta to
        /// correctly compute ratio deltas at any zoom level.
        /// </summary>
        public static double CalculateRenderedWidth(double actualWidth, double zoom)
        {
            if (actualWidth <= 0 || zoom <= 0) return 0;
            return actualWidth * zoom;
        }

        /// <summary>
        /// Dispatches geometry recalculation at Render priority (PowerShell parity).
        /// This ensures WPF layout has updated with the correct image
        /// dimensions before clip/thumb geometry is recalculated.
        /// </summary>
        private void InvokePreviewGeometrySync()
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Render,
                new Action(SyncPreviewGeometry));
        }

        private void ImageSubsystemView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            _isSpacebarHolding = false;
            if (e.OldValue is INotifyPropertyChanged oldVm)
            {
                oldVm.PropertyChanged -= ViewModel_PropertyChanged;
            }
            if (e.NewValue is ImageSubsystemViewModel newVm)
            {
                newVm.PropertyChanged += ViewModel_PropertyChanged;
                newVm.MaskProvider = GenerateMaskBitmap;
                newVm.MaskProviderAsync = GenerateMaskBitmapAsync;
                newVm.UndoMaskAction = UndoStroke;
                newVm.RedoMaskAction = RedoStroke;
                newVm.ClearMaskAction = ClearStrokes;
                UpdateBrushAttributes();
                SyncCanvasBackgroundState(newVm);
            }
            else if (e.NewValue is INotifyPropertyChanged genericVm)
            {
                genericVm.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Reset inpaint strokes on new image
            if (e.PropertyName == nameof(ImageSubsystemViewModel.InputImagePath))
            {
                _isSpacebarHolding = false;
                ClearStrokes();
                RemoveBackgroundBeforeClip.Rect = Rect.Empty;
                ThumbRemoveBackgroundCompare.RenderTransform = Transform.Identity;
                _panX = 0;
                _panY = 0;
                _isPanning = false;
                if (RemoveBackgroundPreview.IsMouseCaptured)
                    RemoveBackgroundPreview.ReleaseMouseCapture();
                InvokePreviewGeometrySync();
                return;
            }

            if (e.PropertyName == nameof(ImageSubsystemViewModel.BrushSize) ||
                e.PropertyName == nameof(ImageSubsystemViewModel.BrushSoftness) ||
                e.PropertyName == nameof(ImageSubsystemViewModel.IsEraserMode) ||
                e.PropertyName == nameof(ImageSubsystemViewModel.IsAutoSelectMode) ||
                e.PropertyName == nameof(ImageSubsystemViewModel.IsRemoveBgRefining) ||
                e.PropertyName == nameof(ImageSubsystemViewModel.IsRemoveBgAddMode))
            {
                UpdateBrushAttributes();
                if (e.PropertyName == nameof(ImageSubsystemViewModel.IsRemoveBgRefining))
                {
                    SyncPreviewGeometry();
                }
                return;
            }

            if (e.PropertyName == nameof(ImageSubsystemViewModel.IsProcessing) ||
                e.PropertyName == nameof(ImageSubsystemViewModel.IsRemoveBgProcessing) ||
                e.PropertyName == nameof(ImageSubsystemViewModel.IsUpscaleProcessing) ||
                e.PropertyName == nameof(ImageSubsystemViewModel.IsInpaintProcessing))
            {
                if (DataContext is ImageSubsystemViewModel vmProc && vmProc.IsProcessing)
                {
                    SyncPreviewGeometry();
                }
                return;
            }

            if (e.PropertyName == nameof(ImageSubsystemViewModel.HasResult) && DataContext is ImageSubsystemViewModel vmResult && vmResult.HasResult)
            {
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(350))
                {
                    DecelerationRatio = 0.3
                };
                ImgRemoveBackgroundAfter.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }

            if (e.PropertyName == nameof(ImageSubsystemViewModel.SelectedMode) && DataContext is ImageSubsystemViewModel vm2)
            {
                SyncCanvasBackgroundState(vm2);
                SyncPreviewGeometry();
                return;
            }

            if (e.PropertyName == nameof(ImageSubsystemViewModel.IsImageLoaded) && DataContext is ImageSubsystemViewModel vm3)
            {
                SyncCanvasBackgroundState(vm3);
                return;
            }

            if (e.PropertyName == nameof(ImageSubsystemViewModel.ZoomLevel) ||
                e.PropertyName == nameof(ImageSubsystemViewModel.CompareRatio) ||
                e.PropertyName == nameof(ImageSubsystemViewModel.IsCompareActive) ||
                e.PropertyName == nameof(ImageSubsystemViewModel.RotationAngle) ||
                e.PropertyName == nameof(ImageSubsystemViewModel.ScaleX) ||
                e.PropertyName == nameof(ImageSubsystemViewModel.ScaleY) ||
                e.PropertyName == nameof(ImageSubsystemViewModel.IsCropActive))
            {
                if (e.PropertyName == nameof(ImageSubsystemViewModel.ZoomLevel) && DataContext is ImageSubsystemViewModel vmZoom)
                {
                    if (vmZoom.ZoomLevel <= 100.0)
                    {
                        _panX = 0;
                        _panY = 0;
                        _isPanning = false;
                        if (RemoveBackgroundPreview.IsMouseCaptured)
                            RemoveBackgroundPreview.ReleaseMouseCapture();
                    }
                    else
                    {
                        LimitPan();
                    }
                }

                if (e.PropertyName == nameof(ImageSubsystemViewModel.IsCropActive) &&
                    DataContext is ImageSubsystemViewModel vmInit && vmInit.IsCropActive)
                {
                    _canonicalNormalizedCrop = new Rect(0, 0, 1, 1);
                    _selectedAspectRatio = -1;
                }

                SyncPreviewGeometry();
                if (e.PropertyName == nameof(ImageSubsystemViewModel.ZoomLevel))
                    InvokePreviewGeometrySync();

                if (DataContext is ImageSubsystemViewModel vmCrop && vmCrop.IsCropActive)
                {
                    if (e.PropertyName == nameof(ImageSubsystemViewModel.IsCropActive) ||
                        e.PropertyName == nameof(ImageSubsystemViewModel.RotationAngle) ||
                        e.PropertyName == nameof(ImageSubsystemViewModel.ScaleX) ||
                        e.PropertyName == nameof(ImageSubsystemViewModel.ScaleY) ||
                        e.PropertyName == nameof(ImageSubsystemViewModel.ZoomLevel))
                    {
                        UpdateCropOverlayState();
                    }
                }
            }
        }

        private void View_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (DataContext is ImageSubsystemViewModel vm && vm.IsInpaintMode)
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 1)
                {
                    // Explicitly reject multiple files in Object Removal mode during drag
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }
            }

            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void View_Drop(object sender, DragEventArgs e) => HandleFileDrop(e);
        private void DropZone_Drop(object sender, DragEventArgs e) => HandleFileDrop(e);

        private void HandleFileDrop(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    if (DataContext is ImageSubsystemViewModel vm)
                    {
                        vm.AddFiles(files);
                    }
                }
            }
        }

        private void ThumbCompare_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (DataContext is ImageSubsystemViewModel vm && RemoveBackgroundPreview != null)
            {
                var zoom = vm.ZoomLevel / 100.0;
                var renderedWidth = CalculateRenderedWidth(
                    ImgRemoveBackgroundBefore.ActualWidth, zoom);
                // Also try TransformToAncestor for exact rendered width
                try
                {
                    var transform = ImgRemoveBackgroundBefore.TransformToAncestor(RemoveBackgroundPreview);
                    var p1 = transform.Transform(new Point(0, 0));
                    var p2 = transform.Transform(new Point(ImgRemoveBackgroundBefore.ActualWidth, 0));
                    renderedWidth = Math.Abs(p2.X - p1.X);
                }
                catch { }

                if (renderedWidth > 0)
                {
                    var newRatio = vm.CompareRatio + (e.HorizontalChange / renderedWidth);
                    vm.CompareRatio = Math.Max(0.0, Math.Min(1.0, newRatio));
                }
            }
        }

        private (bool CanPan, double MaxX, double MaxY) GetPanBounds()
        {
            if (DataContext is not ImageSubsystemViewModel vm || vm.ZoomLevel <= 100.0 || ImgRemoveBackgroundBefore.Source == null)
                return (false, 0.0, 0.0);

            var zoom = vm.ZoomLevel / 100.0;
            var scaledWidth = Math.Max(0, ImgRemoveBackgroundBefore.ActualWidth * zoom);
            var scaledHeight = Math.Max(0, ImgRemoveBackgroundBefore.ActualHeight * zoom);

            var maxX = Math.Max(0, (scaledWidth - RemoveBackgroundPreview.ActualWidth) / 2.0);
            var maxY = Math.Max(0, (scaledHeight - RemoveBackgroundPreview.ActualHeight) / 2.0);

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
            var bounds = GetPanBounds();
            if (_isPanning && bounds.CanPan)
                RemoveBackgroundPreview.Cursor = Cursors.SizeAll;
            else if (bounds.CanPan)
                RemoveBackgroundPreview.Cursor = Cursors.Hand;
            else
                RemoveBackgroundPreview.Cursor = Cursors.Arrow;
        }

        private void SyncPreviewGeometry()
        {
            if (DataContext is not ImageSubsystemViewModel vm) return;

            var activeElement = (vm.HasResult && ImgRemoveBackgroundAfter.Source is BitmapSource)
                ? ImgRemoveBackgroundAfter
                : ImgRemoveBackgroundBefore;

            double fitScale = 1.0;
            if (activeElement.Source is BitmapSource src && src.PixelWidth > 0 && src.PixelHeight > 0)
            {
                bool isSwapped = ((int)Math.Round(vm.RotationAngle) % 180) != 0;
                if (isSwapped)
                {
                    double stageW = RemoveBackgroundImageStage.ActualWidth > 0
                        ? RemoveBackgroundImageStage.ActualWidth
                        : Math.Max(1.0, RemoveBackgroundPreview.ActualWidth - 8.0);
                    double stageH = RemoveBackgroundImageStage.ActualHeight > 0
                        ? RemoveBackgroundImageStage.ActualHeight
                        : Math.Max(1.0, RemoveBackgroundPreview.ActualHeight - 8.0);

                    double sUnrotated = Math.Min(stageW / src.PixelWidth, stageH / src.PixelHeight);
                    double sOriented = Math.Min(stageW / src.PixelHeight, stageH / src.PixelWidth);

                    if (sUnrotated > 0)
                    {
                        fitScale = sOriented / sUnrotated;
                    }
                }
            }

            // Unified Transform Pipeline: Rotate -> Flip (with FitScale) -> Zoom -> Pan
            var zoom = vm.ZoomLevel / 100.0;
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new RotateTransform(vm.RotationAngle));
            transformGroup.Children.Add(new ScaleTransform(vm.ScaleX * fitScale, vm.ScaleY * fitScale));
            transformGroup.Children.Add(new ScaleTransform(zoom, zoom));
            transformGroup.Children.Add(new TranslateTransform(_panX, _panY));
            RemoveBackgroundImageStage.RenderTransform = transformGroup;
            RemoveBackgroundImageStage.RenderTransformOrigin = new Point(0.5, 0.5);

            UpdatePanCursor();

            // Compare Clip / Refine Base
            var width = Math.Max(0, ImgRemoveBackgroundBefore.ActualWidth);
            var height = Math.Max(0, ImgRemoveBackgroundBefore.ActualHeight);

            if (width > 0 && height > 0)
            {
                if (vm.IsRemoveBgRefining)
                {
                    // In refine mode, reveal the full original photo as the canvas base underneath mask visualization
                    RemoveBackgroundBeforeClip.Rect = new Rect(0, 0, width, height);
                }
                else if (vm.IsCropActive && vm.HasResult)
                {
                    // During crop on an active result, hide before image so only the active result is visible under crop frame
                    RemoveBackgroundBeforeClip.Rect = new Rect(0, 0, 0, 0);
                }
                else
                {
                    var clipX = width * vm.CompareRatio;
                    RemoveBackgroundBeforeClip.Rect = new Rect(clipX, 0,
                        Math.Max(0, width * (1.0 - vm.CompareRatio)), height);

                    var imageLeftX = (RemoveBackgroundPreview.ActualWidth - width) / 2.0;
                    var lineX = imageLeftX + clipX;
                    try
                    {
                        var transform = ImgRemoveBackgroundBefore.TransformToAncestor(RemoveBackgroundPreview);
                        lineX = transform.Transform(new Point(clipX, 0)).X;
                    }
                    catch { }

                    ThumbRemoveBackgroundCompare.RenderTransform = new TranslateTransform(
                        Math.Max(0, Math.Min(RemoveBackgroundPreview.ActualWidth, lineX) -
                            ThumbRemoveBackgroundCompare.Width / 2.0), 0);
                }
            }

            // Sync InpaintMaskCanvas, BrushCursorCanvas, and ImageScanner to match rendered image display rect
            var dispRect = GetImageDisplayRect();
            if (dispRect.Width > 0 && dispRect.Height > 0)
            {
                InpaintMaskCanvas.Width = dispRect.Width;
                InpaintMaskCanvas.Height = dispRect.Height;

                BrushCursorCanvas.Width = dispRect.Width;
                BrushCursorCanvas.Height = dispRect.Height;

                // Target geometry: processing scanner overlay strictly follows actual displayed image rect,
                // covering the entire photo surface (Width = dispRect.Width, Height = dispRect.Height)
                if (ImageScanner != null)
                {
                    ImageScanner.Width = dispRect.Width;
                    ImageScanner.Height = dispRect.Height;
                }

                if (vm.IsRemoveBgCanvasVisible)
                {
                    UpdateBrushAttributes();
                }
            }
        }

        private void Preview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            RemoveBackgroundPreview.Focus();

            // Bug C2: Don't start pan when interacting with compare thumb
            if (ThumbRemoveBackgroundCompare.IsMouseOver || ThumbRemoveBackgroundCompare.IsMouseCaptured)
                return;

            if (DataContext is ImageSubsystemViewModel vmInpaint && (vmInpaint.IsInpaintMode || vmInpaint.IsRemoveBgRefining))
            {
                // In inpaint or remove-bg refining mode, left mouse clicks are dedicated to drawing/erasing mask strokes
                return;
            }

            if (e.ClickCount >= 2)
            {
                if (DataContext is ImageSubsystemViewModel vm)
                    vm.ZoomLevel = 100.0;
                e.Handled = true;
                return;
            }

            var bounds = GetPanBounds();
            if (!bounds.CanPan || _isPanning) return;

            _isPanning = true;
            _panStart = e.GetPosition(RemoveBackgroundPreview);
            _panStartX = _panX;
            _panStartY = _panY;
            RemoveBackgroundPreview.CaptureMouse();
            UpdatePanCursor();
            e.Handled = true;
        }

        private void Preview_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanning) return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _isPanning = false;
                if (RemoveBackgroundPreview.IsMouseCaptured)
                    RemoveBackgroundPreview.ReleaseMouseCapture();
                UpdatePanCursor();
                return;
            }

            var pos = e.GetPosition(RemoveBackgroundPreview);
            _panX = _panStartX + (pos.X - _panStart.X);
            _panY = _panStartY + (pos.Y - _panStart.Y);
            LimitPan();
            SyncPreviewGeometry();
            if (DataContext is ImageSubsystemViewModel vm && vm.IsCropActive)
            {
                UpdateCropOverlayState();
            }
            e.Handled = true;
        }

        private void Preview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isPanning) return;
            _isPanning = false;
            if (RemoveBackgroundPreview.IsMouseCaptured)
                RemoveBackgroundPreview.ReleaseMouseCapture();
            UpdatePanCursor();
            e.Handled = true;
        }

        private void Preview_LostMouseCapture(object sender, MouseEventArgs e)
        {
            _isPanning = false;
            UpdatePanCursor();
        }

        private void Preview_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Bug C1: Don't zoom when interacting with compare thumb
            if (ThumbRemoveBackgroundCompare.IsMouseOver || ThumbRemoveBackgroundCompare.IsMouseCaptured)
                return;
            if (_isPanning || DataContext is not ImageSubsystemViewModel vm || string.IsNullOrEmpty(vm.InputImagePath) || vm.IsProcessing)
                return;

            var delta = e.Delta;
            if (delta == 0) return;

            var oldZoom = Math.Max(0.5, Math.Min(10.0, vm.ZoomLevel / 100.0));
            var notches = Math.Max(1.0, Math.Round(Math.Abs((double)delta) / 120.0));
            var factor = Math.Pow(1.20, notches);
            var nextZoom = delta > 0
                ? Math.Min(10.0, oldZoom * factor)
                : Math.Max(0.5, oldZoom / factor);

            if (Math.Abs(nextZoom - oldZoom) < 0.001) return;

            var pos = e.GetPosition(RemoveBackgroundPreview);
            var cursorX = pos.X - (RemoveBackgroundPreview.ActualWidth / 2.0);
            var cursorY = pos.Y - (RemoveBackgroundPreview.ActualHeight / 2.0);
            var scaleRatio = nextZoom / oldZoom;

            _panX = cursorX - ((cursorX - _panX) * scaleRatio);
            _panY = cursorY - ((cursorY - _panY) * scaleRatio);
            
            vm.ZoomLevel = nextZoom * 100.0;
            e.Handled = true;
        }

        private void Preview_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SyncPreviewGeometry();
            if (DataContext is ImageSubsystemViewModel vm && vm.IsCropActive)
            {
                UpdateCropOverlayState();
            }
        }

        public void SyncCanvasBackgroundState(ImageSubsystemViewModel vm)
        {
            if (vm.IsRemoveBgMode)
            {
                RemoveBackgroundDropZone.SetResourceReference(Border.BackgroundProperty, "RemoveBackgroundCheckerboardBrush");
                ApplyCanvasBackgroundSelection();
            }
            else
            {
                RemoveBackgroundDropZone.SetResourceReference(Border.BackgroundProperty, "SurfaceSecondaryBrush");
                RemoveBackgroundAfterSurface.Background = Brushes.White;
            }
        }

        private void ApplyCanvasBackgroundSelection()
        {
            if (CanvasBackgroundComboBox == null)
            {
                RemoveBackgroundAfterSurface.SetResourceReference(Border.BackgroundProperty, "RemoveBackgroundCheckerboardBrush");
                return;
            }

            if (CanvasBackgroundComboBox.SelectedIndex == 1)
            {
                RemoveBackgroundAfterSurface.Background = Brushes.White;
            }
            else if (CanvasBackgroundComboBox.SelectedIndex == 2)
            {
                RemoveBackgroundAfterSurface.Background = Brushes.Black;
            }
            else
            {
                RemoveBackgroundAfterSurface.SetResourceReference(Border.BackgroundProperty, "RemoveBackgroundCheckerboardBrush");
            }
        }

        private void CanvasBackgroundComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs? e)
        {
            if (DataContext is ImageSubsystemViewModel vm && !vm.IsRemoveBgMode)
            {
                RemoveBackgroundAfterSurface.Background = Brushes.White;
                return;
            }
            ApplyCanvasBackgroundSelection();
        }

        private void Preview_MouseDown(object sender, MouseButtonEventArgs e)
        {
            RemoveBackgroundPreview.Focus();
            if (e.ChangedButton == MouseButton.Right || e.ChangedButton == MouseButton.Middle)
            {
                var bounds = GetPanBounds();
                if (!bounds.CanPan || _isPanning) return;

                _isPanning = true;
                _panStart = e.GetPosition(RemoveBackgroundPreview);
                _panStartX = _panX;
                _panStartY = _panY;
                RemoveBackgroundPreview.CaptureMouse();
                UpdatePanCursor();
                e.Handled = true;
            }
        }

        private void Preview_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning && (e.ChangedButton == MouseButton.Right || e.ChangedButton == MouseButton.Middle))
            {
                _isPanning = false;
                if (RemoveBackgroundPreview.IsMouseCaptured)
                    RemoveBackgroundPreview.ReleaseMouseCapture();
                UpdatePanCursor();
                e.Handled = true;
            }
        }

        private async void InpaintMaskCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ImageSubsystemViewModel vm && vm.IsInpaintMode && vm.IsAutoSelectMode)
            {
                if (ImgRemoveBackgroundBefore.Source is BitmapSource src &&
                    InpaintMaskCanvas.ActualWidth > 0 && InpaintMaskCanvas.ActualHeight > 0)
                {
                    var pos = e.GetPosition(InpaintMaskCanvas);
                    int nativeW = vm.OriginalImageWidth > 0 ? vm.OriginalImageWidth : src.PixelWidth;
                    int nativeH = vm.OriginalImageHeight > 0 ? vm.OriginalImageHeight : src.PixelHeight;
                    float nativeX = (float)(pos.X / InpaintMaskCanvas.ActualWidth * nativeW);
                    float nativeY = (float)(pos.Y / InpaintMaskCanvas.ActualHeight * nativeH);
                    nativeX = Math.Clamp(nativeX, 0f, nativeW - 1);
                    nativeY = Math.Clamp(nativeY, 0f, nativeH - 1);

                    e.Handled = true;

                    var point = new System.Drawing.PointF(nativeX, nativeY);
                    var success = await vm.AddAutoSelectPointAsync(point);
                    if (success)
                    {
                        _actionHistory.Push(MaskActionType.AutoSelect);
                        _redoHistory.Clear();
                        vm.HasMaskStrokes = InpaintMaskCanvas.Strokes.Count > 0 || vm.HasAutoMask;
                    }
                }
            }
        }

        private void InpaintMaskCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            UpdateBrushCursorPosition(e.GetPosition(InpaintMaskCanvas));
        }

        private void InpaintMaskCanvas_MouseEnter(object sender, MouseEventArgs e)
        {
            UpdateBrushCursorPosition(e.GetPosition(InpaintMaskCanvas));
        }

        private void InpaintMaskCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            BrushCursorRing.Visibility = Visibility.Collapsed;
        }

        private void UpdateBrushCursorPosition(Point pos)
        {
            if (DataContext is ImageSubsystemViewModel vm && (vm.IsRemoveBgRefining || (vm.IsInpaintMode && !vm.IsAutoSelectMode)))
            {
                double d = BrushCursorRing.Width;
                Canvas.SetLeft(BrushCursorRing, pos.X - d / 2.0);
                Canvas.SetTop(BrushCursorRing, pos.Y - d / 2.0);
                BrushCursorRing.Visibility = Visibility.Visible;
            }
            else
            {
                BrushCursorRing.Visibility = Visibility.Collapsed;
            }
        }

        private void InpaintMaskCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
        {
            if (DataContext is ImageSubsystemViewModel vmRemoveBg && vmRemoveBg.IsRemoveBgCanvasVisible)
            {
                var stroke = e.Stroke;
                InpaintMaskCanvas.Strokes.Remove(stroke);

                if (vmRemoveBg.RemoveBgWidth > 0 && vmRemoveBg.RemoveBgHeight > 0 &&
                    InpaintMaskCanvas.ActualWidth > 0 && InpaintMaskCanvas.ActualHeight > 0)
                {
                    double scaleX = (double)vmRemoveBg.RemoveBgWidth / InpaintMaskCanvas.ActualWidth;
                    double scaleY = (double)vmRemoveBg.RemoveBgHeight / InpaintMaskCanvas.ActualHeight;

                    var strokePoints = stroke.StylusPoints
                        .Select(p => new System.Drawing.PointF((float)(p.X * scaleX), (float)(p.Y * scaleY)))
                        .ToList();

                    float brushDiameter = (float)vmRemoveBg.BrushSize;
                    float brushSoftness = (float)vmRemoveBg.BrushSoftness;
                    float radius = brushDiameter / 2f;

                    float minX = float.MaxValue, minY = float.MaxValue;
                    float maxX = float.MinValue, maxY = float.MinValue;
                    foreach (var pt in strokePoints)
                    {
                        if (pt.X < minX) minX = pt.X;
                        if (pt.X > maxX) maxX = pt.X;
                        if (pt.Y < minY) minY = pt.Y;
                        if (pt.Y > maxY) maxY = pt.Y;
                    }

                    var strokeBounds = new System.Drawing.Rectangle(
                        (int)MathF.Floor(minX - radius),
                        (int)MathF.Floor(minY - radius),
                        (int)MathF.Ceiling(MathF.Max(0f, maxX - minX) + brushDiameter),
                        (int)MathF.Ceiling(MathF.Max(0f, maxY - minY) + brushDiameter));

                    int padding = 4;
                    var roi = RemoveBackgroundPipeline.CalculateStrokeRoi(
                        strokeBounds,
                        padding,
                        vmRemoveBg.RemoveBgWidth,
                        vmRemoveBg.RemoveBgHeight);

                    if (roi.Width > 0 && roi.Height > 0)
                    {
                        var localStrokeMask = RemoveBackgroundPipeline.RasterizePillStroke(
                            strokePoints,
                            brushDiameter,
                            brushSoftness,
                            roi);

                        vmRemoveBg.ApplyRemoveBgStroke(roi, localStrokeMask, vmRemoveBg.IsRemoveBgAddMode);
                    }
                }
                return;
            }

            _undoStack.Push(e.Stroke);
            _redoStack.Clear();
            _actionHistory.Push(MaskActionType.BrushStroke);
            _redoHistory.Clear();
            if (DataContext is ImageSubsystemViewModel vm)
            {
                vm.HasMaskStrokes = InpaintMaskCanvas.Strokes.Count > 0 || vm.HasAutoMask;
            }
        }

        private void InpaintMaskCanvas_StrokeErased(object sender, RoutedEventArgs e)
        {
            if (DataContext is ImageSubsystemViewModel vm)
            {
                vm.HasMaskStrokes = InpaintMaskCanvas.Strokes.Count > 0 || vm.HasAutoMask;
            }
        }

        private async void UndoStroke()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(UndoStroke);
                return;
            }

            if (_actionHistory.Count > 0)
            {
                var action = _actionHistory.Pop();
                if (action == MaskActionType.BrushStroke)
                {
                    if (InpaintMaskCanvas.Strokes.Count > 0)
                    {
                        var last = InpaintMaskCanvas.Strokes[InpaintMaskCanvas.Strokes.Count - 1];
                        InpaintMaskCanvas.Strokes.Remove(last);
                        _redoStack.Push(last);
                    }
                    _redoHistory.Push(MaskActionType.BrushStroke);
                }
                else if (action == MaskActionType.AutoSelect && DataContext is ImageSubsystemViewModel vm)
                {
                    await vm.UndoAutoMaskAsync();
                    _redoHistory.Push(MaskActionType.AutoSelect);
                }
            }
            else if (InpaintMaskCanvas.Strokes.Count > 0)
            {
                var last = InpaintMaskCanvas.Strokes[InpaintMaskCanvas.Strokes.Count - 1];
                InpaintMaskCanvas.Strokes.Remove(last);
                _redoStack.Push(last);
            }

            if (DataContext is ImageSubsystemViewModel vmFinal)
            {
                vmFinal.HasMaskStrokes = InpaintMaskCanvas.Strokes.Count > 0 || vmFinal.HasAutoMask;
            }
        }

        private async void RedoStroke()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(RedoStroke);
                return;
            }

            if (_redoHistory.Count > 0)
            {
                var action = _redoHistory.Pop();
                if (action == MaskActionType.BrushStroke)
                {
                    if (_redoStack.Count > 0)
                    {
                        var stroke = _redoStack.Pop();
                        InpaintMaskCanvas.Strokes.Add(stroke);
                        _undoStack.Push(stroke);
                    }
                    _actionHistory.Push(MaskActionType.BrushStroke);
                }
                else if (action == MaskActionType.AutoSelect && DataContext is ImageSubsystemViewModel vm)
                {
                    await vm.RedoAutoMaskAsync();
                    _actionHistory.Push(MaskActionType.AutoSelect);
                }
            }
            else if (_redoStack.Count > 0)
            {
                var stroke = _redoStack.Pop();
                InpaintMaskCanvas.Strokes.Add(stroke);
                _undoStack.Push(stroke);
            }

            if (DataContext is ImageSubsystemViewModel vmFinal)
            {
                vmFinal.HasMaskStrokes = InpaintMaskCanvas.Strokes.Count > 0 || vmFinal.HasAutoMask;
            }
        }

        private void ClearStrokes()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(ClearStrokes);
                return;
            }

            InpaintMaskCanvas.Strokes.Clear();
            _undoStack.Clear();
            _redoStack.Clear();
            _actionHistory.Clear();
            _redoHistory.Clear();
            if (DataContext is ImageSubsystemViewModel vm)
            {
                vm.ClearAutoMask();
                vm.HasMaskStrokes = false;
            }
        }

        private void UpdateBrushAttributes()
        {
            if (DataContext is not ImageSubsystemViewModel vm) return;

            if (vm.IsRemoveBgCanvasVisible)
            {
                InpaintMaskCanvas.EditingMode = InkCanvasEditingMode.Ink;
                var brushColor = vm.IsRemoveBgAddMode
                    ? Color.FromArgb(140, 16, 185, 129)  // Emerald green for Add Mask
                    : Color.FromArgb(140, 239, 68, 68);   // Crimson red for Remove Mask

                var dispRect = GetImageDisplayRect();
                double scaleX = dispRect.Width > 0 && vm.RemoveBgWidth > 0 ? (double)vm.RemoveBgWidth / dispRect.Width : 1.0;
                double scaleY = dispRect.Height > 0 && vm.RemoveBgHeight > 0 ? (double)vm.RemoveBgHeight / dispRect.Height : 1.0;
                double avgScale = Math.Max(0.0001, (scaleX + scaleY) / 2.0);
                double canvasBrushSize = Math.Max(2.0, vm.BrushSize / avgScale);

                InpaintMaskCanvas.DefaultDrawingAttributes = new DrawingAttributes
                {
                    Color = brushColor,
                    Width = canvasBrushSize,
                    Height = canvasBrushSize,
                    IsHighlighter = false,
                    StylusTip = StylusTip.Ellipse
                };
                InpaintMaskCanvas.UseCustomCursor = true;
                InpaintMaskCanvas.Cursor = Cursors.None;

                BrushCursorRing.Width = canvasBrushSize;
                BrushCursorRing.Height = canvasBrushSize;
                return;
            }

            if (vm.IsAutoSelectMode)
            {
                InpaintMaskCanvas.EditingMode = InkCanvasEditingMode.None;
                InpaintMaskCanvas.UseCustomCursor = true;
                InpaintMaskCanvas.Cursor = Cursors.Cross;
                BrushCursorRing.Visibility = Visibility.Collapsed;
            }
            else if (vm.IsEraserMode)
            {
                InpaintMaskCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
                InpaintMaskCanvas.EraserShape = new EllipseStylusShape(vm.BrushSize, vm.BrushSize);
                InpaintMaskCanvas.UseCustomCursor = true;
                InpaintMaskCanvas.Cursor = Cursors.None;
                BrushCursorRing.Width = vm.BrushSize;
                BrushCursorRing.Height = vm.BrushSize;
            }
            else
            {
                InpaintMaskCanvas.EditingMode = InkCanvasEditingMode.Ink;
                InpaintMaskCanvas.DefaultDrawingAttributes = new DrawingAttributes
                {
                    Color = Color.FromArgb(140, 255, 59, 48), // Bright semi-transparent red
                    Width = vm.BrushSize,
                    Height = vm.BrushSize,
                    IsHighlighter = false,
                    StylusTip = StylusTip.Ellipse
                };
                InpaintMaskCanvas.UseCustomCursor = true;
                InpaintMaskCanvas.Cursor = Cursors.None;
                BrushCursorRing.Width = vm.BrushSize;
                BrushCursorRing.Height = vm.BrushSize;
            }
        }

        private Rect GetImageDisplayRect()
        {
            if (ImgRemoveBackgroundBefore.Source is not BitmapSource src || src.PixelWidth <= 0 || src.PixelHeight <= 0)
                return new Rect(0, 0, Math.Max(0, RemoveBackgroundImageStage.ActualWidth), Math.Max(0, RemoveBackgroundImageStage.ActualHeight));

            double stageW = RemoveBackgroundImageStage.ActualWidth;
            double stageH = RemoveBackgroundImageStage.ActualHeight;
            if (stageW <= 0 || stageH <= 0) return Rect.Empty;

            double imgAspect = (double)src.PixelWidth / src.PixelHeight;
            double stageAspect = stageW / stageH;

            double dispW, dispH;
            if (stageAspect > imgAspect)
            {
                dispH = stageH;
                dispW = stageH * imgAspect;
            }
            else
            {
                dispW = stageW;
                dispH = stageW / imgAspect;
            }

            return new Rect(0, 0, dispW, dispH);
        }

        public async Task<System.Drawing.Bitmap?> GenerateMaskBitmapAsync()
        {
            var vm = DataContext as ImageSubsystemViewModel;
            bool hasStrokes = InpaintMaskCanvas.Strokes.Count > 0;
            bool hasAuto = vm?.HasAutoMask == true && vm.GetAccumulatedAutoMask() != null;

            if (!hasStrokes && !hasAuto) return null;
            if (ImgRemoveBackgroundBefore.Source is not BitmapSource src) return null;

            int nativeWidth = vm?.OriginalImageWidth > 0 ? vm.OriginalImageWidth : src.PixelWidth;
            int nativeHeight = vm?.OriginalImageHeight > 0 ? vm.OriginalImageHeight : src.PixelHeight;
            double canvasWidth = InpaintMaskCanvas.ActualWidth;
            double canvasHeight = InpaintMaskCanvas.ActualHeight;

            if (canvasWidth <= 0 || canvasHeight <= 0 || nativeWidth <= 0 || nativeHeight <= 0)
                return null;

            byte[]? rtbPixels = null;
            int rtbStride = nativeWidth * 4;

            if (hasStrokes)
            {
                double scaleX = nativeWidth / canvasWidth;
                double scaleY = nativeHeight / canvasHeight;

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, nativeWidth, nativeHeight));

                    foreach (var stroke in InpaintMaskCanvas.Strokes)
                    {
                        var nativeStroke = stroke.Clone();
                        var matrix = Matrix.Identity;
                        matrix.Scale(scaleX, scaleY);
                        nativeStroke.Transform(matrix, false);

                        var maskAttrs = nativeStroke.DrawingAttributes.Clone();
                        maskAttrs.Color = Colors.White;
                        maskAttrs.IsHighlighter = false;
                        maskAttrs.Width *= scaleX;
                        maskAttrs.Height *= scaleY;

                        nativeStroke.Draw(dc, maskAttrs);
                    }
                }

                var rtb = new RenderTargetBitmap(nativeWidth, nativeHeight, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(visual);

                rtbPixels = new byte[rtbStride * nativeHeight];
                rtb.CopyPixels(rtbPixels, rtbStride, 0);
            }

            byte[]? autoMaskBytes = hasAuto ? vm!.GetAccumulatedAutoMask() : null;

            // Offload format conversion and pixel loop entirely to background thread
            return await Task.Run(() =>
            {
                var bmp = new System.Drawing.Bitmap(nativeWidth, nativeHeight, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                var rect = new System.Drawing.Rectangle(0, 0, nativeWidth, nativeHeight);
                var bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

                try
                {
                    int bmpStride = bmpData.Stride;
                    byte[] bmpBytes = new byte[bmpStride * nativeHeight];

                    for (int y = 0; y < nativeHeight; y++)
                    {
                        int rtbRowOffset = y * rtbStride;
                        int bmpRowOffset = y * bmpStride;
                        int autoRowOffset = y * nativeWidth;

                        for (int x = 0; x < nativeWidth; x++)
                        {
                            byte strokeVal = 0;
                            if (rtbPixels != null)
                            {
                                int rtbIdx = rtbRowOffset + (x * 4);
                                strokeVal = rtbPixels[rtbIdx + 2]; // White intensity (R==G==B)
                            }

                            byte autoVal = 0;
                            if (autoMaskBytes != null)
                            {
                                autoVal = autoMaskBytes[autoRowOffset + x];
                            }

                            byte val = Math.Max(strokeVal, autoVal);

                            int bmpIdx = bmpRowOffset + (x * 3);
                            bmpBytes[bmpIdx] = val;     // B
                            bmpBytes[bmpIdx + 1] = val; // G
                            bmpBytes[bmpIdx + 2] = val; // R
                        }
                    }

                    System.Runtime.InteropServices.Marshal.Copy(bmpBytes, 0, bmpData.Scan0, bmpBytes.Length);
                }
                finally
                {
                    bmp.UnlockBits(bmpData);
                }

                return bmp;
            }).ConfigureAwait(false);
        }

        public System.Drawing.Bitmap? GenerateMaskBitmap()
        {
            return GenerateMaskBitmapAsync().GetAwaiter().GetResult();
        }

        internal bool IsSpacebarHolding => _isSpacebarHolding;
        internal double SavedCompareRatio => _savedCompareRatio;

        internal void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                if (_isSpacebarHolding)
                {
                    e.Handled = true;
                    return;
                }

                if (e.IsRepeat)
                    return;

                if (e.OriginalSource is TextBoxBase)
                    return;

                if (DataContext is not ImageSubsystemViewModel vm)
                    return;

                if (!vm.IsImageLoaded || !vm.HasResult || vm.IsRemoveBgRefining || vm.IsProcessing)
                    return;

                _savedCompareRatio = vm.CompareRatio;
                _isSpacebarHolding = true;
                vm.CompareRatio = 0.0;
                e.Handled = true;
            }
        }

        internal void UserControl_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && _isSpacebarHolding)
            {
                EndSpacebarHold();
                e.Handled = true;
            }
        }

        internal void UserControl_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            EndSpacebarHold();
        }

        internal void EndSpacebarHold()
        {
            if (_isSpacebarHolding)
            {
                if (DataContext is ImageSubsystemViewModel vm)
                {
                    vm.CompareRatio = _savedCompareRatio;
                }
                _isSpacebarHolding = false;
            }
        }

        #region Crop Subsystem (CommunityToolkit Ported Math & Microsoft Photos Parity)
        // Licensed to the .NET Foundation under one or more agreements.
        // The .NET Foundation licenses this file to you under the MIT license.
        // See the LICENSE file in the project root for more information.
        // Ported and adapted from Microsoft CommunityToolkit ImageCropper for WPF.

        internal enum CropThumbPosition
        {
            Top,
            Bottom,
            Left,
            Right,
            UpperLeft,
            UpperRight,
            LowerLeft,
            LowerRight
        }

        private Rect _canonicalNormalizedCrop = new Rect(0, 0, 1, 1);
        private Rect _cropRect = Rect.Empty;
        private Rect _imageDisplayBounds = Rect.Empty;
        private double _selectedAspectRatio = -1; // -1 = Free

        private void UpdateCropOverlayState()
        {
            if (DataContext is not ImageSubsystemViewModel vm) return;

            if (vm.IsCropActive)
            {
                _imageDisplayBounds = GetRenderedImageBounds();
                ProjectCanonicalCropToScreen();
                PositionCropElements();
            }
        }

        private GeneralTransform GetActiveElementToPreviewTransform()
        {
            var activeElement = (DataContext is ImageSubsystemViewModel vm && vm.HasResult && ImgRemoveBackgroundAfter.Source is BitmapSource)
                ? ImgRemoveBackgroundAfter
                : ImgRemoveBackgroundBefore;

            double stageW = RemoveBackgroundImageStage.ActualWidth > 0
                ? RemoveBackgroundImageStage.ActualWidth
                : Math.Max(1.0, RemoveBackgroundPreview.ActualWidth - 8.0);
            double stageH = RemoveBackgroundImageStage.ActualHeight > 0
                ? RemoveBackgroundImageStage.ActualHeight
                : Math.Max(1.0, RemoveBackgroundPreview.ActualHeight - 8.0);

            double elW = activeElement.ActualWidth > 0 ? activeElement.ActualWidth : stageW;
            double elH = activeElement.ActualHeight > 0 ? activeElement.ActualHeight : stageH;

            double stageX = 4.0;
            double stageY = 4.0;
            if (RemoveBackgroundImageStage.Margin is Thickness m)
            {
                stageX = m.Left;
                stageY = m.Top;
            }

            var group = new TransformGroup();
            group.Children.Add(new TranslateTransform(-elW / 2.0, -elH / 2.0));
            if (RemoveBackgroundImageStage.RenderTransform is Transform rt)
            {
                group.Children.Add(rt);
            }
            group.Children.Add(new TranslateTransform(stageW / 2.0 + stageX, stageH / 2.0 + stageY));
            return group;
        }

        private void ProjectCanonicalCropToScreen()
        {
            if (DataContext is not ImageSubsystemViewModel vm || !vm.IsCropActive)
                return;

            var activeElement = (vm.HasResult && ImgRemoveBackgroundAfter.Source is BitmapSource)
                ? ImgRemoveBackgroundAfter
                : ImgRemoveBackgroundBefore;

            if (activeElement.Source is not BitmapSource src || src.PixelWidth <= 0 || src.PixelHeight <= 0)
                return;

            double elW = activeElement.ActualWidth > 0 ? activeElement.ActualWidth : Math.Max(1.0, RemoveBackgroundPreview.ActualWidth - 8.0);
            double elH = activeElement.ActualHeight > 0 ? activeElement.ActualHeight : Math.Max(1.0, RemoveBackgroundPreview.ActualHeight - 8.0);

            double scale = Math.Min(elW / src.PixelWidth, elH / src.PixelHeight);
            double fitW = src.PixelWidth * scale;
            double fitH = src.PixelHeight * scale;
            if (fitW <= 0 || fitH <= 0) return;
            double fitX = (elW - fitW) / 2.0;
            double fitY = (elH - fitH) / 2.0;

            double u = Math.Max(0.0, Math.Min(1.0, _canonicalNormalizedCrop.X));
            double v = Math.Max(0.0, Math.Min(1.0, _canonicalNormalizedCrop.Y));
            double w = Math.Max(0.001, Math.Min(1.0 - u, _canonicalNormalizedCrop.Width));
            double h = Math.Max(0.001, Math.Min(1.0 - v, _canonicalNormalizedCrop.Height));
            _canonicalNormalizedCrop = new Rect(u, v, w, h);

            var p1 = new Point(fitX + u * fitW, fitY + v * fitH);
            var p2 = new Point(fitX + (u + w) * fitW, fitY + v * fitH);
            var p3 = new Point(fitX + (u + w) * fitW, fitY + (v + h) * fitH);
            var p4 = new Point(fitX + u * fitW, fitY + (v + h) * fitH);

            try
            {
                var transform = GetActiveElementToPreviewTransform();
                var s1 = transform.Transform(p1);
                var s2 = transform.Transform(p2);
                var s3 = transform.Transform(p3);
                var s4 = transform.Transform(p4);

                double minX = Math.Min(Math.Min(s1.X, s2.X), Math.Min(s3.X, s4.X));
                double maxX = Math.Max(Math.Max(s1.X, s2.X), Math.Max(s3.X, s4.X));
                double minY = Math.Min(Math.Min(s1.Y, s2.Y), Math.Min(s3.Y, s4.Y));
                double maxY = Math.Max(Math.Max(s1.Y, s2.Y), Math.Max(s3.Y, s4.Y));

                _cropRect = new Rect(minX, minY, Math.Max(1.0, maxX - minX), Math.Max(1.0, maxY - minY));
            }
            catch
            {
                _cropRect = _imageDisplayBounds;
            }
        }

        private void UpdateCanonicalCropFromScreen()
        {
            if (DataContext is not ImageSubsystemViewModel vm || !vm.IsCropActive || _cropRect.IsEmpty)
                return;

            var activeElement = (vm.HasResult && ImgRemoveBackgroundAfter.Source is BitmapSource)
                ? ImgRemoveBackgroundAfter
                : ImgRemoveBackgroundBefore;

            if (activeElement.Source is not BitmapSource src || src.PixelWidth <= 0 || src.PixelHeight <= 0)
                return;

            double elW = activeElement.ActualWidth > 0 ? activeElement.ActualWidth : Math.Max(1.0, RemoveBackgroundPreview.ActualWidth - 8.0);
            double elH = activeElement.ActualHeight > 0 ? activeElement.ActualHeight : Math.Max(1.0, RemoveBackgroundPreview.ActualHeight - 8.0);

            double scale = Math.Min(elW / src.PixelWidth, elH / src.PixelHeight);
            double fitW = src.PixelWidth * scale;
            double fitH = src.PixelHeight * scale;
            if (fitW <= 0 || fitH <= 0) return;
            double fitX = (elW - fitW) / 2.0;
            double fitY = (elH - fitH) / 2.0;

            try
            {
                var transform = GetActiveElementToPreviewTransform();
                var inverse = transform.Inverse;
                if (inverse == null) return;

                var c1 = new Point(_cropRect.Left, _cropRect.Top);
                var c2 = new Point(_cropRect.Right, _cropRect.Top);
                var c3 = new Point(_cropRect.Right, _cropRect.Bottom);
                var c4 = new Point(_cropRect.Left, _cropRect.Bottom);

                var l1 = inverse.Transform(c1);
                var l2 = inverse.Transform(c2);
                var l3 = inverse.Transform(c3);
                var l4 = inverse.Transform(c4);

                double u1 = (l1.X - fitX) / fitW;
                double v1 = (l1.Y - fitY) / fitH;
                double u2 = (l2.X - fitX) / fitW;
                double v2 = (l2.Y - fitY) / fitH;
                double u3 = (l3.X - fitX) / fitW;
                double v3 = (l3.Y - fitY) / fitH;
                double u4 = (l4.X - fitX) / fitW;
                double v4 = (l4.Y - fitY) / fitH;

                double minU = Math.Min(Math.Min(u1, u2), Math.Min(u3, u4));
                double maxU = Math.Max(Math.Max(u1, u2), Math.Max(u3, u4));
                double minV = Math.Min(Math.Min(v1, v2), Math.Min(v3, v4));
                double maxV = Math.Max(Math.Max(v1, v2), Math.Max(v3, v4));

                double u = Math.Max(0.0, Math.Min(1.0, minU));
                double v = Math.Max(0.0, Math.Min(1.0, minV));
                double w = Math.Max(0.001, Math.Min(1.0 - u, maxU - minU));
                double h = Math.Max(0.001, Math.Min(1.0 - v, maxV - minV));

                _canonicalNormalizedCrop = new Rect(u, v, w, h);
            }
            catch
            {
            }
        }

        private Rect GetRenderedImageBounds()
        {
            if (DataContext is not ImageSubsystemViewModel vm)
            {
                return new Rect(0, 0, Math.Max(100, RemoveBackgroundPreview.ActualWidth), Math.Max(100, RemoveBackgroundPreview.ActualHeight));
            }

            var activeElement = (vm.HasResult && ImgRemoveBackgroundAfter.Source is BitmapSource)
                ? ImgRemoveBackgroundAfter
                : ImgRemoveBackgroundBefore;

            if (activeElement.Source is not BitmapSource src || src.PixelWidth <= 0 || src.PixelHeight <= 0)
            {
                return new Rect(0, 0, Math.Max(100, RemoveBackgroundPreview.ActualWidth), Math.Max(100, RemoveBackgroundPreview.ActualHeight));
            }

            double elW = activeElement.ActualWidth > 0 ? activeElement.ActualWidth : Math.Max(1.0, RemoveBackgroundPreview.ActualWidth - 8.0);
            double elH = activeElement.ActualHeight > 0 ? activeElement.ActualHeight : Math.Max(1.0, RemoveBackgroundPreview.ActualHeight - 8.0);

            double scale = Math.Min(elW / src.PixelWidth, elH / src.PixelHeight);
            double fitW = src.PixelWidth * scale;
            double fitH = src.PixelHeight * scale;
            double fitX = (elW - fitW) / 2.0;
            double fitY = (elH - fitH) / 2.0;
            var localImageRect = new Rect(fitX, fitY, fitW, fitH);

            try
            {
                var transform = GetActiveElementToPreviewTransform();
                var bounds = transform.TransformBounds(localImageRect);
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    return bounds;
                }
            }
            catch
            {
            }

            bool isSwapped = ((int)Math.Round(vm.RotationAngle) % 180) != 0;
            double imgW = isSwapped ? src.PixelHeight : src.PixelWidth;
            double imgH = isSwapped ? src.PixelWidth : src.PixelHeight;

            double previewW = RemoveBackgroundPreview.ActualWidth;
            double previewH = RemoveBackgroundPreview.ActualHeight;
            if (previewW <= 0 || previewH <= 0) return Rect.Empty;

            double previewAspect = previewW / previewH;
            double imgAspect = imgW / imgH;

            double dispW, dispH;
            if (previewAspect > imgAspect)
            {
                dispH = previewH;
                dispW = previewH * imgAspect;
            }
            else
            {
                dispW = previewW;
                dispH = previewW / imgAspect;
            }

            double left = (previewW - dispW) / 2.0;
            double top = (previewH - dispH) / 2.0;

            return new Rect(left, top, dispW, dispH);
        }

        private void PositionCropElements()
        {
            if (CropCanvas == null || _cropRect.IsEmpty || _cropRect.Width <= 0 || _cropRect.Height <= 0)
                return;

            double canvasW = RemoveBackgroundPreview.ActualWidth;
            double canvasH = RemoveBackgroundPreview.ActualHeight;
            if (canvasW <= 0 || canvasH <= 0) return;

            Canvas.SetLeft(CropScrimTop, 0);
            Canvas.SetTop(CropScrimTop, 0);
            CropScrimTop.Width = canvasW;
            CropScrimTop.Height = Math.Max(0, _cropRect.Top);

            Canvas.SetLeft(CropScrimBottom, 0);
            Canvas.SetTop(CropScrimBottom, _cropRect.Bottom);
            CropScrimBottom.Width = canvasW;
            CropScrimBottom.Height = Math.Max(0, canvasH - _cropRect.Bottom);

            Canvas.SetLeft(CropScrimLeft, 0);
            Canvas.SetTop(CropScrimLeft, _cropRect.Top);
            CropScrimLeft.Width = Math.Max(0, _cropRect.Left);
            CropScrimLeft.Height = Math.Max(0, _cropRect.Height);

            Canvas.SetLeft(CropScrimRight, _cropRect.Right);
            Canvas.SetTop(CropScrimRight, _cropRect.Top);
            CropScrimRight.Width = Math.Max(0, canvasW - _cropRect.Right);
            CropScrimRight.Height = Math.Max(0, _cropRect.Height);

            Canvas.SetLeft(CropSelectionFrame, _cropRect.Left);
            Canvas.SetTop(CropSelectionFrame, _cropRect.Top);
            CropSelectionFrame.Width = Math.Max(0, _cropRect.Width);
            CropSelectionFrame.Height = Math.Max(0, _cropRect.Height);

            const double cornerOffset = 7.0;
            const double sideOffset = 6.0;

            Canvas.SetLeft(ThumbCropUpperLeft, _cropRect.Left - cornerOffset);
            Canvas.SetTop(ThumbCropUpperLeft, _cropRect.Top - cornerOffset);

            Canvas.SetLeft(ThumbCropUpperRight, _cropRect.Right - cornerOffset);
            Canvas.SetTop(ThumbCropUpperRight, _cropRect.Top - cornerOffset);

            Canvas.SetLeft(ThumbCropLowerLeft, _cropRect.Left - cornerOffset);
            Canvas.SetTop(ThumbCropLowerLeft, _cropRect.Bottom - cornerOffset);

            Canvas.SetLeft(ThumbCropLowerRight, _cropRect.Right - cornerOffset);
            Canvas.SetTop(ThumbCropLowerRight, _cropRect.Bottom - cornerOffset);

            Canvas.SetLeft(ThumbCropTop, _cropRect.Left + (_cropRect.Width / 2.0) - sideOffset);
            Canvas.SetTop(ThumbCropTop, _cropRect.Top - sideOffset);

            Canvas.SetLeft(ThumbCropBottom, _cropRect.Left + (_cropRect.Width / 2.0) - sideOffset);
            Canvas.SetTop(ThumbCropBottom, _cropRect.Bottom - sideOffset);

            Canvas.SetLeft(ThumbCropLeft, _cropRect.Left - sideOffset);
            Canvas.SetTop(ThumbCropLeft, _cropRect.Top + (_cropRect.Height / 2.0) - sideOffset);

            Canvas.SetLeft(ThumbCropRight, _cropRect.Right - sideOffset);
            Canvas.SetTop(ThumbCropRight, _cropRect.Top + (_cropRect.Height / 2.0) - sideOffset);
        }

        private void CropThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not Thumb thumb) return;

            CropThumbPosition pos;
            if (thumb == ThumbCropTop) pos = CropThumbPosition.Top;
            else if (thumb == ThumbCropBottom) pos = CropThumbPosition.Bottom;
            else if (thumb == ThumbCropLeft) pos = CropThumbPosition.Left;
            else if (thumb == ThumbCropRight) pos = CropThumbPosition.Right;
            else if (thumb == ThumbCropUpperLeft) pos = CropThumbPosition.UpperLeft;
            else if (thumb == ThumbCropUpperRight) pos = CropThumbPosition.UpperRight;
            else if (thumb == ThumbCropLowerLeft) pos = CropThumbPosition.LowerLeft;
            else if (thumb == ThumbCropLowerRight) pos = CropThumbPosition.LowerRight;
            else return;

            UpdateCroppedRect(pos, new Point(e.HorizontalChange, e.VerticalChange));
        }

        private void UpdateCroppedRect(CropThumbPosition position, Point diffPos)
        {
            if (diffPos == default || _imageDisplayBounds.IsEmpty)
                return;

            bool keepAspectRatio = _selectedAspectRatio > 0;
            double actualAspectRatio = keepAspectRatio ? _selectedAspectRatio : 1.0;
            double radian = 0d, diffPointRadian = 0d;
            if (keepAspectRatio)
            {
                radian = Math.Atan(actualAspectRatio);
                diffPointRadian = Math.Abs(diffPos.Y) > 0.0001 ? Math.Atan(diffPos.X / diffPos.Y) : 0d;
            }

            var startPoint = new Point(_cropRect.Left, _cropRect.Top);
            var endPoint = new Point(_cropRect.Right, _cropRect.Bottom);
            var currentSelectedRect = new Rect(startPoint, endPoint);

            switch (position)
            {
                case CropThumbPosition.Top:
                    if (keepAspectRatio)
                    {
                        var originSizeChange = new Point(-diffPos.Y * actualAspectRatio, -diffPos.Y);
                        var safeChange = GetSafeSizeChangeWhenKeepAspectRatio(_imageDisplayBounds, position, currentSelectedRect, originSizeChange, actualAspectRatio);
                        startPoint.X += -safeChange.X / 2.0;
                        endPoint.X += safeChange.X / 2.0;
                        startPoint.Y += -safeChange.Y;
                    }
                    else
                    {
                        startPoint.Y += diffPos.Y;
                    }
                    break;

                case CropThumbPosition.Bottom:
                    if (keepAspectRatio)
                    {
                        var originSizeChange = new Point(diffPos.Y * actualAspectRatio, diffPos.Y);
                        var safeChange = GetSafeSizeChangeWhenKeepAspectRatio(_imageDisplayBounds, position, currentSelectedRect, originSizeChange, actualAspectRatio);
                        startPoint.X += -safeChange.X / 2.0;
                        endPoint.X += safeChange.X / 2.0;
                        endPoint.Y += safeChange.Y;
                    }
                    else
                    {
                        endPoint.Y += diffPos.Y;
                    }
                    break;

                case CropThumbPosition.Left:
                    if (keepAspectRatio)
                    {
                        var originSizeChange = new Point(-diffPos.X, -diffPos.X / actualAspectRatio);
                        var safeChange = GetSafeSizeChangeWhenKeepAspectRatio(_imageDisplayBounds, position, currentSelectedRect, originSizeChange, actualAspectRatio);
                        startPoint.Y += -safeChange.Y / 2.0;
                        endPoint.Y += safeChange.Y / 2.0;
                        startPoint.X += -safeChange.X;
                    }
                    else
                    {
                        startPoint.X += diffPos.X;
                    }
                    break;

                case CropThumbPosition.Right:
                    if (keepAspectRatio)
                    {
                        var originSizeChange = new Point(diffPos.X, diffPos.X / actualAspectRatio);
                        var safeChange = GetSafeSizeChangeWhenKeepAspectRatio(_imageDisplayBounds, position, currentSelectedRect, originSizeChange, actualAspectRatio);
                        startPoint.Y += -safeChange.Y / 2.0;
                        endPoint.Y += safeChange.Y / 2.0;
                        endPoint.X += safeChange.X;
                    }
                    else
                    {
                        endPoint.X += diffPos.X;
                    }
                    break;

                case CropThumbPosition.UpperLeft:
                    if (keepAspectRatio)
                    {
                        var effectiveLength = Math.Abs(Math.Cos(diffPointRadian)) > 0.0001
                            ? diffPos.Y / Math.Cos(diffPointRadian) * Math.Cos(diffPointRadian - radian)
                            : diffPos.Y;
                        var originSizeChange = new Point(-effectiveLength * Math.Sin(radian), -effectiveLength * Math.Cos(radian));
                        var safeChange = GetSafeSizeChangeWhenKeepAspectRatio(_imageDisplayBounds, position, currentSelectedRect, originSizeChange, actualAspectRatio);
                        diffPos.X = -safeChange.X;
                        diffPos.Y = -safeChange.Y;
                    }
                    startPoint.X += diffPos.X;
                    startPoint.Y += diffPos.Y;
                    break;

                case CropThumbPosition.UpperRight:
                    if (keepAspectRatio)
                    {
                        diffPointRadian = -diffPointRadian;
                        var effectiveLength = Math.Abs(Math.Cos(diffPointRadian)) > 0.0001
                            ? diffPos.Y / Math.Cos(diffPointRadian) * Math.Cos(diffPointRadian - radian)
                            : diffPos.Y;
                        var originSizeChange = new Point(-effectiveLength * Math.Sin(radian), -effectiveLength * Math.Cos(radian));
                        var safeChange = GetSafeSizeChangeWhenKeepAspectRatio(_imageDisplayBounds, position, currentSelectedRect, originSizeChange, actualAspectRatio);
                        diffPos.X = safeChange.X;
                        diffPos.Y = -safeChange.Y;
                    }
                    endPoint.X += diffPos.X;
                    startPoint.Y += diffPos.Y;
                    break;

                case CropThumbPosition.LowerLeft:
                    if (keepAspectRatio)
                    {
                        diffPointRadian = -diffPointRadian;
                        var effectiveLength = Math.Abs(Math.Cos(diffPointRadian)) > 0.0001
                            ? diffPos.Y / Math.Cos(diffPointRadian) * Math.Cos(diffPointRadian - radian)
                            : diffPos.Y;
                        var originSizeChange = new Point(effectiveLength * Math.Sin(radian), effectiveLength * Math.Cos(radian));
                        var safeChange = GetSafeSizeChangeWhenKeepAspectRatio(_imageDisplayBounds, position, currentSelectedRect, originSizeChange, actualAspectRatio);
                        diffPos.X = -safeChange.X;
                        diffPos.Y = safeChange.Y;
                    }
                    startPoint.X += diffPos.X;
                    endPoint.Y += diffPos.Y;
                    break;

                case CropThumbPosition.LowerRight:
                    if (keepAspectRatio)
                    {
                        var effectiveLength = Math.Abs(Math.Cos(diffPointRadian)) > 0.0001
                            ? diffPos.Y / Math.Cos(diffPointRadian) * Math.Cos(diffPointRadian - radian)
                            : diffPos.Y;
                        var originSizeChange = new Point(effectiveLength * Math.Sin(radian), effectiveLength * Math.Cos(radian));
                        var safeChange = GetSafeSizeChangeWhenKeepAspectRatio(_imageDisplayBounds, position, currentSelectedRect, originSizeChange, actualAspectRatio);
                        diffPos.X = safeChange.X;
                        diffPos.Y = safeChange.Y;
                    }
                    endPoint.X += diffPos.X;
                    endPoint.Y += diffPos.Y;
                    break;
            }

            const double minSize = 32.0;
            if (endPoint.X - startPoint.X < minSize)
            {
                if (position == CropThumbPosition.Left || position == CropThumbPosition.UpperLeft || position == CropThumbPosition.LowerLeft)
                    startPoint.X = endPoint.X - minSize;
                else
                    endPoint.X = startPoint.X + minSize;
            }
            if (endPoint.Y - startPoint.Y < minSize)
            {
                if (position == CropThumbPosition.Top || position == CropThumbPosition.UpperLeft || position == CropThumbPosition.UpperRight)
                    startPoint.Y = endPoint.Y - minSize;
                else
                    endPoint.Y = startPoint.Y + minSize;
            }

            startPoint.X = Math.Max(_imageDisplayBounds.Left, Math.Min(_imageDisplayBounds.Right - minSize, startPoint.X));
            startPoint.Y = Math.Max(_imageDisplayBounds.Top, Math.Min(_imageDisplayBounds.Bottom - minSize, startPoint.Y));
            endPoint.X = Math.Min(_imageDisplayBounds.Right, Math.Max(_imageDisplayBounds.Left + minSize, endPoint.X));
            endPoint.Y = Math.Min(_imageDisplayBounds.Bottom, Math.Max(_imageDisplayBounds.Top + minSize, endPoint.Y));

            _cropRect = new Rect(startPoint, endPoint);
            UpdateCanonicalCropFromScreen();
            PositionCropElements();
        }

        private static Point GetSafeSizeChangeWhenKeepAspectRatio(Rect targetRect, CropThumbPosition thumbPosition, Rect selectedRect, Point originSizeChange, double aspectRatio)
        {
            var safeWidthChange = originSizeChange.X;
            var safeHeightChange = originSizeChange.Y;
            var maxWidthChange = 0d;
            var maxHeightChange = 0d;
            switch (thumbPosition)
            {
                case CropThumbPosition.Top:
                    maxWidthChange = targetRect.Width - selectedRect.Width;
                    maxHeightChange = selectedRect.Top - targetRect.Top;
                    break;
                case CropThumbPosition.Bottom:
                    maxWidthChange = targetRect.Width - selectedRect.Width;
                    maxHeightChange = targetRect.Bottom - selectedRect.Bottom;
                    break;
                case CropThumbPosition.Left:
                    maxWidthChange = selectedRect.Left - targetRect.Left;
                    maxHeightChange = targetRect.Height - selectedRect.Height;
                    break;
                case CropThumbPosition.Right:
                    maxWidthChange = targetRect.Right - selectedRect.Right;
                    maxHeightChange = targetRect.Height - selectedRect.Height;
                    break;
                case CropThumbPosition.UpperLeft:
                    maxWidthChange = selectedRect.Left - targetRect.Left;
                    maxHeightChange = selectedRect.Top - targetRect.Top;
                    break;
                case CropThumbPosition.UpperRight:
                    maxWidthChange = targetRect.Right - selectedRect.Right;
                    maxHeightChange = selectedRect.Top - targetRect.Top;
                    break;
                case CropThumbPosition.LowerLeft:
                    maxWidthChange = selectedRect.Left - targetRect.Left;
                    maxHeightChange = targetRect.Bottom - selectedRect.Bottom;
                    break;
                case CropThumbPosition.LowerRight:
                    maxWidthChange = targetRect.Right - selectedRect.Right;
                    maxHeightChange = targetRect.Bottom - selectedRect.Bottom;
                    break;
            }

            if (originSizeChange.X > maxWidthChange)
            {
                safeWidthChange = maxWidthChange;
                safeHeightChange = safeWidthChange / aspectRatio;
            }

            if (originSizeChange.Y > maxHeightChange)
            {
                safeHeightChange = maxHeightChange;
                safeWidthChange = safeHeightChange * aspectRatio;
            }

            return new Point(safeWidthChange, safeHeightChange);
        }

        private void BtnAspect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                _selectedAspectRatio = tag switch
                {
                    "1:1" => 1.0,
                    "4:3" => 4.0 / 3.0,
                    "16:9" => 16.0 / 9.0,
                    _ => -1
                };

                if (_selectedAspectRatio > 0 && !_imageDisplayBounds.IsEmpty)
                {
                    double targetAspect = _selectedAspectRatio;
                    double boundsAspect = _imageDisplayBounds.Width / _imageDisplayBounds.Height;
                    double w, h;
                    if (targetAspect > boundsAspect)
                    {
                        w = _imageDisplayBounds.Width;
                        h = w / targetAspect;
                    }
                    else
                    {
                        h = _imageDisplayBounds.Height;
                        w = h * targetAspect;
                    }
                    double cx = _imageDisplayBounds.X + (_imageDisplayBounds.Width / 2.0);
                    double cy = _imageDisplayBounds.Y + (_imageDisplayBounds.Height / 2.0);
                    _cropRect = new Rect(cx - (w / 2.0), cy - (h / 2.0), w, h);
                    UpdateCanonicalCropFromScreen();
                    PositionCropElements();
                }
            }
        }

        private void BtnResetCropArea_Click(object sender, RoutedEventArgs e)
        {
            _selectedAspectRatio = -1;
            _canonicalNormalizedCrop = new Rect(0, 0, 1, 1);
            UpdateCropOverlayState();
        }

        private void BtnCancelCrop_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ImageSubsystemViewModel vm)
            {
                vm.IsCropActive = false;
            }
        }

        private void BtnApplyCrop_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ImageSubsystemViewModel vm ||
                _canonicalNormalizedCrop.IsEmpty ||
                _canonicalNormalizedCrop.Width <= 0 ||
                _canonicalNormalizedCrop.Height <= 0)
            {
                return;
            }

            var sourcePath = vm.GetCurrentEditorSourcePath();
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return;
            }

            try
            {
                // 1. Load active editor bitmap
                using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var rawBmp = new System.Drawing.Bitmap(stream);

                // 2. Slice canonical region directly from source bitmap
                int pxX = (int)Math.Round(_canonicalNormalizedCrop.X * rawBmp.Width);
                int pxY = (int)Math.Round(_canonicalNormalizedCrop.Y * rawBmp.Height);
                int pxW = (int)Math.Round(_canonicalNormalizedCrop.Width * rawBmp.Width);
                int pxH = (int)Math.Round(_canonicalNormalizedCrop.Height * rawBmp.Height);

                pxX = Math.Max(0, Math.Min(rawBmp.Width - 1, pxX));
                pxY = Math.Max(0, Math.Min(rawBmp.Height - 1, pxY));
                pxW = Math.Max(1, Math.Min(rawBmp.Width - pxX, pxW));
                pxH = Math.Max(1, Math.Min(rawBmp.Height - pxY, pxH));

                using var cropped = rawBmp.Clone(new System.Drawing.Rectangle(pxX, pxY, pxW, pxH), rawBmp.PixelFormat);

                // 3. Bake active orientation (Rotate + Flip) into the cropped result
                var flipType = GetRotateFlipType(vm.RotationAngle, vm.ScaleX < 0, vm.ScaleY < 0);
                if (flipType != System.Drawing.RotateFlipType.RotateNoneFlipNone)
                {
                    cropped.RotateFlip(flipType);
                }

                // 4. Save to tracked temporary file
                var tempPath = Path.Combine(Path.GetTempPath(), $"nexus_crop_{Guid.NewGuid():N}.png");
                cropped.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);

                // 5. Update editor: if HasResult, update active result and matching before crop; else reload as derivative
                if (vm.HasResult)
                {
                    string? beforeCroppedPath = null;
                    var beforeSourcePath = !string.IsNullOrWhiteSpace(vm.CurrentBeforeCropPath) && File.Exists(vm.CurrentBeforeCropPath)
                        ? vm.CurrentBeforeCropPath
                        : (!string.IsNullOrWhiteSpace(vm.CropDerivativePath) && File.Exists(vm.CropDerivativePath) ? vm.CropDerivativePath : vm.InputImagePath);

                    if (!string.IsNullOrWhiteSpace(beforeSourcePath) && File.Exists(beforeSourcePath))
                    {
                        try
                        {
                            using var beforeStream = new FileStream(beforeSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            using var beforeRawBmp = new System.Drawing.Bitmap(beforeStream);

                            int bPxX = Math.Max(0, Math.Min(beforeRawBmp.Width - 1, pxX));
                            int bPxY = Math.Max(0, Math.Min(beforeRawBmp.Height - 1, pxY));
                            int bPxW = Math.Max(1, Math.Min(beforeRawBmp.Width - bPxX, pxW));
                            int bPxH = Math.Max(1, Math.Min(beforeRawBmp.Height - bPxY, pxH));

                            using var beforeCropped = beforeRawBmp.Clone(new System.Drawing.Rectangle(bPxX, bPxY, bPxW, bPxH), beforeRawBmp.PixelFormat);
                            if (flipType != System.Drawing.RotateFlipType.RotateNoneFlipNone)
                            {
                                beforeCropped.RotateFlip(flipType);
                            }
                            beforeCroppedPath = Path.Combine(Path.GetTempPath(), $"nexus_crop_before_{Guid.NewGuid():N}.png");
                            beforeCropped.Save(beforeCroppedPath, System.Drawing.Imaging.ImageFormat.Png);
                        }
                        catch (Exception exBefore)
                        {
                            Trace.WriteLine($"[ImageSubsystemView] Failed to slice before crop: {exBefore.Message}");
                        }
                    }

                    vm.ApplyCropToActiveResult(tempPath, cropped.Width, cropped.Height, beforeCroppedPath);
                }
                else
                {
                    vm.ApplyCropToInput(tempPath, cropped.Width, cropped.Height);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ImageSubsystemView] BtnApplyCrop_Click error: {ex.Message}");
            }
        }

        private static System.Drawing.RotateFlipType GetRotateFlipType(double rotationAngle, bool flipX, bool flipY)
            => ImageSubsystemViewModel.GetRotateFlipType(rotationAngle, flipX, flipY);
        #endregion

        #region OCR Word Selection Interaction
        private bool _isOcrSelecting;
        private OneOcrWordViewModel? _ocrDragStartWord;

        private void OcrOverlayCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (DataContext is not ImageSubsystemViewModel vm || !vm.HasOcrWords) return;

            Point pt = e.GetPosition(OcrOverlayCanvas);
            float px = (float)pt.X;
            float py = (float)pt.Y;

            OneOcrWordViewModel? hitWord = null;
            for (int i = 0; i < vm.OcrWords.Count; i++)
            {
                var w = vm.OcrWords[i];
                if (w.Quad.ContainsPoint(px, py))
                {
                    hitWord = w;
                    break;
                }
            }

            if (_isOcrSelecting && _ocrDragStartWord != null)
            {
                if (hitWord != null)
                {
                    vm.SelectWordRange(_ocrDragStartWord.GlobalIndex, hitWord.GlobalIndex);
                }
                OcrOverlayCanvas.Cursor = Cursors.IBeam;
                return;
            }

            if (hitWord != null)
            {
                foreach (var w in vm.OcrWords)
                {
                    w.IsHovered = (w == hitWord);
                }
                OcrOverlayCanvas.Cursor = Cursors.IBeam;
            }
            else
            {
                foreach (var w in vm.OcrWords)
                {
                    if (w.IsHovered) w.IsHovered = false;
                }
                OcrOverlayCanvas.Cursor = Cursors.Arrow;
            }
        }

        private void OcrOverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not ImageSubsystemViewModel vm || !vm.HasOcrWords) return;

            OcrOverlayCanvas.Focus();
            Point pt = e.GetPosition(OcrOverlayCanvas);
            float px = (float)pt.X;
            float py = (float)pt.Y;

            OneOcrWordViewModel? hitWord = null;
            for (int i = 0; i < vm.OcrWords.Count; i++)
            {
                var w = vm.OcrWords[i];
                if (w.Quad.ContainsPoint(px, py))
                {
                    hitWord = w;
                    break;
                }
            }

            if (hitWord != null)
            {
                _isOcrSelecting = true;
                _ocrDragStartWord = hitWord;
                OcrOverlayCanvas.CaptureMouse();

                bool isMulti = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl) ||
                               Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

                if (e.ClickCount >= 2)
                {
                    vm.SelectWord(hitWord, multiSelect: isMulti);
                }
                else
                {
                    if (isMulti)
                    {
                        hitWord.IsSelected = !hitWord.IsSelected;
                    }
                    else
                    {
                        vm.SelectWord(hitWord, multiSelect: false);
                    }
                }
                e.Handled = true;
            }
            else
            {
                vm.ClearWordSelection();
            }
        }

        private void OcrOverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isOcrSelecting)
            {
                _isOcrSelecting = false;
                _ocrDragStartWord = null;
                OcrOverlayCanvas.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void OcrOverlayCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            if (DataContext is not ImageSubsystemViewModel vm) return;
            foreach (var w in vm.OcrWords)
            {
                if (w.IsHovered) w.IsHovered = false;
            }
            OcrOverlayCanvas.Cursor = Cursors.Arrow;
        }

        private void OcrOverlayCanvas_KeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not ImageSubsystemViewModel vm || !vm.HasOcrWords) return;

            if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                vm.SelectAllWords();
                e.Handled = true;
            }
            else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                vm.CopySelectedOcrTextCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                vm.CloseOcrCommand.Execute(null);
                e.Handled = true;
            }
        }
        #endregion
    }
}
