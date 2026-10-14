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
/// The names a load carries over the links drawn since they were written, before it names anything.
///
/// Genesis 14:5 as the corpus stood on 2026-10-14: the Rephaim, the Zuzim and the Emim were named on
/// the Hebrew and carried into the Synodal long before, and the King James's links to the Hebrew were
/// drawn that day, so <em>Rephaims</em>, <em>Zuzims</em> and <em>Emins</em> stayed blank — nothing that
/// writes a name runs again for a name already written.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CarryNewLinksTests : IDisposable
{
    private const string People = "the gentilic Strong's Dictionary derives, resolving to exactly one people";

    private readonly AppDbContext _db;
    private readonly AnnotationCarrier _carrier;
    private readonly Text _hebrew;
    private readonly Text _russian;
    private readonly Text _english;
    private readonly Entity _rephaim;
    private readonly Entity _zuzim;
    private readonly Entity _emim;

    public CarryNewLinksTests(WitnessDatabase database)
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
            (14, 5, ["רפאים", "זוזים", "אימים"]),
            (14, 6, ["חרי"]));
        _russian = Corpus.Add(_db, "RUSV", TextKind.Translation, "rus",
            (14, 5, ["Рефаимов", "Зузимов", "Эмимов"]),
            (14, 6, ["Хорреев"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (14, 5, ["smote", "Rephaims", "Zuzims", "Emins"]));

        _rephaim = Add("rephaim", "Rephaim");
        _zuzim = Add("zuzims", "Zuzim");
        _emim = Add("emims", "Emim");
        var horites = Add("horites", "Horites");

        Seed(Hebrew(1), _rephaim);
        Seed(Hebrew(2), _zuzim);
        Seed(Hebrew(3), _emim);
        Seed(_db.WordAt(_hebrew, 14, 6, 1), horites);
        for (var position = 1; position <= 3; position++)
        {
            Link(Hebrew(position), Russian(position), LinkMethod.StrongNumber, 0.9);
        }

        Link(_db.WordAt(_hebrew, 14, 6, 1), _db.WordAt(_russian, 14, 6, 1), LinkMethod.StrongNumber, 0.9);
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

    private Entity Add(string slug, string name)
    {
        var entity = new Entity { Kind = EntityKind.People, Slug = slug, Name = name, SourceId = slug, Source = "a test" };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private void Seed(Word word, Entity entity)
    {
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id,
            EntityId = entity.Id,
            Method = LinkMethod.Lexical,
            Confidence = 0.9,
            Source = People,
            Note = "a gentilic",
            Claims = [new WordEntityClaim { Method = LinkMethod.Lexical, Confidence = 0.9, Source = People, Note = "a gentilic" }],
        });
        _db.SaveChanges();
    }

    private Word Hebrew(int position) => _db.WordAt(_hebrew, 14, 5, position);

    private Word Russian(int position) => _db.WordAt(_russian, 14, 5, position);

    private Word English(int position) => _db.WordAt(_english, 14, 5, position);

    private void Link(Word witness, Word rendering, LinkMethod method, double? confidence)
    {
        var link = new Link
        {
            FromTextId = rendering.TextId,
            ToTextId = witness.TextId,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = confidence,
            Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = rendering, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = witness, Side = LinkSide.To });
        _db.SaveChanges();
    }

    private async Task<Dictionary<long, WordEntity>> Named(Text text) =>
        await _db.WordEntities
            .AsNoTracking()
            .Where(a => a.Word!.TextId == text.Id)
            .ToDictionaryAsync(a => a.WordId);

    /// <summary>Every annotation and claim as a line, ids included, so two states can be compared whole.</summary>
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

    private async Task<long?> Mark() =>
        await _db.CarryMarks.AsNoTracking().Select(m => (long?)m.LinkId).SingleOrDefaultAsync();

    /// <summary>
    /// The case the step is for: the names stand on the Hebrew and on the Synodal, the King James's
    /// links arrive afterwards, and the next load names the King James's words over them without
    /// touching a row it already holds.
    /// </summary>
    [Fact]
    public async Task ANameWrittenBeforeTheLinkWasDrawnCrossesItAtTheNextLoad()
    {
        var first = await _carrier.CarryNewLinks();
        first.Since.Should().BeNull("nothing recorded how far the carry got, so it goes over every link");
        (await Named(_russian)).Should().HaveCount(4);
        var synodal = await Everything();

        for (var position = 1; position <= 3; position++)
        {
            Link(Hebrew(position), English(position + 1), LinkMethod.StatedBySource, null);
        }

        (await Named(_english)).Should().BeEmpty("nothing has carried over the new links yet");

        var outcome = await _carrier.CarryNewLinks();

        outcome.Since.Should().Be(first.Through);
        var english = await Named(_english);
        english.Keys.Should().BeEquivalentTo(new[] { English(2).Id, English(3).Id, English(4).Id });
        english[English(2).Id].EntityId.Should().Be(_rephaim.Id);
        english[English(3).Id].EntityId.Should().Be(_zuzim.Id);
        english[English(4).Id].EntityId.Should().Be(_emim.Id);
        english[English(4).Id].Confidence.Should().BeApproximately(0.9, 1e-9, "a link the source states adds no doubt");
        outcome.Written.Should().Be(3);
        outcome.Withdrawn.Should().Be(0);
        (await Everything()).Should().Contain(synodal, "a row the links still reproduce keeps its id");
        (await Mark()).Should().Be(outcome.Through);
    }

    /// <summary>
    /// Genesis 14:6 is a verse no new link reaches, and nothing there is asked again; and a second
    /// load finds no link newer than the mark and writes nothing at all.
    /// </summary>
    [Fact]
    public async Task OnlyTheVersesTheNewLinksReachAreAskedAgainAndASecondRunWritesNothing()
    {
        await _carrier.CarryNewLinks();
        Link(Hebrew(1), English(2), LinkMethod.StatedBySource, null);

        var outcome = await _carrier.CarryNewLinks();

        outcome.Scope.Should().Be(7, "the three Hebrew words of 14:5 and the four English ones");
        var before = await Everything();

        var again = await _carrier.CarryNewLinks();

        again.Since.Should().Be(again.Through);
        again.Written.Should().Be(0);
        (await Everything()).Should().Equal(before);
    }

    /// <summary>
    /// What the step leaves on the words a new link reaches is what a carry over every link leaves,
    /// where it matters most: a new link that makes two Hebrew words disagree about one word takes the
    /// name off it, and a firm rendering drawn later takes the name off the faint one beside it, a
    /// word no new link touches.
    /// </summary>
    [Fact]
    public async Task OverTheVersesItReachesTheStepLeavesWhatACarryOverEveryLinkLeaves()
    {
        Link(Hebrew(1), English(1), LinkMethod.Aligner, 0.69);
        Link(Hebrew(3), English(3), LinkMethod.Aligner, 0.8);
        await _carrier.CarryNewLinks();
        var english = await Named(_english);
        english[English(1).Id].EntityId.Should().Be(_rephaim.Id);
        english[English(3).Id].EntityId.Should().Be(_emim.Id);

        Link(Hebrew(1), English(2), LinkMethod.StatedBySource, null);
        Link(Hebrew(2), English(3), LinkMethod.StatedBySource, null);

        var outcome = await _carrier.CarryNewLinks();

        english = await Named(_english);
        english.Should().NotContainKey(English(1).Id, "the Rephaim are rendered firmly beside it now");
        english.Should().NotContainKey(English(3).Id, "the Zuzim and the Emim both reach it");
        english[English(2).Id].EntityId.Should().Be(_rephaim.Id);
        outcome.Withdrawn.Should().Be(2);

        var scoped = await Everything();
        await _carrier.Carry();
        (await Everything()).Should().Equal(scoped, "a carry over every link finds nothing left to change");
    }

    /// <summary>The carry over every link, which the commands that move links run, moves the mark too.</summary>
    [Fact]
    public async Task ACarryOverEveryLinkLeavesNothingForTheNextLoad()
    {
        Link(Hebrew(1), English(2), LinkMethod.StatedBySource, null);
        await _carrier.Carry();
        (await Named(_english)).Should().ContainKey(English(2).Id);

        var outcome = await _carrier.CarryNewLinks();

        outcome.Since.Should().Be(outcome.Through);
        outcome.Written.Should().Be(0);
    }
}
