using System.Text;
using MixedMediaPrint.Core.Printing.Gdi;
using Xunit;

namespace MixedMediaPrint.Tests.Printing.Gdi;

public class CapabilityListParserTests
{
    [Fact]
    public void Parse_Bins_ReadsWordIdsAndTrimsNames()
    {
        // Mirrors what DeviceCapabilities(DC_BINNAMES/DC_BINS) returns for two bins
        // named "Tray 1" and "Tray 2" with WORD (2-byte) ids 1 and 4 (bypass).
        const int nameLen = 24;
        byte[] namesBuffer = ConcatFixedWidthNames(nameLen, "Tray 1", "Tray 2");
        byte[] idsBuffer = ConcatInt16(1, 4);

        var result = CapabilityListParser.Parse(namesBuffer, idsBuffer, nameLen, idSizeBytes: 2);

        Assert.Equal(
            [new CapabilityOption(1, "Tray 1"), new CapabilityOption(4, "Tray 2")],
            result);
    }

    [Fact]
    public void Parse_MediaTypes_ReadsDwordIds()
    {
        const int nameLen = 64;
        byte[] namesBuffer = ConcatFixedWidthNames(nameLen, "Plain", "Tab Paper");
        byte[] idsBuffer = ConcatInt32(1, 275); // Tab Paper's real OEM id from SESSION.md

        var result = CapabilityListParser.Parse(namesBuffer, idsBuffer, nameLen, idSizeBytes: 4);

        Assert.Equal(
            [new CapabilityOption(1, "Plain"), new CapabilityOption(275, "Tab Paper")],
            result);
    }

    [Fact]
    public void Parse_MismatchedCounts_UsesTheSmallerCount()
    {
        const int nameLen = 24;
        byte[] namesBuffer = ConcatFixedWidthNames(nameLen, "Tray 1", "Tray 2", "Tray 3");
        byte[] idsBuffer = ConcatInt16(1, 2); // only 2 ids for 3 names

        var result = CapabilityListParser.Parse(namesBuffer, idsBuffer, nameLen, idSizeBytes: 2);

        Assert.Equal(2, result.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Parse_InvalidIdSize_Throws(int idSizeBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CapabilityListParser.Parse(ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, 24, idSizeBytes));
    }

    private static byte[] ConcatFixedWidthNames(int nameLengthChars, params string[] names)
    {
        int strideBytes = nameLengthChars * 2;
        var buffer = new byte[names.Length * strideBytes];
        for (int i = 0; i < names.Length; i++)
        {
            byte[] nameBytes = Encoding.Unicode.GetBytes(names[i]);
            Array.Copy(nameBytes, 0, buffer, i * strideBytes, nameBytes.Length);
            // Remaining bytes in the slot are already zero (null-padded), matching
            // how DeviceCapabilities null-pads fixed-width name buffers.
        }
        return buffer;
    }

    private static byte[] ConcatInt16(params short[] values)
    {
        var buffer = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
        {
            BitConverter.GetBytes(values[i]).CopyTo(buffer, i * 2);
        }
        return buffer;
    }

    private static byte[] ConcatInt32(params int[] values)
    {
        var buffer = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
        {
            BitConverter.GetBytes(values[i]).CopyTo(buffer, i * 4);
        }
        return buffer;
    }
}
