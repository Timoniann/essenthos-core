using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Occurrences">
/// Words of the Greek witnesses that are a name several records bear and that nothing had named.
/// </param>
/// <param name="Settled">Of those, the ones whose verse the encyclopedia names exactly one bearer in.</param>
/// <param name="Several">Verses it names two or more of the bearers in, which say nothing about the word.</param>
/// <param name="Unlisted">Verses it names none of them in.</param>
/// <param name="Written">Annotations written under this pass's source, the carried ones included.</param>
internal sealed record GreekNamesakeOutcome(
    bool AlreadyLoaded,
    int Occurrences,
    int Settled,
    int Several,
    int Unlisted,
    int Written,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the Greek names several people share are already told apart"
            : $"{Settled} of {Occurrences} Greek words bearing a name several records share are told " +
              $"apart by the verse, in {Elapsed}; {Several} stand in a verse naming more than one of " +
              $"the bearers and {Unlisted} in a verse naming none, and are left alone. {Written} words " +
              "written in all. Per text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// Which Jesus, which Mary and which Herod a Greek word means, where the verse leaves only one.
///
/// <para>
/// The resolution by number stops at a name several records bear, and in the New Testament that is
/// most of the names a reader is looking for: Ἰησοῦς is Jesus, Joshua son of Nun, Jesus called
/// Justus and a man of Luke's genealogy, and Ἰωσήφ, Μαρία, Ἰωάννης and Σίμων are several people
/// each. Those numbers stand on more than two thousand words of Nestle alone, and every translation
/// reached from them named nobody at any of them.
/// </para>
///
/// <para>
/// <strong>The verse is what tells them apart.</strong> The encyclopedia records, for every person
/// and place, the verses it is named in. Where exactly one of the records bearing a word's number is
/// named in that word's verse, the word is that record: Ἡρῴδης in Matthew 2:3 is the Herod the
/// encyclopedia names there, and the Herod of Matthew 14 is not. Where the verse names two of them —
/// the Mary who is James's mother beside Mary Magdalene — or none, nothing is written, because the
/// list is keyed to the verse and cannot say which word is which.
/// </para>
///
/// <para>
/// <strong>Only a dataset's list is read.</strong> Some verse lists here are this corpus's own,
/// read back off the words it annotated, and choosing an annotation by them would be the corpus
/// agreeing with itself. And only a name counts as a bearer: a title filed under a person says what
/// he is, and <em>Christ</em> is not made the word that names Jesus by his being named in the verse.
/// </para>
///
/// <para>
/// <strong>What it rests on is a list, and the row says so.</strong> The method is the form of the
/// word, as it is for the gazetteer settling the places that share a name: a verse list is a
/// statement about the verse, and reaching from there to the word is the lexicon's capital, the
/// witness's noun and the number. It stands below a reading of the verse, so a reading of the Greek
/// can overrule it where the two disagree, and it writes no corroboration: the list that agrees with
/// the annotation is the list it was made from.
/// </para>
///
/// <para>
/// Idempotent on its own source. It runs after every pass that names a Greek word and adds answers
/// only where none of them did.
/// </para>
/// </summary>
internal sealed class GreekNamesakeLoader(AppDbContext db, ILogger<GreekNamesakeLoader> logger)
{
    public const string Source =
        "Essenthos, telling apart the people and places that share a Greek name: the encyclopedia " +
        "names exactly one of them in this verse, the lexicon writes the name with a capital and the " +
        "witness parses the word as a noun";

    /// <summary>
    /// How sure the corpus is of a word the verse list alone settled.
    ///
    /// Not measured on the Greek, which nothing has reviewed. It is the standing the gazetteer alone
    /// is given when it tells namesake places apart, measured there at a 3.6% disagreement with an
    /// independent reading; and the review of the Hebrew readings found the person lists wrong in at
    /// least 61 verses the audit checked, the lists agreeing with a reading that was itself wrong —
    /// so a lone list is short of the readings' 0.99, and nothing measured puts it above this.
    /// </summary>
    private const double Alone = 0.96;

    /// <summary>
    /// Where a verse list is this corpus's own. Every one of them is written under a source that
    /// begins with the project's name, and every dataset's list under the dataset's.
    /// </summary>
    private const string Ours = "Essenthos%";

    /// <summary>
    /// The numbers several records bear as a name, among the records the Greek can reach.
    /// </summary>
    private static readonly string Workspace =
        $"""
         CREATE TEMP TABLE namesake (number text NOT NULL, entity_id integer NOT NULL) ON COMMIT DROP;
         INSERT INTO namesake (number, entity_id)
         SELECT number, entity_id
         FROM (SELECT number, entity_id, count(*) OVER (PARTITION BY number) AS bearers
               FROM (SELECT DISTINCT number, entity_id
                     FROM ({EntityCandidates.GreekNamesakes}) named) distinct_named) counted
         WHERE bearers > 1;
         CREATE INDEX ON namesake (number);
         CREATE TEMP TABLE occurrence (
             word_id bigint PRIMARY KEY,
             number text NOT NULL,
             bearers integer NOT NULL,
             listed integer NOT NULL,
             entity_id integer)
         ON COMMIT DROP
         """;

    /// <summary>
    /// Every unnamed word bearing one of those names, with how many of its bearers the encyclopedia
    /// names in the word's verse. The gate is the resolution's own: a noun whose lexicon lemma is
    /// written with a capital.
    /// </summary>
    private static readonly string Occurrences =
        $"""
         INSERT INTO occurrence (word_id, number, bearers, listed, entity_id)
         SELECT w.id, w.strong_number, bearing.bearers, pick.listed, pick.entity_id
         FROM word w
         JOIN text t ON t.id = w.text_id AND t.slug = ANY(@witnesses)
         JOIN strong_entry lexicon ON lexicon.strong_number = w.strong_number
              AND lower(left(lexicon.lemma, 1)) <> left(lexicon.lemma, 1)
         JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
         CROSS JOIN LATERAL (
             SELECT count(*) AS bearers FROM namesake n WHERE n.number = w.strong_number) bearing
         CROSS JOIN LATERAL (
             SELECT count(*) AS listed, min(n.entity_id) AS entity_id
             FROM namesake n
             WHERE n.number = w.strong_number
               AND EXISTS (
                   SELECT 1 FROM entity_verse ev
                   WHERE ev.entity_id = n.entity_id
                     AND ev.canonical_book = r.canonical_book
                     AND ev.canonical_chapter = r.canonical_chapter
                     AND ev.canonical_verse = r.canonical_verse
                     AND ev.source NOT LIKE @ours)) pick
         WHERE bearing.bearers > 1
           AND {EntityAnnotationLoader.GreekNoun}
           AND NOT EXISTS (SELECT 1 FROM word_entity spoken WHERE spoken.word_id = w.id)
         """;

    private const string Seed =
        """
        INSERT INTO pending_annotation (word_id, entity_id, confidence, corroborated, note)
        SELECT o.word_id, o.entity_id, @confidence, FALSE,
               o.number || ', a name ' || o.bearers || ' records bear, of which the encyclopedia '
               || 'names only this one in this verse'
        FROM occurrence o
        WHERE o.listed = 1
        """;

    private const string Tally =
        """
        SELECT count(*), count(*) FILTER (WHERE listed = 1), count(*) FILTER (WHERE listed > 1),
               count(*) FILTER (WHERE listed = 0)
        FROM occurrence
        """;

    public async Task<GreekNamesakeOutcome> Load(CancellationToken cancellationToken = default)
    {
        if (await db.WordEntities.AnyAsync(a => a.Source == Source, cancellationToken))
        {
            logger.LogInformation("The Greek names several people share are already told apart; nothing to do");
            return new GreekNamesakeOutcome(true, 0, 0, 0, 0, 0, [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Workspace, cancellationToken,
            ("witnesses", EntityCandidates.GreekWitnesses));
        await Annotating.Run(connection, transaction, Occurrences, cancellationToken,
            ("witnesses", EntityCandidates.GreekWitnesses), ("ours", Ours));
        var (occurrences, settled, several, unlisted) = await Counted(connection, transaction, cancellationToken);

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

        var outcome = new GreekNamesakeOutcome(
            false, occurrences, settled, several, unlisted, byText.Sum(t => t.Words), byText,
            started.Elapsed);
        logger.LogInformation("Told the Greek namesakes apart: {Outcome}", outcome);
        return outcome;
    }

    private static async Task<(int Occurrences, int Settled, int Several, int Unlisted)> Counted(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            Tally, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.CommandTimeout = Annotating.Patient;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ((int)reader.GetInt64(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2),
            (int)reader.GetInt64(3));
    }
}
