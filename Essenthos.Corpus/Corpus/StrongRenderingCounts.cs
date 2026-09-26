using System.Data;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Corpus;

/// <summary>One phrase a translation writes for a Strong number, its place among them and how many links write it.</summary>
internal sealed record StrongRenderingCount(string Number, int Rank, string Phrase, int Uses);

/// <summary>
/// The whole phrase each link renders, not each word of it separately, and the commonest few of them
/// for each number — for one number on the entry page, for a page of them on the lexicon's cards, and
/// for every number at once when the load counts the cards ahead of the reader.
///
/// Counting words alone reports אֱלֹהֶיךָ as <em>god</em> 2,284 times and <em>thy</em> 342, which
/// reads as noise and is not: a Hebrew word carries its pronoun and its construct relation inside
/// itself, so <em>thy God</em> is one rendering of one word and splitting it in two destroys the very
/// thing the link recorded. Grouped by link it reads as what it is — <em>god</em> 684, <em>thy
/// god</em> 319, <em>of god</em> 317.
///
/// <para>
/// A link counts for a number when a word carrying it stands on the side facing this text's words,
/// in the one edition of each original language the text is most fully joined to
/// (<see cref="LinkedOriginals.Primary"/>). A translation linked to three Greek editions renders each
/// Greek word three times over, once per edition, and counting all three reported every phrase three
/// times; a translation's own tagged words, facing another translation, are not an original at all.
/// </para>
///
/// <para>
/// Written as SQL because the aggregation is one <c>string_agg</c> over an ordered group inside a
/// grouped, ranked outer query, which EF will not translate; doing it in memory would pull every word
/// of every link for a number like the conjunction. The two statements differ only in where they
/// start: from the words carrying a few numbers, or from every link between the text and its
/// originals, which is the plan that reads the whole of it in one pass.
/// </para>
/// </summary>
internal static class StrongRenderingCounts
{
    /// <summary>
    /// The translation a lexicon card quotes when nobody names one: the English the Strong numbers
    /// were written against.
    /// </summary>
    public const string CardTranslation = "KJV";

    /// <summary>How many renderings a lexicon card quotes — the commonest few, not the list.</summary>
    public const int CardRenderings = 3;

    private const string Ranked =
        """
        rendered AS (
            SELECT p.number, string_agg(lower(w.text), ' ' ORDER BY v.number, w.position) AS phrase
            FROM pairs p
            JOIN link_word o ON o.link_id = p.link_id AND o.side <> p.side
            JOIN word w ON w.id = o.word_id AND w.text_id = @text
            JOIN verse v ON v.id = w.verse_id
            GROUP BY p.number, p.link_id
        )
        SELECT number, rank, phrase, uses
        FROM (
            SELECT number, phrase, count(*) AS uses,
                   row_number() OVER (PARTITION BY number ORDER BY count(*) DESC, phrase) AS rank
            FROM rendered
            WHERE phrase IS NOT NULL
            GROUP BY number, phrase
        ) ranked
        WHERE rank <= @take
        ORDER BY number, rank
        """;

    private const string OfNumbers =
        $"""
         WITH pairs AS (
             SELECT DISTINCT s.link_id, sw.strong_number AS number, s.side
             FROM word sw
             JOIN link_word s ON s.word_id = sw.id
             JOIN link l ON l.id = s.link_id
             WHERE sw.strong_number = ANY(@numbers)
               AND sw.text_id = ANY(@witnesses)
               AND (l.from_text_id = @text OR l.to_text_id = @text)
               AND l.relation IN ('renders', 'equals')
         ),
         {Ranked}
         """;

    private const string OfEveryNumber =
        $"""
         WITH pairs AS (
             SELECT DISTINCT s.link_id, sw.strong_number AS number, s.side
             FROM link l
             JOIN link_word s ON s.link_id = l.id
             JOIN word sw ON sw.id = s.word_id AND sw.text_id = ANY(@witnesses) AND sw.strong_number IS NOT NULL
             WHERE ((l.from_text_id = @text AND l.to_text_id = ANY(@witnesses))
                    OR (l.to_text_id = @text AND l.from_text_id = ANY(@witnesses)))
               AND l.relation IN ('renders', 'equals')
         ),
         {Ranked}
         """;

    /// <summary>
    /// The commonest <paramref name="take"/> phrases for each of <paramref name="numbers"/>, or for every
    /// number the text renders where <paramref name="numbers"/> is null.
    /// </summary>
    /// <param name="timeout">Seconds the statement may take; null for the connection's own limit.</param>
    public static async Task<List<StrongRenderingCount>> Count(
        AppDbContext db,
        int textId,
        IReadOnlyCollection<string>? numbers,
        int take,
        CancellationToken cancellationToken,
        int? timeout = null)
    {
        var witnesses = LinkedOriginals.Primary(await LinkedOriginals.Of(db, textId, cancellationToken))
            .Select(original => original.Id)
            .ToArray();

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(numbers is null ? OfEveryNumber : OfNumbers, connection);
        command.Parameters.AddWithValue("text", textId);
        command.Parameters.AddWithValue("witnesses", witnesses);
        command.Parameters.AddWithValue("take", take);
        if (numbers is not null)
        {
            command.Parameters.AddWithValue("numbers", numbers.ToArray());
        }

        if (timeout is { } seconds)
        {
            command.CommandTimeout = seconds;
        }

        var rows = new List<StrongRenderingCount>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new StrongRenderingCount(
                reader.GetString(0), (int)reader.GetInt64(1), reader.GetString(2), (int)reader.GetInt64(3)));
        }

        return rows;
    }
}
