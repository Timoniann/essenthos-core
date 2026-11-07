using System.Net;
using System.Text.RegularExpressions;
using Essenthos.Core.Accounts;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>
/// A sign-in the provider's round trip did not finish — Cancel on its page, or a callback whose state
/// no longer matches — goes back to the page it started on, told it failed, rather than a bare 500
/// (PRB-1551). Nothing here reaches Google: the provider is configured with made-up test values and
/// only the site's own side of the round trip is driven.
/// </summary>
public sealed partial class SignInFailureTests
{
    [Fact]
    public async Task ACallbackWithAStateThatNoLongerMatchesGoesHomeToldItFailed()
    {
        await using var server = await Server.Start();

        var answer = await server.Http.GetAsync("/v1/auth/google/callback?state=stale&code=anything");

        answer.StatusCode.Should().Be(HttpStatusCode.Redirect);
        answer.Headers.Location!.OriginalString.Should().Be("/?signin=failed");
    }

    [Fact]
    public async Task CancellingOnTheProvidersPageGoesBackToThePageItStartedOn()
    {
        await using var server = await Server.Start();

        var login = await server.Http.GetAsync("/v1/auth/google/login?returnUrl=%2Fread%2Fgenesis%2F1%3Fc%3DKJV");
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var state = StateParameter().Match(login.Headers.Location!.Query).Groups[1].Value;
        state.Should().NotBeEmpty();

        var answer = await server.Http.GetAsync($"/v1/auth/google/callback?error=access_denied&state={state}");

        answer.StatusCode.Should().Be(HttpStatusCode.Redirect);
        answer.Headers.Location!.OriginalString.Should().Be("/read/genesis/1?c=KJV&signin=failed");
    }

    [Theory]
    [InlineData(null, "/?signin=failed")]
    [InlineData("/v1/auth/complete?returnUrl=%2Fpeople%2Fdavid", "/people/david?signin=failed")]
    [InlineData("/v1/auth/complete?returnUrl=https%3A%2F%2Felsewhere.example", "/?signin=failed")]
    [InlineData("/v1/auth/complete?returnUrl=%2F%2Felsewhere.example", "/?signin=failed")]
    public void AnUnfinishedSignInReturnsOnlyToAPageOfTheSite(string? completion, string expected) =>
        AuthEndpoints.Unfinished(completion).Should().Be(expected);

    [GeneratedRegex("[?&]state=([^&]+)")]
    private static partial Regex StateParameter();

    private sealed class Server(WebApplication app) : IAsyncDisposable
    {
        public HttpClient Http { get; } = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true })
        {
            BaseAddress = new Uri(app.Urls.First()),
        };

        public static async Task<Server> Start()
        {
            var builder = WebApplication.CreateSlimBuilder(["--urls=http://127.0.0.1:0"]);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Never connected to: the accounts database is not reached before our own completion step.
                ["Accounts:ConnectionString"] = "Host=127.0.0.1;Database=unused-by-these-tests",
                ["Accounts:Password"] = "not-a-password",
                ["Authentication:Google:ClientId"] = "test-client-id",
                ["Authentication:Google:ClientSecret"] = "test-client-value",
            });
            var providers = builder.Services.AddAccounts(builder.Configuration);
            // The real keys live in the accounts database; these tests protect a state and read it back, nothing more.
            builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            var app = builder.Build();

            app.UseAuthentication();
            app.MapGroup("/v1").MapAuth(providers);

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
}
