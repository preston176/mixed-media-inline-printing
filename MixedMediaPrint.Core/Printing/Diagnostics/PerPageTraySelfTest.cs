using System.Drawing;
using System.Runtime.Versioning;
using MixedMediaPrint.Core.Printing.Gdi;

namespace MixedMediaPrint.Core.Printing.Diagnostics;

/// <summary>
/// THE per-page verdict, ported from legacy-testkit/capture-gdi-perpage.ps1. Renders
/// three 3-page jobs with IDENTICAL content, varying only the per-page tray
/// (dmDefaultSource) — all-TrayA, all-TrayB, and mixed (A,B,A) — all to files, never
/// touching the physical device. If mixed differs from BOTH single-tray renders, the
/// middle page genuinely got a different tray: per-page tray switching works. If
/// mixed matches one of them, the driver collapsed per-page tray to one tray.
///
/// This operationalizes the one-off tribal-knowledge verdict from Phase 1.5 into a
/// repeatable regression check — run it again any time the driver, hardware, or this
/// engine changes, rather than trusting that a past verdict still holds.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PerPageTraySelfTest
{
    private const int DefaultWaitSeconds = 30;
    private const int PollIntervalMs = 2000;

    /// <param name="printerName">Exact print queue name.</param>
    /// <param name="trayABinId">Driver-reported bin id for the first tray under test (e.g. resolved from a "Tray 1" pattern via <see cref="PrinterCapabilities.GetBins"/>).</param>
    /// <param name="trayBBinId">Driver-reported bin id for the second tray under test.</param>
    /// <param name="outputDirectory">Scratch directory for the three captured job files — created if missing.</param>
    /// <param name="waitSeconds">How long to wait for the spooler to finish writing each file before giving up.</param>
    public static PerPageTraySelfTestResult Run(
        string printerName, short trayABinId, short trayBBinId, string outputDirectory, int waitSeconds = DefaultWaitSeconds)
    {
        Directory.CreateDirectory(outputDirectory);
        string allAPath = Path.Combine(outputDirectory, "pp-allA.prn");
        string allBPath = Path.Combine(outputDirectory, "pp-allB.prn");
        string mixedPath = Path.Combine(outputDirectory, "pp-mixed.prn");
        string[] allPaths = [allAPath, allBPath, mixedPath];

        foreach (string path in allPaths)
        {
            File.Delete(path); // no-op if it doesn't exist
        }

        RenderThreeIdenticalPages(printerName, allAPath, [trayABinId, trayABinId, trayABinId]);
        RenderThreeIdenticalPages(printerName, allBPath, [trayBBinId, trayBBinId, trayBBinId]);
        RenderThreeIdenticalPages(printerName, mixedPath, [trayABinId, trayBBinId, trayABinId]);

        foreach (string path in allPaths)
        {
            WaitForNonEmptyFile(path, waitSeconds);
        }

        byte[] bodyAllA = PclBodyExtractor.ExtractBody(File.ReadAllBytes(allAPath));
        byte[] bodyAllB = PclBodyExtractor.ExtractBody(File.ReadAllBytes(allBPath));
        byte[] bodyMixed = PclBodyExtractor.ExtractBody(File.ReadAllBytes(mixedPath));

        return PerPageTrayVerdictEvaluator.Evaluate(bodyAllA, bodyAllB, bodyMixed);
    }

    private static void RenderThreeIdenticalPages(string printerName, string outputFile, short[] binIdsPerPage)
    {
        byte[] firstPageDevMode = DevModeBuilder.Build(printerName, binIdsPerPage[0], mediaId: null);
        using var job = GdiPrintJob.Start(printerName, firstPageDevMode, documentName: "mixedmediaprint-perpage-selftest", outputFile);

        job.RenderPage(null, DrawIdenticalTestContent);
        for (int i = 1; i < binIdsPerPage.Length; i++)
        {
            byte[] pageDevMode = DevModeBuilder.Build(printerName, binIdsPerPage[i], mediaId: null);
            job.RenderPage(pageDevMode, DrawIdenticalTestContent);
        }

        job.Complete();
    }

    private static void DrawIdenticalTestContent(Graphics graphics)
    {
        // Deterministic and content-identical on every page of every job in this test,
        // so the ONLY thing that can vary in the captured bytes is the tray under test.
        graphics.DrawRectangle(Pens.Black, 120, 120, 1880, 520);
    }

    private static void WaitForNonEmptyFile(string path, int waitSeconds)
    {
        // The spooler can still be writing the file after EndDoc returns (observed on
        // hardware, hence this poll — see legacy-testkit/capture-gdi-perpage.ps1).
        int elapsedMs = 0;
        while (elapsedMs < waitSeconds * 1000)
        {
            Thread.Sleep(PollIntervalMs);
            elapsedMs += PollIntervalMs;

            var info = new FileInfo(path);
            if (info.Exists && info.Length > 0)
            {
                return;
            }
        }

        throw new TimeoutException(
            $"No output appeared at '{path}' within {waitSeconds}s (the print spooler may still be rendering it).");
    }
}
