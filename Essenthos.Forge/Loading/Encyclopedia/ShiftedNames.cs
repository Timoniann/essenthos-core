using Essenthos.Core.Loading.Links;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// A name a statistical alignment carries one place off, onto a word that spells another name of
/// the seed's verse.
///
/// <para>
/// In a list of names the aligner has nothing but position to go on, and where the translation
/// renders, drops or adds one name, every name after it lands on its neighbour. The Ge'ez ወዛኦን of
/// Genesis 36:27 is Zaavan, and the aligner paired it with עֲקָן, so Akan was carried onto Zaavan's
/// name; Brenton's Σεμεῒ in 1 Chronicles 4:26 was given Hammuel, the name before Shimei's. The verse itself says which is which: the word spells, letter
/// for letter, a name the verse gives somebody else, and does not spell the name it was carried
/// from.
/// </para>
///
/// <para>
/// So a name carried across an aligner's link is not carried where the word it lands on:
/// </para>
/// <list type="number">
/// <item>is written as a name: with a capital, or in a script that has none;</item>
/// <item>is unlike the seed word (<see cref="NameLists.LeastLikeness"/>, each with two consonants
/// at least, so a name whose letters say nothing, יְהוָה, is never judged);</item>
/// <item>spells another word of the seed's verse that names another record, more closely than it
/// spells the seed: the same consonants where either has fewer than three, four fifths of them
/// otherwise (<see cref="Spelled"/>);</item>
/// <item>where the other name has no rendering of its own in the verse the name lands in: no other
/// word there written as a name is linked to it and alike it, and none names its record by anything
/// but an aligner's link. Brenton's Βαλλὰ of Genesis 14:2 is Bera, though its consonants are Bela's,
/// and Bela is Βαλάκ there;</item>
/// <item>where the other record is no title of the carried one, nor borne by it, nor a word of its
/// name: the <em>Gilead</em> of <em>Jabesh Gilead</em> is half the town's name;</item>
/// <item>and where the text does not write that spelling for the carried record elsewhere, by
/// something other than an aligner, at least twice and more often than for the other.</item>
/// </list>
///
/// <para>
/// It is asked of every name carried across an aligner's link, as it is carried, against what the
/// corpus names at that moment. A link a source states is never questioned here: what corrects a
/// source's own mapping is <see cref="CrossedNameLoader"/>.
/// </para>
/// </summary>
internal static class ShiftedNames
{
    /// <summary>How much of a name's consonants must agree for a word of three or more to spell it.</summary>
    private const double Spelled = 0.8;

    /// <summary>A name shorter than this spells another only where the two are the same consonants.</summary>
    private const int Short = 3;

    /// <summary>
    /// Every word an aligner's link reaches from a seed of this batch that is written as a name, with
    /// the seed's letters and those of every other word of the seed's verse that names a record, here
    /// or in this batch.
    /// </summary>
    private const string Candidates =
        """
        WITH seed AS MATERIALIZED (
            SELECT p.word_id, p.entity_id, w.verse_id, coalesce(w.morphology ->> 'consonantal', w.text) AS letters,
                   t.language
            FROM pending_annotation p
            JOIN word w ON w.id = p.word_id
            JOIN text t ON t.id = w.text_id
            WHERE p.through IS NULL
        ),
        named AS MATERIALIZED (
            SELECT w.verse_id, w.id AS word_id, a.entity_id
            FROM word_entity a
            JOIN word w ON w.id = a.word_id
            WHERE w.verse_id IN (SELECT verse_id FROM seed)
            UNION
            SELECT verse_id, word_id, entity_id FROM seed
        )
        SELECT reached.id, reached.text, coalesce(rt.language, ''), s.word_id, s.letters, coalesce(s.language, ''),
               s.entity_id,
               array_agg(n.word_id ORDER BY n.word_id, n.entity_id),
               array_agg(coalesce(ow.morphology ->> 'consonantal', ow.text) ORDER BY n.word_id, n.entity_id),
               array_agg(n.entity_id ORDER BY n.word_id, n.entity_id)
        FROM seed s
        JOIN link_word mine ON mine.word_id = s.word_id
        JOIN link l ON l.id = mine.link_id AND l.method = 'aligner'
        JOIN link_word other ON other.link_id = l.id AND other.side <> mine.side
        JOIN word reached ON reached.id = other.word_id
        JOIN text rt ON rt.id = reached.text_id
        JOIN named n ON n.verse_id = s.verse_id AND n.word_id <> s.word_id AND n.entity_id <> s.entity_id
        JOIN word ow ON ow.id = n.word_id
        WHERE reached.text ~ '^[[:upper:]]' OR lower(reached.text) = upper(reached.text)
        GROUP BY reached.id, reached.text, rt.language, s.word_id, s.letters, s.language, s.entity_id
        """;

    /// <summary>
    /// The words of the reached verse, other than the reached word and written as names, that any link
    /// joins to the other name's word: where it may have a rendering of its own.
    /// </summary>
    private const string Renderings =
        """
        SELECT c.at, beside.text, coalesce(bt.language, '')
        FROM unnest(@words, @others, @at) AS c(word_id, other_word, at)
        JOIN word w ON w.id = c.word_id
        JOIN link_word mine ON mine.word_id = c.other_word
        JOIN link_word across ON across.link_id = mine.link_id AND across.side <> mine.side
        JOIN word beside ON beside.id = across.word_id AND beside.verse_id = w.verse_id AND beside.id <> w.id
        JOIN text bt ON bt.id = beside.text_id
        WHERE beside.text ~ '^[[:upper:]]' OR lower(beside.text) = upper(beside.text)
        """;

    private const string Workspace =
        """
        DROP TABLE IF EXISTS shifted_candidate, shifted_name;
        CREATE TEMP TABLE shifted_candidate (word_id bigint, through bigint, entity_id integer, other integer)
        ON COMMIT DROP
        """;

    /// <summary>
    /// The candidates nothing else accounts for, as <c>shifted_name (word_id, through)</c>: no other
    /// word of the reached verse names the other record by anything but an aligner's link; the other
    /// record is no title of the carried one, nor borne by it, nor a word of its name; and the words
    /// the text writes alike name the carried record, by anything but an aligner's link, fewer than
    /// twice or no more often than the other. None of these counts a row this guard can refuse, so a
    /// second carry answers as the first did.
    /// </summary>
    private const string Refused =
        """
        CREATE TEMP TABLE shifted_name ON COMMIT DROP AS
        SELECT DISTINCT c.word_id, c.through
        FROM shifted_candidate c
        JOIN word w ON w.id = c.word_id
        JOIN entity carried ON carried.id = c.entity_id
        JOIN entity other ON other.id = c.other
        CROSS JOIN LATERAL (
            SELECT count(*) FILTER (WHERE a.entity_id = c.entity_id) AS mine,
                   count(*) FILTER (WHERE a.entity_id = c.other) AS theirs
            FROM word same
            JOIN word_entity a ON a.word_id = same.id
            WHERE same.text_id = w.text_id AND same.normalised_text = w.normalised_text
              AND coalesce(a.note, '') NOT LIKE @aligned) usage
        WHERE NOT (usage.mine >= 2 AND usage.mine > usage.theirs)
          AND NOT lower(other.name) = ANY (regexp_split_to_array(lower(carried.name), '[^[:alpha:]]+'))
          AND NOT EXISTS (SELECT 1 FROM title_bearer borne
                          WHERE (borne.title_entity_id = c.entity_id AND borne.bearer_entity_id = c.other)
                             OR (borne.title_entity_id = c.other AND borne.bearer_entity_id = c.entity_id))
          AND NOT EXISTS (SELECT 1 FROM word beside
                          JOIN word_entity named ON named.word_id = beside.id
                          WHERE beside.verse_id = w.verse_id AND beside.id <> w.id AND named.entity_id = c.other
                            AND coalesce(named.note, '') NOT LIKE @aligned);
        CREATE INDEX ON shifted_name (word_id, through)
        """;

    /// <summary>How the note of a name carried across an aligner's link reads.</summary>
    private const string Aligned = "through % linked by aligner";

    private sealed record Candidate(long Word, long Through, int Entity, int Other, long OtherWord, string OtherLetters);

    /// <summary>
    /// Makes <c>shifted_name</c> for the seeds in <c>pending_annotation</c>, which <see cref="Annotating.Carry"/>
    /// then leaves alone. Dropped and made again on each call, as the seeds are.
    /// </summary>
    public static async Task Prepare(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var inTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        await using (var workspace = new NpgsqlCommand(Workspace, connection, inTransaction))
        {
            await workspace.ExecuteNonQueryAsync(cancellationToken);
        }

        var letters = new Dictionary<(string, string), string>();
        string Skeleton(string written, string language) =>
            letters.TryGetValue((written, language), out var known)
                ? known
                : letters[(written, language)] = NameLists.Skeleton(written, language);

        var candidates = new List<Candidate>();
        await using (var read = new NpgsqlCommand(Candidates, connection, inTransaction))
        await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var word = Skeleton(reader.GetString(1), reader.GetString(2));
                var seedLanguage = reader.GetString(5);
                var seed = Skeleton(reader.GetString(4), seedLanguage);
                if (word.Length < 2 || seed.Length < 2)
                {
                    continue;
                }

                var asSeed = NameLists.Alike(word, seed);
                if (asSeed >= NameLists.LeastLikeness)
                {
                    continue;
                }

                var otherWords = reader.GetFieldValue<long[]>(7);
                var others = reader.GetFieldValue<string[]>(8);
                var records = reader.GetFieldValue<int[]>(9);
                for (var at = 0; at < others.Length; at++)
                {
                    var other = Skeleton(others[at], seedLanguage);
                    if (Spells(word, other) && NameLists.Alike(word, other) > asSeed)
                    {
                        candidates.Add(new Candidate(
                            reader.GetInt64(0), reader.GetInt64(3), reader.GetInt32(6), records[at], otherWords[at], other));
                    }
                }
            }
        }

        var rendered = new HashSet<int>();
        if (candidates.Count > 0)
        {
            await using var read = new NpgsqlCommand(Renderings, connection, inTransaction);
            read.Parameters.AddWithValue("words", candidates.Select(c => c.Word).ToArray());
            read.Parameters.AddWithValue("others", candidates.Select(c => c.OtherWord).ToArray());
            read.Parameters.AddWithValue("at", Enumerable.Range(0, candidates.Count).ToArray());
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var at = reader.GetInt32(0);
                if (NameLists.Alike(Skeleton(reader.GetString(1), reader.GetString(2)), candidates[at].OtherLetters)
                    >= NameLists.LeastLikeness)
                {
                    rendered.Add(at);
                }
            }
        }

        var unrendered = candidates.Where((_, at) => !rendered.Contains(at)).ToList();
        if (unrendered.Count > 0)
        {
            await using var writer = await connection.BeginBinaryImportAsync(
                "COPY shifted_candidate (word_id, through, entity_id, other) FROM STDIN (FORMAT BINARY)",
                cancellationToken);
            foreach (var candidate in unrendered)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(candidate.Word, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(candidate.Through, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(candidate.Entity, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(candidate.Other, NpgsqlDbType.Integer, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using var refuse = new NpgsqlCommand(Refused, connection, inTransaction);
        refuse.Parameters.AddWithValue("aligned", Aligned);
        await refuse.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Whether a word's consonants are another name's (<see cref="Spelled"/>, <see cref="Short"/>).</summary>
    internal static bool Spells(string word, string name) =>
        Math.Min(word.Length, name.Length) < Short
            ? word.Length > 1 && word == name
            : NameLists.Alike(word, name) >= Spelled;
}
