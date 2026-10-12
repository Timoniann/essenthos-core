using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="ByLexeme">Hebrew names given the record every other occurrence of their lexeme names.</param>
/// <param name="ByDescent">Hebrew names BHSA marks as a descent group, given the people named after their bearer.</param>
/// <param name="Written">Annotations written under this pass's source, the carried ones included.</param>
internal sealed record LexemeNameOutcome(
    int ByLexeme,
    int ByDescent,
    int Written,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        ByLexeme + ByDescent == 0
            ? "no Hebrew name is left that its lexeme or its descent group answers"
            : $"{ByLexeme} Hebrew names given the record their lexeme names everywhere else and {ByDescent} " +
              $"descent-group names given the people called after their bearer, in {Elapsed}; {Written} " +
              "words written in all. Per text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// Hebrew names nothing answered although the corpus knows whom they name.
///
/// <para>
/// <strong>A name whose own number names nobody is read by its lexeme.</strong> BHSA's Strong
/// numbers come word by word, and on a name built of a common noun the word often carries the noun's
/// number: Lo-debar carries <em>not</em>, Mahanaim <em>camp</em>, the Ophel of Micah 4:8 the number
/// beside Ophel's own. The resolution by number asks of that number and finds no record, while every
/// other occurrence of the same lexeme names one record. Such a word is given that record, where its
/// kind is a kind BHSA marks the word: a lexeme of a man is not given a city because one occurrence
/// was misread as one.
/// </para>
///
/// <para>
/// <strong>A descent group is the people named after its bearer.</strong> Where BHSA marks a name as
/// the descent group and nothing else — <em>the children of Ammon</em> — the word is the people named
/// after a man who bears the number, when exactly one such people is held; the land of the same name
/// is not what a descent group names. A name BHSA marks as a man and a people at once says neither, and is left alone.
/// </para>
///
/// <para>
/// Only words no pass named are asked, so a second run writes nothing.
/// </para>
/// </summary>
internal sealed class LexemeNameLoader(AppDbContext db, ILogger<LexemeNameLoader> logger)
{
    public const string Source =
        "Essenthos, reading a Hebrew name by its lexeme or its descent group where its own number names nobody";

    private const double ByTheLexeme = 0.85;

    private const string Occurrences =
        """
        CREATE TEMP TABLE unnamed ON COMMIT DROP AS
        SELECT w.id AS word_id, w.lemma, w.strong_number,
               string_to_array(coalesce(w.morphology->>'nameType', ''), ',') AS kinds,
               coalesce(w.morphology->>'lexicalSet' LIKE '%gntl%', FALSE) AS gentilic
        FROM word w
        JOIN text t ON t.id = w.text_id AND t.slug = @witness
        WHERE (w.morphology->>'pos' = 'nmpr' OR w.morphology->>'lexicalSet' LIKE '%gntl%')
          AND NOT EXISTS (SELECT 1 FROM word_entity a WHERE a.word_id = w.id);

        CREATE TEMP TABLE answer (word_id bigint PRIMARY KEY, entity_id integer NOT NULL, rule text NOT NULL)
        ON COMMIT DROP;

        WITH unheld AS (
            SELECT u.* FROM unnamed u
            WHERE u.lemma IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM entity_name n WHERE n.hebrew_strong_number = u.strong_number)),
        lexeme AS (
            SELECT w.lemma, min(a.entity_id) AS entity_id
            FROM word w
            JOIN text t ON t.id = w.text_id AND t.slug = @witness
            JOIN word_entity a ON a.word_id = w.id
            WHERE w.lemma IN (SELECT lemma FROM unheld)
            GROUP BY w.lemma
            HAVING min(a.entity_id) = max(a.entity_id))
        INSERT INTO answer (word_id, entity_id, rule)
        SELECT u.word_id, l.entity_id, 'lexeme'
        FROM unheld u
        JOIN lexeme l ON l.lemma = u.lemma
        JOIN entity e ON e.id = l.entity_id
        WHERE (u.gentilic AND e.kind = 'people')
           OR (NOT u.gentilic AND (
                ('pers' = ANY(u.kinds) AND e.kind = 'person')
             OR ('topo' = ANY(u.kinds) AND e.kind = 'place')
             OR ('gens' = ANY(u.kinds) AND e.kind = 'people')));

        INSERT INTO answer (word_id, entity_id, rule)
        SELECT u.word_id, min(p.id), 'descent'
        FROM unnamed u
        JOIN entity_name n ON n.hebrew_strong_number = u.strong_number
        JOIN entity p ON p.kind = 'people' AND p.origin_entity_id = n.entity_id
        JOIN entity bearer ON bearer.id = n.entity_id AND bearer.kind = 'person'
        WHERE u.kinds = ARRAY['gens']
        GROUP BY u.word_id
        HAVING min(p.id) = max(p.id)
        ON CONFLICT (word_id) DO NOTHING;
        """;

    private const string Seed =
        """
        INSERT INTO pending_annotation (word_id, entity_id, confidence, corroborated, note)
        SELECT a.word_id, a.entity_id, @confidence, FALSE,
               CASE a.rule
                   WHEN 'lexeme' THEN coalesce(w.strong_number, 'no number')
                        || ', which names no record; every other occurrence of the lexeme names this one'
                   ELSE w.strong_number || ', marked the descent group, which is the people named after its bearer'
               END
        FROM answer a
        JOIN word w ON w.id = a.word_id
        """;

    public async Task<LexemeNameOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Occurrences, cancellationToken,
            ("witness", EntityCandidates.Witness));
        var (byLexeme, byDescent) = await Counted(connection, transaction, cancellationToken);

        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
        await Annotating.Run(connection, transaction, Seed, cancellationToken, ("confidence", ByTheLexeme));
        await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);

        var method = EnumSpelling.Of(LinkMethod.Lexical);
        await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
            ("method", method), ("source", Source));
        await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
            ("method", method), ("source", Source));

        var byText = await Annotating.ByText(connection, transaction, Source, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new LexemeNameOutcome(byLexeme, byDescent, byText.Sum(t => t.Words), byText, started.Elapsed);
        logger.LogInformation("Named by lexeme and descent: {Outcome}", outcome);
        return outcome;
    }

    private static async Task<(int Lexeme, int Descent)> Counted(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FILTER (WHERE rule = 'lexeme'), count(*) FILTER (WHERE rule = 'descent') FROM answer",
            connection, (NpgsqlTransaction)transaction.GetDbTransaction());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ((int)reader.GetInt64(0), (int)reader.GetInt64(1));
    }
}
