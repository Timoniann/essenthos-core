using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The descriptions this corpus writes for itself, from the file a generation pass produces to the
/// line a reader is shown.
///
/// The fixtures are written here rather than taken from the corpus, and deliberately: the real
/// files are the output of a model run and are not on most disks, and a loader that cannot be
/// exercised without a gigabyte of sources is a loader nobody exercises. What they stand in for is
/// the shape of DOC-0191, which is the thing three agents are building against.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class DescriptorTests : IDisposable
{
    private const string Model = "claude-sonnet-5";

    private const string AskedAt = "2026-09-06";

    /// <summary>What BibleData says about Hobab, which nothing here may touch.</summary>
    private const string ImportedSentence = "the son of Reuel, Moses' father-in-law (NUM 10:29)";

    private readonly AppDbContext _db;

    public DescriptorTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");

        Add("hobab-1", EntityKind.Person, "Hobab", ImportedSentence, (4, 10, 29), (7, 4, 11));
        Add("reuel-1", EntityKind.Person, "Reuel", null, (2, 2, 18));
        Add("moses-1", EntityKind.Person, "Moses", null, (2, 2, 10));
        Add("moab-1", EntityKind.Person, "Moab", null, (1, 19, 37));
        Add("moabites", EntityKind.People, "Moabites", null, (1, 19, 37));
        Add("jethro-1", EntityKind.Person, "Jethro", null, (2, 3, 1));
        Add("midianites", EntityKind.People, "Midianites", null, (2, 3, 1));

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    private void Add(
        string slug,
        EntityKind kind,
        string name,
        string? distinguisher,
        params (int Book, int Chapter, int Verse)[] verses)
    {
        var entity = new Entity
        {
            Kind = kind,
            Slug = slug,
            Name = name,
            Distinguisher = distinguisher,
            SourceId = slug,
            Source = "a test",
        };

        foreach (var (book, chapter, verse) in verses)
        {
            entity.Verses.Add(new EntityVerse
            {
                CanonicalBook = book,
                CanonicalChapter = chapter,
                CanonicalVerse = verse,
                Source = "a test",
            });
        }

        _db.Entities.Add(entity);
    }

    /// <summary>The fixtures, which live in the test project's own output rather than in the corpus.</summary>
    private static string Fixtures(string folder) =>
        Path.Combine(AppContext.BaseDirectory, "Resources", "descriptors", folder);

    private EntityDescriptorLoader Loader(string folder) =>
        new(
            _db,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [DescriptorFiles.ConfigurationKey] = Fixtures(folder),
                })
                .Build(),
            NullLogger<EntityDescriptorLoader>.Instance);

    private Task<DescriptorOutcome> Load(string folder) =>
        Loader(folder).Load(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}"));

    private static string Line(EntityDescriptorResponse? description) =>
        string.Concat(description!.Parts.Select(p => p.Text));

    private Task<EntityDescriptorResponse?> Read(string slug, string? language) =>
        Descriptors.Of(_db, slug, language, default);

    [Fact]
    public async Task ADescriptionIsTheClausesTheFileStatesInTheOrderItStatesThem()
    {
        var outcome = await Load("described");

        outcome.Described.Should().Be(2);
        outcome.Clauses.Should().Be(3);
        outcome.Unresolved.Should().Be(1, "Hobab's pass could not place the Kenites");

        var clauses = await _db.EntityDescriptors
            .Include(d => d.Entity).Include(d => d.Target)
            .Where(d => d.Entity!.Slug == "hobab-1")
            .OrderBy(d => d.Ordinal)
            .ToListAsync();

        clauses.Select(d => (d.Ordinal, d.Relation, d.Target!.Slug)).Should().Equal(
            (1, DescriptorRelations.SonOf, "reuel-1"),
            (2, DescriptorRelations.FatherInLawOf, "moses-1"));
        clauses.Select(d => (d.CanonicalBook, d.CanonicalChapter, d.CanonicalVerse))
            .Should().Equal((4, 10, 29), (7, 4, 11));
    }

    /// <summary>
    /// Who said it travels onto every row, and onto a claim of its own beside it, so that a second
    /// method agreeing later has somewhere to go instead of overwriting the first.
    /// </summary>
    [Fact]
    public async Task EveryClauseCarriesTheModelAndTheDateAndAClaimOfItsOwn()
    {
        await Load("described");

        var clauses = await _db.EntityDescriptors.Include(d => d.Claims).ToListAsync();

        clauses.Should().OnlyContain(d => d.Method == LinkMethod.ModelReading);
        clauses.Should().OnlyContain(d => d.Confidence != null);
        clauses.Should().OnlyContain(d => d.Source.Contains(Model) && d.Source.Contains(AskedAt));
        clauses.Should().OnlyContain(d => d.Claims.Count == 1);
        clauses.SelectMany(d => d.Claims).Should()
            .OnlyContain(c => c.Method == LinkMethod.ModelReading && c.Confidence != null);
    }

    [Fact]
    public async Task TheEnglishLineNamesEveryTargetAndLinksIt()
    {
        await Load("described");

        var description = await Read("hobab-1", DescriptorPhrasings.English);

        Line(description).Should().Be("son of Reuel, father-in-law of Moses");
        description!.Parts.Where(p => p.Entity is not null).Select(p => p.Entity!.Slug)
            .Should().Equal("reuel-1", "moses-1");
        description.Claims.Select(c => c.Reference.Slug).Should().Equal("numbers", "judges");
    }

    /// <summary>
    /// The case the whole layer was asked for: <em>тесть Мойсея</em> and not <em>тесть Мойсей</em>,
    /// with the name coming from the form the pass produced rather than from a stemmer.
    /// </summary>
    [Fact]
    public async Task UkrainianPutsTheTargetInTheGenitive()
    {
        await Load("described");

        var description = await Read("hobab-1", DescriptorPhrasings.Ukrainian);

        Line(description).Should().Be("син Регуїла, тесть Мойсея");
        description!.Language.Should().Be(DescriptorPhrasings.Ukrainian);
    }

    /// <summary>
    /// The other case the owner asked for by name: hovering <em>моавітяни</em> produces
    /// <em>нащадки Моава</em>, and Moab is a link.
    /// </summary>
    [Fact]
    public async Task APeopleIsTheDescendantsOfItsEponymAndTheEponymIsALink()
    {
        await Load("described");

        var description = await Read("moabites", DescriptorPhrasings.Ukrainian);

        Line(description).Should().Be("нащадки Моава");
        description!.Parts.Should().ContainSingle(p => p.Entity != null)
            .Which.Entity!.Slug.Should().Be("moab-1");
    }

    [Fact]
    public async Task ALanguageTheEncyclopediaDoesNotSpeakGetsNoDescriptionRatherThanAnEnglishOne()
    {
        await Load("described");

        (await Read("hobab-1", "deu")).Should().BeNull();
    }

    /// <summary>
    /// A form the pass did not produce is a gap, and the gap shows as the English name. Inflecting
    /// it here would be a guess a reader could not tell from a form somebody wrote.
    /// </summary>
    [Fact]
    public async Task AMissingFormFallsBackToTheEnglishNameRatherThanAnInventedInflection()
    {
        await Load("refused");

        Line(await Read("jethro-1", DescriptorPhrasings.Ukrainian)).Should().Be("тесть Moses");
    }

    /// <summary>
    /// Below the confidence at which the corpus states something plainly, the clause is kept and
    /// marked. Dropping it would make the description read as more certain than it is.
    /// </summary>
    [Fact]
    public async Task AClauseBelowTheFloorIsKeptAndMarked()
    {
        await Load("refused");

        var description = await Read("jethro-1", DescriptorPhrasings.English);

        description!.Claims.Should().ContainSingle()
            .Which.Should().Match<DescriptorClaimResponse>(c => c.Doubtful && c.Confidence == 0.55);
        description.Parts.Should().OnlyContain(p => p.Doubtful);
    }

    /// <summary>
    /// The three refusals DOC-0191 asks for, and the fourth the provenance rules force: a claim
    /// with no confidence cannot be stored as an inference, and storing it as testimony would be
    /// a model's guess wearing a source's name.
    /// </summary>
    [Fact]
    public async Task WhatCannotBeRenderedOrCheckedIsRefusedAndCounted()
    {
        var outcome = await Load("refused");

        outcome.Refused.UnknownRelation.Should().Be(1, "shepherd-of is not in the vocabulary");
        outcome.Refused.UnresolvedTarget.Should().Be(1, "no entity answers to zipporah-9");
        outcome.Refused.UnmatchedReference.Should().Be(1, "Jethro is not named in Genesis 1:1");
        outcome.Refused.WithoutConfidence.Should().Be(1);
        outcome.Refused.UnknownEntity.Should().Be(1, "the encyclopedia holds no such record");
        outcome.Refused.Total.Should().Be(5);

        outcome.Clauses.Should().Be(1);
        var clause = await _db.EntityDescriptors.SingleAsync();
        clause.Relation.Should().Be(DescriptorRelations.FatherInLawOf);
        clause.Ordinal.Should().Be(1, "the ordinals of what survives stay contiguous");
    }

    [Fact]
    public async Task LoadingTwiceWritesNothingTheSecondTime()
    {
        await Load("described");
        var before = await _db.EntityDescriptors.CountAsync();
        var forms = await _db.EntityNameForms.CountAsync();

        var again = await Load("described");

        again.Clauses.Should().Be(0);
        again.Forms.Should().Be(0);
        again.Skipped.Should().Be(5, "every record in the file names an entity already described");
        (await _db.EntityDescriptors.CountAsync()).Should().Be(before);
        (await _db.EntityNameForms.CountAsync()).Should().Be(forms);
    }

    /// <summary>
    /// A second batch, generated later, loads beside the first rather than being turned away by a
    /// guard that asks whether the table holds anything at all. That guard is what left a cold
    /// database with no name resolutions in it (PRB-0343).
    /// </summary>
    [Fact]
    public async Task ALaterBatchLoadsBesideTheOneBeforeIt()
    {
        await Load("described");

        var outcome = await Load("refused");

        outcome.Clauses.Should().Be(1);
        (await _db.EntityDescriptors.CountAsync(d => d.Entity!.Slug == "jethro-1")).Should().Be(1);
        (await _db.EntityDescriptors.CountAsync(d => d.Entity!.Slug == "hobab-1")).Should().Be(2);
    }

    /// <summary>
    /// The imported sentence is left exactly as it was. It stops being what a reader is shown and
    /// does not stop being the record BibleData supplied — which is what the generated clauses are
    /// measured against, and the only description an entity nothing has been generated for has.
    /// </summary>
    [Fact]
    public async Task TheImportedSentenceIsNeitherReadNorWritten()
    {
        await Load("described");

        var hobab = await _db.Entities.SingleAsync(e => e.Slug == "hobab-1");
        hobab.Distinguisher.Should().Be(ImportedSentence);
        (await _db.Entities.CountAsync(e => e.Distinguisher != null)).Should().Be(1);
    }

    [Fact]
    public async Task NoDescriptorsOnDiskLoadsNothingAndSaysSo()
    {
        var outcome = await Load(Guid.NewGuid().ToString("N"));

        outcome.NoDescriptors.Should().BeTrue();
        outcome.Clauses.Should().Be(0);
    }
}
