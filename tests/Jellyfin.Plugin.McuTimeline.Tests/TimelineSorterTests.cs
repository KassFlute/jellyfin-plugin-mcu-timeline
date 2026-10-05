using Jellyfin.Plugin.McuTimeline.Model;
using Xunit;

namespace Jellyfin.Plugin.McuTimeline.Tests;

public class TimelineSorterTests
{
    private static readonly TimelineEntry[] _entries =
    [
        TestData.Entry("iron-man", "2008-04-30", 30),
        TestData.Entry("captain-america", "2011-07-22", 10),
        TestData.Entry("captain-marvel", "2019-03-06", 20),
        TestData.Entry("thor", "2011-04-27", 40)
    ];

    [Fact]
    public void Release_SortsByReleaseDate()
    {
        var sorted = TimelineSorter.Sort(_entries, TimelineOrder.Release);

        Assert.Equal(["iron-man", "thor", "captain-america", "captain-marvel"], sorted.Select(e => e.Id));
    }

    [Fact]
    public void Chronological_SortsByChronoOrder()
    {
        var sorted = TimelineSorter.Sort(_entries, TimelineOrder.Chronological);

        Assert.Equal(["captain-america", "captain-marvel", "iron-man", "thor"], sorted.Select(e => e.Id));
    }

    [Fact]
    public void Ties_FallBackOnTheOtherKeyThenTheId()
    {
        TimelineEntry[] entries =
        [
            TestData.Entry("b", "2020-01-01", 20),
            TestData.Entry("a", "2020-01-01", 20),
            TestData.Entry("c", "2020-01-01", 10),
            TestData.Entry("d", "2019-01-01", 20)
        ];

        Assert.Equal(["d", "c", "a", "b"], TimelineSorter.Sort(entries, TimelineOrder.Release).Select(e => e.Id));
        Assert.Equal(["c", "d", "a", "b"], TimelineSorter.Sort(entries, TimelineOrder.Chronological).Select(e => e.Id));
    }

    [Fact]
    public void Result_DoesNotDependOnFileOrder()
    {
        Assert.Equal(
            TimelineSorter.Sort(_entries, TimelineOrder.Release).Select(e => e.Id),
            TimelineSorter.Sort(_entries.Reverse(), TimelineOrder.Release).Select(e => e.Id));
    }
}
