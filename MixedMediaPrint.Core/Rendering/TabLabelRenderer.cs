using System.Drawing;
using System.Runtime.Versioning;

namespace MixedMediaPrint.Core.Rendering;

/// <summary>
/// Print-time drawing of the tab box outline and the rotated, auto-fitted label,
/// via GDI+ on a Graphics from GdiPrintJob.RenderPage's callback. Geometry comes
/// from TabGeometry; sizing from TabLabelFitter + GdiPlusTextMeasurer, so what
/// prints matches what the Tier-0 (TabPreviewRenderer) and print-time paths both
/// compute — aside from the rasterizer itself.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TabLabelRenderer
{
    private const string FontFamily = "Arial";

    /// <param name="rotationDegrees">
    /// Must come from this printer's calibration profile, not a hardcoded default:
    /// the raw GDI escapement direction confirmed correct for a given device
    /// (see legacy-testkit's per-device escapement notes) is NOT guaranteed to
    /// carry the same sign into GDI+'s RotateTransform — see
    /// IMPLEMENTATION_PLAN.md risk R2. Re-verify on paper per printer.
    /// </param>
    /// <param name="drawBoxOutline">Draw the box border too — useful while calibrating; leave off for production labels.</param>
    public static TabLabelFitResult Draw(
        Graphics graphics, TabBoxPixels box, string labelText, int deviceDpiY, float rotationDegrees, bool drawBoxOutline = false)
    {
        var measurer = new GdiPlusTextMeasurer();
        int preferredFontHeightPx = TabGeometry.ComputeFontHeightPx(deviceDpiY);
        TabLabelFitResult fit = TabLabelFitter.Fit(
            measurer, labelText, FontFamily, bold: true,
            preferredFontHeightPx, maxLengthPx: box.Height, maxThicknessPx: box.Width);

        if (drawBoxOutline)
        {
            using var boxPen = new Pen(Color.Black, 2f);
            graphics.DrawRectangle(boxPen, box.X, box.Y, box.Width, box.Height);
        }

        using var font = new Font(FontFamily, fit.FontHeightPx, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.Black);
        using var format = new StringFormat(StringFormatFlags.NoClip)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        float centerX = box.X + box.Width / 2f;
        float centerY = box.Y + box.Height / 2f;

        var state = graphics.Save();
        try
        {
            graphics.TranslateTransform(centerX, centerY);
            graphics.RotateTransform(rotationDegrees);
            graphics.DrawString(labelText, font, brush, PointF.Empty, format);
        }
        finally
        {
            graphics.Restore(state);
        }

        return fit;
    }
}
