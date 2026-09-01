using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The shapes the model has to be able to state. Each of these is a correspondence that occurs in
/// the corpus and that the old schema could not express, because it gave every translated word one
/// original word to point at. If any of them cannot be stored, the schema is wrong.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class LinkShapeTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public LinkShapeTests(WitnessDatabase database)
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
    public void TwoWordsRenderingOneAreOneLinkNamingAllThree()
    {
        var hebrew = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo",
            (1, 1, ["הַכֹּהֵן", "הַגָּדוֹל"]));
        var greek = Corpus.Add(_db, "lxx", TextKind.Translation, "grc",
            (1, 1, ["ἀρχιερεύς"]));
        _db.SaveChanges();

        var link = new Link
        {
            FromTextId = hebrew.Id,
            ToTextId = greek.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource,
            Source = "a test",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(hebrew, 1, 1, 1), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(hebrew, 1, 1, 2), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(greek, 1, 1, 1), Side = LinkSide.To });
        _db.SaveChanges();

        _db.Side(link.Id, LinkSide.From).Select(w => w.Position).Should().BeEquivalentTo([1, 2]);
        _db.Side(link.Id, LinkSide.To).Select(w => w.Position).Should().BeEquivalentTo([1]);
    }

    /// <summary>
    /// The claim is that the two Hebrew words together render the Greek one — not that they render
    /// each other. A shared word identifier could not tell those apart, which is the whole of
    /// DOC-0006 stated as a query.
    /// </summary>
    [Fact]
    public void TwoWordsRenderingOneDoNotTherebyCorrespondToEachOther()
    {
        var hebrew = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo",
            (1, 1, ["הַכֹּהֵן", "הַגָּדוֹל"]));
        var greek = Corpus.Add(_db, "lxx", TextKind.Translation, "grc", (1, 1, ["ἀρχιερεύς"]));
        _db.SaveChanges();

        var link = new Link
        {
            FromTextId = hebrew.Id,
            ToTextId = greek.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource,
            Source = "a test",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(hebrew, 1, 1, 1), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(hebrew, 1, 1, 2), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(greek, 1, 1, 1), Side = LinkSide.To });
        _db.SaveChanges();

        var betweenTwoHebrewWords = _db.Links
            .Where(l => l.FromTextId == hebrew.Id && l.ToTextId == hebrew.Id)
            .ToList();

        betweenTwoHebrewWords.Should().BeEmpty();
    }

    /// <summary>Each Hebrew word reaches the Greek word, and the Greek word reaches both.</summary>
    [Fact]
    public void EitherWordOfAPairReachesTheSameCounterpartAndTheCounterpartReachesBoth()
    {
        var hebrew = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo",
            (1, 1, ["הַכֹּהֵן", "הַגָּדוֹל"]));
        var greek = Corpus.Add(_db, "lxx", TextKind.Translation, "grc", (1, 1, ["ἀρχιερεύς"]));
        _db.SaveChanges();

        var link = new Link
        {
            FromTextId = hebrew.Id,
            ToTextId = greek.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource,
            Source = "a test",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(hebrew, 1, 1, 1), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(hebrew, 1, 1, 2), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(greek, 1, 1, 1), Side = LinkSide.To });
        _db.SaveChanges();

        foreach (var position in new[] { 1, 2 })
        {
            var wordId = _db.WordAt(hebrew, 1, 1, position).Id;
            var reached = _db.LinkWords
                .Where(lw => lw.WordId == wordId && lw.Side == LinkSide.From)
                .SelectMany(lw => _db.LinkWords.Where(other => other.LinkId == lw.LinkId && other.Side == LinkSide.To))
                .Select(other => other.WordId)
                .ToList();

            reached.Should().Equal(_db.WordAt(greek, 1, 1, 1).Id);
        }

        var greekWordId = _db.WordAt(greek, 1, 1, 1).Id;
        var back = _db.LinkWords
            .Where(lw => lw.WordId == greekWordId && lw.Side == LinkSide.To)
            .SelectMany(lw => _db.LinkWords.Where(other => other.LinkId == lw.LinkId && other.Side == LinkSide.From))
            .Select(other => other.WordId)
            .ToList();

        back.Should().HaveCount(2);
    }

    /// <summary>
    /// One-to-none: the translation supplies a word its source only implies. The link exists, names
    /// the English word, and names nothing on the Greek side.
    /// </summary>
    [Fact]
    public void AWordTheTranslationSuppliesHasNothingOnTheSourceSide()
    {
        var english = Corpus.Add(_db, "kjv", TextKind.Translation, "eng", (1, 1, ["the", "beginning"]));
        var greek = Corpus.Add(_db, "nestle1904", TextKind.CriticalEdition, "grc", (1, 1, ["ἀρχῇ"]));
        _db.SaveChanges();

        var link = new Link
        {
            FromTextId = english.Id,
            ToTextId = greek.Id,
            Relation = LinkRelation.Expands,
            Method = LinkMethod.Manual,
            Source = "a test",
            Note = "the article is supplied",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(english, 1, 1, 1), Side = LinkSide.From });
        _db.SaveChanges();

        _db.Side(link.Id, LinkSide.From).Should().ContainSingle();
        _db.Side(link.Id, LinkSide.To).Should().BeEmpty();
        _db.Links.Single(l => l.Id == link.Id).Relation.Should().Be(LinkRelation.Expands);
    }

    /// <summary>
    /// None-to-one: one witness reads what the other lacks. Stored positively — the row is the
    /// statement, and it is what turns an absence from silence into an explanation.
    /// </summary>
    [Fact]
    public void AReadingOneWitnessLacksIsStoredAsARowRatherThanAsSilence()
    {
        var nestle = Corpus.Add(_db, "nestle1904", TextKind.CriticalEdition, "grc", (1, 18, ["μονογενὴς", "θεὸς"]));
        var receptus = Corpus.Add(_db, "tr-scrivener", TextKind.CriticalEdition, "grc",
            (1, 18, ["μονογενὴς", "υἱός"]));
        _db.SaveChanges();

        var link = new Link
        {
            FromTextId = nestle.Id,
            ToTextId = receptus.Id,
            Relation = LinkRelation.Omits,
            Method = LinkMethod.StatedBySource,
            Source = "a test",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(receptus, 1, 18, 2), Side = LinkSide.To });
        _db.SaveChanges();

        _db.Side(link.Id, LinkSide.From).Should().BeEmpty();
        _db.Side(link.Id, LinkSide.To).Should().ContainSingle();

        var omissions = _db.Links
            .Where(l => l.FromTextId == nestle.Id && l.Relation == LinkRelation.Omits)
            .ToList();

        omissions.Should().ContainSingle("what one witness lacks is discoverable from that witness, not only from the other");
    }

    /// <summary>
    /// A link is a set against a set, and the sets are not confined to one verse. That is what makes
    /// a word ending up in a neighbouring verse something the model states rather than something it
    /// trips over.
    /// </summary>
    [Fact]
    public void ALinkMayNameWordsOnBothSidesOfAVerseBoundary()
    {
        var hebrew = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo",
            (1, 1, ["רֵאשִׁית"]),
            (1, 2, ["וְהָאָרֶץ"]));
        var english = Corpus.Add(_db, "kjv", TextKind.Translation, "eng", (1, 1, ["beginning", "earth"]));
        _db.SaveChanges();

        var link = new Link
        {
            FromTextId = hebrew.Id,
            ToTextId = english.Id,
            Relation = LinkRelation.Transposes,
            Method = LinkMethod.Manual,
            Source = "a test",
            Note = "the second Hebrew word is rendered in the previous English verse",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(hebrew, 1, 1, 1), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(hebrew, 1, 2, 1), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(english, 1, 1, 1), Side = LinkSide.To });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(english, 1, 1, 2), Side = LinkSide.To });
        _db.SaveChanges();

        var verses = _db.LinkWords
            .Where(lw => lw.LinkId == link.Id && lw.Side == LinkSide.From)
            .Select(lw => lw.Word!.VerseId)
            .Distinct()
            .ToList();

        verses.Should().HaveCount(2);
    }

    /// <summary>
    /// The Septuagint is Greek and so is Nestle; the Textus Receptus is Greek again. The old schema
    /// identified a corpus by the language on its words and had one slot per canonical book, so it
    /// could hold exactly one of them.
    /// </summary>
    [Fact]
    public void TwoGreekWitnessesEachHaveTheirOwnBooksAndWords()
    {
        var nestle = Corpus.Add(_db, "nestle1904", TextKind.CriticalEdition, "grc", (1, 1, ["ἀρχῇ"]));
        var receptus = Corpus.Add(_db, "tr-scrivener", TextKind.CriticalEdition, "grc", (1, 1, ["ἀρχῇ"]));
        _db.SaveChanges();

        var books = _db.Books
            .Where(b => b.TextId == nestle.Id || b.TextId == receptus.Id)
            .Where(b => b.CanonicalOrdinal == 1)
            .ToList();

        books.Should().HaveCount(2);
        books.Select(b => b.TextId).Should().OnlyHaveUniqueItems();
    }
}
