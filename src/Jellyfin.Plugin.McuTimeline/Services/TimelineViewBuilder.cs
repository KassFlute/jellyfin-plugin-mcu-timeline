using System.Globalization;
using Jellyfin.Database.Implementations.Entities;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="TimelineViewBuilder"/> class.
    /// </summary>
    /// <param name="matcher">Library matcher.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="userDataManager">User data manager.</param>
    public TimelineViewBuilder(LibraryMatcher matcher, ILibraryManager libraryManager, IUserDataManager userDataManager)
    {
        _matcher = matcher;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
    }

    /// <summary>
    /// Builds the timeline for a user.
    /// </summary>
    /// <param name="user">Calling user.</param>
    /// <returns>Data version and entries.</returns>
    public (string Version, IReadOnlyList<TimelineItemDto> Items) Build(User user)
    {
        var snapshot = _matcher.GetSnapshot();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var items = new List<TimelineItemDto>();

        foreach (var entry in VisibleEntries(snapshot.Source.Data, today))
        {
            var owned = ResolveOwned(snapshot, entry, user);
            items.Add(owned is null ? ToDto(entry, entry.IsUpcoming(today) ? "upcoming" : "absent") : ToOwnedDto(entry, owned.Value.Item, owned.Value.Playable, user));
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

    private TimelineItemDto ToOwnedDto(TimelineEntry entry, BaseItem item, IReadOnlyList<BaseItem> playable, User user)
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
        return ToDto(entry, "owned") with
        {
            JellyfinId = item.Id.ToString("N", CultureInfo.InvariantCulture),
            RunTimeTicks = entry.IsSeries ? null : item.RunTimeTicks,
            EpisodeCount = entry.IsSeries ? playable.Count : null,
            Played = all,
            InProgress = !all && (played > 0 || started),
            Progress = progress
        };
    }

    private static TimelineItemDto ToDto(TimelineEntry entry, string status) => new()
    {
        Id = entry.Id,
        Title = entry.Title,
        Type = TypeName(entry.Type),
        TmdbId = entry.TmdbId,
        ReleaseDate = entry.ReleaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ChronoOrder = entry.ChronoOrder,
        StoryYear = entry.StoryYear,
        Phase = entry.Phase,
        Saga = entry.Saga,
        Era = entry.Era,
        Note = entry.Note,
        Seasons = entry.Seasons,
        Status = status
    };
}
