using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.McuTimeline.Services;

/// <summary>
/// Drops the match cache and resyncs the playlists and the collection when the library
/// changes: after a full scan, and after the real time monitor adds or removes a title.
/// </summary>
public sealed class LibraryChangeListener : IHostedService, IDisposable
{
    private const string LibraryScanTaskKey = "RefreshLibrary";

    // a scan fires item events by the hundred, wait for a quiet moment before syncing
    private static readonly TimeSpan _quietPeriod = TimeSpan.FromSeconds(30);

    private static readonly HashSet<BaseItemKind> _relevantKinds =
    [
        BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Season, BaseItemKind.Episode
    ];

    private readonly ILibraryManager _libraryManager;
    private readonly ITaskManager _taskManager;
    private readonly LibraryMatcher _matcher;
    private readonly SyncService _syncService;
    private readonly ILogger<LibraryChangeListener> _logger;
    private readonly Timer _timer;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryChangeListener"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="taskManager">Task manager.</param>
    /// <param name="matcher">Library matcher.</param>
    /// <param name="syncService">Playlist and collection synchronisation.</param>
    /// <param name="logger">Logger.</param>
    public LibraryChangeListener(
        ILibraryManager libraryManager,
        ITaskManager taskManager,
        LibraryMatcher matcher,
        SyncService syncService,
        ILogger<LibraryChangeListener> logger)
    {
        _libraryManager = libraryManager;
        _taskManager = taskManager;
        _matcher = matcher;
        _syncService = syncService;
        _logger = logger;
        _timer = new Timer(_ => RunSync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemChanged;
        _libraryManager.ItemUpdated += OnItemChanged;
        _libraryManager.ItemRemoved += OnItemChanged;
        _taskManager.TaskCompleted += OnTaskCompleted;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemChanged;
        _libraryManager.ItemUpdated -= OnItemChanged;
        _libraryManager.ItemRemoved -= OnItemChanged;
        _taskManager.TaskCompleted -= OnTaskCompleted;

        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => _timer.Dispose();

    private void OnItemChanged(object? sender, ItemChangeEventArgs e)
    {
        if (_relevantKinds.Contains(e.Item.GetBaseItemKind()))
        {
            _matcher.Invalidate();
            ScheduleSync();
        }
    }

    private void OnTaskCompleted(object? sender, TaskCompletionEventArgs e)
    {
        if (string.Equals(e.Task.ScheduledTask.Key, LibraryScanTaskKey, StringComparison.Ordinal))
        {
            _matcher.Invalidate();
            ScheduleSync();
        }
    }

    private void ScheduleSync() => _timer.Change(_quietPeriod, Timeout.InfiniteTimeSpan);

    private void RunSync()
    {
        if (_libraryManager.IsScanRunning)
        {
            // the end of the scan schedules another run
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _syncService.SyncAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MCU Timeline] Synchronisation after a library change failed.");
            }
        });
    }
}
