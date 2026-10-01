using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading;

/// <summary>
/// The translations Door43 publishes with every word tied by hand to the Hebrew or Greek word it
/// renders, read from the aligned USFM their links are drawn from: the Indian Revised Version in ten
/// languages, the unfoldingWord Simplified Text and the Vietnamese Literal Text.
///
/// They are the format the unfoldingWord Literal Text arrives in and are read the same way: the
/// milestones come out and each word is left where it stood. A book the release has not aligned yet
/// arrives as plain USFM and reads the same.
/// </summary>
internal static class Door43TextSource
{
    public const string IrvBengali = "IRVBEN";
    public const string IrvAssamese = "IRVASM";
    public const string IrvGujarati = "IRVGUJ";
    public const string IrvKannada = "IRVKAN";
    public const string IrvMalayalam = "IRVMAL";
    public const string IrvMarathi = "IRVMAR";
    public const string IrvPunjabi = "IRVPAN";
    public const string IrvTamil = "IRVTAM";
    public const string IrvTelugu = "IRVTEL";
    public const string IrvUrdu = "IRVURD";
    public const string Simplified = "UST";
    public const string VietnameseLiteral = "VIGLT";

    private const string Bcs = "Bridge Connectivity Solutions Pvt. Ltd.";

    /// <summary>The folder under <c>Resources/Door43</c> each release is fetched into, and its definition.</summary>
    public static IReadOnlyDictionary<string, TextDefinition> Definitions { get; } =
        new Dictionary<string, TextDefinition>(StringComparer.Ordinal)
        {
            ["bn_irv"] = Irv(IrvBengali, "bn_irv", "v5", "Bengali", "ben", 2020, Bcs, wholeBible: true, clearBible: true),
            ["as_irv"] = Irv(IrvAssamese, "as_irv", "v3", "Assamese", "asm", 2021, Bcs, wholeBible: true, clearBible: true),
            ["gu_irv"] = Irv(IrvGujarati, "gu_irv", "v4", "Gujarati", "guj", 2020, Bcs, wholeBible: false),
            ["kn_irv"] = Irv(IrvKannada, "kn_irv", "v5", "Kannada", "kan", 2021, Bcs, wholeBible: false),
            ["ml_irv"] = Irv(IrvMalayalam, "ml_irv", "v5", "Malayalam", "mal", 2021, Bcs, wholeBible: true),
            ["mr_irv"] = Irv(IrvMarathi, "mr_irv", "v4", "Marathi", "mar", 2020, Bcs, wholeBible: false),
            ["pa_irv"] = Irv(IrvPunjabi, "pa_irv", "v3", "Punjabi", "pan", 2020, "Door43 World Missions Community",
                wholeBible: false),
            ["ta_irv"] = Irv(IrvTamil, "ta_irv", "v3", "Tamil", "tam", 2021, Bcs, wholeBible: true),
            ["te_irv"] = Irv(IrvTelugu, "te_irv", "v2", "Telugu", "tel", 2020, Bcs, wholeBible: false),
            ["ur-deva_irv"] = Irv(IrvUrdu, "ur-deva_irv", "v2", "Urdu, in Devanagari", "urd", 2021, Bcs, wholeBible: true)
                with { Script = "Deva" },

            ["en_ust"] = new(
                Slug: Simplified,
                Name: "unfoldingWord® Simplified Text",
                NameNative: null,
                Kind: TextKind.Translation,
                Language: "eng",
                Direction: TextDirection.LeftToRight,
                Versification: Versification.English,
                PublishedYear: 2022,
                SourceUrl: "https://git.door43.org/unfoldingWord/en_ust/src/tag/v91",
                RightsHolder: "unfoldingWord",
                Licence: "CC-BY-SA-4.0",
                LicenceUrl: "https://creativecommons.org/licenses/by-sa/4.0/",
                Redistribution: Redistribution.ShareAlike,
                TextualFamily: "Alexandrian")
            {
                Translators = "unfoldingWord's translation team and the Door43 World Missions Community",
                Edition = "Release 91 of 2026-09-26: the 59 books unfoldingWord has finished checking",
                EditionYear = 2026,
                About =
                    "The companion of the unfoldingWord Literal Text: an open-licensed English translation that says "
                    + "what the Hebrew and Greek mean in plain sentences, spelling out implied information and "
                    + "turning figures of speech into what they stand for, made to show a translator the meaning "
                    + "where the Literal Text shows the form. Every one of its words is tied by hand to the word of "
                    + "unfoldingWord's own Hebrew Bible or Greek New Testament it renders, and those ties are loaded "
                    + "as the links beside it. The release holds 59 books. Its Greek is a critical text: Acts 8:37 "
                    + "stands only in a note.",
                RightsNote =
                    "Copyright 2022 by unfoldingWord, under Creative Commons Attribution-ShareAlike 4.0, stated in the "
                    + "release's LICENSE.md and its manifest. The text is served unchanged and keeps unfoldingWord's "
                    + "name and mark, as its terms ask of an unmodified copy; ShareAlike binds an adaptation of it, "
                    + "such as its searchable form, and not the texts it is read beside. The original work by "
                    + "unfoldingWord is available from unfoldingword.org/ust. The section headings of the release are "
                    + "not loaded.",
                Citation =
                    "unfoldingWord® Simplified Text, release 91, copyright 2022 by unfoldingWord, CC BY-SA 4.0, "
                    + "git.door43.org/unfoldingWord/en_ust.",
            },

            ["vi_glt"] = new(
                Slug: VietnameseLiteral,
                Name: "Vietnamese Literal Text",
                NameNative: null,
                Kind: TextKind.Translation,
                Language: "vie",
                Direction: TextDirection.LeftToRight,
                Versification: Versification.English,
                PublishedYear: 2025,
                SourceUrl: "https://git.door43.org/vi_gl/vi_glt/src/tag/v1",
                RightsHolder: "Far East Broadcasting Company",
                Licence: "CC-BY-SA-4.0",
                LicenceUrl: "https://creativecommons.org/licenses/by-sa/4.0/",
                Redistribution: Redistribution.ShareAlike,
                TextualFamily: "Alexandrian")
            {
                Translators = "The Door43 World Missions Community, for the Far East Broadcasting Company",
                Edition = "Release v1 of 2025-04-16: the New Testament and Ruth",
                About =
                    "An open-licensed Vietnamese translation made from the unfoldingWord Literal Text, keeping the form "
                    + "of the Hebrew and Greek wherever Vietnamese will bear it, so that translators into other "
                    + "languages can read the originals through it. The release holds the New Testament and Ruth, and "
                    + "every one of its words is tied by hand to the word of unfoldingWord's Hebrew Bible or Greek New "
                    + "Testament it renders; those ties are loaded as the links beside it. Its Greek is a critical "
                    + "text: Acts 8:37 stands only in a note.",
                RightsNote =
                    "Under Creative Commons Attribution-ShareAlike 4.0, stated in the release's LICENSE.md and its "
                    + "manifest, which names the Far East Broadcasting Company as publisher. ShareAlike binds an "
                    + "adaptation of this text, such as its searchable form, and not the texts it is read beside. "
                    + "The text is served unchanged; the section headings of the release are not loaded.",
                Citation =
                    "Vietnamese Literal Text (Tiếng Việt), release v1, Far East Broadcasting Company, CC BY-SA 4.0, "
                    + "git.door43.org/vi_gl/vi_glt.",
            },
        };

    public static TextSource Read(string folder)
    {
        var name = new DirectoryInfo(folder).Name;
        if (!Definitions.TryGetValue(name, out var definition))
        {
            throw new ArgumentException(
                $"There is no text definition for the Door43 folder \"{name}\". A translation cannot be loaded "
                + "without one: its licence and provenance are part of the definition, not something filled in "
                + "afterwards.",
                nameof(folder));
        }

        return UnfoldingWordTextSource.Read(folder, definition);
    }

    /// <param name="wholeBible">Whether the release holds the Old Testament as well as the New.</param>
    /// <param name="clearBible">
    /// Whether Clear Bible aligned the same text too, and with it stated other terms for it.
    /// </param>
    private static TextDefinition Irv(
        string slug,
        string folder,
        string release,
        string language,
        string code,
        int issued,
        string rightsHolder,
        bool wholeBible,
        bool clearBible = false)
    {
        var name = $"Indian Revised Version ({language})";
        var holds = wholeBible ? "the whole Bible" : "the New Testament";
        return new TextDefinition(
            Slug: slug,
            Name: name,
            NameNative: null,
            Kind: TextKind.Translation,
            Language: code,
            Direction: TextDirection.LeftToRight,
            Versification: Versification.English,
            PublishedYear: issued,
            SourceUrl: $"https://git.door43.org/Door43-Catalog/{folder}/src/tag/{release}",
            RightsHolder: rightsHolder,
            Licence: "CC-BY-SA-4.0",
            LicenceUrl: "https://creativecommons.org/licenses/by-sa/4.0/",
            Redistribution: Redistribution.ShareAlike,
            TextualFamily: null)
        {
            Translators = $"{rightsHolder}, with the Door43 World Missions Community",
            Edition = $"Door43's release {release}, which holds {holds}",
            About =
                $"The Indian Revised Version in {language}: a revision of the {language} Bible made for Bridge "
                + "Connectivity Solutions' Indian Revised Version programme and released under an open licence. "
                + $"Door43's release holds {holds}, and its translators tied each word of the New Testament to the "
                + "word of unfoldingWord's Greek New Testament it renders in translationCore; those ties are loaded "
                + "as the links beside it.",
            RightsNote =
                "Under Creative Commons Attribution-ShareAlike 4.0, stated in the release's LICENSE.md and its manifest. "
                + (clearBible
                    ? "Clear Bible's metadata for the same text at the Digital Bible Library says CC BY 4.0, and the "
                      + "more restrictive of the two is the one taken. "
                    : string.Empty)
                + "ShareAlike binds an adaptation of this text, such as its searchable form, and not the texts it is "
                + "read beside. The text is served unchanged; the book introductions and section headings of the "
                + "release are its editors' and are not loaded. Original work available at https://door43.org/.",
            Citation =
                $"{name}, CC BY-SA 4.0, release {release} of git.door43.org/Door43-Catalog/{folder}. Original work "
                + "available at https://door43.org/.",
        };
    }
}
