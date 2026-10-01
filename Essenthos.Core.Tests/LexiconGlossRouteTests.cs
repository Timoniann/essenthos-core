using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Utils;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The lexicon's meaning for a word of a Greek edition that glosses none of its words, and the way it
/// was reached.
///
/// Brenton's Septuagint has lemmas and Swete's has none, and neither prints a gloss: a reader who
/// turns the meaning line on is shown nothing unless the lexicon is reached through the word's own
/// dictionary form, the same word in the other edition, or, failing both, the words spelt exactly
/// like it elsewhere in the corpus.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class LexiconGlossRouteTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _brenton;
    private readonly Text _swete;

    public LexiconGlossRouteTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        _brenton = Corpus.Add(_db, "GRCBRENT", TextKind.PrintedEdition, "grc",
            (1, 1, ["Ἐν", "ἀρχῇ", "φῶς", "Ἰοὺ", "καὶ"]));
        _swete = Corpus.Add(_db, "SWETE", TextKind.CriticalEdition, "grc",
            (1, 1, ["ἐν", "ἀρχῇ", "φῶς", "Ἰού", "καί", "ὅτι", "λόγος"]));
        var nestle = Corpus.Add(_db, "NESTLE1904", TextKind.CriticalEdition, "grc",
            (1, 1, ["φῶς", "ἰοῦ", "ὅτι", "ὅτι", "λόγος"]));
        var english = Corpus.Add(_db, "KJV", TextKind.Translation, "en", (1, 1, ["λόγος"]));
        _db.SaveChanges();

        foreach (var word in _db.Words.Where(w => w.TextId == _brenton.Id || w.TextId == _swete.Id
                                                  || w.TextId == nestle.Id || w.TextId == english.Id))
        {
            word.NormalisedText = WordFolding.Fold(word.Surface, "grc");
        }

        Lemmatise(_brenton, "ἐν", "ἀρχή", "φάος", "Ἰηού", "καί");
        Lemmatise(nestle, "φῶς", "ἰός", "ὅτι", "ὅστις", "λόγος");
        Lemmatise(english, "ὅτι");

        _db.LexiconGlosses.AddRange(
            Entry("G1722", "ἐν", "in"),
            Entry("G746", "ἀρχή", "beginning"),
            Entry("G5457", "φῶς", "light"),
            Entry("G2447", "ἰός", "poison"),
            Entry("G2532", "καί", "and"),
            Entry("G3754", "ὅτι", "that"),
            Entry("G3748", "ὅστις", "whoever"),
            Entry("G3056", "λόγος", "word"));

        var link = new Link
        {
            FromTextId = _swete.Id,
            ToTextId = _brenton.Id,
            Relation = LinkRelation.Equals,
            Method = LinkMethod.Lexical,
            Confidence = 0.9,
            Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(_swete, 1, 1, 2), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(_brenton, 1, 1, 2), Side = LinkSide.To });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task AWordWithADictionaryFormTheLexiconHoldsIsGlossedByIt() =>
        (await Read(_brenton, 2)).LexiconGloss.Should().BeEquivalentTo(new LexiconGlossResponse(["beginning"], "lemma"));

    [Fact]
    public async Task AWordOfAnEditionWithoutLemmasIsGlossedThroughTheSameWordInAnother() =>
        (await Read(_swete, 2)).LexiconGloss.Should().BeEquivalentTo(new LexiconGlossResponse(["beginning"], "equals"));

    [Fact]
    public async Task AWordReachedNoOtherWayIsGlossedByTheWordsSpeltExactlyLikeIt()
    {
        (await Read(_swete, 3)).LexiconGloss.Should().BeEquivalentTo(new LexiconGlossResponse(["light"], "form"));
        (await Read(_brenton, 3)).LexiconGloss.Should().BeEquivalentTo(new LexiconGlossResponse(["light"], "form"),
            "Brenton's own dictionary form φάος is not one the lexicon holds, and every φῶς that has one has φῶς");
    }

    [Fact]
    public async Task AGraveAccentAndACapitalAreTheSameSpelling()
    {
        (await Read(_swete, 5)).LexiconGloss.Should().BeEquivalentTo(new LexiconGlossResponse(["and"], "form"));
        (await Read(_swete, 1)).LexiconGloss.Should().BeEquivalentTo(new LexiconGlossResponse(["in"], "form"));
    }

    [Theory]
    [InlineData("καὶ", "καί")]
    [InlineData("Ἐν", "ἐν")]
    [InlineData("ἂν", "ἄν")]
    [InlineData("ᾒ", "ᾔ")]
    [InlineData("διὰ", "διά")]
    [InlineData("ΚΑῚ", "καί")]
    public void ASpellingSetsAsideOnlyCaseAndTheGrave(string printed, string spelt) =>
        GreekLetters.Spelling(printed).Should().Be(GreekLetters.Spelling(spelt));

    [Theory]
    [InlineData("ἰού", "ἰοῦ")]
    [InlineData("ἡ", "ἤ")]
    [InlineData("ᾗ", "ῇ")]
    public void ABreathingACircumflexOrAnotherAccentIsAnotherSpelling(string one, string other) =>
        GreekLetters.Spelling(one).Should().NotBe(GreekLetters.Spelling(other));

    [Fact]
    public async Task ABreathingOrAnAccentMakesAnotherWord()
    {
        (await Read(_swete, 4)).LexiconGloss.Should().BeNull("Ἰού the name is not ἰοῦ the noun, though they fold alike");
        (await Read(_brenton, 4)).LexiconGloss.Should().BeNull();
    }

    [Fact]
    public async Task ASpellingTheCorpusLemmatisesTwoWaysGlossesNothing() =>
        (await Read(_swete, 6)).LexiconGloss.Should().BeNull("choosing between ὅτι and ὅστις would be a guess");

    [Fact]
    public async Task OnlyGreekWordsSayHowAGreekWordIsSpelt() =>
        (await Read(_swete, 7)).LexiconGloss.Should().BeEquivalentTo(new LexiconGlossResponse(["word"], "form"));

    private async Task<TextWordResponse> Read(Text text, int position)
    {
        var chapter = await Texts.ReadChapter(_db, text.Id, 1, 1, default);
        return chapter.Single().Words[position - 1];
    }

    private void Lemmatise(Text text, params string[] lemmas)
    {
        for (var position = 1; position <= lemmas.Length; position++)
        {
            _db.WordAt(text, 1, 1, position).Lemma = lemmas[position - 1];
        }
    }

    private static LexiconGloss Entry(string number, string lemma, string gloss) => new()
    {
        Entry = $"{number}={lemma}",
        StrongNumber = number,
        Lemma = lemma,
        Gloss = gloss,
        Source = "a test",
    };
}
