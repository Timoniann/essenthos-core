using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What a word's panel says made each rendering, and how often two sources agreed where both spoke.
///
/// A rendering listed with nothing beside it reads as a fact about the text; the same list with the
/// method, the number and the credit beside each row reads as the claim it is.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class RenderingProvenanceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _english;
    private readonly Text _hebrew;

    public RenderingProvenanceTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _hebrew = Corpus.Add(_db, "BHSA", TextKind.ManuscriptTradition, "hbo",
            (1, 1, ["בְּ", "רֵאשִׁית"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (1, 1, ["In", "the", "beginning"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task ARenderingNamesItsMethodAndCarriesNoNumberWhereASourceStatedIt()
    {
        Link(3, 2, (LinkMethod.StatedBySource, null, "a test"));

        var rendering = (await RenderingsOf(_db.WordAt(_english, 1, 1, 3).Id)).Should().ContainSingle().Subject;

        rendering.Method.Should().Be("stated-by-source");
        rendering.Confidence.Should().BeNull();
        rendering.AlsoBy.Should().BeEmpty();
    }

    [Fact]
    public async Task ARenderingTheAlignerAlsoFoundSaysBoth()
    {
        Link(3, 2, (LinkMethod.StatedBySource, null, "a test"), (LinkMethod.Aligner, 0.8, "the aligner"));

        var rendering = (await RenderingsOf(_db.WordAt(_english, 1, 1, 3).Id)).Should().ContainSingle().Subject;

        rendering.Method.Should().Be("stated-by-source");
        rendering.AlsoBy.Should().Equal("aligner");
    }

    /// <summary>
    /// Two links reaching one word are one rendering, and the stated one describes it: the surer
    /// claim is the one a reader should be told first, whichever the database returned first.
    /// </summary>
    [Fact]
    public async Task WhereTwoLinksReachOneWordTheStatedOneSpeaksForIt()
    {
        Link(3, 2, (LinkMethod.Aligner, 0.4, "the aligner"));
        Link(3, 2, (LinkMethod.StatedBySource, null, "a test"));

        var rendering = (await RenderingsOf(_db.WordAt(_english, 1, 1, 3).Id)).Should().ContainSingle().Subject;

        rendering.Method.Should().Be("stated-by-source");
    }

    [Fact]
    public void AgreementIsReportedWhereEnoughWordsWereChecked()
    {
        using var measures = JsonDocument.Parse(
            """
            {"contention": [
              {"text": "RUSV", "against": "NESTLE1904", "disputed": 9667, "contended": 161, "corroborated": 95560},
              {"text": "ASV", "against": "BHSA", "disputed": 378, "contended": 13145, "corroborated": 0},
              {"text": "KJV", "against": "RP2018", "disputed": 0, "contended": 0, "corroborated": 0}
            ]}
            """);

        LinkCheckEndpoints.Checks(measures.RootElement)
            .Should().Equal(new LinkCheckResponse("RUSV", "NESTLE1904", 105227, 95560));
    }

    [Fact]
    public void AReportWithoutContentionChecksNothing()
    {
        using var measures = JsonDocument.Parse("""{"reach": []}""");

        LinkCheckEndpoints.Checks(measures.RootElement).Should().BeEmpty();
    }

    private async Task<IList<WordRenderingResponse>> RenderingsOf(long wordId) =>
        await WordEndpoints.Renderings(_db, await WordEndpoints.Linked(_db, wordId, default), default);

    private void Link(int englishPosition, int hebrewPosition, params (LinkMethod Method, double? Confidence, string Source)[] claims)
    {
        var (method, confidence, source) = claims[0];
        var link = new Link
        {
            FromTextId = _english.Id,
            ToTextId = _hebrew.Id,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = confidence,
            Provenance = new() { Source = source },
        };
        _db.Links.Add(link);
        _db.SaveChanges();

        foreach (var claim in claims)
        {
            _db.LinkClaims.Add(new LinkClaim
            {
                LinkId = link.Id,
                Method = claim.Method,
                Confidence = claim.Confidence,
                Provenance = new() { Source = claim.Source },
            });
        }

        _db.LinkWords.Add(new LinkWord
        {
            LinkId = link.Id, WordId = _db.WordAt(_english, 1, 1, englishPosition).Id, Side = LinkSide.From,
        });
        _db.LinkWords.Add(new LinkWord
        {
            LinkId = link.Id, WordId = _db.WordAt(_hebrew, 1, 1, hebrewPosition).Id, Side = LinkSide.To,
        });
        _db.SaveChanges();
    }
}
