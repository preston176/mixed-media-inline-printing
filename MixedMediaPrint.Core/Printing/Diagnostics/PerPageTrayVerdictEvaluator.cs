namespace MixedMediaPrint.Core.Printing.Diagnostics;

/// <summary>
/// Pure byte-comparison logic behind the per-page-tray byte-diff test, ported from
/// legacy-testkit/capture-gdi-perpage.ps1's Bytes-Equal/Diff-Count/verdict logic.
/// No OS dependency: given three already-extracted PCL-XL bodies (identical visual
/// content, only the per-page tray varied), decides whether per-page tray switching
/// actually happened.
/// </summary>
public static class PerPageTrayVerdictEvaluator
{
    public static PerPageTraySelfTestResult Evaluate(byte[] allTrayABody, byte[] allTrayBBody, byte[] mixedBody)
    {
        bool allDiffer = !allTrayABody.AsSpan().SequenceEqual(allTrayBBody);
        bool mixedEqualsA = mixedBody.AsSpan().SequenceEqual(allTrayABody);
        bool mixedEqualsB = mixedBody.AsSpan().SequenceEqual(allTrayBBody);

        PerPageTrayVerdict verdict = !allDiffer
            ? PerPageTrayVerdict.TrayNotApplied
            : mixedEqualsA || mixedEqualsB
                ? PerPageTrayVerdict.CollapsedToOneTray
                : PerPageTrayVerdict.PerPageTrayWorks;

        return new PerPageTraySelfTestResult(
            verdict,
            AllTrayABodyLength: allTrayABody.Length,
            AllTrayBBodyLength: allTrayBBody.Length,
            MixedBodyLength: mixedBody.Length,
            DiffCountMixedVsA: CountDifferingBytes(mixedBody, allTrayABody),
            DiffCountMixedVsB: CountDifferingBytes(mixedBody, allTrayBBody));
    }

    private static int CountDifferingBytes(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        int diff = Math.Abs(a.Length - b.Length);
        for (int i = 0; i < n; i++)
        {
            if (a[i] != b[i])
            {
                diff++;
            }
        }
        return diff;
    }
}
