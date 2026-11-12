using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexus.App.Services;
using Nexus.App.Views;

namespace Nexus.Integration.Tests;

public sealed class CleanDownloadsVisualCorrectionTests
{
    [Fact]
    public void MissingPreview_UsesCenteredCanonicalFallback()
    {
        RunOnSta(() =>
        {
            var view = CreateView("PDF", hasPreview: false);
            var image = Assert.IsType<Image>(view.FindName("ImgPreview"));
            var fallback = Assert.IsType<TextBlock>(view.FindName("IconCleanPreviewFallback"));
            var fallbackContainer = Assert.IsType<Border>(LogicalTreeHelper.GetParent(fallback));

            Assert.Equal(Visibility.Collapsed, image.Visibility);
            Assert.Equal(Visibility.Visible, fallbackContainer.Visibility);
            Assert.Equal("File", NexusIcon.GetGlyph(fallback));
            Assert.Equal(NexusIcon.FontFamilyName, fallback.FontFamily.Source);
            Assert.Equal(26, fallback.FontSize);
            Assert.Equal(60, fallbackContainer.Width);
            Assert.Equal(60, fallbackContainer.Height);
            Assert.Equal(HorizontalAlignment.Center, fallbackContainer.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Center, fallbackContainer.VerticalAlignment);
        });
    }

    [Fact]
    public void ImageContentPreview_UsesOriginalPanelScaleWithoutCropping()
    {
        RunOnSta(() =>
        {
            foreach (var extension in new[] { "JPG", "JPEG", "PNG", "GIF", "BMP", "WEBP" })
            {
                var view = CreateView(extension, hasPreview: true);
                var image = Assert.IsType<Image>(view.FindName("ImgPreview"));
                var fallback = Assert.IsType<TextBlock>(view.FindName("IconCleanPreviewFallback"));
                var fallbackContainer = Assert.IsType<Border>(LogicalTreeHelper.GetParent(fallback));

                Assert.Equal(Visibility.Visible, image.Visibility);
                Assert.Equal(Visibility.Collapsed, fallbackContainer.Visibility);
                Assert.Equal(double.PositiveInfinity, image.MaxWidth);
                Assert.Equal(double.PositiveInfinity, image.MaxHeight);
                Assert.Equal(HorizontalAlignment.Center, image.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Center, image.VerticalAlignment);
                Assert.Equal(Stretch.Uniform, image.Stretch);
            }
        });
    }

    [Fact]
    public void DocumentThumbnail_RemainsVisibleAndIsConstrained()
    {
        RunOnSta(() =>
        {
            foreach (var extension in new[] { "DOC", "DOCX", "PDF", "PSD", "AI", "MD", "EML" })
            {
                var view = CreateView(extension, hasPreview: true);
                var image = Assert.IsType<Image>(view.FindName("ImgPreview"));
                var fallback = Assert.IsType<TextBlock>(view.FindName("IconCleanPreviewFallback"));
                var fallbackContainer = Assert.IsType<Border>(LogicalTreeHelper.GetParent(fallback));

                Assert.Equal(Visibility.Visible, image.Visibility);
                Assert.Equal(Visibility.Collapsed, fallbackContainer.Visibility);
                Assert.Equal(220, image.MaxWidth);
                Assert.Equal(220, image.MaxHeight);
                Assert.Equal(HorizontalAlignment.Center, image.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Center, image.VerticalAlignment);
                Assert.Equal(Stretch.Uniform, image.Stretch);
            }
        });
    }

    [Fact]
    public void Toolbar_OrdersRefreshAfterUndoWithoutChangingExistingControls()
    {
        RunOnSta(() =>
        {
            var view = new CleanDownloadsView();
            var selectAll = Assert.IsType<Button>(view.FindName("BtnCleanSelectAll"));
            var toolbar = Assert.IsType<StackPanel>(LogicalTreeHelper.GetParent(selectAll));
            var orderedNames = toolbar.Children
                .OfType<FrameworkElement>()
                .Select(element => element.Name)
                .ToArray();

            Assert.Equal(
                new[]
                {
                    "BtnCleanSelectAll",
                    "BtnCleanClearSelect",
                    "BtnCleanUndo",
                    "BtnCleanRefresh",
                    "TxtCleanRightCount"
                },
                orderedNames);
        });
    }

    private static CleanDownloadsView CreateView(string extension, bool hasPreview)
    {
        var bitmap = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
        bitmap.Freeze();

        var view = new CleanDownloadsView
        {
            DataContext = new PreviewContext
            {
                PreviewImage = hasPreview ? bitmap : null,
                SelectedItem = new PreviewItem { Ext = extension },
                IsPreviewEmpty = false
            }
        };
        view.Measure(new Size(1000, 700));
        view.Arrange(new Rect(0, 0, 1000, 700));
        view.UpdateLayout();
        return view;
    }

    private sealed class PreviewContext
    {
        public ImageSource? PreviewImage { get; init; }
        public PreviewItem? SelectedItem { get; init; }
        public bool IsPreviewEmpty { get; init; }
    }

    private sealed class PreviewItem
    {
        public string Ext { get; init; } = string.Empty;
    }

    private static void RunOnSta(Action assertion)
    {
        WpfAppHost.Run(assertion);
    }
}
