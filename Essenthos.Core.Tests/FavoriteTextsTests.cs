using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Essenthos.Core.Accounts;
using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
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

/// <summary>The texts a reader keeps at hand, in the order they put them in.</summary>
public sealed class FavoriteTextsTests
{
    [Fact]
    public void TheTextsNamedGoFirstInTheirOrderAndTheRestFollowInTheirs()
    {
        var list = new[] { Favorite("KJV", 0), Favorite("BHSA", 1), Favorite("RUSV", 4), Favorite("NESTLE1904", 9) };

        FavoriteTextEndpoints.Reorder(list, ["NESTLE1904", "kjv", "SWETE", "NESTLE1904"]);

        list.OrderBy(f => f.Position).Select(f => f.Text).Should().Equal("NESTLE1904", "KJV", "BHSA", "RUSV");
        list.Select(f => f.Position).Order().Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void NamingNothingClosesTheGapsAndKeepsTheOrder()
    {
        var list = new[] { Favorite("KJV", 2), Favorite("BHSA", 5), Favorite("RUSV", 40) };

        FavoriteTextEndpoints.Reorder(list, []);

        list.Select(f => f.Position).Should().Equal(0, 1, 2);
    }

    private static FavoriteText Favorite(string text, int position) => new()
    {
        Id = Guid.CreateVersion7(), Text = text, Position = position,
    };
}

/// <summary>What the table holds, asked of Postgres: a text once per account, gone with the account.</summary>
public sealed class FavoriteTextsStoreTests : IAsyncLifetime
{
    private static readonly string DatabaseName = $"essenthos_app_favorite_texts_test_{Environment.ProcessId}";

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnection.ConnectionStringKey] = "Host=localhost;Port=5437;Database=postgres;Username=essenthos",
            })
            .AddUserSecrets(typeof(FavoriteTextsStoreTests).Assembly)
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
    public async Task AnAccountKeepsATextOnceInItsOrderAndLosesThemWithTheAccount()
    {
        var account = new Account { Id = Guid.CreateVersion7(), DisplayName = "Reader", CreatedAt = DateTimeOffset.UtcNow };
        var other = new Account { Id = Guid.CreateVersion7(), DisplayName = "Another", CreatedAt = DateTimeOffset.UtcNow };
        await using (var db = NewContext())
        {
            db.Accounts.AddRange(account, other);
            db.FavoriteTexts.AddRange(Favorite(account.Id, "KJV", 1), Favorite(account.Id, "BHSA", 0), Favorite(other.Id, "KJV", 0));
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var kept = await db.FavoriteTexts.Where(f => f.AccountId == account.Id).OrderBy(f => f.Position).ToListAsync();
            kept.Select(f => f.Text).Should().Equal("BHSA", "KJV");
            kept.Should().OnlyContain(f => f.Revision > 0);

            db.FavoriteTexts.Add(Favorite(account.Id, "KJV", 2));
            var twice = () => db.SaveChangesAsync();
            (await twice.Should().ThrowAsync<DbUpdateException>())
                .WithInnerException<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        }

        await using (var db = NewContext())
        {
            await db.Accounts.Where(a => a.Id == account.Id).ExecuteDeleteAsync();
            (await db.FavoriteTexts.Select(f => f.AccountId).ToListAsync()).Should().Equal(other.Id);
        }
    }

    [Fact]
    public async Task TheEndpointsKeepEachTextOnceByItsSlugAppendNewOnesAndReorder()
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

        (await http.GetAsync("/v1/me/favorite-texts")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        http.DefaultRequestHeaders.Add(TestAuthentication.Header, reader.Id.ToString());
        (await http.PutAsync("/v1/me/favorite-texts/NOSUCH", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Put(http, "kjv")).Should().Be(("KJV", 0));
        (await Put(http, "BHSA")).Should().Be(("BHSA", 1));
        (await Put(http, "KJV")).Should().Be(("KJV", 0), "a text already a favourite stays where it is");
        (await Put(http, "syno")).Should().Be(("RUSV", 2), "an alias is kept as the text it names");

        var ordered = await http.PutAsJsonAsync("/v1/me/favorite-texts/order", new { items = new[] { "RUSV", "NOSUCH", "kjv" } });
        (await Texts(ordered)).Should().Equal("RUSV", "KJV", "BHSA");

        (await http.DeleteAsync("/v1/me/favorite-texts/kjv")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await http.DeleteAsync("/v1/me/favorite-texts/KJV")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Texts(await http.GetAsync("/v1/me/favorite-texts"))).Should().Equal("RUSV", "BHSA");

        http.DefaultRequestHeaders.Remove(TestAuthentication.Header);
        http.DefaultRequestHeaders.Add(TestAuthentication.Header, other.Id.ToString());
        (await Texts(await http.GetAsync("/v1/me/favorite-texts"))).Should().BeEmpty();
    }

    private static async Task<(string Text, int Position)> Put(HttpClient http, string text)
    {
        var response = await http.PutAsync($"/v1/me/favorite-texts/{text}", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var favorite = (await response.Content.ReadFromJsonAsync<FavoriteTextResponse>())!;
        return (favorite.Text, favorite.Position);
    }

    private static async Task<List<string>> Texts(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = (await response.Content.ReadFromJsonAsync<FavoriteTextsResponse>())!;
        return list.Items.OrderBy(f => f.Position).Select(f => f.Text).ToList();
    }

    private static FavoriteText Favorite(Guid account, string text, int position) => new()
    {
        Id = Guid.CreateVersion7(), AccountId = account, Text = text, Position = position, CreatedAt = DateTimeOffset.UtcNow,
    };

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
            builder.Services.AddSingleton<ICanonIndex>(new Canon());
            builder.Services.AddAuthentication(TestAuthentication.Scheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthentication>(TestAuthentication.Scheme, null);
            builder.Services.AddAuthorization();
            var app = builder.Build();

            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGroup("/v1").MapFavoriteTexts();

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

    /// <summary>Three texts, and the Synodal answering to another site's spelling of it.</summary>
    private sealed class Canon : ICanonIndex
    {
        private static readonly TextEntry[] Entries =
        [
            new(1, "KJV", [1, 40], true),
            new(2, "BHSA", [1], true),
            new(3, "RUSV", [1, 40], true),
        ];

        public Task<TextEntry?> Text(string slug, CancellationToken cancellationToken) =>
            Task.FromResult(Entries.FirstOrDefault(e =>
                string.Equals(e.Slug, slug, StringComparison.OrdinalIgnoreCase) ||
                (e.Slug == "RUSV" && string.Equals(slug, "syno", StringComparison.OrdinalIgnoreCase))));

        public Task<IReadOnlyList<TextEntry>> Texts(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TextEntry>>(Entries);

        public Task<int> ChapterCount(int canonicalBook, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> ChapterCountAcross(IEnumerable<int> textIds, int canonicalBook, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> ChapterCountIn(int textId, int canonicalBook, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Forget()
        {
        }
    }
}
