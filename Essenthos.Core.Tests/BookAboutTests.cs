using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>What a whole book holds: its counts, the parts its own text marks, who it names most and when.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class BookAboutTests : IDisposable
{
    private const int Genesis = 1;
    private const int Psalms = 19;

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public BookAboutTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    /// <summary>
    /// A verse where תּוֹלְדֹת follows <em>these</em> or <em>book</em> opens a part, with the words naming
    /// whose generations they are; one using the word in passing is counted and opens nothing.
    /// </summary>
    [Fact]
    public async Task TheGenerationsOpenThePartsOfGenesis()
    {
        var hebrew = Corpus.Add(_db, "test-hebrew", TextKind.CriticalEdition, "hbo",
            (1, 1, ["בראשית", "ברא"]),
            (2, 3, ["ויברך"]),
            (2, 4, ["אלה", "תולדות", "ה", "שמים", "ו", "ה", "ארץ"]),
            (5, 1, ["זה", "ספר", "תולדת", "אדם", "ב", "יום"]),
            (5, 2, ["זכר"]),
            (10, 1, ["ו", "אלה", "תולדת", "בני", "נח", "שם"]),
            (10, 32, ["אלה", "משפחת", "ל", "תולדתם"]),
            (11, 1, ["ויהי"]));
        await _db.SaveChangesAsync();
        Strong(hebrew, (2, 4, 1, "H428"), (2, 4, 2, "H8435"), (5, 1, 1, "H2088"), (5, 1, 2, "H5612"),
            (5, 1, 3, "H8435"), (10, 1, 1, "H9000"), (10, 1, 2, "H428"), (10, 1, 3, "H8435"),
            (10, 32, 1, "H428"), (10, 32, 3, "H9005"), (10, 32, 4, "H8435"));
        Prefix(hebrew, (2, 4, 3), (10, 1, 1));
        Construct(hebrew, (2, 4, 2), (5, 1, 3), (10, 1, 3), (10, 1, 4));
        await _db.SaveChangesAsync();

        var structure = (await BookStructure.Of(_db, Genesis, default))!;

        structure.Kind.Should().Be("generations");
        structure.Markers.Select(m => (m.Chapter, m.Verse, m.Divides, m.Words)).Should().Equal(
            (2, 4, true, "אלה תולדות השמים"),
            (5, 1, true, "זה ספר תולדת אדם"),
            (10, 1, true, "אלה תולדת בני נח"),
            (10, 32, false, "תולדתם"));
        structure.Parts.Select(p => $"{p.FromChapter}:{p.FromVerse}-{p.ToChapter}:{p.ToVerse}").Should().Equal(
            "1:1-2:3", "2:4-2:4", "5:1-5:2", "10:1-11:1");
    }

    /// <summary>Each of the first books of the Psalms ends with the psalm that says <em>Amen</em>; the last runs to the end.</summary>
    [Fact]
    public async Task ThePsalmsCloseTheirBooksWithAmen()
    {
        var hebrew = Corpus.Add(_db, "test-hebrew", TextKind.CriticalEdition, "hbo",
            (1, 1, ["אשרי"]),
            (41, 13, ["ברוך", "אמן", "ו", "אמן"]),
            (42, 1, ["כאיל"]),
            (72, 19, ["ברוך", "אמן", "ו", "אמן"]),
            (72, 20, ["כלו"]),
            (73, 1, ["אך"]),
            (150, 6, ["כל"]));
        await _db.SaveChangesAsync();
        _db.In(hebrew, Psalms);
        Strong(hebrew, (41, 13, 2, "H543"), (41, 13, 4, "H543"), (72, 19, 2, "H543"), (72, 19, 4, "H543"));
        await _db.SaveChangesAsync();

        var structure = (await BookStructure.Of(_db, Psalms, default))!;

        structure.Kind.Should().Be("books");
        structure.Markers.Select(m => (m.Chapter, m.Verse, m.Words)).Should().Equal(
            (41, 13, "אמן ו אמן"),
            (72, 19, "אמן ו אמן"));
        structure.Parts.Select(p => $"{p.FromChapter}:{p.FromVerse}-{p.ToChapter}:{p.ToVerse}").Should().Equal(
            "1:1-41:13", "42:1-72:20", "73:1-150:6");
    }

    [Fact]
    public async Task ABookWhoseTextMarksNoPartsHasNoStructure()
    {
        Corpus.Add(_db, "test-hebrew", TextKind.CriticalEdition, "hbo", (1, 1, ["בראשית"]));
        await _db.SaveChangesAsync();

        (await BookStructure.Of(_db, Genesis, default)).Should().BeNull();
        (await BookStructure.Of(_db, 2, default)).Should().BeNull();
    }

    /// <summary>
    /// Each text is counted in its own numbering; a person is ranked by the verses naming them in any
    /// text or in their own list, and a place only where the map can show it; the years are those of the
    /// first and the last event the book tells, so a promise dated by its fulfilment does not stretch them.
    /// </summary>
    [Fact]
    public async Task ABookIsCountedRankedAndDated()
    {
        var english = Corpus.Add(_db, "test-english", TextKind.Translation, "eng",
            (1, 1, ["In", "the", "beginning"]),
            (1, 2, ["Adam", "and", "Noah"]),
            (2, 1, ["Noah"]));
        await _db.SaveChangesAsync();
        var adam = Record("adam", EntityKind.Person);
        var noah = Record("noah", EntityKind.Person);
        var eden = Record("eden", EntityKind.Place);
        var earth = Record("earth", EntityKind.Place);
        await _db.SaveChangesAsync();
        _db.PlaceLocations.Add(new PlaceLocation
        {
            EntityId = eden.Id,
            Longitude = 44.4,
            Latitude = 32.5,
            Kind = "point",
            Score = 500,
            ModernId = "test",
            CoordinatesSource = "test",
            Source = "test",
        });
        Names(_db.WordAt(english, 1, 1, 3), earth);
        Names(_db.WordAt(english, 1, 1, 2), earth);
        Names(_db.WordAt(english, 1, 2, 1), adam);
        Names(_db.WordAt(english, 1, 2, 3), noah);
        Names(_db.WordAt(english, 2, 1, 1), noah);
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = eden, CanonicalBook = Genesis, CanonicalChapter = 1, CanonicalVerse = 1, Source = "test",
        });
        var reckoning = new Chronology { Slug = "test-reckoning", Name = "Reckoning", LastYearBeforeTheCommonEra = 4004 };
        _db.Chronologies.Add(reckoning);
        var first = Event("creation", 1, 1);
        var promise = Event("promise-fulfilled", 1, 2);
        var last = Event("flood", 2, 1);
        await _db.SaveChangesAsync();
        _db.EventDates.AddRange(
            new EventDate { EventId = first.Id, ChronologyId = reckoning.Id, Year = 1 },
            new EventDate { EventId = promise.Id, ChronologyId = reckoning.Id, Year = 2513 },
            new EventDate { EventId = last.Id, ChronologyId = reckoning.Id, Year = 1656 });
        await _db.SaveChangesAsync();

        var about = await BookAboutEndpoints.About(_db, Genesis, null, default);

        about.Counts.Should().ContainSingle().Which.Should().Be(new BookTextCountResponse("test-english", 2, 3));
        about.People.Select(p => (p.Slug, p.Verses)).Should().Equal(("noah", 2), ("adam", 1));
        about.Places.Select(p => (p.Slug, p.Verses)).Should().Equal(("eden", 1));
        about.Years.Should().ContainKey("test-reckoning").WhoseValue.Should().Equal(1, 1656);
        about.Structure.Should().BeNull();
    }

    private void Strong(Text text, params (int Chapter, int Verse, int Position, string Number)[] words)
    {
        foreach (var (chapter, verse, position, number) in words)
        {
            _db.WordAt(text, chapter, verse, position).StrongNumber = number;
        }
    }

    /// <summary>A prefix is printed joined to the word after it, as BHSA prints the article and the conjunction.</summary>
    private void Prefix(Text text, params (int Chapter, int Verse, int Position)[] words)
    {
        foreach (var (chapter, verse, position) in words)
        {
            _db.WordAt(text, chapter, verse, position).Trailer = string.Empty;
        }
    }

    private void Construct(Text text, params (int Chapter, int Verse, int Position)[] words)
    {
        foreach (var (chapter, verse, position) in words)
        {
            _db.WordAt(text, chapter, verse, position).Morphology = JsonDocument.Parse("""{"state":"c"}""");
        }
    }

    private Entity Record(string slug, EntityKind kind)
    {
        var entity = new Entity
        {
            Kind = kind,
            Slug = slug,
            Name = char.ToUpperInvariant(slug[0]) + slug[1..],
            SourceId = $"test:{slug}",
            Source = "test",
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Names(Word word, Entity entity) =>
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id,
            EntityId = entity.Id,
            Method = LinkMethod.StatedBySource,
            Source = $"test:{entity.Slug}",
        });

    private Database.Entities.Event Event(string slug, int chapter, int verse)
    {
        var happened = new Database.Entities.Event
        {
            Slug = slug,
            Name = slug,
            CanonicalBook = Genesis,
            CanonicalChapter = chapter,
            CanonicalVerse = verse,
            Source = "test",
        };
        _db.Events.Add(happened);
        return happened;
    }
}
