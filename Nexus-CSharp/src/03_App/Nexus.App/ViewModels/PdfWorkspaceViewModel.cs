using System.Collections.ObjectModel;
using System.Windows.Input;
using Nexus.App.Services;
using Nexus.PdfUtilities;

namespace Nexus.App.ViewModels;

public sealed class PdfWorkspaceViewModel : ViewModelBase
{
    private readonly IPdfConversionService _conversion;
    private readonly IPdfEngineService _engine;
    private readonly INotificationService? _notifications;
    private readonly IPdfOfficeToolService? _officeTools;
    private readonly ObservableCollection<PdfUtilitiesViewModel> _workspaces = new();
    private PdfUtilitiesViewModel? _selectedWorkspace;

    public PdfWorkspaceViewModel(IPdfConversionService conversion, IPdfEngineService engine,
        INotificationService? notifications = null, IPdfOfficeToolService? officeTools = null)
    {
        _conversion = conversion;
        _engine = engine;
        _notifications = notifications;
        _officeTools = officeTools;
        Workspaces = new(_workspaces);
        AddTabCommand = new RelayCommand(AddWorkspace);
        CloseTabCommand = new AsyncRelayCommand<PdfUtilitiesViewModel>(CloseWorkspaceAsync);
        AddWorkspace();
    }

    public ReadOnlyObservableCollection<PdfUtilitiesViewModel> Workspaces { get; }
    public ICommand AddTabCommand { get; }
    public ICommand CloseTabCommand { get; }
    public bool HasNoWorkspaces => _workspaces.Count == 0;

    public PdfUtilitiesViewModel? SelectedWorkspace
    {
        get => _selectedWorkspace;
        set => SetProperty(ref _selectedWorkspace, value);
    }

    private PdfUtilitiesViewModel CreateWorkspace()
    {
        var workspace=new PdfUtilitiesViewModel(_conversion,_engine,_notifications,_officeTools);
        workspace.ToolRequested+=OpenToolWorkspace;
        _workspaces.Add(workspace);SelectedWorkspace=workspace;
        OnPropertyChanged(nameof(HasNoWorkspaces));return workspace;
    }
    private void AddWorkspace()=>CreateWorkspace();
    private void OpenToolWorkspace(PdfUtilitiesViewModel reader,PdfToolId toolId)
    {
        if(!_workspaces.Contains(reader)||!reader.IsReaderWorkspace)return;
        var paths=reader.PdfFiles.Select(x=>x.Path).ToArray();
        var workspace=CreateWorkspace();
        workspace.SelectedTool=workspace.Tools.Single(x=>x.Id==toolId);
        workspace.OutputDirPath=reader.OutputDirPath;
        workspace.AddFiles(paths);
    }

    public async Task CloseWorkspaceAsync(PdfUtilitiesViewModel? workspace)
    {
        if (workspace == null || !_workspaces.Contains(workspace)) return;
        await workspace.CloseWorkspaceAsync();
        int index = _workspaces.IndexOf(workspace);
        if (index < 0) return;
        bool wasSelected = ReferenceEquals(SelectedWorkspace, workspace);
        workspace.ToolRequested-=OpenToolWorkspace;
        _workspaces.RemoveAt(index);
        if (wasSelected || SelectedWorkspace == null)
            SelectedWorkspace = _workspaces.Count == 0 ? null : _workspaces[Math.Min(index, _workspaces.Count - 1)];
        OnPropertyChanged(nameof(HasNoWorkspaces));
    }
}
