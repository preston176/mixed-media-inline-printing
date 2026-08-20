using System.Text.RegularExpressions;
using MixedMediaPrint.Core.Printing;

namespace MixedMediaPrint.Core;

public sealed record ResolvedTrays(int TabTrayId, string TabTrayName, int BodyTrayId, string BodyTrayName);

// Port of the tray resolution in legacy-testkit/print-mixed-test.ps1 (Find-Id/Find-Name +
// the "same bin" / "no match" fail-closed checks). First regex match wins, same as the
// script -- no ambiguity detection beyond what it already did. Takes plain data, no
// P/Invoke, so it's testable without a printer.
public static class TrayResolver
{
    public static ResolvedTrays Resolve(IReadOnlyList<PrinterBin> bins, string tabTrayPattern, string bodyTrayPattern)
    {
        var tabMatch = FindFirst(bins, tabTrayPattern);
        var bodyMatch = FindFirst(bins, bodyTrayPattern);

        if (bodyMatch is null)
            throw new InvalidOperationException($"No bin matching '{bodyTrayPattern}' found in the bin list. Check the printer or the body tray pattern.");
        if (tabMatch is null)
            throw new InvalidOperationException($"No bin matching '{tabTrayPattern}' found in the bin list. Check the printer or the tab tray pattern.");
        if (tabMatch.Value.Id == bodyMatch.Value.Id)
            throw new InvalidOperationException($"Tab tray and body tray resolved to the same bin (id={tabMatch.Value.Id}, patterns '{tabTrayPattern}' / '{bodyTrayPattern}'). They must be different bins.");

        return new ResolvedTrays(tabMatch.Value.Id, tabMatch.Value.Name, bodyMatch.Value.Id, bodyMatch.Value.Name);
    }

    private static PrinterBin? FindFirst(IReadOnlyList<PrinterBin> bins, string pattern)
    {
        var regex = new Regex(pattern);
        foreach (var bin in bins)
        {
            if (regex.IsMatch(bin.Name)) return bin;
        }
        return null;
    }
}
