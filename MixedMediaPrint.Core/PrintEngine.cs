using System.Runtime.Versioning;
using MixedMediaPrint.Core.Printing;
using MixedMediaPrint.Core.TabTemplate;

namespace MixedMediaPrint.Core;

public sealed record TrayDiscovery(IReadOnlyList<PrinterBin> Bins, ResolvedTrays Resolved);

public sealed record PreparedJob(
    ResolvedTrays Trays,
    int Position,
    double TotalNudgeXIn,
    double TotalNudgeYIn,
    string TabDocxPath,
    string MixedDocxPath,
    string DisplayText,
    IReadOnlyList<PageSlot> Sequence);

// Orchestrates the full pipeline from legacy-testkit/print-mixed-test.ps1: resolve trays,
// compute the tab geometry nudge, edit the tab docx, assemble the 3-section mixed docx, and
// (only once the caller has shown its own confirmation, mirroring the script's typed-PRINT
// gate) print it via Word COM. Windows-only because every step here ultimately depends on a
// Windows-only piece.
[SupportedOSPlatform("windows")]
public sealed class PrintEngine
{
    private readonly string _templatePath;
    private readonly string _workDir;

    public event Action<string>? Log;

    public PrintEngine(string templatePath, string workDir)
    {
        _templatePath = templatePath;
        _workDir = workDir;
    }

    public TrayDiscovery DiscoverTrays(string printer, string tabTrayPattern, string bodyTrayPattern)
    {
        var bins = PrinterBins.Get(printer);
        Log?.Invoke($"Found {bins.Count} input bin(s) on '{printer}'.");
        foreach (var b in bins) Log?.Invoke($"  {b.Id}|{b.Name}");

        var resolved = TrayResolver.Resolve(bins, tabTrayPattern, bodyTrayPattern);
        Log?.Invoke($"Tab tray: '{resolved.TabTrayName}' (id={resolved.TabTrayId})   Body tray: '{resolved.BodyTrayName}' (id={resolved.BodyTrayId})");
        return new TrayDiscovery(bins, resolved);
    }

    public PreparedJob Prepare(PrintJobRequest request)
    {
        if (request.TabNumber < 1 || request.TabNumber > 500)
            throw new ArgumentOutOfRangeException(nameof(request), "TabNumber must be 1..500.");

        var discovery = DiscoverTrays(request.Printer, request.TabTrayPattern, request.BodyTrayPattern);

        var info = PrinterDeviceInfo.Get(request.Printer);
        var nudge = TabGeometry.ComputeNudge(request.TabNumber, info, request.NudgeXIn, request.NudgeYIn, request.FlipTabX, request.FlipTabY);
        Log?.Invoke($"Tab #{request.TabNumber} -> cut position {nudge.Position} of 5");
        Log?.Invoke($"Total shift to apply: x={nudge.TotalNudgeXIn:N4}in  y={nudge.TotalNudgeYIn:N4}in");

        Directory.CreateDirectory(_workDir);
        string tabDocxPath = Path.Combine(_workDir, $"tab{request.TabNumber}-formixed.docx");
        var editResult = TabDocxEditor.Edit(_templatePath, request.TabNumber, request.Text, nudge.TotalNudgeXIn, nudge.TotalNudgeYIn, tabDocxPath);
        Log?.Invoke($"Tab {request.TabNumber}: replaced {editResult.TagOccurrencesReplaced} tag occurrence(s) with '{editResult.DisplayText}'.");

        var sequence = request.EffectiveSequence;
        string mixedDocxPath = Path.Combine(_workDir, $"mixed-test-tab{request.TabNumber}.docx");
        MixedDocxBuilder.Build(tabDocxPath, mixedDocxPath, sequence, discovery.Resolved.BodyTrayName, discovery.Resolved.BodyTrayId, request.PdfPath);
        Log?.Invoke($"Wrote {mixedDocxPath} ({sequence.Count} section(s))");

        return new PreparedJob(discovery.Resolved, nudge.Position, nudge.TotalNudgeXIn, nudge.TotalNudgeYIn, tabDocxPath, mixedDocxPath, editResult.DisplayText, sequence);
    }

    public void Print(PrintJobRequest request, PreparedJob prepared)
    {
        var trayOrder = prepared.Sequence
            .Select(s => s.Kind == PageSlotKind.Tab ? prepared.Trays.TabTrayId : prepared.Trays.BodyTrayId)
            .ToArray();
        Log?.Invoke($"Printing via Word on '{request.Printer}', {request.Copies} set(s) ({trayOrder.Length} pages each)...");
        WordPrintJob.Print(prepared.MixedDocxPath, request.Printer, trayOrder, request.Copies);
        Log?.Invoke("Done.");
    }
}
