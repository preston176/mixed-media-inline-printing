namespace MixedMediaPrint.Core;

public enum PageSlotKind
{
    // Today's yellow test label -- used when no PDF is loaded.
    BodyPlaceholder,
    // A real PDF page, rendered full-page into the mixed docx.
    BodyPdfPage,
    // The one tab divider in the job.
    Tab,
}

public sealed record PageSlot(PageSlotKind Kind, int? PdfPageIndex = null);
