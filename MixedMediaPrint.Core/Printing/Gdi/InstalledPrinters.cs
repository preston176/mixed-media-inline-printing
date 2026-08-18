using System.Drawing.Printing;
using System.Runtime.Versioning;

namespace MixedMediaPrint.Core.Printing.Gdi;

/// <summary>Lists installed Windows print queues, for a UI printer picker.</summary>
[SupportedOSPlatform("windows")]
public static class InstalledPrinters
{
    public static IReadOnlyList<string> List()
    {
        var names = new List<string>(PrinterSettings.InstalledPrinters.Count);
        foreach (string name in PrinterSettings.InstalledPrinters)
        {
            names.Add(name);
        }
        return names;
    }
}
