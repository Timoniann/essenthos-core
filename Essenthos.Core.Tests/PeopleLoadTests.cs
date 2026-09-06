using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Strong;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The peoples, loaded against the file this corpus actually ships and a lexicon shaped like the
/// real one.
///
/// What is under test is the shape a reader meets: that a nation named after a man reaches his
/// page, that a word which <em>is</em> the people's name is annotated to them, and — the thing this
/// layer exists to stop — that a name which stands for the man in one verse and the tribe in
/// another is not swept up wholesale. The last of those is asserted by what is <em>not</em>
/// annotated, which is the only way an absence can be pinned.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PeopleLoadTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PeopleFile _file;
    private readonly Text _hebrew;

    /// <summary>The eight occurrences a review ruled name the tribe rather than the patriarch.</summary>
    private readonly IReadOnlyList<PeopleRuling> _rulings;

    public PeopleLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Database.ExecuteSqlRaw("DELETE FROM strong_gentilic");
        _db.Database.ExecuteSqlRaw("DELETE FROM strong_entry");

        _file = PeopleFiles.Read();
        _rulings = _file.Rulings;

        // One verse per word: the ruled occurrences, then a gentilic used as the people, the same
        // gentilic standing as somebody's name, and a gentilic whose origin nobody holds.
        var verses = Enumerable
            .Range(1, _rulings.Count + Extra)
            .Select(number => (Chapter: 1, Verse: number, Words: new[] { "w" }))
            .ToArray();

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo", verses);
        _db.SaveChanges();

        for (var position = 0; position < _rulings.Count; position++)
        {
            Mark(position + 1, Collective, "pers,gens,topo");
            _db.Database.ExecuteSqlRaw(
                "UPDATE word SET id = {0} WHERE id = {1}",
                _rulings[position].WordId,
                _db.WordAt(_hebrew, 1, position + 1, 1).Id);
        }

        Mark(_rulings.Count + 1, Gentilic, null);
        Mark(_rulings.Count + 2, Gentilic, "pers");
        Mark(_rulings.Count + 3, Unheld, null);
        Mark(_rulings.Count + 4, Judah, "pers,gens,topo");
        Mark(_rulings.Count + 5, Judah, "pers,gens,topo");
        Mark(_rulings.Count + 6, Judah, "pers,gens,topo");
        Mark(_rulings.Count + 7, Judah, "pers,gens,topo");

        _db.StrongEntries.AddRange(
            new StrongEntry
            {
                StrongNumber = Gentilic,
                Definition = "a Moabite or Moabitess, i.e. a descendant from Moab",
                KjvDefinition = "(woman) of Moab, Moabite(-ish, -ss).",
            },
            new StrongEntry
            {
                StrongNumber = Unheld,
                Definition = "a Pelishtite or inhabitant of Pelesheth",
                KjvDefinition = "Philistine.",
            });

        var moab = new Entity
        {
            Kind = EntityKind.Person, Slug = "moab", Name = "Moab",
            SourceId = "moab", Source = "a test",
        };
        _db.Entities.Add(moab);

        foreach (var origin in _file.Tribes.Select(t => t.Origin).Distinct())
        {
            _db.Entities.Add(new Entity
            {
                Kind = EntityKind.Person, Slug = origin, Name = origin,
                SourceId = origin, Source = "a test",
            });
        }

        _db.SaveChanges();

        _db.StrongGentilics.AddRange(
            new StrongGentilic
            {
                StrongNumber = Gentilic, OriginNumber = "H4124",
                Kind = GentilicKinds.Patronymic, OriginEntityId = moab.Id,
                Statement = "patronymical from מוֹאָב (H4124)", Source = "a dictionary",
            },
            new StrongGentilic
            {
                StrongNumber = Unheld, OriginNumber = "H6429",
                Kind = GentilicKinds.Patrial,
                Statement = "patrial from פְּלֶשֶׁת (H6429)", Source = "a dictionary",
            });

        _db.SaveChanges();
    }

    /// <summary>The tribal name, which does duty for the man, the people and the land at once.</summary>
    private const string Collective = "H1144";

    /// <summary>A gentilic whose origin the encyclopedia holds.</summary>
    private const string Gentilic = "H4125";

    /// <summary>A gentilic whose origin it does not.</summary>
    private const string Unheld = "H6430";

    /// <summary>The name that does duty for the man, the tribe, the kingdom and the land at once.</summary>
    private const string Judah = "H3063";

    /// <summary>Words beyond the ruled ones: two gentilics, an unheld one, and four of Judah.</summary>
    private const int Extra = 7;

    private void Mark(int verse, string number, string? nameType)
    {
        var word = _db.WordAt(_hebrew, 1, verse, 1);
        word.StrongNumber = number;
        word.Morphology = nameType is null
            ? JsonDocument.Parse("""{"pos": "subs"}""")
            : JsonDocument.Parse($$"""{"pos": "subs", "nameType": "{{nameType}}"}""");
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Database.ExecuteSqlRaw("DELETE FROM strong_gentilic");
        _db.Database.ExecuteSqlRaw("DELETE FROM strong_entry");
        _db.Dispose();
    }

    private Task<PeopleOutcome> Load() =>
        new PeopleLoader(_db, new ConfigurationBuilder().Build(), NullLogger<PeopleLoader>.Instance)
            .Load(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}"));

    [Fact]
    public async Task EveryTribeInTheFileBecomesARecordNamingItsAncestor()
    {
        await Load();

        foreach (var tribe in _file.Tribes)
        {
            var written = await _db.Entities
                .Include(e => e.Origin)
                .Include(e => e.Claims)
                .SingleAsync(e => e.Slug == tribe.Slug);

            written.Kind.Should().Be(EntityKind.People);
            written.Name.Should().Be(tribe.Name);
            written.Origin!.Slug.Should().Be(tribe.Origin);
            written.Source.Should().StartWith("Essenthos");
            written.SourceId.Should().StartWith("essenthos:");
            written.Claims.Should().ContainSingle()
                .Which.Should().Match<EntityClaim>(c =>
                    c.Method == LinkMethod.Manual && c.Confidence == null && c.Note == tribe.Why);
        }
    }

    /// <summary>
    /// A record made from a gentilic entry is ours and its claim is the dictionary's, quoted rather
    /// than paraphrased. That is the difference between citing a lexicon and asserting a genealogy.
    /// </summary>
    [Fact]
    public async Task APeopleFromTheDictionaryCarriesItsSentenceAndReachesItsAncestor()
    {
        await Load();

        var moabites = await _db.Entities
            .Include(e => e.Origin)
            .Include(e => e.Claims)
            .Include(e => e.Names)
            .SingleAsync(e => e.Slug == "moabites");

        moabites.Kind.Should().Be(EntityKind.People);
        moabites.Name.Should().Be("Moabites");
        moabites.Origin!.Slug.Should().Be("moab");
        moabites.Claims.Should().ContainSingle()
            .Which.Should().Match<EntityClaim>(c =>
                c.Method == LinkMethod.StatedBySource
                && c.Confidence == null
                && c.Note!.Contains("patronymical from"));
        moabites.Names.Should().ContainSingle(n => n.HebrewStrongNumber == Gentilic);
    }

    /// <summary>
    /// An origin the encyclopedia does not hold withholds the link and nothing else: the claim
    /// still says whom they are named after, in the dictionary's words.
    /// </summary>
    [Fact]
    public async Task APeopleWhoseAncestorIsNotHeldStillSaysWhoHeIs()
    {
        await Load();

        var philistines = await _db.Entities
            .Include(e => e.Claims)
            .SingleAsync(e => e.Slug == "philistines");

        philistines.OriginEntityId.Should().BeNull();
        philistines.Claims.Should().ContainSingle().Which.Note.Should().Contain("H6429");
    }

    /// <summary>
    /// The near end <c>strong_gentilic</c> was written without. A reader hovering the word reaches
    /// the people rather than a lexeme with nowhere to go.
    /// </summary>
    [Fact]
    public async Task EveryGentilicRowReachesThePeopleItDescribes()
    {
        await Load();

        var rows = await _db.StrongGentilics.Include(g => g.People).ToListAsync();

        rows.Should().OnlyContain(g => g.People != null);
        rows.Should().OnlyContain(g => g.People!.Kind == EntityKind.People);
    }

    /// <summary>
    /// A word carrying a gentilic <em>is</em> the people's name, so it resolves the way a proper
    /// noun does — and where BHSA marks the same lexeme as somebody's own name, it does not, because
    /// that occurrence is a man and not a nation.
    /// </summary>
    [Fact]
    public async Task AGentilicWordNamesThePeopleUnlessItIsSomebodysName()
    {
        await Load();

        var annotated = await _db.WordEntities
            .Include(a => a.Entity)
            .Where(a => a.Entity!.Slug == "moabites")
            .ToListAsync();

        annotated.Should().ContainSingle();
        annotated[0].WordId.Should().Be(_db.WordAt(_hebrew, 1, _rulings.Count + 1, 1).Id);
        annotated[0].Method.Should().Be(LinkMethod.StrongNumber);
        annotated[0].Confidence.Should().Be(0.9);
    }

    /// <summary>
    /// The eight the loader refused to write when there was no kind for them. They annotate the
    /// tribe, at no confidence, because a review decided them and a decision is not an inference.
    /// </summary>
    [Fact]
    public async Task TheRuledOccurrencesNameTheTribe()
    {
        await Load();

        var annotated = await _db.WordEntities
            .Include(a => a.Entity)
            .Where(a => a.Entity!.Slug == "benjaminites")
            .ToListAsync();

        annotated.Select(a => a.WordId).Should().BeEquivalentTo(_rulings.Select(r => r.WordId));
        annotated.Should().OnlyContain(a => a.Method == LinkMethod.Manual);
        annotated.Should().OnlyContain(a => a.Confidence == null);
    }

    /// <summary>
    /// The whole discipline of this layer, asserted as an absence. Eight hundred words carry the
    /// name of Judah and BHSA marks nearly all of them <c>pers,gens,topo</c> at once; a pass that
    /// read that marking as a decision would annotate every one of them to the tribe, which is a
    /// worse encyclopedia than none. Only an occurrence something individually read is annotated.
    /// </summary>
    [Fact]
    public async Task ACollectiveNameIsNotSweptUpBecauseBhsaMarkedIt()
    {
        await Load();

        var collectives = await _db.Words
            .Where(w => w.StrongNumber == Collective)
            .Select(w => w.Id)
            .ToListAsync();

        var annotated = await _db.WordEntities
            .Where(a => collectives.Contains(a.WordId))
            .Select(a => a.WordId)
            .ToListAsync();

        annotated.Should().BeEquivalentTo(_rulings.Select(r => r.WordId));
    }

    /// <summary>
    /// A people's verses are derived from the words this corpus annotated, and the source says so
    /// rather than naming a dataset that never made the claim.
    /// </summary>
    [Fact]
    public async Task APeoplesReferencesSayTheyAreOurs()
    {
        await Load();

        var references = await _db.EntityVerses
            .Include(v => v.Entity)
            .Where(v => v.Entity!.Kind == EntityKind.People)
            .ToListAsync();

        references.Should().NotBeEmpty();
        references.Should().OnlyContain(v => v.Source.StartsWith("Essenthos"));
        references.Select(v => v.Entity!.Slug).Should().Contain(["moabites", "benjaminites"]);
    }

    /// <summary>
    /// The readings a model gave for the collective, which is the demand this layer answers and the
    /// place a heuristic decides something a reader will take for scholarship.
    ///
    /// Four occurrences of the one name: the tribe, the kingdom, a re-asked answer whose description
    /// is gone and whose reason says the same thing, and one in a band nobody has measured. Only the
    /// first and the third are annotated, and the other two are counted so the size of what is being
    /// left alone is visible rather than implied.
    /// </summary>
    [Fact]
    public async Task OnlyAReadingThatNamesAPeopleAndNoTerritoryIsAnnotated()
    {
        var resources = Directory.CreateTempSubdirectory("peoples").FullName;
        var run = Directory.CreateDirectory(
            Path.Combine(resources, SenseReadingFiles.DefaultFolder, "run"));

        var words = Enumerable
            .Range(4, 4)
            .Select(offset => _db.WordAt(_hebrew, 1, _rulings.Count + offset, 1).Id)
            .ToList();

        await File.WriteAllLinesAsync(
            Path.Combine(run.FullName, SenseReadingFiles.AnswersFileName),
            [
                Answer(words[0], "the tribe of Judah", "Census of the tribe.", "high"),
                Answer(words[1], "the kingdom of Judah", "Asa reigned over it.", "high"),
                Answer(words[2], null, "'Camp of Judah' names the tribal division, not the patriarch.", "high"),
                Answer(words[3], "the tribe of Judah", "Nothing decides it.", "low"),
            ]);

        try
        {
            var outcome = await new PeopleLoader(
                    _db, new ConfigurationBuilder().Build(), NullLogger<PeopleLoader>.Instance)
                .Load(resources);

            outcome.Read.Should().Be(2);
            outcome.Undecided.Should().Be(2);

            var annotated = await _db.WordEntities
                .Include(a => a.Entity)
                .Where(a => a.Entity!.Slug == "judahites")
                .ToListAsync();

            annotated.Select(a => a.WordId).Should().BeEquivalentTo([words[0], words[2]]);
            annotated.Should().OnlyContain(a => a.Method == LinkMethod.ModelReading);
            annotated.Should().OnlyContain(a => a.Confidence == 0.99);
        }
        finally
        {
            Directory.Delete(resources, recursive: true);
        }
    }

    private static string Answer(long word, string? names, string reason, string confidence) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["word_id"] = word,
            ["strong_number"] = Judah,
            ["referent"] = SenseReading.Unlisted,
            ["names"] = names,
            ["confidence"] = confidence,
            ["reason"] = reason,
            ["prompt_version"] = "sense-1",
            ["model"] = "a-model",
            ["run"] = "2026-09-06T00:00:00+00:00",
        });

    /// <summary>
    /// A people never becomes a candidate for a word BHSA marks as a person's name or a place's.
    ///
    /// It could never be the answer to that question, so the only thing its presence could do is
    /// take a number's candidate count from one to two — and everything downstream annotates only
    /// the numbers that answer with exactly one. Measured against the live corpus before this was
    /// guarded, nineteen numbers would have stopped resolving and seventeen Hebrew annotations that
    /// are right would have quietly gone away.
    /// </summary>
    [Fact]
    public async Task APeopleIsNeverACandidateForAProperNoun()
    {
        await Load();

        var peoples = await _db.Entities
            .Where(e => e.Kind == EntityKind.People)
            .Select(e => e.Id)
            .ToListAsync();

        var candidates = await _db.Database
            .SqlQueryRaw<int>(
                $"SELECT entity_id AS \"Value\" FROM ({EntityCandidates.Naming}) named",
                new Npgsql.NpgsqlParameter("witness", EntityCandidates.Witness),
                new Npgsql.NpgsqlParameter("rendering", EntityCandidates.Rendering))
            .ToListAsync();

        peoples.Should().NotBeEmpty();
        candidates.Should().NotIntersectWith(peoples);
    }

    /// <summary>
    /// Run twice, the second run writes nothing. The pipeline re-runs on every boot and a pass that
    /// did not check would double the encyclopedia's newest layer each time.
    /// </summary>
    [Fact]
    public async Task LoadingTwiceWritesNothingTheSecondTime()
    {
        var first = await Load();
        var second = await Load();

        first.AlreadyLoaded.Should().BeFalse();
        second.AlreadyLoaded.Should().BeTrue();
        second.Annotated.Should().Be(0);
    }
}
