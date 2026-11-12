namespace Nexus.App.Controls.ContentDialog
{
    /// <summary>
    /// Menentukan hasil aksi tombol dari ContentDialog modal Windows 11
    /// sesuai pola di Nexus.UI Pack (Wpf.Ui.Controls.ContentDialogResult).
    /// </summary>
    public enum ContentDialogResult
    {
        /// <summary>
        /// Pengguna menutup dialog tanpa memilih tombol primer atau sekunder (misal klik Batal, Escape, atau backdrop).
        /// </summary>
        None = 0,

        /// <summary>
        /// Pengguna mengonfirmasi aksi utama (tombol primer/aksi penting).
        /// </summary>
        Primary = 1,

        /// <summary>
        /// Pengguna memilih opsi alternatif kedua (tombol sekunder/opsional).
        /// </summary>
        Secondary = 2
    }
}
