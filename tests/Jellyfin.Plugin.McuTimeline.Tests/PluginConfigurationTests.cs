using Jellyfin.Plugin.McuTimeline.Configuration;
using Xunit;

namespace Jellyfin.Plugin.McuTimeline.Tests;

public class PluginConfigurationTests
{
    [Fact]
    public void Migrate_NewInstall_ShowsNothing()
    {
        var config = new PluginConfiguration();

        Assert.True(config.Migrate());
        Assert.False(config.EnableReleasePlaylist);
        Assert.False(config.EnableChronologicalPlaylist);
        Assert.False(config.EnableCollection);
        Assert.False(config.ShowInPluginPages);
    }

    [Fact]
    public void Migrate_PlaylistsMadeBefore_StayOn()
    {
        // version 1 left EnablePlaylists out of the file while it was on
        var config = new PluginConfiguration { ReleasePlaylistId = "a", ChronologicalPlaylistId = "b" };

        config.Migrate();

        Assert.True(config.EnableReleasePlaylist);
        Assert.True(config.EnableChronologicalPlaylist);
        Assert.False(config.EnableCollection);
        Assert.Null(config.EnablePlaylists);
    }

    [Fact]
    public void Migrate_PlaylistsSwitchedOff_StayOff()
    {
        var config = new PluginConfiguration { EnablePlaylists = false, ReleasePlaylistId = "a", ChronologicalPlaylistId = "b" };

        config.Migrate();

        Assert.False(config.EnableReleasePlaylist);
        Assert.False(config.EnableChronologicalPlaylist);
    }

    [Fact]
    public void Migrate_RunsOnce()
    {
        var config = new PluginConfiguration();
        config.Migrate();
        config.ReleasePlaylistId = "a";

        Assert.False(config.Migrate());
        Assert.False(config.EnableReleasePlaylist);
    }
}
