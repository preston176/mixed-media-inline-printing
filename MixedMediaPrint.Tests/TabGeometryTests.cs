using MixedMediaPrint.Core.Printing;
using MixedMediaPrint.Core.TabTemplate;

namespace MixedMediaPrint.Tests;

// Expected numbers for the correction/flip cases were derived independently (Python,
// mirroring the same published algorithm) rather than by re-running this C# code, so a
// porting mistake in TabGeometry would actually be caught.
public class TabGeometryTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(6, 1)]
    [InlineData(10, 5)]
    [InlineData(500, 5)]
    public void PositionFor_CyclesEveryFivePositions(int tabNumber, int expectedPosition)
    {
        Assert.Equal(expectedPosition, TabGeometry.PositionFor(tabNumber));
    }

    [Fact]
    public void ComputeNudge_WithNoCorrectionOrFlip_PassesTheRequestedNudgeThroughUnchanged()
    {
        var info = new DeviceInfo(DpiX: 600, DpiY: 600, PhysicalWidth: 0, PhysicalHeight: 0,
            PhysicalOffsetX: 0, PhysicalOffsetY: 0, HorzRes: 100_000, VertRes: 100_000);

        var result = TabGeometry.ComputeNudge(tabNumber: 3, info, nudgeXIn: -0.625, nudgeYIn: 0.25, flipTabX: false, flipTabY: false);

        Assert.Equal(3, result.Position);
        Assert.Equal(-0.625, result.TotalNudgeXIn, precision: 6);
        Assert.Equal(0.25, result.TotalNudgeYIn, precision: 6);
    }

    [Fact]
    public void ComputeNudge_FlipTabY_MirrorsWithinTheImageableHeight()
    {
        var info = new DeviceInfo(DpiX: 600, DpiY: 600, PhysicalWidth: 0, PhysicalHeight: 0,
            PhysicalOffsetX: 0, PhysicalOffsetY: 0, HorzRes: 100_000, VertRes: 100_000);

        var result = TabGeometry.ComputeNudge(tabNumber: 3, info, nudgeXIn: 0, nudgeYIn: 0, flipTabX: false, flipTabY: true);

        Assert.Equal(0.0, result.TotalNudgeXIn, precision: 6);
        Assert.Equal(155.58, result.TotalNudgeYIn, precision: 6);
    }

    [Fact]
    public void ComputeNudge_OutOfBoundsX_AppliesTheSafetyMarginCorrection()
    {
        var info = new DeviceInfo(DpiX: 600, DpiY: 600, PhysicalWidth: 0, PhysicalHeight: 0,
            PhysicalOffsetX: 0, PhysicalOffsetY: 0, HorzRes: 4_000, VertRes: 100_000);

        var result = TabGeometry.ComputeNudge(tabNumber: 3, info, nudgeXIn: 0, nudgeYIn: 0, flipTabX: false, flipTabY: false);

        Assert.Equal(-1.81, result.TotalNudgeXIn, precision: 6);
        Assert.Equal(0.0, result.TotalNudgeYIn, precision: 6);
    }
}
