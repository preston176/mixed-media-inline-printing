using System.Drawing;
using System.Runtime.Versioning;
using MixedMediaPrint.Core.JobModel;
using MixedMediaPrint.Core.Printing.Gdi;
using MixedMediaPrint.Core.Rendering;

namespace MixedMediaPrint.Core.Execution;

/// <summary>
/// Ties everything together: resolves trays from live capabilities, expands the
/// job plan into physical pages, and drives GdiPrintJob with the right DEVMODE
/// and drawing (PDF body pages via IPdfPageSource + GDI+, tab pages via
/// TabLabelRenderer) for each one, in order.
///
/// Calibration values (rotation, nudge, flip, tray patterns) are passed in
/// directly here — Phase 4 adds a stored PrinterCalibrationProfile in front of
/// this, this class doesn't know about persistence.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PrintEngine
{
    public sealed record Options(
        string PrinterName,
        string TabTrayPattern,
        string BodyTrayPattern,
        float RotationDegrees,
        double NudgeXIn = 0,
        double NudgeYIn = 0,
        bool FlipX = false,
        bool FlipY = false);

    /// <param name="outputFile">Required (and only meaningful) for RunMode.DryRunToFile.</param>
    public static void Run(PrintJobPlan plan, IPdfPageSource bodySource, Options options, RunMode mode, string? outputFile = null)
    {
        if (mode == RunMode.Preview)
        {
            throw new NotSupportedException($"{nameof(RunMode.Preview)} doesn't drive a real print job — use {nameof(TabPreviewRenderer)} directly.");
        }
        if (mode == RunMode.DryRunToFile && string.IsNullOrEmpty(outputFile))
        {
            throw new ArgumentException($"{nameof(RunMode.DryRunToFile)} requires {nameof(outputFile)}.", nameof(outputFile));
        }

        IReadOnlyList<CapabilityOption> bins = PrinterCapabilities.GetBins(options.PrinterName);
        CapabilityOption tabBin = TrayResolver.Resolve(bins, options.TabTrayPattern);
        CapabilityOption bodyBin = TrayResolver.Resolve(bins, options.BodyTrayPattern);
        if (tabBin.Id == bodyBin.Id)
        {
            throw new InvalidOperationException(
                $"Tab tray and body tray resolved to the same bin (id={tabBin.Id}, name='{tabBin.Name}'); they must be different.");
        }

        DeviceInfo device = GdiDeviceInfoReader.Read(options.PrinterName);
        IReadOnlyList<PageInstance> pages = JobExpander.Expand(plan);
        if (pages.Count == 0)
        {
            throw new InvalidOperationException("Job plan expanded to zero pages.");
        }

        short BinFor(PageInstance page) => (short)(page.Role == PageRole.Tab ? tabBin.Id : bodyBin.Id);

        string? effectiveOutputFile = mode == RunMode.DryRunToFile ? outputFile : null;
        byte[] firstPageDevMode = DevModeBuilder.Build(options.PrinterName, BinFor(pages[0]), mediaId: null);

        using GdiPrintJob job = GdiPrintJob.Start(options.PrinterName, firstPageDevMode, documentName: "MixedMediaPrint job", effectiveOutputFile);

        for (int i = 0; i < pages.Count; i++)
        {
            PageInstance page = pages[i];
            byte[]? devModeForThisPage = i == 0 ? null : DevModeBuilder.Build(options.PrinterName, BinFor(page), mediaId: null);

            job.RenderPage(devModeForThisPage, graphics => DrawPage(graphics, page, bodySource, device, options));
        }

        job.Complete();
    }

    private static void DrawPage(Graphics graphics, PageInstance page, IPdfPageSource bodySource, DeviceInfo device, Options options)
    {
        if (page.Role == PageRole.Body)
        {
            // Assumes the PDF's page size matches the paper physically loaded in
            // the body tray (v1 scope — see IMPLEMENTATION_PLAN.md risk R7). If
            // that ever stops holding, per-page dmPaperSize would need setting too.
            byte[] png = bodySource.RenderPageToPng(page.BodyPageIndex!.Value, device.DpiX);
            using var stream = new MemoryStream(png);
            using var image = Image.FromStream(stream);
            graphics.DrawImage(image, 0, 0, device.HorzRes, device.VertRes);
        }
        else
        {
            TabBoxPixels box = TabGeometry.ComputeBox(
                page.TabNumber!.Value, device, options.NudgeXIn, options.NudgeYIn, options.FlipX, options.FlipY);
            TabLabelRenderer.Draw(graphics, box, page.LabelText!, device.DpiY, options.RotationDegrees);
        }
    }
}
