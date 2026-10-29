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

/// <summary>Who is reading, asked by every page: nobody is an answer, a vanished account is not.</summary>
public sealed class MeTests : IAsyncLifetime
{
    private static readonly string DatabaseName = $"essenthos_app_me_test_{Environment.ProcessId}";

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnection.ConnectionStringKey] = $"{WitnessDatabase.TestServer};Database=postgres",
            })
            .AddUserSecrets(typeof(MeTests).Assembly)
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
    public async Task NobodySignedInIsNoContentWhileTheAccountsOwnChangesStayRefused()
    {
        await using var server = await Server.Start(_connectionString);

        var me = await server.Http.GetAsync("/v1/me");
        me.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await me.Content.ReadAsByteArrayAsync()).Should().BeEmpty();

        (await server.Http.PatchAsJsonAsync("/v1/me", new { displayName = "Anybody" })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ASignedInReaderIsDescribedAndASessionWhoseAccountIsGoneIsRefused()
    {
        var reader = new Account { Id = Guid.CreateVersion7(), DisplayName = "Reader", CreatedAt = DateTimeOffset.UtcNow };
        await using (var db = NewContext())
        {
            db.Accounts.Add(reader);
            await db.SaveChangesAsync();
        }

        await using var server = await Server.Start(_connectionString);
        var http = server.Http;

        http.DefaultRequestHeaders.Add(TestAuthentication.Header, reader.Id.ToString());
        var me = await http.GetAsync("/v1/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        (await me.Content.ReadFromJsonAsync<MeResponse>())!.DisplayName.Should().Be("Reader");

        http.DefaultRequestHeaders.Remove(TestAuthentication.Header);
        http.DefaultRequestHeaders.Add(TestAuthentication.Header, Guid.CreateVersion7().ToString());
        (await http.GetAsync("/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
            builder.Services.AddSingleton(new Admins([]));
            builder.Services.AddAuthentication(TestAuthentication.Scheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthentication>(TestAuthentication.Scheme, null);
            builder.Services.AddAuthorization();
            var app = builder.Build();

            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGroup("/v1").MapMe();

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
