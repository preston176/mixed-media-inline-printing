using MixedMediaPrint.Core.Printing;

namespace MixedMediaPrint.Core.TabTemplate;

// Pure port of the geometry math in legacy-testkit/print-mixed-test.ps1 (the section between
// the tray resolution and the edit-tab-docx.ps1 call). No P/Invoke here -- takes a DeviceInfo
// already read by PrinterDeviceInfo, so this is unit-testable on any platform.
public static class TabGeometry
{
    public const int EmuPerInch = 914400;
    public const int TemplateXEmu = 7162495;
    public const int TemplateWEmu = 557784;
    public const int TemplateHEmu = 1828800;
    private const int SafetyPx = 20;

    public static readonly IReadOnlyDictionary<int, int> TemplateYEmuByPosition = new Dictionary<int, int>
    {
        [1] = 412394,
        [2] = 2174443,
        [3] = 4155033,
        [4] = 6071616,
        [5] = 7697419,
    };

    public static int PositionFor(int tabNumber) => ((tabNumber - 1) % 5) + 1;

    public readonly record struct NudgeResult(int Position, double TotalNudgeXIn, double TotalNudgeYIn);

    public static NudgeResult ComputeNudge(
        int tabNumber,
        DeviceInfo info,
        double nudgeXIn,
        double nudgeYIn,
        bool flipTabX,
        bool flipTabY)
    {
        int position = PositionFor(tabNumber);
        int yEmu = TemplateYEmuByPosition[position];

        double xIn = TemplateXEmu / (double)EmuPerInch;
        double yIn = yEmu / (double)EmuPerInch;
        double wIn = TemplateWEmu / (double)EmuPerInch;
        double hIn = TemplateHEmu / (double)EmuPerInch;

        int xPxPhysical = (int)Math.Round(xIn * info.DpiX);
        int yPxPhysical = (int)Math.Round(yIn * info.DpiY);
        int wPx = (int)Math.Round(wIn * info.DpiX);
        int hPx = (int)Math.Round(hIn * info.DpiY);

        int xPxGdiBase = xPxPhysical - info.PhysicalOffsetX;
        int yPxGdiBase = yPxPhysical - info.PhysicalOffsetY;

        int xPxGdi = xPxGdiBase + (int)Math.Round(nudgeXIn * info.DpiX);
        int yPxGdi = yPxGdiBase + (int)Math.Round(nudgeYIn * info.DpiY);

        xPxGdi += Correction(xPxGdi, wPx, info.HorzRes);
        yPxGdi += Correction(yPxGdi, hPx, info.VertRes);

        // Applied AFTER the safety-margin correction, matching print-mixed-test.ps1: some
        // trays (commonly Bypass vs. a cassette tray) register/feed the sheet differently,
        // which can mirror where content lands relative to the physical tab.
        if (flipTabX) xPxGdi = info.HorzRes - xPxGdi - wPx;
        if (flipTabY) yPxGdi = info.VertRes - yPxGdi - hPx;

        double totalNudgeXIn = (xPxGdi - xPxGdiBase) / (double)info.DpiX;
        double totalNudgeYIn = (yPxGdi - yPxGdiBase) / (double)info.DpiY;

        return new NudgeResult(position, totalNudgeXIn, totalNudgeYIn);
    }

    private static int Correction(int pos, int size, int limit)
    {
        if (pos < 0) return -pos + SafetyPx;
        if (pos + size > limit) return -((pos + size) - limit) - SafetyPx;
        return 0;
    }
}
