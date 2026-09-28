using Essenthos.Core.Berean;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The words where the Berean tables and Clear Bible disagree and this project read the English to
/// settle it: the file names each word canonically and by its letters, and each loader leaves out
/// what it said about a word ruled against it and nothing else.
/// </summary>
public class LinkRulingTests
{
    private const string SolutionFile = "Essenthos.Core.sln";

    [Fact]
    public void EveryRulingNamesItsWordCanonicallyAndSaysWhatTheEnglishReads()
    {
        var rulings = LinkRulings.Read(Path.Combine(Repository(), "Resources")).All;

        rulings.Should().HaveCount(18);
        rulings.Count(ruling => ruling.Over == LinkRulings.Berean).Should().Be(3);
        rulings.Count(ruling => ruling.Over == LinkRulings.ClearBible).Should().Be(15);
        rulings.Should().OnlyContain(ruling =>
            ruling.Text == "BSB" && ruling.Witness == "NESTLE1904"
            && ruling.Stands != ruling.Over
            && (ruling.Stands == LinkRulings.Berean || ruling.Stands == LinkRulings.ClearBible)
            && ruling.Occurrence >= 1
            && ruling.Reason.StartsWith("BSB English reads ", StringComparison.Ordinal));
        rulings.Where(ruling => !LinkRulings.Address(ruling.Reference, out _, out _, out _))
            .Should().BeEmpty("every ruling names a verse by its canonical book, chapter and verse");
        rulings.Select(ruling => (ruling.Reference, ruling.Word, ruling.Occurrence))
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void AReferenceIsReadAsTheCanonicalBookChapterAndVerse()
    {
        LinkRulings.Address("2 Corinthians 13:5", out var book, out var chapter, out var verse).Should().BeTrue();
        (book, chapter, verse).Should().Be((47, 13, 5));

        LinkRulings.Address("Revelation 8:5", out book, out chapter, out verse).Should().BeTrue();
        (book, chapter, verse).Should().Be((66, 8, 5));

        LinkRulings.Address("Nowhere 1:1", out _, out _, out _).Should().BeFalse();
    }

    /// <summary>
    /// 2 Corinthians 13:5 prints the verse's first <em>ἑαυτοὺς</em> with a capital; the one after
    /// <em>ἐπιγινώσκετε</em> is still the third.
    /// </summary>
    [Fact]
    public void AnOccurrenceIsCountedOnTheLettersWithoutAccentsOrCase()
    {
        string[] verse =
        [
            "Ἑαυτοὺς", "πειράζετε", "εἰ", "ἐστὲ", "ἐν", "τῇ", "πίστει", "ἑαυτοὺς", "δοκιμάζετε", "ἢ", "οὐκ",
            "ἐπιγινώσκετε", "ἑαυτοὺς", "ὅτι",
        ];

        LinkRulings.Locate(verse, "ἑαυτοὺς", 3, "grc").Should().Be(12);
        LinkRulings.Locate(verse, "ἑαυτούς", 1, "grc").Should().Be(0);
        LinkRulings.Locate(verse, "ἑαυτοὺς", 4, "grc").Should().BeNull();
    }

    /// <summary>
    /// Matthew 20:9, where the table gives <em>ἐλθόντες</em> a dash and the English reads
    /// <em>came</em>: the ruling keeps the table from writing it as a word the English leaves out,
    /// and the verse's other absence stands.
    /// </summary>
    [Fact]
    public void ABereanAbsenceRuledAgainstIsNotWritten()
    {
        var rows = new[]
        {
            Row(1, 3, "ἐλθόντες", " - "),
            Row(2, 1, "οἱ", " - "),
            Row(3, 2, "ἐργάται", " The workers "),
            Row(4, 4, "ἔλαβον", " each received "),
        };
        var ours = "The workers each received".Split(' ')
            .Select((word, at) => new BereanLinkLoader.Word(100 + at, 1, word, null))
            .ToList();
        List<List<long>> witness = [.. rows.Select(row => new List<long> { (long)row.OriginalOrder })];
        var drafts = new List<BereanLinkLoader.Draft>();
        var silences = new BereanLinkLoader.Silences();

        BereanLinkLoader.Pair(rows, ours, witness, drafts, silences, new HashSet<long> { 1 }).Should().BeTrue();

        drafts.Should().ContainSingle(draft => draft.Relation == LinkRelation.Omits)
            .Which.To.Should().Equal(2);
        drafts.Count(draft => draft.Relation == LinkRelation.Renders).Should().Be(2);
        drafts.SelectMany(draft => draft.To).Should().NotContain(1);
        (silences.Overruled, silences.Absent).Should().Be((1, 1));
    }

    /// <summary>
    /// A Clear Bible record whose only witness word is ruled against is withheld, as its
    /// <em>καὶ</em> = <em>rumblings</em> in Revelation 8:5; one naming other words too, as its
    /// <em>τὸν προπάτορα</em> = <em>forefather</em> in Romans 4:1, is written without it.
    /// </summary>
    [Fact]
    public void AClearBibleRecordLosesTheWordRuledAgainstOrIsWithheld()
    {
        var overruled = new HashSet<long> { 7 };

        List<long> alone = [7];
        LinkRulings.Trim(alone, overruled).Should().Be((false, true));

        List<long> beside = [7, 8, 9];
        LinkRulings.Trim(beside, overruled).Should().Be((true, true));
        beside.Should().Equal(8, 9);

        List<long> untouched = [8];
        LinkRulings.Trim(untouched, overruled).Should().Be((true, false));
        untouched.Should().Equal(8);
    }

    private static BereanRow Row(double order, int english, string greek, string rendering) =>
        new(order, english, 1, "Greek", greek, greek, string.Empty, "Matthew 20:9", rendering);

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFile)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new DirectoryNotFoundException(
                   $"No {SolutionFile} above {AppContext.BaseDirectory}, so the link rulings cannot be found. "
                   + "Run the tests from inside the essenthos-core checkout.");
    }
}
