using Jellyfin.Data.Enums;
using Jellyfin.Plugin.McuTimeline.Model;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.McuTimeline.Services;

/// <summary>
/// Links each timeline entry to a library item. The result is cached until the library or
/// the settings change.
/// </summary>
public class LibraryMatcher
{
    private readonly ILibraryManager _libraryManager;
    private readonly TimelineDataProvider _dataProvider;
    private readonly ILogger<LibraryMatcher> _logger;
    private readonly Lock _lock = new();
    private MatchSnapshot? _snapshot;
    private int _generation;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryMatcher"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="dataProvider">Timeline data.</param>
    /// <param name="logger">Logger.</param>
    public LibraryMatcher(ILibraryManager libraryManager, TimelineDataProvider dataProvider, ILogger<LibraryMatcher> logger)
    {
        _libraryManager = libraryManager;
        _dataProvider = dataProvider;
        _logger = logger;
    }

    /// <summary>
    /// Drops the cache, the next call rebuilds it.
    /// </summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _snapshot = null;
            _generation++;
        }
    }

    /// <summary>
    /// Returns the current matches.
    /// </summary>
    /// <returns>Matches for the data in use.</returns>
    public MatchSnapshot GetSnapshot()
    {
        var source = _dataProvider.Get();
        var libraries = string.Join(',', PluginSettings.Current.LibraryIds.Order(StringComparer.Ordinal));
        int generation;
        lock (_lock)
        {
            if (_snapshot is not null
                && ReferenceEquals(_snapshot.Source, source)
                && string.Equals(_snapshot.Libraries, libraries, StringComparison.Ordinal))
            {
                return _snapshot;
            }

            generation = _generation;
        }

        // built outside the lock, a scan event arriving meanwhile only costs a rebuild
        var built = Build(source, libraries);
        lock (_lock)
        {
            if (generation == _generation)
            {
                _snapshot = built;
            }
        }

        return built;
    }

    private MatchSnapshot Build(TimelineSource source, string libraries)
    {
        var index = new MatchIndex(LoadCandidates());
        var today = DateOnly.FromDateTime(DateTime.Now);
        var matches = new Dictionary<string, EntryMatch>(StringComparer.Ordinal);

        foreach (var entry in source.Data.Items)
        {
            var itemId = index.Find(entry);
            if (itemId is null)
            {
                if (!entry.IsUpcoming(today))
                {
                    _logger.LogWarning(
                        "[MCU Timeline] {Title} ({Id}) not found in the library, tmdbId {TmdbId}.",
                        entry.Title,
                        entry.Id,
                        entry.TmdbId);
                }

                continue;
            }

            var playable = entry.IsSeries ? LoadEpisodes(itemId.Value, entry.Seasons) : [itemId.Value];
            if (playable.Count == 0)
            {
                // the series is there but none of the seasons this entry covers
                _logger.LogWarning(
                    "[MCU Timeline] {Title} ({Id}) found, but no episode of the listed seasons.",
                    entry.Title,
                    entry.Id);
                continue;
            }

            matches[entry.Id] = new EntryMatch(itemId.Value, playable);
        }

        return new MatchSnapshot(source, libraries, matches);
    }

    private List<LibraryCandidate> LoadCandidates()
    {
        var candidates = new List<LibraryCandidate>();
        foreach (var libraryId in GetLibraryIds())
        {
            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                ParentId = libraryId,
                Recursive = true,
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
                IsVirtualItem = false,
                // provider ids are a separate table, only joined when asked for
                DtoOptions = new DtoOptions(false) { Fields = [ItemFields.ProviderIds] }
            });

            foreach (var item in items)
            {
                candidates.Add(new LibraryCandidate(
                    item.Id,
                    item.GetBaseItemKind() == BaseItemKind.Series,
                    item.GetProviderId(MetadataProvider.Tmdb),
                    item.GetProviderId(MetadataProvider.Imdb)));
            }
        }

        return candidates;
    }

    private IEnumerable<Guid> GetLibraryIds()
    {
        var selected = PluginSettings.Current.LibraryIds
            .Select(id => Guid.TryParse(id, out var guid) ? guid : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        foreach (var folder in _libraryManager.GetVirtualFolders())
        {
            if (!Guid.TryParse(folder.ItemId, out var id))
            {
                continue;
            }

            var wanted = selected.Count > 0
                ? selected.Contains(id)
                : folder.CollectionType is CollectionTypeOptions.movies or CollectionTypeOptions.tvshows;
            if (wanted)
            {
                yield return id;
            }
        }
    }

    private List<Guid> LoadEpisodes(Guid seriesId, IReadOnlyList<int> seasons)
    {
        var episodes = _libraryManager.GetItemList(new InternalItemsQuery
        {
            AncestorIds = [seriesId],
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Episode],
            IsVirtualItem = false,
            DtoOptions = new DtoOptions(false)
        });

        // no seasons listed means the whole series, specials (season 0) aside
        return episodes
            .Where(e => e.ParentIndexNumber is int season && (seasons.Count == 0 ? season > 0 : seasons.Contains(season)))
            .OrderBy(e => e.ParentIndexNumber)
            .ThenBy(e => e.IndexNumber ?? int.MaxValue)
            .ThenBy(e => e.SortName, StringComparer.Ordinal)
            .Select(e => e.Id)
            .ToList();
    }
}

/// <summary>
/// Library item of a timeline entry.
/// </summary>
/// <param name="ItemId">The movie or series.</param>
/// <param name="PlayableIds">The movie, or the episodes of the listed seasons in airing order.</param>
public sealed record EntryMatch(Guid ItemId, IReadOnlyList<Guid> PlayableIds);

/// <summary>
/// Matches for one version of the data.
/// </summary>
/// <param name="Source">Data the matches were computed for.</param>
/// <param name="Libraries">Library selection the matches were computed for.</param>
/// <param name="Matches">Matches by entry id. Missing entries are not in the library.</param>
public sealed record MatchSnapshot(TimelineSource Source, string Libraries, IReadOnlyDictionary<string, EntryMatch> Matches);
