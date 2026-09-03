namespace Essenthos.Core.Endpoints;

/// <summary>
/// The datasets that are not texts, and which row came from which.
///
/// A row carries its source as one prose string — *"Wikidata, query.wikidata.org, CC0"* — which is
/// the right thing to store and the wrong thing to show. A reader looking at a date needs to know
/// it was taken from somewhere, whose it is, and what the licence permits, and needs the name and
/// the licence to be things they can follow. So the fields are declared here once, every row that
/// carries a source also answers which of these it is, and the page renders a credit rather than
/// printing a sentence.
///
/// Declared rather than parsed. Pulling a licence back out of prose with a regex is how a page ends
/// up quietly asserting the wrong one, and the wrong one here is share-alike.
/// </summary>
public static class Datasets
{
    /// <param name="Prefix">
    /// What a row's source string starts with, where the dataset supplies rows. A dataset that
    /// supplies annotation rather than rows carries no prefix and is found by <paramref name="Lemmas"/>.
    /// </param>
    /// <param name="Links">
    /// Whether this dataset supplies word links, whose source strings carry <c>Prefix</c> too.
    ///
    /// Set where a dataset states word-to-word correspondences rather than contributing rows of its
    /// own. It is a separate flag rather than another prefix because the link table is millions of
    /// rows and only worth sweeping for the two or three datasets that speak there.
    /// </param>
    /// <param name="Lemmas">
    /// The text whose lemmas this dataset supplies, where it supplies lemmas rather than rows.
    ///
    /// GLAUx is the first source here that annotates a text instead of contributing rows of its own,
    /// and <c>word</c> carries no source column — a lemma is a fact about a word, not a record with
    /// a provenance. Naming the text is enough because no other text in the corpus takes its lemmas
    /// from anywhere but itself, and a share-alike licence that reached the reader through nothing
    /// at all would be worse than the counting being approximate.
    /// </param>
    /// <param name="Methods">
    /// Further prefixes this dataset claims, for rows whose source names a method rather than a
    /// speaker — <c>"the Strong numbers both editions carry, paired within each verse"</c>.
    ///
    /// Only this project's own reasoning is written that way, and on purpose: what a reader needs
    /// from an inferred link is how it was reached, and only then that it was reached here. So the
    /// strings stay as the rows already carry them and the declaration reaches for them, rather
    /// than the rows being rewritten to start with a name — rewriting one changes nothing already
    /// loaded, and a loaded corpus is the only place the undeclared report is read.
    ///
    /// Listed one by one rather than swept up by a catch-all, so a method nobody declared still
    /// shows as undeclared.
    /// </param>
    public sealed record Dataset(
        string Id,
        string Name,
        string Author,
        string Licence,
        string LicenceUrl,
        string Url,
        string Covers,
        string Prefix,
        string? Lemmas = null,
        bool Links = false,
        string[]? Methods = null)
    {
        /// <summary>Every source-string prefix this dataset claims, its own name first.</summary>
        public IEnumerable<string> Prefixes => Methods is null ? [Prefix] : [Prefix, .. Methods];
    }

    public static readonly Dataset[] All =
    [
        new("bibledata", "BibleData", "Brady Stephenson", "CC BY 4.0",
            "https://creativecommons.org/licenses/by/4.0/",
            "https://github.com/BradyStephenson/bible-data",
            "The people the text names, how they stand to one another, and a chronology of the Old "
            + "Testament in which every year is computed from a verse and shows its arithmetic. Its "
            + "places are marked in progress by its author and read that way here: 118 of them, "
            + "named but not placed, and referenced only through Genesis and Exodus.",
            "BibleData by"),

        new("theographic", "Theographic Bible Data", "Robert Rouse", "CC BY-SA 4.0",
            "https://creativecommons.org/licenses/by-sa/4.0/",
            "https://github.com/robertrouse/theographic-bible-metadata",
            "The New Testament chronology, which the other dataset does not have: its method stops "
            + "where the genealogies stop. Share-alike, unlike everything around it.",
            "Theographic"),

        new("wikidata", "Wikidata", "the Wikidata contributors", "CC0",
            "https://creativecommons.org/publicdomain/zero/1.0/",
            "https://query.wikidata.org",
            "World history on the same axis: battles, cities founded, dynasties, writing systems "
            + "and archaeological ages, so the text can be read against what else was happening.",
            "Wikidata"),

        // The corpus's single most load-bearing source, and the one that went longest unnamed: every
        // stated word-level correspondence the Old Testament has comes from it. PRB-0179.
        new("openhebrewbible", "Open Hebrew Bible Project", "Eliran Wong", "CC BY-NC 4.0",
            "https://creativecommons.org/licenses/by-nc/4.0/",
            "https://github.com/eliranwong/OpenHebrewBible",
            "Which King James word renders which Hebrew word, stated rather than computed, for the "
            + "whole Old Testament. It is the only word-level testimony this corpus holds for the "
            + "Hebrew, and therefore also the standard every inferred method is measured against.",
            "Open Hebrew Bible Project", Links: true),

        new("unfoldingword", "Ukrainian Bible Interlinear Ogienko", "unfoldingWord", "CC BY-SA 4.0",
            "https://creativecommons.org/licenses/by-sa/4.0/",
            "https://git.door43.org/uk_ts/uk_ubio",
            "Which Ukrainian word renders which Greek or Hebrew word, stated by people. Small "
            + "beside the Old Testament mapping, and the only stated word-level correspondence any "
            + "Slavic text in the corpus has — so it is what every model here is calibrated on.",
            "unfoldingWord", Links: true),

        // The second answer to a question the corpus already had an answer to, which is why it is
        // here at all: 98,989 of its records corroborate a link the Berean's own tables state, and
        // 8,345 disagree with one. Its repository carries no licence file — every statement of terms
        // is in the per-set TOML, which is the one closest to the bytes (RUL-0105).
        new("clearbible", "Clear Bible Alignments", "BiblioNexus", "CC BY 4.0",
            "https://creativecommons.org/licenses/by/4.0/",
            "https://github.com/Clear-Bible/Alignments",
            "Which English word of the Berean renders which Greek word, aligned by hand by a team "
            + "that did not consult the Berean's own translators. Where the two agree, a link carries "
            + "both their names; where they differ, the corpus holds both answers rather than "
            + "choosing. Their Russian set is in the same download and is deliberately not loaded.",
            "Clear Bible Alignments", Links: true),

        // The site says two things about itself. Its licensing page places the text in the public
        // domain and adds "Licensing is not required for any use"; the footer of every page on the
        // same site still reads "Copyright © 2021 Berean Standard Bible. All rights reserved." The
        // licensing page is the specific and deliberate statement and the footer is template
        // chrome, so the licensing page is believed — and both are recorded, beside the data.
        new("berean", "Berean Standard Bible", "Bible Hub / Berean Bible", "Public Domain",
            "https://berean.bible/licensing.htm",
            "https://berean.bible",
            "Which Berean word renders which Hebrew, Aramaic or Greek word, stated by the "
            + "translators themselves in the tables they publish beside the text. It is the second "
            + "stated English anchor the corpus has and the first that covers both testaments, so "
            + "for the New Testament it is the only word-level testimony there is.",
            "Berean Standard Bible translation tables", Links: true),

        // Public domain by the only statement attached to the bytes — the repository's README says
        // "License? Public Domain. Copy freely." and there is no LICENSE file and no licence on the
        // GitHub repository record. Robinson asks, without requiring it, that his name and the
        // title stay with the text; both are here. The re-wrappings disagree with the original and
        // are more restrictive, so they are not the ones believed. RUL-0105.
        new("byztxt", "Robinson's Textus Receptus", "Maurice A. Robinson", "Public Domain",
            "https://github.com/byztxt/greektext-textus-receptus#license",
            "https://github.com/byztxt/greektext-textus-receptus",
            "Which word of Stephanus 1550 is which word of Scrivener 1894, stated rather than "
            + "aligned. The composite is one token stream that offers a choice at the places the "
            + "two editions differ, so the file itself says which reading is whose — including the "
            + "places where one edition has a word and the other has none, which are the only "
            + "absences this corpus records rather than merely fails to fill.",
            "byztxt/greektext-textus-receptus", Links: true),

        // The one source in this list that states no licence at all: the module's own <rights>
        // element is present and empty, and the SourceForge project declares none either. What it
        // carries is old enough to be out of copyright on its own — see the LICENCE.md kept beside
        // the file — but that is our reading of the contents, not a grant by the packager, and the
        // difference is exactly what this field must not blur.
        new("zefaniakjv", "Zefania KJV+", "Theologische Initative Freiburg", "No licence stated",
            "https://sourceforge.net/projects/zefania-sharp/files/Bibles/ENG/King%20James/KJV%2B/",
            "https://sourceforge.net/projects/zefania-sharp/files/Bibles/ENG/King%20James/KJV%2B/",
            "The Strong number on each King James word, which is what every New Testament link "
            + "between the English and the Greek is matched on. The King James text itself is read "
            + "from elsewhere and only the tagging is taken from here — and the tagging is all this "
            + "supplies: which tagged word pairs with which Greek word is matched within the verse "
            + "by this project, at a confidence, and no part of that pairing is stated by anyone.",
            "Zefania KJV+", Links: true),

        new("glaux", "GLAUx", "Alek Keersmaekers and the GLAUx contributors", "CC BY-SA 3.0",
            "https://creativecommons.org/licenses/by-sa/3.0/",
            "https://github.com/alekkeersmaekers/glaux",
            "The dictionary form of every word of the Septuagint. Brenton's translation is public "
            + "domain and arrived with no annotation at all, so this is the one thing in the corpus "
            + "that makes the Greek Old Testament searchable by word rather than by spelling. Only "
            + "the lexical table is taken: GLAUx's own Greek text is not loaded, and its lemmas are "
            + "applied to the Brenton text already served.",
            "GLAUx", Lemmas: "lxx-brenton"),

        // What this project asserts itself, and it belongs in the list precisely because it is
        // ours: a claim of our own, printed beside the ones we merely carry. The links are nearly
        // all of it — correspondences nobody states, which read exactly like an undeclared third
        // party until they were claimed here. PRB-0180.
        new("essenthos", "Essenthos", "this project", "CC BY 4.0",
            "https://creativecommons.org/licenses/by/4.0/",
            "https://github.com/",
            "What this project works out for itself. Corrections and separations it makes to the "
            + "datasets it carries, each recorded on the row it changed; and the word "
            + "correspondences no source states — the two Greek editions joined on the Strong "
            + "numbers both of them tag, and the English function words the tagging skips, "
            + "recovered from the morphology the Greek states. Every one of them carries a "
            + "confidence, which is how it is told apart from testimony.",
            "Essenthos", Links: true, Methods:
            [
                "the Strong numbers both editions carry",
                "the words left over once the Strong numbers were paired",
                "the untagged English function words",
            ]),
    ];

    /// <summary>Which dataset a row's source string belongs to, or null if none claims it.</summary>
    public static string? Of(string? source) => Match(source)?.Id;

    public static Dataset? Match(string? source) =>
        source is null
            ? null
            : Array.Find(All, d => Claims(d, source));

    /// <summary>Whether a dataset's declaration reaches a row carrying this source string.</summary>
    public static bool Claims(Dataset dataset, string source) =>
        dataset.Prefixes.Any(prefix => source.StartsWith(prefix, StringComparison.Ordinal));

}
