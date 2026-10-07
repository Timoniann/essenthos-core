using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading.Links;

internal sealed record RejectedRendering(RuledWord From, RuledWord To, string Reason);

internal sealed record RejectedRenderingOutcome(int Links, int Annotations)
{
    public override string ToString() => $"{Links} rejected statistical renderings and {Annotations} annotations carried solely over them withdrawn";
}

/// <summary>Specific statistical pairings contradicted by the held editions' wording.</summary>
internal static class RejectedRenderings
{
    public static IReadOnlyList<RejectedRendering> All { get; } =
    [
        new(new("RUSV", "ROM 16:20", 14, "Христа"), new("NESTLE1904", "ROM 16:20", 8, "Σατανᾶν"),
            "Codex, 2026-10-05: the Synodal names Jesus Christ here; Satan is separately named at position 5."),
        new(new("RUSV", "1CO 5:5", 15, "Христа"), new("NESTLE1904", "1CO 5:5", 5, "Σατανᾷ"),
            "Codex, 2026-10-05: the Synodal adds Christ after Jesus; Satan is separately named at position 2."),
        new(new("RUSV", "1CO 5:5", 15, "Христа"), new("TR1894", "1CO 5:5", 5, "σατανα"),
            "Codex, 2026-10-05: Scrivener omits Christ here and names Satan earlier in the verse."),
        new(new("RUSV", "1CO 5:5", 15, "Христа"), new("KJV", "1CO 5:5", 7, "Satan"),
            "Claude, 2026-10-06: the King James's Satan is the Synodal's сатане at position 2; Христа is Christ."),
    ];

    /// <summary>
    /// Both words of every rejected pair, each at the address its own text numbers it by: the two
    /// texts need not number a verse alike, so the second word is never looked for at the first's.
    /// </summary>
    private const string Located =
        """
        SELECT x.n, ft.id, tt.id, f.id, f.text, z.id, z.text
        FROM unnest(@from_texts, @from_books, @from_chapters, @from_verses, @from_labels, @from_positions,
                    @to_texts, @to_books, @to_chapters, @to_verses, @to_labels, @to_positions)
             WITH ORDINALITY x(fs, fb, fc, fv, fl, fp, ts, tb, tc, tv, tl, tp, n)
        LEFT JOIN text ft ON ft.slug = x.fs LEFT JOIN text tt ON tt.slug = x.ts
        LEFT JOIN book fbk ON fbk.text_id = ft.id AND fbk.canonical_ordinal = x.fb
        LEFT JOIN book tbk ON tbk.text_id = tt.id AND tbk.canonical_ordinal = x.tb
        LEFT JOIN verse fve ON fve.book_id = fbk.id AND fve.chapter_number = x.fc AND fve.number = x.fv AND fve.label = x.fl
        LEFT JOIN verse tve ON tve.book_id = tbk.id AND tve.chapter_number = x.tc AND tve.number = x.tv AND tve.label = x.tl
        LEFT JOIN word f ON f.verse_id = fve.id AND f.position = x.fp
        LEFT JOIN word z ON z.verse_id = tve.id AND z.position = x.tp
        """;

    internal static async Task<HashSet<(long From, long To)>> Locate(
        NpgsqlConnection connection, CancellationToken cancellationToken) =>
        await Locate(connection, All, cancellationToken);

    internal static async Task<HashSet<(long From, long To)>> Locate(
        NpgsqlConnection connection, IReadOnlyList<RejectedRendering> rejected, CancellationToken cancellationToken)
    {
        var from = rejected.Select(r => r.From.Address()
            ?? throw new InvalidDataException($"The rejected rendering names no verse: {r.From}.")).ToArray();
        var to = rejected.Select(r => r.To.Address()
            ?? throw new InvalidDataException($"The rejected rendering names no verse: {r.To}.")).ToArray();
        await using var command = new NpgsqlCommand(Located, connection);
        command.Parameters.AddWithValue("from_texts", rejected.Select(r => r.From.Text).ToArray());
        command.Parameters.AddWithValue("from_books", from.Select(a => a.Book).ToArray());
        command.Parameters.AddWithValue("from_chapters", from.Select(a => a.Chapter).ToArray());
        command.Parameters.AddWithValue("from_verses", from.Select(a => a.Verse).ToArray());
        command.Parameters.AddWithValue("from_labels", from.Select(a => a.Label).ToArray());
        command.Parameters.AddWithValue("from_positions", rejected.Select(r => r.From.Position).ToArray());
        command.Parameters.AddWithValue("to_texts", rejected.Select(r => r.To.Text).ToArray());
        command.Parameters.AddWithValue("to_books", to.Select(a => a.Book).ToArray());
        command.Parameters.AddWithValue("to_chapters", to.Select(a => a.Chapter).ToArray());
        command.Parameters.AddWithValue("to_verses", to.Select(a => a.Verse).ToArray());
        command.Parameters.AddWithValue("to_labels", to.Select(a => a.Label).ToArray());
        command.Parameters.AddWithValue("to_positions", rejected.Select(r => r.To.Position).ToArray());
        var found = new HashSet<(long, long)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var ruling = rejected[(int)reader.GetInt64(0) - 1];
            if (reader.IsDBNull(1) || reader.IsDBNull(2)) continue;
            if (reader.IsDBNull(3) || reader.IsDBNull(5)
                || reader.GetString(4) != ruling.From.Surface || reader.GetString(6) != ruling.To.Surface)
                throw new InvalidDataException($"The rejected rendering no longer names its ruled words: {ruling.From}; {ruling.To}.");
            found.Add((reader.GetInt64(3), reader.GetInt64(5)));
        }
        return found;
    }

    private static bool Statistical(NewLink draft) => draft.Method == LinkMethod.Aligner
        && draft.Relation == LinkRelation.Renders && draft.Source.StartsWith("SIL.Machine", StringComparison.Ordinal);

    internal static async Task<IReadOnlyList<NewLink>> Admitted(
        NpgsqlConnection connection, IReadOnlyList<NewLink> drafts, CancellationToken cancellationToken)
    {
        if (!drafts.Any(Statistical)) return drafts;
        var rejected = await Locate(connection, cancellationToken);
        return drafts.Where(draft => !Statistical(draft) || draft.From.Count != 1 || draft.To.Count != 1
            || !(rejected.Contains((draft.From.Single(), draft.To.Single()))
                 || rejected.Contains((draft.To.Single(), draft.From.Single())))).ToArray();
    }

    public static async Task<RejectedRenderingOutcome> Withdraw(AppDbContext db, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var pairs = await Locate((NpgsqlConnection)db.Database.GetDbConnection(), cancellationToken);
        var shapes = pairs.Select(p => LinkShape.Of([p.From], [p.To])).ToArray();
        var links = await db.Links.AsNoTracking().Include(l => l.Provenance)
            .Include(l => l.Claims).ThenInclude(c => c.Provenance)
            .Include(l => l.Words)
            .Where(l => l.Fingerprint != null && shapes.Contains(l.Fingerprint.Value))
            .ToListAsync(cancellationToken);
        foreach (var link in links)
        {
            if (link.Words.Count != 2 || !pairs.Contains((
                    link.Words.Single(w => w.Side == LinkSide.From).WordId,
                    link.Words.Single(w => w.Side == LinkSide.To).WordId))) continue;
            if (link.Method != LinkMethod.Aligner || link.Relation != LinkRelation.Renders
                || !link.Provenance!.Source.StartsWith("SIL.Machine", StringComparison.Ordinal)
                || link.Claims.Any(c => c.Method != LinkMethod.Aligner
                    || !c.Provenance!.Source.StartsWith("SIL.Machine", StringComparison.Ordinal)))
                throw new InvalidDataException($"Protected evidence stands on a rejected rendering ({link.Id}); no links were withdrawn.");
        }
        var ids = links.Where(link => link.Words.Count == 2 && pairs.Contains((
            link.Words.Single(w => w.Side == LinkSide.From).WordId,
            link.Words.Single(w => w.Side == LinkSide.To).WordId))).Select(link => link.Id).ToArray();

        var targets = pairs.Select(p => p.From).Distinct().ToArray();
        var origins = pairs.Select(p => p.To).Distinct().ToArray();
        var routes = await db.Words.Where(w => origins.Contains(w.Id))
            .Select(w => new { w.Id, Slug = w.Text!.Slug }).ToListAsync(cancellationToken);
        var annotations = await db.WordEntities.AsNoTracking().Include(a => a.Claims)
            .Where(a => targets.Contains(a.WordId) && a.Entity!.Slug == "satan")
            .ToListAsync(cancellationToken);
        foreach (var annotation in annotations)
        {
            var notes = pairs.Where(p => p.From == annotation.WordId).Select(p =>
                $"through {routes.Single(w => w.Id == p.To).Slug} word {p.To}, linked by aligner").ToHashSet();
            if (annotation.Method != LinkMethod.StrongNumber || annotation.Source != EntityAnnotationLoader.GreekResolution
                || !notes.Contains(annotation.Note ?? string.Empty)
                || annotation.Claims.Any(c => c.Method != LinkMethod.StrongNumber
                    || c.Source != EntityAnnotationLoader.GreekResolution && c.Source != EntityAnnotationLoader.VerseList
                    || !notes.Contains(c.Note ?? string.Empty)))
                throw new InvalidDataException($"Protected evidence stands on a rejected rendering's annotation ({annotation.Id}); nothing was withdrawn.");
        }
        var withdrawn = await db.Links.Where(l => ids.Contains(l.Id)).ExecuteDeleteAsync(cancellationToken);
        var annotationIds = annotations.Select(a => a.Id).ToArray();
        var unnamed = await db.WordEntities.Where(a => annotationIds.Contains(a.Id)).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(withdrawn, unnamed);
    }
}
