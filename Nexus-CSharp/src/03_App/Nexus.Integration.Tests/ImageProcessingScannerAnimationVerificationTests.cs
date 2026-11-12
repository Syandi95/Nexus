using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Nexus.App.Controls;
using Xunit;

namespace Nexus.Integration.Tests;

[Collection(WpfApplicationCollection.Name)]
public class ImageProcessingScannerAnimationVerificationTests
{
    private static readonly string EvidenceDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".gemini", "antigravity", "brain", "af1a4d3b-1322-484e-96c7-a7fee4e54bc6", "animation_evidence");

    private static void EnsureAppResources()
    {
        if (Application.Current == null) return;
        if (!Application.Current.Resources.Contains("AccentPrimaryBrush"))
        {
            Application.Current.Resources["AccentPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x4F, 0x46, 0xE5)); // Indigo/Blue Accent
        }
    }

    private static ImageProcessingScanner CreateLoadedScanner(ImageScannerMode mode, double width = 800, double height = 600)
    {
        EnsureAppResources();
        var scanner = new ImageProcessingScanner
        {
            ScannerMode = mode,
            Width = width,
            Height = height
        };

        scanner.Measure(new Size(width, height));
        scanner.Arrange(new Rect(0, 0, width, height));
        scanner.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        scanner.UpdateLayout();
        return scanner;
    }

    private static Grid CreateTestStageWithBackground(ImageProcessingScanner scanner, Brush backgroundBrush, double stageWidth = 800, double stageHeight = 600, double imgWidth = 800, double imgHeight = 600)
    {
        var stage = new Grid
        {
            Width = stageWidth,
            Height = stageHeight,
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)) // Neutral canvas background
        };

        var artwork = new Border
        {
            Width = imgWidth,
            Height = imgHeight,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = backgroundBrush
        };
        stage.Children.Add(artwork);

        scanner.HorizontalAlignment = HorizontalAlignment.Center;
        scanner.VerticalAlignment = VerticalAlignment.Center;
        stage.Children.Add(scanner);

        stage.Measure(new Size(stageWidth, stageHeight));
        stage.Arrange(new Rect(0, 0, stageWidth, stageHeight));
        stage.UpdateLayout();
        return stage;
    }

    private static Grid CreateTestStageWithArtwork(ImageProcessingScanner scanner, double stageWidth = 800, double stageHeight = 600, double imgWidth = 800, double imgHeight = 600)
    {
        var gradient = new LinearGradientBrush(
            Color.FromRgb(0x3B, 0x82, 0xF6), // Blue
            Color.FromRgb(0xEC, 0x48, 0x99), // Pink
            45.0);
        return CreateTestStageWithBackground(scanner, gradient, stageWidth, stageHeight, imgWidth, imgHeight);
    }

    private static RenderTargetBitmap SaveRenderToPng(FrameworkElement element, string filename, int width = 800, int height = 600, string subFolder = "")
    {
        var targetDir = string.IsNullOrEmpty(subFolder) ? EvidenceDir : Path.Combine(EvidenceDir, subFolder);
        Directory.CreateDirectory(targetDir);
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        var outPath = Path.Combine(targetDir, filename);
        using var fs = File.Create(outPath);
        encoder.Save(fs);
        return rtb;
    }

    private static (byte B, byte G, byte R, byte A) GetPixel(RenderTargetBitmap rtb, int x, int y)
    {
        byte[] pixels = new byte[4];
        rtb.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        return (pixels[0], pixels[1], pixels[2], pixels[3]);
    }

    private static void SetScannerVisualState(
        ImageProcessingScanner scanner,
        bool isVertical,
        double position,
        double bandOpacity,
        bool isVisible = true)
    {
        scanner.ActiveStoryboard?.Stop(scanner);
        scanner.RootGridElement.BeginAnimation(UIElement.OpacityProperty, null);
        scanner.VerticalBandElement.BeginAnimation(UIElement.OpacityProperty, null);
        scanner.HorizontalBandElement.BeginAnimation(UIElement.OpacityProperty, null);
        scanner.VerticalTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
        scanner.HorizontalTranslateTransform.BeginAnimation(TranslateTransform.XProperty, null);

        if (!isVisible)
        {
            scanner.Visibility = Visibility.Collapsed;
            scanner.Opacity = 0.0;
            scanner.RootGridElement.Opacity = 0.0;
            return;
        }

        scanner.Visibility = Visibility.Visible;
        scanner.Opacity = 1.0;
        scanner.RootGridElement.Opacity = 1.0;

        if (isVertical)
        {
            scanner.VerticalBandElement.Visibility = Visibility.Visible;
            scanner.HorizontalBandElement.Visibility = Visibility.Collapsed;
            scanner.VerticalTranslateTransform.Y = position;
            scanner.VerticalBandElement.Opacity = bandOpacity;
        }
        else
        {
            scanner.HorizontalBandElement.Visibility = Visibility.Visible;
            scanner.VerticalBandElement.Visibility = Visibility.Collapsed;
            scanner.HorizontalTranslateTransform.X = position;
            scanner.HorizontalBandElement.Opacity = bandOpacity;
        }
    }

    // =========================================================================
    // 1. REVISION 6.1: REMOVE BACKGROUND (52px FIELD, 4.40s FOREVER LOOP, REPEAT)
    // =========================================================================

    [Fact]
    public void Verification1_RemoveBackground_Phases_TopToBottom_ForeverLoop()
    {
        WpfAppHost.Run(() =>
        {
            var scanner = CreateLoadedScanner(ImageScannerMode.RemoveBackground, 800, 600);
            var stage = CreateTestStageWithArtwork(scanner, 800, 600, 800, 600);

            // 1. Initial idle state: Collapsed, 0 opacity
            Assert.Equal(Visibility.Collapsed, scanner.Visibility);
            Assert.Equal(0.0, scanner.Opacity);

            // 2. Start scan
            scanner.IsActive = true;
            Assert.Equal(Visibility.Visible, scanner.Visibility);
            Assert.Equal(1.0, scanner.Opacity);
            Assert.NotNull(scanner.ActiveStoryboard);

            var sb = scanner.ActiveStoryboard!;
            // REVISION 6.1: Must repeat forever while IsActive is true
            Assert.Equal(RepeatBehavior.Forever, sb.RepeatBehavior);

            // Extract the two keyframe animations (translation and opacity)
            var keyframeAnims = sb.Children.OfType<DoubleAnimationUsingKeyFrames>().ToList();
            Assert.Equal(2, keyframeAnims.Count);

            var translateAnim = keyframeAnims[0];
            var opacityAnim = keyframeAnims[1];

            // Inspect Translation KeyFrames (Cycle = 4.40s, bandThickness = 52px)
            var frames = translateAnim.KeyFrames.Cast<DoubleKeyFrame>().ToList();
            double maxTravel = 600 - 52; // 548px travel

            // Keyframe 0 (t=0s): Origin
            Assert.Equal(0.0, frames[0].Value);

            // Keyframe 1 (t=3.60s): Endpoint Sweep
            Assert.Equal(maxTravel, frames[1].Value);
            Assert.IsType<EasingDoubleKeyFrame>(frames[1]);
            var easing = ((EasingDoubleKeyFrame)frames[1]).EasingFunction as QuadraticEase;
            Assert.NotNull(easing);
            Assert.Equal(EasingMode.EaseInOut, easing.EasingMode);

            // Keyframe 2 (t=3.75s): Hold at endpoint during 150ms fade-out
            Assert.Equal(maxTravel, frames[2].Value);

            // Keyframe 3 (t=3.76s): Discrete snap back to origin (0.0) while invisible
            Assert.Equal(0.0, frames[3].Value);
            Assert.IsType<DiscreteDoubleKeyFrame>(frames[3]);

            // Keyframe 4 (t=4.40s): Rest period ends at origin, ready for next seamless loop
            Assert.Equal(0.0, frames[4].Value);

            // Verify Opacity KeyFrames:
            var opFrames = opacityAnim.KeyFrames.Cast<DoubleKeyFrame>().ToList();
            // t=0.00s: visible (1.0) - sweeps immediately without per-cycle fade-in
            Assert.Equal(1.0, opFrames.First(f => f.KeyTime.TimeSpan == TimeSpan.Zero).Value);
            // t=3.60s: visible (1.0) throughout sweep
            Assert.Equal(1.0, opFrames.First(f => f.KeyTime.TimeSpan == TimeSpan.FromSeconds(3.60)).Value);
            // t=3.75s: faded out at bottom (0.0)
            Assert.Equal(0.0, opFrames.First(f => f.KeyTime.TimeSpan == TimeSpan.FromSeconds(3.75)).Value);
            // t=4.40s: invisible (0.0) throughout rest and invisible snap-back
            Assert.Equal(0.0, opFrames.First(f => f.KeyTime.TimeSpan == TimeSpan.FromSeconds(4.40)).Value);

            // Save key cycle snapshots
            SetScannerVisualState(scanner, isVertical: true, position: maxTravel * 0.45, bandOpacity: 1.0, isVisible: true);
            SaveRenderToPng(stage, "remove_bg_sweep_midpoint.png");

            SetScannerVisualState(scanner, isVertical: true, position: 0, bandOpacity: 0.0, isVisible: true);
            SaveRenderToPng(stage, "remove_bg_rest_invisible.png");
        });
    }

    // =========================================================================
    // 2. AI UPSCALE: HORIZONTAL SWEEP (LEFT -> RIGHT) WITH 52px FIELD
    // =========================================================================

    [Fact]
    public void Verification2_AiUpscale_HorizontalSweep_LeftToRight_52pxField()
    {
        WpfAppHost.Run(() =>
        {
            var scanner = CreateLoadedScanner(ImageScannerMode.Upscale, 800, 600);
            var stage = CreateTestStageWithArtwork(scanner, 800, 600, 800, 600);

            scanner.IsActive = true;
            Assert.NotNull(scanner.ActiveStoryboard);
            Assert.Equal(Visibility.Visible, scanner.HorizontalBandElement.Visibility);
            Assert.Equal(Visibility.Collapsed, scanner.VerticalBandElement.Visibility);

            var sb = scanner.ActiveStoryboard!;
            Assert.Equal(RepeatBehavior.Forever, sb.RepeatBehavior);

            var keyframeAnims = sb.Children.OfType<DoubleAnimationUsingKeyFrames>().ToList();
            var translateAnim = keyframeAnims[0];
            var frames = translateAnim.KeyFrames.Cast<DoubleKeyFrame>().ToList();

            // Verify Horizontal travel reaches (ActualWidth - 52)
            double maxTravel = 800 - 52; // 748
            Assert.Equal(maxTravel, frames[1].Value); // Sweep endpoint at t=3.60s

            SetScannerVisualState(scanner, isVertical: false, position: maxTravel * 0.50, bandOpacity: 1.0, isVisible: true);
            SaveRenderToPng(stage, "upscale_sweep_midpoint.png");
        });
    }

    // =========================================================================
    // 3. OBJECT REMOVAL: UNIFIED 52px HORIZONTAL SWEEP & MASK UNTOUCHED
    // =========================================================================

    [Fact]
    public void Verification3_ObjectRemoval_BoundsIntegrity_ArtworkUntouched()
    {
        WpfAppHost.Run(() =>
        {
            var scanner = CreateLoadedScanner(ImageScannerMode.ObjectRemoval, 640, 480);
            var stage = CreateTestStageWithArtwork(scanner, 640, 480, 640, 480);

            Assert.False(scanner.IsHitTestVisible);

            scanner.IsActive = true;
            var sb = scanner.ActiveStoryboard!;
            var keyframeAnims = sb.Children.OfType<DoubleAnimationUsingKeyFrames>().ToList();
            var translateAnim = keyframeAnims[0];
            var frames = translateAnim.KeyFrames.Cast<DoubleKeyFrame>().ToList();

            double maxAllowed = 640 - 52; // 588
            Assert.Equal(maxAllowed, frames[1].Value);
            Assert.True(frames[1].Value + 52 <= 640, "Scanner band must never exceed right image boundary");

            SetScannerVisualState(scanner, isVertical: false, position: maxAllowed * 0.50, bandOpacity: 1.0, isVisible: true);
            SaveRenderToPng(stage, "object_removal_sweep_midpoint.png", 640, 480);
        });
    }

    // =========================================================================
    // 4. AI CHECK: STRICTLY CONSTRAINED TO dispRect
    // =========================================================================

    [Fact]
    public void Verification4_AiCheck_ConstrainedToDispRect_NoLetterboxSweep()
    {
        WpfAppHost.Run(() =>
        {
            double containerWidth = 1000;
            double containerHeight = 800;
            double dispWidth = 400;
            double dispHeight = 600;

            var scanner = CreateLoadedScanner(ImageScannerMode.AiCheck, dispWidth, dispHeight);
            var stage = CreateTestStageWithArtwork(scanner, containerWidth, containerHeight, dispWidth, dispHeight);

            Assert.Equal(dispWidth, scanner.Width);
            Assert.Equal(dispHeight, scanner.Height);

            scanner.IsActive = true;
            var sb = scanner.ActiveStoryboard!;
            var keyframeAnims = sb.Children.OfType<DoubleAnimationUsingKeyFrames>().ToList();
            var translateAnim = keyframeAnims[0];
            var frames = translateAnim.KeyFrames.Cast<DoubleKeyFrame>().ToList();

            double maxTravel = dispHeight - 52; // 548
            Assert.Equal(maxTravel, frames[1].Value);

            SetScannerVisualState(scanner, isVertical: true, position: maxTravel * 0.40, bandOpacity: 1.0, isVisible: true);
            SaveRenderToPng(stage, "aicheck_constrained_disprect.png", (int)containerWidth, (int)containerHeight);
        });
    }

    // =========================================================================
    // 5. CANCELLATION / EARLY COMPLETION IN VARIOUS PHASES
    // =========================================================================

    [Theory]
    [InlineData(1.00)] // Mid Sweep
    [InlineData(2.50)] // Later Mid Sweep
    [InlineData(3.70)] // Endpoint Fade
    [InlineData(4.00)] // Rest Phase
    public void Verification5_Cancellation_ImmediateExitFade_ToCollapsed(double seekSeconds)
    {
        WpfAppHost.Run(() =>
        {
            var scanner = CreateLoadedScanner(ImageScannerMode.RemoveBackground, 800, 600);
            scanner.IsActive = true;
            Assert.NotNull(scanner.ActiveStoryboard);
            var sb = scanner.ActiveStoryboard!;
            sb.Seek(scanner, TimeSpan.FromSeconds(seekSeconds), TimeSeekOrigin.BeginTime);

            scanner.IsActive = false;
            scanner.StopAndCollapseImmediate();

            Assert.Equal(Visibility.Collapsed, scanner.Visibility);
            Assert.Equal(0.0, scanner.Opacity);
            Assert.Equal(0.0, scanner.RootGridElement.Opacity);
            Assert.Equal(0.0, scanner.VerticalBandElement.Opacity);
            Assert.Equal(0.0, scanner.HorizontalBandElement.Opacity);
            Assert.Null(scanner.ActiveStoryboard);
        });
    }

    // =========================================================================
    // 6. ZOOM / PAN GEOMETRY SYNCHRONIZATION
    // =========================================================================

    [Fact]
    public void Verification6_ZoomAndPan_GeometrySynchronized()
    {
        WpfAppHost.Run(() =>
        {
            double dispWidth = 500;
            double dispHeight = 400;

            var scanner = CreateLoadedScanner(ImageScannerMode.RemoveBackground, dispWidth, dispHeight);

            var transformStage = new Grid
            {
                Width = dispWidth,
                Height = dispHeight
            };
            transformStage.Children.Add(scanner);

            var group200 = new TransformGroup();
            group200.Children.Add(new ScaleTransform(2.0, 2.0));
            group200.Children.Add(new TranslateTransform(60, -40));
            transformStage.RenderTransform = group200;
            transformStage.Measure(new Size(dispWidth, dispHeight));
            transformStage.Arrange(new Rect(0, 0, dispWidth, dispHeight));
            transformStage.UpdateLayout();

            var bounds = scanner.TransformToAncestor(transformStage).TransformBounds(new Rect(0, 0, dispWidth, dispHeight));
            Assert.Equal(0, bounds.Left);
            Assert.Equal(0, bounds.Top);
            Assert.Equal(dispWidth, bounds.Width);
            Assert.Equal(dispHeight, bounds.Height);
        });
    }

    // =========================================================================
    // 7. VISUAL QUALITY: 52px FIELD, 38px ACCENT TINT, 2px BEAM (0.40), 1px CORE (0.20)
    // =========================================================================

    [Fact]
    public void Verification7_VisualQuality_MeasurementsAndBrushes()
    {
        WpfAppHost.Run(() =>
        {
            var scanner = CreateLoadedScanner(ImageScannerMode.RemoveBackground, 800, 600);

            Assert.Null(scanner.RootGridElement.Background);

            var vGrid = scanner.VerticalBandElement as Grid;
            Assert.NotNull(vGrid);
            Assert.Equal(52, scanner.VerticalBandElement.Height);

            var rects = vGrid.Children.OfType<System.Windows.Shapes.Rectangle>().ToList();
            Assert.Equal(4, rects.Count);

            // Layer 1: Soft Luminance Field (52px height, diffuse white gradient, 5.5% peak)
            var focusBand = rects[0];
            Assert.Equal(52, focusBand.Height);
            var focusBrush = focusBand.Fill as LinearGradientBrush;
            Assert.NotNull(focusBrush);
            var peakStop = focusBrush.GradientStops.First(s => s.Offset == 0.5);
            // #0EFFFFFF -> Alpha = 14 -> 14/255 = ~5.49% white luminance
            Assert.Equal(14, peakStop.Color.A);
            Assert.Equal(255, peakStop.Color.R);
            Assert.Equal(255, peakStop.Color.G);
            Assert.Equal(255, peakStop.Color.B);

            // Layer 2: Minimal Accent Tint (38px height, Opacity 0.12, Mask peak 0.35)
            var glow = rects[1];
            Assert.Equal(38, glow.Height);
            Assert.Equal(0.12, glow.Opacity);
            Assert.NotNull(glow.OpacityMask);

            // Layer 3: Thin Optical Beam (2px line, Opacity 0.40)
            var mainBeam = rects[2];
            Assert.Equal(2, mainBeam.Height);
            Assert.Equal(0.40, mainBeam.Opacity);

            // Layer 4: Specular Center Core (1px line, Opacity 0.20)
            var highlight = rects[3];
            Assert.Equal(1, highlight.Height);
            Assert.Equal(0.20, highlight.Opacity);
            var whiteBrush = highlight.Fill as SolidColorBrush;
            Assert.NotNull(whiteBrush);
            Assert.Equal(Colors.White, whiteBrush.Color);
        });
    }

    // =========================================================================
    // 8. MULTI-IMAGE VISUAL MATRIX: 5 BACKGROUNDS x 4 PHASES RUNTIME EVALUATION
    // =========================================================================

    [Fact]
    public void Verification8_MultiImageVisualMatrix_FiveBackgrounds()
    {
        WpfAppHost.Run(() =>
        {
            // The calibrated blue-channel/luminance assertions below are for the
            // Dark palette; do not inherit a prior test's Light selection.
            Nexus.App.Services.ThemeManager.ApplyTheme(Nexus.App.Services.ThemeMode.Dark);
            var scenarios = new (string Name, Brush Brush)[]
            {
                ("A_White", new SolidColorBrush(Colors.White)),
                ("B_Black", new SolidColorBrush(Colors.Black)),
                ("C_MidGray", new SolidColorBrush(Color.FromRgb(128, 128, 128))),
                ("D_Saturated", new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(239, 68, 68), 0.0),   // Red
                        new GradientStop(Color.FromRgb(234, 179, 8), 0.33),  // Yellow
                        new GradientStop(Color.FromRgb(34, 197, 94), 0.66),  // Green
                        new GradientStop(Color.FromRgb(59, 130, 246), 1.0)   // Blue
                    },
                    new Point(0, 0), new Point(1, 1))),
                ("E_RealBanner", new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(15, 23, 42), 0.0),   // Slate 900
                        new GradientStop(Color.FromRgb(30, 27, 75), 0.5),   // Indigo 950
                        new GradientStop(Color.FromRgb(49, 46, 129), 1.0)   // Indigo 900
                    },
                    new Point(0.5, 0), new Point(0.5, 1)))
            };

            foreach (var (name, brush) in scenarios)
            {
                var scanner = CreateLoadedScanner(ImageScannerMode.RemoveBackground, 800, 600);
                var stage = CreateTestStageWithBackground(scanner, brush, 800, 600, 800, 600);
                double maxTravel = 600 - 52; // 548

                // Phase 1: Normal (Before scanner)
                SetScannerVisualState(scanner, isVertical: true, position: 0, bandOpacity: 0.0, isVisible: false);
                var rtb1 = SaveRenderToPng(stage, $"{name}_1_Normal.png", 800, 600, "matrix");

                // Phase 2: Sweep Midpoint (Beam active at Y = maxTravel * 0.45)
                SetScannerVisualState(scanner, isVertical: true, position: maxTravel * 0.45, bandOpacity: 1.0, isVisible: true);
                var rtb2 = SaveRenderToPng(stage, $"{name}_2_SweepMidpoint.png", 800, 600, "matrix");

                // Phase 3: Endpoint Fade (Beam fading at bottom Y = maxTravel)
                SetScannerVisualState(scanner, isVertical: true, position: maxTravel, bandOpacity: 0.5, isVisible: true);
                var rtb3 = SaveRenderToPng(stage, $"{name}_3_EndpointFade.png", 800, 600, "matrix");

                // Phase 4: Rest Invisible (Band invisible at origin)
                SetScannerVisualState(scanner, isVertical: true, position: 0, bandOpacity: 0.0, isVisible: true);
                var rtb4 = SaveRenderToPng(stage, $"{name}_4_RestInvisible.png", 800, 600, "matrix");

                int centerY = (int)(maxTravel * 0.45 + 26);

                // Pixel verification: Rest (Phase 4) must return EXACTLY to Normal (Phase 1)
                var p1 = GetPixel(rtb1, 400, centerY);
                var p2 = GetPixel(rtb2, 400, centerY);
                var p4 = GetPixel(rtb4, 400, centerY);

                Assert.Equal(p1.R, p4.R);
                Assert.Equal(p1.G, p4.G);
                Assert.Equal(p1.B, p4.B);

                // On Black background: Midpoint must show luminance lift
                if (name == "B_Black")
                {
                    Assert.Equal(0, p1.R);
                    Assert.Equal(0, p1.G);
                    Assert.Equal(0, p1.B);

                    Assert.True(p2.R > 0 || p2.G > 0 || p2.B > 0,
                        $"Expected luminance lift at midpoint on black background, got R={p2.R}, G={p2.G}, B={p2.B}");
                }
                // On White background: Midpoint must remain light and clean
                else if (name == "A_White")
                {
                    Assert.Equal(255, p1.R);
                    Assert.Equal(255, p1.G);
                    Assert.Equal(255, p1.B);

                    double luminance = 0.299 * p2.R + 0.587 * p2.G + 0.114 * p2.B;
                    Assert.True(luminance >= 200, $"Expected bright luminance on white background, got {luminance}");
                    Assert.Equal(255, p2.B);
                    Assert.True(p2.R != p1.R || p2.G != p1.G, "Beam accent tint must be perceptible against pure white");
                }
            }
        });
    }
}
