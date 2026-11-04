using System.Net;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
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

    private readonly AppDbContext _db;
    private readonly WitnessDatabase _database;
    private readonly OccupationLoader _loader;
    private readonly Dictionary<string, Text> _texts = [];

    public OccupationTests(WitnessDatabase database)
    {
        _database = database;
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM occupation; DELETE FROM merged_record; DELETE FROM entity; DELETE FROM link; DELETE FROM provenance; DELETE FROM strong_rendering; DELETE FROM text");
        _loader = new OccupationLoader(_db);
        foreach (var (slug, name, sex) in new[] { ("luke", "Luke", "male"), ("lydia", "Lydia", "female"), ("other", "Luke", "male"), ("saul-2", "Paul", "male") })
        {
            var entity = new Entity { Slug = slug, Name = name, Sex = sex, Kind = EntityKind.Person, Source = "test", SourceId = "test:" + slug };
            _db.Entities.Add(entity);
            _db.SaveChanges();
            _db.EntityNameForms.Add(new EntityNameForm { EntityId = entity.Id, Language = "rus", GrammaticalCase = "nominative",
                Form = slug == "lydia" ? "Лидия" : "Лука", Source = "test" });
        }

        foreach (var (slug, kind, language, words) in new[]
                 {
                     ("NESTLE1904", TextKind.CriticalEdition, "grc", Greek),
                     ("KJV", TextKind.Translation, "eng", English),
                     ("UBIO", TextKind.Translation, "ukr", Ukrainian),
                 })
        {
            var text = Corpus.Add(_db, slug, kind, language, (4, 14, words.Split(' ')));
            _db.SaveChanges();
            _texts[slug] = _db.In(text, 51);
        }

        _db.SaveChanges();
        Word("NESTLE1904", 5).StrongNumber = "G2395";
        Word("KJV", 4).StrongNumber = "G2395";
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Word Word(string text, int position) => _db.WordAt(_texts[text], 4, 14, position);

    /// <summary>A link of <paramref name="relation"/> between words of two texts.</summary>
    private void Link(LinkRelation relation, (string Text, int[] Positions) from, (string Text, int[] Positions) to)
    {
        var link = new Link
        {
            FromTextId = _texts[from.Text].Id, ToTextId = _texts[to.Text].Id, Relation = relation,
            Method = LinkMethod.StatedBySource, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.SaveChanges();
        _db.LinkClaims.Add(new LinkClaim { LinkId = link.Id, Method = LinkMethod.StatedBySource, Provenance = link.Provenance });
        foreach (var position in from.Positions)
            _db.LinkWords.Add(new LinkWord { LinkId = link.Id, WordId = Word(from.Text, position).Id, Side = LinkSide.From });
        foreach (var position in to.Positions)
            _db.LinkWords.Add(new LinkWord { LinkId = link.Id, WordId = Word(to.Text, position).Id, Side = LinkSide.To });
        _db.SaveChanges();
    }

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

    private async Task<string[]> Fingerprint() =>
    [
        .. await _db.PersonOccupations.AsNoTracking().OrderBy(p => p.Id)
            .Select(p => p.Id + "|" + p.PersonId + "|" + p.OccupationId + "|" + p.Source).ToListAsync(),
        .. await _db.OccupationWitnesses.AsNoTracking().OrderBy(w => w.Id)
            .Select(w => w.Id + "|" + w.Kind + "|" + w.Text + "|" + w.Quote + "|" + w.CanonicalBook).ToListAsync(),
        .. await _db.OccupationWords.AsNoTracking().OrderBy(w => w.Id).Select(w => w.Id + "|" + w.WordId).ToListAsync(),
    ];

    [Fact]
    public async Task QuotationsAreFoundInTheirVersesAndPlacedWhereTheCorpusPlacesThem()
    {
        await _loader.Load(File());
        var witnesses = await _db.OccupationWitnesses.OrderBy(w => w.Text).ToListAsync();
        witnesses.Select(w => (w.Text, w.Reference, w.CanonicalBook, w.CanonicalChapter, w.CanonicalVerse))
            .Should().Equal(("KJV", "COL 4:14", 51, 4, 14), ("NESTLE1904", "COL 4:14", 51, 4, 14));
        (await _db.PersonOccupations.SingleAsync()).Source.Should().Be("a test reading");
    }

    [Fact]
    public async Task ASecondLoadWritesNothingAndKeepsEveryId()
    {
        Link(LinkRelation.Renders, ("KJV", [4]), ("NESTLE1904", [5]));
        await _loader.Load(File());
        var before = await Fingerprint();
        (await _loader.Load(File())).Should().Be(new OccupationLoadOutcome(0, 0, 1, 0, 0));
        (await Fingerprint()).Should().Equal(before);
    }

    [Fact]
    public async Task AQuotationNotInItsVerseStopsTheLoadNamingItAndKeepsWhatWasHeld()
    {
        await _loader.Load(File());
        var before = await Fingerprint();
        foreach (var wrong in new[]
                 {
                     File(new OccupationClaim("luke", "physician", [Quote(quote: "Λουκᾶς ὁ ἰατρὸς ὁ σοφός")])),
                     File(new OccupationClaim("luke", "physician", [Quote() with { Reference = "COL 4:15" }])),
                     File(new OccupationClaim("luke", "physician", [Quote() with { Text = "SBLGNT" }])),
                 })
        {
            await FluentActions.Awaiting(() => _loader.Load(wrong)).Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*not in their verse*COL 4:1*");
        }

        await FluentActions.Awaiting(() => _loader.Load(File(new OccupationClaim("missing", "physician", [Quote()]))))
            .Should().ThrowAsync<InvalidDataException>().WithMessage("*missing*");
        await FluentActions.Awaiting(() => _loader.Load(File(new OccupationClaim("luke", "physician", [Quote("identity")]))))
            .Should().ThrowAsync<InvalidDataException>();
        (await Fingerprint()).Should().Equal(before);
    }

    [Fact]
    public async Task AFoldedRecordsSlugReachesTheRecordItWasFoldedInto()
    {
        var luke = await _db.Entities.SingleAsync(e => e.Slug == "luke");
        _db.MergedRecords.Add(new MergedRecord { Slug = "lucas", EntityId = luke.Id, Name = "Lucas", RecordSourceId = "test:lucas", RecordSource = "test", Reason = "the same man", Source = "test" });
        await _db.SaveChangesAsync();
        await _loader.Load(File(new OccupationClaim("lucas", "physician", [Quote()])));
        (await _db.PersonOccupations.SingleAsync()).PersonId.Should().Be(luke.Id);
    }

    [Fact]
    public async Task TheWordCarryingTheNumberIsMarkedAndTheWordsTheLinksSayRenderIt()
    {
        // One word for one word: the King James renders ἰατρός by physician.
        Link(LinkRelation.Renders, ("KJV", [4]), ("NESTLE1904", [5]));
        // Two words for one: the link says лікар and Лука go with ἰατρός together, not which of them it is.
        Link(LinkRelation.Renders, ("UBIO", [3, 5]), ("NESTLE1904", [5]));
        await _loader.Load(File(new OccupationClaim("luke", "physician", [Quote()])));
        var marked = await _db.OccupationWords.Select(w => w.Word!.Surface).ToListAsync();
        marked.Should().BeEquivalentTo("ἰατρὸς", "physician");

        // The number on the Ukrainian word is the link's own word for which one it is.
        Word("UBIO", 5).StrongNumber = "G2395";
        await _db.SaveChangesAsync();
        (await _loader.Load(File(new OccupationClaim("luke", "physician", [Quote()])))).WordsWritten.Should().Be(1);
        (await _db.OccupationWords.Select(w => w.Word!.Surface).ToListAsync()).Should().BeEquivalentTo("ἰατρὸς", "physician", "лікар");
    }

    [Fact]
    public async Task AWordAloneOnItsSideIsMarkedUnlessItIsACommonWordOfItsText()
    {
        var english = _texts["KJV"];
        _db.AddBook(english, 52, "Thessalonians", (1, 1, [.. Enumerable.Repeat("the", 300)]));
        await _db.SaveChangesAsync();
        await _db.Database.ExecuteSqlRawAsync("UPDATE word SET normalised_text = lower(text), strong_number = NULL WHERE text_id = {0}", english.Id);
        Link(LinkRelation.Renders, ("KJV", [4]), ("NESTLE1904", [5]));
        Link(LinkRelation.Renders, ("KJV", [2]), ("NESTLE1904", [5]));
        await _loader.Load(File(new OccupationClaim("luke", "physician", [Quote()])));
        (await _db.OccupationWords.Select(w => w.Word!.Surface).ToListAsync()).Should().BeEquivalentTo("ἰατρὸς", "physician");
    }

    [Fact]
    public async Task OfSeveralWordsALinkGivesTogetherTheOneTheTextRendersTheNumberByIsMarked()
    {
        await _db.Database.ExecuteSqlRawAsync("UPDATE word SET normalised_text = lower(text) WHERE text_id = {0}", _texts["UBIO"].Id);
        _db.StrongRenderings.Add(new StrongRendering { StrongNumber = "G2395", TextId = _texts["UBIO"].Id, Rank = 1, Phrase = "лікар", Uses = 2 });
        await _db.SaveChangesAsync();
        Link(LinkRelation.Renders, ("UBIO", [3, 5]), ("NESTLE1904", [5]));
        await _loader.Load(File(new OccupationClaim("luke", "physician", [Quote()])));
        (await _db.OccupationWords.Select(w => w.Word!.Surface).ToListAsync()).Should().BeEquivalentTo("ἰατρὸς", "лікар");
    }

    [Fact]
    public async Task AnIdentityQuotationMarksNothing()
    {
        await _loader.Load(File(new OccupationClaim("luke", "physician",
            [Quote(text: "KJV", quote: "Luke the beloved"), Quote("identity", quote: "ὁ ἰατρὸς ὁ ἀγαπητὸς")])));
        (await _db.OccupationWords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TheVersesListAndTheChapterMarkTheWordsNamingTheOccupation()
    {
        Link(LinkRelation.Renders, ("KJV", [4]), ("NESTLE1904", [5]));
        await _loader.Load(File());
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
        await _loader.Load(File(
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
        await _loader.Load(file);
        (await Descriptors.Of(_db, ["luke"], "eng", default)).Should().NotContainKey("luke");
        await _loader.Load(file with { Occupations = [Physician("designation")] });
        (await Descriptors.Of(_db, ["luke"], "eng", default))["luke"].Counted!.Single().Relation.Should().Be(OccupationLines.Designation);
    }

    [Fact]
    public async Task CatalogueAndDetailCountPeopleOnceAndPageStably()
    {
        await _loader.Load(File(
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
        await _loader.Load(File());
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
        await _loader.Load(File(new OccupationClaim("lydia", "physician", [Quote()])));
        var result = await OccupationEndpoints.Detail(_db, "physician", language: "rus", prose: prose);
        result!.People.Single().RoleName.Should().Be(role);
        result.People.Single().Person.LocalName.Should().Be("Лидия");
        (await OccupationEndpoints.Person(_db, "lydia", "rus", prose))!.Items.Single().RoleName.Should().Be(role);
    }

    [Fact]
    public async Task SearchReadsTheGenderedFormsAndOnlyHeldOccupations()
    {
        await _loader.Load(File());
        (await OccupationEndpoints.Catalogue(_db, prose: "ukr", q: "ЛІКАРКА")).Total.Should().Be(1);
        (await OccupationEndpoints.Catalogue(_db, prose: "deu", q: "ärztin")).Total.Should().Be(1);
        (await OccupationEndpoints.Catalogue(_db, prose: "ukr", q: "%")).Total.Should().Be(0);
        (await OccupationEndpoints.Catalogue(_db, category: "office")).Items.Should().BeEmpty();
        (await OccupationEndpoints.Detail(_db, "missing")).Should().BeNull();
        (await OccupationEndpoints.Person(_db, "missing")).Should().BeNull();
        (await OccupationEndpoints.Person(_db, "lydia"))!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task AnOccupationTheFileNoLongerNamesIsRemovedWithItsPeople()
    {
        await _loader.Load(File() with { Occupations = [Physician(), Physician() with { Slug = "healer" }] });
        await _loader.Load(File());
        (await _db.Occupations.Select(o => o.Slug).ToListAsync()).Should().Equal("physician");
    }

    [Fact]
    public async Task TheShippedFileIsValidAndSaysWhatTheOwnerRuled()
    {
        await using var stream = System.IO.File.OpenRead(TestResources.Shipped($"Essenthos/{OccupationLoader.FileName}"));
        var file = (await JsonSerializer.DeserializeAsync<OccupationFile>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
        OccupationLoader.Validate(file);
        var claims = file.Assertions.ToDictionary(a => (a.Person, a.Occupation));
        foreach (var ruled in new[] { ("rahab", "harlot"), ("judas", "thief"), ("barjesus", "sorcerer"), ("barjesus", "false-prophet") })
        {
            claims.Should().ContainKey(ruled);
        }

        file.Occupations.Single(o => o.Slug == "harlot").Category.Should().Be("designation");
        claims.Keys.Should().NotContain(("jezebel", "false-prophet"));
        file.Occupations.Where(o => o.Category == "designation").Should().OnlyContain(o => o.StrongNumbers.Count > 0);
    }

    [Fact]
    public async Task RoutesUseSourceGeneratedSerializationAndRejectUnboundedSearchAndUnknownCategory()
    {
        await _loader.Load(File());
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
