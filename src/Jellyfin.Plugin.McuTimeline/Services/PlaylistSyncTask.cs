using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.McuTimeline.Services;

/// <summary>
/// Daily playlist synchronisation. Running it by hand from the dashboard syncs right away.
/// </summary>
public class PlaylistSyncTask : IScheduledTask
{
    private readonly PlaylistSyncService _syncService;
    private readonly LibraryMatcher _matcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistSyncTask"/> class.
    /// </summary>
    /// <param name="syncService">Playlist synchronisation.</param>
    /// <param name="matcher">Library matcher.</param>
    public PlaylistSyncTask(PlaylistSyncService syncService, LibraryMatcher matcher)
    {
        _syncService = syncService;
        _matcher = matcher;
    }

    /// <inheritdoc />
    public string Name => "Synchronise MCU playlists";

    /// <inheritdoc />
    public string Key => "McuTimelinePlaylistSync";

    /// <inheritdoc />
    public string Description => "Recomputes the content and order of the two MCU playlists.";

    /// <inheritdoc />
    public string Category => "MCU Timeline";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromDays(1).Ticks
        };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _matcher.Invalidate();
        await _syncService.SyncAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }
}
