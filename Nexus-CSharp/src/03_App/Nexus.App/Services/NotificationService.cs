using System;
using System.Windows;

namespace Nexus.App.Services
{
    public class NotificationService : INotificationService
    {
        public event EventHandler<NotificationEventArgs>? NotificationRequested;

        public void Show(string title, string message, NotificationSeverity severity = NotificationSeverity.Info, TimeSpan? duration = null)
        {
            var timeout = duration ?? TimeSpan.FromSeconds(3.5);
            var args = new NotificationEventArgs(title, message, severity, timeout);

            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() => NotificationRequested?.Invoke(this, args)));
            }
            else
            {
                NotificationRequested?.Invoke(this, args);
            }
        }

        public void ShowSuccess(string title, string message, TimeSpan? duration = null)
            => Show(title, message, NotificationSeverity.Success, duration);

        public void ShowWarning(string title, string message, TimeSpan? duration = null)
            => Show(title, message, NotificationSeverity.Warning, duration);

        public void ShowError(string title, string message, TimeSpan? duration = null)
            => Show(title, message, NotificationSeverity.Error, duration);

        public void ShowInfo(string title, string message, TimeSpan? duration = null)
            => Show(title, message, NotificationSeverity.Info, duration);
    }
}
