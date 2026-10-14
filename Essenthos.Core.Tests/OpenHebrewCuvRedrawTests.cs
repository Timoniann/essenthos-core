using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Chinese Union's links in Psalm 60's title as a corpus loaded them from running numbers placed
/// by position, a word early between Aram-naharaim and the Valley of Salt, and what the next load does
/// with them: the spans are drawn again from the numbers as they read now, FHL's claim goes with a
/// link it was matched onto by the same wrong number and stays where its number still holds, and a
/// second load changes nothing.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OpenHebrewCuvRedrawTests : IDisposable
{
    private readonly AppDbContext _db;

    public OpenHebrewCuvRedrawTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM text");

    [Fact]
    public async Task TheTitlesSpansAreDrawnAgainFromTheNumbersAsTheyReadNowAndOnlyOnce()
    {
        var bhsa = Corpus.Add(_db, "BHSA", TextKind.ManuscriptTradition, "hbo",
            (1, 1, ["א", "ב"]),
            (1, 2, ["לַ", "מְנַצֵּחַ", "אֲרַם", "נַהֲרַיִם", "וַ", "יֹואָב", "בְּ", "גֵיא־מֶלַח", "שְׁנֵים", "עָשָׂר", "אָלֶף", "אֱלֹהִים", "זְנַחְתָּנוּ", "פְרַצְתָּנוּ"]),
            (1, 3, ["ג", "ד"]));
        var cuv = Corpus.Add(_db, SwordTextSource.ChineseUnion, TextKind.Translation, "zho",
            (1, 2, ["兩河間的亞蘭", "約押", "在鹽谷"]));
        _db.SaveChanges();
        string[] glosses =
        [
            "gloss1", "gloss2", "to", "prevail", "Aram", "stream", "and", "Joab", "in", "the Valley of Salt", "two",
            "-teen", "thousand", "god(s)", "reject", "break", "gloss16", "gloss17",
        ];
        var hebrew = _db.Words.Where(w => w.TextId == bhsa.Id).OrderBy(w => w.Id).ToList();
        for (var i = 0; i < hebrew.Count; i++)
        {
            hebrew[i].Gloss = glosses[i];
        }

        Word Hebrew(string surface) => hebrew.Single(w => w.Surface == surface);
        Hebrew("אֲרַם").StrongNumber = "H758";
        Hebrew("וַ").StrongNumber = "H9000";
        Hebrew("יֹואָב").StrongNumber = "H3097";
        Hebrew("בְּ").StrongNumber = "H9003";
        Hebrew("גֵיא־מֶלַח").StrongNumber = "H1516";
        var chinese = _db.Words.Where(w => w.TextId == cuv.Id).OrderBy(w => w.Position).ToList();
        Word Chinese(string surface) => chinese.Single(w => w.Surface == surface);

        var mapping = new Provenance { Source = OpenHebrewCuvLinkLoader.Credit };
        var numbers = new Provenance { Source = UnionStrongLinkLoader.Credit + ", for a test" };
        var aram = Link(cuv, bhsa, [Chinese("兩河間的亞蘭")], [Hebrew("אֲרַם")], mapping, numbers);
        Link(cuv, bhsa, [Chinese("約押")], [Hebrew("וַ")], mapping, numbers);
        Link(cuv, bhsa, [Chinese("在鹽谷")], [Hebrew("בְּ")], mapping);
        _db.SaveChanges();

        MappingRecord[] records =
        [
            new(1, 1, 1, [new("H1", "c1", 1, "gloss1"), new("H2", "c1", 2, "gloss2")], []),
            new(1, 1, 2,
            [
                new("H9005", "c2", 3, "to"), new("H5329", "c2", 4, "prevail"), new("H763", "c2", 5, "Mesopotamia"),
                new("H9000", "c2", 6, "and"), new("H3097", "c2", 7, "Joab"), new("H9003", "c2", 8, "in"),
                new("H1516", "c2", 9, "the Valley of Salt"), new("H4417", "c2", 9, "the Valley of Salt"),
                new("H8147", "c2", 10, "two"), new("H6240", "c2", 11, "-teen"), new("H505", "c2", 12, "thousand"),
                new("H430", "c2", 13, "god(s)"), new("H2186", "c2", 14, "reject"), new("H6555", "c2", 15, "break"),
            ],
            []),
            new(1, 1, 3, [new("H16", "c3", 16, "gloss16"), new("H17", "c3", 17, "gloss17")], []),
        ];
        OhbVerse[] mapped = [new(1, 1, 2, [new("兩河間的亞蘭", [5]), new("約押", [7]), new("在鹽谷", [9])])];

        // FHL numbers the Aramaeans H758 and H763, which אֲרַם still carries, and Joab H3097, which
        // the conjunction carried only while the numbers were a word early.
        IReadOnlyDictionary<long, WordTag> tags = new Dictionary<long, WordTag>
        {
            [Chinese("兩河間的亞蘭").Id] = new(["H758", "H763"], 1),
            [Chinese("約押").Id] = new(["H3097"], 1),
        };
        var loader = new OpenHebrewCuvLinkLoader(
            _db,
            new OldTestamentLinkLoader(_db, NullLogger<OldTestamentLinkLoader>.Instance),
            NullLogger<OpenHebrewCuvLinkLoader>.Instance);

        (await loader.Redraw(records, mapped, _ => Task.FromResult(tags))).Should().Be(3);

        _db.ChangeTracker.Clear();
        var links = await Links(cuv);
        links.Should().BeEquivalentTo(new[]
        {
            ("兩河間的亞蘭", "אֲרַם", LinkMethod.StrongNumber),
            ("兩河間的亞蘭", "אֲרַם נַהֲרַיִם", LinkMethod.StatedBySource),
            ("約押", "יֹואָב", LinkMethod.StatedBySource),
            ("在鹽谷", "גֵיא־מֶלַח", LinkMethod.StatedBySource),
        });
        (await _db.LinkClaims.Where(c => c.LinkId == aram.Id).Select(c => c.Provenance!.Source).ToListAsync())
            .Should().Equal(numbers.Source);

        var standing = await _db.Links.OrderBy(l => l.Id).Select(l => l.Id).ToListAsync();
        (await loader.Redraw(records, mapped, _ => Task.FromResult(tags))).Should().Be(0);
        (await _db.Links.OrderBy(l => l.Id).Select(l => l.Id).ToListAsync()).Should().Equal(standing);
    }

    private Link Link(Text from, Text to, Word[] chinese, Word[] hebrew, params Provenance[] claims)
    {
        var link = new Link
        {
            FromTextId = from.Id, ToTextId = to.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource, Provenance = claims[0],
            Fingerprint = LinkShape.Of([.. chinese.Select(w => w.Id)], [.. hebrew.Select(w => w.Id)]),
        };
        foreach (var word in chinese)
        {
            link.Words.Add(new LinkWord { Word = word, Side = LinkSide.From });
        }

        foreach (var word in hebrew)
        {
            link.Words.Add(new LinkWord { Word = word, Side = LinkSide.To });
        }

        foreach (var claim in claims)
        {
            link.Claims.Add(new LinkClaim
            {
                Method = claim == claims[0] ? LinkMethod.StatedBySource : LinkMethod.StrongNumber,
                Confidence = claim == claims[0] ? null : 0.9,
                Provenance = claim,
            });
        }

        _db.Links.Add(link);
        return link;
    }

    private async Task<List<(string Chinese, string Hebrew, LinkMethod Method)>> Links(Text cuv)
    {
        var links = await _db.Links.Where(l => l.FromTextId == cuv.Id)
            .Select(l => new
            {
                l.Method,
                From = l.Words.Where(w => w.Side == LinkSide.From).Select(w => w.Word!.Surface).ToList(),
                To = l.Words.Where(w => w.Side == LinkSide.To).OrderBy(w => w.WordId).Select(w => w.Word!.Surface).ToList(),
            })
            .ToListAsync();
        return [.. links.Select(l => (string.Join(" ", l.From), string.Join(" ", l.To), l.Method))];
    }
}
