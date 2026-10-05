using System.Globalization;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// One link a loader means to write: the two texts, what the link says and who says it, and the
/// words on each side.
/// </summary>
internal sealed record NewLink(
    int FromTextId,
    int ToTextId,
    LinkRelation Relation,
    LinkMethod Method,
    double? Confidence,
    string Source,
    string? Note,
    IReadOnlyCollection<long> From,
    IReadOnlyCollection<long> To);

/// <param name="Ids">For each draft, in order, the link that now names its words.</param>
/// <param name="Fresh">
/// For each draft, whether it was written as a link of its own rather than named the same words
/// as a link already there, or as an earlier draft of the same batch, and became a claim on it.
/// </param>
internal sealed record LinkWrite(long[] Ids, bool[] Fresh)
{
    public int Written => Fresh.Count(fresh => fresh);

    public int Joined => Fresh.Length - Written;
}

/// <summary>
/// Writes links the one way every loader writes them: the link, its words and the claim that says
/// the loader is the one asserting it, together, with the source and note by provenance id and the
/// shape written with the link.
///
/// <para>
/// **A draft naming the words a link of the same two texts already names is not written as a
/// link.** The corpus refuses a second link over the same words — two methods arriving at the same
/// words are one link with two claims — so the draft becomes a claim on the link that is there, and
/// if it is the stronger claim it becomes the link's settled answer, the way a reading outranks a
/// guess. The aligner composing a pair again then leaves that link alone, because it is no longer
/// only the aligner's.
/// </para>
///
/// <para>
/// Everything is sent by COPY, ids are taken from the identity sequence up front because COPY
/// cannot report them, and the shapes already in the corpus are found through the unique index on
/// the fingerprint rather than by reading every word of the pair.
/// </para>
/// </summary>
internal static class LinkWriter
{
    private const string LinkImport =
        """
        COPY link (id, from_text_id, to_text_id, relation, method, confidence, provenance_id, fingerprint)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string LinkWordImport = "COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)";

    private const string ClaimImport =
        "COPY link_claim (link_id, method, confidence, provenance_id) FROM STDIN (FORMAT BINARY)";

    private const string Existing =
        """
        SELECT d.i, l.id, l.relation
        FROM unnest(@from, @to, @shape) WITH ORDINALITY AS d(from_text_id, to_text_id, fingerprint, i)
        JOIN link l ON l.from_text_id = d.from_text_id AND l.to_text_id = d.to_text_id
                   AND l.fingerprint = d.fingerprint
        WHERE l.id <> ALL(@leaving)
        """;

    private const string JoinedClaims =
        """
        INSERT INTO link_claim (link_id, method, confidence, provenance_id)
        SELECT * FROM unnest(@links, @methods, @confidences, @provenances)
        ON CONFLICT DO NOTHING
        """;

    /// <summary>
    /// The stronger claim becomes the link's own answer; a link a person corrected keeps the person.
    /// </summary>
    private static readonly string Promote =
        $"""
        UPDATE link l
        SET method = d.method, confidence = d.confidence, provenance_id = d.provenance_id
        FROM unnest(@links, @methods, @confidences, @provenances, @standings)
             AS d(link_id, method, confidence, provenance_id, standing)
        WHERE l.id = d.link_id AND {Standing("l.method")} < d.standing
        """;

    /// <summary><see cref="ClaimStanding"/> as SQL over a method column.</summary>
    public static string Standing(string column) =>
        $"CASE {column} "
        + string.Concat(Enum.GetValues<LinkMethod>().Select(method =>
            $"WHEN '{EnumSpelling.Of(method)}' THEN "
            + ClaimStanding.Of(method).ToString(CultureInfo.InvariantCulture) + " "))
        + "ELSE 0 END";

    /// <param name="leaving">
    /// Links this transaction removes once the drafts are written, which a draft is not folded into:
    /// a guess a stated link replaces is taken out after its claims have moved onto the new link.
    /// </param>
    public static async Task<LinkWrite> Write(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        IReadOnlyList<NewLink> drafts,
        CancellationToken cancellationToken,
        IReadOnlyCollection<long>? leaving = null)
    {
        if (drafts.Count == 0)
        {
            return new LinkWrite([], []);
        }

        if ((await RejectedRenderings.Admitted(connection, drafts, cancellationToken)).Count != drafts.Count)
            throw new InvalidDataException("A rejected rendering reached the link writer; filter the statistical proposals before writing them.");

        var provenance = await ProvenanceIds.Of(
            connection, transaction, drafts.Select(draft => (draft.Source, draft.Note)), cancellationToken);
        var shapes = drafts.Select(draft => LinkShape.Of(draft.From, draft.To)).ToArray();
        var ids = new long[drafts.Count];
        var relations = new LinkRelation[drafts.Count];

        await using (var command = new NpgsqlCommand(Existing, connection, transaction))
        {
            command.Parameters.AddWithValue("from", drafts.Select(draft => draft.FromTextId).ToArray());
            command.Parameters.AddWithValue("to", drafts.Select(draft => draft.ToTextId).ToArray());
            command.Parameters.AddWithValue("shape", shapes);
            command.Parameters.AddWithValue("leaving", leaving?.ToArray() ?? []);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var at = (int)reader.GetInt64(0) - 1;
                ids[at] = reader.GetInt64(1);
                relations[at] = EnumSpelling.ToLinkRelation(reader.GetString(2));
            }
        }

        var fresh = new List<int>(drafts.Count);
        var firstOfShape = new Dictionary<(int, int, Guid), int>(drafts.Count);
        for (var i = 0; i < drafts.Count; i++)
        {
            if (ids[i] == 0 && !firstOfShape.ContainsKey((drafts[i].FromTextId, drafts[i].ToTextId, shapes[i])))
            {
                firstOfShape[(drafts[i].FromTextId, drafts[i].ToTextId, shapes[i])] = i;
                fresh.Add(i);
            }
        }

        var firstId = fresh.Count == 0 ? 0 : await ReserveIds(connection, transaction, fresh.Count, cancellationToken);
        for (var n = 0; n < fresh.Count; n++)
        {
            ids[fresh[n]] = firstId + n;
            relations[fresh[n]] = drafts[fresh[n]].Relation;
        }

        for (var i = 0; i < drafts.Count; i++)
        {
            if (ids[i] == 0)
            {
                var first = firstOfShape[(drafts[i].FromTextId, drafts[i].ToTextId, shapes[i])];
                ids[i] = ids[first];
                relations[i] = relations[first];
            }

            if (relations[i] != drafts[i].Relation)
            {
                throw new InvalidOperationException(
                    $"Link {ids[i]} names the same words as a draft from {drafts[i].Source}, but says they " +
                    $"{EnumSpelling.Of(relations[i])} where the draft says {EnumSpelling.Of(drafts[i].Relation)}. " +
                    "One set of words cannot be both; withdraw the link that is wrong before loading the other.");
            }
        }

        await Copy(connection, drafts, fresh, ids, shapes, provenance, cancellationToken);
        await Join(connection, transaction, drafts, fresh, ids, provenance, cancellationToken);
        var isFresh = new bool[drafts.Count];
        fresh.ForEach(i => isFresh[i] = true);
        return new LinkWrite(ids, isFresh);
    }

    private static async Task Copy(
        NpgsqlConnection connection,
        IReadOnlyList<NewLink> drafts,
        List<int> fresh,
        long[] ids,
        Guid[] shapes,
        Dictionary<(string Source, string? Note), int> provenance,
        CancellationToken cancellationToken)
    {
        if (fresh.Count == 0)
        {
            return;
        }

        await using (var writer = await connection.BeginBinaryImportAsync(LinkImport, cancellationToken))
        {
            foreach (var i in fresh)
            {
                var draft = drafts[i];
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(ids[i], NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(draft.FromTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(draft.ToTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(EnumSpelling.Of(draft.Relation), NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(EnumSpelling.Of(draft.Method), NpgsqlDbType.Text, cancellationToken);
                await WriteConfidence(writer, draft.Confidence, cancellationToken);
                await writer.WriteAsync(provenance[(draft.Source, draft.Note)], NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(shapes[i], NpgsqlDbType.Uuid, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        var from = EnumSpelling.Of(LinkSide.From);
        var to = EnumSpelling.Of(LinkSide.To);
        await using (var writer = await connection.BeginBinaryImportAsync(LinkWordImport, cancellationToken))
        {
            foreach (var i in fresh)
            {
                foreach (var word in drafts[i].From.Distinct())
                {
                    await Row(writer, ids[i], word, from, cancellationToken);
                }

                foreach (var word in drafts[i].To.Distinct())
                {
                    await Row(writer, ids[i], word, to, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        // The claim that says this loader is the one asserting the link. A link with no claim is
        // invisible to the agreement measure, and a claim with no link is refused by the foreign
        // key, so the two arrive together or not at all.
        await using (var writer = await connection.BeginBinaryImportAsync(ClaimImport, cancellationToken))
        {
            foreach (var i in fresh)
            {
                var draft = drafts[i];
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(ids[i], NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(EnumSpelling.Of(draft.Method), NpgsqlDbType.Text, cancellationToken);
                await WriteConfidence(writer, draft.Confidence, cancellationToken);
                await writer.WriteAsync(provenance[(draft.Source, draft.Note)], NpgsqlDbType.Integer, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }
    }

    private static async Task Join(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        IReadOnlyList<NewLink> drafts,
        List<int> fresh,
        long[] ids,
        Dictionary<(string Source, string? Note), int> provenance,
        CancellationToken cancellationToken)
    {
        var written = fresh.ToHashSet();
        var joined = Enumerable.Range(0, drafts.Count).Where(i => !written.Contains(i)).ToList();
        if (joined.Count == 0)
        {
            return;
        }

        var links = joined.Select(i => ids[i]).ToArray();
        var methods = joined.Select(i => EnumSpelling.Of(drafts[i].Method)).ToArray();
        var confidences = joined.Select(i => drafts[i].Confidence).ToArray();
        var provenances = joined.Select(i => provenance[(drafts[i].Source, drafts[i].Note)]).ToArray();

        await using (var command = new NpgsqlCommand(JoinedClaims, connection, transaction))
        {
            command.Parameters.AddWithValue("links", links);
            command.Parameters.AddWithValue("methods", methods);
            command.Parameters.Add(new NpgsqlParameter<double?[]>("confidences", confidences));
            command.Parameters.AddWithValue("provenances", provenances);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // One promotion per link, the strongest draft's, so two drafts on one link cannot both claim it.
        var strongest = joined
            .GroupBy(i => ids[i])
            .Select(group => group.MaxBy(i => ClaimStanding.Of(drafts[i].Method)))
            .ToList();
        await using (var command = new NpgsqlCommand(Promote, connection, transaction))
        {
            command.Parameters.AddWithValue("links", strongest.Select(i => ids[i]).ToArray());
            command.Parameters.AddWithValue("methods", strongest.Select(i => EnumSpelling.Of(drafts[i].Method)).ToArray());
            command.Parameters.Add(new NpgsqlParameter<double?[]>(
                "confidences", strongest.Select(i => drafts[i].Confidence).ToArray()));
            command.Parameters.AddWithValue(
                "provenances", strongest.Select(i => provenance[(drafts[i].Source, drafts[i].Note)]).ToArray());
            command.Parameters.AddWithValue(
                "standings", strongest.Select(i => ClaimStanding.Of(drafts[i].Method)).ToArray());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task WriteConfidence(
        NpgsqlBinaryImporter writer,
        double? confidence,
        CancellationToken cancellationToken)
    {
        if (confidence is { } value)
        {
            await writer.WriteAsync(value, NpgsqlDbType.Double, cancellationToken);
        }
        else
        {
            await writer.WriteNullAsync(cancellationToken);
        }
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

    /// <summary>
    /// COPY cannot report the keys it generated, so a block of them is taken from the identity
    /// sequence up front and the rows are written with the ids already known.
    /// </summary>
    public static async Task<long> ReserveIds(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        int count,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT setval(pg_get_serial_sequence('link', 'id'), " +
            "coalesce((SELECT max(id) FROM link), 0) + @count) - @count + 1", connection, transaction);
        command.Parameters.AddWithValue("count", count);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
