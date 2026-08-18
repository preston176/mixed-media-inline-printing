using MixedMediaPrint.Core.Printing.Gdi;
using MixedMediaPrint.Core.Rendering;
using Xunit;

namespace MixedMediaPrint.Tests.Rendering;

public class TabGeometryTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(5, 5)]
    [InlineData(6, 1)]
    [InlineData(10, 5)]
    [InlineData(11, 1)]
    [InlineData(500, 5)]
    [InlineData(501, 1)]
    public void GetCutPosition_CyclesEveryFiveTabs(int tabNumber, int expectedPosition)
    {
        Assert.Equal(expectedPosition, TabGeometry.GetCutPosition(tabNumber));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetCutPosition_BelowOne_Throws(int tabNumber)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TabGeometry.GetCutPosition(tabNumber));
    }

    [Theory]
    [InlineData(1, 412394)]
    [InlineData(2, 2174443)]
    [InlineData(3, 4155033)]
    [InlineData(4, 6071616)]
    [InlineData(5, 7697419)]
    [InlineData(6, 412394)] // cycles back to position 1
    public void GetTemplateYEmu_MatchesHardSourcedTemplateValues(int tabNumber, int expectedYEmu)
    {
        Assert.Equal(expectedYEmu, TabGeometry.GetTemplateYEmu(tabNumber));
    }

    [Theory]
    [InlineData(600, 117)] // round(14 * 600 / 72) = round(116.67)
    [InlineData(300, 58)]  // round(14 * 300 / 72) = round(58.33)
    public void ComputeFontHeightPx_ScalesWithDeviceDpi(int dpiY, int expectedPx)
    {
        Assert.Equal(expectedPx, TabGeometry.ComputeFontHeightPx(dpiY));
    }

    // DPI == EMU_PER_INCH makes the EMU->pixel conversion an identity (1 EMU = 1 "pixel"),
    // so with zero offsets and a huge imageable area (no margin correction triggered),
    // the resulting box is exactly the template's raw EMU values — the cleanest possible
    // hand-verifiable case for this math.
    private static DeviceInfo IdentityDpiDevice(int horzRes = 50_000_000, int vertRes = 50_000_000) => new(
        DpiX: TabGeometry.EmuPerInch, DpiY: TabGeometry.EmuPerInch,
        PhysicalWidth: horzRes, PhysicalHeight: vertRes,
        PhysicalOffsetX: 0, PhysicalOffsetY: 0,
        HorzRes: horzRes, VertRes: vertRes);

    [Fact]
    public void ComputeBox_NoNudgeNoFlipNoCorrection_MatchesTemplateEmuValuesDirectly()
    {
        var device = IdentityDpiDevice();

        var box = TabGeometry.ComputeBox(tabNumber: 1, device);

        Assert.Equal(TabGeometry.TemplateXEmu, box.X);
        Assert.Equal(TabGeometry.GetTemplateYEmu(1), box.Y);
        Assert.Equal(TabGeometry.TemplateWEmu, box.Width);
        Assert.Equal(TabGeometry.TemplateHEmu, box.Height);
    }

    [Fact]
    public void ComputeBox_PhysicalOffset_ShiftsBoxToImageableOrigin()
    {
        var device = IdentityDpiDevice() with { PhysicalOffsetX = 1000, PhysicalOffsetY = 2000 };

        var box = TabGeometry.ComputeBox(tabNumber: 1, device);

        Assert.Equal(TabGeometry.TemplateXEmu - 1000, box.X);
        Assert.Equal(TabGeometry.GetTemplateYEmu(1) - 2000, box.Y);
    }

    [Fact]
    public void ComputeBox_Nudge_ShiftsByExactNudgeAmountInDeviceUnits()
    {
        var device = IdentityDpiDevice();

        // Positive nudges only, so neither axis crosses into margin-correction range
        // (tab #1's raw Y, 412394, is small enough that a -1in nudge here would go
        // negative and trigger the correction tested separately below).
        var box = TabGeometry.ComputeBox(tabNumber: 1, device, nudgeXIn: 1, nudgeYIn: 1);

        // At DPI == EMU_PER_INCH, 1 inch of nudge == EMU_PER_INCH device units, exactly.
        Assert.Equal(TabGeometry.TemplateXEmu + TabGeometry.EmuPerInch, box.X);
        Assert.Equal(TabGeometry.GetTemplateYEmu(1) + TabGeometry.EmuPerInch, box.Y);
    }

    [Fact]
    public void ComputeBox_PositionOverflowsImageableArea_CorrectsBackInsideWithSafetyBuffer()
    {
        // HorzRes smaller than the template's raw X + width forces the "overflow past
        // the far edge" branch of the margin correction.
        int horzRes = TabGeometry.TemplateXEmu + TabGeometry.TemplateWEmu - 5000;
        var device = IdentityDpiDevice(horzRes: horzRes);

        var box = TabGeometry.ComputeBox(tabNumber: 1, device);

        // Per Get-Correction: corrected pos = limit - size - SAFETY_PX (SAFETY_PX = 20).
        Assert.Equal(horzRes - TabGeometry.TemplateWEmu - 20, box.X);
    }

    [Fact]
    public void ComputeBox_NudgeMakesPositionNegative_CorrectsToSafetyBuffer()
    {
        var device = IdentityDpiDevice();

        // Nudge far enough left that x goes negative before correction.
        var box = TabGeometry.ComputeBox(tabNumber: 1, device, nudgeXIn: -100);

        // Per Get-Correction: corrected pos = 0 + SAFETY_PX when the raw pos is negative.
        Assert.Equal(20, box.X);
    }

    [Fact]
    public void ComputeBox_FlipX_MirrorsAroundImageableWidth()
    {
        var device = IdentityDpiDevice();
        var unflipped = TabGeometry.ComputeBox(tabNumber: 1, device);

        var flipped = TabGeometry.ComputeBox(tabNumber: 1, device, flipX: true);

        Assert.Equal(device.HorzRes - unflipped.X - unflipped.Width, flipped.X);
        Assert.Equal(unflipped.Y, flipped.Y); // flipX must not touch Y
    }

    [Fact]
    public void ComputeBox_FlipY_MirrorsAroundImageableHeight()
    {
        var device = IdentityDpiDevice();
        var unflipped = TabGeometry.ComputeBox(tabNumber: 1, device);

        var flipped = TabGeometry.ComputeBox(tabNumber: 1, device, flipY: true);

        Assert.Equal(device.VertRes - unflipped.Y - unflipped.Height, flipped.Y);
        Assert.Equal(unflipped.X, flipped.X); // flipY must not touch X
    }
}
