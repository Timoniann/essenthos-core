using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Which words of the Greek New Testament end up saying whom they name.
///
/// The Hebrew has a proper-noun marking to gate on and the Greek has none, so the gate here is
/// assembled from three statements — the lexicon writes the lemma with a capital, the occurrence is
/// a noun, and some Greek text spells the name the way the encyclopedia spells it — and every case
/// below is one of those three statements doing work nothing else does. Take any of them away and
/// the corpus says something false: that ἔρχομαι is Jesus 635 times, that being Jewish is being
/// Judah, or that every Judas in the New Testament is a walk-on of Luke's genealogy.
///
/// <para>
/// Asked of Postgres, because the gate is a set of statements rather than a loop and what is under
/// test is what they select.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class GreekEntityAnnotationTests : IDisposable
{
    /// <summary>Matthew, so the Greek text stands where the encyclopedia can reach it.</summary>
    private const int Apostolic = 40;

    /// <summary>Second Chronicles, which no Greek witness here holds.</summary>
    private const int Masoretic = 14;

    private readonly AppDbContext _db;
    private readonly EntityAnnotationLoader _loader;
    private readonly Text _greek;
    private readonly Text _hebrew;

    public GreekEntityAnnotationTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new EntityAnnotationLoader(_db, NullLogger<EntityAnnotationLoader>.Instance);

        _greek = Corpus.Add(_db, NestleTextSource.Slug, TextKind.CriticalEdition, "grc",
            (1, 1, ["Νῶε"]),
            (1, 2, ["ἦλθεν"]),
            (1, 3, ["Ἰουδαῖον"]),
            (1, 4, ["Ἰούδαν"]),
            (1, 5, ["Ἰορδάνην"]),
            (1, 6, ["Μαθθίαν"]),
            (1, 7, ["Ζαχαρίαν"]),
            (1, 8, ["Φῆστον"]),
            (1, 9, ["Ἀβραάμ"]));

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (1, 1, ["אברהם"]));

        _db.SaveChanges();
        _db.In(_greek, Apostolic);

        Word(1, "G3575", "Νῶε");
        Word(2, "G2064", "ἔρχομαι");
        Word(3, "G2453", "Ἰουδαῖος", "A-ASM");
        Word(4, "G2455", "Ἰούδας");
        Word(5, "G2446", "Ἰορδάνης");
        Word(6, "G3159", "Μαθθίας");
        Word(7, "G2197", "Ζαχαρίας");
        Word(8, "G5347", "Φῆστος");
        Word(9, "G11", "Ἀβραάμ");

        Lexicon("G3575", "Νῶε");
        Lexicon("G2064", "ἔρχομαι");
        Lexicon("G2453", "Ἰουδαῖος");
        Lexicon("G2455", "Ἰούδας");
        Lexicon("G2446", "Ἰορδάνης");

        // The lexicon and the edition spell the apostle differently, which is ordinary and is why
        // the edition's own lemma is one of the spellings a name is allowed to match.
        Lexicon("G3159", "Ματθίας");
        Lexicon("G2197", "Ζαχαρίας");
        Lexicon("G5347", "Φῆστος");
        Lexicon("G11", "Ἀβραάμ");

        Named("noah", "Noah", EntityKind.Person, "G3575", "Νῶε");
        Named("jesus", "Jesus", EntityKind.Person, "G2064", "ἔρχομαι");
        Named("judea", "Judea", EntityKind.Place, "G2453", "Ἰουδαῖος");

        // The encyclopedia's slip: a walk-on of Luke's genealogy, spelt Ἰωδά, carrying the Strong
        // number of Ἰούδας. It resolves to exactly one entity and it is not what the word says.
        Named("joda", "Joda", EntityKind.Person, "G2455", "Ἰωδά");

        // A name the New Testament never puts in the nominative, so nothing printed matches the
        // encyclopedia's citation form and only the lexicon's lemma reaches it.
        Named("jordan", "Jordan", EntityKind.Place, "G2446", "Ἰορδάνης");

        Named("matthias", "Matthias", EntityKind.Person, "G3159", "Μαθθίας");

        // The accusative, which is what the encyclopedia happens to have recorded — matched by the
        // form the text actually prints rather than by any lemma.
        Named("festus", "Festus", EntityKind.Person, "G5347", "Φῆστον");

        var abraham = Named("abraham", "Abraham", EntityKind.Person, "G11", "Ἀβραάμ");
        abraham.Names!.Single().HebrewStrongNumber = "H85";

        // Three men called Zechariah, two of whom the encyclopedia places only in a book no Greek
        // witness holds. Without reachability the name is contested and nothing is written.
        Attest(Named("zechariah-1", "Zechariah", EntityKind.Person, "G2197", "Ζαχαρίας"), Masoretic);
        Attest(Named("zechariah-2", "Zechariah", EntityKind.Person, "G2197", "Ζαχαρίας"), Masoretic);
        Attest(Named("zechariah-3", "Zechariah", EntityKind.Person, "G2197", "Ζαχαρίας"), Apostolic);

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
        _db.Database.ExecuteSqlRaw("DELETE FROM strong_entry");
    }

    /// <summary>
    /// One Greek word as a witness records it: a Strong number, the edition's own lemma, the folded
    /// form the corpus searches it by, and a morphology tag. The default tag is a noun, because
    /// that is what all but one of these are.
    /// </summary>
    private void Word(int verse, string number, string lemma, string? robinson = null)
    {
        var word = _db.WordAt(_greek, 1, verse, 1);
        word.StrongNumber = number;
        word.Lemma = lemma;
        word.NormalisedText = WordFolding.Fold(word.Surface, "grc");
        word.Morphology = robinson is null
            ? JsonDocument.Parse("""{"pos": "noun"}""")
            : JsonDocument.Parse($$"""{"pos": "adj", "robinson": "{{robinson}}"}""");
        _db.SaveChanges();
    }

    private void Lexicon(string number, string lemma) =>
        _db.StrongEntries.Add(new StrongEntry { StrongNumber = number, Lemma = lemma });

    private Entity Named(string slug, string name, EntityKind kind, string number, string greek)
    {
        var entity = new Entity
        {
            Kind = kind, Slug = slug, Name = name, SourceId = slug, Source = "a test",
            Names = [new EntityName { Label = name, GreekStrongNumber = number, Greek = greek, Kind = "name" }],
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private void Attest(Entity entity, int book)
    {
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = entity, CanonicalBook = book, CanonicalChapter = 1, CanonicalVerse = 1,
            Source = "a test",
        });
        _db.SaveChanges();
    }

    private async Task<Dictionary<long, string>> Load()
    {
        await _loader.Load();
        return await _db.WordEntities.ToDictionaryAsync(a => a.WordId, a => a.Entity!.Slug);
    }

    private Word Greek(int verse) => _db.WordAt(_greek, 1, verse, 1);

    /// <summary>
    /// The case the whole pass exists for: a name in the New Testament, borne by one person, on a
    /// word that until now said nothing at all.
    /// </summary>
    [Fact]
    public async Task ANameOnlyOnePersonBearsIsAnnotatedWithThatPerson()
    {
        var named = await Load();
        named.Should().ContainKey(Greek(1).Id).WhoseValue.Should().Be("noah");
    }

    /// <summary>
    /// The failure the capital letter prevents. The encyclopedia records the Greek number of what a
    /// name means as readily as the number of the name — ἔρχομαι for the one who comes, ἡμέρα for
    /// Jemimah, ζωή for Eve — and every one of those numbers answers with exactly one entity. There
    /// are 2,622 such words in the New Testament and the lexicon's lower-case lemma is what tells
    /// them from the 1,455 that are names.
    /// </summary>
    [Fact]
    public async Task AWordWhoseLexiconLemmaIsNotWrittenAsANameIsLeftAlone()
    {
        var named = await Load();
        named.Should().NotContainKey(Greek(2).Id);
    }

    /// <summary>
    /// And what the noun test prevents, which the capital cannot: Ἰουδαῖος is capitalised and is an
    /// adjective. Being Jewish is not being Judea, and there are 197 of these.
    /// </summary>
    [Fact]
    public async Task AGentilicFormedFromANameIsNotTheName()
    {
        var named = await Load();
        named.Should().NotContainKey(Greek(3).Id);
    }

    /// <summary>
    /// The encyclopedia's Greek numbers are not always right, and a wrong one that resolves is
    /// worse than one that does not: it is a confident answer. Here it would put a name from Luke's
    /// genealogy on every occurrence of Judas in the New Testament. Nothing but the spelling
    /// catches it — the number resolves, the lemma is capitalised, and the word is a noun.
    /// </summary>
    [Fact]
    public async Task ANameNoGreekTextSpellsThatWayIsRefused()
    {
        var named = await Load();
        named.Should().NotContainKey(Greek(4).Id);
    }

    /// <summary>
    /// And the discipline that keeps that from becoming a licence to refuse everything. A name the
    /// New Testament never prints in the nominative is spelt by nothing on the page, and matching
    /// only the printed forms would lose the Jordan, Damascus, Sidon and Zebedee.
    /// </summary>
    [Fact]
    public async Task ANameThePageOnlyEverInflectsIsReachedThroughTheLexiconsLemma()
    {
        var named = await Load();
        named.Should().ContainKey(Greek(5).Id).WhoseValue.Should().Be("jordan");
    }

    /// <summary>
    /// Two editions spelling one apostle two ways is not a reason to doubt either of them, so the
    /// witness's own lemma counts as a spelling of the name alongside the lexicon's.
    /// </summary>
    [Fact]
    public async Task ANameTheEditionSpellsItsOwnWayIsReachedThroughThatEditionsLemma()
    {
        var named = await Load();
        named.Should().ContainKey(Greek(6).Id).WhoseValue.Should().Be("matthias");
    }

    /// <summary>
    /// The third way in, for the names the encyclopedia recorded in whatever case it found them.
    /// </summary>
    [Fact]
    public async Task ANameRecordedInAnObliqueCaseIsReachedThroughThePrintedForm()
    {
        var named = await Load();
        named.Should().ContainKey(Greek(8).Id).WhoseValue.Should().Be("festus");
    }

    /// <summary>
    /// The mirror of the rule that keeps Judas Iscariot out of the book of Numbers. The
    /// encyclopedia gives an Old Testament man the Greek number of his name as freely as it gives a
    /// New Testament man the Hebrew one, and without reachability sixty-seven kings of Judah and
    /// Assyria are candidates for βασιλεύς and thirteen Zechariahs of Chronicles for Ζαχαρίας.
    /// </summary>
    [Fact]
    public async Task SomebodyNamedOnlyWhereTheGreekDoesNotReachIsNotACandidate()
    {
        var named = await Load();
        named.Should().ContainKey(Greek(7).Id).WhoseValue.Should().Be("zechariah-3");
    }

    /// <summary>
    /// Ἀβραάμ and אברהם are one man, and the encyclopedia carries both numbers on one row — which
    /// is why this is a join rather than a second encyclopedia. Each side annotates its own words,
    /// so the man ends up with two annotations and stays one person.
    /// </summary>
    [Fact]
    public async Task AManNamedInBothLanguagesIsAnnotatedFromBothAndStaysOnePerson()
    {
        var hebrew = _db.WordAt(_hebrew, 1, 1, 1);
        hebrew.StrongNumber = "H85";
        hebrew.Morphology = JsonDocument.Parse("""{"pos": "subs", "nameType": "pers"}""");
        _db.SaveChanges();

        var named = await Load();

        named.Should().ContainKey(Greek(9).Id).WhoseValue.Should().Be("abraham");
        named.Should().ContainKey(hebrew.Id).WhoseValue.Should().Be("abraham");
        (await _db.Entities.CountAsync(e => e.Slug == "abraham")).Should().Be(1);
    }

    /// <summary>
    /// A Greek annotation says what made it, and it does not say what the Hebrew one says. The two
    /// rest on different evidence and a reader comparing them has to be able to tell.
    /// </summary>
    [Fact]
    public async Task AGreekNameSaysWhatEstablishedIt()
    {
        await _loader.Load();

        var annotation = await _db.WordEntities.SingleAsync(a => a.WordId == Greek(1).Id);
        annotation.Source.Should().Contain("lexicon");
        annotation.Note.Should().Contain("G3575");
        annotation.Confidence.Should().BeLessThan(1);
    }

    /// <summary>
    /// What the loader reports about the Greek is counted rather than estimated, and the numbers it
    /// refused for want of a spelling are reported rather than quietly dropped — a refusal nobody
    /// is told about is indistinguishable from a join that was never there.
    /// </summary>
    [Fact]
    public async Task TheOutcomeCountsTheGreekItRefusedAsWellAsWhatItWrote()
    {
        var outcome = await _loader.Load();

        outcome.Greek.Resolved.Should().Be(7);
        outcome.Greek.Contested.Should().Be(0);
        outcome.Refused.Should().Be(1);
    }
}
