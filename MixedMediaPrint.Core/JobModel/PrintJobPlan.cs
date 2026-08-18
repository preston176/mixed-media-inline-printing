namespace MixedMediaPrint.Core.JobModel;

/// <summary>One item in an ordered print job plan: a run of consecutive body-PDF pages, or a run of consecutive tabs.</summary>
public abstract record PrintJobItem;

/// <summary>A contiguous range of pages from the body PDF, in order: [FirstPageIndex, FirstPageIndex + PageCount).</summary>
public sealed record BodyRangeItem(int FirstPageIndex, int PageCount) : PrintJobItem;

/// <summary>
/// N consecutive tabs starting at FirstTabNumber, each optionally repeated
/// CopiesPerTab times in place — e.g. FirstTabNumber=1, Count=5, CopiesPerTab=2
/// prints 1,1,2,2,3,3,4,4,5,5 (NOT 1,2,3,4,5,1,2,3,4,5), matching how an
/// operator physically loads a 5-cut set: each sheet is already cut at a
/// different one of the 5 positions, stacked so position 1 feeds first. Ported
/// from legacy-testkit/print-tab-from-docx.ps1's -TabNumber/-Count/-Copies.
/// </summary>
/// <param name="LabelTextOverride">
/// Replaces the bare tab number on every tab in this run when set (matches the
/// original scripts' -Text flag). Only sensible for Count=1 in practice — a run
/// of several tabs sharing one custom label rarely makes sense — but that's not
/// enforced here; it's the caller/UI's job to make sensible choices.
/// </param>
public sealed record TabRunItem(int FirstTabNumber, int Count = 1, int CopiesPerTab = 1, string? LabelTextOverride = null) : PrintJobItem;

public sealed record PrintJobPlan(IReadOnlyList<PrintJobItem> Items);
