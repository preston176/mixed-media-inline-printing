using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using MixedMediaPrint.Core;
using MixedMediaPrint.Core.TabTemplate;

namespace MixedMediaPrint.Tests;

public class MixedDocxBuilderTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("mmp-mixed-tests-").FullName;

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
        <w:txbxContent><w:p><w:r><w:t>{{1}}</w:t></w:r></w:p></w:txbxContent>
        </wp:anchor></w:drawing>
        </mc:Choice>
        <mc:Fallback>
        <w:pict><v:shape style="position:absolute;margin-left:10.5pt;margin-top:20.25pt;width:10pt;height:10pt">
        <v:textbox><w:txbxContent><w:p><w:r><w:t>{{1}}</w:t></w:r></w:p></w:txbxContent></v:textbox>
        </v:shape></w:pict>
        </mc:Fallback>
        </mc:AlternateContent></w:r></w:p>
        <w:sectPr><w:pgSz w:w="12240" w:h="15840"/></w:sectPr>
        </w:body>
        </w:document>
        """;

    // A minimal but complete set of zip entries -- a real docx always has [Content_Types].xml
    // (required by the OPC spec); MixedDocxBuilder's image path reads it, so the fixture needs
    // it even though TabDocxEditor alone never touches it.
    private const string FixtureContentTypesXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
        <Default Extension="xml" ContentType="application/xml"/>
        <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
        </Types>
        """;

    private string CreateTabDocx()
    {
        string templatePath = Path.Combine(_tempDir, "template.docx");
        using (var zip = ZipFile.Open(templatePath, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "word/document.xml", FixtureDocumentXml);
            WriteEntry(zip, "[Content_Types].xml", FixtureContentTypesXml);
        }

        string tabDocxPath = Path.Combine(_tempDir, "tab1.docx");
        TabDocxEditor.Edit(templatePath, tabNumber: 1, text: null, nudgeXIn: 0, nudgeYIn: 0, tabDocxPath);
        return tabDocxPath;
    }

    private static void WriteEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    [Fact]
    public void Build_DefaultSequence_ProducesThreeSectionsWithNoImages()
    {
        string tabDocx = CreateTabDocx();
        string output = Path.Combine(_tempDir, "mixed-default.docx");
        PageSlot[] sequence =
        [
            new(PageSlotKind.BodyPlaceholder),
            new(PageSlotKind.Tab),
            new(PageSlotKind.BodyPlaceholder),
        ];

        MixedDocxBuilder.Build(tabDocx, output, sequence, "Tray 2", bodyTrayId: 2, pdfPath: null);

        string docXml = ReadEntry(output, "word/document.xml");
        // Not a raw <w:p> count: the tab anchor's own drawing content legitimately nests two
        // more <w:p>s (inside its DrawingML and VML text boxes), unrelated to section breaks.
        Assert.Equal(2, Regex.Matches(docXml, "BODY -- expect Tray 2").Count);
        Assert.Single(Regex.Matches(docXml, "<w:noProof/>"));
        // wp:inline (not wp:anchor -- the tab's own DrawingML anchor is a different element)
        // would mark an embedded PDF page picture; there should be none here.
        Assert.DoesNotContain("<wp:inline", docXml);
    }

    [Fact]
    public void Build_SequenceWithPdfPages_EmbedsOneImagePerSlotInOrder()
    {
        string tabDocx = CreateTabDocx();
        string pdfPath = Path.Combine(_tempDir, "body.pdf");
        File.WriteAllBytes(pdfPath, PdfFixture.CreateMultiPagePdf(pageCount: 2));
        string output = Path.Combine(_tempDir, "mixed-pdf.docx");
        PageSlot[] sequence =
        [
            new(PageSlotKind.BodyPdfPage, PdfPageIndex: 0),
            new(PageSlotKind.Tab),
            new(PageSlotKind.BodyPdfPage, PdfPageIndex: 1),
        ];

        MixedDocxBuilder.Build(tabDocx, output, sequence, "Tray 2", bodyTrayId: 2, pdfPath);

        string docXml = ReadEntry(output, "word/document.xml");
        // wp:inline (not wp:anchor -- the tab's own DrawingML anchor is a different element)
        // marks each embedded PDF page picture, one per BodyPdfPage slot.
        Assert.Equal(2, Regex.Matches(docXml, "<wp:inline").Count);
        Assert.Contains("rIdMmpImg0", docXml);
        Assert.Contains("rIdMmpImg2", docXml);

        using (var zip = ZipFile.OpenRead(output))
        {
            Assert.NotNull(zip.GetEntry("word/media/mmpBodyImg0.png"));
            Assert.NotNull(zip.GetEntry("word/media/mmpBodyImg2.png"));
        }

        Assert.Contains("Extension=\"png\"", ReadEntry(output, "[Content_Types].xml"));

        string rels = ReadEntry(output, "word/_rels/document.xml.rels");
        Assert.Contains("rIdMmpImg0", rels);
        Assert.Contains("rIdMmpImg2", rels);
    }

    [Fact]
    public void Build_NoTabSlot_Throws()
    {
        string tabDocx = CreateTabDocx();
        string output = Path.Combine(_tempDir, "mixed-no-tab.docx");
        PageSlot[] sequence = [new(PageSlotKind.BodyPlaceholder)];

        Assert.Throws<InvalidOperationException>(() =>
            MixedDocxBuilder.Build(tabDocx, output, sequence, "Tray 2", bodyTrayId: 2, pdfPath: null));
    }

    [Fact]
    public void Build_BodyPdfSlotWithNoPdfPath_Throws()
    {
        string tabDocx = CreateTabDocx();
        string output = Path.Combine(_tempDir, "mixed-missing-pdf.docx");
        PageSlot[] sequence =
        [
            new(PageSlotKind.BodyPdfPage, PdfPageIndex: 0),
            new(PageSlotKind.Tab),
        ];

        Assert.Throws<InvalidOperationException>(() =>
            MixedDocxBuilder.Build(tabDocx, output, sequence, "Tray 2", bodyTrayId: 2, pdfPath: null));
    }

    private static string ReadEntry(string zipPath, string entryName)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry(entryName) ?? throw new InvalidOperationException($"{entryName} missing from {zipPath}");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort cleanup */ }
    }
}
