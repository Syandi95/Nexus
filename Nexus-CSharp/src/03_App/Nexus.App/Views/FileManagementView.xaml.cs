using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nexus.App.ViewModels;

namespace Nexus.App.Views
{
    public partial class FileManagementView : UserControl
    {
        private readonly CleanDownloadsView _cleanDownloadsView;
        private readonly RapikanFileView _rapikanFileView;
        private FileManagementViewModel? _viewModel;

        public FileManagementView()
        {
            InitializeComponent();
            _cleanDownloadsView = new CleanDownloadsView();
            _rapikanFileView = new RapikanFileView();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnShellPropertyChanged;
            }

            _viewModel = e.NewValue as FileManagementViewModel;
            if (_viewModel == null)
            {
                FileManagementContentHost.Content = null;
                return;
            }

            _cleanDownloadsView.DataContext = _viewModel.CleanDownloadsVM;
            _rapikanFileView.DataContext = _viewModel.RapikanFileVM;
            _viewModel.PropertyChanged += OnShellPropertyChanged;
            UpdateActiveView();
        }

        private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileManagementViewModel.ActiveViewModel))
            {
                UpdateActiveView();
            }
        }

        private void UpdateActiveView()
        {
            if (_viewModel == null)
            {
                return;
            }

            FileManagementContentHost.Content = _viewModel.SelectedTab switch
            {
                FileManagementTab.CleanDownloads => _cleanDownloadsView,
                FileManagementTab.RapikanFile => _rapikanFileView,
                _ => _cleanDownloadsView
            };
        }
    }
}
