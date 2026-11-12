using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Moq;
using Nexus.App.Controls.InfoBar;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.Contracts.AiCheck;
using Nexus.Contracts.ReferenceData;
using Nexus.Core.HomeSettings.Services;
using Xunit;

namespace Nexus.Integration.Tests
{
    public class UnifiedFeedbackTests
    {
        private class TestNotificationService : INotificationService
        {
            public event EventHandler<NotificationEventArgs>? NotificationRequested;

            public int SuccessCount { get; private set; }
            public int ErrorCount { get; private set; }
            public int WarningCount { get; private set; }
            public int InfoCount { get; private set; }

            public string? LastSuccessTitle { get; private set; }
            public string? LastSuccessMessage { get; private set; }
            public string? LastErrorTitle { get; private set; }
            public string? LastErrorMessage { get; private set; }
            public string? LastWarningTitle { get; private set; }
            public string? LastWarningMessage { get; private set; }

            public void Show(string title, string message, NotificationSeverity severity = NotificationSeverity.Info, TimeSpan? duration = null)
            {
                switch (severity)
                {
                    case NotificationSeverity.Success:
                        ShowSuccess(title, message, duration);
                        break;
                    case NotificationSeverity.Error:
                        ShowError(title, message, duration);
                        break;
                    case NotificationSeverity.Warning:
                        ShowWarning(title, message, duration);
                        break;
                    case NotificationSeverity.Info:
                        ShowInfo(title, message, duration);
                        break;
                }
            }

            public void ShowSuccess(string title, string message, TimeSpan? duration = null)
            {
                SuccessCount++;
                LastSuccessTitle = title;
                LastSuccessMessage = message;
                NotificationRequested?.Invoke(this, new NotificationEventArgs(title, message, NotificationSeverity.Success, duration ?? TimeSpan.FromSeconds(2)));
            }

            public void ShowWarning(string title, string message, TimeSpan? duration = null)
            {
                WarningCount++;
                LastWarningTitle = title;
                LastWarningMessage = message;
                NotificationRequested?.Invoke(this, new NotificationEventArgs(title, message, NotificationSeverity.Warning, duration ?? TimeSpan.FromSeconds(2)));
            }

            public void ShowError(string title, string message, TimeSpan? duration = null)
            {
                ErrorCount++;
                LastErrorTitle = title;
                LastErrorMessage = message;
                NotificationRequested?.Invoke(this, new NotificationEventArgs(title, message, NotificationSeverity.Error, duration ?? TimeSpan.FromSeconds(2)));
            }

            public void ShowInfo(string title, string message, TimeSpan? duration = null)
            {
                InfoCount++;
                NotificationRequested?.Invoke(this, new NotificationEventArgs(title, message, NotificationSeverity.Info, duration ?? TimeSpan.FromSeconds(2)));
            }
        }

        [Fact]
        public void AiCheck_CopyCorrection_Success_SetsIsCopiedOnly_NoSnackbar()
        {
            WpfAppHost.Run(() =>
            {
                var mockRef = new Mock<IReferenceDataManager>();
                var mockJudge = new Mock<IAiCheckJudgeService>();
                var notification = new TestNotificationService();
                var vm = new AiCheckViewModel(mockRef.Object, mockJudge.Object, notification);

                var finding = new AiCheckFindingItemViewModel
                {
                    BoxId = 1,
                    RecommendedText = "Teks Perbaikan"
                };

                // Ensure clean clipboard hook for testing
                NexusClipboard.CustomSetTextAction = _ => { };
                try
                {
                    Assert.False(finding.IsCopied);

                    vm.CopyCorrectionCommand.Execute(finding);

                    // Local visual cue MUST be active
                    Assert.True(finding.IsCopied);

                    // Transient snackbar MUST NOT be called on success (no duplicate noise)
                    Assert.Equal(0, notification.SuccessCount);
                    Assert.Equal(0, notification.ErrorCount);
                }
                finally
                {
                    NexusClipboard.CustomSetTextAction = null;
                }
            });
        }

        [Fact]
        public void AiCheck_CopyCorrection_Failure_ShowsSnackbarError()
        {
            WpfAppHost.Run(() =>
            {
                var mockRef = new Mock<IReferenceDataManager>();
                var mockJudge = new Mock<IAiCheckJudgeService>();
                var notification = new TestNotificationService();
                var vm = new AiCheckViewModel(mockRef.Object, mockJudge.Object, notification);

                var finding = new AiCheckFindingItemViewModel
                {
                    BoxId = 2,
                    RecommendedText = "Teks Perbaikan"
                };

                // Simulate COM clipboard lock failure
                NexusClipboard.CustomSetTextAction = _ =>
                    throw new COMException("Cannot open clipboard", NexusClipboard.CannotOpenClipboard);
                try
                {
                    vm.CopyCorrectionCommand.Execute(finding);

                    // Local visual cue must NOT be active
                    Assert.False(finding.IsCopied);

                    // Error snackbar MUST be shown gracefully
                    Assert.Equal(1, notification.ErrorCount);
                    Assert.Equal("Gagal Menyalin", notification.LastErrorTitle);
                    Assert.Contains("clipboard", notification.LastErrorMessage, StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    NexusClipboard.CustomSetTextAction = null;
                }
            });
        }

        [Fact]
        public void Settings_ValidationFailure_ShowsInfoBarWithoutSnackbarDuplication()
        {
            var notification = new TestNotificationService();
            var manager = new SettingsManagerService(new FakeConfigService());
            var vm = new SettingsViewModel(
                manager,
                new FakeCmykProfileManager(),
                modelCatalogService: null,
                notificationService: notification,
                contentDialogService: null);

            // Trigger Save with empty downloads folder
            vm.DownloadsFolder = "";
            vm.SaveSettingsCommand.Execute(null);

            // InfoBar MUST be open for persistent page form validation
            Assert.True(vm.IsInfoBarOpen);
            Assert.Equal("Peringatan", vm.InfoBarTitle);
            Assert.Equal(InfoBarSeverity.Warning, vm.InfoBarSeverity);

            // Snackbar MUST NOT be duplicated
            Assert.Equal(0, notification.WarningCount);
        }

        [Fact]
        public void AiCheckViewXaml_CopyButton_HasVisualCueTriggers()
        {
            var xamlPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..",
                "src", "03_App", "Nexus.App", "Views", "AiCheckView.xaml");

            if (!File.Exists(xamlPath))
            {
                xamlPath = @"D:\NEXUS\Nexus-CSharp\src\03_App\Nexus.App\Views\AiCheckView.xaml";
            }

            Assert.True(File.Exists(xamlPath), $"AiCheckView.xaml not found at {xamlPath}");
            var xaml = File.ReadAllText(xamlPath);

            // Verify DataTrigger for IsCopied on glyph and text
            Assert.Contains("DataTrigger Binding=\"{Binding IsCopied}\" Value=\"True\"", xaml);
            Assert.Contains("Setter Property=\"services:NexusIcon.Glyph\" Value=\"Check\"", xaml);
            Assert.Contains("Setter Property=\"Text\" Value=\"Tersalin\"", xaml);
        }
    }
}
