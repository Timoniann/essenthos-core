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
/// The names pass over a Ge'ez text, which marks no names: a name the aligner left unlinked is
/// paired with the name of the Greek it spells, consonant for consonant. The verses and the links
/// are the corpus's own, as they stood when Genesis 10:2's Madai was found unnamed beside its Greek.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class GeezNameListTests : IDisposable
{
    private static readonly string[] GeezTenTwo =
        ["ወደቂቀ", "ያፌት", "ጋሜር", "ወያጎግ", "ወማዴ", "ወይህያን", "ወኤልሳ", "ወቶቤል", "ወሞሳኅ", "ወቲራ"];

    private static readonly string[] GreekTenTwo =
    [
        "Υἱοὶ", "Ἰάφεθ", "Γαμὲρ", "καὶ", "Μαγὼγ", "καὶ", "Μαδοὶ", "καὶ", "Ἰωύαν", "καὶ", "Ἐλισὰ", "καὶ", "Θοβὲλ", "καὶ",
        "Μοσὸχ", "καὶ", "Θείρας",
    ];

    private static readonly string[] GeezFourteenFive =
    [
        "ወመጽኡ", "ከደሎጎሞር", "ወነገሦት", "እለ", "ምስሌሁ", "ወቀተልዎሙ", "ለእለ", "ያርብሕኒ", "በአስጣሮስ", "ወለቀራንዮን", "ወለሕዝብኒ",
        "ጽኑዓን", "ምስሌሆሙ", "ወለአምዮስ", "ወለሀገረ", "ሴዊ",
    ];

    private static readonly string[] GreekFourteenFive =
    [
        "Ἐν", "δὲ", "τῷ", "τεσσαρεσκαιδεκάτῳ", "ἔτει", "ἦλθε", "Χοδολλογομὸρ", "καὶ", "οἱ", "βασιλεῖς", "μετʼ", "αὐτοῦ",
        "καὶ", "κατέκοψαν", "τοὺς", "γίγαντας", "τοὺς", "ἐν", "Ἀσταρὼθ", "καὶ", "Καρναῒν", "καὶ", "ἔθνη", "ἰσχυρὰ", "ἅμα",
        "αὐτοῖς", "καὶ", "τοὺς", "Ὀμμαίους", "τοὺς", "ἐν", "Σαυῇ", "τῇ", "πόλει",
    ];

    private readonly AppDbContext _db;
    private readonly Text _geez;
    private readonly Text _greek;

    public GeezNameListTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");

        _geez = Corpus.Add(_db, "GEEZ81", TextKind.Translation, "gez",
            (10, 2, GeezTenTwo), (14, 5, GeezFourteenFive), (1, 1, ["እምነ", "ሰማይ"]), (1, 2, ["እምነ", "ምድር"]),
            (1, 3, ["እምነ", "ብርሃን"]), (1, 4, ["እምነ", "ጽልመት"]), (1, 5, ["እምነ", "ማይ"]), (1, 6, ["ወይቤ", "እምነ"]));
        _greek = Corpus.Add(_db, "GRCBRENT", TextKind.Translation, "grc",
            (10, 2, GreekTenTwo), (14, 5, GreekFourteenFive), (1, 1, ["ἀπὸ", "οὐρανοῦ"]), (1, 2, ["ἀπὸ", "γῆς"]),
            (1, 3, ["ἀπὸ", "φωτός"]), (1, 4, ["ἀπὸ", "σκότους"]), (1, 5, ["ἀπὸ", "ὕδατος"]), (1, 6, ["καὶ", "εἶπεν", "Ἀμάν"]));
        _db.SaveChanges();

        foreach (var word in _db.Words.Where(w => w.TextId == _greek.Id).ToList())
        {
            word.Lemma = char.IsUpper(word.Surface[0]) && !(word.Surface is "Υἱοὶ" or "Ἐν")
                ? word.Surface
                : word.Surface.ToLowerInvariant();
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    /// <summary>
    /// ወማዴ is MD and so is Μαδοί, and nothing of the pair is linked to either: they are paired. ወያጎግ
    /// stays unlinked, because its letters spell only a G and Μαγώγ is MG: the verse puts it where
    /// Magog stands, and no letter of it says so. Every link already there stays.
    /// </summary>
    [Fact]
    public async Task MadaiIsPairedAndMagogIsLeftWhereItsLettersCannotSayIt()
    {
        var before = TenTwoAsItStands();

        var report = await Pass().Run("GEEZ81", "GRCBRENT", new HashSet<(int, int)> { (1, 10) }, null, apply: true);

        report.Should().Contain("Withdrew 0 links");
        var links = await Links(10, 2);
        links.Should().HaveCount(before + 1);
        links.Should().Contain((5, 7, NameLists.Settled));
        links.Should().NotContain(link => link.Geez == 4);
    }

    /// <summary>
    /// Genesis 14:5 gains nothing, and loses nothing. The Ge'ez follows the Greek, whose names here are
    /// Chedorlaomer, Ashteroth, Karnaim, the Ommaeans and Shaveh. The aligner put በአስጣሮስ against
    /// Καρναΐν rather than Ἀσταρώθ, so it is not unlinked; its STRS is three quarters of Ἀσταρώθ's STRT,
    /// which is not a spelling. No word the aligner left unlinked spells a name of the Greek.
    /// </summary>
    [Fact]
    public async Task TheAshterothOfFourteenFiveIsNotMovedOnLettersThatOnlyResembleIt()
    {
        FourteenFiveAsItStands();
        var before = await Links(14, 5);

        var report = await Pass().Run("GEEZ81", "GRCBRENT", new HashSet<(int, int)> { (1, 14) }, null, apply: true);

        report.Should().Contain("Withdrew 0 links");
        (await Links(14, 5)).Should().BeEquivalentTo(before);
        NameLists.Skeleton("በአስጣሮስ", "gez").Should().Be("STRS");
        NameLists.Skeleton("Ἀσταρὼθ").Should().Be("STRT");
    }

    /// <summary>
    /// እምነ "from" is MN, as Haman's Ἀμάν is. The pair's links put it against ἀπό five times and
    /// against no name, so in Genesis 1:6, where it stands unlinked beside Ἀμάν, it is left alone.
    /// </summary>
    [Fact]
    public async Task AFormTheLinksReadAsACommonWordIsNotTakenForAName()
    {
        for (var verse = 1; verse <= 5; verse++)
        {
            Link(1, verse, 1, 1);
        }

        var report = await Pass().Run("GEEZ81", "GRCBRENT", new HashSet<(int, int)>(), null, apply: true);

        report.Should().Contain("Withdrew 0 links");
        (await Links(1, 6)).Should().BeEmpty();
    }

    /// <summary>The same with the readings below the bar: four against ἀπό may be a misplaced name.</summary>
    [Fact]
    public async Task AFormReadAsACommonWordOnlyAFewTimesIsStillPaired()
    {
        for (var verse = 1; verse <= 4; verse++)
        {
            Link(1, verse, 1, 1);
        }

        await Pass().Run("GEEZ81", "GRCBRENT", new HashSet<(int, int)>(), null, apply: true);

        (await Links(1, 6)).Should().BeEquivalentTo([(2, 3, NameLists.Settled)]);
    }

    /// <summary>A second run finds every name it paired already linked, and writes nothing.</summary>
    [Fact]
    public async Task ASecondRunWritesNothing()
    {
        TenTwoAsItStands();
        await Pass().Run("GEEZ81", "GRCBRENT", new HashSet<(int, int)>(), null, apply: true);
        var after = await _db.Links.CountAsync();

        var again = await Pass().Run("GEEZ81", "GRCBRENT", new HashSet<(int, int)>(), null, apply: true);

        again.Should().Contain("Withdrew 0 links and wrote 0.");
        (await _db.Links.CountAsync()).Should().Be(after);
    }

    private int TenTwoAsItStands()
    {
        var links = new[] { (1, 1), (2, 2), (3, 3), (6, 9), (7, 11), (8, 13), (9, 15), (10, 17) };
        foreach (var (geez, greek) in links)
        {
            Link(10, 2, geez, greek);
        }

        return links.Length;
    }

    private void FourteenFiveAsItStands()
    {
        foreach (var (geez, greek) in new[]
                 {
                     (2, 7), (3, 10), (4, 9), (5, 11), (7, 15), (8, 16), (9, 21), (10, 21), (14, 29), (15, 34), (16, 32),
                 })
        {
            Link(14, 5, geez, greek);
        }
    }

    private NameListPass Pass() =>
        new(_db, new AlignmentPipeline(_db, NullLogger<AlignmentPipeline>.Instance), NullLogger<NameListPass>.Instance);

    private async Task<List<(int Geez, int Greek, double? Confidence)>> Links(int chapter, int verse) =>
        (await _db.Links.AsNoTracking()
            .Where(link => link.Words.Any(word => word.Side == LinkSide.From
                                                  && word.Word!.Verse!.ChapterNumber == chapter
                                                  && word.Word.Verse.Number == verse))
            .Select(link => new
            {
                Geez = link.Words.Single(word => word.Side == LinkSide.From).Word!.Position,
                Greek = link.Words.Single(word => word.Side == LinkSide.To).Word!.Position,
                link.Confidence,
            })
            .ToListAsync())
        .Select(link => (link.Geez, link.Greek, link.Confidence))
        .ToList();

    private void Link(int chapter, int verse, int geez, int greek)
    {
        var link = new Link
        {
            FromTextId = _geez.Id,
            ToTextId = _greek.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner,
            Confidence = 0.9,
            Provenance = new() { Source = "SIL.Machine, aligned as written" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(_geez, chapter, verse, geez), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(_greek, chapter, verse, greek), Side = LinkSide.To });
        _db.LinkClaims.Add(new LinkClaim
        {
            Link = link, Method = LinkMethod.Aligner, Confidence = 0.9, Provenance = link.Provenance,
        });
        _db.SaveChanges();
    }
}
