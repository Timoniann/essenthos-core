using System.Text.Json;
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
/// Which Magog a word of Genesis 10 means, where BHSA's marking belongs to the lexeme and the verse
/// says otherwise.
///
/// The marking is a property of the lemma, so H4031 is <c>topo</c> on every occurrence because
/// Ezekiel's Magog is a land — including in the table of the sons of Japheth, where it is a man. The
/// encyclopedia's own verse lists are the second witness, and each case here is one statement they
/// have to make, or must not be allowed to make, before a word marked a place is read as a person.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class GenealogyNameTests : IDisposable
{
    /// <summary>The eponym's number, which the dictionary heads for the man and for the land alike.</summary>
    private const string Eponym = "H4031";

    /// <summary>The town somebody is called the father of, which is a place and stays one.</summary>
    private const string Town = "H1049";

    /// <summary>The word Chronicles names a town by, standing before the name it founded.</summary>
    private const string Father = "H1";

    private const string BibleData =
        "BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0";

    private const string Ours = "Essenthos, from the words this corpus annotates to the person or the place they name";

    private readonly AppDbContext _db;
    private readonly EntityAnnotationLoader _loader;
    private readonly Text _hebrew;
    private readonly Entity _man;
    private readonly Entity _land;

    /// <summary>
    /// Genesis 1, one verse per case, every word marked a place by BHSA and standing in a phrase
    /// the clause is about. What varies is what the verse lists say and what stands before the word.
    /// </summary>
    public GenealogyNameTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _loader = new EntityAnnotationLoader(_db, NullLogger<EntityAnnotationLoader>.Instance);

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (1, 1, ["מגוג"]),
            (1, 2, ["מגוג"]),
            (1, 3, ["מגוג"]),
            (1, 4, ["אבי", "מגוג"]),
            (1, 5, ["מגוג"]),
            (1, 6, ["בית־צור"]));
        _db.SaveChanges();

        Place(1, Eponym, "PreC");
        Place(2, Eponym, "PreC");
        Place(3, Eponym, "PreC");
        Marked(_db.WordAt(_hebrew, 1, 4, 1), Father, null, "PreC");
        Place(4, Eponym, "PreC", position: 2);
        Place(5, Eponym, "Adju");
        Place(6, Town, "PreC");

        _man = Add("magog", "Magog", EntityKind.Person, Eponym);
        _land = Add("magog-2", "Magog", EntityKind.Place, Eponym);
        var bethzur = Add("bethzur", "Bethzur", EntityKind.Person, Town);
        var town = Add("bethzur-2", "Bethzur", EntityKind.Place, Town);

        // 1: the table of sons — the man is named here and the land is not.
        // 2: both are named, so the list says nothing about the word.
        // 3: only a list this corpus wrote itself names him, which is the corpus agreeing with itself.
        // 4: the man is named, and the word stands after "father of", which is how a town is named.
        // 5: the man is named, and the word is a circumstance of its clause rather than its subject.
        // 6: the man is named and so is the town, which the founder formula names together.
        Attest(_man, 1, BibleData);
        Attest(_man, 2, BibleData);
        Attest(_land, 2, BibleData);
        Attest(_man, 3, Ours);
        Attest(_man, 4, BibleData);
        Attest(_man, 5, BibleData);
        Attest(bethzur, 6, BibleData);
        Attest(town, 6, BibleData);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    private void Place(int verse, string number, string function, int position = 1) =>
        Marked(_db.WordAt(_hebrew, 1, verse, position), number, "topo", function);

    /// <summary>
    /// One word, with the number it carries, the kind BHSA marks it and the part its phrase plays
    /// in the clause — which is the third thing the rule reads and the only one held apart from the
    /// word.
    /// </summary>
    private void Marked(Word word, string number, string? nameType, string function)
    {
        word.StrongNumber = number;
        word.Morphology = nameType is null
            ? JsonDocument.Parse("""{"pos": "subs"}""")
            : JsonDocument.Parse($$"""{"pos": "subs", "nameType": "{{nameType}}"}""");

        var phrase = new WordGroup
        {
            TextId = word.TextId,
            Kind = WordGroupKind.Phrase,
            Position = word.Position,
            Features = JsonDocument.Parse($$"""{"function": "{{function}}"}"""),
        };
        _db.WordGroups.Add(phrase);
        _db.WordGroupWords.Add(new WordGroupWord { WordGroup = phrase, Word = word });
        _db.SaveChanges();
    }

    private Entity Add(string slug, string name, EntityKind kind, string number)
    {
        var entity = new Entity
        {
            Kind = kind, Slug = slug, Name = name, SourceId = slug, Source = "a test",
            Names = [new EntityName { Label = name, HebrewStrongNumber = number, Kind = "proper name" }],
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private void Attest(Entity entity, int verse, string source) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = entity, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = verse,
            Source = source,
        });

    private Word Hebrew(int verse, int position = 1) => _db.WordAt(_hebrew, 1, verse, position);

    private async Task<Dictionary<long, string>> Load()
    {
        await _loader.Load();
        return await _db.WordEntities.ToDictionaryAsync(a => a.WordId, a => a.Entity!.Slug);
    }

    /// <summary>
    /// The son of Japheth. The lists name the man in this verse and no place bearing the number, so
    /// they contradict the lexeme and the occurrence is the man.
    /// </summary>
    [Fact]
    public async Task AWordTheVerseNamesAPersonAtIsThatPerson()
    {
        var named = await Load();
        named.Should().ContainKey(Hebrew(1).Id).WhoseValue.Should().Be("magog");
    }

    /// <summary>
    /// The lists naming the land as well say only that both are in the verse, which is what the
    /// lexeme already said. Nothing contradicts the marking, so the place stands.
    /// </summary>
    [Fact]
    public async Task AVerseNamingThePlaceTooLeavesTheMarkingAlone()
    {
        var named = await Load();
        named.Should().ContainKey(Hebrew(2).Id).WhoseValue.Should().Be("magog-2");
    }

    /// <summary>
    /// A list this corpus read back off its own annotations is the corpus agreeing with itself, and
    /// overruling a witness on the strength of it would be circular.
    /// </summary>
    [Fact]
    public async Task AVerseListThisCorpusWroteDoesNotOverruleTheMarking()
    {
        var named = await Load();
        named.Should().ContainKey(Hebrew(3).Id).WhoseValue.Should().Be("magog-2");
    }

    /// <summary>
    /// <em>Raham the father of Jorkeam</em>, <em>Maon the father of Bethzur</em>: in Chronicles the
    /// word after H1 is the town its founder is named the father of, and the encyclopedia names a
    /// man of that name in the verse because the founder bears it too.
    /// </summary>
    [Fact]
    public async Task ATownSomebodyIsCalledTheFatherOfStaysAPlace()
    {
        var named = await Load();
        named.Should().ContainKey(Hebrew(4, 2).Id).WhoseValue.Should().Be("magog-2");
    }

    /// <summary>
    /// <em>the field of Aram</em> that Jacob fled to, <em>with Argob and with Arieh</em>. A name in
    /// an adjunct is where the clause went or whom it went with, not the thing the clause is about.
    /// </summary>
    [Fact]
    public async Task ANameThatIsACircumstanceOfItsClauseStaysAPlace()
    {
        var named = await Load();
        named.Should().ContainKey(Hebrew(5).Id).WhoseValue.Should().Be("magog-2");
    }

    /// <summary>
    /// The founder formula names both, and the place being listed is what keeps it a place even
    /// where nothing stands immediately before the word.
    /// </summary>
    [Fact]
    public async Task AVerseNamingBothTheFounderAndHisTownKeepsTheTown()
    {
        var named = await Load();
        named.Should().ContainKey(Hebrew(6).Id).WhoseValue.Should().Be("bethzur-2");
    }

    /// <summary>
    /// The place written before the rule existed is taken back, not left beside the person. A word
    /// that says it is a land and a man at once is worse than either answer.
    /// </summary>
    [Fact]
    public async Task ThePlaceAlreadyWrittenIsWithdrawn()
    {
        _db.WordEntities.Add(new WordEntity
        {
            WordId = Hebrew(1).Id,
            EntityId = _land.Id,
            Method = LinkMethod.Lexical,
            Confidence = 0.9,
            Source = EntityAnnotationLoader.Written[0],
            Note = "written before the verse was asked",
        });
        await _db.SaveChangesAsync();

        var named = await Load();

        named.Should().ContainKey(Hebrew(1).Id).WhoseValue.Should().Be("magog");
        var son = Hebrew(1).Id;
        (await _db.WordEntities.CountAsync(a => a.WordId == son)).Should().Be(1);
        (await _db.WordEntities.CountAsync(a => a.EntityId == _land.Id && a.WordId == son))
            .Should().Be(0);
    }

    [Fact]
    public async Task RunningAgainWritesNothingFurther()
    {
        await _loader.Load();
        var first = await _db.WordEntities.CountAsync();

        await _loader.Load();

        (await _db.WordEntities.CountAsync()).Should().Be(first);
        (await _db.WordEntities.CountAsync(a => a.EntityId == _man.Id)).Should().Be(1);
    }
}
