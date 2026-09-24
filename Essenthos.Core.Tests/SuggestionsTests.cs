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
/// What a reader's suggestion may hold, and who counts as an admin: the two places where a lax check
/// is a hole rather than a bug — a link that is not one reaching an admin's click, and an account
/// reaching the admin area.
/// </summary>
public sealed class SuggestionsTests
{
    private static SuggestionCreate Create(
        string category,
        string? body = "Something",
        Dictionary<string, string?>? fields = null,
        IReadOnlyList<string>? links = null) =>
        new(category, body, fields, links, "uk");

    [Fact]
    public void ANewTextNeedsOnlyItsTitleAndKeepsEachFieldWhereItBelongs()
    {
        var (form, problem) = SuggestionEndpoints.Check(Create("text", body: null,
            fields: new() { ["title"] = "  Bible kralická ", ["language"] = "Czech", ["year"] = "", ["licence"] = null },
            links: ["https://example.org/kralicka.zip", "https://example.org/kralicka.zip", "  "]));

        problem.Should().BeNull();
        form!.Fields.Should().Equal(new Dictionary<string, string> { ["title"] = "Bible kralická", ["language"] = "Czech" });
        form.Links.Should().Equal("https://example.org/kralicka.zip");
        form.Body.Should().BeNull();
        SuggestionEndpoints.HasLink(form).Should().BeTrue();

        SuggestionEndpoints.Check(Create("text", body: "notes", fields: new() { ["language"] = "Czech" })).Problem
            .Should().Contain("title");
    }

    [Theory]
    [InlineData("error", null)]
    [InlineData("record", "  ")]
    [InlineData("feature", "")]
    [InlineData("other", null)]
    public void EveryOtherCategoryNeedsItsMainText(string category, string? body) =>
        SuggestionEndpoints.Check(Create(category, body)).Form.Should().BeNull();

    [Fact]
    public void AnUnknownCategoryOrFieldIsRefusedRatherThanStored()
    {
        SuggestionEndpoints.Check(Create("spam")).Problem.Should().StartWith("A suggestion is one of");
        SuggestionEndpoints.Check(Create("feature", fields: new() { ["title"] = "x" })).Problem.Should().Contain("\"title\"");
        SuggestionEndpoints.Check(Create("feature", fields: new() { ["where"] = new string('a', Limits.SuggestionField + 1) }))
            .Problem.Should().Contain("at most");
    }

    [Fact]
    public void AnErrorNamesItsVerseCanonicallyWhateverTheReaderTyped()
    {
        var (form, _) = SuggestionEndpoints.Check(Create("error",
            fields: new() { ["book"] = "John", ["chapter"] = "3", ["verse"] = "16", ["text"] = "KJV" }));
        form!.Fields["book"].Should().Be("john");

        SuggestionEndpoints.Check(Create("error", fields: new() { ["book"] = "Hezekiah" })).Form.Should().BeNull();
        SuggestionEndpoints.Check(Create("error", fields: new() { ["book"] = "john", ["chapter"] = "0" })).Form.Should().BeNull();
        SuggestionEndpoints.Check(Create("error", fields: new() { ["book"] = "john", ["verse"] = "3" })).Form.Should().BeNull();
        SuggestionEndpoints.Check(Create("error", fields: new() { ["text"] = "<script>" })).Form.Should().BeNull();
    }

    [Fact]
    public void ARecordIsAKindAndAnAddressTogether()
    {
        SuggestionEndpoints.Check(Create("record", fields: new() { ["recordKind"] = "person", ["record"] = "abraham", ["recordName"] = "Abraham" }))
            .Form.Should().NotBeNull();
        SuggestionEndpoints.Check(Create("record", fields: new() { ["record"] = "abraham" })).Form.Should().BeNull();
        SuggestionEndpoints.Check(Create("record", fields: new() { ["recordKind"] = "planet", ["record"] = "mars" })).Form.Should().BeNull();
        SuggestionEndpoints.Check(Create("record", fields: new() { ["recordKind"] = "person", ["record"] = "../admin" })).Form.Should().BeNull();
    }

    [Theory]
    [InlineData("https://example.org/a", true)]
    [InlineData("http://example.org/a?b=c", true)]
    // Each is how a link an admin clicks runs something or goes somewhere it does not say.
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,<script>alert(1)</script>", false)]
    [InlineData("ftp://example.org/file", false)]
    [InlineData("//example.org/a", false)]
    [InlineData("example.org/a", false)]
    [InlineData("https://exa mple.org", false)]
    [InlineData("file:///C:/Windows", false)]
    public void ALinkIsAnAbsoluteWebAddress(string link, bool accepted) =>
        SuggestionEndpoints.IsLink(link).Should().Be(accepted);

    [Fact]
    public void LinksAreRefusedWhereTheCategoryTakesNoneAndCappedWhereItDoes()
    {
        SuggestionEndpoints.Check(Create("feature", links: ["https://example.org"])).Form.Should().BeNull();
        SuggestionEndpoints.Check(Create("text", fields: new() { ["title"] = "t" }, links: ["javascript:alert(1)"])).Form.Should().BeNull();
        var many = Enumerable.Range(0, Limits.SuggestionLinks + 1).Select(i => $"https://example.org/{i}").ToList();
        SuggestionEndpoints.Check(Create("text", fields: new() { ["title"] = "t" }, links: many)).Problem.Should().Contain("at most");
    }

    [Fact]
    public void AMessageIsTrimmedAndNeitherEmptyNorEndless()
    {
        SuggestionEndpoints.Message("  Thank you\r\nfor this  ").Should().Be("Thank you\nfor this");
        SuggestionEndpoints.Message("   ").Should().BeNull();
        SuggestionEndpoints.Message(new string('a', Limits.SuggestionMessage + 1)).Should().BeNull();
    }

    [Fact]
    public void ASearchTakesTheReadersWordsLiterally() =>
        AdminEndpoints.Like("100%_sure\\").Should().Be("100\\%\\_sure\\\\");

    [Fact]
    public void TheFirstAdminsAreReadFromConfigurationInEitherShape()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Accounts:BootstrapAdmins:0"] = " Owner@Example.org ",
                ["Accounts:BootstrapAdmins:1"] = "second@example.org; not-an-address",
            })
            .Build();
        Admins.Read(configuration).Should().Equal("owner@example.org", "second@example.org");

        var single = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Accounts:BootstrapAdmins"] = "a@example.org,b@example.org" })
            .Build();
        Admins.Read(single).Should().Equal("a@example.org", "b@example.org");

        Admins.Read(new ConfigurationBuilder().Build()).Should().BeEmpty();
    }
}

/// <summary>
/// The same questions asked of Postgres: who is an admin, what a reader is shown of the thread, and
/// what survives an account being deleted. A scratch accounts database of this run's own, as
/// <see cref="WitnessDatabase"/> makes for the corpus.
/// </summary>
public sealed class SuggestionsDatabaseTests : IAsyncLifetime
{
    private static readonly string DatabaseName = $"essenthos_app_test_{Environment.ProcessId}";

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnection.ConnectionStringKey] = "Host=localhost;Port=5437;Database=postgres;Username=essenthos",
            })
            .AddUserSecrets(typeof(SuggestionsDatabaseTests).Assembly)
            .AddEnvironmentVariables()
            .Build();

        // The corpus's owner, which may create databases; the accounts role of a server may not.
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

    private static async Task<Guid> Account(AccountsDbContext db, string name, string? email = null, bool admin = false)
    {
        var account = new Account { Id = Guid.CreateVersion7(), DisplayName = name, Admin = admin, CreatedAt = DateTimeOffset.UtcNow };
        db.Accounts.Add(account);
        if (email is not null)
        {
            db.AccountEmails.Add(new AccountEmail { Email = email, AccountId = account.Id, CreatedAt = DateTimeOffset.UtcNow });
        }

        await db.SaveChangesAsync();
        return account.Id;
    }

    [Fact]
    public async Task AnAdminIsGrantedOrConfiguredByAVerifiedAddressAndNobodyElseIs()
    {
        await using var db = NewContext();
        var admins = new Admins(["owner@example.org"]);
        var owner = await Account(db, "Owner", "owner@example.org");
        var granted = await Account(db, "Granted", "granted@example.org", admin: true);
        var reader = await Account(db, "Reader", "reader@example.org");
        var nobody = await Account(db, "No address");

        (await admins.IsAdmin(db, owner, default)).Should().BeTrue();
        (await admins.IsAdmin(db, granted, default)).Should().BeTrue();
        (await admins.IsAdmin(db, reader, default)).Should().BeFalse();
        (await admins.IsAdmin(db, nobody, default)).Should().BeFalse();
        (await admins.IsAdmin(db, Guid.NewGuid(), default)).Should().BeFalse();

        (await admins.ConfiguredAmong(db, [owner, granted, reader], default)).Should().BeEquivalentTo([owner]);
        (await admins.All(db, default)).Select(a => a.Name).Should().Equal("Granted", "Owner");
        (await new Admins([]).IsAdmin(db, owner, default)).Should().BeFalse();
    }

    [Fact]
    public async Task AReaderIsNeverShownTheAdminsNotesAndDeletingTheirAccountKeepsTheAudit()
    {
        await using var db = NewContext();
        var admin = await Account(db, "Admin", admin: true);
        var reader = await Account(db, "Reader");
        var now = DateTimeOffset.UtcNow;
        var suggestion = new Suggestion
        {
            Id = Guid.CreateVersion7(),
            AccountId = reader,
            Category = "text",
            Status = "new",
            Fields = """{"title":"Bible kralická"}""",
            Links = ["https://example.org/k.zip"],
            HasLink = true,
            Search = "bible kralická",
            CreatedAt = now,
            UpdatedAt = now,
            LastActivityAt = now,
            AuthorSeenAt = now,
            AssignedTo = admin,
        };
        db.Suggestions.Add(suggestion);
        db.SuggestionMessages.AddRange(
            new SuggestionMessage { SuggestionId = suggestion.Id, AuthorId = admin, Kind = SuggestionMessage.Note, Body = "Check the licence", At = now },
            new SuggestionMessage { SuggestionId = suggestion.Id, AuthorId = admin, Kind = SuggestionMessage.Reply, Body = "Thank you", At = now.AddSeconds(1) });
        db.AdminActions.Add(AdminEndpoints.Action("Admin", admin, AdminAction.Replied, now, suggestion: suggestion.Id));
        await db.SaveChangesAsync();

        var shown = await SuggestionEndpoints.Describe(db, suggestion, reader, includeNotes: false, default);
        shown.Messages.Select(m => m.Body).Should().Equal("Thank you");
        shown.Messages[0].AuthorName.Should().Be("Admin");
        shown.Links.Should().Equal("https://example.org/k.zip");
        (await SuggestionEndpoints.Describe(db, suggestion, admin, includeNotes: true, default)).Messages.Should().HaveCount(2);

        (await AdminEndpoints.Counts(db, admin, default)).Should().Be(new AdminCountsResponse(1, 0, 1));

        await db.Accounts.Where(a => a.Id == reader).ExecuteDeleteAsync();
        (await db.Suggestions.AnyAsync(s => s.Id == suggestion.Id)).Should().BeFalse();
        (await db.SuggestionMessages.AnyAsync(m => m.SuggestionId == suggestion.Id)).Should().BeFalse();
        (await db.AdminActions.CountAsync(a => a.SuggestionId == suggestion.Id)).Should().Be(1);
    }

    [Fact]
    public async Task AnAdminsDeletedAccountLeavesTheirRepliesUnsignedAndTheSuggestionUnassigned()
    {
        await using var db = NewContext();
        var admin = await Account(db, "Leaving admin", admin: true);
        var reader = await Account(db, "Reader");
        var now = DateTimeOffset.UtcNow;
        var suggestion = new Suggestion
        {
            Id = Guid.CreateVersion7(), AccountId = reader, Category = "other", Status = "review", Body = "Hello",
            Fields = "{}", Search = "hello", CreatedAt = now, UpdatedAt = now, LastActivityAt = now, AuthorSeenAt = now, AssignedTo = admin,
        };
        db.Suggestions.Add(suggestion);
        db.SuggestionMessages.Add(new SuggestionMessage { SuggestionId = suggestion.Id, AuthorId = admin, Kind = SuggestionMessage.Reply, Body = "Done", At = now });
        await db.SaveChangesAsync();

        await db.Accounts.Where(a => a.Id == admin).ExecuteDeleteAsync();

        var kept = await db.Suggestions.AsNoTracking().SingleAsync(s => s.Id == suggestion.Id);
        kept.AssignedTo.Should().BeNull();
        var shown = await SuggestionEndpoints.Describe(db, kept, reader, includeNotes: false, default);
        shown.Messages.Should().ContainSingle().Which.AuthorName.Should().BeNull();
    }
}
