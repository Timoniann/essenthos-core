using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>Codex Alexandrinus loaded whole and placed in the frame.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class AlexandrinusLoadTests : IDisposable
{
    private const int FirstNewTestamentBook = 40;

    private const int LastNewTestamentBook = 66;

    private readonly AppDbContext _db;
    private readonly ITestOutputHelper _output;

    public AlexandrinusLoadTests(WitnessDatabase database, ITestOutputHelper output)
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

    [Fact]
    public async Task EveryVerseStandsInTheFrameAndTheNewTestamentWhereTheGreekNumbersIt()
    {
        var outcome = await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance)
            .Load(AlexandrinusTextSource.Read(TestResources.Folder(".")));
        _output.WriteLine(outcome.ToString());

        var text = await _db.Texts.SingleAsync();
        await new CanonicalFrameLoader(_db, NullLogger<CanonicalFrameLoader>.Instance)
            .Place(text, TvtmsReader.Read(TestResources.Tvtms));

        var unplaced = await _db.Verses.CountAsync(verse => !_db.VerseReferences.Any(r => r.VerseId == verse.Id && r.IsPrimary));
        var moved = await _db.VerseReferences
            .Where(r => r.IsPrimary && r.Verse!.Book!.CanonicalOrdinal >= FirstNewTestamentBook
                        && r.Verse.Book.CanonicalOrdinal <= LastNewTestamentBook
                        && (r.CanonicalChapter != r.Verse.ChapterNumber || r.CanonicalVerse != r.Verse.Number))
            .Select(r => r.Verse!.Book!.Slug + " " + r.Verse.ChapterNumber + ":" + r.Verse.Number
                         + " -> " + r.CanonicalChapter + ":" + r.CanonicalVerse)
            .ToListAsync();
        _output.WriteLine($"unplaced {unplaced}; New Testament verses placed elsewhere: {string.Join(", ", moved)}");

        unplaced.Should().Be(0);
        (await _db.Verses.CountAsync(verse => verse.Book!.CanonicalOrdinal >= FirstNewTestamentBook
                                               && verse.Book.CanonicalOrdinal <= LastNewTestamentBook)).Should().Be(6787);
        // Where the Greek numbers a verse otherwise than the English the frame is written in, and
        // nowhere else.
        moved.Should().BeEquivalentTo(
            "3-john 1:15 -> 1:14", "2-corinthians 13:13 -> 13:14", "philippians 1:17 -> 1:16",
            "philippians 1:16 -> 1:17", "revelation 12:18 -> 13:1");
    }

    /// <summary>
    /// The codex is joined verse by verse to Nestle in the New Testament and to both Septuagints in
    /// the Old before any of its words are aligned, wherever the frame puts two verses at one address.
    /// </summary>
    [Fact]
    public async Task ItIsJoinedToTheGreekVerseByVerse()
    {
        const int genesis = 1, mark = 41;
        var alexandrinus = AlexandrinusTextSource.Read(TestResources.Folder("."));
        await Load(new TextSource(alexandrinus.Definition,
        [
            .. alexandrinus.Books.Where(book => book.CanonicalOrdinal is genesis or mark)
                .Select(book => book with { Chapters = [book.Chapters[0]] }),
        ]));
        await Load(Tiny(NestleTextSource.Definition, (mark, 45)));
        await Load(Tiny(SeptuagintTextSource.Definition(), (genesis, 31)));
        await Load(Tiny(SweteTextSource.Definition, (genesis, 31)));

        var rules = TvtmsReader.Read(TestResources.Tvtms);
        var placer = new CanonicalFrameLoader(_db, NullLogger<CanonicalFrameLoader>.Instance);
        foreach (var text in await _db.Texts.ToListAsync())
        {
            await placer.Place(text, rules);
        }

        var outcome = await new VerseLinkLoader(_db, NullLogger<VerseLinkLoader>.Instance).Load();
        _output.WriteLine(outcome.ToString());

        outcome.Pairs.Should().Be(3);
        var joined = await _db.VerseLinkVerses
            .Where(member => member.Verse!.Text!.Slug == AlexandrinusTextSource.Slug
                             && member.VerseLink!.Method == LinkMethod.StatedBySource)
            .Select(member => new { member.Verse!.Book!.CanonicalOrdinal, member.Verse.Number, To = member.VerseLink!.ToText!.Slug })
            .ToListAsync();

        joined.Where(verse => verse.CanonicalOrdinal == mark).Select(verse => verse.Number).Distinct()
            .Should().HaveCount(45);
        joined.Where(verse => verse.CanonicalOrdinal == genesis).Select(verse => verse.To).Distinct()
            .Should().BeEquivalentTo(Sources.BrentonSeptuagintSlug, Sources.SweteSlug);
    }

    private async Task Load(TextSource source) =>
        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance).Load(source);

    /// <summary>A text of one-word verses in the first chapter of each book named.</summary>
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
