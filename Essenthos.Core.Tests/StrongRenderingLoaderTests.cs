using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The lexicon's phrases and each number's reach, counted by the load from the links of every text
/// linked to an original, and counted again whenever the links say something else.
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

    private void Renders(int[] hebrew, int[] english, Text? translation = null)
    {
        translation ??= _english;
        var link = new Link
        {
            FromTextId = translation.Id, ToTextId = _hebrew.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        foreach (var position in english)
        {
            _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(translation, 1, 1, position), Side = LinkSide.From });
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

    /// <summary>
    /// A Forge run that changed one text's links outside a load counts that text again as it ends, and
    /// what it leaves is what a fresh count of every text would: the next count finds nothing to write.
    /// A text it did not name keeps its rows as they were.
    /// </summary>
    [Fact]
    public async Task ARunCountsAgainTheTextItRelinkedAndLeavesWhatAFreshCountWould()
    {
        var other = Corpus.Add(_db, "OTHER", TextKind.Translation, "en", (1, 1, ["made", "Gods"]));
        _db.SaveChanges();
        Renders([1], [1], other);
        Renders([2], [2], other);
        _db.SaveChanges();
        await _loader.Load();
        var others = await OtherRows(other);
        var thy = await _db.LinkWords.Where(lw => lw.Word!.Surface == "thy").Select(lw => lw.LinkId).SingleAsync();
        await _db.Links.Where(l => l.Id == thy).ExecuteDeleteAsync();

        var recounted = await _loader.Load(Relinked.Of("kjv"));

        recounted.AlreadyLoaded.Should().BeFalse();
        recounted.Texts.Should().Be(1);
        (await _db.StrongRenderings.Where(r => r.TextId == _english.Id && r.StrongNumber == God).Select(r => r.Phrase).ToListAsync())
            .Should().Equal("god");
        (await _db.StrongReaches.Where(r => r.TextId == _english.Id && r.StrongNumber == God).Select(r => r.Reached).SingleAsync())
            .Should().Be(1);
        (await OtherRows(other)).Should().Equal(others);
        (await _loader.Load()).AlreadyLoaded.Should().BeTrue("the run left what counting every text would");
    }

    [Fact]
    public async Task ARunThatChangedNothingCountsNothing()
    {
        await _loader.Load();
        var ids = await _db.StrongRenderings.Select(r => r.Id).OrderBy(id => id).ToListAsync();

        (await _loader.Load(Relinked.None)).Texts.Should().Be(0);
        (await _loader.Load(Relinked.Run(-1))).Texts.Should().Be(0);

        (await _db.StrongRenderings.Select(r => r.Id).OrderBy(id => id).ToListAsync()).Should().Equal(ids);
    }

    private Task<List<int>> OtherRows(Text other) =>
        _db.StrongRenderings.Where(r => r.TextId == other.Id).Select(r => r.Id).OrderBy(id => id).ToListAsync();

    /// <summary>The entry page answers from what the load counted exactly as it answers counting as it is asked.</summary>
    [Fact]
    public async Task TheEntryPageReadsWhatItWouldHaveCounted()
    {
        var asked = await Page(God);

        await _loader.Load();

        (await _db.StrongReaches.Select(r => new { r.StrongNumber, r.Occurrences, r.Reached }).OrderBy(r => r.StrongNumber).ToListAsync())
            .Should().Equal(new { StrongNumber = Create, Occurrences = 1, Reached = 1 }, new { StrongNumber = God, Occurrences = 2, Reached = 2 });
        (await Page(God)).Should().BeEquivalentTo(asked, options => options.WithStrictOrdering());
    }

    /// <summary>What the load counted is what the page says, so a count it kept is never counted again.</summary>
    [Fact]
    public async Task TheEntryPageAnswersFromTheCount()
    {
        await _loader.Load();
        await _db.StrongReaches.Where(r => r.StrongNumber == God).ExecuteUpdateAsync(r => r.SetProperty(x => x.Reached, 1));

        var page = await Page(God);

        page.Reached.Should().Be(1);
        page.Unrendered.Should().Be(1);
        page.Methods.Should().Equal(new TextLinkMethodResponse("stated-by-source", 2));
    }

    private Task<StrongRenderingsResponse> Page(string number) =>
        StrongEndpoints.RenderingsOf(_db, number, _english.Id, _english.Slug, 40, default);
}
