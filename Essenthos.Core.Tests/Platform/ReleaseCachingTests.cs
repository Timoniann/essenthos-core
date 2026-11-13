using System.Net;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>
/// The answers only a release changes are kept by the browser between pages; a refusal or a missing
/// answer is not, so it is asked again on the next page.
/// </summary>
public sealed class ReleaseCachingTests
{
    [Fact]
    public async Task AnAnswerOnlyAReleaseChangesIsKeptAndARefusalIsNot()
    {
        var builder = WebApplication.CreateSlimBuilder(["--urls=http://127.0.0.1:0"]);
        await using var app = builder.Build();
        app.MapGet("/books", () => new[] { "genesis" }).KeptUntilRelease();
        app.MapGet("/corpora", () => Results.Ok(new[] { "BSB" })).KeptUntilRelease();
        app.MapGet("/missing", () => Results.NotFound()).KeptUntilRelease();
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        foreach (var kept in new[] { "/books", "/corpora" })
        {
            var answer = await http.GetAsync(kept);
            answer.StatusCode.Should().Be(HttpStatusCode.OK);
            answer.Headers.CacheControl!.ToString().Should().Be(ReleaseCaching.CacheControl);
        }

        var missing = await http.GetAsync("/missing");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missing.Headers.CacheControl.Should().BeNull();
    }
}
