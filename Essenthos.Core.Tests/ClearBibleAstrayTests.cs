using Essenthos.Core.ClearBible;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// Which of Clear Bible's records name words of a verse the translation's verse does not render. Its
/// Reina-Valera set pairs the Spanish verse with the Hebrew verse of the same number in the chapters
/// the two divide differently — Spanish Numbers 13:19, which is the Hebrew 13:18, has its words
/// linked to the Hebrew 13:19 — while the token file beside it says 13:18, and so does the frame.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class ClearBibleAstrayTests(Ebible ebible, ITestOutputHelper output) : IClassFixture<Ebible>
{
    private static readonly Dictionary<int, HashSet<int>> Identity = [];

    private static ClearBibleRecord Record(string source, string target) => new([source], [target]);

    private static Dictionary<int, HashSet<int>> Placed(params (int Printed, int Canonical)[] verses) =>
        verses.ToDictionary(verse => verse.Printed, verse => new HashSet<int> { verse.Canonical });

    [Fact]
    public void ARecordTheFileAndTheFrameBothSayCrossesIsAstray()
    {
        var renders = new Dictionary<string, (int, int)> { ["04013019005"] = (4_013_018, 4_013_018) };

        ClearBibleLinkLoader.Astray(
                Record("o040130190012", "04013019005"),
                renders,
                Placed((4_013_019, 4_013_018)),
                Placed((4_013_018, 4_013_018), (4_013_019, 4_013_019)))
            .Should().BeTrue();
    }

    /// <summary>
    /// Where the Spanish runs a verse behind the English, as in 1 Samuel 24, its 24:3 is the Hebrew
    /// 24:3 and its words are linked to the Hebrew 24:4, which the frame places at the English 24:3.
    /// </summary>
    [Fact]
    public void ARecordPairedWithTheVerseTheFramePlacesAtItsNumberIsAstray()
    {
        var renders = new Dictionary<string, (int, int)> { ["09024003003"] = (9_024_003, 9_024_003) };

        ClearBibleLinkLoader.Astray(
                Record("o090240040021", "09024003003"),
                renders,
                Placed((9_024_003, 9_024_002)),
                Placed((9_024_003, 9_024_002), (9_024_004, 9_024_003)))
            .Should().BeTrue();
    }

    /// <summary>
    /// The Spanish 1 Kings 16:30 opens with the last words of the Hebrew 16:29, <em>and Ahab reigned
    /// over Israel in Samaria twenty and two years</em>, and its links there are right, though the
    /// file lists the whole verse against 16:30 and the frame places it there.
    /// </summary>
    [Fact]
    public void ARecordIntoTheVerseBeforeItsOwnNumberIsKept()
    {
        var renders = new Dictionary<string, (int, int)> { ["11016030006"] = (11_016_030, 11_016_030) };

        ClearBibleLinkLoader.Astray(
                Record("o110160290051", "11016030006"),
                renders,
                Placed((11_016_030, 11_016_030)),
                Placed((11_016_029, 11_016_029), (11_016_030, 11_016_030)))
            .Should().BeFalse();
    }

    /// <summary>
    /// The Berean's token list says its Acts 19:41 renders the Greek 19:40, which is how some
    /// editions number it; the Berean Greek numbers it 19:41, and the frame joins the two.
    /// </summary>
    [Fact]
    public void ARecordTheFrameJoinsIsKeptWhateverTheFileSays()
    {
        var renders = new Dictionary<string, (int, int)> { ["44019041001"] = (44_019_040, 44_019_040) };

        ClearBibleLinkLoader.Astray(
                Record("n440190410011", "440190410011"), renders, Placed((44_019_041, 44_019_041)), null)
            .Should().BeFalse();
    }

    /// <summary>
    /// Nestle prints Philippians 1:16 and 1:17 in the other order, and the Spanish, as the file says,
    /// renders <em>οἱ μὲν ἐξ</em> of the verse printed with its own number.
    /// </summary>
    [Fact]
    public void ARecordTheFileStatesIsKeptWhateverTheFrameSays()
    {
        var renders = new Dictionary<string, (int, int)> { ["50001016001"] = (50_001_016, 50_001_016) };

        ClearBibleLinkLoader.Astray(
                Record("n50001016001", "50001016001"),
                renders,
                Placed((50_001_016, 50_001_016)),
                Placed((50_001_016, 50_001_017)))
            .Should().BeFalse();
    }

    [Fact]
    public void ARecordTheFileSaysNothingAboutIsKept() =>
        ClearBibleLinkLoader.Astray(
                Record("o040130190012", "04013019005"),
                new Dictionary<string, (int, int)>(),
                Placed((4_013_019, 4_013_018)),
                Identity)
            .Should().BeFalse();

    /// <summary>
    /// The whole Old Testament set, against the Spanish placed as the frame places it and the Hebrew
    /// in the Hebrew numbering. What is refused lies in the chapters the Spanish and the Hebrew number
    /// differently, and it is a sliver of the set.
    /// </summary>
    [Fact]
    public void TheReinaValeraRecordsRefusedAreInTheChaptersItDividesDifferently()
    {
        var folder = Path.Combine(TestResources.ClearBibleFolder, "data");
        var renders = new Dictionary<string, (int First, int Last)>(StringComparer.Ordinal);
        foreach (var token in ClearBibleAlignment.Tokens(Path.Combine(folder, "spa", "targets", "RV09", "ot_RV09.tsv")))
        {
            if (token.Renders is { } range)
            {
                renders[ClearBibleAlignment.Word(token.Id)] = range;
            }
        }

        var spanish = EnglishEditions.Frame(ebible.ReinaValera);
        var targetFrame = ebible.ReinaValera.Books
            .SelectMany(book => book.Chapters.SelectMany(chapter => chapter.Verses.Select(verse =>
                (Book: book.CanonicalOrdinal, Chapter: chapter.Number, Verse: verse.Number))))
            .Distinct()
            .ToDictionary(
                verse => ClearBibleAlignment.Verse(verse.Book, verse.Chapter, verse.Verse),
                verse => Canonical(spanish.Resolve(verse.Book, verse.Chapter, verse.Verse)));

        var hebrew = TvtmsReader.Read(TestResources.Tvtms).Frame(Versification.Original);
        var sourceFrame = new Dictionary<int, HashSet<int>>();
        var records = ClearBibleAlignment.Records(Path.Combine(folder, "spa", "alignments", "RV09", "WLCM-RV09-manual.json"))
            .ToList();
        foreach (var id in records.SelectMany(record => record.Source))
        {
            if (ClearBibleAlignment.Address(id, out var book, out var chapter, out var verse))
            {
                sourceFrame.TryAdd(ClearBibleAlignment.Verse(book, chapter, verse), Canonical(hebrew.Resolve(book, chapter, verse)));
            }
        }

        var astray = records.Where(record => ClearBibleLinkLoader.Astray(record, renders, targetFrame, sourceFrame))
            .ToList();
        var chapters = astray
            .Select(record => ClearBibleAlignment.Verse(record.Target[0])!.Value / 1000)
            .GroupBy(chapter => chapter)
            .ToDictionary(chapter => chapter.Key, chapter => chapter.Count());
        output.WriteLine($"{astray.Count} of {records.Count} astray: " +
                         string.Join(' ', chapters.OrderBy(c => c.Key).Select(c => $"{c.Key}:{c.Value}")));

        astray.Should().Contain(record => record.Target[0] == "04013019005");
        astray.Should().Contain(record => record.Target[0] == "09024003003");
        astray.Should().NotContain(record => record.Target[0] == "11016030006");
        astray.Count.Should().BeLessThan(records.Count / 200);
    }

    private static HashSet<int> Canonical(IEnumerable<CanonicalReference> references) =>
        [.. references.Select(reference => ClearBibleAlignment.Verse(reference.Book, reference.Chapter, reference.Verse))];
}

/// <summary>
/// The loader over a database: a record pairing verses by number is refused and the one beside it
/// written, and withdrawing a set takes the verse links its word links stated with it.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ClearBibleAstrayLoadTests : IDisposable
{
    private const string Statement = "Clear Bible test set";

    private readonly AppDbContext _db;
    private readonly ClearBibleLinkLoader _loader;
    private readonly string _folder;
    private readonly ClearBibleSet _set;

    public ClearBibleAstrayLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _loader = new ClearBibleLinkLoader(_db, NullLogger<ClearBibleLinkLoader>.Instance);

        // The Spanish 13:19 renders the Hebrew 13:18, and the frame places it there.
        Corpus.Add(_db, "SPANISH", TextKind.Translation, "es",
            (13, 18, ["mirad", "vosotros"]), (13, 19, ["ved", "tierra"]));
        Corpus.Add(_db, "HEBREW", TextKind.CriticalEdition, "hbo",
            (13, 17, ["mirad", "vosotros"]), (13, 18, ["ved", "tierra"]), (13, 19, ["mah", "haarets"]));
        _db.SaveChanges();
        _db.Database.ExecuteSqlRaw(
            """
            UPDATE verse_reference r SET canonical_verse = r.canonical_verse - 1
            FROM verse v JOIN text t ON t.id = v.text_id
            WHERE r.verse_id = v.id AND t.slug = 'SPANISH'
            """);

        _folder = Path.Combine(Path.GetTempPath(), $"essenthos-clearbible-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
        File.WriteAllLines(Path.Combine(_folder, "target.tsv"),
        [
            "id\tsource_verse\ttext\tskip_space_after\texclude\tid_range_end\tsource_verse_range_end",
            "01013018001\t01013017\tmirad\t\t\t\t",
            "01013018002\t01013017\tvosotros\t\t\t\t",
            "01013019001\t01013018\tved\t\t\t\t",
            "01013019002\t01013018\ttierra\t\t\t\t",
        ]);
        File.WriteAllLines(Path.Combine(_folder, "source.tsv"),
        [
            "id\taltId\ttext\tstrongs\tgloss",
            "o010130170011\t\tmirad\t\t", "o010130170021\t\tvosotros\t\t",
            "o010130180011\t\tved\t\t", "o010130180021\t\ttierra\t\t",
            "o010130190011\t\tmah\t\t", "o010130190021\t\thaarets\t\t",
        ]);
        File.WriteAllText(Path.Combine(_folder, "alignment.json"),
            """
            {"records": [
              {"source": ["o010130180021"], "target": ["01013019002"]},
              {"source": ["o010130190021"], "target": ["01013019001"]}
            ]}
            """);
        _set = new ClearBibleSet(
            "SPANISH", "HEBREW", "alignment.json", "target.tsv", "source.tsv", ClearBibleJoin.Letters, Statement);
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task TheRecordPairingVersesByNumberIsRefusedAndTheOtherWritten()
    {
        var outcome = await _loader.Load(_folder, _set);

        (outcome.Records, outcome.Astray, outcome.Added).Should().Be((2, 1, 1));
        var words = await _db.LinkWords.Where(word => word.Link!.Source == Statement)
            .Select(word => word.Word!.Surface).ToListAsync();
        words.Should().BeEquivalentTo(["tierra", "tierra"]);
    }

    [Fact]
    public async Task WithdrawingASetTakesTheVerseLinksItStated()
    {
        await _loader.Load(_folder, _set);
        var spanish = _db.Texts.Single(text => text.Slug == "SPANISH");
        var hebrew = _db.Texts.Single(text => text.Slug == "HEBREW");
        _db.VerseLinks.Add(new VerseLink
        {
            FromTextId = spanish.Id,
            ToTextId = hebrew.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource,
            Source = Statement,
            Verses =
            [
                new VerseLinkVerse { VerseId = _db.VerseAt(spanish, 13, 19).Id, Side = LinkSide.From },
                new VerseLinkVerse { VerseId = _db.VerseAt(hebrew, 13, 19).Id, Side = LinkSide.To },
            ],
        });
        await _db.SaveChangesAsync();

        await _loader.Withdraw(_set);

        (await _db.VerseLinks.CountAsync(link => link.Source == Statement)).Should().Be(0);
        (await _db.Links.CountAsync(link => link.Source == Statement)).Should().Be(0);
    }
}
