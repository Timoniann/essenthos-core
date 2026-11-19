using Essenthos.Core.ClearBible;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The Reina-Valera records that put a Hebrew or Greek word on the Spanish <em>y</em> before the word
/// that renders it, read from the files themselves.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class ClearBibleShiftTests(ITestOutputHelper output)
{
    private static readonly string Folder = Path.Combine(TestResources.ClearBibleFolder, "data");

    private static List<ClearBibleRecord> Shifted(string alignment, string target, string source, ClearBibleShift shift)
    {
        var records = ClearBibleAlignment.Records(Path.Combine(Folder, alignment)).ToList();
        var ands = ClearBibleLinks.Ands(Path.Combine(Folder, target), shift.And);
        var joining = ClearBibleLinks.Joining(Path.Combine(Folder, source), shift.Also);
        var named = ClearBibleLinks.Named(records);
        return [.. records.Where(record => ClearBibleLinks.Shifted(record, ands, joining, named))];
    }

    [Fact]
    public void AnIdentifierKeepsItsMorphemeAndAWordIsTheElevenDigits()
    {
        ClearBibleAlignment.Unit("o010010040041").Should().Be("010010040041");
        ClearBibleAlignment.Word("o010010040041").Should().Be("01001004004");
        ClearBibleAlignment.Unit("n40001001001").Should().Be("40001001001");
    }

    /// <summary>
    /// Genesis 1:3-4: the second וַיְהִי is on the <em>y</em> of <em>y fué</em>, וַיַּרְא on the
    /// <em>Y</em> of <em>Y vió</em>, וַיַּבְדֵּל on the <em>y</em> of <em>y apartó</em>, and none of
    /// the three verbs after them is named. The first verb of 1:3 is on <em>dijo</em> and stays.
    /// </summary>
    [Fact]
    public void TheOldTestamentRecordsOnTheAndBeforeTheirWordAreFound()
    {
        var shifted = Shifted(
            Path.Combine("spa", "alignments", "RV09", "WLCM-RV09-manual.json"),
            Path.Combine("spa", "targets", "RV09", "ot_RV09.tsv"),
            Path.Combine("sources", "WLCM.tsv"),
            ShippedStrongNumbers.ClearBible("RV1909-BHSA").Shift!);
        output.WriteLine($"{shifted.Count} Old Testament records on the 'and' before their word");

        var targets = shifted.Select(record => record.Target[0]).ToHashSet();
        targets.Should().Contain(["01001003009", "01001004001", "01001004010"]);
        targets.Should().NotContain("01001003002");
        shifted.Count.Should().BeInRange(4_900, 5_050);
    }

    [Fact]
    public void TheNewTestamentRecordsOnTheAndBeforeTheirWordAreFewAndNeverAConjunction()
    {
        var shifted = Shifted(
            Path.Combine("spa", "alignments", "RV09", "SBLGNT-RV09-manual.json"),
            Path.Combine("spa", "targets", "RV09", "nt_RV09.tsv"),
            Path.Combine("sources", "SBLGNT.tsv"),
            ShippedStrongNumbers.ClearBible("RV1909-NESTLE1904").Shift!);
        output.WriteLine($"{shifted.Count} New Testament records on the 'and' before their word");

        shifted.Should().Contain(record => record.Target[0] == "40014028006");
        shifted.Count.Should().BeInRange(100, 200);
    }

    /// <summary>גַּם is rendered by <em>y</em> often enough that a record saying so is kept.</summary>
    [Fact]
    public void ARecordOnTheAndFromAParticleThatMeansAlsoIsKept()
    {
        var record = new ClearBibleRecord(["o240460160031"], ["24046016005"]);
        var ands = new Dictionary<string, string?> { ["24046016005"] = "24046016006" };

        ClearBibleLinks.Shifted(record, ands, new HashSet<string> { "240460160031" }, new HashSet<string>())
            .Should().BeFalse();
        ClearBibleLinks.Shifted(record, ands, new HashSet<string>(), new HashSet<string>())
            .Should().BeTrue();
    }

    [Fact]
    public void ARecordOnTheAndIsKeptWhereTheWordAfterItIsNamed()
    {
        var record = new ClearBibleRecord(["o010010040012"], ["01001004001"]);
        var ands = new Dictionary<string, string?> { ["01001004001"] = "01001004002" };

        ClearBibleLinks.Shifted(record, ands, new HashSet<string>(), new HashSet<string> { "01001004002" })
            .Should().BeFalse();
    }
}
