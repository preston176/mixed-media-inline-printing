using System.Runtime.InteropServices;
using MixedMediaPrint.Core.Printing.Gdi;
using Xunit;

namespace MixedMediaPrint.Tests.Printing.Gdi;

public class DevModeFieldWriterTests
{
    // The byte offsets legacy-testkit/GdiPrint.psm1 poked directly (OFF_DMFIELDS=72,
    // OFF_DEFAULTSOURCE=88, OFF_MEDIATYPE=196) — proven correct on real hardware.
    // The typed DevModeW struct must land fields at these exact same offsets, or the
    // port has silently changed hardware-proven behavior.
    private const int LegacyOffsetDmFields = 72;
    private const int LegacyOffsetDefaultSource = 88;
    private const int LegacyOffsetMediaType = 196;

    [Fact]
    public void StructLayout_MatchesLegacyByteOffsets()
    {
        Assert.Equal(LegacyOffsetDmFields, Marshal.OffsetOf<DevModeW>(nameof(DevModeW.dmFields)).ToInt32());
        Assert.Equal(LegacyOffsetDefaultSource, Marshal.OffsetOf<DevModeW>(nameof(DevModeW.dmDefaultSource)).ToInt32());
        Assert.Equal(LegacyOffsetMediaType, Marshal.OffsetOf<DevModeW>(nameof(DevModeW.dmMediaType)).ToInt32());
    }

    [Fact]
    public void SetBinAndMedia_SetsDefaultSourceAndFieldsBit()
    {
        byte[] baseDevMode = CreateBaseDevMode();

        byte[] result = DevModeFieldWriter.SetBinAndMedia(baseDevMode, binId: 3, mediaId: null);

        var devMode = ToStruct(result);
        Assert.Equal(3, devMode.dmDefaultSource);
        Assert.Equal(DevModeW.DM_DEFAULTSOURCE, devMode.dmFields & DevModeW.DM_DEFAULTSOURCE);
        Assert.Equal(0u, devMode.dmFields & DevModeW.DM_MEDIATYPE);
    }

    [Fact]
    public void SetBinAndMedia_SetsMediaTypeAndFieldsBit()
    {
        byte[] baseDevMode = CreateBaseDevMode();

        byte[] result = DevModeFieldWriter.SetBinAndMedia(baseDevMode, binId: null, mediaId: 275);

        var devMode = ToStruct(result);
        Assert.Equal(275u, devMode.dmMediaType);
        Assert.Equal(DevModeW.DM_MEDIATYPE, devMode.dmFields & DevModeW.DM_MEDIATYPE);
        Assert.Equal(0u, devMode.dmFields & DevModeW.DM_DEFAULTSOURCE);
    }

    [Fact]
    public void SetBinAndMedia_BothValues_SetsBothFieldsAndBits()
    {
        byte[] baseDevMode = CreateBaseDevMode();

        byte[] result = DevModeFieldWriter.SetBinAndMedia(baseDevMode, binId: 1, mediaId: 275);

        var devMode = ToStruct(result);
        Assert.Equal(1, devMode.dmDefaultSource);
        Assert.Equal(275u, devMode.dmMediaType);
        Assert.Equal(DevModeW.DM_DEFAULTSOURCE, devMode.dmFields & DevModeW.DM_DEFAULTSOURCE);
        Assert.Equal(DevModeW.DM_MEDIATYPE, devMode.dmFields & DevModeW.DM_MEDIATYPE);
    }

    [Fact]
    public void SetBinAndMedia_NullValues_LeavesFieldsBitsUnset()
    {
        byte[] baseDevMode = CreateBaseDevMode();

        byte[] result = DevModeFieldWriter.SetBinAndMedia(baseDevMode, binId: null, mediaId: null);

        var devMode = ToStruct(result);
        Assert.Equal(0u, devMode.dmFields & (DevModeW.DM_DEFAULTSOURCE | DevModeW.DM_MEDIATYPE));
    }

    [Fact]
    public void SetBinAndMedia_PreservesTrailingDriverPrivateBytes()
    {
        const int extraBytes = 40;
        byte[] baseDevMode = CreateBaseDevMode(extraBytes);
        int structSize = Marshal.SizeOf<DevModeW>();

        byte[] result = DevModeFieldWriter.SetBinAndMedia(baseDevMode, binId: 2, mediaId: 275);

        Assert.Equal(baseDevMode.Length, result.Length);
        for (int i = structSize; i < result.Length; i++)
        {
            Assert.Equal(0xAB, result[i]);
        }
    }

    [Fact]
    public void SetBinAndMedia_BufferTooSmall_Throws()
    {
        byte[] tooSmall = new byte[Marshal.SizeOf<DevModeW>() - 1];

        Assert.Throws<ArgumentException>(() => DevModeFieldWriter.SetBinAndMedia(tooSmall, 1, null));
    }

    private static byte[] CreateBaseDevMode(int extraBytes = 0)
    {
        int structSize = Marshal.SizeOf<DevModeW>();
        int size = structSize + extraBytes;

        var devMode = new DevModeW
        {
            dmDeviceName = "TestPrinter",
            dmFormName = "Letter",
            dmSize = (ushort)structSize,
            dmDriverExtra = (ushort)extraBytes,
        };

        var buffer = new byte[size];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            Marshal.StructureToPtr(devMode, handle.AddrOfPinnedObject(), false);
        }
        finally
        {
            handle.Free();
        }

        // Recognizable pattern standing in for driver-private data past the fixed struct.
        for (int i = structSize; i < size; i++)
        {
            buffer[i] = 0xAB;
        }

        return buffer;
    }

    private static DevModeW ToStruct(byte[] buffer)
    {
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<DevModeW>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }
}
