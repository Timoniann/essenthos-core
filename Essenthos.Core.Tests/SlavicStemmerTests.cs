using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The stemmer is judged on one thing: do the forms of a word land together, and do different words
/// stay apart. Every case here is a word from Genesis 1 that the alignment either missed or nearly
/// missed because the form it wore appears once in the whole Bible.
/// </summary>
public class SlavicStemmerTests
{
    /// <summary>
    /// The case the owner reported. "отделил" in 1:4 and "отделяет" in 1:6 are the same verb and
    /// were two words seen once each.
    /// </summary>
    [Theory]
    [InlineData("отделил", "отделяет", "отделить", "отделю")]
    [InlineData("вода", "воды", "воду", "водою")]
    [InlineData("земля", "земли", "землю", "землёю")]
    [InlineData("сказал", "сказала", "сказали", "сказать")]
    public void TheFormsOfOneWordLandTogether(params string[] forms)
    {
        var stems = forms.Select(form => SlavicStemmer.Stem(form)).Distinct();

        stems.Should().ContainSingle(because: string.Join(", ", forms.Select(f => $"{f} -> {SlavicStemmer.Stem(f)}")));
    }

    /// <summary>Ukrainian inflects the same way, and the same stem has to serve it.</summary>
    [Theory]
    [InlineData("вода", "води", "воду", "водою")]
    [InlineData("небесний", "небесна", "небесні", "небесними")]
    [InlineData("сталося", "сталася", "сталися")]
    public void UkrainianFormsLandTogether(params string[] forms) =>
        forms.Select(form => SlavicStemmer.Stem(form)).Distinct().Should()
            .ContainSingle(because: string.Join(", ", forms.Select(f => $"{f} -> {SlavicStemmer.Stem(f)}")));

    /// <summary>
    /// The failure that would matter more than any missed merge: two different words reduced to one
    /// stem, so the model learns a correspondence for a word that is not there.
    /// </summary>
    [Theory]
    [InlineData("вода", "водитель")]
    [InlineData("свет", "светильник")]
    [InlineData("море", "морковь")]
    [InlineData("бог", "богатство")]
    [InlineData("небо", "небрежность")]
    public void DifferentWordsStayApart(string first, string second) =>
        SlavicStemmer.Stem(first).Should().NotBe(SlavicStemmer.Stem(second));

    /// <summary>
    /// Function words are almost entirely ending. Stripping them leaves nothing to count with, and
    /// they are the words the model has least trouble with in the first place.
    /// </summary>
    [Theory]
    [InlineData("и")]
    [InlineData("в")]
    [InlineData("да")]
    [InlineData("от")]
    [InlineData("над")]
    public void ShortWordsAreLeftAlone(string word) => SlavicStemmer.Stem(word).Should().Be(word);

    /// <summary>
    /// The case a reader found. The gerund ending is a bare -в after а or я, which is right for
    /// сказав and wrong for a name: Аминадав lost its в and Аминадава kept it, so one name became
    /// two stems. In a genealogy where the name stands twice in a verse, one stem matched the Greek
    /// and the other matched nothing and was placed by position alone — onto δέ.
    /// </summary>
    [Theory]
    [InlineData("Аминадав", "Аминадава")]
    [InlineData("Иоав", "Иоава")]
    [InlineData("Ахав", "Ахава")]
    [InlineData("Моав", "Моава")]
    [InlineData("Ісав", "Ісава")]
    public void ANameAndItsInflectionsLandTogether(string bare, string inflected) =>
        SlavicStemmer.Stem(bare, isName: true).Should().Be(SlavicStemmer.Stem(inflected, isName: true));

    /// <summary>
    /// And the rule the exemption is taken from still holds for the words it is for: сказав is a
    /// gerund and must reach the same stem as сказал.
    /// </summary>
    [Fact]
    public void AGerundStillLosesItsEnding() =>
        SlavicStemmer.Stem("сказав").Should().Be(SlavicStemmer.Stem("сказал"));

    [Fact]
    public void TheStemIsNeverEmpty() =>
        new[] { "ая", "ими", "ость", "ться", "ы", "ю" }
            .Select(form => SlavicStemmer.Stem(form)).Should().OnlyContain(stem => stem.Length > 0);

    /// <summary>Case and the ё/е spelling are not two different words.</summary>
    [Fact]
    public void CaseAndTheYoSpellingAreNotDistinctions()
    {
        SlavicStemmer.Stem("Земля").Should().Be(SlavicStemmer.Stem("земля"));
        SlavicStemmer.Stem("всё").Should().Be(SlavicStemmer.Stem("все"));
    }

    /// <summary>Stemming a stem again must not keep eating it.</summary>
    [Theory]
    [InlineData("отделил")]
    [InlineData("небесными")]
    [InlineData("пресмыкающихся")]
    public void StemmingIsStableOnceApplied(string word)
    {
        var once = SlavicStemmer.Stem(word);

        SlavicStemmer.Stem(once).Should().Be(once);
    }
}
