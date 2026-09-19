using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// How each text spells a name, counted from the words that name it, and what a reader in another
/// language is then shown and can search by.
/// </summary>
public sealed class RenderingRuleTests
{
    private static readonly IReadOnlyDictionary<(int, string), string> NoNominatives =
        new Dictionary<(int, string), string>();

    private static NamedWord Word(int verse, int position, string surface, string language = "ukr", string? lemma = null,
        int entity = 1, int text = 1, string trailer = " ") =>
        new(entity, text, verse, position, surface, trailer, lemma, language);

    private static List<Rendering> Of(IReadOnlyDictionary<(int, string), string>? nominatives, params NamedWord[] words) =>
        [.. Renderings.Of(words, nominatives ?? NoNominatives)];

    /// <summary>
    /// The Ohienko Bible prints the genitive more often than the nominative; the heading is the
    /// shortest spelling printed nearly as often as the commonest, and every declined form is kept.
    /// </summary>
    [Fact]
    public void ADecliningTextIsHeadedByItsNominativeAndKeepsEveryCase()
    {
        var renderings = Of(null,
            Word(1, 1, "Аарона"), Word(2, 1, "Аарона"), Word(3, 1, "Аарона"),
            Word(4, 1, "Аарон"), Word(5, 1, "Аарон"),
            Word(6, 1, "Ааронові"));

        renderings.Single(r => r.Heading).Form.Should().Be("Аарон");
        renderings.Select(r => (r.Form, r.Occurrences)).Should().BeEquivalentTo(
            [("Аарона", 3), ("Аарон", 2), ("Ааронові", 1)]);
    }

    /// <summary>The nominative this corpus already holds wins, when the text prints it.</summary>
    [Fact]
    public void TheNominativeHeldForTheLanguageHeadsWhenTheTextPrintsIt()
    {
        var held = new Dictionary<(int, string), string> { [(1, "ukr")] = "Сарра" };

        var renderings = Of(held,
            Word(1, 1, "Сарри"), Word(2, 1, "Сарри"), Word(3, 1, "Сарра"), Word(4, 1, "Саррі"));

        renderings.Single(r => r.Heading).Form.Should().Be("Сарра");
    }

    /// <summary>
    /// An annotation carried onto a pronoun, a conjunction or a common noun is not a spelling of the
    /// name, and neither is a lower-case word.
    /// </summary>
    [Fact]
    public void WordsThatAreNotTheNameAreKeptOut()
    {
        var renderings = Of(null,
            Word(1, 1, "Aaron", "deu"), Word(2, 1, "Aaron", "deu"), Word(3, 1, "Aarons", "deu"),
            Word(4, 1, "und", "deu"), Word(5, 1, "Priester", "deu"), Word(6, 1, "Er", "deu"));

        renderings.Select(r => r.Form).Should().BeEquivalentTo(["Aaron", "Aarons"]);
    }

    /// <summary>Quotation marks and the English possessive belong to the sentence.</summary>
    [Fact]
    public void PunctuationAndThePossessiveAreTakenOff()
    {
        var renderings = Of(null,
            Word(1, 1, "“Aaron,", "eng"), Word(2, 1, "Aaron’s", "eng"), Word(3, 1, "Aaron's", "eng"),
            Word(4, 1, "Moses’", "eng", entity: 2));

        renderings.Where(r => r.EntityId == 1).Should().ContainSingle()
            .Which.Should().Be(new Rendering(1, 1, "Aaron", "aaron", 3, true));
        renderings.Single(r => r.EntityId == 2).Form.Should().Be("Moses");
    }

    /// <summary>
    /// Two adjacent words named as one place are one name, and spellings a search cannot tell apart
    /// are one spelling.
    /// </summary>
    [Fact]
    public void AdjacentWordsNamedAsOneEntityAreOneName()
    {
        var renderings = Of(null,
            Word(1, 4, "Beth", "eng"), Word(1, 5, "Shemesh", "eng"),
            Word(2, 7, "Beth-Shemesh.", "eng"));

        renderings.Should().ContainSingle().Which.Form.Should().Be("Beth Shemesh");
        renderings[0].Occurrences.Should().Be(2);
    }

    /// <summary>The same name ending one sentence and opening the next is two namings, not one name.</summary>
    [Fact]
    public void PunctuationBetweenTwoNamingsKeepsThemApart()
    {
        var renderings = Of(null,
            Word(1, 3, "Mahujael", "deu", trailer: ". "), Word(1, 4, "Mahujael", "deu"));

        renderings.Should().ContainSingle().Which.Should().Be(new Rendering(1, 1, "Mahujael", "mahujael", 2, true));
    }

    /// <summary>
    /// A word an annotation spilled onto and the name itself, each printed once, leave the name: the
    /// longer spelling wins the tie and the short word is not of its family.
    /// </summary>
    [Fact]
    public void ATieGoesToTheNameAndNotToTheWordBesideIt()
    {
        var renderings = Of(null, Word(1, 1, "De", "spa"), Word(1, 3, "Isharitas", "spa"));

        renderings.Should().ContainSingle().Which.Form.Should().Be("Isharitas");
    }

    /// <summary>
    /// A witness with a lemma is spelled by its lemma, which carries no article and no case, and a
    /// script without capitals is not held to having them.
    /// </summary>
    [Fact]
    public void AnOriginalIsSpelledByItsLemma()
    {
        var renderings = Of(null,
            Word(1, 1, "אַהֲרֹ֖ן", "hbo", "אַהֲרֹן"), Word(2, 1, "אַהֲרֹן֙", "hbo", "אַהֲרֹן"),
            Word(3, 1, "אהרן", "hbo", "אהרנ/", text: 2),
            Word(4, 1, "Ἀαρὼν", "grc", "Ἀαρών", text: 3));

        renderings.Single(r => r.TextId == 1).Should().Be(new Rendering(1, 1, "אַהֲרֹן", "אהרן", 2, true));
        renderings.Single(r => r.TextId == 2).Form.Should().Be("אהרן", "a segmented lemma is not a name");
        renderings.Single(r => r.TextId == 3).Should().Be(new Rendering(1, 3, "Ἀαρών", "ααρων", 1, true));
    }

    [Theory]
    [InlineData("Aarón", "aaron")]
    [InlineData("Beth-shemesh", "bethshemesh")]
    [InlineData("Beth Shemesh", "bethshemesh")]
    [InlineData("Ґалаад", "галаад")]
    [InlineData("Аарон", "аарон")]
    [InlineData("Ἰησοῦς", "ιησουσ")]
    [InlineData("Straße", "strasse")]
    public void NamesFoldTheWayTheyAreTyped(string name, string folded) =>
        NameFolding.Fold(name).Should().Be(folded);
}

[Collection(WitnessDatabaseCollection.Name)]
public sealed class EntityRenderingTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Text _ukrainian;
    private readonly Text _spanish;
    private readonly Text _hebrew;
    private readonly Entity _aaron;
    private readonly Entity _moses;

    public EntityRenderingTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        _hebrew = Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo", (1, 1, ["אהרן", "משה"]));
        _ukrainian = Corpus.Add(_db, "UBIO", TextKind.Translation, "ukr",
            (1, 1, ["І", "сказав", "Аарон", "до", "Мойсея"]),
            (1, 2, ["і", "Аарона", "і", "Мойсея"]),
            (1, 3, ["Ааронові"]));
        _spanish = Corpus.Add(_db, "RV1909", TextKind.Translation, "spa", (1, 1, ["Aarón", "y", "Moisés"]));
        _aaron = Record("aaron", "Aaron");
        _moses = Record("moses", "Moses");
        _db.SaveChanges();

        Name(_hebrew, 1, 1, 1, _aaron);
        Name(_hebrew, 1, 1, 2, _moses);
        Name(_ukrainian, 1, 1, 3, _aaron);
        Name(_ukrainian, 1, 1, 5, _moses);
        Name(_ukrainian, 1, 2, 2, _aaron);
        Name(_ukrainian, 1, 2, 4, _moses);
        Name(_ukrainian, 1, 3, 1, _aaron);
        Name(_spanish, 1, 1, 1, _aaron);
        Name(_spanish, 1, 1, 3, _moses);

        // Moses has a Ukrainian nominative of his own, which outranks any text's spelling.
        _db.EntityNameForms.Add(new EntityNameForm
        {
            EntityId = _moses.Id, Language = "ukr", GrammaticalCase = GrammaticalCases.Nominative,
            Form = "Мойсей", Method = LinkMethod.ModelReading, Confidence = 1, Source = "a test",
        });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    private Entity Record(string slug, string name)
    {
        var entity = new Entity { Kind = EntityKind.Person, Slug = slug, Name = name, SourceId = slug, Source = "a test" };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Name(Text text, int chapter, int verse, int position, Entity entity) =>
        _db.WordEntities.Add(new WordEntity
        {
            WordId = _db.WordAt(text, chapter, verse, position).Id,
            EntityId = entity.Id,
            Method = LinkMethod.Manual,
            Source = "a test",
        });

    private Task Load() => new EntityRenderingLoader(_db, NullLogger<EntityRenderingLoader>.Instance).Load();

    [Fact]
    public async Task EachTextsSpellingsAreCountedFromItsNamedWords()
    {
        await Load();

        var aaron = await _db.EntityRenderings.AsNoTracking()
            .Where(r => r.EntityId == _aaron.Id)
            .Select(r => new { r.Text!.Slug, r.Form, r.Occurrences, r.Heading })
            .ToListAsync();

        aaron.Should().BeEquivalentTo(new[]
        {
            new { Slug = "BHSA", Form = "אהרן", Occurrences = 1, Heading = true },
            new { Slug = "UBIO", Form = "Аарон", Occurrences = 1, Heading = true },
            new { Slug = "UBIO", Form = "Аарона", Occurrences = 1, Heading = false },
            new { Slug = "UBIO", Form = "Ааронові", Occurrences = 1, Heading = false },
            new { Slug = "RV1909", Form = "Aarón", Occurrences = 1, Heading = true },
        });
    }

    /// <summary>A second load leaves the same rows, not twice as many.</summary>
    [Fact]
    public async Task LoadingAgainRebuildsRatherThanAdds()
    {
        await Load();
        var first = await _db.EntityRenderings.CountAsync();

        await Load();

        (await _db.EntityRenderings.CountAsync()).Should().Be(first);
    }

    /// <summary>
    /// A reader is shown the nominative the corpus holds in their language. Where it holds none, a
    /// Spanish reader is shown how the Spanish text spells the name, and a Ukrainian one the English
    /// name — a Ukrainian text's commonest spelling may be any case of it. In English, the headword
    /// and no local name at all.
    /// </summary>
    [Fact]
    public async Task TheLocalNameIsTheHeldNominativeThenAnUndeclinedTextsSpelling()
    {
        await Load();

        var ukrainian = await EntityNames.Of(_db, [_aaron.Id, _moses.Id], "ukr", CancellationToken.None);
        ukrainian.Should().BeEquivalentTo(new Dictionary<int, string> { [_moses.Id] = "Мойсей" });

        (await EntityNames.Of(_db, [_aaron.Id], "spa", CancellationToken.None))[_aaron.Id].Should().Be("Aarón");
        (await EntityNames.Of(_db, [_aaron.Id], "eng", CancellationToken.None)).Should().BeEmpty();
        (await EntityNames.Of(_db, [_aaron.Id], "deu", CancellationToken.None)).Should().BeEmpty(
            "no German text names him here, so a German reader falls back to the English name");
    }

    /// <summary>The query the index orders by gives the same names as the lookup a page uses.</summary>
    [Theory]
    [InlineData("ukr")]
    [InlineData("spa")]
    [InlineData("deu")]
    [InlineData("eng")]
    public async Task TheIndexAndThePageAgreeOnTheLocalName(string language)
    {
        await Load();

        var ordered = await EntityNames.Localised(_db, _db.Entities, language)
            .Select(l => new { l.Entity.Id, l.LocalName })
            .ToListAsync();
        var looked = await EntityNames.Of(_db, [_aaron.Id, _moses.Id], language, CancellationToken.None);

        ordered.Should().OnlyContain(row => row.LocalName == looked.GetValueOrDefault(row.Id));
    }

    [Theory]
    [InlineData("Аарон")]
    [InlineData("Aaron")]
    [InlineData("aaron")]
    [InlineData("אהרן")]
    [InlineData("Ааронові")]
    public async Task AnyNameInAnyLanguageFindsTheEntity(string typed)
    {
        await Load();

        var found = await EntityNames.Matching(_db, _db.Entities, typed).Select(e => e.Slug).ToListAsync();

        found.Should().Equal("aaron");
    }

    [Fact]
    public async Task ASpellingNoTextPrintsFindsNothing()
    {
        await Load();

        (await EntityNames.Matching(_db, _db.Entities, "аароні").AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ASearchInTheReadersLanguageHeadsWithTheEntityCalledThat()
    {
        await Load();

        var ranked = await EntityNames.ByRelevance(
                _db,
                EntityNames.Localised(_db, EntityNames.Matching(_db, _db.Entities, "Мойсей"), "ukr"),
                "Мойсей",
                "ukr")
            .Select(l => new { l.Entity.Slug, l.Shown })
            .ToListAsync();

        ranked.Should().Equal(new { Slug = "moses", Shown = "Мойсей" });
    }
}
