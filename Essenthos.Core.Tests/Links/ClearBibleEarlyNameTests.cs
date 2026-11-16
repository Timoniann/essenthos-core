using Essenthos.Core.ClearBible;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The Reina-Valera records that put a name on the preposition or article before it and leave the name unnamed,
/// read from the files themselves, and moved onto the name.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class ClearBibleEarlyNameTests(ITestOutputHelper output)
{
    private static readonly string Folder = Path.Combine(TestResources.ClearBibleFolder, "data");

    private static List<(ClearBibleRecord Record, string Name)> Moved(string alignment, string target, string source)
    {
        var records = ClearBibleAlignment.Records(Path.Combine(Folder, "spa", "alignments", "RV09", alignment)).ToList();
        var tokens = ClearBibleAlignment.Tokens(Path.Combine(Folder, "spa", "targets", "RV09", target)).ToList();
        var early = ClearBibleLinks.EarlyNames.Of(
            tokens, ClearBibleSet.ReinaValeraOldTestament("RV1909", "BHSA").Early!, Path.Combine(Folder, "sources", source), records);
        return
        [
            .. records
                .Select(record => (Record: record, Name: ClearBibleLinks.Early(record, early)))
                .Where(moved => moved.Name is not null)
                .Select(moved => (moved.Record, moved.Name!)),
        ];
    }

    /// <summary>
    /// Genesis 10:7's Cush and 14:3's Siddim are on the <em>de</em> before them and move onto the name; Ezra 6:9's
    /// אֱלָהּ on <em>al Dios</em> moves by the set's own pairing of <em>Dios</em> with it. The records whose Hebrew word
    /// another word of the verse renders stay where they are: 2 Kings 16:6's הֵשִׁיב on <em>rey de Siria</em>
    /// (<em>restituyó</em>), 2 Chronicles 25:14's יַּעֲמִידֵם on <em>los Idumeos</em> (<em>púsoselos</em>), Isaiah
    /// 16:7's אֲשִׁישֵׁי on <em>de Kir-hareseth</em> (<em>fundamentos</em>), 1 Kings 3:15's שְׁלָמִים on <em>de
    /// Jehová</em> (<em>pacíficos</em>), Genesis 12:6's אֵלוֹן on <em>valle de Moreh</em> (<em>valle</em>). Nor do
    /// the records of a verse that names the same person or place twice, which the set crosses: 2 Samuel 10:18's second
    /// אֲרָם on the <em>los</em> before the first <em>Siros</em>, Ezra 6:12's second אֱלָהָא before the first
    /// <em>Dios</em>, Nehemiah 11:21's second נְתִינִים before the first <em>Nethineos</em>.
    /// </summary>
    [Fact]
    public void TheOldTestamentNamesOnTheWordBeforeThemMoveOntoTheName()
    {
        var moved = Moved("WLCM-RV09-manual.json", "ot_RV09.tsv", "WLCM.tsv");
        output.WriteLine($"{moved.Count} Old Testament records moved onto the name after them");

        var to = moved.ToDictionary(item => item.Record.Target[0], item => item.Name);
        to.Should().Contain("01010007004", "01010007005");
        to.Should().Contain("01014003008", "01014003009");
        to.Should().Contain("15006009015", "15006009016");
        to.Keys.Should().NotContain(["12016006006", "14025014008", "23016007014", "11003015023", "01012006016"]);
        to.Keys.Should().NotContain(["10010018002", "15006012002", "16011021002"]);
        moved.Count.Should().BeInRange(330, 400);
    }

    [Fact]
    public void TheNewTestamentHasFewAndMatthewsJosephIsOne()
    {
        var moved = Moved("SBLGNT-RV09-manual.json", "nt_RV09.tsv", "SBLGNT.tsv");
        output.WriteLine($"{moved.Count} New Testament records moved onto the name after them");

        moved.Should().Contain(item => item.Name == "40027056016");
        moved.Count.Should().BeInRange(5, 30);
    }

    /// <summary>
    /// A record is moved only where the preposition stands right before a word written as a name that nothing names,
    /// and its source word is a content word that is that name; a Hebrew preposition on <em>á</em> is the preposition's.
    /// </summary>
    [Fact]
    public void ARecordIsMovedOnlyOntoAnUnnamedNameItsSourceWordIs()
    {
        var name = new ClearBibleToken("o010140030071", "שִׂדִּים", false, "7708", Part: "noun");
        var early = new ClearBibleLinks.EarlyNames(
            new Dictionary<string, string> { ["01014003008"] = "01014003009", ["32002003004"] = "32002003005" },
            new Dictionary<string, ClearBibleToken> { ["010140030071"] = name },
            new HashSet<string>(),
            new Dictionary<string, string> { ["01014003009"] = "siddim", ["32002003005"] = "jehova" },
            new HashSet<(string, int)>(),
            new HashSet<string>(),
            new HashSet<string>());

        ClearBibleLinks.Early(new ClearBibleRecord(["o010140030071"], ["01014003008"]), early).Should().Be("01014003009");
        ClearBibleLinks.Early(new ClearBibleRecord(["o320020030041"], ["32002003004"]), early).Should().BeNull();
        ClearBibleLinks.Early(new ClearBibleRecord(["o010140030071"], ["01014003008", "01014003009"]), early).Should().BeNull();
        ClearBibleLinks.Early(new ClearBibleRecord(["o010140030071"], ["01014003008"]), early with { Named = new HashSet<string> { "01014003009" } })
            .Should().BeNull();
        ClearBibleLinks.Early(
                new ClearBibleRecord(["o010140030071"], ["01014003008"]),
                early with { Texts = new Dictionary<string, string> { ["01014003009"] = "valle" } })
            .Should().BeNull();
        ClearBibleLinks.Early(new ClearBibleRecord(["o010140030071"], ["01014003008"]), early with { Repeated = new HashSet<string> { "01014003009" } })
            .Should().BeNull();
        ClearBibleLinks.Early(new ClearBibleRecord(["o010140030071"], ["01014003008"]), early with { RepeatedSources = new HashSet<string> { "010140030071" } })
            .Should().BeNull();
    }
}
