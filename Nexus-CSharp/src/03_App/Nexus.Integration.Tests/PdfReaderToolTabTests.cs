using System.IO;
using Moq;
using Nexus.App.ViewModels;
using Nexus.PdfUtilities;

namespace Nexus.Integration.Tests;

[Collection(WpfApplicationCollection.Name)]
public class PdfReaderToolTabTests
{
    [Fact]
    public Task ChoosingTool_OpensIndependentCardTab_AndRetainsReader() => WpfAppHost.RunAsync(async () =>
    {
        var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var source=Path.Combine(root,"source.pdf");await File.WriteAllTextAsync(source,"mock PDF");
        var engine=new Mock<IPdfEngineService>();engine.Setup(x=>x.GetPageCount(source)).Returns(2);
        engine.Setup(x=>x.GetPageSizes(source,It.IsAny<CancellationToken>())).Returns(new[]{new PdfPageSize(100,200),new PdfPageSize(200,100)});
        var shell=new PdfWorkspaceViewModel(Mock.Of<IPdfConversionService>(),engine.Object);
        var reader=shell.SelectedWorkspace!;
        try
        {
            reader.AddFiles(new[]{source});await reader.ImportCompletion;
            reader.Reader.PageNumber=2;reader.Reader.ZoomPercent=125;
            reader.SelectToolCommand.Execute(reader.Tools.Single(x=>x.Id==PdfToolId.ImageExport));
            Assert.Equal(2,shell.Workspaces.Count);
            var tool=shell.SelectedWorkspace!;Assert.NotSame(reader,tool);
            await tool.ImportCompletion;
            Assert.Null(reader.SelectedTool);Assert.Same(reader.PdfFiles[0],reader.Reader.ActiveDocument);
            Assert.Equal(2,reader.Reader.PageNumber);Assert.Equal(125,reader.Reader.ZoomPercent);
            Assert.False(tool.IsDocumentView);Assert.Equal("PDF to JPG",tool.WorkspaceTitle);
            Assert.Equal(source,Assert.Single(tool.PdfFiles).Path);Assert.NotSame(reader.PdfFiles[0],tool.PdfFiles[0]);
            tool.RemoveFileCommand.Execute(tool.PdfFiles[0]);Assert.Single(reader.PdfFiles);Assert.True(File.Exists(source));
            await shell.CloseWorkspaceAsync(tool);Assert.Same(reader,shell.SelectedWorkspace);
            reader.SelectToolCommand.Execute(reader.Tools.Single(x=>x.Id==PdfToolId.ToWord));
            Assert.Equal(PdfToolId.ToWord,shell.SelectedWorkspace!.SelectedTool!.Id);
            Assert.False(shell.SelectedWorkspace.ConvertCommand.CanExecute(null));
        }
        finally{foreach(var workspace in shell.Workspaces.ToArray())await shell.CloseWorkspaceAsync(workspace);Directory.Delete(root,true);}
    });

    [Fact]
    public Task OutputFolder_IsEmptyBeforeImport_ThenUsesSourceFolder_WithoutCreatingIt() => WpfAppHost.RunAsync(async () =>
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"),"Engine","source.pdf");Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllTextAsync(path,"mock PDF");
        var engine=new Mock<IPdfEngineService>();engine.Setup(x=>x.GetPageCount(path)).Returns(1);
        var vm=new PdfUtilitiesViewModel(Mock.Of<IPdfConversionService>(),engine.Object);
        try
        {
            Assert.Empty(vm.OutputDirPath);
            vm.AddFiles(new[]{path});await vm.ImportCompletion;
            Assert.Equal(Path.Combine(Path.GetDirectoryName(path)!,"PDF_Output"),vm.OutputDirPath);
            Assert.False(Directory.Exists(vm.OutputDirPath));
        }
        finally{await vm.CloseWorkspaceAsync();Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(path)!)!,true);}
    });
}
