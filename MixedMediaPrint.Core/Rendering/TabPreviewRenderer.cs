using MixedMediaPrint.Core.Printing.Gdi;
using SkiaSharp;

namespace MixedMediaPrint.Core.Rendering;

/// <summary>
/// Tier-0 preview: renders the computed tab box and rotated, fitted label onto a
/// page-sized bitmap, entirely in managed code (SkiaSharp) — no printer, no
/// P/Invoke, runs on macOS. Successor to legacy-testkit/simulate-tabpos.py,
/// sharing the real TabGeometry/TabLabelFitter code instead of a separate
/// hand-rolled PDF generator, so what this shows is what the print engine will
/// actually compute (aside from the rasterizer itself, which is not the same
/// renderer as GDI+ — see IMPLEMENTATION_PLAN.md risk R6).
/// </summary>
public static class TabPreviewRenderer
{
    private const string FontFamily = "Arial";

    // Skia's canvas, like GDI and every other screen/raster API, has y increasing
    // DOWNWARD -- so a positive RotateDegrees is visually clockwise, matching the
    // BP-71C65's confirmed-correct visual rotation (see legacy-testkit's escapement
    // notes). This is a REASONED default for this Skia-specific preview renderer,
    // not a hardware-confirmed value the way the raw GDI escapement is -- eyeball
    // a rendered sample before trusting it. It's also deliberately NOT the same
    // sign legacy-testkit/simulate-tabpos.py used (-90): that script draws in
    // PDF's y-UP coordinate space, which flips the sign needed for the same visual
    // result. Do not "fix" this back to match that script without accounting for
    // the coordinate-system difference.
    public const float DefaultRotationDegrees = 90f;

    public static SKBitmap Render(
        int tabNumber,
        string labelText,
        DeviceInfo device,
        double nudgeXIn = 0,
        double nudgeYIn = 0,
        bool flipX = false,
        bool flipY = false,
        float rotationDegrees = DefaultRotationDegrees,
        ITextMeasurer? measurer = null)
    {
        measurer ??= new SkiaTextMeasurer();

        TabBoxPixels box = TabGeometry.ComputeBox(tabNumber, device, nudgeXIn, nudgeYIn, flipX, flipY);
        int preferredFontHeightPx = TabGeometry.ComputeFontHeightPx(device.DpiY);
        TabLabelFitResult fit = TabLabelFitter.Fit(
            measurer, labelText, FontFamily, bold: true,
            preferredFontHeightPx, maxLengthPx: box.Height, maxThicknessPx: box.Width);

        var bitmap = new SKBitmap(device.PhysicalWidth, device.PhysicalHeight);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using (var pagePen = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true })
        {
            canvas.DrawRect(SKRect.Create(1, 1, device.PhysicalWidth - 2, device.PhysicalHeight - 2), pagePen);
        }

        using (var marginPen = new SKPaint
        {
            Color = SKColors.Gray,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash([6, 4], 0),
        })
        {
            canvas.DrawRect(
                SKRect.Create(device.PhysicalOffsetX, device.PhysicalOffsetY, device.HorzRes, device.VertRes),
                marginPen);
        }

        // TabGeometry's box is relative to the imageable-area origin; shift back to
        // this canvas's physical-page coordinates to draw it against the page/margin.
        float boxPhysicalX = box.X + device.PhysicalOffsetX;
        float boxPhysicalY = box.Y + device.PhysicalOffsetY;

        using (var boxPen = new SKPaint
        {
            Color = fit.Fits ? SKColors.Green : SKColors.Red,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            IsAntialias = true,
        })
        {
            canvas.DrawRect(SKRect.Create(boxPhysicalX, boxPhysicalY, box.Width, box.Height), boxPen);
        }

        using var typeface = SKTypeface.FromFamilyName(FontFamily, SKFontStyle.Bold);
        using var font = new SKFont(typeface, fit.FontHeightPx);
        using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        float centerX = boxPhysicalX + box.Width / 2f;
        float centerY = boxPhysicalY + box.Height / 2f;
        SKFontMetrics metrics = font.Metrics;
        float baselineY = -(metrics.Ascent + metrics.Descent) / 2f; // vertically centers the glyphs' visual span on y=0

        canvas.Save();
        canvas.Translate(centerX, centerY);
        canvas.RotateDegrees(rotationDegrees);
        // SKTextAlign.Center at x=0 centers the text on the (already-translated) origin.
        canvas.DrawText(labelText, 0, baselineY, SKTextAlign.Center, font, textPaint);
        canvas.Restore();

        return bitmap;
    }

    public static byte[] RenderToPng(
        int tabNumber, string labelText, DeviceInfo device,
        double nudgeXIn = 0, double nudgeYIn = 0, bool flipX = false, bool flipY = false,
        float rotationDegrees = DefaultRotationDegrees)
    {
        using SKBitmap bitmap = Render(tabNumber, labelText, device, nudgeXIn, nudgeYIn, flipX, flipY, rotationDegrees);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}
