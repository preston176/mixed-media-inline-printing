using MixedMediaPrint.Core.JobModel;
using Xunit;

namespace MixedMediaPrint.Tests.JobModel;

public class JobExpanderTests
{
    [Fact]
    public void Expand_BodyRange_ProducesSequentialPageIndices()
    {
        var plan = new PrintJobPlan([new BodyRangeItem(FirstPageIndex: 5, PageCount: 3)]);

        var pages = JobExpander.Expand(plan);

        Assert.Equal(
        [
            new PageInstance(PageRole.Body, 5, null, null),
            new PageInstance(PageRole.Body, 6, null, null),
            new PageInstance(PageRole.Body, 7, null, null),
        ], pages);
    }

    [Fact]
    public void Expand_BodyRange_ZeroPageCount_ProducesNothing()
    {
        var plan = new PrintJobPlan([new BodyRangeItem(0, 0)]);

        Assert.Empty(JobExpander.Expand(plan));
    }

    [Theory]
    [InlineData(-1)]
    public void Expand_BodyRange_NegativePageCount_Throws(int pageCount)
    {
        var plan = new PrintJobPlan([new BodyRangeItem(0, pageCount)]);

        Assert.Throws<ArgumentException>(() => JobExpander.Expand(plan));
    }

    [Fact]
    public void Expand_TabRun_CountAndCopies_MatchesConsecutiveThenRepeatSemantics()
    {
        // The exact scenario documented in legacy-testkit/print-tab-from-docx.ps1:
        // Count=5, Copies=2 -> 1,1,2,2,3,3,4,4,5,5 (NOT 1,2,3,4,5,1,2,3,4,5).
        var plan = new PrintJobPlan([new TabRunItem(FirstTabNumber: 1, Count: 5, CopiesPerTab: 2)]);

        var pages = JobExpander.Expand(plan);

        Assert.Equal([1, 1, 2, 2, 3, 3, 4, 4, 5, 5], pages.Select(p => p.TabNumber));
        Assert.All(pages, p => Assert.Equal(PageRole.Tab, p.Role));
    }

    [Fact]
    public void Expand_TabRun_DefaultLabel_IsTheBareTabNumber()
    {
        var plan = new PrintJobPlan([new TabRunItem(FirstTabNumber: 7)]);

        var pages = JobExpander.Expand(plan);

        Assert.Equal("7", Assert.Single(pages).LabelText);
    }

    [Fact]
    public void Expand_TabRun_LabelOverride_AppliesToEveryTabInTheRun()
    {
        var plan = new PrintJobPlan([new TabRunItem(FirstTabNumber: 1, Count: 3, LabelTextOverride: "EXHIBIT")]);

        var pages = JobExpander.Expand(plan);

        Assert.All(pages, p => Assert.Equal("EXHIBIT", p.LabelText));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Expand_TabRun_CountBelowOne_Throws(int count)
    {
        var plan = new PrintJobPlan([new TabRunItem(FirstTabNumber: 1, Count: count)]);

        Assert.Throws<ArgumentException>(() => JobExpander.Expand(plan));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Expand_TabRun_CopiesBelowOne_Throws(int copies)
    {
        var plan = new PrintJobPlan([new TabRunItem(FirstTabNumber: 1, CopiesPerTab: copies)]);

        Assert.Throws<ArgumentException>(() => JobExpander.Expand(plan));
    }

    [Fact]
    public void Expand_TabRun_FirstTabNumberBelowOne_Throws()
    {
        var plan = new PrintJobPlan([new TabRunItem(FirstTabNumber: 0)]);

        Assert.Throws<ArgumentException>(() => JobExpander.Expand(plan));
    }

    [Fact]
    public void Expand_MixedPlan_PreservesOverallOrder()
    {
        // The real production shape: body pages, a tab inserted, more body pages.
        var plan = new PrintJobPlan(
        [
            new BodyRangeItem(0, 2),
            new TabRunItem(FirstTabNumber: 1, LabelTextOverride: "EMAIL CORRESPONDENCE"),
            new BodyRangeItem(2, 2),
        ]);

        var pages = JobExpander.Expand(plan);

        Assert.Equal(
        [
            PageRole.Body, PageRole.Body,
            PageRole.Tab,
            PageRole.Body, PageRole.Body,
        ], pages.Select(p => p.Role));
        Assert.Equal([0, 1, null, 2, 3], pages.Select(p => p.BodyPageIndex));
        Assert.Equal("EMAIL CORRESPONDENCE", pages[2].LabelText);
    }
}
