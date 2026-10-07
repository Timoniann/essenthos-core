using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Tribes">
/// Greek names standing after the word for a tribe, or that the verse goes on to call a tribe, that
/// one ancestor bears.
/// </param>
/// <param name="Realms">Greek names standing after a land, a border, a city or a king that one people bears.</param>
/// <param name="Unheld">Names in either construct that no ancestor or people bears, which this pass cannot answer.</param>
/// <param name="Written">Annotations written under the two sources, the carried ones included.</param>
internal sealed record GreekTribeNameOutcome(
    bool AlreadyLoaded,
    int Tribes,
    int Realms,
    int Unheld,
    int Written,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the Greek names standing after a tribe or a land are already named"
            : $"{Tribes} Greek names standing after the word for a tribe are the ancestor the tribe is named " +
              $"after and {Realms} standing after a land, a border, a city or a king are the people, in " +
              $"{Elapsed}; {Unheld} name nobody the encyclopedia holds. {Written} words written in all. " +
              "Per text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// <em>The tribe of Judah</em> and <em>the land of Naphtali</em>, read in the Greek.
///
/// <para>
/// The Greek resolves a name by its Strong number, and the number of a tribe's name is its
/// ancestor's and every namesake's: Μανασσῆ is borne only by the king in the encyclopedia, so the
/// tribe of Revelation 7:6 was King Manasseh; Ἰούδα is a man of Luke's genealogy and the land, so
/// the Lion of the tribe of Judah was a place; Νεφθαλείμ is borne by nobody, so Matthew's land of
/// Naphtali named nobody. The phrase says what the number cannot.
/// </para>
///
/// <para>
/// <strong>After φυλή the name is the ancestor.</strong> The project owner read Revelation 7 so on
/// 2026-10-04: the phrase already says <em>tribe</em>, so the name in it is the man the tribe is
/// named after — Judah, Manasseh son of Joseph, Jacob in <em>the tribes of the sons of Israel</em>.
/// An article or <em>sons of</em> may stand between. The ancestor is the one record that a people
/// bearing the name descends from, reached from the Greek through the Hebrew name Strong derives
/// it from; Joseph, whose tribe the encyclopedia holds no people for, is left to the rulings.
/// </para>
///
/// <para>
/// <strong>A name the verse goes on to call a tribe is the ancestor too</strong>: <em>ἐξ Ἰούδα
/// ἀνατέταλκεν ὁ κύριος ἡμῶν, εἰς ἣν φυλήν</em> — out of Judah, of which tribe — in Hebrews 7:14,
/// where the relative and φυλή follow the name with no other name between.
/// </para>
///
/// <para>
/// <strong>After a land, a border, a city or a king the name is the people</strong>, on the owner's
/// ruling of 2026-09-28 for the Hebrew, which this reads in the Greek: <em>γῆ Νεφθαλείμ</em> is the
/// land the Naphtalites hold. A name joined to one of these by <em>καί</em> is in the same phrase —
/// <em>ἐν ὁρίοις Ζαβουλὼν καὶ Νεφθαλείμ</em>.
/// </para>
///
/// <para>
/// Both are written whatever already names the word, and settle by standing like everything else:
/// a rule about the phrase outranks a resolution by number or by the form of the word, and a reading
/// of the verse or a person's ruling outranks it. The resolution itself no longer names these words
/// (<see cref="EntityAnnotationLoader"/>). Idempotent on each of its sources.
/// </para>
/// </summary>
internal sealed class GreekTribeNameLoader(AppDbContext db, ILogger<GreekTribeNameLoader> logger)
{
    public const string TribeSource =
        "Essenthos, on the project owner's reading of 2026-10-04 that a name standing after the Greek " +
        "word for a tribe is the ancestor the tribe is named after";

    public const string AntecedentSource =
        "Essenthos, on the project owner's reading of 2026-10-04 that a name standing after the Greek " +
        "word for a tribe is the ancestor the tribe is named after, read where the verse goes on to call " +
        "the name a tribe";

    public const string RealmSource =
        "Essenthos, on the project owner's ruling of 2026-09-28 that a people's name standing after a " +
        "king, a land, a city, a border and the like names the people, read in the Greek";

    /// <summary>φυλή.</summary>
    internal const string Tribe = "G5443";

    /// <summary>A king, a kingdom, a land, a country, a city, a border, a field, a mountain.</summary>
    internal static readonly string[] Realm = ["G935", "G932", "G1093", "G5561", "G4172", "G3725", "G68", "G3735"];

    /// <summary>The relative pronoun, which with φυλή after it calls the name before it a tribe.</summary>
    private const string Relative = "G3739";

    /// <summary>The article and <em>sons</em>, which may stand between the governing word and the name.</summary>
    internal static readonly string[] Between = ["G3588", "G5207"];

    private const double ByThePhrase = 0.9;

    private static string Literal(IEnumerable<string> numbers) =>
        "ARRAY[" + string.Join(", ", numbers.Select(number => $"'{number}'")) + "]";

    /// <summary>
    /// Whether the word at <paramref name="verse"/> and <paramref name="position"/> stands after one
    /// of <paramref name="governing"/>, with nothing but <see cref="Between"/> words between them.
    /// </summary>
    private static string After(string verse, string position, IEnumerable<string> governing) =>
        $"""
         EXISTS (SELECT 1 FROM word head
                 WHERE head.verse_id = {verse}
                   AND head.position BETWEEN {position} - 3 AND {position} - 1
                   AND head.strong_number = ANY({Literal(governing)})
                   AND NOT EXISTS (SELECT 1 FROM word inside
                                   WHERE inside.verse_id = {verse}
                                     AND inside.position > head.position AND inside.position < {position}
                                     AND NOT coalesce(inside.strong_number, '') = ANY({Literal(Between)})))
         """;

    /// <summary>
    /// Whether a word is in the construct: after one of <paramref name="governing"/>, or joined by
    /// <em>καί</em> to a word that is.
    /// </summary>
    private static string In(string word, IEnumerable<string> governing) =>
        $"""
         ({After($"{word}.verse_id", $"{word}.position", governing)}
          OR (EXISTS (SELECT 1 FROM word conjunction
                      WHERE conjunction.verse_id = {word}.verse_id AND conjunction.position = {word}.position - 1
                        AND conjunction.strong_number = 'G2532')
              AND {After($"{word}.verse_id", $"{word}.position - 2", governing)}))
         """;

    /// <summary>The Hebrew name Strong derives a Greek name from, directly or through the Greek name it shares a source with.</summary>
    private static string Hebrew(string number) =>
        $"""
         coalesce(
             (SELECT (regexp_match(origin.derivation, '^of Hebrew origin \((H[0-9]+)'))[1]
              FROM strong_entry origin WHERE origin.strong_number = {number}),
             (SELECT (regexp_match(origin.derivation, '^of Hebrew origin \((H[0-9]+)'))[1]
              FROM strong_entry shared
              JOIN strong_entry origin
                   ON origin.strong_number = (regexp_match(shared.derivation, '^from the same as (G[0-9]+)'))[1]
              WHERE shared.strong_number = {number}))
         """;

    /// <summary>The one ancestor a people bearing the Hebrew name descends from and who bears it himself.</summary>
    internal static string Ancestor(string hebrew) =>
        $"""
         (SELECT CASE WHEN count(DISTINCT a.id) = 1 THEN min(a.id) END
          FROM entity p
          JOIN entity a ON a.id = p.origin_entity_id AND a.kind = 'person'
          WHERE p.kind = 'people'
            AND EXISTS (SELECT 1 FROM entity_name pn WHERE pn.entity_id = p.id AND pn.hebrew_strong_number = {hebrew})
            AND EXISTS (SELECT 1 FROM entity_name an WHERE an.entity_id = a.id AND an.hebrew_strong_number = {hebrew}))
         """;

    /// <summary>The one people bearing the Hebrew name.</summary>
    private static string People(string hebrew) =>
        $"""
         (SELECT CASE WHEN count(DISTINCT p.id) = 1 THEN min(p.id) END
          FROM entity p
          WHERE p.kind = 'people'
            AND EXISTS (SELECT 1 FROM entity_name pn
                        WHERE pn.entity_id = p.id AND pn.hebrew_strong_number = {hebrew}
                          AND coalesce(pn.kind, '') NOT IN ('title', 'description')))
         """;

    /// <summary>Whether a word of a Greek witness stands after the word for a tribe.</summary>
    internal static string AfterATribe(string word) => In(word, [Tribe]);

    /// <summary>
    /// Whether the verse goes on to call a word a tribe: a relative pronoun with φυλή after it
    /// follows the word, and no other name stands between.
    /// </summary>
    internal static string CalledATribe(string word) =>
        $"""
         EXISTS (SELECT 1 FROM word relative
                 JOIN word tribe ON tribe.verse_id = relative.verse_id AND tribe.position = relative.position + 1
                      AND tribe.strong_number = '{Tribe}'
                 WHERE relative.verse_id = {word}.verse_id AND relative.position > {word}.position
                   AND relative.strong_number = '{Relative}'
                   AND NOT EXISTS (SELECT 1 FROM word other
                                   JOIN strong_entry lexicon ON lexicon.strong_number = other.strong_number
                                   WHERE other.verse_id = {word}.verse_id
                                     AND other.position > {word}.position AND other.position < relative.position
                                     AND {EntityAnnotationLoader.GreekName}))
         """;

    /// <summary>Whether a word of a Greek witness stands after a land, a border, a city or a king.</summary>
    internal static string AfterARealm(string word) => In(word, Realm);

    /// <summary>
    /// The record a Greek name in either construct is, as SQL over a word row: the ancestor after a
    /// tribe, the people after a realm, and null where the encyclopedia holds no single one. Which
    /// is also what the resolution by number is asked before it names such a word.
    /// </summary>
    internal static string Answer(string word) =>
        $"""
         CASE WHEN {AfterATribe(word)} OR {CalledATribe(word)} THEN {Ancestor(Hebrew($"{word}.strong_number"))}
              WHEN {AfterARealm(word)} THEN {People(Hebrew($"{word}.strong_number"))} END
         """;

    private static readonly string Occurrences =
        $"""
         CREATE TEMP TABLE occurrence (
             word_id bigint PRIMARY KEY,
             number text NOT NULL,
             construct text NOT NULL,
             entity_id integer)
         ON COMMIT DROP;
         INSERT INTO occurrence (word_id, number, construct, entity_id)
         WITH named AS MATERIALIZED (
             SELECT w.id, w.strong_number, w.verse_id, w.position
             FROM word w
             JOIN text t ON t.id = w.text_id AND t.slug = ANY(@witnesses)
             JOIN strong_entry lexicon ON lexicon.strong_number = w.strong_number
             WHERE {EntityAnnotationLoader.GreekName}
         ),
         placed AS MATERIALIZED (
             SELECT n.id, n.strong_number,
                    CASE WHEN {AfterATribe("n")} THEN 'tribe'
                         WHEN {CalledATribe("n")} THEN 'antecedent'
                         WHEN {AfterARealm("n")} THEN 'realm' END AS construct
             FROM named n
         )
         SELECT candidate.id, candidate.strong_number, candidate.construct,
                CASE WHEN candidate.construct = 'realm' THEN {People(Hebrew("candidate.strong_number"))}
                     ELSE {Ancestor(Hebrew("candidate.strong_number"))} END
         FROM placed candidate
         WHERE candidate.construct IS NOT NULL
         """;

    private const string Seed =
        """
        INSERT INTO pending_annotation (word_id, entity_id, confidence, corroborated, note)
        SELECT o.word_id, o.entity_id, @confidence, FALSE,
               o.number || CASE o.construct
                   WHEN 'tribe' THEN ', standing after the word for a tribe, so the ancestor the tribe is named after'
                   WHEN 'antecedent' THEN ', which the verse goes on to call a tribe, so the ancestor the tribe is named after'
                   ELSE ', standing after a land, a border, a city or a king, so the people of that name' END
        FROM occurrence o
        WHERE o.entity_id IS NOT NULL AND o.construct = @construct
        """;

    private const string Tally =
        """
        SELECT count(*) FILTER (WHERE construct <> 'realm' AND entity_id IS NOT NULL),
               count(*) FILTER (WHERE construct = 'realm' AND entity_id IS NOT NULL),
               count(*) FILTER (WHERE entity_id IS NULL)
        FROM occurrence
        """;

    public async Task<GreekTribeNameOutcome> Load(CancellationToken cancellationToken = default)
    {
        var sources = new[] { ("tribe", TribeSource), ("antecedent", AntecedentSource), ("realm", RealmSource) };
        var written = await db.WordEntities
            .Where(a => a.Source == TribeSource || a.Source == AntecedentSource || a.Source == RealmSource)
            .Select(a => a.Source).Distinct().ToListAsync(cancellationToken);
        if (sources.All(construct => written.Contains(construct.Item2)))
        {
            logger.LogInformation("The Greek names standing after a tribe or a land are already named; nothing to do");
            return new GreekTribeNameOutcome(true, 0, 0, 0, 0, [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Occurrences, cancellationToken,
            ("witnesses", FixedTitles.GreekWitnesses.ToArray()));
        var (tribes, realms, unheld) = await Counted(connection, transaction, cancellationToken);
        var answered = await Answered(connection, transaction, cancellationToken);
        var pending = sources.Where(construct => answered.Contains(construct.Item1) && !written.Contains(construct.Item2))
            .ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("The Greek names standing after a tribe or a land are already named; nothing to do");
            return new GreekTribeNameOutcome(true, 0, 0, 0, 0, [], TimeSpan.Zero);
        }

        var method = EnumSpelling.Of(LinkMethod.RuleBased);
        foreach (var (construct, source) in pending)
        {

            await Annotating.Run(connection, transaction, "DROP TABLE IF EXISTS pending_annotation", cancellationToken);
            await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
            await Annotating.Run(connection, transaction, Seed, cancellationToken,
                ("confidence", ByThePhrase), ("construct", construct));
            await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);
            await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
                ("method", method), ("source", source));
            await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
                ("method", method), ("source", source));
        }

        var byText = (await Annotating.ByText(connection, transaction, TribeSource, cancellationToken))
            .Concat(await Annotating.ByText(connection, transaction, AntecedentSource, cancellationToken))
            .Concat(await Annotating.ByText(connection, transaction, RealmSource, cancellationToken))
            .GroupBy(t => t.Text)
            .Select(g => (g.Key, g.Sum(t => t.Words)))
            .OrderByDescending(t => t.Item2)
            .ToList();
        await transaction.CommitAsync(cancellationToken);

        var outcome = new GreekTribeNameOutcome(
            false, tribes, realms, unheld, byText.Sum(t => t.Item2), byText, started.Elapsed);
        logger.LogInformation("Named the Greek tribes and lands: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>The constructs some occurrence of which the encyclopedia answers.</summary>
    private static async Task<HashSet<string>> Answered(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT DISTINCT construct FROM occurrence WHERE entity_id IS NOT NULL",
            connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        var answered = new HashSet<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            answered.Add(reader.GetString(0));
        }

        return answered;
    }

    private static async Task<(int Tribes, int Realms, int Unheld)> Counted(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            Tally, connection, (NpgsqlTransaction)transaction.GetDbTransaction());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ((int)reader.GetInt64(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2));
    }
}
