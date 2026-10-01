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
        int entity = 1, int text = 1, string trailer = " ", string? renders = null, double? guess = null) =>
        new(entity, text, verse, position, surface, trailer, lemma, language, renders, guess);

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

    /// <summary>
    /// Peter is Κηφᾶς in the Greek and Кифа in the Ohienko Bible, which opens nothing like Петро.
    /// The words linked to that other Greek name keep their own spellings, and the heading stays.
    /// </summary>
    [Fact]
    public void AnotherNameInTheOriginalKeepsItsOwnSpellings()
    {
        var renderings = Of(null,
            Word(1, 1, "Петро", renders: "Πέτρος"), Word(2, 1, "Петро", renders: "Πέτρος"),
            Word(3, 1, "Петра", renders: "Πέτρος"), Word(8, 1, "Петро", renders: "Πέτρος"),
            Word(4, 1, "Кифа", renders: "Κηφᾶς"), Word(5, 1, "Кифа", renders: "Κηφᾶς"),
            Word(6, 1, "Кифі", renders: "Κηφᾶς"),
            Word(7, 1, "Скеля", renders: "Πέτρος"));

        renderings.Select(r => (r.Form, r.Occurrences, r.Heading)).Should().BeEquivalentTo(
            [("Петро", 3, true), ("Петра", 1, false), ("Кифа", 2, false), ("Кифі", 1, false)]);
    }

    /// <summary>
    /// A word linked to another name only once is as likely an annotation carried onto the next word,
    /// and a lower-case lemma or a Hebrew one is not a name the words can be grouped by.
    /// </summary>
    [Fact]
    public void AnotherNameNeedsMoreThanOneWordAndACapital()
    {
        var renderings = Of(null,
            Word(1, 1, "Aaron", "deu", renders: "Ἀαρών"), Word(2, 1, "Aaron", "deu", renders: "Ἀαρών"),
            Word(8, 1, "Aaron", "deu", renders: "Ἀαρών"),
            Word(3, 1, "Gebirge", "deu", renders: "Ἀριμαθαία"),
            Word(4, 1, "Priester", "deu", renders: "ἱερεύς"), Word(5, 1, "Priester", "deu", renders: "ἱερεύς"),
            Word(6, 1, "Sohn", "deu", renders: "בֵּן"), Word(7, 1, "Sohn", "deu", renders: "בֵּן"));

        renderings.Select(r => r.Form).Should().BeEquivalentTo(["Aaron"]);
    }

    /// <summary>
    /// The King James translates Ναζωραῖος, which names Jesus, as "of Nazareth"; the spelling the town
    /// heads with in the same text stays the town's.
    /// </summary>
    [Fact]
    public void AnotherEntitysHeadingStaysItsOwn()
    {
        var renderings = Of(null,
            Word(1, 1, "Jesus", "eng", renders: "Ἰησοῦς"), Word(2, 1, "Jesus", "eng", renders: "Ἰησοῦς"),
            Word(4, 1, "Jesus", "eng", renders: "Ἰησοῦς"),
            Word(1, 3, "Nazareth", "eng", renders: "Ναζωραῖος"), Word(2, 3, "Nazareth", "eng", renders: "Ναζωραῖος"),
            Word(3, 1, "Nazareth", "eng", entity: 2, renders: "Ναζαρέτ"));

        renderings.Where(r => r.EntityId == 1).Select(r => r.Form).Should().BeEquivalentTo(["Jesus"]);
        renderings.Should().Contain(r => r.EntityId == 2 && r.Form == "Nazareth" && r.Heading);
    }

    /// <summary>
    /// The Open New Ukrainian Translation prints the mercy seat once, as a word no link reaches, and
    /// the aligner spread the Greek word over the words beside it; the one of them with a capital is
    /// the conjunction opening the next sentence. The Ohienko Bible's firm spellings stand.
    /// </summary>
    [Fact]
    public void AWordOnlyAFaintGuessNamesIsNotASpelling()
    {
        var renderings = Of(null,
            Word(1, 9, "примирення", trailer: ". ", guess: 0.435, text: 2), Word(1, 10, "Однак", guess: 0.27, text: 2),
            Word(2, 1, "Віко", guess: 0.91), Word(3, 1, "Віко", guess: 0.91));

        renderings.Should().NotContain(r => r.TextId == 2);
        renderings.Select(r => (r.TextId, r.Form)).Should().BeEquivalentTo([(1, "Віко")]);
    }

    /// <summary>
    /// A faint spelling stands when another text prints the same name firmly, when the corpus holds
    /// it as the language's nominative, or when two words print it — and not on its own.
    /// </summary>
    [Fact]
    public void AFaintSpellingStandsWhenSomethingElseSaysItIsTheName()
    {
        var held = new Dictionary<(int, string), string> { [(3, "ukr")] = "Соляне море" };

        var renderings = Of(held,
            Word(1, 1, "Одед", guess: 0.46, text: 1), Word(1, 1, "Одед", "rus", guess: 0.93, text: 2),
            Word(2, 1, "Соляній", guess: 0.49, entity: 3),
            Word(3, 1, "Ясон", guess: 0.48, entity: 4), Word(4, 1, "Ясон", guess: 0.48, entity: 4),
            Word(5, 1, "Юдина", guess: 0.48, entity: 5));

        renderings.Select(r => (r.EntityId, r.TextId, r.Form)).Should().BeEquivalentTo(
            [(1, 1, "Одед"), (1, 2, "Одед"), (3, 1, "Соляній"), (4, 1, "Ясон")]);
    }

    /// <summary>
    /// A faint spelling does not decide which spellings keep company with the commonest: Elberfeld's
    /// Spain was headed by the word beside it, which tied with the name and was longer.
    /// </summary>
    [Fact]
    public void AFaintGuessNoLongerCrowdsOutTheName()
    {
        var renderings = Of(null,
            Word(1, 1, "Durchreise", "deu", guess: 0.33), Word(2, 1, "Spanien", "deu", guess: 0.33),
            Word(2, 1, "Spain", "eng", guess: 0.9, text: 2));

        renderings.Where(r => r.TextId == 1).Select(r => (r.Form, r.Heading)).Should().BeEquivalentTo([("Spanien", true)]);
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

    /// <summary>
    /// What an aligner's link carried is read at the annotation's confidence, and only that: the
    /// same confidence on an annotation a number carried is not a guess.
    /// </summary>
    [Fact]
    public async Task OnlyAnAnnotationAnAlignersLinkCarriedIsReadAsAGuess()
    {
        var open = Corpus.Add(_db, "NPU2022", TextKind.Translation, "ukr",
            (2, 1, ["кришку", "примирення.", "Однак", "зараз"]),
            (2, 2, ["Ковчег"]));
        var mercySeat = Thing("mercy-seat", "Mercy Seat");
        var ark = Thing("ark-of-the-covenant", "Ark of the Covenant");
        _db.SaveChanges();

        Carry(open, 2, 1, 2, mercySeat, 0.435, "aligner");
        Carry(open, 2, 1, 3, mercySeat, 0.27, "aligner");
        Carry(open, 2, 2, 1, ark, 0.27, "strong-number");
        _db.SaveChanges();

        await Load();

        (await _db.EntityRenderings.AsNoTracking()
                .Where(r => r.TextId == open.Id)
                .Select(r => new { r.EntityId, r.Form })
                .ToListAsync())
            .Should().BeEquivalentTo(new[] { new { EntityId = ark.Id, Form = "Ковчег" } });
    }

    private Entity Thing(string slug, string name)
    {
        var entity = new Entity { Kind = EntityKind.Object, Slug = slug, Name = name, SourceId = slug, Source = "a test" };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Carry(Text text, int chapter, int verse, int position, Entity entity, double confidence, string linkedBy) =>
        _db.WordEntities.Add(new WordEntity
        {
            WordId = _db.WordAt(text, chapter, verse, position).Id,
            EntityId = entity.Id,
            Method = LinkMethod.ModelReading,
            Confidence = confidence,
            Source = "a test",
            Note = $"through NESTLE1904 word 1, linked by {linkedBy}",
        });

    /// <summary>
    /// A second load leaves the same rows, ids and all, not twice as many; a spelling that changed is
    /// the only row written again.
    /// </summary>
    [Fact]
    public async Task LoadingAgainWritesOnlyWhatChanged()
    {
        await Load();
        var first = await _db.EntityRenderings.AsNoTracking().OrderBy(r => r.Id).ToListAsync();

        await Load();

        (await _db.EntityRenderings.AsNoTracking().OrderBy(r => r.Id).ToListAsync())
            .Should().BeEquivalentTo(first, options => options.WithStrictOrdering());

        var changed = first[0];
        await _db.EntityRenderings.Where(r => r.Id == changed.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.Occurrences, r => r.Occurrences + 1));

        await Load();

        var after = await _db.EntityRenderings.AsNoTracking().ToListAsync();
        after.Should().HaveCount(first.Count);
        after.Select(r => r.Id).Should().BeEquivalentTo(first.Skip(1).Select(r => r.Id).Append(after.Max(r => r.Id)));
        after.Should().ContainSingle(r => r.EntityId == changed.EntityId && r.TextId == changed.TextId
                                          && r.Form == changed.Form && r.Occurrences == changed.Occurrences);
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
