using System;
using System.Threading.Tasks;
using System.Windows;

namespace Nexus.App.Controls.ContentDialog
{
    public sealed class ContentDialogRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string PrimaryText { get; set; } = "OK";
        public string CancelText { get; set; } = "Batal";
        public string? SecondaryText { get; set; }
        public string IconGlyph { get; set; } = "Help";
        public bool IsDangerPrimary { get; set; }
    }

    /// <summary>
    /// Implementasi ContentDialogService yang mengelola dialog modal non-blocking
    /// terhubung ke presenter antarmuka Nexus.
    /// </summary>
    public class ContentDialogService : IContentDialogService
    {
        private Func<ContentDialogRequest, Task<ContentDialogResult>>? _handler;

        public void RegisterHandler(Func<ContentDialogRequest, Task<ContentDialogResult>> handler)
        {
            _handler = handler;
        }

        public async Task<ContentDialogResult> ShowConfirmationAsync(
            string title,
            string message,
            string primaryText = "OK",
            string cancelText = "Batal",
            string? secondaryText = null,
            string iconGlyph = "Help")
        {
            var request = new ContentDialogRequest
            {
                Title = title,
                Message = message,
                PrimaryText = primaryText,
                CancelText = cancelText,
                SecondaryText = secondaryText,
                IconGlyph = iconGlyph,
                IsDangerPrimary = false
            };

            return await ShowInternalAsync(request);
        }

        public async Task<ContentDialogResult> ShowAlertAsync(
            string title,
            string message,
            string closeText = "OK",
            string iconGlyph = "Info")
        {
            var request = new ContentDialogRequest
            {
                Title = title,
                Message = message,
                PrimaryText = closeText,
                CancelText = string.Empty,
                SecondaryText = null,
                IconGlyph = iconGlyph,
                IsDangerPrimary = false
            };

            return await ShowInternalAsync(request);
        }

        public async Task<ContentDialogResult> ShowDangerConfirmationAsync(
            string title,
            string message,
            string dangerButtonText = "Hapus",
            string cancelText = "Batal",
            string iconGlyph = "Delete")
        {
            var request = new ContentDialogRequest
            {
                Title = title,
                Message = message,
                PrimaryText = dangerButtonText,
                CancelText = cancelText,
                SecondaryText = null,
                IconGlyph = iconGlyph,
                IsDangerPrimary = true
            };

            return await ShowInternalAsync(request);
        }

        private async Task<ContentDialogResult> ShowInternalAsync(ContentDialogRequest request)
        {
            if (_handler != null)
            {
                if (Application.Current != null && Application.Current.Dispatcher != null &&
                    !Application.Current.Dispatcher.CheckAccess())
                {
                    return await Application.Current.Dispatcher.InvokeAsync(async () => await _handler(request)).Task.Unwrap();
                }

                return await _handler(request);
            }

            // Fallback default jika dijalankan dalam unit test tanpa UI Host
            return ContentDialogResult.Primary;
        }
    }
}
