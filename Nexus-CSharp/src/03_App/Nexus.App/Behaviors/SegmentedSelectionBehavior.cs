using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nexus.App.Behaviors;

public static class SegmentedSelectionBehavior
{
    public static readonly DependencyProperty IsSegmentedProperty =
        DependencyProperty.RegisterAttached(
            "IsSegmented",
            typeof(bool),
            typeof(SegmentedSelectionBehavior),
            new PropertyMetadata(false, OnIsSegmentedChanged));

    public static void SetIsSegmented(DependencyObject element, bool value) =>
        element.SetValue(IsSegmentedProperty, value);

    public static bool GetIsSegmented(DependencyObject element) =>
        (bool)element.GetValue(IsSegmentedProperty);

    private static void OnIsSegmentedChanged(
        DependencyObject target,
        DependencyPropertyChangedEventArgs args)
    {
        if (target is not ListBox listBox)
        {
            return;
        }

        listBox.PreviewKeyDown -= OnPreviewKeyDown;

        if ((bool)args.NewValue)
        {
            listBox.SelectionMode = SelectionMode.Single;
            listBox.PreviewKeyDown += OnPreviewKeyDown;
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (sender is not ListBox listBox || listBox.Items.Count == 0)
        {
            return;
        }

        var currentIndex = listBox.SelectedIndex;
        if (args.Key == Key.Left || args.Key == Key.Up)
        {
            if (currentIndex > 0)
            {
                listBox.SelectedIndex = currentIndex - 1;
            }
            else
            {
                listBox.SelectedIndex = listBox.Items.Count - 1;
            }
            args.Handled = true;
        }
        else if (args.Key == Key.Right || args.Key == Key.Down)
        {
            if (currentIndex < listBox.Items.Count - 1)
            {
                listBox.SelectedIndex = currentIndex + 1;
            }
            else
            {
                listBox.SelectedIndex = 0;
            }
            args.Handled = true;
        }
    }
}
