using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Moved">Relationships and clauses that now name the man the verse is about.</param>
/// <param name="Withdrawn">Clauses, and the relationships read off them, that described the wrong man by the verse.</param>
/// <param name="Joined">Clauses that, once they name the right man, said what his record already says.</param>
/// <param name="Renamed">Words a verse list settled that now name the man it lists there.</param>
internal sealed record RefiledTieOutcome(int Moved, int Withdrawn, int Joined, int Renamed, TimeSpan Elapsed)
{
    public override string ToString() =>
        Moved + Withdrawn + Joined + Renamed == 0
            ? "nothing read off a misfiled verse still names the man it was filed under"
            : $"{Moved} relationships and clauses read off a misfiled verse moved to the man it names, " +
              $"{Withdrawn} that described the other man by it withdrawn, {Joined} that repeated what the " +
              $"record already says joined to it, and {Renamed} words a verse list settled renamed, in {Elapsed}";
}

/// <summary>
/// What was read off a verse while the dataset filed it under the wrong man, moved with the verse.
///
/// <para>
/// <see cref="PersonRegisterLoader.MisfiledVerses"/> moves a verse to the record it names before
/// anything reads the verse lists, so a corpus built from nothing reads each verse against the right
/// man. A corpus built before the verse was moved has already read it against the wrong one, and the
/// passes that did so run once: a description of ours was read from it about him, and a name settled
/// by the one record a list named there names him. Nehemiah 12:10 made the Levite Jeshua the father
/// of Joiakim the high priest twice over in our own reading.
/// </para>
///
/// <para>
/// **A tie with the man at the other end moves; a description of the man himself goes.** A
/// relationship or clause pointing at him from the verse was read about whoever the verse names, so
/// it points at the record the verse was moved to. A clause describing him was read from a verse that
/// is not about him, and the descriptor pass would refuse it today for citing a verse he is not named
/// in, so it is withdrawn with the relationship read off it. A clause that, once moved, says what
/// the record already says is joined to it, so the description does not say it twice. A decision
/// the owner made is never touched.
/// </para>
///
/// <para>
/// After the fold, because a folded record's clauses are what a moved clause may repeat. On a corpus
/// already put right it finds nothing.
/// </para>
/// </summary>
internal sealed class RefiledTieLoader(AppDbContext db, ILogger<RefiledTieLoader> logger)
{
    /// <summary>The names a verse list alone settled, which follow the list.</summary>
    private static readonly string[] Listed = [RenderedNameLoader.Source, ListedBearerLoader.Source];

    private const string Refiled =
        """
        CREATE TEMP TABLE refiled (held integer NOT NULL, target integer NOT NULL, book integer NOT NULL,
                                   chapter integer NOT NULL, verse integer NOT NULL) ON COMMIT DROP;
        INSERT INTO refiled
        SELECT DISTINCT h.id, t.id, u.book, u.chapter, u.verse
        FROM unnest(@held, @target, @books, @chapters, @verses) AS u(held, target, book, chapter, verse)
        CROSS JOIN LATERAL (SELECT e.id FROM entity e WHERE e.source_id = u.held
                            UNION ALL SELECT m.entity_id FROM merged_record m WHERE m.record_source_id = u.held
                            LIMIT 1) h
        CROSS JOIN LATERAL (SELECT e.id FROM entity e WHERE e.source_id = u.target
                            UNION ALL SELECT m.entity_id FROM merged_record m WHERE m.record_source_id = u.target
                            LIMIT 1) t
        WHERE h.id <> t.id
        """;

    private const string At = "(x.canonical_book, x.canonical_chapter, x.canonical_verse) = (r.book, r.chapter, r.verse)";

    private const string Reading = "x.source LIKE @reading AND x.method = @read";

    private static readonly string WithdrawReadRelationships =
        $"DELETE FROM entity_relationship x USING refiled r WHERE x.from_entity_id = r.held AND {At} AND {Reading}";

    private static readonly string MoveRelationshipsTo =
        $"""
         UPDATE entity_relationship x SET to_entity_id = r.target FROM refiled r
         WHERE x.to_entity_id = r.held AND {At} AND {Reading}
         """;

    private const string WithdrawSelfRelationships =
        "DELETE FROM entity_relationship x USING refiled r WHERE x.from_entity_id = r.target AND x.to_entity_id = r.target";

    private static readonly string WithdrawClauses =
        $"DELETE FROM entity_descriptor x USING refiled r WHERE x.entity_id = r.held AND {At} AND x.method = @read";

    private static readonly string MoveClauses =
        $"""
         UPDATE entity_descriptor x SET target_entity_id = r.target FROM refiled r
         WHERE x.target_entity_id = r.held AND {At} AND x.method = @read
         """;

    private static readonly string JoinClauses =
        $"""
         DELETE FROM entity_descriptor x USING refiled r
         WHERE x.target_entity_id = r.target AND {At} AND x.method = @read
           AND (x.entity_id = x.target_entity_id OR EXISTS (
               SELECT 1 FROM entity_descriptor o
               WHERE o.entity_id = x.entity_id AND o.relation = x.relation
                 AND o.target_entity_id = x.target_entity_id
                 AND (o.canonical_book, o.canonical_chapter, o.canonical_verse) <> (r.book, r.chapter, r.verse)))
         """;

    private const string OnTheVerse =
        """
        a.entity_id = r.held AND a.source = ANY(@listed) AND w.id = a.word_id
          AND v.verse_id = w.verse_id AND v.is_primary
          AND (v.canonical_book, v.canonical_chapter, v.canonical_verse) = (r.book, r.chapter, r.verse)
        """;

    private static readonly string DropRenamedTwice =
        $"""
         DELETE FROM word_entity a USING refiled r, word w, verse_reference v
         WHERE {OnTheVerse}
           AND EXISTS (SELECT 1 FROM word_entity o WHERE o.word_id = a.word_id AND o.entity_id = r.target)
         """;

    private static readonly string Rename =
        $"UPDATE word_entity a SET entity_id = r.target FROM refiled r, word w, verse_reference v WHERE {OnTheVerse}";

    public async Task<RefiledTieOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var refiled = PersonRegisterLoader.MisfiledVerses
            .Where(m => m.Target is not null)
            .Select(m => (m.Held, Target: m.Target!, At: TitleLoader.Verse(m.Reference)
                ?? throw new InvalidDataException(
                    $"{m.Reference} is not a verse; write a misfiled verse as NEH 12:10.")))
            .ToList();

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        (string, object)[] parameters =
        [
            ("held", refiled.Select(r => r.Held).ToArray()),
            ("target", refiled.Select(r => r.Target).ToArray()),
            ("books", refiled.Select(r => r.At.Book).ToArray()),
            ("chapters", refiled.Select(r => r.At.Chapter).ToArray()),
            ("verses", refiled.Select(r => r.At.Verse).ToArray()),
            ("reading", Sources.DescriptorReadingPrefix + "%"),
            ("read", EnumSpelling.Of(LinkMethod.ModelReading)),
            ("listed", Listed),
        ];

        await Run(connection, transaction, Refiled, parameters, cancellationToken);
        var withdrawn = await Run(connection, transaction, WithdrawReadRelationships, parameters, cancellationToken);
        var moved = await Run(connection, transaction, MoveRelationshipsTo, parameters, cancellationToken);
        withdrawn += await Run(connection, transaction, WithdrawSelfRelationships, parameters, cancellationToken);
        withdrawn += await Run(connection, transaction, WithdrawClauses, parameters, cancellationToken);
        moved += await Run(connection, transaction, MoveClauses, parameters, cancellationToken);
        var joined = await Run(connection, transaction, JoinClauses, parameters, cancellationToken);
        var renamed = await Run(connection, transaction, DropRenamedTwice, parameters, cancellationToken);
        renamed += await Run(connection, transaction, Rename, parameters, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new RefiledTieOutcome(moved, withdrawn, joined, renamed, started.Elapsed);
        logger.LogInformation("What was read off the misfiled verses: {Outcome}", outcome);
        return outcome;
    }

    private static async Task<int> Run(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        (string Name, object Value)[] parameters,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Math.Max(0, await command.ExecuteNonQueryAsync(cancellationToken));
    }
}
