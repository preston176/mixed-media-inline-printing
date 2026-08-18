namespace MixedMediaPrint.Core.Execution;

public enum RunMode
{
    /// <summary>No printer touched at all — use TabPreviewRenderer directly instead of PrintEngine.</summary>
    Preview,

    /// <summary>Real GdiPrintJob/DEVMODE pipeline, output redirected to a file via StartDoc's lpszOutput. No paper, no admin required.</summary>
    DryRunToFile,

    /// <summary>Sends the job to the physical device.</summary>
    Physical,
}
