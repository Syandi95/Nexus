using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Moq;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.App.Views;
using Nexus.Contracts;
using Nexus.Core.HomeSettings.Services;
using Nexus.Engine.ColorManagement;
using Nexus.Engine.ImageSubsystem.Services;
using Xunit;

namespace Nexus.Integration.Tests
{
    [Collection(WpfApplicationCollection.Name)]
    public class ImageGeometryLifecycleTests
    {
        private static readonly byte[] _minimalPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

        private static string CreateTempPng(string dir, string fileName = "test.png")
        {
            var path = Path.Combine(dir, fileName);
            File.WriteAllBytes(path, _minimalPng);
            return path;
        }

        private static ImageSubsystemViewModel CreateViewModel()
        {
            var workflowMock = new Mock<IImageSubsystemWorkflow>();
            var genericEngineMock = new Mock<IGenericImageEngine>();
            var cmykMock = new Mock<ICmykProfileManager>();
            var settingsMock = new Mock<ISettingsManagerService>();
            settingsMock
                .Setup(s => s.ModelRootFolder)
                .Returns(Path.Combine(Path.GetTempPath(), "NexusTest", "Models"));

            return new ImageSubsystemViewModel(
                workflowMock.Object,
                genericEngineMock.Object,
                cmykMock.Object,
                () => Mock.Of<IColorManagementService>(),
                settingsMock.Object);
        }

        private static void DrainDispatcher() =>
            Dispatcher.CurrentDispatcher.Invoke(
                static () => { },
                DispatcherPriority.ApplicationIdle);

        private static void EnsureRequiredResources()
        {
            var appRoot = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "Nexus.App"));

            var safeThemeFiles = new[]
            {
                "Themes/Tokens/PrimitiveTokens.xaml",
                "Themes/Tokens/SharedMetrics.xaml",
                "Themes/Semantic/DarkSemanticTheme.xaml",
                "Themes/Components/Buttons.xaml",
                "Themes/Compatibility/LegacyBrushAliases.xaml",
                "Themes/Compatibility/LegacyStyleAliases.xaml",
                "Themes/DarkTheme.xaml"
            };

            foreach (var file in safeThemeFiles)
            {
                var path = Path.Combine(appRoot, file);
                if (!File.Exists(path)) continue;
                var uri = new Uri(path, UriKind.Absolute);
                bool alreadyLoaded = false;
                foreach (var md in Application.Current.Resources.MergedDictionaries)
                {
                    if (md.Source == uri) { alreadyLoaded = true; break; }
                }
                if (!alreadyLoaded)
                {
                    try
                    {
                        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
                    }
                    catch { }
                }
            }

            var fallbackStyles = new (string Key, Type TargetType)[]
            {
                ("NexusSecondaryButtonStyle", typeof(System.Windows.Controls.Button)),
                ("NexusPrimaryButtonStyle", typeof(System.Windows.Controls.Button)),
                ("NexusToolbarButtonStyle", typeof(System.Windows.Controls.Button)),
                ("NexusIconButtonStyle", typeof(System.Windows.Controls.Button)),
                ("NexusSoftFolderIconButtonStyle", typeof(System.Windows.Controls.Button)),
                ("NexusSliderStyle", typeof(System.Windows.Controls.Slider)),
                ("NexusSliderStandard", typeof(System.Windows.Controls.Slider)),
                ("NexusComboBoxStyle", typeof(System.Windows.Controls.ComboBox)),
                ("NexusBackgroundComboBoxStyle", typeof(System.Windows.Controls.ComboBox)),
                ("NexusRadioButtonStyle", typeof(System.Windows.Controls.RadioButton)),
                ("NexusRadioButtonStandard", typeof(System.Windows.Controls.RadioButton)),
                ("NexusTextBoxStyle", typeof(System.Windows.Controls.TextBox)),
                ("NexusTextBoxStandard", typeof(System.Windows.Controls.TextBox)),
                ("NexusPasswordBoxStyle", typeof(System.Windows.Controls.PasswordBox)),
                ("NexusCheckBoxStyle", typeof(System.Windows.Controls.CheckBox)),
                ("NexusScrollViewerStyle", typeof(System.Windows.Controls.ScrollViewer)),
                ("NexusProgressBarStyle", typeof(System.Windows.Controls.ProgressBar)),
                ("NexusBrushStatusInfo", typeof(System.Windows.Controls.TextBlock)),
                ("NexusFontLabel", typeof(System.Windows.Controls.TextBlock)),
                ("RemoveBackgroundProgressStyle", typeof(System.Windows.Controls.ProgressBar)),
                ("RemoveBackgroundSettingsTextBoxStyle", typeof(System.Windows.Controls.TextBox))
            };

            foreach (var (key, targetType) in fallbackStyles)
            {
                if (!Application.Current.Resources.Contains(key))
                {
                    Application.Current.Resources[key] = new Style(targetType);
                }
            }

            var brushes = new[]
            {
                "AccentPrimaryBrush",
                "SurfacePrimaryBrush",
                "SurfaceSecondaryBrush",
                "BorderSubtleBrush",
                "TextPrimaryBrush",
                "TextSecondaryBrush",
                "NexusSoftFolderForeground"
            };
            foreach (var brushKey in brushes)
            {
                if (!Application.Current.Resources.Contains(brushKey))
                {
                    Application.Current.Resources[brushKey] = Brushes.Transparent;
                }
            }
        }

        [Fact]
        public void Comparison_ZoomCycle_KeepsDividerAndBeforeAfterLabels()
        {
            WpfAppHost.Run(() =>
            {
                EnsureRequiredResources();
                using var tempDir = new TempDirectory();
                var vm = CreateViewModel();
                var view = new ImageSubsystemView { DataContext = vm, Width = 800, Height = 600 };
                vm.LoadImage(CreateTempPng(tempDir.Path));
                vm.ResultImageSource = vm.BeforeImageSource;
                vm.HasResult = true;
                view.Measure(new Size(800, 600));
                view.Arrange(new Rect(0, 0, 800, 600));
                view.UpdateLayout();

                foreach (var zoom in new[] { 100.0, 150.0, 200.0, 75.0, 100.0 })
                {
                    vm.ZoomLevel = zoom;
                    vm.CompareRatio = 0.4;
                    DrainDispatcher();
                    Assert.True(view.RemoveBackgroundBeforeClip.Rect.Width > 0);
                    Assert.Equal(Visibility.Visible, view.ThumbRemoveBackgroundCompare.Visibility);
                    Assert.True(view.ThumbRemoveBackgroundCompare.ActualHeight > 0);
                    var clipX = view.RemoveBackgroundBeforeClip.Rect.Left;
                    var transformedX = view.ImgRemoveBackgroundBefore
                        .TransformToAncestor(view.RemoveBackgroundPreview)
                        .Transform(new Point(clipX, 0)).X;
                    var dividerCenter = ((TranslateTransform)view.ThumbRemoveBackgroundCompare.RenderTransform).X
                        + view.ThumbRemoveBackgroundCompare.Width / 2.0;
                    Assert.Equal(transformedX, dividerCenter, 1);
                }

                vm.ZoomLevel = 200.0;
                DrainDispatcher();
                var dividerX = ((TranslateTransform)view.ThumbRemoveBackgroundCompare.RenderTransform).X;
                typeof(ImageSubsystemView).GetField("_panX", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(view, 24.0);
                typeof(ImageSubsystemView).GetMethod("SyncPreviewGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(view, null);
                view.UpdateLayout();
                typeof(ImageSubsystemView).GetMethod("SyncPreviewGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(view, null);
                Assert.Equal(dividerX + 24.0,
                    ((TranslateTransform)view.ThumbRemoveBackgroundCompare.RenderTransform).X, 1);

                view.ThumbRemoveBackgroundCompare.ApplyTemplate();
                var labels = new System.Collections.Generic.List<string>();
                void Visit(DependencyObject node)
                {
                    if (node is System.Windows.Controls.TextBlock textBlock)
                        labels.Add(textBlock.Text);
                    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                        Visit(VisualTreeHelper.GetChild(node, i));
                }
                Visit(view.ThumbRemoveBackgroundCompare);
                Assert.Contains("BEFORE", labels);
                Assert.Contains("AFTER", labels);
            });
        }

        [Fact]
        public void Xaml_ImgRemoveBackgroundBefore_HasNotifyOnTargetUpdatedAndHandler()
        {
            var appRoot = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "Nexus.App"));
            var xamlPath = Path.Combine(appRoot, "Views", "ImageSubsystemView.xaml");
            Assert.True(File.Exists(xamlPath));

            var xamlContent = File.ReadAllText(xamlPath);
            Assert.Contains("NotifyOnTargetUpdated=True", xamlContent);
            Assert.Contains("Binding.TargetUpdated=\"ImgRemoveBackgroundBefore_TargetUpdated\"", xamlContent);
        }

        [Fact]
        public void Scenario1_ImageSwap_DifferentAspect_GeometryUpdates()
        {
            WpfAppHost.Run(() =>
            {
                EnsureRequiredResources();
                using var tempDir = new TempDirectory();
                var pathA = CreateTempPng(tempDir.Path, "imageA.png");
                var pathB = CreateTempPng(tempDir.Path, "imageB.png");

                var vm = CreateViewModel();
                var view = new ImageSubsystemView
                {
                    DataContext = vm,
                    Width = 800,
                    Height = 600
                };

                // Load Image A with size 400x300
                vm.LoadImage(pathA);
                view.ImgRemoveBackgroundBefore.Width = 400;
                view.ImgRemoveBackgroundBefore.Height = 300;
                view.Measure(new Size(800, 600));
                view.Arrange(new Rect(0, 0, 800, 600));
                view.UpdateLayout();
                DrainDispatcher();

                var clipRectA = view.RemoveBackgroundBeforeClip.Rect;
                Assert.NotEqual(Rect.Empty, clipRectA);
                Assert.True(clipRectA.Width > 0, "Image A clip width must be positive.");
                Assert.Equal(300, clipRectA.Height);

                // Swap to Image B with different aspect ratio (600x200)
                vm.LoadImage(pathB);
                view.ImgRemoveBackgroundBefore.Width = 600;
                view.ImgRemoveBackgroundBefore.Height = 200;
                view.Measure(new Size(800, 600));
                view.Arrange(new Rect(0, 0, 800, 600));
                view.UpdateLayout();
                DrainDispatcher();

                var clipRectB = view.RemoveBackgroundBeforeClip.Rect;
                Assert.NotEqual(Rect.Empty, clipRectB);
                Assert.Equal(200, clipRectB.Height);
                Assert.NotEqual(clipRectA.Height, clipRectB.Height);
            });
        }

        [Fact]
        public void Scenario2_ImageSwap_SameDisplayedSize_GeometryRecalculated_A3Acceptance()
        {
            WpfAppHost.Run(() =>
            {
                EnsureRequiredResources();
                using var tempDir = new TempDirectory();
                var pathA = CreateTempPng(tempDir.Path, "imageA_same.png");
                var pathB = CreateTempPng(tempDir.Path, "imageB_same.png");

                var vm = CreateViewModel();
                var view = new ImageSubsystemView
                {
                    DataContext = vm,
                    Width = 800,
                    Height = 600
                };

                // Setup Image A with 500x500 displayed size
                vm.LoadImage(pathA);
                view.ImgRemoveBackgroundBefore.Width = 500;
                view.ImgRemoveBackgroundBefore.Height = 500;
                view.Measure(new Size(800, 600));
                view.Arrange(new Rect(0, 0, 800, 600));
                view.UpdateLayout();
                DrainDispatcher();

                // Set compare ratio to 0.7
                vm.CompareRatio = 0.7;
                DrainDispatcher();

                var clipA = view.RemoveBackgroundBeforeClip.Rect;
                Assert.Equal(500 * 0.7, clipA.X, 1);
                Assert.Equal(500 * (1.0 - 0.7), clipA.Width, 1);

                // Swap to Image B (same displayed size 500x500, window not resized)
                // A3 Acceptance Test: ViewModel_PropertyChanged resets clip to Rect.Empty,
                // then TargetUpdated / InvokePreviewGeometrySync must recalculate geometry!
                vm.LoadImage(pathB);
                view.Measure(new Size(800, 600));
                view.Arrange(new Rect(0, 0, 800, 600));
                view.UpdateLayout();
                DrainDispatcher();

                var clipB = view.RemoveBackgroundBeforeClip.Rect;
                // Clip must be genuinely recalculated and NOT remain Rect.Empty
                Assert.NotEqual(Rect.Empty, clipB);
                Assert.Equal(500, clipB.Height);
                Assert.True(clipB.Width > 0);
            });
        }

        [Fact]
        public void Scenario3_WindowResize_AfterImageSwap_SizeChangedFallbackWorks()
        {
            WpfAppHost.Run(() =>
            {
                EnsureRequiredResources();
                using var tempDir = new TempDirectory();
                var path = CreateTempPng(tempDir.Path, "image_resize.png");

                var vm = CreateViewModel();
                var view = new ImageSubsystemView
                {
                    DataContext = vm,
                    Width = 800,
                    Height = 600
                };

                vm.LoadImage(path);
                view.ImgRemoveBackgroundBefore.Width = 400;
                view.ImgRemoveBackgroundBefore.Height = 300;
                view.Measure(new Size(800, 600));
                view.Arrange(new Rect(0, 0, 800, 600));
                view.UpdateLayout();
                DrainDispatcher();

                var initialClip = view.RemoveBackgroundBeforeClip.Rect;
                Assert.Equal(300, initialClip.Height);

                // Simulate window/container resize where image element size changes
                view.Width = 1200;
                view.Height = 900;
                view.ImgRemoveBackgroundBefore.Width = 600;
                view.ImgRemoveBackgroundBefore.Height = 450;
                view.Measure(new Size(1200, 900));
                view.Arrange(new Rect(0, 0, 1200, 900));
                view.UpdateLayout();
                DrainDispatcher();

                var resizedClip = view.RemoveBackgroundBeforeClip.Rect;
                Assert.NotEqual(Rect.Empty, resizedClip);
                Assert.Equal(450, resizedClip.Height);
                Assert.NotEqual(initialClip.Height, resizedClip.Height);
            });
        }

        [Fact]
        public void Scenario4_GuardZeroSize_PreventsInvalidThumbPlacement()
        {
            WpfAppHost.Run(() =>
            {
                EnsureRequiredResources();
                var vm = CreateViewModel();
                var view = new ImageSubsystemView
                {
                    DataContext = vm
                };

                // ImgRemoveBackgroundBefore has ActualWidth=0, ActualHeight=0
                Assert.Equal(0, view.ImgRemoveBackgroundBefore.ActualWidth);
                Assert.Equal(0, view.ImgRemoveBackgroundBefore.ActualHeight);

                // Initially clip is Rect.Empty or 0,0,0,0
                view.RemoveBackgroundBeforeClip.Rect = Rect.Empty;
                view.ThumbRemoveBackgroundCompare.RenderTransform = Transform.Identity;

                // Fire property change that invokes geometry sync
                vm.CompareRatio = 0.8;
                DrainDispatcher();

                // Due to guard (width <= 0 || height <= 0), Rect.Empty is preserved
                Assert.Equal(Rect.Empty, view.RemoveBackgroundBeforeClip.Rect);
                Assert.Equal(Transform.Identity, view.ThumbRemoveBackgroundCompare.RenderTransform);
            });
        }

        [Fact]
        public void Scenario5_ProcessingOverlay_ImageProcessingScannerConfiguredCorrectly()
        {
            WpfAppHost.Run(() =>
            {
                EnsureRequiredResources();
                var vm = CreateViewModel();
                var view = new ImageSubsystemView
                {
                    DataContext = vm
                };

                var scanner = view.ImageScanner;
                Assert.NotNull(scanner);
                Assert.True(scanner.ClipToBounds);
                Assert.Equal(Visibility.Collapsed, scanner.Visibility);
                // Under REVISION 6.1 strict dispRect discipline:
                // When IsActive becomes true without valid dispRect geometry, scanner remains Collapsed
                scanner.IsActive = true;
                Assert.Equal(Visibility.Collapsed, scanner.Visibility);

                // Once dispRect geometry is assigned and element is loaded, scanner becomes Visible
                scanner.Width = 400;
                scanner.Height = 300;
                scanner.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Assert.Equal(Visibility.Visible, scanner.Visibility);
            });
        }
    }
}
