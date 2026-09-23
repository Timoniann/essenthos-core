using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Hilkiah son of Meshullam, whom BibleData records once for 1 Chronicles 9:11 and again for its
/// parallel in Nehemiah 11:11. Folding the second into the first must leave one man holding
/// everything both held, say each thing once, and keep the second record's address and testimony.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class DuplicateRecordTests : IDisposable
{
    private const string BibleData = "BibleData";

    private readonly AppDbContext _db;
    private readonly DuplicateRecordLoader _loader;
    private readonly Text _hebrew;
    private readonly Entity _kept;
    private readonly Entity _folded;
    private readonly Entity _meshullam;

    public DuplicateRecordTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new DuplicateRecordLoader(_db, NullLogger<DuplicateRecordLoader>.Instance);

        _hebrew = Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo",
            (9, 11, ["עֲזַרְיָה", "בֶּן", "חִלְקִיָּה"]));
        _db.SaveChanges();

        _kept = Record("hilkiah-3", "person:Hilkiah_3", "son of Shallum and father of Azariah (1CH 6:13)");
        _folded = Record("hilkiah-6", "person:Hilkiah_6", "son of Meshullam (NEH 11:11)");
        _meshullam = Record("meshullam-15", "person:Meshullam_15", "son of Zadok (NEH 11:11)");
        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
    }

    private Entity Record(string slug, string sourceId, string distinguisher)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = slug.Split('-')[0], Distinguisher = distinguisher,
            SourceId = sourceId, Source = BibleData,
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private static DuplicateRecordList List(params (string Keeps, string Folds)[] pairs) =>
        new(LinkMethod.ModelReading, 0.9, "a test",
            [.. pairs.Select(p => new DuplicateRecordPair("H2518", p.Keeps, p.Folds, "one list written twice"))]);

    private void Cites(Entity entity, int book, int chapter, int verse) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = entity, CanonicalBook = book, CanonicalChapter = chapter, CanonicalVerse = verse, Source = BibleData,
        });

    private WordEntity Names(Entity entity, LinkMethod method, double? confidence, string source)
    {
        var row = new WordEntity
        {
            Word = _db.WordAt(_hebrew, 9, 11, 3), Entity = entity, Method = method, Confidence = confidence, Source = source,
        };
        _db.WordEntities.Add(row);
        return row;
    }

    [Fact]
    public async Task EveryVerseOfBothIsHeldOnceByTheRecordThatStays()
    {
        Cites(_kept, 13, 9, 11);
        Cites(_folded, 13, 9, 11);
        Cites(_folded, 16, 11, 11);
        await _db.SaveChangesAsync();

        var outcome = await _loader.Fold(List(("hilkiah-3", "hilkiah-6")));

        outcome.Folded.Should().Be(1);
        var verses = await _db.EntityVerses.AsNoTracking()
            .Select(v => new { v.EntityId, v.CanonicalBook, v.CanonicalChapter })
            .ToListAsync();
        verses.Should().OnlyContain(v => v.EntityId == _kept.Id);
        verses.Select(v => (v.CanonicalBook, v.CanonicalChapter)).Should().BeEquivalentTo([(13, 9), (16, 11)],
            "1 Chronicles 9:11 was cited by both and is cited once; Nehemiah 11:11 comes with the folded record");
        (await _db.Entities.AnyAsync(e => e.Slug == "hilkiah-6")).Should().BeFalse();
    }

    [Fact]
    public async Task TheFoldedRecordKeepsItsAddressAndWhatTheDatasetSaidOfIt()
    {
        await _loader.Fold(List(("hilkiah-3", "hilkiah-6")));

        var merged = await _db.MergedRecords.AsNoTracking().SingleAsync();
        merged.Slug.Should().Be("hilkiah-6");
        merged.EntityId.Should().Be(_kept.Id);
        merged.RecordSourceId.Should().Be("person:Hilkiah_6");
        merged.RecordSource.Should().Be(BibleData);
        merged.Distinguisher.Should().Be("son of Meshullam (NEH 11:11)");
        merged.Method.Should().Be(LinkMethod.ModelReading);
        merged.Confidence.Should().Be(0.9);
    }

    [Fact]
    public async Task ANameOnlyTheFoldedRecordCarriedIsStillAName()
    {
        _db.EntityNames.Add(new EntityName { Entity = _kept, Label = "Hilkiah", HebrewStrongNumber = "H2518" });
        _db.EntityNames.Add(new EntityName { Entity = _folded, Label = "Hilkiah", HebrewStrongNumber = "H2518" });
        _db.EntityNames.Add(new EntityName { Entity = _folded, Label = "Helkiah", HebrewStrongNumber = "H2518" });
        await _db.SaveChangesAsync();

        await _loader.Fold(List(("hilkiah-3", "hilkiah-6")));

        var names = await _db.EntityNames.AsNoTracking().Where(n => n.EntityId == _kept.Id).Select(n => n.Label).ToListAsync();
        names.Should().BeEquivalentTo(["Hilkiah", "Helkiah"]);
    }

    /// <summary>
    /// Two annotations of one word, one on each record. The ruling stays and the reading that agreed
    /// with it becomes its claim, so nothing is counted twice and nothing that said so is forgotten.
    /// </summary>
    [Fact]
    public async Task TwoAnnotationsOfOneWordBecomeOneWithBothTestimonies()
    {
        Names(_kept, LinkMethod.ModelReading, 0.99, "a reading");
        var ruling = Names(_folded, LinkMethod.Manual, null, "the owner's ruling");
        await _db.SaveChangesAsync();

        await _loader.Fold(List(("hilkiah-3", "hilkiah-6")));

        var row = await _db.WordEntities.AsNoTracking().Include(a => a.Claims).SingleAsync();
        row.Id.Should().Be(ruling.Id, "a person's ruling outranks a reading");
        row.EntityId.Should().Be(_kept.Id);
        row.Claims.Select(c => c.Source).Should().Contain("a reading");
    }

    [Fact]
    public async Task RelationshipsMoveAndOneWithHimselfGoes()
    {
        _db.EntityRelationships.Add(new EntityRelationship
        {
            From = _folded, To = _meshullam, Type = "son-of", Category = RelationshipCategories.Explicit,
            Method = LinkMethod.StatedBySource, Source = BibleData,
        });
        _db.EntityRelationships.Add(new EntityRelationship
        {
            From = _folded, To = _kept, Type = "same-as", Category = RelationshipCategories.Explicit,
            Method = LinkMethod.StatedBySource, Source = BibleData,
        });
        await _db.SaveChangesAsync();

        await _loader.Fold(List(("hilkiah-3", "hilkiah-6")));

        var rows = await _db.EntityRelationships.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].FromEntityId.Should().Be(_kept.Id);
        rows[0].ToEntityId.Should().Be(_meshullam.Id);
    }

    [Fact]
    public async Task TheFoldedRecordsClausesFollowTheKeptRecordsOwn()
    {
        _db.EntityDescriptors.Add(Clause(_kept, 1, "son-of"));
        _db.EntityDescriptors.Add(Clause(_kept, 2, "father-of"));
        _db.EntityDescriptors.Add(Clause(_folded, 1, "son-of"));
        await _db.SaveChangesAsync();

        await _loader.Fold(List(("hilkiah-3", "hilkiah-6")));

        var ordinals = await _db.EntityDescriptors.AsNoTracking()
            .Where(d => d.EntityId == _kept.Id).OrderBy(d => d.Ordinal).Select(d => d.Ordinal).ToListAsync();
        ordinals.Should().Equal(1, 2, 3);
    }

    private EntityDescriptor Clause(Entity entity, int ordinal, string relation) => new()
    {
        Entity = entity, Ordinal = ordinal, Relation = relation, Target = _meshullam,
        CanonicalBook = 16, CanonicalChapter = 11, CanonicalVerse = 11,
        Method = LinkMethod.ModelReading, Confidence = 0.9, Source = "read from Scripture by a test",
    };

    /// <summary>
    /// Each text has one heading spelling per record, and a page reads it as the one. After the fold
    /// it is still one: the kept record's, with the folded record's counts added where they share a form.
    /// </summary>
    [Fact]
    public async Task ATextKeepsOneHeadingSpelling()
    {
        _db.EntityRenderings.Add(Spelling(_kept, "Хелкия", 5, heading: true));
        _db.EntityRenderings.Add(Spelling(_folded, "Хелкия", 2, heading: false));
        _db.EntityRenderings.Add(Spelling(_folded, "Хилкия", 3, heading: true));
        await _db.SaveChangesAsync();

        await _loader.Fold(List(("hilkiah-3", "hilkiah-6")));

        var spellings = await _db.EntityRenderings.AsNoTracking().Where(r => r.EntityId == _kept.Id).ToListAsync();
        spellings.Should().ContainSingle(r => r.Heading).Which.Form.Should().Be("Хелкия");
        spellings.Single(r => r.Form == "Хелкия").Occurrences.Should().Be(7);
        spellings.Should().Contain(r => r.Form == "Хилкия");
    }

    private EntityRendering Spelling(Entity entity, string form, int occurrences, bool heading) => new()
    {
        Entity = entity, TextId = _hebrew.Id, Form = form, Folded = form.ToLowerInvariant(),
        Occurrences = occurrences, Heading = heading,
    };

    [Fact]
    public async Task ASecondRunFindsNothingLeftToFold()
    {
        await _loader.Fold(List(("hilkiah-3", "hilkiah-6")));

        var again = await _loader.Fold(List(("hilkiah-3", "hilkiah-6")));

        again.Folded.Should().Be(0);
        again.AlreadyFolded.Should().Be(1);
        (await _db.MergedRecords.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// A record folded into one that is itself folded later reaches the last of them, and so does
    /// its address.
    /// </summary>
    [Fact]
    public async Task AnEarlierFoldFollowsTheRecordItWentInto()
    {
        await _loader.Fold(List(("hilkiah-3", "hilkiah-6")));

        await _loader.Fold(List(("meshullam-15", "hilkiah-3")));

        var addresses = await _db.MergedRecords.AsNoTracking().Select(m => new { m.Slug, m.EntityId }).ToListAsync();
        addresses.Should().OnlyContain(m => m.EntityId == _meshullam.Id);
        addresses.Select(m => m.Slug).Should().BeEquivalentTo(["hilkiah-6", "hilkiah-3"]);
    }

    [Fact]
    public async Task TheFoldedAddressArrivesAtTheRecordThatStays()
    {
        await _loader.Fold(List(("hilkiah-3", "hilkiah-6")));

        (await Endpoints.MergedAddresses.Current(_db, "hilkiah-6", default)).Should().Be("hilkiah-3");
        (await Endpoints.MergedAddresses.Current(_db, "meshullam-15", default)).Should().Be("meshullam-15");
    }

    [Fact]
    public async Task APairNamingARecordNotHeldIsLeftAlone()
    {
        var outcome = await _loader.Fold(List(("hilkiah-3", "hilkiah-99")));

        outcome.Missing.Should().Be(1);
        (await _db.Entities.CountAsync()).Should().Be(3);
    }

    /// <summary>The list as shipped: every pair names two records, and no record is both kept and folded.</summary>
    [Fact]
    public void TheShippedListFoldsEachRecordOnceAndNeverARecordThatStays()
    {
        var list = DuplicateRecordLoader.Read();

        list.Merges.Should().NotBeEmpty();
        list.Merges.Select(m => m.Folds).Should().OnlyHaveUniqueItems();
        list.Merges.Select(m => m.Folds).Should().NotIntersectWith(list.Merges.Select(m => m.Keeps));
        list.Merges.Should().OnlyContain(m => m.Keeps != m.Folds && m.Why.Length > 0);
        list.Confidence.Should().BeInRange(0, 1);
    }
}
