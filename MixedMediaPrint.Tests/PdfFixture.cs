using SkiaSharp;

namespace MixedMediaPrint.Tests;

// Generates a tiny, valid, blank-page PDF at test time via SkiaSharp's own PDF writer,
// rather than committing a hand-built binary fixture with hand-computed xref offsets.
internal static class PdfFixture
{
    public static byte[] CreateMultiPagePdf(int pageCount, float width = 200, float height = 300)
    {
        using var stream = new MemoryStream();
        using (var document = SKDocument.CreatePdf(stream))
        {
            for (int i = 0; i < pageCount; i++)
            {
                var canvas = document.BeginPage(width, height);
                canvas.Clear(SKColors.White);
                document.EndPage();
            }
        }
        return stream.ToArray();
    }
}
