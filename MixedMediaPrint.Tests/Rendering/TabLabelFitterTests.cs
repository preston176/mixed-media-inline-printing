using MixedMediaPrint.Core.Rendering;
using Xunit;

namespace MixedMediaPrint.Tests.Rendering;

public class TabLabelFitterTests
{
    /// <summary>Deterministic stand-in for a real font: width scales with text length and font size, height scales with font size only. Lets the shrink-loop logic be tested precisely, independent of any real font's actual metrics.</summary>
    private sealed class LinearTextMeasurer : ITextMeasurer
    {
        private const float WidthPerCharPerPx = 0.6f;
        private const float HeightPerPx = 1.2f;

        public TextExtent Measure(string text, string fontFamily, bool bold, float fontHeightPx) =>
            new(text.Length * fontHeightPx * WidthPerCharPerPx, fontHeightPx * HeightPerPx);
    }

    private readonly LinearTextMeasurer _measurer = new();

    [Fact]
    public void Fit_TextAlreadyFitsAtPreferredSize_ReturnsPreferredSizeUnchanged()
    {
        // "3" at 100px: width = 1 * 100 * 0.6 = 60, height = 100 * 1.2 = 120.
        // Give it a generously large box so it fits without any shrinking.
        var result = TabLabelFitter.Fit(_measurer, "3", "Arial", bold: true,
            preferredFontHeightPx: 100, maxLengthPx: 1000, maxThicknessPx: 1000);

        Assert.Equal(100, result.FontHeightPx);
        Assert.True(result.Fits);
    }

    [Fact]
    public void Fit_TextTooLong_ShrinksUntilItFitsTheLengthAxis()
    {
        // A long label that would overflow the "runs-along" axis at the preferred size.
        var result = TabLabelFitter.Fit(_measurer, "EMAIL CORRESPONDENCE", "Arial", bold: true,
            preferredFontHeightPx: 130, maxLengthPx: 400, maxThicknessPx: 1000);

        Assert.True(result.Fits);
        Assert.True(result.FontHeightPx < 130, "Font should have shrunk from the preferred size.");
        Assert.True(result.Measured.WidthPx <= 400);
    }

    [Fact]
    public void Fit_ThicknessAxisIsAlsoEnforced()
    {
        // Short text that fits the length axis easily but whose height (driven purely
        // by font size in this fake measurer) overflows a tight thickness constraint.
        var result = TabLabelFitter.Fit(_measurer, "3", "Arial", bold: true,
            preferredFontHeightPx: 130, maxLengthPx: 1000, maxThicknessPx: 50);

        Assert.True(result.Fits);
        Assert.True(result.Measured.HeightPx <= 50);
    }

    [Fact]
    public void Fit_NeverFitsEvenAtMinimumFontSize_ReturnsFitsFalseAtTheFloor()
    {
        var result = TabLabelFitter.Fit(_measurer, "A WILDLY TOO LONG LABEL FOR THIS TINY BOX", "Arial", bold: true,
            preferredFontHeightPx: 130, maxLengthPx: 1, maxThicknessPx: 1000);

        Assert.False(result.Fits);
        Assert.Equal(TabLabelFitter.MinFontHeightPx, result.FontHeightPx);
    }

    [Fact]
    public void Fit_ShrinkingNeverGoesBelowTheMinimumFontHeight()
    {
        var result = TabLabelFitter.Fit(_measurer, "SOMETHING LONG", "Arial", bold: true,
            preferredFontHeightPx: 130, maxLengthPx: 0.01f, maxThicknessPx: 0.01f);

        Assert.True(result.FontHeightPx >= TabLabelFitter.MinFontHeightPx);
    }

    // --- Smoke tests against the real SkiaTextMeasurer: not asserting exact pixel
    // values (font metrics are platform/font-availability dependent), just that
    // measurement behaves sanely and monotonically. ---

    [Fact]
    public void SkiaTextMeasurer_LargerFontHeight_ProducesLargerMeasuredWidth()
    {
        var measurer = new SkiaTextMeasurer();

        TextExtent small = measurer.Measure("EXHIBIT", "Arial", bold: true, fontHeightPx: 20);
        TextExtent large = measurer.Measure("EXHIBIT", "Arial", bold: true, fontHeightPx: 60);

        Assert.True(large.WidthPx > small.WidthPx);
        Assert.True(large.HeightPx > small.HeightPx);
    }

    [Fact]
    public void SkiaTextMeasurer_LongerText_ProducesLargerMeasuredWidthAtSameFontSize()
    {
        var measurer = new SkiaTextMeasurer();

        TextExtent shortText = measurer.Measure("3", "Arial", bold: true, fontHeightPx: 40);
        TextExtent longText = measurer.Measure("EMAIL CORRESPONDENCE", "Arial", bold: true, fontHeightPx: 40);

        Assert.True(longText.WidthPx > shortText.WidthPx);
    }

    [Fact]
    public void TabLabelFitter_WithRealMeasurer_ShrinksLongCustomLabelToFitTheTemplateBox()
    {
        // End-to-end sanity check with the real measurer and the real template box
        // dimensions, using the exact scenario that was hardware-tested untested
        // per legacy-testkit/testing-report.md's "Custom text sizing" gap.
        var measurer = new SkiaTextMeasurer();
        float boxLengthPx = MixedMediaPrint.Core.Rendering.TabGeometry.TemplateHEmu / (float)MixedMediaPrint.Core.Rendering.TabGeometry.EmuPerInch * 600; // ~2in @ 600dpi
        float boxThicknessPx = MixedMediaPrint.Core.Rendering.TabGeometry.TemplateWEmu / (float)MixedMediaPrint.Core.Rendering.TabGeometry.EmuPerInch * 600; // ~0.61in @ 600dpi
        int preferredFontHeightPx = MixedMediaPrint.Core.Rendering.TabGeometry.ComputeFontHeightPx(600);

        var result = TabLabelFitter.Fit(measurer, "EMAIL CORRESPONDENCE", "Arial", bold: true,
            preferredFontHeightPx, boxLengthPx, boxThicknessPx);

        Assert.True(result.Fits, $"Expected 'EMAIL CORRESPONDENCE' to fit by shrinking; got FontHeightPx={result.FontHeightPx}, Measured={result.Measured}");
    }
}
