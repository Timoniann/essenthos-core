using Essenthos.Core.BetaMasaheft;
using Essenthos.Core.Corpus;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A Ge'ez word reduced for the aligner: its consonants, with the homophones the scribes
/// interchange made one, and the words Ge'ez writes onto it taken off. Both halves are tested, as
/// for the other stemmers: that the forms of one word meet, and that a word is not cut into another.
/// </summary>
public class GeezStemmerTests
{
    [Theory]
    [InlineData("እግዚአብሔር")]
    [InlineData("ለእግዚአብሔር")]
    [InlineData("ወለእግዚአብሔር")]
    [InlineData("በእግዚአብሔር")]
    [InlineData("ዘእግዚአብሔር")]
    [InlineData("እምእግዚአብሔር")]
    public void TheWordsWrittenOntoTheFrontComeOff(string form)
    {
        GeezStemmer.Stem(form).Should().Be(GeezStemmer.Stem("እግዚአብሔር"));
    }

    /// <summary>The vowels are where Ge'ez inflects a noun: the accusative, the construct, "his".</summary>
    [Theory]
    [InlineData("ቃል")]
    [InlineData("ቃለ")]
    [InlineData("ቃሉ")]
    [InlineData("ወቃሉ")]
    public void TheVowelsOfAFormAreNotTheWord(string form)
    {
        GeezStemmer.Stem(form).Should().Be(GeezStemmer.Stem("ቃል"));
    }

    [Theory]
    [InlineData("ሐ", "ሀ")]
    [InlineData("ኀ", "ሀ")]
    [InlineData("ሠ", "ሰ")]
    [InlineData("ዐ", "አ")]
    [InlineData("ፀ", "ጸ")]
    public void TheHomophonesTheScribesInterchangeAreOneConsonant(string one, string other)
    {
        GeezStemmer.Stem(one + "ገረ").Should().Be(GeezStemmer.Stem(other + "ገረ"));
    }

    [Fact]
    public void TheLabiovelarIsTheConsonantItRounds()
    {
        GeezStemmer.Stem("ኵሉ").Should().Be(GeezStemmer.Stem("ኩሉ"));
    }

    /// <summary>"His brother", "your brother", "their brother" and "brother" are one word to a model.</summary>
    [Theory]
    [InlineData("እኁሁ")]
    [InlineData("እኁከ")]
    [InlineData("እኁሆሙ")]
    [InlineData("እኁክሙ")]
    public void OnePronounComesOffTheEnd(string form)
    {
        GeezStemmer.Stem(form).Should().Be(GeezStemmer.Stem("እኁ"));
    }

    /// <summary>
    /// A prefix is only a prefix where a word is left behind it: <em>ወለደ</em> "he begot" begins with
    /// the letters of "and" and "to", and taking both leaves one letter that is no word.
    /// </summary>
    [Fact]
    public void AWordIsNotCutDownToALetter()
    {
        GeezStemmer.Stem("ወለደ").Should().NotBe(GeezStemmer.Stem("ደ"));
        GeezStemmer.Unprefixed("ወለደ").Should().Be("ለደ");
        GeezStemmer.Unprefixed("ኮነ").Should().Be("ኮነ");
    }

    /// <summary>
    /// "My" is taken only after a consonant with no vowel. After a vowel it is as often the word's
    /// own last letter, and "heaven" must not lose its y in the accusative.
    /// </summary>
    [Fact]
    public void MyIsTakenOnlyWhereItCanBeMy()
    {
        GeezStemmer.Stem("ቤትየ").Should().Be(GeezStemmer.Stem("ቤት"));
        GeezStemmer.Stem("ሰማየ").Should().Be(GeezStemmer.Stem("ሰማይ"));
    }

    [Fact]
    public void ANumeralIsLeftAsItIsWritten()
    {
        GeezStemmer.Stem("፲፪").Should().Be("፲፪");
    }

    [Fact]
    public void ASearchFindsTheWordWhicheverHomophoneItIsTypedWith()
    {
        WordFolding.Fold("ሐመረ", "gez").Should().Be(WordFolding.Fold("ሀመረ", "gez"));
        WordFolding.Fold("ኀበ", "gez").Should().Be("ሀበ");
        WordFolding.Fold("ሙሴ", "gez").Should().Be("ሙሴ");
    }

    /// <summary>Ge'ez names come through the Greek, so their consonants are the Greek's.</summary>
    [Theory]
    [InlineData("ሙሴ", "Μωυσῆς")]
    [InlineData("ለሙሴ", "Μωυσῇ")]
    [InlineData("ያዕቆብ", "Ἰακώβ")]
    [InlineData("ኢየሩሳሌም", "Ἰερουσαλήμ")]
    [InlineData("ፈርዖን", "Φαραώ")]
    public void ANameIsSpeltByTheConsonantsTheGreekWrites(string geez, string greek)
    {
        NameLists.Alike(NameLists.Skeleton(geez, "gez"), NameLists.Skeleton(greek, "grc"))
            .Should().BeGreaterThanOrEqualTo(NameLists.LeastLikeness);
    }

    /// <summary>
    /// Ethiopic has no capitals, so a Ge'ez word is a name only where it spells a name the Greek of
    /// the verse marks, all its consonants, and has at least three of them.
    /// </summary>
    [Fact]
    public void AWordIsTakenForANameOnlyWhereItSpellsOneTheGreekMarks()
    {
        var names = NameLists.Unmarked(
            [NameLists.Skeleton("ኢየሩሳሌም", "gez"), NameLists.Skeleton("ሀገር", "gez"), NameLists.Skeleton("ዳዊት", "gez")],
            [NameLists.Skeleton("Ἰερουσαλήμ", "grc"), null]);

        names[0].Should().NotBeNull();
        names[1].Should().BeNull();
        names[2].Should().BeNull("David is not a name of this Greek verse, and has only two consonants");
    }

    [Fact]
    public void AMapLineIsTheGeezVersesAgainstTheGreekAddresses()
    {
        var lines = GeezVerseMap.Parse("# comment\n91\t1:1-3\t20\t1:1-2;2:4\t0.9\n17\t5:3\t17\t-\t0.8\n");

        lines.Should().HaveCount(2);
        lines[0].From.Should().Equal((1, 1), (1, 2), (1, 3));
        lines[0].To.Should().Equal((20, 1, 1), (20, 1, 2), (20, 2, 4));
        lines[1].To.Should().BeEmpty();
    }
}
