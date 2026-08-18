using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MixedMediaPrint.Core.Printing.Gdi;

/// <summary>
/// Builds a per-page DEVMODE byte buffer for a printer with a chosen input bin
/// and/or media type merged in — ported from legacy-testkit/GdiPrint.psm1's
/// GetDevMode. The actual field mutation is delegated to the portable
/// DevModeFieldWriter; this class only owns the Windows-only OpenPrinter /
/// DocumentProperties round trip.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DevModeBuilder
{
    /// <param name="binId">Driver-reported input bin id (dmDefaultSource), or null to leave the tray at its current default.</param>
    /// <param name="mediaId">Driver-reported media type id (dmMediaType), or null to leave media at its current default.</param>
    public static byte[] Build(string printerName, short? binId, int? mediaId)
    {
        if (!NativeMethods.OpenPrinter(printerName, out IntPtr hPrinter, IntPtr.Zero))
        {
            throw new InvalidOperationException(
                $"OpenPrinter failed for '{printerName}' (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        try
        {
            int size = NativeMethods.DocumentProperties(IntPtr.Zero, hPrinter, printerName, IntPtr.Zero, IntPtr.Zero, 0);
            if (size <= 0)
            {
                throw new InvalidOperationException($"DocumentProperties(size query) failed for '{printerName}'.");
            }

            IntPtr baseDm = Marshal.AllocHGlobal(size);
            IntPtr mergedDm = Marshal.AllocHGlobal(size);
            try
            {
                if (NativeMethods.DocumentProperties(IntPtr.Zero, hPrinter, printerName, baseDm, IntPtr.Zero, NativeMethods.DM_OUT_BUFFER) < 0)
                {
                    throw new InvalidOperationException($"DocumentProperties(default) failed for '{printerName}'.");
                }

                var baseBytes = new byte[size];
                Marshal.Copy(baseDm, baseBytes, 0, size);

                byte[] modified = DevModeFieldWriter.SetBinAndMedia(baseBytes, binId, mediaId);
                Marshal.Copy(modified, 0, baseDm, size);

                if (NativeMethods.DocumentProperties(IntPtr.Zero, hPrinter, printerName, mergedDm, baseDm, NativeMethods.DM_IN_BUFFER | NativeMethods.DM_OUT_BUFFER) < 0)
                {
                    throw new InvalidOperationException($"DocumentProperties(merge) failed for '{printerName}'.");
                }

                var result = new byte[size];
                Marshal.Copy(mergedDm, result, 0, size);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(baseDm);
                Marshal.FreeHGlobal(mergedDm);
            }
        }
        finally
        {
            NativeMethods.ClosePrinter(hPrinter);
        }
    }
}
