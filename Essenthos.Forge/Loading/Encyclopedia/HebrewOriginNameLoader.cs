using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Occurrences">
/// Unnamed words of the Greek witnesses whose name Strong derives from a Hebrew name he numbers.
/// </param>
/// <param name="Settled">Of those, the ones whose verse the encyclopedia names exactly one bearer in.</param>
/// <param name="Eponyms">
/// Verses naming only the ancestor a people is named after, where the word is as likely the people.
/// </param>
/// <param name="Several">Verses naming two or more of the bearers, which say nothing about the word.</param>
/// <param name="Unlisted">Verses naming none of them.</param>
/// <param name="Written">Annotations written under this pass's source, the carried ones included.</param>
internal sealed record HebrewOriginNameOutcome(
    bool AlreadyLoaded,
    int Occurrences,
    int Settled,
    int Eponyms,
    int Several,
    int Unlisted,
    int Written,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the Greek names of Hebrew origin are already named"
            : $"{Settled} of {Occurrences} unnamed Greek names of Hebrew origin are the one bearer the " +
              $"verse names, in {Elapsed}; left alone: {Eponyms} where the one bearer named is the " +
              $"ancestor of a people of that name, {Several} in a verse naming more than one bearer and " +
              $"{Unlisted} in a verse naming none. {Written} words written in all. Per text: " +
              string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// The Old Testament names the New Testament writes in Greek: Hezron, Tamar, Nahshon and Rehoboam in
/// Matthew 1, Elijah and Jonah wherever Jesus names them.
///
/// <para>
/// The encyclopedia records most of these people with their Hebrew Strong number alone, so no record
/// bears Ἐσρώμ's number, and the resolution by number and the Greek namesake pass both ask only of
/// the Greek. Matthew's genealogy was blank in every text for that reason, and where a record does
/// bear the Greek number it can be the wrong one: the encyclopedia gives Φάρες to Peresh the
/// Manassite rather than to Perez.
/// </para>
///
/// <para>
/// <strong>Strong says which Hebrew name a Greek one is.</strong> His Greek entry for Ἐσρώμ reads
/// <em>of Hebrew origin (H2696)</em>, and H2696 is the number the encyclopedia records Hezron under.
/// So the records bearing the Hebrew number are bearers of the Greek name too, beside the records
/// bearing the Greek number itself, and the verse chooses among them exactly as it does for the Greek
/// namesakes: where a dataset's list names one of them in the word's verse, the word is that record.
/// Only the plain derivation is read — <em>probably</em> and <em>from the same as</em> are Strong
/// hedging, and a hedge is not a statement to join on.
/// </para>
///
/// <para>
/// <strong>An eponym is not taken on a list's word.</strong> Ἰσραήλ in <em>the lost sheep of the
/// house of Israel</em> is the people, but a people has no dataset list to be named in, so the list
/// names Jacob. Where the one bearer the verse names is the ancestor a people bearing the same Hebrew
/// number is named after, nothing is written.
/// </para>
///
/// <para>
/// Idempotent on its own source. It runs after every pass that names a Greek word and adds answers
/// only where none of them did.
/// </para>
/// </summary>
internal sealed class HebrewOriginNameLoader(AppDbContext db, ILogger<HebrewOriginNameLoader> logger)
{
    public const string Source =
        "Essenthos, reading a Greek name Strong derives from a Hebrew one as the one record bearing " +
        "either name that the encyclopedia names in this verse";

    /// <summary>The standing a lone verse list has where it tells the Greek namesakes apart.</summary>
    private const double Alone = 0.96;

    private const string Ours = "Essenthos%";

    /// <summary>
    /// Strong's derivation of a Greek name from a numbered Hebrew one, stated without a hedge. The
    /// capture is the Hebrew number.
    /// </summary>
    private const string OfHebrewOrigin = @"^of Hebrew origin \((H[0-9]+)\)";

    private static readonly string Workspace =
        $"""
         CREATE TEMP TABLE origin (number text PRIMARY KEY, hebrew text NOT NULL) ON COMMIT DROP;
         INSERT INTO origin (number, hebrew)
         SELECT s.strong_number, (regexp_match(s.derivation, '{OfHebrewOrigin}'))[1]
         FROM strong_entry s
         WHERE s.derivation ~ '{OfHebrewOrigin}'
           AND lower(left(s.lemma, 1)) <> left(s.lemma, 1);
         CREATE TEMP TABLE bearer (number text NOT NULL, entity_id integer NOT NULL) ON COMMIT DROP;
         INSERT INTO bearer (number, entity_id)
         SELECT DISTINCT o.number, {EntityCandidates.Resolves}
         FROM origin o
         JOIN entity_name n ON n.hebrew_strong_number = o.hebrew OR n.greek_strong_number = o.number
         WHERE coalesce(n.kind, '') NOT IN ('title', 'description');
         CREATE INDEX ON bearer (number);
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
                        JOIN entity_name n ON n.entity_id = people.id
                        WHERE people.kind = 'people'
                          AND people.origin_entity_id = pick.entity_id
                          AND n.hebrew_strong_number = o.hebrew)
         FROM word w
         JOIN text t ON t.id = w.text_id AND t.slug = ANY(@witnesses)
         JOIN origin o ON o.number = w.strong_number
         JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
         CROSS JOIN LATERAL (
             SELECT count(*) AS listed, min(b.entity_id) AS entity_id
             FROM bearer b
             WHERE b.number = w.strong_number
               AND EXISTS (
                   SELECT 1 FROM entity_verse ev
                   WHERE ev.entity_id = b.entity_id
                     AND ev.canonical_book = r.canonical_book
                     AND ev.canonical_chapter = r.canonical_chapter
                     AND ev.canonical_verse = r.canonical_verse
                     AND ev.source NOT LIKE @ours)) pick
         WHERE {EntityAnnotationLoader.GreekNoun}
           AND NOT EXISTS (SELECT 1 FROM word_entity spoken WHERE spoken.word_id = w.id)
         """;

    private const string Seed =
        """
        INSERT INTO pending_annotation (word_id, entity_id, confidence, corroborated, note)
        SELECT o.word_id, o.entity_id, @confidence, FALSE,
               o.number || ', a Greek name of Hebrew origin, of whose bearers the encyclopedia names '
               || 'only this one in this verse'
        FROM occurrence o
        WHERE o.listed = 1 AND NOT o.eponym
        """;

    private const string Tally =
        """
        SELECT count(*), count(*) FILTER (WHERE listed = 1 AND NOT eponym),
               count(*) FILTER (WHERE listed = 1 AND eponym), count(*) FILTER (WHERE listed > 1),
               count(*) FILTER (WHERE listed = 0)
        FROM occurrence
        """;

    public async Task<HebrewOriginNameOutcome> Load(CancellationToken cancellationToken = default)
    {
        if (await db.WordEntities.AnyAsync(a => a.Source == Source, cancellationToken))
        {
            logger.LogInformation("The Greek names of Hebrew origin are already named; nothing to do");
            return new HebrewOriginNameOutcome(true, 0, 0, 0, 0, 0, 0, [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Workspace, cancellationToken);
        await Annotating.Run(connection, transaction, Occurrences, cancellationToken,
            ("witnesses", EntityCandidates.GreekWitnesses), ("ours", Ours));
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

        var outcome = new HebrewOriginNameOutcome(
            false, tally[0], tally[1], tally[2], tally[3], tally[4], byText.Sum(t => t.Words), byText,
            started.Elapsed);
        logger.LogInformation("Named the Greek names of Hebrew origin: {Outcome}", outcome);
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
