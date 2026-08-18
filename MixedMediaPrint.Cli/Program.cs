// Dev/hardware-checkpoint harness for MixedMediaPrint.Core -- there's no WPF UI to
// drive these checks until Phase 4, but every phase needs to run something real.
// Most commands here call winspool.drv/gdi32.dll and only work on Windows;
// `preview-tab` is the Tier-0, zero-P/Invoke exception and works on any OS.
using MixedMediaPrint.Core.Execution;
using MixedMediaPrint.Core.JobModel;
using MixedMediaPrint.Core.Printing.Diagnostics;
using MixedMediaPrint.Core.Printing.Gdi;
using MixedMediaPrint.Core.Rendering;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    switch (args[0])
    {
        case "list-bins":
            RequireArgCount(args, 2, "list-bins <printer>");
            return RequireWindows(() => PrintCapabilityOptions(PrinterCapabilities.GetBins(args[1])));

        case "list-media":
            RequireArgCount(args, 2, "list-media <printer>");
            return RequireWindows(() => PrintCapabilityOptions(PrinterCapabilities.GetMediaTypes(args[1])));

        case "device-info":
            RequireArgCount(args, 2, "device-info <printer>");
            return RequireWindows(() => PrintDeviceInfo(GdiDeviceInfoReader.Read(args[1])));

        case "selftest-tray":
            RequireArgCount(args, 4, "selftest-tray <printer> <trayABinId> <trayBBinId> [outputDir] [waitSeconds]");
            return RequireWindows(() => RunSelfTest(args));

        case "preview-tab":
            RequireArgCount(args, 4, "preview-tab <tabNumber> <labelText> <outputPngPath> [dpi]");
            RunPreviewTab(args);
            return 0;

        case "dryrun-job":
            RequireArgCount(args, 7,
                "dryrun-job <printer> <pdfPath> <tabTrayPattern> <bodyTrayPattern> <rotationDegrees> <outputFile> " +
                "[tabNumber=1] [nudgeXIn=0] [nudgeYIn=0] [flipX=false] [flipY=false] [tabLabel]");
            return RequireWindows(() => RunDryRunJob(args));

        default:
            Console.Error.WriteLine($"Unknown command '{args[0]}'.");
            PrintUsage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAILED: {ex.Message}");
    return 1;
}

static int RequireWindows(Action action)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("This command calls winspool.drv/gdi32.dll directly and only runs on Windows.");
        return 6;
    }
    action();
    return 0;
}

static void RequireArgCount(string[] args, int minCount, string usage)
{
    if (args.Length < minCount)
    {
        throw new ArgumentException($"Usage: mixedmediaprint-cli {usage}");
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
static void PrintCapabilityOptions(IReadOnlyList<CapabilityOption> options)
{
    if (options.Count == 0)
    {
        Console.WriteLine("(none reported)");
        return;
    }

    foreach (CapabilityOption option in options)
    {
        Console.WriteLine($"{option.Id}\t{option.Name}");
    }
}

static void PrintDeviceInfo(DeviceInfo info)
{
    Console.WriteLine($"DpiX/Y:             {info.DpiX} / {info.DpiY}");
    Console.WriteLine($"PhysicalWidth/Height: {info.PhysicalWidth} / {info.PhysicalHeight}");
    Console.WriteLine($"PhysicalOffsetX/Y:  {info.PhysicalOffsetX} / {info.PhysicalOffsetY}");
    Console.WriteLine($"HorzRes/VertRes:    {info.HorzRes} / {info.VertRes}");
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
static void RunSelfTest(string[] args)
{
    string printer = args[1];
    short trayA = short.Parse(args[2]);
    short trayB = short.Parse(args[3]);
    string outputDir = args.Length > 4 ? args[4] : Path.Combine(Path.GetTempPath(), "mixedmediaprint-selftest");
    int waitSeconds = args.Length > 5 ? int.Parse(args[5]) : 30;

    Console.WriteLine($"Running per-page tray self-test on '{printer}' (Tray A id={trayA}, Tray B id={trayB})...");
    Console.WriteLine($"Output: {outputDir}");

    PerPageTraySelfTestResult result = PerPageTraySelfTest.Run(printer, trayA, trayB, outputDir, waitSeconds);

    Console.WriteLine();
    Console.WriteLine("== RESULT ==");
    Console.WriteLine($"  body lengths: allA={result.AllTrayABodyLength}  allB={result.AllTrayBBodyLength}  mixed={result.MixedBodyLength}");
    Console.WriteLine($"  bytes differing mixed vs allA: {result.DiffCountMixedVsA}   mixed vs allB: {result.DiffCountMixedVsB}");
    Console.WriteLine();
    Console.WriteLine(result.Verdict switch
    {
        PerPageTrayVerdict.TrayNotApplied =>
            "VERDICT: even all-A vs all-B are identical -> tray not applied. Unexpected regression; check the bin ids and printer name.",
        PerPageTrayVerdict.CollapsedToOneTray =>
            "VERDICT: mixed == a single-tray job -> the driver COLLAPSED per-page tray to one tray. Per-page tray is NOT honored.",
        PerPageTrayVerdict.PerPageTrayWorks =>
            "VERDICT: mixed differs from BOTH single-tray renders -> PER-PAGE TRAY WORKS via GDI.",
        _ => throw new InvalidOperationException("Unreachable."),
    });
}

static void RunPreviewTab(string[] args)
{
    int tabNumber = int.Parse(args[1]);
    string labelText = args[2];
    string outputPath = args[3];
    int dpi = args.Length > 4 ? int.Parse(args[4]) : 600;

    // A representative 8.5x11in device with a plausible hardware margin — not any
    // specific real printer. Good enough to sanity-check geometry/fit/rotation
    // without needing the Windows workstation; not a substitute for the real
    // per-printer DeviceInfo once one's available.
    const double pageWidthIn = 8.5;
    const double pageHeightIn = 11.0;
    const double marginIn = 0.1667;
    int offset = (int)Math.Round(marginIn * dpi);
    int physicalWidth = (int)Math.Round(pageWidthIn * dpi);
    int physicalHeight = (int)Math.Round(pageHeightIn * dpi);
    var device = new DeviceInfo(
        DpiX: dpi, DpiY: dpi,
        PhysicalWidth: physicalWidth, PhysicalHeight: physicalHeight,
        PhysicalOffsetX: offset, PhysicalOffsetY: offset,
        HorzRes: physicalWidth - 2 * offset, VertRes: physicalHeight - 2 * offset);

    byte[] png = TabPreviewRenderer.RenderToPng(tabNumber, labelText, device);
    File.WriteAllBytes(outputPath, png);
    Console.WriteLine($"Wrote {outputPath} ({png.Length} bytes) — simulated {pageWidthIn}x{pageHeightIn}in @ {dpi}dpi, tab #{tabNumber} \"{labelText}\".");
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
static void RunDryRunJob(string[] args)
{
    string printer = args[1];
    string pdfPath = args[2];
    string tabTrayPattern = args[3];
    string bodyTrayPattern = args[4];
    float rotationDegrees = float.Parse(args[5]);
    string outputFile = args[6];
    int tabNumber = args.Length > 7 ? int.Parse(args[7]) : 1;
    double nudgeXIn = args.Length > 8 ? double.Parse(args[8]) : 0;
    double nudgeYIn = args.Length > 9 ? double.Parse(args[9]) : 0;
    bool flipX = args.Length > 10 && bool.Parse(args[10]);
    bool flipY = args.Length > 11 && bool.Parse(args[11]);
    string? tabLabel = args.Length > 12 ? args[12] : null;

    using var pdfSource = PdfiumPdfPageSource.FromFile(pdfPath);
    int totalPages = pdfSource.PageCount;
    if (totalPages < 1)
    {
        throw new InvalidOperationException($"'{pdfPath}' has no pages.");
    }

    // Body page 1, then the tab, then any remaining body pages -- the same
    // body/TAB/body shape legacy-testkit/print-mixed-test.ps1 validated, scaled
    // to a real PDF instead of two synthetic placeholder paragraphs.
    var items = new List<PrintJobItem> { new BodyRangeItem(0, 1), new TabRunItem(tabNumber, LabelTextOverride: tabLabel) };
    if (totalPages > 1)
    {
        items.Add(new BodyRangeItem(1, totalPages - 1));
    }

    var plan = new PrintJobPlan(items);
    var options = new PrintEngine.Options(printer, tabTrayPattern, bodyTrayPattern, rotationDegrees, nudgeXIn, nudgeYIn, flipX, flipY);

    Console.WriteLine($"Dry-run job: {totalPages} body page(s) from '{pdfPath}', tab #{tabNumber} inserted after page 1 -> '{outputFile}'");
    PrintEngine.Run(plan, pdfSource, options, RunMode.DryRunToFile, outputFile);
    Console.WriteLine($"Wrote {outputFile}. Inspect it (e.g. a PCL/XPS viewer, or byte-compare against a known-good run) to confirm tray/position/rotation before ever using RunMode.Physical.");
}

static void PrintUsage()
{
    Console.WriteLine("""
        Usage: mixedmediaprint-cli <command> [args]

        Commands (Windows only -- call the real printer driver):
          list-bins <printer>
          list-media <printer>
          device-info <printer>
          selftest-tray <printer> <trayABinId> <trayBBinId> [outputDir] [waitSeconds]
          dryrun-job <printer> <pdfPath> <tabTrayPattern> <bodyTrayPattern> <rotationDegrees> <outputFile>
                     [tabNumber=1] [nudgeXIn=0] [nudgeYIn=0] [flipX=false] [flipY=false] [tabLabel]

        Commands (any OS -- Tier-0 simulation, no printer involved):
          preview-tab <tabNumber> <labelText> <outputPngPath> [dpi]
        """);
}
