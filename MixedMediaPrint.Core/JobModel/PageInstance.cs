namespace MixedMediaPrint.Core.JobModel;

/// <summary>
/// Which physical tray a page must come from. In v1 scope there are exactly two
/// (see IMPLEMENTATION_PLAN.md's confirmed v1 scope) — this doubles as the
/// tray-routing key, not just a content-type label.
/// </summary>
public enum PageRole
{
    Body,
    Tab,
}

/// <summary>One flattened, physical page to print, in job order.</summary>
public sealed record PageInstance(PageRole Role, int? BodyPageIndex, int? TabNumber, string? LabelText);
