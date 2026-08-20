namespace MixedMediaPrint.Core;

// Mirrors the parameters of legacy-testkit/print-mixed-test.ps1 exactly, including its
// defaults ('(?i)tray\s*1' / '(?i)tray\s*2', no flip, Copies=1).
public sealed record PrintJobRequest(
    string Printer,
    int TabNumber,
    string? Text,
    double NudgeXIn,
    double NudgeYIn,
    int Copies,
    string TabTrayPattern,
    string BodyTrayPattern,
    bool FlipTabX,
    bool FlipTabY,
    string? PdfPath = null,
    IReadOnlyList<PageSlot>? Sequence = null)
{
    public static PrintJobRequest CreateDefault(string printer, int tabNumber) => new(
        Printer: printer,
        TabNumber: tabNumber,
        Text: null,
        NudgeXIn: 0,
        NudgeYIn: 0,
        Copies: 1,
        TabTrayPattern: @"(?i)tray\s*1",
        BodyTrayPattern: @"(?i)tray\s*2",
        FlipTabX: false,
        FlipTabY: false);

    private static readonly PageSlot[] DefaultSequence =
    {
        new(PageSlotKind.BodyPlaceholder),
        new(PageSlotKind.Tab),
        new(PageSlotKind.BodyPlaceholder),
    };

    // Falls back to today's placeholder/tab/placeholder job when the caller sends no
    // sequence at all -- e.g. no PDF has been loaded yet -- so nothing already proven
    // stops working just because this field exists.
    public IReadOnlyList<PageSlot> EffectiveSequence => Sequence is { Count: > 0 } ? Sequence : DefaultSequence;
}
