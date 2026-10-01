using System.Data;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Corpus;

/// <summary>One phrase a translation writes for a Strong number, its place among them and how many links write it.</summary>
internal sealed record StrongRenderingCount(string Number, int Rank, string Phrase, int Uses);

/// <summary>
/// How often a number stands in the edition it is counted in, how many of those words the text's links
/// reach, and how many links of each method reach them.
/// </summary>
internal sealed record StrongReachCount(
    string Number,
    int WitnessId,
    int Occurrences,
    int Reached,
    IReadOnlyList<(LinkMethod Method, int Links)> Methods);

/// <summary>Everything the entry page says about how one text renders every number it renders.</summary>
internal sealed record StrongTextCount(
    IReadOnlyList<StrongRenderingCount> Renderings,
    IReadOnlyList<StrongReachCount> Reach);

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
/// of every link for a number like the conjunction. A few numbers are counted from the words carrying
/// them; a whole text from every link between it and its originals, which is the plan that reads the
/// whole of it in one pass, and which the reach of every number is then counted from as well.
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

    /// <summary>How many renderings the load keeps for each number: as many as the entry page lists.</summary>
    public const int Kept = 200;

    private const string Rendering = "l.relation IN ('renders', 'equals')";

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
               AND {Rendering}
         ),
         {Ranked}
         """;

    /// <summary>
    /// Every word of the originals a link of the text renders, read once for the whole text: the
    /// phrases, the reach and the methods are each counted from it.
    ///
    /// <para>
    /// Read from the text's links to its words, each step materialised so the planner cannot start
    /// from the other end. Started from the numbered words of the originals, as it prefers, it read
    /// every link of every text those words stand in — eleven million link words to find the King
    /// James's half million.
    /// </para>
    /// </summary>
    private const string Sides =
        $"""
         DROP TABLE IF EXISTS strong_side;
         CREATE TEMP TABLE strong_side AS
         WITH pair AS MATERIALIZED (
             SELECT l.id, l.method
             FROM link l
             WHERE ((l.from_text_id = @text AND l.to_text_id = ANY(@witnesses))
                    OR (l.to_text_id = @text AND l.from_text_id = ANY(@witnesses)))
               AND {Rendering}
         ),
         named AS MATERIALIZED (
             SELECT s.link_id, s.side, s.word_id, l.method
             FROM pair l
             JOIN link_word s ON s.link_id = l.id
         )
         SELECT s.link_id, s.side, s.word_id, sw.strong_number AS number, sw.text_id AS witness_id, s.method
         FROM named s
         JOIN word sw ON sw.id = s.word_id AND sw.text_id = ANY(@witnesses) AND sw.strong_number IS NOT NULL;
         ANALYZE strong_side;
         """;

    private const string OfSides =
        $"""
         WITH pairs AS (SELECT DISTINCT link_id, number, side FROM strong_side),
         {Ranked}
         """;

    /// <summary>Each number is reached in the one edition counted for its language, and nowhere else.</summary>
    private const string Counted =
        "witness_id = CASE WHEN number LIKE 'G%' THEN @greek ELSE @hebrew END";

    private const string Occurrences =
        """
        SELECT strong_number, count(*)
        FROM word
        WHERE (text_id = @hebrew AND strong_number NOT LIKE 'G%')
           OR (text_id = @greek AND strong_number LIKE 'G%')
        GROUP BY strong_number
        """;

    private const string Reached =
        $"SELECT number, count(DISTINCT word_id) FROM strong_side WHERE {Counted} GROUP BY number";

    private const string Methods =
        $"SELECT number, method, count(DISTINCT link_id) FROM strong_side WHERE {Counted} GROUP BY number, method";

    /// <summary>The commonest <paramref name="take"/> phrases for each of <paramref name="numbers"/>.</summary>
    public static async Task<List<StrongRenderingCount>> Count(
        AppDbContext db,
        int textId,
        IReadOnlyCollection<string> numbers,
        int take,
        CancellationToken cancellationToken)
    {
        var witnesses = LinkedOriginals.Primary(await LinkedOriginals.Of(db, textId, cancellationToken))
            .Select(original => original.Id)
            .ToArray();

        await using var command = new NpgsqlCommand(OfNumbers, await Open(db, cancellationToken));
        command.Parameters.AddWithValue("text", textId);
        command.Parameters.AddWithValue("witnesses", witnesses);
        command.Parameters.AddWithValue("take", take);
        command.Parameters.AddWithValue("numbers", numbers.ToArray());

        return await Read(command, Phrase, cancellationToken);
    }

    /// <summary>
    /// The commonest <see cref="Kept"/> phrases and the reach of every number the text renders, counted
    /// over the editions it is most fully joined to (<paramref name="primary"/>): what the entry page
    /// counts for one number as it is asked, for all of them at once.
    /// </summary>
    public static async Task<StrongTextCount> CountText(
        AppDbContext db,
        int textId,
        IReadOnlyList<LinkedOriginal> primary,
        CancellationToken cancellationToken)
    {
        var connection = await Open(db, cancellationToken);
        var hebrew = LinkedOriginals.WitnessFor(primary, "H1")?.Id ?? 0;
        var greek = LinkedOriginals.WitnessFor(primary, "G1")?.Id ?? 0;

        NpgsqlCommand Command(string sql)
        {
            var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("text", textId);
            command.Parameters.AddWithValue("witnesses", primary.Select(original => original.Id).ToArray());
            command.Parameters.AddWithValue("take", Kept);
            command.Parameters.AddWithValue("hebrew", hebrew);
            command.Parameters.AddWithValue("greek", greek);
            return command;
        }

        await using (var sides = Command(Sides))
        {
            await sides.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await using var phrases = Command(OfSides);
            await using var occurrences = Command(Occurrences);
            await using var reached = Command(Reached);
            await using var methods = Command(Methods);

            var renderings = await Read(phrases, Phrase, cancellationToken);
            var standing = await Read(occurrences, Tally, cancellationToken);
            var reach = (await Read(reached, Tally, cancellationToken)).ToDictionary(row => row.Number, row => row.Count);
            var made = (await Read(methods, reader => (
                    Number: reader.GetString(0),
                    Method: EnumSpelling.ToLinkMethod(reader.GetString(1)),
                    Links: (int)reader.GetInt64(2)), cancellationToken))
                .ToLookup(row => row.Number, row => (row.Method, row.Links));

            return new StrongTextCount(
                renderings,
                [
                    .. standing.Select(row => new StrongReachCount(
                        row.Number,
                        row.Number.StartsWith('G') ? greek : hebrew,
                        row.Count,
                        reach.GetValueOrDefault(row.Number),
                        [.. Ordered(made[row.Number])])),
                ]);
        }
        finally
        {
            await using var drop = new NpgsqlCommand("DROP TABLE IF EXISTS strong_side", connection);
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>The methods that reach a number, the one most links rest on first, ties as they are spelled.</summary>
    public static IEnumerable<(LinkMethod Method, int Links)> Ordered(IEnumerable<(LinkMethod Method, int Links)> methods) =>
        methods
            .OrderByDescending(row => row.Links)
            .ThenBy(row => EnumSpelling.Of(row.Method), StringComparer.Ordinal);

    private static StrongRenderingCount Phrase(NpgsqlDataReader reader) =>
        new(reader.GetString(0), (int)reader.GetInt64(1), reader.GetString(2), (int)reader.GetInt64(3));

    private static (string Number, int Count) Tally(NpgsqlDataReader reader) =>
        (reader.GetString(0), (int)reader.GetInt64(1));

    private static async Task<NpgsqlConnection> Open(AppDbContext db, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return connection;
    }

    private static async Task<List<T>> Read<T>(
        NpgsqlCommand command,
        Func<NpgsqlDataReader, T> row,
        CancellationToken cancellationToken)
    {
        var rows = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(row(reader));
        }

        return rows;
    }
}
