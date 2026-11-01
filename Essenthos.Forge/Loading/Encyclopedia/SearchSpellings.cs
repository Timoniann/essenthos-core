using Essenthos.Core.Corpus;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>A spelling a search finds an entity by, beside its renderings.</summary>
internal sealed record SearchSpelling(int EntityId, string Form, string Folded);

/// <param name="Admitted">The spellings admitted, by entity and folded spelling.</param>
/// <param name="AdmittedInTexts">The same, counted per text that prints them.</param>
/// <param name="Unsure">Spellings per text refused because no annotation on them is sure enough.</param>
/// <param name="CommonWord">Refused because most words printed so in the text are not this entity.</param>
/// <param name="AnotherName">Refused because the text prints it as another entity's name.</param>
/// <param name="Epithet">Refused because the record is one a text names by epithets and titles as much as by its name.</param>
/// <param name="Unsegmented">Refused because the text writes no spaces, so a word of it is a stretch of running text.</param>
internal sealed record SearchSpellingCount(
    int Admitted, int AdmittedInTexts, int Unsure, int CommonWord, int AnotherName, int Unsegmented = 0, int Epithet = 0);

/// <summary>
/// Which of the spellings <see cref="Renderings"/> drops a search may still find the record by.
///
/// <para>
/// How sure an annotation is does not tell a name from the word beside it: Luther's <em>Priester</em>
/// carries Aaron at 0.89, across a number. What tells them apart is how the text uses the word. So a
/// spelling is admitted where all three hold, in some text:
/// </para>
/// <list type="bullet">
/// <item>an annotation on it is a ruling, a source's statement or at least <see cref="HighConfidence"/>
/// sure, across no link the aligner guessed below that;</item>
/// <item>at least <see cref="ShareOfThePrinted"/> of the words the text prints that way are this entity:
/// <em>Priester</em> is printed hundreds of times and is Aaron a few, <em>Цофнат-Панеах</em> once and
/// is Joseph once;</item>
/// <item>it is not how that text spells another entity's name, so <em>Ur</em> stays Ur's.</item>
/// </list>
/// <para>
/// Jesus, the LORD and the Holy Spirit are left out: the texts annotate their titles and the sentences
/// round them (<em>Son of God</em>, <em>by the Spirit</em>, <em>as king</em>) in every language, each
/// printed once so that the share cannot tell them from a name. Their names are found by their renderings.
/// </para>
/// <para>
/// A Chinese text writes no spaces, so the stretch of characters a name was annotated on is the name
/// with the particles and the verb around it (<em>乃在乎耶和華</em>, <em>現在他</em>); a search that opens on
/// those finds a record by the word before its name. Those are left to <see cref="Renderings"/>.
/// </para>
/// <para>
/// Things, feasts and titles are left out: their renderings are common nouns, and a search that finds a
/// record by a common noun has stopped searching names.
/// </para>
/// </summary>
internal static class SearchSpellings
{
    /// <summary>How sure an annotation that is neither a ruling nor a source's statement must be.</summary>
    internal const double HighConfidence = 0.85;

    /// <summary>The records a text names by epithets as much as by name, which a search does not widen.</summary>
    internal static readonly string[] EpithetRecords = ["jesus", "yhvh", "holy-spirit"];

    /// <summary>How much of what a text prints a certain way must be this entity.</summary>
    internal const double ShareOfThePrinted = 0.5;

    /// <summary>The fewest letters a spelling has, so that a stray letter finds nothing.</summary>
    private const int ShortestSpelling = 2;

    /// <summary>The words printed the way some named word is, which the share is taken of.</summary>
    public static IEnumerable<(int Text, string Normalised)> PrintedWordsToCount(
        IReadOnlyList<NamedWord> words,
        IReadOnlyCollection<Rendering> renderings)
    {
        var held = Held(renderings);
        foreach (var run in Renderings.Runs(words.Where(word => !word.Thing)))
        {
            if (Renderings.Spelled(run) is { } name
                && !Unsegmented(name)
                && NameFolding.Fold(name) is { Length: >= ShortestSpelling } folded
                && !held.Contains((run[0].EntityId, folded)))
            {
                foreach (var word in run.Where(word => word.Normalised is not null))
                {
                    yield return (word.TextId, word.Normalised!);
                }
            }
        }
    }

    /// <param name="words">The named words, as <see cref="Renderings.Of"/> reads them.</param>
    /// <param name="renderings">What <see cref="Renderings.Of"/> kept.</param>
    /// <param name="printed">How many words each text prints each way, for the ones asked about.</param>
    /// <param name="epithets">The ids of <see cref="EpithetRecords"/>.</param>
    public static (List<SearchSpelling> Spellings, SearchSpellingCount Count) Of(
        IReadOnlyList<NamedWord> words,
        IReadOnlyCollection<Rendering> renderings,
        IReadOnlyDictionary<(int Text, string Normalised), int> printed,
        IReadOnlySet<int>? epithets = null)
    {
        var held = Held(renderings);
        var others = renderings
            .GroupBy(r => (r.TextId, r.Folded))
            .ToDictionary(g => g.Key, g => g.Select(r => r.EntityId).ToHashSet());
        var named = words
            .Where(word => word.Normalised is not null)
            .GroupBy(word => (word.EntityId, word.TextId, word.Normalised))
            .ToDictionary(g => g.Key, g => g.Count());

        var seen = new Dictionary<(int Entity, int Text, string Folded), Verdict>();
        var forms = new Dictionary<(int Entity, string Folded), Dictionary<string, int>>();
        foreach (var run in Renderings.Runs(words.Where(word => !word.Thing)))
        {
            if (Renderings.Spelled(run) is not { } name)
            {
                continue;
            }

            var folded = NameFolding.Fold(name);
            var first = run[0];
            if (folded.Length < ShortestSpelling || held.Contains((first.EntityId, folded)))
            {
                continue;
            }

            var verdict = epithets?.Contains(first.EntityId) == true
                ? Verdict.Epithet
                : Read(run, folded, others, named, printed);
            var key = (first.EntityId, first.TextId, folded);
            seen[key] = seen.TryGetValue(key, out var before) && before < verdict ? before : verdict;
            if (verdict == Verdict.Admitted)
            {
                if (!forms.TryGetValue((first.EntityId, folded), out var counted))
                {
                    forms[(first.EntityId, folded)] = counted = new Dictionary<string, int>(StringComparer.Ordinal);
                }

                counted[name] = counted.GetValueOrDefault(name) + 1;
            }
        }

        var spellings = forms
            .Select(f => new SearchSpelling(
                f.Key.Entity,
                f.Value.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal).First().Key,
                f.Key.Folded))
            .ToList();

        int Counted(Verdict verdict) => seen.Values.Count(v => v == verdict);
        return (spellings, new SearchSpellingCount(
            spellings.Count,
            Counted(Verdict.Admitted),
            Counted(Verdict.Unsure),
            Counted(Verdict.CommonWord),
            Counted(Verdict.AnotherName),
            Counted(Verdict.Unsegmented),
            Counted(Verdict.Epithet)));
    }

    /// <summary>In the order one run's verdict is preferred to another's for the same spelling in a text.</summary>
    private enum Verdict
    {
        Admitted,
        Unsure,
        CommonWord,
        AnotherName,
        Unsegmented,
        Epithet,
    }

    private static Verdict Read(
        List<NamedWord> run,
        string folded,
        Dictionary<(int TextId, string Folded), HashSet<int>> others,
        Dictionary<(int EntityId, int TextId, string? Normalised), int> named,
        IReadOnlyDictionary<(int Text, string Normalised), int> printed)
    {
        var first = run[0];
        if (Unsegmented(Renderings.Spelled(run) ?? string.Empty))
        {
            return Verdict.Unsegmented;
        }

        if (others.TryGetValue((first.TextId, folded), out var bearers) && bearers.Any(entity => entity != first.EntityId))
        {
            return Verdict.AnotherName;
        }

        if (run.Any(word => word.Normalised is not null
                            && named.GetValueOrDefault((word.EntityId, word.TextId, word.Normalised))
                            < ShareOfThePrinted * printed.GetValueOrDefault((word.TextId, word.Normalised))))
        {
            return Verdict.CommonWord;
        }

        return run.Any(word => word.Firm) ? Verdict.Admitted : Verdict.Unsure;
    }

    /// <summary>The spelling is Han characters, which a text writes with no space between words.</summary>
    private static bool Unsegmented(string name) =>
        name.Any(c => c is >= '㐀' and <= '䶿' or >= '一' and <= '鿿' or >= '豈' and <= '﫿');

    private static HashSet<(int Entity, string Folded)> Held(IEnumerable<Rendering> renderings) =>
        renderings.Select(r => (r.EntityId, r.Folded)).ToHashSet();
}
