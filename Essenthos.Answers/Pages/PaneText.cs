using System.Text;
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

/// <summary>A verse of one text as a column of the reader shows it, before its words can be pointed at.</summary>
/// <param name="Badge">
/// The address the text itself gives the verse where it is not the row's: the one the edition prints, else
/// where its own numbering puts it (<c>118:1</c> in the Synodal's Psalm 119); null where they agree.
/// </param>
internal sealed record ReadingCell(string Text, string? Badge);

/// <summary>One of several texts a chapter is read in side by side, as the reader lays out its column.</summary>
/// <param name="Language">The text's language as a web page names it.</param>
/// <param name="Face">The reader's face for the text (essenthos-web <c>textClassFor</c>): <c>text-ltr</c>, <c>text-hebrew</c> or <c>text-greek</c>.</param>
/// <param name="Cells">Every verse the text's pane holds, by the shared frame's number.</param>
internal sealed record ReadingColumn(string Name, string? Language, bool RightToLeft, string Face, IReadOnlyDictionary<int, ReadingCell> Cells)
{
    /// <summary>
    /// A text's side of a chapter read from its pane as the reader's own columns read it (essenthos-web
    /// <c>composeParallel</c> and <c>VerseCell</c>): every verse of the pane, an elided word's letters left
    /// out and what follows it kept.
    /// </summary>
    public static IReadOnlyDictionary<int, ReadingCell> CellsOf(ReadOnlyMemory<byte> json, int chapter)
    {
        using var document = JsonDocument.Parse(json);
        var cells = new Dictionary<int, ReadingCell>();
        foreach (var verse in document.RootElement.GetProperty("verses").EnumerateArray())
        {
            var number = verse.GetProperty("n").GetInt32();
            var text = new StringBuilder();
            if (verse.TryGetProperty("words", out var words))
            {
                foreach (var word in words.EnumerateArray())
                {
                    if (!word.TryGetProperty("el", out _))
                    {
                        text.Append(word.GetProperty("s").GetString());
                    }

                    text.Append(word.TryGetProperty("t", out var trailer) ? trailer.GetString() : "");
                }
            }

            var row = $"{chapter}:{number}";
            var stated = verse.TryGetProperty("stated", out var printed)
                ? printed.EnumerateArray().Select(address => address.GetString()).Where(address => address is not null && address != row).ToList()
                : [];
            string? badge = stated.Count > 0 ? string.Join(", ", stated) : null;
            if (badge is null && verse.TryGetProperty("ref", out var own) && own.GetArrayLength() == 3
                && (own[1].GetInt32() != chapter || own[2].GetInt32() != number))
            {
                badge = $"{own[1].GetInt32()}:{own[2].GetInt32()}";
            }

            cells[number] = new ReadingCell(PageHtml.Plain(text.ToString()), badge);
        }

        return cells;
    }
}
