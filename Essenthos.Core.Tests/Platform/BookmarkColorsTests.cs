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

/// <summary>The bookmark colours a reader reaches for, kept with the account.</summary>
public sealed class BookmarkColorsTests
{
    [Fact]
    public void AListIsKeptInItsKeptSpellingEachColourOnceWhereItWasFirstNamed()
    {
        var read = BookmarkColorEndpoints.Read(["#AABBCC", "green", "#aabbcc", "amber"], own: false);

        read!.Value.Colors.Should().Equal("#aabbcc", "green", "amber");
        read.Value.Error.Should().BeNull();
    }

    [Fact]
    public void AListNotSentIsLeftAsItIs() =>
        BookmarkColorEndpoints.Read(null, own: true).Should().BeNull();

    [Theory]
    [InlineData("green")]
    [InlineData("#abc")]
    [InlineData("red")]
    [InlineData(null)]
    public void AColourOfOnesOwnIsOnlyEverRrggbb(string? color) =>
        BookmarkColorEndpoints.Read([color], own: true)!.Value.Error.Should().NotBeNull();

    [Fact]
    public void AListLongerThanTheAccountKeepsIsRefusedNotCut()
    {
        var own = Enumerable.Range(0, Limits.OwnBookmarkColors + 1).Select(i => $"#0000{i:x2}").ToList();
        var recent = Enumerable.Range(0, Limits.RecentBookmarkColors + 1).Select(i => $"#1100{i:x2}").ToList();

        BookmarkColorEndpoints.Read(own, own: true)!.Value.Error.Should().Contain("24");
        BookmarkColorEndpoints.Read(recent, own: false)!.Value.Error.Should().Contain("8");
        BookmarkColorEndpoints.Read(own[..Limits.OwnBookmarkColors], own: true)!.Value.Colors.Should().HaveCount(24);
    }
}

/// <summary>The colours as the database and the endpoints keep them, migrated on the test server.</summary>
public sealed class BookmarkColorsStoreTests : IAsyncLifetime
{
    private static readonly string DatabaseName = $"essenthos_app_bookmark_colors_test_{Environment.ProcessId}";

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnection.ConnectionStringKey] = $"{WitnessDatabase.TestServer};Database=postgres",
            })
            .AddUserSecrets(typeof(BookmarkColorsStoreTests).Assembly)
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
    public async Task AnAccountMadeBeforeTheColoursHasNoneAndAnAccountSavedWithoutThemStartsEmpty()
    {
        var account = new Account { Id = Guid.CreateVersion7(), DisplayName = "Reader", CreatedAt = DateTimeOffset.UtcNow };
        await using (var db = NewContext())
        {
            db.Accounts.Add(account);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlAsync($"INSERT INTO account (id, display_name, photo_version, admin, created_at, updated_at, revision) VALUES ({Guid.CreateVersion7()}, 'Older', 0, false, now(), now(), 0)");
        }

        await using (var db = NewContext())
        {
            var all = await db.Accounts.AsNoTracking().ToListAsync();
            all.Should().HaveCount(2).And.OnlyContain(a => a.RecentBookmarkColors.Count == 0 && a.OwnBookmarkColors.Count == 0);
        }
    }

    [Fact]
    public async Task TheEndpointsKeepEachAccountsColoursAndMoveItsRevisionOnlyWhenTheyChange()
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

        (await http.GetAsync("/v1/me/bookmark-colors")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        http.DefaultRequestHeaders.Add(TestAuthentication.Header, reader.Id.ToString());
        (await Colors(await http.GetAsync("/v1/me/bookmark-colors"))).Should().Be(("", ""));

        (await Colors(await http.PutAsJsonAsync("/v1/me/bookmark-colors", new { recent = new[] { "#AA0011", "green" }, own = new[] { "#aa0011" } })))
            .Should().Be(("#aa0011,green", "#aa0011"));
        var revision = await Revision(reader.Id);

        (await Colors(await http.PutAsJsonAsync("/v1/me/bookmark-colors", new { recent = new[] { "blue", "#aa0011" } })))
            .Should().Be(("blue,#aa0011", "#aa0011"), "a list not sent is left as it was");
        (await Revision(reader.Id)).Should().BeGreaterThan(revision);
        revision = await Revision(reader.Id);

        (await Colors(await http.PutAsJsonAsync("/v1/me/bookmark-colors", new { recent = new[] { "blue", "#AA0011" } })))
            .Should().Be(("blue,#aa0011", "#aa0011"));
        (await Revision(reader.Id)).Should().Be(revision, "nothing changed");

        (await http.PutAsJsonAsync("/v1/me/bookmark-colors", new { own = new[] { "green" } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var many = Enumerable.Range(0, Limits.OwnBookmarkColors + 1).Select(i => $"#0000{i:x2}").ToArray();
        (await http.PutAsJsonAsync("/v1/me/bookmark-colors", new { own = many })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Colors(await http.GetAsync("/v1/me/bookmark-colors"))).Should().Be(("blue,#aa0011", "#aa0011"), "a refused list changes nothing");

        http.DefaultRequestHeaders.Remove(TestAuthentication.Header);
        http.DefaultRequestHeaders.Add(TestAuthentication.Header, other.Id.ToString());
        (await Colors(await http.GetAsync("/v1/me/bookmark-colors"))).Should().Be(("", ""));
    }

    private async Task<long> Revision(Guid account)
    {
        await using var db = NewContext();
        return await db.Accounts.Where(a => a.Id == account).Select(a => a.Revision).SingleAsync();
    }

    private static async Task<(string Recent, string Own)> Colors(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var colors = (await response.Content.ReadFromJsonAsync<BookmarkColorsResponse>())!;
        return (string.Join(',', colors.Recent), string.Join(',', colors.Own));
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
            app.MapGroup("/v1").MapBookmarkColors();

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
