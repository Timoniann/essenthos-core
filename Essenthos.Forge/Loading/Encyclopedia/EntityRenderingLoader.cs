using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
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
/// <strong>Counted whole on every load.</strong> It is a projection of <c>word_entity</c>, which a
/// dozen passes write and several correct after the fact, so a guard asking whether it had already
/// run would keep the spellings of annotations that no longer exist. It reads a quarter of a million
/// rows, which is seconds, writes only the spellings that changed, and runs last among the passes
/// that name words so that it counts what they settled on.
/// </para>
///
/// <para>
/// <see cref="Renderings"/> is the rule; this reads the words and the nominatives it prefers and
/// brings the table to what it returns, in one transaction so a reader never sees it half written.
/// </para>
/// </summary>
internal sealed class EntityRenderingLoader(AppDbContext db, ILogger<EntityRenderingLoader> logger)
{
    /// <summary>
    /// Every named word, and for a translation's word the lemma of an original word it is linked to
    /// that names the same entity — which of the entity's names in the original it translates.
    /// And how sure the annotation is where an aligner's link carried it, which decides whether the
    /// spelling it gives is believed, and whether the entity is a thing. A word its language never
    /// names anybody with is no spelling of anything (<see cref="Annotating.NeverAName"/>).
    /// </summary>
    private const string Named =
        $"""
        SELECT a.entity_id, hw.text_id, hw.verse_id, hw.position, hw.text, hw.trailer, hw.lemma, ht.language,
               CASE WHEN ht.language = ANY(@originals) THEN NULL ELSE (
                   SELECT min(theirs_word.lemma)
                   FROM link_word mine
                   JOIN link_word theirs ON theirs.link_id = mine.link_id AND theirs.side <> mine.side
                   JOIN word theirs_word ON theirs_word.id = theirs.word_id
                   JOIN text theirs_text ON theirs_text.id = theirs_word.text_id
                        AND theirs_text.language = ANY(@originals)
                   JOIN word_entity same ON same.word_id = theirs_word.id AND same.entity_id = a.entity_id
                   WHERE mine.word_id = hw.id) END AS renders,
               CASE WHEN a.note LIKE @guessed THEN a.confidence END AS guess,
               e.kind IN ('object', 'observance', 'title') AS thing
        FROM word_entity a
        JOIN entity e ON e.id = a.entity_id
        JOIN word hw ON hw.id = a.word_id
        JOIN text ht ON ht.id = hw.text_id
        WHERE NOT {Annotating.NeverAName}
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

        // Only what changed is written, so a load that counts the same spellings leaves the rows as
        // they are, ids and all.
        var standing = await db.EntityRenderings
            .AsNoTracking()
            .Select(r => new { r.Id, Rendering = new Rendering(r.EntityId, r.TextId, r.Form, r.Folded, r.Occurrences, r.Heading) })
            .ToListAsync(cancellationToken);
        var wanted = renderings.ToHashSet();
        var gone = standing.Where(r => !wanted.Remove(r.Rendering)).Select(r => r.Id).ToList();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.EntityRenderings.Where(r => gone.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken);

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY entity_rendering (entity_id, text_id, form, folded, occurrences, heading) "
                         + "FROM STDIN (FORMAT BINARY)",
                         cancellationToken))
        {
            foreach (var rendering in renderings.Where(wanted.Contains))
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
        command.Parameters.AddWithValue("originals", Renderings.Original.ToArray());
        command.Parameters.AddWithValue("guessed", $"{Annotating.CarriedNote}, linked by {EnumSpelling.Of(LinkMethod.Aligner)}");
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
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9),
                reader.GetBoolean(10)));
        }

        return words;
    }
}
