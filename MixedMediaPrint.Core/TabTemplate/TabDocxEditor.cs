using System.Globalization;
using System.Text.RegularExpressions;

namespace MixedMediaPrint.Core.TabTemplate;

// Port of legacy-testkit/edit-tab-docx.ps1: given the real 5th-cut-1-to-500.docx template,
// produce a single-page docx for ONE tab, its {{N}} merge tag replaced with custom text (or
// the bare number), and its position shifted by a nudge (inches) in all three places Word
// redundantly encodes the same position, so the file stays internally consistent:
//   1. wp:positionH/wp:positionV posOffset (EMU) -- the modern DrawingML anchor
//   2. a:xfrm's a:off x/y (EMU) -- kept in sync with #1 by Word internally
//   3. the legacy VML fallback's style="...margin-left:...pt;margin-top:...pt..."
// Pure zip/regex manipulation -- no P/Invoke, no Windows dependency.
public static class TabDocxEditor
{
    private const int EmuPerInch = TabGeometry.EmuPerInch;
    private const double PtPerInch = 72.0;

    public sealed record Result(string OutputPath, int TagOccurrencesReplaced, string DisplayText);

    public static Result Edit(string templatePath, int tabNumber, string? text, double nudgeXIn, double nudgeYIn, string outputPath)
    {
        if (!File.Exists(templatePath))
            throw new FileNotFoundException($"Template not found: {templatePath}", templatePath);

        int nudgeXEmu = (int)Math.Round(nudgeXIn * EmuPerInch);
        int nudgeYEmu = (int)Math.Round(nudgeYIn * EmuPerInch);
        double nudgeXPt = nudgeXIn * PtPerInch;
        double nudgeYPt = nudgeYIn * PtPerInch;

        string docXml = ReadEntry(templatePath, "word/document.xml");

        string tag = $"{{{{{tabNumber}}}}}";
        var blocks = Regex.Matches(docXml, "<mc:AlternateContent>.*?</mc:AlternateContent>", RegexOptions.Singleline);
        Match? target = null;
        foreach (Match b in blocks)
        {
            if (b.Value.Contains(tag)) { target = b; break; }
        }
        if (target is null)
            throw new InvalidOperationException($"Tab {tabNumber}'s AlternateContent block ({tag}) not found in the template.");

        string block = target.Value;
        block = ShiftPosOffset(block, "H", nudgeXEmu);
        block = ShiftPosOffset(block, "V", nudgeYEmu);
        block = ShiftOff(block, nudgeXEmu, nudgeYEmu);
        block = ShiftVmlStyle(block, nudgeXPt, nudgeYPt);

        string displayText = string.IsNullOrEmpty(text) ? tabNumber.ToString(CultureInfo.InvariantCulture) : text;
        int occurrences = Regex.Matches(block, Regex.Escape(tag)).Count;
        block = block.Replace(tag, displayText);

        Match rootOpenMatch = Regex.Match(docXml, "<w:document[^>]*>");
        if (!rootOpenMatch.Success)
            throw new InvalidOperationException("Could not find the <w:document> root element in the template.");
        Match sectPrMatch = Regex.Match(docXml, "<w:sectPr.*?</w:sectPr>|<w:sectPr[^/]*/>", RegexOptions.Singleline);
        if (!sectPrMatch.Success)
            throw new InvalidOperationException("Could not find a <w:sectPr> (page setup) in the template.");

        string newDocXml = rootOpenMatch.Value + "<w:body>" +
            "<w:p><w:r><w:rPr><w:noProof/></w:rPr>" + block + "</w:r></w:p>" +
            sectPrMatch.Value + "</w:body></w:document>";

        File.Copy(templatePath, outputPath, overwrite: true);
        WriteEntry(outputPath, "word/document.xml", newDocXml);

        return new Result(outputPath, occurrences, displayText);
    }

    private static string ShiftPosOffset(string text, string which, int delta)
    {
        string pattern = $"(<wp:position{which}[^>]*><wp:posOffset>)(-?\\d+)(</wp:posOffset>)";
        if (!Regex.IsMatch(text, pattern))
            throw new InvalidOperationException($"position{which} posOffset not found in the target anchor");
        return Regex.Replace(text, pattern, m =>
            m.Groups[1].Value + (int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) + delta).ToString(CultureInfo.InvariantCulture) + m.Groups[3].Value);
    }

    private static string ShiftOff(string text, int deltaX, int deltaY)
    {
        const string pattern = "(<a:off x=\")(-?\\d+)(\" y=\")(-?\\d+)(\")";
        if (!Regex.IsMatch(text, pattern))
            throw new InvalidOperationException("a:off x/y not found in the target anchor");
        return Regex.Replace(text, pattern, m =>
        {
            int x = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) + deltaX;
            int y = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) + deltaY;
            return $"{m.Groups[1].Value}{x.ToString(CultureInfo.InvariantCulture)}{m.Groups[3].Value}{y.ToString(CultureInfo.InvariantCulture)}{m.Groups[5].Value}";
        });
    }

    private static string ShiftMarginProp(string style, string prop, double delta)
    {
        string pattern = $"({Regex.Escape(prop)}:)(-?[\\d.]+)pt";
        return Regex.Replace(style, pattern, m =>
        {
            double newVal = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) + delta;
            return $"{m.Groups[1].Value}{newVal.ToString("F2", CultureInfo.InvariantCulture)}pt";
        });
    }

    private static string ShiftVmlStyle(string text, double deltaXPt, double deltaYPt)
    {
        var styleRegex = new Regex("style=\"([^\"]*)\"");
        if (!styleRegex.IsMatch(text))
            throw new InvalidOperationException("VML style attribute not found in the target anchor");
        // Only the first occurrence, matching edit-tab-docx.ps1's Shift-VmlStyle (count=1) --
        // the block has exactly one VML fallback to adjust.
        return styleRegex.Replace(text, m =>
        {
            string style = m.Groups[1].Value;
            style = ShiftMarginProp(style, "margin-left", deltaXPt);
            style = ShiftMarginProp(style, "margin-top", deltaYPt);
            return $"style=\"{style}\"";
        }, 1);
    }

    internal static string ReadEntry(string zipPath, string entryName) => DocxZip.ReadEntryText(zipPath, entryName);

    internal static void WriteEntry(string zipPath, string entryName, string content) => DocxZip.WriteEntryText(zipPath, entryName, content);
}
