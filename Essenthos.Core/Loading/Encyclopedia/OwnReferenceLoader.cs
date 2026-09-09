using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Written">
/// Verse references this pass added, one per entity and canonical address however many words or
/// texts name the entity there. It is every one of them on a cold corpus, and on a boot after that
/// only the records nothing had cited yet.
/// </param>
/// <param name="Cited">
/// How many the corpus reads off its own words in all, so that a pass writing nothing can be told
/// from one that had nothing to read.
/// </param>
/// <param name="Withdrawn">
/// References no annotation of ours stands behind any more, taken back. Zero unless something has
/// withdrawn an annotation this pass had already read a verse off.
/// </param>
internal sealed record OwnReferenceOutcome(
    bool AlreadyLoaded,
    int Written,
    int Withdrawn,
    int Cited,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? $"the {Cited} references this corpus reads for itself are already there, checked in " +
              $"{Elapsed}, with {Withdrawn} withdrawn"
            : $"{Written} verse references read off the annotations and {Withdrawn} withdrawn, " +
              $"{Cited} in all, in {Elapsed}";
}

/// <summary>
/// Where a person or a place is named, read off the words this corpus annotated rather than taken
/// from a dataset's list.
///
/// The verse lists on an entity page were BibleData's — 30,105 rows, the largest single block of
/// borrowed rows the reader is shown — and they did not need to be. <see cref="WordEntity"/>
/// already says which word of which text names whom, and a verse that names an entity follows from
/// that with nothing to fetch and nothing to generate: the claim is finer than the dataset's, since
/// it holds the word and not only the verse, and it reaches more entities.
///
/// <para>
/// **The dataset's rows stay.** They are the second witness this derivation is measured against,
/// the way its relationship rows are the answer key for the descriptors, and the measurement is the
/// reason to keep them: of its 28,229 distinct references, our own annotations independently reach
/// 18,924, and 420 of the entities it cites are ones our annotations reach not at all. Nearly all of
/// those 420 are titles rather than names — <em>God the Father</em>, <em>the angel of the LORD</em>,
/// <em>Pharaoh during the Exodus</em> — or the same name told apart into several men, which is the
/// disambiguation this corpus has not done yet. Deleting the dataset's rows would take those pages'
/// verses away and put nothing in their place.
/// </para>
///
/// <para>
/// **Not every annotation puts a verse on a page.** A word may carry several, because several
/// methods may speak about it, and what a reader is shown is one answer settled by standing —
/// testimony over inference, a person over both, and nothing at all where two methods of equal
/// standing name two different entities. That rule is <see cref="Endpoints.Annotations"/>'s and it
/// is not restated here: a verse listed on a page and the word that put it there must be the same
/// claim, so the ordering below is the same ordering, and the standing it orders by is read out of
/// <see cref="ClaimStanding"/> rather than copied into the statement.
/// </para>
///
/// <para>
/// The peoples keep their own derivation in <see cref="PeopleLoader"/>. It reads the same table and
/// arrives at the same addresses, but what establishes a people reference is the form of the word —
/// Strong's gentilic, resolving to one people — and what establishes these is the resolution of a
/// name, so the two are credited apart at the claim rather than blended under one line.
/// </para>
///
/// <para>
/// The derivation runs whole on every boot and writes only the references that are not there yet,
/// because it reads rows this corpus already holds rather than fetching anything. On a corpus that
/// is already complete that is about a second and no rows.
/// </para>
/// </summary>
internal sealed class OwnReferenceLoader(AppDbContext db, ILogger<OwnReferenceLoader> logger)
{
    /// <summary>
    /// What every reference written here says about itself, in the shape the peoples' references
    /// already use — this corpus's own reading of its own words, and never a dataset's testimony.
    /// </summary>
    private const string FromOurOwnWords =
        "Essenthos, from the words this corpus annotates to the person or the place they name";

    public async Task<OwnReferenceOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var written = await Run(connection, transaction, Derivation, cancellationToken);
        var withdrawn = await Run(connection, transaction, Retraction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var cited = await db.EntityVerses.CountAsync(v => v.Source == FromOurOwnWords, cancellationToken);

        var outcome = new OwnReferenceOutcome(
            written == 0 && withdrawn == 0 && cited > 0, written, withdrawn, cited, started.Elapsed);
        logger.LogInformation("Read the references off the annotations: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The settled annotations, and the verse each of them stands in.
    ///
    /// <c>DISTINCT</c> is what makes this a list of verses rather than a list of namings: the same
    /// entity is named by several words of one verse and by a word in each of the twenty-four texts,
    /// and every one of those is the same reference. Counting them apart is how a derivation over
    /// 23,644 references gets reported as 157,429.
    ///
    /// <para>
    /// The closing <c>NOT EXISTS</c> is the whole of the idempotence, and it is a statement about
    /// the row rather than about the pass: what is written is whatever this derivation reaches and
    /// this corpus does not already cite. Asking instead whether the pass had ever run would be
    /// right only if nothing could be added after it — and the peoples, the records this corpus
    /// writes for itself and the place register all add entities, so a record arriving later would
    /// have an empty page for ever.
    /// </para>
    /// </summary>
    private static readonly string Derivation =
        $"""
         WITH standing AS (
             SELECT a.word_id,
                    a.entity_id,
                    {Standing} AS standing,
                    coalesce(a.confidence, 1.0) AS confidence
             FROM word_entity a
         ),
         ranked AS (
             SELECT s.word_id,
                    s.entity_id,
                    s.standing,
                    s.confidence,
                    row_number() OVER settling AS place,
                    count(*) OVER (PARTITION BY s.word_id) AS claims,
                    lead(s.entity_id) OVER settling AS next_entity,
                    lead(s.standing) OVER settling AS next_standing,
                    lead(s.confidence) OVER settling AS next_confidence
             FROM standing s
             WINDOW settling AS (PARTITION BY s.word_id ORDER BY s.standing DESC, s.confidence DESC)
         ),
         settled AS (
             SELECT word_id, entity_id
             FROM ranked
             WHERE place = 1
               AND (claims = 1
                    OR NOT (next_entity <> entity_id
                            AND next_standing = standing
                            AND next_confidence = confidence))
         )
         INSERT INTO entity_verse (entity_id, canonical_book, canonical_chapter, canonical_verse,
                                   label, disputed, source)
         SELECT DISTINCT s.entity_id, r.canonical_book, r.canonical_chapter, r.canonical_verse,
                NULL, FALSE, @source
         FROM settled s
         JOIN entity e ON e.id = s.entity_id AND e.kind IN ({Named})
         JOIN word w ON w.id = s.word_id
         JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
         WHERE NOT EXISTS (
             SELECT 1 FROM entity_verse cited
             WHERE cited.entity_id = s.entity_id
               AND cited.canonical_book = r.canonical_book
               AND cited.canonical_chapter = r.canonical_chapter
               AND cited.canonical_verse = r.canonical_verse
               AND cited.source = @source)
         """;

    /// <summary>
    /// A reference of ours that no annotation of ours stands behind any more.
    ///
    /// The derivation writes a verse because a word in it names the entity. An annotation can be
    /// taken back — <see cref="EntityAnnotationLoader"/> withdraws a resolution once the
    /// encyclopedia answers its number with more than one place — and the reference then goes on
    /// asserting, in this corpus's own name, a reading this corpus no longer has. It reads as
    /// scholarship and it is a leftover, which is the same fault as the annotation it came from and
    /// one hop further from where a reader could see it.
    ///
    /// <para>
    /// Only ours, and only where nothing at all is left: an entity still named at a word of that
    /// verse keeps the reference whichever annotation settles the word, because the derivation only
    /// ever claimed the verse. The gazetteer's rows and the datasets' say what they always said.
    /// </para>
    ///
    /// <para>
    /// In the same transaction as the derivation. The two are one statement about what this corpus
    /// reads, and half of it committing on its own is the failure MST-0184 records.
    /// </para>
    /// </summary>
    private static readonly string Retraction =
        """
        DELETE FROM entity_verse cited
        WHERE cited.source = @source
          AND NOT EXISTS (
              SELECT 1 FROM word_entity a
              JOIN word w ON w.id = a.word_id
              JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
              WHERE a.entity_id = cited.entity_id
                AND r.canonical_book = cited.canonical_book
                AND r.canonical_chapter = cited.canonical_chapter
                AND r.canonical_verse = cited.canonical_verse)
        """;

    private async Task<int> Run(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("source", FromOurOwnWords);
        command.CommandTimeout = Annotating.Patient;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// How much each method knew before it started, written out of <see cref="ClaimStanding"/> so
    /// that renumbering it moves this statement too. A second copy of those ordinals is a second
    /// answer to <em>who does this word name</em>, and the whole point of deriving the references
    /// from the annotations is that a page and a word cannot disagree.
    /// </summary>
    private static string Standing =>
        "CASE a.method "
        + string.Concat(Enum.GetValues<LinkMethod>().Select(method =>
            $"WHEN '{EnumSpelling.Of(method)}' THEN "
            + ClaimStanding.Of(method).ToString(CultureInfo.InvariantCulture) + " "))
        + "ELSE 0 END";

    /// <summary>
    /// The kinds whose references this reads. The peoples are left out because
    /// <see cref="PeopleLoader"/> already reads theirs on a different ground.
    /// </summary>
    private static string Named =>
        string.Join(", ", new[] { EntityKind.Person, EntityKind.Place }
            .Select(kind => $"'{EnumSpelling.Of(kind)}'"));
}
