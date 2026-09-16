namespace Essenthos.Core.Loading.Links.Evidentia;

internal interface ILanguagePack
{
    bool Supports(string language);

    EvidentiaAnalysis Analyse(EvidentiaToken token);
}

internal interface IEvidentiaEvidenceSource
{
    IEnumerable<EvidentiaEvidence> Find(EvidentiaAnalysis source, EvidentiaAnalysis target);
}

internal sealed class LanguagePackRegistry(IEnumerable<ILanguagePack> packs)
{
    public bool TryAnalyse(EvidentiaToken token, out EvidentiaAnalysis analysis)
    {
        var pack = packs.FirstOrDefault(candidate => candidate.Supports(token.Language));
        if (pack is null)
        {
            analysis = null!;
            return false;
        }

        analysis = pack.Analyse(token);
        return true;
    }
}

internal sealed class EnglishLanguagePack : ILanguagePack
{
    private static readonly HashSet<string> FunctionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "as", "at", "be", "but", "by", "for", "from", "he", "in", "is", "it",
        "of", "on", "or", "the", "to", "unto", "was", "were", "with", "ye", "you",
    };

    public bool Supports(string language) => language.Equals("eng", StringComparison.OrdinalIgnoreCase);

    public EvidentiaAnalysis Analyse(EvidentiaToken token)
    {
        var normalised = EnglishStemmer.Stem(token.Surface);
        return new EvidentiaAnalysis(
            token,
            normalised,
            Normalise(token.Lemma) ?? normalised,
            token.PartOfSpeech,
            FunctionWords.Contains(token.Surface) ? EvidentiaWordClass.Function : EvidentiaWordClass.Content,
            Capabilities(token));
    }

    private static string? Normalise(string? lemma) => string.IsNullOrWhiteSpace(lemma) ? null : lemma.ToLowerInvariant();

    private static LanguagePackCapability Capabilities(EvidentiaToken token) =>
        LanguagePackCapability.Normalisation | LanguagePackCapability.FunctionWords
        | (string.IsNullOrWhiteSpace(token.Lemma) ? LanguagePackCapability.None : LanguagePackCapability.Lemma)
        | (string.IsNullOrWhiteSpace(token.PartOfSpeech) ? LanguagePackCapability.None : LanguagePackCapability.PartOfSpeech)
        | (token.Morphology is { Count: > 0 } ? LanguagePackCapability.Morphology : LanguagePackCapability.None);
}

internal sealed class SlavicLanguagePack : ILanguagePack
{
    private static readonly HashSet<string> FunctionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Ukrainian: these are grammar/context carriers, not reliable reverse-dictionary anchors.
        "а", "аби", "або", "але", "без", "б", "би", "бо", "буде", "будеш", "буду", "будуть", "був", "була", "було", "були", "в", "від", "він", "вона",
        "вони", "ви", "де", "до", "же", "з", "за", "і", "й", "із", "її", "їх", "його", "коли", "лише",
        "ми", "на", "над", "не", "ні", "о", "по", "під", "після", "при", "про", "та", "те", "то", "у",
        "усе", "це", "через", "чи", "що", "щоб", "як", "я",
        // Russian: kept alongside Ukrainian until the packs split into per-language morphology.
        "без", "бы", "был", "была", "были", "будет", "в", "во", "вы", "для", "его", "ее", "если", "же", "за",
        "и", "из", "их", "к", "как", "когда", "ли", "лишь", "мы", "на", "не", "о", "он", "она", "они", "от",
        "по", "с", "то", "у", "через", "что", "чтобы", "это", "я",
    };

    public bool Supports(string language) =>
        language.Equals("rus", StringComparison.OrdinalIgnoreCase)
        || language.Equals("ukr", StringComparison.OrdinalIgnoreCase);

    public EvidentiaAnalysis Analyse(EvidentiaToken token)
    {
        var normalised = SlavicStemmer.Stem(token.Surface);
        return new EvidentiaAnalysis(
            token,
            normalised,
            Normalise(token.Lemma) ?? normalised,
            token.PartOfSpeech,
            FunctionWords.Contains(token.Surface) ? EvidentiaWordClass.Function : EvidentiaWordClass.Content,
            Capabilities(token));
    }

    private static string? Normalise(string? lemma) => string.IsNullOrWhiteSpace(lemma) ? null : lemma.ToLowerInvariant();

    private static LanguagePackCapability Capabilities(EvidentiaToken token) =>
        LanguagePackCapability.Normalisation | LanguagePackCapability.FunctionWords
        | (string.IsNullOrWhiteSpace(token.Lemma) ? LanguagePackCapability.None : LanguagePackCapability.Lemma)
        | (string.IsNullOrWhiteSpace(token.PartOfSpeech) ? LanguagePackCapability.None : LanguagePackCapability.PartOfSpeech)
        | (token.Morphology is { Count: > 0 } ? LanguagePackCapability.Morphology : LanguagePackCapability.None);
}

/// <summary>
/// Original-language tokens need an analysis object even when no cross-language form comparison
/// is possible. Their source-provided lemma and morphology remain available to evidence layers.
///
/// A witness that states a part of speech states its own function words with it: BHSA marks the
/// article, the preposition and the conjunction, and Nestle 1904 carries MACULA's classes. Calling
/// every original-language word a content word, as this did, let a Hebrew article be reserved as
/// somebody's rendering.
/// </summary>
internal sealed class OriginalLanguagePack : ILanguagePack
{
    public bool Supports(string language) =>
        language.Equals("hbo", StringComparison.OrdinalIgnoreCase)
        || language.Equals("arc", StringComparison.OrdinalIgnoreCase)
        || language.Equals("grc", StringComparison.OrdinalIgnoreCase);

    public EvidentiaAnalysis Analyse(EvidentiaToken token)
    {
        var normalised = token.Surface.ToLowerInvariant();
        var capabilities = LanguagePackCapability.Normalisation;
        if (!string.IsNullOrWhiteSpace(token.Lemma))
        {
            capabilities |= LanguagePackCapability.Lemma;
        }
        if (!string.IsNullOrWhiteSpace(token.PartOfSpeech))
        {
            capabilities |= LanguagePackCapability.PartOfSpeech;
        }
        if (token.Morphology is { Count: > 0 })
        {
            capabilities |= LanguagePackCapability.Morphology;
        }

        var function = EvidentiaMorphologyLabels.IsFunctionWord(token.PartOfSpeech, token.Language);
        if (function.HasValue)
        {
            capabilities |= LanguagePackCapability.FunctionWords;
        }

        return new EvidentiaAnalysis(
            token,
            normalised,
            token.Lemma?.ToLowerInvariant(),
            token.PartOfSpeech,
            function switch
            {
                true => EvidentiaWordClass.Function,
                false => EvidentiaWordClass.Content,
                null => EvidentiaWordClass.Unknown,
            },
            capabilities);
    }
}
