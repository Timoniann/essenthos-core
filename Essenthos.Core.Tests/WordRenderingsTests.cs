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
/// The concordance read backwards: which words of the originals stand behind one word of a
/// translation. Asked of Postgres, because the answer is one pass over the links and the claim is
/// that it counts each place once per edition, says what it counted over, and never mistakes
/// another translation for an original.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class WordRenderingsTests : IDisposable
{
    private const string Agapao = "G25";
    private const string Agape = "G26";
    private const string Phileo = "G5368";

    /// <summary>An inferred link has to say how sure it is; the value plays no part here.</summary>
    private const double InferredConfidence = 0.5;

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _nestle;
    private readonly Text _receptus;
    private readonly Text _english;
    private readonly Text _russian;

    public WordRenderingsTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        _nestle = Corpus.Add(_db, "NESTLE", TextKind.CriticalEdition, "grc",
            (1, 1, ["ἀγαπᾷ", "φιλεῖ", "ἀγάπη"]),
            (1, 2, ["ἠγάπησεν"]));
        _receptus = Corpus.Add(_db, "TR", TextKind.PrintedEdition, "grc",
            (1, 1, ["ἀγαπᾷ", "φιλεῖ", "ἀγάπη"]),
            (1, 2, ["ἠγάπησεν"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (1, 1, ["He", "loveth", "and", "loveth", "love"]),
            (1, 2, ["He", "Loved", "lovely"]));

        // Another translation whose words carry Strong numbers and are linked to the King James: a
        // Strong-tagged translation is still not an original, and must never be counted as one.
        _russian = Corpus.Add(_db, "RUSV", TextKind.Translation, "rus", (1, 1, ["любит"]));
        _db.SaveChanges();

        foreach (var word in _db.Words.Local.Where(w => w.TextId == _english.Id))
        {
            word.NormalisedText = word.Surface.ToLowerInvariant();
        }

        foreach (var greek in new[] { _nestle, _receptus })
        {
            Tag(greek, 1, 1, 1, Agapao);
            Tag(greek, 1, 1, 2, Phileo);
            Tag(greek, 1, 1, 3, Agape);
            Tag(greek, 1, 2, 1, Agapao);

            Renders(greek, (1, 1, [1]), (1, 1, [2]), LinkMethod.StrongNumber);
            Renders(greek, (1, 1, [2]), (1, 1, [4]), LinkMethod.StrongNumber);
            Renders(greek, (1, 1, [3]), (1, 1, [5]), LinkMethod.StrongNumber);
            Renders(greek, (1, 2, [1]), (1, 2, [2]), LinkMethod.Aligner);
        }

        Tag(_russian, 1, 1, 1, Agapao);
        Renders(_russian, (1, 1, [1]), (1, 1, [2]), LinkMethod.Aligner);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    /// <summary>
    /// Each edition answers for itself, and each original counts the translation's occurrences it
    /// stands behind: <em>loveth</em> twice behind two words, and never the Russian's word.
    /// </summary>
    [Fact]
    public async Task EachEditionCountsTheOriginalsBehindTheWord()
    {
        var witnesses = await RenderingEndpoints.Behind(
            _db, _english.Id, ["love", "loved", "loveth"], default);

        witnesses.Select(witness => witness.Corpus).Should().Equal("NESTLE", "TR");

        var nestle = witnesses[0];
        nestle.Occurrences.Should().Be(4);
        nestle.Reached.Should().Be(4);
        nestle.Originals.Select(original => (original.StrongNumber, original.Occurrences)).Should()
            .Equal((Agapao, 2), (Agape, 1), (Phileo, 1));
    }

    /// <summary>A count says what made its links, per original and for the edition as a whole.</summary>
    [Fact]
    public async Task EveryCountSaysWhatItsLinksWereMadeBy()
    {
        var nestle = (await RenderingEndpoints.Behind(_db, _english.Id, ["loveth", "loved"], default))[0];

        var agapao = nestle.Originals.Single(original => original.StrongNumber == Agapao);
        agapao.Methods.Should().BeEquivalentTo(new[]
        {
            new TextLinkMethodResponse("aligner", 1),
            new TextLinkMethodResponse("strong-number", 1),
        });
        nestle.Links.Should().Be(3);
        nestle.Methods.Sum(method => method.Links).Should().Be(nestle.Links);
    }

    [Fact]
    public async Task SamplesAreThePlacesInCanonicalOrder()
    {
        var nestle = (await RenderingEndpoints.Behind(_db, _english.Id, ["loveth", "loved"], default))[0];

        nestle.Originals.Single(original => original.StrongNumber == Agapao).Samples
            .Select(place => (place.Chapter, place.Verse, place.Text))
            .Should().Equal((1, 1, "loveth"), (1, 2, "Loved"));
    }

    [Fact]
    public async Task AWordNothingIsLinkedToHasNoWitness()
    {
        (await RenderingEndpoints.Behind(_db, _english.Id, ["lovely"], default)).Should().BeEmpty();
    }

    /// <summary>
    /// The Strong page's renderings are counted over one edition. Counted over both, the King
    /// James's <em>love</em> was reported twice for one ἀγάπη, and the translation's neighbour's
    /// tagged words made more places reached than the number stands in.
    /// </summary>
    [Fact]
    public async Task StrongRenderingsAreCountedOverOneEdition()
    {
        var renderings = await StrongEndpoints.Renderings(_db, [Agapao, Agape], _english.Id, 5, default);

        renderings[Agape].Should().Equal(new StrongRenderingResponse("love", 1));
        renderings[Agapao].Should().Equal(
            new StrongRenderingResponse("loved", 1),
            new StrongRenderingResponse("loveth", 1));
    }

    [Fact]
    public async Task TheCriticalEditionComesFirstAndTranslationsNotAtAll()
    {
        var originals = await RenderingEndpoints.Originals(_db, _english.Id, default);

        originals.Select(original => original.Slug).Should().Equal("NESTLE", "TR");
        RenderingEndpoints.Primary(originals).Select(original => original.Slug).Should().Equal("NESTLE");
    }

    private void Tag(Text text, int chapter, int verse, int position, string number) =>
        _db.WordAt(text, chapter, verse, position).StrongNumber = number;

    private void Renders(
        Text original,
        (int Chapter, int Verse, int[] Positions) from,
        (int Chapter, int Verse, int[] Positions) english,
        LinkMethod method)
    {
        var link = new Link
        {
            FromTextId = _english.Id,
            ToTextId = original.Id,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = method == LinkMethod.StatedBySource ? null : InferredConfidence,
            Source = "a test",
        };
        _db.Links.Add(link);

        foreach (var position in english.Positions)
        {
            _db.LinkWords.Add(new LinkWord
            {
                Link = link,
                Word = _db.WordAt(_english, english.Chapter, english.Verse, position),
                Side = LinkSide.From,
            });
        }

        foreach (var position in from.Positions)
        {
            _db.LinkWords.Add(new LinkWord
            {
                Link = link,
                Word = _db.WordAt(original, from.Chapter, from.Verse, position),
                Side = LinkSide.To,
            });
        }
    }
}

/// <summary>
/// How a typed word becomes the spellings a text prints: gathered by the stem the alignment reduces
/// words with, or matched as spelled where the language has no stemmer.
/// </summary>
public sealed class WordFormsTests
{
    [Fact]
    public void TheFormsOfTheWordAreGatheredByTheirStem()
    {
        var index = new FormIndex("eng", [new("love", 3), new("loved", 2), new("loveth", 5), new("glove", 1)]);

        index.Matching("Love", exact: false).Select(form => form.Text).Should()
            .Equal("loveth", "love", "loved");
        index.Stems.Should().BeTrue();
    }

    [Fact]
    public void AnExactAskIsAnsweredWithTheSpellingAlone()
    {
        var index = new FormIndex("eng", [new("love", 3), new("loved", 2)]);

        index.Matching("loved", exact: true).Should().Equal(new WordForm("loved", 2));
    }

    /// <summary>
    /// The Slavic stemmer takes <em>любов</em> down to <em>люб</em>, which is also where <em>любі</em>
    /// (dear) and the verb land; the noun's own forms are the ones kept, however the apostrophe of
    /// <em>любов'ю</em> is written.
    /// </summary>
    [Fact]
    public void ASlavicNounKeepsItsOwnFormsAndNotItsNeighbours()
    {
        var index = new FormIndex("ukr",
            [new("любов", 80), new("любові", 23), new("любов’ю", 16), new("любі", 54), new("любить", 81)]);

        index.Matching("Любов", exact: false).Select(form => form.Text).Should()
            .Equal("любов", "любові", "любов’ю");
    }

    [Fact]
    public void ASlavicVerbKeepsItsOwnForms()
    {
        var index = new FormIndex("ukr", [new("любити", 32), new("любить", 81), new("любив", 14), new("любов", 80)]);

        index.Matching("любити", exact: false).Select(form => form.Text).Should()
            .Equal("любить", "любити", "любив");
    }

    [Fact]
    public void ALanguageWithNoStemmerIsMatchedAsSpelled()
    {
        var index = new FormIndex("hbo", [new("אהב", 3), new("אהבה", 2)]);

        index.Stems.Should().BeFalse();
        index.Matching("אהב", exact: false).Should().Equal(new WordForm("אהב", 3));
    }
}
