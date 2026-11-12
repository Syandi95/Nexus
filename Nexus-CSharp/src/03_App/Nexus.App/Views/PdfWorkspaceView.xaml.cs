using System.Windows;
using System.Windows.Controls;

namespace Nexus.App.Views;

public partial class PdfWorkspaceView : UserControl
{
    public PdfWorkspaceView() => InitializeComponent();

    private void CloseTab_Click(object sender, RoutedEventArgs e) => e.Handled = true;
}
