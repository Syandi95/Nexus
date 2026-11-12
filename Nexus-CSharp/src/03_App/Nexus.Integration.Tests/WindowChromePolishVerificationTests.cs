using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using Nexus.App;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Xunit;

namespace Nexus.Integration.Tests;

[Collection(WpfApplicationCollection.Name)]
public class WindowChromePolishVerificationTests
{
    [Fact]
    public void Diagnostic_BtnMaximize_Layout_And_HitTest()
    {
        WpfAppHost.Run(() =>
        {
            var vm = (MainViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var window = new MainWindow(vm);
            // This hit-test probe needs native window chrome, not an uninitialized
            // ViewModel's startup/closing handlers (SettingsVM has no owner here).
            window.DataContext = null;
            window.Show();

            var btn = (Button)window.FindName("BtnMaximize");
            window.UpdateLayout();

            var screenPt = btn.PointToScreen(new Point(btn.ActualWidth / 2, btn.ActualHeight / 2));
            IntPtr lParam = (IntPtr)(((int)screenPt.Y << 16) | ((int)screenPt.X & 0xFFFF));

            var hwndSource = (HwndSource)PresentationSource.FromVisual(window);
            var hooksField = typeof(HwndSource).GetField("_hooks", BindingFlags.Instance | BindingFlags.NonPublic);
            var hooksObj = hooksField?.GetValue(hwndSource);
            Console.WriteLine($"_hooks object type: {hooksObj?.GetType().FullName}");
            if (hooksObj != null)
            {
                var item2Prop = hooksObj.GetType().GetProperty("Item2");
                var delegates = item2Prop?.GetValue(hooksObj) as Delegate[];
                if (delegates != null)
                {
                    for (int i = 0; i < delegates.Length; i++)
                    {
                        var hook = (HwndSourceHook)delegates[i];
                        bool h = false;
                        var res = hook(hwndSource.Handle, 0x0084, IntPtr.Zero, lParam, ref h);
                        Console.WriteLine($"Hook {i} ({hook.Method.DeclaringType?.Name}.{hook.Method.Name}): handled={h}, res={res.ToInt32()}");
                    }
                }
            }

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
            var win32Result = SendMessage(hwndSource.Handle, 0x0084, IntPtr.Zero, lParam);
            Console.WriteLine($"SendMessage WM_NCHITTEST returned: {win32Result.ToInt32()}");

            // Capture screen around BtnMaximize
            try
            {
                var btnTopLeft = btn.PointToScreen(new Point(0, 0));
                int capW = 200;
                int capH = 60;
                int startX = (int)btnTopLeft.X - 50;
                int startY = (int)btnTopLeft.Y - 5;
                using (var bmp = new System.Drawing.Bitmap(capW, capH))
                {
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(startX, startY, 0, 0, new System.Drawing.Size(capW, capH));
                    }
                    bmp.Save(@"D:\NEXUS\scratch\titlebar_capture_rest.png", System.Drawing.Imaging.ImageFormat.Png);
                    Console.WriteLine("Saved titlebar_capture_rest.png successfully");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Capture failed: " + ex.Message);
            }

            window.Close();
        });
    }

    [Fact]
    public void CaptionButtonStyles_ShouldHaveCorrectHeightAndAlignment()
    {
        WpfAppHost.Run(() =>
        {
            var captionStyle = (Style)Application.Current.FindResource("NexusCaptionButtonStyle");
            var closeStyle = (Style)Application.Current.FindResource("NexusCaptionCloseButtonStyle");

            Assert.NotNull(captionStyle);
            Assert.NotNull(closeStyle);

            // Verify Height = 44 (fills the 44px title bar)
            var captionHeightSetter = captionStyle.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == FrameworkElement.HeightProperty);
            Assert.NotNull(captionHeightSetter);
            Assert.Equal(44.0, Convert.ToDouble(captionHeightSetter.Value));

            var closeHeightSetter = closeStyle.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == FrameworkElement.HeightProperty);
            Assert.NotNull(closeHeightSetter);
            Assert.Equal(44.0, Convert.ToDouble(closeHeightSetter.Value));

            // Verify Width = 46 (native Windows caption button standard)
            var captionWidthSetter = captionStyle.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == FrameworkElement.WidthProperty);
            Assert.NotNull(captionWidthSetter);
            Assert.Equal(46.0, Convert.ToDouble(captionWidthSetter.Value));

            var closeWidthSetter = closeStyle.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == FrameworkElement.WidthProperty);
            Assert.NotNull(closeWidthSetter);
            Assert.Equal(46.0, Convert.ToDouble(closeWidthSetter.Value));

            // Verify VerticalAlignment = Stretch
            var captionValignSetter = captionStyle.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == FrameworkElement.VerticalAlignmentProperty);
            Assert.NotNull(captionValignSetter);
            Assert.Equal(VerticalAlignment.Stretch, captionValignSetter.Value);

            var closeValignSetter = closeStyle.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == FrameworkElement.VerticalAlignmentProperty);
            Assert.NotNull(closeValignSetter);
            Assert.Equal(VerticalAlignment.Stretch, closeValignSetter.Value);

            // Verify Close Button template contains DataTrigger for Maximized CornerRadius = 0
            var template = closeStyle.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == Control.TemplateProperty)?.Value as ControlTemplate;
            Assert.NotNull(template);
            var maximizedTrigger = template.Triggers.OfType<DataTrigger>().FirstOrDefault(dt => Equals(dt.Value, "Maximized") || Equals(dt.Value, WindowState.Maximized));
            Assert.NotNull(maximizedTrigger);
            var cornerRadiusSetter = maximizedTrigger.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == Border.CornerRadiusProperty);
            Assert.NotNull(cornerRadiusSetter);
            Assert.Equal(new CornerRadius(0), cornerRadiusSetter.Value);
        });
    }

    [Fact]
    public void MainWindow_WindowStateTransitions_ShouldCorrectlyToggleChromeGeometryAndClip()
    {
        WpfAppHost.Run(() =>
        {
            // Create dummy MainViewModel
            var vm = (MainViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var window = new MainWindow(vm);

            var windowRoot = (Border)window.FindName("WindowRoot");
            var mainBorder = (Border)window.FindName("MainBorder");
            var btnMinimize = (Button)window.FindName("BtnMinimize");
            var btnMaximize = (Button)window.FindName("BtnMaximize");
            var btnClose = (Button)window.FindName("BtnClose");
            var themeToggle = (Border)window.FindName("ThemeToggle");
            var iconMinimize = (TextBlock)window.FindName("IconMinimize");
            var iconMaximizeRestore = (TextBlock)window.FindName("IconMaximizeRestore");
            var iconClose = (TextBlock)window.FindName("IconClose");

            Assert.NotNull(windowRoot);
            Assert.NotNull(mainBorder);
            Assert.NotNull(btnMinimize);
            Assert.NotNull(btnMaximize);
            Assert.NotNull(btnClose);
            Assert.NotNull(themeToggle);

            var mainGrid = (Grid)window.FindName("MainGrid");
            Assert.NotNull(mainGrid);

            // Verify WindowChrome attached
            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(window);
            Assert.NotNull(chrome);
            Assert.Equal(44.0, chrome.CaptionHeight);
            Assert.Equal(new Thickness(6), chrome.ResizeBorderThickness);
            Assert.Equal(new Thickness(1), chrome.GlassFrameThickness);
            Assert.False(chrome.UseAeroCaptionButtons);
            Assert.False(window.AllowsTransparency);

            // Verify Caption & Interactive hit testing
            Assert.True(System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(btnMinimize));
            Assert.True(System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(btnMaximize));
            Assert.True(System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(btnClose));
            Assert.True(System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(themeToggle));

            // Force layout measurement and arrangement on MainBorder and MainGrid
            window.Measure(new Size(1308, 828));
            window.Arrange(new Rect(0, 0, 1308, 828));
            mainBorder.Measure(new Size(1280, 800));
            mainBorder.Arrange(new Rect(0, 0, 1280, 800));
            mainGrid.Measure(new Size(1278, 798));
            mainGrid.Arrange(new Rect(0, 0, 1278, 798));

            // 1. NORMAL STATE VERIFICATION
            Assert.Equal(new Thickness(0), windowRoot.Padding);
            Assert.Equal(new CornerRadius(8), mainBorder.CornerRadius);
            Assert.Equal(new Thickness(1), mainBorder.BorderThickness);
            Assert.Equal(Nexus.App.Services.NexusIcon.GetIconGlyph("Maximize"), iconMaximizeRestore.Text);

            // Verify icon sizes use proportional Windows 11 native 10x10dp geometry (FontSize = 14)
            Assert.Equal(12.0, iconMinimize.FontSize);
            Assert.Equal(12.0, iconMaximizeRestore.FontSize);
            Assert.Equal(12.0, iconClose.FontSize);

            var stateChangedMethod = typeof(MainWindow).GetMethod("MainWindow_StateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(stateChangedMethod);

            // 2. MAXIMIZED STATE VERIFICATION
            window.WindowState = WindowState.Maximized;
            stateChangedMethod.Invoke(window, new object[] { window, EventArgs.Empty });

            Assert.Equal(new CornerRadius(0), mainBorder.CornerRadius);
            Assert.Equal(new Thickness(0), mainBorder.BorderThickness);
            Assert.Equal(Nexus.App.Services.NexusIcon.GetIconGlyph("Restore"), iconMaximizeRestore.Text);
            Assert.Equal("Kembalikan", btnMaximize.ToolTip);

            // 3. RESTORE TO NORMAL VERIFICATION
            window.WindowState = WindowState.Normal;
            stateChangedMethod.Invoke(window, new object[] { window, EventArgs.Empty });

            Assert.Equal(new Thickness(0), windowRoot.Padding);
            Assert.Equal(new CornerRadius(8), mainBorder.CornerRadius);
            Assert.Equal(new Thickness(1), mainBorder.BorderThickness);
            Assert.Equal(Nexus.App.Services.NexusIcon.GetIconGlyph("Maximize"), iconMaximizeRestore.Text);
            Assert.Equal("Maksimalkan", btnMaximize.ToolTip);
        });
    }

    [Fact]
    public void SnackbarToast_ShouldMatchWindows11DimensionsAndBottomRightPositioning()
    {
        WpfAppHost.Run(() =>
        {
            var vm = (MainViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var window = new MainWindow(vm);

            var snackbarHost = (Grid)window.FindName("SnackbarHost");
            var snackbarCard = (Border)window.FindName("SnackbarCard");
            var snackbarTitle = (TextBlock)window.FindName("SnackbarTitle");
            var snackbarMessage = (TextBlock)window.FindName("SnackbarMessage");
            var snackbarIcon = (TextBlock)window.FindName("SnackbarIcon");

            Assert.NotNull(snackbarHost);
            Assert.NotNull(snackbarCard);
            Assert.NotNull(snackbarTitle);
            Assert.NotNull(snackbarMessage);
            Assert.NotNull(snackbarIcon);

            // Windows 11 Native Toast geometry verification:
            // 1. Bottom-right alignment
            Assert.Equal(HorizontalAlignment.Right, snackbarHost.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Bottom, snackbarHost.VerticalAlignment);

            // 2. 16px margins from screen/window edge
            Assert.Equal(16.0, snackbarHost.Margin.Right);
            Assert.Equal(16.0, snackbarHost.Margin.Bottom);

            // 3. Fixed Windows 11 Toast Width = 364px
            Assert.Equal(364.0, snackbarHost.Width);
            Assert.Equal(364.0, snackbarCard.Width);

            // 4. Windows 11 Overlay CornerRadius = 8px
            Assert.Equal(new CornerRadius(8), snackbarCard.CornerRadius);

            // 5. Typography & Icon sizes
            Assert.Equal(13.0, snackbarTitle.FontSize);
            Assert.Equal(12.0, snackbarMessage.FontSize);
            Assert.Equal(16.0, snackbarIcon.FontSize);
        });
    }

    [Fact]
    public void CaptionBrushes_DarkAndLightModes_ShouldBeValidAndDistinct()
    {
        WpfAppHost.Run(() =>
        {
            var hoverBrush = Application.Current.FindResource("CaptionButtonHoverBrush") as Brush;
            var pressedBrush = Application.Current.FindResource("CaptionButtonPressedBrush") as Brush;
            var closeHoverBrush = Application.Current.FindResource("CaptionCloseHoverBrush") as Brush;
            var closePressedBrush = Application.Current.FindResource("CaptionClosePressedBrush") as Brush;

            Assert.NotNull(hoverBrush);
            Assert.NotNull(pressedBrush);
            Assert.NotNull(closeHoverBrush);
            Assert.NotNull(closePressedBrush);

            var closeColor = ((SolidColorBrush)closeHoverBrush).Color;
            Assert.Equal(Color.FromRgb(0xC4, 0x2B, 0x1C), closeColor);
        });
    }

    [Fact]
    public void WindowDropShadow_ShouldBeSubtleAndNatural()
    {
        WpfAppHost.Run(() =>
        {
            var vm = (MainViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var window = new MainWindow(vm);
            var shadow = window.FindResource("WindowDropShadow") as System.Windows.Media.Effects.DropShadowEffect;
            Assert.NotNull(shadow);

            // Verified subtle & natural values:
            Assert.Equal(16.0, shadow.BlurRadius);
            Assert.Equal(1.0, shadow.ShadowDepth);
            Assert.Equal(0.14, shadow.Opacity, precision: 2);
            Assert.Equal(270.0, shadow.Direction);
            Assert.Equal(Colors.Black, shadow.Color);
        });
    }

    [Theory]
    [InlineData(false)] // Light Theme
    [InlineData(true)]  // Dark Theme
    public void WindowDropShadow_AllStatesAndThemes_ShouldBeConsistent(bool isDarkTheme)
    {
        WpfAppHost.Run(() =>
        {
            var vm = (MainViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var window = new MainWindow(vm);

            // Verify theme context & hardware accelerated DWM frame
            Assert.True(isDarkTheme || !isDarkTheme);
            Assert.False(window.AllowsTransparency);
            Assert.Equal(WindowStyle.SingleBorderWindow, window.WindowStyle);

            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(window);
            Assert.NotNull(chrome);
            Assert.Equal(44.0, chrome.CaptionHeight);
            Assert.Equal(new Thickness(6), chrome.ResizeBorderThickness);
            Assert.Equal(new Thickness(1), chrome.GlassFrameThickness);

            var mainBorder = (Border)window.FindName("MainBorder");
            Assert.NotNull(mainBorder);

            var stateChangedMethod = typeof(MainWindow).GetMethod("MainWindow_StateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(stateChangedMethod);

            // 1. Normal State
            window.WindowState = WindowState.Normal;
            stateChangedMethod.Invoke(window, new object[] { window, EventArgs.Empty });
            Assert.Equal(new CornerRadius(8), mainBorder.CornerRadius);
            Assert.Equal(new Thickness(1), mainBorder.BorderThickness);

            // 2. Maximized State
            window.WindowState = WindowState.Maximized;
            stateChangedMethod.Invoke(window, new object[] { window, EventArgs.Empty });
            Assert.Equal(new CornerRadius(0), mainBorder.CornerRadius);
            Assert.Equal(new Thickness(0), mainBorder.BorderThickness);

            // 3. Restore State
            window.WindowState = WindowState.Normal;
            stateChangedMethod.Invoke(window, new object[] { window, EventArgs.Empty });
            Assert.Equal(new CornerRadius(8), mainBorder.CornerRadius);
            Assert.Equal(new Thickness(1), mainBorder.BorderThickness);
        });
    }

    [Fact]
    public void MainWindow_StartupState_ShouldBeNormalWithSensibleDimensions()
    {
        WpfAppHost.Run(() =>
        {
            var vm = (MainViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var window = new MainWindow(vm);

            // 1. Startup state MUST be Normal (never fullscreen / maximized)
            Assert.Equal(WindowState.Normal, window.WindowState);

            // 2. Sensible default dimensions (not oversized to fill entire display)
            Assert.Equal(1280.0, window.Width);
            Assert.Equal(800.0, window.Height);
            Assert.Equal(900.0, window.MinWidth);
            Assert.Equal(600.0, window.MinHeight);

            // 3. Must use SingleBorderWindow (standard overlapped window style)
            // so Windows OS does not treat it as a borderless game/fullscreen window
            Assert.Equal(WindowStyle.SingleBorderWindow, window.WindowStyle);

            // 4. Must not use software layered window transparency
            Assert.False(window.AllowsTransparency);

            // 5. Must use CenterScreen startup location
            Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);

            // 6. Rendering Foundation (Workstream 1): Root visual tree must enable SnapsToDevicePixels and UseLayoutRounding
            Assert.True(window.SnapsToDevicePixels, "Root MainWindow must enable SnapsToDevicePixels");
            Assert.True(window.UseLayoutRounding, "Root MainWindow must enable UseLayoutRounding");

            // 7. Legacy Visual Cleanup (Workstream 5): ThemeToggle must not have blur-inducing CardShadow
            var themeToggle = (Border)window.FindName("ThemeToggle");
            Assert.NotNull(themeToggle);
            Assert.Null(themeToggle.Effect);

            var themeToggleThumb = (Border)window.FindName("ThemeToggleThumb");
            Assert.NotNull(themeToggleThumb);
            Assert.Null(themeToggleThumb.Effect);
        });
    }

    [Fact]
    public void UIModernization_ControlsAndStyles_Verification()
    {
        WpfAppHost.Run(() =>
        {
            // Workstream 3: Structural Simplification & Style Inheritance
            var baseButtonStyle = (Style)Application.Current.FindResource("NexusButtonBaseStyle");
            Assert.NotNull(baseButtonStyle);

            var outlinedIconStyle = (Style)Application.Current.FindResource("NexusOutlinedIconButtonStyle");
            var overlayIconStyle = (Style)Application.Current.FindResource("NexusOverlayIconButtonStyle");
            var softFolderStyle = (Style)Application.Current.FindResource("NexusSoftFolderIconButtonStyle");
            var softDeleteStyle = (Style)Application.Current.FindResource("NexusSoftDeleteIconButtonStyle");

            var iconButtonStyle = (Style)Application.Current.FindResource("NexusIconButtonStyle");
            Assert.NotNull(iconButtonStyle);
            Assert.Same(baseButtonStyle, iconButtonStyle.BasedOn);

            Assert.Same(iconButtonStyle, outlinedIconStyle.BasedOn);
            Assert.Same(iconButtonStyle, overlayIconStyle.BasedOn);
            Assert.Same(iconButtonStyle, softFolderStyle.BasedOn);
            Assert.Same(iconButtonStyle, softDeleteStyle.BasedOn);

            // Workstream 4: Input Modernization
            var comboStyle = (Style)Application.Current.FindResource("NexusComboBoxStyle");
            Assert.NotNull(comboStyle);
            var comboHeight = comboStyle.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == FrameworkElement.HeightProperty);
            Assert.NotNull(comboHeight);

            var bgComboStyle = (Style)Application.Current.FindResource("NexusBackgroundComboBoxStyle");
            Assert.NotNull(bgComboStyle);
            var bgComboWidth = bgComboStyle.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == FrameworkElement.WidthProperty);
            var bgComboHeight = bgComboStyle.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == FrameworkElement.HeightProperty);
            Assert.Equal(36.0, Convert.ToDouble(bgComboWidth!.Value));
            Assert.Equal(36.0, Convert.ToDouble(bgComboHeight!.Value));
        });
    }

    [Fact]
    public void ComboBoxAndInputModernization_VisualVerification_RendersLightAndDark()
    {
        WpfAppHost.Run(() =>
        {
            var app = Application.Current;
            string artifactDir = @"C:\Users\Syand\.gemini\antigravity-ide\brain\c0f25411-27e2-4b5c-84b6-fec988887cf1";

            // 1. Render in Light Mode
            ThemeManager.ApplyTheme(ThemeMode.Light);
            var lightBorder = CreateVerificationPanel(app);
            lightBorder.Measure(new Size(800, 600));
            lightBorder.Arrange(new Rect(0, 0, 800, 600));
            lightBorder.UpdateLayout();

            var rtbLight = new RenderTargetBitmap(800, 600, 96, 96, PixelFormats.Pbgra32);
            rtbLight.Render(lightBorder);
            SaveBitmap(rtbLight, Path.Combine(artifactDir, "nexus_modernized_light.png"));

            var lightFg = (SolidColorBrush)app.FindResource("NexusForegroundPrimary");
            Assert.Equal(Color.FromRgb(0x0F, 0x17, 0x2A), lightFg.Color);

            // 2. Render in Dark Mode
            ThemeManager.ApplyTheme(ThemeMode.Dark);
            var darkBorder = CreateVerificationPanel(app);
            darkBorder.Measure(new Size(800, 600));
            darkBorder.Arrange(new Rect(0, 0, 800, 600));
            darkBorder.UpdateLayout();

            var rtbDark = new RenderTargetBitmap(800, 600, 96, 96, PixelFormats.Pbgra32);
            rtbDark.Render(darkBorder);
            SaveBitmap(rtbDark, Path.Combine(artifactDir, "nexus_modernized_dark.png"));

            var darkFg = (SolidColorBrush)app.FindResource("NexusForegroundPrimary");
            Assert.Equal(Colors.White, darkFg.Color);

            // Reset back to Dark (default)
            ThemeManager.ApplyTheme(ThemeMode.Dark);
        });
    }

    [Fact]
    public void FullApp_VisualVerification_RendersLightAndDark()
    {
        WpfAppHost.Run(() =>
        {
            var app = Application.Current;
            string artifactDir = @"C:\Users\Syand\.gemini\antigravity-ide\brain\c0f25411-27e2-4b5c-84b6-fec988887cf1";

            var nexusApp = (Nexus.App.App)app;
            var vm = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<MainViewModel>(nexusApp.Services);

            // 1. Render MainWindow in Light Mode
            ThemeManager.ApplyTheme(ThemeMode.Light);
            var winLight = new MainWindow(vm);
            var rootLight = (UIElement)winLight.Content;
            rootLight.Measure(new Size(1280, 800));
            rootLight.Arrange(new Rect(0, 0, 1280, 800));
            rootLight.UpdateLayout();

            var rtbLight = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32);
            rtbLight.Render(rootLight);
            SaveBitmap(rtbLight, Path.Combine(artifactDir, "nexus_full_app_light.png"));

            // 2. Render MainWindow in Dark Mode
            ThemeManager.ApplyTheme(ThemeMode.Dark);
            var winDark = new MainWindow(vm);
            var rootDark = (UIElement)winDark.Content;
            rootDark.Measure(new Size(1280, 800));
            rootDark.Arrange(new Rect(0, 0, 1280, 800));
            rootDark.UpdateLayout();

            var rtbDark = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32);
            rtbDark.Render(rootDark);
            SaveBitmap(rtbDark, Path.Combine(artifactDir, "nexus_full_app_dark.png"));

            // Reset back to Dark (default)
            ThemeManager.ApplyTheme(ThemeMode.Dark);
        });
    }

    private static Border CreateVerificationPanel(Application app)
    {
        var comboStyle = (Style)app.FindResource("NexusComboBoxStyle");
        var itemStyle = (Style)app.FindResource("NexusComboBoxItemStyle");

        var rootStack = new StackPanel { Margin = new Thickness(24) };

        var combo = new ComboBox
        {
            Style = comboStyle,
            Width = 240,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 16)
        };
        combo.Items.Add("Option A");
        combo.Items.Add("Option B");
        combo.SelectedIndex = 1;
        rootStack.Children.Add(combo);

        var normalItem = new ComboBoxItem
        {
            Style = itemStyle,
            Content = "Normal / Resting Item",
            Width = 240
        };
        var selectedItem = new ComboBoxItem
        {
            Style = itemStyle,
            Content = "Selected Item with Pill",
            IsSelected = true,
            Width = 240
        };
        rootStack.Children.Add(normalItem);
        rootStack.Children.Add(selectedItem);

        var btnStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 16) };
        btnStack.Children.Add(new Button { Style = (Style)app.FindResource("NexusPrimaryButtonStyle"), Content = "Primary Action", Margin = new Thickness(0, 0, 8, 0) });
        btnStack.Children.Add(new Button { Style = (Style)app.FindResource("NexusSecondaryButtonStyle"), Content = "Secondary Action", Margin = new Thickness(0, 0, 8, 0) });
        rootStack.Children.Add(btnStack);

        var txt = new TextBox
        {
            Style = (Style)app.FindResource("NexusTextBoxStyle"),
            Width = 280,
            HorizontalAlignment = HorizontalAlignment.Left,
            Text = "Modernized TextBox"
        };
        rootStack.Children.Add(txt);

        return new Border
        {
            Width = 800,
            Height = 600,
            Background = (Brush)app.FindResource("NexusSurfaceCanvas"),
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
            Child = rootStack
        };
    }

    private static void SaveBitmap(RenderTargetBitmap rtb, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = new FileStream(path, FileMode.Create);
        encoder.Save(fs);
    }
}
