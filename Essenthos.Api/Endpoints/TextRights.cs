using Essenthos.Core.Corpus;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// What a reader may do with each text, beside the licence's name: the <see cref="ReuseTerms"/> word a
/// page turns into a sentence in the reader's language, the rights holder's own site where the copy was
/// read from somebody else's, and a short note in every interface language where the licence's name
/// does not say it all.
///
/// Keyed by slug as <see cref="TextSummaries"/> is. The terms are read from the text's licence and
/// recorded permission unless a text is listed here, and a text is listed only where that reading
/// would tell a reader more than the source allows: a public-domain translation whose digital copy
/// carries its own conditions, a public domain that holds in one country, a book or two under
/// stricter terms than the rest.
/// </summary>
internal static class TextRights
{
    private sealed record Entry(
        string? Terms = null,
        string? HolderUrl = null,
        IReadOnlyDictionary<string, string>? Note = null);

    private const string Bridgeconn = "https://www.bridgeconn.com/";
    private const string OpenBible = "https://open.bible/";

    private static readonly IReadOnlyDictionary<string, string> CopiedUnchanged = Say(
        "The digital copy it is read from may be copied only free of charge and with its biblical text unchanged.",
        "Цифрову копію, з якої взято цей текст, можна поширювати лише безкоштовно й без змін у біблійному тексті.",
        "Die digitale Kopie, aus der dieser Text stammt, darf nur kostenlos und mit unverändertem Bibeltext weitergegeben werden.",
        "La copia digital de la que procede solo puede difundirse gratis y con el texto bíblico sin cambios.");

    private static readonly Dictionary<string, Entry> Listed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["KJV"] = new(ReuseTerms.Own, Note: Say(
            "The King James Version is in the public domain outside the United Kingdom. " + CopiedUnchanged["en"],
            "Біблія короля Якова — суспільне надбання за межами Великої Британії. " + CopiedUnchanged["uk"],
            "Die King-James-Bibel ist außerhalb des Vereinigten Königreichs gemeinfrei. " + CopiedUnchanged["de"],
            "La Biblia del rey Jacobo es de dominio público fuera del Reino Unido. " + CopiedUnchanged["es"])),
        ["RUSV"] = new(ReuseTerms.Own, Note: Say(
            "The Synodal translation is in the public domain. " + CopiedUnchanged["en"],
            "Синодальний переклад — суспільне надбання. " + CopiedUnchanged["uk"],
            "Die Synodalübersetzung ist gemeinfrei. " + CopiedUnchanged["de"],
            "La traducción sinodal es de dominio público. " + CopiedUnchanged["es"])),
        ["BBE"] = new(ReuseTerms.Own, Note: Say(
            "In the public domain in the United States. Elsewhere it may remain in copyright until 2038.",
            "Суспільне надбання у США. В інших країнах авторське право на нього може діяти до 2038 року.",
            "In den Vereinigten Staaten gemeinfrei. Anderswo kann sie bis 2038 urheberrechtlich geschützt sein.",
            "De dominio público en Estados Unidos. En otros países puede seguir protegida por derechos de autor hasta 2038.")),
        ["KRV"] = new(ReuseTerms.Attribution, "https://www.bskorea.or.kr/", Say(
            "Free to use without a fee. The Korean Bible Society asks to be named and that the wording and the title be left unaltered.",
            "Можна використовувати безкоштовно. Корейське біблійне товариство просить називати його й не змінювати ні тексту, ні назви.",
            "Ohne Gebühr frei nutzbar. Die Koreanische Bibelgesellschaft bittet, sie zu nennen und Wortlaut und Titel nicht zu verändern.",
            "Se puede usar sin pagar. La Sociedad Bíblica de Corea pide que se la nombre y que no se alteren ni el texto ni el título.")),
        ["SCH1951"] = new(ReuseTerms.Own, "https://www.bibelgesellschaft.com/", Say(
            "Copyright 1951, Genfer Bibelgesellschaft. The copy it is read from may be passed on free of charge for non-commercial use.",
            "Авторське право 1951 року належить Женевському біблійному товариству. Копію, з якої її взято, можна безкоштовно поширювати для некомерційного використання.",
            "Copyright 1951, Genfer Bibelgesellschaft. Die Kopie, aus der sie stammt, darf kostenlos für nichtkommerzielle Zwecke weitergegeben werden.",
            "Copyright 1951, Sociedad Bíblica de Ginebra. La copia de la que procede puede difundirse gratis para usos no comerciales.")),
        ["GEEZ81"] = new(ReuseTerms.NonCommercialShareAlike, "https://betamasaheft.eu/", Say(
            "Shared under CC BY-SA 4.0. Fifteen books — Genesis to Ruth, the four books of Kingdoms, the Psalms, Wisdom and 4 Baruch — are also for non-commercial use only, with the notices of those who digitised them kept.",
            "Поширюється за CC BY-SA 4.0. П'ятнадцять книг — від Буття до Рут, чотири книги Царств, Псалтир, Премудрість і 4 Варуха — крім того, лише для некомерційного використання, зі збереженням застережень тих, хто їх оцифрував.",
            "Unter CC BY-SA 4.0. Fünfzehn Bücher – Genesis bis Rut, die vier Bücher der Königtümer, die Psalmen, Weisheit und 4 Baruch – dürfen außerdem nur nichtkommerziell genutzt werden, mit den Hinweisen derer, die sie digitalisiert haben.",
            "Bajo CC BY-SA 4.0. Quince libros —de Génesis a Rut, los cuatro libros de los Reinos, los Salmos, Sabiduría y 4 Baruc— son además solo para uso no comercial, conservando los avisos de quienes los digitalizaron.")),
        ["CUV"] = new(HolderUrl: "https://bible.fhl.net/"),
        ["CUVS"] = new(HolderUrl: "https://bible.fhl.net/"),
        ["IRV2019"] = new(HolderUrl: Bridgeconn),
        ["IRVASM"] = new(HolderUrl: Bridgeconn),
        ["IRVBEN"] = new(HolderUrl: Bridgeconn),
        ["IRVGUJ"] = new(HolderUrl: Bridgeconn),
        ["IRVKAN"] = new(HolderUrl: Bridgeconn),
        ["IRVMAL"] = new(HolderUrl: Bridgeconn),
        ["IRVMAR"] = new(HolderUrl: Bridgeconn),
        ["IRVTAM"] = new(HolderUrl: Bridgeconn),
        ["IRVTEL"] = new(HolderUrl: Bridgeconn),
        ["IRVURD"] = new(HolderUrl: Bridgeconn),
        ["JND2024"] = new(HolderUrl: "https://www.editeurbpc.com/telechargements/JND"),
        ["NPU2022"] = new(HolderUrl: OpenBible),
        ["OHCB"] = new(HolderUrl: OpenBible),
        ["SBLGNT"] = new(HolderUrl: "https://sblgnt.com/"),
        ["UBIO"] = new(HolderUrl: "https://www.biblesociety.org.uk/"),
        ["ULT"] = new(HolderUrl: "https://www.unfoldingword.org/ult"),
        ["UST"] = new(HolderUrl: "https://www.unfoldingword.org/ust"),
    };

    /// <summary>What the text's terms let a reader do, as <see cref="ReuseTerms"/> words it.</summary>
    /// <param name="redistribution">The recorded permission, spelled as the API spells it.</param>
    public static string Terms(string slug, string? licence, string? redistribution) =>
        Listed.TryGetValue(slug, out var entry) && entry.Terms is { } terms
            ? terms
            : ReuseTerms.Of(licence, redistribution);

    /// <summary>The rights holder's own site, where one is known and it is not the address the copy came from.</summary>
    public static string? HolderUrl(string slug) => Listed.TryGetValue(slug, out var entry) ? entry.HolderUrl : null;

    /// <summary>What the licence's name leaves unsaid, keyed by interface language; null for most texts.</summary>
    public static IReadOnlyDictionary<string, string>? Note(string slug) =>
        Listed.TryGetValue(slug, out var entry) ? entry.Note : null;

    /// <summary>Every slug listed, for the test that checks each is a text the corpus loads.</summary>
    internal static IEnumerable<string> Slugs => Listed.Keys;

    private static IReadOnlyDictionary<string, string> Say(string en, string uk, string de, string es) =>
        new Dictionary<string, string> { ["en"] = en, ["uk"] = uk, ["de"] = de, ["es"] = es };
}
