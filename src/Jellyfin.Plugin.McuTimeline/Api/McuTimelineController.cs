using System.Net.Mime;
using Jellyfin.Plugin.McuTimeline.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.McuTimeline.Api;

/// <summary>
/// Serves the timeline page and its data.
/// </summary>
[ApiController]
[Route("McuTimeline")]
public class McuTimelineController : ControllerBase
{
    private const string WebResourcePrefix = "Jellyfin.Plugin.McuTimeline.Web.";

    private static readonly Dictionary<string, string> _assets = new(StringComparer.Ordinal)
    {
        ["timeline.css"] = "text/css; charset=utf-8",
        ["timeline.js"] = "text/javascript; charset=utf-8",
        ["strings-fr.json"] = "application/json; charset=utf-8"
    };

    private readonly TimelineViewBuilder _viewBuilder;
    private readonly LibraryMatcher _matcher;
    private readonly TimelineDataProvider _dataProvider;
    private readonly PlaylistSyncService _syncService;
    private readonly IAuthorizationContext _authorizationContext;
    private readonly ISessionManager _sessionManager;
    private readonly IServerApplicationHost _applicationHost;

    /// <summary>
    /// Initializes a new instance of the <see cref="McuTimelineController"/> class.
    /// </summary>
    /// <param name="viewBuilder">Per user timeline builder.</param>
    /// <param name="matcher">Library matcher.</param>
    /// <param name="dataProvider">Timeline data.</param>
    /// <param name="syncService">Playlist synchronisation.</param>
    /// <param name="authorizationContext">Authorization context of the caller.</param>
    /// <param name="sessionManager">Session manager.</param>
    /// <param name="applicationHost">Server host.</param>
    public McuTimelineController(
        TimelineViewBuilder viewBuilder,
        LibraryMatcher matcher,
        TimelineDataProvider dataProvider,
        PlaylistSyncService syncService,
        IAuthorizationContext authorizationContext,
        ISessionManager sessionManager,
        IServerApplicationHost applicationHost)
    {
        _viewBuilder = viewBuilder;
        _matcher = matcher;
        _dataProvider = dataProvider;
        _syncService = syncService;
        _authorizationContext = authorizationContext;
        _sessionManager = sessionManager;
        _applicationHost = applicationHost;
    }

    /// <summary>
    /// Serves the timeline page. The page holds no data: a plain navigation carries no
    /// Jellyfin credentials, so the page reads the web client session and every data call
    /// below requires it, like the web client itself.
    /// </summary>
    /// <response code="200">Page returned.</response>
    /// <returns>The HTML page.</returns>
    [HttpGet("page")]
    [AllowAnonymous]
    [Produces(MediaTypeNames.Text.Html)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetPage() => Embedded("timeline.html", "text/html; charset=utf-8");

    /// <summary>
    /// Serves the page stylesheet, script and strings.
    /// </summary>
    /// <param name="file">File name.</param>
    /// <response code="200">File returned.</response>
    /// <response code="404">Unknown file.</response>
    /// <returns>The file.</returns>
    [HttpGet("assets/{file}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetAsset([FromRoute] string file)
    {
        return _assets.TryGetValue(file, out var contentType) ? Embedded(file, contentType) : NotFound();
    }

    /// <summary>
    /// Returns the timeline merged with the library and the calling user's play state.
    /// </summary>
    /// <response code="200">Timeline returned.</response>
    /// <returns>Every entry, unsorted.</returns>
    [HttpGet("items")]
    [Authorize]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TimelineResponse>> GetItems()
    {
        var auth = await _authorizationContext.GetAuthorizationInfo(Request).ConfigureAwait(false);
        if (auth.User is null)
        {
            return Unauthorized();
        }

        var (version, items) = _viewBuilder.Build(auth.User);
        return new TimelineResponse
        {
            Version = version,
            ServerId = _applicationHost.SystemId,
            UserId = auth.User.Id.ToString("N", System.Globalization.CultureInfo.InvariantCulture),
            Items = items
        };
    }

    /// <summary>
    /// Starts or resumes an entry in the caller's own web client.
    /// </summary>
    /// <param name="entryId">Entry id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">Playback sent to the web client.</response>
    /// <response code="404">The entry is not playable for this user.</response>
    /// <response code="409">No web client session to play in, the page opens the item instead.</response>
    /// <returns>No content.</returns>
    [HttpPost("play/{entryId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Play([FromRoute] string entryId, CancellationToken cancellationToken)
    {
        var auth = await _authorizationContext.GetAuthorizationInfo(Request).ConfigureAwait(false);
        if (auth.User is null)
        {
            return Unauthorized();
        }

        if (_viewBuilder.ResolvePlayback(entryId, auth.User) is not { } playback)
        {
            return NotFound();
        }

        // the page shares the web client token, so the token's device is the web client
        // the user is looking at
        var session = _sessionManager.Sessions
            .Where(s => s.UserId.Equals(auth.User.Id)
                && string.Equals(s.DeviceId, auth.DeviceId, StringComparison.Ordinal)
                && s.SupportsMediaControl)
            .OrderByDescending(s => s.LastActivityDate)
            .FirstOrDefault();
        if (session is null)
        {
            return Conflict();
        }

        await _sessionManager.SendPlayCommand(
            null!,
            session.Id,
            new PlayRequest
            {
                ItemIds = [playback.ItemId],
                StartPositionTicks = playback.StartTicks,
                PlayCommand = PlayCommand.PlayNow,
                ControllingUserId = auth.User.Id
            },
            cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Returns the matching state for the configuration page.
    /// </summary>
    /// <response code="200">State returned.</response>
    /// <returns>Data version, found and missing titles, last synchronisation.</returns>
    [HttpGet("status")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<StatusResponse> GetStatus()
    {
        var source = _dataProvider.Get();
        var snapshot = _matcher.GetSnapshot();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var visible = TimelineViewBuilder.VisibleEntries(snapshot.Source.Data, today).ToList();
        var released = visible.Where(e => !e.IsUpcoming(today)).ToList();
        var settings = PluginSettings.Current;

        return new StatusResponse
        {
            DataVersion = source.Data.Version,
            DataSource = source.Kind == TimelineSourceKind.Override ? "override" : "embedded",
            OverridePath = _dataProvider.OverridePath,
            OverrideError = source.OverrideError,
            Total = released.Count,
            Found = released.Count(e => snapshot.Matches.ContainsKey(e.Id)),
            Upcoming = visible.Count - released.Count,
            Missing = released
                .Where(e => !snapshot.Matches.ContainsKey(e.Id))
                .Select(e => new MissingTitleDto(e.Id, e.Title, TimelineViewBuilder.TypeName(e.Type), e.TmdbId, e.ImdbId))
                .ToList(),
            LastSyncUtc = settings.LastSyncUtc,
            LastSyncError = string.IsNullOrEmpty(settings.LastSyncError) ? null : settings.LastSyncError
        };
    }

    /// <summary>
    /// Synchronises the playlists now.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Synchronisation done, state returned.</response>
    /// <returns>The state after the synchronisation.</returns>
    [HttpPost("sync")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<StatusResponse>> Sync(CancellationToken cancellationToken)
    {
        // a manual sync means the admin just fixed something, start from a fresh match
        _matcher.Invalidate();
        await _syncService.SyncAsync(cancellationToken).ConfigureAwait(false);
        return GetStatus();
    }

    private FileStreamResult Embedded(string name, string contentType)
    {
        var stream = typeof(McuTimelineController).Assembly.GetManifestResourceStream(WebResourcePrefix + name)
            ?? throw new FileNotFoundException("Missing embedded web resource.", name);
        Response.Headers.CacheControl = "no-cache";
        return File(stream, contentType);
    }
}
