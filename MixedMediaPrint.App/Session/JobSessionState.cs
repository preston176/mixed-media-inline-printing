using MixedMediaPrint.Core.Calibration;
using MixedMediaPrint.Core.JobModel;
using MixedMediaPrint.Core.Printing.Gdi;

namespace MixedMediaPrint.App.Session;

/// <summary>
/// Mutable state threaded through the wizard's four screens (Printer Setup ->
/// Calibration -> Job Setup -> Run). Plain shared state rather than a service —
/// appropriate for a strictly linear, single-window flow; revisit if the app
/// ever needs more than one job in flight at once.
/// </summary>
public sealed class JobSessionState
{
    public string? PrinterName { get; set; }
    public DeviceInfo? DeviceInfo { get; set; }
    public PrinterCalibrationProfile? CalibrationProfile { get; set; }
    public CalibrationScenario? SelectedScenario { get; set; }
    public float RotationDegrees { get; set; }
    public string? PdfPath { get; set; }
    public PrintJobPlan? Plan { get; set; }
}
