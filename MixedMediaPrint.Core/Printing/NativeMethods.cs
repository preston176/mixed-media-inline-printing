using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MixedMediaPrint.Core.Printing;

// P/Invoke surface ported from legacy-testkit/GdiPrint.psm1's embedded C# (bins +
// device-info portion only). The proven print pipeline (legacy-testkit/print-mixed-test.ps1)
// never draws pixels via GDI or opens a print job through this layer -- it only uses these
// two read-only queries (enumerate bins, read DPI/imageable area) to resolve tray ids and
// compute the tab position nudge; the actual print goes through Word COM (see WordPrintJob).
[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    internal const short DC_BINS = 6;
    internal const short DC_BINNAMES = 12;

    internal const int LOGPIXELSX = 88;
    internal const int LOGPIXELSY = 90;
    internal const int HORZRES = 8;
    internal const int VERTRES = 10;
    internal const int PHYSICALWIDTH = 110;
    internal const int PHYSICALHEIGHT = 111;
    internal const int PHYSICALOFFSETX = 112;
    internal const int PHYSICALOFFSETY = 113;

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int DeviceCapabilities(string device, string? port, short capability, IntPtr output, IntPtr deviceMode);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateDCW")]
    internal static extern IntPtr CreateDC(string? driver, string device, string? port, IntPtr deviceMode);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int GetDeviceCaps(IntPtr hdc, int index);
}
