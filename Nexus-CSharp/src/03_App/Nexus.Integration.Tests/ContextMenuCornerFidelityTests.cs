using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace Nexus.Integration.Tests;

public class ContextMenuCornerFidelityTests
{
    private static (int width, int height, byte[] pixels) RenderElementToPixels(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();

        var w = (int)Math.Ceiling(element.ActualWidth);
        var h = (int)Math.Ceiling(element.ActualHeight);

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(element);

        var stride = w * 4;
        var pixels = new byte[stride * h];
        rtb.CopyPixels(pixels, stride, 0);

        return (w, h, pixels);
    }

    [Fact]
    public void NexusContextMenuStyle_HasTransparentBackground_AndCleanCornerTransparency()
    {
        WpfAppHost.Run(() =>
        {
            var app = Application.Current;
            var cmStyle = (Style)app.FindResource("NexusContextMenuStyle");
            Assert.NotNull(cmStyle);

            var cm = new ContextMenu { Style = cmStyle };
            cm.Items.Add(new MenuItem { Header = "Pindah ke atas" });
            cm.Items.Add(new MenuItem { Header = "Hapus" });

            Assert.Equal(Brushes.Transparent, cm.Background);

            var (w, h, pixels) = RenderElementToPixels(cm);
            var stride = w * 4;

            // Verify outer corners have alpha == 0 (no square background artifact)
            Assert.Equal(0, pixels[0 * stride + 0 * 4 + 3]); // top-left (0,0) alpha
            Assert.Equal(0, pixels[0 * stride + (w - 1) * 4 + 3]); // top-right (w-1, 0) alpha
            Assert.Equal(0, pixels[(h - 1) * stride + 0 * 4 + 3]); // bottom-left (0, h-1) alpha
            Assert.Equal(0, pixels[(h - 1) * stride + (w - 1) * 4 + 3]); // bottom-right (w-1, h-1) alpha
        });
    }

    [Fact]
    public void GeminiAttachmentContextMenuStyle_HasTransparentBackground_AndCleanCornerTransparency()
    {
        WpfAppHost.Run(() =>
        {
            var win = new Window { Width = 800, Height = 600 };
            var convView = new Nexus.App.Views.ConversationView();
            win.Content = convView;
            win.Show();
            try
            {
                var btn = (Button)convView.FindName("BtnAIAddAttachment");
                Assert.NotNull(btn);
                var cm = btn.ContextMenu;
                Assert.NotNull(cm);

                Assert.Equal(Brushes.Transparent, cm.Background);

                var (w, h, pixels) = RenderElementToPixels(cm);
                var stride = w * 4;

                // Verify outer corners have alpha == 0 (no square background artifact)
                Assert.Equal(0, pixels[0 * stride + 0 * 4 + 3]); // top-left (0,0) alpha
                Assert.Equal(0, pixels[0 * stride + (w - 1) * 4 + 3]); // top-right (w-1, 0) alpha
                Assert.Equal(0, pixels[(h - 1) * stride + 0 * 4 + 3]); // bottom-left (0, h-1) alpha
                Assert.Equal(0, pixels[(h - 1) * stride + (w - 1) * 4 + 3]); // bottom-right (w-1, h-1) alpha
            }
            finally
            {
                win.Close();
            }
        });
    }
}
