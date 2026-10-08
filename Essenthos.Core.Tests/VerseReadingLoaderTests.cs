using Essenthos.Core.Corpus;
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
/// The verses read for the records they speak of: the reference of the verse under this project's
/// name and its kind, the word that stands for the record annotated where it is a noun or a name, and
/// what is never touched.
///
/// <para>
/// 2 Kings 25:6 says <em>the king of Babylon</em> of Nebuchadnezzar and 25:7 <em>him</em>; Ezekiel
/// 39:23 says <em>the house of Israel</em>, whose <em>Israel</em> already names Jacob; 1 Samuel 17:42
/// says <em>the Philistine</em> of Goliath, whose word already names the Philistines; and in 2 Kings
/// 25:1 <em>Babylon</em> names the city.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class VerseReadingLoaderTests : IDisposable
{
    private const string King = "H4428";

    private const string Babel = "H894";

    private const string Israel = "H3478";

    private const string Philistine = "H6430";

    private const int FirstSamuel = 9;

    private const int SecondKings = 12;

    private const int Ezekiel = 26;

    private static readonly VerseReadings Header = new(
        "a test", "a test", ["claude-sonnet-5-5", "claude-opus-5-5"], "claude-opus-5-5", "2026-09-30", []);

    private readonly AppDbContext _db;
    private readonly Text _hebrew;
    private readonly Text _english;
    private readonly Entity _nebuchadnezzar;
    private readonly Entity _babylon;
    private readonly Entity _jacob;
    private readonly Entity _israelites;
    private readonly Entity _goliath;
    private readonly Entity _philistines;

    public VerseReadingLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        _hebrew = new Text
        {
            Slug = EntityCandidates.Witness, Name = "BHSA", Kind = TextKind.CriticalEdition, Language = "hbo",
        };
        _db.Texts.Add(_hebrew);
        _db.AddBook(_hebrew, SecondKings, "Kings",
            (25, 1, ["נבכדנאצר", "בבל"]), (25, 6, ["מלך", "בבל"]), (25, 7, ["אתו"]));
        _db.AddBook(_hebrew, Ezekiel, "Ezekiel", (39, 23, ["בית", "ישראל"]));
        _db.AddBook(_hebrew, FirstSamuel, "Samuel", (17, 42, ["הפלשתי"]));
        _english = new Text { Slug = "KJV", Name = "KJV", Kind = TextKind.Translation, Language = "eng" };
        _db.Texts.Add(_english);
        _db.AddBook(_english, SecondKings, "Kings", (25, 6, ["the", "king", "of", "Babylon"]));
        _db.AddBook(_english, Ezekiel, "Ezekiel", (39, 23, ["the", "house", "of", "Israel"]));
        _db.SaveChanges();

        _db.WordAt(_hebrew, 25, 1, 2).StrongNumber = Babel;
        _db.WordAt(_hebrew, 25, 6, 1).StrongNumber = King;
        _db.WordAt(_hebrew, 25, 6, 2).StrongNumber = Babel;
        _db.WordAt(_hebrew, 39, 23, 2).StrongNumber = Israel;
        _db.WordAt(_hebrew, 17, 42, 1).StrongNumber = Philistine;

        _nebuchadnezzar = Add("nebuchadnezzar", EntityKind.Person);
        _babylon = Add("babylon", EntityKind.Place);
        _jacob = Add("jacob", EntityKind.Person);
        _israelites = Add("israelites", EntityKind.People);
        _goliath = Add("goliath", EntityKind.Person);
        _philistines = Add("philistines", EntityKind.People);
        _db.SaveChanges();

        Names(_db.WordAt(_hebrew, 25, 6, 2), _babylon);
        Names(_db.WordAt(_hebrew, 39, 23, 2), _jacob);
        Names(_db.WordAt(_english, 39, 23, 4), _jacob);
        Names(_db.WordAt(_hebrew, 17, 42, 1), _philistines);
        Link(_db.WordAt(_english, 25, 6, 2), _db.WordAt(_hebrew, 25, 6, 1));
        Link(_db.WordAt(_english, 39, 23, 4), _db.WordAt(_hebrew, 39, 23, 2));
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

    private Entity Add(string slug, EntityKind kind)
    {
        var entity = new Entity { Kind = kind, Slug = slug, Name = slug, SourceId = slug, Source = "a test" };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Names(Word word, Entity entity) =>
        _db.WordEntities.Add(new WordEntity
        {
            Word = word, Entity = entity, Method = LinkMethod.RuleBased, Confidence = 0.7, Source = "a rule",
        });

    private void Link(Word rendering, Word word)
    {
        var link = new Link
        {
            FromTextId = _english.Id, ToTextId = _hebrew.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = rendering, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = word, Side = LinkSide.To });
    }

    private VerseReadingLoader Loader() => new(_db, NullLogger<VerseReadingLoader>.Instance);

    private static VerseReadings File(params VerseReading[] readings) => Header with { Readings = readings };

    private static VerseReading TheKingOfBabylon(string strong = King, int position = 1) =>
        new("2KI 25:6", "nebuchadnezzar", ReferenceKinds.SpokenOf, "title", "the king of Babylon",
            [new VerseReadingWord(EntityCandidates.Witness, position, strong, "מלך")], 0.9,
            "'the king of Babylon' is the Nebuchadnezzar of 25:1", "the king of 25:1");

    private static VerseReading Him() =>
        new("2KI 25:7", "nebuchadnezzar", ReferenceKinds.SpokenOf, "pronoun", "him", [], 0.9, "first", "second");

    private static VerseReading TheHouseOfIsrael() =>
        new("EZK 39:23", "israelites", ReferenceKinds.Named, "name", "the house of Israel",
            [new VerseReadingWord(EntityCandidates.Witness, 2, Israel, "ישראל")], 0.8,
            "the nation that went into captivity", "a nation, not the man", "the third reading: the nation");

    private static VerseReading ThePhilistine() =>
        new("1SA 17:42", "goliath", ReferenceKinds.SpokenOf, "title", "the Philistine",
            [new VerseReadingWord(EntityCandidates.Witness, 1, Philistine, "הפלשתי")], 0.9, "first", "second");

    private async Task<List<(long Word, int Entity)>> Named()
    {
        _db.ChangeTracker.Clear();
        return [.. (await _db.WordEntities.ToListAsync()).Select(a => (a.WordId, a.EntityId))];
    }

    /// <summary>
    /// Not printed, a title: the verse is listed as speaking of him, under the words that stand for
    /// him, and the head noun names him in the original and in the translation that renders it.
    /// </summary>
    [Fact]
    public async Task ATitleIsAReferenceOfTheVerseAndAnAnnotationOfItsHeadNoun()
    {
        var file = File(TheKingOfBabylon());

        var outcome = await Loader().Load(file);

        var row = await _db.EntityVerses.SingleAsync(v => v.EntityId == _nebuchadnezzar.Id);
        (row.CanonicalBook, row.CanonicalChapter, row.CanonicalVerse).Should().Be((SecondKings, 25, 6));
        row.Label.Should().Be("the king of Babylon");
        row.Source.Should().Be(
            "Essenthos, read from the verse by two language models, claude-sonnet-5-5 and claude-opus-5-5, on " +
            "2026-09-30: the verse speaks of the record and does not name it");
        ReferenceKinds.IsSpokenOf(row.Source).Should().BeTrue();

        var king = _db.WordAt(_hebrew, 25, 6, 1).Id;
        var rendering = _db.WordAt(_english, 25, 6, 2).Id;
        (await Named()).Should().Contain([(king, _nebuchadnezzar.Id), (rendering, _nebuchadnezzar.Id)]);
        var annotation = await _db.WordEntities.SingleAsync(a => a.WordId == king);
        annotation.Method.Should().Be(LinkMethod.ModelReading);
        annotation.Confidence.Should().Be(0.9);
        annotation.Source.Should().Contain("claude-sonnet-5-5").And.Contain("claude-opus-5-5").And.Contain("2026-09-30");
        annotation.Note.Should().StartWith("2KI 25:6, title: 'the king of Babylon'");
        outcome.Seeded.Should().Be(1);
        outcome.Added.Should().Be(1);
    }

    /// <summary>A pronoun is a reference of the verse and no annotation: there is no noun to put it on.</summary>
    [Fact]
    public async Task APronounIsAReferenceOfTheVerseAlone()
    {
        var before = await Named();

        var outcome = await Loader().Load(File(Him()));

        (await _db.EntityVerses.SingleAsync(v => v.EntityId == _nebuchadnezzar.Id)).Label.Should().Be("him");
        (await Named()).Should().BeEquivalentTo(before);
        outcome.Seeded.Should().Be(0);
    }

    /// <summary>
    /// A people: Israel in the house of Israel keeps Jacob and gains the Israelites, in the original
    /// and in the translation, and the reference is one that names. It was settled by a third reading,
    /// and says so.
    /// </summary>
    [Fact]
    public async Task APeopleStandsBesideTheManItIsNamedAfter()
    {
        await Loader().Load(File(TheHouseOfIsrael()));

        var hebrew = _db.WordAt(_hebrew, 39, 23, 2).Id;
        var english = _db.WordAt(_english, 39, 23, 4).Id;
        (await Named()).Should().Contain(
        [
            (hebrew, _jacob.Id), (hebrew, _israelites.Id), (english, _jacob.Id), (english, _israelites.Id),
        ]);
        var row = await _db.EntityVerses.SingleAsync(v => v.EntityId == _israelites.Id);
        ReferenceKinds.IsSpokenOf(row.Source).Should().BeFalse();
        row.Source.Should().Contain("a third time where the two differed").And.EndWith("the name in the verse names the record");
    }

    /// <summary>
    /// Where the pass telling the man from the people has read the name as the man, the people is not
    /// written beside him, and a second run agrees with the first.
    /// </summary>
    [Fact]
    public async Task ANameReadAsTheManIsNotGivenThePeopleBesideHim()
    {
        var hebrew = _db.WordAt(_hebrew, 39, 23, 2).Id;
        await _db.WordEntities.Where(a => a.WordId == hebrew)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.Source, EponymReadingLoader.ReadingSource));

        var outcome = await Loader().Load(File(TheHouseOfIsrael()));
        var again = await Loader().Load(File(TheHouseOfIsrael()));

        (await Named()).Should().NotContain((hebrew, _israelites.Id));
        outcome.Named.Should().Be(1);
        again.AlreadyLoaded.Should().BeTrue();
    }

    /// <summary>The same where the reading only stands as a claim on a row another pass wrote.</summary>
    [Fact]
    public async Task ANameReadAsTheManOnAnotherPassesRowIsNotGivenThePeopleEither()
    {
        var hebrew = _db.WordAt(_hebrew, 39, 23, 2).Id;
        var row = await _db.WordEntities.SingleAsync(a => a.WordId == hebrew);
        _db.WordEntityClaims.Add(new WordEntityClaim
        {
            WordEntityId = row.Id, Method = LinkMethod.ModelReading, Confidence = 0.9,
            Source = EponymReadingLoader.ReadingSource,
        });
        await _db.SaveChangesAsync();

        (await Loader().Load(File(TheHouseOfIsrael()))).Named.Should().Be(1);
        (await Named()).Should().NotContain((hebrew, _israelites.Id));
    }

    /// <summary>
    /// The same where the owner's ruling on the construct — the name after the word for a tribe is the
    /// man — claims the row: the man-or-people pass keeps that answer alone and would take the people
    /// back on every load, so it is not written in the first place.
    /// </summary>
    [Fact]
    public async Task ANameTheTribeConstructAnswersIsNotGivenThePeopleBesideIt()
    {
        var hebrew = _db.WordAt(_hebrew, 39, 23, 2).Id;
        var row = await _db.WordEntities.SingleAsync(a => a.WordId == hebrew);
        _db.WordEntityClaims.Add(new WordEntityClaim
        {
            WordEntityId = row.Id, Method = LinkMethod.RuleBased, Confidence = 1.0, Source = TribeNameLoader.Source,
        });
        await _db.SaveChangesAsync();

        var outcome = await Loader().Load(File(TheHouseOfIsrael()));
        var again = await Loader().Load(File(TheHouseOfIsrael()));

        (await Named()).Should().NotContain((hebrew, _israelites.Id));
        outcome.Named.Should().Be(1);
        again.AlreadyLoaded.Should().BeTrue();
    }

    /// <summary>The Philistine: the word keeps the people and gains the man it stands for.</summary>
    [Fact]
    public async Task AManStandsBesideThePeopleTheWordNames()
    {
        await Loader().Load(File(ThePhilistine()));

        var word = _db.WordAt(_hebrew, 17, 42, 1).Id;
        (await Named()).Should().Contain([(word, _philistines.Id), (word, _goliath.Id)]);
    }

    /// <summary>
    /// A word that names another record is left, and a word that is no longer the one read: the
    /// reference of the verse is kept either way.
    /// </summary>
    [Fact]
    public async Task AWordNamingAnotherRecordOrMovedIsLeft()
    {
        var before = await Named();

        var onBabylon = await Loader().Load(File(TheKingOfBabylon(Babel, position: 2)));
        (await Named()).Should().BeEquivalentTo(before);
        onBabylon.Named.Should().Be(1);

        var moved = await Loader().Load(File(TheKingOfBabylon("H9999")));
        (await Named()).Should().BeEquivalentTo(before);
        moved.Moved.Should().Be(1);
        (await _db.EntityVerses.CountAsync(v => v.EntityId == _nebuchadnezzar.Id)).Should().Be(1);
    }

    /// <summary>A second run writes nothing, and a reading the file no longer holds is taken back with its word.</summary>
    [Fact]
    public async Task ItIsIdempotentAndFollowsTheFile()
    {
        await Loader().Load(File(TheKingOfBabylon(), Him()));
        var rows = await _db.EntityVerses.Select(v => v.Id).OrderBy(id => id).ToListAsync();
        var words = await _db.WordEntities.Select(a => a.Id).OrderBy(id => id).ToListAsync();

        var again = await Loader().Load(File(TheKingOfBabylon(), Him()));

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.EntityVerses.Select(v => v.Id).OrderBy(id => id).ToListAsync()).Should().Equal(rows);
        (await _db.WordEntities.Select(a => a.Id).OrderBy(id => id).ToListAsync()).Should().Equal(words);

        var fewer = await Loader().Load(File(Him()));

        fewer.Withdrawn.Should().Be(1);
        (await _db.EntityVerses.SingleAsync(v => v.EntityId == _nebuchadnezzar.Id)).CanonicalVerse.Should().Be(7);
        (await Named()).Should().NotContain(a => a.Entity == _nebuchadnezzar.Id);
    }

    /// <summary>A record the encyclopedia does not hold is named in the outcome and nothing is written for it.</summary>
    [Fact]
    public async Task AReadingOfAnUnknownRecordIsLeft()
    {
        var outcome = await Loader().Load(File(Him() with { Record = "nobody" }));

        outcome.Missing.Should().Equal("nobody");
        (await _db.EntityVerses.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// What a reader is sent: the verse that names him first, then the one read as speaking of him,
    /// then the one a source only lists, and each says which it is. A spoken-of verse whose head noun
    /// is annotated is still spoken of.
    /// </summary>
    [Fact]
    public async Task TheListGivesTheKindOfEachVerse()
    {
        Names(_db.WordAt(_hebrew, 25, 1, 1), _nebuchadnezzar);
        await _db.SaveChangesAsync();
        await Loader().Load(File(TheKingOfBabylon(), Him()));
        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = _nebuchadnezzar.Id, CanonicalBook = SecondKings, CanonicalChapter = 25, CanonicalVerse = 1,
            Source = "Essenthos, from the words this corpus annotates to the person or the place they name",
        });
        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = _nebuchadnezzar.Id, CanonicalBook = SecondKings, CanonicalChapter = 24, CanonicalVerse = 1,
            Source = "a gazetteer",
        });
        await _db.SaveChangesAsync();
        await _db.Database.ExecuteSqlRawAsync(DatasetLoader.NamingVerses);
        _db.ChangeTracker.Clear();

        var shown = _db.EntityVerses.Shown().Where(v => v.EntityId == _nebuchadnezzar.Id);
        var order = await EncyclopediaEndpoints.NamingFirst(shown).ToListAsync();
        var rows = await shown.ToListAsync();
        var kinds = order.Select(address => ReferenceKinds.Of(rows
            .Where(v => (v.CanonicalChapter * 1_000) + v.CanonicalVerse == address % 1_000_000)
            .Select(v => (v.Source, v.Names))));

        order.Select(address => address % 1_000_000).Should().Equal(25_001, 25_006, 25_007, 24_001);
        kinds.Should().Equal(
            ReferenceKinds.Named, ReferenceKinds.SpokenOf, ReferenceKinds.SpokenOf, ReferenceKinds.Concerning);
        rows.Single(v => v.CanonicalVerse == 6 && ReferenceKinds.IsSpokenOf(v.Source)).Names.Should().BeTrue(
            "the head noun is annotated to him");
    }

    /// <summary>
    /// The shipped readings: every reference is a verse, every kind is one the API sends, a spoken-of
    /// reading says which words stand for the record, every word names its witness, and no verse is
    /// read twice for one record.
    /// </summary>
    [Fact]
    public void TheShippedReadingsAreWellFormed()
    {
        var file = VerseReadingFiles.Read();

        file.Readers.Should().HaveCount(2);
        file.Readings.Should().NotBeEmpty();
        file.Readings.Should().OnlyContain(r => PassageReadingLoader.Place(r.Reference) != null);
        file.Readings.Should().OnlyContain(r => r.Kind == ReferenceKinds.SpokenOf || r.Kind == ReferenceKinds.Named);
        file.Readings.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Words));
        file.Readings.Should().OnlyContain(r => r.Confidence > 0 && r.Confidence <= 1);
        file.Readings.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.First) && !string.IsNullOrWhiteSpace(r.Second));
        file.Readings.SelectMany(r => r.Named ?? [])
            .Should().OnlyContain(w => (w.Text == EntityCandidates.Witness || w.Text == NestleTextSource.Slug)
                                       && w.Position > 0 && !string.IsNullOrEmpty(w.Strong));
        file.Readings.GroupBy(r => (r.Record, r.Reference)).Should().OnlyContain(g => g.Count() == 1);
        file.ReferenceSources.Should().OnlyHaveUniqueItems();
        file.ReferenceSources.Count(ReferenceKinds.IsSpokenOf).Should().Be(2);
    }
}
