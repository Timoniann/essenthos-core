using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Links;

/// <param name="Text">The edition's slug.</param>
/// <param name="Words">Supplied words stated absent from at least one witness.</param>
/// <param name="Written">Expands links written; a second run writes none.</param>
/// <param name="Withdrawn">
/// The aligner's renderings of supplied words taken back, and this pass's own absences a source's
/// rendering has since come to contradict.
/// </param>
/// <param name="Kept">
/// Supplied words another method renders against the same witness — a hand alignment, a Strong
/// number, an EVIDENTIA verdict — or that an aligner link also naming unmarked words renders. Nothing
/// here outranks a sourced link, and a word is never shown absent and rendered at once, so these are
/// left rendered and not stated absent from that witness.
/// </param>
internal sealed record SuppliedWordOutcome(string Text, int Words, int Written, int Withdrawn, int Kept)
{
    public override string ToString() =>
        $"{Text}: {Words} supplied words stated absent ({Written} expands links written), " +
        $"{Withdrawn} aligner renderings of them withdrawn, {Kept} still rendered by another method";
}

/// <summary>
/// The words an edition prints as its translators' own — the Almeida's, the American Standard's, the
/// Reina-Valera's and Brenton's italics, the Synodal's square brackets, the marks the Biblica Ukrainian
/// sets on what it adds — stated as absent from the originals it translates, the way the King James'
/// italics are: an <see cref="LinkRelation.Expands"/> link with nothing on the witness's side, one per
/// marked run of words in a verse, against every original-language witness the edition is linked to
/// that has the verse. The marks themselves are the <c>supplied</c> word groups the text's loader wrote.
///
/// <para>
/// A mark is a statement about the edition's base text, so the witnesses are those in its language:
/// the Hebrew for the Old Testament and the Greek for the New, and the Greek throughout for Brenton,
/// who translated the Vatican text. The Synodal's brackets also hold readings it took from the
/// Septuagint where the Hebrew has none; against the Hebrew those words are absent too, and they are
/// not stated against the Greek.
/// </para>
///
/// <para>
/// The statistical aligner's renderings of a marked word contradict the edition, and are withdrawn
/// when every word the link names on the edition's side is marked and nothing but the aligner claims
/// it. A link another method made stands, and its words are left rendered rather than shown absent as
/// well; they are counted.
/// </para>
///
/// Runs after the aligner's pairs are composed and the EVIDENTIA verdicts replayed, so it sees every
/// rendering the load makes; a second run writes and withdraws nothing.
/// </summary>
internal static class SuppliedWordAbsences
{
    private const string Hebrew = "hbo";
    private const string Greek = "grc";
    private const int LastOldTestamentBook = 39;
    private const int LastNewTestamentBook = 66;

    /// <param name="OldTestament">The language of the text the edition translated its Old Testament from.</param>
    internal sealed record Edition(string Slug, string OldTestament, string Source);

    internal static IReadOnlyList<Edition> Editions { get; } =
    [
        new("ALM1911", Hebrew, "Almeida 1911, the words its italics print as the translator's own"),
        new("ASV", Hebrew, "American Standard Version 1901, the words its italics print as the revisers' own"),
        new("RV1909", Hebrew, "Reina-Valera 1909, the words its italics print as the translators' own"),
        new("RUSV", Hebrew, "Russian Synodal, the words its square brackets mark as the translators' own"),
        new("NPU2022", Hebrew, "Biblica Open New Ukrainian Translation 2022, the words it marks as added"),
        new("BRENTON", Greek, "Brenton 1844, the words its italics print as the translator's own"),
    ];

    /// <summary>Each marked run of words in a verse, with every witness it is absent from.</summary>
    private static readonly string Absent =
        $"""
        WITH witness AS (
            SELECT DISTINCT l.to_text_id AS id, o.language
            FROM link l JOIN text o ON o.id = l.to_text_id AND o.kind <> 'translation'
            WHERE l.from_text_id = @text AND o.language IN ('{Hebrew}', '{Greek}')
        ),
        held AS (
            SELECT DISTINCT v.text_id, r.canonical_book AS b, r.canonical_chapter AS c, r.canonical_verse AS v
            FROM verse v JOIN verse_reference r ON r.verse_id = v.id AND r.is_primary
            WHERE v.text_id IN (SELECT id FROM witness)
        ),
        marked AS (
            SELECT g.id AS span, w.id AS word_id, w.verse_id, w."position",
                   r.canonical_book AS b, r.canonical_chapter AS c, r.canonical_verse AS v
            FROM word_group g
            JOIN word_group_word gw ON gw.word_group_id = g.id
            JOIN word w ON w.id = gw.word_id
            JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
            WHERE g.text_id = @text AND g.kind = 'supplied'
        )
        SELECT m.span, m.verse_id, x.id, m.word_id
        FROM marked m
        JOIN witness x ON x.language = CASE
            WHEN m.b <= {LastOldTestamentBook} THEN @old
            WHEN m.b <= {LastNewTestamentBook} THEN '{Greek}'
            ELSE CASE WHEN @old = '{Greek}' THEN '{Greek}' END END
        JOIN held h ON h.text_id = x.id AND (h.b, h.c, h.v) = (m.b, m.c, m.v)
        ORDER BY m.span, m.verse_id, x.id, m."position"
        """;

    /// <summary>
    /// The aligner's renderings that name a word stated absent from their witness: whether every word
    /// they name on the edition's side is one of those, and whether anything but the aligner claims them.
    /// </summary>
    private const string Renderings =
        """
        WITH absent AS (SELECT * FROM unnest(@witnesses, @words) AS a(witness, word_id)),
        touched AS (
            SELECT DISTINCT l.id, l.method, l.relation, p.source
            FROM absent a
            JOIN link_word lw ON lw.word_id = a.word_id AND lw.side = 'from'
            JOIN link l ON l.id = lw.link_id AND l.to_text_id = a.witness
            JOIN provenance p ON p.id = l.provenance_id
            WHERE l.relation IN ('renders', 'equals')
        )
        SELECT t.id,
               t.method = 'aligner' AND t.relation = 'renders' AND t.source LIKE 'SIL.Machine%'
               AND NOT EXISTS (
                   SELECT 1 FROM link_word lw
                   JOIN link l ON l.id = lw.link_id
                   LEFT JOIN absent a ON a.word_id = lw.word_id AND a.witness = l.to_text_id
                   WHERE lw.link_id = t.id AND lw.side = 'from' AND a.word_id IS NULL)
               AND NOT EXISTS (
                   SELECT 1 FROM link_claim c JOIN provenance cp ON cp.id = c.provenance_id
                   WHERE c.link_id = t.id AND (c.method <> 'aligner' OR cp.source NOT LIKE 'SIL.Machine%')),
               l.to_text_id,
               array_agg(lw.word_id)
        FROM touched t
        JOIN link_word lw ON lw.link_id = t.id AND lw.side = 'from'
        JOIN link l ON l.id = t.id
        JOIN absent a ON a.word_id = lw.word_id AND a.witness = l.to_text_id
        GROUP BY t.id, t.method, t.relation, t.source, l.to_text_id
        """;

    /// <summary>This pass's own absences of one edition, with their shapes, so one no longer drawn is taken back.</summary>
    private const string Stated =
        """
        SELECT l.id, l.fingerprint
        FROM link l JOIN provenance p ON p.id = l.provenance_id
        WHERE l.from_text_id = @text AND l.relation = 'expands' AND l.method = 'stated-by-source' AND p.source = @source
        """;

    public static async Task<IReadOnlyList<SuppliedWordOutcome>> State(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var outcomes = new List<SuppliedWordOutcome>();
        foreach (var edition in Editions)
        {
            var text = await db.Texts.Where(t => t.Slug == edition.Slug).Select(t => (int?)t.Id).FirstOrDefaultAsync(cancellationToken);
            if (text is { } id)
            {
                outcomes.Add(await State(db, edition, id, cancellationToken));
            }
        }

        return outcomes;
    }

    private static async Task<SuppliedWordOutcome> State(AppDbContext db, Edition edition, int text, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var npgsqlTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();

        var runs = new Dictionary<(long Span, int Verse, int Witness), List<long>>();
        await using (var command = new NpgsqlCommand(Absent, connection, npgsqlTransaction))
        {
            command.Parameters.AddWithValue("text", text);
            command.Parameters.AddWithValue("old", edition.OldTestament);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = (reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2));
                if (!runs.TryGetValue(key, out var words))
                {
                    runs[key] = words = [];
                }

                words.Add(reader.GetInt64(3));
            }
        }

        var absent = runs.SelectMany(run => run.Value.Select(word => (Witness: run.Key.Witness, Word: word))).ToList();
        var withdrawn = new List<long>();
        var kept = new HashSet<(int Witness, long Word)>();
        await using (var command = new NpgsqlCommand(Renderings, connection, npgsqlTransaction))
        {
            command.Parameters.AddWithValue("witnesses", absent.Select(a => a.Witness).ToArray());
            command.Parameters.AddWithValue("words", absent.Select(a => a.Word).ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetBoolean(1))
                {
                    withdrawn.Add(reader.GetInt64(0));
                }
                else
                {
                    var witness = reader.GetInt32(2);
                    kept.UnionWith(reader.GetFieldValue<long[]>(3).Select(word => (witness, word)));
                }
            }
        }

        var drafts = runs
            .Select(run => (run.Key.Witness, Words: run.Value.Where(word => !kept.Contains((run.Key.Witness, word))).ToList()))
            .Where(run => run.Words.Count > 0)
            .Select(run => new NewLink(
                text, run.Witness, LinkRelation.Expands, LinkMethod.StatedBySource, null, edition.Source, null, run.Words, []))
            .ToList();
        var shapes = drafts.Select(draft => (draft.ToTextId, LinkShape.Of(draft.From, draft.To))).ToHashSet();
        await using (var command = new NpgsqlCommand(Stated, connection, npgsqlTransaction))
        {
            command.Parameters.AddWithValue("text", text);
            command.Parameters.AddWithValue("source", edition.Source);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var stale = new List<long>();
            while (await reader.ReadAsync(cancellationToken))
            {
                stale.Add(reader.GetInt64(0));
            }

            await reader.DisposeAsync();
            if (stale.Count > 0)
            {
                var standing = await db.Links.Where(l => stale.Contains(l.Id))
                    .Select(l => new { l.Id, l.ToTextId, l.Fingerprint }).ToListAsync(cancellationToken);
                withdrawn.AddRange(standing
                    .Where(l => l.Fingerprint is not { } shape || !shapes.Contains((l.ToTextId, shape)))
                    .Select(l => l.Id));
            }
        }

        var write = await LinkWriter.Write(connection, npgsqlTransaction, drafts, cancellationToken, withdrawn);

        var removed = withdrawn.Count == 0
            ? 0
            : await db.Links.Where(l => withdrawn.Contains(l.Id)).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new SuppliedWordOutcome(
            edition.Slug,
            drafts.SelectMany(draft => draft.From).Distinct().Count(),
            write.Written,
            removed,
            kept.Select(pair => pair.Word).Distinct().Count());
    }
}
