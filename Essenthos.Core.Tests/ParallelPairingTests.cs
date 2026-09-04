using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Two texts that number the same passage differently, read side by side.
///
/// This is the defect the frame exists to prevent: the reader used to pair verses by number, so
/// asking for Joel 3 put the English third chapter beside the Hebrew third chapter, which is a
/// different passage. Here the offset is built deliberately and the pairing has to survive it.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ParallelPairingTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public ParallelPairingTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task VersesArePairedByWhereTheySitInTheFrameAndNotByTheirNumber()
    {
        // The English text numbers this passage chapter 3; the Hebrew numbers it chapter 4. Both
        // belong at canonical 3, which is the only thing that makes them comparable.
        var english = Corpus.Add(_db, "kjv", TextKind.Translation, "eng", (3, 1, ["For", "behold"]));
        var hebrew = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo", (4, 1, ["כִּי", "הִנֵּה"]));
        _db.SaveChanges();

        Place(english, 3, 1, canonicalChapter: 3, canonicalVerse: 1);
        Place(hebrew, 4, 1, canonicalChapter: 3, canonicalVerse: 1);
        _db.SaveChanges();

        var englishVerses = await Endpoints.Texts.ReadByCanonicalVerse(_db, english.Id, 1, 3, default);
        var hebrewVerses = await Endpoints.Texts.ReadByCanonicalVerse(_db, hebrew.Id, 1, 3, default);

        englishVerses.Should().ContainKey(1);
        hebrewVerses.Should().ContainKey(1);
        Rebuild(englishVerses[1]).Should().Be("For behold");
        Rebuild(hebrewVerses[1]).Should().Be("כִּי הִנֵּה");
    }

    /// <summary>
    /// Reading the chapter the Hebrew calls 3 must not answer with the passage the English calls 3.
    /// Under the old pairing they were the same request.
    /// </summary>
    [Fact]
    public async Task TheHebrewChapterOfTheSameNumberIsADifferentPassage()
    {
        var hebrew = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo",
            (3, 1, ["וְהָיָה"]),
            (4, 1, ["כִּי"]));
        _db.SaveChanges();

        // Hebrew 3:1 is canonically 2:28 — the tail of the previous chapter — and 4:1 is canonically 3:1.
        Place(hebrew, 3, 1, canonicalChapter: 2, canonicalVerse: 28);
        Place(hebrew, 4, 1, canonicalChapter: 3, canonicalVerse: 1);
        _db.SaveChanges();

        var atCanonicalThree = await Endpoints.Texts.ReadByCanonicalVerse(_db, hebrew.Id, 1, 3, default);
        var atCanonicalTwo = await Endpoints.Texts.ReadByCanonicalVerse(_db, hebrew.Id, 1, 2, default);

        Rebuild(atCanonicalThree[1]).Should().Be("כִּי");
        Rebuild(atCanonicalTwo[28]).Should().Be("וְהָיָה");
    }

    /// <summary>
    /// A text that does not reach this address has no row at all, which is a different fact from a
    /// verse with no words in it.
    /// </summary>
    [Fact]
    public async Task ATextWithNoVerseAtAnAddressAnswersNothingRatherThanEmptiness()
    {
        var greek = Corpus.Add(_db, "nestle1904", TextKind.CriticalEdition, "grc", (1, 1, ["Ἐν"]));
        _db.SaveChanges();
        Place(greek, 1, 1, canonicalChapter: 1, canonicalVerse: 1);
        _db.SaveChanges();

        var elsewhere = await Endpoints.Texts.ReadByCanonicalVerse(_db, greek.Id, 1, 9, default);

        elsewhere.Should().BeEmpty();
    }

    /// <summary>
    /// The number the verification pass reads to decide a verse pair is too faint to trust, said to
    /// the reader instead of to a log. A faint pair means one of two things — the verses were laid
    /// against each other wrongly, or the two traditions genuinely differ here — and only somebody
    /// looking at both panes can tell which.
    /// </summary>
    [Fact]
    public async Task AVersePairCarriesHowStronglyItsTwoSidesAnswerEachOther()
    {
        var (greek, hebrew) = Pair();

        Link(greek, 1, hebrew, 1, confidence: 0.2);
        Link(greek, 2, hebrew, 2, confidence: 0.4);

        var strength = await ParallelEndpoints.Strengths(_db, greek.Id, hebrew.Id, 1, 1, default);

        strength[1].Links.Should().Be(2);
        strength[1].Stated.Should().Be(0);
        strength[1].Confidence.Should().BeApproximately(0.3, 1e-12);
    }

    /// <summary>
    /// A stated link carries no confidence, and averaging it in as though it were certainty would
    /// make testimony and a confident guess report the same number. It is counted and named apart.
    /// </summary>
    [Fact]
    public async Task AStatedLinkIsCountedAndNotAveraged()
    {
        var (greek, hebrew) = Pair();

        Link(greek, 1, hebrew, 1, confidence: null);
        Link(greek, 2, hebrew, 2, confidence: 0.4);

        var strength = await ParallelEndpoints.Strengths(_db, greek.Id, hebrew.Id, 1, 1, default);

        strength[1].Links.Should().Be(2);
        strength[1].Stated.Should().Be(1);
        strength[1].Confidence.Should().BeApproximately(0.4, 1e-12);
    }

    /// <summary>
    /// Read from whichever end the loader wrote it. Which text a link is stored as being from is a
    /// fact about the loader, and the strength of a verse pair is not.
    /// </summary>
    [Fact]
    public async Task ThePairIsReadInEitherDirection()
    {
        var (greek, hebrew) = Pair();

        Link(hebrew, 1, greek, 1, confidence: 0.6);

        var strength = await ParallelEndpoints.Strengths(_db, greek.Id, hebrew.Id, 1, 1, default);

        strength[1].Links.Should().Be(1);
        strength[1].Confidence.Should().BeApproximately(0.6, 1e-12);
    }

    /// <summary>
    /// A link naming two words of one verse is one link. Counting its ends would report a verse
    /// answered by a single generous link as more strongly paired than one answered by two.
    /// </summary>
    [Fact]
    public async Task ALinkNamingTwoWordsOfAVerseIsCountedOnce()
    {
        var (greek, hebrew) = Pair();

        var link = Link(greek, 1, hebrew, 1, confidence: 0.5);
        _db.LinkWords.Add(new LinkWord
        {
            LinkId = link.Id,
            WordId = _db.WordAt(greek, 1, 1, 2).Id,
            Side = LinkSide.From,
        });
        _db.SaveChanges();

        var strength = await ParallelEndpoints.Strengths(_db, greek.Id, hebrew.Id, 1, 1, default);

        strength[1].Links.Should().Be(1);
    }

    /// <summary>Two texts of one verse each, already sitting at the same canonical address.</summary>
    private (Text Greek, Text Hebrew) Pair()
    {
        var greek = Corpus.Add(_db, "lxx-brenton", TextKind.Translation, "eng", (1, 1, ["a", "spreading", "trunk"]));
        var hebrew = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo", (1, 1, ["a", "doe", "loosed"]));
        _db.SaveChanges();
        return (greek, hebrew);
    }

    private Link Link(Text from, int fromPosition, Text to, int toPosition, double? confidence)
    {
        var link = new Link
        {
            FromTextId = from.Id,
            ToTextId = to.Id,
            Relation = LinkRelation.Renders,
            Method = confidence is null ? LinkMethod.StatedBySource : LinkMethod.Aligner,
            Source = "a test",
            Confidence = confidence,
        };
        _db.Links.Add(link);
        _db.SaveChanges();

        _db.LinkClaims.Add(new LinkClaim
        {
            LinkId = link.Id,
            Method = link.Method,
            Confidence = link.Confidence,
            Source = link.Source,
        });
        _db.LinkWords.Add(new LinkWord
        {
            LinkId = link.Id,
            WordId = _db.WordAt(from, 1, 1, fromPosition).Id,
            Side = LinkSide.From,
        });
        _db.LinkWords.Add(new LinkWord
        {
            LinkId = link.Id,
            WordId = _db.WordAt(to, 1, 1, toPosition).Id,
            Side = LinkSide.To,
        });
        _db.SaveChanges();

        return link;
    }

    private void Place(Text text, int chapter, int verse, int canonicalChapter, int canonicalVerse)
    {
        var own = _db.VerseAt(text, chapter, verse);
        var existing = _db.VerseReferences.Where(r => r.VerseId == own.Id);
        _db.VerseReferences.RemoveRange(existing);
        _db.VerseReferences.Add(new VerseReference
        {
            VerseId = own.Id,
            CanonicalBook = 1,
            CanonicalChapter = canonicalChapter,
            CanonicalVerse = canonicalVerse,
            IsPrimary = true,
        });
    }

    private static string Rebuild(IEnumerable<TextWordResponse> words) =>
        string.Concat(words.Select(w => w.Text + w.Trailer)).TrimEnd();
}
