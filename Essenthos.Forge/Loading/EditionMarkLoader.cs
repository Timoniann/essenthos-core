using System.Text.Json;
using Essenthos.Core.Berean;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Utils;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading;

/// <param name="Text">The text's slug.</param>
/// <param name="Kind">What the edition's mark says about the words under it; null where it is not known.</param>
/// <param name="Spans">How many marked spans were written.</param>
/// <param name="Words">How many words they cover.</param>
/// <param name="Refused">Verses whose marks could not be placed on the stored words, and were left unmarked.</param>
internal sealed record EditionMarkOutcome(string Text, WordGroupKind? Kind, int Spans, int Words, int Refused)
{
    public override string ToString() =>
        Kind is { } kind
            ? $"{Text}: {Spans} {EnumSpelling.Of(kind)} spans over {Words} words, {Refused} verses refused"
            : $"{Text}: its brackets taken out of the words and nothing claimed for them";
}

/// <summary>
/// What an edition prints about its own words — that its translators supplied them, that its editors
/// doubt them, that they are a scribe's note and not the text — written as <c>word_group</c> rows of
/// the kind that says so, whichever way the edition printed it.
///
/// <para>
/// One fact has one home. The King James' italics, the Berean's brackets and braces and the Synodal's
/// brackets all say the same thing, and a reader asks it once: the group is the first-hand record,
/// and an <see cref="LinkRelation.Expands"/> link is what that record means against one other text.
/// The King James' groups are read back from its stated expands links, because those are the only
/// place its italics were ever matched to the stored words; the Berean's from its own tables, matched
/// word by word the way its links are.
/// </para>
///
/// <para>
/// The Greek editions' brackets are marks of a different kind and were stored as characters of the
/// words. They are taken out of the words and kept as structure: Nestle's <c>[[…]]</c> and <c>[…]</c>
/// as doubtful text with the degree in the features, the Textus Receptus' <c>[προς …]</c> as a
/// subscription. Brenton's two are only taken out: nothing Brenton's distributed files carry says
/// what they mean, so nothing is claimed for them.
/// </para>
///
/// Every step replaces what it wrote before, so running it again changes nothing.
/// </summary>
internal sealed class EditionMarkLoader(AppDbContext db, ILogger<EditionMarkLoader> logger)
{
    private const string KingJames = "KJV";
    private const string Stephanus = "TR1550";
    private const string BrentonGreek = "GRCBRENT";

    private const string StatedAbsences =
        """
        SELECT DISTINCT w.id, w.verse_id, w."position"
        FROM link l
        JOIN link_word lw ON lw.link_id = l.id AND lw.side = @from
        JOIN word w ON w.id = lw.word_id
        WHERE l.from_text_id = @text AND l.relation = @expands AND l.method = @stated
        ORDER BY w.verse_id, w."position"
        """;

    private const string BracketedWords =
        """
        SELECT w.id, w.verse_id, w."position", w.text, w.trailer
        FROM word w JOIN verse v ON v.id = w.verse_id
        WHERE (v.book_id, v.chapter_number) IN (
            SELECT DISTINCT b.book_id, b.chapter_number FROM word x JOIN verse b ON b.id = x.verse_id
            WHERE x.text_id = @text AND (x.text ~ '[\[\]]' OR x.trailer ~ '[\[\]]'))
        ORDER BY v.id, w."position"
        """;

    private const string StripBrackets =
        """
        UPDATE word SET
            text = translate(text, '[]', ''),
            trailer = translate(trailer, '[]', ''),
            normalised_text = translate(normalised_text, '[]', ''),
            graphical_text = translate(graphical_text, '[]', '')
        WHERE text_id = @text AND (text ~ '[\[\]]' OR trailer ~ '[\[\]]')
        """;

    private const string Replace =
        """
        WITH gone AS (DELETE FROM word_group WHERE text_id = @text AND kind = @kind),
        spans AS (
            SELECT span, (array_agg(features))[1] AS features
            FROM unnest(@featureSpans, @features) AS s(span, features)
            GROUP BY span),
        groups AS (
            INSERT INTO word_group (text_id, kind, "position", features)
            SELECT @text, @kind, span, features::jsonb FROM spans ORDER BY span
            RETURNING id, "position")
        INSERT INTO word_group_word (word_group_id, word_id)
        SELECT g.id, m.word_id
        FROM unnest(@spans, @words) AS m(span, word_id)
        JOIN groups g ON g."position" = m.span
        """;

    /// <summary>
    /// Whether a text's groups of one kind are already exactly the spans about to be written: the same
    /// positions, features and words. Asked first so that a load that would write them again as they
    /// are leaves them, ids and all.
    /// </summary>
    private const string Unchanged =
        """
        WITH spans AS (
            SELECT span, (array_agg(features))[1]::jsonb AS features
            FROM unnest(@featureSpans, @features) AS s(span, features)
            GROUP BY span),
        members AS (
            SELECT span, array_agg(word_id ORDER BY word_id) AS words
            FROM unnest(@spans, @words) AS m(span, word_id)
            GROUP BY span),
        wanted AS (
            SELECT s.span AS position, s.features, m.words
            FROM spans s LEFT JOIN members m ON m.span = s.span),
        held AS (
            SELECT g."position", g.features, array_agg(gw.word_id ORDER BY gw.word_id) FILTER (WHERE gw.word_id IS NOT NULL) AS words
            FROM word_group g
            LEFT JOIN word_group_word gw ON gw.word_group_id = g.id
            WHERE g.text_id = @text AND g.kind = @kind
            GROUP BY g.id, g."position", g.features)
        SELECT NOT EXISTS (SELECT * FROM wanted EXCEPT ALL SELECT * FROM held)
           AND NOT EXISTS (SELECT * FROM held EXCEPT ALL SELECT * FROM wanted)
        """;

    public async Task<IReadOnlyList<EditionMarkOutcome>> Mark(string tables, CancellationToken cancellationToken)
    {
        var outcomes = new List<EditionMarkOutcome>();
        if (await TextId(KingJames, cancellationToken) is { } kjv)
        {
            outcomes.Add(await MarkTheItalics(kjv, cancellationToken));
        }

        if (await TextId(BereanTextSource.Slug, cancellationToken) is { } bsb && File.Exists(tables))
        {
            outcomes.Add(await MarkTheBerean(bsb, tables, cancellationToken));
        }

        foreach (var (slug, kind) in new (string, WordGroupKind?)[]
                 {
                     (NestleTextSource.Slug, WordGroupKind.Doubtful),
                     (Stephanus, WordGroupKind.Subscription),
                     (BrentonGreek, null),
                 })
        {
            if (await TextId(slug, cancellationToken) is { } id)
            {
                outcomes.Add(await MarkTheBrackets(id, slug, kind, cancellationToken));
            }
        }

        foreach (var outcome in outcomes)
        {
            logger.LogInformation("{Outcome}", outcome);
        }

        return outcomes;
    }

    /// <summary>
    /// The King James' italics, from the expands links its two stated mappings wrote. The New
    /// Testament's are written once per Greek witness, so a word is read once however many name it,
    /// and a run of italic words in one verse is one span, as it is one run of italics on the page.
    /// </summary>
    private async Task<EditionMarkOutcome> MarkTheItalics(int text, CancellationToken cancellationToken)
    {
        var connection = await Open(cancellationToken);
        var words = new List<(long Id, int Verse, int Position)>();
        await using (var command = new NpgsqlCommand(StatedAbsences, connection))
        {
            command.Parameters.AddWithValue("text", text);
            command.Parameters.AddWithValue("from", EnumSpelling.Of(LinkSide.From));
            command.Parameters.AddWithValue("expands", EnumSpelling.Of(LinkRelation.Expands));
            command.Parameters.AddWithValue("stated", EnumSpelling.Of(LinkMethod.StatedBySource));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                words.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2)));
            }
        }

        var spans = new List<Span>();
        for (var i = 0; i < words.Count; i++)
        {
            var continues = i > 0
                            && words[i].Verse == words[i - 1].Verse
                            && words[i].Position == words[i - 1].Position + 1;
            if (!continues)
            {
                spans.Add(new Span([], null));
            }

            spans[^1].Words.Add(words[i].Id);
        }

        await Write(text, WordGroupKind.Supplied, spans, cancellationToken);
        return new EditionMarkOutcome(KingJames, WordGroupKind.Supplied, spans.Count, words.Count, 0);
    }

    /// <summary>
    /// The Berean's supplied words, from the tables that print them in brackets or braces. The tables
    /// are walked in the edition's English order and matched to the stored words exactly as its links
    /// are, and a verse whose words do not match is left unmarked rather than marked in the wrong place.
    /// Which of the two marks the edition used is kept, because the tables do not say they mean the same.
    /// </summary>
    private async Task<EditionMarkOutcome> MarkTheBerean(int text, string tables, CancellationToken cancellationToken)
    {
        var ours = await BereanLinkLoader.WordsByAddress(db, text, cancellationToken);
        var spans = new List<Span>();
        var refused = 0;

        foreach (var (reference, rows) in BereanTable.Verses(tables))
        {
            if (!BereanTextSource.Address(reference, out var book, out var chapter, out var number)
                || !ours.TryGetValue((book, chapter, number), out var words))
            {
                continue;
            }

            var marked = new List<Span>();
            var at = 0;
            var matched = true;
            foreach (var row in rows.OrderBy(row => row.EnglishOrder))
            {
                Span? open = null;
                var openAt = -1;
                foreach (var (word, span, mark) in BereanWords.Marked(row.English))
                {
                    if (at >= words.Count || !BereanWords.Same(words[at].Surface, word))
                    {
                        matched = false;
                        break;
                    }

                    if (span is { } index)
                    {
                        if (open is null || openAt != index)
                        {
                            open = new Span([], JsonSerializer.Serialize(new { mark }));
                            openAt = index;
                            marked.Add(open);
                        }

                        open.Words.Add(words[at].Id);
                    }
                    else
                    {
                        open = null;
                    }

                    at++;
                }

                if (!matched)
                {
                    break;
                }
            }

            if (!matched || at != words.Count)
            {
                refused += rows.Any(row => row.English.IndexOfAny(['[', '{']) >= 0) ? 1 : 0;
                continue;
            }

            spans.AddRange(marked);
        }

        await Write(text, WordGroupKind.Supplied, spans, cancellationToken);
        return new EditionMarkOutcome(
            BereanTextSource.Slug, WordGroupKind.Supplied, spans.Count, spans.Sum(span => span.Words.Count), refused);
    }

    /// <summary>
    /// An edition's square brackets, taken out of the words that carry them. A span opens at the word
    /// that begins with a bracket and closes at the word whose own text or trailer closes it; the
    /// number of brackets that opened it is its degree. With no kind the characters go and nothing
    /// is written in their place.
    /// </summary>
    private async Task<EditionMarkOutcome> MarkTheBrackets(
        int text,
        string slug,
        WordGroupKind? kind,
        CancellationToken cancellationToken)
    {
        var connection = await Open(cancellationToken);
        var words = new List<(long Id, int Verse, string Text, string Trailer)>();
        await using (var command = new NpgsqlCommand(BracketedWords, connection))
        {
            command.Parameters.AddWithValue("text", text);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                words.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetString(3), reader.GetString(4)));
            }
        }

        // Already taken out: what was written from them stands, and there is nothing to read again.
        if (words.Count == 0)
        {
            return new EditionMarkOutcome(slug, kind, 0, 0, 0);
        }

        // A span can run on through the verses of its chapter — Nestle's brackets hold the whole of
        // Mark 16:9-20 — so the chapter is read and not only the verses that carry a bracket.
        var spans = new List<Span>();
        Span? open = null;
        var refused = 0;
        foreach (var word in words)
        {
            if (word.Text.StartsWith('['))
            {
                var degree = word.Text.TakeWhile(c => c == '[').Count();
                open = new Span([], JsonSerializer.Serialize(new { brackets = degree }));
                spans.Add(open);
            }

            open?.Words.Add(word.Id);

            if (open is not null && (word.Text.Contains(']') || word.Trailer.Contains(']')))
            {
                open = null;
            }
        }

        if (open is not null)
        {
            spans.Remove(open);
            refused++;
        }

        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await using (var strip = new NpgsqlCommand(StripBrackets, connection, transaction))
            {
                strip.Parameters.AddWithValue("text", text);
                await strip.ExecuteNonQueryAsync(cancellationToken);
            }

            if (kind is { } marked)
            {
                await Write(text, marked, spans, cancellationToken, transaction);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        return new EditionMarkOutcome(
            slug, kind, kind is null ? 0 : spans.Count, kind is null ? 0 : spans.Sum(span => span.Words.Count), refused);
    }

    private async Task Write(
        int text,
        WordGroupKind kind,
        List<Span> spans,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        var connection = await Open(cancellationToken);
        var memberSpans = new List<int>();
        var members = new List<long>();
        var featureSpans = new List<int>();
        var features = new List<string?>();
        for (var i = 0; i < spans.Count; i++)
        {
            featureSpans.Add(i + 1);
            features.Add(spans[i].Features);
            foreach (var word in spans[i].Words.Distinct())
            {
                memberSpans.Add(i + 1);
                members.Add(word);
            }
        }

        NpgsqlCommand Command(string sql)
        {
            var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("text", text);
            command.Parameters.AddWithValue("kind", EnumSpelling.Of(kind));
            command.Parameters.AddWithValue("featureSpans", featureSpans.ToArray());
            command.Parameters.Add(new NpgsqlParameter("features", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = features.Select(f => (object?)f ?? DBNull.Value).ToArray(),
            });
            command.Parameters.AddWithValue("spans", memberSpans.ToArray());
            command.Parameters.AddWithValue("words", members.ToArray());
            return command;
        }

        await using (var unchanged = Command(Unchanged))
        {
            if ((bool)(await unchanged.ExecuteScalarAsync(cancellationToken))!)
            {
                return;
            }
        }

        await using var command = Command(Replace);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> Open(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return connection;
    }

    private async Task<int?> TextId(string slug, CancellationToken cancellationToken) =>
        await db.Texts.Where(t => t.Slug == slug).Select(t => (int?)t.Id).FirstOrDefaultAsync(cancellationToken);

    private sealed record Span(List<long> Words, string? Features);
}
