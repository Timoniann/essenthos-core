using Essenthos.Core.Loading;

namespace Essenthos.Core.Door43;

/// <summary>
/// A Bible on Door43 whose words its translators tied to the Hebrew or Greek word each renders, in
/// the USFM 3 alignment the Ukrainian interlinear and the unfoldingWord Literal Text arrive in.
///
/// Some are the text the corpus loads from the same file; others are a release of an edition the
/// corpus already holds from elsewhere, and only their links are drawn from them — onto our words,
/// where the release's words and ours are the same words, and refused verse by verse where they are
/// not. Every one names unfoldingWord's own Hebrew Bible and Greek New Testament as what it aligns to,
/// and is joined to BHSA and Nestle 1904 the way the Literal Text is.
///
/// Their releases were exported over several years, and the older exports leave the Strong number of
/// many original words empty and name them by spelling alone, so a milestone is read by its spelling
/// where its number is missing.
/// </summary>
/// <param name="Folder">The folder under <c>Resources/Door43</c> the release is fetched into.</param>
/// <param name="Slug">The text whose words the alignment ties.</param>
/// <param name="Source">
/// What every link drawn from the release says about itself. It names the release, because an
/// alignment is revised from one release to the next, and a reload finds the statements by it.
/// </param>
internal sealed record Door43AlignedBible(string Folder, string Slug, string Source)
{
    /// <summary>In the order the corpus loads them.</summary>
    public static IReadOnlyList<Door43AlignedBible> All { get; } =
    [
        new("ar_avd", EbibleTextSource.VanDyck,
            "BSOJ's alignment of the Van Dyck to unfoldingWord's Hebrew Bible and Greek New Testament, made in "
            + "translationCore, release v6.9, git.door43.org/BSOJ/ar_avd, CC BY-SA 4.0"),
        new("hi_irv", EbibleTextSource.IrvHindi,
            "Door43 alignment of the Indian Revised Version (Hindi) to unfoldingWord's Hebrew Bible and Greek "
            + "New Testament, made in translationCore, release v12, git.door43.org/Door43-Catalog/hi_irv, CC BY-SA 4.0"),
        new("bn_irv", Door43TextSource.IrvBengali,
            "Door43 alignment of the Indian Revised Version (Bengali) to unfoldingWord's Greek New Testament, made "
            + "in translationCore, release v5, git.door43.org/Door43-Catalog/bn_irv, CC BY-SA 4.0"),
        new("as_irv", Door43TextSource.IrvAssamese,
            "Door43 alignment of the Indian Revised Version (Assamese) to unfoldingWord's Greek New Testament, made "
            + "in translationCore, release v3, git.door43.org/Door43-Catalog/as_irv, CC BY-SA 4.0"),
        new("gu_irv", Door43TextSource.IrvGujarati,
            "Door43 alignment of the Indian Revised Version (Gujarati) to unfoldingWord's Greek New Testament, made "
            + "in translationCore, release v4, git.door43.org/Door43-Catalog/gu_irv, CC BY-SA 4.0"),
        new("kn_irv", Door43TextSource.IrvKannada,
            "Door43 alignment of the Indian Revised Version (Kannada) to unfoldingWord's Greek New Testament, made "
            + "in translationCore, release v5, git.door43.org/Door43-Catalog/kn_irv, CC BY-SA 4.0"),
        new("ml_irv", Door43TextSource.IrvMalayalam,
            "Door43 alignment of the Indian Revised Version (Malayalam) to unfoldingWord's Greek New Testament, made "
            + "in translationCore, release v5, git.door43.org/Door43-Catalog/ml_irv, CC BY-SA 4.0"),
        new("mr_irv", Door43TextSource.IrvMarathi,
            "Door43 alignment of the Indian Revised Version (Marathi) to unfoldingWord's Greek New Testament, made "
            + "in translationCore, release v4, git.door43.org/Door43-Catalog/mr_irv, CC BY-SA 4.0"),
        new("pa_irv", Door43TextSource.IrvPunjabi,
            "Door43 alignment of the Indian Revised Version (Punjabi) to unfoldingWord's Greek New Testament, made "
            + "in translationCore, release v3, git.door43.org/Door43-Catalog/pa_irv, CC BY-SA 4.0"),
        new("ta_irv", Door43TextSource.IrvTamil,
            "Door43 alignment of the Indian Revised Version (Tamil) to unfoldingWord's Greek New Testament, made "
            + "in translationCore, release v3, git.door43.org/Door43-Catalog/ta_irv, CC BY-SA 4.0"),
        new("te_irv", Door43TextSource.IrvTelugu,
            "Door43 alignment of the Indian Revised Version (Telugu) to unfoldingWord's Greek New Testament, made "
            + "in translationCore, release v2, git.door43.org/Door43-Catalog/te_irv, CC BY-SA 4.0"),
        new("ur-deva_irv", Door43TextSource.IrvUrdu,
            "Door43 alignment of the Indian Revised Version (Urdu, in Devanagari) to unfoldingWord's Greek New "
            + "Testament, made in translationCore, release v2, git.door43.org/Door43-Catalog/ur-deva_irv, CC BY-SA 4.0"),
        new("en_ust", Door43TextSource.Simplified,
            "unfoldingWord Simplified Text alignment to unfoldingWord's Hebrew Bible and Greek New Testament, "
            + "release 91, git.door43.org/unfoldingWord/en_ust, CC BY-SA 4.0"),
        new("vi_glt", Door43TextSource.VietnameseLiteral,
            "Door43 alignment of the Vietnamese Literal Text to unfoldingWord's Hebrew Bible and Greek New Testament, "
            + "made in translationCore, release v1, git.door43.org/vi_gl/vi_glt, CC BY-SA 4.0"),
        new("fr_lsg", EbibleTextSource.Segond,
            "Door43 alignment of the Louis Segond 1910 New Testament to unfoldingWord's Greek New Testament, "
            + "release v1, git.door43.org/fr_gl/fr-textTranslation-FR_LSG, CC BY-SA 4.0"),
    ];

    public static Door43AlignedBible? Of(string slug) => All.FirstOrDefault(bible => bible.Slug == slug);
}
