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
/// Groups whose kind has something above it and that no group of that kind contains. Nothing in
/// BHSA is expected to be one — every clause is inside a sentence and every phrase inside a
/// clause, measured over the whole corpus — so this is the number that says the nesting was
/// derived wrongly if it ever stops being zero.
/// </param>
/// <param name="Mothers">
/// Groups whose analysis names what they stand in their relation to. 182,269 of BHSA's do.
/// </param>
internal sealed record SyntaxOutcome(
    bool AlreadyLoaded,
    int Groups,
    int Memberships,
    int Orphans,
    int Mothers,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the syntax is already loaded"
            : $"{Groups} word groups and {Memberships} memberships in {Elapsed}, " +
              $"{Mothers} with a mother, {Orphans} outside every group of the kind above them";
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
/// their parents: a group's parent is the shortest group of the kind above it whose words include
/// all of this one's. Containment is tested over the whole span and not over its first word,
/// because BHSA's linguistic spans are discontinuous — 2,454 clauses, 702 sentences and 672
/// phrases are split around something else — and a split span's first word sits in plenty of
/// groups that do not hold the rest of it.
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

    private const string MotherImport =
        """
        UPDATE word_group
        SET mother_group_id = edge.mother_group, mother_word_id = edge.mother_word
        FROM unnest(@ids, @groups, @words) AS edge(id, mother_group, mother_word)
        WHERE word_group.id = edge.id
        """;

    /// <summary>
    /// What sits inside what. BHSA is two hierarchies rather than one chain: the linguistic one —
    /// sentence, clause, phrase, subphrase — whose spans may be split around something else, and
    /// an atom level under each, which is contiguous by construction. So a clause belongs to a
    /// sentence and not to a sentence atom, and a phrase to a clause and not to a clause atom;
    /// routing them through the atoms asks a split span to fit inside an unsplit one, and 562
    /// times over the corpus it does not.
    ///
    /// A half verse is the Masoretic division of a verse and belongs to no syntactic span, so it
    /// has nothing above it.
    /// </summary>
    private static readonly Dictionary<WordGroupKind, WordGroupKind?> Above = new()
    {
        [WordGroupKind.Sentence] = null,
        [WordGroupKind.SentenceAtom] = WordGroupKind.Sentence,
        [WordGroupKind.Clause] = WordGroupKind.Sentence,
        [WordGroupKind.ClauseAtom] = WordGroupKind.Clause,
        [WordGroupKind.Phrase] = WordGroupKind.Clause,
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
            return new SyntaxOutcome(true, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var words = await WordsBySlot(text.Id, cancellationToken);
        var drafts = Drafts(project, words);
        Count(drafts, project);

        await Write(text.Id, drafts, cancellationToken);
        var outcome = new SyntaxOutcome(
            false,
            drafts.Count,
            drafts.Sum(draft => draft.Words.Count),
            drafts.Count(draft => Above[draft.Kind] is not null && draft.Parent is null),
            drafts.Count(draft => draft.Mother is not null || draft.MotherWordId is not null),
            started.Elapsed);

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

    internal static List<GroupDraft> Drafts(BhsaProject project, Dictionary<int, long> words)
    {
        var drafts = new List<GroupDraft>(1_000_000);

        Add(WordGroupKind.Sentence, project.Sentences,
            s => s.SlotId, s => s.Words, _ => null);
        Add(WordGroupKind.SentenceAtom, project.SentenceAtoms,
            s => s.SlotId, s => s.Words, _ => null);
        Add(WordGroupKind.Clause, project.Clauses, c => c.SlotId, c => c.Words, c => Features(
            ("type", c.Type), ("kind", c.Kind.ToString()), ("relation", c.LinguisticRelation.ToString()),
            ("domain", c.Domain.ToString()),
            // The "?" that is dropped from domain survives here: a text type is a sequence, one
            // letter per clause it is embedded under, and removing a letter renumbers the rest.
            ("textTypes", string.Join(" ", c.TextTypes.Select(type => type.ToString())))));
        Add(WordGroupKind.ClauseAtom, project.ClauseAtoms, c => c.SlotId, c => c.Words, c => Features(
            ("type", c.Type), ("code", c.Relation.ToString()), ("tab", c.Tab.ToString()),
            ("paragraph", c.Paragraph)));
        Add(WordGroupKind.Phrase, project.Phrases, p => p.SlotId, p => p.Words, p => Features(
            ("type", p.Type.ToString()), ("function", p.Function.ToString()),
            ("determination", p.Determination.ToString()), ("relation", p.LinguisticRelation.ToString())));
        Add(WordGroupKind.PhraseAtom, project.PhraseAtoms, p => p.SlotId, p => p.Words, p => Features(
            ("type", p.Type.ToString()), ("determination", p.Determination.ToString()),
            ("relation", p.LinguisticRelation.ToString())));
        // A subphrase's parent may be another subphrase, and it is always a longer one, so the
        // longest are drafted first: ids are handed out in this order and a row has to exist
        // before the row whose parent id names it.
        Add(WordGroupKind.Subphrase, project.Subphrases.OrderByDescending(s => s.Words.Count),
            s => s.SlotId, s => s.Words, s => Features(
                ("relation", s.LinguisticRelation.ToString())));
        Add(WordGroupKind.HalfVerse, project.HalfVerses, h => h.SlotId, h => h.Words, h => Features(
            ("label", h.Part.ToString())));

        Nest(drafts);
        Number(drafts);
        Mothers(project, drafts, words);
        return drafts;

        void Add<T>(
            WordGroupKind kind,
            IEnumerable<T> groups,
            Func<T, int> node,
            Func<T, IList<Bhsa.Core.Word>> members,
            Func<T, string?> features)
        {
            foreach (var group in groups)
            {
                var span = members(group);
                var ids = span
                    .Select(word => words.TryGetValue(word.SlotId, out var id) ? id : 0)
                    .Where(id => id != 0)
                    .ToList();

                if (ids.Count == 0)
                {
                    continue;
                }

                drafts.Add(new GroupDraft(kind, node(group), ids, features(group), span[0].SlotId)
                {
                    Slots = [.. span.Select(word => word.SlotId)],
                });
            }
        }
    }

    /// <summary>
    /// Fills in each group's parent: the shortest group of the kind above it that holds every one
    /// of this group's words.
    /// </summary>
    private static void Nest(List<GroupDraft> drafts)
    {
        var slots = 0;
        foreach (var draft in drafts)
        {
            slots = Math.Max(slots, draft.Slots[^1]);
        }

        // The four kinds anything hangs off each cover every word exactly once, so one candidate
        // per slot is all there is to remember; the shortest wins in case a witness arrives whose
        // spans of one kind overlap.
        var covering = Above.Values
            .OfType<WordGroupKind>()
            .Distinct()
            .ToDictionary(kind => kind, _ => new GroupDraft?[slots + 1]);

        foreach (var draft in drafts)
        {
            if (!covering.TryGetValue(draft.Kind, out var bySlot))
            {
                continue;
            }

            foreach (var slot in draft.Slots)
            {
                if (bySlot[slot] is not { } standing || draft.Slots.Count < standing.Slots.Count)
                {
                    bySlot[slot] = draft;
                }
            }
        }

        foreach (var draft in drafts)
        {
            if (Above[draft.Kind] is not { } above)
            {
                continue;
            }

            if (covering[above][draft.FirstSlot] is { } parent && Holds(parent, draft))
            {
                draft.Parent = parent;
            }
        }

        Deepen(drafts);
    }

    /// <summary>
    /// Subphrases nest — a construct chain inside an apposition — and 27,326 of BHSA's 113,850 sit
    /// entirely inside a longer one. Flat under the phrase atom, <em>what is inside this
    /// subphrase</em> cannot be asked, so the depth is read back from the spans: within one phrase
    /// atom, a subphrase's parent is the shortest sibling that holds it and more.
    ///
    /// Equal spans stay siblings. 4,270 subphrases cover exactly the same words as another one,
    /// and making either the parent of the other is a cycle rather than a depth.
    /// </summary>
    private static void Deepen(List<GroupDraft> drafts)
    {
        var byAtom = new Dictionary<int, List<GroupDraft>>();
        foreach (var draft in drafts)
        {
            if (draft.Kind != WordGroupKind.Subphrase || draft.Parent is not { } atom)
            {
                continue;
            }

            (byAtom.TryGetValue(atom.Node, out var standing) ? standing : byAtom[atom.Node] = []).Add(draft);
        }

        foreach (var siblings in byAtom.Values)
        {
            foreach (var draft in siblings)
            {
                GroupDraft? inside = null;
                foreach (var candidate in siblings)
                {
                    if (candidate.Slots.Count <= draft.Slots.Count ||
                        (inside is not null && candidate.Slots.Count >= inside.Slots.Count) ||
                        !Holds(candidate, draft))
                    {
                        continue;
                    }

                    inside = candidate;
                }

                if (inside is not null)
                {
                    draft.Parent = inside;
                }
            }
        }
    }

    /// <summary>
    /// Whether every word of the second group is a word of the first. Both slot lists run in text
    /// order, because the parser appends words as it walks the text, so this is a merge and not a
    /// lookup.
    /// </summary>
    private static bool Holds(GroupDraft group, GroupDraft inside)
    {
        var at = 0;
        foreach (var slot in inside.Slots)
        {
            while (at < group.Slots.Count && group.Slots[at] < slot)
            {
                at++;
            }

            if (at == group.Slots.Count || group.Slots[at] != slot)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Numbers each kind through the text: earliest word first, and the longer span first where
    /// two start on the same word, which puts a construct chain before the rectum inside it. A
    /// group's children ordered by this are therefore in text order too.
    ///
    /// BHSA's own node numbering is not this order for subphrases — 14,124 of 113,850 are out of
    /// it — because a subphrase is numbered within its phrase atom rather than along the text.
    /// </summary>
    private static void Number(List<GroupDraft> drafts)
    {
        foreach (var kind in drafts.GroupBy(draft => draft.Kind))
        {
            var position = 0;
            foreach (var draft in kind
                         .OrderBy(draft => draft.FirstSlot)
                         .ThenByDescending(draft => draft.Slots.Count))
            {
                draft.Position = ++position;
            }
        }
    }

    /// <summary>
    /// Resolves BHSA's mother edge onto what this corpus holds. The target is a node id like any
    /// other, and BHSA's words are its slots, so a target below the first group is a word and
    /// everything above it is a group — 143,872 groups and 38,397 words.
    /// </summary>
    private static void Mothers(BhsaProject project, List<GroupDraft> drafts, Dictionary<int, long> words)
    {
        var byNode = drafts.ToDictionary(draft => draft.Node);
        foreach (var draft in drafts)
        {
            if (!project.Mothers.TryGetValue(draft.Node, out var mother))
            {
                continue;
            }

            if (byNode.TryGetValue(mother, out var group))
            {
                draft.Mother = group;
            }
            else if (words.TryGetValue(mother, out var word))
            {
                draft.MotherWordId = word;
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

    private async Task Write(int textId, List<GroupDraft> drafts, CancellationToken cancellationToken)
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

        await WriteMothers(connection, drafts, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// The mother edges, in a pass of their own once every row exists. Containment orders the
    /// import — a parent is always of an earlier kind, or a longer subphrase — but nothing orders
    /// a mother, which points sideways and as readily forwards as back.
    /// </summary>
    private static async Task WriteMothers(
        NpgsqlConnection connection,
        List<GroupDraft> drafts,
        CancellationToken cancellationToken)
    {
        var ids = new List<long>();
        var groups = new List<long?>();
        var words = new List<long?>();

        foreach (var draft in drafts)
        {
            if (draft.Mother is null && draft.MotherWordId is null)
            {
                continue;
            }

            ids.Add(draft.Id);
            groups.Add(draft.Mother?.Id);
            words.Add(draft.MotherWordId);
        }

        if (ids.Count == 0)
        {
            return;
        }

        await using var command = new NpgsqlCommand(MotherImport, connection);
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint)
        {
            Value = ids.ToArray(),
        });
        command.Parameters.Add(new NpgsqlParameter("groups", NpgsqlDbType.Array | NpgsqlDbType.Bigint)
        {
            Value = groups.ToArray(),
        });
        command.Parameters.Add(new NpgsqlParameter("words", NpgsqlDbType.Array | NpgsqlDbType.Bigint)
        {
            Value = words.ToArray(),
        });

        await command.ExecuteNonQueryAsync(cancellationToken);
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

    /// <param name="Node">
    /// Its node id in the source, which is what a mother edge names and what the drafts are
    /// looked up by when one is resolved.
    /// </param>
    internal sealed record GroupDraft(
        WordGroupKind Kind,
        int Node,
        List<long> Words,
        string? Features,
        int FirstSlot)
    {
        public long Id { get; set; }

        public int Position { get; set; }

        public GroupDraft? Parent { get; set; }

        public GroupDraft? Mother { get; set; }

        public long? MotherWordId { get; set; }

        /// <summary>The BHSA slots this group covers, which is how a child finds it.</summary>
        public List<int> Slots { get; init; } = [];
    }
}
