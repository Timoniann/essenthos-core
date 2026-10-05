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

[Collection(WitnessDatabaseCollection.Name)]
public sealed class NameFormRemainderTests(WitnessDatabase database)
{
    [Theory]
    [InlineData(false, 18)]
    [InlineData(true, 12)]
    public async Task ATargetBatchCompletesReaderCasesOnColdAndPartiallyLoadedRecords(
        bool partial, int expectedForms)
    {
        await using var db = database.NewContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE entity RESTART IDENTITY CASCADE");
        var folder = Path.Combine(AppContext.BaseDirectory, "Resources", "name-forms", "target-gaps");
        var records = NameFormFiles.Read(folder).Records;
        records.Should().HaveCount(9);
        foreach (var record in records)
        {
            var target = new Entity
            {
                Slug = record.Entity,
                Name = record.Entity,
                Kind = record.Entity == "jezerites" ? EntityKind.People : EntityKind.Place,
                SourceId = record.Entity,
                Source = "a frozen target fixture",
            };
            db.Entities.Add(target);
            var relation = record.Names!["ukr"].ContainsKey("locative") ? "city-in" : "near";
            var subject = new Entity
            {
                Slug = "subject-" + record.Entity,
                Name = "Subject",
                Kind = EntityKind.Place,
                SourceId = "subject-" + record.Entity,
                Source = "a frozen target fixture",
            };
            db.Entities.Add(subject);
            db.EntityDescriptors.Add(new EntityDescriptor
            {
                Entity = subject, Target = target, Ordinal = 1, Relation = relation,
                CanonicalBook = 6, CanonicalChapter = 11, CanonicalVerse = 5,
                Method = LinkMethod.ModelReading, Confidence = 1, Source = "a fixture clause",
            });
            if (partial && record.Entity is not ("jezerites" or "merom" or "netaim"))
            {
                db.EntityNameForms.Add(new EntityNameForm
                {
                    Entity = target, Language = "ukr", GrammaticalCase = "nominative",
                    Form = record.Names["ukr"]["nominative"], Method = LinkMethod.ModelReading,
                    Confidence = 1, Source = EntityNameFormLoader.SourcePrefix + " the earlier batch",
                });
            }
        }
        await db.SaveChangesAsync();
        var previous = await db.EntityNameForms.AsNoTracking().OrderBy(f => f.Id).ToListAsync();
        var subjects = records.Select(r => "subject-" + r.Entity).ToArray();
        var before = await Descriptors.Of(db, subjects, "ukr", default);
        before.Values.SelectMany(line => line.Parts)
            .Count(p => p.Entity is not null && p.Text == p.Entity.EnglishName).Should().Be(9);
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [NameFormFiles.ConfigurationKey] = folder }).Build();
        var loader = new EntityNameFormLoader(db, config, NullLogger<EntityNameFormLoader>.Instance);

        (await loader.Load(folder)).Forms.Should().Be(expectedForms);

        var after = await Descriptors.Of(db, subjects, "ukr", default);
        after.Values.SelectMany(line => line.Parts)
            .Should().NotContain(p => p.Entity != null && p.Text == p.Entity.EnglishName);
        var stored = await db.EntityNameForms.AsNoTracking().OrderBy(f => f.Id).ToListAsync();
        stored.Where(f => previous.Any(p => p.Id == f.Id)).Should().BeEquivalentTo(previous);
        (await loader.Load(folder)).Forms.Should().Be(0);
        (await db.EntityNameForms.AsNoTracking().OrderBy(f => f.Id).ToListAsync())
            .Should().BeEquivalentTo(stored);
    }
}
