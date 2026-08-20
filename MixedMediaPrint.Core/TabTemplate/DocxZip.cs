using System.IO.Compression;
using System.Text;

namespace MixedMediaPrint.Core.TabTemplate;

// Shared zip-entry read/write for the docx-as-OPC-package edits TabDocxEditor and
// MixedDocxBuilder both do.
internal static class DocxZip
{
    public static string ReadEntryText(string zipPath, string entryName)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry(entryName) ?? throw new InvalidOperationException($"{entryName} not found in {zipPath}");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static bool TryReadEntryText(string zipPath, string entryName, out string content)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry(entryName);
        if (entry is null)
        {
            content = "";
            return false;
        }
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        content = reader.ReadToEnd();
        return true;
    }

    public static void WriteEntryText(string zipPath, string entryName, string content) =>
        WriteEntryBytes(zipPath, entryName, Encoding.UTF8.GetBytes(content));

    public static void WriteEntryBytes(string zipPath, string entryName, byte[] bytes)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        zip.GetEntry(entryName)?.Delete();
        var newEntry = zip.CreateEntry(entryName);
        using var stream = newEntry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }
}
