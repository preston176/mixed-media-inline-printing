namespace MixedMediaPrint.Core.Printing.Diagnostics;

public sealed record PerPageTraySelfTestResult(
    PerPageTrayVerdict Verdict,
    int AllTrayABodyLength,
    int AllTrayBBodyLength,
    int MixedBodyLength,
    int DiffCountMixedVsA,
    int DiffCountMixedVsB);
