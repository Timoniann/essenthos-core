using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The lexicon's phrases, counted by the load from the links of the translation the cards quote, and
/// counted again whenever the links say something else.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class StrongRenderingLoaderTests : IDisposable
{
    private const string God = "H430";
    private const string Create = "H1254";

    private readonly AppDbContext _db;
    private readonly StrongRenderingLoader _loader;
    private readonly Text _hebrew;
    private readonly Text _english;

    public StrongRenderingLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new StrongRenderingLoader(_db, NullLogger<StrongRenderingLoader>.Instance);

        _hebrew = Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo", (1, 1, ["בָּרָא", "אֱלֹהִים", "אֱלֹהֶיךָ"]));
        _english = Corpus.Add(_db, StrongRenderingCounts.CardTranslation, TextKind.Translation, "en",
            (1, 1, ["created", "God", "thy", "God"]));
        _db.SaveChanges();

        _db.WordAt(_hebrew, 1, 1, 1).StrongNumber = Create;
        _db.WordAt(_hebrew, 1, 1, 2).StrongNumber = God;
        _db.WordAt(_hebrew, 1, 1, 3).StrongNumber = God;
        Renders([1], [1]);
        Renders([2], [2]);
        Renders([3], [3, 4]);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM text");

    private void Renders(int[] hebrew, int[] english)
    {
        var link = new Link
        {
            FromTextId = _english.Id, ToTextId = _hebrew.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        foreach (var position in english)
        {
            _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(_english, 1, 1, position), Side = LinkSide.From });
        }

        foreach (var position in hebrew)
        {
            _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(_hebrew, 1, 1, position), Side = LinkSide.To });
        }
    }

    private async Task<List<(string, int, string, int)>> Kept() =>
        (await _db.StrongRenderings.AsNoTracking()
            .OrderBy(r => r.StrongNumber).ThenBy(r => r.Rank)
            .Select(r => new { r.StrongNumber, r.Rank, r.Phrase, r.Uses })
            .ToListAsync())
        .Select(r => (r.StrongNumber, r.Rank, r.Phrase, r.Uses))
        .ToList();

    [Fact]
    public async Task EachNumberKeepsItsCommonestPhrases()
    {
        var outcome = await _loader.Load();

        outcome.AlreadyLoaded.Should().BeFalse();
        outcome.Numbers.Should().Be(2);
        (await Kept()).Should().Equal(
            (Create, 1, "created", 1),
            (God, 1, "god", 1),
            (God, 2, "thy god", 1));
    }

    /// <summary>The startup pipeline runs on every load, and a load that counts the same writes nothing.</summary>
    [Fact]
    public async Task ASecondLoadWritesNothing()
    {
        await _loader.Load();
        var ids = await _db.StrongRenderings.Select(r => r.Id).OrderBy(id => id).ToListAsync();

        var again = await _loader.Load();

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.StrongRenderings.Select(r => r.Id).OrderBy(id => id).ToListAsync()).Should().Equal(ids);
    }

    /// <summary>A link withdrawn after the count takes its phrase with it on the next load.</summary>
    [Fact]
    public async Task ALinkWithdrawnTakesItsPhraseWithIt()
    {
        await _loader.Load();
        var thy = await _db.LinkWords.Where(lw => lw.Word!.Surface == "thy").Select(lw => lw.LinkId).SingleAsync();
        await _db.Links.Where(l => l.Id == thy).ExecuteDeleteAsync();

        (await _loader.Load()).AlreadyLoaded.Should().BeFalse();

        (await Kept()).Should().Equal((Create, 1, "created", 1), (God, 1, "god", 1));
    }
}
