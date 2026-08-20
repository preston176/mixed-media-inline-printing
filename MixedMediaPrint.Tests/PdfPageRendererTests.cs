using MixedMediaPrint.Core.Pdf;

namespace MixedMediaPrint.Tests;

public class PdfPageRendererTests
{
    [Fact]
    public void GetPageCount_ReturnsTheNumberOfPagesInTheFixture()
    {
        byte[] pdf = PdfFixture.CreateMultiPagePdf(pageCount: 3);
        Assert.Equal(3, PdfPageRenderer.GetPageCount(pdf));
    }

    [Fact]
    public void RenderThumbnailPng_ProducesAPngScaledToTheRequestedWidth()
    {
        byte[] pdf = PdfFixture.CreateMultiPagePdf(pageCount: 1, width: 200, height: 400);
        byte[] png = PdfPageRenderer.RenderThumbnailPng(pdf, pageIndex: 0, widthPx: 80);

        using var bitmap = SkiaSharp.SKBitmap.Decode(png);
        Assert.Equal(80, bitmap.Width);
        Assert.Equal(160, bitmap.Height); // aspect-preserving: 400/200 * 80
    }

    [Fact]
    public void RenderFullPagePng_ProducesAPngAtTheRequestedDpi()
    {
        byte[] pdf = PdfFixture.CreateMultiPagePdf(pageCount: 1, width: 72, height: 144); // 1in x 2in at 72pt/in
        byte[] png = PdfPageRenderer.RenderFullPagePng(pdf, pageIndex: 0, dpi: 150);

        using var bitmap = SkiaSharp.SKBitmap.Decode(png);
        Assert.Equal(150, bitmap.Width);
        Assert.Equal(300, bitmap.Height);
    }
}
