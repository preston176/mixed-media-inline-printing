using SkiaSharp;

namespace MixedMediaPrint.Core.Rendering;

/// <summary>Cross-platform text measurement backed by SkiaSharp — works on macOS, used by the Tier-0 preview renderer.</summary>
public sealed class SkiaTextMeasurer : ITextMeasurer
{
    public TextExtent Measure(string text, string fontFamily, bool bold, float fontHeightPx)
    {
        using SKTypeface typeface = SKTypeface.FromFamilyName(fontFamily, bold ? SKFontStyle.Bold : SKFontStyle.Normal);
        using var font = new SKFont(typeface, fontHeightPx);

        float width = font.MeasureText(text);
        SKFontMetrics metrics = font.Metrics;
        float height = metrics.Descent - metrics.Ascent; // Ascent is negative in Skia's convention.

        return new TextExtent(width, height);
    }
}
