namespace MixedMediaPrint.Core.Calibration;

/// <summary>
/// A snapshot of a printer's DeviceInfo at the time calibration was last
/// verified. PrintEngine callers should compare this against a fresh
/// GdiDeviceInfoReader.Read() before trusting a profile's scenarios — a direct,
/// cheap guard against the exact mistake this project already made once
/// (two different physical printers, "SHARP#1" vs "SHARP BP-71C65 PCL6",
/// sharing what looked like an interchangeable setup; see
/// legacy-testkit/errors.md ERR-3).
/// </summary>
public sealed record DeviceFingerprint(int DpiX, int DpiY, int HorzRes, int VertRes);

/// <summary>
/// One hardware-validated (tab-tray, body-tray) combination for a printer.
/// Flip/nudge are properties of this PAIR, not of either tray in isolation —
/// the same physical printer needs different flip settings depending on which
/// bin is playing which role (confirmed: see the two real scenarios in
/// IMPLEMENTATION_PLAN.md, both from legacy-testkit/README.md).
/// </summary>
public sealed record CalibrationScenario(
    string TabTrayPattern,
    string BodyTrayPattern,
    bool FlipX,
    bool FlipY,
    double NudgeXIn,
    double NudgeYIn,
    bool ConfirmedOnPaper = false,
    string? Note = null);

/// <summary>
/// Per-printer calibration, keyed by the exact print queue name.
/// </summary>
/// <param name="RotationDegrees">
/// The angle passed to TabLabelRenderer's Graphics.RotateTransform — GDI+'s
/// convention (plain degrees), NOT the historical raw-GDI CreateFont escapement
/// convention (tenths of a degree) legacy-testkit's scripts calibrated (900 on
/// BP-71C65, 2700 on SHARP#1/BP-70C65). Those values are a starting hypothesis
/// for this field, not a value to mechanically convert — GDI+'s rotation sign
/// convention is not guaranteed to match raw GDI's; re-verify on paper per
/// printer (see IMPLEMENTATION_PLAN.md risk R2).
/// </param>
public sealed record PrinterCalibrationProfile(
    string PrinterQueueName,
    DeviceFingerprint? DeviceFingerprint,
    float RotationDegrees,
    IReadOnlyList<CalibrationScenario> Scenarios,
    DateTimeOffset? LastVerifiedUtc = null,
    string? LastVerificationResult = null);
