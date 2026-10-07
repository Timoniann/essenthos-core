using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading;
using Essenthos.Core.Strong;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The relations step reads both dictionaries on every load and writes only when the reading
/// changed, so a second load leaves the table exactly as the first wrote it.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class StrongRelationLoaderTests : IDisposable
{
    private const string Hebrew =
        """
        <osis xmlns="http://www.bibletechnologies.net/2003/OSIS/namespace"><osisText><div type="glossary">
          <div type="entry" n="1004"><w lemma="בַּיִת" ID="H1004">בית</w>
            <note type="exegesis">probably from <w lemma="בָּנָה" src="1129" xlit="banah"/> abbreviated;</note></div>
          <div type="entry" n="1006"><w lemma="בַּיִת" ID="H1006">בית</w>
            <note type="exegesis">the same as <w lemma="בַּיִת" src="1004" xlit="bayith"/>;</note></div>
          <div type="entry" n="1129"><w lemma="בָּנָה" ID="H1129">בנה</w>
            <note type="exegesis">a primitive root;</note></div>
        </div></osisText></osis>
        """;

    private const string Greek =
        """
        <strongsdictionary><entries>
        <entry strongs="02076"><greek unicode="ἐστί"/><strongs_derivation>third person singular present indicative of <strongsref language="GREEK" strongs="1510"/>;</strongs_derivation><strongs_def> he (she or it) is</strongs_def></entry>
        <entry strongs="01510"><greek unicode="εἰμί"/><strongs_derivation>a prolonged form of a primary and defective verb;</strongs_derivation><strongs_def> I exist</strongs_def></entry>
        </entries></strongsdictionary>
        """;

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly StrongRelationLoader _loader;
    private readonly string _folder = Directory.CreateTempSubdirectory("strong-relations").FullName;

    public StrongRelationLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _loader = new StrongRelationLoader(_db, NullLogger<StrongRelationLoader>.Instance);
        File.WriteAllText(HebrewPath, Hebrew);
        File.WriteAllText(GreekPath, Greek);
    }

    private string HebrewPath => Path.Combine(_folder, "StrongHebrew.xml");

    private string GreekPath => Path.Combine(_folder, "StrongGreek.xml");

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
        Directory.Delete(_folder, true);
    }

    [Fact]
    public async Task Each_relation_is_written_with_its_kind_its_clause_and_whose_reading_it_is()
    {
        var outcome = await _loader.Load(HebrewPath, GreekPath);

        outcome.AlreadyLoaded.Should().BeFalse();
        var rows = await _db.StrongRelations.OrderBy(r => r.FromNumber).ThenBy(r => r.Position).ToListAsync();
        rows.Select(r => (r.FromNumber, r.ToNumber, r.Kind, r.Hedged)).Should().Equal(
            ("G2076", "G1510", StrongRelationKinds.FormOf, false),
            ("H1004", "H1129", StrongRelationKinds.From, true),
            ("H1006", "H1004", StrongRelationKinds.SameAs, false),
            ("H1129", null, StrongRelationKinds.Primitive, false));
        rows.Single(r => r.FromNumber == "H1006").Statement.Should().Be("the same as בַּיִת (H1004)");
        rows.Single(r => r.FromNumber == "G2076").Source.Should().Be(StrongRelationLoader.GreekSource);
        rows.Single(r => r.FromNumber == "H1006").Source.Should().Be(StrongRelationLoader.HebrewSource);
    }

    [Fact]
    public async Task A_second_load_writes_nothing_and_keeps_every_row()
    {
        await _loader.Load(HebrewPath, GreekPath);
        var first = await _db.StrongRelations.AsNoTracking().OrderBy(r => r.Id).ToListAsync();

        var outcome = await _loader.Load(HebrewPath, GreekPath);

        outcome.AlreadyLoaded.Should().BeTrue();
        (await _db.StrongRelations.AsNoTracking().OrderBy(r => r.Id).ToListAsync())
            .Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task A_changed_reading_replaces_what_was_held()
    {
        await _loader.Load(HebrewPath, GreekPath);
        File.WriteAllText(HebrewPath, Hebrew.Replace("the same as", "a variation of"));

        var outcome = await _loader.Load(HebrewPath, GreekPath);

        outcome.AlreadyLoaded.Should().BeFalse();
        (await _db.StrongRelations.SingleAsync(r => r.FromNumber == "H1006")).Kind.Should().Be(StrongRelationKinds.Variant);
        (await _db.StrongRelations.CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task The_entry_page_reads_what_the_entry_says_and_what_says_it_of_the_entry()
    {
        _db.StrongEntries.Add(new StrongEntry { StrongNumber = "H1129", Lemma = "בָּנָה", Definition = "to build" });
        _db.StrongEntries.Add(new StrongEntry { StrongNumber = "H1006", Lemma = "בַּיִת", Definition = "Bajith" });
        await _db.SaveChangesAsync();
        await _loader.Load(HebrewPath, GreekPath);

        var (relations, kin) = await Endpoints.StrongEndpoints.Relations(_db, "H1004", CancellationToken.None);

        relations.Should().ContainSingle().Which.Should().Be(new Endpoints.StrongRelationResponse(
            StrongRelationKinds.From, "H1129", "בָּנָה", "to build", true, "probably from בָּנָה (H1129) abbreviated",
            StrongRelationLoader.HebrewSource));
        kin.Should().ContainSingle().Which.Should().Match<Endpoints.StrongRelationResponse>(r =>
            r.Kind == StrongRelationKinds.SameAs && r.Number == "H1006" && r.Lemma == "בַּיִת");
    }

    [Fact]
    public async Task Only_a_primitive_may_name_no_other_entry()
    {
        _db.StrongRelations.Add(new StrongRelation
        {
            FromNumber = "H1", ToNumber = null, Kind = StrongRelationKinds.From, Position = 1, Statement = "from", Source = "a source",
        });

        var refused = async () => await _db.SaveChangesAsync();

        (await refused.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<Npgsql.PostgresException>()
            .Which.ConstraintName.Should().Be("ck_strong_relation_to_number");
    }
}
