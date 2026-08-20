using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MixedMediaPrint.Core.Printing;

public readonly record struct DeviceInfo(
    int DpiX,
    int DpiY,
    int PhysicalWidth,
    int PhysicalHeight,
    int PhysicalOffsetX,
    int PhysicalOffsetY,
    int HorzRes,
    int VertRes);

// Port of Get-GdiDeviceInfo / GdiProbe.DeviceInfo (legacy-testkit/GdiPrint.psm1). Opens a
// read-only device context (no print job) purely to read this printer's actual DPI and
// imageable area, needed to convert the tab template's EMU/inch measurements into this
// device's pixels.
[SupportedOSPlatform("windows")]
public static class PrinterDeviceInfo
{
    public static DeviceInfo Get(string printer)
    {
        IntPtr hdc = NativeMethods.CreateDC("WINSPOOL", printer, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero)
            throw new InvalidOperationException($"CreateDC failed for printer '{printer}' (err={Marshal.GetLastWin32Error()}).");
        try
        {
            return new DeviceInfo(
                DpiX: NativeMethods.GetDeviceCaps(hdc, NativeMethods.LOGPIXELSX),
                DpiY: NativeMethods.GetDeviceCaps(hdc, NativeMethods.LOGPIXELSY),
                PhysicalWidth: NativeMethods.GetDeviceCaps(hdc, NativeMethods.PHYSICALWIDTH),
                PhysicalHeight: NativeMethods.GetDeviceCaps(hdc, NativeMethods.PHYSICALHEIGHT),
                PhysicalOffsetX: NativeMethods.GetDeviceCaps(hdc, NativeMethods.PHYSICALOFFSETX),
                PhysicalOffsetY: NativeMethods.GetDeviceCaps(hdc, NativeMethods.PHYSICALOFFSETY),
                HorzRes: NativeMethods.GetDeviceCaps(hdc, NativeMethods.HORZRES),
                VertRes: NativeMethods.GetDeviceCaps(hdc, NativeMethods.VERTRES));
        }
        finally
        {
            NativeMethods.DeleteDC(hdc);
        }
    }
}
