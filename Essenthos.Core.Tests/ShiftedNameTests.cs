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
/// Names an aligner carried one place off. The Hebrew lists Bilhan, Zaavan and Akan, the Greek drops
/// Bilhan, and the aligner paired the Greek names with the Hebrew ones in order, so Ζαουὰν was given
/// Bilhan and Ἀκάν Zaavan. Each Greek word spells the name after the one it was paired with.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ShiftedNameTests : IDisposable
{
    private const string Hebrew = "the names the Hebrew marks, a test";

    private readonly AppDbContext _db;
    private readonly AnnotationCarrier _carrier;
    private readonly Text _hebrew;
    private readonly Text _greek;
    private readonly Entity _bilhan;
    private readonly Entity _zaavan;
    private readonly Entity _akan;
    private readonly Entity _bera;
    private readonly Entity _bela;

    public ShiftedNameTests(WitnessDatabase database)
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

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (36, 27, ["בִּלְהָן", "וְזַעֲוָן", "וַעֲקָן"]),
            (14, 2, ["בֶּרַע", "סְדֹם", "בֶּלַע"]));
        _greek = Corpus.Add(_db, "GRCBRENT", TextKind.CriticalEdition, "grc",
            (36, 27, ["Ζαουὰν", "καὶ", "Ἀκάν"]),
            (14, 2, ["Βαλλὰ", "Σοδόμων", "Βαλάκ"]));
        _db.SaveChanges();
        _db.Database.ExecuteSqlRaw("UPDATE word SET normalised_text = lower(text)");

        _bilhan = Person("bilhan", "Bilhan");
        _zaavan = Person("zaavan", "Zaavan");
        _akan = Person("akan", "Akan");
        _bera = Person("bera", "Bera");
        _bela = Person("zoar", "Zoar");
        var sodom = Person("sodom", "Sodom");

        Name(HebrewWord(36, 27, 1), _bilhan);
        Name(HebrewWord(36, 27, 2), _zaavan);
        Name(HebrewWord(36, 27, 3), _akan);
        Name(HebrewWord(14, 2, 1), _bera);
        Name(HebrewWord(14, 2, 2), sodom);
        Name(HebrewWord(14, 2, 3), _bela);
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

    [Fact]
    public async Task NamesTheAlignerMovedOnePlaceAreNotCarried()
    {
        Link(HebrewWord(36, 27, 1), GreekWord(36, 27, 1), LinkMethod.Aligner, 0.9);
        Link(HebrewWord(36, 27, 2), GreekWord(36, 27, 3), LinkMethod.Aligner, 0.9);

        await _carrier.Carry();

        NamedOn(GreekWord(36, 27, 1)).Should().BeEmpty("Ζαουὰν spells Zaavan, and Bilhan was carried onto it");
        NamedOn(GreekWord(36, 27, 3)).Should().BeEmpty("Ἀκάν spells Akan, and Zaavan was carried onto it");
    }

    [Fact]
    public async Task ANameSpelledLikeAnotherKeepsItWhereTheOtherHasARenderingOfItsOwn()
    {
        Link(HebrewWord(14, 2, 1), GreekWord(14, 2, 1), LinkMethod.Aligner, 0.9);
        Link(HebrewWord(14, 2, 2), GreekWord(14, 2, 2), LinkMethod.Aligner, 0.9);
        Link(HebrewWord(14, 2, 3), GreekWord(14, 2, 3), LinkMethod.Aligner, 0.9);

        await _carrier.Carry();

        NamedOn(GreekWord(14, 2, 1)).Should().Equal(["bera"], "Βαλάκ is Bela's rendering, so Βαλλὰ is not");
        NamedOn(GreekWord(14, 2, 3)).Should().Equal("zoar");
    }

    [Fact]
    public async Task ASecondCarryWritesAndTakesBackNothing()
    {
        Link(HebrewWord(36, 27, 1), GreekWord(36, 27, 1), LinkMethod.Aligner, 0.9);
        Link(HebrewWord(36, 27, 2), GreekWord(36, 27, 3), LinkMethod.Aligner, 0.9);
        Link(HebrewWord(14, 2, 1), GreekWord(14, 2, 1), LinkMethod.Aligner, 0.9);
        Link(HebrewWord(14, 2, 3), GreekWord(14, 2, 3), LinkMethod.Aligner, 0.9);

        await _carrier.Carry();
        var second = await _carrier.Carry();

        (second.Written, second.Withdrawn).Should().Be((0, 0));
        NamedOn(GreekWord(14, 2, 1)).Should().Equal("bera");
    }

    [Fact]
    public async Task AShiftedNameAlreadyCarriedIsTakenBackByTheNextCarry()
    {
        Link(HebrewWord(36, 27, 2), GreekWord(36, 27, 3), LinkMethod.Aligner, 0.9);
        _db.WordEntities.Add(new WordEntity
        {
            WordId = GreekWord(36, 27, 3).Id, EntityId = _zaavan.Id, Method = LinkMethod.StrongNumber,
            Confidence = 0.89, Source = Hebrew,
            Note = $"through {EntityCandidates.Witness} word {HebrewWord(36, 27, 2).Id}, linked by aligner",
        });
        await _db.SaveChangesAsync();

        var outcome = await _carrier.Carry();

        outcome.Withdrawn.Should().Be(1);
        NamedOn(GreekWord(36, 27, 3)).Should().BeEmpty();
    }

    [Fact]
    public async Task ALinkASourceStatesIsNotQuestioned()
    {
        Link(HebrewWord(36, 27, 2), GreekWord(36, 27, 3), LinkMethod.StatedBySource, null);

        await _carrier.Carry();

        NamedOn(GreekWord(36, 27, 3)).Should().Equal("zaavan");
    }

    [Fact]
    public async Task AWordWhoseLettersMatchNoNameOfTheVerseKeepsTheNameItWasGiven()
    {
        Link(HebrewWord(36, 27, 1), GreekWord(36, 27, 1), LinkMethod.Aligner, 0.9);
        _db.Database.ExecuteSqlRaw("UPDATE word SET text = 'Μαδοὶ', normalised_text = 'μαδοι' WHERE id = {0}",
            GreekWord(36, 27, 1).Id);

        await _carrier.Carry();

        NamedOn(GreekWord(36, 27, 1)).Should().Equal(["bilhan"], "a name the aligner placed is refused only where the letters say whose it is");
    }

    private Entity Person(string slug, string name)
    {
        var person = new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = name, SourceId = slug, Source = "a test",
        };
        _db.Entities.Add(person);
        _db.SaveChanges();
        return person;
    }

    private Word HebrewWord(int chapter, int verse, int position) => _db.WordAt(_hebrew, chapter, verse, position);

    private Word GreekWord(int chapter, int verse, int position) => _db.WordAt(_greek, chapter, verse, position);

    private void Name(Word word, Entity entity) =>
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id, EntityId = entity.Id, Method = LinkMethod.StrongNumber, Confidence = 0.99,
            Source = Hebrew, Note = "a proper noun",
            Claims = [new WordEntityClaim { Method = LinkMethod.StrongNumber, Confidence = 0.99, Source = Hebrew, Note = "a proper noun" }],
        });

    private Link Link(Word hebrew, Word greek, LinkMethod method, double? confidence)
    {
        var link = new Link
        {
            FromTextId = greek.TextId, ToTextId = hebrew.TextId, Relation = LinkRelation.Renders,
            Method = method, Confidence = confidence, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = greek, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = hebrew, Side = LinkSide.To });
        _db.SaveChanges();
        return link;
    }

    private List<string> NamedOn(Word word) =>
        [.. _db.WordEntities.AsNoTracking().Where(a => a.WordId == word.Id).Select(a => a.Entity!.Slug).OrderBy(s => s)];
}
