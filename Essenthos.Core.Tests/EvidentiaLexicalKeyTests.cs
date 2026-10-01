using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The two lexical indexes EVIDENTIA reads, against a real database: the learned renderings refuse to
/// be taught by a text in another language unless the caller says so, and the reverse dictionary asks
/// the same key ladder the renderings do.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EvidentiaLexicalKeyTests : IDisposable
{
    private static readonly LanguagePackRegistry Packs =
        new([new EnglishLanguagePack(), new UkrainianLanguagePack(), new RussianLanguagePack(), new OriginalLanguagePack()]);

    private readonly AppDbContext _db;

    public EvidentiaLexicalKeyTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
    }

    [Fact]
    public async Task AnIndexLearnedFromAnotherLanguageIsRefusedNamingBothTextsAndBothLanguages()
    {
        await Rendered("RUST", "rus", "земля", "земля");

        var act = () => new EvidentiaKnownRenderingIndex(_db, Packs).For(
            "RUST", "HEBT", [Word("земля", "ukr")], 2, 1, sourceSlug: "UKRT");

        var refusal = await act.Should().ThrowAsync<InvalidOperationException>();
        refusal.Which.Message.Should().Contain("UKRT").And.Contain("ukr").And.Contain("RUST").And.Contain("rus")
            .And.Contain("--learn-across-languages");
    }

    [Fact]
    public async Task AnIndexLearnedAcrossLanguagesWhenAskedKeysEachObservationInItsOwnLanguage()
    {
        await Rendered("RUST", "rus", "земля", "земля");

        var evidence = await new EvidentiaKnownRenderingIndex(_db, Packs).For(
            "RUST", "HEBT", [Word("земля", "ukr")], 2, 1, sourceSlug: "UKRT", acrossLanguages: true);

        evidence.Should().NotBeNull("the two languages spell this word alike, which is all the transfer there is");
        Assert.Single(evidence!.Find(Analyse(Word("земля", "ukr")), Target("H776")));
    }

    [Fact]
    public async Task AnIndexInTheMeasuredLanguageNeedsNoPermission()
    {
        await Rendered("ENGT", "eng", "earth", "earth");

        var evidence = await new EvidentiaKnownRenderingIndex(_db, Packs).For(
            "ENGT", "HEBT", [Word("earth", "eng")], 2, 1, sourceSlug: "BSBT");

        evidence.Should().NotBeNull();
    }

    [Fact]
    public async Task ADefinitionWordReachesAnotherFormOfItThroughTheNormalisation()
    {
        Lexicon(("H914", "to divide, separate"));
        await _db.SaveChangesAsync();

        var evidence = await new EvidentiaDictionarySenseIndex(_db, Packs).For([Word("divided", "eng")]);

        var found = Assert.Single(evidence!.Find(Analyse(Word("divided", "eng")), Target("H914")));
        found.Source.Should().Contain("normalised");
    }

    [Fact]
    public async Task AFormTheLexiconSpellsExactlyNeverBacksOffToAStemItShares()
    {
        // Two Ukrainian words the starter stemmer cannot tell apart. Each is written in its own entry,
        // so each is answered by its own spelling, and the collision that switched the stem off for
        // every language cannot reach either of them.
        var рече = SlavicStemmer.Stem("рече");
        var речі = SlavicStemmer.Stem("речі");
        рече.Should().Be(речі, "this test is about the collision, so it needs one");
        Lexicon(("G2036", "рече"), ("G4229", "речі"));
        await _db.SaveChangesAsync();

        var evidence = await new EvidentiaDictionarySenseIndex(_db, Packs).For(
            [Word("рече", "ukr"), Word("речі", "ukr")]);

        evidence!.Find(Analyse(Word("рече", "ukr")), Target("G4229")).Should().BeEmpty();
        Assert.Single(evidence.Find(Analyse(Word("рече", "ukr")), Target("G2036")));
        evidence.Find(Analyse(Word("речі", "ukr")), Target("G2036")).Should().BeEmpty();
    }

    private async Task Rendered(string slug, string language, string first, string second)
    {
        var learned = Corpus.Add(_db, slug, TextKind.Translation, language, (1, 1, [first]), (1, 2, [second]));
        var hebrew = Corpus.Add(_db, "HEBT", TextKind.CriticalEdition, "hbo", (1, 1, ["ארץ"]), (1, 2, ["ארץ"]));
        await _db.SaveChangesAsync();
        foreach (var verse in (int[])[1, 2])
        {
            _db.WordAt(hebrew, 1, verse, 1).StrongNumber = "H776";
            var link = new Link
            {
                FromText = learned,
                ToText = hebrew,
                Relation = LinkRelation.Renders,
                Method = LinkMethod.StatedBySource,
                Provenance = new() { Source = "a test's stated table" },
            };
            _db.Links.Add(link);
            _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(learned, 1, verse, 1), Side = LinkSide.From });
            _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(hebrew, 1, verse, 1), Side = LinkSide.To });
        }

        await _db.SaveChangesAsync();
    }

    private void Lexicon(params (string Number, string Definition)[] entries)
    {
        foreach (var (number, definition) in entries)
        {
            _db.StrongEntries.Add(new StrongEntry { StrongNumber = number, Definition = definition });
            _db.StrongEntryTranslations.Add(new StrongEntryTranslation
            {
                StrongNumber = number,
                Language = "ukr",
                Definition = definition,
                Method = LinkMethod.ModelReading,
                Source = "a test lexicon",
            });
        }
    }

    private static EvidentiaToken Word(string surface, string language) =>
        new(surface.GetHashCode(), new EvidentiaAddress(2, 2, 1), 1, surface, language);

    private static EvidentiaAnalysis Analyse(EvidentiaToken token) =>
        Packs.TryAnalyse(token, out var analysis) ? analysis : throw new InvalidOperationException(token.Language);

    private static EvidentiaAnalysis Target(string strongNumber) =>
        new OriginalLanguagePack().Analyse(
            new EvidentiaToken(11, new EvidentiaAddress(2, 2, 1), 1, "x", "grc", StrongNumber: strongNumber, PartOfSpeech: "noun"));

    private void Clear()
    {
        _db.StrongEntryTranslations.ExecuteDelete();
        _db.StrongEntries.ExecuteDelete();
        _db.LinkClaims.ExecuteDelete();
        _db.LinkWords.ExecuteDelete();
        _db.Links.ExecuteDelete();
        _db.Words.ExecuteDelete();
        _db.VerseReferences.ExecuteDelete();
        _db.Verses.ExecuteDelete();
        _db.Chapters.ExecuteDelete();
        _db.Books.ExecuteDelete();
        _db.Texts.ExecuteDelete();
        _db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }
}
