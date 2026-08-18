using System.Runtime.InteropServices;

namespace MixedMediaPrint.Core.Printing.Gdi;

/// <summary>
/// Pure byte-buffer mutation of a DEVMODE: sets dmDefaultSource (tray/bin) and/or
/// dmMediaType and their dmFields bits, leaving every other byte — including any
/// driver-private data trailing the fixed struct (per dmDriverExtra) — untouched.
/// No P/Invoke, no OS dependency: safe to unit test on any platform.
/// </summary>
public static class DevModeFieldWriter
{
    public static byte[] SetBinAndMedia(ReadOnlySpan<byte> baseDevMode, short? binId, int? mediaId)
    {
        int structSize = Marshal.SizeOf<DevModeW>();
        if (baseDevMode.Length < structSize)
        {
            throw new ArgumentException(
                $"DEVMODE buffer too small: {baseDevMode.Length} bytes, need at least {structSize}.",
                nameof(baseDevMode));
        }

        var buffer = baseDevMode.ToArray();
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var ptr = handle.AddrOfPinnedObject();
            var devMode = Marshal.PtrToStructure<DevModeW>(ptr);

            if (binId is { } bin)
            {
                devMode.dmFields |= DevModeW.DM_DEFAULTSOURCE;
                devMode.dmDefaultSource = bin;
            }

            if (mediaId is { } media)
            {
                devMode.dmFields |= DevModeW.DM_MEDIATYPE;
                devMode.dmMediaType = unchecked((uint)media);
            }

            // Only overwrites the first `structSize` bytes — any trailing
            // driver-private data in `buffer` beyond that is left as-is.
            Marshal.StructureToPtr(devMode, ptr, false);
            return buffer;
        }
        finally
        {
            handle.Free();
        }
    }
}
