using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MixedMediaPrint.Core;

// Port of the Word-COM section of legacy-testkit/print-mixed-test.ps1: late-bound COM
// automation (Type.GetTypeFromProgID, same mechanism as PowerShell's `New-Object -ComObject`)
// -- no Office PIA/interop assembly dependency. Opens the mixed docx invisibly, sets each
// section's paper tray, PrintOut()s Copies times, then closes/quits and releases the COM
// objects -- this is the ONLY thing in the app that actually prints; tray selection happens
// entirely through Word's own Section.PageSetup, not through any P/Invoke DEVMODE.
[SupportedOSPlatform("windows")]
public static class WordPrintJob
{
    public static void Print(string docxPath, string printer, IReadOnlyList<int> sectionTrayIds, int copies)
    {
        Type wordType = Type.GetTypeFromProgID("Word.Application")
            ?? throw new InvalidOperationException("Microsoft Word is not installed (Word.Application COM class not found).");

        dynamic word = Activator.CreateInstance(wordType)
            ?? throw new InvalidOperationException("Could not start Word.");
        word.Visible = false;
        word.DisplayAlerts = 0;
        try
        {
            dynamic doc = word.Documents.Open(docxPath, false, true);
            try
            {
                word.ActivePrinter = printer;

                int sectionCount = (int)doc.Sections.Count;
                for (int i = 0; i < sectionCount && i < sectionTrayIds.Count; i++)
                {
                    dynamic section = doc.Sections.Item(i + 1);
                    section.PageSetup.FirstPageTray = sectionTrayIds[i];
                    section.PageSetup.OtherPagesTray = sectionTrayIds[i];
                }

                for (int c = 0; c < copies; c++)
                {
                    doc.PrintOut();
                }
            }
            finally
            {
                doc.Close(false);
                Marshal.ReleaseComObject(doc);
            }
        }
        finally
        {
            word.Quit();
            Marshal.ReleaseComObject(word);
        }
    }
}
