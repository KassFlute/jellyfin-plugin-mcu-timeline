using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.McuTimeline.Model;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Playlists;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.McuTimeline.Services;

/// <summary>
/// Creates the two playlists and keeps their content and order in step with the timeline.
/// Only touches the playlists whose ids are recorded in the settings.
/// </summary>
public sealed class PlaylistSyncService : IDisposable
{
    private readonly IPlaylistManager _playlistManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly LibraryMatcher _matcher;
    private readonly ILogger<PlaylistSyncService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistSyncService"/> class.
    /// </summary>
    /// <param name="playlistManager">Playlist manager.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="userManager">User manager.</param>
    /// <param name="matcher">Library matcher.</param>
    /// <param name="logger">Logger.</param>
    public PlaylistSyncService(
        IPlaylistManager playlistManager,
        ILibraryManager libraryManager,
        IUserManager userManager,
        LibraryMatcher matcher,
        ILogger<PlaylistSyncService> logger)
    {
        _playlistManager = playlistManager;
        _libraryManager = libraryManager;
        _userManager = userManager;
        _matcher = matcher;
        _logger = logger;
    }

    /// <summary>
    /// Recomputes both playlists. Concurrent calls wait for the running one.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = PluginSettings.Current;
            if (!settings.EnablePlaylists)
            {
                _logger.LogInformation("[MCU Timeline] Playlists are disabled, nothing to synchronise.");
                return;
            }

            try
            {
                var owner = ResolveOwner(settings.PlaylistOwnerId)
                    ?? throw new InvalidOperationException("No administrator found to own the playlists.");

                var snapshot = _matcher.GetSnapshot();
                var entries = snapshot.Source.Data.Items.Where(e => settings.IncludeShorts || e.Type != EntryType.Short).ToList();
                IReadOnlyList<Guid> ItemsOf(TimelineEntry entry) =>
                    snapshot.Matches.TryGetValue(entry.Id, out var match) ? match.PlayableIds : [];

                settings.ReleasePlaylistId = await SyncOneAsync(
                    settings.ReleasePlaylistId,
                    settings.ReleasePlaylistName,
                    PlaylistPlanner.Plan(entries, TimelineOrder.Release, ItemsOf),
                    owner).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                settings.ChronologicalPlaylistId = await SyncOneAsync(
                    settings.ChronologicalPlaylistId,
                    settings.ChronologicalPlaylistName,
                    PlaylistPlanner.Plan(entries, TimelineOrder.Chronological, ItemsOf),
                    owner).ConfigureAwait(false);

                settings.LastSyncError = string.Empty;
                _logger.LogInformation("[MCU Timeline] Playlists synchronised.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                settings.LastSyncError = ex.Message;
                _logger.LogError(ex, "[MCU Timeline] Playlist synchronisation failed.");
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

    private async Task<string> SyncOneAsync(string storedId, string name, IReadOnlyList<Guid> items, User owner)
    {
        var existing = Guid.TryParse(storedId, out var id) ? _libraryManager.GetItemById(id) as Playlist : null;
        if (existing is null)
        {
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

    private User? ResolveOwner(string configuredId)
    {
        if (Guid.TryParse(configuredId, out var id) && _userManager.GetUserById(id) is { } configured)
        {
            return configured;
        }

        return _userManager.GetUsers()
            .Where(u => u.HasPermission(PermissionKind.IsAdministrator))
            .OrderBy(u => u.InternalId)
            .FirstOrDefault();
    }
}
