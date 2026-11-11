using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Npgsql;

namespace Essenthos.Core.Loading.Links;

internal sealed record RejectedRendering(RuledWord From, RuledWord To, string Reason);

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

        // Genesis 14:5, where the Septuagint renders one name of the list as a phrase and the aligner
        // paired the names after it one place off.
        new(new("GRCBRENT", "GEN 14:5", 29, "Ὀμμαίους"), new("BHSA", "GEN 14:5", 22, "זּוּזִ֖ים"),
            "Claude, 2026-10-14: the Septuagint renders the Zuzim 'strong nations'; Ὀμμαίους is the Emim at position 28."),
        new(new("GRCBRENT", "GEN 14:5", 32, "Σαυῇ"), new("BHSA", "GEN 14:5", 24, "הָ֑ם"),
            "Claude, 2026-10-14: Σαυῇ is Shaveh at position 30; the Septuagint reads 'in Ham' as 'with them'."),
        new(new("GRCBRENT", "GEN 14:5", 29, "Ὀμμαίους"), new("KJV", "GEN 14:5", 24, "Zuzims"),
            "Claude, 2026-10-14: Ὀμμαίους is the King James's Emims at position 29, not the Zuzims."),
        new(new("GRCBRENT", "GEN 14:5", 32, "Σαυῇ"), new("KJV", "GEN 14:5", 29, "Emins"),
            "Claude, 2026-10-14: Σαυῇ is the King James's Shaveh at position 31, not the Emims."),
        new(new("GEEZ81", "GEN 14:5", 14, "ወለአምዮስ"), new("BHSA", "GEN 14:5", 22, "זּוּזִ֖ים"),
            "Claude, 2026-10-14: the Ge'ez Ammyos follows the Septuagint's Ommaeans, the Emim at position 28."),

        // Esther 8:9 in Brenton's Greek, the officers of the king's letter, where the aligner paired one
        // with a name of the verse.
        new(new("GRCBRENT", "EST 8:9", 25, "οἰκονόμοις"), new("BHSA", "EST 8:9", 30, "מָרְדֳּכַ֣י"),
            "Claude, 2026-11-01: οἰκονόμοις is the stewards the king commanded; Mordecai is not in the Greek's verse."),
        new(new("GRCBRENT", "EST 8:9", 30, "σατραπῶν"), new("GEEZ81", "EST 9:9", 17, "ወለመሳፍንቱ"),
            "Claude, 2026-11-01: σατραπῶν is the satraps' chiefs, and the Ge'ez word is its princes; neither is a name."),
    ];

    /// <summary>
    /// Both words of every rejected pair, each at the address its own text numbers it by: the two
    /// texts need not number a verse alike, so the second word is never looked for at the first's.
    /// </summary>
    private const string Located =
        """
        SELECT x.n, ft.id, tt.id, f.id, f.text, z.id, z.text, fve.id, tve.id
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
            // A corpus without either text, or without the verse (a partial edition, a test's few
            // verses), has nothing the ruling is about; only a verse that is there and holds other
            // words means the ruling has gone stale.
            if (reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(7) || reader.IsDBNull(8)) continue;
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
}
