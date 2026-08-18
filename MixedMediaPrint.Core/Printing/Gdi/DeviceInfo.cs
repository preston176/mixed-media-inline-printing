namespace MixedMediaPrint.Core.Printing.Gdi;

/// <summary>
/// A printer's DPI and imageable-area facts for its current default DEVMODE —
/// portable data holder, ported from legacy-testkit/GdiPrint.psm1's
/// Get-GdiDeviceInfo. Needed to convert template measurements (inches/EMU) into
/// this specific device's pixels, and to check whether a page position falls
/// inside the printable area before drawing there.
/// </summary>
public sealed record DeviceInfo(
    int DpiX,
    int DpiY,
    int PhysicalWidth,
    int PhysicalHeight,
    int PhysicalOffsetX,
    int PhysicalOffsetY,
    int HorzRes,
    int VertRes);
