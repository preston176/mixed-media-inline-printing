using System.Text.RegularExpressions;
using MixedMediaPrint.Core.Pdf;

namespace MixedMediaPrint.Core.TabTemplate;

// Builds the print-ready docx: sections in the exact order the caller's sequence gives,
// one per PageSlot --
//   BodyPlaceholder -- today's yellow test label, used when no PDF is loaded
//   BodyPdfPage     -- a real PDF page, rendered full-page into the section
//   Tab             -- splices in the exact shifted anchor TabDocxEditor already produced
// Word COM (WordPrintJob) decides which physical tray each section pulls from at print
// time, by section index -- nothing about tray selection is baked into this XML.
public static class MixedDocxBuilder
{
    private const long EmuPerTwip = 635;
    private const int PrintDpi = 200;

    public sealed record Result(string OutputPath);

    public static Result Build(
        string tabDocxPath,
        string outputPath,
        IReadOnlyList<PageSlot> sequence,
        string bodyTrayName,
        int bodyTrayId,
        string? pdfPath)
    {
        if (sequence.Count(s => s.Kind == PageSlotKind.Tab) != 1)
            throw new InvalidOperationException("The page sequence must contain exactly one tab slot.");

        byte[]? pdfBytes = pdfPath is null ? null : File.ReadAllBytes(pdfPath);
        if (pdfBytes is null && sequence.Any(s => s.Kind == PageSlotKind.BodyPdfPage))
            throw new InvalidOperationException("The page sequence references PDF pages, but no PDF was provided.");

        string tabDocXml = DocxZip.ReadEntryText(tabDocxPath, "word/document.xml");

        Match rootOpen = Regex.Match(tabDocXml, "<w:document[^>]*>");
        Match sectPr = Regex.Match(tabDocXml, "<w:sectPr.*?</w:sectPr>|<w:sectPr[^/]*/>", RegexOptions.Singleline);
        Match tabAnchor = Regex.Match(tabDocXml, "<mc:AlternateContent>.*?</mc:AlternateContent>", RegexOptions.Singleline);

        if (!rootOpen.Success || !sectPr.Success || !tabAnchor.Success)
            throw new InvalidOperationException($"Could not extract the shifted tab anchor from {tabDocxPath}");

        File.Copy(tabDocxPath, outputPath, overwrite: true);

        var (pageCx, pageCy) = GetPageSizeEmu(sectPr.Value);
        var imagesToAdd = new List<(string EntryName, byte[] Bytes)>();
        var relationshipsToAdd = new List<(string Id, string FileName)>();

        var paragraphs = new List<string>(sequence.Count);
        for (int i = 0; i < sequence.Count; i++)
        {
            bool isLast = i == sequence.Count - 1;
            string inner = sequence[i].Kind switch
            {
                PageSlotKind.Tab => $"<w:r><w:rPr><w:noProof/></w:rPr>{tabAnchor.Value}</w:r>",
                PageSlotKind.BodyPdfPage => BuildImageRun(sequence[i], i, pdfBytes!, pageCx, pageCy, imagesToAdd, relationshipsToAdd),
                _ => BuildPlaceholderRun(bodyTrayName, bodyTrayId),
            };
            string pPr = isLast ? "" : $"<w:pPr>{sectPr.Value}</w:pPr>";
            paragraphs.Add($"<w:p>{pPr}{inner}</w:p>");
        }

        string newDocXml = rootOpen.Value + "<w:body>" + string.Concat(paragraphs) + sectPr.Value + "</w:body></w:document>";
        DocxZip.WriteEntryText(outputPath, "word/document.xml", newDocXml);

        if (imagesToAdd.Count > 0)
        {
            foreach (var (entryName, bytes) in imagesToAdd)
                DocxZip.WriteEntryBytes(outputPath, entryName, bytes);

            EnsurePngContentType(outputPath);
            AddImageRelationships(outputPath, relationshipsToAdd);
        }

        return new Result(outputPath);
    }

    private static string BuildPlaceholderRun(string bodyTrayName, int bodyTrayId)
    {
        string fullLabel = $"BODY -- expect {bodyTrayName} (resolved bin id: {bodyTrayId})";
        return $"<w:r><w:rPr><w:b/><w:sz w:val=\"96\"/><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"FFFF00\"/></w:rPr><w:t>{fullLabel}</w:t></w:r>";
    }

    private static string BuildImageRun(
        PageSlot slot,
        int slotIndex,
        byte[] pdfBytes,
        long pageCx,
        long pageCy,
        List<(string EntryName, byte[] Bytes)> imagesToAdd,
        List<(string Id, string FileName)> relationshipsToAdd)
    {
        int pageIndex = slot.PdfPageIndex ?? throw new InvalidOperationException("A BodyPdfPage slot is missing its page index.");
        byte[] png = PdfPageRenderer.RenderFullPagePng(pdfBytes, pageIndex, PrintDpi);

        string fileName = $"mmpBodyImg{slotIndex}.png";
        string relId = $"rIdMmpImg{slotIndex}";
        imagesToAdd.Add(($"word/media/{fileName}", png));
        relationshipsToAdd.Add((relId, fileName));

        int shapeId = 100000 + slotIndex;
        return $"""
            <w:r><w:drawing>
              <wp:inline xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" distT="0" distB="0" distL="0" distR="0">
                <wp:extent cx="{pageCx}" cy="{pageCy}"/>
                <wp:docPr id="{shapeId}" name="Picture {shapeId}"/>
                <a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                    <pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
                      <pic:nvPicPr>
                        <pic:cNvPr id="{shapeId}" name="Picture {shapeId}"/>
                        <pic:cNvPicPr/>
                      </pic:nvPicPr>
                      <pic:blipFill>
                        <a:blip xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" r:embed="{relId}"/>
                        <a:stretch><a:fillRect/></a:stretch>
                      </pic:blipFill>
                      <pic:spPr>
                        <a:xfrm><a:off x="0" y="0"/><a:ext cx="{pageCx}" cy="{pageCy}"/></a:xfrm>
                        <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                      </pic:spPr>
                    </pic:pic>
                  </a:graphicData>
                </a:graphic>
              </wp:inline>
            </w:drawing></w:r>
            """;
    }

    private static (long Cx, long Cy) GetPageSizeEmu(string sectPrXml)
    {
        Match pgSz = Regex.Match(sectPrXml, "<w:pgSz[^>]*/>");
        if (!pgSz.Success)
            throw new InvalidOperationException("Could not find <w:pgSz> in the template's sectPr.");
        long w = long.Parse(Regex.Match(pgSz.Value, "w:w=\"(\\d+)\"").Groups[1].Value);
        long h = long.Parse(Regex.Match(pgSz.Value, "w:h=\"(\\d+)\"").Groups[1].Value);
        return (w * EmuPerTwip, h * EmuPerTwip);
    }

    private static void EnsurePngContentType(string zipPath)
    {
        string xml = DocxZip.ReadEntryText(zipPath, "[Content_Types].xml");
        if (xml.Contains("Extension=\"png\"")) return;
        xml = Regex.Replace(xml, "(<Types[^>]*>)", "$1<Default Extension=\"png\" ContentType=\"image/png\"/>");
        DocxZip.WriteEntryText(zipPath, "[Content_Types].xml", xml);
    }

    private static void AddImageRelationships(string zipPath, List<(string Id, string FileName)> relationships)
    {
        const string relsEntry = "word/_rels/document.xml.rels";
        string xml = DocxZip.TryReadEntryText(zipPath, relsEntry, out var existing)
            ? existing
            : """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"></Relationships>""";

        string additions = string.Concat(relationships.Select(r =>
            $"<Relationship Id=\"{r.Id}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/{r.FileName}\"/>"));
        xml = xml.Replace("</Relationships>", additions + "</Relationships>");

        DocxZip.WriteEntryText(zipPath, relsEntry, xml);
    }
}
