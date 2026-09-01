using System.Diagnostics;
using System.Text.Json;
using Essenthos.Core.Bhsa;
using Essenthos.Core.Bhsa.Core;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading;

/// <param name="Orphans">
/// Groups whose kind has something above it and that no group of that kind contains. A sentence
/// crossing a verse boundary or a phrase the analysis leaves outside every clause is not an error —
/// but the number is worth knowing, because a large one would mean the nesting was derived wrongly.
/// </param>
internal sealed record SyntaxOutcome(
    bool AlreadyLoaded,
    int Groups,
    int Memberships,
    int Orphans,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the syntax is already loaded"
            : $"{Groups} word groups and {Memberships} memberships in {Elapsed}, " +
              $"{Orphans} outside every group of the kind above them";
}

/// <summary>
/// BHSA's syntax: 88,131 clauses, 253,203 phrases and the six other kinds of span its analysis
/// names, none of which any endpoint could reach before.
///
/// It is the largest single thing BHSA is worth having. <em>Show me every clause where this verb
/// governs this noun</em> is a research question no free site answers, and the data has been sitting
/// parsed in the resources the whole time.
///
/// The nesting is derived rather than read, because the parsed records carry their words and not
/// their parents. A group's parent is the group of the kind above it that contains its first word —
/// which works because every BHSA span is a contiguous run of slots, so containing the first word
/// means containing the group.
/// </summary>
internal sealed class SyntaxLoader(AppDbContext db, ILogger<SyntaxLoader> logger)
{
    private const string GroupImport =
        """
        COPY word_group (id, text_id, kind, parent_id, "position", features)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string MembershipImport =
        "COPY word_group_word (word_group_id, word_id) FROM STDIN (FORMAT BINARY)";

    /// <summary>
    /// What sits inside what. Read in order: each kind's parent is the one before it that exists.
    /// A half verse is the Masoretic division of a verse and belongs to no syntactic span, so it
    /// has nothing above it.
    /// </summary>
    private static readonly Dictionary<WordGroupKind, WordGroupKind?> Above = new()
    {
        [WordGroupKind.Sentence] = null,
        [WordGroupKind.SentenceAtom] = WordGroupKind.Sentence,
        [WordGroupKind.Clause] = WordGroupKind.SentenceAtom,
        [WordGroupKind.ClauseAtom] = WordGroupKind.Clause,
        [WordGroupKind.Phrase] = WordGroupKind.ClauseAtom,
        [WordGroupKind.PhraseAtom] = WordGroupKind.Phrase,
        [WordGroupKind.Subphrase] = WordGroupKind.PhraseAtom,
        [WordGroupKind.HalfVerse] = null,
    };

    public async Task<SyntaxOutcome> Load(
        BhsaProject project,
        string slug,
        CancellationToken cancellationToken = default)
    {
        var text = await db.Texts.SingleOrDefaultAsync(t => t.Slug == slug, cancellationToken)
                   ?? throw new InvalidOperationException(
                       $"The text \"{slug}\" must be loaded before its syntax can be. This reads its words; it " +
                       "does not create them.");

        if (await db.WordGroups.AnyAsync(g => g.TextId == text.Id, cancellationToken))
        {
            logger.LogInformation("The syntax of {Slug} is already loaded; nothing to do", slug);
            return new SyntaxOutcome(true, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var words = await WordsBySlot(text.Id, cancellationToken);
        var drafts = Drafts(project, words);
        Count(drafts, project);

        var orphans = await Write(text.Id, drafts, cancellationToken);
        var outcome = new SyntaxOutcome(
            false, drafts.Count, drafts.Sum(d => d.Words.Count), orphans, started.Elapsed);

        logger.LogInformation("Loaded the syntax of {Slug}: {Outcome}", slug, outcome);
        return outcome;
    }

    /// <summary>
    /// What the source says it holds, against what is about to be written. A group whose every word
    /// also belongs to a deeper group of the same kind ends up with no words and is dropped
    /// silently — that is how 29,192 of BHSA's 113,850 subphrases went missing on the first run,
    /// and nothing but this count would have said so.
    /// </summary>
    private static void Count(List<GroupDraft> drafts, BhsaProject project)
    {
        (WordGroupKind Kind, int Expected)[] expected =
        [
            (WordGroupKind.Sentence, project.Sentences.Count),
            (WordGroupKind.SentenceAtom, project.SentenceAtoms.Count),
            (WordGroupKind.Clause, project.Clauses.Count),
            (WordGroupKind.ClauseAtom, project.ClauseAtoms.Count),
            (WordGroupKind.Phrase, project.Phrases.Count),
            (WordGroupKind.PhraseAtom, project.PhraseAtoms.Count),
            (WordGroupKind.Subphrase, project.Subphrases.Count),
            (WordGroupKind.HalfVerse, project.HalfVerses.Count),
        ];

        var built = drafts.GroupBy(draft => draft.Kind).ToDictionary(kind => kind.Key, kind => kind.Count());
        var short_ = expected
            .Select(kind => (kind.Kind, kind.Expected, Built: built.GetValueOrDefault(kind.Kind)))
            .Where(kind => kind.Built != kind.Expected)
            .ToList();

        if (short_.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "The syntax read from the source and the syntax about to be written do not agree: " +
            string.Join("; ", short_.Select(k => $"{k.Kind} {k.Built} of {k.Expected}")) +
            ". A group reaching the database with no words is a group the reader will never see, so " +
            "the load stops here rather than leaving the corpus quietly short.");
    }

    /// <summary>
    /// The corpus word behind each BHSA slot. BHSA numbers its words from one across the whole
    /// Bible and this corpus numbers them within a verse, so the two are joined by the order they
    /// were written in — which is the order the loader wrote them.
    /// </summary>
    private async Task<Dictionary<int, long>> WordsBySlot(int textId, CancellationToken cancellationToken)
    {
        var ordered = await db.Words
            .Where(w => w.TextId == textId)
            .OrderBy(w => w.Verse!.Book!.Position)
            .ThenBy(w => w.Verse!.ChapterNumber)
            .ThenBy(w => w.Verse!.Number)
            .ThenBy(w => w.Position)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);

        var bySlot = new Dictionary<int, long>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            bySlot[i + 1] = ordered[i];
        }

        return bySlot;
    }

    private static List<GroupDraft> Drafts(BhsaProject project, Dictionary<int, long> words)
    {
        var drafts = new List<GroupDraft>(1_000_000);

        Add(drafts, WordGroupKind.Sentence, project.Sentences,
            s => s.Words, _ => null);
        Add(drafts, WordGroupKind.SentenceAtom, project.SentenceAtoms,
            s => s.Words, _ => null);
        Add(drafts, WordGroupKind.Clause, project.Clauses, c => c.Words, c => Features(
            ("type", c.Type), ("kind", c.Kind.ToString()), ("relation", c.LinguisticRelation.ToString()),
            ("domain", c.Domain.ToString()), ("textTypes", string.Join(" ", c.TextTypes.Select(type => type.ToString())))));
        Add(drafts, WordGroupKind.ClauseAtom, project.ClauseAtoms,
            c => c.Words, _ => null);
        Add(drafts, WordGroupKind.Phrase, project.Phrases, p => p.Words, p => Features(
            ("type", p.Type.ToString()), ("function", p.Function.ToString()),
            ("determination", p.Determination.ToString()), ("relation", p.LinguisticRelation.ToString())));
        Add(drafts, WordGroupKind.PhraseAtom, project.PhraseAtoms,
            p => p.Words, _ => null);
        Add(drafts, WordGroupKind.Subphrase, project.Subphrases, s => s.Words, s => Features(
            ("relation", s.LinguisticRelation.ToString())));
        Add(drafts, WordGroupKind.HalfVerse, project.HalfVerses,
            h => h.Words, _ => null);

        Nest(drafts);
        return drafts;

        void Add<T>(
            List<GroupDraft> into,
            WordGroupKind kind,
            IReadOnlyList<T> groups,
            Func<T, IList<Bhsa.Core.Word>> members,
            Func<T, string?> features)
        {
            var position = 0;
            foreach (var group in groups)
            {
                var ids = members(group)
                    .Select(word => words.TryGetValue(word.SlotId, out var id) ? id : 0)
                    .Where(id => id != 0)
                    .ToList();

                if (ids.Count == 0)
                {
                    continue;
                }

                into.Add(new GroupDraft(kind, ++position, ids, features(group), members(group)[0].SlotId)
                {
                    Slots = [.. members(group).Select(word => word.SlotId)],
                });
            }
        }
    }

    /// <summary>
    /// Fills in each group's parent by asking which group of the kind above it holds its first
    /// word. Every BHSA span is a contiguous run of slots, so that one word settles it.
    /// </summary>
    private static void Nest(List<GroupDraft> drafts)
    {
        // Groups of one kind can overlap — subphrases nest inside each other — so a slot may be
        // covered several times over. The parent is the smallest of them: a phrase belongs to the
        // clause atom that holds it, not to whatever larger thing also happens to.
        var containing = new Dictionary<WordGroupKind, Dictionary<int, GroupDraft>>();
        foreach (var draft in drafts)
        {
            var byKind = containing.TryGetValue(draft.Kind, out var existing)
                ? existing
                : containing[draft.Kind] = [];

            foreach (var slot in draft.Slots)
            {
                if (!byKind.TryGetValue(slot, out var standing) || draft.Slots.Count < standing.Slots.Count)
                {
                    byKind[slot] = draft;
                }
            }
        }

        foreach (var draft in drafts)
        {
            if (Above[draft.Kind] is not { } above || !containing.TryGetValue(above, out var parents))
            {
                continue;
            }

            if (parents.TryGetValue(draft.FirstSlot, out var parent))
            {
                draft.Parent = parent;
            }
        }
    }

    /// <summary>
    /// BHSA writes "not applicable" and "unknown" as values rather than as absence — NA on a
    /// verbal phrase's determination, ? on a clause's domain. Storing them would make a reader
    /// filter them out of every answer, so they are dropped here once.
    /// </summary>
    private static readonly HashSet<string> NotAValue = ["None", "Unknown", "NA", "?", "none", "unknown"];

    private static string? Features(params (string Key, string? Value)[] pairs)
    {
        var present = pairs
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value) && !NotAValue.Contains(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value!);

        return present.Count == 0 ? null : JsonSerializer.Serialize(present);
    }

    private async Task<int> Write(int textId, List<GroupDraft> drafts, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var firstId = await ReserveIds(connection, drafts.Count, cancellationToken);
        for (var i = 0; i < drafts.Count; i++)
        {
            drafts[i].Id = firstId + i;
        }

        await using (var writer = await connection.BeginBinaryImportAsync(GroupImport, cancellationToken))
        {
            foreach (var draft in drafts)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(draft.Id, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(textId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(EnumSpelling.Of(draft.Kind), NpgsqlDbType.Text, cancellationToken);

                if (draft.Parent is { } parent)
                {
                    await writer.WriteAsync(parent.Id, NpgsqlDbType.Bigint, cancellationToken);
                }
                else
                {
                    await writer.WriteNullAsync(cancellationToken);
                }

                await writer.WriteAsync(draft.Position, NpgsqlDbType.Integer, cancellationToken);

                if (draft.Features is { } features)
                {
                    await writer.WriteAsync(features, NpgsqlDbType.Jsonb, cancellationToken);
                }
                else
                {
                    await writer.WriteNullAsync(cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(MembershipImport, cancellationToken))
        {
            foreach (var draft in drafts)
            {
                foreach (var word in draft.Words)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(draft.Id, NpgsqlDbType.Bigint, cancellationToken);
                    await writer.WriteAsync(word, NpgsqlDbType.Bigint, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return drafts.Count(draft => Above[draft.Kind] is not null && draft.Parent is null);
    }

    private static async Task<long> ReserveIds(
        NpgsqlConnection connection,
        int count,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT setval(pg_get_serial_sequence('word_group', 'id'), " +
            "coalesce((SELECT max(id) FROM word_group), 0) + @count) - @count + 1", connection);
        command.Parameters.AddWithValue("count", count);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private sealed record GroupDraft(
        WordGroupKind Kind,
        int Position,
        List<long> Words,
        string? Features,
        int FirstSlot)
    {
        public long Id { get; set; }

        public GroupDraft? Parent { get; set; }

        /// <summary>The BHSA slots this group covers, which is how a child finds it.</summary>
        public List<int> Slots { get; init; } = [];
    }
}
