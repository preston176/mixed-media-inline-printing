using PDFtoImage;
using SkiaSharp;

namespace MixedMediaPrint.Core.Pdf;

// Wraps PDFtoImage (pdfium + SkiaSharp) -- there is no way to turn an arbitrary PDF page
// into pixels without some PDF rendering engine, so this is Core's one external
// dependency. The same rendering call serves both the thumbnail grid and the
// full-resolution image embedded into the mixed docx at print time.
// PDFtoImage.Conversion is [SupportedOSPlatform]-attributed with an explicit platform
// list rather than a blanket "everywhere" -- but the list already covers every platform
// this app actually targets (Windows, plus macOS for local dev/tests), so CA1416 here is
// a false positive rather than a real platform-compatibility gap.
#pragma warning disable CA1416
public static class PdfPageRenderer
{
    public static int GetPageCount(byte[] pdfBytes) => Conversion.GetPageCount(pdfBytes);

    public static byte[] RenderThumbnailPng(byte[] pdfBytes, int pageIndex, int widthPx)
    {
        using var bitmap = Conversion.ToImage(pdfBytes, pageIndex, options: new RenderOptions(Width: widthPx, WithAspectRatio: true));
        return Encode(bitmap);
    }

    public static byte[] RenderFullPagePng(byte[] pdfBytes, int pageIndex, int dpi)
    {
        using var bitmap = Conversion.ToImage(pdfBytes, pageIndex, options: new RenderOptions(Dpi: dpi));
        return Encode(bitmap);
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
#pragma warning restore CA1416
