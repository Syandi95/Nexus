using System.Threading.Tasks;

namespace Nexus.App.Controls.ContentDialog
{
    /// <summary>
    /// Layanan terpusat untuk menampilkan ContentDialog modal bergaya Windows 11 Fluent Design
    /// secara asinkron (non-blocking thread UI), diadaptasi dari Nexus.UI Pack (Wpf.Ui.IContentDialogService).
    /// </summary>
    public interface IContentDialogService
    {
        /// <summary>
        /// Menampilkan dialog konfirmasi dengan tombol primer dan batal.
        /// </summary>
        Task<ContentDialogResult> ShowConfirmationAsync(
            string title,
            string message,
            string primaryText = "OK",
            string cancelText = "Batal",
            string? secondaryText = null,
            string iconGlyph = "Help");

        /// <summary>
        /// Menampilkan dialog peringatan/informasi dengan satu tombol tutup.
        /// </summary>
        Task<ContentDialogResult> ShowAlertAsync(
            string title,
            string message,
            string closeText = "OK",
            string iconGlyph = "Info");

        /// <summary>
        /// Menampilkan dialog konfirmasi untuk aksi berisiko/destruktif (misal hapus data).
        /// </summary>
        Task<ContentDialogResult> ShowDangerConfirmationAsync(
            string title,
            string message,
            string dangerButtonText = "Hapus",
            string cancelText = "Batal",
            string iconGlyph = "Delete");
    }
}
