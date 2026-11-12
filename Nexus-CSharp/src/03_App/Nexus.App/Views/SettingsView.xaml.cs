using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Nexus.App.ViewModels;

namespace Nexus.App.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
        }

        private void BtnModelOptions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = PlacementMode.Bottom;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void MoveToTop_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { CommandParameter: string modelId } && DataContext is SettingsViewModel vm)
            {
                vm.MoveModelToTopCommand.Execute(modelId);
            }
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { CommandParameter: string modelId } && DataContext is SettingsViewModel vm)
            {
                vm.MoveModelUpCommand.Execute(modelId);
            }
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { CommandParameter: string modelId } && DataContext is SettingsViewModel vm)
            {
                vm.MoveModelDownCommand.Execute(modelId);
            }
        }

        private void DeleteModel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { CommandParameter: string modelId } && DataContext is SettingsViewModel vm)
            {
                vm.DeleteAiModelCommand.Execute(modelId);
            }
        }
    }
}
