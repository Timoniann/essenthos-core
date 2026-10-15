using Essenthos.Core.BetaMasaheft;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Ge'ez numbers a third of the chapters it shares with the Greek its own way. Its 1 Chronicles 7
/// runs to 44 where the Greek runs to 40, and its 7:34, the sons of Asher, is the Greek 7:30: the
/// reading in <c>Resources/Essenthos/versification/geez.tsv</c> says so, and the frame stands it there.
/// </summary>
public sealed class GeezVersificationTests
{
    private const int Numbers = 4;

    private const int FirstChronicles = 13;

    [Fact]
    public void TheReadingPutsFirstChroniclesSevenWhereTheGreekNumbersIt()
    {
        GeezVersification.ByVerse[(FirstChronicles, 7, 34)].Greek.Should().Equal((7, 30));
        GeezVersification.ByVerse[(FirstChronicles, 7, 41)].Greek.Should().Equal((7, 37));
        var third = GeezVersification.ByVerse[(FirstChronicles, 7, 3)];
        third.Relation.Should().Be("part-of");
        third.Greek.Should().Equal((7, 2));
        GeezVersification.ByVerse.Should().NotContainKey((FirstChronicles, 7, 1), "the chapter's first verse is where its number is");
    }

    [Fact]
    public void TheReadingLeavesTheBooksTheChurchDividesItsOwnWayToTheirMap()
    {
        GeezVersification.Lines.Should().NotBeEmpty()
            .And.OnlyContain(line => !GeezVerseMap.Books.Contains(line.Book))
            .And.OnlyContain(line => line.Share == null || (line.Share >= 0 && line.Share <= 1));
    }

    [Fact]
    public void ALineNamingNoGreekVerseMustSayTheGreekHasNone()
    {
        var parse = () => GeezVersification.Parse("13\t7:34\t-\tequals\t-\t0\n");

        parse.Should().Throw<FormatException>();
        GeezVersification.Parse("13\t7:9\t7:7;7:8-9\tspans\t0.75\t4\n").Single().Greek
            .Should().Equal((7, 7), (7, 8), (7, 9));
    }

    /// <summary>
    /// A verse the reading lists stands at the row of the Greek verse it answers; one the Greek has
    /// nothing for — the Ge'ez Numbers 1:4, the end of the Greek 1:3 — stands with the verse before it.
    /// </summary>
    [Fact]
    public void TheFrameStandsAListedVerseWhereTheReadingPutsIt()
    {
        PlacedVerse[] verses =
        [
            .. Enumerable.Range(1, 44).Select(verse => new PlacedVerse(verse, FirstChronicles, 7, verse, string.Empty, 50)),
            .. Enumerable.Range(1, 6).Select(verse => new PlacedVerse(100 + verse, Numbers, 1, verse, string.Empty, 50)),
        ];
        var rules = new VersificationRules([], new HashSet<TvtmsCorrection>(), new HashSet<TvtmsSupplement>(),
            new Dictionary<AdditionVerse, CanonicalReference>());

        var placed = CanonicalFrameLoader.Expected(rules, Versification.Septuagint, verses, read: GeezVersification.Placements)
            .Where(reference => reference.IsPrimary)
            .ToDictionary(reference => reference.VerseId, reference => (reference.Book, reference.Chapter, reference.Verse));

        placed[1].Should().Be((FirstChronicles, 7, 1));
        placed[34].Should().Be((FirstChronicles, 7, 30));
        placed[44].Should().Be((FirstChronicles, 7, 40));
        placed[104].Should().Be((Numbers, 1, 3));
        placed[105].Should().Be((Numbers, 1, 4));
    }
}

/// <summary>
/// The verse links of a chapter the reading places are the reading's, with its share as their
/// confidence, and the frame states the rest; a corpus joined by the Ge'ez's own numbers before the
/// reading existed is joined again once.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class GeezVersificationLinkTests : IDisposable
{
    private const int FirstChronicles = 13;

    private readonly AppDbContext _db;

    public GeezVersificationLinkTests(WitnessDatabase database)
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
    public async Task AVersePlacedByTheReadingIsJoinedAsTheReadingAndTheRestByTheFrame()
    {
        await LoadFirstChroniclesSeven();

        var outcome = await new VerseLinkLoader(_db, NullLogger<VerseLinkLoader>.Instance).Load();

        outcome.AlreadyLoaded.Should().BeFalse();
        var asher = await Joined(34);
        asher.Greek.Should().Equal(30);
        asher.Method.Should().Be(LinkMethod.Aligner);
        asher.Source.Should().Be(GeezVersification.Source);
        asher.Confidence.Should().Be(GeezVersification.ByVerse[(FirstChronicles, 7, 34)].Share);
        (await Joined(41)).Greek.Should().Equal(37);
        (await Joined(3)).Greek.Should().Equal(2);
        var first = await Joined(1);
        first.Greek.Should().Equal(1);
        first.Method.Should().Be(LinkMethod.StatedBySource);

        var count = await _db.VerseLinks.CountAsync();
        (await new VerseLinkLoader(_db, NullLogger<VerseLinkLoader>.Instance).Load()).AlreadyLoaded.Should().BeTrue();
        (await _db.VerseLinks.CountAsync()).Should().Be(count, "a second load finds the reading already joined");
    }

    /// <summary>
    /// A corpus joined before the reading paired the Ge'ez 7:34 with the Greek 7:34 by number. The next
    /// load takes that away and joins the chapter as the reading reads it.
    /// </summary>
    [Fact]
    public async Task ACorpusJoinedByTheGeezNumbersIsJoinedAgainOnce()
    {
        await LoadFirstChroniclesSeven();
        var loader = new VerseLinkLoader(_db, NullLogger<VerseLinkLoader>.Instance);
        await loader.Load();
        await _db.VerseLinks.Where(link => link.Source == GeezVersification.Source).ExecuteDeleteAsync();
        _db.VerseLinks.Add(new VerseLink
        {
            FromTextId = await TextId(GeezTextSource.Slug), ToTextId = await TextId(SeptuagintTextSource.Slug),
            Relation = LinkRelation.Equals, Method = LinkMethod.StatedBySource, Source = VerseLinkLoader.Source,
            Verses =
            [
                new VerseLinkVerse { VerseId = await VerseId(GeezTextSource.Slug, 34), Side = LinkSide.From },
                new VerseLinkVerse { VerseId = await VerseId(SeptuagintTextSource.Slug, 34), Side = LinkSide.To },
            ],
        });
        await _db.SaveChangesAsync();

        await loader.Load();

        var asher = await Joined(34);
        asher.Greek.Should().Equal(30);
        asher.Method.Should().Be(LinkMethod.Aligner);
        var count = await _db.VerseLinks.CountAsync();
        (await loader.Load()).AlreadyLoaded.Should().BeTrue();
        (await _db.VerseLinks.CountAsync()).Should().Be(count);
    }

    private async Task LoadFirstChroniclesSeven()
    {
        var geez = GeezTextSource.Read(TestResources.Folder(GeezTextSource.Folder));
        await Load(new TextSource(geez.Definition,
        [
            .. geez.Books.Where(book => book.CanonicalOrdinal == FirstChronicles)
                .Select(book => book with { Chapters = [.. book.Chapters.Where(chapter => chapter.Number == 7)] }),
        ]));
        await Load(new TextSource(SeptuagintTextSource.Definition(),
        [
            new BookDraft(FirstChronicles, 1, BookReferences.Name(FirstChronicles), BookReferences.Slug(FirstChronicles),
            [
                new ChapterDraft(7,
                [
                    .. Enumerable.Range(1, 40).Select(number => new VerseDraft(number, [new WordDraft("λόγος", "")])),
                ]),
            ]),
        ]));

        var rules = TvtmsReader.Read(TestResources.Tvtms);
        var placer = new CanonicalFrameLoader(_db, NullLogger<CanonicalFrameLoader>.Instance);
        foreach (var text in await _db.Texts.ToListAsync())
        {
            await placer.Place(text, rules);
        }
    }

    private async Task<(List<int> Greek, LinkMethod Method, string Source, double? Confidence)> Joined(int geezVerse)
    {
        var link = await _db.VerseLinks
            .Where(link => link.FromText!.Slug == GeezTextSource.Slug
                           && link.ToText!.Slug == SeptuagintTextSource.Slug
                           && link.Verses.Any(member => member.Side == LinkSide.From && member.Verse!.Number == geezVerse))
            .Select(link => new
            {
                Greek = link.Verses.Where(member => member.Side == LinkSide.To).Select(member => member.Verse!.Number).ToList(),
                link.Method,
                link.Source,
                link.Confidence,
            })
            .SingleAsync();
        return (link.Greek, link.Method, link.Source, link.Confidence);
    }

    private async Task<int> TextId(string slug) => await _db.Texts.Where(text => text.Slug == slug).Select(text => text.Id).SingleAsync();

    private async Task<int> VerseId(string slug, int number) =>
        await _db.Verses.Where(verse => verse.Text!.Slug == slug && verse.Number == number).Select(verse => verse.Id).SingleAsync();

    private async Task Load(TextSource source) =>
        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance).Load(source);
}
