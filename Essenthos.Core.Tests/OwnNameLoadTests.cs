using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>A name no dataset gives a record, written once and beside the dataset's own.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnNameLoadTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public OwnNameLoadTests(WitnessDatabase database)
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

    private static readonly OwnName Heavens =
        new(["heaven"], "heavens", "שָׁמַיִם", "shamayim", "H8064", "the heavens", "no singular");

    [Fact]
    public async Task TheShippedGreekFormCorrectionKeepsRehoboamDistinctFromJeroboamAndRunsOnce()
    {
        var rehoboam = new Entity
        {
            Slug = "rehoboam", SourceId = "person:Rehoboam_1", Name = "Rehoboam", Kind = EntityKind.Person, Source = "a test",
            Names = [new EntityName { Label = "Rehoboam", Hebrew = "רְחַבְעָם", HebrewStrongNumber = "H7346",
                Greek = "Ιεροβοαμ", GreekTransliterated = "Ieroboam", Kind = "proper name" }],
        };
        var jeroboam = new Entity
        {
            Slug = "jeroboam", SourceId = "person:Jeroboam_1", Name = "Jeroboam", Kind = EntityKind.Person, Source = "a test",
            Names = [new EntityName { Label = "Jeroboam", HebrewStrongNumber = "H3379", Greek = "Ιεροβοαμ",
                GreekTransliterated = "Ieroboam", Kind = "proper name" }],
        };
        _db.Entities.AddRange(rehoboam, jeroboam);
        await _db.SaveChangesAsync();
        var nameId = rehoboam.Names.Single().Id;
        var loader = new OwnNameLoader(_db, NullLogger<OwnNameLoader>.Instance);
        (await loader.Correct()).Should().Be(1);
        var name = await _db.EntityNames.SingleAsync(n => n.EntityId == rehoboam.Id);
        name.Id.Should().Be(nameId);
        name.Greek.Should().Be("Ῥοβοάμ");
        name.GreekTransliterated.Should().Be("Rhoboám");
        name.Source.Should().Contain("Codex");
        name.Hebrew.Should().Be("רְחַבְעָם");
        name.HebrewStrongNumber.Should().Be("H7346");
        (await _db.EntityNames.SingleAsync(n => n.EntityId == jeroboam.Id)).Greek.Should().Be("Ιεροβοαμ");
        (await loader.Correct()).Should().Be(0);
        (await _db.EntityNames.SingleAsync(n => n.EntityId == rehoboam.Id)).Id.Should().Be(nameId);
    }

    [Theory]
    [InlineData("person:Other_1", "Rehoboam", "H7346", "Ιεροβοαμ", "Ieroboam", null)]
    [InlineData("person:Rehoboam_1", "King Rehoboam", "H7346", "Ιεροβοαμ", "Ieroboam", null)]
    [InlineData("person:Rehoboam_1", "Rehoboam", "H3379", "Ιεροβοαμ", "Ieroboam", null)]
    [InlineData("person:Rehoboam_1", "Rehoboam", "H7346", "other Greek", "Ieroboam", null)]
    [InlineData("person:Rehoboam_1", "Rehoboam", "H7346", "Ιεροβοαμ", "other transliteration", null)]
    [InlineData("person:Rehoboam_1", "Rehoboam", "H7346", "Ιεροβοαμ", "Ieroboam", "an owner's correction")]
    public async Task AChangedIdentityOrNameRowDoesNotAdmitTheGreekFormCorrection(
        string sourceId, string label, string number, string greek, string transliteration, string? source)
    {
        var entity = new Entity
        {
            Slug = "rehoboam", SourceId = sourceId, Name = "Rehoboam", Kind = EntityKind.Person, Source = "a test",
            Names = [new EntityName { Label = label, HebrewStrongNumber = number, Greek = greek,
                GreekTransliterated = transliteration, Source = source, Kind = "proper name" }],
        };
        _db.Entities.Add(entity);
        await _db.SaveChangesAsync();
        var loader = new OwnNameLoader(_db, NullLogger<OwnNameLoader>.Instance);
        (await loader.Correct()).Should().Be(0);
        var name = await _db.EntityNames.SingleAsync(n => n.EntityId == entity.Id);
        name.Greek.Should().Be(greek);
        name.GreekTransliterated.Should().Be(transliteration);
        name.Source.Should().Be(source);
    }

    [Fact]
    public async Task HeavenAnswersToHeavensOnceHoweverOftenItLoads()
    {
        _db.Entities.Add(new Entity
        {
            Kind = EntityKind.Place,
            Slug = "heaven",
            Name = "heaven",
            SourceId = "test:heaven",
            Source = "test",
            Names = [new EntityName { Label = "heaven", Kind = "name" }],
        });
        await _db.SaveChangesAsync();
        var loader = new OwnNameLoader(_db, NullLogger<OwnNameLoader>.Instance);

        (await loader.Load([Heavens], default)).Written.Should().Be(1);
        (await loader.Load([Heavens], default)).Written.Should().Be(0);

        var names = await _db.EntityNames.Where(n => n.Entity!.Slug == "heaven").Select(n => n.Label).ToListAsync();
        names.Should().BeEquivalentTo(["heaven", "heavens"]);
    }

    [Fact]
    public async Task ANameForARecordNobodyHoldsIsCountedNotWritten()
    {
        var outcome = await new OwnNameLoader(_db, NullLogger<OwnNameLoader>.Instance)
            .Load([Heavens with { Entities = ["nowhere-at-all"] }], default);

        outcome.Missing.Should().Be(1);
        outcome.Written.Should().Be(0);
    }

    private static readonly CorrectedNumber Deborah =
        new(["deborah-2", "deborah"], "Deborah", new WrongNumber("H1682", null), "H1683", null, "the bee");

    /// <summary>
    /// The judge numbered by the word for a bee gets the name's number; the nurse, who already has the
    /// name under it, loses the wrong row a fold brought her; and a second run finds nothing.
    /// </summary>
    [Fact]
    public async Task ANumberADatasetWroteForAnotherWordIsCorrectedOnce()
    {
        _db.Entities.Add(new Entity
        {
            Kind = EntityKind.Person, Slug = "deborah-2", Name = "Deborah", SourceId = "test:deborah2", Source = "test",
            Names = [new EntityName { Label = "Deborah", HebrewStrongNumber = "H1682", Kind = "proper name" }],
        });
        _db.Entities.Add(new Entity
        {
            Kind = EntityKind.Person, Slug = "deborah", Name = "Deborah", SourceId = "test:deborah1", Source = "test",
            Names =
            [
                new EntityName { Label = "Deborah", HebrewStrongNumber = "H1683", Kind = "proper name" },
                new EntityName { Label = "Deborah", HebrewStrongNumber = "H1682", Kind = "proper name" },
            ],
        });
        await _db.SaveChangesAsync();
        var loader = new OwnNameLoader(_db, NullLogger<OwnNameLoader>.Instance);

        (await loader.Correct([Deborah], default)).Should().Be(2);
        (await loader.Correct([Deborah], default)).Should().Be(0);

        var numbers = await _db.EntityNames.Where(n => n.Label == "Deborah")
            .Select(n => new { n.Entity!.Slug, n.HebrewStrongNumber }).ToListAsync();
        numbers.Should().BeEquivalentTo(
        [
            new { Slug = "deborah-2", HebrewStrongNumber = "H1683" },
            new { Slug = "deborah", HebrewStrongNumber = "H1683" },
        ]);
    }

    /// <summary>A row that carries a Greek number the correction does not name is another row and is left.</summary>
    [Fact]
    public async Task OnlyTheRowWithTheWrongNumbersIsTouched()
    {
        _db.Entities.Add(new Entity
        {
            Kind = EntityKind.Person, Slug = "deborah-2", Name = "Deborah", SourceId = "test:deborah2", Source = "test",
            Names = [new EntityName { Label = "Deborah", HebrewStrongNumber = "H1682", GreekStrongNumber = "G1", Kind = "proper name" }],
        });
        await _db.SaveChangesAsync();

        (await new OwnNameLoader(_db, NullLogger<OwnNameLoader>.Instance).Correct([Deborah], default)).Should().Be(0);
    }

    /// <summary>The list ships inside the loader, so a build that dropped it would load nothing silently.</summary>
    [Fact]
    public async Task TheEmbeddedListIsRead()
    {
        await using var stream = typeof(OwnNameLoader).Assembly
            .GetManifestResourceStream("Essenthos.Core.Loading.Encyclopedia.OwnNames.json");
        stream.Should().NotBeNull();
        using var list = await JsonDocument.ParseAsync(stream!);
        list.RootElement.GetProperty("numbers").GetArrayLength().Should().BeGreaterThan(0);

        var outcome = await new OwnNameLoader(_db, NullLogger<OwnNameLoader>.Instance).Load();

        outcome.Missing.Should().Be(0, "no name is given to a record the scratch database lacks");
    }
}
