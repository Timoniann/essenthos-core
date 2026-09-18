using Essenthos.Core.Accounts;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// A reader's notes on passages (FTR-0581).
///
///     GET    /v1/me/notes                          every note, newest first
///     GET    /v1/me/notes?book=john&amp;chapter=3      the notes that touch one chapter — what the reader asks on every page
///     POST   /v1/me/notes                          a new note on a verse or a run of verses
///     PATCH  /v1/me/notes/{id}                     its text, or which wording it is about
///     DELETE /v1/me/notes/{id}
///
/// A note is addressed canonically — book, chapter, verse in the shared frame — and never by a corpus
/// row id (RUL-0185), so a corpus release cannot move it. The address is checked against the corpus
/// when the note is written, so a note can only be put on a verse that exists.
/// </summary>
internal static class NoteEndpoints
{
    public static void MapNotes(this IEndpointRouteBuilder routes)
    {
        var notes = routes.MapGroup("/me/notes").RequireAuthorization();

        notes.MapGet("", async (HttpContext context, AccountsDbContext db, string? book, int? chapter) =>
        {
            var account = context.User.AccountId();
            var query = db.Notes.Where(n => n.AccountId == account);

            if (book is not null || chapter is not null)
            {
                if (book is null || chapter is not { } number || BookReferences.ResolveOrdinal(book) is not { } ordinal)
                {
                    return Results.BadRequest(new ProblemResponse(
                        "Ask for one chapter with both book and chapter — book=john&chapter=3 — or for every note with neither."));
                }

                query = query.Where(n => n.Book == ordinal && n.Chapter <= number && n.EndChapter >= number);
            }

            var found = await query
                .OrderByDescending(n => n.UpdatedAt)
                .ToListAsync(context.RequestAborted);
            return Results.Ok(new NotesResponse(found.Select(Describe).ToList()));
        });

        notes.MapPost("", async (HttpContext context, AccountsDbContext db, AppDbContext corpus, ICanonIndex canon, NoteCreate create) =>
        {
            if (Body(create.Body) is not { } body)
            {
                return Results.BadRequest(new ProblemResponse(BodyRule));
            }

            var (anchor, problem) = await Anchor(corpus, canon, create.Book, create.Chapter, create.Verse,
                create.EndChapter, create.EndVerse, create.Text, context.RequestAborted);
            if (anchor is null)
            {
                return Results.BadRequest(new ProblemResponse(problem!));
            }

            var account = context.User.AccountId();
            if (await db.Notes.CountAsync(n => n.AccountId == account, context.RequestAborted) >= Limits.NotesPerAccount)
            {
                return Results.BadRequest(new ProblemResponse($"An account keeps at most {Limits.NotesPerAccount:N0} notes."));
            }

            var now = DateTimeOffset.UtcNow;
            var note = new Note
            {
                Id = Guid.CreateVersion7(),
                AccountId = account,
                Text = anchor.Text,
                Book = anchor.Book,
                Chapter = anchor.Chapter,
                Verse = anchor.Verse,
                EndChapter = anchor.EndChapter,
                EndVerse = anchor.EndVerse,
                Body = body,
                CreatedAt = now,
            };
            db.Notes.Add(note);
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(Describe(note));
        });

        notes.MapPatch("/{id:guid}", async (Guid id, HttpContext context, AccountsDbContext db, AppDbContext corpus, ICanonIndex canon, NoteUpdate update) =>
        {
            var account = context.User.AccountId();
            if (await db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.AccountId == account, context.RequestAborted) is not { } note)
            {
                return Results.NotFound(new ProblemResponse("There is no such note."));
            }

            if (update.Body is not null)
            {
                if (Body(update.Body) is not { } body)
                {
                    return Results.BadRequest(new ProblemResponse(BodyRule));
                }

                note.Body = body;
            }

            // The wording a note is about can change, the passage cannot: a note moved to another
            // passage is a different note, and the reader writes it there.
            if (update.Text is not null)
            {
                var (anchor, problem) = await Anchor(corpus, canon, note.Book.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    note.Chapter, note.Verse, note.EndChapter, note.EndVerse,
                    update.Text.Length == 0 ? null : update.Text, context.RequestAborted);
                if (anchor is null)
                {
                    return Results.BadRequest(new ProblemResponse(problem!));
                }

                note.Text = anchor.Text;
            }

            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(Describe(note));
        });

        notes.MapDelete("/{id:guid}", async (Guid id, HttpContext context, AccountsDbContext db) =>
        {
            var account = context.User.AccountId();
            var removed = await db.Notes.Where(n => n.Id == id && n.AccountId == account).ExecuteDeleteAsync(context.RequestAborted);
            return removed == 0 ? Results.NotFound(new ProblemResponse("There is no such note.")) : Results.NoContent();
        });
    }

    private const string BodyRule = "A note has something written in it, and at most 20,000 characters.";

    /// <summary>The body with its ends trimmed and its line endings made one kind, or null if it is not a note.</summary>
    internal static string? Body(string? body)
    {
        var cleaned = body?.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return cleaned is { Length: > 0 and <= Limits.NoteBody } ? cleaned : null;
    }

    internal sealed record NoteAnchor(int Book, int Chapter, int Verse, int EndChapter, int EndVerse, string? Text);

    /// <summary>
    /// The canonical address the request names, if it names a passage the corpus has: a start that is
    /// not after the end, both ends inside one book, both verses real — in the text named, when a text
    /// is named, and in some text otherwise.
    /// </summary>
    private static async Task<(NoteAnchor? Anchor, string? Problem)> Anchor(
        AppDbContext corpus,
        ICanonIndex canon,
        string? book,
        int chapter,
        int verse,
        int? endChapter,
        int? endVerse,
        string? text,
        CancellationToken cancellationToken)
    {
        if (book is null || BookReferences.ResolveOrdinal(book) is not { } ordinal)
        {
            return (null, $"\"{book}\" is not a book.");
        }

        var lastChapter = endChapter ?? chapter;
        var lastVerse = endVerse ?? verse;
        if (!Ordered(chapter, verse, lastChapter, lastVerse))
        {
            return (null, "A passage starts at a verse and ends at the same one or a later one in the same book.");
        }

        int? textId = null;
        string? slug = null;
        if (text is not null)
        {
            if (await canon.Text(text, cancellationToken) is not { } entry)
            {
                return (null, $"There is no text \"{text}\".");
            }

            textId = entry.Id;
            slug = entry.Slug;
        }

        foreach (var (c, v) in new[] { (chapter, verse), (lastChapter, lastVerse) }.Distinct())
        {
            var exists = await corpus.VerseReferences.AnyAsync(r =>
                r.CanonicalBook == ordinal && r.CanonicalChapter == c && r.CanonicalVerse == v &&
                (textId == null || r.Verse!.TextId == textId), cancellationToken);
            if (!exists)
            {
                return (null, slug is null
                    ? $"{BookReferences.Name(ordinal)} {c}:{v} is not a verse."
                    : $"{slug} has no {BookReferences.Name(ordinal)} {c}:{v}.");
            }
        }

        return (new NoteAnchor(ordinal, chapter, verse, lastChapter, lastVerse, slug), null);
    }

    internal static bool Ordered(int chapter, int verse, int endChapter, int endVerse) =>
        chapter > 0 && verse > 0 && (endChapter > chapter || (endChapter == chapter && endVerse >= verse));

    private static NoteResponse Describe(Note note) => new(
        note.Id,
        BookReferences.Slug(note.Book),
        note.Chapter,
        note.Verse,
        note.EndChapter,
        note.EndVerse,
        note.Text,
        note.Body,
        note.CreatedAt,
        note.UpdatedAt);
}

internal record NoteCreate(string? Book, int Chapter, int Verse, int? EndChapter, int? EndVerse, string? Text, string? Body);

/// <summary>Either or both. An empty <c>Text</c> makes the note about the passage rather than one wording.</summary>
internal record NoteUpdate(string? Body, string? Text);

internal record NoteResponse(
    Guid Id,
    string Book,
    int Chapter,
    int Verse,
    int EndChapter,
    int EndVerse,
    string? Text,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal record NotesResponse(IReadOnlyList<NoteResponse> Items);
