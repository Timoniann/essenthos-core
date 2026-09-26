using Essenthos.Core.Accounts;
using Essenthos.Core.Configuration;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A place a reader is reading: the verse it can remember, the texts it keeps open, and the order the
/// reader puts the list in.
/// </summary>
public sealed class ChapterBookmarksTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(1, true)]
    [InlineData(176, true)]
    [InlineData(ChapterBookmarkEndpoints.LastVerse, true)]
    [InlineData(ChapterBookmarkEndpoints.LastVerse + 1, false)]
    [InlineData(0, false)]
    [InlineData(-3, false)]
    public void AVerseIsUnknownOrOneAChapterCouldHave(int? verse, bool valid) =>
        ChapterBookmarkEndpoints.Verse(verse).Should().Be(valid);

    [Fact]
    public void TextsAreKeptOnceEachInTheOrderFirstAsked()
    {
        ChapterBookmarkEndpoints.Unique(["KJV", " HEB ", "kjv", "GRK", "HEB"]).Should().Equal("KJV", "HEB", "GRK");
        ChapterBookmarkEndpoints.Unique([]).Should().BeEmpty();
    }

    [Fact]
    public void ABookmarkRemembersSoManyTexts()
    {
        var most = Enumerable.Range(1, Limits.ChapterBookmarkTexts).Select(i => $"T{i}").ToList();
        ChapterBookmarkEndpoints.Unique(most).Should().HaveCount(Limits.ChapterBookmarkTexts);
        ChapterBookmarkEndpoints.Unique([.. most, "t1"]).Should().HaveCount(Limits.ChapterBookmarkTexts);
        ChapterBookmarkEndpoints.Unique([.. most, "More"]).Should().BeNull();
    }

    [Fact]
    public void TheChaptersNamedGoFirstInTheirOrderAndTheRestFollowInTheirs()
    {
        var list = new[] { Mark(1, 1, 0), Mark(19, 23, 1), Mark(43, 1, 2), Mark(43, 3, 5), Mark(45, 8, 9) };

        ChapterBookmarkEndpoints.Reorder(list, [(43, 3), (1, 1), (66, 22), (43, 3)]);

        list.OrderBy(b => b.Position).Select(b => (b.Book, b.Chapter)).Should().Equal(
            (43, 3), (1, 1), (19, 23), (43, 1), (45, 8));
        list.Select(b => b.Position).Order().Should().Equal(0, 1, 2, 3, 4);
    }

    [Fact]
    public void NamingNothingClosesTheGapsAndKeepsTheOrder()
    {
        var list = new[] { Mark(1, 1, 3), Mark(2, 1, 7), Mark(3, 1, 40) };

        ChapterBookmarkEndpoints.Reorder(list, []);

        list.Select(b => b.Position).Should().Equal(0, 1, 2);
    }

    private static ChapterBookmark Mark(int book, int chapter, int position) => new()
    {
        Id = Guid.CreateVersion7(), Book = book, Chapter = chapter, Position = position, Color = "amber",
    };
}

/// <summary>What the table holds, asked of Postgres: one bookmark a chapter, gone with the account.</summary>
public sealed class ChapterBookmarksStoreTests : IAsyncLifetime
{
    private static readonly string DatabaseName = $"essenthos_app_chapter_bookmarks_test_{Environment.ProcessId}";

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnection.ConnectionStringKey] = "Host=localhost;Port=5437;Database=postgres;Username=essenthos",
            })
            .AddUserSecrets(typeof(ChapterBookmarksStoreTests).Assembly)
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

    [Fact]
    public async Task AnAccountHasOneBookmarkAChapterWithItsTextsAndLosesThemWithTheAccount()
    {
        var account = new Account { Id = Guid.CreateVersion7(), DisplayName = "Reader", CreatedAt = DateTimeOffset.UtcNow };
        await using (var db = NewContext())
        {
            db.Accounts.Add(account);
            db.ChapterBookmarks.Add(Mark(account.Id, ["KJV", "HEB"]));
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var kept = await db.ChapterBookmarks.SingleAsync(b => b.AccountId == account.Id);
            kept.Texts.Should().Equal("KJV", "HEB");
            kept.Verse.Should().Be(16);
            kept.Revision.Should().BePositive();

            db.ChapterBookmarks.Add(Mark(account.Id, []));
            var twice = () => db.SaveChangesAsync();
            (await twice.Should().ThrowAsync<DbUpdateException>())
                .WithInnerException<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        }

        await using (var db = NewContext())
        {
            await db.Accounts.Where(a => a.Id == account.Id).ExecuteDeleteAsync();
            (await db.ChapterBookmarks.CountAsync()).Should().Be(0);
        }
    }

    private static ChapterBookmark Mark(Guid account, List<string> texts) => new()
    {
        Id = Guid.CreateVersion7(), AccountId = account, Book = 43, Chapter = 3, Verse = 16, Texts = texts,
        Color = "amber", CreatedAt = DateTimeOffset.UtcNow,
    };
}
