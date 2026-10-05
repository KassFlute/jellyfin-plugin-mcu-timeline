using Jellyfin.Plugin.McuTimeline.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.McuTimeline;

/// <summary>
/// Registers plugin services in the server container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<TimelineDataProvider>();
        serviceCollection.AddSingleton<LibraryMatcher>();
        serviceCollection.AddSingleton<TimelineViewBuilder>();
        serviceCollection.AddSingleton<PlaylistSyncService>();
        serviceCollection.AddSingleton<IScheduledTask, PlaylistSyncTask>();
        serviceCollection.AddHostedService<LibraryChangeListener>();
    }
}
