using Essenthos.Core.Corpus;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>One named word, as the query reads it: which entity, where, and how it is printed.</summary>
/// <param name="Renders">
/// The lemma of the original word a translation's word is linked to and that names the same entity,
/// where there is one: which of the entity's names in the original this word translates.
/// </param>
/// <param name="Guess">
/// How sure the annotation naming the word is, where it reached the word across a link the aligner
/// guessed; null where a source states it or a number, a lexicon or a rule carried it.
/// </param>
internal readonly record struct NamedWord(
    int EntityId,
    int TextId,
    int VerseId,
    int Position,
    string Surface,
    string Trailer,
    string? Lemma,
    string Language,
    string? Renders = null,
    double? Guess = null);

/// <summary>One spelling of one entity in one text, counted, and whether a list heads with it.</summary>
internal sealed record Rendering(int EntityId, int TextId, string Form, string Folded, int Occurrences, bool Heading);

/// <summary>
/// How the words that name an entity in a text become that text's spellings of its name.
///
/// <para>
/// Three things stand between an annotated word and a name. Adjacent words named as the same entity
/// are one name — <em>Beth Shemesh</em> is two words of the Berean and one place. Quotation marks,
/// punctuation and an English possessive are the sentence's and not the name's. And an annotation
/// sometimes sits on a word that is not the name at all — <em>he</em>, <em>und</em>, <em>Sohn</em>,
/// <em>Priester</em> — because it was carried across a link to whatever the name was rendered as.
/// </para>
///
/// <para>
/// The last is kept out by what the words have in common rather than by a list of words. A spelling
/// in a script with capitals must begin with one, and every spelling kept must open the way the
/// text's commonest spelling opens: <em>Аарона</em>, <em>Ааронові</em> and <em>Аароном</em> are the
/// declension of <em>Аарон</em>, and <em>Sohn</em> is not anything of <em>Aaron</em>. It costs the rare
/// genuinely different spelling a text also prints — <em>Oshea</em> beside <em>Joshua</em> — and
/// that is the side to err on: a search that misses a variant is better than one that finds Aaron
/// under <em>Priester</em>.
/// </para>
///
/// <para>
/// Except where the original says it is another name. Peter is <em>Κηφᾶς</em> nine times in the
/// Greek, and the Ohienko Bible prints that <em>Кифа</em>, which opens nothing like <em>Петро</em>.
/// So a translation's words are also read in groups by the name in the original they are linked to,
/// and a group whose commonest spelling is printed more than once keeps the spellings that open the
/// way that one does. Only a lemma the lexicon writes with a capital makes a group: a word linked to
/// a pronoun or a common noun stays under the first rule, and Hebrew, which has no capitals, makes
/// none. The heading is chosen from the first rule's spellings alone, and a spelling another entity
/// heads with in the same text stays that entity's.
/// </para>
///
/// <para>
/// The heading is the spelling a list shows. Where the language has a nominative this corpus holds
/// and the text prints it, that is the heading. Otherwise it is the shortest of the spellings printed
/// at least a third as often as the commonest, which in a declining language is the nominative more
/// often than the commonest is — the Ohienko Bible prints <em>Аарона</em> 128 times and <em>Аарон</em>
/// 118 — and in a language that does not decline is simply the commonest.
/// </para>
/// </summary>
internal static class Renderings
{
    /// <summary>How many opening letters a spelling must share with the commonest to be kept.</summary>
    private const int SharedOpening = 3;

    /// <summary>
    /// A spelling printed at least this share as often as the commonest is a candidate heading. A
    /// third keeps the nominative of a name whose genitive dominates and drops the stray form.
    /// </summary>
    private const double HeadingShare = 1.0 / 3.0;

    /// <summary>
    /// How often a group read by the name in the original must print its commonest spelling for the
    /// group to count. Once is as likely an annotation carried onto the next word as a name.
    /// </summary>
    private const int AnotherNameAtLeast = 2;

    /// <summary>
    /// How sure the annotation on a spelling must be, on at least one of the words that print it,
    /// for the spelling to stand on that alone. It is the line below which the reader draws a link
    /// as a guess, and a spelling is only as good as its best link.
    /// </summary>
    internal const double TrustedAt = 0.5;

    /// <summary>
    /// How many words must print a spelling none of whose annotations reaches
    /// <see cref="TrustedAt"/> for it to stand anyway. A faint link the aligner made once is as
    /// likely the word beside the name; two of them agreeing on one spelling are a rendering.
    /// </summary>
    internal const int AgreeingAt = 2;

    /// <summary>The languages whose witnesses carry a lemma, which is the name without its case.</summary>
    internal static readonly HashSet<string> Original = new(StringComparer.Ordinal) { "hbo", "arc", "grc" };

    /// <summary>
    /// A lemma with a morpheme boundary in it is a segmentation and not a name: the Samaritan
    /// Pentateuch writes <c>אהרנ/</c>.
    /// </summary>
    private const char MorphemeBoundary = '/';

    private const string EnglishLanguage = "eng";

    /// <summary>
    /// Every text's spellings of every entity in <paramref name="words"/>.
    /// </summary>
    /// <param name="words">The named words, in any order.</param>
    /// <param name="nominatives">
    /// The nominative this corpus holds for an entity in a language, keyed by entity and language,
    /// so that a text which prints it heads with it.
    /// </param>
    public static IEnumerable<Rendering> Of(
        IEnumerable<NamedWord> words,
        IReadOnlyDictionary<(int Entity, string Language), string> nominatives)
    {
        var names = new Dictionary<(int Entity, int Text), Dictionary<string, Tally>>();
        var byOriginal = new Dictionary<(int Entity, int Text), Dictionary<string, Dictionary<string, Tally>>>();
        var languages = new Dictionary<int, string>();
        var trusted = new Dictionary<int, HashSet<string>>();

        foreach (var run in Runs(words))
        {
            var first = run[0];
            languages[first.TextId] = first.Language;

            var name = Spelled(run);
            if (name is null)
            {
                continue;
            }

            var key = (first.EntityId, first.TextId);
            var best = run.Max(word => word.Guess ?? NotGuessed);
            Count(names, key, name, best);

            if (best >= TrustedAt)
            {
                if (!trusted.TryGetValue(first.EntityId, out var forms))
                {
                    trusted[first.EntityId] = forms = new HashSet<string>(StringComparer.Ordinal);
                }

                forms.Add(NameFolding.Fold(name));
            }

            if (OriginalName(run) is { } original)
            {
                if (!byOriginal.TryGetValue(key, out var groups))
                {
                    byOriginal[key] = groups = new Dictionary<string, Dictionary<string, Tally>>(StringComparer.Ordinal);
                }

                Count(groups, original, name, best);
            }
        }

        var another = new List<Rendering>();
        var headed = new HashSet<(int Text, string Folded)>();
        foreach (var ((entity, text), counted) in names)
        {
            var nominative = nominatives.GetValueOrDefault((entity, languages[text]));
            var groups = byOriginal.TryGetValue((entity, text), out var found)
                ? found.Values
                : (IEnumerable<Dictionary<string, Tally>>)[];
            var corroborating = trusted.GetValueOrDefault(entity) ?? [];
            if (nominative is not null)
            {
                corroborating = [.. corroborating, NameFolding.Fold(nominative)];
            }

            foreach (var (rendering, isAnother) in Kept(entity, text, counted, groups, nominative, corroborating))
            {
                if (isAnother)
                {
                    another.Add(rendering);
                    continue;
                }

                if (rendering.Heading)
                {
                    headed.Add((text, rendering.Folded));
                }

                yield return rendering;
            }
        }

        // A spelling another entity heads with is that entity's name: the King James's "Jesus of
        // Nazareth" translates Ναζωραῖος, which names Jesus, and a search for Nazareth is for the town.
        foreach (var rendering in another.Where(r => !headed.Contains((r.TextId, r.Folded))))
        {
            yield return rendering;
        }
    }

    /// <summary>How sure an annotation no guess carried is, for comparing it with the ones one did.</summary>
    private const double NotGuessed = 1.0;

    /// <summary>How often a spelling is printed, and the surest annotation on any word printing it.</summary>
    private readonly record struct Tally(int Occurrences, double Best);

    private static void Count<TKey>(Dictionary<TKey, Dictionary<string, Tally>> into, TKey key, string name, double best)
        where TKey : notnull
    {
        if (!into.TryGetValue(key, out var counted))
        {
            into[key] = counted = new Dictionary<string, Tally>(StringComparer.Ordinal);
        }

        var tally = counted.GetValueOrDefault(name);
        counted[name] = new Tally(tally.Occurrences + 1, Math.Max(tally.Best, best));
    }

    /// <summary>
    /// The name in the original a translation's run renders, when the lexicon writes it with a
    /// capital. An original is spelled by its own lemma and is read by the first rule alone.
    /// </summary>
    private static string? OriginalName(IReadOnlyList<NamedWord> run) =>
        Original.Contains(run[0].Language)
            ? null
            : run.Select(word => word.Renders)
                .FirstOrDefault(lemma => lemma is { Length: > 0 } && char.IsUpper(lemma[0]));

    /// <summary>
    /// The named words cut into names: consecutive words of one verse named as one entity in one
    /// text are one name.
    /// </summary>
    internal static IEnumerable<List<NamedWord>> Runs(IEnumerable<NamedWord> words)
    {
        List<NamedWord>? run = null;

        foreach (var word in words
                     .OrderBy(w => w.EntityId).ThenBy(w => w.TextId).ThenBy(w => w.VerseId).ThenBy(w => w.Position))
        {
            if (run is not null)
            {
                var last = run[^1];
                if (last.EntityId == word.EntityId && last.VerseId == word.VerseId && last.TextId == word.TextId
                    && last.Position + 1 == word.Position && !EndsAClause(last))
                {
                    run.Add(word);
                    continue;
                }

                yield return run;
            }

            run = [word];
        }

        if (run is not null)
        {
            yield return run;
        }
    }

    /// <summary>
    /// Whether punctuation follows the word, so that the next one starts another name even when both
    /// name the same entity: <em>Irad zeugte Mahujael. Mahujael zeugte Methusael</em>.
    /// </summary>
    private static bool EndsAClause(NamedWord word) =>
        word.Trailer.AsSpan().IndexOfAny(ClauseMarks) >= 0 || word.Surface.AsSpan().IndexOfAny(ClauseMarks) >= 0;

    /// <summary>
    /// The marks that close a clause in the scripts the corpus holds. Not the hyphen and not the
    /// Hebrew maqaf, which join the parts of one name.
    /// </summary>
    private static readonly System.Buffers.SearchValues<char> ClauseMarks =
        System.Buffers.SearchValues.Create(".,;:!?·;׃׀");

    /// <summary>
    /// One run as a name, or null when nothing in it is a name. A lower-case word in a script that
    /// has capitals is not part of one — <em>he</em>, <em>und</em>, or the noun Brenton prints
    /// straight after <em>Ἀαρών</em> that an annotation spilled onto.
    /// </summary>
    internal static string? Spelled(IReadOnlyList<NamedWord> run)
    {
        var parts = new List<string>(run.Count);
        foreach (var word in run)
        {
            var part = Trimmed(Printed(word));
            if (part.Length > 0 && !char.IsLower(part[0]))
            {
                parts.Add(part);
            }
        }

        if (parts.Count == 0)
        {
            return null;
        }

        if (run[0].Language == EnglishLanguage)
        {
            parts[^1] = WithoutPossessive(parts[^1]);
        }

        return string.Join(' ', parts);
    }

    private static string Printed(NamedWord word) =>
        Original.Contains(word.Language) && word.Lemma is { Length: > 0 } lemma && !lemma.Contains(MorphemeBoundary)
            ? lemma
            : word.Surface;

    /// <summary>The word without whatever the sentence put around it — quotation marks, punctuation.</summary>
    internal static string Trimmed(string word)
    {
        var start = 0;
        var end = word.Length;
        while (start < end && !IsPartOfAName(word[start]))
        {
            start++;
        }

        while (end > start && !IsPartOfAName(word[end - 1]))
        {
            end--;
        }

        return word[start..end];
    }

    /// <summary>A letter, or a Hebrew point or Greek accent riding on one.</summary>
    private static bool IsPartOfAName(char c) =>
        char.IsLetter(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark;

    /// <summary><em>Aaron's</em> and <em>Aaron’s</em> are Aaron.</summary>
    internal static string WithoutPossessive(string word) =>
        word.Length > 2 && word[^1] == 's' && word[^2] is '\'' or '’' ? word[..^2] : word;

    private static IEnumerable<(Rendering Rendering, bool Another)> Kept(
        int entity,
        int text,
        Dictionary<string, Tally> counted,
        IEnumerable<Dictionary<string, Tally>> byOriginal,
        string? nominative,
        IReadOnlySet<string> corroborating)
    {
        var spellings = Spellings(counted, corroborating);
        if (spellings.Count == 0)
        {
            return [];
        }

        var commonest = Commonest(spellings);
        var kept = spellings.Where(c => OpensLike(c.Folded, commonest.Folded)).ToList();

        var taken = kept.Select(c => c.Folded).ToHashSet(StringComparer.Ordinal);
        var others = byOriginal
            .Select(group => Spellings(group, corroborating))
            .Where(group => group.Count > 0 && Commonest(group).Occurrences >= AnotherNameAtLeast)
            .SelectMany(Opening)
            .Where(c => taken.Add(c.Folded))
            .Select(c => c.Folded)
            .ToHashSet(StringComparer.Ordinal);

        var foldedNominative = nominative is null ? null : NameFolding.Fold(nominative);
        var heading = kept
                          .Where(c => c.Folded == foldedNominative)
                          .OrderByDescending(c => c.Occurrences)
                          .Select(c => c.Form)
                          .FirstOrDefault()
                      ?? kept
                          .Where(c => c.Occurrences >= commonest.Occurrences * HeadingShare)
                          .OrderBy(c => c.Form.Length).ThenByDescending(c => c.Occurrences)
                          .ThenBy(c => c.Form, StringComparer.Ordinal)
                          .First().Form;

        return kept
            .Select(c => (new Rendering(entity, text, c.Form, c.Folded, c.Occurrences, c.Form == heading), false))
            .Concat(spellings
                .Where(c => others.Contains(c.Folded))
                .Select(c => (new Rendering(entity, text, c.Form, c.Folded, c.Occurrences, false), true)));
    }

    /// <summary>
    /// Spellings a search cannot tell apart are one spelling, printed as its commonest member: BHSA
    /// points Jerusalem four ways, and the Berean hyphenates what the King James runs together.
    ///
    /// <para>
    /// Only the spellings the corpus has a reason to believe. An annotation carried across a link
    /// is as sure as the link, and where the aligner cannot place a word it spreads the original
    /// over the words beside it: the Open New Ukrainian Translation prints the mercy seat once, as
    /// <em>кришку</em>, which no link reaches, and the one capitalised word the annotation did reach
    /// is the <em>Однак</em> opening the next sentence. So a spelling stands when one of its words is
    /// annotated at <see cref="TrustedAt"/> or better, when <see cref="AgreeingAt"/> words print it,
    /// or when it opens the way a firm spelling of the entity in any text does, or the nominative
    /// held for the language: the Ohienko Bible's one faint <em>Одед</em> is the name the Synodal
    /// prints firmly, and <em>Однак</em> opens like nothing the mercy seat is called anywhere.
    /// </para>
    /// </summary>
    private static List<(string Form, string Folded, int Occurrences)> Spellings(
        Dictionary<string, Tally> counted,
        IReadOnlySet<string> corroborating) =>
        [.. counted
            .GroupBy(c => NameFolding.Fold(c.Key))
            .Where(g => g.Key.Length > 0)
            .Where(g => g.Max(c => c.Value.Best) >= TrustedAt
                        || g.Sum(c => c.Value.Occurrences) >= AgreeingAt
                        || corroborating.Any(form => OpensLike(g.Key, form)))
            .Select(g => (
                Form: g.OrderByDescending(c => c.Value.Occurrences).ThenBy(c => c.Key, StringComparer.Ordinal).First().Key,
                Folded: g.Key,
                Occurrences: g.Sum(c => c.Value.Occurrences)))];

    /// <summary>
    /// A tie goes to the longer spelling, which is the name more often than the short word an
    /// annotation spilled onto: RV1909 prints "De los Isharitas" and both ends are named once.
    /// </summary>
    private static (string Form, string Folded, int Occurrences) Commonest(
        List<(string Form, string Folded, int Occurrences)> spellings) =>
        spellings
            .OrderByDescending(c => c.Occurrences).ThenByDescending(c => c.Form.Length)
            .ThenBy(c => c.Form, StringComparer.Ordinal)
            .First();

    private static IEnumerable<(string Form, string Folded, int Occurrences)> Opening(
        List<(string Form, string Folded, int Occurrences)> spellings)
    {
        var commonest = Commonest(spellings);
        return spellings.Where(c => OpensLike(c.Folded, commonest.Folded));
    }

    private static bool OpensLike(string folded, string opening)
    {
        var length = Math.Min(SharedOpening, Math.Min(folded.Length, opening.Length));
        return string.CompareOrdinal(folded, 0, opening, 0, length) == 0;
    }
}
