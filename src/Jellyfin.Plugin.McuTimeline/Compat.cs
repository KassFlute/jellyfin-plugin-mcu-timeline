// what moved between the Jellyfin versions the plugin is built for, see JellyfinAbi in the csproj
#if JELLYFIN_10_11
global using User = Jellyfin.Database.Implementations.Entities.User;
#else
global using User = Jellyfin.Data.Entities.User;
#endif

#if !NET9_0_OR_GREATER
namespace Jellyfin.Plugin.McuTimeline
{
    /// <summary>
    /// Stands in for System.Threading.Lock, new in .NET 9.
    /// </summary>
    internal sealed class Lock
    {
    }
}
#endif
