using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Names;

/// <summary>
/// 1 Kings 7:13 with its crossed links: the Hebrew names Hiram before Tyre and the Synodal names Tyre
/// before Hiram, and the numbering links each Hebrew name to the other Russian word. The carry reaches
/// the Russian words with each other's names; the crossing takes them back. Once crossed back, a carry
/// over the verse again (a link drawn there later) leaves them as they are, so the two passes do not
/// undo each other at every load.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CrossedNameCarryTests : IDisposable
{
    private const string Seeded = "the name resolution of a test";

    private readonly AppDbContext _db;
    private readonly AnnotationCarrier _carrier;
    private readonly CrossedNameLoader _crossed;
    private readonly Text _hebrew;
    private readonly Text _russian;
    private readonly Entity _hiram;
    private readonly Entity _tyre;

    public CrossedNameCarryTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _crossed = new CrossedNameLoader(_db, NullLogger<CrossedNameLoader>.Instance);
        _carrier = new AnnotationCarrier(
            _db,
            _crossed,
            new ForeignNames(_db, NullLogger<ForeignNames>.Instance),
            new EqualTwinNames(_db, NullLogger<EqualTwinNames>.Instance),
            new PronounReferents(_db, NullLogger<PronounReferents>.Instance),
            NullLogger<AnnotationCarrier>.Instance);

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (7, 13, ["ויקח", "את", "חירם", "מצר"]));
        _russian = Corpus.Add(_db, "RUSV", TextKind.Translation, "rus",
            (7, 13, ["взял", "из", "Тира", "Хирама"]));
        _db.SaveChanges();
        foreach (var word in _db.Words.Where(w => w.TextId == _russian.Id))
        {
            word.NormalisedText = word.Surface.ToLowerInvariant();
        }

        _hiram = Record("hiram-2", "Hiram", ("nominative", "Хирам"), ("genitive", "Хирама"));
        _tyre = Record("tyre", "Tyre", ("nominative", "Тир"), ("genitive", "Тира"));
        _db.SaveChanges();

        Seed(Hebrew(3), _hiram);
        Seed(Hebrew(4), _tyre);
        Link(Hebrew(3), Russian(3));
        Link(Hebrew(4), Russian(4));
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM carry_mark");
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    private Entity Record(string slug, string name, params (string Case, string Form)[] forms)
    {
        var entity = new Entity { Kind = EntityKind.Person, Slug = slug, Name = name, SourceId = slug, Source = "a test" };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        foreach (var (grammaticalCase, form) in forms)
        {
            _db.EntityNameForms.Add(new EntityNameForm
            {
                Entity = entity, Language = "rus", GrammaticalCase = grammaticalCase, Form = form,
                Method = LinkMethod.Manual, Source = "a test",
            });
        }

        return entity;
    }

    private void Seed(Word word, Entity entity)
    {
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id, EntityId = entity.Id, Method = LinkMethod.Lexical, Confidence = 0.9,
            Source = Seeded, Note = "a seed",
            Claims = [new WordEntityClaim { Method = LinkMethod.Lexical, Confidence = 0.9, Source = Seeded, Note = "a seed" }],
        });
        _db.SaveChanges();
    }

    private Word Hebrew(int position) => _db.WordAt(_hebrew, 7, 13, position);

    private Word Russian(int position) => _db.WordAt(_russian, 7, 13, position);

    /// <summary>The numbering's link, which puts the Hebrew name on the Russian word of the other.</summary>
    private void Link(Word witness, Word rendering)
    {
        var link = new Link
        {
            FromTextId = rendering.TextId, ToTextId = witness.TextId, Relation = LinkRelation.Renders,
            Method = LinkMethod.StrongNumber, Confidence = 0.9, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = rendering, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = witness, Side = LinkSide.To });
        _db.SaveChanges();
    }

    private async Task<List<string>> Everything() =>
    [
        .. await _db.WordEntities.AsNoTracking()
            .OrderBy(a => a.Id)
            .Select(a => $"{a.Id} {a.WordId} {a.EntityId} {a.Method} {a.Confidence} {a.Source} {a.Note}")
            .ToListAsync(),
        .. await _db.WordEntityClaims.AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => $"{c.Id} {c.WordEntityId} {c.Method} {c.Confidence} {c.Source} {c.Note}")
            .ToListAsync(),
    ];

    [Fact]
    public async Task ACarryOverTheVerseAgainLeavesTheCrossedBackNamesAsTheyAre()
    {
        await _carrier.Carry();
        var named = await _db.WordEntities.AsNoTracking().Where(a => a.Word!.TextId == _russian.Id).ToListAsync();
        named.Single(a => a.WordId == Russian(3).Id).EntityId.Should().Be(_tyre.Id);
        named.Single(a => a.WordId == Russian(4).Id).EntityId.Should().Be(_hiram.Id);
        var crossedBack = await Everything();

        // A link drawn later in the same verse: the next load carries over the verse again.
        Link(Hebrew(1), Russian(1));
        var carried = await _carrier.CarryNewLinks();

        carried.Scope.Should().BeGreaterThan(0);
        (await _crossed.Load()).Pairs.Should().Be(0, "the carry did not give the two words each other's names again");
        (await Everything()).Should().Equal(crossedBack, "neither pass wrote or took back anything");
    }

    [Fact]
    public async Task ACarryOverEveryLinkLeavesTheCrossedBackNamesAsTheyAre()
    {
        await _carrier.Carry();
        var crossedBack = await Everything();

        await _carrier.Carry();

        (await Everything()).Should().Equal(crossedBack);
    }
}
