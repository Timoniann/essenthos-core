using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The thirty closed-class words a stemmer cannot hold together.
///
/// Everything here is one claim: these forms are one word, and a caller that asks for the table
/// should be told so. A test that only checked the table returns *something* would pass on a table
/// that merged every pronoun into one lexeme, which is the failure worth guarding — so each case
/// names both what must land together and what must not.
///
/// The table is not on by default: scored against the correspondences the Ukrainian interlinear
/// states it added twelve right links and seventy-one wrong ones, and
/// <see cref="SlavicSuppletion"/> carries the numbers. So every case here asks for it explicitly,
/// which is also what the pipeline does when it is measured.
/// </summary>
public class SlavicSuppletionTests
{
    private static string Joined(string word) => SlavicStemmer.Stem(word, suppletion: true);

    /// <summary>
    /// Ukrainian writes the copula as `є`, `буде` and `був`, which share no stem, so the stemmer
    /// alone leaves the aligner three rare words where the language has one common one. Whether
    /// joining them helps the alignment is a separate question, and a measured one.
    /// </summary>
    [Theory]
    [InlineData("є")]
    [InlineData("буде")]
    [InlineData("був")]
    [InlineData("будуть")]
    [InlineData("бути")]
    public void CountsEveryFormOfTheUkrainianCopulaAsOneWord(string form) =>
        Joined(form).Should().Be(Joined("бути"));

    [Theory]
    [InlineData("был")]
    [InlineData("будет")]
    [InlineData("будут")]
    [InlineData("есть")]
    public void CountsEveryFormOfTheRussianCopulaAsOneWord(string form) =>
        Joined(form).Should().Be(Joined("быть"));

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
        Joined(oblique).Should().Be(Joined(nominative));

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
        Joined(one).Should().NotBe(Joined(other));

    [Theory]
    [InlineData("сказал", "сказать")]
    [InlineData("скажет", "сказать")]
    [InlineData("сказав", "сказати")]
    [InlineData("скажуть", "сказати")]
    public void JoinsTheFormsOfToSay(string form, string word) =>
        Joined(form).Should().Be(Joined(word));

    [Theory]
    [InlineData("бога", "бог")]
    [InlineData("боже", "бог")]
    [InlineData("богові", "бог")]
    public void JoinsTheFormsOfGod(string form, string word) =>
        Joined(form).Should().Be(Joined(word));

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
        Joined(form).Should().Be(Joined("отделил"));
    }

    [Fact]
    public void HasNothingToSayAboutAWordThatIsNotInIt() =>
        SlavicSuppletion.Of("свет").Should().BeNull();

    /// <summary>Case never distinguishes a form, and Russian writes ё and е for one letter.</summary>
    [Theory]
    [InlineData("Бог", "бог")]
    [InlineData("Её", "ее")]
    public void ReadsAFormHoweverItIsWritten(string written, string plain) =>
        Joined(written).Should().Be(Joined(plain));
}
