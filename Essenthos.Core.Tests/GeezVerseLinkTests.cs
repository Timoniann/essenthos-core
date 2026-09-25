using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Ethiopic arrives linked to nothing word by word, and is joined verse by verse to the texts it
/// is read beside wherever the frame puts two verses at one address — except in the books it divides
/// in its own way, where a shared address would be a guess.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class GeezVerseLinkTests : IDisposable
{
    private const int Genesis = 1;

    private const int Esther = 17;

    private const int Matthew = 40;

    private readonly AppDbContext _db;

    public GeezVerseLinkTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    [Fact]
    public async Task TheEthiopicIsJoinedToTheHebrewAndTheGreekButNotInABookItDividesItsOwnWay()
    {
        var geez = GeezTextSource.Read(TestResources.Folder(GeezTextSource.Folder));
        await Load(new TextSource(geez.Definition,
        [
            .. geez.Books.Where(book => book.CanonicalOrdinal is Genesis or Esther or Matthew)
                .Select(book => book with { Chapters = [book.Chapters[0]] }),
        ]));
        await Load(Tiny(BhsaTextSource.Definition, (Genesis, 3), (Esther, 3)));
        await Load(Tiny(NestleTextSource.Definition, (Matthew, 3)));

        var rules = TvtmsReader.Read(TestResources.Tvtms);
        var placer = new CanonicalFrameLoader(_db, NullLogger<CanonicalFrameLoader>.Instance);
        foreach (var text in await _db.Texts.ToListAsync())
        {
            await placer.Place(text, rules);
        }

        var outcome = await new VerseLinkLoader(_db, NullLogger<VerseLinkLoader>.Instance).Load();

        outcome.Pairs.Should().Be(2, "Swete and Brenton are not loaded here, and a pair with a text absent says nothing");
        var joined = await _db.VerseLinkVerses
            .Where(member => member.Verse!.Text!.Slug == GeezTextSource.Slug)
            .Select(member => new { member.Verse!.Book!.CanonicalOrdinal, member.Verse.ChapterNumber, member.Verse.Number })
            .ToListAsync();

        joined.Should().Contain(verse => verse.CanonicalOrdinal == Genesis && verse.ChapterNumber == 1 && verse.Number == 1);
        joined.Should().Contain(verse => verse.CanonicalOrdinal == Matthew && verse.ChapterNumber == 1 && verse.Number == 1);
        joined.Should().NotContain(verse => verse.CanonicalOrdinal == Esther);

        (await new VerseLinkLoader(_db, NullLogger<VerseLinkLoader>.Instance).Load()).AlreadyLoaded.Should().BeTrue();
    }

    private async Task Load(TextSource source) =>
        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance).Load(source);

    /// <summary>A text of a few one-word verses in the first chapter of each book named.</summary>
    private static TextSource Tiny(TextDefinition definition, params (int Ordinal, int Verses)[] books) =>
        new(definition,
        [
            .. books.Select((book, index) => new BookDraft(
                book.Ordinal,
                index + 1,
                BookReferences.Name(book.Ordinal),
                BookReferences.Slug(book.Ordinal),
                [
                    new ChapterDraft(1,
                    [
                        .. Enumerable.Range(1, book.Verses)
                            .Select(number => new VerseDraft(number, [new WordDraft("word", "")])),
                    ]),
                ])),
        ]);
}
