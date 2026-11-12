using System;
using System.Linq;
using System.Windows;

namespace Nexus.App.Services
{
    public enum ThemeMode
    {
        Dark,
        Light,
        HighContrast
    }

    public static class ThemeManager
    {
        public static ThemeMode CurrentTheme { get; private set; } = ThemeMode.Dark;
        public static event EventHandler? ThemeChanged;

        public static void ApplyTheme(ThemeMode mode)
        {
            var application = Application.Current;
            if (application == null)
            {
                CurrentTheme = mode;
                return;
            }

            if (!application.Dispatcher.CheckAccess())
            {
                application.Dispatcher.Invoke(() => ApplyTheme(mode));
                return;
            }

            var appResources = application.Resources;
            var semanticThemes = appResources.MergedDictionaries
                .Select((dictionary, index) => new { dictionary, index })
                .Where(entry => HasThemeSource(
                    entry.dictionary,
                    "Themes/Semantic/LightSemanticTheme.xaml",
                    "Themes/Semantic/DarkSemanticTheme.xaml",
                    "Themes/Semantic/HighContrastSemanticTheme.xaml"))
                .ToArray();
            var compatibilityThemes = appResources.MergedDictionaries
                .Select((dictionary, index) => new { dictionary, index })
                .Where(entry => HasThemeSource(
                    entry.dictionary,
                    "Themes/LightTheme.xaml",
                    "Themes/DarkTheme.xaml",
                    "Themes/HighContrastTheme.xaml"))
                .ToArray();

            if (semanticThemes.Length != 1 || compatibilityThemes.Length != 1)
            {
                return;
            }

            var semanticTheme = new ResourceDictionary
            {
                Source = new Uri(
                    $"/Nexus.App;component/Themes/Semantic/{GetSemanticThemeFileName(mode)}",
                    UriKind.Relative)
            };
            var compatibilityTheme = new ResourceDictionary
            {
                Source = new Uri(
                    $"/Nexus.App;component/Themes/{GetCompatibilityThemeFileName(mode)}",
                    UriKind.Relative)
            };

            var semanticThemeIndex = semanticThemes[0].index;
            var compatibilityThemeIndex = compatibilityThemes[0].index;
            var previousSemanticTheme = appResources.MergedDictionaries[semanticThemeIndex];
            var previousCompatibilityTheme = appResources.MergedDictionaries[compatibilityThemeIndex];

            try
            {
                appResources.BeginInit();
                try
                {
                    appResources.MergedDictionaries[semanticThemeIndex] = semanticTheme;
                    appResources.MergedDictionaries[compatibilityThemeIndex] = compatibilityTheme;
                }
                catch
                {
                    appResources.MergedDictionaries[semanticThemeIndex] = previousSemanticTheme;
                    appResources.MergedDictionaries[compatibilityThemeIndex] = previousCompatibilityTheme;
                    throw;
                }
                finally
                {
                    appResources.EndInit();
                }
            }
            catch
            {
                return;
            }

            CurrentTheme = mode;
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        private static string GetSemanticThemeFileName(ThemeMode mode)
            => mode switch
            {
                ThemeMode.Light => "LightSemanticTheme.xaml",
                ThemeMode.HighContrast => "HighContrastSemanticTheme.xaml",
                _ => "DarkSemanticTheme.xaml"
            };

        private static string GetCompatibilityThemeFileName(ThemeMode mode)
            => mode switch
            {
                ThemeMode.Light => "LightTheme.xaml",
                ThemeMode.HighContrast => "HighContrastTheme.xaml",
                _ => "DarkTheme.xaml"
            };

        private static bool HasThemeSource(ResourceDictionary dictionary, params string[] expectedSources)
        {
            var source = dictionary.Source?.OriginalString.Replace('\\', '/');
            return source != null && expectedSources.Any(expected =>
                source.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
                source.EndsWith($"/{expected}", StringComparison.OrdinalIgnoreCase));
        }
    }
}
