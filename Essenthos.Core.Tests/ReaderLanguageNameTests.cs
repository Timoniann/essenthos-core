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
/// The records that had no name in Ukrainian, German, Russian or Spanish are given one: persons,
/// peoples, places, titles and terms by the name-forms batches of 2026-10-14, the objects and
/// appointed times in their own record files.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed partial class ReaderLanguageNameTests(WitnessDatabase database)
{
    private const string BatchFile = "zz-complete-2026-10-14.jsonl";

    private const string SpanishBatchFile = "zz-complete-spa-2026-10-14.jsonl";

    private static readonly string[] Languages = ["ukr", "deu", "rus"];

    private static string Shipped => TestResources.Shipped(Path.Combine("Essenthos", "name-forms"));

    private static List<NameFormRecord> Dated() =>
        NameFormFiles.Read(Shipped).Records.Where(r => r.AskedAt == "2026-10-14").ToList();

    private static List<NameFormRecord> Batch() => Dated().Where(r => !r.Names!.ContainsKey("spa")).ToList();

    private static List<NameFormRecord> SpanishBatch() => Dated().Where(r => r.Names!.ContainsKey("spa")).ToList();

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
    public void TheSpanishBatchNamesEachRecordOnceByItsNominativeOnly()
    {
        var records = SpanishBatch();

        records.Select(r => r.Entity).Should().OnlyHaveUniqueItems()
            .And.Contain(["aaron", "kingofachshaph", "carthage", "azariah-18", "elohim"]);
        foreach (var record in records)
        {
            record.Names!.Keys.Should().Equal(["spa"], record.Entity);
            var forms = record.Names["spa"];
            forms.Keys.Should().Equal(["nominative"], $"{record.Entity}: Spanish does not decline, the phrase supplies \"de\"");
            Spanish().IsMatch(forms["nominative"]).Should().BeTrue($"{record.Entity} is named \"{forms["nominative"]}\"");
            NameForms.Bare("spa", forms["nominative"], GrammaticalCases.Nominative)
                .Should().Be(forms["nominative"], $"{record.Entity} carries no word the phrase supplies");
        }
    }

    [Fact]
    public void TheObjectsAndAppointedTimesAreNamedInRussianAndSpanish()
    {
        var records = ThingFiles.Read().ToDictionary(r => r.Slug);

        records["ark-of-the-covenant"].Names!["rus"].Should().Be("Ковчег завета");
        records["passover"].Names!["rus"].Should().Be("Пасха");
        records["feast-of-booths"].Names!["rus"].Should().Be("Праздник кущей");
        records["sauls-spear"].Names!["deu"].Should().Be("Spieß Sauls");
        records["sauls-spear"].Names!["spa"].Should().Be("Lanza de Saúl");
        records["jar-of-manna"].Names!["spa"].Should().Be("Urna del maná");
        records["second-passover"].Names!["spa"].Should().Be("Pascua del segundo mes");
    }

    [Fact]
    public async Task EveryFormOfTheBatchIsLoadedAndNamesItsRecord()
    {
        await using var db = database.NewContext();
        var ids = await Load(db, BatchFile, Batch());

        (await Named(db, ids["chelub-father-of-ezri"], "deu")).Should().Be("Chelub");
        (await Named(db, ids["kohathites"], "rus")).Should().Be("Каафовы");
        (await Named(db, ids["caesar-title"], "ukr")).Should().Be("Кесар");
        (await Named(db, ids["elohim"], "rus")).Should().Be("Элохим");
        (await Named(db, ids["carthage"], "deu")).Should().Be("Karthago");
    }

    [Fact]
    public async Task EveryFormOfTheSpanishBatchIsLoadedAndNamesItsRecord()
    {
        await using var db = database.NewContext();
        var ids = await Load(db, SpanishBatchFile, SpanishBatch());

        (await Named(db, ids["aaron"], "spa")).Should().Be("Aarón");
        (await Named(db, ids["kingofachshaph"], "spa")).Should().Be("rey de Achsaph");
        (await Named(db, ids["carthage"], "spa")).Should().Be("Cartago");
        (await Named(db, ids["azariah-18"], "spa")).Should().Be("Azarías");
        (await Named(db, ids["abaddon"], "spa")).Should().Be("Abaddon");
    }

    /// <summary>
    /// Loads one batch file over an encyclopedia holding exactly its records, and holds the loader to
    /// writing every form once: nothing refused, nothing taken off, nothing written a second time.
    /// </summary>
    private static async Task<Dictionary<string, int>> Load(
        Essenthos.Core.Database.AppDbContext db,
        string file,
        IReadOnlyCollection<NameFormRecord> records)
    {
        await db.Database.ExecuteSqlRawAsync("TRUNCATE entity RESTART IDENTITY CASCADE");
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
            File.Copy(Path.Combine(Shipped, file), Path.Combine(folder, file));
            var config = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { [NameFormFiles.ConfigurationKey] = folder }).Build();
            var loader = new EntityNameFormLoader(db, config, NullLogger<EntityNameFormLoader>.Instance);

            var outcome = await loader.Load(folder);

            outcome.Refused.Total.Should().Be(0, outcome.Refused.ToString());
            outcome.Bared.Should().Be(0, "no form carries a word the phrase supplies");
            outcome.Forms.Should().Be(records.Sum(r => r.Names!.Sum(language => language.Value.Count)));
            (await loader.Load(folder)).Forms.Should().Be(0);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        return await db.Entities.ToDictionaryAsync(e => e.Slug, e => e.Id);
    }

    private static async Task<string?> Named(Essenthos.Core.Database.AppDbContext db, int entity, string language) =>
        (await EntityNames.Of(db, [entity], language, CancellationToken.None)).GetValueOrDefault(entity);

    [GeneratedRegex(@"^[\p{IsCyrillic}' \-(),]+$")]
    private static partial Regex Cyrillic();

    [GeneratedRegex(@"^[A-Za-zÄÖÜäöüß' \-]+$")]
    private static partial Regex German();

    /// <summary>Spanish letters, with the circumflex the Reina-Valera of 1909 prints for a hard ch.</summary>
    [GeneratedRegex(@"^[A-Za-zÁÉÍÓÚÜÑáéíóúüñÂÊÎÔÛâêîôû \-]+$")]
    private static partial Regex Spanish();
}
