using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Synodal matched to the Hebrew by numbers that are held in memory for the run and never
/// stored, against a corpus where the aligner has already spoken about the same words.
///
/// The shapes that must survive being rows: the numbers never reach the words; a guess the numbers
/// confirm becomes a claim on the match rather than a second link; a guess they refute is gone; a
/// guess about a word they say nothing of stays; and a source's statement is never overwritten.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SynodalStrongLinkTests : IDisposable
{
    private const string Aligner = "SIL.Machine, aligned as written";

    private const string Credit = "a numbering under test";

    private readonly AppDbContext _db;
    private readonly TaggedTextLinkLoader _loader;
    private readonly Text _russian;
    private readonly Text _hebrew;

    public SynodalStrongLinkTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _loader = new TaggedTextLinkLoader(_db, NullLogger<TaggedTextLinkLoader>.Instance);

        _russian = Corpus.Add(_db, "RUSV", TextKind.Translation, "rus",
            (1, 1, ["В", "начале", "сотворил", "Бог", "небо", "и", "землю"]));
        _hebrew = Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo",
            (1, 1, ["בְּ", "רֵאשִׁית", "בָּרָא", "אֱלֹהִים", "אֵת", "הַ", "שָּׁמַיִם"]));
        _db.SaveChanges();

        var hebrew = _db.Words.Where(w => w.TextId == _hebrew.Id).OrderBy(w => w.Position).ToList();
        string?[] numbers = ["H9003", "H7225", "H1254", "H430", "H853", "H9009", "H8064"];
        for (var at = 0; at < numbers.Length; at++)
        {
            hebrew[at].StrongNumber = numbers[at];
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    private Word Russian(int position) => _db.WordAt(_russian, 1, 1, position);

    private Word Hebrew(int position) => _db.WordAt(_hebrew, 1, 1, position);

    /// <summary>The edition's numbers, by the Russian word's position: начале, сотворил, Бог, небо.</summary>
    private EditionNumbers Numbers(params (int Position, string Number, int Unit)[] tags) =>
        new(tags.ToDictionary(
                tag => Russian(tag.Position).Id,
                tag => new WordTag([tag.Number], tag.Unit)),
            Credit);

    private EditionNumbers Genesis() =>
        Numbers((2, "H7225", 2), (3, "H1254", 3), (4, "H430", 4), (5, "H8064", 5));

    private Link Drawn(LinkMethod method, int russian, int hebrew)
    {
        var link = new Link
        {
            FromTextId = _russian.Id,
            ToTextId = _hebrew.Id,
            Relation = LinkRelation.Renders,
            Method = method,
            Source = method == LinkMethod.Aligner ? Aligner : "a source under test",
            Confidence = method == LinkMethod.Aligner ? 0.6 : null,
        };
        link.Words.Add(new LinkWord { WordId = Russian(russian).Id, Side = LinkSide.From });
        link.Words.Add(new LinkWord { WordId = Hebrew(hebrew).Id, Side = LinkSide.To });
        _db.Links.Add(link);
        _db.SaveChanges();

        _db.LinkClaims.Add(new LinkClaim
        {
            LinkId = link.Id, Method = link.Method, Confidence = link.Confidence, Source = link.Source,
        });
        _db.SaveChanges();
        return link;
    }

    private async Task<List<Link>> Links() =>
        await _db.Links
            .AsNoTracking()
            .Include(l => l.Words)
            .Include(l => l.Claims)
            .Where(l => l.FromTextId == _russian.Id && l.ToTextId == _hebrew.Id)
            .ToListAsync();

    private static bool Names(Link link, Word word) => link.Words.Any(w => w.WordId == word.Id);

    /// <summary>
    /// The numbering's terms allow it to be used only unmodified, so nothing of it may land on the
    /// Synodal's words — not the column and not the table of further numbers. Only links.
    /// </summary>
    [Fact]
    public async Task TheNumbersReachTheLinksAndNeverTheWords()
    {
        var outcome = await _loader.Load(_russian.Slug, _hebrew.Slug, Genesis());

        outcome.Links.Should().Be(4);
        (await Links()).Should().OnlyContain(link =>
            link.Method == LinkMethod.StrongNumber && link.Source.StartsWith(Credit));
        _db.Words.AsNoTracking().Where(w => w.TextId == _russian.Id).Should().OnlyContain(w => w.StrongNumber == null);
        _db.WordStrongs.Should().BeEmpty();
    }

    /// <summary>
    /// The aligner reaching the same pair is agreement, and agreement is a second claim on one link,
    /// not a second link the integrity check counts as a duplicate.
    /// </summary>
    [Fact]
    public async Task AGuessTheNumbersConfirmBecomesAClaimOnTheMatch()
    {
        var guess = Drawn(LinkMethod.Aligner, russian: 4, hebrew: 4);

        var outcome = await _loader.Load(_russian.Slug, _hebrew.Slug, Genesis());

        outcome.Confirmed.Should().Be(1);
        var links = await Links();
        links.Should().NotContain(link => link.Id == guess.Id);
        var match = links.Single(link => Names(link, Russian(4)));
        match.Method.Should().Be(LinkMethod.StrongNumber);
        match.Claims.Select(claim => claim.Method).Should().BeEquivalentTo([LinkMethod.StrongNumber, LinkMethod.Aligner]);
        match.Claims.Single(claim => claim.Method == LinkMethod.Aligner).Source.Should().Be(Aligner);
    }

    /// <summary>
    /// The aligner joining небо to אֱלֹהִים, which the numbers give to Бог, while the numbers give
    /// небо to שָּׁמַיִם. The number outranks the guess and the guess is removed.
    /// </summary>
    [Fact]
    public async Task AGuessTheNumbersRefuteIsRemoved()
    {
        var guess = Drawn(LinkMethod.Aligner, russian: 5, hebrew: 4);

        var outcome = await _loader.Load(_russian.Slug, _hebrew.Slug, Genesis());

        outcome.Contradicted.Should().Be(1);
        (await Links()).Should().NotContain(link => link.Id == guess.Id);
    }

    /// <summary>
    /// The article is written with no number the Synodal's tags could name, so the aligner joining
    /// небо to it as well says something the numbers are silent about, and it stays. So does a guess
    /// about a word the numbering leaves untagged.
    /// </summary>
    [Fact]
    public async Task AGuessTheNumbersAreSilentAboutStays()
    {
        var article = Drawn(LinkMethod.Aligner, russian: 5, hebrew: 6);
        var untagged = Drawn(LinkMethod.Aligner, russian: 1, hebrew: 1);

        var outcome = await _loader.Load(_russian.Slug, _hebrew.Slug, Genesis());

        outcome.Beside.Should().Be(1);
        var links = await Links();
        links.Should().Contain(link => link.Id == article.Id);
        links.Should().Contain(link => link.Id == untagged.Id);
    }

    /// <summary>
    /// A source's statement is testimony and a match is an inference: where a source already says
    /// what a word renders, no match is written over it, and where the match names the same words
    /// it is recorded as a second claim on the source's link.
    /// </summary>
    [Fact]
    public async Task ASourcesStatementIsNeverOverwritten()
    {
        var same = Drawn(LinkMethod.StatedBySource, russian: 4, hebrew: 4);
        var different = Drawn(LinkMethod.StatedBySource, russian: 5, hebrew: 6);

        var outcome = await _loader.Load(_russian.Slug, _hebrew.Slug, Genesis());

        outcome.Testified.Should().Be(2);
        outcome.Corroborated.Should().Be(1);
        var links = await Links();
        links.Where(link => Names(link, Russian(4))).Should().ContainSingle().Which.Id.Should().Be(same.Id);
        links.Single(link => link.Id == same.Id).Claims.Should().Contain(claim => claim.Method == LinkMethod.StrongNumber);
        links.Where(link => Names(link, Russian(5))).Should().ContainSingle().Which.Id.Should().Be(different.Id);
        links.Single(link => link.Id == different.Id).Claims.Should().ContainSingle();
    }

    /// <summary>
    /// The pair already has the aligner's links, and those are not this loader's — the check that
    /// makes a second run add nothing looks only at links drawn from these numbers.
    /// </summary>
    [Fact]
    public async Task TheAlignersLinksDoNotMakeThePairLookLoaded()
    {
        Drawn(LinkMethod.Aligner, russian: 1, hebrew: 1);

        var first = await _loader.Load(_russian.Slug, _hebrew.Slug, Genesis());
        var again = await _loader.Load(_russian.Slug, _hebrew.Slug, Genesis());

        first.AlreadyLoaded.Should().BeFalse();
        first.Links.Should().Be(4);
        again.AlreadyLoaded.Should().BeTrue();
    }

    /// <summary>
    /// Two words the edition marks as one rendering are one occurrence of the number: сотворил and Бог
    /// tagged together as H1254 against one בָּרָא are settled, not two claimants for one word.
    /// </summary>
    [Fact]
    public async Task TwoWordsOfOneRenderingAreOneOccurrence()
    {
        var outcome = await _loader.Load(_russian.Slug, _hebrew.Slug, Numbers((3, "H1254", 7), (4, "H1254", 7)));

        outcome.Unambiguous.Should().Be(1);
        var link = (await Links()).Single();
        link.Words.Where(w => w.Side == LinkSide.From).Should().HaveCount(2);
        link.Confidence.Should().Be(StrongNumberMatch.Unambiguous);
    }

    /// <summary>
    /// A psalm's title is a verse of its own in the Hebrew and the head of the Synodal's first verse,
    /// which therefore stands at two addresses. The Hebrew title verse is offered to it, or the
    /// title's words have nothing to match against.
    /// </summary>
    [Fact]
    public async Task AVerseIsOfferedTheWitnessAtEveryAddressItStandsAt()
    {
        var title = new Verse
        {
            TextId = _hebrew.Id,
            BookId = _db.Books.Single(b => b.TextId == _hebrew.Id).Id,
            ChapterId = _db.Chapters.Single(c => c.TextId == _hebrew.Id).Id,
            ChapterNumber = 1,
            Number = 0,
        };
        _db.Verses.Add(title);
        _db.VerseReferences.Add(new VerseReference
        {
            Verse = title, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 0, IsPrimary = true,
        });
        _db.Words.Add(new Word
        {
            TextId = _hebrew.Id, Verse = title, Position = 1, Surface = "דָוִד", Trailer = string.Empty,
            StrongNumber = "H1732",
        });
        _db.VerseReferences.Add(new VerseReference
        {
            VerseId = _db.VerseAt(_russian, 1, 1).Id, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 0,
            IsPrimary = false,
        });
        _db.SaveChanges();

        var outcome = await _loader.Load(_russian.Slug, _hebrew.Slug, Numbers((1, "H1732", 1)));

        outcome.Unmatched.Should().Be(0);
        _db.Side((await Links()).Single().Id, LinkSide.To).Select(word => word.Surface).Should().Equal("דָוִד");
    }

    /// <summary>The declaration reaches the links by the words they begin with, so none is published unattributed.</summary>
    [Fact]
    public void TheNumberingIsDeclaredAndItsLinksAreClaimed()
    {
        var source = $"{SynodalStrongLinkLoader.Credit}, matched within the verse against BHSA";

        Datasets.Match(source)!.Obliges.Should().Contain("NonCommercial");
        Datasets.Of(source).Should().Be("bju-synodal-strong");
    }
}
