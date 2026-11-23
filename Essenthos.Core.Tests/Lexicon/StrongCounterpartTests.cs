using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Lexicons;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests.Lexicon;

/// <summary>
/// The Greek words that stand for a Hebrew word and back, counted from the Septuagint's settled links to the Hebrew: a word in
/// its narrowest link only, one content number on each side, and kept where the pairing is more than a one-off.
/// </summary>
public sealed class StrongCounterpartTests
{
    private static readonly HashSet<string> FunctionNumbers = new(["H9003", "H853", "G1722", "G3588", "G2532"], StringComparer.Ordinal);

    private static bool Content(string number) => !FunctionNumbers.Contains(number);

    private static StrongCounterparts.Link Link(string greek, string hebrew, bool stated = false) =>
        new(greek.Split(' '), hebrew.Split(' '), stated);

    [Fact]
    public void APhraseIsReadThroughTheOneToOneLinksInsideIt()
    {
        // Gen 1:1 as CATSS states it, ἐν ἀρχῇ over בְּ רֵאשִׁית, with the aligner's one-to-one links inside it.
        var greek = new Dictionary<string, string> { ["1"] = "G1722", ["2"] = "G746", ["3"] = "G4160", ["4"] = "G3588", ["5"] = "G2316" };
        var hebrew = new Dictionary<string, string> { ["1"] = "H9003", ["2"] = "H7225", ["3"] = "H1254", ["4"] = "H430" };
        var links = new List<StrongCounterparts.Link>
        {
            Link("1 2", "1 2", stated: true), Link("1", "1"), Link("2", "2"),
            Link("3", "3", stated: true),
            Link("4 5", "4", stated: true), Link("5", "4"),
        };

        var counted = StrongCounterparts.Count(links, greek, hebrew, Content, []);

        // Each pairing is counted once, and below the least count nothing is kept.
        counted.Should().BeEmpty();
        StrongCounterparts.Count([.. links, .. links.Select(l => Link(Twice(l.Greek), Twice(l.Hebrew), l.Stated))],
                With(greek), With(hebrew), Content, [])
            .Select(c => (c.Hebrew, c.Greek, c.Septuagint, c.Stated))
            .Should().Equal(("H1254", "G4160", 2, 2), ("H430", "G2316", 2, 0), ("H7225", "G746", 2, 0));
    }

    [Fact]
    public void APhraseOfTwoContentWordsOnASideCountsForNothing()
    {
        var greek = new Dictionary<string, string> { ["1"] = "G3056", ["2"] = "G2962", ["3"] = "G3056", ["4"] = "G2962" };
        var hebrew = new Dictionary<string, string> { ["1"] = "H1697", ["2"] = "H3068", ["3"] = "H1697", ["4"] = "H3068" };

        StrongCounterparts.Count([Link("1 2", "1 2"), Link("3 4", "3 4")], greek, hebrew, Content, []).Should().BeEmpty();
    }

    [Fact]
    public void AOneOffRenderingIsDroppedAndARareOneKeptWhereItIsTheOthersUsualWord()
    {
        // דָּבָר is λόγος 30 times, ῥῆμα 10 times and οὐδείς once; πρᾶγμα renders it twice, and renders nothing else.
        var links = new List<StrongCounterparts.Link>();
        var greek = new Dictionary<string, string>();
        var hebrew = new Dictionary<string, string>();
        var at = 0;
        void Pair(string h, string g, int times)
        {
            for (var i = 0; i < times; i++)
            {
                var key = (++at).ToString(System.Globalization.CultureInfo.InvariantCulture);
                greek[key] = g;
                hebrew[key] = h;
                links.Add(Link(key, key));
            }
        }

        Pair("H1697", "G3056", 30);
        Pair("H1697", "G4487", 10);
        Pair("H1697", "G3762", 1);
        Pair("H1697", "G4229", 2);
        Pair("H3808", "G3762", 20);

        var counted = StrongCounterparts.Count(links, greek, hebrew, Content, []);

        counted.Where(c => c.Hebrew == "H1697").Select(c => (c.Greek, c.Septuagint, c.HebrewLinked))
            .Should().Equal(("G3056", 30, 43), ("G4229", 2, 43), ("G4487", 10, 43));
        counted.Single(c => c.Greek == "G3762").Hebrew.Should().Be("H3808");
    }

    [Fact]
    public void StrongsDerivationIsKeptWhateverTheSeptuagintSays()
    {
        var counted = StrongCounterparts.Count([], new Dictionary<string, string>(), new Dictionary<string, string>(), Content,
        [
            new("G2", "H175", "loan-from", "of Hebrew origin (H175)"),
            new("G3", "G4", "from", "from G4"),
            new("G5", "H6", "compare", "compare H6"),
        ]);

        counted.Should().ContainSingle().Which.Should().Be(new StrongCounterpartRow("H175", "G2", 0, 0, 0, 0, 0, "loan-from", "of Hebrew origin (H175)"));
    }

    [Theory]
    [InlineData(1, 1, 1, 1, false)]
    [InlineData(2, 2, 20, 1000, true)]
    [InlineData(2, 2, 1000, 20, true)]
    [InlineData(2, 2, 1000, 1000, false)]
    [InlineData(20, 20, 6000, 3000, true)]
    public void APairingIsKeptFromTwoWordsAndATenthOfEitherSideOrTwentyWords(int septuagint, int greek, int hebrewLinked, int greekLinked, bool kept) =>
        StrongCounterparts.Kept(septuagint, greek, hebrewLinked, greekLinked).Should().Be(kept);

    private static string Twice(IEnumerable<string> keys) => string.Join(' ', keys.Select(k => k + "b"));

    private static Dictionary<string, string> With(Dictionary<string, string> numbers) =>
        numbers.Concat(numbers.Select(n => KeyValuePair.Create(n.Key + "b", n.Value))).ToDictionary(n => n.Key, n => n.Value);
}

/// <summary>A Strong page lists the other language's entries from <c>strong_counterpart</c>, counted from its own side.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class StrongCounterpartEndpointTests : IDisposable
{
    private readonly AppDbContext _db;

    public StrongCounterpartEndpointTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.StrongCounterparts.ExecuteDelete();
        _db.StrongCounterparts.AddRange(
            Row("H1697", "G3056", 811, 811),
            Row("H1697", "G4487", 325, 325),
            Row("H4405", "G3056", 8, 8),
            Row("H175", "G2", 70, 75, "loan-from", "of Hebrew origin (H175)"));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.StrongCounterparts.ExecuteDelete();
        _db.Dispose();
    }

    [Fact]
    public async Task AHebrewEntryListsTheGreekCommonestFirstAndAGreekOneTheHebrew()
    {
        var hebrew = await StrongEndpoints.Counterparts(_db, "H1697", null, default);
        hebrew.Select(c => (c.Number, c.Septuagint)).Should().Equal(("G3056", 811), ("G4487", 325));

        var greek = await StrongEndpoints.Counterparts(_db, "G3056", null, default);
        greek.Select(c => (c.Number, c.Septuagint)).Should().Equal(("H1697", 811), ("H4405", 8));

        var borrowed = await StrongEndpoints.Counterparts(_db, "G2", null, default);
        borrowed.Should().ContainSingle().Which.Should().Match<StrongCounterpartResponse>(c =>
            c.Number == "H175" && c.Septuagint == 75 && c.Dictionary == "loan-from" && c.Statement == "of Hebrew origin (H175)");

        (await StrongEndpoints.Counterparts(_db, "H1", null, default)).Should().BeEmpty();
    }

    private static StrongCounterpart Row(string hebrew, string greek, int septuagint, int greekWords, string? dictionary = null, string? statement = null) =>
        new()
        {
            Hebrew = hebrew,
            Greek = greek,
            Septuagint = septuagint,
            GreekWords = greekWords,
            HebrewLinked = septuagint * 2,
            GreekLinked = greekWords * 2,
            Stated = septuagint / 2,
            Dictionary = dictionary,
            Statement = statement,
        };
}
