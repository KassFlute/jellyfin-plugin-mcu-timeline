using Jellyfin.Plugin.McuTimeline.Model;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.McuTimeline.Services;

/// <summary>
/// Serves the timeline data: the local override when present and valid, the embedded file
/// otherwise. The override is re-read whenever it changes on disk, no restart needed.
/// </summary>
public class TimelineDataProvider
{
    /// <summary>
    /// File name of the override, next to the plugin configuration.
    /// </summary>
    public const string OverrideFileName = "mcu-timeline.json";

    private const string EmbeddedResource = "Jellyfin.Plugin.McuTimeline.Data.mcu-timeline.json";

    private readonly ILogger<TimelineDataProvider> _logger;
    private readonly Lock _lock = new();
    private readonly TimelineSource _embedded;
    private TimelineSource? _current;
    private (bool Exists, DateTime WriteTime, long Length) _overrideStamp;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimelineDataProvider"/> class.
    /// </summary>
    /// <param name="applicationPaths">Server paths.</param>
    /// <param name="logger">Logger.</param>
    public TimelineDataProvider(IApplicationPaths applicationPaths, ILogger<TimelineDataProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);
        _logger = logger;
        OverridePath = Path.Combine(applicationPaths.PluginConfigurationsPath, OverrideFileName);
        _embedded = LoadEmbedded();
    }

    /// <summary>
    /// Gets the full path of the override file.
    /// </summary>
    public string OverridePath { get; }

    /// <summary>
    /// Returns the data in use.
    /// </summary>
    /// <returns>Data and where it comes from.</returns>
    public TimelineSource Get()
    {
        var stamp = ReadStamp();
        lock (_lock)
        {
            if (_current is not null && stamp == _overrideStamp)
            {
                return _current;
            }

            _overrideStamp = stamp;
            _current = stamp.Exists ? LoadOverride() : _embedded;
            return _current;
        }
    }

    private (bool Exists, DateTime WriteTime, long Length) ReadStamp()
    {
        var info = new FileInfo(OverridePath);
        return info.Exists ? (true, info.LastWriteTimeUtc, info.Length) : (false, default, 0);
    }

    private TimelineSource LoadOverride()
    {
        try
        {
            var data = TimelineParser.Parse(File.ReadAllText(OverridePath));
            _logger.LogInformation(
                "[MCU Timeline] Using the local override {Path}, data version {Version}, {Count} titles.",
                OverridePath,
                data.Version,
                data.Items.Count);
            return new TimelineSource(data, TimelineSourceKind.Override, null);
        }
        catch (Exception ex) when (ex is TimelineDataException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(
                "[MCU Timeline] The local override {Path} is ignored, the embedded data stays in use: {Error}",
                OverridePath,
                ex.Message);
            return _embedded with { OverrideError = ex.Message };
        }
    }

    private TimelineSource LoadEmbedded()
    {
        using var stream = typeof(TimelineDataProvider).Assembly.GetManifestResourceStream(EmbeddedResource)
            ?? throw new InvalidOperationException("Embedded timeline data is missing from the assembly.");
        using var reader = new StreamReader(stream);
        try
        {
            return new TimelineSource(TimelineParser.Parse(reader.ReadToEnd()), TimelineSourceKind.Embedded, null);
        }
        catch (TimelineDataException ex)
        {
            // a broken embedded file is a packaging bug, keep the server up and show it
            _logger.LogError("[MCU Timeline] The embedded data is invalid: {Error}", ex.Message);
            return new TimelineSource(new TimelineData("invalid", []), TimelineSourceKind.Embedded, null);
        }
    }
}
