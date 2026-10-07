using Essenthos.Core.Corpus;
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
/// <em>The tribe of Manasseh</em>, <em>the tribe of Judah</em> and <em>the borders of Zebulun and
/// Naphtali</em> in the Greek. The number of Μανασσῆ is borne only by the king, Ἰούδα's by the land
/// and a namesake, and Νεφθαλείμ's by nobody; the phrase says who is meant.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class GreekTribeNameTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Text _greek;
    private readonly Dictionary<string, Entity> _records = [];

    public GreekTribeNameTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        _greek = new Text { Slug = "NESTLE1904", Name = "NESTLE1904", Kind = TextKind.CriticalEdition, Language = "grc" };
        _db.Texts.Add(_greek);
        _db.AddBook(_greek, 40, "Matthew", (4, 13, ["ὁρίοις", "Ζαβουλὼν", "καὶ", "Νεφθαλείμ"]));
        _db.AddBook(_greek, 66, "Revelation",
            (5, 5, ["τῆς", "φυλῆς", "Ἰούδα"]),
            (7, 6, ["ἐκ", "φυλῆς", "Μανασσῆ"]),
            (7, 7, ["ὁ", "Μανασσῆ"]));
        _db.AddBook(_greek, 58, "Hebrews",
            (7, 14, ["ἐξ", "Ἰούδα", "ἀνατέταλκεν", "ὁ", "Κύριος", "ἡμῶν", "εἰς", "ἣν", "φυλὴν", "περὶ", "ἱερέων", "Μωϋσῆς"]));
        _db.SaveChanges();
        _db.Database.ExecuteSqlRaw(
            """
            UPDATE word SET normalised_text = lower(text), strong_number = CASE text
                WHEN 'ὁρίοις' THEN 'G3725' WHEN 'Ζαβουλὼν' THEN 'G2194' WHEN 'καὶ' THEN 'G2532'
                WHEN 'Νεφθαλείμ' THEN 'G3508' WHEN 'τῆς' THEN 'G3588' WHEN 'φυλῆς' THEN 'G5443'
                WHEN 'Ἰούδα' THEN 'G2448' WHEN 'ἐκ' THEN 'G1537' WHEN 'Μανασσῆ' THEN 'G3128' WHEN 'ὁ' THEN 'G3588'
                WHEN 'ἐξ' THEN 'G1537' WHEN 'Κύριος' THEN 'G2962' WHEN 'εἰς' THEN 'G1519' WHEN 'ἣν' THEN 'G3739'
                WHEN 'φυλὴν' THEN 'G5443' WHEN 'Μωϋσῆς' THEN 'G3475' END
            """);
        foreach (var (number, lemma, origin) in new[]
                 {
                     ("G3128", "Μανασσῆς", "H4519"), ("G2448", "Ἰουδά", "H3063 or perhaps H3194"),
                     ("G3508", "Νεφθαλείμ", "H5321"), ("G2194", "Ζαβουλών", "H2074"), ("G3475", "Μωϋσῆς", "H4872"),
                 })
        {
            _db.StrongEntries.Add(new StrongEntry
            {
                StrongNumber = number, Lemma = lemma, Definition = lemma + ", an Israelite",
                Derivation = $"of Hebrew origin ({origin});",
            });
        }

        Person("manasseh", "H4519", null);
        Person("manasseh-3", "H4519", "G3128");
        People("manassites", "manasseh", "H4519");
        Person("judah", "H3063", null);
        Person("judah-4", null, "G2448");
        Record("judah-6", EntityKind.Place, null, "G2448");
        People("judahites", "judah", "H3063");
        Person("naphtali", "H5321", null);
        People("naphtalites", "naphtali", "H5321");
        Person("zebulun", "H2074", null);
        Record("zabulon", EntityKind.Place, null, "G2194");
        People("zebulonites", "zebulun", "H2074");
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

    private Entity Record(string slug, EntityKind kind, string? hebrew, string? greek, Entity? origin = null)
    {
        var record = new Entity
        {
            Kind = kind, Slug = slug, Name = slug, SourceId = slug, Source = "a test", Origin = origin,
            Names = [new EntityName { Label = slug.Split('-')[0], HebrewStrongNumber = hebrew, GreekStrongNumber = greek, Kind = "proper name" }],
        };
        _db.Entities.Add(record);
        _records[slug] = record;
        return record;
    }

    private void Person(string slug, string? hebrew, string? greek) => Record(slug, EntityKind.Person, hebrew, greek);

    private void People(string slug, string ancestor, string hebrew) =>
        Record(slug, EntityKind.People, hebrew, null, _records[ancestor]);

    private Word Word(int book, int chapter, int verse, int position) =>
        _db.Words.Single(w => w.TextId == _greek.Id && w.Verse!.Book!.CanonicalOrdinal == book
                              && w.Verse.ChapterNumber == chapter && w.Verse.Number == verse && w.Position == position);

    private async Task<List<string>> Shown(Word word) =>
        [.. (await Annotations.AllOf(_db, word.Id, CancellationToken.None)).Select(entity => entity.Slug)];

    private GreekTribeNameLoader Loader() => new(_db, NullLogger<GreekTribeNameLoader>.Instance);

    [Fact]
    public async Task ANameAfterTheWordForATribeIsTheAncestorAndAfterABorderThePeople()
    {
        var first = await Loader().Load();

        (await Shown(Word(66, 7, 6, 3))).Should().Equal("manasseh");
        (await Shown(Word(66, 5, 5, 3))).Should().Equal("judah");
        (await Shown(Word(40, 4, 13, 2))).Should().Equal("zebulonites");
        (await Shown(Word(40, 4, 13, 4))).Should().Equal(["naphtalites"], "a name joined by καί stands in the same phrase");
        (await Shown(Word(66, 7, 7, 2))).Should().BeEmpty("a name outside the phrase is the number's to settle");
        first.Tribes.Should().Be(3, "two after φυλή and Hebrews 7:14's Judah");
        first.Realms.Should().Be(2);

        (await Loader().Load()).AlreadyLoaded.Should().BeTrue();
    }

    /// <summary>
    /// Hebrews 7:14, <em>out of Judah … of which tribe</em>: the relative and φυλή after the name
    /// call it a tribe, and the name further on is not in the phrase.
    /// </summary>
    [Fact]
    public async Task ANameTheVerseGoesOnToCallATribeIsTheAncestor()
    {
        var first = await Loader().Load();

        (await Shown(Word(58, 7, 14, 2))).Should().Equal("judah");
        (await Shown(Word(58, 7, 14, 12))).Should().BeEmpty("a name after the relative is not the one it calls a tribe");
        first.Tribes.Should().Be(3);
        (await _db.WordEntities.CountAsync(a => a.Source == GreekTribeNameLoader.AntecedentSource)).Should().Be(1);
    }

    [Fact]
    public async Task ThePhraseOutranksAResolutionByTheFormOfTheWord()
    {
        _db.WordEntities.Add(new WordEntity
        {
            WordId = Word(66, 5, 5, 3).Id, EntityId = _records["judah-6"].Id, Method = LinkMethod.Lexical,
            Confidence = 0.94, Source = "the lexicon's capitalised lemma, a test", Note = "G2448",
        });
        await _db.SaveChangesAsync();

        await Loader().Load();

        (await Shown(Word(66, 5, 5, 3))).Should().Equal("judah");
    }

    [Fact]
    public async Task ARulingHeldAsAClaimOnAnEarlierAnswerStandsAsARuling()
    {
        var word = Word(66, 7, 6, 3);
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id, EntityId = _records["manasseh"].Id, Method = LinkMethod.Lexical, Confidence = 0.96,
            Source = "the lexicon's capitalised lemma, a test", Note = "G3128",
            Claims =
            [
                new WordEntityClaim { Method = LinkMethod.Lexical, Confidence = 0.96, Source = "the lexicon's capitalised lemma, a test", Note = "G3128" },
                new WordEntityClaim { Method = LinkMethod.Manual, Confidence = null, Source = "a ruling", Note = "the tribe's ancestor" },
            ],
        });
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id, EntityId = _records["manassites"].Id, Method = LinkMethod.ModelReading, Confidence = 0.9,
            Source = "a reading of the verse", Note = "the tribe of Manasseh",
            Claims = [new WordEntityClaim { Method = LinkMethod.ModelReading, Confidence = 0.9, Source = "a reading of the verse", Note = "the tribe" }],
        });
        await _db.SaveChangesAsync();

        (await Shown(word)).First().Should().Be("manasseh", "the ruling's answer comes first; the people stands beside it");
        (await _db.Database
                .SqlQueryRaw<int>($"WITH {Annotating.Settled} SELECT entity_id AS \"Value\" FROM settled WHERE word_id = {word.Id}")
                .ToListAsync())
            .Should().Equal(_records["manasseh"].Id);
    }

    [Fact]
    public async Task AConsensusNameTheVerseGivesAnotherBearerOfTheNameIsHis()
    {
        var douay = Corpus.Add(_db, "DRA", TextKind.Translation, "eng", (1, 1, ["x"]));
        _db.AddBook(douay, 66, "Revelation", (7, 6, ["of", "Manasses"]));
        _db.SaveChanges();
        var manasses = _db.Words.Single(w => w.TextId == douay.Id && w.Surface == "Manasses");
        _db.WordEntities.Add(new WordEntity
        {
            WordId = manasses.Id, EntityId = _records["manasseh-3"].Id, Method = LinkMethod.RuleBased,
            Confidence = 0.99, Source = NameConsensusPass.Source, Note = "the word its verses share in this text: manasses",
        });
        _db.WordEntities.Add(new WordEntity
        {
            WordId = Word(66, 7, 6, 3).Id, EntityId = _records["manasseh"].Id, Method = LinkMethod.Manual,
            Confidence = null, Source = "a ruling", Note = "the tribe's ancestor",
        });
        await _db.SaveChangesAsync();
        var namesakes = new ConsensusNamesakes(_db, NullLogger<ConsensusNamesakes>.Instance);

        var first = await namesakes.Load();
        var second = await namesakes.Load();

        first.Given.Should().Be(1);
        second.Given.Should().Be(0);
        second.Withdrawn.Should().Be(0);
        (await Shown(manasses)).Should().Equal("manasseh");
    }
}
