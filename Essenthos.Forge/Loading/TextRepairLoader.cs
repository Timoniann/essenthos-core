using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.XmlBible;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading;

/// <param name="Verses">Verses whose words were changed.</param>
/// <param name="Rewritten">Words corrected in place — their letters, their capitals or what follows them — keeping their rows.</param>
/// <param name="Removed">Words the file prints and the edition does not, removed with the links they stood in alone.</param>
/// <param name="Added">Words written where the file lacked them, standing in no link yet.</param>
/// <param name="Moved">Words the file prints in another order, moved with their rows.</param>
/// <param name="AddedWords">The rows <paramref name="Added"/> counts, for a link pass that reaches them.</param>
/// <param name="Reshaped">The addresses of the verses that gained or lost a word.</param>
internal sealed record TextRepairOutcome(
    string Slug,
    int Verses,
    int Rewritten,
    int Removed,
    int Added,
    int Moved,
    IReadOnlyList<long> AddedWords,
    IReadOnlyList<(int Book, int Chapter, int Verse)> Reshaped,
    TimeSpan Elapsed)
{
    public override string ToString() => Verses == 0
        ? $"{Slug} already reads as its corrections say"
        : $"{Slug}: {Verses} verses corrected — {Rewritten} words rewritten in place, {Removed} removed, {Added} added and " +
          $"{Moved} moved, {Reshaped.Count} verses gaining or losing a word — in {Elapsed}";
}

/// <summary>
/// Makes a text's <see cref="TextRepairs"/> on a corpus that loaded the text before them.
///
/// <para>
/// A cold load reads the corrected verses straight from the reader, and this finds every one of
/// them already right. A warm one holds the verses as the file printed them, and the corpus loader
/// does not load a text twice, so they are corrected here in place. The two readings of a verse are
/// aligned word by word, case aside: a word both have keeps its row and whatever stands on it, with
/// its letters, capitals and punctuation brought to the edition's; a word only the file has goes,
/// with the links it stood in alone; a word only the edition has is written with no link, which is
/// what it is until a link pass reaches it; and a word the two print in another order is moved with
/// its row. Where the two readings differ by as many words as each other at one place, they are
/// the same words misprinted and are corrected in place — <c>произщшли</c> is <c>произошли</c>, not
/// a word removed and another added.
/// </para>
///
/// <para>
/// It is guarded on what the verse says. Its words have to read as the file prints the verse or as
/// the correction does, anywhere in it — a psalm's first verse may have gained its superscription at
/// its head and an epistle's last its subscription at its end — and a verse reading as neither stops
/// the pass, because then the corpus holds a text nobody here has read.
/// </para>
/// </summary>
internal sealed class TextRepairLoader(AppDbContext db, ILogger<TextRepairLoader> logger)
{
    /// <summary>
    /// The links a word about to go stands in alone on its side. The row cascades out of every link,
    /// and a link left with words on one side only would read as a statement that those words have
    /// no counterpart, which nobody made.
    /// </summary>
    private const string Unlink =
        """
        DELETE FROM link l
        USING link_word lw
        WHERE lw.link_id = l.id AND lw.word_id = ANY(@ids)
          AND NOT EXISTS (SELECT 1 FROM link_word other
                          WHERE other.link_id = l.id AND other.side = lw.side AND NOT (other.word_id = ANY(@ids)));
        """;

    private const string SuppliedGroups =
        """
        SELECT DISTINCT g.id FROM word_group g JOIN word_group_word gw ON gw.word_group_id = g.id
        WHERE gw.word_id = ANY(@ids) AND g.kind = @kind
        """;

    private const string Remove = "DELETE FROM word WHERE id = ANY(@ids)";

    /// <summary>
    /// Every position of the verse out of the way before the words are placed again. Negative first,
    /// as the Swete restoration does, so the unique index on (verse, position) holds after each statement.
    /// </summary>
    private const string FreeThePositions = """UPDATE word SET "position" = -"position" WHERE verse_id = @verseId""";

    private const string Place =
        """
        UPDATE word w SET "position" = p.position
        FROM unnest(@ids, @positions) AS p(id, position) WHERE w.id = p.id
        """;

    private const string Rewrite =
        """
        UPDATE word w SET "text" = r.surface, trailer = r.trailer, normalised_text = r.normalised
        FROM unnest(@ids, @surfaces, @trailers, @normalised) AS r(id, surface, trailer, normalised) WHERE w.id = r.id
        """;

    private const string Join = "INSERT INTO word_group_word (word_group_id, word_id) VALUES (@group, @word)";

    private const string Rebuild =
        """
        SELECT string_agg("text" || trailer, '' ORDER BY "position") FROM word WHERE verse_id = @verseId
        """;

    public async Task<TextRepairOutcome> Load(TextRepairs repairs, CancellationToken cancellationToken = default)
    {
        var text = await db.Texts.FirstOrDefaultAsync(t => t.Slug == repairs.Slug, cancellationToken);
        if (text is null || repairs.Verses.Count == 0)
        {
            return new TextRepairOutcome(repairs.Slug, 0, 0, 0, 0, 0, [], [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var stored = await Stored(text.Id, repairs.Verses, cancellationToken);
        var rewrites = new List<(long Id, VerseToken Token)>();
        var added = new List<long>();
        var reshaped = new List<(int, int, int)>();
        int verses = 0, removed = 0, moved = 0;

        foreach (var repair in repairs.Verses)
        {
            var address = $"{repairs.Slug} {BookReferences.Name(repair.Book)} {repair.Chapter}:{repair.Verse}";
            if (!stored.TryGetValue((repair.Book, repair.Chapter, repair.Verse), out var words))
            {
                // A corpus loaded without the book, which only a test builds.
                continue;
            }

            var after = repair.After;
            if (Find(words, after) >= 0)
            {
                continue;
            }

            var before = repair.Before;
            var at = Find(words, before);
            if (at < 0)
            {
                throw new InvalidOperationException(
                    $"{address} reads \"{string.Concat(words.Select(w => w.Surface + w.Trailer))}\", which is neither the " +
                    $"verse its file prints nor its correction: \"{repair.Printed}\". The corpus holds a text loaded from " +
                    "another file; load it again from this one before correcting anything in it. Nothing was changed.");
            }

            var plan = Plan(words.Skip(at).Take(before.Count).ToList(), after);
            verses++;
            removed += plan.Removed.Count;
            moved += plan.Moved;
            foreach (var (id, token) in plan.Rewritten)
            {
                rewrites.Add((id, token));
            }

            if (plan.Removed.Count == 0 && plan.Moved == 0 && plan.Order.All(slot => slot.Id is not null))
            {
                continue;
            }

            var verseId = words[0].VerseId;
            var groups = plan.Removed.Count == 0
                ? []
                : await Groups(plan.Removed, cancellationToken);
            if (plan.Removed.Count > 0)
            {
                await Execute(Unlink, cancellationToken, ("ids", plan.Removed.ToArray()));
                await Execute(Remove, cancellationToken, ("ids", plan.Removed.ToArray()));
            }

            await Execute(FreeThePositions, cancellationToken, ("verseId", verseId));
            var order = words.Take(at).Select(w => (long?)w.Id)
                .Concat(plan.Order.Select(slot => slot.Id))
                .Concat(words.Skip(at + before.Count).Select(w => (long?)w.Id))
                .ToList();
            var ids = new List<long>();
            var positions = new List<int>();
            var written = new List<(Word Row, VerseToken Token)>();
            for (var i = 0; i < order.Count; i++)
            {
                if (order[i] is { } id)
                {
                    ids.Add(id);
                    positions.Add(i + 1);
                    continue;
                }

                var token = plan.Order[i - at].Token;
                var row = new Word
                {
                    TextId = text.Id,
                    VerseId = verseId,
                    Position = i + 1,
                    Surface = token.Word,
                    Trailer = token.Trailer,
                    Elided = token.Word.Length == 0,
                    NormalisedText = WordFolding.Fold(token.Word, text.Language),
                };
                db.Words.Add(row);
                written.Add((row, token));
            }

            await Execute(Place, cancellationToken, ("ids", ids.ToArray()), ("positions", positions.ToArray()));
            await db.SaveChangesAsync(cancellationToken);
            await Supply(written, groups, address, cancellationToken);

            added.AddRange(written.Select(w => w.Row.Id));
            if (written.Count > 0 || plan.Removed.Count > 0)
            {
                reshaped.Add((repair.Book, repair.Chapter, repair.Verse));
            }
        }

        await RewriteInPlace(rewrites, text.Language, cancellationToken);
        await EnsureRebuilds(repairs, stored, cancellationToken);

        if (text.RightsNote?.Contains(repairs.Note, StringComparison.Ordinal) != true && repairs.Note.Length > 0)
        {
            text.RightsNote = text.RightsNote is { Length: > 0 } existing ? $"{existing} {repairs.Note}" : repairs.Note;
        }

        if (repairs.Source is { } source)
        {
            TextPartSources.Add(text, source);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new TextRepairOutcome(
            repairs.Slug, verses, rewrites.Count, removed, added.Count, moved, added, reshaped, started.Elapsed);
        logger.LogInformation("Corrections: {Outcome}", outcome);
        return outcome;
    }

    private sealed record StoredWord(long Id, int VerseId, int Position, string Surface, string Trailer);

    /// <param name="Id">The row the word keeps, or null for a word written afresh.</param>
    private sealed record Slot(long? Id, VerseToken Token);

    /// <param name="Order">The corrected verse's words in order, each with the row it keeps.</param>
    private sealed record RepairPlan(
        List<Slot> Order,
        List<(long Id, VerseToken Token)> Rewritten,
        List<long> Removed,
        int Moved);

    /// <summary>
    /// The words of the file's reading against the edition's, aligned on the words themselves with
    /// case set aside; then, between two aligned words, as many on each side are the same words
    /// misprinted, and a word left over on both sides with the same letters is one moved.
    /// </summary>
    private static RepairPlan Plan(List<StoredWord> stored, List<VerseToken> after)
    {
        var matched = Lcs(stored.Select(w => Key(w.Surface)).ToList(), after.Select(t => Key(t.Word)).ToList());
        var kept = new long?[after.Count];
        var gone = new List<StoredWord>();

        var i = 0;
        var j = 0;
        foreach (var (mi, mj) in matched.Append((stored.Count, after.Count)))
        {
            var old = stored.Skip(i).Take(mi - i).ToList();
            var now = Enumerable.Range(j, mj - j).ToList();
            if (old.Count == now.Count)
            {
                for (var k = 0; k < old.Count; k++)
                {
                    kept[now[k]] = old[k].Id;
                }
            }
            else if (now.Count == 1 && old.Count > 1
                     && Letters(string.Concat(old.Select(w => w.Surface))) == Letters(after[now[0]].Word))
            {
                // A word the file printed in pieces, which the edition prints whole: it is the first
                // piece's word, and that row keeps what stands on it.
                kept[now[0]] = old[0].Id;
                gone.AddRange(old.Skip(1));
            }
            else
            {
                gone.AddRange(old);
            }

            if (mi < stored.Count)
            {
                kept[mj] = stored[mi].Id;
            }

            i = mi + 1;
            j = mj + 1;
        }

        var moved = 0;
        for (var at = 0; at < after.Count; at++)
        {
            if (kept[at] is null && gone.FirstOrDefault(w => Key(w.Surface) == Key(after[at].Word)) is { } same)
            {
                kept[at] = same.Id;
                gone.Remove(same);
                moved++;
            }
        }

        var byId = stored.ToDictionary(w => w.Id);
        var rewritten = new List<(long, VerseToken)>();
        for (var at = 0; at < after.Count; at++)
        {
            if (kept[at] is { } id && (byId[id].Surface != after[at].Word || byId[id].Trailer != after[at].Trailer))
            {
                rewritten.Add((id, after[at]));
            }
        }

        return new RepairPlan(
            [.. after.Select((token, at) => new Slot(kept[at], token))],
            rewritten,
            [.. gone.Select(w => w.Id)],
            moved);
    }

    private static string Key(string word) => word.ToLowerInvariant();

    private static string Letters(string word) => string.Concat(word.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    /// <summary>
    /// Where a verse's words read as these, or -1. The last word's trailer is compared without its
    /// trailing space, which a pass that wrote words after it adds.
    /// </summary>
    private static int Find(List<StoredWord> words, List<VerseToken> tokens)
    {
        for (var at = 0; at + tokens.Count <= words.Count; at++)
        {
            var same = true;
            for (var k = 0; k < tokens.Count && same; k++)
            {
                var word = words[at + k];
                same = word.Surface == tokens[k].Word
                       && (word.Trailer == tokens[k].Trailer
                           || (k == tokens.Count - 1 && word.Trailer.TrimEnd() == tokens[k].Trailer.TrimEnd()));
            }

            if (same)
            {
                return at;
            }
        }

        return -1;
    }

    private static List<(int, int)> Lcs(IReadOnlyList<string> one, IReadOnlyList<string> other)
    {
        var lengths = new int[one.Count + 1, other.Count + 1];
        for (var i = one.Count - 1; i >= 0; i--)
        {
            for (var j = other.Count - 1; j >= 0; j--)
            {
                lengths[i, j] = one[i] == other[j]
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var pairs = new List<(int, int)>();
        for (int i = 0, j = 0; i < one.Count && j < other.Count;)
        {
            if (one[i] == other[j])
            {
                pairs.Add((i++, j++));
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return pairs;
    }

    /// <summary>The words of every verse a correction names, by address, in order.</summary>
    private async Task<Dictionary<(int, int, int), List<StoredWord>>> Stored(
        int textId,
        IReadOnlyList<VerseRepair> repairs,
        CancellationToken cancellationToken)
    {
        var wanted = repairs.Select(r => (r.Book, r.Chapter, r.Verse)).ToHashSet();
        var verses = (await db.Verses
                .Where(v => v.TextId == textId && v.Label == string.Empty)
                .Select(v => new { v.Id, v.Book!.CanonicalOrdinal, v.ChapterNumber, v.Number })
                .ToListAsync(cancellationToken))
            .Where(v => wanted.Contains((v.CanonicalOrdinal, v.ChapterNumber, v.Number)))
            .ToDictionary(v => v.Id, v => (v.CanonicalOrdinal, v.ChapterNumber, v.Number));

        var ids = verses.Keys.ToList();
        var words = await db.Words
            .Where(w => ids.Contains(w.VerseId))
            .Select(w => new StoredWord(w.Id, w.VerseId, w.Position, w.Surface, w.Trailer))
            .ToListAsync(cancellationToken);

        return words
            .GroupBy(w => w.VerseId)
            .ToDictionary(verse => verses[verse.Key], verse => verse.OrderBy(w => w.Position).ToList());
    }

    /// <summary>The spans of words the edition supplies that the words about to go stand in.</summary>
    private async Task<List<long>> Groups(List<long> removed, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(SuppliedGroups, connection,
            (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
        command.Parameters.AddWithValue("ids", removed.ToArray());
        command.Parameters.AddWithValue("kind", EnumSpelling.Of(WordGroupKind.Supplied));

        var groups = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            groups.Add(reader.GetInt64(0));
        }

        return groups;
    }

    /// <summary>
    /// A word written inside the edition's brackets joins the span the word it replaces stood in:
    /// the brackets are the edition's statement about those letters, whichever way they are divided.
    /// </summary>
    private async Task Supply(
        List<(Word Row, VerseToken Token)> written,
        List<long> groups,
        string address,
        CancellationToken cancellationToken)
    {
        foreach (var (row, token) in written.Where(w => w.Token.SuppliedSpan is not null))
        {
            if (groups.Count != 1)
            {
                throw new InvalidOperationException(
                    $"{address}: \"{token.Word}\" stands inside the edition's brackets, and the words it replaces stood " +
                    $"in {groups.Count} supplied spans rather than one, so which it belongs to is not settled. Correct " +
                    "the verse by hand. Nothing was changed.");
            }

            await Execute(Join, cancellationToken, ("group", groups[0]), ("word", row.Id));
        }
    }

    private async Task RewriteInPlace(
        List<(long Id, VerseToken Token)> rewrites,
        string language,
        CancellationToken cancellationToken)
    {
        if (rewrites.Count == 0)
        {
            return;
        }

        await Execute(Rewrite, cancellationToken,
            ("ids", rewrites.Select(r => r.Id).ToArray()),
            ("surfaces", rewrites.Select(r => r.Token.Word).ToArray()),
            ("trailers", rewrites.Select(r => r.Token.Trailer).ToArray()),
            ("normalised", rewrites.Select(r => WordFolding.Fold(r.Token.Word, language)).ToArray()));
    }

    /// <summary>
    /// Every corrected verse must read as its correction, checked inside the transaction, so a verse
    /// that does not leaves nothing behind.
    /// </summary>
    private async Task EnsureRebuilds(
        TextRepairs repairs,
        Dictionary<(int, int, int), List<StoredWord>> stored,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        foreach (var repair in repairs.Verses)
        {
            if (!stored.TryGetValue((repair.Book, repair.Chapter, repair.Verse), out var words))
            {
                continue;
            }

            await using var command = new NpgsqlCommand(Rebuild, connection,
                (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
            command.Parameters.AddWithValue("verseId", words[0].VerseId);
            var rebuilt = (await command.ExecuteScalarAsync(cancellationToken)) as string ?? string.Empty;
            var printed = string.Concat(repair.After.Select(t => t.Word + t.Trailer)).TrimEnd();
            if (!rebuilt.Contains(printed, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{repairs.Slug} {BookReferences.Name(repair.Book)} {repair.Chapter}:{repair.Verse} reads \"{rebuilt}\" " +
                    $"after its correction, where \"{printed}\" was written. The words went to the wrong places; the " +
                    "transaction is rolled back, so nothing was changed.");
            }
        }
    }

    private async Task Execute(string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(sql, connection,
            (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
