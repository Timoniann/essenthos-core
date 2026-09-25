using System.Text.Json;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A source some of a text's words come from that is not the one it was loaded from: the verses a
/// digitisation lost, restored from another copy of the same edition. Its terms are its own, so it
/// is credited by name, author, licence and address, and says which words are its.
/// </summary>
/// <param name="Covers">Which words of the text are this source's.</param>
public sealed record TextPartSource(string Name, string Author, string Licence, string? LicenceUrl, string Url, string Covers);

/// <summary><see cref="Text.PartSources"/>, which is a JSON array of <see cref="TextPartSource"/>.</summary>
public static class TextPartSources
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<TextPartSource> Of(Text text) =>
        text.PartSources is { Length: > 0 } json
            ? JsonSerializer.Deserialize<List<TextPartSource>>(json, Json) ?? []
            : [];

    /// <summary>Credits <paramref name="source"/> on the text, once; false where it already was.</summary>
    public static bool Add(Text text, TextPartSource source)
    {
        var sources = Of(text);
        if (sources.Contains(source))
        {
            return false;
        }

        text.PartSources = JsonSerializer.Serialize(sources.Append(source).ToList(), Json);
        return true;
    }
}
