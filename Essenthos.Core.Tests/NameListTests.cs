using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The names of a verse paired by their letters and their order. Genesis 46:16 is the case that was
/// found: Gad's seven sons, and Brenton's Θασοβάν put against Arodi at 0.98 by two readings.
/// </summary>
public class NameListTests
{
    [Theory]
    [InlineData("Θασοβὰν", null, "אצבן", null)]
    [InlineData("Σαφὼν", null, "צפיון", null)]
    [InlineData("Ἀροηδεὶς", null, "ארודי", null)]
    [InlineData("Рувим", "rus", "ראובן", null)]
    [InlineData("Arphaxad", null, "Arpachshad", null)]
    [InlineData("Давидів", "ukr", "David", null)]
    public void ATransliteratedNameIsAlikeInEveryScript(
        string one,
        string? oneLanguage,
        string other,
        string? otherLanguage) =>
        NameLists.Alike(NameLists.Skeleton(one, oneLanguage), NameLists.Skeleton(other, otherLanguage))
            .Should().BeGreaterThanOrEqualTo(NameLists.LeastLikeness);

    /// <summary>The Septuagint read a dalet in עֵרִי; nothing in the letters says Ἀηδείς is Eri.</summary>
    [Theory]
    [InlineData("Ἀηδεὶς", "ערי")]
    [InlineData("Θασοβὰν", "ארודי")]
    [InlineData("Ἰεοὺλ", "רעואל")]
    public void DifferentNamesAreNot(string one, string other) =>
        NameLists.Alike(NameLists.Skeleton(one), NameLists.Skeleton(other))
            .Should().BeLessThan(NameLists.LeastLikeness);

    [Fact]
    public void TheWrongNameInAListIsRefusedAndTheRightOneProposed()
    {
        // Υἱοὶ δὲ Γάδ· Σαφὼν καὶ Ἀγγὶς καὶ Σαννὶς καὶ Θασοβὰν καὶ Ἀηδεὶς καὶ Ἀροηδεὶς καὶ Ἀρεηλείς
        string?[] greek = ["Υἱοὶ", "δὲ", "Γάδ", "Σαφὼν", "καὶ", "Ἀγγὶς", "καὶ", "Σαννὶς", "καὶ", "Θασοβὰν",
            "καὶ", "Ἀηδεὶς", "καὶ", "Ἀροηδεὶς", "καὶ", "Ἀρεηλείς"];
        bool[] greekNames = [false, false, true, true, false, true, false, true, false, true, false, true, false,
            true, false, true];
        // וּבְנֵי גָד צִפְיוֹן וְחַגִּי שׁוּנִי וְאֶצְבֹּן עֵרִי וַאֲרוֹדִי וְאַרְאֵלִי
        string[] hebrew = ["ו", "בני", "גד", "צפיון", "ו", "חגי", "שוני", "ו", "אצבן", "ערי", "ו", "ארודי", "ו", "אראלי"];
        bool[] hebrewNames = [false, false, true, true, false, true, true, false, true, true, false, true, false, true];

        var settled = NameLists.Settle(
            [
                (2, 2, 0.98, 0.5), (3, 3, 0.98, 0.5), (5, 5, 0.98, 0.5),
                (7, 9, 0.98, 0.5), // Σαννὶς against Eri
                (9, 11, 0.98, 0.5), // Θασοβὰν against Arodi
                (11, 9, 0.98, 0.5), (13, 11, 0.98, 0.5), (15, 13, 0.98, 0.5),
            ],
            Names(greek, greekNames),
            Names(hebrew, hebrewNames),
            [.. hebrew.Select(word => NameLists.Skeleton(word))]);

        settled.Should().NotContain(pair => pair.Source == 7 && pair.Target == 9);
        settled.Should().NotContain(pair => pair.Source == 9 && pair.Target == 11);
        settled.Should().Contain(pair => pair.Source == 7 && pair.Target == 6 && pair.Confidence == NameLists.Settled);
        settled.Should().Contain(pair => pair.Source == 9 && pair.Target == 8 && double.IsNaN(pair.Position));
        settled.Should().Contain(pair => pair.Source == 11 && pair.Target == 9, "Ἀηδεὶς is Eri, and nothing says otherwise");
        settled.Should().Contain(pair => pair.Source == 13 && pair.Target == 11);
    }

    /// <summary>
    /// BHSA marks as the name the אָדָם the Septuagint renders ἀνθρώπων, and as a noun the one it
    /// renders Ἀδάμ. The model got both right, and the marking must not move Adam onto the men.
    /// </summary>
    [Fact]
    public void AWordTheModelPutAgainstANameSpeltAlikeCountsAsOne()
    {
        string?[] greek = ["Αὕτη", "ἡ", "βίβλος", "γενέσεως", "ἀνθρώπων", "ᾗ", "ἡμέρᾳ", "ἐποίησεν", "ὁ", "θεὸς",
            "τὸν", "Ἀδάμ"];
        string[] hebrew = ["זה", "ספר", "תולדת", "אדם", "ב", "יום", "ברא", "אלהים", "אדם"];

        var settled = NameLists.Settle(
            [(4, 3, 0.9, 0.5), (11, 8, 0.9, 0.5)],
            Names(greek, [.. greek.Select((_, at) => at == 11)]),
            Names(hebrew, [.. hebrew.Select((_, at) => at == 3)]),
            [.. hebrew.Select(word => NameLists.Skeleton(word))]);

        settled.Select(pair => (pair.Source, pair.Target)).Should().BeEquivalentTo([(4, 3), (11, 8)]);
    }

    /// <summary>
    /// Hebrew says <em>the days of Adam</em> with the construct, and the King James's mapping puts its
    /// <em>of</em> on אָדָם. A word that is not a name is left to the model.
    /// </summary>
    [Fact]
    public void AWordThatIsNotANameIsLeftToTheModel()
    {
        string?[] english = ["And", "the", "days", "of", "Adam"];
        string[] hebrew = ["ו", "יהיו", "ימי", "אדם"];

        var settled = NameLists.Settle(
            [(3, 3, 0.7, 0.5), (4, 3, 0.98, 0.5)],
            Names(english, [false, false, false, false, true]),
            Names(hebrew, [false, false, false, true]),
            [.. hebrew.Select(word => NameLists.Skeleton(word))]);

        settled.Select(pair => (pair.Source, pair.Target)).Should().BeEquivalentTo([(3, 3), (4, 3)]);
    }

    /// <summary>The Synodal capitalises the pronouns of God; Моему is as much M as the μου it renders.</summary>
    [Fact]
    public void AWordOfOneConsonantIsNeverMadeAName()
    {
        string?[] russian = ["к", "Отцу", "Моему", "и", "Богу", "Моему"];
        string[] greek = ["πρὸς", "τὸν", "Πατέρα", "μου", "καὶ", "Θεόν", "μου"];

        var recognised = NameLists.Recognised(
            [.. russian.Select((word, at) => at is 2 or 5 ? NameLists.Skeleton(word, "rus") : null)],
            new string?[greek.Length],
            [.. greek.Select(word => NameLists.Skeleton(word))],
            [(2, 3), (5, 6)]);

        recognised.Should().OnlyContain(name => name == null);
    }

    /// <summary>One name is sometimes two words on one side, and only one half can be its match.</summary>
    [Fact]
    public void ANameMayReachTheOtherHalfOfItsMatch()
    {
        string?[] english = ["Salma", "the", "father", "of", "Bethlehem"];
        string[] hebrew = ["שלמא", "אבי", "בית", "לחם"];

        var settled = NameLists.Settle(
            [(0, 0, 0.9, 0.5), (4, 2, 0.9, 0.5), (4, 3, 0.9, 0.5)],
            Names(english, [true, false, false, false, true]),
            Names(hebrew, [true, false, true, true]),
            [.. hebrew.Select(word => NameLists.Skeleton(word))]);

        settled.Select(pair => (pair.Source, pair.Target)).Should().BeEquivalentTo([(0, 0), (4, 2), (4, 3)]);
    }

    [Fact]
    public void TheNamesKeepTheirOrderWhereTwoAreSpeltAlike()
    {
        var matched = NameLists.Match(
            [(1, "PRS"), (4, "PRS")],
            [(0, "PRS"), (6, "PRS")]);

        matched.Should().BeEquivalentTo(new Dictionary<int, int> { [1] = 0, [4] = 6 });
    }

    /// <summary>
    /// A model scores exactly 1 a pair it saw together once and never apart, and that is the one
    /// number that reads as certainty. What an inference is written with stays below it.
    /// </summary>
    [Theory]
    [InlineData(1.0, Routes.Ceiling)]
    [InlineData(0.99923, Routes.Ceiling)]
    [InlineData(0.5, 0.5)]
    public void AnInferenceIsNeverWrittenAsCertain(double scored, double written) =>
        Routes.Written(scored).Should().Be(written);

    private static string?[] Names(string?[] words, bool[] names) =>
        [.. words.Select((word, at) => names[at] ? NameLists.Skeleton(word) : null)];
}

/// <summary>The pass over links already written, which is how the corpus gets the correction without aligning again.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class NameListPassTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Text _greek;
    private readonly Text _hebrew;

    public NameListPassTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");

        _greek = Corpus.Add(_db, "GRCBRENT", TextKind.Translation, "grc",
            (46, 16, ["Υἱοὶ", "δὲ", "Γάδ", "Σαφὼν", "καὶ", "Σαννὶς", "καὶ", "Θασοβὰν", "καὶ", "Ἀροηδεὶς"]));
        _hebrew = Corpus.Add(_db, "BHSA", TextKind.ManuscriptTradition, "hbo",
            (46, 16, ["וּ", "בְנֵי", "גָד", "צִפְיֹון", "שׁוּנִי", "וְ", "אֶצְבֹּן", "וַ", "אֲרֹודִי"]));
        _db.SaveChanges();

        foreach (var word in _db.Words.Where(w => w.TextId == _greek.Id).ToList())
        {
            word.Lemma = word.Surface.ToLowerInvariant() is "υἱοὶ" or "δὲ" or "καὶ"
                ? word.Surface.ToLowerInvariant()
                : word.Surface;
        }

        foreach (var word in _db.Words.Where(w => w.TextId == _hebrew.Id).ToList())
        {
            var pos = word.Position is 3 or 4 or 5 or 7 or 9 ? "nmpr" : "subs";
            word.Morphology = JsonDocument.Parse(
                $$"""{"pos": "{{pos}}", "consonantal": "{{Consonants(word.Surface)}}"}""");
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    [Fact]
    public async Task TheWrongNameIsWithdrawnAndTheRightOneWritten()
    {
        Link(3, 3);
        Link(4, 4);
        var wrong = Link(8, 9); // Θασοβὰν against Arodi
        Link(10, 9);

        var pass = new NameListPass(
            _db, new AlignmentPipeline(_db, NullLogger<AlignmentPipeline>.Instance), NullLogger<NameListPass>.Instance);
        var report = await pass.Run("GRCBRENT", "BHSA", new HashSet<(int, int)> { (1, 46) }, null, apply: true);

        report.Should().Contain("1 contradict it");
        var links = await _db.Links.AsNoTracking()
            .Select(link => new
            {
                link.Id,
                link.Confidence,
                From = link.Words.Single(word => word.Side == LinkSide.From).Word!.Position,
                To = link.Words.Single(word => word.Side == LinkSide.To).Word!.Position,
            })
            .ToListAsync();

        links.Should().NotContain(link => link.Id == wrong.Id);
        links.Should().Contain(link => link.From == 8 && link.To == 7 && link.Confidence == NameLists.Settled);
        links.Should().Contain(link => link.From == 6 && link.To == 5);
        (await _db.LinkClaims.CountAsync()).Should().Be(links.Count);
    }

    private Link Link(int greek, int hebrew)
    {
        var link = new Link
        {
            FromTextId = _greek.Id,
            ToTextId = _hebrew.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner,
            Confidence = 0.98,
            Source = "SIL.Machine, aligned as written",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(_greek, 46, 16, greek), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(_hebrew, 46, 16, hebrew), Side = LinkSide.To });
        _db.LinkClaims.Add(new LinkClaim
        {
            Link = link, Method = LinkMethod.Aligner, Confidence = 0.98, Source = link.Source,
        });
        _db.SaveChanges();
        return link;
    }

    private static string Consonants(string pointed) =>
        new([.. pointed.Where(letter => letter is >= 'א' and <= 'ת')]);
}
