using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using Essenthos.Core.Configuration;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Occurrences">Hebrew names of a people standing in the construct after a realm's word.</param>
/// <param name="Settled">Of those, the ones exactly one people bears the name of and nothing stronger had named.</param>
/// <param name="Unheld">Nameless words whose name no people record bears, which this pass cannot answer.</param>
/// <param name="Contested">Nameless words whose name several peoples bear, which it does not choose between.</param>
/// <param name="Spoken">Words something that does not give way had already named, which it does not contest.</param>
/// <param name="Replaced">
/// Annotations it took the place of: the ancestor read off a Hebrew word by a model, the words that
/// reading was carried to, and the ancestor on words only the name consensus had named.
/// </param>
/// <param name="Declined">Translation words the links reached that already name somebody else, left as they are.</param>
/// <param name="Written">Annotations written under this pass's source, the carried ones included.</param>
/// <param name="UnheldNames">The names behind <paramref name="Unheld"/>, with how often each stands there.</param>
internal sealed record RealmNameOutcome(
    bool AlreadyLoaded,
    int Occurrences,
    int Settled,
    int Unheld,
    int Contested,
    int Spoken,
    int Replaced,
    int Declined,
    int Written,
    IReadOnlyList<(string Text, int Words)> ByText,
    IReadOnlyList<(string Number, string Lemma, int Words)> UnheldNames,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the peoples named after a king, a land or a city are already named"
            : $"{Settled} of {Occurrences} Hebrew names standing after a king, a land, a city or a border " +
              $"are the people of that name, in {Elapsed}; {Unheld} name a people the encyclopedia does not " +
              $"hold ({string.Join(", ", UnheldNames.Select(n => $"{n.Lemma} {n.Number} {n.Words}"))}), " +
              $"{Contested} a name several peoples bear, and {Spoken} were already named. {Replaced} weaker " +
              $"annotations were replaced and listed for review; {Declined} translation words already naming " +
              $"somebody else were left. {Written} words written in all. Per text: " +
              string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// <em>The king of Israel</em>, <em>the land of Judah</em>, <em>the border of Benjamin</em>: the
/// name after a realm's word is the people.
///
/// <para>
/// A tribe's name is its ancestor's, and the owner's ruling of 2026-09-16 gives the ancestor every
/// such name the sentence does not settle (<see cref="EponymNameLoader"/>). A name standing after a
/// king, a land or a city was left out of that ruling, because Judah the man had no king and held no
/// land. The owner settled it on 2026-09-28: it is the people — the Israelites, the Judahites — and
/// neither the ancestor nor blank.
/// </para>
///
/// <para>
/// <strong>The construct is what settles it, and only for these nouns.</strong> Each of
/// <see cref="Governing"/> is something ruled or held — a king, a kingdom, a reign, a land, a city,
/// a border, a field, a mountain — and in every one of the 555 places a tribe's name stands after
/// one of them it is the people that is ruled or holds it. The construct nouns left out are left
/// out because the man can be meant: <em>the sons of Reuben</em> are the man's sons in Genesis 46:9
/// and the tribe's families in Numbers 26:5, <em>the daughters</em> and <em>the house of Judah</em>
/// are a family as readily as a nation, and <em>the God of Israel</em> is the God of the patriarch
/// at Genesis 33:20 before he is the God of the nation. The words for a body of the people —
/// elders, princes, assembly, camp, remnant, clans, thousands — name the people's members rather
/// than a realm; the ruling of 2026-09-16 already answers them, and a rule here would contest it.
/// The words for a tribe have their own rule in <see cref="TribeNameLoader"/>.
/// </para>
///
/// <para>
/// The name must be one BHSA marks as able to name a people (<c>gens</c>), and exactly one people
/// must bear its number. A name no people bears is counted and listed rather than given a record
/// nobody wrote; a name several bear is a choice this rule does not make.
/// </para>
///
/// <para>
/// <strong>It outranks what is weaker than the owner's word, and nothing else.</strong> A model that
/// read the verse and named the ancestor gave the answer the ruling overturns, so that reading gives
/// way, with every word it was carried to; so does the ancestor on a word only the name consensus
/// (<see cref="NameConsensusPass"/>) named. Nothing a person, the owner or a source stated gives way
/// — neither the annotation nor any claim standing on it — and a word carrying such an answer keeps
/// it alone, since a carried one would sit beside it as a second answer and which a reader is shown
/// would be decided by standing rather than by reading. Every annotation replaced is listed in
/// <see cref="ReviewFile"/>.
/// </para>
///
/// <para>
/// That makes the order of the two passes immaterial. On a corpus built from empty this runs first,
/// and the consensus writes only where nothing names a word; on one the consensus was already read
/// into, this takes back exactly the words the consensus would then have left alone.
/// </para>
///
/// <para>
/// Idempotent on its own source, and it runs after <see cref="EponymNameLoader"/>, whose realms it
/// answers.
/// </para>
/// </summary>
internal sealed class RealmNameLoader(AppDbContext db, ReviewLists lists, ILogger<RealmNameLoader> logger)
{
    public const string Source =
        "Essenthos, on the project owner's ruling of 2026-09-28 that a people's name standing after a " +
        "king, a land, a city, a border and the like names the people";

    public const string ReviewFile = "realm-name-replacements.json";

    private const string ReviewAbout =
        "Annotations replaced by the rule that a people's name standing after a king, a land, a city, a border and " +
        "the like names the people: a model's reading of the ancestor, the words that reading was carried to, and " +
        "the ancestor on words only the name consensus had named. Reference is canonical book:chapter:verse, position the word's " +
        "place in the verse from 1, replaced the record the word named before and by what, now the records it names " +
        "after the rule, or null where it names nobody.";

    /// <summary>
    /// מֶלֶךְ king, מַמְלָכָה kingdom, מַלְכוּת reign, אֶרֶץ land, אֲדָמָה ground (Ezekiel's
    /// <em>land of Israel</em>), עִיר city, גְּבוּל border, שָׂדֶה field (<em>the fields of
    /// Ephraim</em>, Obadiah 19) and הַר mountain (<em>the hill country of Ephraim</em>).
    /// </summary>
    public static readonly string[] Governing =
        ["H4428", "H4467", "H4438", "H776", "H127", "H5892", "H1366", "H7704", "H2022"];

    /// <summary>
    /// The phrase's own meaning, as sure as the construct after a word for a tribe: nothing in the
    /// data contradicts it, and it is a reading of the phrase rather than a source naming the people.
    /// </summary>
    private const double ByThePhrase = 0.9;

    /// <summary>
    /// Whether annotation <c>a</c> carries anything a person, the project owner or a source stated,
    /// on itself or in any claim standing on it.
    /// </summary>
    private const string Stated =
        """
        (a.method IN (@manual, @stated) OR a.source LIKE '%project owner%'
         OR EXISTS (SELECT 1 FROM word_entity_claim c
                    WHERE c.word_entity_id = a.id
                      AND (c.method IN (@manual, @stated) OR c.source LIKE '%project owner%')))
        """;

    /// <summary>Whether annotation <c>a</c> is the name consensus's and nothing else's.</summary>
    private const string ConsensusOnly =
        """
        (a.source = @consensus
         AND NOT EXISTS (SELECT 1 FROM word_entity_claim c
                         WHERE c.word_entity_id = a.id AND c.source <> @consensus))
        """;

    /// <summary>
    /// Whether annotation <c>a</c> on a Hebrew word gives way to the people: the ancestor the people
    /// is named after, read by a model or found by the consensus alone.
    /// </summary>
    private const string GivesWay =
        $"""
         (NOT {Stated} AND a.entity_id = named.ancestor_id AND (a.method = @reading OR {ConsensusOnly}))
         """;

    private const string Occurrences =
        $"""
         CREATE TEMP TABLE occurrence (
             word_id bigint PRIMARY KEY,
             number text NOT NULL,
             lemma text,
             peoples integer NOT NULL,
             entity_id integer,
             spoken boolean NOT NULL)
         ON COMMIT DROP;
         INSERT INTO occurrence (word_id, number, lemma, peoples, entity_id, spoken)
         SELECT w.id, w.strong_number, w.lemma, named.peoples, named.entity_id,
                EXISTS (SELECT 1 FROM word_entity a
                        WHERE a.word_id = w.id
                          AND (named.peoples <> 1 OR a.entity_id = named.entity_id OR NOT {GivesWay}))
         FROM word w
         JOIN text t ON t.id = w.text_id AND t.slug = @witness
         JOIN word realm ON realm.verse_id = w.verse_id AND realm.position = w.position - 1
              AND realm.strong_number = ANY(@governing)
              AND realm.morphology->>'state' = 'c'
         CROSS JOIN LATERAL (
             SELECT count(DISTINCT e.id) AS peoples, min(e.id) AS entity_id,
                    min(e.origin_entity_id) AS ancestor_id
             FROM entity_name n
             JOIN entity e ON e.id = n.entity_id AND e.kind = 'people'
             WHERE n.hebrew_strong_number = w.strong_number
               AND coalesce(n.kind, '') NOT IN ('title', 'description')) named
         WHERE w.strong_number IS NOT NULL
           AND w.morphology->>'pos' = 'nmpr'
           AND 'gens' = ANY(string_to_array(w.morphology->>'nameType', ','))
         """;

    /// <summary>
    /// What gives way on the Hebrew words this pass answers, and every word a model's reading of the
    /// ancestor was carried to from them, recorded and then taken back. A carried row's note names
    /// the word it was carried from, in whatever case the text's slug was written when it was.
    /// </summary>
    private const string YieldTheHebrew =
        $"""
         CREATE TEMP TABLE replaced (
             word_entity_id bigint PRIMARY KEY,
             word_id bigint NOT NULL,
             entity_id integer NOT NULL,
             source text NOT NULL)
         ON COMMIT DROP;
         INSERT INTO replaced (word_entity_id, word_id, entity_id, source)
         SELECT a.id, a.word_id, a.entity_id, a.source
         FROM occurrence o
         JOIN word_entity a ON a.word_id = o.word_id
         WHERE o.peoples = 1 AND NOT o.spoken;
         INSERT INTO replaced (word_entity_id, word_id, entity_id, source)
         SELECT a.id, a.word_id, a.entity_id, a.source
         FROM replaced r
         JOIN word_entity a ON a.entity_id = r.entity_id AND a.source = r.source AND a.word_id <> r.word_id
              AND substring(a.note FROM '^through [^ ]+ word ([0-9]+),')::bigint = r.word_id
         WHERE r.source <> @consensus AND NOT {Stated}
         ON CONFLICT DO NOTHING;
         DELETE FROM word_entity a USING replaced r WHERE a.id = r.word_entity_id;
         """;

    private const string Seed =
        """
        INSERT INTO pending_annotation (word_id, entity_id, confidence, corroborated, note)
        SELECT o.word_id, o.entity_id, @confidence, FALSE,
               o.number || ', standing after a king, a land, a city or a border, so the people of that name'
        FROM occurrence o
        WHERE o.peoples = 1 AND NOT o.spoken
        """;

    /// <summary>
    /// The carried rows that would sit beside another entity's annotation that does not give way. On a
    /// translation word only the consensus's ancestor does: where it found somebody else — Josiah in
    /// the Chinese of Jeremiah 46:2, the Jezreelite in the Ukrainian of 2 Kings 9:21 — the link
    /// reached the wrong word, and the consensus is right.
    /// </summary>
    private const string Decline =
        $"""
         DELETE FROM pending_annotation p
         WHERE p.through IS NOT NULL
           AND EXISTS (SELECT 1 FROM word_entity a
                       WHERE a.word_id = p.word_id AND a.entity_id <> p.entity_id
                         AND ({Stated} OR NOT {ConsensusOnly}
                              OR a.entity_id IS DISTINCT FROM
                                 (SELECT people.origin_entity_id FROM entity people WHERE people.id = p.entity_id)))
         """;

    /// <summary>On the carried words left, the consensus's annotations of the ancestor, which give way.</summary>
    private const string YieldTheTranslations =
        """
        INSERT INTO replaced (word_entity_id, word_id, entity_id, source)
        SELECT a.id, a.word_id, a.entity_id, a.source
        FROM pending_annotation p
        JOIN word_entity a ON a.word_id = p.word_id AND a.entity_id <> p.entity_id
        WHERE p.through IS NOT NULL
        ON CONFLICT DO NOTHING;
        DELETE FROM word_entity a USING replaced r WHERE a.id = r.word_entity_id;
        """;

    /// <summary>Every annotation replaced, addressed canonically, and what the word names now.</summary>
    private const string Listed =
        """
        SELECT t.slug, r.canonical_book, r.canonical_chapter, r.canonical_verse, w.position, w.text,
               was.slug, x.source, now.slug
        FROM replaced x
        JOIN word w ON w.id = x.word_id
        JOIN text t ON t.id = w.text_id
        JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
        JOIN entity was ON was.id = x.entity_id
        LEFT JOIN LATERAL (
            SELECT string_agg(DISTINCT e.slug, ', ') AS slug
            FROM word_entity a JOIN entity e ON e.id = a.entity_id
            WHERE a.word_id = x.word_id) now ON TRUE
        ORDER BY t.slug, r.canonical_book, r.canonical_chapter, r.canonical_verse, w.position
        """;

    private const string Tally =
        """
        SELECT count(*), count(*) FILTER (WHERE peoples = 1 AND NOT spoken),
               count(*) FILTER (WHERE peoples = 0 AND NOT spoken),
               count(*) FILTER (WHERE peoples > 1 AND NOT spoken), count(*) FILTER (WHERE spoken)
        FROM occurrence
        """;

    private const string Unheld =
        """
        SELECT number, coalesce(min(lemma), ''), count(*)
        FROM occurrence
        WHERE peoples = 0 AND NOT spoken
        GROUP BY number
        ORDER BY 3 DESC, 1
        """;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <param name="resources">The corpus sources, under which the review lists are kept.</param>
    public async Task<RealmNameOutcome> Load(string resources, CancellationToken cancellationToken = default)
    {
        if (await db.WordEntities.AnyAsync(a => a.Source == Source, cancellationToken))
        {
            logger.LogInformation(
                "The peoples named after a king, a land or a city are already named; nothing to do");
            return new RealmNameOutcome(true, 0, 0, 0, 0, 0, 0, 0, 0, [], [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        (string Name, object? Value)[] weighing =
        [
            ("manual", EnumSpelling.Of(LinkMethod.Manual)),
            ("stated", EnumSpelling.Of(LinkMethod.StatedBySource)),
            ("reading", EnumSpelling.Of(LinkMethod.ModelReading)),
            ("consensus", NameConsensusPass.Source),
        ];

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Occurrences, cancellationToken,
            [("witness", EntityCandidates.Witness), ("governing", Governing), .. weighing]);
        var (occurrences, settled, unheld, contested, spoken) =
            await Counted(connection, transaction, cancellationToken);
        var unheldNames = await Unnamed(connection, transaction, cancellationToken);
        await Annotating.Run(connection, transaction, YieldTheHebrew, cancellationToken, weighing);

        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
        await Annotating.Run(connection, transaction, Seed, cancellationToken, ("confidence", ByThePhrase));
        await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);
        var declined = await Affected(connection, transaction, Decline, cancellationToken, weighing);
        await Annotating.Run(connection, transaction, YieldTheTranslations, cancellationToken);

        var method = EnumSpelling.Of(LinkMethod.RuleBased);
        await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
            ("method", method), ("source", Source));
        await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
            ("method", method), ("source", Source));

        var byText = await Annotating.ByText(connection, transaction, Source, cancellationToken);
        var replaced = await Replacements(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Review(resources, replaced);

        var outcome = new RealmNameOutcome(
            false, occurrences, settled, unheld, contested, spoken, replaced.Count, declined,
            byText.Sum(t => t.Words), byText, unheldNames, started.Elapsed);
        logger.LogInformation("Named the peoples a realm is named after: {Outcome}", outcome);
        return outcome;
    }

    private sealed record Replacement(
        string Text, string Reference, int Position, string Word, string Replaced, string By, string? Now);

    private sealed record ReviewList(string About, DateTimeOffset Written, IReadOnlyList<Replacement> Words);

    /// <summary>
    /// The replaced annotations, beside the other review lists — or, when this load ran against a
    /// database the owner's console does not read, in a folder of that database's own.
    /// </summary>
    private void Review(string resources, IReadOnlyList<Replacement> replaced)
    {
        var database = db.Database.GetDbConnection().Database;
        var path = lists.For(resources, ReviewFile, database);
        if (!lists.IsOwners(database))
        {
            logger.LogWarning(
                "This load ran against {Database}, not the database the owner's console reads, so its " +
                "{Replaced} replaced annotations are listed in {Path}", database, replaced.Count, path);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path, JsonSerializer.Serialize(new ReviewList(ReviewAbout, DateTimeOffset.UtcNow, replaced), Json) + "\n");
    }

    private static async Task<IReadOnlyList<Replacement>> Replacements(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            Listed, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.CommandTimeout = Annotating.Patient;

        var replaced = new List<Replacement>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            replaced.Add(new Replacement(
                reader.GetString(0), $"{reader.GetInt32(1)}:{reader.GetInt32(2)}:{reader.GetInt32(3)}",
                reader.GetInt32(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return replaced;
    }

    private static async Task<int> Affected(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(
            sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.CommandTimeout = Annotating.Patient;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(int Occurrences, int Settled, int Unheld, int Contested, int Spoken)> Counted(
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
            (int)reader.GetInt64(3), (int)reader.GetInt64(4));
    }

    private static async Task<IReadOnlyList<(string Number, string Lemma, int Words)>> Unnamed(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            Unheld, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.CommandTimeout = Annotating.Patient;

        var names = new List<(string, string, int)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add((reader.GetString(0), reader.GetString(1), (int)reader.GetInt64(2)));
        }

        return names;
    }
}
