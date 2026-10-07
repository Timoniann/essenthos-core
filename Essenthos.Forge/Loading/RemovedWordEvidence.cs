using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Glaux;
using Essenthos.Core.Loading.Links;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading;

/// <summary>
/// What may go with a word a loader takes out of a text because the edition does not print it — a
/// chapter number the transcription let in, a heading an editor set above a verse, a word a file
/// printed twice.
///
/// <para>
/// A row deleted from <c>word</c> takes with it, by cascade, everything that stands on it: its
/// annotations, its parsings and Strong numbers, its place in a word group, EVIDENTIA's decisions,
/// reviews and withdrawals about it, its place in every link. Each of those is somebody's statement,
/// and a removal that deletes one silently has decided it was worthless without anyone reading it.
/// So the only thing allowed to go with the word is a link made by a matcher about that word alone:
/// one the statistical aligner or the Greek editions' letter matcher drew, with no other kind of
/// claim on it, where every word on the removed word's side is going too. Anything else refuses, and
/// the caller's transaction leaves the corpus as it was.
/// </para>
///
/// <para>
/// The tables are read from the database's own foreign keys rather than listed here, so a table that
/// comes to point at a word is guarded the day it is added.
/// </para>
/// </summary>
internal static class RemovedWordEvidence
{
    private const string References =
        """
        SELECT c.conrelid::regclass::text, a.attname
        FROM pg_constraint c
        JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY (c.conkey)
        WHERE c.contype = 'f' AND c.confrelid = 'word'::regclass
        ORDER BY 1, 2
        """;

    /// <summary>
    /// The links that go with these words, or a refusal naming what stands on them. Nothing is
    /// changed here; the caller deletes the links and then the words, inside its own transaction.
    /// </summary>
    public static async Task<long[]> Removable(AppDbContext db, IReadOnlyCollection<long> words, CancellationToken token)
    {
        if (words.Count == 0)
        {
            return [];
        }

        var ids = words.ToArray();
        var held = await Referenced(db, ids, token);
        if (held.Count > 0)
        {
            throw new InvalidOperationException(
                $"A word about to be removed carries protected evidence ({string.Join(", ", held)}); nothing changed. "
                + "Review that evidence before removing the word.");
        }

        var links = await db.Links.AsNoTracking()
            .Where(l => l.Words.Any(w => ids.Contains(w.WordId)))
            .Select(l => new
            {
                l.Id,
                l.Method,
                Source = l.Provenance!.Source,
                Claims = l.Claims.Select(c => new { c.Method, Source = c.Provenance!.Source }).ToList(),
                Words = l.Words.Select(w => new { w.WordId, w.Side }).ToList(),
            })
            .ToListAsync(token);

        var protectedLinks = links.Where(link =>
        {
            var sides = link.Words.Where(w => ids.Contains(w.WordId)).Select(w => w.Side).ToHashSet();
            var whole = link.Words.Where(w => sides.Contains(w.Side)).All(w => ids.Contains(w.WordId));
            var aligned = link.Method == LinkMethod.Aligner && link.Claims.All(c => c.Method == LinkMethod.Aligner);
            var matched = link.Method == LinkMethod.Lexical && link.Source == SeptuagintLinkLoader.Source
                          && link.Claims.All(c => c.Method == LinkMethod.Lexical && c.Source == SeptuagintLinkLoader.Source);
            return !whole || !(aligned || matched);
        }).Select(link => link.Id).ToList();

        return protectedLinks.Count > 0
            ? throw new InvalidOperationException(
                $"A word about to be removed stands in {protectedLinks.Count} link(s) with protected source, manual, "
                + "accepted or other words' evidence; nothing changed. Review that evidence before removing the word.")
            : [.. links.Select(link => link.Id)];
    }

    /// <summary>The links, then the words, after <see cref="Removable"/> has allowed them.</summary>
    public static async Task Remove(AppDbContext db, IReadOnlyCollection<long> words, CancellationToken token)
    {
        var links = await Removable(db, words, token);
        await db.Links.Where(l => links.Contains(l.Id)).ExecuteDeleteAsync(token);
        await db.Words.Where(w => words.Contains(w.Id)).ExecuteDeleteAsync(token);
    }

    /// <summary>Every table other than a link's words that holds a row for one of these words, by name.</summary>
    private static async Task<List<string>> Referenced(AppDbContext db, long[] ids, CancellationToken token)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();

        var columns = new List<(string Table, string Column)>();
        await using (var command = new NpgsqlCommand(References, connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                columns.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        var held = new List<string>();
        foreach (var (table, column) in columns.Where(c => !(c.Table == "link_word" && c.Column == "word_id")))
        {
            // A link word headed by a removed word goes with it when it is removed too, and a Strong number
            // read off the word's own lemma is that lemma's projection rather than anybody's statement.
            var others = table switch
            {
                "link_word" => " AND word_id <> ALL (@ids)",
                "word_strong" => $" AND NOT (method = '{EnumSpelling.Of(LinkMethod.Lexical)}' AND source = @lemma)",
                _ => string.Empty,
            };
            await using var command = new NpgsqlCommand(
                $"SELECT EXISTS (SELECT 1 FROM {table} WHERE \"{column}\" = ANY (@ids){others})", connection, transaction);
            command.Parameters.AddWithValue("ids", ids);
            command.Parameters.AddWithValue("lemma", SeptuagintStrongLoader.Source);
            if ((bool)(await command.ExecuteScalarAsync(token))!)
            {
                held.Add($"{table}.{column}");
            }
        }

        return held;
    }
}
