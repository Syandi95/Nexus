using System.Windows.Input;

namespace Nexus.App.ViewModels
{
    public enum FileManagementTab
    {
        CleanDownloads,
        RapikanFile
    }

    public sealed class FileManagementViewModel : ViewModelBase
    {
        private FileManagementTab _selectedTab = FileManagementTab.CleanDownloads;

        public FileManagementViewModel(
            CleanDownloadsViewModel cleanDownloads,
            RapikanFileViewModel rapikanFile)
        {
            CleanDownloadsVM = cleanDownloads ?? throw new ArgumentNullException(nameof(cleanDownloads));
            RapikanFileVM = rapikanFile ?? throw new ArgumentNullException(nameof(rapikanFile));
            SelectTabCommand = new RelayCommand<FileManagementTab>(tab => SelectedTab = tab);
        }

        public CleanDownloadsViewModel CleanDownloadsVM { get; }
        public RapikanFileViewModel RapikanFileVM { get; }
        public ICommand SelectTabCommand { get; }

        public FileManagementTab SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (!SetProperty(ref _selectedTab, value)) return;
                OnPropertyChanged(nameof(ActiveViewModel));
            }
        }

        public ViewModelBase ActiveViewModel => SelectedTab switch
        {
            FileManagementTab.CleanDownloads => CleanDownloadsVM,
            FileManagementTab.RapikanFile => RapikanFileVM,
            _ => CleanDownloadsVM
        };
    }
}
