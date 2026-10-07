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
/// 1 Chronicles 1, where the table of nations names the sons of Japheth and Ham and nothing names
/// them back: BHSA marks their lexemes as several kinds at once, and the resolution by number waits
/// for a marking that commits.
///
/// Each case is one thing the verse list and the marking have to say, or must not be allowed to
/// say, before an unnamed Hebrew name is taken to be the one bearer the list names.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ListedBearerTests : IDisposable
{
    private const string Ham = "H2526";

    private const string Reuben = "H7205";

    private const string Zadok = "H6659";

    private const string Gath = "H1661";

    private const string Zilpah = "H2153";

    private const string Leummim = "H3817";

    private const string Gedor = "H1446";

    private const string Son = "H1121";

    private const string Father = "H1";

    private const string BibleData =
        "BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0";

    private const string Ours = "Essenthos, from the words this corpus annotates to the person or the place they name";

    private readonly AppDbContext _db;
    private readonly string _readings;
    private readonly Text _hebrew;
    private readonly Text _russian;

    /// <summary>
    /// One verse per case, the name always the last word; what varies is the marking, the bearers,
    /// what the list names and what else the verse says.
    /// </summary>
    public ListedBearerTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        _readings = Path.Combine(Path.GetTempPath(), $"listed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_readings, "run-1"));
        File.WriteAllText(Path.Combine(_readings, "run-1", SenseReadingFiles.AnswersFileName), string.Empty);

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (1, 1, ["בני", "חם"]),
            (1, 2, ["בני", "ראובן"]),
            (1, 3, ["צדוק"]),
            (1, 4, ["גת"]),
            (1, 5, ["בני", "זלפה"]),
            (1, 6, ["זלפה"]),
            (1, 7, ["בני", "לאמים"]),
            (1, 8, ["אבי", "גדור"]),
            (1, 9, ["חם"]),
            (1, 10, ["חם"]));
        _russian = Corpus.Add(_db, "RUSV", TextKind.Translation, "rus", (1, 1, ["Хама"]));
        _db.SaveChanges();

        Name(1, 1, Son, null);
        Name(1, 2, Ham, "pers,gens");
        Name(2, 1, Son, null);
        Name(2, 2, Reuben, "pers,gens,topo");
        Name(3, 1, Zadok, "pers");
        Name(4, 1, Gath, "topo");
        Name(5, 1, Son, null);
        Name(5, 2, Zilpah, "gens");
        Name(6, 1, Zilpah, "gens");
        Name(7, 1, Son, null);
        Name(7, 2, Leummim, "gens", plural: true);
        Name(8, 1, Father, null);
        Name(8, 2, Gedor, "pers");
        Name(9, 1, Ham, "pers,gens");
        Name(10, 1, Ham, "pers,gens");

        var ham = Add("ham", EntityKind.Person, Ham);
        var reuben = Add("reuben", EntityKind.Person, Reuben);
        Add("reubenites", EntityKind.People, Reuben);
        var zadok = Add("zadok", EntityKind.Person, Zadok);
        Add("zadok-2", EntityKind.Person, Zadok);
        var gath = Add("gath", EntityKind.Place, Gath);
        Add("gath-2", EntityKind.Place, Gath);
        var zilpah = Add("zilpah", EntityKind.Person, Zilpah);
        var leum = Add("leum", EntityKind.Person, Leummim);
        var founder = Add("gedor", EntityKind.Person, Gedor);

        // 1 the son of Noah; 2 a name a tribe bears too; 3 one of two Zadoks; 4 one of two Gaths;
        // 5 Zilpah where the verse speaks of sons; 6 the same name where it speaks of nobody's kin;
        // 7 a gentilic plural; 8 the name after "father of"; 9 a list this corpus wrote; 10 no list.
        Attest(ham, 1, BibleData);
        Attest(reuben, 2, BibleData);
        Attest(zadok, 3, BibleData);
        Attest(gath, 4, BibleData);
        Attest(zilpah, 5, BibleData);
        Attest(zilpah, 6, BibleData);
        Attest(leum, 7, BibleData);
        Attest(founder, 8, BibleData);
        Attest(ham, 9, Ours);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
        if (Directory.Exists(_readings))
        {
            Directory.Delete(_readings, recursive: true);
        }
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    private void Name(int verse, int position, string number, string? marking, bool plural = false)
    {
        var word = Hebrew(verse, position);
        word.StrongNumber = number;
        word.Morphology = marking is null
            ? JsonDocument.Parse("""{"pos": "subs"}""")
            : JsonDocument.Parse(
                $$"""{"pos": "nmpr", "nameType": "{{marking}}", "number": "{{(plural ? "pl" : "sg")}}"}""");
        _db.SaveChanges();
    }

    private Entity Add(string slug, EntityKind kind, string number)
    {
        var entity = new Entity
        {
            Kind = kind, Slug = slug, Name = slug, SourceId = slug, Source = "a test",
            Names = [new EntityName { Label = slug, HebrewStrongNumber = number, Kind = "proper name" }],
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private void Attest(Entity entity, int verse, string source) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = entity, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = verse, Source = source,
        });

    private Word Hebrew(int verse, int position = 1) => _db.WordAt(_hebrew, 1, verse, position);

    private ListedBearerLoader Loader(string? readings = null) =>
        new(
            _db,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [SenseReadingFiles.ConfigurationKey] = readings ?? _readings,
                })
                .Build(),
            NullLogger<ListedBearerLoader>.Instance);

    private async Task<Dictionary<long, string>> Load()
    {
        await Loader().Load("unused, the path is configured");
        return await _db.WordEntities.ToDictionaryAsync(a => a.WordId, a => a.Entity!.Slug);
    }

    [Fact]
    public async Task ANameOfSeveralKindsIsTheOneBearerTheVerseNames()
    {
        var named = await Load();

        named.Should().ContainKey(Hebrew(1, 2).Id).WhoseValue.Should().Be("ham");
        var row = await _db.WordEntities.SingleAsync(a => a.WordId == Hebrew(1, 2).Id);
        row.Method.Should().Be(LinkMethod.Lexical);
        row.Confidence.Should().Be(0.96);
    }

    /// <summary>
    /// <em>The sons of Reuben</em> are the man's sons in Genesis 46 and the tribe in Numbers 1, and
    /// the tribe has no dataset list to be named in, so a list can only ever say the man.
    /// </summary>
    [Fact]
    public async Task ANameATribeBearsTooIsLeftAlone()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(2, 2).Id);
    }

    /// <summary>Which of the Zadoks a verse means is the readings' question, not a list's.</summary>
    [Fact]
    public async Task ANameSeveralPersonsBearIsLeftAlone()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(3).Id);
    }

    [Fact]
    public async Task APlaceSeveralRecordsBearIsTheOneTheVerseNames()
    {
        var named = await Load();
        named.Should().ContainKey(Hebrew(4).Id).WhoseValue.Should().Be("gath");
    }

    [Fact]
    public async Task ANameMarkedAPeopleIsAPersonWhereTheVerseSpeaksOfKin()
    {
        var named = await Load();

        named.Should().ContainKey(Hebrew(5, 2).Id).WhoseValue.Should().Be("zilpah");
        named.Should().NotContainKey(Hebrew(6).Id,
            "a people's name in a verse about nobody's kin is the people, however a list files it");
    }

    [Fact]
    public async Task AGentilicPluralStaysAPeople()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(7, 2).Id);
    }

    [Fact]
    public async Task TheNameAfterFatherOfIsNotTheMan()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(8, 2).Id);
    }

    [Fact]
    public async Task OnlyADatasetsListCounts()
    {
        var named = await Load();

        named.Should().NotContainKey(Hebrew(9).Id);
        named.Should().NotContainKey(Hebrew(10).Id);
    }

    /// <summary>
    /// A reading that said the encyclopedia holds nobody who fits is an answer, and it stands above a
    /// list that names somebody.
    /// </summary>
    [Fact]
    public async Task AWordAReadingAnsweredIsLeftAlone()
    {
        var row = JsonSerializer.Serialize(new Dictionary<string, object?>(_db.AddressFields(Hebrew(1, 2).Id))
        {
            ["strong_number"] = Ham,
            ["referent"] = "unlisted",
            ["names"] = null,
            ["confidence"] = "high",
            ["reason"] = "the verse decides it",
            ["prompt_version"] = "sense-1",
            ["model"] = "a-model",
            ["run"] = "2026-09-05T21:35:25+00:00",
        });
        await File.WriteAllTextAsync(
            Path.Combine(_readings, "run-1", SenseReadingFiles.AnswersFileName), row + Environment.NewLine);

        var named = await Load();

        named.Should().NotContainKey(Hebrew(1, 2).Id);
    }

    [Fact]
    public async Task WithoutTheReadingsNothingIsWritten()
    {
        var outcome = await Loader(Path.Combine(_readings, "absent")).Load("unused");

        outcome.NoReadings.Should().BeTrue();
        (await _db.WordEntities.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TheAnswerTravelsToTheTranslationsTheLinksReach()
    {
        var rendering = _db.WordAt(_russian, 1, 1, 1);
        var link = new Link
        {
            FromTextId = _russian.Id, ToTextId = _hebrew.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StrongNumber, Confidence = 0.9, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = rendering, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = Hebrew(1, 2), Side = LinkSide.To });
        await _db.SaveChangesAsync();

        var named = await Load();

        named.Should().ContainKey(rendering.Id).WhoseValue.Should().Be("ham");
    }

    [Fact]
    public async Task LoadingTwiceWritesNothingTheSecondTime()
    {
        var first = await Loader().Load("unused");
        var count = await _db.WordEntities.CountAsync();
        var second = await Loader().Load("unused");

        first.Settled.Should().Be(3);
        first.Tribal.Should().Be(1);
        first.Namesakes.Should().Be(1);
        second.AlreadyLoaded.Should().BeTrue();
        (await _db.WordEntities.CountAsync()).Should().Be(count);
    }
}
