using Essenthos.Core.Accounts;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The places a reader is reading: whole chapters, each remembering the verse they were on and the
/// texts they had open, in an order of their own.
///
///     GET    /v1/me/chapter-bookmarks                   every one, in the reader's order
///     PUT    /v1/me/chapter-bookmarks/{book}/{chapter}  makes it, or changes what is given of it
///     DELETE /v1/me/chapter-bookmarks/{book}/{chapter}
///     PUT    /v1/me/chapter-bookmarks/order             puts the ones named first, in that order
///
/// One per chapter per account, addressed canonically and never by a corpus row id. The chapter is
/// checked against the corpus when the bookmark is made and not on every change after, which is
/// what the reader sends as they move through it. An account keeps
/// <see cref="Limits.ChapterBookmarksPerAccount"/> of them.
/// </summary>
internal static class ChapterBookmarkEndpoints
{
    /// <summary>More verses than the longest chapter has: Psalm 119 has 176.</summary>
    internal const int LastVerse = 200;

    public static void MapChapterBookmarks(this IEndpointRouteBuilder routes)
    {
        var bookmarks = routes.MapGroup("/me/chapter-bookmarks").RequireAuthorization();

        bookmarks.MapGet("", async (HttpContext context, AccountsDbContext db) =>
            Results.Ok(await List(db, context.User.AccountId(), context.RequestAborted)));

        bookmarks.MapPut("/{book}/{chapter:int}", async (string book, int chapter, HttpContext context, AccountsDbContext db,
            AppDbContext corpus, ICanonIndex canon, ChapterBookmarkPut put) =>
        {
            if (BookReferences.ResolveOrdinal(book) is not { } ordinal)
            {
                return Results.BadRequest(new ProblemResponse($"\"{book}\" is not a book."));
            }

            if (!Verse(put.Verse))
            {
                return Results.BadRequest(new ProblemResponse($"A verse is a number from 1 to {LastVerse}, or null when it is not known."));
            }

            if (put.Position is < 0)
            {
                return Results.BadRequest(new ProblemResponse("A position is 0 or more."));
            }

            if (put.Color is not null && BookmarkEndpoints.Color(put.Color) is null)
            {
                return Results.BadRequest(new ProblemResponse(ColorRule));
            }

            List<string>? texts = null;
            if (put.Texts is not null)
            {
                var (resolved, problem) = await Texts(canon, put.Texts, context.RequestAborted);
                if (resolved is null)
                {
                    return Results.BadRequest(new ProblemResponse(problem!));
                }

                texts = resolved;
            }

            var account = context.User.AccountId();
            var bookmark = await db.ChapterBookmarks.FirstOrDefaultAsync(b =>
                b.AccountId == account && b.Book == ordinal && b.Chapter == chapter, context.RequestAborted);

            if (bookmark is null)
            {
                if (chapter < 1 || !await corpus.VerseReferences.AnyAsync(r =>
                        r.CanonicalBook == ordinal && r.CanonicalChapter == chapter, context.RequestAborted))
                {
                    return Results.BadRequest(new ProblemResponse($"{BookReferences.Name(ordinal)} has no chapter {chapter}."));
                }

                var mine = db.ChapterBookmarks.Where(b => b.AccountId == account);
                if (await mine.CountAsync(context.RequestAborted) >= Limits.ChapterBookmarksPerAccount)
                {
                    return Results.Conflict(new ProblemResponse(
                        $"An account keeps at most {Limits.ChapterBookmarksPerAccount:N0} chapter bookmarks. Remove some to make room."));
                }

                bookmark = new ChapterBookmark
                {
                    Id = Guid.CreateVersion7(),
                    AccountId = account,
                    Book = ordinal,
                    Chapter = chapter,
                    Color = put.Color ?? Bookmark.Colors[0],
                    Position = put.Position ?? (await mine.MaxAsync(b => (int?)b.Position, context.RequestAborted) ?? -1) + 1,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.ChapterBookmarks.Add(bookmark);
            }
            else
            {
                bookmark.Color = put.Color ?? bookmark.Color;
                bookmark.Position = put.Position ?? bookmark.Position;
            }

            bookmark.Verse = put.Verse ?? bookmark.Verse;
            bookmark.Texts = texts ?? bookmark.Texts;

            try
            {
                await db.SaveChangesAsync(context.RequestAborted);
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Results.Conflict(new ProblemResponse("This chapter was bookmarked by another request at the same moment. Send it again."));
            }

            return Results.Ok(Describe(bookmark));
        });

        bookmarks.MapDelete("/{book}/{chapter:int}", async (string book, int chapter, HttpContext context, AccountsDbContext db) =>
        {
            if (BookReferences.ResolveOrdinal(book) is not { } ordinal)
            {
                return Results.BadRequest(new ProblemResponse($"\"{book}\" is not a book."));
            }

            var account = context.User.AccountId();
            if (await db.ChapterBookmarks.FirstOrDefaultAsync(
                    b => b.AccountId == account && b.Book == ordinal && b.Chapter == chapter, context.RequestAborted) is not { } bookmark)
            {
                return Results.NotFound(new ProblemResponse("There is no such bookmark."));
            }

            db.ChapterBookmarks.Remove(bookmark);
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.NoContent();
        });

        bookmarks.MapPut("/order", async (HttpContext context, AccountsDbContext db, ChapterBookmarkOrder order) =>
        {
            var account = context.User.AccountId();
            var mine = await db.ChapterBookmarks
                .Where(b => b.AccountId == account)
                .OrderBy(b => b.Position).ThenBy(b => b.CreatedAt)
                .ToListAsync(context.RequestAborted);

            var named = (order.Items ?? [])
                .Where(i => i is not null)
                .Select(i => BookReferences.ResolveOrdinal(i.Book) is { } ordinal ? (ordinal, i.Chapter) : ((int, int)?)null)
                .OfType<(int, int)>();
            Reorder(mine, named);

            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(new ChapterBookmarksResponse(mine.OrderBy(b => b.Position).Select(Describe).ToList()));
        });
    }

    private static readonly string ColorRule = $"A bookmark is one of {string.Join(", ", Bookmark.Colors)}.";

    private static async Task<ChapterBookmarksResponse> List(AccountsDbContext db, Guid account, CancellationToken cancellationToken)
    {
        var found = await db.ChapterBookmarks
            .Where(b => b.AccountId == account)
            .OrderBy(b => b.Position).ThenBy(b => b.CreatedAt)
            .ToListAsync(cancellationToken);
        return new ChapterBookmarksResponse(found.Select(Describe).ToList());
    }

    /// <summary>Whether a verse can be the one the reader was on: none, or one a chapter could have.</summary>
    internal static bool Verse(int? verse) => verse is null or (> 0 and <= LastVerse);

    /// <summary>
    /// The texts asked for, each once, in the order first asked — or null when there are more than a
    /// bookmark remembers. Spellings that differ only in case are the same text.
    /// </summary>
    internal static List<string>? Unique(IEnumerable<string?> texts)
    {
        var unique = texts
            .Select(t => t?.Trim() ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return unique.Count <= Limits.ChapterBookmarkTexts ? unique : null;
    }

    /// <summary>The texts asked for, as the canonical slugs of texts there are, or what is wrong with them.</summary>
    private static async Task<(List<string>? Texts, string? Problem)> Texts(
        ICanonIndex canon,
        IEnumerable<string?> texts,
        CancellationToken cancellationToken)
    {
        if (Unique(texts) is not { } asked)
        {
            return (null, $"A chapter bookmark remembers at most {Limits.ChapterBookmarkTexts} texts.");
        }

        var slugs = new List<string>();
        foreach (var text in asked)
        {
            if (await canon.Text(text, cancellationToken) is not { } entry)
            {
                return (null, $"There is no text \"{text}\".");
            }

            // Two aliases of one text are one pane.
            if (!slugs.Contains(entry.Slug))
            {
                slugs.Add(entry.Slug);
            }
        }

        return (slugs, null);
    }

    /// <summary>
    /// Numbers the bookmarks from 0: those named first, in the order named, then the rest in the order
    /// they were in. <paramref name="bookmarks"/> is expected in its current order; a chapter named
    /// that has no bookmark, or named twice, is passed over.
    /// </summary>
    internal static void Reorder(IReadOnlyList<ChapterBookmark> bookmarks, IEnumerable<(int Book, int Chapter)> named)
    {
        var byChapter = bookmarks.ToDictionary(b => (b.Book, b.Chapter));
        var ordered = new List<ChapterBookmark>();
        var placed = new HashSet<ChapterBookmark>();
        foreach (var chapter in named)
        {
            if (byChapter.TryGetValue(chapter, out var bookmark) && placed.Add(bookmark))
            {
                ordered.Add(bookmark);
            }
        }

        ordered.AddRange(bookmarks.Where(b => !placed.Contains(b)));
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Position != i)
            {
                ordered[i].Position = i;
            }
        }
    }

    private static ChapterBookmarkResponse Describe(ChapterBookmark bookmark) => new(
        BookReferences.Slug(bookmark.Book),
        bookmark.Chapter,
        bookmark.Verse,
        bookmark.Texts,
        bookmark.Color,
        bookmark.Position,
        bookmark.CreatedAt,
        bookmark.UpdatedAt);
}

/// <summary>
/// Any of them; what is null is left as it is. An empty <c>Texts</c> forgets which texts were open.
/// </summary>
internal record ChapterBookmarkPut(int? Verse, IReadOnlyList<string?>? Texts, string? Color, int? Position);

internal record ChapterRef(string Book, int Chapter);

internal record ChapterBookmarkOrder(IReadOnlyList<ChapterRef>? Items);

internal record ChapterBookmarkResponse(
    string Book,
    int Chapter,
    int? Verse,
    IReadOnlyList<string> Texts,
    string Color,
    int Position,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal record ChapterBookmarksResponse(IReadOnlyList<ChapterBookmarkResponse> Items);
