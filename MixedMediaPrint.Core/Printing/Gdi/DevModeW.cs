using System.Runtime.InteropServices;

namespace MixedMediaPrint.Core.Printing.Gdi;

/// <summary>
/// Typed layout of the fixed portion of Win32's DEVMODEW struct. Replaces the
/// magic byte offsets used in legacy-testkit/GdiPrint.psm1 (dmFields=72,
/// dmDefaultSource=88, dmMediaType=196) with named fields, same layout.
/// The real buffer returned by DocumentProperties is usually larger than this
/// (driver-private data follows, per dmDriverExtra) — callers must preserve
/// those trailing bytes untouched; see DevModeFieldWriter.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct DevModeW
{
    public const uint DM_DEFAULTSOURCE = 0x00000200;
    public const uint DM_MEDIATYPE = 0x02000000;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string dmDeviceName;
    public ushort dmSpecVersion;
    public ushort dmDriverVersion;
    public ushort dmSize;
    public ushort dmDriverExtra;
    public uint dmFields;
    public short dmOrientation;
    public short dmPaperSize;
    public short dmPaperLength;
    public short dmPaperWidth;
    public short dmScale;
    public short dmCopies;
    public short dmDefaultSource;
    public short dmPrintQuality;
    public short dmColor;
    public short dmDuplex;
    public short dmYResolution;
    public short dmTTOption;
    public short dmCollate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string dmFormName;
    public ushort dmLogPixels;
    public uint dmBitsPerPel;
    public uint dmPelsWidth;
    public uint dmPelsHeight;
    public uint dmDisplayFlags;
    public uint dmDisplayFrequency;
    public uint dmICMMethod;
    public uint dmICMIntent;
    public uint dmMediaType;
    public uint dmDitherType;
    public uint dmReserved1;
    public uint dmReserved2;
    public uint dmPanningWidth;
    public uint dmPanningHeight;
}
