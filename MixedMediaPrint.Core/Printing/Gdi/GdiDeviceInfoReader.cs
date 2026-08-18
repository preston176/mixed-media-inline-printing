using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MixedMediaPrint.Core.Printing.Gdi;

/// <summary>Reads DeviceInfo for a printer via CreateDC + GetDeviceCaps + DeleteDC.</summary>
[SupportedOSPlatform("windows")]
public static class GdiDeviceInfoReader
{
    public static DeviceInfo Read(string printerName)
    {
        IntPtr hdc = NativeMethods.CreateDC(null, printerName, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"CreateDC failed for printer '{printerName}' (Win32 error {Marshal.GetLastWin32Error()}).");
        }

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
