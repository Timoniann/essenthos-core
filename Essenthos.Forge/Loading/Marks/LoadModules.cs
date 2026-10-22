namespace Essenthos.Core.Loading;

/// <summary>
/// The groups of load steps <c>load --only</c> can name instead of listing their steps. A group is a
/// name for the steps that write one part of the corpus; it says nothing about order, which stays the
/// load's own.
/// </summary>
internal static class LoadModules
{
    /// <summary>Every witness's words, verses and notes, and the repairs their files need.</summary>
    public const string Texts = "texts";

    /// <summary>Strong's concordance and its translations, and the lexicons and lemmas of the other languages.</summary>
    public const string Lexicons = "lexicons";

    /// <summary>BHSA's syntax and every text placed in the canonical frame.</summary>
    public const string SyntaxFrame = "syntax-frame";

    /// <summary>What other datasets say about the Greek words: a second parsing, the Macula annotations.</summary>
    public const string OriginalAnnotations = "original-annotations";

    /// <summary>Every word and verse link between texts, the recipe's runs and EVIDENTIA's verdicts.</summary>
    public const string Links = "links";

    /// <summary>The people, places and events of the datasets, the registers, and which word names whom.</summary>
    public const string EncyclopediaRegisters = "encyclopedia-registers";

    /// <summary>The records, rulings, readings and lines this project writes itself.</summary>
    public const string Records = "records";

    /// <summary>The names settled again over the records, and the passes over the names.</summary>
    public const string Names = "names";

    /// <summary>What is counted or listed from all the rest: the lexicon's phrases, places on the map, pictures.</summary>
    public const string Derived = "derived";

    /// <summary>The verification pass.</summary>
    public const string Verify = "verify";

    public static IReadOnlyList<string> All { get; } =
        [Texts, Lexicons, SyntaxFrame, OriginalAnnotations, Links, EncyclopediaRegisters, Records, Names, Derived, Verify];
}
