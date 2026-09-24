using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Swete;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// Swete's Isaiah arriving in a corpus that already holds the rest of Swete, and Ottley's beside it:
/// the book goes into the text already there, the frame places it, and the letter links reach it
/// without the books already linked being linked again.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class IsaiahLoadTests : IDisposable
{
    private const int Genesis = 1;

    private const int Isaiah = 23;

    private const int Jeremiah = 24;

    private readonly AppDbContext _db;
    private readonly ITestOutputHelper _output;

    public IsaiahLoadTests(WitnessDatabase database, ITestOutputHelper output)
    {
        _db = database.NewContext();
        _output = output;
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    private CorpusLoader Loader => new(_db, NullLogger<CorpusLoader>.Instance);

    private static TextSource Books(TextSource source, params int[] ordinals) =>
        new(source.Definition, [.. source.Books.Where(book => ordinals.Contains(book.CanonicalOrdinal))]);

    /// <summary>
    /// Swete as a corpus loaded before Isaiah could be read holds it: Jeremiah where Isaiah now stands,
    /// and a rights note that does not yet mention Isaiah.
    /// </summary>
    private async Task<TextSource> SweteWithoutIsaiah()
    {
        var swete = SweteTextSource.Read(TestResources.SweteFolder);
        var before = swete.Definition with
        {
            RightsNote = swete.Definition.RightsNote![..^(SweteIsaiah.Note.Length + 1)],
        };

        await Loader.Load(new TextSource(before,
        [
            swete.Books.Single(book => book.CanonicalOrdinal == Genesis),
            swete.Books.Single(book => book.CanonicalOrdinal == Jeremiah) with { Position = 44 },
        ]));

        return Books(swete, Genesis, Isaiah, Jeremiah);
    }

    private async Task Place(string slug)
    {
        var rules = TvtmsReader.Read(TestResources.Tvtms);
        var text = await _db.Texts.SingleAsync(t => t.Slug == slug);
        await new CanonicalFrameLoader(_db, NullLogger<CanonicalFrameLoader>.Instance).Place(text, rules);
    }

    [Fact]
    public async Task ABookTheSourceGainedGoesIntoTheTextAlreadyLoaded()
    {
        var swete = await SweteWithoutIsaiah();
        var genesisWords = await _db.Words.Where(w => w.Verse!.Book!.CanonicalOrdinal == Genesis)
            .Select(w => w.Id).OrderBy(id => id).ToListAsync();

        var added = await Loader.AddMissingBooks(swete);

        added.Books.Should().Equal("Isaiah");
        added.Verses.Should().Be(1289);
        added.Words.Should().Be(26971);

        var books = await _db.Books.OrderBy(b => b.Position)
            .Select(b => new { b.CanonicalOrdinal, b.Position }).ToListAsync();
        books.Select(b => (b.CanonicalOrdinal, b.Position))
            .Should().Equal((Genesis, 1), (Isaiah, 44), (Jeremiah, 45));

        (await _db.Words.Where(w => w.Verse!.Book!.CanonicalOrdinal == Genesis)
                .Select(w => w.Id).OrderBy(id => id).ToListAsync())
            .Should().Equal(genesisWords, "the books already there keep every row");

        (await _db.Texts.SingleAsync()).RightsNote.Should().Be(SweteTextSource.Definition.RightsNote);

        (await Loader.AddMissingBooks(swete)).Books.Should().BeEmpty();
    }

    /// <summary>
    /// The pair was linked before Isaiah arrived; linking again reaches Isaiah and leaves every link
    /// of the book already linked where it was.
    /// </summary>
    [Fact]
    public async Task TheLettersAreLinkedInTheNewBookAndOnlyThere()
    {
        var swete = await SweteWithoutIsaiah();
        await Loader.Load(Books(SeptuagintTextSource.Read(TestResources.SeptuagintFolder), Genesis, Isaiah, Jeremiah));
        await Place(SweteTextSource.Slug);
        await Place(SeptuagintTextSource.Slug);

        var links = new SeptuagintLinkLoader(_db, NullLogger<SeptuagintLinkLoader>.Instance);
        var first = await links.Load(SweteTextSource.Slug, SeptuagintTextSource.Slug);
        var before = await _db.Links.Select(l => l.Id).OrderBy(id => id).ToListAsync();

        await Loader.AddMissingBooks(swete);
        await Place(SweteTextSource.Slug);
        var second = await links.Load(SweteTextSource.Slug, SeptuagintTextSource.Slug);
        _output.WriteLine($"Swete-Brenton, Isaiah: {second}");

        first.ByBook.Select(b => b.Book).Should().NotContain("Isaiah");
        second.ByBook.Select(b => b.Book).Should().Equal("Isaiah");
        (await _db.Links.Select(l => l.Id).Where(id => id <= before.Max()).OrderBy(id => id).ToListAsync())
            .Should().Equal(before);

        (await links.Load(SweteTextSource.Slug, SeptuagintTextSource.Slug)).AlreadyLoaded.Should().BeTrue();

        await Loader.Load(OttleyTextSource.Read(TestResources.SweteFolder));
        await Place(OttleyTextSource.Slug);
        var ottley = await links.Load(OttleyTextSource.Slug, SweteTextSource.Slug);
        _output.WriteLine($"Ottley-Swete: {ottley}");

        ottley.Addresses.Should().Be(1288);
        ottley.Identical.Should().BeGreaterThan(ottley.Links / 2);
    }

    /// <summary>
    /// Ottley's Isaiah numbers as the English does and the frame is the English numbering, so it
    /// stands where it says it does; 40:8 is one verse holding what the others call 40:7 and 40:8.
    /// </summary>
    [Fact]
    public async Task OttleyStandsAtItsOwnAddresses()
    {
        var outcome = await Loader.Load(OttleyTextSource.Read(TestResources.SweteFolder));
        await Place(OttleyTextSource.Slug);

        outcome.Words.Should().Be(27162);
        var moved = await _db.VerseReferences
            .Where(r => r.IsPrimary && (r.CanonicalChapter != r.Verse!.ChapterNumber || r.CanonicalVerse != r.Verse.Number))
            .CountAsync();
        moved.Should().Be(0);
        (await _db.Texts.SingleAsync()).Kind.Should().Be(TextKind.ManuscriptTradition);
    }
}
