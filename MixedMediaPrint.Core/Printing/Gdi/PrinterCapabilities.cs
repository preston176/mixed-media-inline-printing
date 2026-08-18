using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MixedMediaPrint.Core.Printing.Gdi;

/// <summary>
/// Enumerates a printer's input bins (trays) and media types via Win32
/// DeviceCapabilities — ported from legacy-testkit/GdiPrint.psm1's
/// Get-GdiBins/Get-GdiMediaTypes. Fetches the raw name/id buffers, then hands
/// them to the portable CapabilityListParser.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PrinterCapabilities
{
    // Empirically the max bin/media name length DeviceCapabilities reports for
    // this driver family (matches legacy-testkit/GdiPrint.psm1's EnumPairs calls).
    private const int BinNameLengthChars = 24;
    private const int MediaTypeNameLengthChars = 64;
    private const int BinIdSizeBytes = 2;
    private const int MediaTypeIdSizeBytes = 4;

    public static IReadOnlyList<CapabilityOption> GetBins(string printerName) =>
        EnumPairs(printerName, NativeMethods.DC_BINS, NativeMethods.DC_BINNAMES, BinNameLengthChars, BinIdSizeBytes);

    public static IReadOnlyList<CapabilityOption> GetMediaTypes(string printerName) =>
        EnumPairs(printerName, NativeMethods.DC_MEDIATYPES, NativeMethods.DC_MEDIATYPENAMES, MediaTypeNameLengthChars, MediaTypeIdSizeBytes);

    private static IReadOnlyList<CapabilityOption> EnumPairs(
        string printerName, short capIds, short capNames, int nameLengthChars, int idSizeBytes)
    {
        int count = NativeMethods.DeviceCapabilities(printerName, null, capNames, IntPtr.Zero, IntPtr.Zero);
        if (count <= 0)
        {
            return [];
        }

        int namesByteLen = count * nameLengthChars * 2;
        int idsByteLen = count * idSizeBytes;
        IntPtr namesBuf = Marshal.AllocHGlobal(namesByteLen);
        IntPtr idsBuf = Marshal.AllocHGlobal(idsByteLen);
        try
        {
            int actualNameCount = NativeMethods.DeviceCapabilities(printerName, null, capNames, namesBuf, IntPtr.Zero);
            int actualIdCount = NativeMethods.DeviceCapabilities(printerName, null, capIds, idsBuf, IntPtr.Zero);
            int n = Math.Min(actualNameCount, actualIdCount);
            if (n <= 0)
            {
                return [];
            }

            var namesBytes = new byte[n * nameLengthChars * 2];
            var idsBytes = new byte[n * idSizeBytes];
            Marshal.Copy(namesBuf, namesBytes, 0, namesBytes.Length);
            Marshal.Copy(idsBuf, idsBytes, 0, idsBytes.Length);

            return CapabilityListParser.Parse(namesBytes, idsBytes, nameLengthChars, idSizeBytes);
        }
        finally
        {
            Marshal.FreeHGlobal(namesBuf);
            Marshal.FreeHGlobal(idsBuf);
        }
    }
}
