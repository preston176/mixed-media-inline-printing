namespace MixedMediaPrint.Core.Printing.Diagnostics;

public enum PerPageTrayVerdict
{
    /// <summary>Even the two single-tray control renders were identical — tray isn't being applied at all. Unexpected; a regression from the already-confirmed baseline.</summary>
    TrayNotApplied,

    /// <summary>The mixed render matched one of the single-tray renders — the driver collapsed per-page tray to one tray for the whole job.</summary>
    CollapsedToOneTray,

    /// <summary>The mixed render differs from both single-tray renders — the middle page genuinely got a different tray. Per-page tray switching works.</summary>
    PerPageTrayWorks,
}
