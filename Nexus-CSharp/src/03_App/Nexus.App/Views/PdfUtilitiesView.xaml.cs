using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexus.App.ViewModels;

namespace Nexus.App.Views
{
    public partial class PdfUtilitiesView : UserControl
    {
        private IInputElement? _previewPreviousFocus;
        public PdfUtilitiesView()
        {
            InitializeComponent();
        }

        private void DropArea_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void DropArea_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void DropArea_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0 && DataContext is PdfUtilitiesViewModel vm)
                {
                    vm.AddFiles(files);
                }
            }
            e.Handled = true;
        }

        private void View_Drop(object sender, DragEventArgs e)
        {
            DropArea_Drop(sender, e);
        }

        private void View_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Card_OpenDocument(object sender,MouseButtonEventArgs e)
        { if(DataContext is PdfUtilitiesViewModel vm && (sender as FrameworkElement)?.DataContext is PdfFileItem item)vm.SelectDocumentCommand.Execute(item); }

        private void TxtCustomDpi_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is PdfUtilitiesViewModel vm)
            {
                if (vm.ApplyCustomDpiCommand.CanExecute(null))
                {
                    vm.ApplyCustomDpiCommand.Execute(null);
                }
            }
        }

        private void OverlayBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is PdfUtilitiesViewModel vm)
            {
                vm.CloseFullScreenCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void View_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && DataContext is PdfUtilitiesViewModel { IsFullScreenVisible: true } vm)
            {
                vm.CloseFullScreenCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void PreviewOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
            {
                _previewPreviousFocus = Keyboard.FocusedElement;
                ClosePdfPreviewButton.Focus();
            }
            else if (_previewPreviousFocus != null)
            {
                Keyboard.Focus(_previewPreviousFocus);
                _previewPreviousFocus = null;
            }
        }
    }
}
