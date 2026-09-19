using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading.Frame;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading;

/// <summary>Whether the words the edition lost stand before the verse's own words or after them.</summary>
internal enum PsalmOpeningPlace
{
    BeforeTheVerse,
    AfterTheVerse,
}

/// <param name="Psalm">The psalm, by the chapter number the loaded text gives it.</param>
internal sealed record PsalmOpening(
    int Psalm,
    PsalmOpeningPlace Place,
    IReadOnlyList<WordDraft> Words);

internal sealed record PsalmOpeningOutcome(string Slug, int Psalms, int Words, int Placed, TimeSpan Elapsed)
{
    public override string ToString() => Psalms == 0
        ? $"{Slug} already opens every psalm as its edition prints it"
        : $"{Slug}: {Words} words written into the first verse of {Psalms} psalms, {Placed} of those " +
          $"verses newly placed at the title address, in {Elapsed}";
}

/// <summary>
/// Writes the opening of a psalm that the file a text was loaded from does not print, from a
/// complete copy of the same edition.
///
/// <para>
/// Two texts here lost their psalm openings, and lost them differently. The King James comes from
/// bible4u, whose Zefania XML has no element for a superscription: *Psalm 51:1* there is *Have mercy
/// upon me*, and *To the chief Musician, A Psalm of David, when Nathan the prophet came unto him* is
/// in no verse of the file. Ohienko's Ukrainian comes from a digitisation that dropped a line: its
/// Psalm 7 has seventeen verses where Ohienko printed eighteen, and the body of the first — *Господи,
/// Боже мій, я до Тебе вдаюся* — is nowhere in it.
/// </para>
///
/// <para>
/// **Both are written into the psalm's first verse rather than into a verse of their own.** That is
/// what the six other English texts here already do — the American Standard, the Berean, Geneva,
/// the JPS TaNaKH, the World English Bible and Young's all print the superscription inside verse 1
/// — and it is what the Synodal and the rest of Ohienko's own psalms do. A King James title standing
/// at an address of its own would make this the one text of the corpus that separates them, and
/// would leave the place references the encyclopedia states at Psalm 56:1 and 60:1 pointing at a
/// verse that still does not hold the words they name.
/// </para>
///
/// <para>
/// A verse that then holds a title as well as a body stands at two addresses, which is the frame's
/// existing way of saying so and what <see cref="SuperscriptionFrameLoader"/> writes for the two
/// Slavic files. The second address is written only where the frame already holds a title verse at
/// it — 63 of the 116 psalms the King James titles — because for the other 53 the Hebrew keeps the
/// title inside its own verse 1 and there is nothing above it to stand at.
/// </para>
///
/// <para>
/// A pass of its own, because both texts are loaded and placed everywhere this corpus exists and the
/// corpus loader returns early for a text it already holds. It is guarded on what the verse says
/// rather than on a marker of its own: a psalm whose first verse already opens with these words has
/// been done, and a second run costs one query. It creates no verse, so it needs the frame loader to
/// have run rather than to run after it — whether the frame numbers a psalm's title apart is a
/// question only a placed corpus can answer.
/// </para>
/// </summary>
internal sealed class PsalmOpeningLoader(AppDbContext db, ILogger<PsalmOpeningLoader> logger)
{
    /// <summary>A psalm's title and its first line both belong to the verse the psalm opens with.</summary>
    private const int FirstVerse = 1;

    /// <summary>The only book this is about, by the code the canonical ordinals are keyed on.</summary>
    private const string PsalmsCode = "Psa";

    /// <summary>
    /// Makes room at the head of a verse. The existing positions go negative first, which keeps
    /// them distinct from each other and from everything about to be written, so the unique index
    /// on (verse, position) holds at every row of both statements rather than only at the end.
    /// </summary>
    private const string FreeTheHead =
        """
        UPDATE word SET "position" = -"position" WHERE verse_id = @verseId;
        """;

    private const string ShiftBack =
        """
        UPDATE word SET "position" = @written - "position" WHERE verse_id = @verseId AND "position" < 0;
        """;

    private const string SeparateTheTail =
        """
        UPDATE word SET trailer = trailer || ' '
        WHERE verse_id = @verseId
          AND "position" = (SELECT max("position") FROM word WHERE verse_id = @verseId)
        """;

    private const string Rebuild =
        """
        SELECT string_agg("text" || trailer, '' ORDER BY "position") FROM word WHERE verse_id = @verseId
        """;

    public async Task<PsalmOpeningOutcome> Load(
        string slug,
        IReadOnlyList<PsalmOpening> openings,
        string rightsNote,
        CancellationToken cancellationToken = default)
    {
        if (openings.Count == 0)
        {
            return new PsalmOpeningOutcome(slug, 0, 0, 0, TimeSpan.Zero);
        }

        var text = await db.Texts.FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken)
                   ?? throw new InvalidOperationException(
                       $"The text \"{slug}\" is missing the opening of {openings.Count} psalms but is not loaded. " +
                       "Run this after the corpus loader, which is what writes the verses these words go into.");

        var started = Stopwatch.StartNew();
        if (!BookCodes.TryGetOrdinal(PsalmsCode, out var psalms))
        {
            throw new InvalidOperationException(
                $"\"{PsalmsCode}\" is not a book code the canonical ordinals know, so there is no book to " +
                $"write a psalm into. Add it to {nameof(BookCodes)}.");
        }

        var verses = await db.Verses
            .Where(v => v.TextId == text.Id
                        && v.Book!.CanonicalOrdinal == psalms
                        && v.Number == FirstVerse
                        && v.Label == string.Empty)
            .Select(v => new
            {
                v.Id,
                v.ChapterNumber,
                Words = v.Words.OrderBy(w => w.Position).Select(w => w.Surface).ToList(),
                Count = v.Words.Count,
                Last = v.Words.OrderByDescending(w => w.Position).Select(w => w.Trailer).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var byChapter = verses.ToDictionary(v => v.ChapterNumber);
        var written = 0;
        var done = 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var opening in openings)
        {
            if (!byChapter.TryGetValue(opening.Psalm, out var verse))
            {
                throw new InvalidOperationException(
                    $"{slug} has no Psalm {opening.Psalm}:{FirstVerse} to write an opening into. The psalm " +
                    "numbering of the loaded text and of the edition these words come from disagree; settle " +
                    "which psalm is meant before loading anything.");
            }

            if (AlreadyThere(verse.Words, opening))
            {
                continue;
            }

            await Write(text.Id, verse.Id, verse.Count, verse.Last ?? string.Empty, opening, cancellationToken);
            written += opening.Words.Count;
            done++;
        }

        var placed = await CoverTheTitleAddress(text.Id, psalms, openings, cancellationToken);

        if (done > 0 || placed > 0)
        {
            await Attribute(text, rightsNote, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var outcome = new PsalmOpeningOutcome(slug, done, written, placed, started.Elapsed);
        logger.LogInformation("Psalm openings: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// Whether this verse already carries the words. Read off the verse itself rather than out of a
    /// row recording that the pass ran, because what has to be true is that the words are there —
    /// and a database restored from before the pass would carry the row and not the words.
    /// </summary>
    private static bool AlreadyThere(List<string> surfaces, PsalmOpening opening)
    {
        if (surfaces.Count < opening.Words.Count)
        {
            return false;
        }

        var from = opening.Place == PsalmOpeningPlace.BeforeTheVerse ? 0 : surfaces.Count - opening.Words.Count;
        return !opening.Words.Where((word, at) => surfaces[from + at] != word.Surface).Any();
    }

    private async Task Write(
        int textId,
        int verseId,
        int existing,
        string lastTrailer,
        PsalmOpening opening,
        CancellationToken cancellationToken)
    {
        var before = opening.Place == PsalmOpeningPlace.BeforeTheVerse;
        if (before)
        {
            await Execute(FreeTheHead, verseId, null, cancellationToken);
        }
        else if (existing > 0 && (lastTrailer.Length == 0 || !char.IsWhiteSpace(lastTrailer[^1])))
        {
            // A verse ends on its full stop with nothing after it, so the word the lost line
            // follows has to gain the space that separated them on the page. Without it the two
            // words are printed as one and searched for as one.
            await Execute(SeparateTheTail, verseId, null, cancellationToken);
        }

        for (var at = 0; at < opening.Words.Count; at++)
        {
            var word = opening.Words[at];
            db.Words.Add(new Word
            {
                TextId = textId,
                VerseId = verseId,
                Position = before ? at + 1 : existing + at + 1,
                Surface = word.Surface,
                Trailer = word.Trailer,
                StrongNumber = word.StrongNumber,
                Elided = word.Elided,
                Break = word.Break,
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        if (before)
        {
            await Execute(ShiftBack, verseId, opening.Words.Count, cancellationToken);
        }

        await EnsureRebuilds(verseId, opening, cancellationToken);
    }

    /// <summary>
    /// The property the corpus loader checks at load time, checked again here because this is the
    /// one pass that writes into a verse somebody else wrote: the words in order must still give
    /// back the verse, now with the opening in it. It runs inside the transaction, so a verse that
    /// does not rebuild leaves nothing behind.
    /// </summary>
    private async Task EnsureRebuilds(int verseId, PsalmOpening opening, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(Rebuild, connection,
            (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
        command.Parameters.AddWithValue("verseId", verseId);

        var rebuilt = (await command.ExecuteScalarAsync(cancellationToken)) as string ?? string.Empty;
        var head = VerseRoundTrip.Rebuild(opening.Words, w => w.Surface, w => w.Trailer);
        var lands = opening.Place == PsalmOpeningPlace.BeforeTheVerse
            ? rebuilt.StartsWith(head, StringComparison.Ordinal)
            : rebuilt.TrimEnd().EndsWith(head.TrimEnd(), StringComparison.Ordinal);

        if (!lands)
        {
            throw new InvalidOperationException(
                $"Psalm {opening.Psalm}:{FirstVerse} does not read as the opening plus what was already there " +
                $"after writing it: \"{rebuilt}\". The words were written at the wrong positions; the " +
                "transaction is rolled back, so nothing was changed.");
        }
    }

    /// <summary>
    /// The second address the psalm's first verse stands at, now that it holds the title as well as
    /// the body — and only where the frame already holds a title verse there, which is the 63
    /// psalms the Hebrew numbers apart out of the 116 the King James gives a title.
    /// </summary>
    private async Task<int> CoverTheTitleAddress(
        int textId,
        int psalms,
        IReadOnlyList<PsalmOpening> openings,
        CancellationToken cancellationToken)
    {
        var chapters = openings.Select(o => o.Psalm).ToHashSet();
        var numbered = await db.VerseReferences
            .Where(r => r.IsPrimary
                        && r.CanonicalBook == psalms
                        && r.CanonicalVerse == CanonicalReference.TitleVerse)
            .Select(r => r.CanonicalChapter)
            .Distinct()
            .ToListAsync(cancellationToken);

        var wanted = chapters.Intersect(numbered).ToHashSet();
        if (wanted.Count == 0)
        {
            return 0;
        }

        var rows = await db.VerseReferences
            .Where(r => r.Verse!.TextId == textId && r.CanonicalBook == psalms)
            .Select(r => new { r.VerseId, r.CanonicalChapter, r.CanonicalVerse, r.IsPrimary })
            .ToListAsync(cancellationToken);

        var covered = rows.Where(r => r.CanonicalVerse == CanonicalReference.TitleVerse)
            .Select(r => r.CanonicalChapter)
            .ToHashSet();

        var placed = 0;
        foreach (var row in rows.Where(r =>
                     r.IsPrimary && r.CanonicalVerse == FirstVerse && wanted.Contains(r.CanonicalChapter)
                     && !covered.Contains(r.CanonicalChapter)))
        {
            db.VerseReferences.Add(new VerseReference
            {
                VerseId = row.VerseId,
                CanonicalBook = psalms,
                CanonicalChapter = row.CanonicalChapter,
                CanonicalVerse = CanonicalReference.TitleVerse,
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

    /// <summary>
    /// Says on the text's own row that some of its words came from somewhere else. RUL-0181 asks
    /// for it whatever the licence says, and Ohienko's licence asks for it in as many words: CC
    /// BY-SA 4.0 requires that a modification be indicated by whoever passes the text on.
    /// </summary>
    private async Task Attribute(Text text, string note, CancellationToken cancellationToken)
    {
        if (note.Length == 0 || text.RightsNote?.Contains(note, StringComparison.Ordinal) == true)
        {
            return;
        }

        text.RightsNote = text.RightsNote is { Length: > 0 } existing ? $"{existing} {note}" : note;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task Execute(string sql, int verseId, int? written, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(sql, connection,
            (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
        command.Parameters.AddWithValue("verseId", verseId);
        if (written is { } count)
        {
            command.Parameters.AddWithValue("written", count);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
