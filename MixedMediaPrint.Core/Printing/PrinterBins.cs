using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MixedMediaPrint.Core.Printing;

public readonly record struct PrinterBin(int Id, string Name);

// Port of Get-GdiBins / GdiProbe.Bins (legacy-testkit/GdiPrint.psm1).
[SupportedOSPlatform("windows")]
public static class PrinterBins
{
    private const int NameLength = 24;

    public static IReadOnlyList<PrinterBin> Get(string printer)
    {
        int count = NativeMethods.DeviceCapabilities(printer, null, NativeMethods.DC_BINNAMES, IntPtr.Zero, IntPtr.Zero);
        if (count <= 0) return Array.Empty<PrinterBin>();

        IntPtr namesBuf = Marshal.AllocHGlobal(count * NameLength * 2);
        IntPtr idsBuf = Marshal.AllocHGlobal(count * sizeof(short));
        try
        {
            int nameCount = NativeMethods.DeviceCapabilities(printer, null, NativeMethods.DC_BINNAMES, namesBuf, IntPtr.Zero);
            int idCount = NativeMethods.DeviceCapabilities(printer, null, NativeMethods.DC_BINS, idsBuf, IntPtr.Zero);
            int n = Math.Min(nameCount, idCount);

            var result = new List<PrinterBin>(n);
            for (int i = 0; i < n; i++)
            {
                string name = Marshal.PtrToStringUni(namesBuf + i * NameLength * 2, NameLength);
                int nul = name.IndexOf('\0');
                if (nul >= 0) name = name[..nul];
                short id = Marshal.ReadInt16(idsBuf, i * sizeof(short));
                result.Add(new PrinterBin(id, name.Trim()));
            }
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(namesBuf);
            Marshal.FreeHGlobal(idsBuf);
        }
    }
}
