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
    ];

    private const string Located =
        """
        SELECT x.n, ft.id, tt.id, f.id, f.text, z.id, z.text
        FROM unnest(@from_texts, @to_texts, @books, @chapters, @verses, @from_positions, @to_positions)
             WITH ORDINALITY x(fs, ts, b, c, v, fp, tp, n)
        LEFT JOIN text ft ON ft.slug = x.fs LEFT JOIN text tt ON tt.slug = x.ts
        LEFT JOIN book fb ON fb.text_id = ft.id AND fb.canonical_ordinal = x.b
        LEFT JOIN book tb ON tb.text_id = tt.id AND tb.canonical_ordinal = x.b
        LEFT JOIN verse fv ON fv.book_id = fb.id AND fv.chapter_number = x.c AND fv.number = x.v AND fv.label = ''
        LEFT JOIN verse tv ON tv.book_id = tb.id AND tv.chapter_number = x.c AND tv.number = x.v AND tv.label = ''
        LEFT JOIN word f ON f.verse_id = fv.id AND f.position = x.fp
        LEFT JOIN word z ON z.verse_id = tv.id AND z.position = x.tp
        """;

    internal static async Task<HashSet<(long From, long To)>> Locate(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Located, connection);
        command.Parameters.AddWithValue("from_texts", All.Select(r => r.From.Text).ToArray());
        command.Parameters.AddWithValue("to_texts", All.Select(r => r.To.Text).ToArray());
        command.Parameters.AddWithValue("books", All.Select(r => r.From.Address()!.Value.Book).ToArray());
        command.Parameters.AddWithValue("chapters", All.Select(r => r.From.Address()!.Value.Chapter).ToArray());
        command.Parameters.AddWithValue("verses", All.Select(r => r.From.Address()!.Value.Verse).ToArray());
        command.Parameters.AddWithValue("from_positions", All.Select(r => r.From.Position).ToArray());
        command.Parameters.AddWithValue("to_positions", All.Select(r => r.To.Position).ToArray());
        var found = new HashSet<(long, long)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var ruling = All[(int)reader.GetInt64(0) - 1];
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
