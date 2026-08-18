using System.Runtime.Versioning;
using PDFtoImage;
using SkiaSharp;

namespace MixedMediaPrint.Core.Rendering;

/// <summary>
/// PDFium-backed (via the PDFtoImage package) implementation of IPdfPageSource.
/// Genuinely cross-platform — PDFium/PDFtoImage ship native binaries for
/// Windows/macOS/Linux, so this (unlike the GDI printing layer) is not
/// Windows-only and can be exercised for real on the dev machine. The platform
/// attributes below just mirror what PDFtoImage's own API already declares
/// support for (desktop OSes only — we don't target mobile/browser).
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("linux")]
public sealed class PdfiumPdfPageSource : IPdfPageSource
{
    private readonly byte[] _pdfBytes;
    private int? _pageCount;

    public PdfiumPdfPageSource(byte[] pdfBytes)
    {
        _pdfBytes = pdfBytes;
    }

    public static PdfiumPdfPageSource FromFile(string path) => new(File.ReadAllBytes(path));

    public int PageCount => _pageCount ??= Conversion.GetPageCount(_pdfBytes);

    public byte[] RenderPageToPng(int pageIndex, int dpi)
    {
        using SKBitmap bitmap = Conversion.ToImage(_pdfBytes, page: pageIndex, options: new RenderOptions(Dpi: dpi));
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    public void Dispose()
    {
        // Nothing unmanaged held directly (PDFtoImage opens/closes the document
        // internally per call); present for API symmetry and future-proofing if
        // a later implementation caches an open PdfDocument handle.
    }
}
