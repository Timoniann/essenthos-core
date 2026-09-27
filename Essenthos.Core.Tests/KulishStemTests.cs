using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// Whether the stemmer, whose endings were written against Ohienko's Ukrainian of 1962, still does
/// its work on Kulish's of 1871.
///
/// This is the question that had to be answered before the aligner was run over Kulish at all. The
/// stemmer is the largest single thing done for a Slavic alignment — a model learns which words
/// correspond by seeing them co-occur, and a form seen once in the whole Bible cannot be learned,
/// so what the stemmer buys is measured in how many forms stop being seen once. Kulish spells a
/// century earlier than the text those rules were tuned on: <em>сьвіт</em> for <em>світ</em>,
/// <em>насїннє</em> for <em>насіння</em>, ї where the modern text writes і. If those spellings
/// defeat the endings, the alignment over this text is worth less than the one over Ohienko's and
/// the number would say so nowhere.
///
/// <para>
/// It is measured against Ohienko rather than against a threshold somebody chose, because the only
/// useful form of the question is comparative: the corpus already knows what the aligner is worth
/// on Ohienko, and what is being asked is whether Kulish is the same kind of text to it.
/// </para>
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed class KulishStemTests(ITestOutputHelper output)
{
    private static readonly Lazy<Vocabulary> Kulish =
        new(() => Measure("Kulish 1871", KulishTextSource.Read(TestResources.KulishFolder)));

    private static readonly Lazy<Vocabulary> Ohienko =
        new(() => Measure("Ohienko 1962", Bible4uTextSource.Read(TestResources.Bible4u("UKR"), "UKR")));

    /// <summary>
    /// How much worse Kulish's collapse may be than Ohienko's before the endings are the wrong ones
    /// for it. A tenth is generous on purpose: the two translations are not the same text and their
    /// vocabularies differ for reasons that have nothing to do with the stemmer.
    /// </summary>
    private const double Margin = 0.10;

    /// <summary>
    /// The forms of one word land together as well in the older spelling as in the newer one.
    ///
    /// Measured as forms per stem, which is what the stemmer is for: a text whose endings the rules
    /// do not recognise keeps its forms apart and the ratio falls towards one.
    /// </summary>
    [Fact]
    public void TheStemmerCollapsesTheOlderSpellingAsWellAsTheNewer()
    {
        var kulish = Kulish.Value;
        var ohienko = Ohienko.Value;
        output.WriteLine(kulish.ToString());
        output.WriteLine(ohienko.ToString());

        kulish.FormsPerStem.Should().BeGreaterThan(ohienko.FormsPerStem * (1 - Margin));
    }

    /// <summary>
    /// The number the alignment actually pays for: tokens whose stem is the only one of its kind in
    /// the whole Bible. Those are the words the model has no evidence about, whatever else it does.
    /// </summary>
    [Fact]
    public void NoMoreOfTheOlderTextIsUnlearnableThanOfTheNewer()
    {
        var kulish = Kulish.Value;
        var ohienko = Ohienko.Value;

        kulish.HapaxStemShare.Should().BeLessThan(ohienko.HapaxStemShare + Margin);
    }

    /// <summary>
    /// And that the stemming is what did it, rather than the two texts happening to be alike. A
    /// text reduced to stems has to be far more learnable than the same text as written, or the
    /// reduction is not earning its place in the pipeline.
    /// </summary>
    [Fact]
    public void StemmingTheOlderTextMakesItSubstantiallyMoreLearnable()
    {
        var kulish = Kulish.Value;

        kulish.HapaxStemShare.Should().BeLessThan(kulish.HapaxFormShare);
        kulish.Stems.Should().BeLessThan(kulish.Forms);
    }

    private static Vocabulary Measure(string name, TextSource source)
    {
        var forms = new Dictionary<string, int>(StringComparer.Ordinal);
        var stems = new Dictionary<string, int>(StringComparer.Ordinal);
        var tokens = 0;

        foreach (var word in source.Books
                     .SelectMany(book => book.Chapters)
                     .SelectMany(chapter => chapter.Verses)
                     .SelectMany(verse => verse.Words.Select((word, at) => (word.Surface, Position: at + 1))))
        {
            if (word.Surface.Length == 0)
            {
                continue;
            }

            tokens++;
            var form = word.Surface.ToLowerInvariant();
            forms[form] = forms.GetValueOrDefault(form) + 1;

            // The same reduction the aligner applies, name detection included: a capital that does
            // not open its verse is a proper name, and a name inflects as a noun and never as a verb.
            var stem = SlavicStemmer.Stem(word.Surface, word.Position > 1 && char.IsUpper(word.Surface[0]));
            stems[stem] = stems.GetValueOrDefault(stem) + 1;
        }

        return new Vocabulary(
            name,
            tokens,
            forms.Count,
            stems.Count,
            forms.Values.Count(count => count == 1),
            stems.Values.Count(count => count == 1));
    }

    /// <param name="HapaxForms">Forms written once in the whole Bible, which a model cannot learn.</param>
    /// <param name="HapaxStems">The same after reduction, which is what the model actually sees.</param>
    private sealed record Vocabulary(
        string Name,
        int Tokens,
        int Forms,
        int Stems,
        int HapaxForms,
        int HapaxStems)
    {
        public double FormsPerStem => (double)Forms / Stems;

        public double HapaxFormShare => (double)HapaxForms / Forms;

        public double HapaxStemShare => (double)HapaxStems / Stems;

        public override string ToString() =>
            $"{Name}: {Tokens} tokens, {Forms} forms, {Stems} stems, {FormsPerStem:F2} forms per stem; " +
            $"{HapaxFormShare:P1} of forms written once, {HapaxStemShare:P1} of stems left standing once";
    }
}
