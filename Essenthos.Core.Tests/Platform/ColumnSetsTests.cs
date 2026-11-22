using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Essenthos.Core.Accounts;
using Essenthos.Core.Configuration;
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

namespace Essenthos.Core.Tests.Platform;

/// <summary>The reader's own sets of columns, kept with the account.</summary>
public sealed class ColumnSetsTests
{
    [Fact]
    public void RecentSetsKeepEachTextOnceAndEachSetOnceWhereItWasFirstListed()
    {
        var read = ColumnSetEndpoints.ReadRecent([["KJV", "BHSA", "kjv"], [" NESTLE1904 ", "TR1894"], ["kjv", "bhsa"]]);

        read!.Value.Error.Should().BeNull();
        read.Value.Sets!.Select(set => string.Join(',', set)).Should().Equal("KJV,BHSA", "NESTLE1904,TR1894");
    }

    [Fact]
    public void TheOrderOfTheColumnsMakesAnotherSet() =>
        ColumnSetEndpoints.ReadRecent([["KJV", "BHSA"], ["BHSA", "KJV"]])!.Value.Sets.Should().HaveCount(2);

    [Fact]
    public void AListNotSentIsLeftAsItIs()
    {
        ColumnSetEndpoints.ReadRecent(null).Should().BeNull();
        ColumnSetEndpoints.ReadSaved(null).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("K J V")]
    [InlineData("KJV,BHSA")]
    [InlineData("-KJV")]
    [InlineData(null)]
    public void ATextIsNamedByItsSlug(string? text)
    {
        ColumnSetEndpoints.ReadRecent([[text]])!.Value.Error.Should().NotBeNull();
        ColumnSetEndpoints.ReadSaved([new ColumnSetSent("Mine", [text])])!.Value.Error.Should().NotBeNull();
    }

    [Fact]
    public void ASetHasAtLeastOneTextAndNoMoreThanAScreenHolds()
    {
        var many = Enumerable.Range(0, Limits.ChapterBookmarkTexts + 1).Select(i => $"T{i}").ToList<string?>();

        ColumnSetEndpoints.ReadRecent([[]])!.Value.Error.Should().NotBeNull();
        ColumnSetEndpoints.ReadRecent([null])!.Value.Error.Should().NotBeNull();
        ColumnSetEndpoints.ReadRecent([many])!.Value.Error.Should().NotBeNull();
        ColumnSetEndpoints.ReadRecent([many[..Limits.ChapterBookmarkTexts]])!.Value.Sets!.Single().Should().HaveCount(12);
    }

    [Fact]
    public void ASavedSetIsNamedInOneToSixtyCharactersTrimmed()
    {
        var kept = ColumnSetEndpoints.ReadSaved([new ColumnSetSent("  Морфологія  ", ["BHSA", "UBIO"])])!.Value.Sets!.Single();
        kept.Name.Should().Be("Морфологія");
        kept.Texts.Should().Equal("BHSA", "UBIO");
        ColumnSetEndpoints.ReadSaved([new ColumnSetSent("   ", ["KJV"])])!.Value.Error.Should().NotBeNull();
        ColumnSetEndpoints.ReadSaved([new ColumnSetSent(null, ["KJV"])])!.Value.Error.Should().NotBeNull();
        ColumnSetEndpoints.ReadSaved([new ColumnSetSent(new string('a', Limits.ColumnSetName + 1), ["KJV"])])!.Value.Error.Should().NotBeNull();
        ColumnSetEndpoints.ReadSaved([null])!.Value.Error.Should().NotBeNull();
    }

    [Fact]
    public void TwoSavedSetsMayHoldTheSameTexts() =>
        ColumnSetEndpoints.ReadSaved([new ColumnSetSent("One", ["KJV"]), new ColumnSetSent("Two", ["KJV"])])!.Value.Sets.Should().HaveCount(2);

    [Fact]
    public void AListLongerThanTheAccountKeepsIsRefusedNotCut()
    {
        var recent = Enumerable.Range(0, Limits.RecentColumnSets + 1).Select(i => (IReadOnlyList<string?>)[$"T{i}"]).ToList();
        var saved = Enumerable.Range(0, Limits.SavedColumnSets + 1).Select(i => new ColumnSetSent($"Set {i}", ["KJV"])).ToList();

        ColumnSetEndpoints.ReadRecent(recent)!.Value.Error.Should().Contain("5");
        ColumnSetEndpoints.ReadSaved(saved)!.Value.Error.Should().Contain("30");
        ColumnSetEndpoints.ReadRecent(recent[..Limits.RecentColumnSets])!.Value.Sets.Should().HaveCount(5);
        ColumnSetEndpoints.ReadSaved(saved[..Limits.SavedColumnSets])!.Value.Sets.Should().HaveCount(30);
    }
}

/// <summary>The sets as the database and the endpoints keep them, migrated on the test server.</summary>
public sealed class ColumnSetsStoreTests : IAsyncLifetime
{
    private static readonly string DatabaseName = $"essenthos_app_column_sets_test_{Environment.ProcessId}";

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnection.ConnectionStringKey] = $"{WitnessDatabase.TestServer};Database=postgres",
            })
            .AddUserSecrets(typeof(ColumnSetsStoreTests).Assembly)
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
    public async Task AnAccountMadeBeforeTheSetsHasNone()
    {
        await using (var db = NewContext())
        {
            await db.Database.ExecuteSqlAsync($"INSERT INTO account (id, display_name, photo_version, admin, created_at, updated_at, revision) VALUES ({Guid.CreateVersion7()}, 'Older', 0, false, now(), now(), 0)");
        }

        await using (var db = NewContext())
        {
            var older = await db.Accounts.AsNoTracking().SingleAsync(a => a.DisplayName == "Older");
            var sets = ColumnSetEndpoints.Describe(older.RecentColumnSets, older.SavedColumnSets);
            sets.Recent.Should().BeEmpty();
            sets.Saved.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task TheEndpointsKeepEachAccountsSetsAndMoveItsRevisionOnlyWhenTheyChange()
    {
        var reader = new Account { Id = Guid.CreateVersion7(), DisplayName = "Reader", CreatedAt = DateTimeOffset.UtcNow };
        var other = new Account { Id = Guid.CreateVersion7(), DisplayName = "Another", CreatedAt = DateTimeOffset.UtcNow };
        await using (var db = NewContext())
        {
            db.Accounts.AddRange(reader, other);
            await db.SaveChangesAsync();
        }

        await using var server = await Server.Start(_connectionString);
        var http = server.Http;

        (await http.GetAsync("/v1/me/column-sets")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        http.DefaultRequestHeaders.Add(TestAuthentication.Header, reader.Id.ToString());
        (await Sets(await http.GetAsync("/v1/me/column-sets"))).Should().Be(("", ""));

        (await Sets(await http.PutAsJsonAsync("/v1/me/column-sets", new
        {
            recent = new[] { new[] { "KJV", "BHSA" }, new[] { "kjv", "bhsa" }, new[] { "UBIO" } },
            saved = new[] { new { name = "Hebrew", texts = new[] { "BHSA", "SWETE", "KJV" } } },
        }))).Should().Be(("KJV,BHSA|UBIO", "Hebrew:BHSA,SWETE,KJV"));
        var revision = await Revision(reader.Id);

        (await Sets(await http.PutAsJsonAsync("/v1/me/column-sets", new { recent = new[] { new[] { "UBIO" }, new[] { "KJV", "BHSA" } } })))
            .Should().Be(("UBIO|KJV,BHSA", "Hebrew:BHSA,SWETE,KJV"), "a list not sent is left as it was");
        (await Revision(reader.Id)).Should().BeGreaterThan(revision);
        revision = await Revision(reader.Id);

        (await Sets(await http.PutAsJsonAsync("/v1/me/column-sets", new
        {
            recent = new[] { new[] { "UBIO" }, new[] { "KJV", "BHSA" } },
            saved = new[] { new { name = " Hebrew ", texts = new[] { "BHSA", "SWETE", "KJV" } } },
        }))).Should().Be(("UBIO|KJV,BHSA", "Hebrew:BHSA,SWETE,KJV"));
        (await Revision(reader.Id)).Should().Be(revision, "nothing changed");

        (await http.PutAsJsonAsync("/v1/me/column-sets", new { saved = new[] { new { name = "", texts = new[] { "KJV" } } } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var many = Enumerable.Range(0, Limits.RecentColumnSets + 1).Select(i => new[] { $"T{i}" }).ToArray();
        (await http.PutAsJsonAsync("/v1/me/column-sets", new { recent = many })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Sets(await http.GetAsync("/v1/me/column-sets"))).Should().Be(("UBIO|KJV,BHSA", "Hebrew:BHSA,SWETE,KJV"), "a refused list changes nothing");

        (await Sets(await http.PutAsJsonAsync("/v1/me/column-sets", new { saved = Array.Empty<object>() })))
            .Should().Be(("UBIO|KJV,BHSA", ""), "an empty list removes every saved set");

        await using (var db = NewContext())
        {
            var feed = await ChangeFeedEndpoints.Read(db, reader.Id, 0, 10, CancellationToken.None);
            feed.Profile!.RecentColumnSets.Select(set => string.Join(',', set)).Should().Equal("UBIO", "KJV,BHSA");
            feed.Profile.SavedColumnSets.Should().BeEmpty();
        }

        http.DefaultRequestHeaders.Remove(TestAuthentication.Header);
        http.DefaultRequestHeaders.Add(TestAuthentication.Header, other.Id.ToString());
        (await Sets(await http.GetAsync("/v1/me/column-sets"))).Should().Be(("", ""));
    }

    private async Task<long> Revision(Guid account)
    {
        await using var db = NewContext();
        return await db.Accounts.Where(a => a.Id == account).Select(a => a.Revision).SingleAsync();
    }

    private static async Task<(string Recent, string Saved)> Sets(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sets = (await response.Content.ReadFromJsonAsync<ColumnSetsResponse>())!;
        return (
            string.Join('|', sets.Recent.Select(set => string.Join(',', set))),
            string.Join('|', sets.Saved.Select(set => $"{set.Name}:{string.Join(',', set.Texts)}")));
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
            builder.Services.AddAuthentication(TestAuthentication.Scheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthentication>(TestAuthentication.Scheme, null);
            builder.Services.AddAuthorization();
            var app = builder.Build();

            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGroup("/v1").MapColumnSets();

            await app.StartAsync();
            return new Server(app);
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    /// <summary>Signs a request in as the account its header names, or leaves it signed out.</summary>
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
}
