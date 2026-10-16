using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The names of a text no link reaches, placed on the original's: the Korean of Genesis 10:2 against
/// the Greek, with whom the Korean's verses say its words name and nothing else.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class NamesFirstPassTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Text _korean;
    private readonly Text _greek;

    public NamesFirstPassTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");

        _korean = Corpus.Add(_db, "KRV", TextKind.Translation, "kor", (10, 2, ["야벳의", "아들은", "고멜과", "마곡과", "마대와"]));
        _greek = Corpus.Add(_db, "GRCBRENT", TextKind.Translation, "grc",
            (10, 2, ["Υἱοὶ", "Ἰάφεθ", "Γαμὲρ", "καὶ", "Μαγὼγ", "καὶ", "Μαδοὶ"]));
        _db.SaveChanges();
        foreach (var word in _db.Words.Where(w => w.TextId == _greek.Id).ToList())
        {
            word.Lemma = char.IsUpper(word.Surface[0]) && word.Surface != "Υἱοὶ" ? word.Surface : word.Surface.ToLowerInvariant();
        }

        var japheth = Person("japheth");
        var gomer = Person("gomer");
        var magog = Person("magog");
        var madai = Person("madai");
        _db.SaveChanges();

        foreach (var (position, entity) in new[] { (2, japheth), (3, gomer), (5, magog), (7, madai) })
        {
            Name(_greek, position, entity, LinkMethod.Lexical, "a test");
        }

        // The Korean's verses name Japheth, Gomer and Magog; Madai's word they leave to nobody.
        foreach (var (position, entity) in new[] { (1, japheth), (3, gomer), (4, magog) })
        {
            Name(_korean, position, entity, LinkMethod.RuleBased, Annotations.Consensus);
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    /// <summary>
    /// Japheth, Gomer and Magog are named by the verses and spelt alike on both sides: placed, and written
    /// as a rule's links. 마대와 is MD as Μαδοί is, but in a script without capitals two consonants
    /// are no name by their letters alone, and nothing else says it is one: left.
    /// </summary>
    [Fact]
    public async Task PlacesTheNamesTheVersesAndTheLettersAgreeOnAndWritesThem()
    {
        var report = await Pass().Run("KRV", "GRCBRENT", null, null, NamePolicy.Default, held: false, output: null, apply: true);

        report.Should().Contain("Wrote 3 links of names");
        var links = await Links();
        links.Should().BeEquivalentTo([(1, 2), (3, 3), (4, 5)]);
        (await _db.Links.AllAsync(link => link.Method == LinkMethod.RuleBased && link.Confidence == NamesFirstPass.Confidence))
            .Should().BeTrue();
    }

    /// <summary>A second run finds every name already linked and writes nothing.</summary>
    [Fact]
    public async Task ASecondRunWritesNothing()
    {
        await Pass().Run("KRV", "GRCBRENT", null, null, NamePolicy.Default, held: false, output: null, apply: true);
        var after = await _db.Links.CountAsync();

        var again = await Pass().Run("KRV", "GRCBRENT", null, null, NamePolicy.Default, held: false, output: null, apply: true);

        again.Should().Contain("Wrote 0 links of names");
        (await _db.Links.CountAsync()).Should().Be(after);
    }

    /// <summary>Without --apply the pass reports and writes nothing.</summary>
    [Fact]
    public async Task WritesNothingUnlessAsked()
    {
        var report = await Pass().Run("KRV", "GRCBRENT", null, null, NamePolicy.Default, held: false, output: null, apply: false);

        report.Should().Contain("3 pairs placed");
        (await _db.Links.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// אֲדֹנָי beside יְהוִה is a title rendered word by word, <em>O Lord God</em>: both are names of the
    /// one God to BHSA, but neither is placed, where the Douay's verses name the Lord with its O and
    /// its Lord and the name took the Lord (PRB-1099). Joshua beside them still is.
    /// </summary>
    [Fact]
    public async Task LeavesTheLordGodToTheOtherSteps()
    {
        var english = Corpus.Add(_db, "DRA", TextKind.Translation, "eng", (10, 2, ["Josue", "said", "Alas", "O", "Lord", "God"]));
        var hebrew = Corpus.Add(_db, "BHSA", TextKind.ManuscriptTradition, "hbo", (10, 2, ["אמר", "יְהֹושֻׁעַ", "אֲהָהּ", "אֲדֹנָי", "יְהוִה"]));
        _db.SaveChanges();
        foreach (var (position, lemma) in new[] { (2, "יְהֹושֻׁעַ"), (4, NamesFirstPass.Adonai), (5, NamesFirstPass.Yhwh) })
        {
            var word = _db.WordAt(hebrew, 10, 2, position);
            word.Lemma = lemma;
            word.Morphology = JsonDocument.Parse("""{"pos":"nmpr"}""");
        }

        var joshua = Person("joshua");
        var god = Person("yhvh");
        _db.SaveChanges();
        foreach (var (position, entity) in new[] { (2, joshua), (4, god), (5, god) })
        {
            Name(hebrew, position, entity, LinkMethod.Lexical, "a test");
        }

        foreach (var (position, entity) in new[] { (1, joshua), (4, god), (5, god) })
        {
            Name(english, position, entity, LinkMethod.RuleBased, Annotations.Consensus);
        }

        _db.SaveChanges();

        var report = await Pass().Run("DRA", "BHSA", null, null, NamePolicy.Default, held: false, output: null, apply: true);

        report.Should().Contain("Wrote 1 links of names");
        (await Links()).Should().BeEquivalentTo([(1, 2)]);
    }

    private Entity Person(string slug)
    {
        var entity = new Entity { Kind = EntityKind.Person, Slug = slug, Name = slug, SourceId = slug, Source = "a test" };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Name(Text text, int position, Entity entity, LinkMethod method, string source) =>
        _db.WordEntities.Add(new WordEntity
        {
            WordId = _db.WordAt(text, 10, 2, position).Id, EntityId = entity.Id, Method = method, Confidence = 0.9, Source = source,
        });

    private NamesFirstPass Pass() =>
        new(_db, new AlignmentPipeline(_db, NullLogger<AlignmentPipeline>.Instance), NullLogger<NamesFirstPass>.Instance);

    private async Task<List<(int Korean, int Greek)>> Links() =>
        (await _db.Links.AsNoTracking()
            .Select(link => new
            {
                Korean = link.Words.Single(word => word.Side == LinkSide.From).Word!.Position,
                Greek = link.Words.Single(word => word.Side == LinkSide.To).Word!.Position,
            })
            .ToListAsync())
        .Select(link => (link.Korean, link.Greek))
        .ToList();
}
