using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.McuTimeline.Services;

/// <summary>
/// What a user said about titles beyond Jellyfin's own play state: seen elsewhere, for
/// titles the library does not have, and skipped, for titles left out of the progression.
/// Kept per user in the plugin data folder.
/// </summary>
public sealed class UserMarks
{
    private readonly ILogger<UserMarks> _logger;
    private readonly Lock _lock = new();
    private Dictionary<string, Marks>? _byUser;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserMarks"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public UserMarks(ILogger<UserMarks> logger)
    {
        _logger = logger;
    }

    private static string? FilePath => Plugin.Instance is { } plugin ? Path.Combine(plugin.DataFolderPath, "marks.json") : null;

    /// <summary>
    /// Gets a user's marks. The sets are copies.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <returns>Entries seen elsewhere, and entries skipped.</returns>
    public (IReadOnlySet<string> Watched, IReadOnlySet<string> Skipped) Get(Guid userId)
    {
        lock (_lock)
        {
            var marks = Load().GetValueOrDefault(Key(userId)) ?? new Marks();
            return (new HashSet<string>(marks.Watched, StringComparer.Ordinal), new HashSet<string>(marks.Skipped, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// Sets or clears the seen elsewhere mark.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="entryId">Entry id.</param>
    /// <param name="on">True to set it.</param>
    public void SetWatched(Guid userId, string entryId, bool on) => Update(userId, marks => Toggle(marks.Watched, entryId, on));

    /// <summary>
    /// Sets or clears the skipped mark.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="entryId">Entry id.</param>
    /// <param name="on">True to set it.</param>
    public void SetSkipped(Guid userId, string entryId, bool on) => Update(userId, marks => Toggle(marks.Skipped, entryId, on));

    private static string Key(Guid userId) => userId.ToString("N", CultureInfo.InvariantCulture);

    private static void Toggle(List<string> list, string entryId, bool on)
    {
        list.Remove(entryId);
        if (on)
        {
            list.Add(entryId);
        }
    }

    private void Update(Guid userId, Action<Marks> change)
    {
        lock (_lock)
        {
            var all = Load();
            if (!all.TryGetValue(Key(userId), out var marks))
            {
                marks = new Marks();
                all[Key(userId)] = marks;
            }

            change(marks);
            try
            {
                if (FilePath is { } path)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, JsonSerializer.Serialize(all));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "[MCU Timeline] Marks not saved.");
                throw;
            }
        }
    }

    private Dictionary<string, Marks> Load()
    {
        if (_byUser is not null)
        {
            return _byUser;
        }

        _byUser = new Dictionary<string, Marks>(StringComparer.Ordinal);
        try
        {
            if (FilePath is { } path && File.Exists(path)
                && JsonSerializer.Deserialize<Dictionary<string, Marks>>(File.ReadAllText(path)) is { } saved)
            {
                _byUser = new Dictionary<string, Marks>(saved, StringComparer.Ordinal);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "[MCU Timeline] Marks unreadable, starting empty.");
        }

        return _byUser;
    }

    private sealed class Marks
    {
        public List<string> Watched { get; set; } = [];

        public List<string> Skipped { get; set; } = [];
    }
}
