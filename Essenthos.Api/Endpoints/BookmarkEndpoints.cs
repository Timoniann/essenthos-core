using Essenthos.Core.Accounts;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// A reader's bookmarks on passages, each in a colour and with a comment to themselves if they
/// want one.
///
///     GET    /v1/me/bookmarks                          every bookmark, newest first
///     GET    /v1/me/bookmarks?book=john&amp;chapter=3      the bookmarks that touch one chapter — asked on every page
///     POST   /v1/me/bookmarks                          a new bookmark on a verse or a run of verses
///     PATCH  /v1/me/bookmarks/{id}                     its colour, its comment, or which wording it marks
///     DELETE /v1/me/bookmarks/{id}
///
/// A bookmark is addressed canonically — book, chapter, verse in the shared frame — and never by a
/// corpus row id, which every rebuild renumbers, so a corpus release cannot move it. The address is
/// checked against the corpus when the bookmark is made, so one can only be put on a verse that
/// exists. An account keeps <see cref="Limits.BookmarksPerAccount"/> bookmarks, makes
/// <see cref="Limits.BookmarksPerDay"/> a day, and its comments hold
/// <see cref="Limits.BookmarkCommentsPerAccount"/> characters between them.
/// </summary>
internal static class BookmarkEndpoints
{
    public static void MapBookmarks(this IEndpointRouteBuilder routes)
    {
        var bookmarks = routes.MapGroup("/me/bookmarks").RequireAuthorization();

        bookmarks.MapGet("", async (HttpContext context, AccountsDbContext db, string? book, int? chapter) =>
        {
            var account = context.User.AccountId();
            var query = db.Bookmarks.Where(b => b.AccountId == account);

            if (book is not null || chapter is not null)
            {
                if (book is null || chapter is not { } number || BookReferences.ResolveOrdinal(book) is not { } ordinal)
                {
                    return Results.BadRequest(new ProblemResponse(
                        "Ask for one chapter with both book and chapter — book=john&chapter=3 — or for every bookmark with neither."));
                }

                query = query.Where(b => b.Book == ordinal && b.Chapter <= number && b.EndChapter >= number);
            }

            var found = await query
                .OrderByDescending(b => b.UpdatedAt)
                .ToListAsync(context.RequestAborted);
            return Results.Ok(new BookmarksResponse(found.Select(Describe).ToList()));
        });

        bookmarks.MapPost("", async (HttpContext context, AccountsDbContext db, AppDbContext corpus, ICanonIndex canon, BookmarkCreate create) =>
        {
            if (Color(create.Color) is not { } color)
            {
                return Results.BadRequest(new ProblemResponse(ColorRule));
            }

            if (!Comment(create.Comment, out var comment))
            {
                return Results.BadRequest(new ProblemResponse(CommentRule));
            }

            var (anchor, problem) = await Anchor(corpus, canon, create.Book, create.Chapter, create.Verse,
                create.EndChapter, create.EndVerse, create.Text, context.RequestAborted);
            if (anchor is null)
            {
                return Results.BadRequest(new ProblemResponse(problem!));
            }

            var account = context.User.AccountId();
            if (await Refusal(db, account, null, comment?.Length ?? 0, DateTimeOffset.UtcNow, context.RequestAborted) is { } refused)
            {
                return Refuse(refused);
            }

            var bookmark = new Bookmark
            {
                Id = Guid.CreateVersion7(),
                AccountId = account,
                Text = anchor.Text,
                Book = anchor.Book,
                Chapter = anchor.Chapter,
                Verse = anchor.Verse,
                EndChapter = anchor.EndChapter,
                EndVerse = anchor.EndVerse,
                Color = color,
                Comment = comment,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Bookmarks.Add(bookmark);
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(Describe(bookmark));
        });

        bookmarks.MapPatch("/{id:guid}", async (Guid id, HttpContext context, AccountsDbContext db, AppDbContext corpus, ICanonIndex canon, BookmarkUpdate update) =>
        {
            var account = context.User.AccountId();
            if (await db.Bookmarks.FirstOrDefaultAsync(b => b.Id == id && b.AccountId == account, context.RequestAborted) is not { } bookmark)
            {
                return Results.NotFound(new ProblemResponse("There is no such bookmark."));
            }

            if (update.Color is not null)
            {
                if (Color(update.Color) is not { } color)
                {
                    return Results.BadRequest(new ProblemResponse(ColorRule));
                }

                bookmark.Color = color;
            }

            // An empty comment removes it: a bookmark is still a bookmark without one.
            if (update.Comment is not null)
            {
                if (!Comment(update.Comment, out var comment))
                {
                    return Results.BadRequest(new ProblemResponse(CommentRule));
                }

                if ((comment?.Length ?? 0) > (bookmark.Comment?.Length ?? 0) &&
                    await Refusal(db, account, bookmark.Id, comment!.Length, null, context.RequestAborted) is { } refused)
                {
                    return Refuse(refused);
                }

                bookmark.Comment = comment;
            }

            // The wording a bookmark marks can change, the passage cannot: a bookmark moved to another
            // passage is a different bookmark, and the reader makes it there.
            if (update.Text is not null)
            {
                var (anchor, problem) = await Anchor(corpus, canon,
                    bookmark.Book.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    bookmark.Chapter, bookmark.Verse, bookmark.EndChapter, bookmark.EndVerse,
                    update.Text.Length == 0 ? null : update.Text, context.RequestAborted);
                if (anchor is null)
                {
                    return Results.BadRequest(new ProblemResponse(problem!));
                }

                bookmark.Text = anchor.Text;
            }

            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(Describe(bookmark));
        });

        bookmarks.MapDelete("/{id:guid}", async (Guid id, HttpContext context, AccountsDbContext db) =>
        {
            var account = context.User.AccountId();
            if (await db.Bookmarks.FirstOrDefaultAsync(b => b.Id == id && b.AccountId == account, context.RequestAborted) is not { } bookmark)
            {
                return Results.NotFound(new ProblemResponse("There is no such bookmark."));
            }

            db.Bookmarks.Remove(bookmark);
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.NoContent();
        });
    }

    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    /// <summary>Why an account cannot take the bookmark or the comment asked for, and with what status.</summary>
    internal sealed record Refused(int Status, string Problem);

    /// <summary>
    /// Whether the account has room: for a new bookmark (<paramref name="made"/> is its time), under the
    /// count it keeps and the count it may make in a day; for a comment of <paramref name="comment"/>
    /// characters, under the characters its comments hold, not counting the one it replaces. A full
    /// account is a conflict with what it holds; a busy day is too many requests, to be tried tomorrow.
    /// </summary>
    internal static async Task<Refused?> Refusal(
        AccountsDbContext db,
        Guid account,
        Guid? replacing,
        int comment,
        DateTimeOffset? made,
        CancellationToken cancellationToken)
    {
        var mine = db.Bookmarks.Where(b => b.AccountId == account);
        if (made is { } now)
        {
            if (await mine.CountAsync(cancellationToken) >= Limits.BookmarksPerAccount)
            {
                return new Refused(StatusCodes.Status409Conflict,
                    $"An account keeps at most {Limits.BookmarksPerAccount:N0} bookmarks. Remove some to make room.");
            }

            if (await mine.CountAsync(b => b.CreatedAt > now - Day, cancellationToken) >= Limits.BookmarksPerDay)
            {
                return new Refused(StatusCodes.Status429TooManyRequests,
                    $"An account makes at most {Limits.BookmarksPerDay:N0} bookmarks a day.");
            }
        }

        if (comment > 0)
        {
            var others = await mine
                .Where(b => b.Id != replacing && b.Comment != null)
                .SumAsync(b => (long)b.Comment!.Length, cancellationToken);
            if (others + comment > Limits.BookmarkCommentsPerAccount)
            {
                return new Refused(StatusCodes.Status409Conflict,
                    $"An account's comments hold at most {Limits.BookmarkCommentsPerAccount:N0} characters between them. Shorten or remove some to make room.");
            }
        }

        return null;
    }

    private static IResult Refuse(Refused refused) =>
        Results.Json(new ProblemResponse(refused.Problem), statusCode: refused.Status);

    private static readonly string ColorRule = $"A bookmark is one of {string.Join(", ", Bookmark.Colors)}.";

    private const string CommentRule = "A comment is at most 20,000 characters.";

    /// <summary>The colour asked for, the first of the palette when none is, or null for one that is not in it.</summary>
    internal static string? Color(string? color) =>
        color is null ? Bookmark.Colors[0] : Bookmark.Colors.FirstOrDefault(c => c == color);

    /// <summary>
    /// The comment with its ends trimmed and its line endings made one kind — null when nothing was
    /// written — or false when it is too long to be one.
    /// </summary>
    internal static bool Comment(string? comment, out string? cleaned)
    {
        cleaned = comment?.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (cleaned is { Length: 0 })
        {
            cleaned = null;
        }

        return cleaned is null || cleaned.Length <= Limits.BookmarkComment;
    }

    internal sealed record BookmarkAnchor(int Book, int Chapter, int Verse, int EndChapter, int EndVerse, string? Text);

    /// <summary>
    /// The canonical address the request names, if it names a passage the corpus has: a start that is
    /// not after the end, both ends inside one book, both verses real — in the text named, when a text
    /// is named, and in some text otherwise.
    /// </summary>
    private static async Task<(BookmarkAnchor? Anchor, string? Problem)> Anchor(
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

        return (new BookmarkAnchor(ordinal, chapter, verse, lastChapter, lastVerse, slug), null);
    }

    internal static bool Ordered(int chapter, int verse, int endChapter, int endVerse) =>
        chapter > 0 && verse > 0 && (endChapter > chapter || (endChapter == chapter && endVerse >= verse));

    private static BookmarkResponse Describe(Bookmark bookmark) => new(
        bookmark.Id,
        BookReferences.Slug(bookmark.Book),
        bookmark.Chapter,
        bookmark.Verse,
        bookmark.EndChapter,
        bookmark.EndVerse,
        bookmark.Text,
        bookmark.Color,
        bookmark.Comment,
        bookmark.CreatedAt,
        bookmark.UpdatedAt);
}

internal record BookmarkCreate(
    string? Book,
    int Chapter,
    int Verse,
    int? EndChapter,
    int? EndVerse,
    string? Text,
    string? Color,
    string? Comment);

/// <summary>
/// Any of them. An empty <c>Comment</c> removes the comment; an empty <c>Text</c> makes the bookmark mark
/// the passage rather than one wording.
/// </summary>
internal record BookmarkUpdate(string? Color, string? Comment, string? Text);

internal record BookmarkResponse(
    Guid Id,
    string Book,
    int Chapter,
    int Verse,
    int EndChapter,
    int EndVerse,
    string? Text,
    string Color,
    string? Comment,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal record BookmarksResponse(IReadOnlyList<BookmarkResponse> Items);
