using System.Text.Json;
using System.Text.Json.Nodes;

namespace Essenthos.Core.Configuration;

/// <summary>A switch the project owner sets for the site, and what it is when nobody has set it.</summary>
public sealed record SiteSetting(string Key, bool Default);

/// <summary>
/// A text the project owner picks for the site, named by its identifier, and the one the site uses
/// when nobody has picked.
/// </summary>
public sealed record SiteChoice(string Key, string Default);

/// <summary>
/// The site's switches the project owner decides — whether a feature on trial is shown, whether our
/// own generated pictures are — kept in a tracked file beside the API, which the API serves and the
/// owner's console writes. Only switches with one plain meaning a reader can see belong here; how
/// the site is built or where it reads from stays configuration.
///
/// <para>
/// The file is <c>{ "about": "...", "settings": { "naveTopics": true, "readerTranslation": "BSB", ... } }</c>.
/// A switch or a choice it does not name is at its default, and a name it holds that is in neither
/// <see cref="Catalogue"/> nor <see cref="Choices"/> is ignored, so a file written by a newer
/// console never breaks an older API.
/// </para>
/// </summary>
public static class SiteSettings
{
    public const string FileName = "site-settings.json";

    /// <summary>The topics Nave's Topical Bible files a chapter's verses under, shown on trial in the chapter's context.</summary>
    public const string NaveTopics = "naveTopics";

    /// <summary>Our own generated pictures — portraits and the picture of the glory — on person pages and in lists.</summary>
    public const string GeneratedImages = "generatedImages";

    public static readonly IReadOnlyList<SiteSetting> Catalogue =
    [
        new(NaveTopics, true),
        new(GeneratedImages, true),
    ];

    /// <summary>
    /// The translation a reader who has chosen none opens in, beside the original. The Berean by
    /// default: it is the corpus's best-linked English text, so a word touched in it is the likeliest
    /// to light up in the other pane.
    /// </summary>
    public const string ReaderTranslation = "readerTranslation";

    public static readonly IReadOnlyList<SiteChoice> Choices =
    [
        new(ReaderTranslation, "BSB"),
    ];

    private static readonly JsonDocumentOptions Reading = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static bool Knows(string key) => Catalogue.Any(s => s.Key == key);

    public static bool Chooses(string key) => Choices.Any(c => c.Key == key);

    public static IReadOnlyDictionary<string, bool> Defaults() => Catalogue.ToDictionary(s => s.Key, s => s.Default, StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, string> ChoiceDefaults() =>
        Choices.ToDictionary(c => c.Key, c => c.Default, StringComparer.Ordinal);

    /// <summary>Every switch in the catalogue, as the file sets it or at its default where the file is absent or silent.</summary>
    public static IReadOnlyDictionary<string, bool> Read(string path) => From(Parse(path));

    /// <summary>Every choice, as the file sets it or at its default where the file is absent or silent.</summary>
    public static IReadOnlyDictionary<string, string> ReadChoices(string path) => ChoicesFrom(Parse(path));

    /// <summary>The file as it stands, or null where there is none.</summary>
    public static JsonNode? Parse(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        return JsonNode.Parse(stream, documentOptions: Reading);
    }

    public static IReadOnlyDictionary<string, bool> From(JsonNode? file)
    {
        var values = new Dictionary<string, bool>(StringComparer.Ordinal);
        var set = file?["settings"] as JsonObject;
        foreach (var setting in Catalogue)
        {
            values[setting.Key] = set?[setting.Key]?.GetValueKind() switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => setting.Default,
            };
        }

        return values;
    }

    /// <summary>Every choice, as the file names it where it names it as text, and at its default otherwise.</summary>
    public static IReadOnlyDictionary<string, string> ChoicesFrom(JsonNode? file)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var set = file?["settings"] as JsonObject;
        foreach (var choice in Choices)
        {
            values[choice.Key] = set?[choice.Key] is JsonValue value
                                 && value.TryGetValue<string>(out var chosen)
                                 && !string.IsNullOrWhiteSpace(chosen)
                ? chosen.Trim()
                : choice.Default;
        }

        return values;
    }
}
