using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.McuTimeline.Model;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.McuTimeline.Services;

/// <summary>
/// Finds TMDB posters for titles the library does not have, through the TMDb provider
/// built into Jellyfin, so no extra API key is needed.
/// </summary>
public sealed class PosterService : IDisposable
{
    private const string ProviderName = "TheMovieDb";

    // a miss is retried after a while, TMDB may get the poster later
    private static readonly TimeSpan _missRetry = TimeSpan.FromHours(12);

    private readonly IProviderManager _providerManager;
    private readonly IServerConfigurationManager _configurationManager;
    private readonly ILogger<PosterService> _logger;
    private readonly ConcurrentDictionary<string, CachedPoster> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(4);
    private readonly Lock _fileLock = new();
    private bool _loaded;

    /// <summary>
    /// Initializes a new instance of the <see cref="PosterService"/> class.
    /// </summary>
    /// <param name="providerManager">Provider manager.</param>
    /// <param name="configurationManager">Server configuration, for the metadata language.</param>
    /// <param name="logger">Logger.</param>
    public PosterService(IProviderManager providerManager, IServerConfigurationManager configurationManager, ILogger<PosterService> logger)
    {
        _providerManager = providerManager;
        _configurationManager = configurationManager;
        _logger = logger;
    }

    private static string? CachePath => Plugin.Instance is { } plugin ? Path.Combine(plugin.DataFolderPath, "posters.json") : null;

    /// <summary>
    /// Returns the poster address of each entry, looking up the ones not known yet.
    /// </summary>
    /// <param name="entries">Entries that need a poster.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Poster address by entry id. Entries without a poster are left out.</returns>
    public async Task<IReadOnlyDictionary<string, string>> GetPostersAsync(IEnumerable<TimelineEntry> entries, CancellationToken cancellationToken)
    {
        LoadOnce();
        var now = DateTime.UtcNow;
        var wanted = entries.ToList();
        var lookups = wanted
            .Where(e => !_cache.TryGetValue(Key(e), out var cached) || (cached.Url is null && now - cached.CheckedUtc > _missRetry))
            .Select(e => LookupAsync(e, cancellationToken))
            .ToList();

        if (lookups.Count > 0)
        {
            await Task.WhenAll(lookups).ConfigureAwait(false);
            Save();
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in wanted)
        {
            if (_cache.TryGetValue(Key(entry), out var cached) && cached.Url is not null)
            {
                result[entry.Id] = cached.Url;
            }
        }

        return result;
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    // keyed on what is looked up, so a corrected tmdbId is looked up again
    private static string Key(TimelineEntry entry) =>
        string.Create(CultureInfo.InvariantCulture, $"{(entry.IsSeries ? "tv" : "movie")}/{entry.TmdbId}");

    private async Task LookupAsync(TimelineEntry entry, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var config = _configurationManager.Configuration;
            var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataProvider.Tmdb.ToString()] = entry.TmdbId.ToString(CultureInfo.InvariantCulture)
            };

            IEnumerable<RemoteSearchResult> results = entry.IsSeries
                ? await _providerManager.GetRemoteSearchResults<Series, SeriesInfo>(
                    new RemoteSearchQuery<SeriesInfo>
                    {
                        SearchProviderName = ProviderName,
                        SearchInfo = new SeriesInfo { ProviderIds = ids, MetadataLanguage = config.PreferredMetadataLanguage, MetadataCountryCode = config.MetadataCountryCode }
                    },
                    cancellationToken).ConfigureAwait(false)
                : await _providerManager.GetRemoteSearchResults<Movie, MovieInfo>(
                    new RemoteSearchQuery<MovieInfo>
                    {
                        SearchProviderName = ProviderName,
                        SearchInfo = new MovieInfo { ProviderIds = ids, MetadataLanguage = config.PreferredMetadataLanguage, MetadataCountryCode = config.MetadataCountryCode }
                    },
                    cancellationToken).ConfigureAwait(false);

            var url = results.Select(r => r.ImageUrl).FirstOrDefault(u => !string.IsNullOrEmpty(u));
            _cache[Key(entry)] = new CachedPoster(url, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or IOException)
        {
            _logger.LogWarning(ex, "[MCU Timeline] No poster for {Title}.", entry.Title.Default);
            _cache[Key(entry)] = new CachedPoster(null, DateTime.UtcNow);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void LoadOnce()
    {
        lock (_fileLock)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            try
            {
                if (CachePath is { } path && File.Exists(path)
                    && JsonSerializer.Deserialize<Dictionary<string, CachedPoster>>(File.ReadAllText(path)) is { } saved)
                {
                    foreach (var (key, value) in saved)
                    {
                        _cache[key] = value;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "[MCU Timeline] Poster cache unreadable, starting empty.");
            }
        }
    }

    private void Save()
    {
        lock (_fileLock)
        {
            try
            {
                if (CachePath is { } path)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, JsonSerializer.Serialize(_cache.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "[MCU Timeline] Poster cache not saved.");
            }
        }
    }

    private sealed record CachedPoster(string? Url, DateTime CheckedUtc);
}
