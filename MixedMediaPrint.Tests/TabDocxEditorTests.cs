using System.IO.Compression;
using System.Text;
using MixedMediaPrint.Core.TabTemplate;

namespace MixedMediaPrint.Tests;

// Exercises the real regex-based position shifting against a small fixture that reproduces
// the three redundant places Word stores an anchor's position (DrawingML posOffset, a:xfrm's
// a:off, legacy VML style margin-left/margin-top), plus the {{N}} merge tag appearing twice
// (Choice + Fallback), matching the real template's structure.
public class TabDocxEditorTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("mmp-tests-").FullName;

    private const string FixtureDocumentXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="w-ns" xmlns:wp="wp-ns" xmlns:a="a-ns" xmlns:mc="mc-ns" xmlns:v="v-ns">
        <w:body>
        <w:p><w:r><mc:AlternateContent>
        <mc:Choice Requires="wps">
        <w:drawing><wp:anchor>
        <wp:positionH relativeFrom="page"><wp:posOffset>1000</wp:posOffset></wp:positionH>
        <wp:positionV relativeFrom="page"><wp:posOffset>2000</wp:posOffset></wp:positionV>
        <a:xfrm><a:off x="1000" y="2000"/><a:ext cx="100" cy="100"/></a:xfrm>
        <w:txbxContent><w:p><w:r><w:t>{{7}}</w:t></w:r></w:p></w:txbxContent>
        </wp:anchor></w:drawing>
        </mc:Choice>
        <mc:Fallback>
        <w:pict><v:shape style="position:absolute;margin-left:10.5pt;margin-top:20.25pt;width:10pt;height:10pt">
        <v:textbox><w:txbxContent><w:p><w:r><w:t>{{7}}</w:t></w:r></w:p></w:txbxContent></v:textbox>
        </v:shape></w:pict>
        </mc:Fallback>
        </mc:AlternateContent></w:r></w:p>
        <w:sectPr><w:pgSz w:w="12240" w:h="15840"/></w:sectPr>
        </w:body>
        </w:document>
        """;

    private string CreateFixtureTemplate()
    {
        string path = Path.Combine(_tempDir, "template.docx");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var stream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(FixtureDocumentXml);
            stream.Write(bytes, 0, bytes.Length);
        }
        return path;
    }

    [Fact]
    public void Edit_ShiftsAllThreeRedundantPositionEncodingsByTheNudge()
    {
        string template = CreateFixtureTemplate();
        string output = Path.Combine(_tempDir, "tab7-formixed.docx");

        var result = TabDocxEditor.Edit(template, tabNumber: 7, text: null, nudgeXIn: 0.1, nudgeYIn: -0.2, output);

        Assert.Equal(2, result.TagOccurrencesReplaced);
        Assert.Equal("7", result.DisplayText);

        string outXml = TabDocxEditor.ReadEntry(output, "word/document.xml");
        Assert.Contains("<wp:posOffset>92440</wp:posOffset>", outXml);   // 1000 + round(0.1in * 914400 emu/in)
        Assert.Contains("<wp:posOffset>-180880</wp:posOffset>", outXml); // 2000 + round(-0.2in * 914400 emu/in)
        Assert.Contains("<a:off x=\"92440\" y=\"-180880\"/>", outXml);
        Assert.Contains("margin-left:17.70pt", outXml);                  // 10.5 + 0.1in * 72pt/in
        Assert.Contains("margin-top:5.85pt", outXml);                    // 20.25 + (-0.2in * 72pt/in)
        Assert.DoesNotContain("{{7}}", outXml);
    }

    [Fact]
    public void Edit_WithCustomText_ReplacesBothTagOccurrencesWithIt()
    {
        string template = CreateFixtureTemplate();
        string output = Path.Combine(_tempDir, "tab7-email.docx");

        var result = TabDocxEditor.Edit(template, tabNumber: 7, text: "EMAIL CORRESPONDENCE", nudgeXIn: 0, nudgeYIn: 0, output);

        Assert.Equal("EMAIL CORRESPONDENCE", result.DisplayText);
        string outXml = TabDocxEditor.ReadEntry(output, "word/document.xml");
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(outXml, "EMAIL CORRESPONDENCE").Count);
    }

    [Fact]
    public void Edit_TabNumberWithNoMatchingAnchor_Throws()
    {
        string template = CreateFixtureTemplate();
        string output = Path.Combine(_tempDir, "tab99.docx");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            TabDocxEditor.Edit(template, tabNumber: 99, text: null, nudgeXIn: 0, nudgeYIn: 0, output));

        Assert.Contains("99", ex.Message);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort cleanup */ }
    }
}
