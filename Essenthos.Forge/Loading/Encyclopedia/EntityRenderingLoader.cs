using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Words">Named words read.</param>
/// <param name="Renderings">Spellings written, across every text.</param>
/// <param name="Entities">Entities that have at least one.</param>
internal sealed record EntityRenderingOutcome(
    int Words,
    int Renderings,
    int Entities,
    IReadOnlyList<(string Text, int Entities)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        $"{Renderings} spellings of {Entities} entities counted from {Words} named words in {Elapsed}. " +
        "Entities per text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Entities}"));
}

/// <summary>
/// Every text's spellings of every entity's name, counted from the words that name it.
///
/// <para>
/// <strong>Rebuilt whole on every load.</strong> It is a projection of <c>word_entity</c>, which a
/// dozen passes write and several correct after the fact, so a guard asking whether it had already
/// run would keep the spellings of annotations that no longer exist. It reads a quarter of a million
/// rows and writes a few tens of thousands, which is seconds, and it runs last among the passes that
/// name words so that it counts what they settled on.
/// </para>
///
/// <para>
/// <see cref="Renderings"/> is the rule; this reads the words and the nominatives it prefers and
/// writes what it returns, in one transaction so a reader never sees the table half empty.
/// </para>
/// </summary>
internal sealed class EntityRenderingLoader(AppDbContext db, ILogger<EntityRenderingLoader> logger)
{
    private const string Named =
        """
        SELECT a.entity_id, w.text_id, w.verse_id, w.position, w.text, w.trailer, w.lemma, t.language
        FROM word_entity a
        JOIN word w ON w.id = a.word_id
        JOIN text t ON t.id = w.text_id
        """;

    public async Task<EntityRenderingOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var words = await Words(connection, cancellationToken);
        var nominatives = await db.EntityNameForms
            .Where(f => f.GrammaticalCase == GrammaticalCases.Nominative)
            .Select(f => new { f.EntityId, f.Language, f.Form })
            .ToDictionaryAsync(f => (f.EntityId, f.Language), f => f.Form, cancellationToken);

        var renderings = Renderings.Of(words, nominatives).ToList();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.EntityRenderings.ExecuteDeleteAsync(cancellationToken);

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY entity_rendering (entity_id, text_id, form, folded, occurrences, heading) "
                         + "FROM STDIN (FORMAT BINARY)",
                         cancellationToken))
        {
            foreach (var rendering in renderings)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(rendering.EntityId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(rendering.TextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(rendering.Form, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(rendering.Folded, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(rendering.Occurrences, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(rendering.Heading, NpgsqlDbType.Boolean, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var slugs = await db.Texts.ToDictionaryAsync(t => t.Id, t => t.Slug, cancellationToken);
        var byText = renderings
            .Where(r => r.Heading)
            .GroupBy(r => r.TextId)
            .Select(g => (Text: slugs.GetValueOrDefault(g.Key, g.Key.ToString()), Entities: g.Count()))
            .OrderBy(t => t.Text, StringComparer.Ordinal)
            .ToList();

        var outcome = new EntityRenderingOutcome(
            words.Count,
            renderings.Count,
            renderings.Select(r => r.EntityId).Distinct().Count(),
            byText,
            started.Elapsed);
        logger.LogInformation("Counted how each text spells each name: {Outcome}", outcome);
        return outcome;
    }

    private static async Task<List<NamedWord>> Words(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var words = new List<NamedWord>();
        await using var command = new NpgsqlCommand(Named, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            words.Add(new NamedWord(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7)));
        }

        return words;
    }
}
