using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>A title before a name is read past, so the person it names is still found.</summary>
public sealed class EntitySearchTitleTests
{
    [Theory]
    [InlineData("цар Давид", "Давид")]
    [InlineData("Цар Давид", "Давид")]
    [InlineData("царя Давида", "Давида")]
    [InlineData("пророк Ілля", "Ілля")]
    [InlineData("святий апостол Павло", "Павло")]
    [InlineData("св. Павло", "Павло")]
    [InlineData("царь Давид", "Давид")]
    [InlineData("King David", "David")]
    [InlineData("the prophet Elijah", "Elijah")]
    [InlineData("high priest Aaron", "Aaron")]
    [InlineData("König David", "David")]
    [InlineData("el rey David", "David")]
    public void ATitleInFrontOfTheNameIsTakenOff(string typed, string name) =>
        TitleWords.Without(typed).Should().Be(name);

    [Theory]
    [InlineData("Давид")]
    [InlineData("цар")]
    [InlineData("King")]
    [InlineData("Kingdom of David")]
    [InlineData("Ісус Христос")]
    public void ANameWithNoTitleInFrontOfItIsLeftAlone(string typed) =>
        TitleWords.Without(typed).Should().BeNull();
}

/// <summary>
/// What a search of the encyclopedia finds, in which order, and what it says it found a row by when
/// the row's heading does not show it.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EntitySearchTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Entity _david;
    private readonly Entity _jesus;
    private readonly Entity _god;
    private readonly Entity _abram;
    private readonly Entity _barnabas;
    private readonly Entity _paul;
    private readonly Entity _elijah;
    private readonly Entity _cityOfDavid;

    public EntitySearchTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        var ohienko = Corpus.Add(_db, "UBIO", TextKind.Translation, "ukr", (1, 1, ["слово"]));

        _david = Record("david", "David", ("David", BibleDataLoader.LabelKind), ("King David", "title"), ("King of Israel", "title"));
        _jesus = Record("jesus", "Jesus", ("Jesus", BibleDataLoader.LabelKind), ("Son of David", "title"), ("Son of Abraham", "title"));
        _god = Record("yhvh", "YHVH", ("YHVH", BibleDataLoader.LabelKind), ("God of Abraham", "title"));
        _abram = Record("abram", "Abram", ("Abram", BibleDataLoader.LabelKind), ("Abraham", BibleDataLoader.LabelKind));
        _barnabas = Record("joseph-12", "Joseph", ("Joseph", BibleDataLoader.LabelKind), ("Barnabas", BibleDataLoader.LabelKind));
        _paul = Record("saul-2", "Saul", ("Saul", BibleDataLoader.LabelKind), ("Paul", BibleDataLoader.LabelKind));
        _elijah = Record("elijah", "Elijah", ("Elijah", BibleDataLoader.LabelKind), ("Elijah the Tishbite", BibleDataLoader.LabelKind));
        _cityOfDavid = Record("cityofdavid", "City of David", ("City of David", BibleDataLoader.LabelKind));
        _db.SaveChanges();

        Nominative(_david, "Давид");
        Nominative(_jesus, "Ісус");
        Nominative(_god, "Господь");
        Nominative(_abram, "Аврам");
        Nominative(_barnabas, "Йосип");
        Nominative(_paul, "Савл");
        Nominative(_elijah, "Ілля");

        Printed(ohienko, _barnabas, "Варнава", 24, heading: true);
        Printed(ohienko, _barnabas, "Варнави", 4);
        Printed(ohienko, _barnabas, "Йосип", 1);
        Printed(ohienko, _barnabas, "Йосипа", 1);
        Printed(ohienko, _paul, "Павло", 120, heading: true);
        Printed(ohienko, _paul, "Павла", 40);
        Printed(ohienko, _abram, "Авраам", 90);
        Printed(ohienko, _abram, "Авраама", 50);
        Printed(ohienko, _abram, "Аврам", 28, heading: true);
        Printed(ohienko, _abram, "Аврама", 12);
        Printed(ohienko, _paul, "Савл", 15);
        Printed(ohienko, _david, "Давид", 800, heading: true);
        Printed(ohienko, _david, "Давида", 200);
        Printed(ohienko, _elijah, "Ілля", 60, heading: true);

        // The God of Israel and Jesus are named far more often than Abram or David, which is what a
        // count alone would head a search with.
        Named(_god, 40);
        Named(_jesus, 30);
        Named(_david, 20);
        Named(_cityOfDavid, 5);
        Named(_abram, 3);

        // Each verse the dataset lists says which name it uses: Paul far more often than Saul,
        // Barnabas and never Joseph, Abraham more than Abram; David by his one name.
        Named(_paul, 6, "Paul", from: 100);
        Named(_paul, 2, "Saul", from: 200);
        Named(_paul, 1, "they", from: 300);
        Named(_barnabas, 3, "Barnabas", from: 100);
        Named(_abram, 4, "Abraham", from: 100);
        Named(_abram, 2, "Abram", from: 200);
        Named(_abram, 9, "Abraham our father", from: 300);
        Named(_david, 3, "David", from: 100);
        Named(_jesus, 5, "Son of David", from: 100);
        Named(_elijah, 4, "Elijah the Tishbite", from: 100);
        Named(_elijah, 1, "Elijah", from: 200);
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

    private Entity Record(string slug, string name, params (string Label, string Kind)[] labels)
    {
        var entity = new Entity
        {
            Kind = slug == "cityofdavid" ? EntityKind.Place : EntityKind.Person,
            Slug = slug, Name = name, SourceId = slug, Source = "a test",
        };
        foreach (var (label, kind) in labels)
        {
            entity.Names.Add(new EntityName { Label = label, Kind = kind });
        }

        _db.Entities.Add(entity);
        return entity;
    }

    private void Nominative(Entity entity, string form) =>
        _db.EntityNameForms.Add(new EntityNameForm
        {
            EntityId = entity.Id, Language = "ukr", GrammaticalCase = GrammaticalCases.Nominative,
            Form = form, Method = LinkMethod.ModelReading, Confidence = 1, Source = "a test",
        });

    private void Printed(Text text, Entity entity, string form, int occurrences, bool heading = false) =>
        _db.EntityRenderings.Add(new EntityRendering
        {
            EntityId = entity.Id, TextId = text.Id, Form = form, Folded = NameFolding.Fold(form),
            Occurrences = occurrences, Heading = heading,
        });

    private void Named(Entity entity, int verses, string? label = null, int from = 1)
    {
        for (var verse = from; verse < from + verses; verse++)
        {
            _db.EntityVerses.Add(new EntityVerse
            {
                EntityId = entity.Id, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = verse,
                Label = label, Source = BibleDataLoader.Source,
            });
        }
    }

    private async Task<List<string>> Found(string typed) =>
        await EntityNames.Matching(_db, _db.Entities, typed).Select(e => e.Slug).ToListAsync();

    [Theory]
    [InlineData("цар Давид", "david")]
    [InlineData("царя Давида", "david")]
    [InlineData("пророк Ілля", "elijah")]
    [InlineData("апостол Павло", "saul-2")]
    [InlineData("King David", "david")]
    [InlineData("king Saul", "saul-2")]
    public async Task APersonIsFoundWithATitleInFrontOfTheName(string typed, string slug) =>
        (await Found(typed)).Should().Contain(slug);

    [Theory]
    [InlineData("Давид", "david")]
    [InlineData("Ілля", "elijah")]
    [InlineData("Варнава", "joseph-12")]
    public async Task TheBareNameStillFindsThePerson(string typed, string slug) =>
        (await Found(typed)).Should().Contain(slug);

    /// <summary>
    /// Go To orders what it finds by how often each is named. Jesus and the God of Israel are named
    /// more often than Abram and carry <em>Abraham</em> only inside a title, so Abram heads.
    /// </summary>
    [Theory]
    [InlineData("verses", "Abraham", "abram")]
    [InlineData("mentions", "Abraham", "abram")]
    [InlineData(null, "Abraham", "abram")]
    [InlineData("verses", "David", "david")]
    [InlineData("verses", "цар Давид", "david")]
    public async Task APersonCalledWhatWasTypedHeadsThoseWhoseTitleHoldsIt(string? sort, string typed, string first)
    {
        var ranked = await Ranked(sort, typed);

        ranked[0].Should().Be(first);
    }

    [Fact]
    public async Task ATitleMatchComesAfterANameThatHoldsWhatWasTyped()
    {
        var ranked = await Ranked("verses", "David");

        ranked.Should().Equal("david", "cityofdavid", "jesus");
    }

    private async Task<List<string>> Ranked(string? sort, string typed)
    {
        var matched = await EntityNames.Matching(_db, _db.Entities, typed).Select(e => e.Id).ToListAsync();
        return await EncyclopediaEndpoints
            .Ordered(_db, EntityNames.Localised(_db, _db.Entities.Where(e => matched.Contains(e.Id)), "ukr"), sort, typed, "ukr")
            .Select(l => l.Entity.Slug)
            .ToListAsync();
    }

    private async Task<Dictionary<string, EntityMatchResponse>> FoundAs(string typed)
    {
        var matched = await EntityNames.Matching(_db, _db.Entities, typed).Select(e => e.Id).ToListAsync();
        var shown = await EntityNames.Localised(_db, _db.Entities.Where(e => matched.Contains(e.Id)), "ukr")
            .ToDictionaryAsync(l => l.Entity.Id, l => l.Shown);
        var renamed = await RenamedHeadings.Of(_db, shown.Keys, "ukr", CancellationToken.None);
        foreach (var (id, heads) in renamed)
        {
            shown[id] = heads.LocalName ?? heads.Name;
        }

        var slugs = await _db.Entities.ToDictionaryAsync(e => e.Id, e => e.Slug);
        return (await EntityNames.FoundAs(_db, shown, typed, CancellationToken.None, renamed))
            .ToDictionary(pair => slugs[pair.Key], pair => pair.Value);
    }

    [Fact]
    public async Task ARowFoundByAnotherNameSaysWhichInTheReadersText()
    {
        (await FoundAs("Савл"))["saul-2"].Should().Be(new EntityMatchResponse("Савл", MatchKinds.Name));
        (await FoundAs("Аврам"))["abram"].Should().Be(new EntityMatchResponse("Аврам", MatchKinds.Name));
    }

    [Fact]
    public async Task ARowFoundByAnotherNameOfTheSourceSaysWhich()
    {
        (await FoundAs("Joseph"))["joseph-12"].Should().Be(new EntityMatchResponse("Joseph", MatchKinds.Name));
        (await FoundAs("Saul"))["saul-2"].Should().Be(new EntityMatchResponse("Saul", MatchKinds.Name));
        (await FoundAs("Abram"))["abram"].Should().Be(new EntityMatchResponse("Abram", MatchKinds.Name));
    }

    [Theory]
    [InlineData("Варнава", "joseph-12")]
    [InlineData("Barnabas", "joseph-12")]
    [InlineData("Paul", "saul-2")]
    [InlineData("Павло", "saul-2")]
    [InlineData("Павла", "saul-2")]
    [InlineData("Abraham", "abram")]
    [InlineData("Авраама", "abram")]
    public async Task ARowFoundByTheNameItIsHeadedWithSaysNothing(string typed, string slug) =>
        (await FoundAs(typed)).Should().NotContainKey(slug);

    [Fact]
    public async Task ARowFoundOnlyThroughATitleSaysTheTitle()
    {
        var found = await FoundAs("David");

        found["jesus"].Should().Be(new EntityMatchResponse("Son of David", MatchKinds.Title));
        found.Should().NotContainKey("david");
    }

    [Theory]
    [InlineData("Давид")]
    [InlineData("Давида")]
    [InlineData("David")]
    [InlineData("цар Давид")]
    [InlineData("King David")]
    public async Task ARowWhoseHeadingHoldsWhatWasTypedSaysNothing(string typed) =>
        (await FoundAs(typed)).Should().NotContainKey("david");

    /// <summary>
    /// Paul is named in more verses than Saul, Barnabas than Joseph, Abraham than Abram, so each is
    /// headed by that name — in Ukrainian as the Ohienko Bible prints it — and told to be also called
    /// the name the record is filed under. David has one name, and Abraham's titles are not names.
    /// </summary>
    [Fact]
    public async Task ARenamedPersonIsHeadedByTheNameTheTextUsesMost()
    {
        var ukrainian = await RenamedHeadings.Of(
            _db, [_paul.Id, _barnabas.Id, _abram.Id, _david.Id, _jesus.Id], "ukr", CancellationToken.None);
        var english = await RenamedHeadings.Of(_db, [_paul.Id, _barnabas.Id, _abram.Id], "eng", CancellationToken.None);

        ukrainian.Should().BeEquivalentTo(new Dictionary<int, RenamedHeading>
        {
            [_paul.Id] = new("Paul", "Павло", "Saul", "Савл"),
            [_barnabas.Id] = new("Barnabas", "Варнава", "Joseph", "Йосип"),
            [_abram.Id] = new("Abraham", "Авраам", "Abram", "Аврам"),
        });
        english[_paul.Id].Should().Be(new RenamedHeading("Paul", null, "Saul", null));
        english[_paul.Id].AlsoCalled.Should().Be("Saul");
        ukrainian[_paul.Id].AlsoCalled.Should().Be("Савл");
        english[_abram.Id].Should().Be(new RenamedHeading("Abraham", null, "Abram", null));
    }

    /// <summary><em>Elijah the Tishbite</em> says which Elijah; it does not rename him.</summary>
    [Fact]
    public async Task ANameWithAnEpithetIsNotARenaming() =>
        (await RenamedHeadings.Of(_db, [_elijah.Id], "eng", CancellationToken.None)).Should().BeEmpty();

    [Fact]
    public void ADeclinedSpellingIsTheNameItDeclines()
    {
        EntityNames.SameName("павло", "павла").Should().BeTrue();
        EntityNames.SameName("йосип", "йосипові").Should().BeTrue();
        EntityNames.SameName("ілля", "іллі").Should().BeTrue();
        EntityNames.SameName("аврам", "авраам").Should().BeFalse();
        EntityNames.SameName("йосип", "варнава").Should().BeFalse();
    }
}
