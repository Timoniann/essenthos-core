using System.Text.RegularExpressions;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Builds a small reverse index from a reader-language Strong lexicon for the particular source
/// passage. A sense mentioning a translated word is lexical evidence, not a source assertion:
/// words can occur in a gloss incidentally and the translation file itself may be model-made.
/// The index therefore creates candidates only and never participates in automatic acceptance.
///
/// A definition's word is filed under every key <see cref="RenderingKeys"/> gives it, and a source
/// word is looked up by the same keys. A definition writes a word in its citation form, so that form
/// is filed as a lemma too: a source word whose analysis has a lemma reaches it that way, and only a
/// word that reaches nothing by its own spelling or its lemma falls back to the pack's normalisation.
/// </summary>
internal sealed partial class EvidentiaDictionarySenseIndex(
    AppDbContext db,
    LanguagePackRegistry languagePacks)
{
    /// <summary>
    /// The whole lexicon, reversed from Strong number to the words its definitions use, built once
    /// for the life of this scope. It used to be read from the database and regex-tokenised again
    /// for every chapter - fifty times over a book run - to answer a question about one passage.
    /// </summary>
    private readonly Dictionary<string, ReverseSenseIndex> byLanguage = new(StringComparer.OrdinalIgnoreCase);

    public async Task<EvidentiaDictionarySenseEvidenceSource?> For(
        IReadOnlyList<EvidentiaToken> source,
        CancellationToken cancellationToken = default)
    {
        var language = source.Select(token => token.Language).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (language.Count != 1)
        {
            return null;
        }

        var wanted = source
            .Select(Analyse)
            .Where(analysis => analysis is { IsContentWord: true })
            .SelectMany(analysis => RenderingKeys.Of(analysis!))
            .ToHashSet();
        if (wanted.Count == 0)
        {
            return null;
        }

        var reverse = await Reverse(language[0], cancellationToken);
        var numbersByKey = wanted
            .Where(reverse.NumbersByKey.ContainsKey)
            .ToDictionary(key => key, key => reverse.NumbersByKey[key]);
        return numbersByKey.Count == 0
            ? null
            : new EvidentiaDictionarySenseEvidenceSource(numbersByKey, reverse.Source);
    }

    private async Task<ReverseSenseIndex> Reverse(string language, CancellationToken cancellationToken)
    {
        if (byLanguage.TryGetValue(language, out var cached))
        {
            return cached;
        }

        var (entries, provenance) = await Entries(language, cancellationToken);
        var numbersByKey = new Dictionary<RenderingKey, HashSet<string>>();
        var keysByForm = new Dictionary<string, IReadOnlyList<RenderingKey>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            foreach (var form in Words(entry.Definition, entry.KjvDefinition, entry.DetailedDefinition).Distinct())
            {
                if (!keysByForm.TryGetValue(form, out var keys))
                {
                    var analysis = Analyse(new EvidentiaToken(0, default, 0, form, language, Lemma: form.ToLowerInvariant()));
                    keys = analysis is { IsContentWord: true } ? RenderingKeys.Of(analysis) : [];
                    keysByForm.Add(form, keys);
                }

                foreach (var key in keys)
                {
                    if (!numbersByKey.TryGetValue(key, out var numbers))
                    {
                        numbers = new HashSet<string>(StringComparer.Ordinal);
                        numbersByKey.Add(key, numbers);
                    }

                    numbers.Add(entry.StrongNumber);
                }
            }
        }

        var index = new ReverseSenseIndex(numbersByKey, provenance);
        byLanguage.Add(language, index);
        return index;
    }

    private async Task<(List<SenseEntry> Entries, string Source)> Entries(
        string language,
        CancellationToken cancellationToken)
    {
        if (language.Equals("eng", StringComparison.OrdinalIgnoreCase))
        {
            var entries = await db.StrongEntries.AsNoTracking()
                .Select(entry => new SenseEntry(
                    entry.StrongNumber,
                    entry.Definition,
                    entry.KjvDefinition,
                    entry.DetailedDefinition))
                .ToListAsync(cancellationToken);
            return (entries, "strong-entry:eng");
        }

        var translated = await db.StrongEntryTranslations.AsNoTracking()
            .Where(entry => entry.Language == language)
            .Select(entry => new SenseEntry(
                entry.StrongNumber,
                entry.Definition,
                entry.KjvDefinition,
                entry.DetailedDefinition))
            .ToListAsync(cancellationToken);
        return (translated, $"strong-entry-translation:{language}");
    }

    private EvidentiaAnalysis? Analyse(EvidentiaToken token) =>
        languagePacks.TryAnalyse(token, out var analysis) ? analysis : null;

    private static IEnumerable<string> Words(params string?[] fields) => fields
        .Where(field => !string.IsNullOrWhiteSpace(field))
        .SelectMany(field => Word().Matches(field!).Select(match => match.Value));

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Word();

    private sealed record ReverseSenseIndex(
        IReadOnlyDictionary<RenderingKey, HashSet<string>> NumbersByKey,
        string Source);

    private sealed record SenseEntry(
        string StrongNumber,
        string? Definition,
        string? KjvDefinition,
        string? DetailedDefinition);
}

/// <summary>One source passage's reverse Strong-sense index. It is deliberately not a singleton.</summary>
internal sealed class EvidentiaDictionarySenseEvidenceSource(
    IReadOnlyDictionary<RenderingKey, HashSet<string>> strongNumbersByKey,
    string sourceName) : IEvidentiaEvidenceSource
{
    /// <summary>
    /// A definition can contain related words and a translated Strong lexicon is itself a secondary
    /// reading. The score is deliberately below direct Strong and needs canonical framing plus a
    /// future global matcher before it may become an accepted link.
    /// </summary>
    private const double SenseScore = EvidentiaDefaults.DictionarySenseScore;

    private readonly Dictionary<long, (string Key, HashSet<string> Numbers)?> entries = [];

    public IEnumerable<EvidentiaEvidence> Find(EvidentiaAnalysis source, EvidentiaAnalysis target)
    {
        if (!source.IsContentWord
            || target.Token.StrongNumber is not { } targetNumber
            || EntryOf(source) is not { } entry
            || !entry.Numbers.Contains(targetNumber))
        {
            yield break;
        }

        yield return new EvidentiaEvidence(
            EvidentiaEvidenceKind.DictionarySense,
            SenseScore,
            $"{sourceName}; key={entry.Key}");
    }

    /// <summary>
    /// What the word's own spelling and its lemma reach in the lexicon, together; and only where
    /// neither reaches anything, what its normalisation does. Chosen before the target is looked at,
    /// for the reason <see cref="EvidentiaKnownRenderingEvidenceSource"/> gives.
    ///
    /// The spelling and the lemma are not a ladder here. A definition names a sense in a citation
    /// form, so a spelling that happens to stand in some other definition would, taken first, hide
    /// the lemma's sense: measured on the Ukrainian benchmark that cost both precision and recall.
    /// </summary>
    private (string Key, HashSet<string> Numbers)? EntryOf(EvidentiaAnalysis source)
    {
        if (!entries.TryGetValue(source.Token.Id, out var entry))
        {
            entry = Entry(source);
            entries.Add(source.Token.Id, entry);
        }

        return entry;
    }

    private (string Key, HashSet<string> Numbers)? Entry(EvidentiaAnalysis source)
    {
        var keys = RenderingKeys.Of(source);
        var exact = keys
            .Where(key => key.Kind != EvidentiaFormKind.Normalised && strongNumbersByKey.ContainsKey(key))
            .ToList();
        if (exact.Count > 0)
        {
            return (string.Join(" + ", exact), exact.SelectMany(key => strongNumbersByKey[key]).ToHashSet(StringComparer.Ordinal));
        }

        var normalised = keys.FirstOrDefault(key => key.Kind == EvidentiaFormKind.Normalised);
        return normalised != default && strongNumbersByKey.TryGetValue(normalised, out var numbers)
            ? (normalised.ToString(), numbers)
            : null;
    }
}
