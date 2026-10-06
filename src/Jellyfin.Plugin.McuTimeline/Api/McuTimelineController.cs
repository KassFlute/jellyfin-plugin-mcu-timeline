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
/// Serves the timeline assets and data.
/// </summary>
[ApiController]
[Route("McuTimeline")]
public class McuTimelineController : ControllerBase
{
    private const string WebResourcePrefix = "Jellyfin.Plugin.McuTimeline.Web.";

    private static readonly Dictionary<string, string> _assets = new(StringComparer.Ordinal)
    {
        ["timeline.css"] = "text/css; charset=utf-8",
        ["strings-fr.json"] = "application/json; charset=utf-8",
        ["strings-en.json"] = "application/json; charset=utf-8"
    };

    private readonly TimelineViewBuilder _viewBuilder;
    private readonly LibraryMatcher _matcher;
    private readonly TimelineDataProvider _dataProvider;
    private readonly PlaylistSyncService _syncService;
    private readonly IAuthorizationContext _authorizationContext;
    private readonly ISessionManager _sessionManager;
    private readonly IServerApplicationHost _applicationHost;
    private readonly PosterService _posterService;
    private readonly JellyseerrClient _jellyseerr;

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
    /// <param name="posterService">Posters of missing titles.</param>
    /// <param name="jellyseerr">Jellyseerr client.</param>
    public McuTimelineController(
        TimelineViewBuilder viewBuilder,
        LibraryMatcher matcher,
        TimelineDataProvider dataProvider,
        PlaylistSyncService syncService,
        IAuthorizationContext authorizationContext,
        ISessionManager sessionManager,
        IServerApplicationHost applicationHost,
        PosterService posterService,
        JellyseerrClient jellyseerr)
    {
        _viewBuilder = viewBuilder;
        _matcher = matcher;
        _dataProvider = dataProvider;
        _syncService = syncService;
        _authorizationContext = authorizationContext;
        _sessionManager = sessionManager;
        _applicationHost = applicationHost;
        _posterService = posterService;
        _jellyseerr = jellyseerr;
    }

    /// <summary>
    /// Serves the page Plugin Pages loads into the web client. It only loads the script,
    /// which draws the timeline in place.
    /// </summary>
    /// <response code="200">Page returned.</response>
    /// <returns>An HTML fragment.</returns>
    [HttpGet("page")]
    [Authorize]
    [Produces(MediaTypeNames.Text.Html)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ContentResult GetPage()
    {
        Response.Headers.CacheControl = "no-cache";
        var script = $"{Request.PathBase}/McuTimeline/assets/timeline.js?v={WebAssetVersion.Value}";
        return Content(
            $"<div class=\"mcuTimelineHost\"></div><script src=\"{script}\"></script>",
            "text/html; charset=utf-8");
    }

    /// <summary>
    /// Serves the timeline script. Open to all, a script tag carries no token.
    /// </summary>
    /// <response code="200">Script returned.</response>
    /// <returns>The script.</returns>
    [HttpGet("assets/timeline.js")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetScript() => Embedded("timeline.js", "text/javascript; charset=utf-8");

    /// <summary>
    /// Serves the script that adds the entry under Media, loaded with the web client.
    /// </summary>
    /// <response code="200">Script returned.</response>
    /// <returns>The script.</returns>
    [HttpGet("assets/menu.js")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetMenuScript() => Embedded("menu.js", "text/javascript; charset=utf-8");

    /// <summary>
    /// Serves the stylesheet and the strings, fetched by the script with the user's token.
    /// </summary>
    /// <param name="file">File name.</param>
    /// <response code="200">File returned.</response>
    /// <response code="404">Unknown file.</response>
    /// <returns>The file.</returns>
    [HttpGet("assets/{file}")]
    [Authorize]
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
    /// <param name="language">Web client language, such as fr or en-US.</param>
    /// <returns>Every entry, unsorted.</returns>
    [HttpGet("items")]
    [Authorize]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TimelineResponse>> GetItems([FromQuery] string? language)
    {
        var auth = await _authorizationContext.GetAuthorizationInfo(Request).ConfigureAwait(false);
        if (auth.User is null)
        {
            return Unauthorized();
        }

        var (version, items) = _viewBuilder.Build(auth.User, language);
        return new TimelineResponse
        {
            Version = version,
            ServerId = _applicationHost.SystemId,
            UserId = auth.User.Id.ToString("N", System.Globalization.CultureInfo.InvariantCulture),
            CanRequest = JellyseerrClient.IsConfigured,
            Items = items
        };
    }

    /// <summary>
    /// Tells the menu script whether to add the entry under Media.
    /// </summary>
    /// <response code="200">Setting returned.</response>
    /// <returns>Whether the entry is shown.</returns>
    [HttpGet("menu")]
    [Authorize]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<MenuDto> GetMenu() => new MenuDto(PluginSettings.Current.ShowInPluginPages);

    /// <summary>
    /// Marks an entry as seen by the calling user: in Jellyfin when the library has it,
    /// else as seen elsewhere.
    /// </summary>
    /// <param name="entryId">Entry id.</param>
    /// <param name="language">Web client language.</param>
    /// <response code="200">Done, the entry is returned as it now stands.</response>
    /// <response code="404">Unknown entry, or not released yet.</response>
    /// <returns>The entry.</returns>
    [HttpPost("played/{entryId}")]
    [Authorize]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<TimelineItemDto>> MarkPlayed([FromRoute] string entryId, [FromQuery] string? language) => SetPlayed(entryId, true, language);

    /// <summary>
    /// Marks an entry as not seen by the calling user.
    /// </summary>
    /// <param name="entryId">Entry id.</param>
    /// <param name="language">Web client language.</param>
    /// <response code="200">Done, the entry is returned as it now stands.</response>
    /// <response code="404">Unknown entry, or not released yet.</response>
    /// <returns>The entry.</returns>
    [HttpDelete("played/{entryId}")]
    [Authorize]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<TimelineItemDto>> MarkUnplayed([FromRoute] string entryId, [FromQuery] string? language) => SetPlayed(entryId, false, language);

    /// <summary>
    /// Returns TMDB posters for the titles the calling user does not have. Slow on the first
    /// call, the page asks for them after drawing the timeline.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Posters returned.</response>
    /// <returns>Poster address by entry id.</returns>
    [HttpGet("posters")]
    [Authorize]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyDictionary<string, string>>> GetPosters(CancellationToken cancellationToken)
    {
        var missing = await MissingEntriesAsync().ConfigureAwait(false);
        if (missing is null)
        {
            return Unauthorized();
        }

        return Ok(await _posterService.GetPostersAsync(missing, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Returns where the titles the calling user does not have stand in Jellyseerr.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Statuses returned, empty without Jellyseerr.</response>
    /// <returns>pending, processing or available by entry id.</returns>
    [HttpGet("requests")]
    [Authorize]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyDictionary<string, string>>> GetRequests(CancellationToken cancellationToken)
    {
        var missing = await MissingEntriesAsync().ConfigureAwait(false);
        if (missing is null)
        {
            return Unauthorized();
        }

        return Ok(JellyseerrClient.IsConfigured
            ? await _jellyseerr.GetStatusesAsync(missing, cancellationToken).ConfigureAwait(false)
            : new Dictionary<string, string>());
    }

    /// <summary>
    /// Requests a title from Jellyseerr as the calling user.
    /// </summary>
    /// <param name="entryId">Entry id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Request made, new status returned.</response>
    /// <response code="404">Unknown entry, or Jellyseerr not set up.</response>
    /// <response code="502">Jellyseerr refused or is unreachable, the error code says which.</response>
    /// <returns>The status.</returns>
    [HttpPost("request/{entryId}")]
    [Authorize]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<RequestResultDto>> RequestTitle([FromRoute] string entryId, CancellationToken cancellationToken)
    {
        var auth = await _authorizationContext.GetAuthorizationInfo(Request).ConfigureAwait(false);
        if (auth.User is null)
        {
            return Unauthorized();
        }

        var entry = _dataProvider.Get().Data.Items.FirstOrDefault(e => string.Equals(e.Id, entryId, StringComparison.Ordinal));
        if (entry is null || !JellyseerrClient.IsConfigured)
        {
            return NotFound();
        }

        try
        {
            return new RequestResultDto(await _jellyseerr.RequestAsync(entry, auth.User.Id, cancellationToken).ConfigureAwait(false), null, null);
        }
        catch (JellyseerrException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new RequestResultDto(null, ex.Code, ex.SeerrMessage));
        }
    }

    /// <summary>
    /// Starts or resumes an entry in the caller's own web client.
    /// </summary>
    /// <param name="entryId">Entry id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">Playback sent to the web client.</response>
    /// <response code="404">The entry is not playable for this user.</response>
    /// <response code="409">No web client session to play in, the timeline opens the item instead.</response>
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

        // the timeline runs in the web client with its token, so the token's device is the
        // web client the user is looking at
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
    /// <param name="language">Dashboard language, for the titles.</param>
    /// <returns>Data version, found and missing titles, last synchronisation.</returns>
    [HttpGet("status")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<StatusResponse> GetStatus([FromQuery] string? language)
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
                .Select(e => new MissingTitleDto(e.Id, e.Title.For(language), TimelineViewBuilder.TypeName(e.Type), e.TmdbId, e.ImdbId))
                .ToList(),
            LastSyncUtc = settings.LastSyncUtc,
            LastSyncError = string.IsNullOrEmpty(settings.LastSyncError) ? null : settings.LastSyncError
        };
    }

    /// <summary>
    /// Synchronises the playlists now.
    /// </summary>
    /// <param name="language">Dashboard language, for the titles.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Synchronisation done, state returned.</response>
    /// <returns>The state after the synchronisation.</returns>
    [HttpPost("sync")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<StatusResponse>> Sync([FromQuery] string? language, CancellationToken cancellationToken)
    {
        // a manual sync means the admin just fixed something, start from a fresh match
        _matcher.Invalidate();
        await _syncService.SyncAsync(cancellationToken).ConfigureAwait(false);
        return GetStatus(language);
    }

    /// <summary>
    /// Leaves an entry out of the calling user's progression.
    /// </summary>
    /// <param name="entryId">Entry id.</param>
    /// <param name="language">Web client language.</param>
    /// <response code="200">Done, the entry is returned as it now stands.</response>
    /// <response code="404">Unknown entry.</response>
    /// <returns>The entry.</returns>
    [HttpPost("skipped/{entryId}")]
    [Authorize]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<TimelineItemDto>> Skip([FromRoute] string entryId, [FromQuery] string? language) =>
        Change(user => _viewBuilder.SetSkipped(entryId, user, true, language));

    /// <summary>
    /// Brings an entry back into the calling user's progression.
    /// </summary>
    /// <param name="entryId">Entry id.</param>
    /// <param name="language">Web client language.</param>
    /// <response code="200">Done, the entry is returned as it now stands.</response>
    /// <response code="404">Unknown entry.</response>
    /// <returns>The entry.</returns>
    [HttpDelete("skipped/{entryId}")]
    [Authorize]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<TimelineItemDto>> Unskip([FromRoute] string entryId, [FromQuery] string? language) =>
        Change(user => _viewBuilder.SetSkipped(entryId, user, false, language));

    private Task<ActionResult<TimelineItemDto>> SetPlayed(string entryId, bool played, string? language) =>
        Change(user => _viewBuilder.SetPlayed(entryId, user, played, language));

    private async Task<ActionResult<TimelineItemDto>> Change(Func<Jellyfin.Database.Implementations.Entities.User, TimelineItemDto?> change)
    {
        var auth = await _authorizationContext.GetAuthorizationInfo(Request).ConfigureAwait(false);
        if (auth.User is null)
        {
            return Unauthorized();
        }

        return change(auth.User) is { } item ? item : NotFound();
    }

    private async Task<List<Model.TimelineEntry>?> MissingEntriesAsync()
    {
        var auth = await _authorizationContext.GetAuthorizationInfo(Request).ConfigureAwait(false);
        if (auth.User is null)
        {
            return null;
        }

        var missing = _viewBuilder.Build(auth.User).Items
            .Where(i => i.Status != "owned")
            .Select(i => i.Id)
            .ToHashSet(StringComparer.Ordinal);
        return _dataProvider.Get().Data.Items.Where(e => missing.Contains(e.Id)).ToList();
    }

    private FileStreamResult Embedded(string name, string contentType)
    {
        var stream = typeof(McuTimelineController).Assembly.GetManifestResourceStream(WebResourcePrefix + name)
            ?? throw new FileNotFoundException("Missing embedded web resource.", name);
        Response.Headers.CacheControl = "no-cache";
        return File(stream, contentType);
    }
}
