using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>Writing the renderings of our own lines, and serving them only while they are true.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class DistinguisherLoaderTests : IDisposable
{
    private const string English = "the living beings God placed at the east of the garden of Eden (GEN 3:24)";
    private const string Ukrainian = "живі істоти, яких Бог поставив на схід від саду в Едені (GEN 3:24)";

    private readonly AppDbContext _db;
    private readonly DistinguisherLoader _loader;
    private readonly Entity _cherubim;

    private static readonly DistinguisherFile File = new(
        "a test",
        [
            new DistinguisherRecord("cherubim", English, Ukrainian, "die Lebewesen", "los seres vivientes"),
            new DistinguisherRecord("nobody", "nothing", "ніщо", null, null),
        ]);

    public DistinguisherLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _loader = new DistinguisherLoader(_db, NullLogger<DistinguisherLoader>.Instance);
        _cherubim = new Entity
        {
            Kind = EntityKind.Person,
            Slug = "cherubim",
            Name = "Cherubim",
            Distinguisher = English,
            SourceId = "cherubim",
            Source = "a test",
        };
        _db.Entities.Add(_cherubim);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    [Fact]
    public async Task WritesEachLanguageOnceAndServesTheOneAskedFor()
    {
        var first = await _loader.Load(File, CancellationToken.None);
        var second = await _loader.Load(File, CancellationToken.None);

        first.Written.Should().Be(3);
        first.Missing.Should().Be(1);
        second.Written.Should().Be(0);

        (await EntityDistinguishers.Of(_db, [_cherubim.Id], "ukr", CancellationToken.None))
            .Should().Equal(new Dictionary<int, string> { [_cherubim.Id] = Ukrainian });
        (await EntityDistinguishers.Of(_db, [_cherubim.Id], "eng", CancellationToken.None)).Should().BeEmpty();
    }

    /// <summary>
    /// A line this project wrote for a record a dataset supplied replaces the dataset's English, once, and
    /// is rendered beside the lines of the other file in the same pass.
    /// </summary>
    [Fact]
    public async Task OurOwnLineReplacesTheDatasetsEnglishAndIsRendered()
    {
        const string Ours = "took Kenath and its villages and called it Nobah after his own name (NUM 32:42)";
        var nobah = new Entity
        {
            Kind = EntityKind.Person,
            Slug = "nobah",
            Name = "Nobah",
            Distinguisher = "a Manassite who captured Kenath (NUM 32:42)",
            SourceId = "nobah",
            Source = "BibleData by a test",
        };
        _db.Entities.Add(nobah);
        await _db.SaveChangesAsync();
        var own = new DistinguisherFile(
            "Essenthos, a test",
            [new DistinguisherRecord("nobah", Ours, "здобув Кенат (NUM 32:42)", "nahm Kenat ein (NUM 32:42)", "tomó Kenat (NUM 32:42)")])
        {
            SetsTheLine = true,
        };

        var first = await _loader.Load([File, own], CancellationToken.None);
        var second = await _loader.Load([File, own], CancellationToken.None);

        first.Lined.Should().Be(1);
        first.Written.Should().Be(6);
        second.Lined.Should().Be(0);
        second.Written.Should().Be(0);
        _db.ChangeTracker.Clear();
        (await _db.Entities.SingleAsync(e => e.Slug == "nobah")).Distinguisher.Should().Be(Ours);
        (await EntityDistinguishers.Of(_db, [nobah.Id], "ukr", CancellationToken.None))
            .Should().Equal(new Dictionary<int, string> { [nobah.Id] = "здобув Кенат (NUM 32:42)" });
        (await _db.EntityDistinguishers.SingleAsync(d => d.EntityId == nobah.Id && d.Language == "deu"))
            .Source.Should().Be("Essenthos, a test");
    }

    [Fact]
    public async Task ALineRewrittenSinceItWasRenderedKeepsNoTranslation()
    {
        await _loader.Load(File, CancellationToken.None);

        _cherubim.Distinguisher = "the beings who kept the way of the tree of life (GEN 3:24)";
        await _db.SaveChangesAsync();

        (await EntityDistinguishers.Of(_db, [_cherubim.Id], "ukr", CancellationToken.None))
            .Should().BeEmpty("a translation of what the record used to say is not what it says");

        var reload = await _loader.Load(File, CancellationToken.None);
        reload.Stale.Should().Be(1);
        (await _db.EntityDistinguishers.CountAsync()).Should().Be(0);
    }
}
