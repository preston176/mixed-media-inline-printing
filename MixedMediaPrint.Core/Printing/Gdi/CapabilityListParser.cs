using System.Buffers.Binary;
using System.Text;

namespace MixedMediaPrint.Core.Printing.Gdi;

/// <summary>A single driver-reported capability option (an input bin or a media type), by its raw numeric id and display name.</summary>
public readonly record struct CapabilityOption(int Id, string Name);

/// <summary>
/// Pure parsing of the paired name/id buffers DeviceCapabilities returns (e.g.
/// DC_BINNAMES+DC_BINS, or DC_MEDIATYPENAMES+DC_MEDIATYPES) — ported from
/// legacy-testkit/GdiPrint.psm1's EnumPairs. Takes plain byte buffers rather than
/// native pointers so it has no OS dependency and can be unit tested anywhere.
/// </summary>
public static class CapabilityListParser
{
    public static IReadOnlyList<CapabilityOption> Parse(
        ReadOnlySpan<byte> namesBuffer, ReadOnlySpan<byte> idsBuffer, int nameLengthChars, int idSizeBytes)
    {
        if (idSizeBytes != 2 && idSizeBytes != 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(idSizeBytes), idSizeBytes, "Must be 2 (WORD bin ids) or 4 (DWORD media ids).");
        }

        int nameStrideBytes = nameLengthChars * 2; // UTF-16
        if (nameStrideBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nameLengthChars), nameLengthChars, "Must be positive.");
        }

        int countByNames = namesBuffer.Length / nameStrideBytes;
        int countByIds = idsBuffer.Length / idSizeBytes;
        int count = Math.Min(countByNames, countByIds);

        var results = new List<CapabilityOption>(count);
        for (int i = 0; i < count; i++)
        {
            var nameSlice = namesBuffer.Slice(i * nameStrideBytes, nameStrideBytes);
            string name = Encoding.Unicode.GetString(nameSlice);
            int nul = name.IndexOf('\0');
            if (nul >= 0)
            {
                name = name[..nul];
            }
            name = name.Trim();

            var idSlice = idsBuffer.Slice(i * idSizeBytes, idSizeBytes);
            int id = idSizeBytes == 2
                ? BinaryPrimitives.ReadInt16LittleEndian(idSlice)
                : BinaryPrimitives.ReadInt32LittleEndian(idSlice);

            results.Add(new CapabilityOption(id, name));
        }

        return results;
    }
}
