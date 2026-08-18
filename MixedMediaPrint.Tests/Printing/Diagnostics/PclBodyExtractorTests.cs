using System.Text;
using MixedMediaPrint.Core.Printing.Diagnostics;
using Xunit;

namespace MixedMediaPrint.Tests.Printing.Diagnostics;

public class PclBodyExtractorTests
{
    [Fact]
    public void ExtractBody_SkipsPjlHeaderUpToAndIncludingTheMarkerLine()
    {
        byte[] header = Encoding.Latin1.GetBytes("@PJL JOB NAME=\"whatever\"\r\n@PJL ENTER LANGUAGE=PCLXL\n");
        byte[] body = [0x01, 0x02, 0x03, 0xFF, 0x00];
        byte[] file = [.. header, .. body];

        byte[] extracted = PclBodyExtractor.ExtractBody(file);

        Assert.Equal(body, extracted);
    }

    [Fact]
    public void ExtractBody_TwoFilesWithDifferentHeadersButSameBody_ExtractIdenticalBodies()
    {
        byte[] body = [0xAA, 0xBB, 0xCC];
        byte[] fileA = [.. Encoding.Latin1.GetBytes("@PJL JOB NAME=\"job-1\"\n@PJL ENTER LANGUAGE=PCLXL\n"), .. body];
        byte[] fileB = [.. Encoding.Latin1.GetBytes("@PJL JOB NAME=\"a-totally-different-name\"\n@PJL ENTER LANGUAGE=PCLXL\n"), .. body];

        Assert.Equal(PclBodyExtractor.ExtractBody(fileA), PclBodyExtractor.ExtractBody(fileB));
    }

    [Fact]
    public void ExtractBody_NoMarkerFound_ReturnsWholeFile()
    {
        byte[] file = Encoding.Latin1.GetBytes("no PJL marker in here at all");

        byte[] extracted = PclBodyExtractor.ExtractBody(file);

        Assert.Equal(file, extracted);
    }

    [Fact]
    public void ExtractBody_MarkerWithNoTrailingNewline_ReturnsWholeFile()
    {
        byte[] file = Encoding.Latin1.GetBytes("@PJL ENTER LANGUAGE=PCLXL");

        byte[] extracted = PclBodyExtractor.ExtractBody(file);

        Assert.Equal(file, extracted);
    }
}
