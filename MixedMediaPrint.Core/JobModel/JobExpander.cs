namespace MixedMediaPrint.Core.JobModel;

/// <summary>Expands a PrintJobPlan's items into the flat, ordered sequence of physical pages to print. Pure — no OS dependency.</summary>
public static class JobExpander
{
    public static IReadOnlyList<PageInstance> Expand(PrintJobPlan plan)
    {
        var result = new List<PageInstance>();

        foreach (PrintJobItem item in plan.Items)
        {
            switch (item)
            {
                case BodyRangeItem body:
                    ExpandBodyRange(body, result);
                    break;

                case TabRunItem tabRun:
                    ExpandTabRun(tabRun, result);
                    break;

                default:
                    throw new NotSupportedException($"Unknown job item type: {item.GetType()}");
            }
        }

        return result;
    }

    private static void ExpandBodyRange(BodyRangeItem body, List<PageInstance> result)
    {
        if (body.PageCount < 0)
        {
            throw new ArgumentException($"{nameof(BodyRangeItem)}.{nameof(BodyRangeItem.PageCount)} cannot be negative.", nameof(body));
        }
        if (body.FirstPageIndex < 0)
        {
            throw new ArgumentException($"{nameof(BodyRangeItem)}.{nameof(BodyRangeItem.FirstPageIndex)} cannot be negative.", nameof(body));
        }

        for (int i = 0; i < body.PageCount; i++)
        {
            result.Add(new PageInstance(PageRole.Body, BodyPageIndex: body.FirstPageIndex + i, TabNumber: null, LabelText: null));
        }
    }

    private static void ExpandTabRun(TabRunItem tabRun, List<PageInstance> result)
    {
        if (tabRun.Count < 1)
        {
            throw new ArgumentException($"{nameof(TabRunItem)}.{nameof(TabRunItem.Count)} must be at least 1.", nameof(tabRun));
        }
        if (tabRun.CopiesPerTab < 1)
        {
            throw new ArgumentException($"{nameof(TabRunItem)}.{nameof(TabRunItem.CopiesPerTab)} must be at least 1.", nameof(tabRun));
        }
        if (tabRun.FirstTabNumber < 1)
        {
            throw new ArgumentException($"{nameof(TabRunItem)}.{nameof(TabRunItem.FirstTabNumber)} must be at least 1.", nameof(tabRun));
        }

        for (int i = 0; i < tabRun.Count; i++)
        {
            int tabNumber = tabRun.FirstTabNumber + i;
            string label = tabRun.LabelTextOverride ?? tabNumber.ToString();

            for (int c = 0; c < tabRun.CopiesPerTab; c++)
            {
                result.Add(new PageInstance(PageRole.Tab, BodyPageIndex: null, TabNumber: tabNumber, LabelText: label));
            }
        }
    }
}
