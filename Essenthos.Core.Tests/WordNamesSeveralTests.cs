using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

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
        var entity = new Entity { Kind = kind, Slug = slug, Name = slug, SourceId = slug, Source = "a test" };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private void Annotate(Word word, Entity entity, LinkMethod method, double? confidence)
    {
        _db.WordEntities.Add(new WordEntity
        {
            Word = word, Entity = entity, Method = method, Confidence = confidence, Source = "a test",
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

    private async Task<List<int>> Cited(Entity entity)
    {
        await new OwnReferenceLoader(_db, NullLogger<OwnReferenceLoader>.Instance).Load();
        return await _db.EntityVerses.Where(v => v.EntityId == entity.Id)
            .OrderBy(v => v.CanonicalVerse).Select(v => v.CanonicalVerse).ToListAsync();
    }

    /// <summary>
    /// Christ in Jesus Christ: the title and its bearer at the same standing and the same confidence.
    /// The word names both, the man first, and the verse is on both pages.
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
        (await Cited(jesus)).Should().Equal(1);
        (await Cited(anointed)).Should().Equal(1);
        (await Annotations.InChapter(_db, 1, 1, CancellationToken.None)).Keys.Should().BeEquivalentTo("jesus", "anointed");
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
        (await Cited(jesus)).Should().Equal(2);
        (await Cited(anointed)).Should().Equal(2);
    }

    /// <summary>A title alone is the whole answer: John 1:20 names the title and nobody.</summary>
    [Fact]
    public async Task ATitleAloneIsTheOnlyAnswer()
    {
        var anointed = Add("anointed", EntityKind.Title);
        Annotate(Greek(2, 1), anointed, LinkMethod.RuleBased, 0.99);

        (await Shown(Greek(2, 1))).Should().Equal("anointed");
        (await Cited(anointed)).Should().Equal(2);
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
        (await Cited(tetrarch)).Should().BeEmpty();
        (await Cited(abilene)).Should().Equal(4);
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
        (await Cited(abishag)).Should().Equal(3);
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
        (await Cited(sisera)).Should().BeEmpty();
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
        (await Cited(seir)).Should().BeEmpty();
    }

    /// <summary>A word that names nothing has an empty list and no first answer.</summary>
    [Fact]
    public async Task AWordThatNamesNothingHasNone()
    {
        (await Shown(Greek(1, 1))).Should().BeEmpty();
        (await Annotations.Of(_db, Greek(1, 1).Id, CancellationToken.None)).Should().BeNull();
    }
}
