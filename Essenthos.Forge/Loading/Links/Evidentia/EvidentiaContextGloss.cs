using System.Text.RegularExpressions;
using Essenthos.Core.Configuration;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// The English STEPBible's TAHOT prints for each Hebrew morpheme where it stands - <em>formlessness</em>
/// for תֹהוּ of Genesis 1:2, <em>he created</em> for בָּרָא - read beside BHSA's word. It is a gloss in
/// context rather than a dictionary's list of senses, so it names the word a translation is likely to
/// use in that verse even where no stated link ever taught the index the pair. The Greek has no such
/// file here, so a Greek word is read with the brief gloss of its lemma in STEPBible's TBESG, which the
/// corpus holds.
/// </summary>
internal sealed partial class EvidentiaContextGlossIndex(IConfiguration configuration, IHostEnvironment environment)
{
    private const string StepBibleFolder = "STEPBible";

    private const string TahotVolumes = "TAHOT *.txt";

    private const string HebrewLanguage = "hbo";

    private readonly Lazy<TahotSegmentation?> tahot = new(() =>
    {
        var folder = Path.Combine(ResourcePaths.Read(configuration, environment.ContentRootPath), StepBibleFolder);
        var volumes = Directory.Exists(folder) ? Directory.GetFiles(folder, TahotVolumes) : [];
        return volumes.Length == 0 ? null : TahotSegmentation.Read(volumes);
    });

    /// <summary>The stems of the words each word of the passage is glossed with, by word id.</summary>
    public IReadOnlyDictionary<long, IReadOnlySet<string>> For(
        IReadOnlyList<EvidentiaToken> target,
        IReadOnlyDictionary<string, string>? greekGlosses = null)
    {
        var glosses = new Dictionary<long, IReadOnlySet<string>>();
        foreach (var word in target.Where(token => greekGlosses is not null && token.Lemma is not null).DistinctBy(token => token.Id))
        {
            if (greekGlosses!.TryGetValue(word.Lemma!, out var gloss) && Words(gloss) is { Count: > 0 } greek)
            {
                glosses[word.Id] = greek;
            }
        }

        if (tahot.Value is not { } segmentation)
        {
            return glosses;
        }

        foreach (var verse in target
                     .Where(token => token.Language.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase))
                     .DistinctBy(token => token.Id)
                     .GroupBy(token => token.Address))
        {
            var words = verse.OrderBy(token => token.Position).ToList();
            var aligned = segmentation.Align(
                verse.Key.Book, verse.Key.Chapter, verse.Key.Verse,
                [.. words.Select(token => new HebrewEntry(token.StrongNumber ?? string.Empty, string.Empty, token.Position, string.Empty))]);
            if (aligned is null)
            {
                continue;
            }

            foreach (var word in words)
            {
                if (aligned.TryGetValue(word.Position, out var morpheme)
                    && Words(morpheme.Gloss) is { Count: > 0 } stems)
                {
                    glosses[word.Id] = stems;
                }
            }
        }

        return glosses;
    }

    /// <summary>
    /// The words of the gloss that say what the morpheme means: not the ones in brackets, which the
    /// glossers supplied (<em>&lt;it&gt; was</em>), and not the grammatical helpers English needs around
    /// a Hebrew verb or noun (<em>let</em> of <em>let sprout</em>, <em>have</em> of <em>you have tested</em>).
    /// </summary>
    private static HashSet<string> Words(string gloss) =>
        Bracketed().Replace(gloss, " ")
            .Split([' ', '-', '/', ',', ';', '.', ':', '?', '!', '’', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.ToLowerInvariant())
            .Where(word => word.All(char.IsLetter) && !EvidentiaContextGloss.Helpers.Contains(word))
            .Select(EnglishStemmer.Stem)
            .ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"<[^>]*>|\[[^\]]*\]|\([^)]*\)")]
    private static partial Regex Bracketed();
}

/// <summary>
/// Places a noun, name, adjective or number left free on the free word of its verse whose gloss names
/// it, where that gloss names no other free word of the verse and no other free word's gloss names it:
/// a pairing unique in both directions. The word's form and the lexeme each stand once in the verse,
/// since the gloss cannot say which occurrence of a repeated one is meant. Verbs are left out: their
/// glosses name the verb English uses, which is as often another word's (74-81%).
/// </summary>
internal static class EvidentiaContextGloss
{
    private const double ReviewConfidence = EvidentiaDefaults.TargetGlossReviewConfidence;

    private const string GreekLanguage = "grc";

    /// <summary>Words that carry grammar rather than the meaning of a gloss or of a translation's word.</summary>
    internal static readonly HashSet<string> Helpers = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "and", "or", "but", "of", "to", "in", "on", "at", "by", "for", "from", "with", "as", "into", "upon",
        "be", "is", "am", "are", "was", "were", "been", "being", "have", "has", "had", "do", "does", "did",
        "will", "shall", "would", "should", "may", "might", "can", "could", "must", "let", "not", "no",
        "i", "you", "he", "she", "it", "we", "they", "me", "him", "her", "us", "them", "my", "your", "his", "its",
        "our", "their", "this", "that", "these", "those", "who", "which", "what", "there", "thing", "things",
        "take", "put", "make", "get", "give", "go", "come", "one",
    };

    public static IReadOnlyList<EvidentiaProposal> Resolve(
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaAnalysis> target,
        IReadOnlyDictionary<long, IReadOnlySet<string>> glosses,
        IReadOnlyList<EvidentiaProposal> placed)
    {
        var takenSources = placed.Select(proposal => proposal.Source.Token.Id).ToHashSet();
        var takenTargets = placed.Select(proposal => proposal.Target.Token.Id).ToHashSet();
        var lexemesByVerse = target
            .DistinctBy(word => word.Token.Id)
            .GroupBy(word => word.Token.Address)
            .ToDictionary(group => group.Key, group => group
                .Where(word => word.Token.StrongNumber is not null)
                .GroupBy(word => word.Token.StrongNumber!)
                .ToDictionary(lexeme => lexeme.Key, lexeme => lexeme.Count()));
        var formsByVerse = source
            .DistinctBy(word => word.Token.Id)
            .GroupBy(word => word.Token.Address)
            .ToDictionary(group => group.Key, group => group.GroupBy(word => word.Normalised).ToDictionary(form => form.Key, form => form.Count()));
        var targetsByVerse = target
            .DistinctBy(word => word.Token.Id)
            .Where(word => !word.IsFunctionWord && !takenTargets.Contains(word.Token.Id) && glosses.ContainsKey(word.Token.Id))
            .GroupBy(word => word.Token.Address)
            .ToDictionary(group => group.Key, group => group.ToList());
        var proposals = new List<EvidentiaProposal>();
        foreach (var verse in source
                     .DistinctBy(word => word.Token.Id)
                     .Where(word => word.IsContentWord && !takenSources.Contains(word.Token.Id)
                         && EvidentiaMorphologyLabels.PartOfSpeech(word.PartOfSpeech, word.Token.Language) is { } wordClass
                         && wordClass is "noun" or "propn" or "adj" or "num"
                         && !Helpers.Contains((word.Lemma ?? word.Token.Surface).ToLowerInvariant()))
                     .GroupBy(word => word.Token.Address))
        {
            if (!targetsByVerse.TryGetValue(verse.Key, out var targets))
            {
                continue;
            }

            var matches = verse
                .Where(word => formsByVerse[verse.Key][word.Normalised] == 1)
                .SelectMany(word => targets
                    .Where(other => Names(glosses[other.Token.Id], word)
                        && lexemesByVerse[verse.Key].GetValueOrDefault(other.Token.StrongNumber ?? string.Empty) <= 1)
                    .Select(other => (Source: word, Target: other)))
                .ToList();
            foreach (var match in matches)
            {
                if (matches.Count(other => other.Source.Token.Id == match.Source.Token.Id) != 1
                    || matches.Count(other => other.Target.Token.Id == match.Target.Token.Id) != 1)
                {
                    continue;
                }

                proposals.Add(new EvidentiaProposal(
                    match.Source,
                    match.Target,
                    EvidentiaProposalKind.UniqueContextGlossReview,
                    ReviewConfidence,
                    new EvidentiaDecisionTrace("review", Rationale(match.Target), [])));
            }
        }

        return proposals;
    }

    private static string Rationale(EvidentiaAnalysis target) =>
        target.Token.Language.Equals(GreekLanguage, StringComparison.OrdinalIgnoreCase)
            ? "unique gloss in the verse, from STEPBible's TBESG (Tyndale House, Cambridge, CC BY 4.0)"
            : "unique gloss in context, from STEPBible's TAHOT (Tyndale House, Cambridge, CC BY-NC 3.0)";

    private static bool Names(IReadOnlySet<string> gloss, EvidentiaAnalysis word) =>
        gloss.Contains(word.Normalised)
        || word.Lemma is { Length: > 0 } lemma && gloss.Contains(EnglishStemmer.Stem(lemma));
}
