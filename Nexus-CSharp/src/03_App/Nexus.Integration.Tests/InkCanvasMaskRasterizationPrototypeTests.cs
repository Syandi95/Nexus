using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace Nexus.Integration.Tests;

public sealed class InkCanvasMaskRasterizationPrototypeTests
{
    [Fact]
    public void Point1_Rasterization_ProducesPureWhiteOnPureBlack_ImmuneToDisplayColorAndAlpha()
    {
        WpfAppHost.Run(() =>
        {
            // 1. Simulasi tampilan di UI: Kuas berwarna merah semi-transparan (Alpha 120)
            var displayBrushColor = Color.FromArgb(120, 255, 0, 0); // Merah tembus pandang
            var canvas = new InkCanvas
            {
                Width = 400,
                Height = 300,
                DefaultDrawingAttributes = new DrawingAttributes
                {
                    Color = displayBrushColor,
                    Width = 40,
                    Height = 40,
                    IsHighlighter = false
                }
            };

            // Tambahkan satu goresan horizontal di tengah kanvas (Y = 150)
            var stylusPoints = new StylusPointCollection
            {
                new StylusPoint(100, 150),
                new StylusPoint(200, 150),
                new StylusPoint(300, 150)
            };
            var userStroke = new Stroke(stylusPoints, canvas.DefaultDrawingAttributes.Clone());
            canvas.Strokes.Add(userStroke);

            // 2. RASTERISASI ISOLASI: Render ulang ke DrawingVisual terpisah
            // TANPA mengambil screenshot tampilan visual kanvas
            int targetWidth = 400;
            int targetHeight = 300;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // Background hitam pekat absolut
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, targetWidth, targetHeight));

                // Setiap goresan digambar ulang dengan kuas SOLID WHITE (Colors.White),
                // mengabaikan sama sekali warna displayBrushColor
                foreach (var stroke in canvas.Strokes)
                {
                    var maskAttributes = stroke.DrawingAttributes.Clone();
                    maskAttributes.Color = Colors.White;
                    maskAttributes.IsHighlighter = false;

                    stroke.Draw(dc, maskAttributes);
                }
            }

            var rtb = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            // Ekstrak buffer piksel mentah
            int stride = targetWidth * 4;
            var pixels = new byte[stride * targetHeight];
            rtb.CopyPixels(pixels, stride, 0);

            // 3. VERIFIKASI MATEMATIS PIKSEL
            // Titik di luar goresan (X=50, Y=50) harus hitam pekat: R=0, G=0, B=0, A=255
            int idxOutside = (50 * stride) + (50 * 4);
            byte bOut = pixels[idxOutside];
            byte gOut = pixels[idxOutside + 1];
            byte rOut = pixels[idxOutside + 2];
            Assert.Equal(0, rOut);
            Assert.Equal(0, gOut);
            Assert.Equal(0, bOut);

            // Titik di pusat goresan (X=200, Y=150) harus putih solid: R=255, G=255, B=255
            int idxInside = (150 * stride) + (200 * 4);
            byte bIn = pixels[idxInside];
            byte gIn = pixels[idxInside + 1];
            byte rIn = pixels[idxInside + 2];
            Assert.Equal(255, rIn);
            Assert.Equal(255, gIn);
            Assert.Equal(255, bIn);

            // BUKTI MUTLAK: Tidak ada kontaminasi kanal merah (R == G == B di setiap piksel)
            // Seluruh gambar adalah grayscale murni (monokrom)
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                // Dalam monokrom hitam-putih murni antialiased, R, G, B selalu identik
                Assert.Equal(r, g);
                Assert.Equal(g, b);
            }
        });
    }

    [Fact]
    public void Point2_CoordinateTransformation_TransformsZoomedAndPannedStrokes_ToNativeImageResolution()
    {
        WpfAppHost.Run(() =>
        {
            // Kasus dunia nyata:
            // - Gambar asli beresolusi tinggi: 4000 x 3000 piksel
            // - Tampilan gambar di layar (Displayed Size): 800 x 600 piksel
            // - Faktor Zoom kanvas: 250% (Zoom = 2.5)
            // - Geser/Pan kanvas: PanX = 120 px, PanY = -80 px
            double nativeWidth = 4000;
            double nativeHeight = 3000;
            double displayedWidth = 800;
            double displayedHeight = 600;

            double zoom = 2.5;
            double panX = 120.0;
            double panY = -80.0;

            // Pengguna mencoret objek tertentu pada layar (Viewport Coordinate)
            // Misalkan kursor berada di koordinat Viewport (X = 450, Y = 300)
            Point viewportPoint = new Point(450, 300);

            // LANGKAH 1: Transformasi Terbalik Zoom & Pan (dari Viewport ke Ruang Stage Display)
            // StageTransform = Translate(panX, panY) * Scale(zoom, zoom)
            var stageTransform = new TransformGroup();
            stageTransform.Children.Add(new ScaleTransform(zoom, zoom));
            stageTransform.Children.Add(new TranslateTransform(panX, panY));
            stageTransform.Children.Add(new TranslateTransform(displayedWidth * (1.0 - zoom) * 0.5, displayedHeight * (1.0 - zoom) * 0.5)); // Origin 0.5, 0.5

            var inverseTransform = stageTransform.Inverse;
            Assert.NotNull(inverseTransform);

            Point displayStagePoint = inverseTransform.Transform(viewportPoint);

            // LANGKAH 2: Transformasi Penskalaan Display ke Resolusi Asli (Native Resolution)
            double scaleRatioX = nativeWidth / displayedWidth;   // 4000 / 800 = 5.0
            double scaleRatioY = nativeHeight / displayedHeight; // 3000 / 600 = 5.0

            Point nativePoint = new Point(
                displayStagePoint.X * scaleRatioX,
                displayStagePoint.Y * scaleRatioY);

            // Validasi: Koordinat native berada tepat dalam batas gambar asli [0..4000, 0..3000]
            Assert.InRange(nativePoint.X, 0, nativeWidth);
            Assert.InRange(nativePoint.Y, 0, nativeHeight);

            // LANGKAH 3: Penskalaan Ketebalan Kuas (Brush Size)
            // Jika kuas di layar 30 px, pada resolusi 4000x3000 kuas harus berskala 30 * 5.0 = 150 px
            double displayBrushThickness = 30.0;
            double nativeBrushThickness = displayBrushThickness * scaleRatioX;
            Assert.Equal(150.0, nativeBrushThickness);

            // LANGKAH 4: Menerapkan Matrix Transformasi langsung ke StrokeCollection
            var stroke = new Stroke(new StylusPointCollection { new StylusPoint(displayStagePoint.X, displayStagePoint.Y) })
            {
                DrawingAttributes = new DrawingAttributes
                {
                    Width = displayBrushThickness,
                    Height = displayBrushThickness
                }
            };

            var nativeMatrix = Matrix.Identity;
            nativeMatrix.Scale(scaleRatioX, scaleRatioY);
            stroke.Transform(nativeMatrix, false);
            stroke.DrawingAttributes.Width *= scaleRatioX;
            stroke.DrawingAttributes.Height *= scaleRatioY;

            // Titik goresan pada native space harus persis sama dengan perhitungan nativePoint
            Assert.Equal(nativePoint.X, stroke.StylusPoints[0].X, 3);
            Assert.Equal(nativePoint.Y, stroke.StylusPoints[0].Y, 3);
            Assert.Equal(nativeBrushThickness, stroke.DrawingAttributes.Width, 3);
        });
    }

    [Fact]
    public void Point3_ZoomAndPanActive_ContainerLevelTransform_PreservesExactNativeCoordinates()
    {
        WpfAppHost.Run(() =>
        {
            // Verifikasi Poin 2:
            // Buktikan bahwa ketika RenderTransform (Zoom 200% + Pan (150, -80))
            // diterapkan pada kontainer bersama (RemoveBackgroundImageStage),
            // koordinat lokal InkCanvas yang menjadi anak kontainer secara otomatis
            // terisolasi dari pergeseran visual, sehingga pemetaan ke piksel native 100% presisi.

            double nativeWidth = 4000.0;
            double nativeHeight = 3000.0;
            double displayedWidth = 800.0;
            double displayedHeight = 600.0;

            // 1. Buat hierarki visual WPF nyata
            var viewport = new Canvas { Width = 1000, Height = 800 };
            var stageContainer = new Grid { Width = displayedWidth, Height = displayedHeight };
            var inkCanvas = new InkCanvas { Width = displayedWidth, Height = displayedHeight };

            stageContainer.Children.Add(inkCanvas);
            viewport.Children.Add(stageContainer);
            Canvas.SetLeft(stageContainer, 100);
            Canvas.SetTop(stageContainer, 100);

            // 2. Terapkan Zoom 200% dan Pan persis seperti SyncPreviewGeometry di ImageSubsystemView.xaml.cs
            double zoom = 2.0; // 200%
            double panX = 150.0;
            double panY = -80.0;

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(zoom, zoom));
            transformGroup.Children.Add(new TranslateTransform(panX, panY));

            // DITERAPKAN DI LEVEL KONTAINER BERSAMA (RemoveBackgroundImageStage)
            stageContainer.RenderTransform = transformGroup;
            stageContainer.RenderTransformOrigin = new Point(0.5, 0.5);

            // Paksa pembaruan tata letak WPF
            viewport.Measure(new Size(1000, 800));
            viewport.Arrange(new Rect(0, 0, 1000, 800));
            viewport.UpdateLayout();

            // 3. Skenario: Pengguna ingin mencoret target objek di foto asli pada koordinat (X=2500, Y=1500)
            double expectedNativeX = 2500.0;
            double expectedNativeY = 1500.0;

            // Pada tampilan stage display 800x600, objek tersebut berada di:
            double stageX = expectedNativeX / (nativeWidth / displayedWidth);   // 2500 / 5.0 = 500.0
            double stageY = expectedNativeY / (nativeHeight / displayedHeight); // 1500 / 5.0 = 300.0

            // WPF menghitung posisi visual objek tersebut di layar viewport setelah Zoom & Pan:
            var stageToViewportTransform = stageContainer.TransformToAncestor(viewport);
            Point visualPointOnScreen = stageToViewportTransform.Transform(new Point(stageX, stageY));

            // Ketika mouse pengguna menekan titik visualPointOnScreen di layar:
            // WPF input subsystem secara otomatis memetakan balik ke koordinat lokal InkCanvas:
            var viewportToInkCanvasTransform = viewport.TransformToDescendant(inkCanvas);
            Point localInkPoint = viewportToInkCanvasTransform.Transform(visualPointOnScreen);

            // BUKTI: Koordinat lokal InkCanvas kembali persis ke stage coordinate (500, 300)
            // tanpa terdistorsi oleh Zoom 200% maupun Pan 150/-80!
            Assert.Equal(stageX, localInkPoint.X, 3);
            Assert.Equal(stageY, localInkPoint.Y, 3);

            // 4. Transformasikan ke resolusi native
            double scaleX = nativeWidth / displayedWidth;
            double scaleY = nativeHeight / displayedHeight;
            double calculatedNativeX = localInkPoint.X * scaleX;
            double calculatedNativeY = localInkPoint.Y * scaleY;

            // BUKTI FINAL: Koordinat hasil kalkulasi mendarat BIT-FOR-BIT tepat di (2500, 1500)
            Assert.Equal(expectedNativeX, calculatedNativeX, 3);
            Assert.Equal(expectedNativeY, calculatedNativeY, 3);
        });
    }
}
