using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// A second answer on links somebody else already wrote. A loader's own claim on the links it writes
/// arrives with them, from <see cref="LinkWriter"/>; this is for a source that reached the same place
/// independently and is by definition not what the link's own columns say.
/// </summary>
internal static class LinkClaims
{
    /// <summary>
    /// A second answer on links that already have one: another source, asked independently, that
    /// agrees. Two sources agreeing is the whole point of the table — a link with two claims is one
    /// the corroboration measure can see — and it is thrown away by writing a second <em>link</em>
    /// instead, which reads as two facts about the same words.
    ///
    /// <para>
    /// The claim is passed in here rather than copied from the link, because it is by definition not
    /// what the link says: the link records the method and source that established it, and this
    /// records a different one that reached the same place. A claim is unique on
    /// (link, method, source and note), so calling this twice with the same source adds nothing.
    /// </para>
    /// </summary>
    public static async Task Corroborate(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        IReadOnlyCollection<long> linkIds,
        LinkMethod method,
        double? confidence,
        string source,
        string note,
        CancellationToken cancellationToken)
    {
        if (linkIds.Count == 0)
        {
            return;
        }

        var npgsqlTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        await using var command = new NpgsqlCommand(Corroboration, connection, npgsqlTransaction);
        command.Parameters.AddWithValue("ids", linkIds as long[] ?? [.. linkIds]);
        command.Parameters.AddWithValue("method", EnumSpelling.Of(method));
        command.Parameters.AddWithValue("confidence", (object?)confidence ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "provenance", await ProvenanceIds.Of(connection, npgsqlTransaction, source, note, cancellationToken));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// The same second answer, where each link's claim carries a confidence and a source of its
    /// own rather than one shared by the batch.
    ///
    /// <para>
    /// The alignment routes need it. A pair is reached as written, as stems, through a third text
    /// or by several of those at once, and which of them found it is written into the source, so a
    /// run's corroborations are not one claim repeated — they are one claim per link, and giving
    /// them a single source would throw away the one thing the source is there to say. The
    /// confidence differs for the same reason: it is what that pair scored, not what the run did.
    /// </para>
    /// </summary>
    public static async Task Corroborate(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        IReadOnlyList<(long Link, double Confidence, string Source)> claims,
        LinkMethod method,
        string note,
        CancellationToken cancellationToken)
    {
        if (claims.Count == 0)
        {
            return;
        }

        var npgsqlTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        var provenance = await ProvenanceIds.Of(
            connection, npgsqlTransaction, claims.Select(claim => (claim.Source, (string?)note)), cancellationToken);
        await using var command = new NpgsqlCommand(EachCorroboration, connection, npgsqlTransaction);
        command.Parameters.AddWithValue("ids", claims.Select(claim => claim.Link).ToArray());
        command.Parameters.AddWithValue("confidences", claims.Select(claim => claim.Confidence).ToArray());
        command.Parameters.AddWithValue(
            "provenances", claims.Select(claim => provenance[(claim.Source, note)]).ToArray());
        command.Parameters.AddWithValue("method", EnumSpelling.Of(method));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string Corroboration =
        """
        INSERT INTO link_claim (link_id, method, confidence, provenance_id)
        SELECT id, @method, @confidence, @provenance FROM unnest(@ids) AS id
        ON CONFLICT DO NOTHING
        """;

    private const string EachCorroboration =
        """
        INSERT INTO link_claim (link_id, method, confidence, provenance_id)
        SELECT claim.link_id, @method, claim.confidence, claim.provenance_id
        FROM unnest(@ids, @confidences, @provenances) AS claim(link_id, confidence, provenance_id)
        ON CONFLICT DO NOTHING
        """;
}
