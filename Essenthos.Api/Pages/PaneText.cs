using System.Text.Json;

namespace Essenthos.Core.Pages;

/// <summary>A word of a verse as the stored pane holds it, with what a page prints of it.</summary>
/// <param name="Strong">The Strong numbers as the edition writes them, unsplit; null where it gives none.</param>
/// <param name="Gloss">What the edition says the word means; null where it says nothing.</param>
/// <param name="Record">The first record the word names, as <c>(slug, kind)</c> with the kind spelled as the API spells it; null where it names none.</param>
internal sealed record PaneWord(string Surface, string Trailer, string? Strong, string? Gloss, (string Slug, string Kind)? Record);

/// <summary>One verse of a text at one address of the shared frame, its elided words left out.</summary>
internal sealed record PaneVerse(int Number, IReadOnlyList<PaneWord> Words)
{
    /// <summary>The verse as plain text: every word and what follows it, the white space collapsed.</summary>
    public string Text { get; } = PageHtml.Plain(string.Concat(Words.Select(word => word.Surface + word.Trailer)));
}

/// <summary>
/// A chapter of one text read from its stored pane (<c>panes/{text}</c>, <see cref="Corpus.Panes"/>) rather than
/// from <c>verse_reference</c> and <c>word</c>: one row by its key instead of two walks over the largest tables.
///
/// <para>
/// A pane joins at one address every verse of the text placed there and reads a passage a chapter holds under
/// another name, so it is taken only where it says exactly what the word table says: one verse of the text at
/// each address (<see cref="Single"/>), and a chapter with no passage named elsewhere. Anywhere else the page
/// reads the word table as before.
/// </para>
/// </summary>
internal sealed record PaneText(IReadOnlyList<PaneVerse> Verses, bool Single)
{
    /// <summary>The verses with words, each as plain text, in the frame's order.</summary>
    public IReadOnlyList<(int Number, string Text)> Plain { get; } =
        [.. Verses.Where(verse => verse.Text.Length > 0).Select(verse => (verse.Number, verse.Text))];

    public PaneVerse? At(int verse) => Verses.FirstOrDefault(found => found.Number == verse);

    /// <summary>What the parsed chapter takes in memory, roughly, for the page cache.</summary>
    public long Bytes => Verses.Sum(verse => 64L + verse.Words.Sum(word => 96L + ((word.Surface.Length + word.Trailer.Length) * sizeof(char))));

    /// <summary>A pane's body, format 1, as it is served uncompressed.</summary>
    public static PaneText Parse(ReadOnlyMemory<byte> json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var records = new List<(string, string)>();
        if (root.TryGetProperty("records", out var listed))
        {
            foreach (var record in listed.EnumerateArray())
            {
                records.Add((record[0].GetString()!, record[1].GetString()!));
            }
        }

        var single = true;
        var verses = new List<PaneVerse>();
        foreach (var verse in root.GetProperty("verses").EnumerateArray())
        {
            if (verse.TryGetProperty("own", out var own) && own.GetArrayLength() > 1)
            {
                single = false;
            }

            var words = new List<PaneWord>();
            if (verse.TryGetProperty("words", out var written))
            {
                foreach (var word in written.EnumerateArray())
                {
                    if (word.TryGetProperty("el", out _))
                    {
                        continue;
                    }

                    (string, string)? named = word.TryGetProperty("e", out var cards) && cards.GetArrayLength() > 0
                                              && cards[0].GetProperty("r").GetInt32() is var index && index < records.Count
                        ? records[index]
                        : null;
                    words.Add(new PaneWord(
                        word.GetProperty("s").GetString() ?? "",
                        Optional(word, "t") ?? "",
                        Optional(word, "n"),
                        Optional(word, "g"),
                        named));
                }
            }

            verses.Add(new PaneVerse(verse.GetProperty("n").GetInt32(), words));
        }

        return new PaneText(verses, single);
    }

    private static string? Optional(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() : null;
}
