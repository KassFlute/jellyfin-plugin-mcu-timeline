using System.Security.Cryptography;

namespace Jellyfin.Plugin.McuTimeline.Api;

/// <summary>
/// Cache key for the web files: a hash of their content, so any change to them gives new
/// URLs, even between two builds that carry the same plugin version.
/// </summary>
public static class WebAssetVersion
{
    private const string Prefix = "Jellyfin.Plugin.McuTimeline.Web.";

    /// <summary>
    /// Gets the hash of every embedded web file, 12 hex characters.
    /// </summary>
    public static string Value { get; } = Compute();

    private static string Compute()
    {
        var assembly = typeof(WebAssetVersion).Assembly;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            hash.AppendData(buffer.ToArray());
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset())[..12];
    }
}
