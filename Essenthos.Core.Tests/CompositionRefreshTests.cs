using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A composition drawn before its middle text was linked where it is now. The American Standard
/// Version was composed to BHSA through the King James before the King James's psalm titles had any
/// link to the Hebrew, so its titles reached no Hebrew word, and a load replayed the composition only
/// for a pair with no links at all.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CompositionRefreshTests : IDisposable
{
    private const string Mapping = "a word-level mapping, a test";
    private const string Names = "the names of the verse, paired by spelling and order";

    private readonly AppDbContext _db;
    private readonly CompositionPipeline _composer;
    private readonly Text _american;
    private readonly Text _kingJames;
    private readonly Text _hebrew;

    public CompositionRefreshTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _composer = new CompositionPipeline(
            _db,
            new AlignmentPipeline(_db, NullLogger<AlignmentPipeline>.Instance),
            NullLogger<CompositionPipeline>.Instance);

        _american = Corpus.Add(_db, "ASV", TextKind.Translation, "eng",
            (52, 1, ["when", "Doeg", "came"]), (52, 2, ["thy", "tongue"]));
        _kingJames = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (52, 1, ["when", "Doeg", "came"]), (52, 2, ["thy", "tongue"]));
        _hebrew = Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo",
            (52, 1, ["בְּבֹוא", "דֹּואֵג"]), (52, 2, ["לְשֹׁונֶךָ"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    [Fact]
    public async Task OnlyTheVersesWhereTheMiddleTextGainedLinksAreStale()
    {
        Link(_kingJames, Word(_kingJames, 2, 2), Word(_hebrew, 2, 1), LinkMethod.StatedBySource, null, Mapping);
        Link(_american, Word(_american, 2, 2), Word(_hebrew, 2, 1), LinkMethod.Aligner, 0.8, "SIL.Machine, aligned through KJV");
        Link(_kingJames, Word(_kingJames, 1, 2), Word(_hebrew, 1, 2), LinkMethod.StatedBySource, null, Mapping);

        var stale = await Stale(daughter: false);

        stale.Should().Equal(_db.VerseAt(_american, 52, 1).Id);
    }

    [Fact]
    public async Task APairNeverComposedAndOneComposedSinceAreNotStale()
    {
        Link(_kingJames, Word(_kingJames, 1, 2), Word(_hebrew, 1, 2), LinkMethod.StatedBySource, null, Mapping);
        (await Stale(daughter: false)).Should().BeEmpty("a pair with no composition is drawn whole");

        Link(_american, Word(_american, 1, 2), Word(_hebrew, 1, 2), LinkMethod.Aligner, 0.8, "SIL.Machine, aligned through KJV");
        (await Stale(daughter: false)).Should().BeEmpty();
    }

    [Fact]
    public async Task ALinkTheMiddleTextOnlyGuessedCountsForADaughterAlone()
    {
        Link(_american, Word(_american, 2, 2), Word(_hebrew, 2, 1), LinkMethod.Aligner, 0.8, "SIL.Machine, aligned through KJV");
        Link(_kingJames, Word(_kingJames, 1, 2), Word(_hebrew, 1, 2), LinkMethod.Aligner, 0.9, "SIL.Machine, aligned as stems");

        (await Stale(daughter: false)).Should().BeEmpty();
        (await Stale(daughter: true)).Should().Equal(_db.VerseAt(_american, 52, 1).Id);
    }

    [Fact]
    public async Task ARedrawReplacesTheCompositionInItsVersesAndLeavesTheRest()
    {
        var elsewhere = Link(_american, Word(_american, 2, 2), Word(_hebrew, 2, 1), LinkMethod.Aligner, 0.8,
            "SIL.Machine, aligned through KJV");
        Link(_american, Word(_american, 1, 1), Word(_hebrew, 1, 1), LinkMethod.Aligner, 0.5, "SIL.Machine, aligned as stems");
        var named = Link(_american, Word(_american, 1, 3), Word(_hebrew, 1, 1), LinkMethod.Aligner, 0.9, Names);

        var (fresh, _) = await Write(
            new HashSet<int> { _db.VerseAt(_american, 52, 1).Id },
            new RoutedLink(Word(_american, 1, 2).Id, Word(_hebrew, 1, 2).Id, 0.95, Route.Composed),
            new RoutedLink(Word(_american, 2, 1).Id, Word(_hebrew, 2, 1).Id, 0.95, Route.Composed));

        fresh.Should().Be(1, "the second verse is not being redrawn");
        var links = await _db.Links.AsNoTracking().Include(link => link.Words).Include(link => link.Provenance)
            .Where(link => link.FromTextId == _american.Id).ToListAsync();
        links.Select(link => link.Id).Should().Contain([elsewhere.Id, named.Id]);
        links.Should().NotContain(link => link.Words.Any(word => word.WordId == Word(_american, 1, 1).Id));
        links.Should().ContainSingle(link => link.Words.Any(word => word.WordId == Word(_american, 1, 2).Id))
            .Which.Words.Select(word => word.WordId).Should().BeEquivalentTo([Word(_american, 1, 2).Id, Word(_hebrew, 1, 2).Id]);
    }

    [Fact]
    public async Task TheLoadReplaysAStaleCompositionAsARedrawAndAFreshOneNotAtAll()
    {
        Link(_american, Word(_american, 2, 2), Word(_hebrew, 2, 1), LinkMethod.Aligner, 0.8, "SIL.Machine, aligned through KJV");
        var step = new RecipeStep("compose", ["ASV", "KJV,BSB", "BHSA"]);

        await _db.Database.OpenConnectionAsync();
        try
        {
            var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
            var fresh = await Recipe.Replayed(connection, step, CancellationToken.None);
            fresh.Should().Be(step);
            (await Recipe.AlreadyThere(connection, fresh, CancellationToken.None)).Should().NotBeNull();

            Link(_kingJames, Word(_kingJames, 1, 2), Word(_hebrew, 1, 2), LinkMethod.StatedBySource, null, Mapping);
            var stale = await Recipe.Replayed(connection, step, CancellationToken.None);
            stale.ToString().Should().Be("compose ASV KJV,BSB BHSA --refresh");
            (await Recipe.AlreadyThere(connection, stale, CancellationToken.None)).Should().BeNull();
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }

        Recipe.Records(["compose", "ASV", "KJV,BSB", "BHSA", "--refresh"]).Should().BeFalse();
    }

    private async Task<List<int>> Stale(bool daughter)
    {
        await _db.Database.OpenConnectionAsync();
        try
        {
            return [.. await CompositionPipeline.Stale(
                (NpgsqlConnection)_db.Database.GetDbConnection(), "ASV", ["KJV"], "BHSA", daughter, CancellationToken.None)];
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }

    private async Task<(int Fresh, int Corroborated)> Write(IReadOnlySet<int> verses, params RoutedLink[] merged)
    {
        await _db.Database.OpenConnectionAsync();
        try
        {
            return await _composer.Write(
                (NpgsqlConnection)_db.Database.GetDbConnection(), _american, _hebrew, ["KJV"], merged,
                CancellationToken.None, verses: verses);
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }

    private Word Word(Text text, int verse, int position) => _db.WordAt(text, 52, verse, position);

    private Link Link(Text from, Word source, Word target, LinkMethod method, double? confidence, string provenance)
    {
        var link = new Link
        {
            FromTextId = from.Id,
            ToTextId = _hebrew.Id,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = confidence,
            Provenance = new() { Source = provenance },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = source, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = target, Side = LinkSide.To });
        _db.SaveChanges();
        return link;
    }
}
