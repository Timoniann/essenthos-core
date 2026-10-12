using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading;

internal sealed record BookNoteOutcome(string Slug, bool AlreadyLoaded, int Books, int Notes)
{
    public override string ToString() => AlreadyLoaded
        ? $"{Slug} already has the notes on its book titles"
        : $"{Slug}: {Notes} notes on {Books} book titles";
}

/// <summary>
/// Adds the notes an edition printed on its books' titles to a corpus already holding the text. A
/// pass of its own beside <see cref="SourceNoteLoader"/>, for the same reason: an established corpus
/// skips whole texts on a load, and a layer written only with the text would never reach it.
///
/// Guarded book by book, so a book that already carries its notes is left as it is and a second
/// run writes nothing.
/// </summary>
internal sealed class BookNoteLoader(AppDbContext db, ILogger<BookNoteLoader> logger)
{
    public async Task<BookNoteOutcome> Load(TextSource source, CancellationToken cancellationToken = default)
    {
        var noted = source.Books.Where(book => book.Notes.Count > 0).ToList();
        if (noted.Count == 0)
        {
            return new BookNoteOutcome(source.Definition.Slug, AlreadyLoaded: false, 0, 0);
        }

        var text = await db.Texts.FirstOrDefaultAsync(t => t.Slug == source.Definition.Slug, cancellationToken)
                   ?? throw new InvalidOperationException(
                       $"The text \"{source.Definition.Slug}\" carries notes on its book titles but is not " +
                       "loaded. Run this after the corpus loader, which writes the books they belong to.");

        var loaded = await db.Books
            .Where(book => book.TextId == text.Id)
            .Select(book => new
            {
                book.Id,
                book.CanonicalOrdinal,
                Noted = db.BookNotes.Any(note => note.BookId == book.Id),
            })
            .ToDictionaryAsync(book => book.CanonicalOrdinal, cancellationToken);

        var books = 0;
        var written = 0;
        foreach (var draft in noted)
        {
            if (!loaded.TryGetValue(draft.CanonicalOrdinal, out var book))
            {
                throw new InvalidOperationException(
                    $"{text.Slug} has notes on the title of book {draft.CanonicalOrdinal}, but the loaded text " +
                    "has no such book. The notes and the scripture were read from different editions.");
            }

            if (book.Noted)
            {
                continue;
            }

            for (var position = 0; position < draft.Notes.Count; position++)
            {
                db.BookNotes.Add(new BookNote
                {
                    BookId = book.Id,
                    Position = position + 1,
                    Kind = draft.Notes[position].Kind,
                    Content = draft.Notes[position].Content,
                });
                written++;
            }

            books++;
        }

        if (written == 0)
        {
            return new BookNoteOutcome(text.Slug, AlreadyLoaded: true, 0, 0);
        }

        await db.SaveChangesAsync(cancellationToken);
        var outcome = new BookNoteOutcome(text.Slug, AlreadyLoaded: false, books, written);
        logger.LogInformation("Loaded {Outcome}", outcome);
        return outcome;
    }
}
