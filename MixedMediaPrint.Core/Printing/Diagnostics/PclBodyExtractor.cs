using System.Text;

namespace MixedMediaPrint.Core.Printing.Diagnostics;

/// <summary>
/// Extracts the PCL-XL "body" bytes from a captured print-job file, skipping the
/// PJL header preamble (job name/timestamp lines that legitimately differ between
/// otherwise-identical jobs). Ported from legacy-testkit/capture-gdi-perpage.ps1's
/// Get-PxlBody. Pure byte manipulation — no OS dependency.
/// </summary>
public static class PclBodyExtractor
{
    private static readonly byte[] Marker = Encoding.Latin1.GetBytes("@PJL ENTER LANGUAGE");

    public static byte[] ExtractBody(ReadOnlySpan<byte> fileBytes)
    {
        int markerIndex = fileBytes.IndexOf((ReadOnlySpan<byte>)Marker);
        if (markerIndex < 0)
        {
            return fileBytes.ToArray();
        }

        ReadOnlySpan<byte> afterMarker = fileBytes[markerIndex..];
        int newlineOffset = afterMarker.IndexOf((byte)'\n');
        if (newlineOffset < 0)
        {
            return fileBytes.ToArray();
        }

        int bodyStart = markerIndex + newlineOffset + 1;
        return fileBytes[bodyStart..].ToArray();
    }
}
