namespace MixedMediaPrint.Core.Rendering;

public sealed record TabLabelFitResult(float FontHeightPx, bool Fits, TextExtent Measured);

/// <summary>
/// Shrinks a tab label's font size until it fits the tab box, once rotated 90
/// degrees onto the physical tab. No such logic existed anywhere in the original
/// scripts — every label printed at a fixed 14pt regardless of length (see
/// legacy-testkit/testing-report.md's "Custom text sizing... not yet
/// stress-tested for fit" gap). This is new, and needs its own hardware
/// validation pass (see IMPLEMENTATION_PLAN.md risk R4).
///
/// Pure logic against the ITextMeasurer abstraction — no OS dependency.
/// </summary>
public static class TabLabelFitter
{
    public const float MinFontHeightPx = 6f;
    private const float ShrinkStepRatio = 0.92f;

    /// <param name="maxLengthPx">The axis the rotated text runs along — the tab box's long dimension (its height, pre-rotation).</param>
    /// <param name="maxThicknessPx">The axis that limits font size — the tab box's short dimension (its width, pre-rotation).</param>
    public static TabLabelFitResult Fit(
        ITextMeasurer measurer, string text, string fontFamily, bool bold,
        float preferredFontHeightPx, float maxLengthPx, float maxThicknessPx)
    {
        float fontHeightPx = preferredFontHeightPx;
        while (true)
        {
            TextExtent measured = measurer.Measure(text, fontFamily, bold, fontHeightPx);
            bool fits = measured.WidthPx <= maxLengthPx && measured.HeightPx <= maxThicknessPx;
            if (fits || fontHeightPx <= MinFontHeightPx)
            {
                return new TabLabelFitResult(fontHeightPx, fits, measured);
            }
            fontHeightPx = MathF.Max(MinFontHeightPx, fontHeightPx * ShrinkStepRatio);
        }
    }
}
