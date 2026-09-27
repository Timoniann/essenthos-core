namespace Essenthos.Core.Corpus;

/// <summary>
/// The few identifiers and provenance strings that the loader writing a row and the surface reading
/// it have to agree on exactly.
///
/// A text's slug is how every other table addresses it, and a source string is what the dataset
/// declaration matches a row's credit against — by prefix, character for character. Declared in one
/// place because the failure when the two sides drift is silent: the rows are written, the
/// declaration finds none of them, and the page says a dataset contributed nothing.
///
/// This is the whole of what the corpus's vocabulary needs from the loaders. Everything else a
/// loader knows is the loader's own.
/// </summary>
internal static class Sources
{
    /// <summary>
    /// Brenton's Septuagint, which GLAUx's lexical table annotates. <c>GRCBRENT</c> is eBible's own
    /// identifier for this Greek text, and <c>BRENTON</c> alone would name his English translation,
    /// which e-Sword and theWord serve under it.
    /// </summary>
    public const string BrentonSeptuagintSlug = "GRCBRENT";

    /// <summary>Nestle 1904, whose words carry the Berean interlinear's English gloss.</summary>
    public const string NestleSlug = "NESTLE1904";

    /// <summary>Swete's Septuagint, Codex Vaticanus as it stands.</summary>
    public const string SweteSlug = "SWETE";

    /// <summary>The Old Greek of Susanna, Daniel and Bel, which Swete prints beside Theodotion's.</summary>
    public const string SweteOldGreekSlug = "SWETEOG";

    /// <summary>Ottley's Isaiah, Codex Alexandrinus as it stands.</summary>
    public const string OttleySlug = "OTTLEY";

    /// <summary>Codex Alexandrinus, both Testaments, as far as its own text exists typed.</summary>
    public const string AlexandrinusSlug = "ALEX";

    /// <summary>
    /// The Ethiopic Bible in Ge'ez, all eighty-one books. CrossWire's <c>Geez</c> is sixteen of them,
    /// HaCohen's Octateuch and Psalter, and is another text.
    /// </summary>
    public const string GeezSlug = "GEEZ81";

    /// <summary>The Robinson-Pierpont Byzantine Textform, 2018, as its own repository spells it.</summary>
    public const string ByzantineSlug = "RP2018";

    /// <summary>Scrivener's Textus Receptus of 1894, as Bible Gateway spells it.</summary>
    public const string ScrivenerSlug = "TR1894";

    /// <summary>Stephanus's Textus Receptus of 1550, as Bible Gateway spells it.</summary>
    public const string StephanusSlug = "TR1550";

    /// <summary>Tischendorf's eighth edition, as CrossWire, STEP and bolls.life spell it.</summary>
    public const string TischendorfSlug = "TISCH";

    /// <summary>Westcott and Hort's 1881 text, by the siglum an apparatus writes for it.</summary>
    public const string WestcottHortSlug = "WH1881";

    /// <summary>The Kulish Bible, the first complete Ukrainian one.</summary>
    public const string KulishSlug = "UKR1871";

    /// <summary>
    /// What every link drawn from the Synodal's Strong numbering begins with. It names whose
    /// numbering it is and where the copy came from, because the copy carries no credit of its own.
    /// </summary>
    public const string SynodalStrongCredit =
        "the Bob Jones University Strong numbering of the Synodal (1996), read from swmail/RST";

    /// <summary>
    /// What every link drawn from the Chinese Union Version's Strong numbers begins with. It names
    /// whose numbers they are and the copy they were read from.
    /// </summary>
    public const string UnionStrongCredit =
        "the Faith Hope Love foundation's Strong numbering of the Chinese Union Version, read from CrossWire's ChiUn and ChiUns";

    /// <summary>
    /// How a translated lexicon row begins. The English of the lexicon is public domain and the
    /// rendering into a reader's language is this project's, so the row names the model, the prompt
    /// version and the day, and the English stays beside it.
    /// </summary>
    public const string StrongTranslationPrefix = "a translation of the Strong lexicon by";

    /// <summary>
    /// How a descriptor read off the text begins. The row names the model and the day it was asked,
    /// so the pass is identifiable and removable.
    /// </summary>
    public const string DescriptorReadingPrefix = "read from Scripture by";

    /// <summary>
    /// How a model's reading of a verse begins, for the names no number settles. The row goes on to
    /// name the model, the prompt version and the date of the run.
    /// </summary>
    public const string VerseReadingPrefix = "a reading of the verse by";

    /// <summary>
    /// What every gloss taken from STEPBible's brief Greek lexicon carries, and what its dataset
    /// declaration claims them by: whose lexicon, which commit of it, and on what terms.
    /// </summary>
    public const string BriefGreekLexicon =
        BriefGreekLexiconPrefix + ", the Translators Brief lexicon of Extended Strongs for Greek, by Tyndale House, "
        + "Cambridge, CC BY 4.0, read from STEPBible/STEPBible-Data at ae39711";

    public const string BriefGreekLexiconPrefix = "STEPBible TBESG";

    /// <summary>
    /// What every row of the Ge'ez lexicon carries: whose lexicon, whose digitisation, which commit
    /// of it, and on what terms.
    /// </summary>
    public const string DillmannLexicon =
        DillmannLexiconPrefix + " (August Dillmann, Leipzig 1865), digitised by Beta maṣāḥǝft and the TraCES "
        + "project, Hiob-Ludolf-Zentrum für Äthiopistik, Universität Hamburg, CC BY-NC-SA 4.0, read from "
        + "BetaMasaheft/DillmannData at 44f2da8";

    public const string DillmannLexiconPrefix = "Lexicon Linguae Aethiopicae";

    /// <summary>The King James, the Russian Synodal and the Ohienko Ukrainian, as bible4u spells them.</summary>
    public const string KingJamesSlug = "KJV";

    public const string SynodalSlug = "RUSV";

    public const string OhienkoSlug = "UBIO";

    /// <summary>
    /// The commandments, and what their dataset declaration claims them by: whose count, whose
    /// English, and where both were read.
    /// </summary>
    public const string Maimonides =
        MaimonidesPrefix + ", in Moses Hyamson's English of the Mishneh Torah's list (1937-1949), read from Sefaria";

    public const string MaimonidesPrefix = "Maimonides' Sefer HaMitzvot";

    /// <summary>The topics, and what their dataset declaration claims them by.</summary>
    public const string Naves =
        NavesPrefix + ", as Brady Stephenson transcribed it in BibleData, CC BY 4.0";

    public const string NavesPrefix = "Nave's Topical Bible (1896)";
}