using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests.Names;

/// <summary>
/// A word that names two records at once: a title and the man who bears it, a person and a people.
/// Both are shown, the first answer first, and both pages get the verse; two answers to one
/// question are still one answer or none.
///
/// <para>
/// Asked of Postgres because the rule is written twice, once for the word a reader hovers and once
/// as the statement the verse lists are read through, and what is under test is that the two agree.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class WordNamesSeveralTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Text _greek;

    public WordNamesSeveralTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _greek = Corpus.Add(_db, NestleTextSource.Slug, TextKind.CriticalEdition, "grc",
            (1, 1, ["Ἰησοῦ", "Χριστοῦ"]),
            (1, 2, ["Χριστός"]),
            (1, 3, ["Σουναμῖτις"]),
            (1, 4, ["Βαράκ"]));
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

    private Word Greek(int verse, int position) => _db.WordAt(_greek, 1, verse, position);

    private Entity Add(string slug, EntityKind kind)
    {
        var entity = new Entity { Kind = kind, Slug = slug, Name = slug };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private void Annotate(
        Word word, Entity entity, LinkMethod method, double? confidence, string? role = null, string? origin = null, string? note = null,
        string source = "a test")
    {
        _db.WordEntities.Add(new WordEntity
        {
            Word = word, Entity = entity, Method = method, Confidence = confidence, Source = source,
            Role = role, Origin = origin, Note = note,
        });
        _db.SaveChanges();
    }

    private void Bears(Entity bearer, Entity title)
    {
        _db.TitleBearers.Add(new TitleBearer
        {
            Title = title, Bearer = bearer, CanonicalBook = 40, CanonicalChapter = 1, CanonicalVerse = 16, Source = "a test",
        });
        _db.SaveChanges();
    }

    private async Task<List<string>> Shown(Word word) =>
        [.. (await Annotations.AllOf(_db, word.Id, CancellationToken.None)).Select(named => named.Slug)];

    /// <summary>
    /// Christ in Jesus Christ: the title and its bearer at the same standing and the same confidence.
    /// The word names both, the man first.
    /// </summary>
    [Fact]
    public async Task ATitleAndItsBearerAreBothNamedTheBearerFirst()
    {
        var jesus = Add("jesus", EntityKind.Person);
        var anointed = Add("anointed", EntityKind.Title);
        Bears(jesus, anointed);
        Annotate(Greek(1, 2), anointed, LinkMethod.RuleBased, 0.99);
        Annotate(Greek(1, 2), jesus, LinkMethod.RuleBased, 0.99);

        (await Shown(Greek(1, 2))).Should().Equal("jesus", "anointed");
        (await Annotations.Of(_db, Greek(1, 2).Id, CancellationToken.None))!.Slug.Should().Be("jesus");
    }

    /// <summary>The bearer stands first whichever of the two outranks the other, and the title beside him.</summary>
    [Fact]
    public async Task TheTitleStandsBesideABearerARulingNamed()
    {
        var cyrus = Add("cyrus", EntityKind.Person);
        var anointed = Add("anointed", EntityKind.Title);
        Bears(cyrus, anointed);
        Annotate(Greek(2, 1), anointed, LinkMethod.RuleBased, 0.99);
        Annotate(Greek(2, 1), cyrus, LinkMethod.Manual, null);

        (await Shown(Greek(2, 1))).Should().Equal("cyrus", "anointed");
    }

    /// <summary>
    /// The links carried the title onto a word more firmly than the man. The man is still the first
    /// answer, and the verse is still his: the title says what the word is, the bearer whom it names.
    /// </summary>
    [Fact]
    public async Task TheBearerStandsFirstEvenWhereTheTitleOutranksHim()
    {
        var jesus = Add("jesus", EntityKind.Person);
        var anointed = Add("anointed", EntityKind.Title);
        Bears(jesus, anointed);
        Annotate(Greek(2, 1), anointed, LinkMethod.RuleBased, 0.99);
        Annotate(Greek(2, 1), jesus, LinkMethod.RuleBased, 0.72);

        (await Shown(Greek(2, 1))).Should().Equal("jesus", "anointed");
    }

    /// <summary>A title alone is the whole answer: John 1:20 names the title and nobody.</summary>
    [Fact]
    public async Task ATitleAloneIsTheOnlyAnswer()
    {
        var anointed = Add("anointed", EntityKind.Title);
        Annotate(Greek(2, 1), anointed, LinkMethod.RuleBased, 0.99);

        (await Shown(Greek(2, 1))).Should().Equal("anointed");
    }

    /// <summary>
    /// A title beside somebody who does not bear it is two answers to one question: at equal
    /// standing neither is shown, and where one outranks the other it alone is.
    /// </summary>
    [Fact]
    public async Task ATitleAndSomebodyWhoDoesNotBearItAreRivals()
    {
        var abilene = Add("abilene", EntityKind.Place);
        var tetrarch = Add("tetrarch", EntityKind.Title);
        Annotate(Greek(2, 1), tetrarch, LinkMethod.RuleBased, 0.99);
        Annotate(Greek(2, 1), abilene, LinkMethod.RuleBased, 0.99);
        Annotate(Greek(4, 1), tetrarch, LinkMethod.RuleBased, 0.99);
        Annotate(Greek(4, 1), abilene, LinkMethod.StrongNumber, 0.9);

        (await Shown(Greek(2, 1))).Should().BeEmpty();
        (await Shown(Greek(4, 1))).Should().Equal("abilene");
    }

    /// <summary>The Shunammite: Abishag and her people, the higher standing first.</summary>
    [Fact]
    public async Task APersonAndAPeopleAreBothNamed()
    {
        var abishag = Add("abishag", EntityKind.Person);
        var shunammites = Add("shunamites", EntityKind.People);
        Annotate(Greek(3, 1), shunammites, LinkMethod.Lexical, 0.9);
        Annotate(Greek(3, 1), abishag, LinkMethod.RuleBased, 0.95);

        (await Shown(Greek(3, 1))).Should().Equal("abishag", "shunamites");
    }

    /// <summary>Two men on one word are one answer, the one of higher standing, as before.</summary>
    [Fact]
    public async Task TwoMenAreStillOneAnswer()
    {
        var barak = Add("barak", EntityKind.Person);
        var sisera = Add("sisera", EntityKind.Person);
        Annotate(Greek(4, 1), barak, LinkMethod.StrongNumber, 0.9);
        Annotate(Greek(4, 1), sisera, LinkMethod.ModelReading, 0.9);

        (await Shown(Greek(4, 1))).Should().Equal("barak");
    }

    /// <summary>A man and a town on one word are one answer too: the reading that outranks the other.</summary>
    [Fact]
    public async Task AManAndATownAreStillOneAnswer()
    {
        var seir = Add("seir", EntityKind.Person);
        var mountSeir = Add("mountseir", EntityKind.Place);
        Annotate(Greek(4, 1), seir, LinkMethod.Lexical, 0.9);
        Annotate(Greek(4, 1), mountSeir, LinkMethod.ModelReading, 0.9);

        (await Shown(Greek(4, 1))).Should().Equal("mountseir");
    }

    /// <summary>
    /// Matthew 1:11 where a translation names Eliakim and its Greek base Jechoniah: the translation's record is the first
    /// card and the base's the second, saying which text names it; the base's reading is never the word's own answer.
    /// </summary>
    [Fact]
    public async Task AWordThatDiffersFromItsBaseShowsBothCardsTheTranslationsFirst()
    {
        var eliakim = Add("eliakim", EntityKind.Person);
        var jehoiachin = Add("jehoiachin", EntityKind.Person);
        Annotate(Greek(4, 1), eliakim, LinkMethod.Manual, null, WordEntityRoles.Name, WordEntityOrigins.Differs);
        Annotate(Greek(4, 1), jehoiachin, LinkMethod.Aligner, 0.9, WordEntityRoles.Name, WordEntityOrigins.BaseReading,
            "through TR1894 word 77");

        var cards = await Annotations.AllOf(_db, Greek(4, 1).Id, CancellationToken.None);

        cards.Select(c => (c.Slug, c.Origin, c.BaseText)).Should().Equal(
            ("eliakim", WordEntityOrigins.Differs, null),
            ("jehoiachin", WordEntityOrigins.BaseReading, "TR1894"));
        cards[0].Role.Should().Be(WordEntityRoles.Name);
        (await Annotations.Of(_db, Greek(4, 1).Id, CancellationToken.None))!.Slug.Should().Be("eliakim");
        (await _db.WordEntities.CountAsync(a => a.WordId == Greek(4, 1).Id)).Should().Be(1, "a base reading is asked for, never read by default");
    }

    /// <summary>A base reading on a word whose own name is not a differs is not shown: it is a second card or nothing.</summary>
    [Fact]
    public async Task ABaseReadingWithoutItsDiffersIsNotShown()
    {
        var barak = Add("barak", EntityKind.Person);
        var sisera = Add("sisera", EntityKind.Person);
        Annotate(Greek(4, 1), barak, LinkMethod.StrongNumber, 0.9, WordEntityRoles.Name, WordEntityOrigins.Projected);
        Annotate(Greek(4, 1), sisera, LinkMethod.StrongNumber, 0.9, WordEntityRoles.Name, WordEntityOrigins.BaseReading);

        (await Shown(Greek(4, 1))).Should().Equal("barak");
    }

    /// <summary>
    /// One word standing for two records in two roles names both, whatever their kinds: a man and the title the corpus
    /// joins to nobody, each answering its own question.
    /// </summary>
    [Fact]
    public async Task RecordsInDifferentRolesStandBesideEachOther()
    {
        var herod = Add("herod", EntityKind.Person);
        var tetrarch = Add("tetrarch", EntityKind.Title);
        Annotate(Greek(2, 1), tetrarch, LinkMethod.RuleBased, 0.99, WordEntityRoles.Title, WordEntityOrigins.Settled);
        Annotate(Greek(2, 1), herod, LinkMethod.RuleBased, 0.99, WordEntityRoles.Name, WordEntityOrigins.Settled);

        var cards = await Annotations.AllOf(_db, Greek(2, 1).Id, CancellationToken.None);

        cards.Select(c => (c.Slug, c.Role)).Should().Equal(("herod", WordEntityRoles.Name), ("tetrarch", WordEntityRoles.Title));
    }

    /// <summary>Two records in one role are still rivals: two men named on one word are one answer.</summary>
    [Fact]
    public async Task RecordsInOneRoleAreStillRivals()
    {
        var barak = Add("barak", EntityKind.Person);
        var sisera = Add("sisera", EntityKind.Person);
        Annotate(Greek(4, 1), barak, LinkMethod.StrongNumber, 0.9, WordEntityRoles.Name, WordEntityOrigins.Settled);
        Annotate(Greek(4, 1), sisera, LinkMethod.StrongNumber, 0.9, WordEntityRoles.Name, WordEntityOrigins.Settled);

        (await Shown(Greek(4, 1))).Should().BeEmpty();
    }

    private const string Decision = "the project owner's decision of 2026-11-05";

    /// <summary>
    /// The pillars of 1 Kings 7:21, which one decision names as Jachin and Boaz: both are shown, read of the word itself
    /// and carried through it alike, and the verse lists read both.
    /// </summary>
    [Fact]
    public async Task ThingsOneRulingNamesTogetherAreAllShown()
    {
        var jachin = Add("jachin-pillar", EntityKind.Object);
        var boaz = Add("boaz-pillar", EntityKind.Object);
        Annotate(Greek(4, 1), jachin, LinkMethod.Manual, null, WordEntityRoles.Thing, WordEntityOrigins.Settled, source: Decision);
        Annotate(Greek(4, 1), boaz, LinkMethod.Manual, null, WordEntityRoles.Thing, WordEntityOrigins.Settled, source: Decision);
        Annotate(Greek(3, 1), jachin, LinkMethod.Manual, 0.98, WordEntityRoles.Thing, WordEntityOrigins.Projected, "through BHSA word 7", Decision);
        Annotate(Greek(3, 1), boaz, LinkMethod.Manual, 0.98, WordEntityRoles.Thing, WordEntityOrigins.Projected, "through BHSA word 7", Decision);

        (await Shown(Greek(4, 1))).Should().BeEquivalentTo("jachin-pillar", "boaz-pillar");
        (await Shown(Greek(3, 1))).Should().BeEquivalentTo("jachin-pillar", "boaz-pillar");
        (await Settled(Greek(4, 1))).Should().ContainSingle("the verse lists' statement settles the word, which it read as nobody before");
    }

    /// <summary>
    /// Two men one ruling names on one word are still two answers to one question, and two things are no group where
    /// two decisions name them, or where a word carries each through another word.
    /// </summary>
    [Fact]
    public async Task OnlyThingsOneRulingNamesOnOneWordStandTogether()
    {
        var barak = Add("barak", EntityKind.Person);
        var sisera = Add("sisera", EntityKind.Person);
        var jachin = Add("jachin-pillar", EntityKind.Object);
        var boaz = Add("boaz-pillar", EntityKind.Object);
        Annotate(Greek(4, 1), barak, LinkMethod.Manual, null, WordEntityRoles.Name, WordEntityOrigins.Settled, source: Decision);
        Annotate(Greek(4, 1), sisera, LinkMethod.Manual, null, WordEntityRoles.Name, WordEntityOrigins.Settled, source: Decision);
        Annotate(Greek(3, 1), jachin, LinkMethod.Manual, null, WordEntityRoles.Thing, WordEntityOrigins.Settled, source: Decision);
        Annotate(Greek(3, 1), boaz, LinkMethod.Manual, null, WordEntityRoles.Thing, WordEntityOrigins.Settled, source: "another decision");
        Annotate(Greek(2, 1), jachin, LinkMethod.Manual, 0.98, WordEntityRoles.Thing, WordEntityOrigins.Projected, "through BHSA word 7", Decision);
        Annotate(Greek(2, 1), boaz, LinkMethod.Manual, 0.98, WordEntityRoles.Thing, WordEntityOrigins.Projected, "through BHSA word 8", Decision);

        (await Shown(Greek(4, 1))).Should().BeEmpty();
        (await Shown(Greek(3, 1))).Should().BeEmpty();
        (await Shown(Greek(2, 1))).Should().BeEmpty();
        (await Settled(Greek(4, 1))).Should().BeEmpty();
        (await Settled(Greek(3, 1))).Should().BeEmpty();
        (await Settled(Greek(2, 1))).Should().BeEmpty();
    }

    private Task<List<int>> Settled(Word word) =>
        _db.Database
            .SqlQueryRaw<int>($"WITH {Annotating.Settled} SELECT entity_id AS \"Value\" FROM settled WHERE word_id = {word.Id}")
            .ToListAsync();

    /// <summary>A word that names nothing has an empty list and no first answer.</summary>
    [Fact]
    public async Task AWordThatNamesNothingHasNone()
    {
        (await Shown(Greek(1, 1))).Should().BeEmpty();
        (await Annotations.Of(_db, Greek(1, 1).Id, CancellationToken.None)).Should().BeNull();
    }
}
