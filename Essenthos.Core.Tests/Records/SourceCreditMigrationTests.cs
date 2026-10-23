using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The rows a database already holds take the credits the record files now hold. A file recorded
/// once is skipped whole by the loaders, so the words a load wrote stay until a migration changes
/// them, and the rows are what the API serves.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SourceCreditMigrationTests : IDisposable
{
    private const string TieBreakBefore =
        "Essenthos, two verses the tie-break of 2026-10-08 left on PRB-0490, decided by the lead from the readings of " +
        "gpt-6.1-sol, a blind judge and claude-fable-5-1, 2026-10-09";

    private const string TieBreakAfter =
        "Essenthos, two verses the tie-break of 2026-10-08 left, decided by the lead from the readings of " +
        "gpt-6.1-sol, a blind judge and claude-fable-5-1, 2026-10-09";

    private const string ReadBy = "read from Scripture by ";
    private const string GenesisBefore = ReadBy + "an agent reading GEN 36:26 and 1CH 1:41 on the lead's decision of 2026-10-08 (PRB-0985)";
    private const string GenesisAfter = ReadBy + "an agent reading GEN 36:26 and 1CH 1:41 on the lead's decision of 2026-10-08";
    private const string OptionBefore = ReadBy + "an agent reading 1SA 25:3 on the lead's decision of 2026-10-08 (PRB-0476, option a)";
    private const string OptionAfter = ReadBy + "an agent reading 1SA 25:3 on the lead's decision of 2026-10-08";
    private const string AnsweredBefore = ReadBy + "the project owner, answered on PRB-0829 2026-09-28";
    private const string AnsweredAfter = ReadBy + "the project owner, decided 2026-09-28";
    private const string AdoptiveBefore =
        ReadBy + "the project owner, decided 2026-10-08 (his answer on PRB-0977: add the relation adoptive mother, " +
        "Rachel of Dan, and do not draw it on the family tree)";
    private const string AdoptiveAfter = ReadBy + "the project owner, decided 2026-10-08";

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public SourceCreditMigrationTests(WitnessDatabase database)
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
    public async Task A_credit_naming_a_ticket_or_an_instruction_is_rewritten_and_every_other_is_left()
    {
        Entity Person(string slug, string source = "a test") => new()
        {
            Kind = EntityKind.Person, Slug = slug, Name = slug, SourceId = slug, Source = source,
        };

        var tieBreak = Person("berechiah-doorkeeper-of-the-ark", TieBreakBefore);
        var jesus = Person("jesus");
        var rachel = Person("rachel");
        var dan = Person("dan");
        tieBreak.Claims = [new EntityClaim { Method = LinkMethod.Manual, Source = TieBreakBefore }];
        jesus.Claims =
        [
            new EntityClaim
            {
                Method = LinkMethod.StatedBySource,
                Source = "Essenthos, separated from BibleData's YHVH_1 — see DOC-0099",
            },
            new EntityClaim { Method = LinkMethod.ModelReading, Confidence = 0.9, Source = "Essenthos, an untouched credit" },
        ];
        _db.Entities.AddRange(tieBreak, jesus, rachel, dan);
        await _db.SaveChangesAsync();

        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = tieBreak.Id, CanonicalBook = 13, CanonicalChapter = 15, CanonicalVerse = 23,
            Label = "Berechiah", Disputed = false, Source = TieBreakBefore,
        });

        var descriptors = new[] { GenesisBefore, OptionBefore, AnsweredBefore, AdoptiveBefore, ReadBy + "claude-sonnet-5, asked 2026-09-09" }
            .Select((source, index) => new EntityDescriptor
            {
                EntityId = rachel.Id, Ordinal = index + 1, Relation = DescriptorRelations.ServantOf, TargetEntityId = dan.Id,
                CanonicalBook = 1, CanonicalChapter = 30, CanonicalVerse = index + 1,
                Method = LinkMethod.Manual, Source = source,
                Claims = [new EntityDescriptorClaim { Method = LinkMethod.Manual, Source = source }],
            })
            .ToList();
        _db.EntityDescriptors.AddRange(descriptors);
        _db.EntityRelationships.AddRange(descriptors.Select(d => new EntityRelationship
        {
            FromEntityId = d.EntityId, ToEntityId = d.TargetEntityId, Type = DescriptorRelations.ServantOf,
            Category = RelationshipCategories.Read, CanonicalBook = 1, CanonicalChapter = 30,
            CanonicalVerse = d.CanonicalVerse, Method = LinkMethod.Manual, Source = d.Source,
        }));

        var merged = new[]
        {
            "Essenthos: the namesakes judge's pairs of one man held twice (PRB-0490), each read by gpt-6.1-sol (Codex) and, blind, by claude-opus-5-5, folded where both call them one person, 2026-10-08",
            "an agent reading GEN 26:34 and GEN 36:2 on the project owner's instruction of 2026-10-12 to decide the identities as is most logical, decided 2026-10-12",
            "the lead reading 1CH 27:18 against 1SA 16:6-11, 17:13 and 1CH 2:13-15 on the project owner's ruling in chat of 2026-10-15 to follow the common identification, decided 2026-10-15",
            "the project owner, ruled 2026-10-12",
        };
        _db.MergedRecords.AddRange(merged.Select((source, index) => new MergedRecord
        {
            Slug = $"folded-{index}", EntityId = rachel.Id, Name = "Folded", RecordSourceId = $"folded:{index}",
            RecordSource = "a test", Method = LinkMethod.ModelReading, Confidence = 0.9, Reason = "a test", Source = source,
        }));

        var greek = Corpus.Add(_db, "NESTLE1904", TextKind.CriticalEdition, "grc", (1, 1, ["Α", "Β", "Γ"]));
        await _db.SaveChangesAsync();
        WordEntity Annotation(int position, string source, string? note)
        {
            var annotation = new WordEntity
            {
                Word = _db.WordAt(greek, 1, 1, position), Entity = tieBreak, Method = LinkMethod.Manual,
                Source = source, Note = note,
                Claims = [new WordEntityClaim { Method = LinkMethod.Manual, Source = source, Note = note }],
            };
            _db.WordEntities.Add(annotation);
            return annotation;
        }

        Annotation(1, TieBreakBefore, "kept note");
        Annotation(
            2, "Essenthos, a different credit",
            "'The land of Ham' is Egypt, where Jacob sojourned and the wonders were done. The owner's word: the land " +
            "where a land record is held, and the record holds the land of Ham as another name for Egypt.");
        Annotation(3, "Essenthos, a different credit", "Dan names an eastern gate under the preceding instruction to use tribal names.");
        await _db.SaveChangesAsync();

        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        new Rewriting().Apply(builder);
        foreach (var operation in builder.Operations.OfType<SqlOperation>())
        {
            await _db.Database.ExecuteSqlRawAsync(operation.Sql);
        }

        _db.ChangeTracker.Clear();
        (await _db.Entities.SingleAsync(e => e.Slug == tieBreak.Slug)).Source.Should().Be(TieBreakAfter);
        (await _db.EntityClaims.Where(c => c.EntityId == tieBreak.Id).SingleAsync()).Source.Should().Be(TieBreakAfter);
        (await _db.EntityVerses.SingleAsync()).Source.Should().Be(TieBreakAfter);
        (await _db.EntityClaims.Where(c => c.EntityId == jesus.Id).Select(c => c.Source).ToListAsync())
            .Should().BeEquivalentTo("Essenthos, separated from BibleData's YHVH_1", "Essenthos, an untouched credit");

        var after = new[] { GenesisAfter, OptionAfter, AnsweredAfter, AdoptiveAfter, ReadBy + "claude-sonnet-5, asked 2026-09-09" };
        (await _db.EntityDescriptors.OrderBy(d => d.Ordinal).Select(d => d.Source).ToListAsync()).Should().Equal(after);
        (await _db.EntityDescriptorClaims.OrderBy(d => d.EntityDescriptorId).Select(d => d.Source).ToListAsync()).Should().Equal(after);
        (await _db.EntityRelationships.OrderBy(d => d.CanonicalVerse).Select(d => d.Source).ToListAsync()).Should().Equal(after);

        (await _db.MergedRecords.OrderBy(m => m.Slug).Select(m => m.Source).ToListAsync()).Should().Equal(
            "Essenthos: the namesakes judge's pairs of one man held twice, each read by gpt-6.1-sol (Codex) and, blind, by claude-opus-5-5, folded where both call them one person, 2026-10-08",
            "an agent reading GEN 26:34 and GEN 36:2 on the project owner's instruction of 2026-10-12, decided 2026-10-12",
            "the lead reading 1CH 27:18 against 1SA 16:6-11, 17:13 and 1CH 2:13-15 on the project owner's ruling of 2026-10-15, decided 2026-10-15",
            "the project owner, ruled 2026-10-12");

        var annotations = await _db.WordEntities.Include(a => a.Claims).OrderBy(a => a.WordId).ToListAsync();
        annotations.Select(a => a.Source).Should().Equal(TieBreakAfter, "Essenthos, a different credit", "Essenthos, a different credit");
        annotations[0].Claims.Single().Source.Should().Be(TieBreakAfter);
        annotations[0].Note.Should().Be("kept note");
        string[] notes =
        [
            "'The land of Ham' is Egypt, where Jacob sojourned and the wonders were done. The land is held as a record " +
            "of its own, and the record holds the land of Ham as another name for Egypt.",
            "Dan names an eastern gate, and the gates are named for the tribes.",
        ];
        annotations.Skip(1).Select(a => a.Note).Should().Equal(notes);
        annotations.Skip(1).Select(a => a.Claims.Single().Note).Should().Equal(notes);
    }

    private sealed class Rewriting : TheCreditsOfOurOwnRowsNameNoTicketAndQuoteNoInstruction
    {
        public void Apply(MigrationBuilder builder) => Up(builder);
    }
}
