using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Nexus.App.Services;

namespace Nexus.App.Behaviors;

public static class ReducedMotion
{
    public static readonly DependencyProperty ObserveProperty =
        DependencyProperty.RegisterAttached(
            "Observe",
            typeof(bool),
            typeof(ReducedMotion),
            new PropertyMetadata(false, OnObserveChanged));

    private static readonly DependencyPropertyKey IsAnimationEnabledPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly(
            "IsAnimationEnabled",
            typeof(bool),
            typeof(ReducedMotion),
            new PropertyMetadata(true));

    public static readonly DependencyProperty IsAnimationEnabledProperty =
        IsAnimationEnabledPropertyKey.DependencyProperty;

    private static readonly DependencyProperty PreferencesProperty =
        DependencyProperty.RegisterAttached(
            "Preferences",
            typeof(IApplicationVisualPreferences),
            typeof(ReducedMotion));

    private static readonly DependencyProperty PreferencesChangedHandlerProperty =
        DependencyProperty.RegisterAttached(
            "PreferencesChangedHandler",
            typeof(EventHandler),
            typeof(ReducedMotion));

    public static void SetObserve(DependencyObject element, bool value) =>
        element.SetValue(ObserveProperty, value);

    public static bool GetObserve(DependencyObject element) =>
        (bool)element.GetValue(ObserveProperty);

    public static bool GetIsAnimationEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsAnimationEnabledProperty);

    private static void OnObserveChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element)
        {
            return;
        }

        element.Loaded -= OnLoaded;
        element.Unloaded -= OnUnloaded;
        Detach(element);

        if ((bool)args.NewValue)
        {
            element.Loaded += OnLoaded;
            element.Unloaded += OnUnloaded;
            Attach(element);
        }
        else
        {
            element.SetValue(IsAnimationEnabledPropertyKey, true);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs args) =>
        Attach((FrameworkElement)sender);

    private static void OnUnloaded(object sender, RoutedEventArgs args) =>
        Detach((FrameworkElement)sender);

    private static void Attach(FrameworkElement element)
    {
        if (element.GetValue(PreferencesChangedHandlerProperty) is EventHandler)
        {
            return;
        }

        var preferences = (Application.Current as App)?
            .Services.GetService<IApplicationVisualPreferences>();
        if (preferences is null)
        {
            element.SetValue(IsAnimationEnabledPropertyKey, true);
            return;
        }

        EventHandler handler = (_, _) =>
        {
            if (element.Dispatcher.CheckAccess())
            {
                Update(element, preferences);
            }
            else
            {
                element.Dispatcher.Invoke(() => Update(element, preferences));
            }
        };

        element.SetValue(PreferencesProperty, preferences);
        element.SetValue(PreferencesChangedHandlerProperty, handler);
        preferences.PreferencesChanged += handler;
        Update(element, preferences);
    }

    private static void Detach(FrameworkElement element)
    {
        if (element.GetValue(PreferencesProperty) is IApplicationVisualPreferences preferences &&
            element.GetValue(PreferencesChangedHandlerProperty) is EventHandler handler)
        {
            preferences.PreferencesChanged -= handler;
        }

        element.ClearValue(PreferencesProperty);
        element.ClearValue(PreferencesChangedHandlerProperty);
    }

    private static void Update(
        DependencyObject element,
        IApplicationVisualPreferences preferences) =>
        element.SetValue(
            IsAnimationEnabledPropertyKey,
            !preferences.IsReducedMotionEnabled);
}
