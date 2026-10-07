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
/// A person's name carried onto a word the text writes as somebody else's name.
///
/// Ohienko's Romans 16:20 as it stood: <em>сатану</em> is Satan, <em>Христа</em> is Christ, and an
/// aligner put <em>Христа</em> against <em>Σατανᾶν</em>, so the lexicon's Satan was carried onto it.
/// The Ukrainian writes <em>Христа</em> for Jesus in the verses beside it, and the Greek of the verse
/// names Jesus, so the word is Jesus's name and not Satan's.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ForeignNameTests : IDisposable
{
    private const string Lexicon = "the lexicon's capitalised lemma, a test";

    private readonly AppDbContext _db;
    private readonly ForeignNames _foreign;
    private readonly AnnotationCarrier _carrier;
    private readonly Text _greek;
    private readonly Text _ukrainian;
    private readonly Entity _satan;
    private readonly Entity _jesus;
    private readonly Entity _anointed;

    public ForeignNameTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _foreign = new ForeignNames(_db, NullLogger<ForeignNames>.Instance);
        _carrier = new AnnotationCarrier(
            _db, new CrossedNameLoader(_db, NullLogger<CrossedNameLoader>.Instance), _foreign,
            new EqualTwinNames(_db, NullLogger<EqualTwinNames>.Instance), new PronounReferents(_db, NullLogger<PronounReferents>.Instance), NullLogger<AnnotationCarrier>.Instance);

        _greek = Corpus.Add(_db, "NESTLE1904", TextKind.CriticalEdition, "grc",
            (16, 20, ["Σατανᾶν", "Ἰησοῦ", "Χριστοῦ"]),
            (16, 21, ["Χριστοῦ"]),
            (16, 22, ["Χριστοῦ"]),
            (16, 23, ["Χριστοῦ"]));
        _ukrainian = Corpus.Add(_db, "UBIO", TextKind.Translation, "ukr",
            (16, 20, ["Бог", "сатану", "Ісуса", "Христа"]),
            (16, 21, ["у", "Христа"]),
            (16, 22, ["у", "Христа"]),
            (16, 23, ["у", "Христа"]));
        _db.SaveChanges();
        _db.Database.ExecuteSqlRaw("UPDATE word SET normalised_text = lower(text)");
        _db.Database.ExecuteSqlRaw(
            "UPDATE word SET strong_number = CASE text WHEN 'Σατανᾶν' THEN 'G4567' WHEN 'Ἰησοῦ' THEN 'G2424' " +
            "WHEN 'Χριστοῦ' THEN 'G5547' END WHERE text_id = {0}", _greek.Id);

        _satan = Person("satan", "Satan", "Σατανᾶς", "G4567", "сатана");
        _jesus = Person("jesus", "Jesus", "Ἰησοῦς", "G2424", "Ісус");
        _anointed = new Entity
        {
            Kind = EntityKind.Title, Slug = "anointed", Name = "Anointed", SourceId = "anointed", Source = "a test",
            Names = [new EntityName { Label = "Christ", Greek = "Χριστός", GreekStrongNumber = "G5547", Kind = "title" }],
        };
        _db.Entities.Add(_anointed);
        _db.SaveChanges();
        _db.TitleBearers.Add(new TitleBearer
        {
            TitleEntityId = _anointed.Id, BearerEntityId = _jesus.Id,
            CanonicalBook = 1, CanonicalChapter = 16, CanonicalVerse = 20, Source = "a test",
        });

        Name(Greek(16, 20, 1), _satan, LinkMethod.StrongNumber, 0.97, "G4567");
        Name(Greek(16, 20, 2), _jesus, LinkMethod.Lexical, 0.94, "G2424");
        foreach (var verse in new[] { 21, 22, 23 })
        {
            Name(Ukrainian(verse, 2), _jesus, LinkMethod.RuleBased, 0.97, "the title the text fixes");
        }

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

    private Entity Person(string slug, string label, string greek, string number, string ukrainian)
    {
        var person = new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = label, SourceId = slug, Source = "a test",
            Names = [new EntityName { Label = label, Greek = greek, GreekStrongNumber = number, Kind = "proper name" }],
        };
        _db.Entities.Add(person);
        _db.SaveChanges();
        _db.EntityNameForms.Add(new EntityNameForm
        {
            EntityId = person.Id, Language = "ukr", GrammaticalCase = GrammaticalCases.Nominative, Form = ukrainian,
            Method = LinkMethod.Manual, Source = "a test",
        });
        _db.SaveChanges();
        return person;
    }

    private Word Greek(int chapter, int verse, int position) => _db.WordAt(_greek, chapter, verse, position);

    private Word Ukrainian(int verse, int position) => _db.WordAt(_ukrainian, 16, verse, position);

    private WordEntity Name(Word word, Entity entity, LinkMethod method, double? confidence, string note)
    {
        var annotation = new WordEntity
        {
            WordId = word.Id, EntityId = entity.Id, Method = method, Confidence = confidence, Source = Lexicon,
            Note = note,
            Claims = [new WordEntityClaim { Method = method, Confidence = confidence, Source = Lexicon, Note = note }],
        };
        _db.WordEntities.Add(annotation);
        return annotation;
    }

    private void Link(Word witness, Word rendering, double confidence)
    {
        var link = new Link
        {
            FromTextId = rendering.TextId, ToTextId = witness.TextId, Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner, Confidence = confidence, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = rendering, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = witness, Side = LinkSide.To });
        _db.SaveChanges();
    }

    private List<string> NamedOn(Word word) =>
        [.. _db.WordEntities.AsNoTracking().Where(a => a.WordId == word.Id).Select(a => a.Entity!.Slug).OrderBy(s => s)];

    [Fact]
    public async Task APersonsNameIsNotCarriedOntoAWordTheTextWritesAsAnothersName()
    {
        Link(Greek(16, 20, 1), Ukrainian(20, 4), 0.95);

        await _carrier.Carry();

        NamedOn(Ukrainian(20, 4)).Should().BeEmpty("Христа is how this text writes Jesus, whom the verse names");
    }

    [Fact]
    public async Task ANameCarriedOntoAnothersNameIsTakenBackOnceAndOnlyOnce()
    {
        var satan = Greek(16, 20, 1);
        Name(Ukrainian(20, 4), _satan, LinkMethod.StrongNumber, 0.89,
            $"through NESTLE1904 word {satan.Id}, linked by aligner");
        Name(Ukrainian(20, 2), _satan, LinkMethod.StrongNumber, 0.94,
            $"through NESTLE1904 word {satan.Id}, linked by aligner");
        await _db.SaveChangesAsync();

        var first = await _foreign.Withdraw();
        var second = await _foreign.Withdraw();

        first.Withdrawn.Should().Be(1);
        second.Withdrawn.Should().Be(0);
        NamedOn(Ukrainian(20, 4)).Should().BeEmpty();
        NamedOn(Ukrainian(20, 2)).Should().Equal("satan");
    }

    [Fact]
    public async Task ARulingOnTheWordItselfIsNotTakenBackBySpelling()
    {
        var satan = Greek(16, 20, 1);
        var carried = Name(Ukrainian(20, 4), _satan, LinkMethod.StrongNumber, 0.89,
            $"through NESTLE1904 word {satan.Id}, linked by aligner");
        carried.Claims.Add(new WordEntityClaim
        {
            Method = LinkMethod.Manual, Confidence = null, Source = "a person's ruling", Note = "read here",
        });
        await _db.SaveChangesAsync();

        (await _foreign.Withdraw()).Withdrawn.Should().Be(0);
        NamedOn(Ukrainian(20, 4)).Should().Contain("satan");
    }

    [Fact]
    public async Task ASpellingTheTextUsesForThePersonWhereTheOtherIsUnnamedIsTheirs()
    {
        // The text writes Satan Христа in two verses whose originals name no Jesus: then Христа is a
        // second spelling of Satan in this text, however often it is Jesus's.
        var more = Corpus.AddBook(_db, _ukrainian, 46, "Corinthians", (5, 5, ["і", "Христа"]), (5, 6, ["і", "Христа"]));
        _db.SaveChanges();
        _db.Database.ExecuteSqlRaw("UPDATE word SET normalised_text = lower(text) WHERE normalised_text IS NULL");
        foreach (var verse in new[] { 5, 6 })
        {
            var word = _db.Words.Single(w => w.TextId == more.Id && w.Verse!.Book!.CanonicalOrdinal == 46
                                              && w.Verse.Number == verse && w.Position == 2);
            Name(word, _satan, LinkMethod.Manual, null, "a ruling");
        }

        var jesus = Corpus.AddBook(_db, _ukrainian, 43, "John",
            [.. Enumerable.Range(1, 6).Select(verse => (1, verse, new[] { "у", "Христа" }))]);
        _db.SaveChanges();
        _db.Database.ExecuteSqlRaw("UPDATE word SET normalised_text = lower(text) WHERE normalised_text IS NULL");
        foreach (var word in _db.Words.Where(w => w.TextId == jesus.Id && w.Verse!.Book!.CanonicalOrdinal == 43
                                                  && w.Position == 2).ToList())
        {
            Name(word, _jesus, LinkMethod.RuleBased, 0.97, "the title the text fixes");
        }

        var satan = Greek(16, 20, 1);
        Name(Ukrainian(20, 4), _satan, LinkMethod.StrongNumber, 0.89,
            $"through NESTLE1904 word {satan.Id}, linked by aligner");
        await _db.SaveChangesAsync();

        (await _foreign.Withdraw()).Withdrawn.Should().Be(0);
    }

    [Fact]
    public async Task AReadingOfTheWordItselfOutranksACarriedNameWhateverItsMethod()
    {
        var word = Ukrainian(20, 4);
        Name(word, _satan, LinkMethod.StrongNumber, 0.89, $"through NESTLE1904 word {Greek(16, 20, 1).Id}, linked by aligner");
        Name(word, _jesus, LinkMethod.ModelReading, 0.8, "a reading of the verse");
        await _db.SaveChangesAsync();

        var shown = await Annotations.AllOf(_db, word.Id, CancellationToken.None);

        shown.Select(entity => entity.Slug).Should().Equal("jesus");
    }

    [Fact]
    public async Task TheVersesConsensusDoesNotOutrankACarriedName()
    {
        var word = Ukrainian(20, 4);
        Name(word, _jesus, LinkMethod.StrongNumber, 0.89, $"through NESTLE1904 word {Greek(16, 20, 2).Id}, linked by aligner");
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id, EntityId = _satan.Id, Method = LinkMethod.RuleBased, Confidence = 0.99,
            Source = Annotations.Consensus, Note = "the word its verses share in this text",
        });
        await _db.SaveChangesAsync();

        var shown = await Annotations.AllOf(_db, word.Id, CancellationToken.None);

        shown.Select(entity => entity.Slug).Should().Equal("jesus");
    }

    [Fact]
    public async Task TheVerseListsSettleAWordAsTheReaderIsShownIt()
    {
        var word = Ukrainian(20, 4);
        Name(word, _satan, LinkMethod.StrongNumber, 0.89, $"through NESTLE1904 word {Greek(16, 20, 1).Id}, linked by aligner");
        Name(word, _jesus, LinkMethod.ModelReading, 0.8, "a reading of the verse");
        await _db.SaveChangesAsync();

        var settled = await _db.Database
            .SqlQueryRaw<int>($"WITH {Annotating.Settled} SELECT entity_id AS \"Value\" FROM settled WHERE word_id = {word.Id}")
            .ToListAsync();

        settled.Should().Equal(_jesus.Id);
    }
}
