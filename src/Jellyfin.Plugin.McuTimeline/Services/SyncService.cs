using Jellyfin.Data.Enums;
using Jellyfin.Plugin.McuTimeline.Model;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Playlists;
using Microsoft.Extensions.Logging;
#if JELLYFIN_10_11_OR_GREATER
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
#endif

namespace Jellyfin.Plugin.McuTimeline.Services;

/// <summary>
/// Keeps the two playlists and the collection in step with the timeline, and deletes the
/// ones switched off in the settings. Only touches the items whose ids are recorded there.
/// </summary>
public sealed class SyncService : IDisposable
{
    private readonly IPlaylistManager _playlistManager;
    private readonly ICollectionManager _collectionManager;
    private readonly IProviderManager _providerManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly LibraryMatcher _matcher;
    private readonly IServerConfigurationManager _configurationManager;
    private readonly ILogger<SyncService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncService"/> class.
    /// </summary>
    /// <param name="playlistManager">Playlist manager.</param>
    /// <param name="collectionManager">Collection manager.</param>
    /// <param name="providerManager">Provider manager, to give the collection its images.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="userManager">User manager.</param>
    /// <param name="matcher">Library matcher.</param>
    /// <param name="configurationManager">Server configuration, for the default names.</param>
    /// <param name="logger">Logger.</param>
    public SyncService(
        IPlaylistManager playlistManager,
        ICollectionManager collectionManager,
        IProviderManager providerManager,
        ILibraryManager libraryManager,
        IUserManager userManager,
        LibraryMatcher matcher,
        IServerConfigurationManager configurationManager,
        ILogger<SyncService> logger)
    {
        _playlistManager = playlistManager;
        _collectionManager = collectionManager;
        _providerManager = providerManager;
        _libraryManager = libraryManager;
        _userManager = userManager;
        _matcher = matcher;
        _configurationManager = configurationManager;
        _logger = logger;
    }

    // shared by every user, so an unset name follows the server language
    private bool FrenchServer => _configurationManager.Configuration.UICulture?.StartsWith("fr", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Recomputes the playlists and the collection. Concurrent calls wait for the running one.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = PluginSettings.Current;
            try
            {
                var snapshot = _matcher.GetSnapshot();
                var entries = snapshot.Source.Data.Items.Where(e => settings.IncludeShorts || e.Type != EntryType.Short).ToList();
                IReadOnlyList<Guid> ItemsOf(TimelineEntry entry) =>
                    snapshot.Matches.TryGetValue(entry.Id, out var match) ? match.PlayableIds : [];

                settings.ReleasePlaylistId = settings.EnableReleasePlaylist
                    ? await SyncPlaylistAsync(
                        settings.ReleasePlaylistId,
                        NameOr(settings.ReleasePlaylistName, FrenchServer ? "MCU : ordre de sortie" : "MCU: release order"),
                        PlaylistPlanner.Plan(entries, TimelineOrder.Release, ItemsOf)).ConfigureAwait(false)
                    : Delete(settings.ReleasePlaylistId);

                cancellationToken.ThrowIfCancellationRequested();

                settings.ChronologicalPlaylistId = settings.EnableChronologicalPlaylist
                    ? await SyncPlaylistAsync(
                        settings.ChronologicalPlaylistId,
                        NameOr(settings.ChronologicalPlaylistName, FrenchServer ? "MCU : ordre chronologique" : "MCU: story order"),
                        PlaylistPlanner.Plan(entries, TimelineOrder.Chronological, ItemsOf)).ConfigureAwait(false)
                    : Delete(settings.ChronologicalPlaylistId);

                cancellationToken.ThrowIfCancellationRequested();

                // a series covering several entries is in it once
                var titles = entries
                    .Where(e => snapshot.Matches.ContainsKey(e.Id))
                    .Select(e => snapshot.Matches[e.Id].ItemId)
                    .Distinct()
                    .ToList();
                settings.CollectionId = settings.EnableCollection
                    ? await SyncCollectionAsync(settings.CollectionId, NameOr(settings.CollectionName, "Marvel Cinematic Universe"), titles, cancellationToken).ConfigureAwait(false)
                    : Delete(settings.CollectionId);

                settings.LastSyncError = string.Empty;
                _logger.LogInformation("[MCU Timeline] Playlists and collection synchronised.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                settings.LastSyncError = ex.Message;
                _logger.LogError(ex, "[MCU Timeline] Synchronisation failed.");
            }

            settings.LastSyncUtc = DateTime.UtcNow;
            PluginSettings.Save();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private async Task<string> SyncPlaylistAsync(string storedId, string name, IReadOnlyList<Guid> items)
    {
        var existing = Guid.TryParse(storedId, out var id) ? _libraryManager.GetItemById(id) as Playlist : null;
        if (existing is null)
        {
            var owner = ResolveOwner(PluginSettings.Current.PlaylistOwnerId)
                ?? throw new InvalidOperationException("No administrator found to own the playlists.");
            var result = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = name,
                ItemIdList = items,
                // set explicitly, an empty playlist would otherwise default to audio
                MediaType = MediaType.Video,
                UserId = owner.Id,
                Public = true
            }).ConfigureAwait(false);

            _logger.LogInformation("[MCU Timeline] Created playlist {Name} with {Count} items.", name, items.Count);
            return result.Id;
        }

        var current = existing.LinkedChildren.Select(c => c.ItemId ?? Guid.Empty).ToList();
        var sameItems = current.SequenceEqual(items);
        if (sameItems && existing.OpenAccess && string.Equals(existing.Name, name, StringComparison.Ordinal))
        {
            return storedId;
        }

        await _playlistManager.UpdatePlaylist(new PlaylistUpdateRequest
        {
            Id = existing.Id,
            // the owner always sees their playlist, whoever is configured as owner today
            UserId = existing.OwnerUserId,
            Name = name,
            Ids = sameItems ? null : items,
            Public = true
        }).ConfigureAwait(false);

        _logger.LogInformation("[MCU Timeline] Updated playlist {Name}, {Count} items.", name, items.Count);
        return storedId;
    }

    private async Task<string> SyncCollectionAsync(string storedId, string name, List<Guid> items, CancellationToken cancellationToken)
    {
        var existing = Guid.TryParse(storedId, out var id) ? _libraryManager.GetItemById(id) as BoxSet : null;
        if (existing is null)
        {
            var created = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
            {
                Name = name,
                ItemIdList = items.Select(i => i.ToString("N", System.Globalization.CultureInfo.InvariantCulture)).ToArray()
            }).ConfigureAwait(false);

            _logger.LogInformation("[MCU Timeline] Created collection {Name} with {Count} titles.", name, items.Count);
            await AddImagesAsync(created, cancellationToken).ConfigureAwait(false);
            return created.Id.ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        }

        await AddImagesAsync(existing, cancellationToken).ConfigureAwait(false);

        var current = existing.LinkedChildren.Select(c => c.ItemId ?? Guid.Empty).Where(i => i != Guid.Empty).ToHashSet();
        var added = items.Where(i => !current.Contains(i)).ToList();
        var wanted = items.ToHashSet();
        var removed = current.Where(i => !wanted.Contains(i)).ToList();
        if (added.Count > 0)
        {
            await _collectionManager.AddToCollectionAsync(existing.Id, added).ConfigureAwait(false);
        }

        if (removed.Count > 0)
        {
            await _collectionManager.RemoveFromCollectionAsync(existing.Id, removed).ConfigureAwait(false);
        }

        if (!string.Equals(existing.Name, name, StringComparison.Ordinal))
        {
            existing.Name = name;
            await existing.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
        }

        if (added.Count > 0 || removed.Count > 0)
        {
            _logger.LogInformation("[MCU Timeline] Updated collection {Name}, {Added} added, {Removed} removed.", name, added.Count, removed.Count);
        }

        return storedId;
    }

    // only where the collection has none, an image picked in Jellyfin stays
    private async Task AddImagesAsync(BoxSet collection, CancellationToken cancellationToken)
    {
        var added = false;
        foreach (var (type, file) in new[] { (ImageType.Primary, "collection-primary.jpg"), (ImageType.Backdrop, "collection-backdrop.jpg") })
        {
            if (collection.HasImage(type, 0))
            {
                continue;
            }

            var stream = typeof(SyncService).Assembly.GetManifestResourceStream("Jellyfin.Plugin.McuTimeline.Data." + file)
                ?? throw new FileNotFoundException("Missing embedded image.", file);
            await using (stream.ConfigureAwait(false))
            {
                await _providerManager.SaveImage(collection, stream, "image/jpeg", type, null, cancellationToken).ConfigureAwait(false);
            }

            added = true;
        }

        if (added)
        {
            await collection.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);
        }
    }

    // switched off: the playlist or collection goes, with its folder under the server data
    private string Delete(string storedId)
    {
        if (Guid.TryParse(storedId, out var id) && _libraryManager.GetItemById(id) is { } item)
        {
            _libraryManager.DeleteItem(item, new DeleteOptions { DeleteFileLocation = true });
            _logger.LogInformation("[MCU Timeline] Deleted {Name}, switched off in the settings.", item.Name);
        }

        return string.Empty;
    }

    private User? ResolveOwner(string configuredId)
    {
        if (Guid.TryParse(configuredId, out var id) && _userManager.GetUserById(id) is { } configured)
        {
            return configured;
        }

        return AllUsers()
            .Where(u => u.HasPermission(PermissionKind.IsAdministrator))
            .OrderBy(u => u.InternalId)
            .FirstOrDefault();
    }

    // a 10.11 patch release swapped the Users property for GetUsers(), no member is in both,
    // so whichever the running server has is looked up
    private IEnumerable<User> AllUsers()
    {
        var type = typeof(IUserManager);
        var users = type.GetMethod("GetUsers", Type.EmptyTypes)?.Invoke(_userManager, null)
            ?? type.GetProperty("Users")?.GetValue(_userManager);
        return users as IEnumerable<User> ?? throw new InvalidOperationException("The server lists no users.");
    }

    private static string NameOr(string name, string fallback) => string.IsNullOrWhiteSpace(name) ? fallback : name;
}
