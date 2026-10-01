using Npgsql;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// Which verses of one text a word link may join to which verses of another: those standing at the
/// same canonical address, and those a verse link joins.
///
/// <para>
/// A composed answer is two hops, and the second is somebody's statement about the middle text,
/// which may cross a verse boundary where that text divides the verses its own way. Carried onto a
/// third text that divides them like the target, the crossing names the wrong verse. Clear Bible's
/// Berean joins the <em>The</em> of Philippians 1:16 to the οἱ of 1:17, the two verses standing in
/// the other order in the critical text, and every English text composed through it claimed the
/// same, across a pair of verses nothing in the frame joins.
/// </para>
/// </summary>
internal sealed class VerseJoins(
    IReadOnlyDictionary<long, int> verseOf,
    IReadOnlyDictionary<int, (int Book, int Chapter, int Verse)> addressOf,
    IReadOnlySet<(int From, int To)> joined)
{
    public bool Joins(long fromWord, long toWord)
    {
        if (!verseOf.TryGetValue(fromWord, out var from) || !verseOf.TryGetValue(toWord, out var to))
        {
            return false;
        }

        return joined.Contains((from, to))
               || (addressOf.TryGetValue(from, out var here) && addressOf.TryGetValue(to, out var there) && here == there);
    }

    public static async Task<VerseJoins> Load(
        NpgsqlConnection connection,
        int fromTextId,
        int toTextId,
        CancellationToken cancellationToken)
    {
        int[] texts = [fromTextId, toTextId];

        var verseOf = new Dictionary<long, int>(1_200_000);
        await using (var command = new NpgsqlCommand(
            "SELECT id, verse_id FROM word WHERE text_id = ANY(@texts)", connection))
        {
            command.Parameters.AddWithValue("texts", texts);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                verseOf[reader.GetInt64(0)] = reader.GetInt32(1);
            }
        }

        var addressOf = new Dictionary<int, (int, int, int)>(60_000);
        await using (var command = new NpgsqlCommand(
            """
            SELECT r.verse_id, r.canonical_book, r.canonical_chapter, r.canonical_verse
            FROM verse_reference r JOIN verse v ON v.id = r.verse_id
            WHERE r.is_primary AND v.text_id = ANY(@texts)
            """, connection))
        {
            command.Parameters.AddWithValue("texts", texts);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                addressOf[reader.GetInt32(0)] = (reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
            }
        }

        var joined = new HashSet<(int, int)>(40_000);
        await using (var command = new NpgsqlCommand(
            """
            SELECT a.verse_id, b.verse_id
            FROM verse_link l
            JOIN verse_link_verse a ON a.verse_link_id = l.id
            JOIN verse_link_verse b ON b.verse_link_id = l.id
            JOIN verse va ON va.id = a.verse_id AND va.text_id = @from
            JOIN verse vb ON vb.id = b.verse_id AND vb.text_id = @to
            WHERE (l.from_text_id = @from AND l.to_text_id = @to)
               OR (l.from_text_id = @to AND l.to_text_id = @from)
            """, connection))
        {
            command.Parameters.AddWithValue("from", fromTextId);
            command.Parameters.AddWithValue("to", toTextId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                joined.Add((reader.GetInt32(0), reader.GetInt32(1)));
            }
        }

        return new VerseJoins(verseOf, addressOf, joined);
    }
}
