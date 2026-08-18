namespace MixedMediaPrint.Core.Rendering;

/// <summary>
/// Abstraction over a PDF's pages, so job-model/print-engine code doesn't depend
/// on PDFtoImage/PDFium directly. PNG is the chosen hand-off format because both
/// GDI+ (print-time, via System.Drawing.Image.FromStream) and SkiaSharp (preview
/// thumbnails, via SKBitmap.Decode) can consume it natively without either side
/// needing to agree on a specific in-memory bitmap type.
/// </summary>
public interface IPdfPageSource : IDisposable
{
    int PageCount { get; }

    /// <param name="pageIndex">0-based page index.</param>
    /// <param name="dpi">Render resolution — should match the target device's DPI for print-time use.</param>
    byte[] RenderPageToPng(int pageIndex, int dpi);
}
