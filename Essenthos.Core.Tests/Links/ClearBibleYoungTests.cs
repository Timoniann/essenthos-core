using Essenthos.Core.ClearBible;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The shape of Clear Bible's Young's Literal Translation set, whose target file marks punctuation in an
/// <c>isPunc</c> column rather than <c>exclude</c>.
/// </summary>
public sealed class ClearBibleYoungTokenTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"essenthos-ylt-{Guid.NewGuid():N}");

    public ClearBibleYoungTokenTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void ATokenTheFileMarksAsPunctuationIsLeftOut()
    {
        var path = Path.Combine(_folder, "target.tsv");
        File.WriteAllLines(path,
        [
            "id	altId	text	transType	isPunc	isPrimary",
            "01001001001	In-1	In	k	False	True",
            "01001001002	,-1	,		True	False",
        ]);

        ClearBibleAlignment.Tokens(path).Select(token => token.Excluded).Should().Equal(false, true);
    }
}

/// <summary>
/// The Old Testament set's records are numbered by the morpheme edition, though the set calls its source
/// <c>WLC</c>: read by the release's <c>WLC.tsv</c>, which writes גֵ֣יא~בֶנ־הִנֹּ֔ם as one word, every
/// identifier after such a word named a later one.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed class ClearBibleYoungNumberingTests
{
    private static readonly string Folder = TestResources.ClearBibleFolder;

    private static readonly ClearBibleSet Set = ClearBibleSet.YoungOldTestament("YLT", "BHSA");

    [Fact]
    public void EverySourceWordTheRecordsNameIsARowOfTheSetsTokenFile()
    {
        var rows = ClearBibleAlignment.Tokens(Path.Combine(Folder, Set.Source)).Select(token => token.Id).ToHashSet();

        ClearBibleAlignment.Records(Path.Combine(Folder, Set.Alignment))
            .SelectMany(record => record.Source)
            .Where(id => !rows.Contains(id))
            .Should().BeEmpty();
    }

    /// <summary>
    /// Jeremiah 7:31: <em>the valley of the son of Hinnom</em> names three source words and <em>To burn</em>
    /// the two after them, which are the ל and the verb, not בְּנֵי and its suffix.
    /// </summary>
    [Fact]
    public void JeremiahSevenThirtyOnesToBurnIsTheInfinitive()
    {
        var tokens = ClearBibleAlignment.Tokens(Path.Combine(Folder, Set.Source)).ToDictionary(token => token.Id);
        var toBurn = ClearBibleAlignment.Records(Path.Combine(Folder, Set.Alignment))
            .Where(record => record.Target.Count == 1 && record.Target[0] is "24007031023" or "24007031024")
            .SelectMany(record => record.Source)
            .Select(id => ClearBibleLinks.Comparable(tokens[id].Text, "hbo"));

        toBurn.Should().Equal("ל", "שרף");
    }
}

/// <summary>
/// A set whose records are numbered over the source edition as the team edited it: a word added in a verse moves
/// every number after it, and the edited numbering is read back from the records' own words.
/// </summary>
public sealed class ClearBibleEditedSourceTests
{
    private static readonly string[] Words = ["apple", "bread", "cheese", "dates", "eggs", "figs"];

    /// <summary>Every verse but the first reads the six words as the edition has them, so the set itself says what renders each.</summary>
    private const int Verses = 60;

    private static IEnumerable<ClearBibleToken> Source() =>
        from verse in Enumerable.Range(1, Verses)
        from position in Enumerable.Range(1, Words.Length)
        select new ClearBibleToken($"n40001{verse:D3}{position:D3}", Words[position - 1], false, $"G{position}", Gloss: Words[position - 1]);

    private static (List<ClearBibleRecord> Records, List<ClearBibleToken> Target) Set(IReadOnlyList<string> first)
    {
        var records = new List<ClearBibleRecord>();
        var target = new List<ClearBibleToken>();
        for (var verse = 1; verse <= Verses; verse++)
        {
            var words = verse == 1 ? first : Words;
            for (var position = 1; position <= words.Count; position++)
            {
                target.Add(new ClearBibleToken($"40001{verse:D3}{position:D3}", words[position - 1], false, null));
                records.Add(new ClearBibleRecord([$"n40001{verse:D3}{position:D3}"], [$"40001{verse:D3}{position:D3}"]));
            }
        }

        return (records, target);
    }

    [Fact]
    public void AWordTheEditedTextAddsIsPlacedOnNothingAndEveryNumberAfterItOnTheWordBefore()
    {
        // The edited verse reads apple bread [extra] cheese dates eggs figs: seven numbers over six words.
        var (records, target) = Set(["apple", "bread", "extra", "cheese", "dates", "eggs", "figs"]);

        var read = ClearBibleSourceEdits.Of(records, Source(), target, [], "grc");

        read.Placed.Should().BeEquivalentTo(new Dictionary<string, string?>
        {
            ["40001001003"] = null,
            ["40001001004"] = "40001001003",
            ["40001001005"] = "40001001004",
            ["40001001006"] = "40001001005",
            ["40001001007"] = "40001001006",
        });
        read.Verses.Should().Be(1);
    }

    [Fact]
    public void AVerseNumberedOverTheEditionAsItStandsIsLeftAlone()
    {
        var (records, target) = Set(Words);

        var read = ClearBibleSourceEdits.Of(records, Source(), target, [], "grc");

        read.Placed.Should().BeEmpty();
        read.Verses.Should().Be(0);
    }

    [Fact]
    public void TwoRecordsCrossedWithEachOtherArePlacedOnNothing()
    {
        var (records, target) = Set(["apple", "bread", "dates", "cheese", "eggs", "figs"]);

        var read = ClearBibleSourceEdits.Of(records, Source(), target, [], "grc");

        read.Placed.Should().BeEquivalentTo(new Dictionary<string, string?> { ["40001001003"] = null, ["40001001004"] = null });
        read.Crossed.Should().Be(2);
    }

    [Fact]
    public void ARecordIsMovedOnlyWithTheRestOfItsSourceWords()
    {
        var placed = new Dictionary<string, string?> { ["40001001004"] = "40001001003", ["40001001005"] = null };

        ClearBibleLinks.Edited(["n40001001001", "n40001001004"], placed).Should().Equal("n40001001001", "40001001003");
        ClearBibleLinks.Edited(["n40001001004", "n40001001005"], placed).Should().BeNull();
        IReadOnlyList<string> untouched = ["n40001001001"];
        ClearBibleLinks.Edited(untouched, placed).Should().BeSameAs(untouched);
    }
}

/// <summary>
/// The New Testament set's records are numbered over the SBLGNT as Clear's team edited it toward the Nestle-Aland:
/// Matthew 3:16 adds αὐτῷ after ἠνεῴχθησαν, so read by <c>SBLGNT.tsv</c> <em>he saw</em> stood on πνεῦμα.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed class ClearBibleYoungNewTestamentNumberingTests
{
    private static readonly string Folder = TestResources.ClearBibleFolder;

    private static readonly ClearBibleSet Set = ClearBibleSet.YoungNewTestament("YLT", "NESTLE1904");

    private static readonly Lazy<(ClearBibleSourceEdits.Read Read, List<ClearBibleRecord> Records, HashSet<string> Rows)> Edits = new(() =>
    {
        var records = ClearBibleAlignment.Records(Path.Combine(Folder, Set.Alignment)).ToList();
        var source = ClearBibleAlignment.Tokens(Path.Combine(Folder, Set.Source)).ToList();
        var target = ClearBibleAlignment.Tokens(Path.Combine(Folder, Set.Target)).ToList();
        return (ClearBibleSourceEdits.Of(records, source, target, [], "grc"), records,
            source.Select(token => ClearBibleAlignment.Unit(token.Id)).ToHashSet());
    });

    [Fact]
    public void EveryIdentifierPastItsVersesWordsIsPlacedOnAWordOrOnNothing()
    {
        Set.Edited.Should().BeTrue();
        var (read, records, rows) = Edits.Value;
        var past = records.SelectMany(record => record.Source).Select(ClearBibleAlignment.Unit)
            .Where(unit => !rows.Contains(unit) && rows.Contains($"{ClearBibleAlignment.Verse(unit):D8}001")).ToHashSet();

        past.Should().HaveCountGreaterThan(300);
        past.Should().OnlyContain(unit => read.Placed.ContainsKey(unit) && (read.Placed[unit] == null || rows.Contains(read.Placed[unit]!)));
    }

    /// <summary>Matthew 3:16: <em>to him</em> names the added αὐτῷ, and <em>he saw</em> (the seventeenth number) εἶδεν, the sixteenth word.</summary>
    [Fact]
    public void MatthewThreeSixteensHeSawIsEidenAndToHimIsPlacedOnNothing()
    {
        var placed = Edits.Value.Read.Placed;

        placed["40003016013"].Should().BeNull();
        placed["40003016015"].Should().Be("40003016014");
        placed["40003016017"].Should().Be("40003016016");
        placed.Should().NotContainKey("40003016011");
    }
}
