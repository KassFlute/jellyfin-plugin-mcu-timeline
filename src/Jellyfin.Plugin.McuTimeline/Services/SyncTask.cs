using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.McuTimeline.Services;

/// <summary>
/// Daily playlist and collection synchronisation. Running it by hand from the dashboard syncs right away.
/// </summary>
public class SyncTask : IScheduledTask
{
    private readonly SyncService _syncService;
    private readonly LibraryMatcher _matcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncTask"/> class.
    /// </summary>
    /// <param name="syncService">Playlist and collection synchronisation.</param>
    /// <param name="matcher">Library matcher.</param>
    public SyncTask(SyncService syncService, LibraryMatcher matcher)
    {
        _syncService = syncService;
        _matcher = matcher;
    }

    /// <inheritdoc />
    public string Name => "Synchronise MCU playlists and collection";

    /// <inheritdoc />
    public string Key => "McuTimelinePlaylistSync";

    /// <inheritdoc />
    public string Description => "Recomputes the MCU playlists and collection, and removes the ones switched off.";

    /// <inheritdoc />
    public string Category => "MCU Timeline";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
#if JELLYFIN_10_11
            Type = TaskTriggerInfoType.IntervalTrigger,
#else
            Type = TaskTriggerInfo.TriggerInterval,
#endif
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
