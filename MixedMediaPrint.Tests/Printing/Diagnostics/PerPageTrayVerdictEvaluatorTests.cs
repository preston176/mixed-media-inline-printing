using MixedMediaPrint.Core.Printing.Diagnostics;
using Xunit;

namespace MixedMediaPrint.Tests.Printing.Diagnostics;

public class PerPageTrayVerdictEvaluatorTests
{
    private static readonly byte[] BodyA = [1, 2, 3, 4];
    private static readonly byte[] BodyB = [9, 9, 9, 9];
    private static readonly byte[] BodyMixed = [1, 2, 3, 4, 5, 6]; // genuinely different from both

    [Fact]
    public void Evaluate_MixedDiffersFromBoth_PerPageTrayWorks()
    {
        var result = PerPageTrayVerdictEvaluator.Evaluate(BodyA, BodyB, BodyMixed);

        Assert.Equal(PerPageTrayVerdict.PerPageTrayWorks, result.Verdict);
    }

    [Fact]
    public void Evaluate_MixedMatchesTrayA_CollapsedToOneTray()
    {
        var result = PerPageTrayVerdictEvaluator.Evaluate(BodyA, BodyB, mixedBody: BodyA);

        Assert.Equal(PerPageTrayVerdict.CollapsedToOneTray, result.Verdict);
    }

    [Fact]
    public void Evaluate_MixedMatchesTrayB_CollapsedToOneTray()
    {
        var result = PerPageTrayVerdictEvaluator.Evaluate(BodyA, BodyB, mixedBody: BodyB);

        Assert.Equal(PerPageTrayVerdict.CollapsedToOneTray, result.Verdict);
    }

    [Fact]
    public void Evaluate_TraysProduceIdenticalOutput_TrayNotApplied()
    {
        // Control case: if all-A and all-B are byte-identical, tray isn't being
        // applied at all (a regression from the already-confirmed baseline) — this
        // takes priority over any mixed-vs-single comparison.
        var result = PerPageTrayVerdictEvaluator.Evaluate(BodyA, allTrayBBody: BodyA, mixedBody: BodyA);

        Assert.Equal(PerPageTrayVerdict.TrayNotApplied, result.Verdict);
    }

    [Fact]
    public void Evaluate_ReportsLengthsAndDiffCounts()
    {
        var result = PerPageTrayVerdictEvaluator.Evaluate(BodyA, BodyB, BodyMixed);

        Assert.Equal(BodyA.Length, result.AllTrayABodyLength);
        Assert.Equal(BodyB.Length, result.AllTrayBBodyLength);
        Assert.Equal(BodyMixed.Length, result.MixedBodyLength);
        // BodyMixed vs BodyA: first 4 bytes equal, plus 2 extra trailing bytes.
        Assert.Equal(2, result.DiffCountMixedVsA);
        // BodyMixed vs BodyB: all 4 compared bytes differ, plus 2 extra trailing bytes.
        Assert.Equal(6, result.DiffCountMixedVsB);
    }
}
