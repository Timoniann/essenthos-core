using Essenthos.Core.StepBible;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// How STEPBible's brief Greek lexicon is read: only its entry lines, each form an entry prints, and
/// the gloss without the punctuation the file leaves after it.
/// </summary>
public class BriefGreekLexiconTests
{
    [Fact]
    public void ReadsTheEntriesAndNothingOfThePreamble()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path,
            [
                "eStrong\tdStrong\tuStrong\tGreek\tTransliteration\tMorph\tGloss\tAbbott-Smith lexicon",
                "* Gloss = a meaning in one word or as few as possible",
                "G0001\tG0001G =\tG0001G\tα, Ἀλφα\tAlpha\tG:N-LI\tAlpha\t<b>Α, α</b> the first letter",
                "G0001\tG0001H =\tG0001H\tἆ\ta\tG:INJ\tah!\t<b>ἔα</b>",
                "G20003\tG20003 =\tG20003\tἀβούλευτος\tabouleutos\t\till-advised,.\till-advised",
            ]);

            var entries = BriefGreekLexicon.Read(path).ToList();

            entries.Select(entry => (entry.Entry, entry.StrongNumber, entry.Gloss)).Should().Equal(
                ("G0001G", "G1", "Alpha"),
                ("G0001H", "G1", "ah!"),
                ("G20003", "G20003", "ill-advised"));
            entries[0].Lemmas.Should().Equal("α", "Ἀλφα");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>The copy on disk: every entry has a number, a form and a gloss, and an identifier of its own.</summary>
    [Fact]
    public void TheFileOnDiskReadsWhole()
    {
        var entries = BriefGreekLexicon.Read(TestResources.Path("STEPBibleLexicons", "TBESG.txt")).ToList();

        entries.Should().HaveCountGreaterThan(11_000);
        entries.Select(entry => entry.Entry).Should().OnlyHaveUniqueItems();
        entries.Should().Contain(entry => entry.StrongNumber == "G746" && entry.Gloss == "beginning");
    }
}
