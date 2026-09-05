using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading.Frame;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading;

internal sealed record SuperscriptionOutcome(string Slug, int Verses, int Placed, int Joined, TimeSpan Elapsed)
{
    public override string ToString() => (Verses, Placed, Joined) switch
    {
        (0, _, _) => $"{Slug} marks no superscription inside a verse of its own",
        (_, 0, 0) => $"{Slug} already covers the title verse of {Verses} psalms",
        _ => $"{Slug}: {Verses} verses cover a title verse as well as their own, {Placed} of them newly " +
             $"placed and {Joined} memberships added, in {Elapsed}",
    };
}

/// <summary>
/// Says that one verse of a translation covers two verses of the frame, where the verse holds a
/// psalm's superscription and the frame numbers that superscription as a verse of its own.
///
/// <para>
/// The Hebrew counts <em>A Psalm of David, when he fled from Absalom his son</em> as Psalm 3:1 and
/// the body as 3:2; the Synodal and Ohienko's Ukrainian print both inside the verse their publisher
/// numbered 3:1. Placed at one address that verse is aligned against the body alone, and its nine
/// title words reach nothing while the Hebrew states them word for word one row above. The frame
/// already has the shape to say otherwise — a verse may carry more than one canonical address, and
/// seventy-four of them already do — so this writes the second address rather than redrawing a
/// verse or asserting a division inside one.
/// </para>
///
/// <para>
/// **Which verses those are is read out of the two editions, not out of the shift.** That a psalm
/// is numbered one verse apart is a fact about the Hebrew and says nothing about what any Russian
/// verse holds, so two statements the files make about themselves are what decides it: the Synodal
/// wraps a superscription in <c>^^</c>, and both files write the address their own pages give what
/// follows a marker, so words standing before a marker naming verse two or later are the edition's
/// own earlier verse. Where a file says neither, nothing is written for that psalm.
/// </para>
///
/// <para>
/// Both are then required to land on a chapter the frame already holds a title verse for, which is
/// what makes the statement checkable rather than merely plausible: the Synodal marks a
/// superscription in 120 psalms and only 63 of them are ones the Hebrew numbers apart, and the
/// other 57 keep the title inside verse one exactly as the Hebrew does, so there is nothing to say
/// about them. Measured over the two files as loaded, the two signals select 62 and 61 psalms and
/// every one of them is among those 63.
/// </para>
///
/// <para>
/// A pass of its own, after the verses have been joined, because both texts are already loaded and
/// already placed everywhere this corpus exists: the frame loader returns early for a text that has
/// references and the verse-link loader for a pair that has links, so a filler guarded on its own
/// rows is the only thing that reaches those databases. It is idempotent twice over — a verse that
/// already carries the address is skipped, and the memberships are inserted on conflict — so it
/// also repairs itself after the verse links are rebuilt from the primary addresses alone.
/// </para>
/// </summary>
internal sealed class SuperscriptionFrameLoader(AppDbContext db, ILogger<SuperscriptionFrameLoader> logger)
{
    /// <summary>
    /// The verse a title stands before. A superscription is a chapter's title and nothing else in a
    /// chapter has one, so the verse that covers it is the chapter's first.
    /// </summary>
    private const int FirstVerse = 1;

    /// <summary>
    /// Adds the title verses of the other text to the verse link the two texts already share for a
    /// covering verse, so that the boundary a word link now crosses is one the frame joins.
    ///
    /// <para>
    /// Written here rather than derived by the verse-link loader from every address a verse carries,
    /// because that loader reads the primary address on purpose and the other addresses in this
    /// corpus are not all of this kind: the King James' Esther 1:1 carries twenty of them, standing
    /// for the Greek additions it does not contain, and joining it to twenty verses of anything
    /// would be a correspondence nobody stated.
    /// </para>
    /// </summary>
    private const string JoinTitleVerses =
        """
        INSERT INTO verse_link_verse (verse_link_id, verse_id, side)
        SELECT DISTINCT member.verse_link_id,
               title.id,
               CASE WHEN member.side = 'from' THEN 'to' ELSE 'from' END
        FROM verse_reference cover
        JOIN verse covering ON covering.id = cover.verse_id AND covering.text_id = @text
        JOIN verse_link_verse member ON member.verse_id = cover.verse_id
        JOIN verse_link link ON link.id = member.verse_link_id
        JOIN verse_reference stands
          ON stands.is_primary
         AND stands.canonical_book = cover.canonical_book
         AND stands.canonical_chapter = cover.canonical_chapter
         AND stands.canonical_verse = @title
        JOIN verse title
          ON title.id = stands.verse_id
         AND title.text_id = CASE WHEN member.side = 'from' THEN link.to_text_id ELSE link.from_text_id END
        WHERE NOT cover.is_primary AND cover.canonical_verse = @title
        ON CONFLICT DO NOTHING
        """;

    public async Task<SuperscriptionOutcome> Load(TextSource source, CancellationToken cancellationToken = default)
    {
        var slug = source.Definition.Slug;
        var marked = source.Books
            .SelectMany(book => book.Chapters
                .SelectMany(chapter => chapter.Verses
                    .Where(verse => verse.MarksASuperscription || verse.OpensBeforeItsStatedAddress)
                    .Select(verse => (book.CanonicalOrdinal, chapter.Number, verse.Number, verse.Label))))
            .ToList();

        if (marked.Count == 0)
        {
            return new SuperscriptionOutcome(slug, 0, 0, 0, TimeSpan.Zero);
        }

        var text = await db.Texts.FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken);
        if (text is null)
        {
            throw new InvalidOperationException(
                $"The text \"{slug}\" marks a superscription inside a verse but is not loaded. Run this after " +
                "the corpus loader and the canonical frame, which are what write the verse and the address it " +
                "already stands at.");
        }

        var started = Stopwatch.StartNew();
        var covering = await Covering(text.Id, marked, cancellationToken);
        var placed = await Place(covering, cancellationToken);
        var joined = await Join(text.Id, cancellationToken);

        var outcome = new SuperscriptionOutcome(slug, covering.Count, placed, joined, started.Elapsed);
        logger.LogInformation("Superscriptions: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The verses that cover a title verse: the ones the edition marked, which stand at the first
    /// verse of a chapter the frame already holds a title verse for.
    ///
    /// The second condition is the one doing the work. A verse holding two of the edition's verses
    /// in the middle of a chapter satisfies the first — 1 Samuel 20:42 of the Synodal does, and so
    /// does 3 John 1:14 in both — and no title stands anywhere near either of them.
    /// </summary>
    private async Task<List<(int VerseId, CanonicalReference Title)>> Covering(
        int textId,
        List<(int Ordinal, int Chapter, int Number, string Label)> marked,
        CancellationToken cancellationToken)
    {
        var wanted = marked.ToHashSet();

        var titled = await db.VerseReferences
            .Where(r => r.IsPrimary && r.CanonicalVerse == CanonicalReference.TitleVerse)
            .Select(r => new { r.CanonicalBook, r.CanonicalChapter })
            .Distinct()
            .ToListAsync(cancellationToken);

        var titles = titled
            .Select(t => new CanonicalReference(t.CanonicalBook, t.CanonicalChapter, CanonicalReference.TitleVerse))
            .ToHashSet();

        var rows = await db.VerseReferences
            .Where(r => r.IsPrimary && r.Verse!.TextId == textId && r.CanonicalVerse == FirstVerse)
            .Select(r => new
            {
                r.VerseId,
                Ordinal = r.Verse!.Book!.CanonicalOrdinal,
                r.Verse.ChapterNumber,
                r.Verse.Number,
                r.Verse.Label,
                r.CanonicalBook,
                r.CanonicalChapter,
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows
                .Where(row => wanted.Contains((row.Ordinal, row.ChapterNumber, row.Number, row.Label)))
                .Select(row => (row.VerseId, new CanonicalReference(
                    row.CanonicalBook, row.CanonicalChapter, CanonicalReference.TitleVerse)))
                .Where(row => titles.Contains(row.Item2)),
        ];
    }

    private async Task<int> Place(
        List<(int VerseId, CanonicalReference Title)> covering,
        CancellationToken cancellationToken)
    {
        var ids = covering.Select(row => row.VerseId).ToList();
        var already = await db.VerseReferences
            .Where(r => ids.Contains(r.VerseId) && r.CanonicalVerse == CanonicalReference.TitleVerse)
            .Select(r => r.VerseId)
            .ToListAsync(cancellationToken);

        var placed = 0;
        foreach (var (verseId, title) in covering.Where(row => !already.Contains(row.VerseId)))
        {
            db.VerseReferences.Add(new VerseReference
            {
                VerseId = verseId,
                CanonicalBook = title.Book,
                CanonicalChapter = title.Chapter,
                CanonicalVerse = title.Verse,
                IsPrimary = false,
            });
            placed++;
        }

        if (placed > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return placed;
    }

    private async Task<int> Join(int textId, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var command = new NpgsqlCommand(JoinTitleVerses, connection);
        command.Parameters.AddWithValue("text", textId);
        command.Parameters.AddWithValue("title", CanonicalReference.TitleVerse);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
