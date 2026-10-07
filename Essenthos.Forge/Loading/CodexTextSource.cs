using Essenthos.Core.Alexandrinus;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;

namespace Essenthos.Core.Loading;

/// <param name="Folder">The folder under Resources the transcription and its check are kept in.</param>
internal sealed record CodexText(TextDefinition Definition, string Folder, NtvmrManuscript Manuscript)
{
    public string Slug => Definition.Slug;

    /// <summary>The manuscript's New Testament in the order the codex has its books.</summary>
    public TextSource Read(string resources)
    {
        var transcription = Path.Combine(resources, Folder, Manuscript.File);
        if (!File.Exists(transcription))
        {
            throw new InvalidOperationException(
                $"{Slug} is missing {transcription}. Run scripts/fetch-alexandrinus.ps1, which fetches INTF's "
                + "transcription of each of the three codices with CNTR's beside it.");
        }

        return new TextSource(Definition, NtvmrTranscription.Books(transcription, 1, Manuscript));
    }

    /// <summary>
    /// The text it is joined to verse by verse before any of its words are aligned: Nestle 1904, in
    /// the chapters where the two print the same verses.
    /// </summary>
    public DeclaredVersePair VersePair =>
        new(Slug, Sources.NestleSlug, new HashSet<int>()) { AgreeingChaptersOnly = true };
}

/// <summary>
/// The two fourth-century codices of the Greek Bible, as far as their New Testaments go: the
/// manuscripts themselves, word by word as INTF transcribed them for the Virtual Manuscript Room,
/// under Creative Commons Attribution 4.0. Their Old Testaments have no typed transcription anybody
/// publishes on terms this corpus can take; Vaticanus's is what Swete prints as <c>SWETE</c>.
///
/// <para>
/// Neither identifier is anybody else's: no software that serves these manuscripts publishes one,
/// and <c>01</c> and <c>03</c> would name only their New Testaments.
/// </para>
/// </summary>
internal static class CodexTextSource
{
    private const string NtvmrAuthor = "Institut für Neutestamentliche Textforschung, Münster";

    private const string Licence = "CC-BY-4.0";

    private const string LicenceUrl = "https://creativecommons.org/licenses/by/4.0/";

    private const string Relabelled =
        "Modified: the transcription labels some verses in a form other than its own, and Essenthos "
        + "places them by that form, checked verse for verse against CNTR's independent transcription of "
        + "the manuscript; the first hand is the text and each correction a note on its verse.";

    public static readonly CodexText Sinaiticus = new(
        new TextDefinition(
            Slug: Sources.SinaiticusSlug,
            Name: "Codex Sinaiticus",
            NameNative: "ΚΩΔΙΞ ΣΙΝΑΪΤΙΚΟΣ",
            Kind: TextKind.ManuscriptTradition,
            Language: "grc",
            Direction: TextDirection.LeftToRight,
            Versification: Versification.Septuagint,
            PublishedYear: null,
            SourceUrl: NtvmrTranscription.Sinaiticus.Url,
            RightsHolder: "The Institut für Neutestamentliche Textforschung over the transcription. Nobody holds "
                          + "the manuscript's text itself.",
            Licence: Licence,
            LicenceUrl: LicenceUrl,
            Redistribution: Redistribution.PermittedWithAttribution,
            TextualFamily: "Alexandrian")
        {
            Editors = NtvmrAuthor,
            Edition = "London, British Library, Add. MS 43725 (the New Testament), copied in the fourth century",
            About = "The oldest complete New Testament, copied in the fourth century and found by Constantin "
                    + "von Tischendorf at Saint Catherine's Monastery on Sinai in 1844 and 1859; its New "
                    + "Testament is now in the British Library. Here it is the manuscript itself, word by word "
                    + "as the scribe wrote it — unaccented, with the sacred names contracted — from the "
                    + "transcription the Institut für Neutestamentliche Textforschung made for its editions. "
                    + "The codex is heavily corrected, by several hands over several centuries: the first hand "
                    + "is the text, and each correction is a note naming its corrector. All twenty-seven books "
                    + "are here, Paul's letters with Hebrews before the Pastorals and Acts after them. Verses "
                    + "the first hand did not write are absent — among them Mark 16:9–20 and John 7:53–8:11 — and "
                    + "so are nine the first hand passed over and the first corrector supplied, Matthew 12:47, "
                    + "Luke 17:35 and John 21:25 among them. The Letter of Barnabas and the Shepherd of Hermas, "
                    + "which follow Revelation in "
                    + "the codex, and its Old Testament are not here.",
            RightsNote = "The manuscript is out of copyright. What carries a licence is the transcription, by "
                         + "INTF, under Creative Commons Attribution 4.0, which asks for credit and nothing else. "
                         + "The Codex Sinaiticus Project publishes its own transcription under a NonCommercial "
                         + "ShareAlike licence, and it is not used. " + Relabelled,
            Citation = "Codex Sinaiticus (GA 01), New Testament transcription by the Institut für "
                       + "Neutestamentliche Textforschung, New Testament Virtual Manuscript Room, CC BY 4.0.",
            PartSources =
            [
                new TextPartSource(
                    Name: "Codex Sinaiticus (GA 01), transcription for the New Testament Virtual Manuscript Room",
                    Author: NtvmrAuthor,
                    Licence: Licence,
                    LicenceUrl: LicenceUrl,
                    Url: NtvmrTranscription.Sinaiticus.Url,
                    Covers: "the New Testament"),
            ],
        },
        "Sinaiticus",
        NtvmrTranscription.Sinaiticus);

    public static readonly CodexText Vaticanus = new(
        new TextDefinition(
            Slug: Sources.VaticanusSlug,
            Name: "Codex Vaticanus",
            NameNative: "ΚΩΔΙΞ ΒΑΤΙΚΑΝΟΣ",
            Kind: TextKind.ManuscriptTradition,
            Language: "grc",
            Direction: TextDirection.LeftToRight,
            Versification: Versification.Septuagint,
            PublishedYear: null,
            SourceUrl: NtvmrTranscription.Vaticanus.Url,
            RightsHolder: "The Institut für Neutestamentliche Textforschung over the transcription. Nobody holds "
                          + "the manuscript's text itself.",
            Licence: Licence,
            LicenceUrl: LicenceUrl,
            Redistribution: Redistribution.PermittedWithAttribution,
            TextualFamily: "Alexandrian")
        {
            Editors = NtvmrAuthor,
            Edition = "Vatican City, Biblioteca Apostolica Vaticana, Vat. gr. 1209, pages 1235–1518, copied in "
                      + "the fourth century",
            About = "The codex Westcott and Hort trusted above every other, copied in the fourth century and kept "
                    + "in the Vatican Library since at least 1475. Here is its New Testament as the manuscript "
                    + "has it, word by word as the scribe wrote it — unaccented, with the sacred names "
                    + "contracted — from the transcription the Institut für Neutestamentliche Textforschung made "
                    + "for its editions: the first hand is the text, and each correction is a note naming its "
                    + "corrector. The codex breaks off at Hebrews 9:14, where its last surviving leaf ends: the "
                    + "rest of Hebrews is lost with it, and so is whatever followed, so the letters to Timothy, "
                    + "Titus and Philemon and Revelation are not here — the Revelation bound into the codex "
                    + "today was added in the fifteenth century and is another manuscript. The Catholic "
                    + "letters follow Acts. Verses the first hand did not write are absent, among them "
                    + "Mark 16:9–20, Luke 22:43–44, John 7:53–8:11 and 1 Peter 5:3. Its Old Testament is Swete's "
                    + "text of the Septuagint.",
            RightsNote = "The manuscript is out of copyright. What carries a licence is the transcription, by "
                         + "INTF, under Creative Commons Attribution 4.0, which asks for credit and nothing else. "
                         + Relabelled,
            Citation = "Codex Vaticanus (GA 03), New Testament transcription by the Institut für "
                       + "Neutestamentliche Textforschung, New Testament Virtual Manuscript Room, CC BY 4.0.",
            PartSources =
            [
                new TextPartSource(
                    Name: "Codex Vaticanus (GA 03), transcription for the New Testament Virtual Manuscript Room",
                    Author: NtvmrAuthor,
                    Licence: Licence,
                    LicenceUrl: LicenceUrl,
                    Url: NtvmrTranscription.Vaticanus.Url,
                    Covers: "the New Testament"),
            ],
        },
        "Vaticanus",
        NtvmrTranscription.Vaticanus);

    public static IReadOnlyList<CodexText> All => [Sinaiticus, Vaticanus];

    public static IReadOnlyList<TextDefinition> Definitions => [.. All.Select(codex => codex.Definition)];

    public static IReadOnlyList<DeclaredVersePair> VersePairs => [.. All.Select(codex => codex.VersePair)];
}
