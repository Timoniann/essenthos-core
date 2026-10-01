using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Occurrences">
/// Unnamed BHSA names whose Strong number no record of the encyclopedia bears.
/// </param>
/// <param name="Settled">
/// Of those, the ones whose verse the encyclopedia names exactly one record in that is spelled the way
/// the King James renders the word.
/// </param>
/// <param name="Eponyms">Of the settled ones, records a people is named after, which are left alone.</param>
/// <param name="Several">Verses naming two or more records spelled that way.</param>
/// <param name="Written">Annotations written under this pass's source, the carried ones included.</param>
internal sealed record RenderedNameOutcome(
    bool AlreadyLoaded,
    int Occurrences,
    int Settled,
    int Eponyms,
    int Several,
    int Written,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the names the encyclopedia holds under another number are already named"
            : $"{Settled} of {Occurrences} names whose number no record bears are the one record the " +
              $"verse names under the King James's spelling, in {Elapsed}; left alone: {Eponyms} " +
              $"eponyms of a people and {Several} in a verse naming several records so spelled. " +
              $"{Written} words written in all. Per text: " +
              string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// The Hebrew names the encyclopedia holds under a Strong number BHSA does not print.
///
/// <para>
/// Every resolution here joins a word to a record by the number, and the encyclopedia's numbers and
/// the witnesses' are not always the same numbers. BHSA numbers Abner H74 and the encyclopedia files
/// him under H7410; Daniel's Aramaic chapters write H1841 and Nebuchadnezzar's H5020, and the records
/// carry the Hebrew H1840 and H5019; Hodiah, Hori, Shuah and Shuppim are one entry of Strong's off.
/// So all 61 of Abner's occurrences, Daniel through the whole of his own book, Shadrach, Meshach and
/// Micaiah named nobody, in any text.
/// </para>
///
/// <para>
/// <strong>Where no record bears the number, the verse and the spelling decide.</strong> The
/// encyclopedia lists the verses each record is named in, and the King James — the rendering the
/// corpus already reads Hebrew place names back from — prints the name. Where a dataset's list names
/// exactly one record in the verse whose name is spelled as the King James renders the word, the
/// word is that record. The spelling test is equality after the possessive and the hyphen are taken
/// off, so <em>Abner's</em> is Abner and <em>Merib-baal</em> is Meribbaal, and a record whose name
/// only resembles the word is nobody.
/// </para>
///
/// <para>
/// <strong>It never overrules a number.</strong> A word whose number any record bears is the
/// resolution's and the readings' question, and nothing here reaches it. And an eponym is left alone:
/// the Aramaic <em>children of Israel</em> of Ezra 6:16 is the people, and a people has no dataset
/// list to be named in, so the list names Jacob.
/// </para>
///
/// <para>
/// <strong>Only the Hebrew.</strong> BHSA marks what is a name, and a gentilic is an adjective there.
/// The Greek has nothing but the lexicon's capital, which Νινευῖται and Κύπριος carry as much as
/// Καῖσαρ, and the King James renders <em>the men of Nineveh</em>, so the spelling would put the city
/// on the people; and the encyclopedia files Mark's Levi under a label of James the son of Alphaeus.
/// </para>
///
/// <para>
/// Idempotent on its own source. It runs after every pass that names a Hebrew word and answers only
/// where none of them did.
/// </para>
/// </summary>
internal sealed class RenderedNameLoader(AppDbContext db, ILogger<RenderedNameLoader> logger)
{
    public const string Source =
        "Essenthos, reading a name whose Strong number no record bears as the one record named in " +
        "this verse that is spelled as the King James renders the word";

    /// <summary>The standing a lone verse list has where it tells namesakes apart.</summary>
    private const double Alone = 0.96;

    private const string Ours = "Essenthos%";

    /// <summary>
    /// A spelling as it is compared: without the English possessive that closes <em>Abner's</em> and
    /// without the hyphens, apostrophes and points of <em>Merib-baal</em>, and in lower case.
    /// </summary>
    private const string Folded =
        "lower(regexp_replace(regexp_replace({0}, '[''’]s?$', ''), '[^[:alnum:]]', '', 'g'))";

    private static readonly string Workspace =
        $"""
         CREATE TEMP TABLE numbered (number text PRIMARY KEY) ON COMMIT DROP;
         INSERT INTO numbered (number)
         SELECT DISTINCT trim(single)
         FROM entity_name n
         CROSS JOIN LATERAL unnest(string_to_array(
             concat_ws(',', n.hebrew_strong_number, n.greek_strong_number), ',')) single
         WHERE coalesce(n.kind, '') NOT IN ('title', 'description')
         ON CONFLICT DO NOTHING;
         CREATE TEMP TABLE spelling (entity_id integer NOT NULL, spelled text NOT NULL) ON COMMIT DROP;
         INSERT INTO spelling (entity_id, spelled)
         SELECT DISTINCT e.id, {string.Format(Folded, "known.name")}
         FROM entity e
         CROSS JOIN LATERAL (
             SELECT e.name
             UNION ALL
             SELECT n.label FROM entity_name n
             WHERE n.entity_id = e.id AND coalesce(n.kind, '') NOT IN ('title', 'description')) known
         WHERE e.kind IN ('person', 'place');
         CREATE INDEX ON spelling (spelled);
         CREATE TEMP TABLE occurrence (
             word_id bigint PRIMARY KEY,
             number text NOT NULL,
             listed integer NOT NULL,
             entity_id integer,
             eponym boolean NOT NULL)
         ON COMMIT DROP
         """;

    private static readonly string Occurrences =
        $"""
         INSERT INTO occurrence (word_id, number, listed, entity_id, eponym)
         SELECT w.id, w.strong_number, pick.listed, pick.entity_id,
                EXISTS (SELECT 1 FROM entity people
                        WHERE people.kind = 'people' AND people.origin_entity_id = pick.entity_id)
         FROM word w
         JOIN text t ON t.id = w.text_id AND t.slug = @witness
         JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
         CROSS JOIN LATERAL (
             SELECT count(DISTINCT s.entity_id) AS listed, min(s.entity_id) AS entity_id
             FROM link_word mine
             JOIN link l ON l.id = mine.link_id
             JOIN text rendering ON rendering.id = l.from_text_id AND rendering.slug = @rendering
             JOIN link_word theirs ON theirs.link_id = l.id AND theirs.side <> mine.side
             JOIN word printed ON printed.id = theirs.word_id
             JOIN spelling s ON s.spelled = {string.Format(Folded, "printed.text")}
             WHERE mine.word_id = w.id
               AND EXISTS (
                   SELECT 1 FROM entity_verse ev
                   WHERE ev.entity_id = s.entity_id
                     AND ev.canonical_book = r.canonical_book
                     AND ev.canonical_chapter = r.canonical_chapter
                     AND ev.canonical_verse = r.canonical_verse
                     AND ev.source NOT LIKE @ours)) pick
         WHERE w.strong_number IS NOT NULL
           AND w.morphology->>'pos' = 'nmpr'
           AND NOT EXISTS (SELECT 1 FROM numbered bears WHERE bears.number = w.strong_number)
           AND NOT EXISTS (SELECT 1 FROM word_entity spoken WHERE spoken.word_id = w.id)
         """;

    private const string Seed =
        """
        INSERT INTO pending_annotation (word_id, entity_id, confidence, corroborated, note)
        SELECT o.word_id, o.entity_id, @confidence, FALSE,
               o.number || ', a number no record bears; the encyclopedia names only this record in '
               || 'this verse under the name the King James prints for the word'
        FROM occurrence o
        WHERE o.listed = 1 AND NOT o.eponym
        """;

    private const string Tally =
        """
        SELECT count(*), count(*) FILTER (WHERE listed = 1 AND NOT eponym),
               count(*) FILTER (WHERE listed = 1 AND eponym), count(*) FILTER (WHERE listed > 1)
        FROM occurrence
        """;

    public async Task<RenderedNameOutcome> Load(CancellationToken cancellationToken = default)
    {
        if (await db.WordEntities.AnyAsync(a => a.Source == Source, cancellationToken))
        {
            logger.LogInformation("The names the encyclopedia holds under another number are already named; nothing to do");
            return new RenderedNameOutcome(true, 0, 0, 0, 0, 0, [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Workspace, cancellationToken);
        await Annotating.Run(connection, transaction, Occurrences, cancellationToken,
            ("witness", EntityCandidates.Witness),
            ("rendering", EntityCandidates.Rendering), ("ours", Ours));
        var tally = await Counted(connection, transaction, cancellationToken);

        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
        await Annotating.Run(connection, transaction, Seed, cancellationToken, ("confidence", Alone));
        await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);

        var method = EnumSpelling.Of(LinkMethod.Lexical);
        await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
            ("method", method), ("source", Source));
        await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
            ("method", method), ("source", Source));

        var byText = await Annotating.ByText(connection, transaction, Source, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new RenderedNameOutcome(
            false, tally[0], tally[1], tally[2], tally[3], byText.Sum(t => t.Words), byText, started.Elapsed);
        logger.LogInformation("Named the names the encyclopedia holds under another number: {Outcome}", outcome);
        return outcome;
    }

    private static async Task<int[]> Counted(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            Tally, connection, (NpgsqlTransaction)transaction.GetDbTransaction());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return Enumerable.Range(0, reader.FieldCount).Select(i => (int)reader.GetInt64(i)).ToArray();
    }
}
