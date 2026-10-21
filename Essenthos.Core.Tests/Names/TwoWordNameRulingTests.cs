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

namespace Essenthos.Core.Tests.Names;

/// <summary>
/// The names a translation prints apart from the word its link reaches: the Kulish Bible's two-word
/// names, of which the statistical link reaches one word and leaves the other blank or puts the next
/// name on it (<em>Астарот Карнаїмі</em> of Genesis 14:5, the second word named Kiriathaim), and the
/// Synodal's <em>Доик</em> in the title of Psalm 52, whose links to the Hebrew were drawn while the
/// Hebrew title carried its neighbours' numbers.
///
/// <para>
/// Both are laid in one world before one load of the rulings: they are words of different texts,
/// linked to different words of the translation, and each test reads that load.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TwoWordNameRulingTests(WitnessDatabase database) : IAsyncLifetime
{
    private const string Resolution = "BHSA's proper-noun marking, and the Strong number resolving to exactly one entity";

    private World _world = null!;

    private AppDbContext Db => _world.Db;

    public async Task InitializeAsync() => _world = await database.Shared(() => World.Loaded(database));

    public Task DisposeAsync() => Task.CompletedTask;

    private static OwnRecordRulings File(string source) =>
        SenseReadingFiles.AllRulings().Single(file => file.Source.Contains(source, StringComparison.Ordinal));

    private async Task<List<string>> Names(long word) =>
        await Db.WordEntities.Where(a => a.WordId == word).Select(a => a.Entity!.Slug).ToListAsync();

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
        var karnaim = _world.Word("UKR1871", "GEN 14:5", 19);
        _world.KarnaimBefore.Should().Equal("kiriathaim");

        (await Names(karnaim)).Should().Equal("ashterothkarnaim");
        (await Db.WordEntities.SingleAsync(a => a.WordId == karnaim)).Method.Should().Be(LinkMethod.Manual);
        (await Names(_world.Beside(1))).Should().BeEmpty();
        (await Names(_world.Word("UKR1871", "GEN 14:5", 27))).Should().Equal("kiriathaim");
        (await Names(_world.Word("UKR1871", "2KI 20:12", 5))).Should().Equal("berodachbaladan");

        var before = await Db.WordEntities.OrderBy(a => a.Id).Select(a => a.Id).ToListAsync();
        (await _world.Load()).AlreadyLoaded.Should().BeTrue();
        (await Db.WordEntities.OrderBy(a => a.Id).Select(a => a.Id).ToListAsync()).Should().Equal(before);
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

        (await Names(_world.Word("RUSV", "PSA 52:1", 9))).Should().Equal("doeg");
        (await Names(_world.Beside(2))).Should().BeEmpty();
    }

    /// <summary>
    /// Every word a ruling the corpus ships names, at its address and with its Strong number, the
    /// records the rulings name, and a translation whose two words the arrangements link to.
    /// </summary>
    private sealed class World : IDisposable
    {
        private readonly Dictionary<RuledWord, long> _words;
        private readonly Text _english;

        private World(AppDbContext db, Dictionary<RuledWord, long> words, Text english)
        {
            Db = db;
            _words = words;
            _english = english;
        }

        public AppDbContext Db { get; }

        /// <summary>What <em>Карнаїмі</em> was named before the load.</summary>
        public List<string> KarnaimBefore { get; private set; } = [];

        public static async Task<World> Loaded(WitnessDatabase database)
        {
            var world = Make(database);
            await world.ArrangeKarnaim();
            await world.Linked(world.Word("RUSV", "PSA 52:1", 9), world.Beside(2));
            await world.Load();
            return world;
        }

        private static World Make(WitnessDatabase database)
        {
            var db = database.NewContext();
            IReadOnlyList<OwnRecordRuling> rulings = [.. SenseReadingFiles.AllRulings().SelectMany(file => file.Rulings)];

            var placed = db.Place(rulings.Select(ruling => ruling.Word));
            foreach (var ruling in rulings)
            {
                placed[ruling.Word].StrongNumber = ruling.StrongNumber;
                placed[ruling.Word].Morphology = JsonDocument.Parse("""{"pos": "subs", "nameType": "pers"}""");
            }

            var english = Corpus.Add(db, "ENGLISH", TextKind.Translation, "eng", (1, 1, ["Karnaim", "Doeg"]));
            db.SaveChanges();
            var words = placed.ToDictionary(word => word.Key, word => word.Value.Id);

            foreach (var slug in rulings
                         .SelectMany(r => new[] { r.Existing }.Concat(r.Alternatives?.Select(a => a.Slug) ?? []))
                         .Where(slug => slug is not null && rulings.All(r => r.Create?.Slug != slug))
                         .Distinct())
            {
                db.Entities.Add(new Entity
                {
                    Kind = slug == "anointed" ? EntityKind.Title : EntityKind.Person,
                    Slug = slug!,
                    Name = slug!,
                    SourceId = slug!,
                    Source = "a test",
                });
            }

            db.SaveChanges();

            // The loader compares everything its context tracks on every query and save; the tens of
            // thousands of rows written here made a load of seconds take minutes.
            db.ChangeTracker.Clear();
            return new World(db, words, english);
        }

        public long Word(string text, string reference, int position) =>
            _words.Single(w => w.Key.Text == text && w.Key.Reference == reference && w.Key.Position == position).Value;

        /// <summary>The translation's word an arrangement links to.</summary>
        public long Beside(int position) => Db.WordAt(_english, 1, 1, position).Id;

        public Task<OwnRecordOutcome> Load() =>
            new OwnRecordLoader(
                    Db,
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?> { [OwnRecordLoader.BulkConfigurationKey] = "false" })
                        .Build(),
                    NullLogger<OwnRecordLoader>.Instance)
                .Load(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}"));

        public void Dispose() => Db.Dispose();

        /// <summary><em>Карнаїмі</em> linked by the aligner and carrying the Kiriathaim the link gave it.</summary>
        private async Task ArrangeKarnaim()
        {
            var karnaim = Word("UKR1871", "GEN 14:5", 19);
            await Linked(karnaim, Beside(1));
            Db.WordEntities.Add(new WordEntity
            {
                WordId = karnaim, EntityId = Db.Entities.Single(e => e.Slug == "kiriathaim").Id,
                Method = LinkMethod.StrongNumber, Confidence = 0.9, Source = Resolution,
                Note = "through BHSA word 1, linked by aligner",
            });
            await Db.SaveChangesAsync();
            KarnaimBefore = await Db.WordEntities.Where(a => a.WordId == karnaim).Select(a => a.Entity!.Slug).ToListAsync();
        }

        /// <summary>A link of the kind the aligner draws, between one word of each of two texts.</summary>
        private async Task Linked(long from, long to)
        {
            var words = await Db.Words.Where(w => w.Id == from || w.Id == to).ToDictionaryAsync(w => w.Id);
            var link = new Link
            {
                FromTextId = words[from].TextId,
                ToTextId = words[to].TextId,
                Relation = LinkRelation.Renders,
                Method = LinkMethod.Aligner,
                Confidence = 0.9,
                Provenance = new() { Source = "a test" },
            };
            Db.Links.Add(link);
            Db.LinkWords.Add(new LinkWord { Link = link, WordId = from, Side = LinkSide.From });
            Db.LinkWords.Add(new LinkWord { Link = link, WordId = to, Side = LinkSide.To });
            await Db.SaveChangesAsync();
        }
    }
}
