using System.Net;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// <c>/v1/search</c> asked for nothing but punctuation — a dash, a quotation mark, an ellipsis — on a real
/// server over a real database: no word to look for, so no verse holds it, and the answer is an empty
/// page like any other search that finds nothing, never a fault. A word beside the mark is searched alone.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SearchWithoutAWordTests : IAsyncLifetime
{
    private readonly WitnessDatabase _database;
    private Server? _server;

    public SearchWithoutAWordTests(WitnessDatabase database)
    {
        _database = database;
        using var db = database.NewContext();
        db.Database.ExecuteSqlRaw("DELETE FROM text");

        Corpus.Add(db, "RUSV", TextKind.Translation, "rus",
            (1, 1, ["В", "начале", "было", "Слово"]),
            (1, 2, ["и", "Слово", "было", "у", "Бога"]),
            (1, 3, ["и", "сказал", "Бог"]));
        Corpus.Add(db, "KJV", TextKind.Translation, "eng",
            (1, 1, ["In", "the", "beginning", "was", "the", "Word"]),
            (1, 2, ["and", "God", "said"]));
        db.SaveChanges();

        foreach (var word in db.Words.ToList())
        {
            word.NormalisedText = word.Surface.ToLowerInvariant();
        }

        db.SaveChanges();
    }

    public async Task InitializeAsync()
    {
        await using (var db = _database.NewContext())
        {
            var rusv = await db.Texts.SingleAsync(t => t.Slug == "RUSV");
            var forms = await db.Words.Where(w => w.TextId == rusv.Id)
                .GroupBy(w => w.NormalisedText!).Select(g => new { Form = g.Key, Occurrences = g.Count() }).ToListAsync();
            db.TextForms.AddRange(forms.Select(form => new TextForm
            {
                TextId = rusv.Id, Form = form.Form, Occurrences = form.Occurrences, Stem = form.Form,
            }));
            await db.SaveChangesAsync();
        }

        _server = await Server.Start(_database);
    }

    public async Task DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }

    public static TheoryData<string> Marks() =>
    [
        "—", "–", "«", "»", "„", "“", "”", "--", "-", "*", "…", "·", "’", "(", ")", "\"", "!", "\"\"", "« »", "— –",
    ];

    [Theory]
    [MemberData(nameof(Marks))]
    public async Task OnlyPunctuationFindsNothingAndSaysSo(string query)
    {
        foreach (var corpus in new[] { "RUSV", "KJV" })
        {
            var (status, body) = await _server!.Search($"q={Uri.EscapeDataString(query)}&corpus={corpus}");

            status.Should().Be(HttpStatusCode.OK, $"«{query}» in {corpus} answered {body}");
            using var found = JsonDocument.Parse(body);
            found.RootElement.GetProperty("total").GetInt32().Should().Be(0);
            found.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
            found.RootElement.GetProperty("terms").GetArrayLength().Should().Be(0);
        }
    }

    [Theory]
    [InlineData("whole")]
    [InlineData("substring")]
    public async Task OnlyPunctuationFindsNothingHoweverTheWordsAreMatched(string match)
    {
        var (status, body) = await _server!.Search($"q={Uri.EscapeDataString("—")}&corpus=RUSV&match={match}");

        status.Should().Be(HttpStatusCode.OK, body);
        JsonDocument.Parse(body).RootElement.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Theory]
    [InlineData("— слово")]
    [InlineData("слово —")]
    [InlineData("«слово»")]
    [InlineData("„слово“ …")]
    [InlineData("слово, — ")]
    public async Task AWordBesideTheMarksIsSearchedAlone(string query)
    {
        var (status, body) = await _server!.Search($"q={Uri.EscapeDataString(query)}&corpus=RUSV");

        status.Should().Be(HttpStatusCode.OK, body);
        using var found = JsonDocument.Parse(body);
        found.RootElement.GetProperty("total").GetInt32().Should().Be(2);
        found.RootElement.GetProperty("terms").EnumerateArray().Select(term => term.GetProperty("text").GetString())
            .Should().Equal("слово");
    }

    [Fact]
    public async Task AWordBesideTheMarksIsSearchedAloneInAnUncountedText()
    {
        var (status, body) = await _server!.Search($"q={Uri.EscapeDataString("— word …")}&corpus=KJV");

        status.Should().Be(HttpStatusCode.OK, body);
        JsonDocument.Parse(body).RootElement.GetProperty("total").GetInt32().Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("q=")]
    [InlineData("q=%20%20")]
    public async Task NoQueryAtAllIsStillAMalformedRequest(string query)
    {
        var (status, _) = await _server!.Search($"{query}&corpus=RUSV");

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed class Server(WebApplication app) : IAsyncDisposable
    {
        private HttpClient Http { get; } = new() { BaseAddress = new Uri(app.Urls.First()) };

        public async Task<(HttpStatusCode Status, string Body)> Search(string query)
        {
            using var response = await Http.GetAsync($"/v1/search?{query}");
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        public static async Task<Server> Start(WitnessDatabase database)
        {
            using var connection = database.NewConnection();
            var builder = WebApplication.CreateSlimBuilder(["--urls=http://127.0.0.1:0"]);
            builder.Logging.ClearProviders();
            builder.Services.ConfigureHttpJsonOptions(options =>
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default));
            builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connection.ConnectionString));
            builder.Services.AddSingleton<ICanonIndex, CanonIndex>();
            builder.Services.AddSingleton<WordForms>();
            var app = builder.Build();
            app.MapGroup("/v1").MapSearch();

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
