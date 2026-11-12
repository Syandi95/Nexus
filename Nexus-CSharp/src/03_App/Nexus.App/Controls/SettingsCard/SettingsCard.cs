using System.Windows;
using System.Windows.Controls;

namespace Nexus.App.Controls.SettingsCard
{
    /// <summary>
    /// Kontrol card baris pengaturan modern yang mengelompokkan
    /// Header (Judul), Description (Subteks), dan Action Content (Kontrol interaktif)
    /// secara konsisten di dalam kartu NexusCardStyle.
    /// </summary>
    public class SettingsCard : ContentControl
    {
        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.Register(
                nameof(Header),
                typeof(object),
                typeof(SettingsCard),
                new PropertyMetadata(null));

        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register(
                nameof(Description),
                typeof(object),
                typeof(SettingsCard),
                new PropertyMetadata(null));

        public static readonly DependencyProperty HeaderIconProperty =
            DependencyProperty.Register(
                nameof(HeaderIcon),
                typeof(string),
                typeof(SettingsCard),
                new PropertyMetadata(null));

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(
                nameof(CornerRadius),
                typeof(CornerRadius),
                typeof(SettingsCard),
                new PropertyMetadata(new CornerRadius(10)));

        public object? Header
        {
            get => GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        public object? Description
        {
            get => GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        public string? HeaderIcon
        {
            get => (string?)GetValue(HeaderIconProperty);
            set => SetValue(HeaderIconProperty, value);
        }

        public CornerRadius CornerRadius
        {
            get => (CornerRadius)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        static SettingsCard()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(SettingsCard),
                new FrameworkPropertyMetadata(typeof(SettingsCard)));
        }
    }
}
