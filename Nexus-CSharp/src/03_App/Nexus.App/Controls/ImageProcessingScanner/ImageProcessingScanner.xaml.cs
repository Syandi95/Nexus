using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nexus.App.Controls;

public partial class ImageProcessingScanner : UserControl
{
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(
            nameof(IsActive),
            typeof(bool),
            typeof(ImageProcessingScanner),
            new PropertyMetadata(false, OnIsActiveChanged));

    public static readonly DependencyProperty ScannerModeProperty =
        DependencyProperty.Register(
            nameof(ScannerMode),
            typeof(ImageScannerMode),
            typeof(ImageProcessingScanner),
            new PropertyMetadata(ImageScannerMode.RemoveBackground, OnScannerModeChanged));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public ImageScannerMode ScannerMode
    {
        get => (ImageScannerMode)GetValue(ScannerModeProperty);
        set => SetValue(ScannerModeProperty, value);
    }

    /// <summary>
    /// Architectural hook for future system-level Reduced Motion settings.
    /// When true, animated sweeps are bypassed and scanner enters Static Hold immediately.
    /// </summary>
    public bool IsReducedMotion { get; set; } = false;

    private Storyboard? _activeStoryboard;
    private bool _isLoaded;

    public ImageProcessingScanner()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        if (IsActive)
        {
            StartScannerPhases();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        StopAndCollapseImmediate();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double w = e.NewSize.Width > 0 ? e.NewSize.Width : ActualWidth;
        double h = e.NewSize.Height > 0 ? e.NewSize.Height : ActualHeight;

        // If dimensions were previously zero and scanner became active before initial layout, start now
        if (IsActive && _activeStoryboard == null && w > 0 && h > 0)
        {
            StartScannerPhases();
        }
    }

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ImageProcessingScanner scanner)
        {
            if ((bool)e.NewValue)
            {
                scanner.StartScannerPhases();
            }
            else
            {
                scanner.StopScannerPhasesWithFade();
            }
        }
    }

    private static void OnScannerModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ImageProcessingScanner scanner && scanner.IsActive)
        {
            // Restart with the newly selected mode orientation
            scanner.StartScannerPhases();
        }
    }

    internal Storyboard? ActiveStoryboard => _activeStoryboard;
    internal FrameworkElement VerticalBandElement => VerticalBand;
    internal FrameworkElement HorizontalBandElement => HorizontalBand;
    internal TranslateTransform VerticalTranslateTransform => VerticalTranslate;
    internal TranslateTransform HorizontalTranslateTransform => HorizontalTranslate;
    internal Grid RootGridElement => RootGrid;

    internal void StartScannerPhases()
    {
        double effectiveWidth = ActualWidth > 0 ? ActualWidth : (double.IsNaN(Width) ? 0 : Width);
        double effectiveHeight = ActualHeight > 0 ? ActualHeight : (double.IsNaN(Height) ? 0 : Height);

        if (!_isLoaded || effectiveWidth <= 0 || effectiveHeight <= 0)
        {
            // Strictly wait for valid dispRect geometry; do not show with wrong dimensions
            Visibility = Visibility.Collapsed;
            Opacity = 0.0;
            return;
        }

        // Clean up any previously running storyboard
        if (_activeStoryboard != null)
        {
            try { _activeStoryboard.Stop(this); } catch { }
            _activeStoryboard = null;
        }
        RootGrid.BeginAnimation(OpacityProperty, null);

        Visibility = Visibility.Visible;
        Opacity = 1.0;

        // 1. One-shot root enter fade (180ms)
        var enterAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        RootGrid.BeginAnimation(OpacityProperty, enterAnim);

        bool isVertical = ScannerMode == ImageScannerMode.RemoveBackground ||
                          ScannerMode == ImageScannerMode.AiCheck;

        if (isVertical)
        {
            VerticalBand.Visibility = Visibility.Visible;
            HorizontalBand.Visibility = Visibility.Collapsed;
            HorizontalBand.Opacity = 0.0;
            VerticalTranslate.Y = 0;
        }
        else
        {
            HorizontalBand.Visibility = Visibility.Visible;
            VerticalBand.Visibility = Visibility.Collapsed;
            VerticalBand.Opacity = 0.0;
            HorizontalTranslate.X = 0;
        }

        // If reduced motion is active: remain in Static Hold (visual off)
        if (IsReducedMotion)
        {
            VerticalBand.Opacity = 0.0;
            HorizontalBand.Opacity = 0.0;
            return;
        }

        const double bandThickness = 52.0;
        double maxTravel = isVertical
            ? Math.Max(1.0, effectiveHeight - bandThickness)
            : Math.Max(1.0, effectiveWidth - bandThickness);

        var easeInOut = new QuadraticEase { EasingMode = EasingMode.EaseInOut };

        // 2. Repeating Sweep Storyboard (RepeatBehavior.Forever)
        var storyboard = new Storyboard
        {
            RepeatBehavior = RepeatBehavior.Forever
        };

        FrameworkElement targetBand = isVertical ? VerticalBand : HorizontalBand;

        // Translation keyframes (Total cycle = 4.40s):
        // t = 0.00s: start at origin
        // t = 3.60s: sweep to maxTravel with QuadraticEase (EaseInOut)
        // t = 3.75s: hold at endpoint while fading out
        // t = 3.76s: discrete reset to 0 while invisible
        // t = 4.40s: rest period ends at origin
        var translateAnimation = new DoubleAnimationUsingKeyFrames();
        translateAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        translateAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(maxTravel, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.60)), easeInOut));
        translateAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(maxTravel, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.75))));
        translateAnimation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.76))));
        translateAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.40))));

        Storyboard.SetTarget(translateAnimation, targetBand);
        Storyboard.SetTargetProperty(translateAnimation, new PropertyPath(isVertical ? "RenderTransform.(TranslateTransform.Y)" : "RenderTransform.(TranslateTransform.X)"));
        storyboard.Children.Add(translateAnimation);

        // Band Opacity keyframes:
        // t = 0.00s: visible (1.0) - sweeps immediately from origin without per-cycle fade-in
        // t = 3.60s: visible (1.0)
        // t = 3.75s: fade out at bottom/right edge (150ms)
        // t = 4.40s: invisible (0.0) during rest and reset
        var opacityAnimation = new DoubleAnimationUsingKeyFrames();
        opacityAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacityAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.60))));
        opacityAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.75))));
        opacityAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.40))));

        Storyboard.SetTarget(opacityAnimation, targetBand);
        Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(opacityAnimation);

        _activeStoryboard = storyboard;
        storyboard.Begin(this, isControllable: true);
    }

    internal void StopScannerPhasesWithFade()
    {
        if (Visibility != Visibility.Visible)
        {
            StopAndCollapseImmediate();
            return;
        }

        // Cancelable immediate exit: fade RootGrid out over 120ms
        var exitAnim = new DoubleAnimation(RootGrid.Opacity, 0.0, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        exitAnim.Completed += (_, _) =>
        {
            if (!IsActive)
            {
                StopAndCollapseImmediate();
            }
        };
        RootGrid.BeginAnimation(OpacityProperty, exitAnim);
    }

    internal void StopAndCollapseImmediate()
    {
        if (_activeStoryboard != null)
        {
            try { _activeStoryboard.Stop(this); } catch { }
            _activeStoryboard = null;
        }
        RootGrid.BeginAnimation(OpacityProperty, null);
        RootGrid.Opacity = 0.0;
        VerticalBand.Opacity = 0.0;
        HorizontalBand.Opacity = 0.0;
        VerticalTranslate.Y = 0;
        HorizontalTranslate.X = 0;
        Visibility = Visibility.Collapsed;
        Opacity = 0.0;
    }
}
