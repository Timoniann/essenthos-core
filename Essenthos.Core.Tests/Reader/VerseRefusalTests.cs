using System.Net;
using System.Threading.Channels;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// The line a refused <c>/v1/verses</c> leaves in the log: which refusal, how many addresses, the first of them, the names
/// of the parameters sent and the query's length, the page and the agent, and nothing a reader typed. Every refusal is decided before the database is asked anything, so the
/// server here has none.
/// </summary>
public sealed class VerseRefusalTests
{
    [Fact]
    public async Task ARefusalNamesWhichItWasTheAddressesItCouldNotReadAndWhereItCameFrom()
    {
        await using var server = await Server.Start();
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "/v1/verses?corpus=KJV&verses=genesis:1:1,genesis:0:1,nowhere:1:1,exodus:2:3");
        request.Headers.Referrer = new Uri("https://essenthos.org/read/genesis/0?q=a+reader%27s+words");
        request.Headers.TryAddWithoutValidation("User-Agent", "SomeBot/1.0");

        var response = await server.Http.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await server.NextLine()).Should().Be(
            "verses refused unreadable corpus=KJV asked=4 verses=genesis:0:1,nowhere:1:1 marking=- parameters=corpus,verses query=64 "
            + "page=/read/genesis/0 agent=SomeBot/1.0");
    }

    [Theory]
    [InlineData("verses=", "no-verses", "asked=0 verses=- marking=- parameters=corpus,verses query=18")]
    [InlineData("verses=genesis:1:1&strong=love", "not-strong", "asked=1 verses=genesis:1:1 marking=strong parameters=corpus,strong,verses query=41")]
    [InlineData("verses=luke:4:23&occupation=physician", "occupation-alone", "asked=1 verses=luke:4:23 marking=occupation parameters=corpus,occupation,verses query=48")]
    [InlineData("verses=genesis:1:1,genesis:1:2&word=a+reader%27s+words", "not-one-word", "asked=2 verses=genesis:1:1,genesis:1:2 marking=word parameters=corpus,verses,word query=65")]
    [InlineData("refs=genesis:1:1&strong=love", "not-strong", "asked=1 verses=genesis:1:1 marking=strong parameters=corpus,refs,strong query=39")]
    public async Task EachRefusalIsNamed(string query, string reason, string asked)
    {
        await using var server = await Server.Start();

        var response = await server.Http.GetAsync($"/v1/verses?corpus=KJV&{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var line = await server.NextLine();
        line.Should().Be($"verses refused {reason} corpus=KJV {asked} page=- agent=-");
        line.Should().NotContainAny("reader", "physician", "love");
    }

    [Fact]
    public async Task TooManyAddressesQuoteTheFirstFewAndCountThemAll()
    {
        await using var server = await Server.Start();
        var refs = string.Join(',', Enumerable.Range(1, 121).Select(verse => $"psalms:119:{verse}"));

        var response = await server.Http.GetAsync($"/v1/verses?corpus=KJV&verses={refs}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await server.NextLine()).Should().Be(
            $"verses refused too-many corpus=KJV asked=121 verses=psalms:119:1,psalms:119:2,psalms:119:3 marking=- "
            + $"parameters=corpus,verses query={"corpus=KJV&verses=".Length + refs.Length} page=- agent=-");
    }

    [Fact]
    public async Task ACallerThatDroppedTheAddressesIsSeenByTheNamesItSentAndNeverByTheirValues()
    {
        await using var server = await Server.Start();

        var response = await server.Http.GetAsync("/v1/verses?corpus=KJV&strong=H430&secret=a+reader%27s+words");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("verses=book:chapter:verse");
        var line = await server.NextLine();
        line.Should().Be(
            "verses refused no-verses corpus=KJV asked=0 verses=- marking=strong parameters=corpus,secret,strong query=48 page=- agent=-");
        line.Should().NotContainAny("reader", "H430");
    }

    [Theory]
    [InlineData("refs=genesis:0:1", "verses=genesis:0:1")]
    [InlineData("verses=genesis:0:1", "verses=genesis:0:1")]
    [InlineData("verses=genesis:0:1&refs=exodus:0:1", "verses=genesis:0:1")]
    [InlineData("verses=&refs=exodus:0:1", "verses=exodus:0:1")]
    public async Task VersesIsTheNameAndRefsItsAliasAndWhereBothAreSentVersesIsRead(string query, string read)
    {
        await using var server = await Server.Start();

        var response = await server.Http.GetAsync($"/v1/verses?corpus=KJV&{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await server.NextLine()).Should().StartWith($"verses refused unreadable corpus=KJV asked=1 {read} ");
    }

    [Theory]
    [InlineData("https://essenthos.org/word/father?corpus=BSB", "/word/father")]
    [InlineData("not an address", "-")]
    [InlineData(null, "-")]
    public void APageIsItsPathAlone(string? referer, string page) =>
        VerseEndpoints.PageOf(referer).Should().Be(page);

    private sealed class Server(WebApplication app, Channel<string> lines) : IAsyncDisposable
    {
        public HttpClient Http { get; } = new() { BaseAddress = new Uri(app.Urls.First()) };

        public async Task<string> NextLine()
        {
            using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            return await lines.Reader.ReadAsync(patience.Token);
        }

        public static async Task<Server> Start()
        {
            var builder = WebApplication.CreateSlimBuilder(["--urls=http://127.0.0.1:0"]);
            var lines = Channel.CreateUnbounded<string>();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new Capture(lines));
            builder.Services.ConfigureHttpJsonOptions(options =>
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default));
            builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql("Host=127.0.0.1;Port=1;Database=none"));
            builder.Services.AddSingleton<ICanonIndex, OneText>();
            builder.Services.AddSingleton<WordForms>();
            var app = builder.Build();
            app.MapGroup("/v1").MapVerses();

            await app.StartAsync();
            return new Server(app, lines);
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    /// <summary>Every slug is a text the corpus holds, so the request reaches the refusals.</summary>
    private sealed class OneText : ICanonIndex
    {
        private static readonly TextEntry Held = new(1, "KJV", [1], false);

        public Task<TextEntry?> Text(string slug, CancellationToken cancellationToken) => Task.FromResult<TextEntry?>(Held);

        public Task<IReadOnlyList<TextEntry>> Texts(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TextEntry>>([Held]);

        public Task<int> ChapterCount(int canonicalBook, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<int> ChapterCountAcross(IEnumerable<int> textIds, int canonicalBook, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<int> ChapterCountIn(int textId, int canonicalBook, CancellationToken cancellationToken) => Task.FromResult(0);

        public void Forget()
        {
        }
    }

    private sealed class Capture(Channel<string> lines) : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) =>
            categoryName == VerseEndpoints.LogCategory ? this : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Writer.TryWrite(formatter(state, exception));

        public void Dispose()
        {
        }
    }
}
