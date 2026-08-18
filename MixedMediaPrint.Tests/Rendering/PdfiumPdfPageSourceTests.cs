using System.Runtime.Versioning;
using MixedMediaPrint.Core.Rendering;
using Xunit;

namespace MixedMediaPrint.Tests.Rendering;

/// <summary>
/// Exercises the real PDFium native library against a real PDF (a copy of
/// legacy-testkit/tabpos-preview.pdf) — this is genuinely runnable on macOS,
/// unlike the GDI printing layer, since PDFium ships native binaries for every
/// desktop OS. Not a fake/mock: if PDFium's native binary is missing or
/// mismatched for this platform, this test fails loudly instead of the gap
/// going unnoticed until Phase 4 on the actual Windows workstation.
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("linux")]
public class PdfiumPdfPageSourceTests
{
    private static string SamplePdfPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.pdf");

    [Fact]
    public void PageCount_RealOnePageFixture_ReportsOne()
    {
        using var source = PdfiumPdfPageSource.FromFile(SamplePdfPath);

        Assert.Equal(1, source.PageCount);
    }

    [Fact]
    public void RenderPageToPng_ProducesAValidPngAtRoughlyTheExpectedPixelSize()
    {
        using var source = PdfiumPdfPageSource.FromFile(SamplePdfPath);

        byte[] png = source.RenderPageToPng(pageIndex: 0, dpi: 150);

        // PNG signature: 89 50 4E 47 0D 0A 1A 0A.
        byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Assert.Equal(pngSignature, png[..8]);

        // The fixture is a US Letter page (8.5x11in); at 150dpi that's ~1275x1650px.
        // Read the IHDR chunk's width/height (big-endian, right after the signature
        // and the 4-byte length + "IHDR" tag) rather than pulling in an image
        // library just to assert this loosely.
        int width = ReadBigEndianInt32(png, 16);
        int height = ReadBigEndianInt32(png, 20);
        Assert.InRange(width, 1250, 1300);
        Assert.InRange(height, 1625, 1675);
    }

    [Fact]
    public void RenderPageToPng_HigherDpi_ProducesALargerImage()
    {
        using var source = PdfiumPdfPageSource.FromFile(SamplePdfPath);

        byte[] lowDpi = source.RenderPageToPng(pageIndex: 0, dpi: 72);
        byte[] highDpi = source.RenderPageToPng(pageIndex: 0, dpi: 300);

        Assert.True(highDpi.Length > lowDpi.Length);
    }

    private static int ReadBigEndianInt32(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
