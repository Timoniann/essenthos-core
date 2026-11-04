using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Essenthos.Core.Desk;
using Essenthos.Core.Products;
using Essenthos.Core.Products.Essx;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>The console's ESSX page: a dropped file and a product opened, their structure, rows under a verse and a search, the check and the timings.</summary>
public sealed class DeskEssxTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private sealed record Word(WordKey Key, string Surface, long Count);

    private static readonly ProductSchema<Word> Schema = new ProductSchema<Word>()
        .OwnWordKey("own", w => w.Key)
        .Text("surface", w => w.Surface)
        .Integer("count", w => w.Count)
        .Reads(row => new Word(row.WordKey(0, "KJV"), row.Text(1), row.Integer(2)));

    private static readonly string[] Surfaces = ["In", "the", "beginning", "God", "created"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"desk-essx-{Guid.NewGuid():n}");

    private WebApplication? _app;

    private HttpClient _http = null!;

    private string Owner => Path.Combine(_root, "repository", "Resources", "Essenthos");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(Owner, "texts", "KJV"));
        Directory.CreateDirectory(Path.Combine(_root, "workspace", ".avioniq"));
        Directory.CreateDirectory(Path.Combine(_root, "resources"));
        Write(Path.Combine(Owner, "texts", "KJV", "words.essx"));

        _app = DeskApplication.Build(
        [
            "--Urls=http://127.0.0.1:0",
            $"--Desk:Repository={Path.Combine(_root, "repository")}",
            $"--Dataset:ResourcesPath={Path.Combine(_root, "resources")}",
            $"--Desk:Workspace={Path.Combine(_root, "workspace")}",
            $"--Desk:Cache={Path.Combine(_root, "cache")}",
            "--Database:ConnectionString=Host=localhost;Port=5437;Database=unused;Username=unused",
            "--Database:Password=unused",
        ], contentRoot: _root);
        await _app.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    private static void Write(string path)
    {
        using var writer = new EssxFormat(new EssxOptions { RowGroupRows = 100 }).Create(path, new ProductHeader("Essenthos/texts/KJV/words.essx",
            [new ProductInput("bible4u/kjv.xml", new string('a', 64))], "test"), Schema);
        writer.WriteAll(Enumerable.Range(0, 1000).Select(i =>
            new Word(new WordKey("KJV", new VerseKey(1 + i / 500, 1 + i / 50 % 10, "", 1 + i / 5 % 10, ""), 1 + i % 5), Surfaces[i % 5], i)));
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        Directory.Delete(_root, recursive: true);
    }

    private static async Task<T> Json<T>(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(Web))!;
    }

    [Fact]
    public async Task ADroppedFileShowsItsStructureRowsAndTimingsAndGoesWhenClosed()
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(Owner, "texts", "KJV", "words.essx"));
        var view = await Json<EssxFileView>(await _http.PostAsync("/desk-api/essx/files?name=words.essx", new ByteArrayContent(bytes)));
        view.Name.Should().Be("words.essx");
        view.Bytes.Should().Be(bytes.Length);
        view.Header.Inputs.Should().ContainSingle().Which.Path.Should().Be("bible4u/kjv.xml");
        var table = view.Tables.Should().ContainSingle().Subject;
        table.Rows.Should().Be(1000);
        table.KeyColumn.Should().Be("own");
        table.Groups.Should().HaveCount(10);
        table.Groups[0].Books.Should().Be("1");
        table.Columns.Select(c => c.Type).Should().Equal("wordkey", "text", "integer");
        table.Columns[1].Physical.Single().Distinct.Should().Be(5);
        view.Layout.Preamble.Should().Be(8);
        (view.Layout.Preamble + view.Layout.Chunks + view.Layout.Dictionaries + view.Layout.Footer + view.Layout.Trailer).Should().Be(bytes.Length);

        var window = await Json<EssxRowsView>(await _http.GetAsync($"/desk-api/essx/files/{view.Id}/rows?offset=95&limit=10&columns=own,count"));
        window.Total.Should().Be(1000);
        window.Columns.Should().Equal("own", "count");
        window.RowNumbers.Should().Equal(Enumerable.Range(95, 10).Select(i => (long)i));
        window.Rows[6].Should().Equal("1.3:1.2", "101");

        var verse = await Json<EssxRowsView>(await _http.GetAsync($"/desk-api/essx/files/{view.Id}/rows?verse=2.3:4&search=god"));
        verse.Filtered.Should().BeTrue();
        verse.Total.Should().Be(1);
        verse.Rows.Single()[1].Should().Be("God");

        var dictionary = await Json<EssxDictionaryView>(await _http.GetAsync($"/desk-api/essx/files/{view.Id}/dictionary?physical=surface&search=e"));
        dictionary.Count.Should().Be(5);
        dictionary.Values.Select(v => v.Value).Should().Equal("the", "beginning", "created");

        var check = await Json<EssxVerifyView>(await _http.GetAsync($"/desk-api/essx/files/{view.Id}/verify"));
        check.Ok.Should().BeTrue();
        check.StoredSha256.Should().Be(view.StoredSha256);

        var features = await Json<EssxFeaturesView>(await _http.PostAsync($"/desk-api/essx/files/{view.Id}/features?head=20", null));
        features.Features.Select(f => f.Key).Should().Equal("open", "footer", "column", "verse", "head", "full", "verify");
        features.Features.Should().OnlyContain(f => f.Runs == 5 && f.BestMs <= f.MedianMs);
        features.Column.Should().Be("surface");

        (await _http.DeleteAsync($"/desk-api/essx/files/{view.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _http.GetAsync($"/desk-api/essx/files/{view.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        Directory.EnumerateFiles(Path.Combine(_root, "cache", "essx")).Should().BeEmpty();
    }

    [Fact]
    public async Task AProductUnderResourcesOpensByItsPathAndAnythingElseIsRefused()
    {
        var products = await Json<EssxProductsResponse>(await _http.GetAsync("/desk-api/essx/products"));
        products.Products.Should().ContainSingle().Which.Path.Should().Be("texts/KJV/words.essx");

        var view = await Json<EssxFileView>(await _http.PostAsJsonAsync("/desk-api/essx/open", new EssxOpenRequest("texts/KJV/words.essx")));
        view.Path.Should().EndWith("words.essx");
        (await _http.PostAsJsonAsync("/desk-api/essx/open", new EssxOpenRequest("../../outside.essx"))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var notEssx = await _http.PostAsync("/desk-api/essx/files?name=x.essx", new ByteArrayContent("not a product file at all, just some text bytes here........................"u8.ToArray()));
        notEssx.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Directory.EnumerateFiles(Path.Combine(_root, "cache", "essx")).Should().BeEmpty("a file that cannot be read is not kept");
    }
}
