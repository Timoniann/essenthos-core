using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The names a translation prints apart from the word its link reaches: the Kulish Bible's two-word
/// names, of which the statistical link reaches one word and leaves the other blank or puts the next
/// name on it (<em>Астарот Карнаїмі</em> of Genesis 14:5, the second word named Kiriathaim), and the
/// Synodal's <em>Доик</em> in the title of Psalm 52, whose links to the Hebrew were drawn while the
/// Hebrew title carried its neighbours' numbers.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TwoWordNameRulingTests : IDisposable
{
    private const string Kulish = "Essenthos.Core.Loading.Encyclopedia.KulishTwoWordNameRecords.json";

    private const string Resolution = "BHSA's proper-noun marking, and the Strong number resolving to exactly one entity";

    private readonly AppDbContext _db;
    private readonly IReadOnlyList<OwnRecordRuling> _rulings;
    private readonly Dictionary<RuledWord, long> _words;
    private readonly Text _english;

    public TwoWordNameRulingTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _rulings = [.. SenseReadingFiles.AllRulings().SelectMany(file => file.Rulings)];

        var placed = _db.Place(_rulings.Select(ruling => ruling.Word));
        foreach (var ruling in _rulings)
        {
            placed[ruling.Word].StrongNumber = ruling.StrongNumber;
            placed[ruling.Word].Morphology = JsonDocument.Parse("""{"pos": "subs", "nameType": "pers"}""");
        }

        _english = Corpus.Add(_db, "ENGLISH", TextKind.Translation, "eng", (1, 1, ["Karnaim", "Doeg"]));
        _db.SaveChanges();
        _words = placed.ToDictionary(word => word.Key, word => word.Value.Id);

        foreach (var slug in _rulings
                     .SelectMany(r => new[] { r.Existing }.Concat(r.Alternatives?.Select(a => a.Slug) ?? []))
                     .Where(slug => slug is not null && _rulings.All(r => r.Create?.Slug != slug))
                     .Append("kiriathaim")
                     .Distinct())
        {
            _db.Entities.Add(new Entity
            {
                Kind = slug == "anointed" ? EntityKind.Title : EntityKind.Person,
                Slug = slug!,
                Name = slug!,
                SourceId = slug!,
                Source = "a test",
            });
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    private Task<OwnRecordOutcome> Load() =>
        new OwnRecordLoader(
                _db,
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { [OwnRecordLoader.BulkConfigurationKey] = "false" })
                    .Build(),
                NullLogger<OwnRecordLoader>.Instance)
            .Load(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}"));

    private static OwnRecordRulings File(string source) =>
        SenseReadingFiles.AllRulings().Single(file => file.Source.Contains(source, StringComparison.Ordinal));

    private long Word(string text, string reference, int position) =>
        _words.Single(w => w.Key.Text == text && w.Key.Reference == reference && w.Key.Position == position).Value;

    private int Entity(string slug) => _db.Entities.Single(e => e.Slug == slug).Id;

    private async Task<List<string>> Names(long word) =>
        await _db.WordEntities.Where(a => a.WordId == word).Select(a => a.Entity!.Slug).ToListAsync();

    /// <summary>A link of the kind the aligner draws, between one word of each of two texts.</summary>
    private async Task Linked(long from, long to)
    {
        var words = await _db.Words.Where(w => w.Id == from || w.Id == to).ToDictionaryAsync(w => w.Id);
        var link = new Link
        {
            FromTextId = words[from].TextId,
            ToTextId = words[to].TextId,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner,
            Confidence = 0.9,
            Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, WordId = from, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, WordId = to, Side = LinkSide.To });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Every word of a name the Kulish Bible prints in two words where the link reaches the other one:
    /// the half of Ashteroth-karnaim, Almon-diblathaim, Cushan-rishathaim, Baal-shalishah,
    /// Berodach-baladan and Pochereth-hazzebaim left blank, and the plain of Kiriathaim, whose word
    /// the link put on the Hebrew Shaveh. The 1 Chronicles 13:11 <em>Убиттє Ози</em> is left out: it
    /// translates Perez-uzzah as a phrase, and its <em>Ози</em> is Uzzah.
    /// </summary>
    [Fact]
    public void TheKulishBiblesTwoWordNamesAreRuledWordByWordAndStayOnTheirWords()
    {
        var file = File("the names the Kulish Bible prints in two words");

        file.Carry.Should().BeFalse("the link is what went wrong, so the ruling must not cross it");
        file.Method.Should().Be("manual");
        file.Rulings.Should().OnlyContain(r => r.Text == "UKR1871" && r.Corrects == null);
        file.Rulings.Select(r => (r.Reference, r.Position, r.Existing)).Should().Equal(
            ("GEN 14:5", 19, "ashterothkarnaim"),
            ("GEN 14:5", 27, "kiriathaim"),
            ("NUM 33:46", 9, "almondiblathaim"),
            ("JDG 3:8", 14, "cushanrishathaim"),
            ("JDG 3:8", 23, "cushanrishathaim"),
            ("JDG 3:10", 24, "cushanrishathaim"),
            ("JDG 3:10", 33, "cushanrishathaim"),
            ("2KI 4:42", 4, "baalshalishah"),
            ("2KI 20:12", 5, "berodachbaladan"),
            ("ISA 39:1", 5, "berodachbaladan"),
            ("EZR 2:57", 8, "pocherethhazzebaim"));
    }

    /// <summary>
    /// <em>Карнаїмі</em> carried Kiriathaim over the aligner's link to the Hebrew word at the end of the
    /// verse. The ruling names it Ashteroth-karnaim, takes the carried Kiriathaim off it, and puts the
    /// name on no word the link reaches; a second load writes nothing.
    /// </summary>
    [Fact]
    public async Task KarnaimNamesAshterothKarnaimAndNotTheKiriathaimTheLinkGaveIt()
    {
        var karnaim = Word("UKR1871", "GEN 14:5", 19);
        var beside = _db.WordAt(_english, 1, 1, 1).Id;
        await Linked(karnaim, beside);
        _db.WordEntities.Add(new WordEntity
        {
            WordId = karnaim, EntityId = Entity("kiriathaim"), Method = LinkMethod.StrongNumber, Confidence = 0.9,
            Source = Resolution, Note = "through BHSA word 1, linked by aligner",
        });
        await _db.SaveChangesAsync();
        (await Names(karnaim)).Should().Equal("kiriathaim");

        await Load();

        (await Names(karnaim)).Should().Equal("ashterothkarnaim");
        (await _db.WordEntities.SingleAsync(a => a.WordId == karnaim)).Method.Should().Be(LinkMethod.Manual);
        (await Names(beside)).Should().BeEmpty();
        (await Names(Word("UKR1871", "GEN 14:5", 27))).Should().Equal("kiriathaim");
        (await Names(Word("UKR1871", "2KI 20:12", 5))).Should().Equal("berodachbaladan");

        var before = await _db.WordEntities.OrderBy(a => a.Id).Select(a => a.Id).ToListAsync();
        (await Load()).AlreadyLoaded.Should().BeTrue();
        (await _db.WordEntities.OrderBy(a => a.Id).Select(a => a.Id).ToListAsync()).Should().Equal(before);
    }

    /// <summary>
    /// The Synodal's <em>Доик</em> in the title of Psalm 52 is Doeg, as the King James's <em>Doeg</em>
    /// beside it is, and the name stays on the Synodal's word: its link to the Hebrew title reaches a
    /// preposition.
    /// </summary>
    [Fact]
    public async Task TheSynodalsDoegInTheTitleOfPsalm52IsDoeg()
    {
        var file = File("the Synodal's names in the titles of the Psalms");
        file.Carry.Should().BeFalse();
        file.Rulings.Should().ContainSingle().Which.Should().Match<OwnRecordRuling>(r =>
            r.Text == "RUSV" && r.Reference == "PSA 52:1" && r.Position == 9 && r.Surface == "Доик" && r.Existing == "doeg");

        var doeg = Word("RUSV", "PSA 52:1", 9);
        var beside = _db.WordAt(_english, 1, 1, 2).Id;
        await Linked(doeg, beside);

        await Load();

        (await Names(doeg)).Should().Equal("doeg");
        (await Names(beside)).Should().BeEmpty();
    }
}
