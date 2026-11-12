$appDir = "c:\Users\Syand\3D Objects\Nexus\Nexus-CSharp\src\03_App\Nexus.App"
$vmDir = "$appDir\ViewModels"
$viewsDir = "$appDir\Views"

New-Item -ItemType Directory -Force -Path $vmDir | Out-Null
New-Item -ItemType Directory -Force -Path $viewsDir | Out-Null

$viewModels = @("HomeViewModel", "ConversationViewModel", "FileUtilitiesViewModel", "PdfUtilitiesViewModel", "ImageSubsystemViewModel", "SettingsViewModel")
foreach ($vm in $viewModels) {
    $content = @"
namespace Nexus.App.ViewModels
{
    public class $vm : ViewModelBase
    {
    }
}
"@
    Set-Content -Path "$vmDir\$vm.cs" -Value $content
}

# MainViewModel
$mainVm = @"
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Nexus.App.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private ViewModelBase _currentViewModel;
        
        public ViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            set => SetProperty(ref _currentViewModel, value);
        }

        public HomeViewModel HomeVM { get; }
        public ConversationViewModel ConversationVM { get; }
        public FileUtilitiesViewModel FileUtilitiesVM { get; }
        public PdfUtilitiesViewModel PdfUtilitiesVM { get; }
        public ImageSubsystemViewModel ImageSubsystemVM { get; }
        public SettingsViewModel SettingsVM { get; }

        public ICommand NavigateCommand { get; }

        public MainViewModel(
            HomeViewModel home,
            ConversationViewModel conversation,
            FileUtilitiesViewModel fileUtilities,
            PdfUtilitiesViewModel pdfUtilities,
            ImageSubsystemViewModel imageSubsystem,
            SettingsViewModel settings)
        {
            HomeVM = home;
            ConversationVM = conversation;
            FileUtilitiesVM = fileUtilities;
            PdfUtilitiesVM = pdfUtilities;
            ImageSubsystemVM = imageSubsystem;
            SettingsVM = settings;

            CurrentViewModel = HomeVM;

            NavigateCommand = new RelayCommand<string>(Navigate);
        }

        private void Navigate(string viewName)
        {
            CurrentViewModel = viewName switch
            {
                "Home" => HomeVM,
                "Conversation" => ConversationVM,
                "FileUtilities" => FileUtilitiesVM,
                "PdfUtilities" => PdfUtilitiesVM,
                "ImageSubsystem" => ImageSubsystemVM,
                "Settings" => SettingsVM,
                _ => CurrentViewModel
            };
        }
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly System.Action<T> _execute;
        public RelayCommand(System.Action<T> execute) => _execute = execute;
        public event System.EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute((T)parameter!);
    }
}
"@
Set-Content -Path "$vmDir\MainViewModel.cs" -Value $mainVm

$views = @("HomeView", "ConversationView", "FileUtilitiesView", "PdfUtilitiesView", "ImageSubsystemView", "SettingsView")
foreach ($view in $views) {
    $xaml = @"
<UserControl x:Class="Nexus.App.Views.$view"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" 
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008" 
             mc:Ignorable="d" 
             d:DesignHeight="450" d:DesignWidth="800">
    <Grid>
        <TextBlock Text="$view" HorizontalAlignment="Center" VerticalAlignment="Center" FontSize="24"/>
    </Grid>
</UserControl>
"@
    Set-Content -Path "$viewsDir\$view.xaml" -Value $xaml

    $cs = @"
using System.Windows.Controls;

namespace Nexus.App.Views
{
    public partial class $view : UserControl
    {
        public $view()
        {
            InitializeComponent();
        }
    }
}
"@
    Set-Content -Path "$viewsDir\$view.xaml.cs" -Value $cs
}
