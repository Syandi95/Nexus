using Nexus.App.ViewModels;
using System.Collections.ObjectModel;
using Moq;
using Nexus.PdfUtilities;

namespace Nexus.Integration.Tests;

[Collection(WpfApplicationCollection.Name)]
public class PdfReaderTests
{
    [Fact]
    public Task DocumentSelectionBinding_DoesNotLoadSameDocumentTwice() => WpfAppHost.RunAsync(async () =>
    {
        var source=new PdfFileItem{Path=@"C:\sample.pdf",PageCount=2};
        var sources=new ObservableCollection<PdfFileItem>{source};
        var engine=new Mock<IPdfEngineService>();
        engine.Setup(x=>x.GetPageSizes(source.Path,It.IsAny<CancellationToken>())).Returns(new[]{new PdfPageSize(100,200),new PdfPageSize(200,100)});
        var reader=new PdfReaderViewModel(engine.Object,new(sources));
        reader.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(reader.ActiveDocument))_ = reader.OpenDocumentAsync(source);};
        await reader.OpenDocumentAsync(source);
        Assert.Equal(2,reader.PageCount);
        engine.Verify(x=>x.GetPageSizes(source.Path,It.IsAny<CancellationToken>()),Times.Once);
        await reader.CloseAsync();
    });

    [Fact]
    public Task ClosedReader_CanReceiveLateViewUnload() => WpfAppHost.RunAsync(async () =>
    {
        var vm=new PdfUtilitiesViewModel(Mock.Of<IPdfConversionService>(),Mock.Of<IPdfEngineService>());
        await vm.Reader.CloseAsync();vm.Reader.Suspend();await vm.Reader.CloseAsync();
    });

    [Fact]
    public void WorkspaceProvidesReaderUsingExistingSources()
    {
        var reader=typeof(PdfUtilitiesViewModel).GetProperty("Reader");
        Assert.NotNull(reader);
        Assert.NotNull(reader.PropertyType.GetProperty("ActiveDocument"));
        Assert.NotNull(reader.PropertyType.GetMethod("OpenDocumentAsync"));
    }

    [Fact]
    public Task ReaderSwitch_PreservesOneSourceIdentityAndDocumentState_ReleasesOffscreenBitmaps() => WpfAppHost.RunAsync(async () =>
    {
        var sources=new ObservableCollection<PdfFileItem>{new(){Path=@"C:\one.pdf",Name="one.pdf",PageCount=2},new(){Path=@"C:\two.pdf",Name="two.pdf",PageCount=2}};
        var engine=new Mock<IPdfEngineService>();
        engine.Setup(x=>x.GetPageSizes(It.IsAny<string>(),It.IsAny<CancellationToken>())).Returns(new[]{new PdfPageSize(100,200),new PdfPageSize(200,100)});
        engine.Setup(x=>x.GetPageImage(It.IsAny<string>(),It.IsAny<int>(),It.IsAny<int>(),It.IsAny<int>())).Returns(new PdfPageImageResult(new byte[4],1,1,4));
        var reader=new PdfReaderViewModel(engine.Object,new(sources));
        try
        {
            await reader.OpenDocumentAsync(sources[0]);
            Assert.Same(sources[0],reader.ActiveDocument);
            reader.PageNumber=999;Assert.Equal(2,reader.PageNumber);
            reader.ZoomPercent=2000;Assert.Equal(1000,reader.ZoomPercent);
            reader.RotateViewCommand.Execute(90);
            await reader.SetVisiblePagesAsync(new[]{0});Assert.NotNull(reader.Pages[0].Image);
            await reader.SetVisiblePagesAsync(new[]{1});Assert.Null(reader.Pages[0].Image);Assert.NotNull(reader.Pages[1].Image);
            await reader.OpenDocumentAsync(sources[1]);
            Assert.Same(sources[1],reader.ActiveDocument);
            Assert.Equal(PdfReaderFitMode.FitPage,reader.FitMode);
            await reader.OpenDocumentAsync(sources[0]);
            Assert.Equal(1000,reader.ZoomPercent);Assert.Equal(2,reader.PageNumber);Assert.Equal(90,reader.Pages[0].Rotation);
            Assert.Equal(2,sources.Count);
        }
        finally { await reader.CloseAsync(); }
    });

    [Fact]
    public Task ReaderZoom_RejectsStaleRenderAndErrors() => WpfAppHost.RunAsync(async () =>
    {
        var source=new PdfFileItem{Path=@"C:\same.pdf",PageCount=1};
        var sources=new ObservableCollection<PdfFileItem>{source};
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release=new ManualResetEventSlim();
        int calls=0;
        var engine=new Mock<IPdfEngineService>();
        engine.Setup(x=>x.GetPageSizes(source.Path,It.IsAny<CancellationToken>())).Returns(new[]{new PdfPageSize(100,200)});
        engine.Setup(x=>x.GetPageImage(source.Path,0,It.IsAny<int>(),It.IsAny<int>())).Returns(()=>
        {
            if(Interlocked.Increment(ref calls)==1){entered.SetResult();if(!release.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException();throw new InvalidOperationException("old render");}
            return new PdfPageImageResult(new byte[8],2,1,8);
        });
        var reader=new PdfReaderViewModel(engine.Object,new(sources));
        await reader.OpenDocumentAsync(source);
        var older=reader.SetVisiblePagesAsync(new[]{0});
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            reader.ZoomPercent=200;
            var newer=reader.SetVisiblePagesAsync(new[]{0});
            release.Set();await Task.WhenAll(older,newer);
            Assert.Equal(2,reader.Pages[0].Image!.PixelWidth);
            Assert.Empty(reader.Pages[0].Status);
        }
        finally { release.Set();await older;await reader.CloseAsync(); }
    });
}
