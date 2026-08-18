using System.Drawing;
using System.Runtime.Versioning;

namespace MixedMediaPrint.Core.Rendering;

/// <summary>Print-time text measurement backed by GDI+, matching what TabLabelRenderer actually draws with.</summary>
[SupportedOSPlatform("windows")]
public sealed class GdiPlusTextMeasurer : ITextMeasurer
{
    public TextExtent Measure(string text, string fontFamily, bool bold, float fontHeightPx)
    {
        using var font = new Font(fontFamily, fontHeightPx, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var bitmap = new Bitmap(1, 1);
        using Graphics graphics = Graphics.FromImage(bitmap);

        // GenericTypographic + a bare origin (no layout rectangle) gives a tight
        // measurement close to what's actually drawn, avoiding the extra internal
        // padding Graphics.MeasureString's default StringFormat adds.
        SizeF size = graphics.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
        return new TextExtent(size.Width, size.Height);
    }
}
