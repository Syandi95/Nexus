namespace Nexus.App.ViewModels;

public sealed class PdfJobResult(string sourcePath,string name,string outputDirectory) : ViewModelBase
{
    private string _status="Menunggu";
    private IReadOnlyList<string> _outputs=Array.Empty<string>();
    public string SourcePath { get; } = sourcePath;
    public string Name { get; } = name;
    public string OutputDirectory { get; } = outputDirectory;
    public string Status { get => _status; internal set => SetProperty(ref _status,value); }
    public IReadOnlyList<string> Outputs { get => _outputs; internal set { SetProperty(ref _outputs,value);OnPropertyChanged(nameof(HasOutputs)); } }
    public bool HasOutputs => Outputs.Count>0;
}
