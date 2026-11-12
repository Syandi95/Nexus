using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Nexus.App.Controls.ContentDialog;
using Nexus.App.ViewModels;

namespace Nexus.App.Views
{
    public partial class DshSidebarView : UserControl
    {
        private const double ExpandedWidth = 260;
        private const double RailWidth = 56;
        private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(160);

        public static readonly DependencyProperty IsExpandedProperty =
            DependencyProperty.Register(
                nameof(IsExpanded),
                typeof(bool),
                typeof(DshSidebarView),
                new PropertyMetadata(true, OnIsExpandedChanged));

        public bool IsExpanded
        {
            get => (bool)GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        public DshSidebarView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DshSidebarView sidebar && e.NewValue is bool expanded)
            {
                sidebar.AnimateCollapse(expanded);
            }
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ConversationViewModel oldVm)
            {
                oldVm.PropertyChanged -= ViewModel_PropertyChanged;
            }
            if (e.NewValue is ConversationViewModel newVm)
            {
                newVm.PropertyChanged += ViewModel_PropertyChanged;
                SetExpandedState(newVm.IsConversationPanelVisible, animate: false);
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ConversationViewModel.IsConversationPanelVisible) &&
                sender is ConversationViewModel vm)
            {
                SetExpandedState(vm.IsConversationPanelVisible, animate: true);
            }
        }

        public void SetExpandedState(bool expanded, bool animate)
        {
            IsExpanded = expanded;
            if (!animate)
            {
                Width = expanded ? ExpandedWidth : RailWidth;
                ExpandedView.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                CollapsedRailView.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void AnimateCollapse(bool expanded)
        {
            var targetWidth = expanded ? ExpandedWidth : RailWidth;
            var currentWidth = ActualWidth > 0 ? ActualWidth : (expanded ? RailWidth : ExpandedWidth);

            var animation = new DoubleAnimation(currentWidth, targetWidth, AnimationDuration)
            {
                EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
            };

            animation.Completed += (_, _) =>
            {
                BeginAnimation(WidthProperty, null);
                Width = targetWidth;
                ExpandedView.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                CollapsedRailView.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            };

            if (expanded)
            {
                ExpandedView.Visibility = Visibility.Visible;
                CollapsedRailView.Visibility = Visibility.Collapsed;
            }

            BeginAnimation(WidthProperty, animation);
        }

        private void RenameSession_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ConversationListItemViewModel item })
            {
                item.BeginRename();
            }
            else if (sender is MenuItem { CommandParameter: ConversationListItemViewModel paramItem })
            {
                paramItem.BeginRename();
            }
        }

        private async void DeleteSession_Click(object sender, RoutedEventArgs e)
        {
            ConversationListItemViewModel? targetItem = null;
            if (sender is FrameworkElement { DataContext: ConversationListItemViewModel item })
            {
                targetItem = item;
            }
            else if (sender is MenuItem { CommandParameter: ConversationListItemViewModel paramItem })
            {
                targetItem = paramItem;
            }

            if (targetItem == null || DataContext is not ConversationViewModel vm) return;

            var dialogService = (Application.Current as App)?.Services.GetService(typeof(IContentDialogService)) as IContentDialogService;
            if (dialogService != null)
            {
                var confirm = await dialogService.ShowDangerConfirmationAsync(
                    "Delete Session?",
                    $"Are you sure you want to delete session '{targetItem.Title}'?\nHistory files will be moved to the Recycle Bin.",
                    "Delete",
                    "Cancel",
                    iconGlyph: "Delete");

                if (confirm != ContentDialogResult.Primary) return;
            }

            await vm.DeleteConversationAsync(targetItem);
        }

        private void QuickDelete_Click(object sender, RoutedEventArgs e)
        {
            DeleteSession_Click(sender, e);
        }

        private async void RenameBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox { DataContext: ConversationListItemViewModel item } ||
                DataContext is not ConversationViewModel vm)
            {
                return;
            }

            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                item.CancelRename();
                return;
            }

            if (e.Key != Key.Enter) return;
            e.Handled = true;
            try
            {
                await vm.CommitRenameAsync(item);
            }
            catch
            {
                item.CancelRename();
            }
        }

        private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.IsVisible)
            {
                textBox.Dispatcher.InvokeAsync(() =>
                {
                    textBox.Focus();
                    textBox.SelectAll();
                });
            }
        }

        private void SessionListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || SessionListScrollViewer == null)
            {
                return;
            }

            e.Handled = true;
            double step = e.Delta / 2.5;
            double nextOffset = Math.Clamp(
                SessionListScrollViewer.VerticalOffset - step,
                0.0,
                SessionListScrollViewer.ScrollableHeight);
            SessionListScrollViewer.ScrollToVerticalOffset(nextOffset);
        }
    }
}
