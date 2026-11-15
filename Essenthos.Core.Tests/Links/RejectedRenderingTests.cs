using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Links;

[Collection(WitnessDatabaseCollection.Name)]
public sealed class RejectedRenderingTests : IDisposable
{
    private readonly AppDbContext db;
    private readonly Dictionary<RuledWord, Word> words;
    private static readonly RuledWord Christ = new("RUSV", "1CO 5:5", 15, "Христа");
    private static readonly RuledWord Satan = new("NESTLE1904", "1CO 5:5", 5, "Σατανᾷ");
    private const string Source = "SIL.Machine, aligned as written and as stems and through KJV";

    public RejectedRenderingTests(WitnessDatabase database)
    {
        db = database.NewContext();
        db.Database.ExecuteSqlRaw("DELETE FROM text; DELETE FROM entity");
        words = db.Place(RejectedRenderings.All.SelectMany(r => new[] { r.From, r.To })
            .Concat(new[] { new RuledWord("RUSV", "ROM 16:20", 5, "сатану"),
                new RuledWord("RUSV", "1CO 5:5", 2, "сатане") }));
    }

    public void Dispose()
    {
        db.Database.ExecuteSqlRaw("DELETE FROM text; DELETE FROM entity");
        db.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeldEditionsWithAMissingOrDifferentRuledWordFailClosed(bool different)
    {
        var word = words[Satan];
        if (different) word.Surface = "another reading";
        else db.Words.Remove(word);
        await db.SaveChangesAsync();
        await db.Database.OpenConnectionAsync();
        var locate = () => RejectedRenderings.Locate((NpgsqlConnection)db.Database.GetDbConnection(), CancellationToken.None);
        await locate.Should().ThrowAsync<InvalidDataException>().WithMessage("*no longer names its ruled words*");
    }

    [Fact]
    public async Task ARulingOnAVerseTheCorpusDoesNotHoldIsPassedOver()
    {
        var absent = new RejectedRendering(
            new RuledWord("RUSV", "GEN 50:26", 1, "Христа"), new RuledWord("GRCBRENT", "GEN 50:26", 1, "Σατανᾶν"), "a verse nobody loaded");
        await db.Database.OpenConnectionAsync();

        var found = await RejectedRenderings.Locate((NpgsqlConnection)db.Database.GetDbConnection(), [absent], CancellationToken.None);

        found.Should().BeEmpty();
    }

    [Fact]
    public async Task EachWordOfARejectedPairIsLookedForAtItsOwnTextsAddress()
    {
        var russian = new RuledWord("RUSV", "PSA 9:22", 3, "Христа");
        var greek = new RuledWord("GRCBRENT", "PSA 10:1", 2, "Σατανᾶν");
        var placed = db.Place([russian, greek]);
        await db.SaveChangesAsync();
        await db.Database.OpenConnectionAsync();

        var found = await RejectedRenderings.Locate((NpgsqlConnection)db.Database.GetDbConnection(),
            [new RejectedRendering(russian, greek, "a test")], CancellationToken.None);

        found.Should().Equal((placed[russian].Id, placed[greek].Id));
    }

    /// <summary>
    /// Genesis 14:5 in the Septuagint: the Zuzim are rendered "strong nations", and the aligner paired
    /// the names after them one place off, the Emim's Ὀμμαίους with the Zuzim and Shaveh's Σαυῇ with
    /// Ham. Each of those pairings is found at its words, so no writer and no rulings seed can miss it.
    /// </summary>
    [Fact]
    public async Task TheSeptuagintsGenesisFourteenFivePairingsAreFoundAtTheirWords()
    {
        var genesis = RejectedRenderings.All.Where(r => r.From.Reference == "GEN 14:5").ToList();
        genesis.Should().HaveCount(5);
        await db.Database.OpenConnectionAsync();

        var found = await RejectedRenderings.Locate((NpgsqlConnection)db.Database.GetDbConnection(), genesis, CancellationToken.None);

        found.Should().BeEquivalentTo(genesis.Select(r => (words[r.From].Id, words[r.To].Id)));
    }
}
