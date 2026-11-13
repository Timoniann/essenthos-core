using System.Net;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Conversion;
using Essenthos.Core.Indexes;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using Npgsql;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Essenthos.Core.Tests.Records;

[Collection(WitnessDatabaseCollection.Name)]
public sealed class OccupationTests : IDisposable
{
    private const string Greek = "ἀσπάζεται ὑμᾶς Λουκᾶς ὁ ἰατρὸς ὁ ἀγαπητὸς καὶ Δημᾶς";
    private const string English = "Luke the beloved physician and Demas greet you";
    private const string Ukrainian = "Вітає вас Лука улюблений лікар і Димас";

    private static readonly VerseKey Colossians = new(51, 4, 14);

    private static readonly (string Slug, TextKind Kind, string Language, string Words)[] Texts =
    [
        ("NESTLE1904", TextKind.CriticalEdition, "grc", Greek),
        ("KJV", TextKind.Translation, "eng", English),
        ("UBIO", TextKind.Translation, "ukr", Ukrainian),
    ];

    private readonly AppDbContext _db;
    private readonly WitnessDatabase _database;
    private readonly Dictionary<string, Text> _texts = [];
    private readonly string _resources = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));
    private readonly ProductStore _store;
    private readonly List<(string From, string To, int[] FromPositions, int[] ToPositions)> _links = [];
    private readonly Dictionary<(string Text, int Position), string> _strong = new()
    {
        [("NESTLE1904", 5)] = "G2395", [("KJV", 4)] = "G2395",
    };

    public OccupationTests(WitnessDatabase database)
    {
        _database = database;
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM occupation; DELETE FROM merged_record; DELETE FROM entity; DELETE FROM link; DELETE FROM provenance; DELETE FROM strong_rendering; DELETE FROM text");
        foreach (var (slug, name, sex) in new[] { ("luke", "Luke", "male"), ("lydia", "Lydia", "female"), ("other", "Luke", "male"), ("saul-2", "Paul", "male") })
        {
            var entity = new Entity { Slug = slug, Name = name, Sex = sex, Kind = EntityKind.Person };
            _db.Entities.Add(entity);
            _db.SaveChanges();
            _db.EntityNameForms.Add(new EntityNameForm { EntityId = entity.Id, Language = "rus", GrammaticalCase = "nominative",
                Form = slug == "lydia" ? "Лидия" : "Лука", Source = "test" });
        }

        foreach (var (slug, kind, language, words) in Texts)
        {
            var text = Corpus.Add(_db, slug, kind, language, (4, 14, words.Split(' ')));
            _db.SaveChanges();
            _texts[slug] = _db.In(text, 51);
        }

        _db.SaveChanges();
        _store = new ProductStore(_resources);
        Records(["luke", "lydia", "other", "saul-2"]);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    private void Records(string[] people, params (string Slug, string Into)[] folds)
    {
        Write("records/records", people.Select(slug => new string?[] { slug, "person", null, null, slug, "male", null, null, null, null, null, null }));
        Write("records/folds", folds.Select(f => new string?[] { f.Slug, f.Into, f.Slug, null, "manual", null, "test", "the same man" }));
    }

    private void Write(string product, IEnumerable<string?[]> rows)
    {
        var file = RecordProducers.File(product);
        using var writer = _store.Create(product, file.Kind, file.Schema(), "test", []);
        writer.WriteAll(rows);
        writer.Complete();
    }

    /// <summary>
    /// The three texts as products: their declarations, their words at Colossians 4:14 with the Strong numbers the test
    /// gave (the King James's <paramref name="extra"/> words in another verse; with <paramref name="lower"/> each word's
    /// lower-cased form), their Strong indexes from <paramref name="renderings"/>, and the links as finals.
    /// </summary>
    private void Products(bool lower = false, string[]? extra = null, string? englishServed = null,
        params (string Text, string Number, string Phrase, int Uses)[] renderings)
    {
        foreach (var (slug, _, language, words) in Texts)
        {
            using (var writer = _store.Create(TextField.ProductOf(slug), ProductKind.Hand, TextField.Schema, "test", []))
            {
                writer.WriteAll([new TextField("slug", slug), new TextField("language", language)]);
                writer.Complete();
            }

            var rows = words.Split(' ').Select((surface, at) => (Key: new WordKey(slug, Colossians, at + 1), Surface: surface)).ToList();
            if (slug == "KJV" && extra is not null)
            {
                rows.AddRange(extra.Select((surface, at) => (new WordKey(slug, new VerseKey(52, 1, 1), at + 1), surface)));
            }

            using (var writer = _store.Create(TextWord.ProductOf(slug), ProductKind.Machine, TextWord.Schema(slug), "test", [], header =>
                   {
                       if (slug == "KJV" && englishServed is not null)
                       {
                           header.Notes.Add(new(StrongTerms.Served, englishServed));
                       }
                   }))
            {
                writer.WriteAll(rows.Select(w => new TextWord(w.Key, w.Surface, " ", null, _strong.GetValueOrDefault((slug, w.Key.Position)),
                    null, null, lower ? w.Surface.ToLowerInvariant() : null, null, false, null, null, null)));
                writer.Complete();
            }

            using (var writer = _store.Create(IndexProducts.Of(slug, IndexProducts.Strong), ProductKind.Machine, StrongIndexRow.Schema, "test", []))
            {
                writer.WriteAll(renderings.Where(r => r.Text == slug).SelectMany(r => Enumerable.Range(0, r.Uses)
                    .Select(i => new StrongIndexRow(r.Number, "NESTLE1904", $"51.4:14.{i + 1}", "51.4:14.1", r.Phrase, "1", "stated"))));
                writer.Complete();
            }
        }

        foreach (var pair in _links.GroupBy(l => (l.From, l.To)))
        {
            using var writer = _store.Create(MappingProducts.Of(MappingProducts.Pair(pair.Key.From, pair.Key.To), MappingProducts.Final),
                ProductKind.Machine, FinalRow.Schema, "test", []);
            writer.WriteAll(pair.Select((l, i) => new FinalRow(i + 1,
                string.Join(' ', l.FromPositions.Select(p => new WordKey(l.From, Colossians, p).Own)),
                string.Join(' ', l.ToPositions.Select(p => new WordKey(l.To, Colossians, p).Own)), "renders", null, true, "stated", null, "a test")));
            writer.Complete();
        }
    }

    /// <summary>A link of the merge's final between words of two texts at Colossians 4:14.</summary>
    private void Link((string Text, int[] Positions) from, (string Text, int[] Positions) to) =>
        _links.Add((from.Text, to.Text, from.Positions, to.Positions));

    private OccupationOutcome Make(OccupationFile file)
    {
        Directory.CreateDirectory(Path.Combine(_resources, "Essenthos"));
        System.IO.File.WriteAllText(Path.Combine(_resources, "Essenthos", OccupationFiles.FileName),
            JsonSerializer.Serialize(file, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        if (!_store.Exists(TextWord.ProductOf("KJV")))
        {
            Products();
        }

        return OccupationFiles.Write(new ProductStore(_resources), force: false);
    }

    private List<string?[]> Rows(string product)
    {
        using var reader = new ProductStore(_resources).Open(product, RecordProducers.File(product).Schema());
        return [.. reader.ReadAll()];
    }

    /// <summary>The words marked, as their surfaces.</summary>
    private List<string> Marked() =>
    [
        .. Rows(OccupationFiles.WordsProduct).Select(r =>
            Texts.Single(t => t.Slug == r[2]).Words.Split(' ')[WordKey.Parse(r[3]!, r[2]!).Position - 1]),
    ];

    /// <summary>Makes the files and puts the occupation files into the database, as the build's records load does.</summary>
    private async Task Load(OccupationFile file)
    {
        Make(file);
        var files = RecordFiles.All.Where(f => f.Product.Contains("occupation", StringComparison.Ordinal)).ToList();
        await using var connection = new NpgsqlConnection(_db.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await RecordFiles.LoadFiles(connection, transaction, new ProductStore(_resources), files, default);
        await transaction.CommitAsync();
    }

    private Word Word(string text, int position) => _db.WordAt(_texts[text], 4, 14, position);

    private static OccupationQuote Quote(string kind = "profession", string text = "NESTLE1904", string quote = "Λουκᾶς ὁ ἰατρὸς") =>
        new(kind, text, "COL 4:14", quote);

    private static OccupationConcept Physician(string category = "trade") => new("physician", category, ["G2395"], new()
    {
        ["eng"] = new("Physician", "Physician", "Physician"),
        ["ukr"] = new("Лікар", "Лікар", "Лікарка"),
        ["deu"] = new("Arzt", "Arzt", "Ärztin"),
        ["spa"] = new("Médico", "Médico", "Médica"),
    });

    private static OccupationFile File(params OccupationClaim[] claims) => new(1, "a test reading", [Physician()],
        claims.Length > 0 ? claims : [new("luke", "physician", [Quote(), Quote(text: "KJV", quote: "Luke the beloved physician")])]);

    [Fact]
    public void QuotationsAreFoundInTheirVersesAndPlacedWhereTheFramePlacesThem()
    {
        using (var writer = _store.Create(FrameFile.Product("KJV"), ProductKind.Machine, FrameRow.Schema, "test", []))
        {
            writer.WriteAll([new FrameRow(Colossians, null, [new VerseKey(51, 4, 13)], "moved")]);
            writer.Complete();
        }

        Make(File());
        Rows(OccupationFiles.WitnessesProduct).Select(r => (r[4], r[5], r[6]))
            .Should().Equal(("NESTLE1904", "COL 4:14", "51.4:14"), ("KJV", "COL 4:14", "51.4:13"));
        Rows(OccupationFiles.PeopleProduct).Single().Should().Equal("luke", "physician", "a test reading");
        Rows(OccupationFiles.LabelsProduct("ukr")).Single().Should().Equal("physician", "Лікар", "Лікар", "Лікарка", null);
    }

    [Fact]
    public void ASecondRunWithNothingMovedWritesNothingAndAMovedFileWritesAgain()
    {
        Link(("KJV", [4]), ("NESTLE1904", [5]));
        Make(File()).Written.Should().BeTrue();
        var before = System.IO.File.ReadAllBytes(_store.FullPath(OccupationFiles.WordsProduct, ProductKind.Machine));

        Make(File()).Written.Should().BeFalse();
        System.IO.File.ReadAllBytes(_store.FullPath(OccupationFiles.WordsProduct, ProductKind.Machine)).Should().Equal(before);
        Make(File(new OccupationClaim("other", "physician", [Quote()]))).Should().Be(new OccupationOutcome(true, 1, 1, 1, 2));
    }

    [Fact]
    public void AQuotationNotInItsVerseStopsTheProducerNamingItAndKeepsWhatWasMade()
    {
        Make(File());
        var before = Rows(OccupationFiles.WitnessesProduct);
        foreach (var wrong in new[]
                 {
                     File(new OccupationClaim("luke", "physician", [Quote(quote: "Λουκᾶς ὁ ἰατρὸς ὁ σοφός")])),
                     File(new OccupationClaim("luke", "physician", [Quote() with { Reference = "COL 4:15" }])),
                     File(new OccupationClaim("luke", "physician", [Quote() with { Text = "SBLGNT" }])),
                 })
        {
            FluentActions.Invoking(() => Make(wrong)).Should().Throw<InvalidDataException>().WithMessage("*not in their verse*COL 4:1*");
        }

        FluentActions.Invoking(() => Make(File(new OccupationClaim("missing", "physician", [Quote()]))))
            .Should().Throw<InvalidDataException>().WithMessage("*missing*");
        FluentActions.Invoking(() => Make(File(new OccupationClaim("luke", "physician", [Quote("identity")]))))
            .Should().Throw<InvalidDataException>();
        Rows(OccupationFiles.WitnessesProduct).Should().BeEquivalentTo(before, o => o.WithStrictOrdering());
    }

    [Fact]
    public void AFoldedRecordsSlugReachesTheRecordItWasFoldedInto()
    {
        Records(["luke", "lydia", "other", "saul-2"], ("lucas", "luke"));
        Make(File(new OccupationClaim("lucas", "physician", [Quote()])));
        Rows(OccupationFiles.PeopleProduct).Single()[0].Should().Be("luke");

        FluentActions.Invoking(() => Make(File(new OccupationClaim("lucas", "physician", [Quote()]), new OccupationClaim("luke", "physician", [Quote()]))))
            .Should().Throw<InvalidDataException>().WithMessage("*one record, luke*");
    }

    [Fact]
    public void TheWordCarryingTheNumberIsMarkedAndTheWordsTheLinksSayRenderIt()
    {
        // One word for one word: the King James renders ἰατρός by physician.
        Link(("KJV", [4]), ("NESTLE1904", [5]));
        // Two words for one: the link says лікар and Лука go with ἰατρός together, not which of them it is.
        Link(("UBIO", [3, 5]), ("NESTLE1904", [5]));
        Make(File(new OccupationClaim("luke", "physician", [Quote()])));
        Marked().Should().BeEquivalentTo("ἰατρὸς", "physician");
        Rows(OccupationFiles.WordsProduct).Select(r => (r[2], r[3])).Should().Contain(("KJV", "51.4:14.4"));

        // The number on the Ukrainian word is the link's own word for which one it is.
        _strong[("UBIO", 5)] = "G2395";
        Products();
        Make(File(new OccupationClaim("luke", "physician", [Quote()])));
        Marked().Should().BeEquivalentTo("ἰατρὸς", "physician", "лікар");
    }

    [Fact]
    public void ANumberTheTextMayNotServeMarksNothingByItself()
    {
        Link(("KJV", [1, 4]), ("NESTLE1904", [5]));
        Products(englishServed: "no: a test's terms");
        Make(File(new OccupationClaim("luke", "physician", [Quote()])));
        Marked().Should().BeEquivalentTo("ἰατρὸς");
    }

    [Fact]
    public void AWordAloneOnItsSideIsMarkedUnlessItIsACommonWordOfItsText()
    {
        _strong.Remove(("KJV", 4));
        Link(("KJV", [4]), ("NESTLE1904", [5]));
        Link(("KJV", [2]), ("NESTLE1904", [5]));
        Products(lower: true, extra: [.. Enumerable.Repeat("the", 300)]);
        Make(File(new OccupationClaim("luke", "physician", [Quote()])));
        Marked().Should().BeEquivalentTo("ἰατρὸς", "physician");
    }

    [Fact]
    public void OfSeveralWordsALinkGivesTogetherTheOneTheTextRendersTheNumberByIsMarked()
    {
        Link(("UBIO", [3, 5]), ("NESTLE1904", [5]));
        Products(lower: true, renderings: ("UBIO", "G2395", "лікар", 2));
        Make(File(new OccupationClaim("luke", "physician", [Quote()])));
        Marked().Should().BeEquivalentTo("ἰατρὸς", "лікар");
    }

    [Fact]
    public void AClaimRestsOnItsOwnBookOnlyWhereNoQuotationSaysIt()
    {
        Make(File(new OccupationClaim("luke", "physician", [Quote(kind: "own-book")])));

        Rows(OccupationFiles.WitnessesProduct).Single()[3].Should().Be("own-book");
        var both = () => OccupationFiles.Validate(File(new OccupationClaim("luke", "physician", [Quote(), Quote(kind: "own-book")])));
        both.Should().Throw<InvalidDataException>();
        var neither = () => OccupationFiles.Validate(File(new OccupationClaim("luke", "physician", [Quote(kind: "identity")])));
        neither.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void AnIdentityQuotationMarksNothing()
    {
        Make(File(new OccupationClaim("luke", "physician",
            [Quote(text: "KJV", quote: "Luke the beloved"), Quote("identity", quote: "ὁ ἰατρὸς ὁ ἀγαπητὸς")])));
        Rows(OccupationFiles.WordsProduct).Should().BeEmpty();
    }

    [Fact]
    public async Task TheVersesListAndTheChapterMarkTheWordsNamingTheOccupation()
    {
        Link(("KJV", [4]), ("NESTLE1904", [5]));
        await Load(File());
        (await _db.OccupationWords.Select(w => w.WordId).ToListAsync()).Should().BeEquivalentTo([Word("KJV", 4).Id, Word("NESTLE1904", 5).Id]);
        var verse = (await VerseEndpoints.Read(_db, _texts["KJV"].Id, [51_004_014], "luke", default, occupation: "physician")).Single();
        verse.Marks.Should().ContainSingle().Which.Should().Be(new VerseMarkResponse(17, 26));
        verse.Text[17..26].Should().Be("physician");

        var chapter = await ChapterReading.ReadChapter(_db, _texts["KJV"].Id, 51, 4, default);
        var words = chapter.Single().Words;
        words.Single(w => w.Text == "physician").Roles.Should().Equal(new WordRoleResponse("physician", "trade", "luke"));
        words.Where(w => w.Text != "physician").Should().OnlyContain(w => w.Roles == null);
    }

    [Fact]
    public async Task TheLineSaysTheTradeInTheInterfaceLanguageAgreeingWithThePerson()
    {
        await Load(File(
            new OccupationClaim("luke", "physician", [Quote()]),
            new OccupationClaim("lydia", "physician", [Quote()])));
        var paul = await _db.Entities.SingleAsync(e => e.Slug == "saul-2");
        var luke = await _db.Entities.SingleAsync(e => e.Slug == "luke");
        _db.EntityDescriptors.Add(new EntityDescriptor
        {
            EntityId = luke.Id, Ordinal = 1, Relation = DescriptorRelations.CompanionOf, TargetEntityId = paul.Id,
            CanonicalBook = 55, CanonicalChapter = 4, CanonicalVerse = 11, Method = LinkMethod.Manual, Source = "test",
        });
        await _db.SaveChangesAsync();

        var english = (await Descriptors.Of(_db, ["luke"], "eng", default))["luke"];
        string.Concat(english.Parts.Select(p => p.Text)).Should().Be("physician, companion of Paul");
        var said = english.Counted!.Single();
        said.Relation.Should().Be(OccupationLines.Trade);
        said.At.Should().Be(0);
        said.References!.Single().Should().Be(BookReferences.At(51, 4, 14));
        said.Parts!.Single().Entity.Should().BeEquivalentTo(new { Kind = "occupation", Slug = "physician", Name = "physician" });

        string.Concat((await Descriptors.Of(_db, ["lydia"], "ukr", default))["lydia"].Parts.Select(p => p.Text)).Should().Be("лікарка");
        string.Concat((await Descriptors.Of(_db, ["luke"], "deu", default))["luke"].Parts.Select(p => p.Text)).Should().StartWith("Arzt, ");
    }

    [Fact]
    public async Task OnlyTradesAndDesignationsStandInTheLine()
    {
        var file = File() with { Occupations = [Physician("office")] };
        await Load(file);
        (await Descriptors.Of(_db, ["luke"], "eng", default)).Should().NotContainKey("luke");
        await Load(file with { Occupations = [Physician("designation")] });
        (await Descriptors.Of(_db, ["luke"], "eng", default))["luke"].Counted!.Single().Relation.Should().Be(OccupationLines.Designation);
    }

    [Fact]
    public async Task CatalogueAndDetailCountPeopleOnceAndPageStably()
    {
        await Load(File(
            new OccupationClaim("luke", "physician", [Quote(), Quote(text: "KJV", quote: "Luke the beloved physician")]),
            new OccupationClaim("other", "physician", [Quote()])));
        var catalogue = await OccupationEndpoints.Catalogue(_db, skip: -2, take: 500);
        catalogue.Skip.Should().Be(0);
        catalogue.Take.Should().Be(200);
        catalogue.Items.Single().PersonCount.Should().Be(2);
        catalogue.Categories.Single(c => c.Category == "trade").Should().Be(new OccupationCategoryResponse("trade", 1, 2));
        (await OccupationEndpoints.Detail(_db, "physician", skip: 0, take: 1))!.People.Single().Person.Slug.Should().Be("luke");
        (await OccupationEndpoints.Detail(_db, "physician", skip: 1, take: 1))!.People.Single().Person.Slug.Should().Be("other");
        (await OccupationEndpoints.Detail(_db, "physician", skip: 99))!.People.Should().BeEmpty();
    }

    [Fact]
    public async Task APractitionerRowCarriesThePersonsLine()
    {
        await Load(File());
        var row = (await OccupationEndpoints.Detail(_db, "physician", prose: "eng"))!.People.Single();
        string.Concat(row.Person.Descriptor!.Parts.Select(p => p.Text)).Should().Be("physician");
    }

    [Theory]
    [InlineData("ukr", "Лікарка")]
    [InlineData("deu", "Ärztin")]
    [InlineData("spa", "Médica")]
    [InlineData("eng", "Physician")]
    public async Task RoleWordsFollowTheInterfaceAndTheGenderWhileNamesFollowTheText(string prose, string role)
    {
        await Load(File(new OccupationClaim("lydia", "physician", [Quote()])));
        var result = await OccupationEndpoints.Detail(_db, "physician", language: "rus", prose: prose);
        result!.People.Single().RoleName.Should().Be(role);
        result.People.Single().Person.LocalName.Should().Be("Лидия");
        (await OccupationEndpoints.Person(_db, "lydia", "rus", prose))!.Items.Single().RoleName.Should().Be(role);
    }

    [Fact]
    public async Task SearchReadsTheGenderedFormsAndOnlyHeldOccupations()
    {
        await Load(File());
        (await OccupationEndpoints.Catalogue(_db, prose: "ukr", q: "ЛІКАРКА")).Total.Should().Be(1);
        (await OccupationEndpoints.Catalogue(_db, prose: "deu", q: "ärztin")).Total.Should().Be(1);
        (await OccupationEndpoints.Catalogue(_db, prose: "ukr", q: "%")).Total.Should().Be(0);
        (await OccupationEndpoints.Catalogue(_db, category: "office")).Items.Should().BeEmpty();
        (await OccupationEndpoints.Detail(_db, "missing")).Should().BeNull();
        (await OccupationEndpoints.Person(_db, "missing")).Should().BeNull();
        (await OccupationEndpoints.Person(_db, "lydia"))!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task TheShippedFileIsValidAndSaysWhatTheOwnerRuled()
    {
        await using var stream = System.IO.File.OpenRead(TestResources.Shipped($"Essenthos/{OccupationFiles.FileName}"));
        var file = (await JsonSerializer.DeserializeAsync<OccupationFile>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
        OccupationFiles.Validate(file);
        var claims = file.Assertions.ToDictionary(a => (a.Person, a.Occupation));
        foreach (var ruled in new[] { ("rahab", "harlot"), ("judas", "thief"), ("barjesus", "sorcerer"), ("barjesus", "false-prophet"),
                     ("joshua", "military-commander"), ("barak", "military-commander") })
        {
            claims.Should().ContainKey(ruled);
        }

        file.Occupations.Single(o => o.Slug == "harlot").Category.Should().Be("designation");
        claims.Keys.Should().NotContain(("jezebel", "false-prophet"));
        file.Occupations.Should().NotContain(o => o.Slug == "ancestral-house-chief");
        file.Occupations.Where(o => o.Category == "designation").Should().OnlyContain(o => o.StrongNumbers.Count > 0);
    }

    [Fact]
    public async Task RoutesUseSourceGeneratedSerializationAndRejectUnboundedSearchAndUnknownCategory()
    {
        await Load(File());
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
            (await response.Content.ReadAsStringAsync()).Should().Contain("physician").And.NotContain("a test reading");
        }

        (await client.GetAsync("/v1/occupations?category=unknown")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/v1/occupations?category=designation")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/v1/occupations?q=" + new string('x', 121))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/v1/entities/missing/occupations")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await app.StopAsync();
    }
}
