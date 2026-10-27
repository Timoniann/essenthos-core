using System.Net;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Verbs;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Records;

[Collection(WitnessDatabaseCollection.Name)]
public sealed class OccupationTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly WitnessDatabase _database;
    private readonly OccupationLoader _loader;

    public OccupationTests(WitnessDatabase database)
    {
        _database = database;
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM occupation; DELETE FROM entity");
        _loader = new OccupationLoader(_db);
        foreach (var (slug, name, sex) in new[] { ("luke", "Luke", "male"), ("lydia", "Lydia", "female"), ("other", "Luke", "male") })
        {
            var entity = new Entity { Slug = slug, Name = name, Sex = sex, Kind = EntityKind.Person, Source = "test", SourceId = "test:" + slug };
            _db.Entities.Add(entity);
            _db.SaveChanges();
            _db.EntityNameForms.Add(new EntityNameForm { EntityId = entity.Id, Language = "rus", GrammaticalCase = "nominative",
                Form = slug == "lydia" ? "Лидия" : "Лука", Source = "test" });
        }
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static OccupationEvidence Witness(string kind = "profession", string reference = "COL 4:14", string quote = "beloved physician")
    {
        const string source = "Luke, the beloved physician, and Demas, greet you.";
        return new(kind, "KJV", reference, reference, quote, source, OccupationLoader.Digest(source));
    }

    private static OccupationPublication Publication() => new(1, "test-reviewed-publication",
        new("reviewed", "independent source reviewer", DateTimeOffset.Parse("2026-10-27T10:00:00Z")),
        new("King James Version, 1611", "Public domain", "https://example.org/frozen-source", new string('a', 64)),
        [new("physician", "trade", new()
        {
            ["eng"] = new("Physician", "physician", "physician", null),
            ["ukr"] = new("Лікар", "лікар", "лікарка", null),
            ["deu"] = new("Arzt", "Arzt", "Ärztin", null),
            ["spa"] = new("Médico", "médico", "médica", null),
        })], [new("luke", "physician", [Witness(), Witness("identity", "COL 4:14", "Luke")])]);

    private Task<OccupationLoadOutcome> Load(OccupationPublication publication)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(publication, OccupationLoader.Shape);
        return _loader.Load(bytes, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
    }

    [Fact]
    public async Task CatalogueAndDetailReadRealQueriesWithDistinctPeopleAndStableBoundedPaging()
    {
        var file = Publication();
        await Load(file with { Assertions = [.. file.Assertions, new("other", "physician", [Witness()]), file.Assertions[0]] });
        var catalogue = await OccupationEndpoints.Catalogue(_db, skip: -2, take: 500);
        catalogue.Skip.Should().Be(0);
        catalogue.Take.Should().Be(100);
        catalogue.Total.Should().Be(1);
        catalogue.Items.Single().PersonCount.Should().Be(2);
        catalogue.Categories.Single(c => c.Category == "trade").PersonCount.Should().Be(2);
        (await OccupationEndpoints.Detail(_db, "physician", skip: 0, take: 1))!.People.Single().Person.Slug.Should().Be("luke");
        (await OccupationEndpoints.Detail(_db, "physician", skip: 1, take: 1))!.People.Single().Person.Slug.Should().Be("other");
        (await OccupationEndpoints.Detail(_db, "physician", skip: 99))!.People.Should().BeEmpty();
    }

    [Fact]
    public async Task HeavenlyRolesRemainSeparateFromEarthlyOccupationsInPublicationAndQueries()
    {
        var file = Publication();
        var heavenly = file.Occupations[0] with { Slug = "heavenly-high-priest", Category = "heavenly_role" };
        await Load(file with { Occupations = [.. file.Occupations, heavenly],
            Assertions = [.. file.Assertions, new("other", heavenly.Slug, [Witness()])] });
        var heaven = await OccupationEndpoints.Catalogue(_db, category: "heavenly_role");
        heaven.Items.Should().ContainSingle().Which.Category.Should().Be("heavenly_role");
        heaven.Categories.Single(c => c.Category == "heavenly_role").PersonCount.Should().Be(1);
        (await OccupationEndpoints.Catalogue(_db, category: "trade")).Items.Single().Slug.Should().Be("physician");
        (await OccupationEndpoints.Person(_db, "other"))!.Items.Single().Occupation.Category.Should().Be("heavenly_role");
        (await OccupationEndpoints.Person(_db, "luke"))!.Items.Should().OnlyContain(p => p.Occupation.Category == "trade");
    }

    [Fact]
    public async Task CategoryCountsCountOnePersonOnceAcrossSeveralOccupations()
    {
        var file = Publication();
        await Load(file with { Occupations = [.. file.Occupations, file.Occupations[0] with { Slug = "healer" }],
            Assertions = [.. file.Assertions, new("luke", "healer", [Witness()])] });
        var result = await OccupationEndpoints.Catalogue(_db);
        result.Total.Should().Be(2);
        result.Categories.Single(c => c.Category == "trade").Should().Be(new OccupationCategoryResponse("trade", 2, 1));
    }

    [Theory]
    [InlineData("ukr", "лікарка")]
    [InlineData("deu", "Ärztin")]
    [InlineData("spa", "médica")]
    [InlineData("eng", "physician")]
    public async Task RoleWordsUseProseGenderWhileNamesUseBaseLanguage(string prose, string role)
    {
        await Load(Publication() with { Assertions = [new("lydia", "physician", [Witness()])] });
        var result = await OccupationEndpoints.Detail(_db, "physician", language: "rus", prose: prose);
        result!.People.Single().RoleName.Should().Be(role);
        result.People.Single().Person.LocalName.Should().Be("Лидия");
        var person = await OccupationEndpoints.Person(_db, "lydia", "rus", prose);
        person!.Items.Single().RoleName.Should().Be(role);
    }

    [Fact]
    public async Task SearchIncludesGenderedFormsAndOnlyPublishedOccupations()
    {
        await Load(Publication());
        (await OccupationEndpoints.Catalogue(_db, prose: "ukr", q: "ЛІКАРКА")).Total.Should().Be(1);
        (await OccupationEndpoints.Catalogue(_db, prose: "deu", q: "ärztin")).Total.Should().Be(1);
        (await OccupationEndpoints.Catalogue(_db, prose: "ukr", q: "%")).Total.Should().Be(0);
        (await OccupationEndpoints.Catalogue(_db, category: "office")).Items.Should().BeEmpty();
        (await OccupationEndpoints.Detail(_db, "missing")).Should().BeNull();
        (await OccupationEndpoints.Person(_db, "missing")).Should().BeNull();
        (await OccupationEndpoints.Person(_db, "lydia"))!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task EvidenceRetainsIdentityApartFromProfessionAndDoesNotServeReviewNotes()
    {
        var file = Publication();
        var assertion = file.Assertions[0] with { Origin = JsonSerializer.Deserialize<JsonElement>("{\"model\":\"test-model\",\"readingRun\":\"run-1\",\"reviewedBy\":\"working reviewer\"}") };
        await Load(file with { Assertions = [assertion] });
        var person = await OccupationEndpoints.Person(_db, "luke");
        var occupation = person!.Items.Single();
        occupation.Verses.Should().ContainSingle();
        occupation.Witnesses.Select(w => w.Kind).Should().BeEquivalentTo("profession", "identity");
        var json = JsonSerializer.Serialize(person, AppJsonSerializerContext.Default.PersonOccupationsResponse);
        json.Should().Contain("beloved physician").And.NotContain("working reviewer").And.NotContain("readingRun");
        (await _db.PersonOccupations.SingleAsync()).Provenance.Should().Contain("working reviewer").And.Contain("run-1");
        (await _db.OccupationWitnesses.FirstAsync()).Reference.Should().Be("COL 4:14");
    }

    [Fact]
    public async Task RepeatingThePinnedPublicationWritesNothingAndPreservesIds()
    {
        var file = Publication();
        await Load(file);
        var before = await Fingerprint();
        (await Load(file)).Should().Be(new OccupationLoadOutcome(0, 0, 1));
        (await Fingerprint()).Should().Equal(before);
    }

    [Fact]
    public async Task InvalidUnreviewedUnknownAndUnpinnedPublicationsPreservePreviousInventory()
    {
        var file = Publication();
        await Load(file);
        var before = await Fingerprint();
        foreach (var invalid in new[]
        {
            file with { Review = file.Review with { Status = "model-accepted" } },
            file with { Source = file.Source with { License = "" } },
            file with { Assertions = [new("missing", "physician", [Witness()])] },
            file with { Assertions = [new("luke", "physician", [Witness() with { Quote = "invented physician" }])] },
            file with { Assertions = [new("luke", "physician", [Witness() with { SourceSha256 = new string('b', 64) }])] },
            file with { Assertions = [new("luke", "physician", [Witness("identity")])] },
        })
            await FluentActions.Awaiting(() => Load(invalid)).Should().ThrowAsync<InvalidDataException>();
        await FluentActions.Awaiting(() => _loader.Load(JsonSerializer.SerializeToUtf8Bytes(file, OccupationLoader.Shape), new string('0', 64)))
            .Should().ThrowAsync<InvalidDataException>();
        (await Fingerprint()).Should().Equal(before);
    }

    [Fact]
    public async Task FailedReplacementRollsBackLabelsAssertionsAndEvidenceTogether()
    {
        var file = Publication();
        await Load(file);
        var before = await Fingerprint();
        await _db.Database.ExecuteSqlRawAsync("CREATE FUNCTION refuse_occupation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'refused'; END $$; CREATE TRIGGER refuse_occupation BEFORE INSERT ON person_occupation FOR EACH ROW EXECUTE FUNCTION refuse_occupation();");
        try
        {
            var changed = file with { Assertions = [new("lydia", "physician", [Witness()])] };
            changed.Occupations[0].Labels["eng"] = new("Changed", "changed", "changed", null);
            await FluentActions.Awaiting(() => Load(changed)).Should().ThrowAsync<DbUpdateException>();
            (await Fingerprint()).Should().Equal(before);
            (await OccupationEndpoints.Catalogue(_db)).Items.Single().Name.Should().Be("Physician");
        }
        finally { await _db.Database.ExecuteSqlRawAsync("DROP TRIGGER refuse_occupation ON person_occupation; DROP FUNCTION refuse_occupation()"); }
    }

    [Theory]
    [InlineData("Host=localhost;Port=5437;Database=essenthos_occupation_preview;Username=test")]
    [InlineData("Host=localhost;Port=5439;Database=essenthos_core;Username=test")]
    [InlineData("Host=remote;Port=5439;Database=essenthos_occupation_preview;Username=test")]
    public void PreviewRefusesAnythingExceptNamedLocalTestDatabase(string connection) =>
        FluentActions.Invoking(() => OccupationPreview.Guard(connection)).Should().Throw<InvalidOperationException>();

    private async Task<string[]> Fingerprint() => await _db.PersonOccupations.AsNoTracking().OrderBy(p => p.Id)
        .Select(p => p.Id + "|" + p.PersonId + "|" + p.OccupationId + "|" + p.PublicationId + "|" + p.PublicationSha256 + "|" + p.Provenance)
        .ToArrayAsync();

    [Fact]
    public async Task PreviewVerbSeedsAndRepeatsExactSourceVersesWithGuardedArguments()
    {
        var connection = new NpgsqlConnectionStringBuilder(_db.Database.GetConnectionString()!)
            { Database = "essenthos_occupation_preview_test_" + Environment.ProcessId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options;
        await using var preview = new AppDbContext(options);
        var folder = Path.Combine(Path.GetTempPath(), "occupation-preview-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var publicationPath = Path.Combine(folder, "publication.json");
        var peoplePath = Path.Combine(folder, "people.json");
        var file = Publication();
        await File.WriteAllBytesAsync(publicationPath, JsonSerializer.SerializeToUtf8Bytes(file, OccupationLoader.Shape));
        await File.WriteAllTextAsync(peoplePath, JsonSerializer.Serialize(new OccupationPreviewPeople(
            [new("luke", "Luke", "male", new() { ["rus"] = "Лука" })]), OccupationLoader.Shape));
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(publicationPath)));
        var services = new ServiceCollection().AddScoped(_ => new AppDbContext(options)).BuildServiceProvider();
        try
        {
            var verb = ForgeVerbs.Find("occupation-preview")!;
            var arguments = new[] { "occupation-preview", publicationPath, hash, peoplePath };
            verb.Accepts(arguments).Should().BeTrue();
            var run = new ForgeRun(services, NullLogger.Instance, folder, connection.ConnectionString);
            (await verb.Run(run, arguments)).Should().Be(0);
            (await verb.Run(run, arguments)).Should().Be(0);
            (await preview.PersonOccupations.CountAsync()).Should().Be(1);
            (await preview.EntityNameForms.SingleAsync()).Form.Should().Be("Лука");
            var words = await preview.Words.OrderBy(w => w.Position).Select(w => w.Surface + w.Trailer).ToListAsync();
            string.Concat(words).Should().Be(file.Assertions[0].Witnesses[0].SourceText);
            var reference = await preview.VerseReferences.SingleAsync();
            (reference.CanonicalBook, reference.CanonicalChapter, reference.CanonicalVerse).Should().Be((51, 4, 14));
            (await preview.Texts.SingleAsync()).Licence.Should().Be(file.Source.License);
            File.Exists(Path.Combine(folder, "Essenthos", "recipe.json")).Should().BeFalse();
        }
        finally
        {
            await services.DisposeAsync();
            await preview.Database.EnsureDeletedAsync();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task RoutesUseSourceGeneratedSerializationAndRejectUnboundedSearchAndUnknownCategory()
    {
        await Load(Publication());
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped(_ => _database.NewContext());
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default));
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.MapGroup("/v1").MapOccupations();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        foreach (var path in new[] { "/v1/occupations", "/v1/occupations/physician", "/v1/entities/luke/occupations" })
        {
            var response = await client.GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Contain("physician");
        }
        (await client.GetAsync("/v1/occupations?category=unknown")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/v1/occupations?category=heavenly_role")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/v1/occupations?q=" + new string('x', 121))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/v1/entities/missing/occupations")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await app.StopAsync();
    }
}
