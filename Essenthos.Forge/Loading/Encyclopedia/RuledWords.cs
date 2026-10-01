using System.Globalization;
using Essenthos.Core.Loading.Frame;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// The word a ruling is about, named the way the witness itself names it: the text, the verse as that
/// text numbers it, the word's position in the verse, and the word as it then read.
///
/// <para>
/// A row id would be shorter and is no address at all. Every rebuild numbers the words afresh, so an
/// id kept in a file names whatever word comes to hold that number next — six of the rulings on the
/// titles had already lost their words to a reload of the Greek Brenton, and on a corpus loaded from
/// nothing every other ruling would have landed on a stranger. The canonical address is not enough
/// either: BHSA's 1 Chronicles 12:4 and 12:5 both stand primarily at the English 12:4, so a position
/// there names two words. The text's own book, chapter, verse and label are unique, and they come
/// from the witness's file rather than from anything a load computes.
/// </para>
/// </summary>
/// <param name="Text">The witness, by slug.</param>
/// <param name="Reference">
/// The verse as the witness numbers it, with its label where it has one: <c>NEH 10:3</c> in BHSA is
/// the English Nehemiah 10:2, and <c>JOS 9:2a</c> is the Greek's addition after Joshua 9:2.
/// </param>
/// <param name="Position">The word's position in that verse, as the corpus counts it.</param>
/// <param name="Surface">
/// The word as it read when the ruling was made. A word at the address that reads otherwise is not
/// the word that was ruled on.
/// </param>
internal sealed record RuledWord(string Text, string Reference, int Position, string Surface)
{
    public override string ToString() => $"{Text} {Reference} word {Position} ({Surface})";

    /// <summary>The book, chapter, verse and label <see cref="Reference"/> names, or null where it names none.</summary>
    public (int Book, int Chapter, int Verse, string Label)? Address()
    {
        var space = Reference.LastIndexOf(' ');
        var colon = Reference.IndexOf(':', Math.Max(space, 0));
        if (space <= 0 || colon < 0
            || !BookCodes.TryGetOrdinal(Reference[..space], out var book)
            || !int.TryParse(Reference.AsSpan(space + 1, colon - space - 1), CultureInfo.InvariantCulture, out var chapter))
        {
            return null;
        }

        var digits = colon + 1;
        while (digits < Reference.Length && char.IsAsciiDigit(Reference[digits]))
        {
            digits++;
        }

        return int.TryParse(Reference.AsSpan(colon + 1, digits - colon - 1), CultureInfo.InvariantCulture, out var verse)
            ? (book, chapter, verse, Reference[digits..])
            : null;
    }
}

/// <summary>Finds the words rulings name, by their addresses, in one query.</summary>
internal static class RuledWords
{
    private const string Words =
        """
        SELECT x.n, w.id, w.text
        FROM unnest(@texts, @books, @chapters, @verses, @labels, @positions)
             WITH ORDINALITY AS x(slug, b, c, v, l, p, n)
        JOIN text t ON t.slug = x.slug
        JOIN book bk ON bk.text_id = t.id AND bk.canonical_ordinal = x.b
        JOIN verse ve ON ve.book_id = bk.id AND ve.chapter_number = x.c AND ve.number = x.v AND ve.label = x.l
        JOIN word w ON w.verse_id = ve.id AND w.position = x.p
        """;

    /// <summary>
    /// The id of every word that stands at its address and still reads as it did, and the words that
    /// do not: no such text, verse or position, or another word there now.
    /// </summary>
    public static async Task<(IReadOnlyDictionary<RuledWord, long> Found, IReadOnlyList<RuledWord> Lost)> Find(
        NpgsqlConnection connection,
        IEnumerable<RuledWord> words,
        CancellationToken cancellationToken)
    {
        var wanted = words.Distinct().ToList();
        var placed = wanted.Select(word => (Word: word, At: word.Address())).Where(x => x.At is not null).ToList();

        await using var command = new NpgsqlCommand(Words, connection);
        command.Parameters.AddWithValue("texts", placed.Select(x => x.Word.Text).ToArray());
        command.Parameters.AddWithValue("books", placed.Select(x => x.At!.Value.Book).ToArray());
        command.Parameters.AddWithValue("chapters", placed.Select(x => x.At!.Value.Chapter).ToArray());
        command.Parameters.AddWithValue("verses", placed.Select(x => x.At!.Value.Verse).ToArray());
        command.Parameters.AddWithValue("labels", placed.Select(x => x.At!.Value.Label).ToArray());
        command.Parameters.AddWithValue("positions", placed.Select(x => x.Word.Position).ToArray());

        var found = new Dictionary<RuledWord, long>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var word = placed[(int)reader.GetInt64(0) - 1].Word;
                if (string.Equals(reader.GetString(2), word.Surface, StringComparison.Ordinal))
                {
                    found[word] = reader.GetInt64(1);
                }
            }
        }

        return (found, [.. wanted.Where(word => !found.ContainsKey(word))]);
    }

    /// <summary>
    /// The id of every word, or an error naming each one that is not where its ruling says. A ruling
    /// whose word has moved is not applied to whatever stands there now.
    /// </summary>
    public static async Task<IReadOnlyDictionary<RuledWord, long>> Resolve(
        NpgsqlConnection connection,
        IEnumerable<RuledWord> words,
        CancellationToken cancellationToken)
    {
        var (found, lost) = await Find(connection, words, cancellationToken);
        if (lost.Count > 0)
        {
            throw new InvalidDataException(
                $"{lost.Count} ruled words are not where their rulings say: " + string.Join("; ", lost.Take(20)) +
                (lost.Count > 20 ? "; …" : "") + ". Each ruling names its word by text, the verse as that text " +
                "numbers it, position and surface. Find the word the ruling meant in that verse and correct " +
                "the ruling's text, reference, position or surface in its file; never point it at whatever " +
                "word stands there now.");
        }

        return found;
    }
}
