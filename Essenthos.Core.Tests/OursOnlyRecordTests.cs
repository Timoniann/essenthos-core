using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A record BibleData supplied and this project has read for itself — a verse of ours or a relationship
/// of ours stands on it — is ours: it loses the dataset's line and notes, its line is its clauses or the
/// line we wrote for it, and its sex and tribe are what our own rows say and nothing the dataset says.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OursOnlyRecordTests : IDisposable
{
    private const string OurVerse = "Essenthos, from the words this corpus annotates to the person or the place they name";

    private const string OurSource = "read from Scripture by the project owner, decided 2026-09-30";

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public OursOnlyRecordTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task ARecordWithADescriptorAndAVerseOfOursIsOursAndItsSexIsWhatOurClauseSays()
    {
        var (zebedee, james) = (Supplied("zebedee", "female", "Levi"), Person("james"));
        await _db.SaveChangesAsync();
        Clause(zebedee, "father-of", james);
        Verse(zebedee, OurVerse);
        await _db.SaveChangesAsync();

        var ours = await OursOnlyRecords.Among(_db, ["zebedee", "james"], default);

        ours.Should().ContainKey("zebedee").WhoseValue.Should().Be(new OursOnlyRecords.OwnFacts("male", null));
        ours.Should().NotContainKey("james");
    }

    [Fact]
    public async Task ARecordWhoseOnlyVersesAndTiesAreTheDatasetsStaysTheDatasets()
    {
        var (bare, target) = (Supplied("bare", "male", null), Person("target"));
        await _db.SaveChangesAsync();
        Clause(bare, "father-of", target);
        Verse(bare, BibleDataLoader.Source);
        Tie(bare, "father-of", target, BibleDataLoader.Source);
        await _db.SaveChangesAsync();

        (await OursOnlyRecords.Among(_db, ["bare"], default)).Should().BeEmpty();
    }

    /// <summary>
    /// A verse of ours is enough without a clause, and so is a relationship of ours without a verse,
    /// from either end of it; neither shows the dataset's line.
    /// </summary>
    [Fact]
    public async Task AVerseOfOursOrARelationshipOfOursMakesTheRecordOurs()
    {
        var (mute, son, father) = (Supplied("mute", "male", null), Supplied("son", "male", null), Supplied("father", null, null));
        await _db.SaveChangesAsync();
        Verse(mute, OurVerse);
        Tie(son, "son-of", father, OurSource);
        await _db.SaveChangesAsync();

        var ours = await OursOnlyRecords.Among(_db, ["mute", "son", "father"], default);

        ours.Keys.Should().BeEquivalentTo(["mute", "son", "father"]);
        ours.Values.Should().OnlyContain(facts => facts.Line == null, "no line of ours was written for any of them");
        OursOnlyRecords.Line(ours, "son", "a line only BibleData states").Should().BeNull();
    }

    /// <summary>
    /// The line we wrote is the record's line only where it is not described by clauses, and only while
    /// its English is the line this project rendered; the dataset's English is never ours.
    /// </summary>
    [Fact]
    public async Task OurOwnLineIsTheLineOfARecordWithNoClause()
    {
        var (lined, described, unrendered, target) = (
            Supplied("lined", "male", null), Supplied("described", "male", null), Supplied("unrendered", "male", null),
            Person("target"));
        lined.Distinguisher = "took Kenath and called it Nobah after his own name (NUM 32:42)";
        described.Distinguisher = lined.Distinguisher;
        await _db.SaveChangesAsync();
        foreach (var record in new[] { lined, described, unrendered })
        {
            Verse(record, OurVerse);
            Rendered(record, record.Distinguisher!);
        }

        Clause(described, "son-of", target);
        unrendered.Distinguisher = "a line only BibleData states, changed since";
        await _db.SaveChangesAsync();

        var ours = await OursOnlyRecords.Among(_db, ["lined", "described", "unrendered"], default);

        ours["lined"].Line.Should().Be(lined.Distinguisher);
        ours["described"].Line.Should().BeNull("its clauses are its line");
        ours["unrendered"].Line.Should().BeNull("its English is not the line this project rendered");
        OursOnlyRecords.Line(ours, "lined", lined.Distinguisher).Should().Be(lined.Distinguisher);
    }

    [Fact]
    public async Task OurRowsThatDisagreeAboutSexLeaveItEmptyAndNothingFallsBackToTheDataset()
    {
        var (odd, target) = (Supplied("odd", "male", null), Person("target"));
        await _db.SaveChangesAsync();
        Clause(odd, "son-of", target);
        Clause(odd, "mother-of", target);
        Verse(odd, OurVerse);
        await _db.SaveChangesAsync();

        (await OursOnlyRecords.Among(_db, ["odd"], default)).Single().Value.Sex.Should().BeNull();
    }

    /// <summary>Levi is a tribe because a clause of ours says someone is of it; the Levites are Levi's descendants.</summary>
    [Fact]
    public async Task TheTribeIsThePatriarchOfThePeopleOursSaysTheyAreOfAndEmptyWhereTwoDiffer()
    {
        var (aaron, ehud, both) = (Supplied("aaron", "male", "Judah"), Supplied("ehud", "male", null), Supplied("both", "male", null));
        var (levi, judah) = (Person("levi"), Person("judah"));
        var levites = Person("levites", EntityKind.People);
        await _db.SaveChangesAsync();
        Clause(ehud, "of-tribe", levi);
        Clause(aaron, "of-tribe", levites);
        Clause(levites, "descendants-of", levi);
        Clause(both, "of-tribe", levi);
        Clause(both, "of-tribe", judah);
        foreach (var record in new[] { aaron, ehud, both })
        {
            Verse(record, OurVerse);
        }

        await _db.SaveChangesAsync();

        var ours = await OursOnlyRecords.Among(_db, ["aaron", "ehud", "both"], default);

        ours["aaron"].Tribe.Should().Be("Levi", "ours says the Levites, not the dataset's Judah");
        ours["ehud"].Tribe.Should().Be("Levi");
        ours["both"].Tribe.Should().BeNull();
    }

    /// <summary>
    /// Uri is Levi's grandson by the relationships, so the tribe is Levi's and not the one the dataset's
    /// record gives him; a record nothing ties to a tribe has none whatever the dataset's record says,
    /// a line that reaches two tribes gives none, and a loop of rows ends.
    /// </summary>
    [Fact]
    public async Task TheTribeFollowsOurLinesOfDescentUpToAPatriarchAndOnlyWhenItIsOne()
    {
        var (uri, kohath, mixed, byDataset, loop) = (
            Supplied("uri", "male", "Judah"), Person("kohath"), Supplied("mixed", "male", null),
            Supplied("by-dataset", "male", "Levi"), Supplied("loop", "male", null));
        var (levi, judah, elder, other, wife, bystander) = (
            Person("levi"), Person("judah"), Person("elder"), Person("other"), Person("wife"), Person("bystander"));
        await _db.SaveChangesAsync();
        Clause(bystander, "of-tribe", levi);
        Clause(bystander, "of-tribe", judah);
        foreach (var record in new[] { uri, mixed, byDataset, loop })
        {
            Clause(record, "brother-of", bystander);
            Verse(record, OurVerse);
        }

        Tie(uri, "son-of", kohath, OurSource);
        Tie(kohath, "descendant-of", levi, OurSource);
        Tie(mixed, "son-of", elder, OurSource);
        Tie(elder, "son-of", levi, OurSource);
        Tie(mixed, "son-of", other, OurSource);
        Tie(other, "son-of", judah, OurSource);
        Tie(loop, "son-of", wife, OurSource);
        Tie(wife, "son-of", loop, OurSource);
        await _db.SaveChangesAsync();

        var ours = await OursOnlyRecords.Among(_db, ["uri", "mixed", "by-dataset", "loop"], default);

        ours["uri"].Tribe.Should().Be("Levi", "ours puts Uri under Levi; BibleData's Judah is not used");
        ours["mixed"].Tribe.Should().BeNull("two tribes are reached");
        ours["by-dataset"].Tribe.Should().BeNull("only BibleData's record puts him under Levi");
        ours["loop"].Tribe.Should().BeNull();
    }

    /// <summary>Where the two sides reach two tribes the father's line decides, and only a father ours says is male.</summary>
    [Fact]
    public async Task WhereTheLinesReachTwoTribesTheFathersLineDecides()
    {
        var (child, orphan) = (Supplied("child", "male", null), Supplied("orphan", "male", null));
        var (dad, mum, unknown, levi, judah, bystander) = (
            Person("dad"), Person("mum"), Person("unknown"), Person("levi"), Person("judah"), Person("bystander"));
        await _db.SaveChangesAsync();
        Clause(bystander, "of-tribe", levi);
        Clause(bystander, "of-tribe", judah);
        Clause(dad, "father-of", bystander);
        Clause(mum, "mother-of", bystander);
        foreach (var record in new[] { child, orphan })
        {
            Clause(record, "brother-of", bystander);
            Verse(record, OurVerse);
        }

        Tie(child, "son-of", dad, OurSource);
        Tie(child, "son-of", mum, OurSource);
        Tie(dad, "son-of", levi, OurSource);
        Tie(mum, "daughter-of", judah, OurSource);
        Tie(orphan, "son-of", unknown, OurSource);
        Tie(orphan, "son-of", mum, OurSource);
        Tie(unknown, "descendant-of", levi, OurSource);
        await _db.SaveChangesAsync();

        var ours = await OursOnlyRecords.Among(_db, ["child", "orphan"], default);

        ours["child"].Tribe.Should().Be("Levi", "the father's line reaches Levi, the mother's Judah");
        ours["orphan"].Tribe.Should().BeNull("nothing says the father is male, and the mother's side is Judah");
    }

    [Fact]
    public async Task ATribesOwnPatriarchIsOfThatTribe()
    {
        var (levi, other) = (Supplied("levi", "male", null), Person("other"));
        await _db.SaveChangesAsync();
        Clause(other, "of-tribe", levi);
        Clause(levi, "father-of", other);
        Verse(levi, OurVerse);
        await _db.SaveChangesAsync();

        (await OursOnlyRecords.Among(_db, ["levi"], default))["levi"].Tribe.Should().Be("Levi");
    }

    [Fact]
    public void ARecordOfOursShowsOnlyOurLineAndAnyOtherItsOwn()
    {
        var ours = new Dictionary<string, OursOnlyRecords.OwnFacts>
        {
            ["zebedee"] = new(null, null),
            ["nobah"] = new(null, null, "took Kenath (NUM 32:42)"),
        };

        OursOnlyRecords.Line(ours, "zebedee", "father of James").Should().BeNull();
        OursOnlyRecords.Line(ours, "nobah", "a line only BibleData states").Should().Be("took Kenath (NUM 32:42)");
        OursOnlyRecords.Line(ours, "james", "son of Zebedee").Should().Be("son of Zebedee");
    }

    private Entity Supplied(string slug, string? sex, string? tribe)
    {
        var entity = Person(slug);
        entity.Sex = sex;
        entity.Tribe = tribe;
        entity.Source = BibleDataLoader.Source;
        entity.Distinguisher = "a line only BibleData states";
        entity.Notes = "notes only BibleData states";
        return entity;
    }

    private Entity Person(string slug, EntityKind kind = EntityKind.Person)
    {
        var entity = new Entity
        {
            Kind = kind,
            Slug = slug,
            Name = char.ToUpperInvariant(slug[0]) + slug[1..],
            SourceId = $"test:{slug}",
            Source = "test",
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Clause(Entity entity, string relation, Entity target) =>
        _db.EntityDescriptors.Add(new EntityDescriptor
        {
            Entity = entity,
            Ordinal = _db.EntityDescriptors.Local.Count(d => d.Entity == entity) + 1,
            Relation = relation,
            Target = target,
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 1,
            Method = LinkMethod.ModelReading,
            Confidence = 0.9,
            Source = "read from Scripture by a test",
        });

    private void Tie(Entity from, string type, Entity to, string source) =>
        _db.EntityRelationships.Add(new EntityRelationship
        {
            From = from, To = to, Type = type, Category = RelationshipCategories.Read,
            CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 1, Method = LinkMethod.Manual, Source = source,
        });

    private void Rendered(Entity entity, string english) =>
        _db.EntityDistinguishers.Add(new EntityDistinguisher
        {
            Entity = entity, Language = "ukr", Text = "рядок", English = english, Source = "Essenthos, a test",
        });

    private void Verse(Entity entity, string source) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = entity, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 1, Source = source,
        });
}
