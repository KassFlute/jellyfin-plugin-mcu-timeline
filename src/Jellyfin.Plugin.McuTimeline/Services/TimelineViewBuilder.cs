using System.Globalization;
using Jellyfin.Plugin.McuTimeline.Api;
using Jellyfin.Plugin.McuTimeline.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.McuTimeline.Services;

/// <summary>
/// Merges the timeline with the library and the play state of one user.
/// </summary>
public class TimelineViewBuilder
{
    private readonly LibraryMatcher _matcher;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly UserMarks _marks;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimelineViewBuilder"/> class.
    /// </summary>
    /// <param name="matcher">Library matcher.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="userDataManager">User data manager.</param>
    /// <param name="marks">Seen elsewhere and skipped marks.</param>
    public TimelineViewBuilder(LibraryMatcher matcher, ILibraryManager libraryManager, IUserDataManager userDataManager, UserMarks marks)
    {
        _marks = marks;
        _matcher = matcher;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
    }

    /// <summary>
    /// Builds the timeline for a user.
    /// </summary>
    /// <param name="user">Calling user.</param>
    /// <param name="language">Web client language, picks the texts of the data file.</param>
    /// <returns>Data version and entries.</returns>
    public (string Version, IReadOnlyList<TimelineItemDto> Items) Build(User user, string? language = null)
    {
        var snapshot = _matcher.GetSnapshot();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var visible = VisibleEntries(snapshot.Source.Data, today).ToList();
        var releaseRank = Ranks(visible, TimelineOrder.Release);
        var chronoRank = Ranks(visible, TimelineOrder.Chronological);
        var (watched, skipped) = _marks.Get(user.Id);
        var items = new List<TimelineItemDto>();

        foreach (var entry in visible)
        {
            var owned = ResolveOwned(snapshot, entry, user);
            var dto = owned is not null
                ? ToOwnedDto(entry, owned.Value.Item, owned.Value.Playable, user, language)
                : entry.IsUpcoming(today)
                    ? ToDto(entry, "upcoming", language)
                    // the library owns the play state once it has the title, until then the mark does
                    : ToDto(entry, "absent", language) with { Played = watched.Contains(entry.Id), Progress = watched.Contains(entry.Id) ? 1 : 0 };
            items.Add(dto with
            {
                ReleaseRank = releaseRank[entry.Id],
                ChronoRank = chronoRank[entry.Id],
                Skipped = skipped.Contains(entry.Id)
            });
        }

        return (snapshot.Source.Data.Version, items);
    }

    /// <summary>
    /// Picks what the play button starts: the movie, or the episode to carry on with.
    /// </summary>
    /// <param name="entryId">Entry id.</param>
    /// <param name="user">Calling user.</param>
    /// <returns>Item and resume position, or null when the user cannot play the entry.</returns>
    public (Guid ItemId, long StartTicks)? ResolvePlayback(string entryId, User user)
    {
        var snapshot = _matcher.GetSnapshot();
        var entry = snapshot.Source.Data.Items.FirstOrDefault(e => string.Equals(e.Id, entryId, StringComparison.Ordinal));
        if (entry is null || ResolveOwned(snapshot, entry, user) is not { } owned)
        {
            return null;
        }

        var data = UserDataOf(owned.Playable, user);
        UserItemData? StateOf(BaseItem item) => data.GetValueOrDefault(item.Id);

        // carry on with what was started, else the first unseen, else from the top
        var target = owned.Playable.FirstOrDefault(i => StateOf(i) is { Played: false, PlaybackPositionTicks: > 0 })
            ?? owned.Playable.FirstOrDefault(i => StateOf(i) is not { Played: true })
            ?? owned.Playable[0];

        var state = StateOf(target);
        var start = state is { Played: false } ? state.PlaybackPositionTicks : 0;
        return (target.Id, start);
    }

    /// <summary>
    /// Marks every item of an entry as played or unplayed: the movie, or the episodes of
    /// the seasons the entry covers.
    /// </summary>
    /// <param name="entryId">Entry id.</param>
    /// <param name="user">Calling user.</param>
    /// <param name="played">True to mark as played.</param>
    /// <param name="language">Web client language.</param>
    /// <returns>The entry as it now stands, or null for an unknown or unreleased entry.</returns>
    public TimelineItemDto? SetPlayed(string entryId, User user, bool played, string? language)
    {
        var snapshot = _matcher.GetSnapshot();
        var entry = snapshot.Source.Data.Items.FirstOrDefault(e => string.Equals(e.Id, entryId, StringComparison.Ordinal));
        if (entry is null)
        {
            return null;
        }

        if (ResolveOwned(snapshot, entry, user) is not { } owned)
        {
            // seen elsewhere: only a released title can have been seen
            if (entry.IsUpcoming(DateOnly.FromDateTime(DateTime.Now)))
            {
                return null;
            }

            _marks.SetWatched(user.Id, entryId, played);
            return Find(user, entryId, language);
        }

        foreach (var item in owned.Playable)
        {
            if (played)
            {
                item.MarkPlayed(user, DateTime.UtcNow, resetPosition: true);
            }
            else
            {
                item.MarkUnplayed(user);
            }
        }

        return Find(user, entryId, language);
    }

    /// <summary>
    /// Leaves an entry out of the progression, or brings it back.
    /// </summary>
    /// <param name="entryId">Entry id.</param>
    /// <param name="user">Calling user.</param>
    /// <param name="skipped">True to leave it out.</param>
    /// <param name="language">Web client language.</param>
    /// <returns>The entry as it now stands, or null for an unknown entry.</returns>
    public TimelineItemDto? SetSkipped(string entryId, User user, bool skipped, string? language)
    {
        if (!_matcher.GetSnapshot().Source.Data.Items.Any(e => string.Equals(e.Id, entryId, StringComparison.Ordinal)))
        {
            return null;
        }

        _marks.SetSkipped(user.Id, entryId, skipped);
        return Find(user, entryId, language);
    }

    private TimelineItemDto? Find(User user, string entryId, string? language) =>
        Build(user, language).Items.FirstOrDefault(i => string.Equals(i.Id, entryId, StringComparison.Ordinal));

    /// <summary>
    /// Lists the entries shown with the current settings.
    /// </summary>
    /// <param name="data">Timeline data.</param>
    /// <param name="today">Current date.</param>
    /// <returns>Entries kept.</returns>
    public static IEnumerable<TimelineEntry> VisibleEntries(TimelineData data, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(data);
        var settings = PluginSettings.Current;
        return data.Items.Where(e =>
            (settings.IncludeShorts || e.Type != EntryType.Short)
            && (settings.IncludeUpcoming || !e.IsUpcoming(today)));
    }

    /// <summary>
    /// Lower case name of a type, as written in the data file.
    /// </summary>
    /// <param name="type">Entry type.</param>
    /// <returns>movie, series or short.</returns>
    public static string TypeName(EntryType type) => type.ToString().ToLowerInvariant();

    private static Dictionary<string, int> Ranks(IEnumerable<TimelineEntry> entries, TimelineOrder order)
    {
        return TimelineSorter.Sort(entries, order)
            .Select((entry, index) => (entry.Id, index))
            .ToDictionary(x => x.Id, x => x.index, StringComparer.Ordinal);
    }

    private (BaseItem Item, IReadOnlyList<BaseItem> Playable)? ResolveOwned(MatchSnapshot snapshot, TimelineEntry entry, User user)
    {
        if (!snapshot.Matches.TryGetValue(entry.Id, out var match)
            || _libraryManager.GetItemById(match.ItemId) is not { } item
            || !item.IsVisibleStandalone(user))
        {
            // titles from libraries the user cannot open show as absent
            return null;
        }

        var playable = match.PlayableIds
            .Select(_libraryManager.GetItemById)
            .OfType<BaseItem>()
            .ToList();
        return playable.Count == 0 ? null : (item, playable);
    }

    // 10.11 has no batch lookup
    private Dictionary<Guid, UserItemData> UserDataOf(IReadOnlyList<BaseItem> items, User user)
    {
        var result = new Dictionary<Guid, UserItemData>();
        foreach (var item in items)
        {
            if (_userDataManager.GetUserData(user, item) is { } data)
            {
                result[item.Id] = data;
            }
        }

        return result;
    }

    private TimelineItemDto ToOwnedDto(TimelineEntry entry, BaseItem item, IReadOnlyList<BaseItem> playable, User user, string? language)
    {
        var data = UserDataOf(playable, user);
        var played = playable.Count(i => data.GetValueOrDefault(i.Id) is { Played: true });
        var started = playable.Any(i => data.GetValueOrDefault(i.Id) is { Played: false, PlaybackPositionTicks: > 0 });

        double progress;
        if (entry.IsSeries)
        {
            progress = (double)played / playable.Count;
        }
        else
        {
            var state = data.GetValueOrDefault(item.Id);
            var runtime = item.RunTimeTicks ?? 0;
            progress = state is { Played: true } ? 1
                : runtime > 0 && state is not null ? Math.Clamp((double)state.PlaybackPositionTicks / runtime, 0, 1)
                : 0;
        }

        var all = played == playable.Count;
        return ToDto(entry, "owned", language) with
        {
            JellyfinId = item.Id.ToString("N", CultureInfo.InvariantCulture),
            RunTimeTicks = entry.IsSeries ? null : item.RunTimeTicks,
            EpisodeCount = entry.IsSeries ? playable.Count : null,
            Played = all,
            InProgress = !all && (played > 0 || started),
            Progress = progress
        };
    }

    private static TimelineItemDto ToDto(TimelineEntry entry, string status, string? language) => new()
    {
        Id = entry.Id,
        Title = entry.Title.For(language),
        Type = TypeName(entry.Type),
        TmdbId = entry.TmdbId,
        ReleaseDate = entry.ReleaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ChronoOrder = entry.ChronoOrder,
        StoryYear = entry.StoryYear?.For(language),
        Phase = entry.Phase,
        Saga = entry.Saga,
        Era = entry.Era,
        AccentColor = entry.AccentColor,
        Note = entry.Note?.For(language),
        Seasons = entry.Seasons,
        Status = status
    };
}
