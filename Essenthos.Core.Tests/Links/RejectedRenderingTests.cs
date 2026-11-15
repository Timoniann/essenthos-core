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

    [Fact]
    public async Task ARejectedStatisticalChristToSatanPairCannotBeWrittenAgain()
    {
        await db.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync();
        var draft = new NewLink(words[Christ].TextId, words[Satan].TextId, LinkRelation.Renders,
            LinkMethod.Aligner, 0.98, Source, null, [words[Christ].Id], [words[Satan].Id]);
        var write = () => LinkWriter.Write(connection, transaction, [draft], CancellationToken.None);
        await write.Should().ThrowAsync<InvalidDataException>().WithMessage("*rejected rendering*");
    }

    [Fact]
    public async Task AdmissionRejectsEveryListedPairingAndItsReverseButKeepsOtherCorrespondences()
    {
        await db.Database.OpenConnectionAsync();
        var rejected = RejectedRenderings.All.Select(r => Draft(words[r.From], words[r.To])).ToArray();
        var reverse = rejected.Select(d => d with { FromTextId = d.ToTextId, ToTextId = d.FromTextId,
            From = d.To, To = d.From }).ToArray();
        var correct = Draft(words[new("RUSV", "1CO 5:5", 2, "сатане")], words[Satan]);
        var sourced = rejected[0] with { Method = LinkMethod.StatedBySource, Confidence = null, Source = "an independent source" };
        var manual = rejected[0] with { Method = LinkMethod.Manual, Confidence = null, Source = "a person's reading" };
        var phrase = rejected[0] with { From = [.. rejected[0].From, correct.From.Single()] };
        var admitted = await RejectedRenderings.Admitted((NpgsqlConnection)db.Database.GetDbConnection(),
            [.. rejected, .. reverse, correct, sourced, manual, phrase], CancellationToken.None);
        admitted.Should().Equal(correct, sourced, manual, phrase);
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

    private static NewLink Draft(Word from, Word to) => new(from.TextId, to.TextId, LinkRelation.Renders,
        LinkMethod.Aligner, 0.98, Source, null, [from.Id], [to.Id]);
}
