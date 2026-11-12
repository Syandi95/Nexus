using Nexus.PdfUtilities;
using Xunit;

namespace Nexus.PdfUtilities.Tests;

public class PdfPreviewSizingTests
{
    [Fact]
    public void IndexedRender_ReturnsSecondPage_RejectsInvalidIndex_AndCapsPixels()
    {
        var path=Path.Combine(Path.GetTempPath(),$"reader-pages-{Guid.NewGuid():N}.pdf");
        try
        {
            using(var doc=new PdfSharp.Pdf.PdfDocument())
            {
                var first=doc.AddPage();first.Width=PdfSharp.Drawing.XUnit.FromPoint(144);first.Height=PdfSharp.Drawing.XUnit.FromPoint(72);
                var second=doc.AddPage();second.Width=PdfSharp.Drawing.XUnit.FromPoint(72);second.Height=PdfSharp.Drawing.XUnit.FromPoint(144);
                doc.Save(path);
            }
            var engine=new PdfEngineService();
            var indexed=typeof(PdfEngineService).GetMethod("GetPageImage",new[]{typeof(string),typeof(int),typeof(int),typeof(int)});
            Assert.NotNull(indexed);
            var image=Assert.IsType<PdfPageImageResult>(indexed.Invoke(engine,new object[]{path,1,200,400}));
            Assert.InRange(image.Width,199,200);Assert.InRange(image.Height,399,400);
            Assert.Null(indexed.Invoke(engine,new object[]{path,-1,200,400}));
            Assert.Null(indexed.Invoke(engine,new object[]{path,2,200,400}));
            var bounded=Assert.IsType<PdfPageImageResult>(indexed.Invoke(engine,new object[]{path,1,20000,20000}));
            Assert.True((long)bounded.Width*bounded.Height<=4_000_000);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(720,360)]
    [InlineData(100,50)]
    public void Preview_RasterizesToRequestedPixelsWithoutChangingAspectRatio(int width,int height)
    {
        var path=Path.Combine(Path.GetTempPath(),$"preview-size-{Guid.NewGuid():N}.pdf");
        try
        {
            using(var doc=new PdfSharp.Pdf.PdfDocument())
            {
                var page=doc.AddPage();page.Width=PdfSharp.Drawing.XUnit.FromPoint(144);page.Height=PdfSharp.Drawing.XUnit.FromPoint(72);
                doc.Save(path);
            }
            var image=new PdfEngineService().GetPageImage(path,width,height);
            Assert.NotNull(image);
            Assert.InRange(image.Width,width-1,width);
            Assert.InRange(image.Height,height-1,height);
            Assert.Equal(image.Width*image.Height*4,image.Pixels.Length);
            Assert.Equal(0,image.Pixels[3]); // Presentation backing must not flatten PNG/export alpha.
        }
        finally{File.Delete(path);}
    }
}
