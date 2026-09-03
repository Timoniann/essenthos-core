using System.Text.Json;

namespace Essenthos.Core.ClearBible;

/// <param name="Source">The original-language word ids this record names, in the source document.</param>
/// <param name="Target">The translation word ids it names.</param>
internal readonly record struct ClearBibleRecord(IReadOnlyList<string> Source, IReadOnlyList<string> Target);

/// <param name="Excluded">
/// Whether the token is punctuation. The file numbers it like a word and marks it out of the
/// alignment, so an index that counts it and an index that does not are both defensible — which is
/// why PRB-0185's Russian set could not be salvaged, and why this reads the flag rather than
/// guessing.
/// </param>
internal readonly record struct ClearBibleToken(string Id, string Text, bool Excluded);

/// <summary>
/// Clear Bible's hand-made alignments, in Scripture Burrito form.
///
/// A record names a set of source words and a set of target words, which is the shape this corpus
/// stores already — so nothing has to be flattened or guessed at on the way in. What does have to be
/// watched is the identifiers, and they are not uniform across one release: the alignment against
/// the Berean Greek numbers its target words <c>BBCCCVVVWWWP</c>, twelve digits with a part on the
/// end, while the one against SBLGNT numbers them <c>BBCCCVVVWWW</c>, eleven. Reading one with the
/// other's assumption resolves nothing at all and reports it as a clean zero.
/// </summary>
internal static class ClearBibleAlignment
{
    /// <summary>Book, chapter, verse and word: the eleven digits that name a word.</summary>
    private const int WordIdLength = 11;

    public static IEnumerable<ClearBibleRecord> Records(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);

        if (!document.RootElement.TryGetProperty("records", out var records))
        {
            yield break;
        }

        foreach (var record in records.EnumerateArray())
        {
            yield return new ClearBibleRecord(Ids(record, "source"), Ids(record, "target"));
        }
    }

    /// <summary>
    /// One side of a record. A side that is absent is an empty set rather than a fault: a record
    /// naming words on one side only is how the format says a word is rendered by nothing.
    /// </summary>
    private static List<string> Ids(JsonElement record, string side)
    {
        if (!record.TryGetProperty(side, out var ids) || ids.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var words = new List<string>(ids.GetArrayLength());
        foreach (var id in ids.EnumerateArray())
        {
            if (id.GetString() is { Length: > 0 } value)
            {
                words.Add(value);
            }
        }

        return words;
    }

    /// <summary>
    /// The tokens of a target text, in file order. The id carries the address, so nothing else has
    /// to be tracked while reading.
    /// </summary>
    public static IEnumerable<ClearBibleToken> Tokens(string path)
    {
        using var reader = new StreamReader(path);
        var header = reader.ReadLine()?.Split('\t') ?? [];
        var excludes = Array.IndexOf(header, "exclude");

        while (reader.ReadLine() is { } line)
        {
            var cells = line.Split('\t');
            if (cells.Length < 3 || cells[0].Length < WordIdLength)
            {
                continue;
            }

            yield return new ClearBibleToken(
                cells[0],
                cells[2],
                excludes >= 0 && cells.Length > excludes && cells[excludes].Trim() is "y");
        }
    }

    /// <summary>
    /// The word an identifier names, whatever else the identifier carries. A source id is prefixed
    /// with a letter and a target id may carry a part number after the word; both reduce to the
    /// eleven digits that are the address.
    /// </summary>
    public static string Word(string id)
    {
        var digits = id.AsSpan();
        while (digits.Length > 0 && !char.IsAsciiDigit(digits[0]))
        {
            digits = digits[1..];
        }

        return digits.Length >= WordIdLength ? digits[..WordIdLength].ToString() : string.Empty;
    }

    /// <summary>The canonical address in an identifier: book, chapter, verse.</summary>
    public static bool Address(string id, out int book, out int chapter, out int verse)
    {
        book = chapter = verse = 0;
        var word = Word(id);
        return word.Length == WordIdLength
               && int.TryParse(word.AsSpan(0, 2), out book)
               && int.TryParse(word.AsSpan(2, 3), out chapter)
               && int.TryParse(word.AsSpan(5, 3), out verse);
    }

    /// <summary>Where the word stands in its verse, counting from one.</summary>
    public static int Position(string id) =>
        Word(id) is { Length: WordIdLength } word && int.TryParse(word.AsSpan(8, 3), out var at) ? at : 0;
}
