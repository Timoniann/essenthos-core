using System.Text.Json;
using Essenthos.Core.Bhsa;
using Essenthos.Core.Bhsa.Core;
using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading;

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
internal static class SyntaxGroups
{
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
