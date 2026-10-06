namespace Jellyfin.Plugin.McuTimeline.Model;

/// <summary>
/// A text of the data file, either one plain string or one string per language.
/// </summary>
public sealed class LocalizedText
{
    private const string FallbackLanguage = "en";

    private readonly Dictionary<string, string> _byLanguage;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizedText"/> class.
    /// </summary>
    /// <param name="byLanguage">Text by two letter language code. Must not be empty.</param>
    public LocalizedText(IReadOnlyDictionary<string, string> byLanguage)
    {
        ArgumentNullException.ThrowIfNull(byLanguage);
        if (byLanguage.Count == 0)
        {
            throw new ArgumentException("At least one language is needed.", nameof(byLanguage));
        }

        _byLanguage = new Dictionary<string, string>(byLanguage, StringComparer.OrdinalIgnoreCase);
        Default = _byLanguage.TryGetValue(FallbackLanguage, out var english) ? english : byLanguage.Values.First();
    }

    /// <summary>
    /// Gets the English text, or the only one there is. Used for logs.
    /// </summary>
    public string Default { get; }

    /// <summary>
    /// Wraps a plain string, the same in every language.
    /// </summary>
    /// <param name="text">Text.</param>
    public static implicit operator LocalizedText(string text) => FromString(text);

    /// <summary>
    /// Wraps a plain string, the same in every language.
    /// </summary>
    /// <param name="text">Text.</param>
    /// <returns>The wrapped text.</returns>
    public static LocalizedText FromString(string text) =>
        new(new Dictionary<string, string> { [FallbackLanguage] = text });

    /// <summary>
    /// Picks the text for a web client language such as fr or en-US, else English.
    /// </summary>
    /// <param name="language">Language tag, may be null.</param>
    /// <returns>The text.</returns>
    public string For(string? language)
    {
        if (!string.IsNullOrEmpty(language))
        {
            var primary = language.Split('-', '_')[0];
            if (_byLanguage.TryGetValue(language, out var exact) || _byLanguage.TryGetValue(primary, out exact))
            {
                return exact;
            }
        }

        return Default;
    }

    /// <inheritdoc />
    public override string ToString() => Default;
}
