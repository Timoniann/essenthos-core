using System.Text.Json;

namespace Essenthos.Core.ClearBible;

/// <param name="Source">The original-language word ids this record names, in the source document.</param>
/// <param name="Target">The translation word ids it names.</param>
internal readonly record struct ClearBibleRecord(IReadOnlyList<string> Source, IReadOnlyList<string> Target);

/// <param name="Excluded">
/// Whether the token is punctuation. The file numbers it like a word and marks it out of the
/// alignment, so an index that counts it and an index that does not are both defensible — which is
/// why the Russian set, 12,550 of whose records name a punctuation mark as the Russian word, could
/// not be salvaged, and why this reads the flag rather than guessing.
/// </param>
/// <param name="Strong">
/// The Strong number a source edition's row states for the word, as the file writes it, or null in a
/// target file, which carries none.
/// </param>
/// <param name="Renders">
/// The source verses a target file says the token renders, first and last, each as the eight digits
/// <c>BBCCCVVV</c> read as a number; null where the file says nothing, as a source file does not.
/// </param>
/// <param name="Part">The part of speech a source edition's row states, or null in a target file.</param>
internal readonly record struct ClearBibleToken(
    string Id,
    string Text,
    bool Excluded,
    string? Strong,
    (int First, int Last)? Renders = null,
    string? Part = null);

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

    /// <summary>What a chapter and a verse are each counted in, three digits apiece.</summary>
    private const int VerseDigits = 1000;

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
        var punctuation = Array.IndexOf(header, "isPunc");
        var strongs = Array.IndexOf(header, "strongs");
        var renders = Array.IndexOf(header, "source_verse");
        var rendersTo = Array.IndexOf(header, "source_verse_range_end");
        var parts = Array.IndexOf(header, "pos");
        var morphology = Array.IndexOf(header, "morph");

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
                (excludes >= 0 && cells.Length > excludes && cells[excludes].Trim() is "y")
                || (punctuation >= 0 && cells.Length > punctuation && cells[punctuation].Trim() is "True"),
                strongs >= 0 && cells.Length > strongs ? cells[strongs] : null,
                Number(cells, renders) is { } first ? (first, Number(cells, rendersTo) ?? first) : null,
                Part(cells, parts, morphology));
        }
    }

    /// <summary>What the Westminster morphology calls a pronominal suffix in its part-of-speech column.</summary>
    public const string Suffix = "suffix";

    /// <summary>
    /// A source row's part of speech, with a pronominal suffix called one whichever edition the file
    /// is. The morpheme edition says <c>suffix</c>; the Westminster Leningrad Codex says <c>pron</c>
    /// and tells a suffix from a free pronoun only in its morphology, <c>psn3ms</c> against
    /// <c>pp3ms</c>, so read alone its suffixes would be compared as words of their own.
    /// </summary>
    private static string? Part(string[] cells, int parts, int morphology)
    {
        var part = parts >= 0 && cells.Length > parts ? cells[parts] : null;
        return morphology >= 0 && cells.Length > morphology && cells[morphology].StartsWith("ps", StringComparison.Ordinal)
            ? Suffix
            : part;
    }

    private static int? Number(string[] cells, int column) =>
        column >= 0 && cells.Length > column && int.TryParse(cells[column], out var verse) ? verse : null;

    /// <summary>
    /// The verse in an identifier as the eight digits <c>BBCCCVVV</c> read as a number, which is how a
    /// target file writes the verses its tokens render, so the two compare directly and a range of
    /// them runs in canonical order.
    /// </summary>
    public static int? Verse(string id) =>
        Address(id, out var book, out var chapter, out var verse) ? Verse(book, chapter, verse) : null;

    /// <inheritdoc cref="Verse(string)"/>
    public static int Verse(int book, int chapter, int verse) => (book * VerseDigits + chapter) * VerseDigits + verse;

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

    /// <summary>
    /// What an identifier names, all of it: the word, and in the Westminster morphology the morpheme
    /// of the word as well. Its twelfth digit tells the ו of <em>וַיֹּאמֶר</em> from the verb, which
    /// BHSA writes as two words; reduced to the word, a record about the one is a record about the
    /// other. A target id's part is not kept this way, because the target files do not number it.
    /// </summary>
    public static string Unit(string id)
    {
        var digits = id.AsSpan();
        while (digits.Length > 0 && !char.IsAsciiDigit(digits[0]))
        {
            digits = digits[1..];
        }

        return digits.Length >= WordIdLength ? digits.ToString() : string.Empty;
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
}
