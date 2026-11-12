using System.IO;
using Moq;
using Nexus.App.ViewModels;
using Nexus.PdfUtilities;

namespace Nexus.Integration.Tests;

[Collection(WpfApplicationCollection.Name)]
public class PdfToolWorkflowTests
{
    [Fact]
    public void RightPanel_DistinguishesToolsRailFromHiddenSettings()
    {
        var vm=new PdfUtilitiesViewModel(Mock.Of<IPdfConversionService>(),Mock.Of<IPdfEngineService>());
        var width=vm.GetType().GetProperty("RightPanelWidth");
        Assert.NotNull(width);
        Assert.Equal(338d,width!.GetValue(vm));
        vm.ToggleSettingsCommand.Execute(null);
        Assert.Equal(90d,width.GetValue(vm));
        vm.SelectedTool=vm.Tools.Single(x=>x.Id==PdfToolId.ImageExport);
        Assert.Equal(328d,width.GetValue(vm));
        vm.ToggleSettingsCommand.Execute(null);
        Assert.Equal(0d,width.GetValue(vm));
    }

    [Fact]
    public void NewWorkspace_StartsAtAddFiles()
    {
        var shell = new PdfWorkspaceViewModel(Mock.Of<IPdfConversionService>(),Mock.Of<IPdfEngineService>());
        Assert.Equal("Tambahkan file",shell.SelectedWorkspace!.WorkspaceTitle);
        Assert.Empty(shell.SelectedWorkspace.PdfFiles);
    }

    [Fact]
    public Task ImportRejectsDuplicateSources() => WpfAppHost.RunAsync(async () =>
    {
        var path = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".pdf");
        await File.WriteAllTextAsync(path,"%PDF-1.4\n%%EOF");
        var engine = new Mock<IPdfEngineService>();
        engine.Setup(x=>x.GetPageCount(path)).Returns(1);
        var vm = new PdfUtilitiesViewModel(Mock.Of<IPdfConversionService>(),engine.Object);
        try
        {
            vm.AddFiles(new[]{path,path});
            Assert.Single(vm.PdfFiles);
            await vm.ImportCompletion;
            Assert.True(vm.PdfFiles[0].IsReady);
            Assert.True(File.Exists(path));
        }
        finally { await vm.CloseWorkspaceAsync(); File.Delete(path); }
    });

    [Fact]
    public async Task ToolSwitch_PreservesSourcesAndEachToolSettings_AndRejectsUnavailableExecution()
    {
        var conversion=new Mock<IPdfConversionService>(MockBehavior.Strict);
        var vm=new PdfUtilitiesViewModel(conversion.Object,Mock.Of<IPdfEngineService>());
        var word=vm.Tools.Single(x=>x.Id==PdfToolId.ToWord);
        var excel=vm.Tools.Single(x=>x.Id==PdfToolId.ToExcel);
        Assert.Equal(14,vm.Tools.Count);
        Assert.Null(vm.SelectedTool);
        vm.SelectedTool=word;
        ((PdfChoiceSetting)word.Settings.Find("ocr")!).Value="OCR";
        vm.SelectedTool=excel;
        ((PdfChoiceSetting)excel.Settings.Find("layout")!).Value="One Sheet";
        vm.SelectedTool=word;
        Assert.Equal("OCR",((PdfChoiceSetting)vm.SelectedToolSettings!.Find("ocr")!).Value);
        Assert.Equal("One Sheet",((PdfChoiceSetting)excel.Settings.Find("layout")!).Value);
        Assert.DoesNotContain(word.Settings.Fields,x=>x.Key is "dpi" or "quality");
        Assert.False(vm.ConvertCommand.CanExecute(null));
        await vm.StartConversionAsync();
        conversion.VerifyNoOtherCalls();
        Assert.True(vm.IsInfoBarOpen);
    }

    [Fact]
    public Task ImportPendingOrInvalid_IsNotAProcessTarget() => WpfAppHost.RunAsync(async () =>
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".pdf");
        await File.WriteAllTextAsync(path,"broken PDF");
        var engine=new Mock<IPdfEngineService>();
        engine.Setup(x=>x.GetPageCount(path)).Throws(new InvalidDataException("corrupt"));
        var vm=new PdfUtilitiesViewModel(Mock.Of<IPdfConversionService>(),engine.Object);
        vm.SelectedTool=vm.Tools.Single(x=>x.Id==PdfToolId.ImageExport);
        try
        {
            vm.AddFiles(new[]{path});
            Assert.False(vm.CanConvert);
            await vm.ImportCompletion;
            Assert.False(vm.CanConvert);
            Assert.False(vm.PdfFiles[0].IsReady);
            Assert.Contains("corrupt",vm.PdfFiles[0].ImportError);
        }
        finally { await vm.CloseWorkspaceAsync(); File.Delete(path); }
    });
}
