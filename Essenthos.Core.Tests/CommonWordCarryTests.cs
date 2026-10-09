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
/// A name a translation's own alignment carries back onto a Greek or Hebrew word whose Strong number
/// is a common word. The Hindi interlinear states <em>अब्राहम</em> opposite the βίβλῳ of Mark 12:26,
/// and the verses' consensus had named Abraham on the Hindi word, so Nestle's <em>book</em> named
/// Abraham at 0.99. The lexicon says what βίβλος is; a person or a place is not carried onto it,
/// while a name, a title the record bears, and a feast's own common noun still are.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CommonWordCarryTests : IDisposable
{
    private const string Reading = "Essenthos, read from the verses that name it";

    private readonly AppDbContext _db;
    private readonly AnnotationCarrier _carrier;
    private readonly Text _hindi;
    private readonly Text _greek;
    private readonly Text _hebrew;
    private readonly Text _scrivener;
    private readonly Entity _abraham;
    private readonly Entity _jesus;
    private readonly Entity _egypt;
    private readonly Entity _pharaoh;
    private readonly Entity _passover;

    public CommonWordCarryTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _carrier = new AnnotationCarrier(
            _db,
            new CrossedNameLoader(_db, NullLogger<CrossedNameLoader>.Instance),
            new ForeignNames(_db, NullLogger<ForeignNames>.Instance),
            new EqualTwinNames(_db, NullLogger<EqualTwinNames>.Instance),
            new PronounReferents(_db, NullLogger<PronounReferents>.Instance),
            NullLogger<AnnotationCarrier>.Instance);

        _hindi = Corpus.Add(_db, "IRV2019", TextKind.Translation, "hin",
            (12, 26, ["अब्राहम", "यीशु", "प्रभु", "मिस्र", "फ़िरौन", "फसह"]));
        _greek = Corpus.Add(_db, "NESTLE1904", TextKind.CriticalEdition, "grc",
            (12, 26, ["βίβλῳ", "Ἀβραάμ", "κύριος", "πάσχα"]));
        _scrivener = Corpus.Add(_db, "TR1894", TextKind.CriticalEdition, "grc",
            (12, 26, ["βιβλω", "Αβρααμ"]));
        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (12, 26, ["אֶרֶץ", "פַּרְעֹה"]));
        _db.SaveChanges();
        Number(_greek, "G976", "G11", "G2962", "G3957");
        Number(_scrivener, "G976", "G11");
        Number(_hebrew, "H776", "H6547");

        _db.StrongEntries.AddRange(
            new StrongEntry { StrongNumber = "G976", Lemma = "βίβλος" },
            new StrongEntry { StrongNumber = "G11", Lemma = "Ἀβραάμ" },
            new StrongEntry { StrongNumber = "G2962", Lemma = "κύριος" },
            new StrongEntry { StrongNumber = "G3957", Lemma = "πάσχα" },
            new StrongEntry { StrongNumber = "H776", Lemma = "אֶרֶץ", Morphology = "n-f" },
            new StrongEntry { StrongNumber = "H6547", Lemma = "פַּרְעֹה", Morphology = "n-m" });

        _abraham = Record(EntityKind.Person, "abram", new EntityName { Label = "Abraham", GreekStrongNumber = "G11", Kind = "proper name" });
        _jesus = Record(EntityKind.Person, "jesus", new EntityName { Label = "Lord", GreekStrongNumber = "G2962", Kind = "title" });
        _egypt = Record(EntityKind.Place, "egypt", new EntityName { Label = "Egypt", HebrewStrongNumber = "H4714", Kind = "name" });
        _pharaoh = Record(EntityKind.Person, "pharaoh-4");
        _passover = Record(EntityKind.Observance, "passover", new EntityName { Label = "Passover", HebrewStrongNumber = "H6453", Kind = "name" });
        var title = Record(EntityKind.Title, "pharaoh-title", new EntityName { Label = "Pharaoh", HebrewStrongNumber = "H6547", Kind = "title" });
        _db.TitleBearers.Add(new TitleBearer
        {
            TitleEntityId = title.Id, BearerEntityId = _pharaoh.Id,
            CanonicalBook = 1, CanonicalChapter = 12, CanonicalVerse = 26, Source = "a test",
        });

        Seed(Hindi(1), _abraham);
        Seed(Hindi(2), _jesus);
        Seed(Hindi(3), _jesus);
        Seed(Hindi(4), _egypt);
        Seed(Hindi(5), _pharaoh);
        Seed(Hindi(6), _passover);
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

    private void Number(Text text, params string[] numbers)
    {
        for (var position = 1; position <= numbers.Length; position++)
        {
            _db.WordAt(text, 12, 26, position).StrongNumber = numbers[position - 1];
        }

        _db.SaveChanges();
    }

    private Entity Record(EntityKind kind, string slug, params EntityName[] names)
    {
        var entity = new Entity { Kind = kind, Slug = slug, Name = slug, SourceId = slug, Source = "a test", Names = [.. names] };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private void Seed(Word word, Entity entity) =>
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id, EntityId = entity.Id, Method = LinkMethod.RuleBased, Confidence = 0.99, Source = Reading,
            Claims = [new WordEntityClaim { Method = LinkMethod.RuleBased, Confidence = 0.99, Source = Reading }],
        });

    private Word Hindi(int position) => _db.WordAt(_hindi, 12, 26, position);

    private Word Greek(int position) => _db.WordAt(_greek, 12, 26, position);

    private Word Hebrew(int position) => _db.WordAt(_hebrew, 12, 26, position);

    private Word Scrivener(int position) => _db.WordAt(_scrivener, 12, 26, position);

    /// <summary>A link the interlinear states between one Hindi word and one word of the original.</summary>
    private void Stated(Word hindi, Word original)
    {
        var link = new Link
        {
            FromTextId = hindi.TextId, ToTextId = original.TextId, Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource, Confidence = null, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = hindi, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = original, Side = LinkSide.To });
        _db.SaveChanges();
    }

    private void Equal(Word word, Word twin)
    {
        var link = new Link
        {
            FromTextId = twin.TextId, ToTextId = word.TextId, Relation = LinkRelation.Equals,
            Method = LinkMethod.Lexical, Confidence = 0.95, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = twin, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = word, Side = LinkSide.To });
        _db.SaveChanges();
    }

    private async Task<int?> NamedOn(Word word) =>
        await _db.WordEntities.AsNoTracking()
            .Where(a => a.WordId == word.Id)
            .Select(a => (int?)a.EntityId)
            .SingleOrDefaultAsync();

    [Fact]
    public async Task APersonIsNotCarriedOntoAGreekCommonWord()
    {
        Stated(Hindi(1), Greek(1));

        await _carrier.Carry();

        (await NamedOn(Greek(1))).Should().BeNull();
    }

    [Fact]
    public async Task APersonIsCarriedOntoItsOwnName()
    {
        Stated(Hindi(1), Greek(2));

        await _carrier.Carry();

        (await NamedOn(Greek(2))).Should().Be(_abraham.Id);
    }

    /// <summary>
    /// A row carried there before the guard is taken back by the next carry, and the carry after it
    /// finds nothing to do.
    /// </summary>
    [Fact]
    public async Task ANameAlreadyCarriedOntoACommonWordIsWithdrawn()
    {
        Stated(Hindi(1), Greek(1));
        _db.WordEntities.Add(new WordEntity
        {
            WordId = Greek(1).Id, EntityId = _abraham.Id, Method = LinkMethod.RuleBased, Confidence = 0.99,
            Source = Reading, Note = $"through IRV2019 word {Hindi(1).Id}, linked by stated-by-source",
        });
        await _db.SaveChangesAsync();

        var first = await _carrier.Carry();
        var second = await _carrier.Carry();

        (await NamedOn(Greek(1))).Should().BeNull();
        first.Withdrawn.Should().Be(1);
        second.Withdrawn.Should().Be(0);
        second.Written.Should().Be(0);
    }

    /// <summary>κύριος is a common noun, and one of the titles the encyclopedia files under Jesus.</summary>
    [Fact]
    public async Task APersonIsCarriedOntoACommonWordThatIsOneOfItsOwnTitles()
    {
        Stated(Hindi(3), Greek(3));

        await _carrier.Carry();

        (await NamedOn(Greek(3))).Should().Be(_jesus.Id);
    }

    /// <summary>The same word is nobody else's: the alignment that puts Abraham opposite it is wrong.</summary>
    [Fact]
    public async Task AnotherPersonIsNotCarriedOntoThatTitle()
    {
        Stated(Hindi(1), Greek(3));

        await _carrier.Carry();

        (await NamedOn(Greek(3))).Should().BeNull();
    }

    /// <summary>פַּרְעֹה is a common noun in Strong's, and the title the king of Egypt bears.</summary>
    [Fact]
    public async Task ABearerIsCarriedOntoTheTitleItBears()
    {
        Stated(Hindi(5), Hebrew(2));

        await _carrier.Carry();

        (await NamedOn(Hebrew(2))).Should().Be(_pharaoh.Id);
    }

    [Fact]
    public async Task APlaceIsNotCarriedOntoAHebrewCommonWord()
    {
        Stated(Hindi(4), Hebrew(1));

        await _carrier.Carry();

        (await NamedOn(Hebrew(1))).Should().BeNull();
    }

    /// <summary>A feast's own word is a common noun: πάσχα is the Passover, and keeps it.</summary>
    [Fact]
    public async Task AFeastIsStillCarriedOntoItsCommonNoun()
    {
        Stated(Hindi(6), Greek(4));

        await _carrier.Carry();

        (await NamedOn(Greek(4))).Should().Be(_passover.Id);
    }

    /// <summary>
    /// The twin in another edition is not given the person either, whatever named the word it is
    /// twinned with; its name is.
    /// </summary>
    [Fact]
    public async Task TheTwinOfACommonWordIsNotGivenThePerson()
    {
        Equal(Greek(1), Scrivener(1));
        Equal(Greek(2), Scrivener(2));
        _db.WordEntities.AddRange(
            new WordEntity { WordId = Greek(1).Id, EntityId = _abraham.Id, Method = LinkMethod.ModelReading, Confidence = 0.9, Source = "a reading of the verse" },
            new WordEntity { WordId = Greek(2).Id, EntityId = _abraham.Id, Method = LinkMethod.ModelReading, Confidence = 0.9, Source = "a reading of the verse" });
        await _db.SaveChangesAsync();

        await new EqualTwinNames(_db, NullLogger<EqualTwinNames>.Instance).Load();

        (await NamedOn(Scrivener(1))).Should().BeNull();
        (await NamedOn(Scrivener(2))).Should().Be(_abraham.Id);
    }
}
