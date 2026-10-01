using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A link's words as one fingerprint, the one link a pair of texts may hold over them, and the
/// sources and notes kept once and pointed at by id. A second link over the same words is what a
/// Clear Bible set loaded over the aligner's links wrote 388,190 times, and the database is now
/// what refuses it.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class LinkWriterTests : IDisposable
{
    private const string Aligner = "SIL.Machine, aligned as written";
    private const string Clear = "Clear Bible's alignment";

    private readonly AppDbContext _db;
    private readonly Text _english;
    private readonly Text _greek;

    public LinkWriterTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _english = Corpus.Add(_db, "ENGT", TextKind.Translation, "eng", (1, 1, ["Paul", "an", "apostle"]));
        _greek = Corpus.Add(_db, "GRKT", TextKind.CriticalEdition, "grc", (1, 1, ["Παῦλος", "ἀπόστολος"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    [Fact]
    public async Task TheDatabaseShapesALinkAsTheLoadersDo()
    {
        var link = Link([English(2), English(3)], [Greek(2)], LinkMethod.Manual, null, "a person");

        (await Fingerprint(link.Id)).Should().Be(LinkShape.Of([English(3), English(2)], [Greek(2)]));
        LinkShape.Of([1, 2], [3]).Should().NotBe(LinkShape.Of([1], [2, 3]), "the side a word stands on is part of the shape");
    }

    [Fact]
    public void ASecondLinkOverTheSameWordsIsRefused()
    {
        Link([English(1)], [Greek(1)], LinkMethod.Aligner, 0.8, Aligner);

        var second = () => Link([English(1)], [Greek(1)], LinkMethod.StatedBySource, null, Clear);

        second.Should().Throw<Exception>().Where(e => Refused(e));
    }

    [Fact]
    public async Task TakingAWordOutOfALinkReshapesIt()
    {
        var link = Link([English(2), English(3)], [Greek(2)], LinkMethod.Aligner, 0.7, Aligner);

        await _db.LinkWords.Where(word => word.LinkId == link.Id && word.WordId == English(2)).ExecuteDeleteAsync();

        (await Fingerprint(link.Id)).Should().Be(LinkShape.Of([English(3)], [Greek(2)]));
    }

    [Fact]
    public async Task LinksWhoseWordsAreAllTakenOutHaveNoShapeToShare()
    {
        var one = Link([English(1)], [Greek(1)], LinkMethod.Aligner, 0.7, Aligner);
        var two = Link([English(2)], [Greek(2)], LinkMethod.Aligner, 0.7, Aligner);

        await _db.LinkWords.ExecuteDeleteAsync();

        (await Fingerprint(one.Id)).Should().BeNull();
        (await Fingerprint(two.Id)).Should().BeNull();
    }

    [Fact]
    public async Task AStatementOverTheAlignersWordsBecomesItsClaimAndItsAnswer()
    {
        var guess = Link([English(1)], [Greek(1)], LinkMethod.Aligner, 0.8, Aligner);

        var written = await Write(Draft([English(1)], [Greek(1)], LinkMethod.StatedBySource, null, Clear));

        written.Ids.Should().Equal(guess.Id);
        written.Fresh.Should().Equal(false);
        var link = await Stored(guess.Id);
        link.Method.Should().Be(LinkMethod.StatedBySource, "a statement outranks the guess it agrees with");
        link.Confidence.Should().BeNull();
        link.Provenance!.Source.Should().Be(Clear);
        link.Claims.Select(claim => (claim.Method, claim.Provenance!.Source)).Should()
            .BeEquivalentTo([(LinkMethod.Aligner, Aligner), (LinkMethod.StatedBySource, Clear)]);
    }

    [Fact]
    public async Task AGuessOverAStatementsWordsIsOnlyAClaim()
    {
        var stated = Link([English(1)], [Greek(1)], LinkMethod.StatedBySource, null, Clear);

        await Write(Draft([English(1)], [Greek(1)], LinkMethod.Aligner, 0.6, Aligner));

        var link = await Stored(stated.Id);
        link.Method.Should().Be(LinkMethod.StatedBySource);
        link.Claims.Should().HaveCount(2);
    }

    [Fact]
    public async Task TwoDraftsOverTheSameWordsAreOneLinkWithTwoClaims()
    {
        var written = await Write(
            Draft([English(2), English(3)], [Greek(2)], LinkMethod.Aligner, 0.5, Aligner),
            Draft([English(3), English(2)], [Greek(2)], LinkMethod.StatedBySource, null, Clear),
            Draft([English(1)], [Greek(1)], LinkMethod.StatedBySource, null, Clear));

        written.Fresh.Should().Equal(true, false, true);
        written.Ids[1].Should().Be(written.Ids[0]);
        (await _db.Links.CountAsync()).Should().Be(2);
        var link = await Stored(written.Ids[0]);
        link.Method.Should().Be(LinkMethod.StatedBySource);
        link.Words.Should().HaveCount(3);
        (await Fingerprint(written.Ids[0])).Should().Be(LinkShape.Of([English(2), English(3)], [Greek(2)]));
    }

    [Fact]
    public async Task OneSourceAndNoteIsOneRowWhoeverWritesIt()
    {
        Link([English(1)], [Greek(1)], LinkMethod.StatedBySource, null, Clear);
        await Write(Draft([English(2)], [Greek(2)], LinkMethod.StatedBySource, null, Clear));

        (await _db.Provenances.CountAsync(p => p.Source == Clear)).Should().Be(1);
        (await _db.Links.Select(link => link.ProvenanceId).Distinct().CountAsync()).Should().Be(1);
    }

    private Link Link(long[] english, long[] greek, LinkMethod method, double? confidence, string source)
    {
        var link = new Link
        {
            FromTextId = _english.Id,
            ToTextId = _greek.Id,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = confidence,
            Provenance = new() { Source = source },
            Claims = [new LinkClaim { Method = method, Confidence = confidence, Provenance = new() { Source = source } }],
            Words =
            [
                .. english.Select(word => new LinkWord { WordId = word, Side = LinkSide.From }),
                .. greek.Select(word => new LinkWord { WordId = word, Side = LinkSide.To }),
            ],
        };
        _db.Links.Add(link);
        try
        {
            _db.SaveChanges();
        }
        finally
        {
            _db.ChangeTracker.Clear();
        }

        return link;
    }

    private NewLink Draft(long[] english, long[] greek, LinkMethod method, double? confidence, string source) =>
        new(_english.Id, _greek.Id, LinkRelation.Renders, method, confidence, source, null, english, greek);

    private async Task<LinkWrite> Write(params NewLink[] drafts)
    {
        await _db.Database.OpenConnectionAsync();
        try
        {
            await using var transaction = await ((NpgsqlConnection)_db.Database.GetDbConnection()).BeginTransactionAsync();
            var written = await LinkWriter.Write(
                (NpgsqlConnection)_db.Database.GetDbConnection(), transaction, drafts, CancellationToken.None);
            await transaction.CommitAsync();
            return written;
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }

    private async Task<Link> Stored(long id) =>
        await _db.Links.AsNoTracking()
            .Include(link => link.Provenance)
            .Include(link => link.Claims).ThenInclude(claim => claim.Provenance)
            .Include(link => link.Words)
            .SingleAsync(link => link.Id == id);

    private async Task<Guid?> Fingerprint(long id) =>
        await _db.Links.AsNoTracking().Where(link => link.Id == id).Select(link => link.Fingerprint).SingleAsync();

    private static bool Refused(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
        || (exception.InnerException is { } inner && Refused(inner));

    private long English(int position) => _db.WordAt(_english, 1, 1, position).Id;

    private long Greek(int position) => _db.WordAt(_greek, 1, 1, position).Id;
}
