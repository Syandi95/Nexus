using System;

namespace Nexus.App.Services
{
    public enum NotificationSeverity
    {
        Info,
        Success,
        Warning,
        Error
    }

    public class NotificationEventArgs : EventArgs
    {
        public string Title { get; }
        public string Message { get; }
        public NotificationSeverity Severity { get; }
        public TimeSpan Duration { get; }

        public NotificationEventArgs(string title, string message, NotificationSeverity severity, TimeSpan duration)
        {
            Title = title;
            Message = message;
            Severity = severity;
            Duration = duration;
        }
    }

    public interface INotificationService
    {
        event EventHandler<NotificationEventArgs>? NotificationRequested;

        void Show(string title, string message, NotificationSeverity severity = NotificationSeverity.Info, TimeSpan? duration = null);
        void ShowSuccess(string title, string message, TimeSpan? duration = null);
        void ShowWarning(string title, string message, TimeSpan? duration = null);
        void ShowError(string title, string message, TimeSpan? duration = null);
        void ShowInfo(string title, string message, TimeSpan? duration = null);
    }
}
