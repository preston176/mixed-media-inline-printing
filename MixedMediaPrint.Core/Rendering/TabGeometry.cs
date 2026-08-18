using MixedMediaPrint.Core.Printing.Gdi;

namespace MixedMediaPrint.Core.Rendering;

/// <summary>The tab label box, in this device's pixels, already offset-adjusted to the imageable-area origin and ready to hand to GDI/GDI+ drawing calls.</summary>
public readonly record struct TabBoxPixels(int X, int Y, int Width, int Height);

/// <summary>
/// The fixed 5-cut tab-stock template's geometry — hard-sourced from
/// 5th-cut-1-to-500.docx's OOXML (see legacy-testkit/capture-gdi-tabpos.ps1's
/// header comment), not guessed. v1 supports only this one template (per the
/// project's confirmed v1 scope); a different cut count or stock would need new
/// constants here, not a code change.
///
/// Pure geometry math — no OS dependency, unit-tested on macOS.
/// </summary>
public static class TabGeometry
{
    public const int EmuPerInch = 914400;
    public const int TemplateXEmu = 7162495;
    public const int TemplateWEmu = 557784;
    public const int TemplateHEmu = 1828800;
    public const int CutPositionCount = 5;
    public const double TemplateFontPointSize = 14.0;

    // Small buffer past the exact printable-area boundary; real-world rasterization
    // has some slop. Ported from capture-gdi-tabpos.ps1's SAFETY_PX.
    private const int SafetyPx = 20;

    private static readonly IReadOnlyDictionary<int, int> TemplateYEmuByPosition = new Dictionary<int, int>
    {
        [1] = 412394,
        [2] = 2174443,
        [3] = 4155033,
        [4] = 6071616,
        [5] = 7697419,
    };

    /// <summary>Tab N -> which of the 5 physical cut positions it lands on (1-based), cycling every 5 tabs.</summary>
    public static int GetCutPosition(int tabNumber)
    {
        if (tabNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tabNumber), tabNumber, "Tab numbers start at 1.");
        }
        return ((tabNumber - 1) % CutPositionCount) + 1;
    }

    public static int GetTemplateYEmu(int tabNumber) => TemplateYEmuByPosition[GetCutPosition(tabNumber)];

    /// <summary>Arial Bold's rendered height in pixels for this device's DPI, targeting the template's actual 14pt — a fixed pixel count would only look right at whatever DPI it was tuned against.</summary>
    public static int ComputeFontHeightPx(int deviceDpiY, double pointSize = TemplateFontPointSize) =>
        (int)Math.Round(pointSize * deviceDpiY / 72.0);

    /// <summary>
    /// The tab box for a given tab number, in this device's pixels, with nudge and
    /// flip applied. Ordering (offset -&gt; nudge -&gt; margin-correct -&gt; flip) matches
    /// legacy-testkit/print-mixed-test.ps1 — the version actually confirmed correct
    /// on real paper — not legacy-testkit/capture-gdi-tabpos.ps1's earlier ordering
    /// (offset -&gt; flip -&gt; nudge -&gt; margin-correct); the two scripts disagree and
    /// print-mixed-test.ps1 is the one with field evidence behind it.
    /// </summary>
    public static TabBoxPixels ComputeBox(
        int tabNumber,
        DeviceInfo device,
        double nudgeXIn = 0,
        double nudgeYIn = 0,
        bool flipX = false,
        bool flipY = false)
    {
        int yEmu = GetTemplateYEmu(tabNumber);

        double xIn = (double)TemplateXEmu / EmuPerInch;
        double yIn = (double)yEmu / EmuPerInch;
        double wIn = (double)TemplateWEmu / EmuPerInch;
        double hIn = (double)TemplateHEmu / EmuPerInch;

        int xPxPhysical = (int)Math.Round(xIn * device.DpiX);
        int yPxPhysical = (int)Math.Round(yIn * device.DpiY);
        int wPx = (int)Math.Round(wIn * device.DpiX);
        int hPx = (int)Math.Round(hIn * device.DpiY);

        // GDI draws relative to the imageable area's origin, not the physical page's.
        int xPxGdiBase = xPxPhysical - device.PhysicalOffsetX;
        int yPxGdiBase = yPxPhysical - device.PhysicalOffsetY;

        int x = xPxGdiBase + (int)Math.Round(nudgeXIn * device.DpiX);
        int y = yPxGdiBase + (int)Math.Round(nudgeYIn * device.DpiY);

        x += GetMarginCorrection(x, wPx, device.HorzRes);
        y += GetMarginCorrection(y, hPx, device.VertRes);

        // Applied AFTER margin correction, on whatever tray is actually resolved —
        // some trays (commonly Bypass vs. a cassette tray) register/feed the sheet
        // differently, which can mirror where content lands relative to the
        // physical tab.
        if (flipX)
        {
            x = device.HorzRes - x - wPx;
        }
        if (flipY)
        {
            y = device.VertRes - y - hPx;
        }

        return new TabBoxPixels(x, y, wPx, hPx);
    }

    private static int GetMarginCorrection(int pos, int size, int limit)
    {
        if (pos < 0)
        {
            return -pos + SafetyPx;
        }
        if (pos + size > limit)
        {
            return -((pos + size) - limit) - SafetyPx;
        }
        return 0;
    }
}
