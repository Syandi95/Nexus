using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexus.App.Services
{
    public static class NexusIcon
    {
        public const string FontFamilyName = "pack://application:,,,/Nexus.App;component/Resources/Fonts/#FluentSystemIcons-Regular";

        private static readonly Dictionary<string, string> Catalog = new(StringComparer.OrdinalIgnoreCase)
        {
            // Navigation
            { "Menu", "\uF561" },
            { "Home", "\uF481" },
            { "AIWorkspace", "\uF287" },
            { "CleanDownloads", "\uF151" },
            { "RapikanFile", "\uF4D3" },
            { "PDFTools", "\uF379" },
            { "ImageTools", "\uF489" },
            { "RemoveBG", "\uE732" },
            { "Account", "\uE77B" }, { "User", "\uE77B" }, { "Models", "\uE74C" }, { "Database", "\uE74C" }, { "Sliders", "\uE713" }, { "Globe", "\uE774" }, { "Sparkle", "\uE734" }, { "Settings", "\uF6AA" },
            { "RecentActivity", "\uF47F" },
            { "Engine", "\uF83B" },
            { "DeepThink", "\uF83B" },

            // Actions
            { "Search", "\uF690" },
            { "Send", "\uE12A" },
            { "Stop", "\uF72B" },
            { "Attach", "\uF1AA" },
            { "AttachFile", "\uF1AA" },
            { "AttachFolder", "\uF41D" },
            { "Add", "\uF10A" },
            { "Remove", "\uEBD1" },
            { "Delete", "\uF34D" },
            { "Save", "\uF680" },
            { "Edit", "\uF3DE" },
            { "Copy", "\uF32C" },
            { "Paste", "\uF2D6" },
            { "Cut", "\uF33B" },
            { "Undo", "\uF19A" },
            { "Redo", "\uF16F" },
            { "RotateLeft", "\uF19A" },
            { "RotateRight", "\uF16F" },
            { "Brush", "\uF593" },
            { "Eraser", "\uF3EF" },
            { "Clean", "\uF202" },
            { "Refresh", "\uF13E" },
            { "Reset", "\uF1A0" },
            { "Sync", "\uF191" },
            { "Check", "\uF295" },
            { "MoveToTop", "\uF1A5" },
            { "MoveUp", "\uF19C" },
            { "MoveDown", "\uF149" },
            { "Up", "\uF19C" },
            { "Down", "\uF149" },

            // Window Controls
            { "Minimize", "\uEBD1" },
            { "Maximize", "\uEB76" },
            { "Restore", "\uF78C" },
            { "Close", "\uF36A" },

            // Theme
            { "LightTheme", "\uF8A2" },
            { "DarkTheme", "\uF87E" },
            { "ThemePalette", "\uF2F6" },
            { "Palette", "\uF2F6" },
            { "Light", "\uF8A2" },

            // Files & Folders
            { "Folder", "\uF419" },
            { "FolderOpen", "\uF42F" },
            { "NewFolder", "\uF41D" },
            { "File", "\uF379" },
            { "Document", "\uF379" },
            { "PDF", "\uF379" },
            { "PDFPage", "\uF379" },

            // Media
            { "Image", "\uF489" },
            { "Videos", "\uF84D" },
            { "Audio", "\uEB43" },
            { "Font", "\uF7E5" },
            { "Play", "\uF606" },
            { "Pause", "\uF5A2" },

            // Utilities
            { "Download", "\uF151" },
            { "Upload", "\uF1A5" },
            { "Print", "\uF62B" },
            { "Zoom", "\uF8C5" },
            { "ZoomIn", "\uF8C5" },
            { "ZoomOut", "\uF8C7" },
            { "Camera", "\uF255" },
            { "Lock", "\uE790" },
            { "Share", "\uF6B0" },

            // Status & Info
            { "Info", "\uF4A4" },
            { "Help", "\uF63E" },
            { "Warning", "\uF86A" },
            { "Error", "\uF3F2" },
            { "Confirmation", "\uF299" },
            { "Alert", "\uF115" },

            // Misc
            { "Stats", "\uF345" },
            { "Size", "\uEA13" },
            { "Pin", "\uF602" },
            { "Flag", "\uF40C" },
            { "Tag", "\uF77D" },
            { "Label", "\uF77D" },
            { "Compare", "\uF18E" },
            { "More", "\uE825" },
            { "Chat", "\uF287" },
            { "Repair", "\uF8C1" },
            { "ChevronLeft", "\uF2AB" },
            { "ChevronRight", "\uF2B1" },
            { "ChevronUp", "\uF2B7" },
            { "ChevronDown", "\uF2A4" },
            { "SelectAll", "\uEA87" },
            { "AutoSelect", "\uEA87" },
            { "Selection", "\uEA87" },
            { "Color", "\uF2F6" },
            { "OpenPane", "\uE8B1" },
            { "PaneToggle", "\uE8B1" },
            { "SidebarToggle", "\uE8B1" },
            { "FullScreen", "\uE685" },
            { "Archive", "\uF13A" },
            { "Applications", "\uF134" },

            // Image & Canvas Tools
            { "Filmstrip", "\uEEFC" },
            { "Scan", "\uF68B" },
            { "ScanText", "\uEA66" },
            { "ScanObject", "\uEA62" },
            { "ImageEdit", "\uF494" },
            { "Crop", "\uF331" },
            { "ZoomFit", "\uEE8D" }
        };

        public static string GetIconGlyph(string iconName)
        {
            if (string.IsNullOrWhiteSpace(iconName)) return string.Empty;
            return Catalog.TryGetValue(iconName, out var glyph) ? glyph : string.Empty;
        }

        public static readonly DependencyProperty GlyphProperty =
            DependencyProperty.RegisterAttached(
                "Glyph",
                typeof(string),
                typeof(NexusIcon),
                new PropertyMetadata(string.Empty, OnGlyphChanged));

        public static string GetGlyph(DependencyObject obj)
        {
            return (string)obj.GetValue(GlyphProperty);
        }

        public static void SetGlyph(DependencyObject obj, string value)
        {
            obj.SetValue(GlyphProperty, value);
        }

        private static readonly Uri DefaultBaseUri = new("pack://application:,,,/Nexus.App;component/Themes/Tokens/SharedMetrics.xaml");
        private static readonly FontFamily DefaultFontFamily = new(DefaultBaseUri, FontFamilyName);

        public static FontFamily Font
        {
            get
            {
                if (Application.Current?.TryFindResource("NexusGlyphFontFamily") is FontFamily xamlFont)
                {
                    return xamlFont;
                }
                return DefaultFontFamily;
            }
        }

        private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock textBlock && e.NewValue is string iconName)
            {
                var glyph = GetIconGlyph(iconName);
                if (!string.IsNullOrEmpty(glyph))
                {
                    textBlock.Text = glyph;
                    textBlock.FontFamily = Font;
                    textBlock.FontWeight = FontWeights.Normal;
                    textBlock.UseLayoutRounding = true;
                    textBlock.SnapsToDevicePixels = true;
                    TextOptions.SetTextFormattingMode(textBlock, TextFormattingMode.Ideal);
                    TextOptions.SetTextRenderingMode(textBlock, TextRenderingMode.Grayscale);
                }
            }
        }
    }
}
