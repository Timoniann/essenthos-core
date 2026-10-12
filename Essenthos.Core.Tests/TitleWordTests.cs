using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The words each title is written with, found by the Strong number the witnesses state on them and
/// the rules the title file gives, and carried to the translations like every other annotation.
///
/// <para>
/// Asked of Postgres because what is under test is the statement that finds the occurrences — a
/// second word beside the first, the singular of a Greek noun, a verse the rule leaves out, a word
/// something else already names — and the one that carries them.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TitleWordTests : IDisposable
{
    private const string Pharaoh = "H6547";

    private const string Priest = "H3548";

    private const string Great = "H1419";

    private const string HighPriest = "G749";

    private const string Governor = "G2232";

    private const int Matthew = 40;

    private readonly AppDbContext _db;
    private readonly TitleLoader _loader;
    private readonly Text _hebrew;
    private readonly Text _greek;
    private readonly Text _english;

    private static readonly IReadOnlyList<TitleRecord> Titles =
    [
        Title("pharaoh-title", new TitleWord(Pharaoh)),
        Title("high-priest", new TitleWord(Priest, With: Great), new TitleWord(Great, With: Priest),
            new TitleWord(HighPriest, Singular: true)),
        Title("governor", new TitleWord(Governor, Except: ["MAT 2:6"])),
    ];

    public TitleWordTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new TitleLoader(_db, NullLogger<TitleLoader>.Instance);

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (12, 15, ["פרעה", "שרי"]),
            (41, 1, ["פרעה"]),
            (21, 10, ["ה", "כהן", "ה", "גדול"]),
            (21, 1, ["כהן"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (12, 15, ["Pharaoh", "Sarai"]),
            (41, 1, ["Pharaoh"]));
        _greek = Corpus.Add(_db, EntityCandidates.GreekWitnesses[0], TextKind.CriticalEdition, "grc",
            (26, 3, ["ἀρχιερέως"]),
            (26, 4, ["ἀρχιερεῖς"]),
            (2, 6, ["ἡγεμόσιν"]),
            (27, 2, ["ἡγεμόνι"]));
        _db.SaveChanges();
        _db.In(_greek, Matthew);

        Number(_hebrew, 12, 15, 1, Pharaoh);
        Number(_hebrew, 41, 1, 1, Pharaoh);
        Number(_hebrew, 21, 10, 2, Priest);
        Number(_hebrew, 21, 10, 4, Great);
        Number(_hebrew, 21, 1, 1, Priest);
        Number(_greek, 26, 3, 1, HighPriest, "N-GSM");
        Number(_greek, 26, 4, 1, HighPriest, "N-NPM");
        Number(_greek, 2, 6, 1, Governor, "N-DPM");
        Number(_greek, 27, 2, 1, Governor, "N-DSM");

        foreach (var title in Titles)
        {
            _db.Entities.Add(new Entity
            {
                Kind = EntityKind.Title, Slug = title.Slug, Name = title.Name, SourceId = title.Slug, Source = "a test",
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

    private static TitleRecord Title(string slug, params TitleWord[] words) =>
        new(slug, slug, "a title", "Notes.", "A decision.", null, null, Words: words);

    private void Number(Text text, int chapter, int verse, int position, string number, string? form = null)
    {
        var word = _db.WordAt(text, chapter, verse, position);
        word.StrongNumber = number;
        if (form is not null)
        {
            word.Morphology = JsonDocument.Parse($$"""{"form": "{{form}}"}""");
        }

        _db.SaveChanges();
    }

    private async Task<Dictionary<long, string>> Named() =>
        await _db.WordEntities.AsNoTracking()
            .Where(a => a.Source == TitleLoader.WordSource)
            .Select(a => new { a.WordId, a.Entity!.Slug })
            .ToDictionaryAsync(a => a.WordId, a => a.Slug);

    /// <summary>Every word the witness numbers as Pharaoh is the title, stated by the number at its standing.</summary>
    [Fact]
    public async Task EveryWordOfTheNumberIsTheTitle()
    {
        var (seed, byText) = await _loader.NameTheWords(Titles, CancellationToken.None);

        var word = _db.WordAt(_hebrew, 12, 15, 1);
        (await Named())[word.Id].Should().Be("pharaoh-title");
        var row = await _db.WordEntities.SingleAsync(a => a.WordId == word.Id);
        row.Method.Should().Be(LinkMethod.RuleBased);
        row.Confidence.Should().Be(TitleLoader.ByTheNumber);
        (await _db.WordEntityClaims.CountAsync(c => c.WordEntityId == row.Id)).Should().Be(1);
        seed.Should().Be(6);
        byText.Should().NotBeNull();
    }

    /// <summary>A word something else already names keeps its name, and the title takes nothing from it.</summary>
    [Fact]
    public async Task AWordAlreadyNamedIsLeftToItsName()
    {
        var word = _db.WordAt(_hebrew, 41, 1, 1);
        var man = new Entity { Kind = EntityKind.Person, Slug = "pharaoh-2", Name = "Pharaoh", SourceId = "p2", Source = "a test" };
        _db.Entities.Add(man);
        _db.WordEntities.Add(new WordEntity
        {
            Word = word, Entity = man, Method = LinkMethod.ModelReading, Confidence = 0.9, Source = "a reading",
        });
        await _db.SaveChangesAsync();

        await _loader.NameTheWords(Titles, CancellationToken.None);

        (await Named()).Should().NotContainKey(word.Id);
        (await _db.WordEntities.CountAsync(a => a.WordId == word.Id)).Should().Be(1);
    }

    /// <summary>
    /// A title the file says stands beside its bearer is written on a word somebody already names,
    /// and the name stays: Christ in Jesus Christ is the title and the man.
    /// </summary>
    [Fact]
    public async Task ATitleThatStandsBesideItsBearerIsWrittenOnAWordAlreadyNamed()
    {
        var word = _db.WordAt(_hebrew, 41, 1, 1);
        var man = new Entity { Kind = EntityKind.Person, Slug = "pharaoh-2", Name = "Pharaoh", SourceId = "p2", Source = "a test" };
        _db.Entities.Add(man);
        _db.WordEntities.Add(new WordEntity
        {
            Word = word, Entity = man, Method = LinkMethod.ModelReading, Confidence = 0.9, Source = "a reading",
        });
        await _db.SaveChangesAsync();

        await _loader.NameTheWords(
            [Title("pharaoh-title", new TitleWord(Pharaoh, Beside: true))], CancellationToken.None);

        (await Named())[word.Id].Should().Be("pharaoh-title");
        (await _db.WordEntities.Where(a => a.WordId == word.Id).Select(a => a.Entity!.Slug).ToListAsync())
            .Should().BeEquivalentTo("pharaoh-2", "pharaoh-title");
    }

    /// <summary>
    /// <em>The priest</em> is the high priest beside <em>great</em>, and both words are the title;
    /// alone it is not.
    /// </summary>
    [Fact]
    public async Task TheSecondWordDecidesTheHighPriest()
    {
        await _loader.NameTheWords(Titles, CancellationToken.None);

        var named = await Named();
        named[_db.WordAt(_hebrew, 21, 10, 2).Id].Should().Be("high-priest");
        named[_db.WordAt(_hebrew, 21, 10, 4).Id].Should().Be("high-priest");
        named.Should().NotContainKey(_db.WordAt(_hebrew, 21, 1, 1).Id);
    }

    /// <summary>The high priest is the singular; the plural is the chief priests as a body.</summary>
    [Fact]
    public async Task ThePluralIsNotTheTitle()
    {
        await _loader.NameTheWords(Titles, CancellationToken.None);

        var named = await Named();
        named[_db.WordAt(_greek, 26, 3, 1).Id].Should().Be("high-priest");
        named.Should().NotContainKey(_db.WordAt(_greek, 26, 4, 1).Id);
    }

    /// <summary>The princes of Judah at Matthew 2:6 are not governors, and the rule leaves that verse out.</summary>
    [Fact]
    public async Task AVerseTheRuleLeavesOutStaysUnnamed()
    {
        await _loader.NameTheWords(Titles, CancellationToken.None);

        var named = await Named();
        named[_db.WordAt(_greek, 27, 2, 1).Id].Should().Be("governor");
        named.Should().NotContainKey(_db.WordAt(_greek, 2, 6, 1).Id);
    }

    /// <summary>The translations reach the title through the links, as every other annotation does.</summary>
    [Fact]
    public async Task TheTitleTravelsToTheTranslationTheLinksReach()
    {
        var rendering = _db.WordAt(_english, 12, 15, 1);
        var link = new Link
        {
            FromTextId = _english.Id, ToTextId = _hebrew.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = rendering, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(_hebrew, 12, 15, 1), Side = LinkSide.To });
        await _db.SaveChangesAsync();

        var (_, byText) = await _loader.NameTheWords(Titles, CancellationToken.None);

        (await Named())[rendering.Id].Should().Be("pharaoh-title");
        byText.Should().Contain(("KJV", 1));
    }

    /// <summary>The startup pipeline runs on every boot, and a second boot writes nothing.</summary>
    [Fact]
    public async Task ASecondBootWritesNothing()
    {
        await _loader.NameTheWords(Titles, CancellationToken.None);
        var ids = await _db.WordEntities.Select(a => a.Id).OrderBy(id => id).ToListAsync();

        var (seed, byText) = await _loader.NameTheWords(Titles, CancellationToken.None);

        byText.Should().BeNull();
        seed.Should().Be(6);
        (await _db.WordEntities.Select(a => a.Id).OrderBy(id => id).ToListAsync()).Should().Equal(ids);
    }

    /// <summary>
    /// A reading fixes Pharaoh of Genesis 41:1 to one king after the title has been written on every
    /// Pharaoh. The next pass takes the title off that word and off the word the links carried it to,
    /// and leaves every other row with its id; the pass after it writes nothing; and the corpus holds
    /// what a pass run after the reading writes from nothing.
    /// </summary>
    [Fact]
    public async Task AWordAReadingFixesToOneKingLosesTheTitleAndNothingElseIsWrittenAgain()
    {
        var hebrew = _db.WordAt(_hebrew, 41, 1, 1);
        var rendering = _db.WordAt(_english, 41, 1, 1);
        Link(_db.WordAt(_english, 12, 15, 1), _db.WordAt(_hebrew, 12, 15, 1));
        Link(rendering, hebrew);
        _db.Entities.Add(new Entity { Kind = EntityKind.Person, Slug = "pharaoh-2", Name = "Pharaoh", SourceId = "p2", Source = "a test" });
        await _db.SaveChangesAsync();
        await _loader.NameTheWords(Titles, CancellationToken.None);
        (await Named()).Should().ContainKeys(hebrew.Id, rendering.Id);

        var readings = new TitleReadings(
            "two models", "which king the verse means", "model-reading", "a reading of Pharaoh by two models",
            "pharaoh-title", [new TitleReading("GEN 41:1", Pharaoh, "pharaoh-2", "the king of the dreams", Confidence: 0.9)]);
        (await new TitleReadingLoader(_db, NullLogger<TitleReadingLoader>.Instance).Load(readings))
            .ByBearer.Should().Equal(("pharaoh-2", 1));
        var read = await Rows();

        var (_, byText) = await _loader.NameTheWords(Titles, CancellationToken.None);

        byText.Should().NotBeNull();
        var titled = await Rows();
        titled.Should().BeEquivalentTo(read.Where(row =>
            !(row.Source == TitleLoader.WordSource && (row.WordId == hebrew.Id || row.WordId == rendering.Id))));
        titled.Where(row => row.WordId == hebrew.Id || row.WordId == rendering.Id)
            .Should().HaveCount(2).And.OnlyContain(row => row.Source == readings.Source);

        (await _loader.NameTheWords(Titles, CancellationToken.None)).ByText.Should().BeNull();
        (await Rows()).Should().BeEquivalentTo(titled);

        var written = titled.Where(row => row.Source == TitleLoader.WordSource)
            .Select(row => (row.WordId, row.EntityId, row.Confidence, row.Note)).ToList();
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM word_entity WHERE source = {0}", TitleLoader.WordSource);
        await _loader.NameTheWords(Titles, CancellationToken.None);
        (await Rows()).Where(row => row.Source == TitleLoader.WordSource)
            .Select(row => (row.WordId, row.EntityId, row.Confidence, row.Note))
            .Should().BeEquivalentTo(written);
    }

    /// <summary>
    /// A rule reaches one word more than it did: that word and the word it carries to are written,
    /// and every row already there keeps its id and its claim.
    /// </summary>
    [Fact]
    public async Task ARuleThatReachesAWordMoreWritesOnlyThatWord()
    {
        Link(_db.WordAt(_english, 12, 15, 1), _db.WordAt(_hebrew, 12, 15, 1));
        await _db.SaveChangesAsync();
        await _loader.NameTheWords([Titles[0]], CancellationToken.None);
        var before = await Rows();
        var claims = await _db.WordEntityClaims.AsNoTracking().Select(c => c.Id).ToListAsync();

        await _loader.NameTheWords(Titles, CancellationToken.None);

        var after = await Rows();
        after.Should().Contain(before);
        after.Except(before).Should().OnlyContain(row => row.Source == TitleLoader.WordSource && row.EntityId != before[0].EntityId);
        (await _db.WordEntityClaims.AsNoTracking().Select(c => c.Id).ToListAsync()).Should().Contain(claims);
    }

    private void Link(Word rendering, Word word)
    {
        var link = new Link
        {
            FromTextId = rendering.TextId, ToTextId = word.TextId, Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = rendering, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = word, Side = LinkSide.To });
    }

    private sealed record Row(long Id, long WordId, int EntityId, string Source, double? Confidence, string? Note);

    private async Task<List<Row>> Rows() =>
        await _db.WordEntities.AsNoTracking()
            .OrderBy(a => a.Id)
            .Select(a => new Row(a.Id, a.WordId, a.EntityId, a.Source, a.Confidence, a.Note))
            .ToListAsync();

    /// <summary>Every rule the shipped file gives names a number and, where it leaves verses out, verses.</summary>
    [Fact]
    public void EveryRuleInTheFileIsWellFormed()
    {
        var rules = SenseReadingFiles.Titles().Titles.SelectMany(t => t.Words ?? []).ToList();

        rules.Should().NotBeEmpty();
        rules.Should().OnlyContain(rule => System.Text.RegularExpressions.Regex.IsMatch(rule.Strong, "^[HG][0-9]+$"));
        rules.SelectMany(rule => rule.Except ?? [])
            .Should().OnlyContain(verse => ScriptureSpan.TryParse(verse) != null && ScriptureSpan.Parse(verse).IsVerse);
    }
}
