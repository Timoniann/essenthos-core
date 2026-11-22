using Essenthos.Core.ClearBible;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The Reina-Valera records that put a noun, a verb or an adjective on the preposition or article before the common word that
/// renders it, read from the files themselves: moved onto that word where the set renders the same Strong number with it
/// elsewhere, refused where it does not.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class ClearBibleSlippedTests(ITestOutputHelper output)
{
    private static readonly string Folder = Path.Combine(TestResources.ClearBibleFolder, "data");

    private static (Dictionary<string, string> Moved, HashSet<string> Refused) Slipped(string alignment, string target, string source)
    {
        var records = ClearBibleAlignment.Records(Path.Combine(Folder, "spa", "alignments", "RV09", alignment)).ToList();
        var tokens = ClearBibleAlignment.Tokens(Path.Combine(Folder, "spa", "targets", "RV09", target)).ToList();
        var early = ClearBibleLinks.EarlyNames.Of(
            tokens, ShippedStrongNumbers.ClearBible("RV1909-BHSA").Early!, Path.Combine(Folder, "sources", source), records);
        var moved = new Dictionary<string, string>(StringComparer.Ordinal);
        var refused = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records.Where(record => ClearBibleLinks.Early(record, early) is null))
        {
            var (slip, word) = ClearBibleLinks.Slipped(record, early);
            if (slip == ClearBibleLinks.Slip.Moved)
            {
                moved[record.Target[0]] = word!;
            }
            else if (slip == ClearBibleLinks.Slip.Refused)
            {
                refused.Add(record.Target[0]);
            }
        }

        return (moved, refused);
    }

    /// <summary>
    /// Ezekiel 28:13's אֹדֶם on the <em>el</em> of <em>el sardio</em>, 2 Chronicles 1:11's נֶפֶשׁ on <em>el alma</em>, 2 Chronicles
    /// 34:33's אֲרָצוֹת on <em>las tierras</em>, Ezekiel 7:27's שְׁמָמָה on <em>de asolamiento</em>, and two words early, Judges
    /// 3:13's תְּמָרִים on the <em>de</em> of <em>de las palmas</em>, move onto the word. Ruth 3:7's מַרְגְּלֹתָי on <em>los
    /// pies</em>, Job 3:12's שָּׁדַיִם on <em>las tetas</em> and Daniel 8:23's מַלְכוּת on <em>del imperio</em> are refused: the set
    /// renders those numbers with those words in no other record, or too rarely to say so.
    /// </summary>
    [Fact]
    public void TheOldTestamentRecordsMoveOntoTheWordOrAreRefused()
    {
        var (moved, refused) = Slipped("WLCM-RV09-manual.json", "ot_RV09.tsv", "WLCM.tsv");
        output.WriteLine($"{moved.Count} Old Testament records moved onto the common word after them, {refused.Count} refused");

        moved.Should().Contain("26028013018", "26028013019");
        moved.Should().Contain("14001011026", "14001011027");
        moved.Should().Contain("14034033009", "14034033010");
        moved.Should().Contain("26007027011", "26007027012");
        moved.Should().Contain("07003013025", "07003013027");
        refused.Should().Contain(["08003007031", "18003012013", "27008023004"]);
        moved.Count.Should().BeInRange(2400, 2700);
        refused.Count.Should().BeInRange(3000, 3400);
    }

    [Fact]
    public void TheNewTestamentHasTheSameSlip()
    {
        var (moved, refused) = Slipped("SBLGNT-RV09-manual.json", "nt_RV09.tsv", "SBLGNT.tsv");
        output.WriteLine($"{moved.Count} New Testament records moved onto the common word after them, {refused.Count} refused");

        moved.Count.Should().BeInRange(370, 450);
        refused.Count.Should().BeInRange(490, 600);
    }
}

/// <summary>What <see cref="ClearBibleLinks.Slipped"/> moves, refuses and leaves, on words made up for it.</summary>
public class ClearBibleSlipTests
{
    private static readonly ClearBibleToken Sardius = new("o260280130041", "אֹדֶם", false, "0124", Part: "noun");

    /// <summary>
    /// <em>el sardio</em>, with <em>sardio</em> named alone by four records of which two pair it with H124, and <em>de las
    /// palmas</em>, two words on; <em>el oro</em>'s <em>oro</em> is named by a record of its own.
    /// </summary>
    private static ClearBibleLinks.EarlyNames Early() => new(
        new Dictionary<string, string>(),
        new Dictionary<string, ClearBibleToken> { ["260280130041"] = Sardius },
        new HashSet<string> { "26028013030" },
        new Dictionary<string, string> { ["26028013019"] = "sardio", ["26028013030"] = "oro", ["07003013027"] = "palmas" },
        new HashSet<(string, int)>(),
        new HashSet<string>(),
        new HashSet<string>(),
        new Dictionary<string, string> { ["26028013018"] = "26028013019", ["26028013029"] = "26028013030", ["07003013025"] = "07003013027" },
        new Dictionary<(string, int), int> { [("sardio", 124)] = 2, [("palmas", 124)] = 1 },
        new Dictionary<string, int> { ["sardio"] = 4, ["palmas"] = 40 });

    [Fact]
    public void ARecordMovesOntoAnUnnamedWordTheSetRendersItsNumberWith()
    {
        var early = Early();
        ClearBibleLinks.Slipped(new ClearBibleRecord(["o260280130041"], ["26028013018"]), early)
            .Should().Be((ClearBibleLinks.Slip.Moved, "26028013019"));
    }

    [Fact]
    public void ARecordWhoseWordTheSetRarelyGivesItsNumberIsRefused()
    {
        ClearBibleLinks.Slipped(new ClearBibleRecord(["o260280130041"], ["07003013025"]), Early())
            .Should().Be((ClearBibleLinks.Slip.Refused, (string?)null), "one of forty is under the least share");
        ClearBibleLinks.Slipped(
                new ClearBibleRecord(["o260280130041"], ["26028013018"]), Early() with { Repeated = new HashSet<string> { "26028013019" } })
            .Should().Be((ClearBibleLinks.Slip.Refused, (string?)null), "the word stands twice in its verse");
    }

    [Fact]
    public void OtherRecordsAreLeftAlone()
    {
        var early = Early();
        ClearBibleLinks.Slipped(new ClearBibleRecord(["o260280130041"], ["26028013029"]), early).Slip
            .Should().Be(ClearBibleLinks.Slip.None, "a record names the word after it");
        ClearBibleLinks.Slipped(new ClearBibleRecord(["o260280130011"], ["26028013018"]), early).Slip
            .Should().Be(ClearBibleLinks.Slip.None, "a Hebrew preposition is the Spanish preposition's own");
        ClearBibleLinks.Slipped(new ClearBibleRecord(["o260280130041"], ["26028013018", "26028013019"]), early).Slip
            .Should().Be(ClearBibleLinks.Slip.None, "the record names more than the article");
        ClearBibleLinks.Slipped(new ClearBibleRecord(["o260280130041"], ["26028013019"]), early).Slip
            .Should().Be(ClearBibleLinks.Slip.None, "the record is on no preposition or article");
    }

    [Fact]
    public void TheWordAheadSkipsOnePrepositionOrArticleAndNoName()
    {
        var before = new HashSet<string> { "de", "las", "el", "en" };
        ClearBibleToken Word(string id, string text, bool excluded = false) => new(id, text, excluded, null);
        var aheads = ClearBibleLinks.Aheads(
            [
                Word("07003013024", "ciudad"), Word("07003013025", "de"), Word("07003013026", "las"), Word("07003013027", "palmas"),
                Word("07003013028", ",", excluded: true), Word("07003013029", "en"), Word("07003013030", "Jericó"),
                Word("07003014001", "el"),
            ],
            before);

        aheads.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["07003013025"] = "07003013027",
            ["07003013026"] = "07003013027",
        });
    }
}
