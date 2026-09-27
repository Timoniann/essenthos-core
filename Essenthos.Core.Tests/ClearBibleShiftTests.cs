using Essenthos.Core.ClearBible;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

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
        var ands = ClearBibleLinkLoader.Ands(Path.Combine(Folder, target), shift.And);
        var joining = ClearBibleLinkLoader.Joining(Path.Combine(Folder, source), shift.Also);
        var named = ClearBibleLinkLoader.Named(records);
        return [.. records.Where(record => ClearBibleLinkLoader.Shifted(record, ands, joining, named))];
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
            ClearBibleSet.ReinaValeraOldTestament("RV1909", "BHSA").Shift!);
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
            ClearBibleSet.ReinaValeraNewTestament("RV1909", "NESTLE1904").Shift!);
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

        ClearBibleLinkLoader.Shifted(record, ands, new HashSet<string> { "240460160031" }, new HashSet<string>())
            .Should().BeFalse();
        ClearBibleLinkLoader.Shifted(record, ands, new HashSet<string>(), new HashSet<string>())
            .Should().BeTrue();
    }

    [Fact]
    public void ARecordOnTheAndIsKeptWhereTheWordAfterItIsNamed()
    {
        var record = new ClearBibleRecord(["o010010040012"], ["01001004001"]);
        var ands = new Dictionary<string, string?> { ["01001004001"] = "01001004002" };

        ClearBibleLinkLoader.Shifted(record, ands, new HashSet<string>(), new HashSet<string> { "01001004002" })
            .Should().BeFalse();
    }
}

/// <summary>
/// The loader over a database: a record naming a Hebrew prefix is stored against the prefix, which
/// BHSA writes as a word of its own, and a record on the <em>y</em> before its word is refused.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ClearBibleMorphemeLoadTests : IDisposable
{
    private const string Statement = "Clear Bible morpheme test set";

    private readonly AppDbContext _db;
    private readonly ClearBibleLinkLoader _loader;
    private readonly string _folder;
    private readonly ClearBibleSet _set;

    public ClearBibleMorphemeLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _loader = new ClearBibleLinkLoader(_db, NullLogger<ClearBibleLinkLoader>.Instance);

        // Genesis 2:17's "of the tree", Genesis 1:4's "and he saw God" and Job 1:13's "his sons and his
        // daughters", transliterated: BHSA writes the preposition and the conjunction as words of
        // their own and the suffix on its word, the Westminster morphology all three as morphemes.
        Corpus.Add(_db, "SPANISH", TextKind.Translation, "es",
            (2, 17, ["del", "árbol"]), (1, 4, ["Y", "vió", "Dios"]), (1, 13, ["sus", "hijos", "y", "sus", "hijas"]));
        Corpus.Add(_db, "HEBREW", TextKind.CriticalEdition, "hbo",
            (2, 17, ["me", "ets"]), (1, 4, ["va", "yar", "elohim"]), (1, 13, ["u", "banaw", "u", "benotaw"]));
        _db.SaveChanges();

        _folder = Path.Combine(Path.GetTempPath(), $"essenthos-clearbible-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
        File.WriteAllLines(Path.Combine(_folder, "target.tsv"),
        [
            "id\tsource_verse\ttext\tskip_space_after\texclude\tid_range_end\tsource_verse_range_end",
            "01001004001\t01001004\tY\t\t\t\t",
            "01001004002\t01001004\tvió\t\t\t\t",
            "01001004003\t01001004\tDios\t\t\t\t",
            "01001013001\t01001013\tsus\t\t\t\t",
            "01001013002\t01001013\thijos\t\t\t\t",
            "01001013003\t01001013\ty\t\t\t\t",
            "01001013004\t01001013\tsus\t\t\t\t",
            "01001013005\t01001013\thijas\t\t\t\t",
            "01002017001\t01002017\tdel\t\t\t\t",
            "01002017002\t01002017\tárbol\t\t\t\t",
        ]);
        File.WriteAllLines(Path.Combine(_folder, "source.tsv"),
        [
            "id\taltId\ttext\tstrongs\tgloss\tgloss2\tpos\tmorph",
            "o010010040011\t\tva\t\tand\t\tconjunction\t",
            "o010010040012\t\tyar\t7200\tsaw\t\tverb\t",
            "o010010040021\t\telohim\t0430\tGod\t\tnoun\t",
            "o010010130021\t\tu\t\tand\t\tconjunction\t",
            "o010010130022\t\tbenota\t1323\tdaughters\t\tnoun\t",
            "o010010130023\t\tw\t\this\t\tsuffix\t",
            "o010010130011\t\tu\t\tand\t\tconjunction\t",
            "o010010130012\t\tbana\t1121\tsons\t\tnoun\t",
            "o010010130013\t\tw\t\this\t\tsuffix\t",
            "o010020170011\t\tme\t4480\tfrom\t\tpreposition\t",
            "o010020170012\t\tets\t6086\ttree\t\tnoun\t",
        ]);
        File.WriteAllText(Path.Combine(_folder, "alignment.json"),
            """
            {"records": [
              {"source": ["o010010040012"], "target": ["01001004001"]},
              {"source": ["o010010040021"], "target": ["01001004003"]},
              {"source": ["o010010130012"], "target": ["01001013002"]},
              {"source": ["o010010130022"], "target": ["01001013005"]},
              {"source": ["o010020170011"], "target": ["01002017001"]},
              {"source": ["o010020170012"], "target": ["01002017002"]}
            ]}
            """);
        _set = new ClearBibleSet(
            "SPANISH", "HEBREW", "alignment.json", "target.tsv", "source.tsv", ClearBibleJoin.Letters, Statement,
            new ClearBibleShift(new HashSet<string> { "y", "e" }, new HashSet<int>()));
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private async Task<List<(string Spanish, string Hebrew)>> Pairs() =>
        (await _db.Links.Where(link => link.Source == Statement)
            .Select(link => new
            {
                Spanish = link.Words.Where(word => word.Side == LinkSide.From).Select(word => word.Word!.Surface).Single(),
                Hebrew = link.Words.Where(word => word.Side == LinkSide.To).Select(word => word.Word!.Surface).Single(),
            })
            .ToListAsync())
        .Select(pair => (pair.Spanish, pair.Hebrew))
        .ToList();

    [Fact]
    public async Task ARecordNamingThePrefixIsStoredAgainstThePrefix()
    {
        await _loader.Load(_folder, _set);

        (await Pairs()).Should().Contain([("del", "me"), ("árbol", "ets")]);
    }

    /// <summary>
    /// The file lists the daughters before the sons, and each suffix as a morpheme of its own; read
    /// in that order and apart, the suffix of one word is matched to the conjunction of the next.
    /// </summary>
    [Fact]
    public async Task AWordIsPlacedInTheOrderItsIdentifiersNumberItWithItsSuffix()
    {
        await _loader.Load(_folder, _set);

        (await Pairs()).Should().Contain([("hijos", "banaw"), ("hijas", "benotaw")]);
    }

    [Fact]
    public async Task TheRecordOnTheAndBeforeItsWordIsRefusedAndTheOthersWritten()
    {
        var outcome = await _loader.Load(_folder, _set);

        (outcome.Records, outcome.Shifted, outcome.Added, outcome.Unresolved).Should().Be((6, 1, 5, 0));
        (await Pairs()).Should().NotContain(pair => pair.Spanish == "Y");
    }
}
