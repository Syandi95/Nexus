using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nexus.App.Behaviors;

public static class SearchBoxBehavior
{
    public static readonly RoutedCommand ClearCommand =
        new(nameof(ClearCommand), typeof(SearchBoxBehavior));

    public static readonly DependencyProperty IsSearchEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsSearchEnabled",
            typeof(bool),
            typeof(SearchBoxBehavior),
            new PropertyMetadata(false, OnIsSearchEnabledChanged));

    private static readonly DependencyProperty ClearCommandBindingProperty =
        DependencyProperty.RegisterAttached(
            "ClearCommandBinding",
            typeof(CommandBinding),
            typeof(SearchBoxBehavior));

    public static void SetIsSearchEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsSearchEnabledProperty, value);

    public static bool GetIsSearchEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsSearchEnabledProperty);

    private static void OnIsSearchEnabledChanged(
        DependencyObject target,
        DependencyPropertyChangedEventArgs args)
    {
        if (target is not TextBox textBox)
        {
            return;
        }

        textBox.PreviewKeyDown -= OnPreviewKeyDown;

        if (textBox.GetValue(ClearCommandBindingProperty) is CommandBinding previousBinding)
        {
            textBox.CommandBindings.Remove(previousBinding);
            textBox.ClearValue(ClearCommandBindingProperty);
        }

        if (!(bool)args.NewValue)
        {
            return;
        }

        var commandBinding = new CommandBinding(
            ClearCommand,
            OnClearExecuted,
            OnClearCanExecute);
        textBox.CommandBindings.Add(commandBinding);
        textBox.SetValue(ClearCommandBindingProperty, commandBinding);
        textBox.PreviewKeyDown += OnPreviewKeyDown;
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape ||
            sender is not TextBox textBox ||
            !ClearCommand.CanExecute(null, textBox))
        {
            return;
        }

        ClearCommand.Execute(null, textBox);
        args.Handled = true;
    }

    private static void OnClearCanExecute(object sender, CanExecuteRoutedEventArgs args)
    {
        args.CanExecute =
            sender is TextBox textBox &&
            !textBox.IsReadOnly &&
            textBox.Text.Length > 0;
        args.Handled = true;
    }

    private static void OnClearExecuted(object sender, ExecutedRoutedEventArgs args)
    {
        if (sender is TextBox textBox)
        {
            textBox.SetCurrentValue(TextBox.TextProperty, string.Empty);
            args.Handled = true;
        }
    }
}
