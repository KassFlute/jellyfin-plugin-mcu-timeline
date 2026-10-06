using System.Globalization;
using System.Text.Json;

namespace Jellyfin.Plugin.McuTimeline.Model;

/// <summary>
/// Reads and validates the timeline data file. Any error rejects the whole file, so a
/// half valid override never replaces the embedded data.
/// </summary>
public static class TimelineParser
{
    /// <summary>
    /// Saga identifiers accepted in the data file.
    /// </summary>
    public static readonly IReadOnlySet<string> Sagas = new HashSet<string>(StringComparer.Ordinal)
    {
        "infinity", "multiverse"
    };

    /// <summary>
    /// Era identifiers accepted in the data file. Closed list, see the spec section 4.2.
    /// </summary>
    public static readonly IReadOnlySet<string> Eras = new HashSet<string>(StringComparer.Ordinal)
    {
        "origins", "avengers", "fracture", "blip", "post-blip"
    };

    /// <summary>
    /// Parses a data file.
    /// </summary>
    /// <param name="json">File content.</param>
    /// <returns>The validated data.</returns>
    /// <exception cref="TimelineDataException">The file is not valid.</exception>
    public static TimelineData Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            throw new TimelineDataException($"Invalid JSON at line {ex.LineNumber + 1}: {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new TimelineDataException("The root must be an object with \"version\" and \"items\".");
            }

            var version = RequiredString(root, "version", "root");
            if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                throw new TimelineDataException("\"items\" is missing or is not an array.");
            }

            var entries = new List<TimelineEntry>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            foreach (var item in items.EnumerateArray())
            {
                var entry = ParseEntry(item, index);
                if (!ids.Add(entry.Id))
                {
                    throw new TimelineDataException($"Duplicate id \"{entry.Id}\".");
                }

                entries.Add(entry);
                index++;
            }

            return new TimelineData(version, entries);
        }
    }

    private static TimelineEntry ParseEntry(JsonElement item, int index)
    {
        var where = $"items[{index}]";
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new TimelineDataException($"{where} is not an object.");
        }

        var id = RequiredString(item, "id", where);
        where = $"items[{index}] (\"{id}\")";

        var type = RequiredString(item, "type", where) switch
        {
            "movie" => EntryType.Movie,
            "series" => EntryType.Series,
            "short" => EntryType.Short,
            var other => throw new TimelineDataException($"{where}: unknown type \"{other}\", expected movie, series or short.")
        };

        var tmdbId = RequiredInt(item, "tmdbId", where);
        if (tmdbId <= 0)
        {
            throw new TimelineDataException($"{where}: tmdbId must be positive.");
        }

        var releaseText = RequiredString(item, "releaseDate", where);
        if (!DateOnly.TryParseExact(releaseText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var releaseDate))
        {
            throw new TimelineDataException($"{where}: releaseDate \"{releaseText}\" is not a yyyy-MM-dd date.");
        }

        var phase = RequiredInt(item, "phase", where);
        if (phase is < 1 or > 6)
        {
            throw new TimelineDataException($"{where}: phase must be between 1 and 6.");
        }

        var saga = RequiredString(item, "saga", where);
        if (!Sagas.Contains(saga))
        {
            throw new TimelineDataException($"{where}: unknown saga \"{saga}\", expected infinity or multiverse.");
        }

        var era = RequiredString(item, "era", where);
        if (!Eras.Contains(era))
        {
            throw new TimelineDataException($"{where}: unknown era \"{era}\", expected one of {string.Join(", ", Eras)}.");
        }

        var seasons = OptionalSeasons(item, where);
        if (seasons.Count > 0 && type != EntryType.Series)
        {
            throw new TimelineDataException($"{where}: seasons is only allowed on a series.");
        }

        return new TimelineEntry
        {
            Id = id,
            Title = OptionalText(item, "title", where) ?? throw new TimelineDataException($"{where}: \"title\" is required."),
            Type = type,
            TmdbId = tmdbId,
            ImdbId = OptionalString(item, "imdbId", where),
            Seasons = seasons,
            ReleaseDate = releaseDate,
            ChronoOrder = RequiredInt(item, "chronoOrder", where),
            StoryYear = OptionalText(item, "storyYear", where),
            Phase = phase,
            Saga = saga,
            Era = era,
            AccentColor = OptionalColor(item, where),
            Note = OptionalText(item, "note", where)
        };
    }

    private static string RequiredString(JsonElement parent, string name, string where)
    {
        return OptionalString(parent, name, where)
            ?? throw new TimelineDataException($"{where}: \"{name}\" is required.");
    }

    private static string? OptionalString(JsonElement parent, string name, string where)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new TimelineDataException($"{where}: \"{name}\" must be a string.");
        }

        var text = value.GetString()!.Trim();
        return text.Length == 0 ? null : text;
    }

    // a plain string, or { "fr": "...", "en": "..." }
    private static LocalizedText? OptionalText(JsonElement parent, string name, string where)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return OptionalString(parent, name, where) is { } plain ? LocalizedText.FromString(plain) : null;
        }

        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in value.EnumerateObject())
        {
            if (language.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(language.Value.GetString()))
            {
                throw new TimelineDataException($"{where}: \"{name}.{language.Name}\" must be a non empty string.");
            }

            texts[language.Name] = language.Value.GetString()!.Trim();
        }

        return texts.Count == 0 ? null : new LocalizedText(texts);
    }

    private static string? OptionalColor(JsonElement parent, string where)
    {
        var color = OptionalString(parent, "accentColor", where);
        if (color is not null && !(color.Length == 7 && color[0] == '#' && color.Skip(1).All(char.IsAsciiHexDigit)))
        {
            throw new TimelineDataException($"{where}: accentColor \"{color}\" is not a #RRGGBB colour.");
        }

        return color;
    }

    private static int RequiredInt(JsonElement parent, string name, string where)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            throw new TimelineDataException($"{where}: \"{name}\" is required.");
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            throw new TimelineDataException($"{where}: \"{name}\" must be an integer.");
        }

        return number;
    }

    private static List<int> OptionalSeasons(JsonElement parent, string where)
    {
        if (!parent.TryGetProperty("seasons", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new TimelineDataException($"{where}: \"seasons\" must be a list of integers.");
        }

        var seasons = new List<int>();
        foreach (var season in value.EnumerateArray())
        {
            if (season.ValueKind != JsonValueKind.Number || !season.TryGetInt32(out var number) || number < 0)
            {
                throw new TimelineDataException($"{where}: \"seasons\" must be a list of integers.");
            }

            seasons.Add(number);
        }

        return seasons;
    }
}
