using System.Text.RegularExpressions;
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
/// The records that had no name in Ukrainian, German or Russian are given one: persons, peoples,
/// places, titles and terms by the name-forms batch of 2026-10-14, the objects and appointed times
/// in their own record files.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed partial class ReaderLanguageNameTests(WitnessDatabase database)
{
    private const string BatchFile = "zz-complete-2026-10-14.jsonl";

    private static readonly string[] Languages = ["ukr", "deu", "rus"];

    private static string Shipped => TestResources.Shipped(Path.Combine("Essenthos", "name-forms"));

    private static List<NameFormRecord> Batch() =>
        NameFormFiles.Read(Shipped).Records.Where(r => r.AskedAt == "2026-10-14").ToList();

    [Fact]
    public void TheBatchNamesEachRecordOnceInEachLanguagesOwnAlphabet()
    {
        var records = Batch();

        records.Select(r => r.Entity).Should().OnlyHaveUniqueItems()
            .And.Contain(["chelub-father-of-ezri", "kohathites", "caesar-title", "elohim", "carthage"]);
        foreach (var record in records)
        {
            record.Names.Should().NotBeNullOrEmpty(record.Entity);
            foreach (var (language, forms) in record.Names!)
            {
                language.Should().BeOneOf(Languages);
                forms.Keys.Should().BeEquivalentTo(["nominative", "genitive"], record.Entity);
                var alphabet = language == "deu" ? German() : Cyrillic();
                forms.Values.Should().OnlyContain(form => alphabet.IsMatch(form), $"{record.Entity} in {language}");
            }
        }
    }

    [Fact]
    public void TheObjectsAndAppointedTimesAreNamedInRussian()
    {
        var records = ThingFiles.Read().ToDictionary(r => r.Slug);

        records["ark-of-the-covenant"].Names!["rus"].Should().Be("Ковчег завета");
        records["passover"].Names!["rus"].Should().Be("Пасха");
        records["feast-of-booths"].Names!["rus"].Should().Be("Праздник кущей");
        records["sauls-spear"].Names!["deu"].Should().Be("Spieß Sauls");
    }

    [Fact]
    public async Task EveryFormOfTheBatchIsLoadedAndNamesItsRecord()
    {
        await using var db = database.NewContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE entity RESTART IDENTITY CASCADE");
        var records = Batch();
        foreach (var record in records)
        {
            db.Entities.Add(new Entity
            {
                Slug = record.Entity, Name = record.Entity, Kind = EntityKind.Place,
                SourceId = record.Entity, Source = "a fixture record",
            });
        }

        await db.SaveChangesAsync();
        var folder = Directory.CreateTempSubdirectory("name-forms-").FullName;
        try
        {
            File.Copy(Path.Combine(Shipped, BatchFile), Path.Combine(folder, BatchFile));
            var config = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { [NameFormFiles.ConfigurationKey] = folder }).Build();
            var loader = new EntityNameFormLoader(db, config, NullLogger<EntityNameFormLoader>.Instance);

            var outcome = await loader.Load(folder);

            outcome.Refused.Total.Should().Be(0, outcome.Refused.ToString());
            outcome.Bared.Should().Be(0, "no form carries a word the phrase supplies");
            outcome.Forms.Should().Be(records.Sum(r => r.Names!.Sum(language => language.Value.Count)));
            (await loader.Load(folder)).Forms.Should().Be(0);

            var ids = await db.Entities.ToDictionaryAsync(e => e.Slug, e => e.Id);
            (await Named(db, ids["chelub-father-of-ezri"], "deu")).Should().Be("Chelub");
            (await Named(db, ids["kohathites"], "rus")).Should().Be("Каафовы");
            (await Named(db, ids["caesar-title"], "ukr")).Should().Be("Кесар");
            (await Named(db, ids["elohim"], "rus")).Should().Be("Элохим");
            (await Named(db, ids["carthage"], "deu")).Should().Be("Karthago");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task<string?> Named(Essenthos.Core.Database.AppDbContext db, int entity, string language) =>
        (await EntityNames.Of(db, [entity], language, CancellationToken.None)).GetValueOrDefault(entity);

    [GeneratedRegex(@"^[\p{IsCyrillic}' \-(),]+$")]
    private static partial Regex Cyrillic();

    [GeneratedRegex(@"^[A-Za-zÄÖÜäöüß' \-]+$")]
    private static partial Regex German();
}
