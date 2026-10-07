using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Verification;
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

    private const string Decided = "read from Scripture by the project owner, decided 2026-09-12";

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

    [Theory]
    [InlineData("joahaz-2", "jehoahaz-2")]
    [InlineData("joahaz-3", "jehoahaz")]
    [InlineData("joahaz-4", "ahaziah-2")]
    [InlineData("reuel-5", "deuel")]
    [InlineData("jeiel-11", "jeiel-2")]
    [InlineData("jeshua-10", "joshua")]
    [InlineData("cush-5", "cush-2")]
    public async Task AShippedRegisterAliasKeepsTheCanonicalEvidenceAndRedirectOnRepeatedLoads(string alias, string canonical)
    {
        var shipped = DuplicateRecordLoader.Read();
        var pairs = shipped.Merges.Where(m => m.Folds == alias).ToList();
        pairs.Should().ContainSingle().Which.Keeps.Should().Be(canonical);
        var kept = Record(canonical, $"test:{canonical}", "the attested bearer");
        var folded = Record(alias, $"test:{alias}", "the same bearer under the register's other spelling");
        folded.Names.Add(new EntityName { Label = alias, Kind = "proper name" });
        Cites(kept, 14, 36, 1);
        var relationship = new EntityRelationship
        {
            From = kept, To = _meshullam, Type = "son-of", Category = RelationshipCategories.Read,
            CanonicalBook = 14, CanonicalChapter = 36, CanonicalVerse = 1,
            Method = LinkMethod.Manual, Source = Decided,
        };
        _db.EntityRelationships.Add(relationship);
        await _db.SaveChangesAsync();
        var verseId = await _db.EntityVerses.Where(v => v.EntityId == kept.Id).Select(v => v.Id).SingleAsync();
        var list = shipped with { Merges = pairs, Splits = [] };

        (await _loader.Fold(list)).Folded.Should().Be(1);
        (await _db.Entities.AnyAsync(e => e.Slug == alias)).Should().BeFalse();
        var merged = await _db.MergedRecords.AsNoTracking().SingleAsync();
        merged.EntityId.Should().Be(kept.Id);
        merged.RecordSourceId.Should().Be($"test:{alias}");
        merged.Source.Should().Contain("Codex");
        (await _db.EntityVerses.SingleAsync()).Id.Should().Be(verseId);
        (await _db.EntityRelationships.SingleAsync()).Id.Should().Be(relationship.Id);
        var name = await _db.EntityNames.AsNoTracking().SingleAsync();
        name.EntityId.Should().Be(kept.Id);
        name.Label.Should().Be(alias);
        var again = await _loader.Fold(list);
        again.Folded.Should().Be(0);
        again.Moved.Should().Be(0);
        again.Joined.Should().Be(0);
        again.AlreadyFolded.Should().Be(1);
        (await _db.EntityNames.AsNoTracking().SingleAsync()).Id.Should().Be(name.Id);
        (await _db.MergedRecords.AsNoTracking().SingleAsync()).Id.Should().Be(merged.Id);
    }

    /// <summary>
    /// A place the register wrote under Strong's headword beside the held record of the same place
    /// folds into the held one, which keeps the name the text prints: the words naming Jobesh name
    /// Jabesh-gilead, and the register's address still arrives there.
    /// </summary>
    [Theory]
    [InlineData("jobesh", "jabeshgilead")]
    [InlineData("allonbachuth", "allonbacuth")]
    [InlineData("padan", "paddanaram")]
    public async Task APlaceTheRegisterSpeltAsStrongDoesFoldsIntoTheHeldPlace(string alias, string canonical)
    {
        var pair = DuplicateRecordLoader.Read().Merges.Single(m => m.Folds == alias);
        pair.Keeps.Should().Be(canonical);
        var kept = Record(canonical, $"place:{canonical}", "the held place");
        var folded = Record(alias, $"essenthos:{alias}", "the register's place");
        var word = Names(folded, LinkMethod.StrongNumber, 0.9, "the register's number");
        Cites(folded, 7, 21, 8);
        await _db.SaveChangesAsync();

        (await _loader.Fold(DuplicateRecordLoader.Read() with { Merges = [pair], Splits = [] })).Folded.Should().Be(1);

        (await _db.Entities.AnyAsync(e => e.Slug == alias)).Should().BeFalse();
        (await _db.Entities.SingleAsync(e => e.Slug == canonical)).Name.Should().Be(kept.Name);
        (await _db.WordEntities.AsNoTracking().SingleAsync(a => a.Id == word.Id)).EntityId.Should().Be(kept.Id);
        (await _db.EntityVerses.AsNoTracking().SingleAsync()).EntityId.Should().Be(kept.Id);
        (await _db.MergedRecords.AsNoTracking().SingleAsync()).Slug.Should().Be(alias);
    }

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
            From = _folded, To = _meshullam, Type = "son-of", Category = RelationshipCategories.Read,
            CanonicalBook = 16, CanonicalChapter = 11, CanonicalVerse = 11,
            Method = LinkMethod.Manual, Source = Decided,
        });
        _db.EntityRelationships.Add(new EntityRelationship
        {
            From = _folded, To = _kept, Type = "same-as", Category = RelationshipCategories.Read,
            CanonicalBook = 16, CanonicalChapter = 11, CanonicalVerse = 11,
            Method = LinkMethod.Manual, Source = Decided,
        });
        await _db.SaveChangesAsync();

        await _loader.Fold(List(("hilkiah-3", "hilkiah-6")));

        var rows = await _db.EntityRelationships.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].FromEntityId.Should().Be(_kept.Id);
        rows[0].ToEntityId.Should().Be(_meshullam.Id);
    }

    /// <summary>
    /// The folded record's clauses follow the kept record's own, and one that says what the kept
    /// record already says is not said a second time.
    /// </summary>
    [Fact]
    public async Task TheFoldedRecordsClausesFollowTheKeptRecordsOwnAndARepeatedOneIsSaidOnce()
    {
        _db.EntityDescriptors.Add(Clause(_kept, 1, "son-of"));
        _db.EntityDescriptors.Add(Clause(_kept, 2, "father-of"));
        _db.EntityDescriptors.Add(Clause(_folded, 1, "son-of"));
        _db.EntityDescriptors.Add(Clause(_folded, 2, "brother-of"));
        await _db.SaveChangesAsync();

        await _loader.Fold(List(("hilkiah-3", "hilkiah-6")));

        var clauses = await _db.EntityDescriptors.AsNoTracking()
            .Where(d => d.EntityId == _kept.Id).OrderBy(d => d.Ordinal).Select(d => new { d.Ordinal, d.Relation })
            .ToListAsync();
        clauses.Select(c => c.Relation).Should().Equal("son-of", "father-of", "brother-of");
        clauses.Select(c => c.Ordinal).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
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

    /// <summary>
    /// The person register reached each record for a different one of its bearers. Folded, the man
    /// is both, and the claim says both; what the dataset said of each in the same words is said once.
    /// </summary>
    [Fact]
    public async Task AClaimBothRecordsCarryFromOneSourceSaysWhatEachSaid()
    {
        Claims(_kept, LinkMethod.ModelReading, 0.9, "the register", "Hilkiah #3, of 1 Chronicles 9:11");
        Claims(_folded, LinkMethod.ModelReading, 0.7, "the register", "Hilkiah #6, of Nehemiah 11:11");
        Claims(_kept, LinkMethod.StatedBySource, null, BibleData, "holds this man as a record of its own");
        Claims(_folded, LinkMethod.StatedBySource, null, BibleData, "holds this man as a record of its own");
        await _db.SaveChangesAsync();

        await _loader.Fold(List(("hilkiah-3", "hilkiah-6")));

        var claims = await _db.EntityClaims.AsNoTracking().ToListAsync();
        claims.Should().HaveCount(2).And.OnlyContain(c => c.EntityId == _kept.Id);
        var register = claims.Single(c => c.Method == LinkMethod.ModelReading);
        register.Note.Should().Be("Hilkiah #3, of 1 Chronicles 9:11; Hilkiah #6, of Nehemiah 11:11");
        register.Confidence.Should().Be(0.9);
    }

    private void Claims(Entity entity, LinkMethod method, double? confidence, string source, string note) =>
        _db.EntityClaims.Add(new EntityClaim
        {
            Entity = entity, Method = method, Confidence = confidence, Source = source, Note = note,
        });

    [Fact]
    public async Task APairReadLessSurelyThanTheListSaysSo()
    {
        await _loader.Fold(new DuplicateRecordList(LinkMethod.ModelReading, 0.9, "a test",
            [new DuplicateRecordPair("H2518", "hilkiah-3", "hilkiah-6", "one list written twice", 0.8)]));

        (await _db.MergedRecords.AsNoTracking().SingleAsync()).Confidence.Should().Be(0.8);
    }

    /// <summary>A pair the owner ruled says so, and carries no confidence the list gives its readings.</summary>
    [Fact]
    public async Task APairTheOwnerRuledIsHisDecision()
    {
        await _loader.Fold(new DuplicateRecordList(LinkMethod.ModelReading, 0.9, "a test",
            [new DuplicateRecordPair("H2518", "hilkiah-3", "hilkiah-6", "one man", Source: "the project owner", Method: "manual")]));

        var merged = await _db.MergedRecords.AsNoTracking().SingleAsync();
        merged.Method.Should().Be(LinkMethod.Manual);
        merged.Confidence.Should().BeNull();
        merged.Source.Should().Be("the project owner");
    }

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
    /// A record a pass wrote again under an address already folded. The pair that folded the address
    /// leaves a different record standing under it alone, and the address keeps arriving where it did;
    /// the record that was folded before goes where it went the first time, writing no second address.
    /// </summary>
    [Fact]
    public async Task ARecordWrittenAgainUnderAFoldedAddressIsNotFoldedAsTheAddressesOwner()
    {
        await _loader.Fold(List(("hilkiah-3", "hilkiah-6")));
        Record("meshullam-16", "person:Meshullam_16", "a namesake");
        await _db.SaveChangesAsync();
        await _loader.Fold(List(("meshullam-15", "meshullam-16")));
        var again = Record("meshullam-16", "person:Hilkiah_6", "son of Meshullam (NEH 11:11), written again");
        await _db.SaveChangesAsync();

        var byTheList = await _loader.Fold(List(("meshullam-15", "meshullam-16")));

        byTheList.Folded.Should().Be(0);
        byTheList.Missing.Should().Be(1);
        (await _db.Entities.AnyAsync(e => e.Id == again.Id)).Should().BeTrue();

        var byItsOwnFold = await _loader.Fold(List(("hilkiah-3", "meshullam-16")));

        byItsOwnFold.Folded.Should().Be(1);
        (await _db.Entities.AnyAsync(e => e.Id == again.Id)).Should().BeFalse();
        var addresses = await _db.MergedRecords.AsNoTracking().ToDictionaryAsync(m => m.Slug, m => m.EntityId);
        addresses.Should().HaveCount(2);
        addresses["meshullam-16"].Should().Be(_meshullam.Id);
        addresses["hilkiah-6"].Should().Be(_kept.Id);
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

    /// <summary>
    /// A man the dataset wrote for a people — the Amorite of Genesis 10:16 — folds into the people
    /// only where the pair says it crosses kinds, and what was read of him as a man does not follow.
    /// </summary>
    [Fact]
    public async Task APersonFoldsIntoAPeopleOnlyWhereThePairSaysSo()
    {
        var amorites = new Entity
        {
            Kind = EntityKind.People, Slug = "amorites", Name = "Amorites", SourceId = "essenthos:people:H567",
            Source = "a test",
        };
        var amor = Record("amor", "person:Amor_1", "son of Canaan (GEN 10:15)");
        _db.Entities.Add(amorites);
        Cites(amor, 1, 10, 16);
        _db.EntityDescriptors.Add(Clause(amor, 1, "son-of"));
        await _db.SaveChangesAsync();

        (await _loader.Fold(List(("amorites", "amor")))).Missing.Should().Be(1, "a man is not folded into a people unasked");

        var across = new DuplicateRecordList(LinkMethod.ModelReading, 0.9, "a test",
            [new DuplicateRecordPair("H567", "amorites", "amor", "the Amorite", AcrossKinds: true)]);
        (await _loader.Fold(across)).Folded.Should().Be(1);

        (await _db.Entities.AnyAsync(e => e.Slug == "amor")).Should().BeFalse();
        (await _db.EntityVerses.AsNoTracking().SingleAsync(v => v.CanonicalBook == 1)).EntityId.Should().Be(amorites.Id);
        (await _db.EntityDescriptors.AnyAsync(d => d.EntityId == amorites.Id)).Should().BeFalse(
            "the people is not Canaan's son");
    }

    [Fact]
    public async Task APairNamingARecordNotHeldIsLeftAlone()
    {
        var outcome = await _loader.Fold(List(("hilkiah-3", "hilkiah-99")));

        outcome.Missing.Should().Be(1);
        (await _db.Entities.CountAsync()).Should().Be(3);
    }

    private static readonly DuplicateRecordSplit Helkiah =
        new("hilkiah-6", "meshullam-15", "Helkiah", ["GEN 9:11"], "a ruling");

    private void Labels(Entity entity, int book, int chapter, int verse, string? label, string source) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = entity, CanonicalBook = book, CanonicalChapter = chapter, CanonicalVerse = verse, Label = label,
            Source = source,
        });

    /// <summary>
    /// A record the dataset wrote for two men. The second man's verse moves to his record with the
    /// word that names him there and the name the dataset held for him; the first man keeps the rest.
    /// </summary>
    [Fact]
    public async Task ASplitMovesTheSecondMansWordsVersesAndName()
    {
        Names(_folded, LinkMethod.Lexical, 0.9, "the lexicon");
        Labels(_folded, 1, 9, 11, "Helkiah", BibleData);
        Labels(_folded, 1, 9, 11, null, "our own words");
        Labels(_folded, 16, 11, 11, "Hilkiah", BibleData);
        _db.EntityNames.Add(new EntityName { Entity = _folded, Label = "Hilkiah", HebrewStrongNumber = "H2518" });
        _db.EntityNames.Add(new EntityName { Entity = _folded, Label = "Helkiah", HebrewStrongNumber = "H2518" });
        await _db.SaveChangesAsync();

        var (parted, _) = await _loader.Split([Helkiah]);

        parted.Should().Be(4);
        (await _db.WordEntities.AsNoTracking().SingleAsync()).EntityId.Should().Be(_meshullam.Id);
        var verses = await _db.EntityVerses.AsNoTracking().ToListAsync();
        verses.Where(v => v.CanonicalBook == 1).Should().HaveCount(2).And.OnlyContain(v => v.EntityId == _meshullam.Id);
        verses.Single(v => v.CanonicalBook == 16).EntityId.Should().Be(_folded.Id);
        var names = await _db.EntityNames.AsNoTracking().ToListAsync();
        names.Single(n => n.Label == "Helkiah").EntityId.Should().Be(_meshullam.Id);
        names.Single(n => n.Label == "Hilkiah").EntityId.Should().Be(_folded.Id);
    }

    /// <summary>
    /// Two records that both hold the name, the dataset's verses on the wrong one: the verses and the
    /// words move and each record keeps its own name.
    /// </summary>
    [Fact]
    public async Task ASplitWithoutANameMovesTheVersesAndLeavesTheNames()
    {
        Names(_folded, LinkMethod.Lexical, 0.9, "the lexicon");
        Labels(_folded, 1, 9, 11, "Helkiah", BibleData);
        _db.EntityNames.Add(new EntityName { Entity = _folded, Label = "Helkiah", HebrewStrongNumber = "H2518" });
        await _db.SaveChangesAsync();

        var (parted, _) = await _loader.Split([Helkiah with { Name = null }]);

        parted.Should().Be(2);
        (await _db.WordEntities.AsNoTracking().SingleAsync()).EntityId.Should().Be(_meshullam.Id);
        (await _db.EntityVerses.AsNoTracking().SingleAsync()).EntityId.Should().Be(_meshullam.Id);
        (await _db.EntityNames.AsNoTracking().SingleAsync()).EntityId.Should().Be(_folded.Id);
    }

    /// <summary>
    /// His own record already names the word, by the owner's ruling: the dataset's annotation is not
    /// kept beside it, and what it said stays as the ruling's claim.
    /// </summary>
    [Fact]
    public async Task ASplitJoinsAnAnnotationHisRecordAlreadyHas()
    {
        var ruling = Names(_meshullam, LinkMethod.Manual, null, "the owner's ruling");
        Names(_folded, LinkMethod.Lexical, 0.9, "the lexicon");
        await _db.SaveChangesAsync();

        await _loader.Split([Helkiah]);

        var row = await _db.WordEntities.AsNoTracking().Include(a => a.Claims).SingleAsync();
        row.Id.Should().Be(ruling.Id);
        row.Claims.Select(c => c.Source).Should().Contain("the lexicon");
    }

    /// <summary>
    /// A relationship or a clause citing the second man's verse is about him: it moves with the verse,
    /// by row, and one citing a verse the first man keeps stays.
    /// </summary>
    [Fact]
    public async Task ASplitTakesTheRelationshipsAndClausesItsVersesCite()
    {
        _db.EntityRelationships.Add(new EntityRelationship
        {
            From = _kept, To = _folded, Type = "father-of", Category = RelationshipCategories.Read,
            CanonicalBook = 1, CanonicalChapter = 9, CanonicalVerse = 11,
            Method = LinkMethod.Manual, Source = Decided,
        });
        _db.EntityRelationships.Add(new EntityRelationship
        {
            From = _folded, To = _kept, Type = "son-of", Category = RelationshipCategories.Read,
            CanonicalBook = 16, CanonicalChapter = 11, CanonicalVerse = 11,
            Method = LinkMethod.Manual, Source = Decided,
        });
        var staying = Clause(_meshullam, 1, "son-of");
        staying.Target = _kept;
        _db.EntityDescriptors.Add(staying);
        var leaving = Clause(_folded, 1, "son-of");
        leaving.Target = _kept;
        leaving.CanonicalBook = 1;
        leaving.CanonicalChapter = 9;
        leaving.CanonicalVerse = 11;
        _db.EntityDescriptors.Add(leaving);
        await _db.SaveChangesAsync();

        await _loader.Split([Helkiah with { Name = null }]);

        var rows = await _db.EntityRelationships.AsNoTracking().ToListAsync();
        rows.Single(r => r.CanonicalBook == 1).ToEntityId.Should().Be(_meshullam.Id);
        rows.Single(r => r.CanonicalBook == 16).FromEntityId.Should().Be(_folded.Id);
        var clauses = await _db.EntityDescriptors.AsNoTracking().Where(d => d.EntityId == _meshullam.Id)
            .OrderBy(d => d.Ordinal).ToListAsync();
        clauses.Select(d => d.Ordinal).Should().Equal(1, 2);
        clauses[1].TargetEntityId.Should().Be(_kept.Id);
    }

    /// <summary>
    /// Hilkiah's verse in Nehemiah stays his, and the split has given Meshullam the name as well: the
    /// word Hilkiah keeps no longer resolves by the number alone, and it and the word the King James
    /// was carried from it say the split chose them rather than being withdrawn as a name two men bear.
    /// </summary>
    [Fact]
    public async Task AWordTheSplitLeavesUnderANameItMadeTwoMensSaysTheSplitChoseIt()
    {
        _db.AddBook(_hebrew, 16, "Nehemiah", (11, 11, ["חִלְקִיָּה"]));
        var english = Corpus.Add(_db, "KJV", TextKind.Translation, "en");
        _db.AddBook(english, 16, "Nehemiah", (11, 11, ["Hilkiah"]));
        _db.EntityNames.Add(new EntityName { Entity = _folded, Label = "Hilkiah", HebrewStrongNumber = "H2518" });
        _db.EntityNames.Add(new EntityName { Entity = _folded, Label = "Helkiah", HebrewStrongNumber = "H2518" });
        await _db.SaveChangesAsync();

        var hebrew = _db.WordAt(_hebrew, 11, 11, 1);
        hebrew.StrongNumber = "H2518";
        var resolution = EntityAnnotationLoader.Written[0];
        var seed = new WordEntity
        {
            Word = hebrew, Entity = _folded, Method = LinkMethod.StrongNumber, Confidence = 0.99,
            Source = resolution, Note = "H2518, which BHSA marks pers",
            Claims =
            [
                new WordEntityClaim { Method = LinkMethod.StrongNumber, Confidence = 0.9, Source = resolution },
                new WordEntityClaim { Method = LinkMethod.StrongNumber, Confidence = 0.99, Source = "the verse list" },
            ],
        };
        _db.WordEntities.Add(seed);
        await _db.SaveChangesAsync();
        var carried = new WordEntity
        {
            Word = _db.WordAt(english, 11, 11, 1), Entity = _folded, Method = LinkMethod.StrongNumber,
            Confidence = 0.9702, Source = resolution, Note = $"through bhsa word {hebrew.Id}, linked by aligner",
            Claims = [new WordEntityClaim { Method = LinkMethod.StrongNumber, Confidence = 0.882, Source = resolution }],
        };
        _db.WordEntities.Add(carried);
        await _db.SaveChangesAsync();

        await _loader.Split([Helkiah]);

        var rows = await _db.WordEntities.AsNoTracking().Include(a => a.Claims)
            .Where(a => a.EntityId == _folded.Id).ToListAsync();
        rows.Select(a => a.Id).Should().BeEquivalentTo([seed.Id, carried.Id]);
        rows.Should().OnlyContain(a => a.Method == LinkMethod.Manual && a.Source == DuplicateRecordLoader.Chose);
        rows.Single(a => a.Id == carried.Id).Confidence.Should().Be(0.9702, "the carrying pass must find it unchanged");
        rows.Single(a => a.Id == seed.Id).Claims.Select(c => (c.Method, c.Source)).Should().BeEquivalentTo(
            [(LinkMethod.Manual, DuplicateRecordLoader.Chose), (LinkMethod.Manual, "the verse list")]);
        var check = new CorpusCheck(_db, NullLogger<CorpusCheck>.Instance);
        (await check.Measure()).Integrity
            .Single(i => i.Breaks == "words a name-resolution annotated although the name is several people's")
            .Found.Should().Be(0);
    }

    /// <summary>
    /// The annotation pass ran after the split's record came to bear the name and before the split said
    /// what it chose, and withdrew Hilkiah's word in Nehemiah as a name two men bear. The ruling lists
    /// the verse as his: the word and the King James word it renders name him again on the ruling, as
    /// the words the split chose would, once and not twice.
    /// </summary>
    [Fact]
    public async Task AWordWithdrawnInAVerseTheRulingKeepsNamesTheRecordAgain()
    {
        var english = KeptVerse();
        Cites(_folded, 16, 11, 11);
        await _db.SaveChangesAsync();
        var keeps = Helkiah with { Name = null, Keeps = ["NEH 11:11"] };

        var (_, kept) = await _loader.Split([keeps]);

        kept.Should().Be(2);
        var rows = await _db.WordEntities.AsNoTracking().Include(a => a.Claims).Include(a => a.Word)
            .Where(a => a.EntityId == _folded.Id).ToListAsync();
        rows.Should().HaveCount(2).And.OnlyContain(a => a.Method == LinkMethod.Manual && a.Source == DuplicateRecordLoader.Chose);
        var seed = rows.Single(a => a.Word!.TextId == _hebrew.Id);
        seed.Confidence.Should().Be(EntityAnnotationLoader.Corroborated, "the verse list names him in the verse");
        seed.Claims.Select(c => (c.Method, c.Source)).Should().BeEquivalentTo(
            [(LinkMethod.Manual, DuplicateRecordLoader.Chose), (LinkMethod.StatedBySource, EntityAnnotationLoader.VerseList)]);
        rows.Single(a => a.Word!.TextId == english.Id).Note.Should().StartWith("through BHSA word ");
        (await _loader.Split([keeps])).Kept.Should().Be(0);
        var check = new CorpusCheck(_db, NullLogger<CorpusCheck>.Instance);
        (await check.Measure()).Integrity
            .Single(i => i.Breaks == "words a name-resolution annotated although the name is several people's")
            .Found.Should().Be(0);
    }

    /// <summary>
    /// A third Hilkiah bears the name: the resolution never answered the word, and a ruling on which
    /// verses are whose between two of them does not choose among three.
    /// </summary>
    [Fact]
    public async Task AVerseTheRulingKeepsIsLeftAloneWhereAThirdRecordBearsTheName()
    {
        KeptVerse();
        _db.EntityNames.Add(new EntityName { Entity = _kept, Label = "Hilkiah", HebrewStrongNumber = "H2518" });
        await _db.SaveChangesAsync();

        var (_, kept) = await _loader.Split([Helkiah with { Name = null, Keeps = ["NEH 11:11"] }]);

        kept.Should().Be(0);
        (await _db.WordEntities.AnyAsync()).Should().BeFalse();
    }

    /// <summary>
    /// Nehemiah 11:11 in the Hebrew, the name marked a person's and borne by Hilkiah and Meshullam
    /// alike, and in the King James linked to it by the translators' mapping; nothing names either word.
    /// </summary>
    private Text KeptVerse()
    {
        _db.AddBook(_hebrew, 16, "Nehemiah", (11, 11, ["חִלְקִיָּה"]));
        var english = Corpus.Add(_db, "KJV", TextKind.Translation, "en");
        _db.AddBook(english, 16, "Nehemiah", (11, 11, ["Hilkiah"]));
        _db.EntityNames.Add(new EntityName { Entity = _folded, Label = "Hilkiah", HebrewStrongNumber = "H2518" });
        _db.EntityNames.Add(new EntityName { Entity = _meshullam, Label = "Hilkiah", HebrewStrongNumber = "H2518" });
        _db.SaveChanges();

        var hebrew = _db.WordAt(_hebrew, 11, 11, 1);
        hebrew.StrongNumber = "H2518";
        hebrew.Morphology = JsonDocument.Parse("""{"pos": "nmpr", "nameType": "pers"}""");
        var rendering = _db.WordAt(english, 11, 11, 1);
        var link = new Link
        {
            FromTextId = _hebrew.Id, ToTextId = english.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = hebrew, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = rendering, Side = LinkSide.To });
        _db.SaveChanges();
        return english;
    }

    [Fact]
    public async Task ASplitMadeFindsNothingLeftToMove()
    {
        Names(_folded, LinkMethod.Lexical, 0.9, "the lexicon");
        await _db.SaveChangesAsync();
        await _loader.Split([Helkiah]);

        (await _loader.Split([Helkiah])).Should().Be((0, 0));
        (await _loader.Split([Helkiah with { To = "nobody" }])).Should().Be((0, 0));
    }

    /// <summary>A pair read on another day, under another ruling, says whose reading it is.</summary>
    [Fact]
    public async Task APairCarriesItsOwnSourceWhereItHasOne()
    {
        await _loader.Fold(new DuplicateRecordList(LinkMethod.ModelReading, 0.9, "a test",
            [new DuplicateRecordPair("H2518", "hilkiah-3", "hilkiah-6", "one word", Source: "the owner's rule")]));

        (await _db.MergedRecords.AsNoTracking().SingleAsync()).Source.Should().Be("the owner's rule");
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
        list.Merges.Should().OnlyContain(m => m.Confidence == null || (m.Confidence > 0 && m.Confidence <= 1));
        list.Splits.Should().NotBeNullOrEmpty();
        list.Splits!.SelectMany(split => split.Verses).Should().OnlyContain(span => ScriptureSpan.TryParse(span) != null);
        list.Splits.Select(split => split.From).Should().NotIntersectWith(list.Merges.Select(m => m.Folds));
        list.Splits.SelectMany(split => split.Keeps ?? []).Should().OnlyContain(span => ScriptureSpan.TryParse(span) != null);
        list.Splits.Single(split => split.To == "ahasuerus-father-of-darius").Keeps.Should().Contain("EZR 4:6");
    }

    /// <summary>
    /// A record that held several people keeps the man its line and picture were made for, and no
    /// verse of it is sent to two records or back to the record it leaves: Zedekiah's envoy keeps
    /// Jeremiah 21:1 and 38:1 while the priest's verses go, and the son of Hoshaiah leaves the
    /// Maachathite's son only at Jeremiah 42:1.
    /// </summary>
    [Fact]
    public void TheShippedSplitsSendEachVerseOfARecordToOneOtherMan()
    {
        var splits = DuplicateRecordLoader.Read().Splits!;

        splits.Should().OnlyContain(split => split.From != split.To && split.Why.Length > 0);
        splits.SelectMany(split => split.Verses.Select(verse => (split.From, verse)))
            .Should().OnlyHaveUniqueItems("a verse of one record goes to one other record");

        var priest = splits.Single(split => split.From == "pashhur" && split.To == "pashhur-the-priest").Verses;
        priest.Should().Contain(["1CH 9:12", "NEH 11:12", "EZR 2:38", "NEH 7:41"]).And.NotContain(["JER 21:1", "JER 38:1"]);
        splits.Should().ContainSingle(split => split.From == "jaazaniah").Which.Verses.Should().Equal("JER 42:1");
        splits.Where(split => split.From == "malchijah-2").SelectMany(split => split.Verses)
            .Should().NotContain("NEH 8:4", "the record keeps the man at Ezra's left hand its portrait shows");
    }
}
