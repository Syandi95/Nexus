using System;
using Nexus.App.Services;

namespace Nexus.App.Controls.Snackbar
{
    /// <summary>
    /// Kontrak layanan Snackbar mengambang (floating toast) sesuai pola Wpf.Ui.ISnackbarService
    /// dari Nexus.UI Pack.
    /// </summary>
    public interface ISnackbarService : INotificationService
    {
    }
}
