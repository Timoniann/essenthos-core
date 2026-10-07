using Essenthos.Core.Corpus;
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
/// The names a rendered line puts into a case, from the file a generation pass produces to the
/// sentence a reader is shown.
///
/// The line under test is the broken one this work exists to repair: Aaron's, which read
/// <em>брат Moses, з племені Levites</em> against the live corpus on 2026-09-09 because nothing had
/// declined the names his clauses point at. Everything else here is about what must not happen on
/// the way to fixing it.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class NameFormTests : IDisposable
{
    private const string Model = "claude-sonnet-5";

    private readonly AppDbContext _db;

    public NameFormTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");

        Add("aaron", EntityKind.Person, "Aaron", (2, 4, 14));
        Add("moses", EntityKind.Person, "Moses", (2, 4, 14));
        Add("levites", EntityKind.People, "Levites", (2, 4, 14));
        Add("hebron", EntityKind.Place, "Hebron", (1, 13, 18));

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    private void Add(string slug, EntityKind kind, string name, params (int Book, int Chapter, int Verse)[] verses)
    {
        var entity = new Entity
        {
            Kind = kind,
            Slug = slug,
            Name = name,
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

    private static string Fixtures(string kind, string folder) =>
        Path.Combine(AppContext.BaseDirectory, "Resources", kind, folder);

    private static string Absent => Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}");

    private Task<NameFormOutcome> Decline(string folder) =>
        new EntityNameFormLoader(
                _db,
                Configured(NameFormFiles.ConfigurationKey, Fixtures("name-forms", folder)),
                NullLogger<EntityNameFormLoader>.Instance)
            .Load(Absent);

    private Task<DescriptorOutcome> Describe(string folder) =>
        new EntityDescriptorLoader(
                _db,
                Configured(DescriptorFiles.ConfigurationKey, Fixtures("descriptors", folder)),
                NullLogger<EntityDescriptorLoader>.Instance)
            .Load(Absent);

    private static IConfiguration Configured(string key, string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();

    private async Task<string> Line(string slug, string language) =>
        string.Concat((await Descriptors.Of(_db, slug, language, default))!.Parts.Select(p => p.Text));

    private Task<EntityNameForm?> Form(string slug, string language, string grammaticalCase) =>
        _db.EntityNameForms
            .FirstOrDefaultAsync(f => f.Entity!.Slug == slug
                                      && f.Language == language
                                      && f.GrammaticalCase == grammaticalCase);

    /// <summary>
    /// The whole point of the pass, in one sentence: the clauses are the same either side of it and
    /// only the names change.
    /// </summary>
    [Fact]
    public async Task ADescriptionShowsTheEnglishNameUntilSomethingHasDeclinedIt()
    {
        await Describe("aaron");

        (await Line("aaron", DescriptorPhrasings.Ukrainian))
            .Should().Be("брат Moses, з племені Levites");

        var outcome = await Decline("named");

        outcome.Declined.Should().Be(2);
        (await Line("aaron", DescriptorPhrasings.Ukrainian))
            .Should().Be("брат Мойсея, з племені Левитів");
    }

    /// <summary>
    /// A Ukrainian page over the Synodal: the words of the line are the interface's and the names
    /// in it the chosen text's, each in the case the Ukrainian phrase puts it in.
    /// </summary>
    [Fact]
    public async Task TheLineSpeaksTheInterfacesWordsAroundTheChosenTextsNames()
    {
        await Describe("aaron");
        _db.EntityNameForms.Add(Held("moses", "rus", GrammaticalCases.Genitive, "Моисея"));
        _db.EntityNameForms.Add(Held("moses", "rus", GrammaticalCases.Nominative, "Моисей"));
        await _db.SaveChangesAsync();

        var line = await Descriptors.Of(_db, "aaron", DescriptorPhrasings.Ukrainian, default, names: "rus");

        line!.Language.Should().Be(DescriptorPhrasings.Ukrainian);
        string.Concat(line.Parts.Select(p => p.Text)).Should().StartWith("брат Моисея");
        (await Line("aaron", "rus")).Should().StartWith("brother of ", "a line asked in Russian alone has no Russian words to be said in");
    }

    /// <summary>
    /// The guard is on this loader's own rows, and per entity. Guarding on whether the table holds
    /// anything is what left a cold database with no name resolutions at all, and this
    /// loader writes into a table the descriptor pass is already filling, so the table is never
    /// empty by the time it runs.
    /// </summary>
    [Fact]
    public async Task WhatThisLoaderHasAlreadyDeclinedIsLeftAloneAndTheRestIsNot()
    {
        await Decline("named");
        var again = await Decline("named");

        again.AlreadyLoaded.Should().BeTrue();
        again.Skipped.Should().Be(2);
        again.Forms.Should().Be(0);

        _db.EntityNameForms.Count(f => f.Entity!.Slug == "moses").Should().Be(6);
    }

    [Fact]
    public async Task AMissingCaseIsFilledAfterAnEarlierBatchDeclinedTheEntity()
    {
        await Decline("named");
        var missing = (await Form("moses", "ukr", GrammaticalCases.Genitive))!;
        _db.EntityNameForms.Remove(missing);
        var preserved = (await Form("moses", "ukr", GrammaticalCases.Nominative))!;
        preserved.Source = "an editor's held form";
        await _db.SaveChangesAsync();
        var preservedId = preserved.Id;
        var preservedSource = preserved.Source;

        var repaired = await Decline("named");

        repaired.Forms.Should().Be(1);
        (await Form("moses", "ukr", GrammaticalCases.Genitive))!.Form.Should().Be("Мойсея");
        var held = (await Form("moses", "ukr", GrammaticalCases.Nominative))!;
        held.Id.Should().Be(preservedId);
        held.Source.Should().Be(preservedSource);
        held.Form.Should().Be("Мойсей");
        var beforeRepeat = await _db.EntityNameForms.OrderBy(f => f.Id)
            .Select(f => new { f.Id, f.EntityId, f.Language, f.GrammaticalCase, f.Form, f.Source })
            .ToListAsync();

        var again = await Decline("named");

        again.Forms.Should().Be(0);
        (await _db.EntityNameForms.OrderBy(f => f.Id)
            .Select(f => new { f.Id, f.EntityId, f.Language, f.GrammaticalCase, f.Form, f.Source })
            .ToListAsync()).Should().Equal(beforeRepeat);
    }

    /// <summary>
    /// A form the descriptor pass wrote for its own subject is the one left standing: the two
    /// loaders share a table with a unique key per entity, language and case, so the second writer
    /// decides rather than colliding.
    /// </summary>
    [Fact]
    public async Task AFormTheDescriptorPassAlreadyWroteIsNotOverwritten()
    {
        _db.EntityNameForms.Add(Held("moses", "ukr", GrammaticalCases.Genitive, "Мойсея"));
        await _db.SaveChangesAsync();

        var outcome = await Decline("named");

        outcome.Refused.AlreadyHeld.Should().Be(1);
        (await Form("moses", "ukr", GrammaticalCases.Genitive))!.Source
            .Should().StartWith(EntityDescriptorLoader.SourcePrefix);
    }

    /// <summary>
    /// <c>DescriptorPhrasings</c> renders <em>похований у </em> and then the locative, so a
    /// locative that carries its own preposition reaches a reader as <em>похований у в Авані</em>.
    /// The preposition comes off on the way in, and a stored form carrying one is superseded rather
    /// than left standing — which is what makes the rows already in the corpus repairable by a
    /// later file.
    /// </summary>
    [Fact]
    public async Task AFormCarryingThePrepositionThePhraseSuppliesIsStoredWithoutIt()
    {
        _db.EntityNameForms.Add(Held("hebron", "ukr", GrammaticalCases.Locative, "в Хевроні"));
        await _db.SaveChangesAsync();

        var outcome = await Decline("prepositions");

        outcome.Bared.Should().Be(1, "the file gives \"у Хевроні\"");
        outcome.Repaired.Should().Be(1, "the corpus held \"в Хевроні\"");
        outcome.Refused.AlreadyHeld.Should().Be(0);
        (await Form("hebron", "ukr", GrammaticalCases.Locative))!.Form.Should().Be("Хевроні");
    }

    /// <summary>
    /// Two files naming one entity settle it form by form, not record by record.
    ///
    /// It is the ordinary case rather than a re-run: a repair pass puts back the locatives the
    /// doubled preposition broke and a target pass asks for the four languages the client speaks,
    /// and neither is the other's second attempt. Taking the later record whole dropped whatever it
    /// happened not to carry — the locative here, and a Spanish nominative in the other direction —
    /// and the row it dropped was one nothing would ask for again, because the entity counts as
    /// declined.
    /// </summary>
    [Fact]
    public async Task ALaterFileFillsWhatAnEarlierOneLeftOutAndOverwritesOnlyWhatItAlsoSays()
    {
        var outcome = await Decline("twofiles");

        outcome.Replaced.Should().Be(1, "both files give Hebron a Ukrainian nominative");
        outcome.Forms.Should().Be(5);

        var locative = await Form("hebron", "ukr", GrammaticalCases.Locative);
        locative!.Form.Should().Be("Хевроні", "the preposition comes off on the way in");
        locative.Source.Should().Contain("the earlier pass", "the later file has no locative");

        var nominative = await Form("hebron", "ukr", GrammaticalCases.Nominative);
        nominative!.Form.Should().Be("Хеврін");
        nominative.Source.Should().Contain("the later pass");

        (await Form("hebron", "spa", GrammaticalCases.Nominative))!.Form.Should().Be("Hebrón");
    }

    /// <summary>
    /// What a file may say that the corpus will not store. None of it is guessed at instead: a
    /// language with no form falls back to the English name, and an invented ending is the one
    /// outcome a reader cannot tell from a correct one.
    /// </summary>
    [Fact]
    public async Task AFormNothingCanPlaceIsRefusedAndCounted()
    {
        var outcome = await Decline("refused");

        outcome.Refused.UnknownEntity.Should().Be(2, "nobody-at-all is not in the encyclopedia");
        outcome.Refused.UnknownCase.Should().Be(1, "a form cannot be held in the vocative");
        outcome.Refused.Empty.Should().Be(1, "the genitive is whitespace");
        outcome.Forms.Should().Be(1);
        (await Form("moses", "ukr", GrammaticalCases.Genitive)).Should().BeNull();
    }

    /// <summary>A checkout without the files loads nothing and says which key would point at them.</summary>
    [Fact]
    public async Task ATreeWithNoFilesLoadsNothingRatherThanFailing()
    {
        var outcome = await new EntityNameFormLoader(
                _db,
                new ConfigurationBuilder().Build(),
                NullLogger<EntityNameFormLoader>.Instance)
            .Load(Absent);

        outcome.NoFiles.Should().BeTrue();
        outcome.Forms.Should().Be(0);
    }

    /// <summary>
    /// The rule itself, which both loaders share. It takes off what the phrase around the name
    /// supplies and never the name — <em>Zur</em> is a Midianite prince and <em>zur</em> is a
    /// German preposition, and a one-word form is left exactly as it stands.
    /// </summary>
    [Theory]
    [InlineData("ukr", "в Авані", "Авані")]
    [InlineData("ukr", "у Хевроні", "Хевроні")]
    [InlineData("ukr", "Хевроні", "Хевроні")]
    [InlineData("ukr", "Авел-Бет-Мааха", "Авел-Бет-Мааха")]
    [InlineData("deu", "des Baches", "Baches")]
    [InlineData("deu", "Zur", "Zur")]
    [InlineData("deu", "Zurs", "Zurs")]
    [InlineData("spa", "de los moabitas", "moabitas")]
    [InlineData("eng", "the LORD", "LORD")]
    [InlineData("heb", "в Авані", "в Авані")]
    public void WhatThePhraseSuppliesComesOffAndNothingElseDoes(
        string language, string form, string bare) =>
        NameForms.Bare(language, form).Should().Be(bare);

    /// <summary>A row as the descriptor pass left it, which is what the corpus holds today.</summary>
    private EntityNameForm Held(
        string slug, string language, string grammaticalCase, string form) =>
        new()
        {
            EntityId = _db.Entities.Single(e => e.Slug == slug).Id,
            Language = language,
            GrammaticalCase = grammaticalCase,
            Form = form,
            Method = LinkMethod.ModelReading,
            Confidence = 1,
            Source = $"{EntityDescriptorLoader.SourcePrefix} {Model}, asked 2026-09-06",
        };
}
