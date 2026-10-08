using Essenthos.Core.Accounts;
using Essenthos.Core.Corpus;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// What changed in the signed-in reader's account since a revision, for a second client to catch up from.
///
///     GET /v1/me/changes?since=0&amp;take=500
///
/// Every row a device synchronises — the profile, devices, bookmarks, chapter bookmarks, favourite
/// texts — carries a revision from one sequence, so the changes are those with a higher revision than
/// the one the client holds, oldest first. A row the reader removed is a tombstone: it comes back with
/// <c>deletedAt</c> set and without its content, which is the only way a client learns it is gone.
///
/// The answer is a page. <c>revision</c> is the last revision in it, or the one asked about when nothing
/// changed, and is what the client stores and sends next; <c>more</c> says there are further changes
/// beyond the page, to be asked for straight away from that revision. The limit counts changes across
/// every collection, and revisions come from one sequence and are never reused, so following the cursor
/// misses nothing and repeats nothing.
///
/// Nothing in it addresses the corpus by a row id: a passage is its book, chapter and verse.
/// </summary>
internal static class ChangeFeedEndpoints
{
    internal const int DefaultTake = 500;

    internal const int MostTake = 1000;

    public static void MapChanges(this IEndpointRouteBuilder routes) =>
        routes.MapGroup("/me").RequireAuthorization().MapGet("/changes", async (HttpContext context, AccountsDbContext db, long? since, int? take) =>
        {
            var after = since ?? 0;
            if (after < 0)
            {
                return Results.BadRequest(new ProblemResponse("A revision is 0, for everything, or the one the last answer gave."));
            }

            var account = context.User.AccountId();
            var feed = await Read(db, account, after, Math.Clamp(take ?? DefaultTake, 1, MostTake), context.RequestAborted);
            return Results.Ok(feed);
        });

    internal static async Task<ChangesResponse> Read(AccountsDbContext db, Guid account, long since, int take, CancellationToken cancellationToken)
    {
        // Each collection gives its oldest changes past the revision, one more than a page can use. The
        // revisions are one sequence, so merging them in order and cutting the page is exact.
        var profile = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == account && a.Revision > since)
            .Select(a => new ProfileChange(a.Revision, a.DisplayName, a.About, a.Locale, a.PhotoVersion))
            .FirstOrDefaultAsync(cancellationToken);

        var bookmarks = await db.Bookmarks.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.AccountId == account && b.Revision > since)
            .OrderBy(b => b.Revision).Take(take + 1)
            .ToListAsync(cancellationToken);
        var chapters = await db.ChapterBookmarks.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.AccountId == account && b.Revision > since)
            .OrderBy(b => b.Revision).Take(take + 1)
            .ToListAsync(cancellationToken);
        var favorites = await db.FavoriteTexts.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.AccountId == account && f.Revision > since)
            .OrderBy(f => f.Revision).Take(take + 1)
            .ToListAsync(cancellationToken);
        var devices = await db.Devices.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.AccountId == account && d.Revision > since)
            .OrderBy(d => d.Revision).Take(take + 1)
            .ToListAsync(cancellationToken);

        var all = new List<(long Revision, object Change)>();
        if (profile is not null)
        {
            all.Add((profile.Revision, profile));
        }

        all.AddRange(bookmarks.Select(b => (b.Revision, (object)Describe(b))));
        all.AddRange(chapters.Select(b => (b.Revision, (object)Describe(b))));
        all.AddRange(favorites.Select(f => (f.Revision, (object)Describe(f))));
        all.AddRange(devices.Select(d => (d.Revision, (object)Describe(d))));

        var page = all.OrderBy(c => c.Revision).Take(take).ToList();
        return new ChangesResponse(
            page.Count == 0 ? since : page[^1].Revision,
            all.Count > take,
            page.Select(c => c.Change).OfType<ProfileChange>().FirstOrDefault(),
            [.. page.Select(c => c.Change).OfType<BookmarkChange>()],
            [.. page.Select(c => c.Change).OfType<ChapterBookmarkChange>()],
            [.. page.Select(c => c.Change).OfType<FavoriteTextChange>()],
            [.. page.Select(c => c.Change).OfType<DeviceChange>()]);
    }

    private static BookmarkChange Describe(Bookmark bookmark) => new(
        bookmark.Id,
        bookmark.Revision,
        bookmark.DeletedAt,
        bookmark.DeletedAt is null
            ? new BookmarkResponse(
                bookmark.Id, BookReferences.Slug(bookmark.Book), bookmark.Chapter, bookmark.Verse, bookmark.EndChapter,
                bookmark.EndVerse, bookmark.Text, bookmark.Color, bookmark.Comment, bookmark.CreatedAt, bookmark.UpdatedAt)
            : null);

    private static ChapterBookmarkChange Describe(ChapterBookmark bookmark) => new(
        BookReferences.Slug(bookmark.Book),
        bookmark.Chapter,
        bookmark.Revision,
        bookmark.DeletedAt,
        bookmark.DeletedAt is null
            ? new ChapterBookmarkResponse(
                BookReferences.Slug(bookmark.Book), bookmark.Chapter, bookmark.Verse, bookmark.Texts, bookmark.Color,
                bookmark.Position, bookmark.CreatedAt, bookmark.UpdatedAt)
            : null);

    private static FavoriteTextChange Describe(FavoriteText favorite) => new(
        favorite.Text,
        favorite.Revision,
        favorite.DeletedAt,
        favorite.DeletedAt is null
            ? new FavoriteTextResponse(favorite.Text, favorite.Position, favorite.CreatedAt, favorite.UpdatedAt)
            : null);

    private static DeviceChange Describe(Device device) => new(
        device.Id,
        device.Revision,
        device.DeletedAt,
        device.DeletedAt is null
            ? new DeviceDetail(device.Kind, device.Os, device.Browser, device.Model, device.SignedOutAt)
            : null);
}

/// <param name="Revision">The last revision in this answer, or the one asked about when nothing changed. Store it and send it next.</param>
/// <param name="More">Whether the answer stopped at the page's limit and there are further changes to ask for from <paramref name="Revision"/>.</param>
/// <param name="Profile">The account's own name, about line and language, when they changed.</param>
internal record ChangesResponse(
    long Revision,
    bool More,
    ProfileChange? Profile,
    IReadOnlyList<BookmarkChange> Bookmarks,
    IReadOnlyList<ChapterBookmarkChange> ChapterBookmarks,
    IReadOnlyList<FavoriteTextChange> FavoriteTexts,
    IReadOnlyList<DeviceChange> Devices);

internal record ProfileChange(long Revision, string DisplayName, string? About, string? Locale, int PhotoVersion);

/// <param name="DeletedAt">When the reader removed it; then <paramref name="Bookmark"/> is null.</param>
internal record BookmarkChange(Guid Id, long Revision, DateTimeOffset? DeletedAt, BookmarkResponse? Bookmark);

/// <param name="Book">With <paramref name="Chapter"/>, what names a chapter bookmark: there is one per chapter.</param>
internal record ChapterBookmarkChange(
    string Book,
    int Chapter,
    long Revision,
    DateTimeOffset? DeletedAt,
    ChapterBookmarkResponse? ChapterBookmark);

/// <param name="Text">What names a favourite: the text's slug.</param>
internal record FavoriteTextChange(string Text, long Revision, DateTimeOffset? DeletedAt, FavoriteTextResponse? Favorite);

internal record DeviceChange(Guid Id, long Revision, DateTimeOffset? DeletedAt, DeviceDetail? Device);

/// <summary>What a device is called to its reader. Its settings and its history are not in the feed.</summary>
internal record DeviceDetail(string Kind, string Os, string Browser, string? Model, DateTimeOffset? SignedOutAt);
