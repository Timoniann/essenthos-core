using Essenthos.Core.Berean;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The pairs' rulings (<c>mapping/&lt;pair&gt;/rulings</c>) that reach a producer before the merge: the words where the
/// Berean tables and Clear Bible disagree and this project read the English to settle it, which each source leaves out of
/// what it says and nothing else, and the statistical pairings the held editions' wording rules out, which no composition
/// writes.
/// </summary>
public sealed class LinkRulingTests : IDisposable
{
    private const string SolutionFile = "Essenthos.Core.sln";

    private const string Berean = "Berean Standard Bible translation tables";

    private const string ClearBible = "Clear Bible Alignments";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-rulings-test", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void TheBereanAndClearBibleRulingsSetOneWitnessWordAsideEachAndSayWhatTheEnglishReads()
    {
        var rulings = Rulings(new ProductStore(Path.Combine(Repository(), "Resources")), "BSB-NESTLE1904")
            .Where(ruling => ruling.From.Length == 0).ToList();

        rulings.Should().HaveCount(18);
        rulings.Count(ruling => ruling.Source == Berean).Should().Be(3);
        rulings.Count(ruling => ruling.Source == ClearBible).Should().Be(15);
        rulings.Should().OnlyContain(ruling => ruling.Action == RulingRow.Refuse
            && MappingText.Split(ruling.To).Count() == 1
            && ruling.Reason.Contains(" stands over ", StringComparison.Ordinal)
            && ruling.Reason.Contains("BSB English reads ", StringComparison.Ordinal));
        rulings.Select(ruling => ruling.To).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Genesis 14:5 in the Septuagint: the Zuzim are rendered "strong nations", and the aligner paired the names after them
    /// one place off, the Emim's Ὀμμαίους with the Zuzim and Shaveh's Σαυῇ with Ham.
    /// </summary>
    [Fact]
    public void TheSeptuagintsGenesisFourteenFivePairingsAreRefusedToTheAligner()
    {
        var refused = Rulings(new ProductStore(Path.Combine(Repository(), "Resources")), "GRCBRENT-BHSA")
            .Where(ruling => ruling.Action == RulingRow.Refuse && ruling.Source == CompositionPipeline.Machine)
            .Select(ruling => (ruling.From, ruling.To));

        refused.Should().Contain([("1.14:5.29", "1.14:5.22"), ("1.14:5.32", "1.14:5.24")]);
    }

    [Fact]
    public void ACompositionLeavesOutOnlyTheWordPairsItsPairsRulingsRefuseToTheAligner()
    {
        var store = new ProductStore(_root);
        ProductCorpus.Text(store, "RUSV", "rus", [("46.5:5", ["предать", "сатане", "Иисуса", "Христа"])]);
        ProductCorpus.Text(store, "NESTLE1904", "grc", [("46.5:5", ["παραδοῦναι", "τῷ", "Σατανᾷ", "Ἰησοῦ"])]);
        var file = Path.Combine(_root, "Essenthos", "mapping", "RUSV-NESTLE1904", "rulings.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllLines(file,
        [
            "# producer   by hand",
            "action	from	to	relation	source	reason",
            "refuse	46.5:5.4	46.5:5.3	renders	SIL.Machine	Christ is not Satan",
            "refuse	46.5:5.1	46.5:5.1	renders		a refusal of every source is the merge's",
            "refuse	46.5:5.2	46.5:5.2	renders	Berean Standard Bible translation tables	another source's",
            "refuse	46.5:5.3	46.5:5.3 46.5:5.4	renders	SIL.Machine	a phrase, not a word pair",
            "accept	46.5:5.2	46.5:5.3	renders	SIL.Machine	an accepted shape",
            "refuse	46.5:5.4	46.5:5.9	renders	SIL.Machine	a word the text lacks",
            "refuse		46.5:5.4		SIL.Machine	one side only",
        ]);
        var aligner = new AlignerFiles(new ProductTexts(store), new AlignmentPipeline(null, NullLogger<AlignmentPipeline>.Instance) { Trains = false });

        aligner.Rejected("RUSV", "NESTLE1904").Should().Equal((3L, 2L));
        aligner.Rejected("NESTLE1904", "RUSV").Should().BeEmpty();
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
            .Select((word, at) => new BereanLinks.Word(100 + at, 1, word, null))
            .ToList();
        List<List<long>> witness = [.. rows.Select(row => new List<long> { (long)row.OriginalOrder })];
        var drafts = new List<BereanLinks.Draft>();
        var silences = new BereanLinks.Silences();

        BereanLinks.Pair(rows, ours, witness, drafts, silences, new HashSet<long> { 1 }).Should().BeTrue();

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
        ClearBibleLinks.Trim(alone, overruled).Should().Be((false, true));

        List<long> beside = [7, 8, 9];
        ClearBibleLinks.Trim(beside, overruled).Should().Be((true, true));
        beside.Should().Equal(8, 9);

        List<long> untouched = [8];
        ClearBibleLinks.Trim(untouched, overruled).Should().Be((true, false));
        untouched.Should().Equal(8);
    }

    private static List<RulingRow> Rulings(ProductStore store, string pair)
    {
        using var reader = store.Open(MappingProducts.Of(pair, MappingProducts.Rulings), RulingRow.Schema);
        return [.. reader.Rows()];
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
                   $"No {SolutionFile} above {AppContext.BaseDirectory}, so the rulings cannot be found. "
                   + "Run the tests from inside the essenthos-core checkout.");
    }
}
