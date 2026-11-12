using System.IO;
using Moq;
using Nexus.App.ViewModels;
using Nexus.PdfUtilities;

namespace Nexus.Integration.Tests;

public class PdfWorkspaceTests
{
    [Fact]
    public async Task Tabs_IsolateSettingsAndSelection_CloseWithoutDeletingSource_AllowReopeningLastTab()
    {
        var shell = new PdfWorkspaceViewModel(Mock.Of<IPdfConversionService>(), Mock.Of<IPdfEngineService>());
        var first = Assert.Single(shell.Workspaces);
        first.OutputDirPath = @"C:\first-output";
        first.SelectedDpi = 144;
        first.QualityValue = 91;
        first.IsSettingsVisible = false;
        var source = Path.GetTempFileName();
        try
        {
            // A file reference is workspace state; closing its tab must not delete the file.
            var files = (System.Collections.ObjectModel.ObservableCollection<PdfFileItem>)typeof(PdfUtilitiesViewModel)
                .GetField("_pdfFiles", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(first)!;
            files.Add(new PdfFileItem { Path=source, Name="source.pdf" });
            shell.AddTabCommand.Execute(null);
            var second = shell.SelectedWorkspace!;
            Assert.NotSame(first, second);
            Assert.Empty(second.PdfFiles);
            Assert.True(second.IsSettingsVisible);
            Assert.Equal(300, second.SelectedDpi);
            second.OutputDirPath = @"C:\second-output";
            shell.SelectedWorkspace = first;
            Assert.Equal(@"C:\first-output", shell.SelectedWorkspace.OutputDirPath);
            Assert.Equal(144, first.SelectedDpi);
            Assert.Equal(91, first.QualityValue);
            Assert.False(first.IsSettingsVisible);
            Assert.Single(first.PdfFiles);
            await shell.CloseWorkspaceAsync(first);
            Assert.Same(second, shell.SelectedWorkspace);
            Assert.True(File.Exists(source));
            await shell.CloseWorkspaceAsync(second);
            Assert.Empty(shell.Workspaces);
            Assert.Null(shell.SelectedWorkspace);
            shell.AddTabCommand.Execute(null);
            Assert.Same(Assert.Single(shell.Workspaces), shell.SelectedWorkspace);
        }
        finally { File.Delete(source); }
    }

    [Fact]
    public void HideRestore_PreservesInputAndSettings()
    {
        var workspace = new PdfUtilitiesViewModel(Mock.Of<IPdfConversionService>(), Mock.Of<IPdfEngineService>());
        workspace.SelectedFormatIndex = 1;
        workspace.OutputDirPath = @"C:\output";
        workspace.PageRangeText = "2-3";
        workspace.SelectedTool=workspace.Tools.Single(x=>x.Id==PdfToolId.ImageExport);
        workspace.ToggleSettingsCommand.Execute(null);
        Assert.False(workspace.IsSettingsVisible);
        workspace.ToggleSettingsCommand.Execute(null);
        Assert.True(workspace.IsSettingsVisible);
        Assert.Equal(1, workspace.SelectedFormatIndex);
        Assert.Equal(@"C:\output", workspace.OutputDirPath);
        Assert.Equal("2-3", workspace.PageRangeText);
    }

    [Fact]
    public async Task ConcurrentTabs_QueueConversionBatchesInsteadOfWritingSameOutputsTogether()
    {
        var release = new TaskCompletionSource<PdfPreflightResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls=0;
        var conversion=new Mock<IPdfConversionService>();
        conversion.Setup(x=>x.PreflightAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<int>(),
                It.IsAny<IReadOnlyList<int>?>(),It.IsAny<CancellationToken>()))
            .Returns(()=>Interlocked.Increment(ref calls)==1 ? release.Task : Task.FromResult(new PdfPreflightResult{IsSafe=true}));
        conversion.Setup(x=>x.ConvertAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<int>(),
                It.IsAny<int>(),It.IsAny<int>(),It.IsAny<IReadOnlyList<int>?>(),It.IsAny<IProgress<PdfConversionProgressReport>?>(),It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var shell=new PdfWorkspaceViewModel(conversion.Object,Mock.Of<IPdfEngineService>());
        shell.AddTabCommand.Execute(null);
        foreach(var workspace in shell.Workspaces)
        {
            workspace.SelectedTool=workspace.Tools.Single(x=>x.Id==PdfToolId.ImageExport);
            var files=(System.Collections.ObjectModel.ObservableCollection<PdfFileItem>)typeof(PdfUtilitiesViewModel)
                .GetField("_pdfFiles",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(workspace)!;
            files.Add(new PdfFileItem{Path=@"C:\same.pdf",PageCount=1});
            workspace.OutputDirPath=@"C:\same-output";
        }
        var first=shell.Workspaces[0].StartConversionAsync();
        var second=shell.Workspaces[1].StartConversionAsync();
        try{Assert.Equal(1,calls);Assert.False(second.IsCompleted);}
        finally{release.TrySetResult(new PdfPreflightResult{IsSafe=true});await Task.WhenAll(first,second);}
        Assert.Equal(2,calls);
        // A mocked writer produces no output file: success status and actual output availability are distinct.
        Assert.All(shell.Workspaces,workspace=>Assert.Equal("Berhasil",Assert.Single(workspace.JobResults).Status));
        Assert.All(shell.Workspaces,workspace=>Assert.False(workspace.HasConverted));
        Assert.All(shell.Workspaces,workspace=>Assert.Equal(@"C:\same-output",workspace.LastOutputDirectory));
    }

    [Fact]
    public async Task CloseDuringConversion_CancelsAndWaitsForWriterBeforeRemovingWorkspace()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var conversion = new Mock<IPdfConversionService>();
        conversion.Setup(x => x.PreflightAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<int>?>(), It.IsAny<CancellationToken>()))
            .Returns(async (string path, string format, int dpi, IReadOnlyList<int>? pages, CancellationToken token) =>
            {
                entered.SetResult();
                using var registration = token.Register(() => canceled.TrySetResult());
                await release.Task;
                token.ThrowIfCancellationRequested();
                return new PdfPreflightResult { IsSafe = true };
            });
        var shell = new PdfWorkspaceViewModel(conversion.Object, Mock.Of<IPdfEngineService>());
        var workspace = shell.SelectedWorkspace!;
        workspace.SelectedTool=workspace.Tools.Single(x=>x.Id==PdfToolId.ImageExport);
        var files = (System.Collections.ObjectModel.ObservableCollection<PdfFileItem>)typeof(PdfUtilitiesViewModel)
            .GetField("_pdfFiles", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(workspace)!;
        files.Add(new PdfFileItem {Path=@"C:\source.pdf",PageCount=1});
        workspace.OutputDirPath = @"C:\output";
        var running = workspace.StartConversionAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var closing = shell.CloseWorkspaceAsync(workspace);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(closing.IsCompleted);
        Assert.Contains(workspace, shell.Workspaces);
        release.SetResult();
        await Task.WhenAll(running, closing);
        Assert.Empty(shell.Workspaces);
        Assert.False(workspace.IsConverting);
    }
}
