using Essenthos.Core.Bhsa;
using Essenthos.Core.Loading;
using Essenthos.Core.Nestle;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>Parsed once; both witnesses together are about three seconds.</summary>
public sealed class ParsedWitnesses
{
    public BhsaProject Bhsa { get; } = BhsaProject.Load(TestResources.Etcbc);

    public IReadOnlyList<NestleWord> Nestle { get; } =
        new NestleParser().Parse(File.ReadAllText(TestResources.Nestle1904), glossText: null);
}

/// <summary>
/// What the two witnesses become before anything is written. The one thing that has to be right
/// here is that a book's place in this text and its place in the shared order are two numbers.
/// </summary>
public class TextSourceTests(ParsedWitnesses witnesses) : IClassFixture<ParsedWitnesses>
{
    [Fact]
    public void EveryBhsaBookHasACanonicalOrdinal()
    {
        var source = BhsaTextSource.Build(witnesses.Bhsa);

        source.Books.Should().HaveCount(39);
        source.Books.Select(b => b.CanonicalOrdinal).Should().OnlyHaveUniqueItems();
        source.Books.Select(b => b.CanonicalOrdinal).Should().BeEquivalentTo(Enumerable.Range(1, 39));
    }

    /// <summary>
    /// BHSA's eighth book is 1 Samuel and the canonical eighth is Ruth. Reading one number as the
    /// other is what made the old API answer a request for one book with another, so the two are
    /// separate columns and this is the case that proves they disagree.
    /// </summary>
    [Fact]
    public void TheTanakhOrderIsNotTheCanonicalOrder()
    {
        var source = BhsaTextSource.Build(witnesses.Bhsa);

        var eighth = source.Books.Single(b => b.Position == 8);
        eighth.Name.Should().Be("1 Samuel");
        eighth.CanonicalOrdinal.Should().Be(9);

        source.Books.Single(b => b.CanonicalOrdinal == 8).Name.Should().Be("Ruth");
        source.Books.Count(b => b.Position != b.CanonicalOrdinal).Should().BeGreaterThan(0);
    }

    /// <summary>
    /// They agree through Judges and part company at the eighth book, because the Tanakh puts Ruth
    /// among the Writings and the canonical order puts it after Judges. Thirty-two of the
    /// thirty-nine books sit at a different number in each.
    /// </summary>
    [Fact]
    public void TheTwoOrdersPartCompanyAtTheEighthBook()
    {
        var source = BhsaTextSource.Build(witnesses.Bhsa);

        source.Books.Where(b => b.Position <= 7).Should().OnlyContain(b => b.Position == b.CanonicalOrdinal);
        source.Books.Count(b => b.Position != b.CanonicalOrdinal).Should().Be(32);
    }

    [Fact]
    public void EveryNestleBookHasACanonicalOrdinal()
    {
        var source = NestleTextSource.Build(witnesses.Nestle);

        source.Books.Should().HaveCount(27);
        source.Books.Select(b => b.CanonicalOrdinal).Should().BeEquivalentTo(Enumerable.Range(40, 27));
    }

    /// <summary>The New Testament is in canonical order already, so the two numbers are offset.</summary>
    [Fact]
    public void NestlePositionsRunFromOneWhileItsCanonicalOrdinalsRunFromForty()
    {
        var source = NestleTextSource.Build(witnesses.Nestle);

        source.Books.Should().OnlyContain(b => b.CanonicalOrdinal == b.Position + 39);
    }

    [Fact]
    public void NoWordIsLostBetweenTheParserAndTheSource()
    {
        BhsaTextSource.Build(witnesses.Bhsa).Books
            .SelectMany(b => b.Chapters).SelectMany(c => c.Verses).Sum(v => v.Words.Count)
            .Should().Be(witnesses.Bhsa.Words.Count);

        NestleTextSource.Build(witnesses.Nestle).Books
            .SelectMany(b => b.Chapters).SelectMany(c => c.Verses).Sum(v => v.Words.Count)
            .Should().Be(witnesses.Nestle.Count);
    }

    /// <summary>
    /// Both texts state a licence, and neither leaves redistribution unknown. BHSA is
    /// non-commercial, which is a constraint on the product and not a detail.
    /// </summary>
    [Fact]
    public void BothWitnessesRecordTheirProvenance()
    {
        foreach (var source in new[] { BhsaTextSource.Build(witnesses.Bhsa), NestleTextSource.Build(witnesses.Nestle) })
        {
            source.Definition.Invoking(d => d.Validate()).Should().NotThrow();
        }

        BhsaTextSource.Build(witnesses.Bhsa).Definition.Redistribution
            .Should().Be(Database.Entities.Enums.Redistribution.NonCommercialOnly);
        NestleTextSource.Build(witnesses.Nestle).Definition.Redistribution
            .Should().Be(Database.Entities.Enums.Redistribution.PublicDomain);
    }

    /// <summary>
    /// The morphology is the annotation this witness happens to carry, and BHSA's includes the
    /// word's own language: the Hebrew Bible has Aramaic in it, so a text has one language and its
    /// words do not.
    /// </summary>
    [Fact]
    public void BhsaWordsCarryTheirOwnLanguage()
    {
        var words = BhsaTextSource.Build(witnesses.Bhsa).Books
            .SelectMany(b => b.Chapters).SelectMany(c => c.Verses).SelectMany(v => v.Words)
            .ToList();

        words.Count(w => w.Morphology!.Contains("\"language\":\"arc\"")).Should().BeGreaterThan(0);
        words.Count(w => w.Morphology!.Contains("\"language\":\"hbo\"")).Should().BeGreaterThan(0);
    }

    /// <summary>
    /// The word written other ways, which is most of what a reader who does not read Hebrew needs.
    /// The counts are pinned because losing one of these is silent: the corpus stays the right size
    /// and every verse still reads correctly, and only the field nobody looked at goes missing.
    /// </summary>
    [Fact]
    public void BhsaWordsCarryTheOtherWaysTheyAreWritten()
    {
        var words = BhsaWords();

        Carrying(words, "consonantal").Should().Be(420_102);
        Carrying(words, "vocalizedLexeme").Should().Be(426_590);
        Carrying(words, "phono").Should().Be(420_166);
        Carrying(words, "qere").Should().Be(1_867);
    }

    /// <summary>
    /// A reading and its trailer are stored together or not at all, the trailer included when it is
    /// empty — a third of these words are followed by nothing, and an absent trailer and an empty
    /// one must not be the same thing.
    /// </summary>
    [Fact]
    public void EveryReadingCarriesItsTrailer()
    {
        var words = BhsaWords();

        Carrying(words, "phonoTrailer").Should().Be(Carrying(words, "phono"));
        Carrying(words, "qereTrailer").Should().Be(Carrying(words, "qere"));
    }

    /// <summary>
    /// The suffixed pronoun's own person, number and gender — a different word's grammar riding on
    /// this one, and the reason the plain "person" of a word is not enough to read it.
    /// </summary>
    [Fact]
    public void BhsaWordsCarryTheGrammarOfTheirPronominalSuffix()
    {
        var words = BhsaWords();

        Carrying(words, "suffixPerson").Should().Be(45_158);
        Carrying(words, "phrasePos").Should().Be(426_590);
    }

    /// <summary>
    /// The transcription rebuilds a verse exactly as the Hebrew does, joining the elided article
    /// onto its noun in the same place: ha + ššāmˌayim is one word in both. That is the property
    /// the reading-and-trailer pair exists for, and Genesis 1:1 is where it is easiest to check.
    /// </summary>
    [Fact]
    public void TheTranscriptionOfAVerseRebuildsTheSameWayItsHebrewDoes()
    {
        var verse = BhsaTextSource.Build(witnesses.Bhsa).Books
            .Single(b => b.CanonicalOrdinal == 1).Chapters
            .Single(c => c.Number == 1).Verses
            .Single(v => v.Number == 1);

        var transcription = string.Concat(verse.Words.Select(w =>
            Feature(w, "phono") + Feature(w, "phonoTrailer")));

        transcription.Should().Be("bᵊrēšˌîṯ bārˈā ʔᵉlōhˈîm ʔˌēṯ haššāmˌayim wᵊʔˌēṯ hāʔˈāreṣ . ");
    }

    private static string Feature(WordDraft word, string name)
    {
        var document = System.Text.Json.JsonDocument.Parse(word.Morphology!);
        return document.RootElement.TryGetProperty(name, out var value) ? value.GetString()! : string.Empty;
    }

    private List<WordDraft> BhsaWords() =>
        BhsaTextSource.Build(witnesses.Bhsa).Books
            .SelectMany(b => b.Chapters).SelectMany(c => c.Verses).SelectMany(v => v.Words)
            .ToList();

    private static int Carrying(List<WordDraft> words, string feature) =>
        words.Count(w => w.Morphology!.Contains($"\"{feature}\":"));

    [Fact]
    public void NestleWordsCarryANormalisedStrongNumber()
    {
        var words = NestleTextSource.Build(witnesses.Nestle).Books
            .SelectMany(b => b.Chapters).SelectMany(c => c.Verses).SelectMany(v => v.Words)
            .ToList();

        words.Should().OnlyContain(w => w.StrongNumber != null && w.StrongNumber.StartsWith('G'));
    }
}
