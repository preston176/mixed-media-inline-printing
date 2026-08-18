namespace MixedMediaPrint.Core.Rendering;

public readonly record struct TextExtent(float WidthPx, float HeightPx);

/// <summary>Measures unrotated text extent at a given font size — the input to tab-label auto-fit. Implementations: SkiaTextMeasurer (portable, preview) and GdiPlusTextMeasurer (Windows-only, print-time).</summary>
public interface ITextMeasurer
{
    TextExtent Measure(string text, string fontFamily, bool bold, float fontHeightPx);
}
