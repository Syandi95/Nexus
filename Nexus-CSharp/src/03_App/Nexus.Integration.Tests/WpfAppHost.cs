using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Linq;
using System.Collections;
using System.Runtime.InteropServices;
using Nexus.App.Services;

namespace Nexus.Integration.Tests;

// Single WPF application per AppDomain (WPF forbids more than one).
// All STA test actions are marshalled onto one dedicated host thread so
// Application.Current and themed resources are shared and consistent.
public static class WpfAppHost
{
    private static readonly object Sync = new();
    private static Dispatcher? _dispatcher;
    private static Exception? _dispatcherFailure;
    private static Uri[] _canonicalSources = [];
    private static DictionaryEntry[] _canonicalEntries = [];

    public static Task RunAsync(Func<Task> action) =>
        Dispatcher.InvokeAsync(action).Task.Unwrap();

    // Read assertions also need bounded retry: another desktop process may
    // briefly own the Win32 clipboard even though the test collections are serial.
    public static T WithClipboardRetry<T>(Func<T> action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return action(); }
            catch (COMException error) when (error.HResult == NexusClipboard.CannotOpenClipboard && attempt < 9)
            {
                Thread.Sleep(40);
            }
        }
    }

    public static void Run(Action action)
    {
        Exception? failure = null;
        Dispatcher.Invoke(() =>
        {
            // Recreate dictionaries, not the Application. Deferred BAML resources
            // and mutations of child dictionaries must not leak between tests.
            ResetCanonicalResources();
            var resources = Application.Current.Resources;
            var dictionaries = resources.MergedDictionaries.ToArray();
            var entries = resources.Cast<DictionaryEntry>().ToArray();
            var theme = ThemeManager.CurrentTheme;
            try
            {
                action();
                Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
                if (_dispatcherFailure is { } dispatcherFailure)
                {
                    _dispatcherFailure = null;
                    ExceptionDispatchInfo.Capture(dispatcherFailure).Throw();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                resources.BeginInit();
                try
                {
                resources.Clear();
                resources.MergedDictionaries.Clear();
                foreach (var dictionary in dictionaries)
                    resources.MergedDictionaries.Add(dictionary);
                foreach (var entry in entries)
                    resources[entry.Key] = entry.Value;
                }
                finally { resources.EndInit(); }
                if (ThemeManager.CurrentTheme != theme)
                    ThemeManager.ApplyTheme(theme);
            }
        });

        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static Dispatcher Dispatcher
    {
        get
        {
            EnsureStarted();
            return _dispatcher!;
        }
    }

    private static void EnsureStarted()
    {
        if (_dispatcher != null)
        {
            return;
        }

        lock (Sync)
        {
            if (_dispatcher != null)
            {
                return;
            }

            var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    var app = new Nexus.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    // Production shows a modal crash dialog; a test must surface the
                    // exception as a failure instead of hanging a shared test process.
                    var productionHandler = typeof(Nexus.App.App).GetMethod(
                        "App_DispatcherUnhandledException", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    app.DispatcherUnhandledException -= (DispatcherUnhandledExceptionEventHandler)
                        productionHandler.CreateDelegate(typeof(DispatcherUnhandledExceptionEventHandler), app);
                    app.DispatcherUnhandledException += (_, args) =>
                    {
                        _dispatcherFailure ??= args.Exception;
                        args.Handled = true;
                    };
                    app.InitializeComponent();
                    typeof(Nexus.App.App).GetMethod(
                        "RegisterTypographyCompatibilityResources",
                        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);
                    _canonicalSources = app.Resources.MergedDictionaries.Select(d =>
                    {
                        var source = d.Source!.OriginalString;
                        if (source.Contains(";component/", StringComparison.Ordinal)) return d.Source;
                        return new Uri($"pack://application:,,,/Nexus.App;component/{source.TrimStart('/')}", UriKind.Absolute);
                    }).ToArray();
                    _canonicalEntries = app.Resources.Cast<DictionaryEntry>().ToArray();
                    ready.SetResult(Dispatcher.CurrentDispatcher);
                }
                catch (Exception error)
                {
                    ready.SetException(error);
                    return;
                }
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "Nexus.WpfTestHost"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            _dispatcher = ready.Task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
        }
    }

    private static void ResetCanonicalResources()
    {
        var resources = Application.Current.Resources;
        resources.BeginInit();
        try
        {
            resources.Clear();
            resources.MergedDictionaries.Clear();
            foreach (var source in _canonicalSources)
                resources.MergedDictionaries.Add(new ResourceDictionary { Source = source });
            foreach (var entry in _canonicalEntries)
                resources[entry.Key] = entry.Value;
        }
        finally { resources.EndInit(); }
        ThemeManager.ApplyTheme(ThemeMode.Dark);
    }

    private static void LoadThemeResources()
    {
        var files = new[]
        {
            "Themes/Tokens/PrimitiveTokens.xaml",
            "Themes/Tokens/SharedMetrics.xaml",
            "Themes/Semantic/DarkSemanticTheme.xaml",
            "Themes/Components/Accessibility.xaml",
            "Themes/Components/Buttons.xaml",
            "Themes/Components/InputControls.xaml",
            "Themes/Components/SegmentedControl.xaml",
            "Themes/Components/Surfaces.xaml",
            "Themes/Components/Feedback.xaml",
            "Themes/Components/Overlays.xaml",
            "Themes/Components/Menus.xaml",
            "Themes/Components/Layout.xaml",
            "Themes/Compatibility/LegacyBrushAliases.xaml",
            "Themes/Compatibility/LegacyStyleAliases.xaml",
            "Themes/SharedStyles.xaml",
            "Themes/DarkTheme.xaml",
            "Controls/SettingsCard/SettingsCard.xaml"
        };

        foreach (var file in files)
        {
            var dictionary = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Nexus.App;component/{file}", UriKind.Absolute)
            };
            Application.Current.Resources.MergedDictionaries.Add(dictionary);
        }
    }
}
