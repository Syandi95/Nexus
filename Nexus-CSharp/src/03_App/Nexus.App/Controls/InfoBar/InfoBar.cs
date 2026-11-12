using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexus.App.ViewModels;

namespace Nexus.App.Controls.InfoBar
{
    /// <summary>
    /// Kontrol inline notification banner yang diadaptasi langsung dari Nexus.UI Pack (Wpf.Ui.Controls.InfoBar).
    /// Menyajikan banner status di dalam form/halaman dengan 4 tingkat severity,
    /// ikon fluent, dan opsi tombol tutup.
    /// </summary>
    public class InfoBar : ContentControl
    {
        public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
            nameof(IsOpen),
            typeof(bool),
            typeof(InfoBar),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty IsClosableProperty = DependencyProperty.Register(
            nameof(IsClosable),
            typeof(bool),
            typeof(InfoBar),
            new PropertyMetadata(true));

        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(InfoBar),
            new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
            nameof(Message),
            typeof(string),
            typeof(InfoBar),
            new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
            nameof(Severity),
            typeof(InfoBarSeverity),
            typeof(InfoBar),
            new PropertyMetadata(InfoBarSeverity.Informational));

        public static readonly DependencyProperty CloseCommandProperty = DependencyProperty.Register(
            nameof(CloseCommand),
            typeof(ICommand),
            typeof(InfoBar),
            new PropertyMetadata(null));

        public bool IsOpen
        {
            get => (bool)GetValue(IsOpenProperty);
            set => SetValue(IsOpenProperty, value);
        }

        public bool IsClosable
        {
            get => (bool)GetValue(IsClosableProperty);
            set => SetValue(IsClosableProperty, value);
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string Message
        {
            get => (string)GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        public InfoBarSeverity Severity
        {
            get => (InfoBarSeverity)GetValue(SeverityProperty);
            set => SetValue(SeverityProperty, value);
        }

        public ICommand? CloseCommand
        {
            get => (ICommand?)GetValue(CloseCommandProperty);
            set => SetValue(CloseCommandProperty, value);
        }

        static InfoBar()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(InfoBar), new FrameworkPropertyMetadata(typeof(InfoBar)));
        }

        public InfoBar()
        {
            CloseCommand = new RelayCommand(() => IsOpen = false);
        }
    }
}
