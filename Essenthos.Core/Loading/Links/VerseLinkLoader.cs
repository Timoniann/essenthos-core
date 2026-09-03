using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Straight">Verse pairs that are one verse against one verse.</param>
/// <param name="Divided">
/// Pairs where one side says in several verses what the other says in one, or the two divide a
/// passage differently. These are the ones worth having: a word link inside them crosses a verse
/// boundary legitimately, and without the verse link there is nothing to say so.
/// </param>
/// <param name="Alone">
/// Verses on one side that no verse of the other answers. Counted and not written: a verse absent
/// from the other text is not a correspondence, and writing it as one would assert an absence the
/// frame does not state.
/// </param>
internal sealed record VerseLinkOutcome(
    bool AlreadyLoaded,
    int Pairs,
    int Links,
    int Straight,
    int Divided,
    int Alone,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the verse links are already loaded"
            : $"{Links} verse links over {Pairs} text pairs in {Elapsed}: {Straight} one verse against " +
              $"one, {Divided} where the two divide the passage differently, {Alone} verses with no " +
              "counterpart at all";
}

/// <summary>
/// Which verse of one text is which verse of another — the statement one level above a word link,
/// and the one that has to exist first.
///
/// DOC-0007 declares <c>verse_link</c> and nothing was writing it. That is not a cosmetic hole. A
/// word link may name words in two verses on purpose, because that is how *the word ended up
/// elsewhere* is said, and the verification check that separates a legitimate crossing from a wrong
/// one asks whether a verse link joins the two verses. With the table empty the check could only
/// ever report every crossing as a fault, so it was measuring the emptiness of this table rather
/// than the correctness of the links.
///
/// <para>
/// The first crossings it found, on 2026-09-03, turned out to be wrong links rather than legitimate
/// ones: Nestle's Philippians 1:16 and 1:17 stand in the opposite order to the Textus Receptus, the
/// frame learned to say so, and the word links between the two editions had been built under the
/// older frame and still paired 1:17 with 1:17. Deleting and rebuilding them is what fixed those.
/// This table is what will tell the difference the next time — and it is the verse-level
/// correspondence in its own right, which is the layer word alignment rests on.
/// </para>
///
/// <para>
/// It is derived from the canonical frame and then stored, which is what DOC-0007 asks for. Two
/// verses correspond when they stand at the same canonical address, and the correspondence is
/// transitive through the addresses they share: where one text divides a passage into two verses
/// and another into three, all five belong to one link rather than to some arbitrary pairing of
/// them. So this walks the shared addresses as a graph and takes its connected components, and a
/// component is one link naming a set against a set — the same shape as <c>link</c>, one level up.
/// </para>
///
/// <para>
/// The method is <c>stated-by-source</c> and the confidence is therefore null, because the frame
/// comes from the versification data rather than from anything we inferred. Where a text's own
/// numbering has been identified rather than declared, that identification is itself stated: the
/// conditions it was tested against are in the file.
/// </para>
///
/// <para>
/// Pairs come from the word links that exist, so a pair the aligner has not run on yet has no verse
/// links either. That is deliberate — the table answers *is this crossing legitimate*, and a
/// question nobody has asked needs no answer. It also means the loader has to run after the
/// alignment commands rather than before, which it does: it is idempotent per pair, so the next
/// start picks up whatever a `compose` or an `align` added.
/// </para>
/// </summary>
internal sealed class VerseLinkLoader(AppDbContext db, ILogger<VerseLinkLoader> logger)
{
    private const string Source = "the canonical frame: the addresses the two texts share";

    private const string LinkImport =
        """
        COPY verse_link (id, from_text_id, to_text_id, relation, method, confidence, source)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string VerseImport =
        "COPY verse_link_verse (verse_link_id, verse_id, side) FROM STDIN (FORMAT BINARY)";

    public async Task<VerseLinkOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();

        var wanted = await db.Links
            .Select(link => new { link.FromTextId, link.ToTextId })
            .Distinct()
            .ToListAsync(cancellationToken);

        var already = await db.VerseLinks
            .Select(link => new { link.FromTextId, link.ToTextId })
            .Distinct()
            .ToListAsync(cancellationToken);

        var todo = wanted
            .Where(pair => !already.Any(had => had.FromTextId == pair.FromTextId && had.ToTextId == pair.ToTextId))
            .OrderBy(pair => pair.FromTextId).ThenBy(pair => pair.ToTextId)
            .ToList();

        if (todo.Count == 0)
        {
            logger.LogInformation("Every linked pair already has its verse links; nothing to do");
            return new VerseLinkOutcome(true, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var addresses = new Dictionary<int, Dictionary<(int, int, int), List<int>>>();
        int straight = 0, divided = 0, alone = 0, written = 0;

        foreach (var pair in todo)
        {
            var here = await Addressed(addresses, pair.FromTextId, cancellationToken);
            var there = await Addressed(addresses, pair.ToTextId, cancellationToken);
            var components = Components(here, there, ref alone);

            straight += components.Count(c => c.From.Count == 1 && c.To.Count == 1);
            divided += components.Count(c => c.From.Count != 1 || c.To.Count != 1);
            written += components.Count;

            await Write(pair.FromTextId, pair.ToTextId, components, cancellationToken);
        }

        var outcome = new VerseLinkOutcome(
            false, todo.Count, written, straight, divided, alone, started.Elapsed);
        logger.LogInformation("Verse links: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The connected components of the graph whose edges are *these two verses share a canonical
    /// address*.
    ///
    /// Union-find rather than a pairwise join, because the relation is transitive and a pairwise
    /// join would cut it: if the one text's verse 12 covers the other's 12 and 13, and its verse 13
    /// covers the other's 13 as well, all four are one statement. Taking them two at a time would
    /// write three links that each contradict the others about what corresponds to what.
    /// </summary>
    internal static List<Component> Components(
        Dictionary<(int, int, int), List<int>> here,
        Dictionary<(int, int, int), List<int>> there,
        ref int alone)
    {
        var parent = new Dictionary<long, long>();
        var side = new Dictionary<long, LinkSide>();

        // A verse is only *missing a counterpart* inside a book the other text has. Without this
        // the count is dominated by pairs that cover one testament, and reads as thirty thousand
        // faults where the truth is that the King James New Testament is not in BHSA.
        var shared = here.Keys.Select(address => address.Item1)
            .Intersect(there.Keys.Select(address => address.Item1))
            .ToHashSet();
        var books = new Dictionary<long, int>();

        long Key(int verseId, LinkSide which) => which == LinkSide.From ? verseId : -(long)verseId;

        long Find(long node)
        {
            while (parent[node] != node)
            {
                parent[node] = parent[parent[node]];
                node = parent[node];
            }

            return node;
        }

        void Add(int verseId, LinkSide which)
        {
            var key = Key(verseId, which);
            if (parent.TryAdd(key, key))
            {
                side[key] = which;
            }
        }

        void Union(long left, long right)
        {
            var a = Find(left);
            var b = Find(right);
            if (a != b)
            {
                parent[a] = b;
            }
        }

        // Every verse of both texts is a node, including the ones the other text never answers, so
        // that a verse with no counterpart is counted rather than quietly absent.
        foreach (var (address, verses) in here)
        {
            foreach (var verse in verses)
            {
                Add(verse, LinkSide.From);
                books[Key(verse, LinkSide.From)] = address.Item1;
            }
        }

        foreach (var (address, verses) in there)
        {
            foreach (var verse in verses)
            {
                Add(verse, LinkSide.To);
                books[Key(verse, LinkSide.To)] = address.Item1;
            }
        }

        foreach (var (address, mine) in here)
        {
            if (!there.TryGetValue(address, out var theirs))
            {
                continue;
            }

            // Every verse standing at this address is one statement, whichever text it belongs to.
            foreach (var verse in mine.Skip(1))
            {
                Union(Key(mine[0], LinkSide.From), Key(verse, LinkSide.From));
            }

            foreach (var verse in theirs)
            {
                Union(Key(mine[0], LinkSide.From), Key(verse, LinkSide.To));
            }
        }

        var grouped = new Dictionary<long, Component>();
        foreach (var node in parent.Keys)
        {
            var root = Find(node);
            if (!grouped.TryGetValue(root, out var component))
            {
                component = new Component([], []);
                grouped[root] = component;
            }

            (side[node] == LinkSide.From ? component.From : component.To).Add((int)Math.Abs(node));
        }

        var complete = new List<Component>(grouped.Count);
        foreach (var (root, component) in grouped)
        {
            if (component.From.Count > 0 && component.To.Count > 0)
            {
                complete.Add(component);
            }
            else if (shared.Contains(books[root]))
            {
                alone++;
            }
        }

        return complete;
    }

    /// <summary>
    /// Every verse of a text by the canonical address it primarily stands at, cached because a text
    /// takes part in several pairs and the query is the expensive half of this.
    /// </summary>
    private async Task<Dictionary<(int, int, int), List<int>>> Addressed(
        Dictionary<int, Dictionary<(int, int, int), List<int>>> cache,
        int textId,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(textId, out var known))
        {
            return known;
        }

        var rows = await db.VerseReferences
            .Where(reference => reference.IsPrimary && reference.Verse!.TextId == textId)
            .Select(reference => new
            {
                reference.CanonicalBook,
                reference.CanonicalChapter,
                reference.CanonicalVerse,
                reference.VerseId,
            })
            .ToListAsync(cancellationToken);

        var addressed = rows
            .GroupBy(row => (row.CanonicalBook, row.CanonicalChapter, row.CanonicalVerse))
            .ToDictionary(group => group.Key, group => group.Select(row => row.VerseId).Distinct().ToList());

        cache[textId] = addressed;
        return addressed;
    }

    private async Task Write(
        int fromTextId,
        int toTextId,
        List<Component> components,
        CancellationToken cancellationToken)
    {
        if (components.Count == 0)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var firstId = await ReserveIds(connection, components.Count, cancellationToken);
        var relation = EnumSpelling.Of(LinkRelation.Equals);
        var method = EnumSpelling.Of(LinkMethod.StatedBySource);

        await using (var writer = await connection.BeginBinaryImportAsync(LinkImport, cancellationToken))
        {
            for (var i = 0; i < components.Count; i++)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(firstId + i, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(fromTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(toTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(relation, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteNullAsync(cancellationToken);
                await writer.WriteAsync(Source, NpgsqlDbType.Text, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        var fromSide = EnumSpelling.Of(LinkSide.From);
        var toSide = EnumSpelling.Of(LinkSide.To);

        await using (var writer = await connection.BeginBinaryImportAsync(VerseImport, cancellationToken))
        {
            for (var i = 0; i < components.Count; i++)
            {
                foreach (var verse in components[i].From)
                {
                    await Row(writer, firstId + i, verse, fromSide, cancellationToken);
                }

                foreach (var verse in components[i].To)
                {
                    await Row(writer, firstId + i, verse, toSide, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task Row(
        NpgsqlBinaryImporter writer,
        int verseLinkId,
        int verseId,
        string side,
        CancellationToken cancellationToken)
    {
        await writer.StartRowAsync(cancellationToken);
        await writer.WriteAsync(verseLinkId, NpgsqlDbType.Integer, cancellationToken);
        await writer.WriteAsync(verseId, NpgsqlDbType.Integer, cancellationToken);
        await writer.WriteAsync(side, NpgsqlDbType.Text, cancellationToken);
    }

    private static async Task<int> ReserveIds(
        NpgsqlConnection connection,
        int count,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT setval(pg_get_serial_sequence('verse_link', 'id'), " +
            "coalesce((SELECT max(id) FROM verse_link), 0) + @count) - @count + 1", connection);
        command.Parameters.AddWithValue("count", count);
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    internal sealed record Component(List<int> From, List<int> To);
}
