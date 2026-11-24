namespace Essenthos.Core.Pages;

/// <summary>
/// One of the site's interface languages, as its addresses spell it: English at the root, the others
/// under a prefix of their own — <c>/uk/read/mark/3</c> — so that each language's page is a page of its
/// own to a search engine. The home page of a prefixed language is <c>/uk/</c>, as the reader writes it.
/// </summary>
/// <param name="Code">The interface locale, which is also the address prefix.</param>
/// <param name="Corpus">The same language as the corpus spells it, ISO 639-3.</param>
/// <param name="Locale">Open Graph's spelling, language and region.</param>
/// <param name="Text">The translation a reader of this language opens in before choosing one.</param>
internal sealed record SiteLanguage(string Code, string Corpus, string Locale, string Text)
{
    public bool IsEnglish => Code == English.Code;

    public string Prefix => IsEnglish ? "" : "/" + Code;

    /// <summary>The address of a page in this language, from its English address.</summary>
    public string Path(string english) => Prefix + english;

    public static readonly SiteLanguage English = new("en", "eng", "en_US", "BSB");

    public static readonly SiteLanguage Ukrainian = new("uk", "ukr", "uk_UA", "UBIO");

    public static readonly SiteLanguage German = new("de", "deu", "de_DE", "LUTH1912");

    public static readonly SiteLanguage Spanish = new("es", "spa", "es_ES", "RV1909");

    public static readonly IReadOnlyList<SiteLanguage> All = [English, Ukrainian, German, Spanish];

    public static SiteLanguage? Prefixed(string segment) =>
        All.FirstOrDefault(language => !language.IsEnglish && language.Code == segment);
}
