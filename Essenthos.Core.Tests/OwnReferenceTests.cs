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
/// Which verses an entity page lists once the list is read off the words this corpus annotated.
///
/// The cases are the ones where a list of namings and a list of verses come apart, and the one
/// where a page must stay empty. A verse whose Hebrew names a man twice is one reference; the same
/// verse read in four texts is still one reference; and a word two equal-standing methods disagree
/// about puts no verse anywhere, because that is what the reader is shown at the word and a page
/// citing a verse the word refuses to explain is the two halves of the corpus disagreeing in
/// public.
///
/// <para>
/// Asked of Postgres because the derivation is one statement, and what is under test is what that
/// statement selects.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnReferenceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly OwnReferenceLoader _loader;
    private readonly Text _hebrew;
    private readonly Text _english;

    public OwnReferenceTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _loader = new OwnReferenceLoader(_db, NullLogger<OwnReferenceLoader>.Instance);

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (1, 1, ["משה", "משה"]),
            (1, 2, ["זכריה"]),
            (1, 3, ["ישראל"]),
            (1, 4, ["מלך"]));

        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (1, 1, ["Moses"]));

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    /// <summary>
    /// Genesis 1:1 names Moses in two Hebrew words and again in the English standing opposite them.
    /// Four namings, one verse, and the page says one.
    /// </summary>
    [Fact]
    public async Task AVerseNamingTheSameManFourTimesIsOneReference()
    {
        var moses = Person("moses");
        Annotate(Hebrew(1, 1), moses, LinkMethod.StrongNumber, 0.9);
        Annotate(Hebrew(1, 2), moses, LinkMethod.StrongNumber, 0.9);
        Annotate(English(1), moses, LinkMethod.StrongNumber, 0.81);

        var written = await Load();

        written.Should().Be(1);
        (await Referenced(moses)).Should().Equal((1, 1, 1));
    }

    /// <summary>
    /// The Zechariah case, carried through to the page. Two methods of equal standing and equal
    /// confidence name two different men, the word panel shows nothing, and so does the page.
    /// </summary>
    [Fact]
    public async Task NeitherManIsCitedWhereTwoEqualMethodsDisagree()
    {
        var first = Person("zechariah-1");
        var second = Person("zechariah-2");
        Annotate(Hebrew(2, 1), first, LinkMethod.ModelReading, 0.9);
        Annotate(Hebrew(2, 1), second, LinkMethod.ModelReading, 0.9);

        await Load();

        (await Referenced(first)).Should().BeEmpty();
        (await Referenced(second)).Should().BeEmpty();
    }

    /// <summary>
    /// The same disagreement between methods of different standing, which is not a disagreement the
    /// reader ever sees: the resolution stands and the reading does not unseat it, so the verse
    /// goes on the resolution's page and on no other.
    /// </summary>
    [Fact]
    public async Task TheStrongerMethodPutsTheVerseOnItsOwnPage()
    {
        var resolved = Person("israel");
        var read = Person("jacob");
        Annotate(Hebrew(3, 1), resolved, LinkMethod.StrongNumber, 0.78);
        Annotate(Hebrew(3, 1), read, LinkMethod.ModelReading, 0.99);

        await Load();

        (await Referenced(resolved)).Should().Equal((1, 1, 3));
        (await Referenced(read)).Should().BeEmpty();
    }

    /// <summary>
    /// The reference says this corpus read it, not that a dataset stated it. The whole reason for
    /// the derivation is that the page can stop crediting somebody else's list for something the
    /// annotations already know.
    /// </summary>
    [Fact]
    public async Task AReferenceReadOffTheAnnotationsSaysItIsOurs()
    {
        var moses = Person("moses");
        Annotate(Hebrew(1, 1), moses, LinkMethod.StrongNumber, 0.9);

        await Load();

        var reference = await _db.EntityVerses.SingleAsync();
        reference.Source.Should().StartWith("Essenthos");
        Datasets.Of(reference.Source).Should().Be("essenthos");
    }

    /// <summary>
    /// A people's verse is cited under the people's own line, not the one names resolve under, and
    /// once: a verse <see cref="PeopleLoader"/> already cited is not cited again, and a word a later
    /// pass named the people — the children of Israel — puts its verse on the page on the next boot.
    /// </summary>
    [Fact]
    public async Task APeopleIsCitedUnderItsOwnLineOnce()
    {
        var people = Add("moabites", EntityKind.People);
        Annotate(Hebrew(4, 1), people, LinkMethod.Lexical, 0.9);
        Annotate(Hebrew(3, 1), people, LinkMethod.RuleBased, 0.9);
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = people, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 4,
            Source = PeopleLoader.FromOurOwnWords,
        });
        await _db.SaveChangesAsync();

        (await Load()).Should().Be(1);

        (await Referenced(people)).Should().Equal((1, 1, 3), (1, 1, 4));
        (await _db.EntityVerses.Where(v => v.EntityId == people.Id).Select(v => v.Source).Distinct().ToListAsync())
            .Should().Equal(PeopleLoader.FromOurOwnWords);
    }

    /// <summary>
    /// A title is named at its words as the person it was held as, so the verse of a word that names
    /// it is on its page — the title of Psalm 34 and the Septuagint's Ochozath are occurrences no
    /// dataset lists.
    /// </summary>
    [Fact]
    public async Task ATitleIsCitedWhereItsWordsNameIt()
    {
        var abimelech = Add("abimelech", EntityKind.Title);
        Annotate(Hebrew(2, 1), abimelech, LinkMethod.Manual, null);

        await Load();

        (await Referenced(abimelech)).Should().ContainSingle();
    }

    /// <summary>
    /// The startup pipeline runs on every boot, so a second pass must not write the corpus a second
    /// time.
    /// </summary>
    [Fact]
    public async Task ASecondPassWritesNothing()
    {
        Annotate(Hebrew(1, 1), Person("moses"), LinkMethod.StrongNumber, 0.9);

        (await Load()).Should().Be(1);

        var again = await _loader.Load();
        again.AlreadyLoaded.Should().BeTrue();
        again.Written.Should().Be(0);
        (await _db.EntityVerses.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// A record annotated after this pass has already run gets its verses on the next boot, and
    /// gets only its own.
    ///
    /// The peoples, the records this corpus writes for itself and the place register all add
    /// entities after this pass, so a guard that asked whether the pass had ever run would leave
    /// every one of them with an empty page for ever.
    /// </summary>
    [Fact]
    public async Task ARecordAnnotatedAfterThePassHasRunIsCitedOnTheNextBoot()
    {
        var moses = Person("moses");
        Annotate(Hebrew(1, 1), moses, LinkMethod.StrongNumber, 0.9);
        (await Load()).Should().Be(1);

        var jerusalem = Add("jerusalem", EntityKind.Place);
        Annotate(Hebrew(3, 1), jerusalem, LinkMethod.StrongNumber, 0.9);

        var again = await _loader.Load();

        again.AlreadyLoaded.Should().BeFalse();
        again.Written.Should().Be(1);
        (await Referenced(jerusalem)).Should().Equal((1, 1, 3));
        (await Referenced(moses)).Should().Equal((1, 1, 1));
    }

    /// <summary>
    /// A verse cited for a record whose word another record's reading has since outranked is taken
    /// back, although the word still carries the first record's claim: Boaz of 1 Kings 7:21 is the
    /// pillar once the number no longer settles it alone, and a cold load would not cite the verse on
    /// Ruth's husband's page.
    /// </summary>
    [Fact]
    public async Task AVerseWhoseWordIsNowAnotherRecordsLeavesThePage()
    {
        var man = Person("boaz");
        var pillar = Add("boaz-pillar", EntityKind.Object);
        Annotate(Hebrew(2, 1), man, LinkMethod.StrongNumber, 0.9);
        (await Load()).Should().Be(1);

        var resolution = await _db.WordEntities.SingleAsync(a => a.EntityId == man.Id);
        resolution.Method = LinkMethod.Lexical;
        await _db.SaveChangesAsync();
        Annotate(Hebrew(2, 1), pillar, LinkMethod.ModelReading, 0.93);

        var again = await _loader.Load();

        again.Withdrawn.Should().Be(1);
        (await Referenced(man)).Should().BeEmpty();
        (await Referenced(pillar)).Should().Equal((1, 1, 2));
    }

    private async Task<int> Load()
    {
        var outcome = await _loader.Load();
        outcome.AlreadyLoaded.Should().BeFalse();
        return outcome.Written;
    }

    private async Task<List<(int Book, int Chapter, int Verse)>> Referenced(Entity entity) =>
        await _db.EntityVerses
            .Where(v => v.EntityId == entity.Id)
            .OrderBy(v => v.CanonicalBook).ThenBy(v => v.CanonicalChapter).ThenBy(v => v.CanonicalVerse)
            .Select(v => new ValueTuple<int, int, int>(v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse))
            .ToListAsync();

    private void Annotate(Word word, Entity entity, LinkMethod method, double? confidence)
    {
        _db.WordEntities.Add(new WordEntity
        {
            Word = word,
            Entity = entity,
            Method = method,
            Confidence = confidence,
            Source = "a test",
        });
        _db.SaveChanges();
    }

    private Entity Person(string slug) => Add(slug, EntityKind.Person);

    private Entity Add(string slug, EntityKind kind)
    {
        var entity = new Entity
        {
            Kind = kind, Slug = slug, Name = slug, SourceId = slug, Source = "a test",
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private Word Hebrew(int verse, int position) => _db.WordAt(_hebrew, 1, verse, position);

    private Word English(int verse) => _db.WordAt(_english, 1, verse, 1);
}
