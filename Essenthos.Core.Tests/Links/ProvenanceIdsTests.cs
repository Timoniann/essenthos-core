using Essenthos.Core.Database;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// One source carrying many notes, as an EVIDENTIA run's every decision does: the ids come back for
/// the pairs already written and for the new ones, a missing note is matched as missing, and asking
/// again writes nothing.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ProvenanceIdsTests(WitnessDatabase database)
{
    private const string Source = "EVIDENTIA run 7 (1.0.0), attached-word";

    [Fact]
    public async Task EveryPairComesBackWithItsOwnRowAndNothingIsWrittenTwice()
    {
        await using var connection = database.NewConnection();
        await connection.OpenAsync();
        await Execute(connection, "TRUNCATE provenance CASCADE");
        var written = Enumerable.Range(0, 500).Select(n => (Source, (string?)$"decision {n}"))
            .Append((Source, null))
            .ToList();
        var first = await ProvenanceIds.Of(connection, null, written, CancellationToken.None);

        var asked = written.Where((_, n) => n % 7 == 0)
            .Concat(Enumerable.Range(500, 20).Select(n => (Source, (string?)$"decision {n}")))
            .Append((Source, null))
            .Append(("another source", null))
            .ToList();
        var second = await ProvenanceIds.Of(connection, null, asked, CancellationToken.None);

        second.Keys.Should().BeEquivalentTo(asked);
        foreach (var pair in asked.Where(first.ContainsKey))
        {
            second[pair].Should().Be(first[pair], $"{pair} was written before");
        }

        second[(Source, null)].Should().Be(first[(Source, null)]);
        await using var context = database.NewContext();
        var stored = await context.Provenances.AsNoTracking().ToListAsync();
        stored.Should().HaveCount(written.Count + 21);
        var byPair = stored.ToDictionary(row => (row.Source, row.Note), row => row.Id);
        second.Should().Equal(asked.ToDictionary(pair => pair, pair => byPair[pair]));
        (await ProvenanceIds.Of(connection, null, asked, CancellationToken.None)).Should().Equal(second);
        (await context.Provenances.CountAsync()).Should().Be(written.Count + 21);
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
