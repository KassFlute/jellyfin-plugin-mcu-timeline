using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.McuTimeline.Configuration;

/// <summary>
/// Persisted plugin settings.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the libraries searched for the titles. Empty means every movie and
    /// show library.
    /// </summary>
    public string[] LibraryIds { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether One-Shots and Special Presentations are shown
    /// and added to the playlists.
    /// </summary>
    public bool IncludeShorts { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether announced titles are shown.
    /// </summary>
    public bool IncludeUpcoming { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the two playlists are maintained.
    /// </summary>
    public bool EnablePlaylists { get; set; } = true;

    /// <summary>
    /// Gets or sets the user who owns the playlists. Empty means the first administrator.
    /// </summary>
    public string PlaylistOwnerId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the release order playlist. Empty means a default in the
    /// server language.
    /// </summary>
    public string ReleasePlaylistName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the story order playlist. Empty means a default in the
    /// server language.
    /// </summary>
    public string ChronologicalPlaylistName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the timeline is listed in the side menu
    /// through the Plugin Pages plugin.
    /// </summary>
    public bool ShowInPluginPages { get; set; }

    /// <summary>
    /// Gets or sets the Jellyseerr address. Empty hides the request button.
    /// </summary>
    public string JellyseerrUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Jellyseerr API key.
    /// </summary>
    public string JellyseerrApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the id of the release order playlist. The plugin only ever touches the
    /// two playlists recorded here.
    /// </summary>
    public string ReleasePlaylistId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the id of the story order playlist.
    /// </summary>
    public string ChronologicalPlaylistId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the playlists were last synchronised, UTC.
    /// </summary>
    public DateTime? LastSyncUtc { get; set; }

    /// <summary>
    /// Gets or sets the error of the last synchronisation, empty when it succeeded.
    /// </summary>
    public string LastSyncError { get; set; } = string.Empty;
}
