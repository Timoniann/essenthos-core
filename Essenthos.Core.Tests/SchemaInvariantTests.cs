using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the schema holds true on its own, without a loader remembering to.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SchemaInvariantTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public SchemaInvariantTests(WitnessDatabase database)
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
    public void AVerseHasOnlyOnePrimaryPlacement()
    {
        var text = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo", (1, 1, ["רֵאשִׁית"]));
        _db.SaveChanges();

        _db.VerseReferences.Add(new VerseReference
        {
            VerseId = _db.VerseAt(text, 1, 1).Id,
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 2,
            IsPrimary = true,
        });

        _db.Invoking(db => db.SaveChanges()).Should().Throw<DbUpdateException>();
    }

    /// <summary>
    /// A verse spanning two canonical verses gets a further, non-primary placement. That is the case
    /// the frame exists for, so the index must not stand in its way.
    /// </summary>
    [Fact]
    public void AVerseMaySpanTwoCanonicalVerses()
    {
        var text = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo", (1, 1, ["רֵאשִׁית"]));
        _db.SaveChanges();

        _db.VerseReferences.Add(new VerseReference
        {
            VerseId = _db.VerseAt(text, 1, 1).Id,
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 2,
            IsPrimary = false,
        });
        _db.SaveChanges();

        _db.VerseReferences
            .Count(r => r.VerseId == _db.VerseAt(text, 1, 1).Id)
            .Should().Be(2);
    }

    /// <summary>
    /// The point of the frame: two texts land on the same canonical address without colliding,
    /// because a reference is a placement rather than an identity.
    /// </summary>
    [Fact]
    public void TwoTextsMayShareACanonicalAddress()
    {
        var hebrew = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo", (1, 1, ["רֵאשִׁית"]));
        var english = Corpus.Add(_db, "kjv", TextKind.Translation, "eng", (1, 1, ["beginning"]));
        _db.SaveChanges();

        var here = _db.VerseReferences
            .Where(r => r.CanonicalBook == 1 && r.CanonicalChapter == 1 && r.CanonicalVerse == 1 && r.IsPrimary)
            .Select(r => r.Verse!.TextId)
            .ToList();

        here.Should().BeEquivalentTo([hebrew.Id, english.Id]);
    }

    /// <summary>
    /// A verse read back is the verse that was written. The storage may not trim, collapse or
    /// normalise anything — losing the space after punctuation is how 72,277 words were corrupted.
    /// </summary>
    [Fact]
    public void SurfaceAndTrailerComeBackExactly()
    {
        var text = Corpus.Add(_db, "kjv", TextKind.Translation, "eng", (1, 1, ["placeholder"]));
        _db.SaveChanges();

        var word = _db.WordAt(text, 1, 1, 1);
        word.Surface = "  God, ";
        word.Trailer = "; ";
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var stored = _db.Words.Single(w => w.Id == word.Id);
        stored.Surface.Should().Be("  God, ");
        stored.Trailer.Should().Be("; ");
    }

    [Fact]
    public void MorphologyIsStoredAsJsonAndCanBeQueriedByKey()
    {
        var text = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo", (1, 1, ["בָּרָא"]));
        _db.SaveChanges();

        var word = _db.WordAt(text, 1, 1, 1);
        word.Morphology = JsonDocument.Parse("""{"pos":"verb","stem":"qal","tense":"perfect"}""");
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var qal = _db.Database
            .SqlQuery<long>($"""select id as "Value" from word where morphology ->> 'stem' = 'qal'""")
            .ToList();

        qal.Should().Equal(word.Id);
    }

    /// <summary>
    /// Reloading one text of ten must not need the other nine. Removing a text takes its structure,
    /// its words, and every link that named it.
    /// </summary>
    [Fact]
    public void RemovingATextTakesItsStructureItsWordsAndItsLinks()
    {
        var hebrew = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo", (1, 1, ["רֵאשִׁית"]));
        var english = Corpus.Add(_db, "kjv", TextKind.Translation, "eng", (1, 1, ["beginning"]));
        _db.SaveChanges();

        var link = new Link
        {
            FromTextId = hebrew.Id,
            ToTextId = english.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource,
            Source = "a test",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(hebrew, 1, 1, 1), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(english, 1, 1, 1), Side = LinkSide.To });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        _db.Texts.Remove(_db.Texts.Single(t => t.Id == hebrew.Id));
        _db.SaveChanges();

        _db.Words.Count(w => w.TextId == hebrew.Id).Should().Be(0);
        _db.Books.Count(b => b.TextId == hebrew.Id).Should().Be(0);
        _db.Links.Count(l => l.Id == link.Id).Should().Be(0);
        _db.LinkWords.Count(lw => lw.LinkId == link.Id).Should().Be(0);
        _db.Words.Count(w => w.TextId == english.Id).Should().Be(1);
    }

    [Fact]
    public void ASlugNamesOneTextOnly()
    {
        Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo", (1, 1, ["רֵאשִׁית"]));
        _db.SaveChanges();

        _db.Texts.Add(new Text
        {
            Slug = "bhsa",
            Name = "another BHSA",
            Kind = TextKind.ManuscriptTradition,
            Language = "hbo",
        });

        _db.Invoking(db => db.SaveChanges()).Should().Throw<DbUpdateException>();
    }

    /// <summary>
    /// A text's role is a property of its relations. The King James is translated from two different
    /// sources in two halves of the canon, which is two rows and not one column.
    /// </summary>
    [Fact]
    public void ATextMayBeTranslatedFromMoreThanOneSource()
    {
        var masoretic = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo", (1, 1, ["רֵאשִׁית"]));
        var receptus = Corpus.Add(_db, "tr-scrivener", TextKind.CriticalEdition, "grc", (1, 1, ["ἀρχῇ"]));
        var english = Corpus.Add(_db, "kjv", TextKind.Translation, "eng", (1, 1, ["beginning"]));
        _db.SaveChanges();

        _db.TextRelations.AddRange(
            new TextRelation
            {
                FromTextId = english.Id,
                ToTextId = masoretic.Id,
                Relation = TextRelationKind.TranslatedFrom,
                Scope = "1-39",
            },
            new TextRelation
            {
                FromTextId = english.Id,
                ToTextId = receptus.Id,
                Relation = TextRelationKind.TranslatedFrom,
                Scope = "40-66",
            });
        _db.SaveChanges();

        _db.TextRelations
            .Count(r => r.FromTextId == english.Id && r.Relation == TextRelationKind.TranslatedFrom)
            .Should().Be(2);
    }
}
