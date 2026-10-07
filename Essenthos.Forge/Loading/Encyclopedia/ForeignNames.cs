using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Utils;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Withdrawn">Carried annotations taken back because their word is another record's name.</param>
/// <param name="ByText">How many of those each text held.</param>
/// <param name="Examples">A few of them in words, for the log: the text, the word, who it was given and who it is.</param>
internal sealed record ForeignNameOutcome(
    int Withdrawn,
    IReadOnlyList<(string Text, int Words)> ByText,
    IReadOnlyList<string> Examples,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        Withdrawn == 0
            ? "no person's name stands carried on a word that is another record's name"
            : $"{Withdrawn} names carried onto a word that is another record's name taken back, in {Elapsed}. " +
              "Per text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}")) +
              ". For example: " + string.Join("; ", Examples);
}

/// <summary>
/// A person's name carried along a link onto a word that is somebody else's name.
///
/// <para>
/// An aligner that pairs the wrong two names of a verse makes every name carried along the pair
/// wrong with it. Ohienko's Romans 16:20 reads <em>сатану</em> at its sixth word and
/// <em>Христа</em> at its fourteenth, the aligner put <em>Христа</em> against <em>Σατανᾶν</em>, and
/// the lexicon's Satan arrived on Christ — where, carried by a strong number, it outranked the
/// Jesus the title carried there. Tyndale's and Douay's <em>Ananias</em> of Acts 5:3 were given
/// Satan the same way. The link is a guess about which words correspond; the word's own spelling is
/// not a guess.
/// </para>
///
/// <para>
/// <strong>What makes a word another's name is the text itself.</strong> The test asks five
/// things, and refuses only where all five hold:
/// </para>
/// <list type="number">
/// <item>The seed word is the person's own name — its number is one the person's names bear, or it
/// is spelled like one of them. A reading that names a man on <em>the king</em> or on
/// <em>the LORD</em> carries a description, not a name, and a description may be rendered by any
/// word.</item>
/// <item>The word it lands on is written as a name: with a capital where the sentence does not
/// open. A word written in lower case, or in a script that has no capitals, is never taken for
/// another's name here, because nothing on the page says it is one.</item>
/// <item>The word is no spelling of the person: not alike any name or name form the encyclopedia
/// holds for them, nor the seed word itself, nor a part of one of their names — <em>Moab</em> in
/// <em>Pahath-moab</em> is half the man's name as well as the land's.</item>
/// <item>The text writes the word, wherever else it stands, as the name of another record the verse
/// names, in any text: words of the same spelling in the same text name that record at least
/// <see cref="Least"/> times and at least <see cref="Outnumbering"/> times as often as they name this
/// person or anyone who shares a name, a number or a title with them. Namesakes are therefore never
/// each other's foreigners — <em>Захарии</em> is thirty men's — and neither are a title and its
/// bearer.</item>
/// <item>The text does not write the word for this person in <see cref="Elsewhere"/> verses or more
/// where the other record goes unnamed. A translation that spells two men alike keeps its spelling: Almeida writes Jehoiachin
/// <em>Joaquim</em> as it writes his father, in verses that name the son alone, and so the son's
/// <em>Joaquim</em> of 2 Chronicles 36:8 stays his though the father is named three words before it.
/// What a link misplaces, it misplaces in the same company each time: every <em>João</em> given
/// James stands in a verse that names John.</item>
/// </list>
///
/// <para>
/// So it costs nothing where it cannot see: a language whose forms are not held, a script with no
/// capitals, a name written once. Measured on the corpus of 2026-10-06 it takes back some seven
/// hundred carried names in the cased texts, and the examples are the class: <em>João</em> given
/// James, <em>Jeroboam</em> given Nebat in <em>Jeroboam son of Nebat</em>, <em>Joshua</em> given
/// Nun, <em>Христа</em> given Satan.
/// </para>
///
/// <para>
/// A row a person or a source settled is left alone: it carries no confidence, and a spelling does
/// not overturn a ruling. Nor is a row taken back where something besides the links names the word
/// so — an answer read of the word itself stands on its own.
/// </para>
///
/// <para>
/// The same test is asked of every row <see cref="Annotating.Carry"/> reaches, so no pass writes
/// such a name; this takes back what was written before the record whose name the word is had
/// reached it, which in the load is most of them, because the titles are written after the names.
/// It runs after the last pass that carries, at the end of <see cref="AnnotationCarrier"/>, and as
/// its own verb, and finds nothing the second time.
/// </para>
/// </summary>
internal sealed class ForeignNames(AppDbContext db, ILogger<ForeignNames> logger)
{
    /// <summary>How many times the text must write the spelling as the other record's name.</summary>
    public const int Least = 3;

    /// <summary>How many times more often than as this person's, or a namesake's.</summary>
    public const int Outnumbering = 3;

    /// <summary>
    /// In how many verses that do not name the other record the text must write the word for this
    /// person before the word is taken for a second spelling of them rather than a misplaced one.
    /// One is not enough: a link that misplaces a name once misplaces it in a verse or two more.
    /// </summary>
    public const int Elsewhere = 2;

    /// <summary>
    /// How alike a word must be to one of the person's spellings to be taken for it: the same line
    /// <see cref="Annotating"/> draws between two inflections of one name.
    /// </summary>
    private const string Spelled = "0.3";

    /// <summary>A spelling folded as the corpus folds a word's searchable form, with everything but letters and digits taken out.</summary>
    private static string Folded(string source) =>
        $"regexp_replace({DiacriticFolding.Expression(source)}, '[^[:alnum:]]', '', 'g')";

    /// <summary>
    /// What the test reads of the encyclopedia, gathered once per transaction rather than once per
    /// word: every spelling it holds for a record in any language, folded; the Strong numbers of each
    /// record's own names; and the keys two records may share and still be one name's — the name,
    /// the label, the number, and the title a record bears or is. Dropped and made again on each call,
    /// because a pass may add records between two carries of one transaction.
    /// </summary>
    public static readonly string Prepare =
        $"""
         DROP TABLE IF EXISTS foreign_spelling, foreign_number, foreign_key;
         CREATE TEMP TABLE foreign_number ON COMMIT DROP AS
         SELECT DISTINCT n.entity_id, btrim(number) AS number
         FROM entity_name n
         CROSS JOIN LATERAL unnest(string_to_array(concat_ws(',', n.hebrew_strong_number, n.greek_strong_number), ',')) number
         WHERE coalesce(n.kind, '') NOT IN ('title', 'description') AND btrim(number) <> '';
         CREATE INDEX ON foreign_number (entity_id, number);
         CREATE TEMP TABLE foreign_spelling ON COMMIT DROP AS
         SELECT DISTINCT known.entity_id, {Folded("known.spelling")} AS spelling
         FROM (
             SELECT e.id AS entity_id, e.name AS spelling FROM entity e
             UNION ALL SELECT e.id, regexp_replace(e.slug, '-[0-9]+$', '') FROM entity e
             UNION ALL SELECT n.entity_id, x.spelling
                       FROM entity_name n
                       CROSS JOIN LATERAL (VALUES (n.label), (n.hebrew), (n.hebrew_transliterated),
                                                  (n.greek), (n.greek_transliterated)) x(spelling)
                       WHERE coalesce(n.kind, '') NOT IN ('title', 'description')
             UNION ALL SELECT f.entity_id, f.form FROM entity_name_form f) known
         WHERE known.spelling IS NOT NULL;
         DELETE FROM foreign_spelling WHERE spelling = '';
         CREATE INDEX ON foreign_spelling (entity_id);
         CREATE TEMP TABLE foreign_key ON COMMIT DROP AS
         SELECT DISTINCT keyed.entity_id, keyed.k
         FROM (
             SELECT e.id AS entity_id, lower(e.name) AS k FROM entity e
             UNION ALL SELECT e.id, regexp_replace(e.slug, '-[0-9]+$', '') FROM entity e
             UNION ALL SELECT n.entity_id, lower(n.label) FROM entity_name n
                       WHERE coalesce(n.kind, '') NOT IN ('title', 'description')
             UNION ALL SELECT entity_id, number FROM foreign_number
             UNION ALL SELECT title_entity_id, 'title ' || title_entity_id FROM title_bearer
             UNION ALL SELECT bearer_entity_id, 'title ' || title_entity_id FROM title_bearer) keyed;
         CREATE INDEX ON foreign_key (entity_id);
         CREATE INDEX ON foreign_key (k, entity_id)
         """;

    /// <summary>
    /// Whether two records are one name's: the same record, namesakes sharing a name or a number, or
    /// a title and the record it is borne by.
    /// </summary>
    private static string Kin(string one, string other) =>
        $"""
         ({one} = {other}
          OR EXISTS (SELECT 1 FROM foreign_key mine
                     JOIN foreign_key theirs ON theirs.k = mine.k AND theirs.entity_id = {other}
                     WHERE mine.entity_id = {one}))
         """;

    /// <summary>
    /// The rows of <paramref name="rows"/> the test refuses, as the common table
    /// <c>foreign_refused (word_id, entity_id, through)</c> and the tables it is built from, to be
    /// spliced into a <c>WITH</c> list. The rows need the reached word's id, spelling, text and
    /// searchable form, whether it opens a sentence, the record, the confidence and the seed word's
    /// id as <c>through</c>. Reads the tables <see cref="Prepare"/> makes.
    ///
    /// <para>
    /// Set-wise rather than row by row, because the same spelling is asked about thousands of
    /// times — every <em>Израиля</em> the Hebrew's Jacob reaches — and what the text writes it for is
    /// the same answer each time.
    /// </para>
    ///
    /// <para>
    /// The text's own record of a spelling is counted by name rather than by record: what the text
    /// writes <em>João</em> for is the Baptist, the apostle and two more men called John, and it is
    /// all of them together that outnumber the James a link put there.
    /// </para>
    /// </summary>
    public static string Refusals(string rows) =>
        $"""
         foreign_suspect AS MATERIALIZED (
             SELECT DISTINCT r.word_id, r.entity_id, r.through, r.text_id, r.normalised_text,
                    seed_word.verse_id AS seed_verse
             FROM {rows} r
             JOIN entity named ON named.id = r.entity_id AND named.kind = 'person'
             JOIN word seed_word ON seed_word.id = r.through
             WHERE NOT r.opens AND r.spelling ~ '^[[:upper:]]' AND r.confidence IS NOT NULL
               AND r.normalised_text IS NOT NULL
               AND (EXISTS (SELECT 1 FROM foreign_number numbered
                            WHERE numbered.entity_id = named.id AND numbered.number = seed_word.strong_number)
                    OR EXISTS (SELECT 1 FROM foreign_spelling known
                               WHERE known.entity_id = named.id
                                 AND similarity({Folded("seed_word.text")}, known.spelling) >= {Spelled}))
               AND similarity({Folded("r.spelling")}, {Folded("seed_word.text")}) < {Spelled}
               AND NOT EXISTS (SELECT 1 FROM foreign_spelling known
                               WHERE known.entity_id = named.id
                                 AND (similarity({Folded("r.spelling")}, known.spelling) >= {Spelled}
                                      OR (length({Folded("r.spelling")}) >= 3
                                          AND strpos(known.spelling, {Folded("r.spelling")}) > 0)))
         ),
         foreign_seen AS MATERIALIZED (
             SELECT f.text_id, f.normalised_text, a.entity_id, count(*) AS n
             FROM (SELECT DISTINCT text_id, normalised_text FROM foreign_suspect) f
             JOIN word same ON same.text_id = f.text_id AND same.normalised_text = f.normalised_text
             JOIN word_entity a ON a.word_id = same.id
             GROUP BY 1, 2, 3
         ),
         foreign_name AS MATERIALIZED (
             SELECT seen.text_id, seen.normalised_text, seen.entity_id,
                    (SELECT sum(alike.n) FROM foreign_seen alike
                     WHERE alike.text_id = seen.text_id AND alike.normalised_text = seen.normalised_text
                       AND {Kin("seen.entity_id", "alike.entity_id")}) AS n
             FROM foreign_seen seen
         ),
         foreign_tally AS MATERIALIZED (
             SELECT s.word_id, s.entity_id, s.through, s.seed_verse, seen.entity_id AS other, seen.n,
                    {Kin("s.entity_id", "seen.entity_id")} AS kin
             FROM foreign_suspect s
             JOIN foreign_seen seen ON seen.text_id = s.text_id AND seen.normalised_text = s.normalised_text
         ),
         foreign_refused AS MATERIALIZED (
             SELECT DISTINCT s.word_id, s.entity_id, s.through
             FROM foreign_suspect s
             JOIN foreign_tally other ON other.word_id = s.word_id AND other.entity_id = s.entity_id
                  AND other.through = s.through AND NOT other.kin
             JOIN foreign_name theirs ON theirs.text_id = s.text_id AND theirs.normalised_text = s.normalised_text
                  AND theirs.entity_id = other.other
             CROSS JOIN LATERAL (
                 SELECT coalesce(sum(mine.n), 0) AS n FROM foreign_tally mine
                 WHERE mine.word_id = s.word_id AND mine.entity_id = s.entity_id AND mine.through = s.through
                   AND mine.kin) ours
             WHERE theirs.n >= {Least}
               AND theirs.n >= {Outnumbering} * ours.n
               AND {NamedAt("s.seed_verse", "other.other")}
               AND (SELECT count(*) FROM (
                        SELECT 1 FROM word same
                        JOIN word_entity mine ON mine.word_id = same.id
                        WHERE same.text_id = s.text_id AND same.normalised_text = s.normalised_text
                          AND {Kin("s.entity_id", "mine.entity_id")}
                          AND NOT {NamedAt("same.verse_id", "other.other")}
                        LIMIT {Elsewhere}) found) < {Elsewhere}
         )
         """;

    /// <summary>
    /// Whether a record, or one of its name, is named anywhere in a verse — in any text standing at
    /// the verse's canonical address, so that a translation the record reaches nowhere still answers
    /// for the verse its original names it in.
    /// </summary>
    private static string NamedAt(string verse, string entity) =>
        $"""
         EXISTS (SELECT 1 FROM verse_reference at_verse
                 JOIN verse_reference alike ON alike.canonical_book = at_verse.canonical_book
                      AND alike.canonical_chapter = at_verse.canonical_chapter
                      AND alike.canonical_verse = at_verse.canonical_verse AND alike.is_primary
                 JOIN word beside ON beside.verse_id = alike.verse_id
                 JOIN word_entity there ON there.word_id = beside.id
                 WHERE at_verse.verse_id = {verse} AND at_verse.is_primary
                   AND {Kin(entity, "there.entity_id")})
         """;

    /// <summary>A word written as a name where it stands: with a capital, after a word that does not close a sentence.</summary>
    public static string WrittenAsName(string word) =>
        $"""
         ({word}.text ~ '^[[:upper:]]'
          AND EXISTS (SELECT 1 FROM word before
                      WHERE before.verse_id = {word}.verse_id AND before.position = {word}.position - 1
                        AND before.trailer !~ '[.!?]'))
         """;

    /// <summary>
    /// The carried rows the test refuses, which nothing else holds up: a row with a claim no link
    /// carried has an answer read of its own word beneath it.
    /// </summary>
    private static readonly string Refused =
        $"""
         CREATE TEMP TABLE refused ON COMMIT DROP AS
         WITH carried AS MATERIALIZED (
             SELECT a.id AS row_id, a.word_id, a.entity_id, a.confidence, w.text AS spelling, w.text_id,
                    w.normalised_text, before.id IS NULL OR before.trailer ~ '[.!?]' AS opens,
                    (regexp_match(a.note, '^through \S+ word (\d+)'))[1]::bigint AS through
             FROM word_entity a
             JOIN entity named ON named.id = a.entity_id AND named.kind = 'person'
             JOIN word w ON w.id = a.word_id
             LEFT JOIN word before ON before.verse_id = w.verse_id AND before.position = w.position - 1
             WHERE a.note LIKE @carried AND a.confidence IS NOT NULL
               AND w.text ~ '^[[:upper:]]'
               AND NOT EXISTS (SELECT 1 FROM word_entity_claim c
                               WHERE c.word_entity_id = a.id AND c.method <> 'stated-by-source'
                                 AND coalesce(c.note, '') NOT LIKE @carried)
         ),
         {Refusals("carried")}
         SELECT c.row_id AS id, t.slug, c.spelling, named.slug AS named
         FROM carried c
         JOIN foreign_refused r ON r.word_id = c.word_id AND r.entity_id = c.entity_id AND r.through = c.through
         JOIN text t ON t.id = c.text_id
         JOIN entity named ON named.id = c.entity_id
         """;

    /// <param name="write">False to count what would be taken back and take back nothing.</param>
    public async Task<ForeignNameOutcome> Withdraw(CancellationToken cancellationToken = default, bool write = true)
    {
        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var prepare = new NpgsqlCommand(Prepare, connection, transaction))
        {
            await prepare.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var find = new NpgsqlCommand(Refused, connection, transaction))
        {
            find.Parameters.AddWithValue("carried", Annotating.CarriedNote);
            await find.ExecuteNonQueryAsync(cancellationToken);
        }

        var byText = new List<(string, int)>();
        await using (var count = new NpgsqlCommand(
                         "SELECT slug, count(*) FROM refused GROUP BY 1 ORDER BY 2 DESC, 1", connection, transaction))
        await using (var reader = await count.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                byText.Add((reader.GetString(0), (int)reader.GetInt64(1)));
            }
        }

        var examples = new List<string>();
        await using (var some = new NpgsqlCommand(
                         """
                         SELECT r.slug || ' ' || r.spelling || ': ' || r.named
                         FROM refused r ORDER BY r.slug, r.id LIMIT 12
                         """, connection, transaction))
        await using (var reader = await some.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                examples.Add(reader.GetString(0));
            }
        }

        int withdrawn;
        await using (var delete = new NpgsqlCommand(
                         "DELETE FROM word_entity a USING refused r WHERE a.id = r.id", connection, transaction))
        {
            withdrawn = await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        if (write)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        var outcome = new ForeignNameOutcome(withdrawn, byText, examples, started.Elapsed);
        logger.LogInformation("{Outcome}", outcome);
        return outcome;
    }
}
