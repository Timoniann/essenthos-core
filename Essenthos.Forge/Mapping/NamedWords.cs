using System.Text.Json;
using Essenthos.Core.BetaMasaheft;
using Essenthos.Core.Loading.Links;
using GeezTextSource = Essenthos.Core.Loading.GeezTextSource;

namespace Essenthos.Core.Mapping;

/// <summary>
/// Every word of a text by frame address as it is written, with the consonants of its names: the words
/// <see cref="AlignmentPipeline"/> reads from a database for <see cref="NameLists"/>, read from the text's words and frame.
/// </summary>
internal static class NamedWords
{
    /// <param name="lettered">Whether the verses the text prints with a letter are read: only against a text numbered in the same tradition.</param>
    public static Dictionary<(int, int, int), List<AlignmentPipeline.Word>> Read(ProductText text, bool lettered)
    {
        var verses = text.Verses.Where(verse => lettered || verse.VerseLabel.Length == 0).ToList();
        var uncapitalised = verses.SelectMany(text.InVerse)
            .Select(id => text.Words[id])
            .Where(word => word.Key.Position > 1 && word.Surface.Length > 0 && char.IsLower(word.Surface[0]))
            .Select(word => word.Surface.ToLowerInvariant())
            .ToHashSet();

        var mapped = text.Slug == GeezTextSource.Slug;
        var placed = new Dictionary<(int, int, int), List<AlignmentPipeline.Word>>();
        foreach (var verse in verses)
        {
            IEnumerable<(int, int, int)> addresses = !mapped
                ? text.Addresses(verse).Select(address => (address.Book, address.Chapter, address.Verse)).Distinct()
                : !GeezVerseMap.Books.Contains(verse.Book)
                    ? [text.Primary(verse)]
                    : GeezVerseMap.Addresses.TryGetValue((verse.Book, verse.Chapter, verse.Verse), out var at) ? at : [];
            List<AlignmentPipeline.Word>? words = null;
            foreach (var address in addresses)
            {
                words ??= [.. text.InVerse(verse).Select(id => Word(text, id, uncapitalised))];
                if (!placed.TryGetValue(address, out var standing))
                {
                    placed[address] = standing = [];
                }

                standing.AddRange(words);
            }
        }

        return placed;
    }

    private static AlignmentPipeline.Word Word(ProductText text, int id, IReadOnlySet<string> uncapitalised)
    {
        var word = text.Words[id];
        string? consonantal = null, pos = null, form = null, robinson = null;
        if (word.Morphology is { } morphology)
        {
            using var json = JsonDocument.Parse(morphology);
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                consonantal = Text(json.RootElement, "consonantal");
                pos = Text(json.RootElement, "pos");
                form = Text(json.RootElement, "form");
                robinson = Text(json.RootElement, "robinson");
            }
        }

        var forms = new AlignmentPipeline.WordForms(word.Surface, word.Lemma, consonantal, word.Strong, text.Language, word.Key.Position);
        var letters = NameLists.Skeleton(consonantal ?? word.Surface, text.Language);
        return new AlignmentPipeline.Word(
            id,
            AlignmentTokens.One(word.Surface.ToLowerInvariant()),
            word.Strong,
            letters,
            AlignmentPipeline.IsNamed(forms, pos, form ?? robinson, uncapitalised) && letters.Length > 0 ? letters : null);
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
