using System.Net;
using System.Security.Cryptography;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The smaller copies the API makes itself: made on the first request for one, kept and served from
/// the cache after, and never made for an address that does not name a picture's own bytes at one of
/// the widths the API copies at.
/// </summary>
public sealed class PictureCopyTests : IAsyncLifetime
{
    private const int PictureWidth = 600;

    private const int PictureHeight = 400;

    private readonly string _scratch = Directory.CreateTempSubdirectory("picture-copies-").FullName;

    private string _pictures = "";

    private string _cache = "";

    private string _digest = "";

    private byte[] _picture = [];

    private WebApplication? _app;

    private HttpClient _http = new();

    public async Task InitializeAsync()
    {
        _pictures = Path.Combine(_scratch, "Images");
        _cache = Path.Combine(_scratch, "cache");
        _picture = Picture(PictureWidth, PictureHeight);
        _digest = Digest(_picture);
        Write("generated/aaron.png", _picture);
        File.WriteAllBytes(Path.Combine(_scratch, "secret.png"), _picture);
        (_app, _http) = await Start(_cache);
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        Directory.Delete(_scratch, recursive: true);
    }

    [Fact]
    public async Task ACopyIsMadeOnTheFirstRequestAtTheNarrowestWidthAsWideAsAsked()
    {
        var response = await _http.GetAsync($"/v1/images/generated/aaron.png?v={_digest}&w=100");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
        response.Headers.CacheControl!.ToString().Should().Contain("immutable");
        using var copy = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync());
        (copy.Width, copy.Height).Should().Be((128, 85), "a copy keeps the picture's proportions");
        File.Exists(Path.Combine(_cache, "128", "generated", $"aaron.{_digest}.webp")).Should().BeTrue();
    }

    [Fact]
    public async Task AKeptCopyIsServedFromTheCacheAfter()
    {
        (await _http.GetAsync($"/v1/images/generated/aaron.png?v={_digest}&w=200")).EnsureSuccessStatusCode();
        var kept = Path.Combine(_cache, "256", "generated", $"aaron.{_digest}.webp");
        byte[] marked = [.. File.ReadAllBytes(kept), 1, 2, 3];
        File.WriteAllBytes(kept, marked);

        (await _http.GetByteArrayAsync($"/v1/images/generated/aaron.png?v={_digest}&w=256")).Should().Equal(marked);
    }

    [Fact]
    public async Task ACopyMadeAheadBesideThePicturesIsServedAndNoneIsMade()
    {
        byte[] premade = [9, 9, 9];
        Write($"sized/512/generated/aaron.{_digest}.webp", premade);

        (await _http.GetByteArrayAsync($"/v1/images/generated/aaron.png?v={_digest}&w=300")).Should().Equal(premade);
        Directory.Exists(Path.Combine(_cache, "512")).Should().BeFalse();
    }

    [Fact]
    public async Task ManyRequestsAtOnceForOneCopyMakeItOnce()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => _http.GetByteArrayAsync($"/v1/images/generated/aaron.png?v={_digest}&w=512")));

        responses.Should().AllSatisfy(bytes => bytes.Should().Equal(responses[0]));
        Directory.GetFiles(Path.Combine(_cache, "512"), "*", SearchOption.AllDirectories).Should().ContainSingle()
            .Which.Should().EndWith($"aaron.{_digest}.webp", "a half-written copy is never left behind");
    }

    [Theory]
    [InlineData("fedcba987654")]
    [InlineData("0123456789AB")]
    [InlineData("0123")]
    [InlineData(null)]
    public async Task NoCopyIsMadeUnderADigestThatIsNotThePictures(string? digest)
    {
        var response = await _http.GetAsync($"/v1/images/generated/aaron.png?w=128{(digest is null ? "" : $"&v={digest}")}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(_picture, "the picture itself is served");
        response.Headers.CacheControl!.ToString().Should().NotContain("immutable");
        Directory.Exists(_cache).Should().BeFalse();
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(5000)]
    public async Task NoCopyIsMadeWiderThanThePictureOrPastTheWidestWidth(int width)
    {
        var response = await _http.GetAsync($"/v1/images/generated/aaron.png?v={_digest}&w={width}");

        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(_picture);
        response.Headers.CacheControl!.ToString().Should().Contain("immutable", "the digest is the picture's, so it is its own copy");
        Directory.Exists(_cache).Should().BeFalse();
    }

    [Fact]
    public async Task ACopyIsNeverCopiedAgain()
    {
        var premade = Picture(300, 200);
        Write($"sized/512/generated/aaron.{_digest}.webp", premade);

        (await _http.GetAsync($"/v1/images/sized/512/generated/aaron.{_digest}.webp?v={Digest(premade)}&w=128"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        Directory.Exists(_cache).Should().BeFalse();
    }

    [Theory]
    [InlineData("..%2Fsecret.png")]
    [InlineData("..%5Csecret.png")]
    [InlineData("generated/..%2F..%2Fsecret.png")]
    [InlineData("generated/%2E%2E/%2E%2E/secret.png")]
    public async Task NothingOutsideThePicturesFolderIsServedOrCopied(string file)
    {
        (await _http.GetAsync($"/v1/images/{file}?v={_digest}&w=128")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        Directory.Exists(_cache).Should().BeFalse();
    }

    [Fact]
    public async Task WhereNoCopyCanBeKeptThePictureItselfIsServed()
    {
        var unwritable = Path.Combine(_scratch, "not-a-folder");
        File.WriteAllBytes(unwritable, [0]);
        var (app, http) = await Start(unwritable);
        await using (app)
        using (http)
        {
            var response = await http.GetAsync($"/v1/images/generated/aaron.png?v={_digest}&w=128");

            (await response.Content.ReadAsByteArrayAsync()).Should().Equal(_picture);
            response.Headers.CacheControl!.ToString().Should().NotContain("immutable");
        }
    }

    private async Task<(WebApplication App, HttpClient Http)> Start(string cache)
    {
        var builder = WebApplication.CreateSlimBuilder(["--urls=http://127.0.0.1:0"]);
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(services =>
            new PictureCopies(_pictures, cache, services.GetRequiredService<ILogger<PictureCopies>>()));
        var app = builder.Build();
        app.MapGroup("/v1").MapImages(app.Services.GetRequiredService<PictureCopies>());
        await app.StartAsync();
        return (app, new HttpClient { BaseAddress = new Uri(app.Urls.First()) });
    }

    private void Write(string file, byte[] bytes)
    {
        var path = Path.Combine(_pictures, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>A picture with detail in it, so a copy of it is a real resize rather than a flat colour.</summary>
    private static byte[] Picture(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                bitmap.SetPixel(x, y, new SKColor((byte)(x * 255 / width), (byte)(y * 255 / height), (byte)((x ^ y) & 0xff)));
            }
        }

        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes))[..12];
}
