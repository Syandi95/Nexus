using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
    public class SpacebarComparisonTests
    {
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

        private static (ImageSubsystemView View, ImageSubsystemViewModel Vm, HwndSource Source) CreateViewAndVm()
        {
            var vm = CreateViewModel();
            var view = new ImageSubsystemView
            {
                DataContext = vm,
                Width = 800,
                Height = 600
            };
            var source = new HwndSource(new HwndSourceParameters("Test")
            {
                Width = 1,
                Height = 1,
                WindowStyle = 0
            })
            {
                RootVisual = view
            };
            return (view, vm, source);
        }

        [Fact]
        public void SpaceKeyDown_WhenResultAvailable_SavesCompareRatio_AndSetsRatioToZero()
        {
            WpfAppHost.Run(() =>
            {
                var (view, vm, source) = CreateViewAndVm();
                try
                {
                    vm.IsImageLoaded = true;
                    vm.HasResult = true;
                    vm.CompareRatio = 0.5;

                    var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space)
                    {
                        RoutedEvent = UIElement.PreviewKeyDownEvent
                    };

                    view.UserControl_PreviewKeyDown(view, args);

                    Assert.True(args.Handled);
                    Assert.True(view.IsSpacebarHolding);
                    Assert.Equal(0.5, view.SavedCompareRatio, precision: 3);
                    Assert.Equal(0.0, vm.CompareRatio, precision: 3);
                }
                finally
                {
                    source.Dispose();
                }
            });
        }

        [Fact]
        public void SpaceKeyUp_WhenHolding_RestoresSavedRatio()
        {
            WpfAppHost.Run(() =>
            {
                var (view, vm, source) = CreateViewAndVm();
                try
                {
                    vm.IsImageLoaded = true;
                    vm.HasResult = true;
                    vm.CompareRatio = 0.65;

                    var downArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space)
                    {
                        RoutedEvent = UIElement.PreviewKeyDownEvent
                    };
                    view.UserControl_PreviewKeyDown(view, downArgs);

                    Assert.True(view.IsSpacebarHolding);
                    Assert.Equal(0.0, vm.CompareRatio, precision: 3);

                    var upArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space)
                    {
                        RoutedEvent = UIElement.PreviewKeyUpEvent
                    };
                    view.UserControl_PreviewKeyUp(view, upArgs);

                    Assert.True(upArgs.Handled);
                    Assert.False(view.IsSpacebarHolding);
                    Assert.Equal(0.65, vm.CompareRatio, precision: 3);
                }
                finally
                {
                    source.Dispose();
                }
            });
        }

        [Fact]
        public void LostKeyboardFocus_WhenHolding_RestoresSavedRatio()
        {
            WpfAppHost.Run(() =>
            {
                var (view, vm, source) = CreateViewAndVm();
                try
                {
                    vm.IsImageLoaded = true;
                    vm.HasResult = true;
                    vm.CompareRatio = 0.8;

                    var downArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space)
                    {
                        RoutedEvent = UIElement.PreviewKeyDownEvent
                    };
                    view.UserControl_PreviewKeyDown(view, downArgs);

                    Assert.True(view.IsSpacebarHolding);
                    Assert.Equal(0.0, vm.CompareRatio, precision: 3);

                    var focusArgs = new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, view, null)
                    {
                        RoutedEvent = UIElement.LostKeyboardFocusEvent
                    };
                    view.UserControl_LostKeyboardFocus(view, focusArgs);

                    Assert.False(view.IsSpacebarHolding);
                    Assert.Equal(0.8, vm.CompareRatio, precision: 3);
                }
                finally
                {
                    source.Dispose();
                }
            });
        }

        [Fact]
        public void SpaceKeyDown_WhenRepeat_IsHandled_DoesNotOverwriteSavedRatio()
        {
            WpfAppHost.Run(() =>
            {
                var (view, vm, source) = CreateViewAndVm();
                try
                {
                    vm.IsImageLoaded = true;
                    vm.HasResult = true;
                    vm.CompareRatio = 0.55;

                    var downArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space)
                    {
                        RoutedEvent = UIElement.PreviewKeyDownEvent
                    };
                    view.UserControl_PreviewKeyDown(view, downArgs);
                    Assert.Equal(0.55, view.SavedCompareRatio, precision: 3);

                    // Simulate repeat key down while holding
                    var repeatArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space)
                    {
                        RoutedEvent = UIElement.PreviewKeyDownEvent
                    };
                    view.UserControl_PreviewKeyDown(view, repeatArgs);

                    Assert.True(repeatArgs.Handled);
                    Assert.Equal(0.55, view.SavedCompareRatio, precision: 3);
                    Assert.Equal(0.0, vm.CompareRatio, precision: 3);
                }
                finally
                {
                    source.Dispose();
                }
            });
        }

        [Fact]
        public void SpaceKeyDown_FromTextBoxBase_IsIgnored()
        {
            WpfAppHost.Run(() =>
            {
                var (view, vm, source) = CreateViewAndVm();
                try
                {
                    vm.IsImageLoaded = true;
                    vm.HasResult = true;
                    vm.CompareRatio = 0.5;

                    var textBox = new TextBox();
                    var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space)
                    {
                        RoutedEvent = UIElement.PreviewKeyDownEvent,
                        Source = textBox
                    };

                    view.UserControl_PreviewKeyDown(textBox, args);

                    Assert.False(args.Handled);
                    Assert.False(view.IsSpacebarHolding);
                    Assert.Equal(0.5, vm.CompareRatio, precision: 3);
                }
                finally
                {
                    source.Dispose();
                }
            });
        }

        [Fact]
        public void SpaceKeyDown_WhenNoResult_IsIgnored()
        {
            WpfAppHost.Run(() =>
            {
                var (view, vm, source) = CreateViewAndVm();
                try
                {
                    vm.IsImageLoaded = true;
                    vm.HasResult = false;
                    vm.CompareRatio = 0.5;

                    var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space)
                    {
                        RoutedEvent = UIElement.PreviewKeyDownEvent
                    };

                    view.UserControl_PreviewKeyDown(view, args);

                    Assert.False(args.Handled);
                    Assert.False(view.IsSpacebarHolding);
                    Assert.Equal(0.5, vm.CompareRatio, precision: 3);
                }
                finally
                {
                    source.Dispose();
                }
            });
        }

        [Fact]
        public void SpaceKeyDown_WhenRefining_IsIgnored()
        {
            WpfAppHost.Run(() =>
            {
                var (view, vm, source) = CreateViewAndVm();
                try
                {
                    vm.IsImageLoaded = true;
                    vm.HasResult = true;
                    vm.IsRemoveBgRefining = true;
                    vm.CompareRatio = 0.5;

                    var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space)
                    {
                        RoutedEvent = UIElement.PreviewKeyDownEvent
                    };

                    view.UserControl_PreviewKeyDown(view, args);

                    Assert.False(args.Handled);
                    Assert.False(view.IsSpacebarHolding);
                    Assert.Equal(0.5, vm.CompareRatio, precision: 3);
                }
                finally
                {
                    source.Dispose();
                }
            });
        }
    }
}
