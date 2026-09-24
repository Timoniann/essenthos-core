using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Composing a pair a second time. The first run's agreement with a statement lives as a claim on
/// the stated link rather than as a link of its own, so a rerun that replaced only the aligner's
/// links would keep every agreement the model once made beside the ones it makes now.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CompositionRerunTests : IDisposable
{
    private const string Door43 = "a Door43 interlinear";

    private readonly AppDbContext _db;
    private readonly CompositionPipeline _composer;
    private readonly Text _ukrainian;
    private readonly Text _greek;

    public CompositionRerunTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _composer = new CompositionPipeline(
            _db,
            new AlignmentPipeline(_db, NullLogger<AlignmentPipeline>.Instance),
            NullLogger<CompositionPipeline>.Instance);

        _ukrainian = Corpus.Add(_db, "UBIO", TextKind.Translation, "ukr", (1, 1, ["Павло", "апостол", "Христа"]));
        _greek = Corpus.Add(_db, "NESTLE1904", TextKind.CriticalEdition, "grc", (1, 1, ["Παῦλος", "ἀπόστολος", "Χριστοῦ"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    [Fact]
    public async Task AnAgreementTheModelNoLongerMakesLeavesTheStatementAlone()
    {
        var stated = Link(1, 1, LinkMethod.StatedBySource, null, Door43);
        Claim(stated, LinkMethod.Aligner, 0.9, "SIL.Machine, aligned as written");

        await Write(new RoutedLink(Ukrainian(1).Id, Greek(2).Id, 0.7, Route.Written));

        var link = (await Links()).Single(one => one.Id == stated.Id);
        link.Method.Should().Be(LinkMethod.StatedBySource);
        link.Claims.Should().ContainSingle().Which.Source.Should().Be(Door43);
    }

    [Fact]
    public async Task AnAgreementMadeAgainIsOneClaimAndNotTwo()
    {
        var stated = Link(1, 1, LinkMethod.StatedBySource, null, Door43);
        Claim(stated, LinkMethod.Aligner, 0.9, "SIL.Machine, aligned as written");

        var (fresh, corroborated) = await Write(
            new RoutedLink(Ukrainian(1).Id, Greek(1).Id, 0.95, Route.Written | Route.Composed));

        (fresh, corroborated).Should().Be((0, 1));
        var aligner = (await Links()).Single(one => one.Id == stated.Id).Claims
            .Should().ContainSingle(claim => claim.Method == LinkMethod.Aligner).Which;
        aligner.Confidence.Should().Be(0.95);
        aligner.Source.Should().Be(Routes.Describe(Route.Written | Route.Composed, "KJV"));
    }

    [Fact]
    public async Task TheAlignersOwnLinksAreReplacedAndAnotherMethodsClaimsAreNot()
    {
        Link(2, 2, LinkMethod.Aligner, 0.6, "SIL.Machine, aligned as stems");
        var numbered = Link(3, 3, LinkMethod.StrongNumber, 0.9, "a Strong edition");
        Claim(numbered, LinkMethod.StatedBySource, null, Door43);

        await Write(new RoutedLink(Ukrainian(2).Id, Greek(3).Id, 0.5, Route.Reduced));

        var links = await Links();
        links.Should().HaveCount(2);
        links.Should().ContainSingle(link => link.Method == LinkMethod.Aligner)
            .Which.Words.Select(word => word.WordId).Should().BeEquivalentTo([Ukrainian(2).Id, Greek(3).Id]);
        links.Single(link => link.Id == numbered.Id).Claims.Select(claim => claim.Method).Should()
            .BeEquivalentTo([LinkMethod.StrongNumber, LinkMethod.StatedBySource]);
    }

    private async Task<(int Fresh, int Corroborated)> Write(params RoutedLink[] merged)
    {
        await _db.Database.OpenConnectionAsync();
        try
        {
            return await _composer.Write(
                (NpgsqlConnection)_db.Database.GetDbConnection(), _ukrainian, _greek, ["KJV"], merged,
                CancellationToken.None);
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }

    private Word Ukrainian(int position) => _db.WordAt(_ukrainian, 1, 1, position);

    private Word Greek(int position) => _db.WordAt(_greek, 1, 1, position);

    private Link Link(int ukrainian, int greek, LinkMethod method, double? confidence, string source)
    {
        var link = new Link
        {
            FromTextId = _ukrainian.Id,
            ToTextId = _greek.Id,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = confidence,
            Source = source,
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = Ukrainian(ukrainian), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = Greek(greek), Side = LinkSide.To });
        _db.LinkClaims.Add(new LinkClaim { Link = link, Method = method, Confidence = confidence, Source = source });
        _db.SaveChanges();
        return link;
    }

    private void Claim(Link link, LinkMethod method, double? confidence, string source)
    {
        _db.LinkClaims.Add(new LinkClaim { LinkId = link.Id, Method = method, Confidence = confidence, Source = source });
        _db.SaveChanges();
    }

    private async Task<List<Link>> Links() =>
        await _db.Links
            .AsNoTracking()
            .Include(link => link.Claims)
            .Include(link => link.Words)
            .Where(link => link.FromTextId == _ukrainian.Id)
            .OrderBy(link => link.Id)
            .ToListAsync();
}
