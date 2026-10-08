using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Essenthos.Core.Accounts;
using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The change feed a second client catches up from: removed rows kept as tombstones that every read
/// leaves out, and <c>GET /v1/me/changes</c>, against a real accounts database.
/// </summary>
public sealed class ChangeFeedTests : IAsyncLifetime
{
    private static readonly string DatabaseName = $"essenthos_app_change_feed_test_{Environment.ProcessId}";

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnection.ConnectionStringKey] = "Host=localhost;Port=5437;Database=postgres;Username=essenthos",
            })
            .AddUserSecrets(typeof(ChangeFeedTests).Assembly)
            .AddEnvironmentVariables()
            .Build();

        _connectionString = new NpgsqlConnectionStringBuilder(DatabaseConnection.Read(configuration)) { Database = DatabaseName }
            .ConnectionString;

        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_connectionString.Length == 0)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    private AccountsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AccountsDbContext>().UseNpgsql(_connectionString).Options);

    private async Task<Account> NewAccount(string name = "Reader")
    {
        var account = new Account { Id = Guid.CreateVersion7(), DisplayName = name, CreatedAt = DateTimeOffset.UtcNow };
        await using var db = NewContext();
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    private static Bookmark Bookmark(Guid account, string? comment = null) => new()
    {
        Id = Guid.CreateVersion7(), AccountId = account, Book = 43, Chapter = 3, Verse = 16, EndChapter = 3, EndVerse = 16,
        Color = "amber", Comment = comment, CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ChapterBookmark Chapter(Guid account, int number) => new()
    {
        Id = Guid.CreateVersion7(), AccountId = account, Book = 43, Chapter = number, Verse = 5, Texts = ["KJV", "BHSA"],
        Color = "blue", Position = number, CreatedAt = DateTimeOffset.UtcNow,
    };

    private static FavoriteText Favorite(Guid account, string text, int position) => new()
    {
        Id = Guid.CreateVersion7(), AccountId = account, Text = text, Position = position, CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Device Device(Guid account) => new()
    {
        Id = Guid.CreateVersion7(), AccountId = account, Kind = "mobile", Os = "Android", Browser = "Chrome", Model = "Pixel",
        Settings = "{\"font\":3}", SettingsChangedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
        LastSeenAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task ARemovedRowIsKeptAsATombstoneThatClearsWhatTheReaderWroteAndIsStampedAgain()
    {
        var account = await NewAccount();
        var bookmark = Bookmark(account.Id, "a private margin");
        await using (var db = NewContext())
        {
            db.Bookmarks.Add(bookmark);
            await db.SaveChangesAsync();
        }

        long before;
        await using (var db = NewContext())
        {
            var kept = await db.Bookmarks.SingleAsync(b => b.Id == bookmark.Id);
            before = kept.Revision;
            db.Bookmarks.Remove(kept);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            (await db.Bookmarks.Where(b => b.AccountId == account.Id).ToListAsync()).Should().BeEmpty("every read leaves a tombstone out");
            var tombstone = await db.Bookmarks.IgnoreQueryFilters().SingleAsync(b => b.Id == bookmark.Id);
            tombstone.DeletedAt.Should().NotBeNull();
            tombstone.Comment.Should().BeNull("a deleted comment is not kept");
            tombstone.Revision.Should().BeGreaterThan(before);
        }
    }

    [Fact]
    public async Task ADeviceThatWasForgottenKeepsNoSettingsAndNoModel()
    {
        var account = await NewAccount();
        var device = Device(account.Id);
        await using (var db = NewContext())
        {
            db.Devices.Add(device);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.Devices.Remove(await db.Devices.SingleAsync(d => d.Id == device.Id));
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            (await db.Devices.Where(d => d.AccountId == account.Id).ToListAsync()).Should().BeEmpty();
            var tombstone = await db.Devices.IgnoreQueryFilters().SingleAsync(d => d.Id == device.Id);
            tombstone.DeletedAt.Should().NotBeNull();
            tombstone.Settings.Should().BeNull();
            tombstone.Model.Should().BeNull();
        }
    }

    [Fact]
    public async Task AChapterOrATextCanBeMarkedAgainAfterItWasRemovedButNotTwiceAtOnce()
    {
        var account = await NewAccount();
        await using (var db = NewContext())
        {
            db.ChapterBookmarks.Add(Chapter(account.Id, 3));
            db.FavoriteTexts.Add(Favorite(account.Id, "KJV", 0));
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.ChapterBookmarks.Remove(await db.ChapterBookmarks.SingleAsync(b => b.AccountId == account.Id));
            db.FavoriteTexts.Remove(await db.FavoriteTexts.SingleAsync(f => f.AccountId == account.Id));
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.ChapterBookmarks.Add(Chapter(account.Id, 3));
            db.FavoriteTexts.Add(Favorite(account.Id, "KJV", 0));
            await db.SaveChangesAsync();

            (await db.ChapterBookmarks.IgnoreQueryFilters().CountAsync(b => b.AccountId == account.Id)).Should().Be(2);
            (await db.FavoriteTexts.IgnoreQueryFilters().CountAsync(f => f.AccountId == account.Id)).Should().Be(2);
        }

        await using (var db = NewContext())
        {
            db.FavoriteTexts.Add(Favorite(account.Id, "KJV", 1));
            var twice = () => db.SaveChangesAsync();
            (await twice.Should().ThrowAsync<DbUpdateException>())
                .WithInnerException<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        }
    }

    [Fact]
    public async Task LeavingTheAccountRemovesTheTombstonesToo()
    {
        var account = await NewAccount();
        await using (var db = NewContext())
        {
            db.Bookmarks.Add(Bookmark(account.Id, "kept for now"));
            await db.SaveChangesAsync();
            db.Bookmarks.Remove(await db.Bookmarks.SingleAsync(b => b.AccountId == account.Id));
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            await db.Accounts.Where(a => a.Id == account.Id).ExecuteDeleteAsync();
            (await db.Bookmarks.IgnoreQueryFilters().CountAsync(b => b.AccountId == account.Id)).Should().Be(0);
        }
    }

    [Fact]
    public async Task OneAccountsRevisionsAreHandedOutInTheOrderTheyCommitAndAnotherAccountIsNotHeldUp()
    {
        var reader = await NewAccount();
        var other = await NewAccount("Another");
        var first = Bookmark(reader.Id);
        var second = Bookmark(reader.Id);

        await using var holding = NewContext();
        await using var transaction = await holding.Database.BeginTransactionAsync();
        holding.Bookmarks.Add(first);
        await holding.SaveChangesAsync();

        await using var waiting = NewContext();
        waiting.Bookmarks.Add(second);
        var blocked = waiting.SaveChangesAsync();
        await Task.Delay(700);
        blocked.IsCompleted.Should().BeFalse("the first save has not committed, and a revision handed out now could commit before its own");

        await using (var apart = NewContext())
        {
            apart.Bookmarks.Add(Bookmark(other.Id));
            await apart.SaveChangesAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }

        await transaction.CommitAsync();
        await blocked.WaitAsync(TimeSpan.FromSeconds(10));

        second.Revision.Should().BeGreaterThan(first.Revision);
    }

    [Fact]
    public async Task TheFeedListsOneAccountsChangesOldestFirstFromTheRevisionAsked()
    {
        var reader = await NewAccount();
        var other = await NewAccount("Another");
        var bookmark = Bookmark(reader.Id, "John 3:16");
        var device = Device(reader.Id);
        await using (var db = NewContext())
        {
            db.Bookmarks.AddRange(bookmark, Bookmark(other.Id, "not yours"));
            db.ChapterBookmarks.Add(Chapter(reader.Id, 3));
            db.FavoriteTexts.Add(Favorite(reader.Id, "KJV", 0));
            db.Devices.Add(device);
            await db.SaveChangesAsync();
        }

        await using var server = await Server.Start(_connectionString);
        server.SignInAs(reader.Id);

        (await server.Http.GetAsync("/v1/me/changes?since=-1")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var all = await server.Changes(0);
        all.More.Should().BeFalse();
        all.Profile!.DisplayName.Should().Be("Reader");
        all.Bookmarks.Select(b => b.Bookmark!.Comment).Should().Equal("John 3:16");
        all.Bookmarks.Single().Bookmark!.Book.Should().Be("john");
        all.ChapterBookmarks.Single().Should().Match<ChapterBookmarkChange>(c => c.Book == "john" && c.Chapter == 3 && c.ChapterBookmark!.Texts.Count == 2);
        all.FavoriteTexts.Single().Text.Should().Be("KJV");
        all.Devices.Single().Device!.Model.Should().Be("Pixel");
        all.Revision.Should().Be(Revisions(all).Max());
        Revisions(all).Should().OnlyHaveUniqueItems();

        var nothing = await server.Changes(all.Revision);
        nothing.Revision.Should().Be(all.Revision);
        nothing.More.Should().BeFalse();
        nothing.Profile.Should().BeNull();
        Revisions(nothing).Should().BeEmpty();

        await using (var db = NewContext())
        {
            var changed = await db.Bookmarks.SingleAsync(b => b.Id == bookmark.Id);
            changed.Comment = "John 3:16, again";
            await db.SaveChangesAsync();
        }

        var latest = await server.Changes(all.Revision);
        latest.Bookmarks.Single().Bookmark!.Comment.Should().Be("John 3:16, again");
        latest.Revision.Should().BeGreaterThan(all.Revision);
        latest.ChapterBookmarks.Should().BeEmpty();
        latest.FavoriteTexts.Should().BeEmpty();
    }

    [Fact]
    public async Task ARowRemovedThroughTheEndpointsComesBackAsATombstoneAndEveryReadLeavesItOut()
    {
        var reader = await NewAccount();
        var bookmark = Bookmark(reader.Id, "to be removed");
        var device = Device(reader.Id);
        await using (var db = NewContext())
        {
            db.Bookmarks.Add(bookmark);
            db.ChapterBookmarks.Add(Chapter(reader.Id, 3));
            db.FavoriteTexts.Add(Favorite(reader.Id, "KJV", 0));
            db.Devices.Add(device);
            await db.SaveChangesAsync();
        }

        await using var server = await Server.Start(_connectionString);
        server.SignInAs(reader.Id);
        var cursor = (await server.Changes(0)).Revision;

        (await server.Http.DeleteAsync($"/v1/me/bookmarks/{bookmark.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await server.Http.DeleteAsync("/v1/me/chapter-bookmarks/john/3")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await server.Http.DeleteAsync("/v1/me/favorite-texts/kjv")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await server.Http.DeleteAsync($"/v1/me/devices/{device.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await server.Http.DeleteAsync($"/v1/me/bookmarks/{bookmark.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "a tombstone is not there to be removed");
        (await server.Http.DeleteAsync("/v1/me/favorite-texts/KJV")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await server.Http.GetFromJsonAsync<BookmarksResponse>("/v1/me/bookmarks"))!.Items.Should().BeEmpty();
        (await server.Http.GetFromJsonAsync<ChapterBookmarksResponse>("/v1/me/chapter-bookmarks"))!.Items.Should().BeEmpty();
        (await server.Http.GetFromJsonAsync<FavoriteTextsResponse>("/v1/me/favorite-texts"))!.Items.Should().BeEmpty();
        await using (var db = NewContext())
        {
            (await db.Devices.Where(d => d.AccountId == reader.Id).ToListAsync()).Should().BeEmpty();
        }

        var feed = await server.Changes(cursor);
        feed.Revision.Should().BeGreaterThan(cursor);
        feed.Bookmarks.Single().Should().Match<BookmarkChange>(b => b.Id == bookmark.Id && b.DeletedAt != null && b.Bookmark == null);
        feed.ChapterBookmarks.Single().Should().Match<ChapterBookmarkChange>(c => c.Book == "john" && c.Chapter == 3 && c.DeletedAt != null && c.ChapterBookmark == null);
        feed.FavoriteTexts.Single().Should().Match<FavoriteTextChange>(f => f.Text == "KJV" && f.DeletedAt != null && f.Favorite == null);
        feed.Devices.Single().Should().Match<DeviceChange>(d => d.Id == device.Id && d.DeletedAt != null && d.Device == null);

        // The text is put back: the feed holds the removal and then the new row, in that order.
        (await server.Http.PutAsync("/v1/me/favorite-texts/KJV", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await server.Changes(cursor);
        again.FavoriteTexts.Select(f => f.DeletedAt is null).Should().Equal(false, true);
        again.FavoriteTexts.Select(f => f.Revision).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task APageStopsAtItsLimitAndFollowingTheCursorMissesNothingAndRepeatsNothing()
    {
        var reader = await NewAccount();
        await using (var db = NewContext())
        {
            db.Bookmarks.AddRange(Bookmark(reader.Id, "one"), Bookmark(reader.Id, "two"), Bookmark(reader.Id, "three"));
            db.ChapterBookmarks.AddRange(Chapter(reader.Id, 1), Chapter(reader.Id, 2));
            db.FavoriteTexts.AddRange(Favorite(reader.Id, "KJV", 0), Favorite(reader.Id, "BHSA", 1));
            await db.SaveChangesAsync();
            db.Bookmarks.Add(Bookmark(reader.Id, "four"));
            await db.SaveChangesAsync();
        }

        await using var server = await Server.Start(_connectionString);
        server.SignInAs(reader.Id);
        var everything = Revisions(await server.Changes(0)).Order().ToList();
        everything.Should().HaveCount(9);

        var seen = new List<long>();
        var cursor = 0L;
        var pages = 0;
        bool more;
        do
        {
            var page = await server.Changes(cursor, take: 4);
            var revisions = Revisions(page).Order().ToList();
            revisions.Should().HaveCountLessThanOrEqualTo(4);
            revisions.Should().OnlyContain(r => r > cursor);
            seen.AddRange(revisions);
            page.Revision.Should().Be(revisions.Max());
            cursor = page.Revision;
            more = page.More;
            pages++;
        }
        while (more);

        pages.Should().Be(3);
        seen.Should().Equal(everything);
    }

    private static IEnumerable<long> Revisions(ChangesResponse feed) =>
        new[] { feed.Profile?.Revision }.Where(r => r is not null).Select(r => r!.Value)
            .Concat(feed.Bookmarks.Select(b => b.Revision))
            .Concat(feed.ChapterBookmarks.Select(b => b.Revision))
            .Concat(feed.FavoriteTexts.Select(f => f.Revision))
            .Concat(feed.Devices.Select(d => d.Revision));

    [Fact]
    public async Task OnlyASignedInReaderHasAFeed()
    {
        await using var server = await Server.Start(_connectionString);

        (await server.Http.GetAsync("/v1/me/changes")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>The endpoints on a real server on a free port, against this class's database.</summary>
    private sealed class Server(WebApplication app) : IAsyncDisposable
    {
        public HttpClient Http { get; } = new() { BaseAddress = new Uri(app.Urls.First()) };

        public static async Task<Server> Start(string connectionString)
        {
            var builder = WebApplication.CreateSlimBuilder(["--urls=http://127.0.0.1:0"]);
            builder.Services.ConfigureHttpJsonOptions(options =>
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default));
            builder.Services.AddDbContext<AccountsDbContext>(options => options.UseNpgsql(connectionString));

            // The bookmark endpoints check a new passage against the corpus; the requests here only read and remove.
            builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
            builder.Services.AddSingleton<ICanonIndex>(new Canon());
            builder.Services.AddAuthentication(TestAuthentication.Scheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthentication>(TestAuthentication.Scheme, null);
            builder.Services.AddAuthorization();
            var app = builder.Build();

            app.UseAuthentication();
            app.UseAuthorization();
            var v1 = app.MapGroup("/v1");
            v1.MapBookmarks();
            v1.MapChapterBookmarks();
            v1.MapFavoriteTexts();
            v1.MapDevices();
            v1.MapChanges();

            await app.StartAsync();
            return new Server(app);
        }

        public void SignInAs(Guid account)
        {
            Http.DefaultRequestHeaders.Remove(TestAuthentication.Header);
            Http.DefaultRequestHeaders.Add(TestAuthentication.Header, account.ToString());
        }

        public async Task<ChangesResponse> Changes(long since, int? take = null)
        {
            var response = await Http.GetAsync($"/v1/me/changes?since={since}" + (take is { } n ? $"&take={n}" : string.Empty));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<ChangesResponse>())!;
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private sealed class TestAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "Test";

        public const string Header = "X-Test-Account";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(Request.Headers.TryGetValue(Header, out var account)
                ? AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, account.ToString())], Scheme)),
                    Scheme))
                : AuthenticateResult.NoResult());
    }

    private sealed class Canon : ICanonIndex
    {
        private static readonly TextEntry[] Entries = [new(1, "KJV", [1, 40], true), new(2, "BHSA", [1], true)];

        public Task<TextEntry?> Text(string slug, CancellationToken cancellationToken) =>
            Task.FromResult(Entries.FirstOrDefault(e => string.Equals(e.Slug, slug, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<TextEntry>> Texts(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TextEntry>>(Entries);

        public Task<int> ChapterCount(int canonicalBook, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> ChapterCountAcross(IEnumerable<int> textIds, int canonicalBook, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> ChapterCountIn(int textId, int canonicalBook, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Forget()
        {
        }
    }
}
