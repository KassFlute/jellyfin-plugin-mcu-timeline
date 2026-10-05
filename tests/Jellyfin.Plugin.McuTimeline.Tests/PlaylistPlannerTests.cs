using Jellyfin.Plugin.McuTimeline.Model;
using Xunit;

namespace Jellyfin.Plugin.McuTimeline.Tests;

public class PlaylistPlannerTests
{
    private static readonly Guid _ironMan = Guid.NewGuid();
    private static readonly Guid _cap = Guid.NewGuid();
    private static readonly Guid _s1e1 = Guid.NewGuid();
    private static readonly Guid _s1e2 = Guid.NewGuid();
    private static readonly Guid _s2e1 = Guid.NewGuid();

    private static readonly TimelineEntry[] _entries =
    [
        TestData.Entry("iron-man", "2008-04-30", 30),
        TestData.Entry("captain-america", "2011-07-22", 10),
        TestData.Entry("missing", "2012-05-04", 40),
        TestData.Entry("loki-s1", "2021-06-09", 50, EntryType.Series),
        TestData.Entry("loki-s2", "2023-10-05", 20, EntryType.Series)
    ];

    private static IReadOnlyList<Guid> ItemsOf(TimelineEntry entry) => entry.Id switch
    {
        "iron-man" => [_ironMan],
        "captain-america" => [_cap],
        "loki-s1" => [_s1e1, _s1e2],
        "loki-s2" => [_s2e1],
        _ => []
    };

    [Fact]
    public void Release_ListsOwnedItemsByReleaseDate()
    {
        var plan = PlaylistPlanner.Plan(_entries, TimelineOrder.Release, ItemsOf);

        Assert.Equal([_ironMan, _cap, _s1e1, _s1e2, _s2e1], plan);
    }

    [Fact]
    public void Chronological_PlacesEachSeasonAtItsOwnPosition()
    {
        var plan = PlaylistPlanner.Plan(_entries, TimelineOrder.Chronological, ItemsOf);

        Assert.Equal([_cap, _s2e1, _ironMan, _s1e1, _s1e2], plan);
    }

    [Fact]
    public void Plan_NeverRepeatsAnItem()
    {
        TimelineEntry[] entries =
        [
            TestData.Entry("a", "2020-01-01", 10),
            TestData.Entry("b", "2021-01-01", 20)
        ];

        var plan = PlaylistPlanner.Plan(entries, TimelineOrder.Release, _ => [_ironMan]);

        Assert.Equal([_ironMan], plan);
    }

    [Fact]
    public void Plan_WithNothingOwned_IsEmpty()
    {
        Assert.Empty(PlaylistPlanner.Plan(_entries, TimelineOrder.Release, _ => []));
    }
}
