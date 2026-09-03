using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// Records that the loader which just wrote a batch of links is the one claiming them.
///
/// `link_claim` arrived with a migration that backfilled one claim per link *that existed when it
/// ran*, and nothing was taught to keep it up. Only <see cref="ClearBibleLinkLoader"/> wrote claims
/// afterwards, so every link written since — the Berean's 403,343, every aligner run, everything
/// reloaded — carried none, and a link two independent sources had both arrived at read as a link
/// one source stated. The agreement measure sat at 4,664 and was measuring the migration.
///
/// <para>
/// **The claim is taken from the link rather than passed in.** At the moment a batch is written the
/// link's own <c>method</c>, <c>confidence</c> and <c>source</c> *are* its single claim, so copying
/// them cannot disagree with them — and a helper that took them as arguments could be called with
/// the wrong ones. The columns on <c>link</c> become a cached view of the strongest claim; this is
/// where the reasoning is kept.
/// </para>
/// </summary>
internal static class LinkClaims
{
    private const string Import =
        """
        INSERT INTO link_claim (link_id, method, confidence, source, note)
        SELECT id, method, confidence, source, note
        FROM link
        WHERE id >= @first AND id < @first + @count
        ON CONFLICT DO NOTHING
        """;

    /// <summary>
    /// One claim for each of the <paramref name="count"/> links written from
    /// <paramref name="firstId"/>. Call it inside the same transaction as the links themselves: a
    /// link with no claim is invisible to the agreement measure, and a claim with no link is refused
    /// by the foreign key, so the two have to arrive together or not at all.
    /// </summary>
    public static async Task Record(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        long firstId,
        int count,
        CancellationToken cancellationToken)
    {
        if (count <= 0)
        {
            return;
        }

        await using var command = new NpgsqlCommand(
            Import, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("first", firstId);
        command.Parameters.AddWithValue("count", (long)count);
        command.CommandTimeout = 600;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
