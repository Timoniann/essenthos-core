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
/// Who a word of the Hebrew Bible is allowed to be naming.
///
/// Two failures are under test here and they pull in opposite directions. A candidate list that
/// carries people the text cannot be talking about asks a harder question than the text poses and
/// inflates every count of ambiguity; a list that cannot carry a place at all forces the answer
/// <em>the referent is not listed</em> for words whose referent the corpus is holding. Both were
/// real, and a rule that fixes one by loosening or tightening everything would cause the other.
///
/// <para>
/// Asked of Postgres, because the rule is a statement rather than a loop and what is under test is
/// what it selects.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EntityCandidateTests : IDisposable
{
    private const string Geocoding = "OpenBible.info Bible Geocoding";

    private readonly AppDbContext _db;
    private readonly EntityAnnotationLoader _loader;
    private readonly Text _hebrew;
    private readonly Text _english;

    /// <summary>
    /// One verse per case, so a failure names the case. The Hebrew is the witness and the King
    /// James beside it is both the rendering the place join is read through and the text the
    /// annotation travels to.
    /// </summary>
    public EntityCandidateTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _loader = new EntityAnnotationLoader(_db, NullLogger<EntityAnnotationLoader>.Instance);

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (1, 1, ["יהודה"]),
            (1, 2, ["פלוני"]),
            (1, 3, ["צער"]),
            (1, 4, ["יבש", "גלעד"]),
            (1, 5, ["אחר"]));

        _english = Corpus.Add(_db, EntityCandidates.Rendering, TextKind.Translation, "eng",
            (1, 3, ["Zoar"]),
            (1, 4, ["Jabeshgilead"]),
            (1, 5, ["thither"]));

        _db.SaveChanges();

        Marked(1, 1, "H3063", "pers");
        Marked(2, 1, "H1000", "pers");
        Marked(3, 1, "H6820", "topo");
        Marked(4, 1, "H3003", "topo");
        Marked(4, 2, "H1568", "topo");
        Marked(5, 1, "H7000", "topo");

        // The man of the Hebrew Bible, and the disciple who bears his name in Greek. The
        // encyclopedia records the Hebrew number on both, because Judas is Judah in Greek.
        Attest(Person("judah", "Judah", "H3063"), book: 1, verse: 1);
        Attest(Person("judas", "Judas", "H3063"), book: 40, verse: 10);

        // A person the encyclopedia holds and attests nowhere. Silence is not evidence that the
        // Masoretic text does not name him.
        Person("unattested", "Unattested", "H1000");

        Attest(Place("zoar", "Zoar"), book: 1, verse: 3);
        Attest(Place("jabeshgilead", "Jabesh-gilead"), book: 1, verse: 4);
        Attest(Place("elsewhere", "Elsewhere"), book: 1, verse: 5);

        Renders([(3, 1)], (3, 1));
        Renders([(4, 1), (4, 2)], (4, 1));
        Renders([(5, 1)], (5, 1));
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    private void Marked(int verse, int position, string number, string nameType)
    {
        var word = _db.WordAt(_hebrew, 1, verse, position);
        word.StrongNumber = number;
        word.Morphology = JsonDocument.Parse($$"""{"pos": "subs", "nameType": "{{nameType}}"}""");
        _db.SaveChanges();
    }

    private Entity Person(string slug, string name, string number)
    {
        var entity = Add(slug, name, EntityKind.Person, "a test", null);
        _db.EntityNames.Add(new EntityName
        {
            Entity = entity, Label = name, HebrewStrongNumber = number, Kind = "name",
        });
        _db.SaveChanges();
        return entity;
    }

    /// <summary>
    /// A place as the geocoding dataset supplies one: a name, an identifier of its own, and no
    /// Strong number anywhere, which is why nothing could reach it before.
    /// </summary>
    private Entity Place(string slug, string name) =>
        Add(slug, name, EntityKind.Place, Geocoding, $"a{slug}");

    private Entity Add(string slug, string name, EntityKind kind, string source, string? openBibleId)
    {
        var entity = new Entity
        {
            Kind = kind,
            Slug = slug,
            Name = name,
            SourceId = slug,
            Source = source,
            OpenBibleId = openBibleId,
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private void Attest(Entity entity, int book, int verse)
    {
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = entity,
            CanonicalBook = book,
            CanonicalChapter = 1,
            CanonicalVerse = verse,
            Source = "a test",
        });
        _db.SaveChanges();
    }

    /// <summary>
    /// One link, naming the Hebrew words it takes and the King James word that stands for them.
    /// Together with the attestation and BHSA's marking, that is everything the place join reads.
    /// </summary>
    private void Renders((int Verse, int Position)[] hebrew, (int Verse, int Position) english)
    {
        var rendering = _db.WordAt(_english, 1, english.Verse, english.Position);
        var link = new Link
        {
            FromTextId = _hebrew.Id,
            ToTextId = _english.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource,
            Source = "a test",
        };
        _db.Links.Add(link);

        foreach (var (verse, position) in hebrew)
        {
            _db.LinkWords.Add(new LinkWord
            {
                Link = link, Word = _db.WordAt(_hebrew, 1, verse, position), Side = LinkSide.From,
            });
        }

        _db.LinkWords.Add(new LinkWord { Link = link, Word = rendering, Side = LinkSide.To });
        _db.SaveChanges();
    }

    private async Task<Dictionary<long, string>> Load()
    {
        await _loader.Load();
        return await _db.WordEntities.ToDictionaryAsync(a => a.WordId, a => a.Entity!.Slug);
    }

    private Word Hebrew(int verse, int position = 1) => _db.WordAt(_hebrew, 1, verse, position);

    /// <summary>
    /// The failure this rule exists for. Judas Iscariot cannot be what a word of the Masoretic text
    /// means, however truly the encyclopedia records that his name is Judah's, and while he counts
    /// as a candidate the name looks contested when it is not.
    /// </summary>
    [Fact]
    public async Task SomebodyNamedOnlyWhereTheTextDoesNotReachIsNotACandidate()
    {
        var named = await Load();
        named.Should().ContainKey(Hebrew(1).Id).WhoseValue.Should().Be("judah");
    }

    /// <summary>
    /// And the discipline that keeps that from becoming a licence to prune. An entity the
    /// encyclopedia attests nowhere has not been shown to be out of reach; it has been shown
    /// nothing about, and a rule that dropped it would be reading a gap in the dataset as a fact
    /// about the text.
    /// </summary>
    [Fact]
    public async Task SomebodyTheEncyclopediaAttestsNowhereIsStillACandidate()
    {
        var named = await Load();
        named.Should().ContainKey(Hebrew(2).Id).WhoseValue.Should().Be("unattested");
    }

    /// <summary>
    /// The other failure. The geocoding dataset carries no Strong numbers, so before the join was
    /// built a word BHSA marks as a place had only people to choose from and every place-sense
    /// occurrence had to be answered with a person or with nothing.
    /// </summary>
    [Fact]
    public async Task APlaceWithNoStrongNumberIsReachedThroughTheKingJamesRendering()
    {
        var named = await Load();
        named.Should().ContainKey(Hebrew(3).Id).WhoseValue.Should().Be("zoar");
    }

    /// <summary>
    /// A derived name is worth less than a stated one and says so in the row, so that a reader — or
    /// a later loader — can tell the corpus's own conclusion from the encyclopedia's statement.
    /// </summary>
    [Fact]
    public async Task ADerivedNameIsWrittenAsTheCorpusOwnConclusion()
    {
        await _loader.Load();

        var place = Hebrew(3).Id;
        var person = Hebrew(1).Id;
        var derived = await _db.WordEntities.SingleAsync(a => a.WordId == place);
        var stated = await _db.WordEntities.SingleAsync(a => a.WordId == person);

        derived.Source.Should().NotBe(stated.Source);
        derived.Confidence.Should().BeLessThan(stated.Confidence!.Value);
    }

    /// <summary>
    /// <em>Jabesh-gilead</em> is one King James word standing opposite two Hebrew place names, and
    /// nothing in the link says which of them the place is called by. Taking either would make
    /// Jabesh-gilead a candidate for every occurrence of Gilead in the Bible, so the link is
    /// refused whole and the place stays unreachable.
    /// </summary>
    [Fact]
    public async Task ANameTheLinkCannotTellApartFromItsNeighbourIsRefused()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(4).Id);
        named.Should().NotContainKey(Hebrew(4, 2).Id);
    }

    /// <summary>
    /// Being named in the verse is not enough. The King James has to print the place's own name at
    /// the word, or the only thing established is that the two happen to be in the same verse —
    /// which is how a place ends up on the word beside it.
    /// </summary>
    [Fact]
    public async Task APlaceTheRenderingDoesNotNameIsRefused()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(5).Id);
    }
}
