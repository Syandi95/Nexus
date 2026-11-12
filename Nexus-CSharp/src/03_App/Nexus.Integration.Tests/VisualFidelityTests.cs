using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.AI.Llama.Services;
using Nexus.Foundation.Config;
using Xunit.Abstractions;

namespace Nexus.Integration.Tests;

public sealed class VisualCaptureFactAttribute : FactAttribute
{
    public VisualCaptureFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXUS_VISUAL_OUTPUT")))
            Skip = "Explicit screenshot output directory required.";
    }
}

public sealed class VisualFidelityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(96d)]
    [InlineData(144d)]
    public void OneDipBoundary_HasNoBlurTailOrClippingAtSupportedDpi(double dpi)
    {
        WpfAppHost.Run(() =>
        {
            ThemeManager.ApplyTheme(ThemeMode.Light);
            var edge = new Border { Width=100, Height=20, BorderThickness=new Thickness(1),
                BorderBrush=(Brush)Application.Current.FindResource("NexusStrokeDefault"),
                Background=(Brush)Application.Current.FindResource("NexusSurfaceBase"),
                SnapsToDevicePixels=true, UseLayoutRounding=true };
            edge.Measure(new Size(100,20));
            edge.Arrange(new Rect(0,0,100,20));
            var bitmap = new RenderTargetBitmap((int)(100*dpi/96),(int)(20*dpi/96),dpi,dpi,PixelFormats.Pbgra32);
            bitmap.Render(edge);
            var data = new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];
            bitmap.CopyPixels(data,bitmap.PixelWidth*4,0);
            var centerX=bitmap.PixelWidth/2;
            var first=(centerX)*4;
            Assert.Equal(255,data[first+3]);
            Assert.True(data[first]<255, "Top boundary must be visible and inside the bitmap.");
            // 1 DIP spans 1px at 100%, 1.5px at 150%; at most one fractional
            // coverage row is legitimate. No blur/shadow tail beyond ceil(scale).
            for(var y=(int)Math.Ceiling(dpi/96); y<bitmap.PixelHeight-(int)Math.Ceiling(dpi/96); y++)
            {
                var offset=(y*bitmap.PixelWidth+centerX)*4;
                Assert.Equal(255,data[offset]);
                Assert.Equal(255,data[offset+1]);
                Assert.Equal(255,data[offset+2]);
                Assert.Equal(255,data[offset+3]);
            }
        });
    }

    [Theory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    [InlineData(ThemeMode.HighContrast)]
    public void CompiledCanonicalBrushesAndControlStates_RetainVisualRoles(ThemeMode mode)
    {
        WpfAppHost.Run(() =>
        {
            ThemeManager.ApplyTheme(mode);
            foreach (var key in new[] { "NexusStrokeSubtle", "NexusStrokeDefault", "NexusStrokeStrong", "NexusStrokeFocus", "NexusContentBorder", "NexusInfoStroke" })
                Assert.IsType<SolidColorBrush>(Application.Current.FindResource(key));
            if (mode == ThemeMode.HighContrast)
            {
                Assert.Equal(SystemColors.WindowTextColor,BrushColor("TextPrimaryBrush"));
                Assert.Equal(SystemColors.WindowColor,BrushColor("WindowBackgroundBrush"));
                Assert.Equal(SystemColors.ControlDarkColor,BrushColor("NexusStrokeSubtle"));
            }
            var normal = new Button { Style = (Style)Application.Current.FindResource("NexusButtonStyle"), Content = "Action" };
            normal.Measure(new Size(160,40));
            normal.Arrange(new Rect(0,0,160,40));
            normal.ApplyTemplate();
            var chrome = Assert.IsType<Border>(normal.Template.FindName("Chrome",normal));
            Assert.Equal(new Thickness(1), chrome.BorderThickness);
            Assert.Equal(BrushColor("NexusStrokeDefault"), ((SolidColorBrush)chrome.BorderBrush).Color);
            SetState(normal, typeof(UIElement), "IsMouseOverPropertyKey", true);
            Assert.Equal(BrushColor("NexusSurfaceSunken"), ((SolidColorBrush)normal.Background).Color);
            Assert.Equal(BrushColor("NexusStrokeDefault"), ((SolidColorBrush)normal.BorderBrush).Color);
            SetState(normal, typeof(ButtonBase), "IsPressedPropertyKey", true);
            Assert.Equal(BrushColor("NexusStrokeStrong"), ((SolidColorBrush)normal.BorderBrush).Color);
            SetState(normal, typeof(ButtonBase), "IsPressedPropertyKey", false);
            SetState(normal, typeof(UIElement), "IsMouseOverPropertyKey", false);
            normal.IsEnabled = false;
            Assert.Equal(BrushColor("NexusStrokeSubtle"), ((SolidColorBrush)chrome.BorderBrush).Color);
            Assert.NotNull(normal.FocusVisualStyle);
            var card = new Border { Style = (Style)Application.Current.FindResource("NexusCardStyle") };
            Assert.Equal(new Thickness(1),card.BorderThickness);
            Assert.Equal(BrushColor("NexusStrokeSubtle"),((SolidColorBrush)card.BorderBrush).Color);
            // Selected state is verified on its real TabItem target.
            var tab = new TabItem { Style = (Style)Application.Current.FindResource("NexusWorkspaceTabItemStyle"), IsSelected = true };
            tab.Measure(new Size(160,44));
            tab.Arrange(new Rect(0,0,160,44));
            tab.ApplyTemplate();
            Assert.True(tab.IsSelected);
        });
    }

    private static Color BrushColor(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;

    [Theory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    [InlineData(ThemeMode.HighContrast)]
    public void ImageNavigation_ComponentResourcePreservesPreviewOverlayStates(ThemeMode mode)
    {
        WpfAppHost.Run(() =>
        {
            ThemeManager.ApplyTheme(mode);
            var style = Assert.IsType<Style>(Application.Current.FindResource("PhotosNavigationButtonStyle"));
            var view = new Nexus.App.Views.ImageSubsystemView();
            Assert.Same(style, view.FindResource("PhotosNavigationButtonStyle"));
            Assert.False(view.Resources.Contains("PhotosNavigationButtonStyle"));

            var button = new Button { Style = style, Content = "›" };
            var host = new Border { Child = button };
            host.Measure(new Size(36, 56));
            host.Arrange(new Rect(0, 0, 36, 56));
            button.ApplyTemplate();
            var chrome = Assert.IsType<Border>(button.Template.FindName("Chrome", button));
            Assert.Equal(36d, button.Width);
            Assert.Equal(56d, button.Height);
            Assert.Equal(new Thickness(1), chrome.BorderThickness);
            Assert.Equal(new CornerRadius(4), chrome.CornerRadius);
            Assert.Equal(0d, chrome.Opacity);
            void AssertOverlayBrush(Brush brush, byte alpha, bool stroke = false)
            {
                var solid = Assert.IsType<SolidColorBrush>(brush);
                if (mode == ThemeMode.HighContrast)
                {
                    Assert.Equal(stroke ? SystemColors.WindowColor : SystemColors.WindowTextColor, solid.Color);
                    Assert.Equal(alpha / 255d, solid.Opacity, 12);
                }
                else
                {
                    Assert.Equal(stroke ? Color.FromArgb(alpha, 255, 255, 255) : Color.FromArgb(alpha, 0, 0, 0), solid.Color);
                    Assert.Equal(1d, solid.Opacity);
                }
            }
            AssertOverlayBrush(chrome.Background, 0x50);
            AssertOverlayBrush(chrome.BorderBrush, 0x20, stroke: true);

            SetState(host, typeof(UIElement), "IsMouseOverPropertyKey", true);
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(static () => { },
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.Equal(0.88, chrome.Opacity);
            AssertOverlayBrush(chrome.Background, 0x65);
            AssertOverlayBrush(chrome.BorderBrush, 0x28, stroke: true);
            SetState(button, typeof(ButtonBase), "IsPressedPropertyKey", true);
            AssertOverlayBrush(chrome.Background, 0x85);
            SetState(button, typeof(ButtonBase), "IsPressedPropertyKey", false);
            button.IsEnabled = false;
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(static () => { },
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.Equal(0.28, chrome.Opacity);
            AssertOverlayBrush(chrome.Background, 0x35);
            AssertOverlayBrush(chrome.BorderBrush, 0x15, stroke: true);
        });
    }

    private static void SetState(DependencyObject target, Type owner, string key, bool value)
    {
        var field = owner.GetField(key,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.FlattenHierarchy);
        Assert.NotNull(field);
        target.SetValue((DependencyPropertyKey)field!.GetValue(null)!,value);
    }

    [VisualCaptureFact]
    public async Task CaptureCompiledModuleThemeDpiMatrix()
    {
        var directory = Environment.GetEnvironmentVariable("NEXUS_VISUAL_OUTPUT")!;
        Assert.True(Directory.Exists(Path.GetDirectoryName(directory)));
        Directory.CreateDirectory(directory);
        var services = ArchitectureGovernanceTests.CreateProductionServices();
        var config = new ConfigService(Path.Combine(directory, "fixture-config.json"));
        config.SaveConfig(new AppConfigRecord { ModelRootFolder = @"D:\NEXUS\Models", EngineRootFolder = @"D:\NEXUS\Engine", ConfirmExit = false });
        services.RemoveAll<IConfigService>();
        services.AddSingleton<IConfigService>(config);
        await using var provider = services.BuildServiceProvider();
        var resources = new List<object>();
        await WpfAppHost.RunAsync(() =>
        {
            provider.GetRequiredService<IModelCatalogService>().Load();
            var main = provider.GetRequiredService<MainViewModel>();
            var window = provider.GetRequiredService<Nexus.App.MainWindow>();
            window.Show();
            try
            {
                foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.HighContrast })
                {
                    ThemeManager.ApplyTheme(mode);
                    foreach (var key in new[] { "NexusStrokeSubtle", "NexusStrokeDefault", "NexusStrokeStrong", "NexusStrokeFocus", "NexusContentBorder", "NexusSurfaceBase" })
                    {
                        var value = Application.Current.TryFindResource(key);
                        resources.Add(new { mode = mode.ToString(), key, type = value?.GetType().Name, value = value?.ToString() });
                    }
                    foreach (var name in new[] { "Conversation", "PDF", "Rapikan", "CleanDownloads", "Image", "Settings" })
                    {
                        main.CurrentViewModel = name switch
                        {
                            "Conversation" => main.ConversationVM,
                            "PDF" => main.PdfUtilitiesVM,
                            "Image" => main.ImageSubsystemVM,
                            "Settings" => main.SettingsVM,
                            _ => main.FileManagementVM
                        };
                        if (name is "Rapikan" or "CleanDownloads")
                            main.FileManagementVM.SelectedTab = name == "Rapikan" ? FileManagementTab.RapikanFile : FileManagementTab.CleanDownloads;
                        window.UpdateLayout();
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        foreach (var dpi in new[] { 96d, 144d })
                        {
                            var bitmap = new RenderTargetBitmap((int)(window.ActualWidth*dpi/96), (int)(window.ActualHeight*dpi/96),dpi,dpi,PixelFormats.Pbgra32);
                            bitmap.Render(window);
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var file = File.Create(Path.Combine(directory,$"{name}-{mode}-{dpi}.png"));
                            encoder.Save(file);
                        }
                    }
                }
            }
            finally { window.DataContext = null; window.Close(); ThemeManager.ApplyTheme(ThemeMode.Dark); }
            return Task.CompletedTask;
        });
        File.WriteAllText(Path.Combine(directory,"resolved-resources.json"),JsonSerializer.Serialize(resources,new JsonSerializerOptions { WriteIndented=true }));
        output.WriteLine(JsonSerializer.Serialize(resources));
    }
}
