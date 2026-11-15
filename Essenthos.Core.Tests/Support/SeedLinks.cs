using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Npgsql;

namespace Essenthos.Core.Tests;

/// <summary>One link a test puts in its database: the two texts, what the link says and who says it, and the words on each side.</summary>
internal sealed record SeedLink(
    int FromTextId,
    int ToTextId,
    LinkRelation Relation,
    LinkMethod Method,
    double? Confidence,
    string Source,
    string? Note,
    IReadOnlyCollection<long> From,
    IReadOnlyCollection<long> To);

/// <summary>
/// Links put into a test database as the corpus holds them, for tests of what reads links (the mapping export, the
/// corpus check). Links over the same words of the same two texts are one link with a claim for each; the strongest
/// claim (<see cref="ClaimStanding"/>), the first among equals, heads it. The build writes links from the mapping's files
/// (<c>mapping-load</c>); this is only a way to seed a test.
/// </summary>
internal static class SeedLinks
{
    public static async Task Write(NpgsqlConnection connection, NpgsqlTransaction? transaction, IReadOnlyList<SeedLink> links)
    {
        var provenance = await ProvenanceIds.Of(connection, transaction, links.Select(link => (link.Source, link.Note)), CancellationToken.None);
        foreach (var shape in links.GroupBy(link => (link.FromTextId, link.ToTextId, Shape: LinkShape.Of(link.From, link.To))))
        {
            var head = shape.OrderByDescending(link => ClaimStanding.Of(link.Method)).First();
            long id;
            await using (var insert = new NpgsqlCommand(
                """
                INSERT INTO link (from_text_id, to_text_id, relation, method, confidence, provenance_id, fingerprint)
                VALUES (@from, @to, @relation, @method, @confidence, @provenance, @fingerprint) RETURNING id
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("from", head.FromTextId);
                insert.Parameters.AddWithValue("to", head.ToTextId);
                insert.Parameters.AddWithValue("relation", EnumSpelling.Of(head.Relation));
                insert.Parameters.AddWithValue("method", EnumSpelling.Of(head.Method));
                insert.Parameters.AddWithValue("confidence", (object?)head.Confidence ?? DBNull.Value);
                insert.Parameters.AddWithValue("provenance", provenance[(head.Source, head.Note)]);
                insert.Parameters.AddWithValue("fingerprint", shape.Key.Shape);
                id = (long)(await insert.ExecuteScalarAsync())!;
            }

            await using (var words = new NpgsqlCommand(
                "INSERT INTO link_word (link_id, word_id, side) SELECT @link, unnest(@words), unnest(@sides)", connection, transaction))
            {
                long[] named = [.. head.From.Distinct(), .. head.To.Distinct()];
                words.Parameters.AddWithValue("link", id);
                words.Parameters.AddWithValue("words", named);
                words.Parameters.AddWithValue("sides", named.Select((_, at) => EnumSpelling.Of(at < head.From.Distinct().Count() ? LinkSide.From : LinkSide.To)).ToArray());
                await words.ExecuteNonQueryAsync();
            }

            foreach (var claim in shape)
            {
                await using var insert = new NpgsqlCommand(
                    """
                    INSERT INTO link_claim (link_id, method, confidence, provenance_id) VALUES (@link, @method, @confidence, @provenance)
                    ON CONFLICT DO NOTHING
                    """, connection, transaction);
                insert.Parameters.AddWithValue("link", id);
                insert.Parameters.AddWithValue("method", EnumSpelling.Of(claim.Method));
                insert.Parameters.AddWithValue("confidence", (object?)claim.Confidence ?? DBNull.Value);
                insert.Parameters.AddWithValue("provenance", provenance[(claim.Source, claim.Note)]);
                await insert.ExecuteNonQueryAsync();
            }
        }
    }
}
