using System.Text.RegularExpressions;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Builds a small reverse index from a reader-language Strong lexicon for the particular source
/// passage. A sense mentioning a translated word is lexical evidence, not a source assertion:
/// words can occur in a gloss incidentally and the translation file itself may be model-made.
/// The index therefore creates candidates only and never participates in automatic acceptance.
/// </summary>
internal sealed partial class EvidentiaDictionarySenseIndex(
    AppDbContext db,
    LanguagePackRegistry languagePacks)
{
    public async Task<EvidentiaDictionarySenseEvidenceSource?> For(
        IReadOnlyList<EvidentiaToken> source,
        CancellationToken cancellationToken = default)
    {
        var language = source.Select(token => token.Language).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (language.Count != 1)
        {
            return null;
        }

        // The starter Slavic stemmer is intentionally light-weight. Using its stem as a reverse
        // dictionary key made unrelated forms collide (for example "рече" and "речі"). Until
        // UDPipe lemmas are supplied, an exact lower-case form is the conservative lexical key.
        var evidenceKeysByLexicalForm = source
            .Where(token => Analyse(token) is { IsFunctionWord: false })
            .SelectMany(token => LexicalForms(token).Select(form => (Form: Key(form), EvidenceKey: EvidenceKey(token))))
            .GroupBy(pair => pair.Form)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.EvidenceKey).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        if (evidenceKeysByLexicalForm.Count == 0)
        {
            return null;
        }

        var (entries, provenance) = await Entries(language[0], cancellationToken);
        var numbersByForm = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            foreach (var form in Words(entry.Definition, entry.KjvDefinition, entry.DetailedDefinition))
            {
                var analysis = Analyse(new EvidentiaToken(0, default, 0, form, language[0]));
                if (analysis is null || analysis.IsFunctionWord
                    || !evidenceKeysByLexicalForm.TryGetValue(Key(form), out var evidenceKeys))
                {
                    continue;
                }
                foreach (var evidenceKey in evidenceKeys)
                {
                    if (!numbersByForm.TryGetValue(evidenceKey, out var numbers))
                    {
                        numbers = new HashSet<string>(StringComparer.Ordinal);
                        numbersByForm.Add(evidenceKey, numbers);
                    }
                    numbers.Add(entry.StrongNumber);
                }
            }
        }

        return numbersByForm.Count == 0
            ? null
            : new EvidentiaDictionarySenseEvidenceSource(numbersByForm, provenance);
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

    private static IEnumerable<string> LexicalForms(EvidentiaToken token)
    {
        yield return token.Surface;
        if (!string.IsNullOrWhiteSpace(token.Lemma))
        {
            yield return token.Lemma;
        }
    }

    private static string EvidenceKey(EvidentiaToken token) => Key(token.Lemma ?? token.Surface);

    private static IEnumerable<string> Words(params string?[] fields) => fields
        .Where(field => !string.IsNullOrWhiteSpace(field))
        .SelectMany(field => Word().Matches(field!).Select(match => match.Value));

    private static string Key(string value) => value.ToLowerInvariant();

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Word();

    private sealed record SenseEntry(
        string StrongNumber,
        string? Definition,
        string? KjvDefinition,
        string? DetailedDefinition);
}

/// <summary>One source passage's reverse Strong-sense index. It is deliberately not a singleton.</summary>
internal sealed class EvidentiaDictionarySenseEvidenceSource(
    IReadOnlyDictionary<string, HashSet<string>> strongNumbersBySourceForm,
    string sourceName) : IEvidentiaEvidenceSource
{
    /// <summary>
    /// A definition can contain related words and a translated Strong lexicon is itself a secondary
    /// reading. The score is deliberately below direct Strong and needs canonical framing plus a
    /// future global matcher before it may become an accepted link.
    /// </summary>
    private const double SenseScore = 0.34;

    public IEnumerable<EvidentiaEvidence> Find(EvidentiaAnalysis source, EvidentiaAnalysis target)
    {
        if (source.IsFunctionWord
            || !strongNumbersBySourceForm.TryGetValue((source.Token.Lemma ?? source.Token.Surface).ToLowerInvariant(), out var numbers)
            || target.Token.StrongNumber is not { } targetNumber
            || !numbers.Contains(targetNumber))
        {
            yield break;
        }

        yield return new EvidentiaEvidence(
            EvidentiaEvidenceKind.DictionarySense,
            SenseScore,
            sourceName);
    }
}
