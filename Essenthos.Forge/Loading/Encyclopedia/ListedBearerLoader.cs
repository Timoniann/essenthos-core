using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Occurrences">BHSA words marked a name that nothing had named and no reading answered.</param>
/// <param name="Settled">Of those, the ones the verse list and the marking together leave one answer for.</param>
/// <param name="Tribal">Names a people bears as well, which a verse list cannot tell from the man.</param>
/// <param name="Namesakes">Names several persons bear, which are the readings' question and not a list's.</param>
/// <param name="Several">Verses the list names two or more of the bearers in, which say nothing about the word.</param>
/// <param name="Unlisted">Verses the list names none of them in.</param>
/// <param name="Unmarked">One bearer listed, of a kind the marking does not commit the word to.</param>
/// <param name="Written">Annotations written under this pass's source, the carried ones included.</param>
internal sealed record ListedBearerOutcome(
    bool AlreadyLoaded,
    bool NoReadings,
    int Occurrences,
    int Settled,
    int Tribal,
    int Namesakes,
    int Several,
    int Unlisted,
    int Unmarked,
    int Written,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded ? "the Hebrew names a verse list leaves one bearer for are already named"
        : NoReadings ? "no model readings are on this disk, so which names were already read is unknown and nothing was written"
        : $"{Settled} of {Occurrences} unnamed Hebrew names are the one bearer the verse list names, in " +
          $"{Elapsed}; left alone: {Tribal} a people bears too, {Namesakes} several persons bear, " +
          $"{Several} in a verse naming more than one bearer, {Unlisted} in a verse naming none and " +
          $"{Unmarked} whose one listed bearer is of a kind the marking does not allow. {Written} words " +
          "written in all. Per text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// The Hebrew names that nothing settled and that the verse leaves one answer for.
///
/// <para>
/// The resolution by number answers only where BHSA's marking commits a word to one kind and one
/// record of that kind bears its number. Two shapes fall through it and stay blank however plain the
/// verse is. <strong>A marking of several kinds</strong>: BHSA marks the lexemes of the table of
/// nations <c>pers,gens,topo</c>, so Ham, Javan, Madai, Tiras and Seba of 1 Chronicles 1, which name
/// the same men as Genesis 10, name nobody. <strong>A place several records bear</strong>: Babylon
/// is a Hebrew name the encyclopedia holds for the city and, in Greek, for Rome, so all of Jeremiah's
/// Babylons are blank, and so are Gath, Kedesh, Bethlehem and Ramoth in the Levitical and tribal
/// lists.
/// </para>
///
/// <para>
/// <strong>The verse list is what settles both,</strong> exactly as it settles the Greek namesakes
/// and the places the gazetteer tells apart: where a dataset's list names exactly one of the records
/// bearing the word's number in the word's verse, the word is that record. Only a dataset's list is
/// read, never this corpus's own, and only a name counts as a bearer, never a title.
/// </para>
///
/// <para>
/// <strong>The marking still has to allow the answer.</strong> A place is taken only where BHSA marks
/// the lexeme a place, or marks it only a person — which is the case the lists overrule in the
/// resolution too. A person is taken where it marks a person, or marks a people and nothing else in
/// a verse that speaks of sons, fathers, wives or bearing: Zilpah is a people to BHSA and Leah's maid
/// to Genesis 30:9, and the Tubal Ezekiel trades with is the people however the list files it. A
/// plural people stays a people — <em>Letushim and Leummim</em> are the Hebrew's gentilic plurals
/// whatever a list calls them.
/// </para>
///
/// <para>
/// <strong>Three things it will not decide.</strong> A name a people bears as well — Reuben, Ephraim,
/// Israel — is the tribe or the man by what the sentence is doing, and a people has no dataset list
/// to be named in, so the list can only ever name the man. A name several persons bear is the
/// readings' question: the lists put Ezra the scribe at Nehemiah 12:1, among the priests who came up
/// with Zerubbabel a century before him, and a list read alone would publish that. And a word a
/// reading already answered — even with <em>nobody the encyclopedia holds</em> — has an answer that
/// stands above a list. So without the readings on disk, nothing is written.
/// </para>
///
/// <para>
/// Idempotent on its own source. It runs after every pass that names a Hebrew word and adds answers
/// only where none of them did.
/// </para>
/// </summary>
internal sealed class ListedBearerLoader(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<ListedBearerLoader> logger)
{
    public const string Source =
        "Essenthos, reading a Hebrew name nothing else settled as the one record bearing it that the " +
        "encyclopedia names in this verse, of a kind BHSA's marking allows";

    /// <summary>
    /// How sure the corpus is of a word a verse list alone settled: the standing a lone list is given
    /// where it tells the Greek namesakes and the gazetteer's places apart, measured there at a 3.6%
    /// disagreement with an independent reading.
    /// </summary>
    private const double Alone = 0.96;

    private const string Ours = "Essenthos%";

    /// <summary>
    /// The words of a verse that make it speak of kin: son, daughter, father, mother, brother,
    /// sister, wife, maid, concubine, and bearing a child.
    /// </summary>
    private static readonly string[] Kinship =
        ["H1121", "H1323", "H1", "H517", "H251", "H269", "H802", "H8198", "H6370", "H3205"];

    /// <summary>The word Chronicles names a town by, which makes the name after it the town.</summary>
    private const string FatherOf = "H1";

    private static readonly string Workspace =
        $"""
         CREATE TEMP TABLE read_word (word_id bigint PRIMARY KEY) ON COMMIT DROP;
         CREATE TEMP TABLE bearer (number text NOT NULL, entity_id integer NOT NULL, kind text NOT NULL)
         ON COMMIT DROP;
         INSERT INTO bearer (number, entity_id, kind)
         SELECT DISTINCT n.hebrew_strong_number, {EntityCandidates.Resolves}, e.kind
         FROM entity_name n
         JOIN entity e ON e.id = {EntityCandidates.Resolves}
         WHERE n.hebrew_strong_number IS NOT NULL AND position(',' IN n.hebrew_strong_number) = 0
           AND coalesce(n.kind, '') NOT IN ('title', 'description');
         CREATE INDEX ON bearer (number);
         CREATE TEMP TABLE occurrence (
             word_id bigint PRIMARY KEY,
             number text NOT NULL,
             marking text NOT NULL,
             peoples integer NOT NULL,
             persons integer NOT NULL,
             listed integer NOT NULL,
             entity_id integer,
             kind text,
             after_father boolean NOT NULL,
             kin boolean NOT NULL,
             plural boolean NOT NULL)
         ON COMMIT DROP
         """;

    private static readonly string Occurrences =
        """
        INSERT INTO occurrence
        SELECT w.id, w.strong_number, coalesce(w.morphology->>'nameType', ''),
               bearing.peoples, bearing.persons, pick.listed, pick.entity_id, pick.kind,
               EXISTS (SELECT 1 FROM word before
                       WHERE before.verse_id = w.verse_id AND before.position = w.position - 1
                         AND before.strong_number = @father),
               EXISTS (SELECT 1 FROM word kin
                       WHERE kin.verse_id = w.verse_id AND kin.strong_number = ANY(@kinship)),
               coalesce(w.morphology->>'number' = 'pl', FALSE)
        FROM word w
        JOIN text t ON t.id = w.text_id AND t.slug = @witness
        JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
        CROSS JOIN LATERAL (
            SELECT count(*) FILTER (WHERE b.kind = 'people') AS peoples,
                   count(*) FILTER (WHERE b.kind = 'person') AS persons
            FROM bearer b WHERE b.number = w.strong_number) bearing
        CROSS JOIN LATERAL (
            SELECT count(*) AS listed, min(b.entity_id) AS entity_id, min(b.kind) AS kind
            FROM bearer b
            WHERE b.number = w.strong_number
              AND EXISTS (
                  SELECT 1 FROM entity_verse ev
                  WHERE ev.entity_id = b.entity_id
                    AND ev.canonical_book = r.canonical_book
                    AND ev.canonical_chapter = r.canonical_chapter
                    AND ev.canonical_verse = r.canonical_verse
                    AND ev.source NOT LIKE @ours)) pick
        WHERE w.morphology->>'pos' = 'nmpr'
          AND w.strong_number IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM word_entity spoken WHERE spoken.word_id = w.id)
          AND NOT EXISTS (SELECT 1 FROM read_word answered WHERE answered.word_id = w.id)
        """;

    /// <summary>What the marking lets a listed bearer be, written once for the seed and the tally.</summary>
    private const string Allowed =
        """
        ((o.kind = 'place' AND (o.marking LIKE '%topo%' OR o.marking = 'pers'))
         OR (o.kind = 'person' AND NOT o.after_father
             AND (o.marking LIKE '%pers%' OR (o.marking = 'gens' AND o.kin AND NOT o.plural))))
        """;

    private const string Candidate =
        "o.listed = 1 AND o.peoples = 0 AND (o.kind <> 'person' OR o.persons = 1)";

    private static readonly string Seed =
        $"""
         INSERT INTO pending_annotation (word_id, entity_id, confidence, corroborated, note)
         SELECT o.word_id, o.entity_id, @confidence, FALSE,
                o.number || ', which BHSA marks ' || o.marking || ', a name of which the encyclopedia '
                || 'names only this record in this verse'
         FROM occurrence o
         WHERE {Candidate} AND {Allowed}
         """;

    private static readonly string Tally =
        $"""
         SELECT count(*),
                count(*) FILTER (WHERE {Candidate} AND {Allowed}),
                count(*) FILTER (WHERE o.peoples > 0),
                count(*) FILTER (WHERE o.peoples = 0 AND o.listed = 1 AND o.kind = 'person' AND o.persons > 1),
                count(*) FILTER (WHERE o.peoples = 0 AND o.listed > 1),
                count(*) FILTER (WHERE o.peoples = 0 AND o.listed = 0),
                count(*) FILTER (WHERE {Candidate} AND NOT {Allowed})
         FROM occurrence o
         """;

    public async Task<ListedBearerOutcome> Load(string resources, CancellationToken cancellationToken = default)
    {
        if (await db.WordEntities.AnyAsync(a => a.Source == Source, cancellationToken))
        {
            logger.LogInformation("The Hebrew names a verse list leaves one bearer for are already named; nothing to do");
            return new ListedBearerOutcome(true, false, 0, 0, 0, 0, 0, 0, 0, 0, [], TimeSpan.Zero);
        }

        var directory = Where(resources);
        if (!Directory.Exists(directory))
        {
            logger.LogInformation(
                "No model readings at {Directory}, so which Hebrew names a reading already answered is " +
                "unknown and no name was taken from a verse list alone. Point \"{Key}\" at the run folders " +
                "to let this pass run", directory, SenseReadingFiles.ConfigurationKey);
            return new ListedBearerOutcome(false, true, 0, 0, 0, 0, 0, 0, 0, 0, [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var placed = await SenseReadingFiles.Place(connection, directory, cancellationToken);
        var answered = placed.Readings.Select(r => r.WordId)
            .Concat(placed.Contradicted)
            .Concat(placed.Refused.Select(r => r.WordId))
            .Distinct()
            .ToArray();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Workspace, cancellationToken);
        await Annotating.Run(connection, transaction,
            "INSERT INTO read_word (word_id) SELECT unnest(@words)", cancellationToken, ("words", answered));
        await Annotating.Run(connection, transaction, Occurrences, cancellationToken,
            ("witness", EntityCandidates.Witness), ("ours", Ours), ("father", FatherOf),
            ("kinship", Kinship));
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

        var outcome = new ListedBearerOutcome(
            false, false, tally[0], tally[1], tally[2], tally[3], tally[4], tally[5], tally[6],
            byText.Sum(t => t.Words), byText, started.Elapsed);
        logger.LogInformation("Named the Hebrew names a verse list leaves one bearer for: {Outcome}", outcome);
        return outcome;
    }

    private string Where(string resources)
    {
        var configured = configuration[SenseReadingFiles.ConfigurationKey];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(resources, SenseReadingFiles.DefaultFolder)
            : configured;
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
