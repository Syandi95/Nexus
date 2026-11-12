using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using Moq;
using Nexus.App.ViewModels;
using Nexus.PdfUtilities;

namespace Nexus.Integration.Tests;

[Collection(WpfApplicationCollection.Name)]
public class PdfJobSnapshotTests
{
    [Fact]
    public Task CancelBatch_KeepsPartialOutputsAndDistinguishesUnstartedSources() => WpfAppHost.RunAsync(async () =>
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var converter=new Mock<IPdfConversionService>();
        converter.Setup(x=>x.PreflightAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<int>(),It.IsAny<IReadOnlyList<int>?>(),It.IsAny<CancellationToken>())).ReturnsAsync(new PdfPreflightResult{IsSafe=true});
        converter.Setup(x=>x.ConvertAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<int>(),It.IsAny<int>(),It.IsAny<int>(),It.IsAny<IReadOnlyList<int>?>(),It.IsAny<IProgress<PdfConversionProgressReport>?>(),It.IsAny<CancellationToken>()))
            .Returns(async (string path,string output,string format,int dpi,int quality,int naming,IReadOnlyList<int>? pages,IProgress<PdfConversionProgressReport>? progress,CancellationToken token)=>
            {
                Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,"1.png"),"completed page",token);
                progress?.Report(new(){CurrentPage=1,OutputFilePath=Path.Combine(output,"1.png")});
                entered.TrySetResult();await Task.Delay(Timeout.Infinite,token);
            });
        var vm=new PdfUtilitiesViewModel(converter.Object,Mock.Of<IPdfEngineService>());
        var files=(ObservableCollection<PdfFileItem>)typeof(PdfUtilitiesViewModel).GetField("_pdfFiles",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(vm)!;
        for(int i=0;i<3;i++)files.Add(new(){Path=$@"C:\input\{i}.pdf",PageCount=2});
        vm.SelectedTool=vm.Tools.Single(x=>x.Id==PdfToolId.ImageExport);
        var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));vm.OutputDirPath=root;
        try
        {
            var job=vm.StartConversionAsync();await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.CancelConversionCommand.Execute(null);await job;
            Assert.Equal("Dibatalkan",vm.JobResults[0].Status);Assert.Single(vm.JobResults[0].Outputs);
            Assert.All(vm.JobResults.Skip(1),x=>{Assert.StartsWith("Belum diproses",x.Status);Assert.Empty(x.Outputs);});
            Assert.True(vm.HasConverted);Assert.False(vm.IsConverting);
        }
        finally{await vm.CloseWorkspaceAsync();if(Directory.Exists(root))Directory.Delete(root,true);}
    });

    [Fact]
    public Task ChangingWorkspaceWhilePreflightWaits_DoesNotChangeJobTargetsOrParameters() => WpfAppHost.RunAsync(async () =>
    {
        var gate=new TaskCompletionSource<PdfPreflightResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls=new List<(string Path,string Output,int Quality,int Naming)>();
        var converter=new Mock<IPdfConversionService>();
        converter.Setup(x=>x.PreflightAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<int>(),It.IsAny<IReadOnlyList<int>?>(),It.IsAny<CancellationToken>())).Returns(gate.Task);
        converter.Setup(x=>x.ConvertAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<int>(),It.IsAny<int>(),It.IsAny<int>(),It.IsAny<IReadOnlyList<int>?>(),It.IsAny<IProgress<PdfConversionProgressReport>?>(),It.IsAny<CancellationToken>()))
            .Callback<string,string,string,int,int,int,IReadOnlyList<int>?,IProgress<PdfConversionProgressReport>?,CancellationToken>((path,output,format,dpi,quality,naming,pages,progress,token)=>calls.Add((path,output,quality,naming))).Returns(Task.CompletedTask);
        var vm=new PdfUtilitiesViewModel(converter.Object,Mock.Of<IPdfEngineService>());
        var files=(ObservableCollection<PdfFileItem>)typeof(PdfUtilitiesViewModel).GetField("_pdfFiles",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(vm)!;
        files.Add(new(){Path=@"C:\input\a.pdf",Name="a.pdf",PageCount=2});
        vm.SelectedTool=vm.Tools.Single(x=>x.Id==PdfToolId.ImageExport);
        var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        vm.OutputDirPath=root;vm.QualityValue=81;vm.NamingIndex=2;
        var job=vm.StartConversionAsync();
        files.Add(new(){Path=@"C:\input\b.pdf",Name="b.pdf",PageCount=2});
        vm.OutputDirPath=root+"-changed";vm.QualityValue=20;vm.NamingIndex=0;
        vm.SelectedTool=vm.Tools.Single(x=>x.Id==PdfToolId.ToWord);
        gate.SetResult(new(){IsSafe=true});await job;
        var call=Assert.Single(calls);
        Assert.Equal(@"C:\input\a.pdf",call.Path);Assert.Equal(81,call.Quality);Assert.Equal(2,call.Naming);
        Assert.Equal(root,call.Output);
        await vm.CloseWorkspaceAsync();
        if(Directory.Exists(root))Directory.Delete(root,true);
    });
}
