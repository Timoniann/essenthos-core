using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading;

internal sealed record ChapterNoteOutcome(string Slug, bool AlreadyLoaded, int Chapters, int Notes)
{
    public override string ToString() => AlreadyLoaded
        ? $"{Slug} already has the notes on its chapter headings"
        : $"{Slug}: {Notes} notes on {Chapters} chapter headings";
}

/// <summary>
/// Adds the notes an edition printed on its chapters' own headings to a corpus already holding the
/// text, beside <see cref="BookNoteLoader"/> and for the same reason: an established corpus skips
/// whole texts on a load, and a layer written only with the text would never reach it.
///
/// Guarded chapter by chapter, so a chapter that already carries its notes is left as it is and a
/// second run writes nothing.
/// </summary>
internal sealed class ChapterNoteLoader(AppDbContext db, ILogger<ChapterNoteLoader> logger)
{
    public async Task<ChapterNoteOutcome> Load(TextSource source, CancellationToken cancellationToken = default)
    {
        var noted = source.Books
            .SelectMany(book => book.Chapters
                .Where(chapter => chapter.Notes.Count > 0)
                .Select(chapter => (book.CanonicalOrdinal, Chapter: chapter)))
            .ToList();
        if (noted.Count == 0)
        {
            return new ChapterNoteOutcome(source.Definition.Slug, AlreadyLoaded: false, 0, 0);
        }

        var text = await db.Texts.FirstOrDefaultAsync(t => t.Slug == source.Definition.Slug, cancellationToken)
                   ?? throw new InvalidOperationException(
                       $"The text \"{source.Definition.Slug}\" carries notes on its chapter headings but is not " +
                       "loaded. Run this after the corpus loader, which writes the chapters they belong to.");

        var books = noted.Select(entry => entry.CanonicalOrdinal).Distinct().ToList();
        var loaded = await db.Chapters
            .Where(chapter => chapter.TextId == text.Id && books.Contains(chapter.Book!.CanonicalOrdinal))
            .Select(chapter => new
            {
                chapter.Id,
                chapter.Book!.CanonicalOrdinal,
                chapter.Number,
                Noted = db.ChapterNotes.Any(note => note.ChapterId == chapter.Id),
            })
            .ToDictionaryAsync(chapter => (chapter.CanonicalOrdinal, chapter.Number), cancellationToken);

        var chapters = 0;
        var written = 0;
        foreach (var (ordinal, draft) in noted)
        {
            if (!loaded.TryGetValue((ordinal, draft.Number), out var chapter))
            {
                throw new InvalidOperationException(
                    $"{text.Slug} has notes on the heading of chapter {draft.Number} of book {ordinal}, but the " +
                    "loaded text has no such chapter. The notes and the scripture were read from different editions.");
            }

            if (chapter.Noted)
            {
                continue;
            }

            for (var position = 0; position < draft.Notes.Count; position++)
            {
                db.ChapterNotes.Add(new ChapterNote
                {
                    ChapterId = chapter.Id,
                    Position = position + 1,
                    Heading = draft.Notes[position].Heading,
                    Kind = draft.Notes[position].Kind,
                    Content = draft.Notes[position].Content,
                });
                written++;
            }

            chapters++;
        }

        if (written == 0)
        {
            return new ChapterNoteOutcome(text.Slug, AlreadyLoaded: true, 0, 0);
        }

        await db.SaveChangesAsync(cancellationToken);
        var outcome = new ChapterNoteOutcome(text.Slug, AlreadyLoaded: false, chapters, written);
        logger.LogInformation("Loaded {Outcome}", outcome);
        return outcome;
    }
}
