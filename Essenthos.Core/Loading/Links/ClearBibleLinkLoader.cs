using System.Diagnostics;
using Essenthos.Core.ClearBible;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Corroborated">
/// Records naming a link the corpus already holds. These write no link at all — they add a claim,
/// which is the whole point: a word pair two independent people arrived at is worth more than one
/// either of them arrived at alone, and until <c>link_claim</c> existed there was nowhere to say so.
/// </param>
/// <param name="Added">Records naming words no link joined, which become links of their own.</param>
/// <param name="Contradicted">
/// Records whose Greek word the corpus already links to *different* English words. Written, because
/// a disagreement between two people who both looked is a fact about the translation and the most
/// interesting row in the corpus — not an error to be resolved by whoever loaded second.
/// </param>
internal sealed record ClearBibleOutcome(
    bool AlreadyLoaded,
    int Records,
    int Corroborated,
    int Added,
    int Contradicted,
    int Unresolved,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "Clear Bible has already spoken about this pair"
            : $"{Records} records in {Elapsed}: {Corroborated} corroborate a link the corpus already " +
              $"holds ({(Records == 0 ? 0 : (double)Corroborated / Records):P1}), {Added} name words " +
              $"nothing joined, {Contradicted} disagree with a link already here, {Unresolved} could " +
              "not be resolved to words on both sides";
}

/// <summary>
/// Clear Bible's hand-made alignment of the Berean Standard Bible, as a second opinion on links the
/// corpus already has.
///
/// The Berean's own publisher states which English word renders which Greek word, and that is loaded
/// (FTR-0182). Clear Bible's team answered the same question about the same translation, without
/// consulting them. Measured before this was written, over the 7,925 verses where the two tokenise
/// the text identically: of 115,016 Greek words both name, **96.6% get exactly the same English
/// words**, 1.1% overlap, 2.3% share none.
///
/// <para>
/// **So this loader mostly writes nothing.** Where the two agree it adds a claim to the link that is
/// already there, and the link's own method and source do not change — the Berean stated it first
/// and still states it. What changes is that the link can now say two people arrived at it, which is
/// the cheapest evidence this corpus has and the thing DOC-0170 says it was throwing away.
/// </para>
///
/// <para>
/// Where they disagree, both answers are kept. A second link naming different words is not a
/// duplicate and does not trip the check that catches those — that check is about two links naming
/// *the same* words, which is agreement stored as rivalry. Two people disagreeing about which
/// English word renders a Greek one is a fact about translation, and the corpus should hold it
/// rather than pick.
/// </para>
///
/// <para>
/// Their Russian set is in the same download and is not loaded by anything: its records do not
/// correspond to the token file shipped beside them. PRB-0185.
/// </para>
/// </summary>
internal sealed class ClearBibleLinkLoader(AppDbContext db, ILogger<ClearBibleLinkLoader> logger)
{
    private const string Source =
        "Clear Bible Alignments, BiblioNexus, github.com/Clear-Bible/Alignments, CC BY 4.0";

    private const string LinkImport =
        """
        COPY link (id, from_text_id, to_text_id, relation, method, confidence, source)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string LinkWordImport =
        "COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)";

    private const string ClaimImport =
        """
        COPY link_claim (link_id, method, confidence, source, note)
        FROM STDIN (FORMAT BINARY)
        """;

    public async Task<ClearBibleOutcome> Load(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var from = await Text(BereanTextSource.Slug, cancellationToken);
        var to = await Text(NestleTextSource.Slug, cancellationToken);

        if (from == 0 || to == 0)
        {
            return new ClearBibleOutcome(true, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var alignment = Path.Combine(directory, "data", "eng", "alignments", "BSB", "BGNT-BSB-manual.json");
        var tokens = Path.Combine(directory, "data", "eng", "targets", "BSB", "nt_BSB.tsv");

        if (!File.Exists(alignment) || !File.Exists(tokens))
        {
            logger.LogWarning(
                "Clear Bible is not at {Directory}, so nothing corroborates the Berean tables. It is "
                + "fetched rather than committed; scripts/fetch-clearbible.ps1 says from where", directory);
            return new ClearBibleOutcome(true, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        if (await db.LinkClaims.AnyAsync(
                c => c.Source == Source && c.Link!.FromTextId == from, cancellationToken))
        {
            logger.LogInformation("Clear Bible has already spoken about the Berean; nothing to do");
            return new ClearBibleOutcome(true, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var english = await Words(from, cancellationToken);
        var greek = await Words(to, cancellationToken);
        var theirs = TheirWords(tokens);
        var existing = await Shapes(from, to, cancellationToken);

        var claims = new List<long>();
        var drafts = new List<Draft>();
        int records = 0, corroborated = 0, added = 0, contradicted = 0, unresolved = 0;

        foreach (var record in ClearBibleAlignment.Records(alignment))
        {
            records++;
            var source = Ours(record.Source, greek, id => ClearBibleAlignment.Position(id));
            var target = Ours(record.Target, english, id => theirs.GetValueOrDefault(ClearBibleAlignment.Word(id)));

            if (source.Count == 0 || target.Count == 0)
            {
                unresolved++;
                continue;
            }

            if (existing.Shapes.TryGetValue(Shape(target, source), out var link))
            {
                corroborated++;
                claims.Add(link);
                continue;
            }

            // A Greek word the corpus already joins to different English words: two people who both
            // looked, disagreeing. Counted apart from a plain addition because the two mean
            // different things about the corpus and a single total would hide it.
            if (source.Any(existing.SpokenFor.Contains))
            {
                contradicted++;
            }
            else
            {
                added++;
            }

            drafts.Add(new Draft(target, source));
        }

        await Write(from, to, drafts, [.. claims.Distinct()], cancellationToken);

        var outcome = new ClearBibleOutcome(
            false, records, corroborated, added, contradicted, unresolved, started.Elapsed);
        logger.LogInformation("Clear Bible on the Berean: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// Their word ids as ours, dropping a record whose words this corpus does not hold. A record
    /// half-resolved would be a claim about fewer words than the person made, which is a different
    /// claim.
    /// </summary>
    private static List<long> Ours(
        IReadOnlyList<string> ids,
        IReadOnlyDictionary<(int, int, int), List<long>> words,
        Func<string, int> position)
    {
        var resolved = new List<long>(ids.Count);
        foreach (var id in ids)
        {
            if (!ClearBibleAlignment.Address(id, out var book, out var chapter, out var verse)
                || !words.TryGetValue((book, chapter, verse), out var verseWords))
            {
                return [];
            }

            var at = position(id);
            if (at < 1 || at > verseWords.Count)
            {
                return [];
            }

            resolved.Add(verseWords[at - 1]);
        }

        return resolved;
    }

    /// <summary>
    /// Their token ids against the position each holds among the words of its verse, counting only
    /// the tokens the file does not exclude. Their punctuation is numbered like a word and marked
    /// out of the alignment; ours is not a word at all.
    /// </summary>
    private static Dictionary<string, int> TheirWords(string path)
    {
        var positions = new Dictionary<string, int>(220_000, StringComparer.Ordinal);
        var verse = string.Empty;
        var at = 0;

        foreach (var token in ClearBibleAlignment.Tokens(path))
        {
            var address = token.Id[..8];
            if (!string.Equals(address, verse, StringComparison.Ordinal))
            {
                verse = address;
                at = 0;
            }

            if (token.Excluded)
            {
                continue;
            }

            positions[token.Id] = ++at;
        }

        return positions;
    }

    /// <summary>Every link of a pair by the words it names, so a second opinion can find it.</summary>
    private async Task<Existing> Shapes(
        int fromTextId,
        int toTextId,
        CancellationToken cancellationToken)
    {
        var rows = await db.LinkWords
            .Where(word => word.Link!.FromTextId == fromTextId && word.Link!.ToTextId == toTextId)
            .Select(word => new { word.LinkId, word.WordId, word.Side })
            .ToListAsync(cancellationToken);

        var shapes = new Dictionary<string, long>(rows.Count / 2, StringComparer.Ordinal);
        var spokenFor = new HashSet<long>(rows.Count / 2);

        foreach (var link in rows.GroupBy(row => row.LinkId))
        {
            var fromWords = link.Where(w => w.Side == LinkSide.From).Select(w => w.WordId).ToList();
            var toWords = link.Where(w => w.Side == LinkSide.To).Select(w => w.WordId).ToList();
            shapes[Shape(fromWords, toWords)] = link.Key;
            spokenFor.UnionWith(toWords);
        }

        return new Existing(shapes, spokenFor);
    }

    /// <param name="Shapes">Every link of the pair by the words it names.</param>
    /// <param name="SpokenFor">
    /// The witness words some link already reaches, which is what separates *nobody had an answer
    /// for this word* from *somebody had a different one*.
    /// </param>
    private sealed record Existing(Dictionary<string, long> Shapes, HashSet<long> SpokenFor);

    /// <summary>The words a link names, as one key. Order never enters a link, so it is sorted.</summary>
    private static string Shape(IEnumerable<long> from, IEnumerable<long> to) =>
        string.Join(',', from.Order()) + '|' + string.Join(',', to.Order());

    private async Task<Dictionary<(int, int, int), List<long>>> Words(
        int textId,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(reference => reference.IsPrimary && reference.Verse!.TextId == textId)
            .SelectMany(reference => reference.Verse!.Words.Select(word => new
            {
                reference.CanonicalBook,
                reference.CanonicalChapter,
                reference.CanonicalVerse,
                word.Id,
                word.Position,
            }))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => (row.CanonicalBook, row.CanonicalChapter, row.CanonicalVerse))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(row => row.Position).Select(row => row.Id).ToList());
    }

    private async Task Write(
        int fromTextId,
        int toTextId,
        List<Draft> drafts,
        IReadOnlyList<long> corroborated,
        CancellationToken cancellationToken)
    {
        if (drafts.Count == 0 && corroborated.Count == 0)
        {
            return;
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var stated = EnumSpelling.Of(LinkMethod.StatedBySource);
        var renders = EnumSpelling.Of(LinkRelation.Renders);
        var fromSide = EnumSpelling.Of(LinkSide.From);
        var toSide = EnumSpelling.Of(LinkSide.To);
        var firstId = drafts.Count == 0 ? 0 : await ReserveLinkIds(connection, drafts.Count, cancellationToken);

        if (drafts.Count > 0)
        {
            await using (var writer = await connection.BeginBinaryImportAsync(LinkImport, cancellationToken))
            {
                for (var i = 0; i < drafts.Count; i++)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(firstId + i, NpgsqlDbType.Bigint, cancellationToken);
                    await writer.WriteAsync(fromTextId, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(toTextId, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(renders, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(stated, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteNullAsync(cancellationToken);
                    await writer.WriteAsync(Source, NpgsqlDbType.Text, cancellationToken);
                }

                await writer.CompleteAsync(cancellationToken);
            }

            await using (var writer = await connection.BeginBinaryImportAsync(LinkWordImport, cancellationToken))
            {
                for (var i = 0; i < drafts.Count; i++)
                {
                    foreach (var word in drafts[i].From)
                    {
                        await Row(writer, firstId + i, word, fromSide, cancellationToken);
                    }

                    foreach (var word in drafts[i].To)
                    {
                        await Row(writer, firstId + i, word, toSide, cancellationToken);
                    }
                }

                await writer.CompleteAsync(cancellationToken);
            }
        }

        await using (var writer = await connection.BeginBinaryImportAsync(ClaimImport, cancellationToken))
        {
            foreach (var link in corroborated.Concat(Enumerable.Range(0, drafts.Count).Select(i => firstId + i)))
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(link, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(stated, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteNullAsync(cancellationToken);
                await writer.WriteAsync(Source, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteNullAsync(cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task Row(
        NpgsqlBinaryImporter writer,
        long linkId,
        long wordId,
        string side,
        CancellationToken cancellationToken)
    {
        await writer.StartRowAsync(cancellationToken);
        await writer.WriteAsync(linkId, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(wordId, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(side, NpgsqlDbType.Text, cancellationToken);
    }

    private static async Task<long> ReserveLinkIds(
        NpgsqlConnection connection,
        int count,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT setval(pg_get_serial_sequence('link', 'id'), " +
            "coalesce((SELECT max(id) FROM link), 0) + @count) - @count + 1", connection);
        command.Parameters.AddWithValue("count", count);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task<int> Text(string slug, CancellationToken cancellationToken) =>
        await db.Texts.Where(t => t.Slug == slug).Select(t => t.Id).FirstOrDefaultAsync(cancellationToken);

    private sealed record Draft(List<long> From, List<long> To);
}
