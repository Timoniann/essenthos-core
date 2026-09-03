using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The thirty closed-class words a stemmer cannot hold together.
///
/// Everything here is one claim: these forms are one word, and the aligner should count them as
/// one. A test that only checked the table returns *something* would pass on a table that merged
/// every pronoun into one lexeme, which is the failure worth guarding — so each case names both
/// what must land together and what must not.
/// </summary>
public class SlavicSuppletionTests
{
    /// <summary>
    /// The case PRB-0076 is about. Ukrainian writes the copula as `є`, `буде` and `був`, which
    /// share no stem, so the aligner saw three rare words instead of one common one — and
    /// "станеться" linked to nothing while "сталося" two words later linked at 0.98.
    /// </summary>
    [Theory]
    [InlineData("є")]
    [InlineData("буде")]
    [InlineData("був")]
    [InlineData("будуть")]
    [InlineData("бути")]
    public void CountsEveryFormOfTheUkrainianCopulaAsOneWord(string form) =>
        SlavicStemmer.Stem(form).Should().Be(SlavicStemmer.Stem("бути"));

    [Theory]
    [InlineData("был")]
    [InlineData("будет")]
    [InlineData("будут")]
    [InlineData("есть")]
    public void CountsEveryFormOfTheRussianCopulaAsOneWord(string form) =>
        SlavicStemmer.Stem(form).Should().Be(SlavicStemmer.Stem("быть"));

    /// <summary>A pronoun's forms share no letters at all, which is why no rule can join them.</summary>
    [Theory]
    [InlineData("я", "меня")]
    [InlineData("я", "мне")]
    [InlineData("он", "его")]
    [InlineData("он", "ему")]
    [InlineData("він", "його")]
    [InlineData("вони", "їх")]
    [InlineData("мы", "нас")]
    public void JoinsThePersonalPronounsToTheirOwnObliqueForms(string nominative, string oblique) =>
        SlavicStemmer.Stem(oblique).Should().Be(SlavicStemmer.Stem(nominative));

    /// <summary>
    /// The guard that matters. A table that merged the pronouns into one another would make the
    /// model confidently wrong everywhere they stand, which is worse than the fragmentation it was
    /// written to fix.
    /// </summary>
    [Theory]
    [InlineData("я", "ты")]
    [InlineData("он", "она")]
    [InlineData("він", "вона")]
    [InlineData("мой", "твой")]
    [InlineData("быть", "бог")]
    [InlineData("кто", "что")]
    public void KeepsTwoDifferentWordsApart(string one, string other) =>
        SlavicStemmer.Stem(one).Should().NotBe(SlavicStemmer.Stem(other));

    [Theory]
    [InlineData("сказал", "сказать")]
    [InlineData("скажет", "сказать")]
    [InlineData("сказав", "сказати")]
    [InlineData("скажуть", "сказати")]
    public void JoinsTheFormsOfToSay(string form, string word) =>
        SlavicStemmer.Stem(form).Should().Be(SlavicStemmer.Stem(word));

    [Theory]
    [InlineData("бога", "бог")]
    [InlineData("боже", "бог")]
    [InlineData("богові", "бог")]
    public void JoinsTheFormsOfGod(string form, string word) =>
        SlavicStemmer.Stem(form).Should().Be(SlavicStemmer.Stem(word));

    /// <summary>
    /// Closed classes only. A content word must reach the stemmer, because the table is written by
    /// hand and cannot grow to the size of a language — and because the stemmer is the right
    /// instrument there: these three differ only by ending.
    /// </summary>
    [Theory]
    [InlineData("отделил")]
    [InlineData("отделяет")]
    [InlineData("отделить")]
    public void LeavesTheOpenClassesToTheStemmer(string form)
    {
        SlavicSuppletion.Of(form).Should().BeNull();
        SlavicStemmer.Stem(form).Should().Be(SlavicStemmer.Stem("отделил"));
    }

    [Fact]
    public void HasNothingToSayAboutAWordThatIsNotInIt() =>
        SlavicSuppletion.Of("свет").Should().BeNull();

    /// <summary>Case never distinguishes a form, and Russian writes ё and е for one letter.</summary>
    [Theory]
    [InlineData("Бог", "бог")]
    [InlineData("Её", "ее")]
    public void ReadsAFormHoweverItIsWritten(string written, string plain) =>
        SlavicStemmer.Stem(written).Should().Be(SlavicStemmer.Stem(plain));
}
