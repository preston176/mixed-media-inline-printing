namespace MixedMediaPrint.Core.Rendering;

/// <summary>
/// A PDF page source with no pages — for jobs that only print tabs (e.g. a
/// calibration test print), so PrintEngine's IPdfPageSource parameter can be
/// satisfied without a real PDF loaded.
/// </summary>
public sealed class EmptyPdfPageSource : IPdfPageSource
{
    public static readonly EmptyPdfPageSource Instance = new();

    private EmptyPdfPageSource()
    {
    }

    public int PageCount => 0;

    public byte[] RenderPageToPng(int pageIndex, int dpi) =>
        throw new InvalidOperationException(
            "EmptyPdfPageSource has no pages to render; this indicates a job plan referenced a body page despite no PDF being loaded.");

    public void Dispose()
    {
    }
}
