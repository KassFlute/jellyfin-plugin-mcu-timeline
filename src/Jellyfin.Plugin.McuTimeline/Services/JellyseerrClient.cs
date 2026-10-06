using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.McuTimeline.Model;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.McuTimeline.Services;

/// <summary>
/// Requests missing titles from Jellyseerr on behalf of the Jellyfin user.
/// </summary>
public sealed class JellyseerrClient
{
    private static readonly TimeSpan _cacheLife = TimeSpan.FromMinutes(10);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<JellyseerrClient> _logger;
    private readonly ConcurrentDictionary<string, (string? Status, DateTime FetchedUtc)> _statuses = new(StringComparer.Ordinal);
    private (Dictionary<string, int> Ids, DateTime FetchedUtc)? _users;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyseerrClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public JellyseerrClient(IHttpClientFactory httpClientFactory, ILogger<JellyseerrClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether an address and a key are set.
    /// </summary>
    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PluginSettings.Current.JellyseerrUrl)
        && !string.IsNullOrWhiteSpace(PluginSettings.Current.JellyseerrApiKey);

    /// <summary>
    /// Returns where each title stands in Jellyseerr: pending, processing or available.
    /// </summary>
    /// <param name="entries">Titles the library does not have.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status by entry id. Titles never requested are left out.</returns>
    public async Task<IReadOnlyDictionary<string, string>> GetStatusesAsync(IEnumerable<TimelineEntry> entries, CancellationToken cancellationToken)
    {
        var result = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        using var gate = new SemaphoreSlim(6);
        var now = DateTime.UtcNow;

        await Task.WhenAll(entries.Select(async entry =>
        {
            var key = MediaPath(entry);
            if (!_statuses.TryGetValue(key, out var cached) || now - cached.FetchedUtc > _cacheLife)
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var media = await SendAsync(HttpMethod.Get, "/api/v1/" + key, null, null, cancellationToken).ConfigureAwait(false);
                    cached = (StatusName(media?["mediaInfo"]?["status"]?.GetValue<int>()), DateTime.UtcNow);
                }
                catch (JellyseerrException ex)
                {
                    _logger.LogWarning("[MCU Timeline] Jellyseerr status of {Title}: {Error}", entry.Title.Default, ex.Message);
                    cached = (null, now);
                }
                finally
                {
                    gate.Release();
                }

                _statuses[key] = cached;
            }

            if (cached.Status is not null)
            {
                result[entry.Id] = cached.Status;
            }
        })).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Requests a title as the given Jellyfin user, with that user's Jellyseerr rights and quota.
    /// </summary>
    /// <param name="entry">Title to request.</param>
    /// <param name="jellyfinUserId">Jellyfin user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The title status after the request.</returns>
    /// <exception cref="JellyseerrException">Jellyseerr refused the request or could not be reached.</exception>
    public async Task<string> RequestAsync(TimelineEntry entry, Guid jellyfinUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var users = await GetUsersAsync(cancellationToken).ConfigureAwait(false);
        if (!users.TryGetValue(jellyfinUserId.ToString("N", CultureInfo.InvariantCulture), out var seerrUserId))
        {
            throw new JellyseerrException(JellyseerrException.NotLinked);
        }

        var body = new JsonObject
        {
            ["mediaType"] = entry.IsSeries ? "tv" : "movie",
            ["mediaId"] = entry.TmdbId
        };
        if (entry.IsSeries)
        {
            body["seasons"] = entry.Seasons.Count > 0 ? new JsonArray(entry.Seasons.Select(s => JsonValue.Create(s)).ToArray<JsonNode?>()) : "all";
        }

        var created = await SendAsync(HttpMethod.Post, "/api/v1/request", body, seerrUserId, cancellationToken).ConfigureAwait(false);
        var status = StatusName(created?["media"]?["status"]?.GetValue<int>()) ?? "pending";
        _statuses[MediaPath(entry)] = (status, DateTime.UtcNow);
        return status;
    }

    private static string MediaPath(TimelineEntry entry) =>
        string.Create(CultureInfo.InvariantCulture, $"{(entry.IsSeries ? "tv" : "movie")}/{entry.TmdbId}");

    // Jellyseerr MediaStatus: 2 pending, 3 processing, 4 partially available, 5 available
    private static string? StatusName(int? status) => status switch
    {
        2 => "pending",
        3 => "processing",
        4 or 5 => "available",
        _ => null
    };

    private async Task<Dictionary<string, int>> GetUsersAsync(CancellationToken cancellationToken)
    {
        if (_users is { } users && DateTime.UtcNow - users.FetchedUtc < _cacheLife)
        {
            return users.Ids;
        }

        var page = await SendAsync(HttpMethod.Get, "/api/v1/user?take=1000", null, null, cancellationToken).ConfigureAwait(false);
        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in page?["results"]?.AsArray() ?? [])
        {
            var jellyfinId = user?["jellyfinUserId"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(jellyfinId) && user?["id"]?.GetValue<int>() is { } id)
            {
                ids[jellyfinId.Replace("-", string.Empty, StringComparison.Ordinal)] = id;
            }
        }

        _users = (ids, DateTime.UtcNow);
        return ids;
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body, int? asUser, CancellationToken cancellationToken)
    {
        var settings = PluginSettings.Current;
        using var request = new HttpRequestMessage(method, settings.JellyseerrUrl.TrimEnd('/') + path);
        request.Headers.Add("X-Api-Key", settings.JellyseerrApiKey);
        if (asUser is { } userId)
        {
            request.Headers.Add("X-Api-User", userId.ToString(CultureInfo.InvariantCulture));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        try
        {
            using var response = await _httpClientFactory.CreateClient(NamedClient.Default)
                .SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var json = text.Length > 0 ? JsonNode.Parse(text) : null;
            if (!response.IsSuccessStatusCode)
            {
                var message = json is JsonObject error && error["message"] is JsonValue value ? value.ToString() : null;
                throw new JellyseerrException(JellyseerrException.Refused, message ?? $"HTTP {(int)response.StatusCode}");
            }

            return json;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or UriFormatException)
        {
            throw new JellyseerrException(JellyseerrException.Unreachable, ex.Message, ex);
        }
    }
}

/// <summary>
/// A Jellyseerr call failed. <see cref="Code"/> tells the page which text to show.
/// </summary>
public sealed class JellyseerrException : Exception
{
    /// <summary>The Jellyfin account has no Jellyseerr user.</summary>
    public const string NotLinked = "notLinked";

    /// <summary>Jellyseerr could not be reached.</summary>
    public const string Unreachable = "unreachable";

    /// <summary>Jellyseerr answered with an error.</summary>
    public const string Refused = "refused";

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyseerrException"/> class.
    /// </summary>
    public JellyseerrException()
        : this(Refused)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyseerrException"/> class.
    /// </summary>
    /// <param name="code">One of the codes above.</param>
    public JellyseerrException(string code)
        : this(code, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyseerrException"/> class.
    /// </summary>
    /// <param name="code">One of the codes above.</param>
    /// <param name="seerrMessage">Jellyseerr's own message, if any.</param>
    public JellyseerrException(string code, string? seerrMessage)
        : base(seerrMessage ?? code)
    {
        Code = code;
        SeerrMessage = seerrMessage;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyseerrException"/> class.
    /// </summary>
    /// <param name="code">One of the codes above.</param>
    /// <param name="message">Detail for the log.</param>
    /// <param name="innerException">Cause.</param>
    public JellyseerrException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>
    /// Gets the error code.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets Jellyseerr's own message for a refusal.
    /// </summary>
    public string? SeerrMessage { get; }
}
