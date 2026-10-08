using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The place layer with both its sources in it.
///
/// The numbers here are the measure that chose this dataset: BibleData states 492 verses and stops
/// after Exodus, and this states 8,742 references across 61 books over the same entries. They are
/// asserted exactly, because the point of loading a second source is a coverage claim and a
/// coverage claim that drifts silently is the defect this whole layer exists to answer.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OpenBiblePlaceLoadTests : IClassFixture<OpenBiblePlaceLoadTests.Places>, IDisposable
{
    private const string OpenBible =
        "OpenBible.info Bible Geocoding, github.com/openbibleinfo/Bible-Geocoding-Data, CC BY 4.0";

    private const string BibleData =
        "BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0";

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly PlacesOutcome _outcome;

    public OpenBiblePlaceLoadTests(WitnessDatabase database, Places places)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _outcome = places.Outcome;
    }

    public sealed class Places(WitnessDatabase database) : SeededDatabase(database)
    {
        internal PlacesOutcome Outcome { get; private set; } = null!;

        protected override async Task Seed(AppDbContext db)
        {
            var bibleData = Path.GetDirectoryName(TestResources.Path("BibleData2026", "BibleData-Person.csv"))!;
            await new BibleDataLoader(db, NullLogger<BibleDataLoader>.Instance).Load(bibleData);

            Outcome = await new OpenBiblePlaceLoader(db, NullLogger<OpenBiblePlaceLoader>.Instance)
                .Load(TestResources.OpenBibleFolder);
        }
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    /// <summary>
    /// 110 of the other dataset's places carry an identifier from this one and 109 of them join.
    /// The one that does not is Shiba, whose identifier is a modern location rather than an ancient
    /// place — the two id spaces are separate and that row names the wrong one — so it stays its
    /// own entry rather than being joined to whatever happens to share the prefix.
    /// </summary>
    [Fact]
    public void EveryPlaceInTheSourceIsEitherJoinedOrAdded()
    {
        _outcome.AlreadyLoaded.Should().BeFalse();
        _outcome.Places.Should().Be(1_342);
        _outcome.Joined.Should().Be(109);
        _outcome.Added.Should().Be(1_233);
        _outcome.References.Should().Be(8_742);
        _outcome.Unaddressed.Should().Be(0);
    }

    /// <summary>
    /// The reason the layer was unusable. The other dataset knows Jerusalem under one label, in
    /// Genesis 14:18, and its place-verse file never reaches a second.
    /// </summary>
    [Fact]
    public async Task JerusalemIsNamedThroughTheCanonAndNotOnceInGenesis()
    {
        var jerusalem = await _db.Entities
            .Include(e => e.Verses)
            .SingleAsync(e => e.Slug == "jerusalem");

        jerusalem.Verses.Should().HaveCount(956);
        jerusalem.Verses.Count(v => v.Source == OpenBible).Should().Be(955);
        jerusalem.Verses.Count(v => v.Source == BibleData).Should().Be(1);
        jerusalem.Verses.Select(v => v.CanonicalBook).Distinct().Should().HaveCountGreaterThan(20);
    }

    /// <summary>
    /// A place both datasets know is one entry, reached by the identifier the first already
    /// carried — not a second Jerusalem beside the first.
    /// </summary>
    [Fact]
    public async Task APlaceBothDatasetsKnowStaysOneEntity()
    {
        var jerusalems = await _db.Entities.CountAsync(e => e.Name == "Jerusalem");
        var joined = await _db.Entities
            .SingleAsync(e => e.Kind == EntityKind.Place && e.OpenBibleId == "a15257a");

        jerusalems.Should().Be(1);
        joined.Source.Should().Be(BibleData);
        joined.SourceId.Should().Be("place:Jerusalem_1");
    }

    [Fact]
    public async Task EveryReferenceSaysWhichDatasetStatedIt()
    {
        var unattributed = await _db.EntityVerses.CountAsync(v => v.Source == string.Empty);
        var sources = await _db.EntityVerses
            .Where(v => v.Entity!.Kind == EntityKind.Place)
            .Select(v => v.Source)
            .Distinct()
            .ToListAsync();

        unattributed.Should().Be(0);
        sources.Should().BeEquivalentTo([BibleData, OpenBible]);
    }

    /// <summary>
    /// BibleData's 674 place references stay in the table and are not among the sources a reader is
    /// shown the layer by.
    /// </summary>
    [Fact]
    public async Task TheCoverageOfTheLayerIsReportedPerSourceAsWellAsWhole()
    {
        var places = (await EncyclopediaEndpoints.Coverage(_db)).Layers.Single(l => l.Kind == "place");
        var openBible = places.Sources.Single(s => s.Dataset == "openbible");

        places.Books.Books.Should().HaveCount(61);
        openBible.Books.Books.Should().HaveCount(61);
        openBible.Mentions.Should().Be(8_742);
        places.Sources.Should().NotContain(s => s.Dataset == "bibledata");
        (await _db.EntityVerses.CountAsync(v => v.Source == BibleData && v.Entity!.Kind == EntityKind.Place))
            .Should().Be(674);
    }

    /// <summary>
    /// The source's trailing index is its own catalogue number and not part of the name, and what
    /// tells two places of one name apart is where the scholarship puts each of them.
    /// </summary>
    [Fact]
    public async Task PlacesSharingANameAreToldApartByTheirIdentification()
    {
        var aroers = await _db.Entities
            .Where(e => e.Kind == EntityKind.Place && e.Name == "Aroer")
            .ToListAsync();

        aroers.Should().HaveCountGreaterThan(1);
        aroers.Should().OnlyContain(e => e.Distinguisher != null && e.Distinguisher.Length > 0);
        aroers.Select(e => e.Slug).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task LoadingTwiceAddsNothing()
    {
        var before = await _db.EntityVerses.CountAsync();

        var again = await new OpenBiblePlaceLoader(_db, NullLogger<OpenBiblePlaceLoader>.Instance)
            .Load(TestResources.OpenBibleFolder);

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.EntityVerses.CountAsync()).Should().Be(before);
    }

    /// <summary>
    /// The 289 entries whose first line says they are another name for another place are tied to that
    /// place by the id the source writes inside the phrase, and none is left telling a reader that the
    /// modern equivalent of a place is another place's name.
    /// </summary>
    [Fact]
    public async Task APlaceTheGazetteerCallsAnotherNameForAnotherIsTiedToThatPlace()
    {
        var aliases = OpenBiblePlaceLoader.Read(TestResources.Path("OpenBible", "ancient.jsonl"))
            .Where(p => p.AnotherNameFor is not null)
            .ToList();
        var held = await _db.Entities
            .Where(e => e.Kind == EntityKind.Place && e.OpenBibleId != null)
            .ToDictionaryAsync(e => e.OpenBibleId!);

        aliases.Should().HaveCount(289);
        foreach (var alias in aliases)
        {
            held[alias.Id].AnotherNameForEntityId.Should().Be(held[alias.AnotherNameFor!].Id, $"{alias.Name} says so");
        }

        (await _db.Entities.CountAsync(e => e.ModernEquivalent!.StartsWith("another name for"))).Should().Be(0);
        (await _db.Entities.Where(e => e.Distinguisher!.StartsWith("another name for")).Select(e => e.Distinguisher!).ToListAsync())
            .Should().NotContain(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"\d$"),
                "the phrase names the place, and not the source's index of it");
        OpenBiblePlaceLoader.Site(held[aliases[0].Id]).Should().BeNull("an alias claims no site of its own");
    }

    [Fact]
    public async Task AWarmCorpusIsGivenTheAliasesAndFreedOfThePhraseOnceAndThenLeftAlone()
    {
        var alias = OpenBiblePlaceLoader.Read(TestResources.Path("OpenBible", "ancient.jsonl"))
            .First(p => p.AnotherNameFor is not null && p.Identification is not null);
        var entity = await _db.Entities.SingleAsync(e => e.OpenBibleId == alias.Id);
        var target = await _db.Entities.SingleAsync(e => e.OpenBibleId == alias.AnotherNameFor);
        entity.AnotherNameForEntityId = null;
        entity.ModernEquivalent = "another name for Somewhere 1";
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await new OpenBiblePlaceLoader(_db, NullLogger<OpenBiblePlaceLoader>.Instance).Load(TestResources.OpenBibleFolder);

        var corrected = await _db.Entities.AsNoTracking().SingleAsync(e => e.Id == entity.Id);
        corrected.AnotherNameForEntityId.Should().Be(target.Id);
        corrected.ModernEquivalent.Should().Be(alias.Identification == alias.Name ? null : alias.Identification);

        var before = await _db.Entities.AsNoTracking().OrderBy(e => e.Id)
            .Select(e => new { e.Id, e.AnotherNameForEntityId, e.ModernEquivalent, e.Distinguisher }).ToListAsync();
        await new OpenBiblePlaceLoader(_db, NullLogger<OpenBiblePlaceLoader>.Instance).Load(TestResources.OpenBibleFolder);
        (await _db.Entities.AsNoTracking().OrderBy(e => e.Id)
            .Select(e => new { e.Id, e.AnotherNameForEntityId, e.ModernEquivalent, e.Distinguisher }).ToListAsync())
            .Should().BeEquivalentTo(before, options => options.WithStrictOrdering());
    }

    /// <summary>The King James's Tyrus and Zidon reach Tyre and Sidon, credited to the source that gives them.</summary>
    [Fact]
    public async Task TheSpellingsTheTranslationsPrintAreNamesOfThePlace()
    {
        var spelt = await _db.EntityNames
            .Where(n => n.Kind == OpenBiblePlaceLoader.SpellingKind && (n.Label == "Tyrus" || n.Label == "Zidon"))
            .Select(n => new { n.Entity!.Name, n.Source })
            .ToListAsync();

        spelt.Select(n => n.Name).Should().Contain(["Tyre", "Sidon"]);
        spelt.Should().OnlyContain(n => n.Source == OpenBible);
        (await _db.EntityNames.AnyAsync(n => n.Kind == OpenBiblePlaceLoader.SpellingKind && n.Label == "Judah"))
            .Should().BeFalse("Judah is another record's name");
    }

    [Fact]
    public void ASpellingIsANameOfThePlaceAndAGentilicIsNot() =>
        OpenBiblePlaceLoader.Spellings(["Tyre", "Tyrus", "Tyrians", "Tyrian", "Palestine", " "], "Tyre")
            .Should().Equal("Tyrus", "Palestine");
}
