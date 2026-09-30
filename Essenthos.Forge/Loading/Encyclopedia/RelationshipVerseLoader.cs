using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Written">Verses listed for a record on this run.</param>
/// <param name="Withdrawn">
/// Verses taken back, because no relationship of the record is read from the verse any more or because
/// the record has come to be listed at a verse of another kind.
/// </param>
/// <param name="Listed">How many such verses the corpus lists in all.</param>
internal sealed record RelationshipVerseOutcome(int Written, int Withdrawn, int Listed, TimeSpan Elapsed)
{
    public override string ToString() =>
        Written == 0 && Withdrawn == 0
            ? $"the {Listed} verses the relationships of records listed nowhere else were read from are already listed"
            : $"{Written} verses a relationship was read from listed for a record listed nowhere else, " +
              $"{Withdrawn} withdrawn, {Listed} in all, in {Elapsed}";
}

/// <summary>
/// The verses a record's relationships were read from, listed on its page where nothing else lists a
/// verse for it.
///
/// <para>
/// Every relationship is this project's own reading of a verse, so the verse it was read from is one
/// the text has the record in. A record the corpus has no word of — a son of Mizraim the genealogy
/// names under the plural of his people, a king the list of Joshua 12 counts — would otherwise have a
/// page with no verse at all while its family tree cites them. The rows say what they are: the verse
/// concerns the record, and no word of it is claimed to name him, so the naming step leaves them
/// <see cref="ReferenceKinds.Concerning"/>.
/// </para>
///
/// <para>
/// Only for a record with no other verse a reader is shown, and taken back once it has one, so the
/// same corpus lists the same verses whatever order the passes ran in. Idempotent.
/// </para>
/// </summary>
internal sealed class RelationshipVerseLoader(AppDbContext db, ILogger<RelationshipVerseLoader> logger)
{
    /// <summary>What every row written here says about itself.</summary>
    public const string Source =
        "Essenthos, the verse a relationship of this project's own was read from, which concerns the record";

    private static readonly string Read =
        $"""
         read AS MATERIALIZED (
             SELECT r.from_entity_id AS entity_id, r.canonical_book, r.canonical_chapter, r.canonical_verse
             FROM entity_relationship r
             WHERE r.canonical_book IS NOT NULL AND r.canonical_chapter IS NOT NULL
               AND r.canonical_verse IS NOT NULL AND r.source NOT LIKE '{ShownVerses.Witness}%'
             UNION
             SELECT r.to_entity_id, r.canonical_book, r.canonical_chapter, r.canonical_verse
             FROM entity_relationship r
             WHERE r.canonical_book IS NOT NULL AND r.canonical_chapter IS NOT NULL
               AND r.canonical_verse IS NOT NULL AND r.source NOT LIKE '{ShownVerses.Witness}%'
         ),
         listed AS MATERIALIZED (
             SELECT DISTINCT v.entity_id
             FROM entity_verse v
             WHERE v.source NOT LIKE '{ShownVerses.Witness}%' AND v.source <> @source
         )
         """;

    private static readonly string Listing =
        $"""
         WITH {Read}
         INSERT INTO entity_verse (entity_id, canonical_book, canonical_chapter, canonical_verse,
                                   label, disputed, source)
         SELECT DISTINCT t.entity_id, t.canonical_book, t.canonical_chapter, t.canonical_verse, NULL, FALSE, @source
         FROM read t
         WHERE NOT EXISTS (SELECT 1 FROM listed l WHERE l.entity_id = t.entity_id)
           AND NOT EXISTS (
               SELECT 1 FROM entity_verse cited
               WHERE cited.entity_id = t.entity_id
                 AND cited.canonical_book = t.canonical_book
                 AND cited.canonical_chapter = t.canonical_chapter
                 AND cited.canonical_verse = t.canonical_verse
                 AND cited.source = @source)
         """;

    private static readonly string Withdrawal =
        $"""
         WITH {Read}
         DELETE FROM entity_verse cited
         WHERE cited.source = @source
           AND (EXISTS (SELECT 1 FROM listed l WHERE l.entity_id = cited.entity_id)
                OR NOT EXISTS (
                    SELECT 1 FROM read t
                    WHERE t.entity_id = cited.entity_id
                      AND t.canonical_book = cited.canonical_book
                      AND t.canonical_chapter = cited.canonical_chapter
                      AND t.canonical_verse = cited.canonical_verse))
         """;

    public async Task<RelationshipVerseOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var withdrawn = await Run(connection, transaction, Withdrawal, cancellationToken);
        var written = await Run(connection, transaction, Listing, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var listed = await db.EntityVerses.CountAsync(v => v.Source == Source, cancellationToken);
        var outcome = new RelationshipVerseOutcome(written, withdrawn, listed, started.Elapsed);
        logger.LogInformation("Listed the verses the relationships were read from: {Outcome}", outcome);
        return outcome;
    }

    private static async Task<int> Run(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("source", Source);
        command.CommandTimeout = Annotating.Patient;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
