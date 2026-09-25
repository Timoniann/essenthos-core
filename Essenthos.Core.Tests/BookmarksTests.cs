using Essenthos.Core.Accounts;
using Essenthos.Core.Configuration;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Publishing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A reader's bookmark: what colour and comment it can have, what counts as a passage, and the check
/// that stops a release from taking the verse out from under one.
/// </summary>
public sealed class BookmarksTests
{
    [Fact]
    public void ACommentIsWhatWasWrittenWithItsEndsTrimmedAndNothingIsNoComment()
    {
        BookmarkEndpoints.Comment("  In the beginning\r\nwas the Word  ", out var written).Should().BeTrue();
        written.Should().Be("In the beginning\nwas the Word");

        BookmarkEndpoints.Comment("   \n  ", out var blank).Should().BeTrue();
        blank.Should().BeNull();
        BookmarkEndpoints.Comment(null, out var none).Should().BeTrue();
        none.Should().BeNull();

        BookmarkEndpoints.Comment(new string('a', Limits.BookmarkComment), out _).Should().BeTrue();
        BookmarkEndpoints.Comment(new string('a', Limits.BookmarkComment + 1), out _).Should().BeFalse();
    }

    [Fact]
    public void AColourIsOneOfThePaletteAndDefaultsToTheFirst()
    {
        BookmarkEndpoints.Color(null).Should().Be("amber");
        BookmarkEndpoints.Color("rose").Should().Be("rose");
        BookmarkEndpoints.Color("#ff0000").Should().BeNull();
        BookmarkEndpoints.Color("Rose").Should().BeNull();
    }

    [Theory]
    [InlineData(3, 16, 3, 16, true)]
    [InlineData(3, 16, 3, 21, true)]
    // John 7:53-8:11 is one passage.
    [InlineData(7, 53, 8, 11, true)]
    [InlineData(3, 16, 3, 15, false)]
    [InlineData(8, 1, 7, 53, false)]
    [InlineData(0, 1, 1, 1, false)]
    [InlineData(1, 0, 1, 1, false)]
    public void APassageEndsWhereItStartsOrLater(int chapter, int verse, int endChapter, int endVerse, bool ordered) =>
        BookmarkEndpoints.Ordered(chapter, verse, endChapter, endVerse).Should().Be(ordered);

    [Fact]
    public void AnchorsAreReadOutOfPsqlRows()
    {
        BookmarkAnchors.Parse("|43|3|16\nKJV|43|8|11\n\n").Should().Equal(
            new BookmarkAnchors.Point("", 43, 3, 16),
            new BookmarkAnchors.Point("KJV", 43, 8, 11));
    }

    [Fact]
    public void TheCheckAsksForTheNamedTextOrAnyAndQuotesWhatItIsGiven()
    {
        var query = BookmarkAnchors.Query([new BookmarkAnchors.Point("", 43, 3, 16), new BookmarkAnchors.Point("K'JV", 1, 1, 1)]);

        query.Should().Contain("('', 43, 3, 16), ('K''JV', 1, 1, 1)");
        query.Should().Contain("a.text = '' OR lower(t.slug) = lower(a.text)");
        query.Should().Contain("NOT EXISTS");
    }
}

/// <summary>
/// How much one account can keep, asked of Postgres: accounts cost nothing to make, and the accounts
/// database shares its disk with the corpus.
/// </summary>
public sealed class BookmarkAllowanceTests : IAsyncLifetime
{
    private static readonly string DatabaseName = $"essenthos_app_bookmarks_test_{Environment.ProcessId}";

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnection.ConnectionStringKey] = "Host=localhost;Port=5437;Database=postgres;Username=essenthos",
            })
            .AddUserSecrets(typeof(BookmarkAllowanceTests).Assembly)
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

    private static async Task<Guid> Account(AccountsDbContext db)
    {
        var account = new Account { Id = Guid.CreateVersion7(), DisplayName = "Reader", CreatedAt = DateTimeOffset.UtcNow };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    /// <summary>Bookmarks written in one statement, made <paramref name="age"/> ago, each with a comment of that many characters.</summary>
    private static Task Mark(AccountsDbContext db, Guid account, int count, TimeSpan age, int comment = 0) =>
        db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO bookmark (id, account_id, book, chapter, verse, end_chapter, end_verse, color, comment, created_at, updated_at, revision)
            SELECT gen_random_uuid(), {0}, 1, 1, 1, 1, 1, 'amber', CASE WHEN {2} > 0 THEN repeat('a', {2}) END, now() - {3}, now(), n
            FROM generate_series(1, {1}) AS n
            """,
            account, count, comment, age);

    [Fact]
    public async Task AnAccountMakesSoManyBookmarksADayAndKeepsSoManyInAll()
    {
        await using var db = NewContext();
        var now = DateTimeOffset.UtcNow;
        var busy = await Account(db);
        await Mark(db, busy, Limits.BookmarksPerDay - 1, TimeSpan.FromHours(1));
        (await BookmarkEndpoints.Refusal(db, busy, null, 0, now, default)).Should().BeNull();

        await Mark(db, busy, 1, TimeSpan.FromHours(2));
        (await BookmarkEndpoints.Refusal(db, busy, null, 0, now, default))!.Status.Should().Be(StatusCodes.Status429TooManyRequests);
        (await BookmarkEndpoints.Refusal(db, busy, null, 0, now.AddDays(1), default)).Should().BeNull();

        var full = await Account(db);
        await Mark(db, full, Limits.BookmarksPerAccount, TimeSpan.FromDays(30));
        (await BookmarkEndpoints.Refusal(db, full, null, 0, now, default))!.Status.Should().Be(StatusCodes.Status409Conflict);
        (await BookmarkEndpoints.Refusal(db, busy, null, 0, now.AddDays(1), default)).Should().BeNull();
    }

    [Fact]
    public async Task AnAccountsCommentsHoldSoManyCharactersBetweenThem()
    {
        await using var db = NewContext();
        var account = await Account(db);
        const int each = 10_000;
        await Mark(db, account, Limits.BookmarkCommentsPerAccount / each - 1, TimeSpan.FromDays(30), each);
        var last = Guid.CreateVersion7();
        db.Bookmarks.Add(new Bookmark
        {
            Id = last, AccountId = account, Book = 1, Chapter = 1, Verse = 2, EndChapter = 1, EndVerse = 2,
            Color = "amber", Comment = new string('b', each), CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        });
        await db.SaveChangesAsync();

        (await BookmarkEndpoints.Refusal(db, account, null, 1, null, default))!.Status.Should().Be(StatusCodes.Status409Conflict);
        (await BookmarkEndpoints.Refusal(db, account, null, 0, null, default)).Should().BeNull();
        (await BookmarkEndpoints.Refusal(db, account, last, each, null, default)).Should().BeNull();
        (await BookmarkEndpoints.Refusal(db, account, last, each + 1, null, default))!.Status.Should().Be(StatusCodes.Status409Conflict);

        var other = await Account(db);
        (await BookmarkEndpoints.Refusal(db, other, null, Limits.BookmarkComment, DateTimeOffset.UtcNow, default)).Should().BeNull();
    }
}
