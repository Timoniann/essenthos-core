using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// Which sources a reader is told the links come from, on the sources page and on a text's own page:
/// the claims a dataset makes on links others drew are counted, the verse numbering is counted by the
/// texts it places, and a numbering the corpus uses without naming it is named nowhere.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SourceCreditTests : IAsyncLifetime
{
    private const string Hebrew = "CREDIT-HEBREW";
    private const string Spanish = "CREDIT-SPANISH";

    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;

    public SourceCreditTests(WitnessDatabase database)
    {
        _database = database;
        _db = database.NewContext();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _db.Links.Where(l => l.FromText!.Slug.StartsWith("CREDIT-")).ExecuteDeleteAsync();
        await _db.Texts.Where(t => t.Slug.StartsWith("CREDIT-")).ExecuteDeleteAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task TheSourcesPageCountsClaimsAndTextsAndLeavesTheUnlistedNumberingsOut()
    {
        var (hebrew, spanish) = await Texts();
        var bju = Link(spanish, hebrew, 1, LinkMethod.StrongNumber, Sources.SynodalStrongCredit + ", a test");
        Link(spanish, hebrew, 2, LinkMethod.StrongNumber, Sources.ReinaValeraStrongCredit + ", a test");
        await _db.SaveChangesAsync();

        _db.LinkClaims.Add(new LinkClaim
        {
            LinkId = bju.Id,
            Method = LinkMethod.Lexical,
            Confidence = 0.8,
            Provenance = new() { Source = Essenthos.Core.Loading.Links.OldTestamentLinkLoader.StatedSource, Note = "a test" },
        });
        await _db.SaveChangesAsync();

        var tally = await DatasetEndpoints.Count(_db, CancellationToken.None);
        var counted = tally.Counted.ToDictionary(row => row.Dataset.Id, row => row.Counts);

        counted["tahot"].Claims.Should().Be(1);
        counted["tvtms"].Texts.Should().Be(await _db.Texts.CountAsync());
        counted["bju-synodal-strong"].Links.Should().Be(1);
        counted.Should().NotContainKey("gomez-rv1909-strong");
        tally.Undeclared.Should().NotContain(row => row.Source.StartsWith(Sources.ReinaValeraStrongCredit));
    }

    [Fact]
    public async Task ATextsPageCreditsTheListedNumberingAndNotTheUnlistedOne()
    {
        var (hebrew, spanish) = await Texts();
        Link(spanish, hebrew, 1, LinkMethod.StrongNumber, Sources.SynodalStrongCredit + ", a test");
        Link(spanish, hebrew, 2, LinkMethod.StrongNumber, Sources.ReinaValeraStrongCredit + ", a test");
        await _db.SaveChangesAsync();

        var tally = await new TextFacts(Scopes()).Of(spanish.Id, CancellationToken.None);

        var bju = Datasets.All.Single(dataset => dataset.Id == "bju-synodal-strong");
        tally.Links.Should().ContainSingle().Which.StatedBy.Should().Equal(new TextCreditResponse(bju.Name, bju.Author));
    }

    private async Task<(Text Hebrew, Text Spanish)> Texts()
    {
        var hebrew = Corpus.Add(_db, Hebrew, TextKind.CriticalEdition, "hbo", (1, 1, ["בְּרֵאשִׁית", "בָּרָא"]));
        var spanish = Corpus.Add(_db, Spanish, TextKind.Translation, "spa", (1, 1, ["En", "el"]));
        await _db.SaveChangesAsync();
        return (hebrew, spanish);
    }

    private Link Link(Text from, Text to, int position, LinkMethod method, string source)
    {
        var link = new Link
        {
            FromTextId = from.Id,
            ToTextId = to.Id,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = 0.9,
            Provenance = new() { Source = source },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(from, 1, 1, position), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(to, 1, 1, position), Side = LinkSide.To });
        return link;
    }

    private IServiceScopeFactory Scopes() =>
        new ServiceCollection()
            .AddScoped(_ => _database.NewContext())
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
}
