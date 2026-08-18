using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MixedMediaPrint.Core.Printing.Gdi;

// Ported from legacy-testkit/GdiPrint.psm1 (proven on hardware: per-page tray
// switching via DEVMODE + ResetDC survives on the Sharp BP-71C65/BP-70C65 PCL6
// drivers, where PrintTicket/XPS submission collapses per-page settings to one
// value for the whole job). Signatures and constants are unchanged from that
// script; only the DEVMODE field pokes move to the typed struct in DevModeW.cs.
[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    public const int DM_OUT_BUFFER = 2;
    public const int DM_IN_BUFFER = 8;

    public const short DC_BINS = 6;
    public const short DC_BINNAMES = 12;
    public const short DC_MEDIATYPENAMES = 34;
    public const short DC_MEDIATYPES = 35;

    public const int LOGPIXELSX = 88;
    public const int LOGPIXELSY = 90;
    public const int HORZRES = 8;
    public const int VERTRES = 10;
    public const int PHYSICALWIDTH = 110;
    public const int PHYSICALHEIGHT = 111;
    public const int PHYSICALOFFSETX = 112;
    public const int PHYSICALOFFSETY = 113;

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int DocumentProperties(
        IntPtr hwnd, IntPtr hPrinter, string pDeviceName,
        IntPtr pDevModeOutput, IntPtr pDevModeInput, int fMode);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int DeviceCapabilities(
        string pDevice, string? pPort, short fwCapability, IntPtr pOutput, IntPtr pDevMode);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateDCW")]
    public static extern IntPtr CreateDC(string? pwszDriver, string pwszDevice, string? pszPort, IntPtr pdm);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ResetDCW")]
    public static extern IntPtr ResetDC(IntPtr hdc, IntPtr pdm);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DOCINFO
    {
        public int cbSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszDocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszOutput;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszDatatype;
        public int fwType;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "StartDocW")]
    public static extern int StartDoc(IntPtr hdc, ref DOCINFO lpdi);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int StartPage(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int EndPage(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int EndDoc(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int AbortDoc(IntPtr hdc);
}
