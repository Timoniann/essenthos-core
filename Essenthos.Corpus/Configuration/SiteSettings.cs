using System.Text.Json;
using System.Text.Json.Nodes;

namespace Essenthos.Core.Configuration;

/// <summary>A switch the project owner sets for the site, and what it is when nobody has set it.</summary>
public sealed record SiteSetting(string Key, bool Default);

/// <summary>
/// The site's switches the project owner decides — whether a feature on trial is shown, whether our
/// own generated pictures are — kept in a tracked file beside the API, which the API serves and the
/// owner's console writes. Only switches with one plain meaning a reader can see belong here; how
/// the site is built or where it reads from stays configuration.
///
/// <para>
/// The file is <c>{ "about": "...", "settings": { "naveTopics": true, ... } }</c>. A switch it does
/// not name is at its default, and a name it holds that is not in <see cref="Catalogue"/> is
/// ignored, so a file written by a newer console never breaks an older API.
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

    private static readonly JsonDocumentOptions Reading = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static bool Knows(string key) => Catalogue.Any(s => s.Key == key);

    public static IReadOnlyDictionary<string, bool> Defaults() => Catalogue.ToDictionary(s => s.Key, s => s.Default, StringComparer.Ordinal);

    /// <summary>Every switch in the catalogue, as the file sets it or at its default where the file is absent or silent.</summary>
    public static IReadOnlyDictionary<string, bool> Read(string path)
    {
        if (!File.Exists(path))
        {
            return Defaults();
        }

        using var stream = File.OpenRead(path);
        return From(JsonNode.Parse(stream, documentOptions: Reading));
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
}
