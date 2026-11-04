using System.Text.Json.Nodes;
using Essenthos.Core.Glaux;
using Essenthos.Core.Loading;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Conversion;

/// <summary>The stages the converter runs after reading and correcting a text, in this order.</summary>
internal static class TextStages
{
    public static IReadOnlyList<ITextStage> All { get; } =
    [
        new BhsaHeadwords(),
        new BhsaStrongNumbers(),
        new SeptuagintLemmas(),
        new KingJamesNumbers(),
        new SideStrongNumbers(),
        new DerivedStrongNumbers(),
        new MisplacedNumbers(),
        new BereanSupplied(),
        new EditionBrackets(),
        new PrintedWords(),
    ];
}

/// <summary>
/// BHSA's lemma is its dictionary form (the vocalised lexeme); the spelling of the occurrence the reader
/// gave as the lemma is kept in the morphology as <c>realisedLexeme</c>. The load's <c>bhsa-headwords</c> step.
/// </summary>
internal sealed class BhsaHeadwords : ITextStage
{
    public bool AppliesTo(string slug) => slug == BhsaTextSource.Slug;

    public IReadOnlyList<string> Sources(string slug) => [];

    public void Apply(ConvertedText text, string resources)
    {
        foreach (var (_, _, word) in text.Words())
        {
            if (word.Morphology is null || JsonNode.Parse(word.Morphology) is not JsonObject morphology
                || !morphology.ContainsKey("vocalizedLexeme") || morphology.ContainsKey("realisedLexeme"))
            {
                continue;
            }

            var headword = morphology["vocalizedLexeme"]?.GetValueKind() == System.Text.Json.JsonValueKind.String
                ? morphology["vocalizedLexeme"]!.GetValue<string>()
                : morphology["vocalizedLexeme"]?.ToJsonString();
            if (word.Lemma is not null)
            {
                morphology["realisedLexeme"] = word.Lemma;
                word.Morphology = JsonbText.Canonical(morphology.ToJsonString());
            }

            word.Lemma = headword;
        }

        text.Provenance.Add(new("lemma", "BHSA's vocalised lexeme; the occurrence's spelling kept as morphology.realisedLexeme"));
    }
}

/// <summary>
/// Brenton's Greek lemmatised by GLAUx: a word's folded form takes the lemma GLAUx gives it at least four
/// times in five, spelled as GLAUx most often spells it, composed. The load's <c>septuagint-lemmas</c> step.
/// </summary>
internal sealed class SeptuagintLemmas : ITextStage
{
    private const double Agreed = 0.8;

    public bool AppliesTo(string slug) => slug == SeptuagintTextSource.Slug;

    public IReadOnlyList<string> Sources(string slug) => ["Glaux"];

    public void Apply(ConvertedText text, string resources)
    {
        var directory = Path.Combine(resources, "Glaux", "xml");
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"GLAUx is not at {directory}; Brenton's Greek is converted with its lemmas or not at all. Run scripts/fetch-glaux.ps1.");
        }

        var (lexicon, spelling) = GlauxLemmaLoader.Read(directory);
        foreach (var (_, _, word) in text.Words())
        {
            var form = word.Normalised is { Length: > 0 } folded ? folded : GreekLetters.Bare(word.Surface);
            if (!lexicon.TryGetValue(form, out var choice) || choice.Share < Agreed)
            {
                continue;
            }

            var lemma = spelling.TryGetValue(choice.Lemma, out var asWritten)
                ? asWritten
                : GreekLemmaBridge.Candidates(choice.Lemma).Select(c => spelling.GetValueOrDefault(c)).FirstOrDefault(c => c is not null);
            if (lemma is not null)
            {
                word.Lemma = UnicodeNormalization.Composed(lemma);
            }
        }

        text.Provenance.Add(new("lemma", "GLAUx (Resources/Glaux), the lemma a folded form takes at least 4 times in 5"));
    }
}

/// <summary>
/// The folded form of the whole printed word on every member of a run of words joined by empty trailers,
/// as the load's <c>printed-forms</c> step writes it; null on a word printed alone.
/// </summary>
internal sealed class PrintedWords : ITextStage
{
    public bool AppliesTo(string slug) => true;

    public IReadOnlyList<string> Sources(string slug) => [];

    public void Apply(ConvertedText text, string resources)
    {
        foreach (var verse in text.Verses)
        {
            var words = verse.Words;
            var start = 0;
            for (var i = 1; i <= words.Count; i++)
            {
                if (i < words.Count && words[i - 1].Trailer.Length == 0)
                {
                    continue;
                }

                if (i - start > 1)
                {
                    var folded = string.Concat(words.Skip(start).Take(i - start).Select(w => w.Normalised ?? string.Empty));
                    for (var j = start; j < i; j++)
                    {
                        words[j].Graphical = folded;
                    }
                }

                start = i;
            }
        }
    }
}
