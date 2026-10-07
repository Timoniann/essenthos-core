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
        token = EnglishPersonalPronouns.Complete(EnglishSpelling.Bare(token));
        var normalised = EnglishStemmer.Stem(EnglishSpelling.Common(token.Surface));
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
/// One spelling for what two editions write two ways: the possessive's <em>'s</em> comes off, since the
/// original writes the noun either way, and the British <em>-our</em> of <em>neighbour</em> and a few
/// other spellings the King James keeps become the ones a modern translation prints.
/// </summary>
internal static class EnglishSpelling
{
    private static readonly string[] Possessives = ["’s", "'s", "’", "'"];

    private static readonly string[] OurWords =
    [
        "neighbour", "honour", "labour", "favour", "saviour", "savour", "colour", "behaviour", "harbour",
        "rumour", "vapour", "odour", "valour", "armour", "fervour", "humour", "splendour", "succour",
        "clamour", "vigour", "ardour", "endeavour",
    ];

    private static readonly (string British, string American)[] Stems =
    [
        ("plough", "plow"), ("defence", "defense"), ("offence", "offense"), ("judgement", "judgment"),
        ("grey", "gray"), ("travell", "travel"), ("jewell", "jewel"), ("counsell", "counsel"), ("marvell", "marvel"),
    ];

    private static readonly string[] Negated = ["n’t", "n't"];

    /// <summary>
    /// The word without the quotation marks and dashes an edition prints against it: <em>‘Behold</em> is
    /// <em>Behold</em>, and <em>them—do</em>, two words the edition joined with a dash, is the first.
    /// </summary>
    public static EvidentiaToken Bare(EvidentiaToken token)
    {
        var surface = token.Surface;
        var dash = surface.IndexOfAny(['—', '–']);
        if (dash > 0 && surface[..dash].Any(char.IsLetter))
        {
            surface = surface[..dash];
        }

        var bare = surface.Trim().TrimStart(Marks).TrimEnd(Marks);
        return bare.Length > 0 && bare != token.Surface ? token with { Surface = bare } : token;
    }

    private static readonly char[] Marks = ['“', '”', '‘', '’', '"', '\'', '(', ')', '[', ']', '—', '–', '-', ',', '.', ';', ':', '!', '?'];

    public static string Common(string surface)
    {
        var word = surface.ToLowerInvariant();
        // didn’t, won’t, can’t: the original writes the negation, and the tense the contraction carries
        // is English's own.
        if (Negated.Any(ending => word.EndsWith(ending, StringComparison.Ordinal) && word.Length > ending.Length))
        {
            return "not";
        }

        foreach (var possessive in Possessives)
        {
            if (word.Length > possessive.Length + 2 && word.EndsWith(possessive, StringComparison.Ordinal))
            {
                word = word[..^possessive.Length];
                break;
            }
        }

        foreach (var our in OurWords)
        {
            if (word.StartsWith(our, StringComparison.Ordinal))
            {
                return our[..^3] + "or" + word[our.Length..];
            }
        }

        foreach (var (british, american) in Stems)
        {
            if (word.StartsWith(british, StringComparison.Ordinal))
            {
                return american + word[british.Length..];
            }
        }

        return word;
    }
}

/// <summary>
/// What an English personal pronoun says of itself, for the word the parser did not read as one: the
/// Berean capitalises <em>Him</em> and <em>His</em> of God and the parser takes them for names, and it
/// reads the King James <em>thee</em> as a numeral or a noun. Only what the form itself fixes is
/// stated, so <em>her</em>, <em>you</em> and <em>it</em> say nothing of case.
/// </summary>
internal static class EnglishPersonalPronouns
{
    private static readonly Dictionary<string, (string Person, string? Number, string? Case, bool Possessive)> Forms =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["i"] = ("1", "Sing", "Nom", false), ["me"] = ("1", "Sing", "Acc", false),
            ["my"] = ("1", "Sing", null, true), ["mine"] = ("1", "Sing", null, true),
            ["we"] = ("1", "Plur", "Nom", false), ["us"] = ("1", "Plur", "Acc", false), ["our"] = ("1", "Plur", null, true),
            ["thou"] = ("2", "Sing", "Nom", false), ["thee"] = ("2", "Sing", "Acc", false),
            ["thy"] = ("2", "Sing", null, true), ["thine"] = ("2", "Sing", null, true),
            ["ye"] = ("2", "Plur", "Nom", false), ["you"] = ("2", null, null, false), ["your"] = ("2", null, null, true),
            ["he"] = ("3", "Sing", "Nom", false), ["him"] = ("3", "Sing", "Acc", false), ["his"] = ("3", "Sing", null, true),
            ["she"] = ("3", "Sing", "Nom", false), ["her"] = ("3", "Sing", null, false),
            ["it"] = ("3", "Sing", null, false), ["its"] = ("3", "Sing", null, true),
            ["they"] = ("3", "Plur", "Nom", false), ["them"] = ("3", "Plur", "Acc", false), ["their"] = ("3", "Plur", null, true),
        };

    public static EvidentiaToken Complete(EvidentiaToken token)
    {
        if (!Forms.TryGetValue(token.Surface, out var form)
            || token.Morphology?.Any(pair => pair.Key.Equals("PronType", StringComparison.OrdinalIgnoreCase)) == true)
        {
            return token;
        }

        var features = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["PronType"] = "Prs", ["Person"] = form.Person };
        if (form.Number is not null) features["Number"] = form.Number;
        if (form.Case is not null) features["Case"] = form.Case;
        if (form.Possessive) features["Poss"] = "Yes";
        return token with { PartOfSpeech = "PRON", Morphology = features };
    }
}

/// <summary>
/// A language whose words are compared by a stemmer of its own and which has its own function words,
/// because a word that carries only grammar in one language is a content word in another, and the pack
/// that claims a language is the one that decides which of its words can anchor a link.
/// </summary>
internal abstract class StemmedLanguagePack(string language, IReadOnlySet<string> functionWords, Func<string, string> stem)
    : ILanguagePack
{
    public bool Supports(string candidate) => candidate.Equals(language, StringComparison.OrdinalIgnoreCase);

    public EvidentiaAnalysis Analyse(EvidentiaToken token)
    {
        var normalised = stem(token.Surface);
        return new EvidentiaAnalysis(
            token,
            normalised,
            Normalise(token.Lemma) ?? normalised,
            token.PartOfSpeech,
            functionWords.Contains(token.Surface) ? EvidentiaWordClass.Function : EvidentiaWordClass.Content,
            Capabilities(token));
    }

    private static string? Normalise(string? lemma) => string.IsNullOrWhiteSpace(lemma) ? null : lemma.ToLowerInvariant();

    private static LanguagePackCapability Capabilities(EvidentiaToken token) =>
        LanguagePackCapability.Normalisation | LanguagePackCapability.FunctionWords
        | (string.IsNullOrWhiteSpace(token.Lemma) ? LanguagePackCapability.None : LanguagePackCapability.Lemma)
        | (string.IsNullOrWhiteSpace(token.PartOfSpeech) ? LanguagePackCapability.None : LanguagePackCapability.PartOfSpeech)
        | (token.Morphology is { Count: > 0 } ? LanguagePackCapability.Morphology : LanguagePackCapability.None);
}

/// <summary>Ukrainian and Russian share a stemmer and nothing else.</summary>
internal abstract class SlavicLanguagePack(string language, IReadOnlySet<string> functionWords)
    : StemmedLanguagePack(language, functionWords, word => SlavicStemmer.Stem(word));

internal sealed class UkrainianLanguagePack() : SlavicLanguagePack("ukr", FunctionWords)
{
    // Grammar and context carriers, not reliable reverse-dictionary anchors.
    internal static readonly IReadOnlySet<string> FunctionWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "а", "аби", "або", "але", "без", "б", "би", "бо", "буде", "будеш", "буду", "будуть", "був", "була", "було", "були", "в", "від", "він", "вона",
        "вони", "ви", "де", "до", "же", "з", "за", "і", "й", "із", "її", "їх", "його", "коли", "лише",
        "ми", "на", "над", "не", "ні", "о", "по", "під", "після", "при", "про", "та", "те", "то", "у",
        "усе", "це", "через", "чи", "що", "щоб", "як", "я",
    };
}

internal sealed class RussianLanguagePack() : SlavicLanguagePack("rus", FunctionWords)
{
    internal static readonly IReadOnlySet<string> FunctionWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "без", "бы", "был", "была", "были", "будет", "в", "во", "вы", "для", "его", "ее", "если", "же", "за",
        "и", "из", "их", "к", "как", "когда", "ли", "лишь", "мы", "на", "не", "о", "он", "она", "они", "от",
        "по", "с", "то", "у", "через", "что", "чтобы", "это", "я",
    };
}

/// <summary>Luther's and the Elberfelder's German, spelt as 1905 and 1912 spelt it: <em>daß</em>, <em>ward</em>.</summary>
internal sealed class GermanLanguagePack() : StemmedLanguagePack("deu", FunctionWords, GermanStemmer.Stem)
{
    internal static readonly IReadOnlySet<string> FunctionWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "der", "die", "das", "des", "dem", "den", "ein", "eine", "einen", "einem", "einer", "eines",
        "und", "oder", "aber", "denn", "doch", "daß", "dass", "so", "wie", "als", "wenn", "ob", "da",
        "zu", "zum", "zur", "in", "im", "ins", "an", "am", "auf", "aus", "bei", "beim", "mit", "nach", "von", "vom",
        "vor", "über", "unter", "um", "durch", "für", "gegen", "bis",
        "ich", "du", "er", "sie", "es", "wir", "ihr", "mich", "dich", "mir", "dir", "ihn", "ihm", "ihnen",
        "uns", "euch", "sich", "ist", "war", "sind", "waren", "wird", "werden", "ward", "wurde", "hat", "haben", "hatte",
        "nicht", "auch",
        // The possessive determiners write the original's suffix or genitive pronoun, which is never
        // where a learned rendering puts them: seine Knechte is not αὐτῷ.
        "mein", "meine", "meinen", "meinem", "meiner", "meines",
        "dein", "deine", "deinen", "deinem", "deiner", "deines",
        "sein", "seine", "seinen", "seinem", "seiner", "seines",
        "ihre", "ihren", "ihrem", "ihrer", "ihres",
        "unser", "unsere", "unseren", "unserem", "unserer", "unseres", "unsre", "unsren", "unsrem", "unsrer", "unsres", "unsern", "unserm",
        "euer", "eure", "euren", "eurem", "eurer", "eures", "euern", "euerm",
        "sondern", "weil", "indem", "damit", "nachdem", "obgleich", "obwohl", "ehe", "bevor", "sobald", "solange",
        "wider", "gen",
    };
}

/// <summary>The Reina-Valera's Spanish of 1909, which still writes <em>á</em> and <em>fué</em>.</summary>
internal sealed class SpanishLanguagePack() : StemmedLanguagePack("spa", FunctionWords, SpanishStemmer.Stem)
{
    internal static readonly IReadOnlySet<string> FunctionWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "el", "la", "los", "las", "lo", "un", "una", "unos", "unas", "y", "e", "o", "u", "pero", "mas", "que",
        "de", "del", "a", "á", "al", "en", "con", "por", "para", "sin", "sobre", "entre", "hasta", "desde",
        "él", "ella", "ellos", "ellas", "yo", "tú", "nosotros", "vosotros", "le", "les", "se", "me", "te", "nos", "os",
        "su", "sus", "mi", "mis", "tu", "tus", "es", "fué", "fue", "era", "son", "ha", "han", "había", "no", "ni",
        "como", "cuando", "si", "sino", "porque",
        "nuestro", "nuestra", "nuestros", "nuestras", "vuestro", "vuestra", "vuestros", "vuestras",
    };
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
