using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests.Lexicon;

/// <summary>
/// The concordance read backwards: which words of the originals stand behind one word of a
/// translation. The links are written to Postgres, counted as the build counts them
/// (<see cref="CountedRenderings"/>) and read back, because the claim is that it counts each place
/// once per edition, says what it counted over, and never mistakes another translation for an
/// original, whatever spellings a request adds up.
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
        _db.WriteStoredStrong().GetAwaiter().GetResult();
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
        var witnesses = await Behind(["love", "loved", "loveth"]);

        witnesses.Select(witness => witness.Corpus).Should().Equal("NESTLE", "TR");

        var nestle = witnesses[0];
        nestle.Occurrences.Should().Be(4);
        nestle.Reached.Should().Be(4);
        nestle.Originals.Select(original => (original.StrongNumber, original.Occurrences)).Should()
            .Equal((Agapao, 2), (Agape, 1), (Phileo, 1));
    }

    /// <summary>Each original carries its gloss in the reader's language beside Strong's English, where one is loaded.</summary>
    [Fact]
    public async Task EachOriginalIsGlossedInTheReadersLanguage()
    {
        _db.StrongEntryTranslations.Add(new StrongEntryTranslation
        {
            StrongNumber = Agapao, Language = "deu", Definition = "lieben",
            Method = LinkMethod.ModelReading, Source = "translator",
        });
        await _db.SaveChangesAsync();

        var german = (await Behind(["love", "loved", "loveth"], "de"))[0];
        var english = (await Behind(["love", "loved", "loveth"]))[0];

        german.Originals.Single(o => o.StrongNumber == Agapao).Translation.Should()
            .Be(new StrongGlossTranslationResponse("deu", "lieben"));
        german.Originals.Single(o => o.StrongNumber == Agape).Translation.Should().BeNull();
        english.Originals.Should().OnlyContain(o => o.Translation == null);
    }

    /// <summary>A count says what made its links, per original and for the edition as a whole.</summary>
    [Fact]
    public async Task EveryCountSaysWhatItsLinksWereMadeBy()
    {
        var nestle = (await Behind(["loveth", "loved"]))[0];

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
        var nestle = (await Behind(["loveth", "loved"]))[0];

        nestle.Originals.Single(original => original.StrongNumber == Agapao).Samples
            .Select(place => (place.Chapter, place.Verse, place.Text))
            .Should().Equal((1, 1, "loveth"), (1, 2, "Loved"));
    }

    /// <summary>
    /// A sample opens the verse it quotes, so it is addressed by where the verse stands in the
    /// frame, not by the number the translation prints: the New Ukrainian Translation's Psalm 36:4
    /// is the frame's 37:4.
    /// </summary>
    [Fact]
    public async Task SamplesAreAddressedByTheFrameNotByTheTranslationsOwnNumbers()
    {
        var renumbered = _db.VerseReferences.Single(r =>
            r.Verse!.TextId == _english.Id && r.Verse.ChapterNumber == 1 && r.Verse.Number == 2);
        renumbered.CanonicalChapter = 2;
        renumbered.CanonicalVerse = 3;
        await _db.SaveChangesAsync();

        var nestle = (await Behind(["loveth", "loved"]))[0];

        nestle.Originals.Single(original => original.StrongNumber == Agapao).Samples
            .Select(place => (place.Chapter, place.Verse, place.Text))
            .Should().Equal((1, 1, "loveth"), (2, 3, "Loved"));
    }

    [Fact]
    public async Task AWordNothingIsLinkedToHasNoWitness()
    {
        (await Behind(["lovely"])).Should().BeEmpty();
    }

    /// <summary>
    /// A phrase linked whole can hold two spellings one request adds up: <em>loveth</em> and <em>loved</em> on one link are
    /// one link of the edition and of the original, while each spelling asked alone still counts it.
    /// </summary>
    [Fact]
    public async Task ALinkHoldingTwoSpellingsOfTheWordIsCountedOnce()
    {
        var english = Corpus.Add(_db, "ASV", TextKind.Translation, "eng", (1, 1, ["loveth", "loved"]));
        _db.SaveChanges();
        foreach (var word in _db.Words.Local.Where(w => w.TextId == english.Id))
        {
            word.NormalisedText = word.Surface.ToLowerInvariant();
        }

        var link = new Link
        {
            FromTextId = english.Id, ToTextId = _nestle.Id, Relation = LinkRelation.Renders, Method = LinkMethod.StatedBySource,
            Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(english, 1, 1, 1), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(english, 1, 1, 2), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(_nestle, 1, 1, 1), Side = LinkSide.To });
        await CountedRenderings.Count(_db);

        var both = (await RenderingEndpoints.Behind(_db, english.Id, ["loveth", "loved"], default))[0];
        var one = (await RenderingEndpoints.Behind(_db, english.Id, ["loved"], default))[0];

        both.Reached.Should().Be(2);
        both.Links.Should().Be(1);
        both.Methods.Should().Equal(new TextLinkMethodResponse("stated-by-source", 1));
        both.Originals.Single().Should().Match<WordOriginalResponse>(o => o.Occurrences == 2 && o.Links == 1 && o.Places == 1);
        one.Links.Should().Be(1);
    }

    /// <summary>The originals come a page at a time, the commonest first, and say how many there are.</summary>
    [Fact]
    public async Task TheOriginalsComeAPageAtATime()
    {
        await CountedRenderings.Count(_db);

        var first = (await RenderingEndpoints.Behind(_db, _english.Id, ["love", "loved", "loveth"], default, take: 1))[0];
        var second = (await RenderingEndpoints.Behind(_db, _english.Id, ["love", "loved", "loveth"], default, skip: 1, take: 1))[0];

        first.TotalOriginals.Should().Be(3);
        first.Originals.Select(o => o.StrongNumber).Should().Equal(Agapao);
        first.OtherOriginals.Should().Be(2);
        first.OtherOccurrences.Should().Be(2);
        second.Originals.Select(o => o.StrongNumber).Should().Equal(Agape);
        second.OtherOriginals.Should().Be(1);
    }

    /// <summary>
    /// Past its samples, an original's places come a page at a time, each place once, across the spellings asked about, with
    /// how many there are.
    /// </summary>
    [Fact]
    public async Task AnOriginalsPlacesComeAPageAtATime()
    {
        await CountedRenderings.Count(_db);

        var (total, items) = await RenderingEndpoints.Places(_db, _english.Id, ["love", "loved", "loveth"], _nestle.Id, Agapao, null, 1, 5, default);

        total.Should().Be(2);
        items.Select(place => (place.Chapter, place.Verse, place.Text)).Should().Equal((1, 2, "Loved"));
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
        var originals = await LinkedOriginals.Of(_db, _english.Id, default);

        originals.Select(original => original.Slug).Should().Equal("NESTLE", "TR");
        LinkedOriginals.Primary(originals).Select(original => original.Slug).Should().Equal("NESTLE");
    }

    private async Task<IList<WordWitnessResponse>> Behind(IReadOnlyCollection<string> spellings, string? prose = null)
    {
        await CountedRenderings.Count(_db);
        return await RenderingEndpoints.Behind(_db, _english.Id, spellings, default, prose);
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
            Provenance = new() { Source = "a test" },
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
/// words with, by their endings in Russian and Ukrainian, or matched as spelled where the language
/// has no stemmer.
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
    /// <em>любов</em>, <em>любі</em> (dear) and the verb share letters and no ending; the noun's own
    /// forms are the ones kept, however the apostrophe of <em>любов'ю</em> is written.
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

    /// <summary>
    /// The aligner's stemmer lands <em>завітати</em> (to visit) on <em>завіт</em> (covenant) and
    /// reads <em>завіти</em> as an infinitive; the concordance asked about the noun answers the
    /// noun's forms, and in a text that prints only the verb, nothing.
    /// </summary>
    [Fact]
    public void ASlavicNounDoesNotGatherTheVerbMadeFromIt()
    {
        var forms = new WordForm[]
        {
            new("завіт", 28), new("завіту", 20), new("завіти", 5), new("завітом", 3), new("завітати", 1),
            new("завітаю", 1),
        };

        new FormIndex("ukr", forms).Matching("Завіт", exact: false).Select(form => form.Text).Should()
            .Equal("завіт", "завіту", "завіти", "завітом");
        new FormIndex("ukr", [new("завітати", 1), new("завітаю", 1), new("заповіт", 42)])
            .Matching("завіт", exact: false).Should().BeEmpty();
        new FormIndex("ukr", forms).Matching("завітати", exact: false).Select(form => form.Text).Should()
            .Equal("завітати", "завітаю");
    }

    [Fact]
    public void ASlavicNounKeepsTheCasesWhoseEndingsAVerbCouldHave()
    {
        var index = new FormIndex("ukr",
        [
            new("земля", 219), new("землі", 1500), new("землю", 700), new("землею", 40), new("земляна", 1),
        ]);

        index.Matching("земля", exact: false).Select(form => form.Text).Should()
            .Equal("землі", "землю", "земля", "землею");
    }

    [Fact]
    public void ARussianNounDoesNotGatherTheVerbMadeFromIt()
    {
        var index = new FormIndex("rus",
        [
            new("свет", 60), new("света", 30), new("светом", 4), new("светит", 3), new("светил", 2),
            new("светить", 1),
        ]);

        index.Matching("свет", exact: false).Select(form => form.Text).Should().Equal("свет", "света", "светом");
        index.Matching("светить", exact: false).Select(form => form.Text).Should()
            .Equal("светит", "светил", "светить");
    }

    /// <summary>
    /// <em>свет</em> (light) and <em>свят</em> (holy) both end like a verb's third person, and read so
    /// would share a root of two letters; a root that short is a verb's only where it ends in a vowel
    /// or a soft sign, as <em>пьет</em> and <em>пьют</em> do.
    /// </summary>
    [Fact]
    public void TwoNounsInAVerbsEndingShareNoRootOfTwoLetters()
    {
        var index = new FormIndex("rus", [new("свет", 60), new("свят", 37), new("пьет", 3), new("пьют", 2)]);

        index.Matching("свет", exact: false).Select(form => form.Text).Should().Equal("свет");
        index.Matching("пьет", exact: false).Select(form => form.Text).Should().Equal("пьет", "пьют");
    }

    /// <summary>
    /// Whether one request can gather two spellings of a link, which decides whether a sum over spellings must count the link
    /// once: by a shared stem, or in Russian and Ukrainian by stems one of which begins the other.
    /// </summary>
    [Fact]
    public void TwoSpellingsGatherTogetherOnlyWhereOneRequestCanAskForBoth()
    {
        FormIndex.Together("eng", ["loveth", "loved"]).Should().BeTrue();
        FormIndex.Together("eng", ["a", "as"]).Should().BeFalse();
        FormIndex.Together("eng", ["the", "lord"]).Should().BeFalse();
        FormIndex.Together("ukr", ["любов", "любові"]).Should().BeTrue();
        FormIndex.Together("ukr", ["і", "сказав"]).Should().BeFalse();
        FormIndex.Together("hbo", ["אהב", "אהבה"]).Should().BeFalse();
    }

    [Fact]
    public void ALanguageWithNoStemmerIsMatchedAsSpelled()
    {
        var index = new FormIndex("hbo", [new("אהב", 3), new("אהבה", 2)]);

        index.Stems.Should().BeFalse();
        index.Matching("אהב", exact: false).Should().Equal(new WordForm("אהב", 3));
    }
}
