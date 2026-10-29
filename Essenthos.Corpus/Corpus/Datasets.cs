namespace Essenthos.Core.Corpus;

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
    /// <summary>
    /// This project's own id in the list below. What the corpus concluded for itself is told apart
    /// from what it merely carries in more than one place — the credit on a row, and which of two
    /// witnesses survives when they agree — and both ask the same question of the same string.
    /// </summary>
    public const string Own = "essenthos";

    /// <param name="Prefix">
    /// What a row's source string starts with, where the dataset supplies rows. A dataset that
    /// supplies annotation rather than rows carries no prefix and is found by <paramref name="Lemmas"/>.
    /// </param>
    /// <param name="Lexicon">
    /// Whether this dataset is the Strong lexicon, counted by its entries.
    ///
    /// A third shape, because the lexicon belongs to no text and contributes neither rows carrying a
    /// source string nor links — 14,298 entries the whole corpus resolves its numbers through, and
    /// the one dataset the undeclared report could not see.
    /// </param>
    /// <param name="Glossary">
    /// Whether this dataset is a lexicon of short glosses, counted by the entries it glosses. Its
    /// rows belong to no text and to no word, so like <paramref name="Lexicon"/> nothing else would
    /// count it, and a dataset nothing counts drops off the sources page.
    /// </param>
    /// <param name="Links">
    /// Whether this dataset supplies word links, whose source strings carry <c>Prefix</c> too.
    ///
    /// Set where a dataset states word-to-word correspondences rather than contributing rows of its
    /// own. It is a separate flag rather than another prefix because the link table is millions of
    /// rows and only worth sweeping for the two or three datasets that speak there.
    /// </param>
    /// <param name="WordGlosses">
    /// The text whose words this dataset glosses, where it supplies the gloss on a word rather than
    /// rows of its own. The same shape as <paramref name="Lemmas"/> and for the same reason: a
    /// gloss is a fact about a word and <c>word</c> carries no source column, so naming the text is
    /// what makes the credit countable. Unlike <paramref name="Glossary"/>, which is a lexicon
    /// keyed to a lemma, this sits on the word of one edition.
    /// </param>
    /// <param name="Parsings">
    /// Whether this dataset supplies a second analysis of words already in the corpus, counted by
    /// its <c>word_parsing</c> rows, whose source strings carry <c>Prefix</c> too. A parsing is
    /// neither an entity nor a link, so without this a dataset contributing only parsings would
    /// count nothing and fall off the page.
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
    /// <param name="Citation">
    /// The form the author asks to be cited in, verbatim, where the source publishes one.
    ///
    /// **It is what a contested licence leaves standing.** BibleData ships four licence statements
    /// and three different answers — its LICENSE file and README say Attribution 4.0, its citation
    /// file said NonCommercial-ShareAlike until the author corrected it, and the copy on Kaggle
    /// still says NonCommercial-ShareAlike 3.0 IGO beside byte-identical data. Whichever governs,
    /// every one of them requires attribution, and every one of them is satisfied by citing the
    /// work as its author asked. So where the terms are argued the citation is the part that is
    /// not.
    ///
    /// It also pins a version, which a licence never does. A DOI resolves to one deposit; a
    /// repository URL resolves to whatever is on the branch this week, and this dataset's terms
    /// changed on that branch between two of our own loads. Naming the deposit says which bytes
    /// were used, which is the question a reader who disagrees with a row actually has.
    ///
    /// Null where the source publishes no citation form. That is silence, not an assertion that
    /// none is owed — the author, the licence and the URL are recorded either way, and this is the
    /// extra thing an author asked for on top of them.
    /// </param>
    /// <param name="Obliges">
    /// What this source's terms require of anything the corpus publishes from it, in a sentence.
    ///
    /// A licence name says what may be taken; it does not say what is owed back, and the two are
    /// not the same reading. **CC BY-SA 4.0** on an alignment means the corpus's own form of those
    /// links has to be offered under CC BY-SA 4.0 in turn, and unfoldingWord adds that a derivative
    /// work must not carry their trademark. **CC BY-NC 4.0** means the rows it stands behind cannot
    /// be published commercially. None of that is inferable from four letters and a URL, and an
    /// obligation nobody wrote down is one nobody meets.
    ///
    /// Null where the terms ask for nothing beyond the attribution every source here gets anyway —
    /// which is most of them, and is why this being set is worth noticing.
    /// </param>
    /// <param name="Contains">
    /// Further works bound into the same file, each with its own author and its own terms.
    ///
    /// One field is not enough for a source that is two works. <c>StrongHebrew.xml</c> declares
    /// three in its own header — Strong's Hebrew and Greek dictionaries, both public domain, and
    /// the Theological Wordbook of the Old Testament, under a 1980 Moody copyright — and the file
    /// says which is which: <c>workPrefix</c> assigns the entry to Strong and the gloss to TWOT.
    /// Flattening that to one author and one licence would put the wrong name on 6,070 rows and
    /// the wrong terms on all of them.
    /// </param>
    /// <param name="LinkClaims">
    /// Whether this dataset is counted by the claims it makes on links somebody else drew: a second
    /// answer beside the link's own, which neither makes a link nor carries one's source string.
    /// </param>
    /// <param name="Frame">
    /// Whether this dataset is the verse numbering every text is placed by, counted by the texts it places.
    /// </param>
    /// <param name="Changes">
    /// What this project changed in the dataset, one sentence each, where its terms ask for the changes
    /// to be noted where those who use the result can read them.
    /// </param>
    /// <param name="OwnWork">
    /// Whether this is this project's own work, published under the licence the owner sets for all of
    /// it; <c>Licence</c> is what stands until he has set one.
    /// </param>
    /// <param name="Unlisted">
    /// Declared so that the rows it stands behind are accounted for, and named to no reader: not on the
    /// sources page, not as the credit on a link. The owner ruled so on 2026-10-29 for the numberings
    /// and taggings used until something else replaces them.
    /// </param>
    public sealed record Dataset(
        string Id,
        string Name,
        string Author,
        string? Licence,
        string? LicenceUrl,
        string Url,
        string Covers,
        string Prefix,
        string? Lemmas = null,
        string? WordGlosses = null,
        bool Links = false,
        bool Lexicon = false,
        bool Glossary = false,
        bool Parsings = false,
        string[]? Methods = null,
        Work[]? Contains = null,
        string? Citation = null,
        string? Obliges = null,
        string? HolderUrl = null,
        bool LinkClaims = false,
        bool Frame = false,
        string[]? Changes = null,
        bool OwnWork = false,
        bool Unlisted = false)
    {
        /// <summary>Every source-string prefix this dataset claims, its own name first.</summary>
        public IEnumerable<string> Prefixes => Methods is null ? [Prefix] : [Prefix, .. Methods];

        /// <summary>What its licence lets a reader do, as <see cref="ReuseTerms"/> words it.</summary>
        public string Terms => ReuseTerms.Of(Licence);
    }

    /// <param name="Covers">Which part of the dataset is this work's, so the credit lands on the right rows.</param>
    public sealed record Work(
        string Name,
        string Author,
        string Licence,
        string LicenceUrl,
        string Covers);

    /// <summary>The BibleData commit the files on disk were taken from (Resources/BibleData2026/LICENCE.md).</summary>
    private const string BibleDataCommit =
        "https://github.com/BradyStephenson/bible-data/tree/8799b409c82a5d4acebba3be5107d6eff7c85d78";

    /// <summary>How the BibleData release asks to be cited, for everything it carries.</summary>
    private const string BibleDataCitation =
        "Stephenson, B. (2026). BibleData: Structured Datasets from the Holy Bible "
        + "(1.0) [Data set]. Zenodo. https://doi.org/10.5281/zenodo.19539956";

    /// <summary>What STEP Bible asks to be credited by: its name, linked to its own site.</summary>
    private const string StepBible = "https://www.stepbible.org/";

    /// <summary>The terms of the RLT module, as its configuration states them (rlt.conf).</summary>
    private const string RevisedLiteralTerms =
        "CrossWire distributes the module under \"GPL\", naming no version, and Michael W. Jones, Sr. "
        + "grants \"a general public license to use this text for any purpose\".";

    public static readonly Dataset[] All =
    [
        new("bibledata", "BibleData", "Brady Stephenson", "CC BY 4.0",
            "https://creativecommons.org/licenses/by/4.0/",
            BibleDataCommit,
            "The people the text names, and a chronology of the Old Testament in which every year "
            + "is computed from a verse and shows its arithmetic. Its "
            + "places are marked in progress by its author and read that way here: 118 of them, "
            + "named but not placed, and referenced only through Genesis and Exodus.",
            "BibleData by", Citation: BibleDataCitation),

        // The place layer, which the dataset above marks in progress: 118 places referenced only
        // through Genesis and Exodus against 1,342 across 61 books. Attribution 4.0 is why this and
        // not Theographic, whose 7,310 place references are share-alike. The places are taken, and
        // one point per place where the gazetteer credits that point to anyone but OpenStreetMap;
        // the geometry, and every point it credits to OpenStreetMap, are ODbL and are not.
        new("openbible", "OpenBible.info Bible Geocoding", "Stephen Smith", "CC BY 4.0",
            "https://creativecommons.org/licenses/by/4.0/",
            "https://github.com/openbibleinfo/Bible-Geocoding-Data",
            "Every identifiable place the Bible names and the verses that name it, drawn from over "
            + "seventy modern commentaries, dictionaries and atlases with the confidence of each "
            + "identification recorded. It is what the place layer reaches past Exodus on, and where "
            + "it names a place the other dataset already had, the reference joins that same entry "
            + "rather than making a second one. It also says where each place is, and the map of "
            + "places is drawn from its most confident identification of each.",
            "OpenBible.info"),

        // Its own entry rather than a line under BibleData, because the words are Ussher's and the
        // dataset above only carries them. The rows it supplies are the only ones in the corpus
        // that date anything the gospels narrate.
        new("ussher", "Ussher's Annals of the World",
            "James Ussher, 1650, in Edmund Pierce's English of 1658, transcribed by Brady Stephenson",
            "CC BY 4.0",
            "https://creativecommons.org/licenses/by/4.0/",
            BibleDataCommit,
            "The New Testament chronology, and the only one here: the computed reckoning is "
            + "arithmetic over the genealogies and stops at Artaxerxes, where Ussher goes on by "
            + "reading the consular lists and Josephus beside Scripture. One seventeenth-century "
            + "reckoning, drawn as his and never as the axis. Ussher's own text is out of copyright "
            + "by age; the Attribution licence covers the transcription into numbered paragraphs, "
            + "which is Stephenson's work.",
            "Ussher's Annals of the World", Citation: BibleDataCitation),

        // The count is the Sefer HaMitzvot's, read in its Hebrew; the English is Hyamson's
        // translation of the Mishneh Torah's own list of the same commandments, since Sefaria holds
        // no Hyamson Sefer HaMitzvot. Sefaria marks both versions Public Domain. Which of Hyamson's
        // numbers answers to which of the count's, and where a verse had to move to the English
        // numbering, is this project's and is said on the row. Resources/Maimonides/LICENCE.md has
        // the reading.
        new("maimonides", "Maimonides' count of the commandments",
            "Moses Maimonides: the count from the Hebrew of his Sefer HaMitzvot (Warsaw, 1883), the "
            + "titles from Moses Hyamson's English of the list that opens his Mishneh Torah (New York, "
            + "1937–1949), both read from Sefaria",
            "Public Domain",
            "https://en.wikipedia.org/wiki/Public_domain",
            "https://www.sefaria.org/Mishneh_Torah%2C_Positive_Mitzvot",
            "The 613 commandments of the Torah, 248 positive and 365 negative, numbered as the Sefer "
            + "HaMitzvot counts them, each with the verses it rests on. The titles are Hyamson's "
            + "English of the list that opens the Mishneh Torah, shortened and not reworded. Where a "
            + "verse he prints is in the Hebrew numbering it is moved to the English one, and five "
            + "references are this project's, each saying why.",
            Sources.MaimonidesPrefix),

        // On trial: shown beside the chapter until the owner decides whether it stays. Nave's own
        // text is out of copyright; the transcription is Stephenson's, under the licence of the
        // BibleData release it comes in.
        new("naves", "Nave's Topical Bible",
            "Orville J. Nave, 1896, transcribed by Brady Stephenson",
            "CC BY 4.0",
            "https://creativecommons.org/licenses/by/4.0/",
            BibleDataCommit,
            "Some five thousand subjects and the verses Nave filed under each, which the reader "
            + "shows beside a chapter as the topics its verses are filed under. Nave's index of 1896 "
            + "is out of copyright; the Attribution licence covers the transcription, which is "
            + "Stephenson's work.",
            Sources.NavesPrefix, Citation: BibleDataCitation),

        // Off in the reader until the reader turns it on, and then the set it opens on. The page
        // offering the file says CC BY and so does the file's own first line; neither names a
        // version, and every version asks only for this credit.
        new("openbible-cross-references", "OpenBible.info Cross References", "Stephen Smith",
            "CC BY",
            "https://creativecommons.org/licenses/by/4.0/",
            "https://www.openbible.info/labs/cross-references/",
            "Some 345,000 pairs of verses a reader might turn to from one another, drawn mostly from "
            + "the Treasury of Scripture Knowledge and ranked by the votes of the site's readers, "
            + "which the reader shows beside a verse, the most voted first.",
            CrossReferenceSets.OpenBibleSource),

        // Public domain by age, and the module says so in its own configuration.
        new("tsk", "The Treasury of Scripture Knowledge",
            "Canne, Browne, Blayney, Scott and others, about 1880, from CrossWire's SWORD module",
            "Public Domain",
            "https://en.wikipedia.org/wiki/Public_domain",
            "https://www.crosswire.org/sword/modules/ModInfo.jsp?modName=TSK",
            "The classic set of cross references: every verse's references filed under the word of "
            + "the verse they bear on, in the order the Treasury prints them.",
            CrossReferenceSets.TreasurySource),

        new("wikidata", "Wikidata", "the Wikidata contributors", "CC0",
            "https://creativecommons.org/publicdomain/zero/1.0/",
            "https://query.wikidata.org",
            "World history on the same axis: battles, cities founded, dynasties, writing systems "
            + "and archaeological ages, so the text can be read against what else was happening. "
            + "It also says which Wikipedia article each person, place and thing is, so a page can "
            + "send the reader on to it in their own language.",
            "Wikidata", HolderUrl: "https://www.wikidata.org/"),

        // Public domain, and attributed on every band anyway: the period's own authority is named
        // where its dates are shown, and this is the credit for having gathered them.
        new("periodo", "PeriodO", "Adam Rabinowitz, Ryan Shaw and the PeriodO contributors", "CC0",
            "https://creativecommons.org/publicdomain/zero/1.0/",
            "https://perio.do",
            "The periods of the lands around the Bible — the Levant, Egypt, Mesopotamia, Anatolia, "
            + "Persia, the Aegean and Rome — each as a published work dates it, drawn behind the "
            + "timeline's events, with every authority's dates kept where they disagree.",
            "PeriodO"),

        // The corpus's single most load-bearing source, and the one that went longest unnamed: every
        // stated word-level correspondence the Old Testament has comes from it.
        new("openhebrewbible", "Open Hebrew Bible Project", "Eliran Wong", "CC BY-NC 4.0",
            "https://creativecommons.org/licenses/by-nc/4.0/",
            "https://github.com/eliranwong/OpenHebrewBible",
            "Which King James word renders which Hebrew word, stated rather than computed, for the "
            + "whole Old Testament. It is the only word-level testimony this corpus holds for the "
            + "Hebrew, and therefore also the standard every inferred method is measured against.",
            "Open Hebrew Bible Project", Links: true,
            Obliges: "NonCommercial: the links this source states, and anything published from "
                + "them, may not be used commercially."),

        // Share-alike reaches back out of the database onto whatever the corpus publishes from these
        // links. The release's manifest names uk_gt as its creator and publisher, with the Door43
        // World Missions Community contributing; its LICENSE.md is the plain CC BY-SA 4.0 summary.
        new("unfoldingword", "Ukrainian Bible Interlinear Ogienko",
            "uk_gt, the Ukrainian team on Door43, with the Door43 World Missions Community", "CC BY-SA 4.0",
            "https://creativecommons.org/licenses/by-sa/4.0/",
            "https://git.door43.org/uk_ts/uk_ubio",
            "Which Ukrainian word renders which Greek or Hebrew word, stated by people. Small "
            + "beside the Old Testament mapping, and for a long time the only stated word-level "
            + "correspondence any Slavic text had — so it is what every model here is calibrated on.",
            "unfoldingWord's Ukrainian", Links: true,
            Obliges: "ShareAlike: re-serialising these alignments is a derivative work, so the "
                + "corpus's own form of these links is offered under CC BY-SA 4.0 in turn."),

        // The same publisher, the same format and the same terms as the entry above, and a different
        // work: an English translation aligned by unfoldingWord's own team rather than a Ukrainian one
        // aligned by volunteers, so a reader asking whose a link is gets the right name.
        new("unfoldingword-ult", "unfoldingWord Literal Text alignment", "unfoldingWord", "CC BY-SA 4.0",
            "https://creativecommons.org/licenses/by-sa/4.0/",
            "https://git.door43.org/unfoldingWord/en_ult",
            "Which English word of the unfoldingWord Literal Text renders which Hebrew or Greek word, stated "
            + "by the translators who made it, in 56 books. unfoldingWord aligned it to its own Hebrew Bible "
            + "and Greek New Testament, not to BHSA and Nestle 1904, so a tie is carried over only where the "
            + "same word stands in the same verse of ours and cannot be mistaken for another.",
            "unfoldingWord Literal Text", Links: true,
            Obliges: "ShareAlike: re-serialising these alignments is a derivative work, so the corpus's own "
                + "form of these links is offered under CC BY-SA 4.0 in turn, with the credit \"The original "
                + "work by unfoldingWord is available from unfoldingword.org/ult\"."),

        // The same format and the same ecosystem as the entry above, and deliberately not folded
        // into it: the three Synodal books are dedicated to the public domain by whoever aligned
        // them, while the Ukrainian is share-alike and carries a trademark condition. One entry
        // would put the wrong terms on one of them. The identical alignment is also published in
        // the ru_rsb aggregate under CC BY-SA 4.0; the per-book repositories are what is taken and
        // what is quoted in Resources/Door43/LICENCE.md.
        new("door43-rsb", "Russian Synodal word alignment", "the Door43 World Missions Community",
            "CC0 1.0",
            "https://creativecommons.org/publicdomain/zero/1.0/",
            "https://git.door43.org/Anna/ru_rsb_tit_book",
            "Which Russian word renders which Greek word, stated by people, for Titus, Philemon and "
            + "2 John — which is the whole of what anyone has aligned of the Synodal. Three books of "
            + "sixty-six is not coverage; it is the only thing in the corpus that can say whether a "
            + "Russian link a model proposed is right.",
            "Door43 Russian Synodal alignment", Links: true),

        // The Van Dyck's ties to the Hebrew and Greek as BSOJ made them, beside Clear Bible's: a second
        // statement about the same Arabic words by a different team. The release's LICENSE.md is the
        // unfoldingWord Literal Text's, copied with the template; its manifest says CC BY-SA 4.0, and the
        // two agree on the terms.
        new("door43-avd", "Van Dyck word alignment", "BSOJ, on Door43", "CC BY-SA 4.0",
            "https://creativecommons.org/licenses/by-sa/4.0/",
            "https://git.door43.org/BSOJ/ar_avd",
            "Which Arabic word of the Van Dyck renders which Hebrew or Greek word, stated by the people "
            + "who tied them in translationCore, over the whole Bible. Where Clear Bible's team says the "
            + "same, a link carries both their names.",
            "BSOJ's alignment of the Van Dyck", Links: true,
            Obliges: "ShareAlike: re-serialising these alignments is a derivative work, so the corpus's "
                + "own form of these links is offered under CC BY-SA 4.0 in turn, with the credit \"The "
                + "original work by unfoldingWord is available from unfoldingword.org/ult\" that the "
                + "release's licence asks for, and without the unfoldingWord® trademark."),

        new("unfoldingword-ust", "unfoldingWord Simplified Text alignment", "unfoldingWord", "CC BY-SA 4.0",
            "https://creativecommons.org/licenses/by-sa/4.0/",
            "https://git.door43.org/unfoldingWord/en_ust",
            "Which English word of the unfoldingWord Simplified Text renders which Hebrew or Greek word, stated "
            + "by the translators who made it, in 59 books.",
            "unfoldingWord Simplified Text alignment", Links: true,
            Obliges: "ShareAlike: re-serialising these alignments is a derivative work, so the corpus's own "
                + "form of these links is offered under CC BY-SA 4.0 in turn, with the credit \"The original "
                + "work by unfoldingWord is available from unfoldingword.org/ust\"."),

        new("door43-vi", "Vietnamese Literal Text alignment", "Far East Broadcasting Company, on Door43",
            "CC BY-SA 4.0",
            "https://creativecommons.org/licenses/by-sa/4.0/",
            "https://git.door43.org/vi_gl/vi_glt",
            "Which Vietnamese word of the Literal Text renders which Hebrew or Greek word, stated by its "
            + "translators, for the New Testament and Ruth.",
            "Door43 alignment of the Vietnamese Literal Text", Links: true,
            Obliges: "ShareAlike: re-serialising these alignments is a derivative work, so the corpus's "
                + "own form of these links is offered under CC BY-SA 4.0 in turn."),

        new("door43-lsg", "Louis Segond word alignment", "the French gateway-language team on Door43",
            "CC BY-SA 4.0",
            "https://creativecommons.org/licenses/by-sa/4.0/",
            "https://git.door43.org/fr_gl/fr-textTranslation-FR_LSG",
            "Which French word of the Segond's New Testament renders which Greek word, stated by the people who "
            + "tied them, beside Clear Bible's alignment of the same text.",
            "Door43 alignment of the Louis Segond", Links: true,
            Obliges: "ShareAlike: re-serialising these alignments is a derivative work, so the corpus's "
                + "own form of these links is offered under CC BY-SA 4.0 in turn."),

        // One entry for the Indian Revised Versions because they are one programme: the same
        // publisher, the same tool, the same terms, released together in the Door43 catalogue.
        new("door43-irv", "Indian Revised Version word alignment", "Bridge Connectivity Solutions, on Door43",
            "CC BY-SA 4.0",
            "https://creativecommons.org/licenses/by-sa/4.0/",
            "https://git.door43.org/Door43-Catalog",
            "Which word of the Indian Revised Version renders which Hebrew or Greek word, stated by its "
            + "translators in translationCore: the whole Hindi Bible, and the New Testament in Assamese, "
            + "Bengali, Gujarati, Kannada, Malayalam, Marathi, Punjabi, Tamil, Telugu and Urdu.",
            "Door43 alignment of the Indian Revised Version", Links: true,
            Obliges: "ShareAlike: re-serialising these alignments is a derivative work, so the corpus's "
                + "own form of these links is offered under CC BY-SA 4.0 in turn. Original work available "
                + "at https://door43.org/."),

        // The second answer to a question the corpus already had an answer to, which is why it is
        // here at all: 98,989 of its records corroborate a link the Berean's own tables state, and
        // 8,345 disagree with one. Its repository does say CC BY 4.0 over the whole of the data,
        // in its README and its LICENSE.md, but the per-set TOML is the statement closest to the
        // bytes and the more restrictive where they differ — the Arabic ONAV set says CC BY-SA 4.0
        // and is excluded on that.
        new("clearbible", "Clear Bible Alignments",
            "Clear Bible, Inc.; each set is credited to the holder it names — BiblioNexus, Biblica, Inc. "
            + "or the United Bible Societies — or to the team that made it",
            "CC BY 4.0",
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

        // The same publisher and a different work, so a second entry rather than a line under the
        // one above: the interlinear's English gloss stands on every word of the Greek New
        // Testament, and one credit over both would put the tables' name on it. The file it comes
        // in still carries Bible Hub's 2016 all-rights-reserved notice above the line saying it is
        // now public domain; the owner ruled on 2026-09-20 that the licensing page's statement
        // about the Berean texts is the one believed, and both are kept beside the data.
        new("berean-interlinear", "Berean Interlinear Bible", "Bible Hub / Berean Bible",
            "Public Domain",
            "https://berean.bible/licensing.htm",
            "https://interlinearbible.com",
            "What each Greek word of Nestle 1904 means in English, word by word, as the Berean "
            + "interlinear glosses it. The edition itself arrived with no English at all, so this is "
            + "what lets a reader who has no Greek see what the word in front of them says.",
            "Berean Interlinear Bible", WordGlosses: Sources.NestleSlug),

        // Public domain by the only statement attached to the bytes — the repository's README says
        // "License? Public Domain. Copy freely." and there is no LICENSE file and no licence on the
        // GitHub repository record. Robinson asks, without requiring it, that his name and the
        // title stay with the text; both are here. The re-wrappings disagree with the original and
        // are more restrictive, so they are not the ones believed.
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
            "Zefania KJV+", Links: true, Unlisted: true),

        // The same footing as the entry above and the same words for it. eBible's Public Domain line
        // is over the package; nobody names who tagged the words or on what terms. toledot.info,
        // which publishes a corrected copy of the same layer, traces it to the Zefania XML Strong
        // module of December 2005, and eBible's copy carries that module's uncorrected errors. The
        // prefix is the text's slug as the rows spell it, which is case-sensitive like every other.
        new("luther1912-strong", "Strong tagging of the Lutherbibel 1912",
            "not named by anyone; descended from the Zefania XML Strong module of December 2005, and "
            + "read from the copy eBible.org publishes",
            "No licence stated",
            "https://ebible.org/deu1912/copyright.htm",
            "https://ebible.org/find/details.php?id=deu1912",
            "The Strong number on each word of Luther's 1912 German, which is what every link between "
            + "the German and the Hebrew or Greek is matched on. The text under it is out of copyright "
            + "by age; the tagging is somebody's work, and nobody who publishes it says whose. It is "
            + "all this supplies: which tagged word pairs with which original word is matched within "
            + "the verse by this project, at a confidence, and no part of that pairing is stated by "
            + "anyone.",
            "the Strong numbers LUTH1912 carries", Links: true, Unlisted: true),

        // Used as an input to the mapping and nothing more, and the declaration says so: the only
        // terms anyone states for this numbering permit use of the work unmodified, for a purpose
        // this project does not claim. The owner decided on 2026-09-13 to take the correspondences
        // it yields and nothing of the numbering itself; the notice in full, and that decision, are
        // in Resources/SynodalStrong/LICENCE.md. The copy that was read credits no one for it.
        new("bju-synodal-strong", "Strong numbering of the Russian Synodal",
            "Bob Jones University, 1996; the copy read is swmail/RST, prepared by Wjatscheslaw Stoljarski",
            "Non-profit use only, of the work unmodified — Bob Jones University's 1996 notice",
            "http://www.clavmon.cz/ultranet/bw/bwpopisVerzi.htm",
            "https://github.com/swmail/RST",
            "Which word of the Synodal renders which Hebrew or Greek word, matched within the verse on "
            + "the Pierce-Strong numbers Bob Jones University keyed to every word and phrase of the "
            + "Synodal. The numbering is read for the length of one run and never stored or served: "
            + "the corpus holds only the links drawn from it, each carrying a confidence, because a "
            + "number is a lemma and which occurrence a word renders is this project's inference.",
            Sources.SynodalStrongCredit, Links: true,
            Obliges: "Bob Jones University's notice of 1996 permits use of the work unmodified, for the "
                + "purpose of propagating the gospel, and not for profit. The corpus holds neither the "
                + "tagged text nor its numbers, only the correspondences drawn from them, and credits "
                + "Bob Jones University on every link drawn from them."),

        // Released by FHL under the GNU FDL, which permits this use without asking. They are read the
        // way the Synodal's numbering is — for one run, never stored — so nothing of the numbering
        // itself is served; Resources/ChineseUnion1919/LICENCE.md has the statement and the reading.
        new("fhl-cuv-strong", "Strong numbering of the Chinese Union Version",
            "the Faith Hope Love foundation (信望愛), in its Chinese Bible Online Library project; the copy read "
            + "is CrossWire's ChiUn and ChiUns modules, rebuilt from bible.fhl.net in 2021",
            "GNU Free Documentation License (the version is not named)",
            "https://www.gnu.org/licenses/fdl.html",
            "https://www.fhl.net/gb/fhl/fhl8.html",
            "Which words of the Chinese Union Version render which Hebrew or Greek word, matched within the "
            + "verse on the Strong numbers FHL keyed to the spans of its transcription. The numbering is read "
            + "for the length of one run and never stored or served: the corpus holds only the links drawn "
            + "from it, each carrying a confidence, because a number is a lemma and which occurrence a word "
            + "renders is this project's inference.",
            Sources.UnionStrongCredit, Links: true,
            Obliges: "FHL asks that its Strong numbers be credited and not stripped from a copy of its file; the "
                + "corpus copies neither the file nor the numbers, and credits FHL on every link drawn from them. "
                + "The FDL does not say whether correspondences derived from the numbers are a modified version "
                + "of them."),

        // Read the way the Synodal's numbering is: for one run, never stored or served. eBible republishes
        // the tagging under a public-domain line without his name; his own terms are that it is given to
        // software publishers and kept locked. The owner ruled on 2026-09-20 that it may be used for the
        // mapping alone; Resources/ReinaValera1909/LICENCE.md has the comparison, his terms and the ruling.
        new("gomez-rv1909-strong", "Strong numbering of the Reina-Valera 1909",
            "Rubén Gómez, 2012; the copy read is the one eBible.org carries in spaRV1909, which does not name him",
            "Copyrighted; given by its author to software publishers, locked — his announcement of 2012-03-28",
            "http://www.bsreview.org/blog/2012/03/reina-valera-1909-con-numeros-de-strong.html",
            "https://ebible.org/spaRV1909/",
            "Which word of the Reina-Valera 1909 renders which Hebrew or Greek word, matched within the verse on "
            + "the Strong numbers Rubén Gómez keyed to its words. The numbering is read for the length of one run "
            + "and never stored or served: the corpus holds only the links drawn from it, each carrying a "
            + "confidence, and only on the words Clear Bible's hand-made alignment leaves.",
            Sources.ReinaValeraStrongCredit, Links: true, Unlisted: true,
            Obliges: "Used for the mapping alone, by the owner's ruling of 2026-09-20: the numbers are shown nowhere "
                + "and the tagged text is not copied; Gómez is credited on every link drawn from them."),

        // Read the way FHL's numbers are: for one run, never stored. CrossWire's terms for the Segond
        // module are a permission to CrossWire; the owner approved taking the numbering for the mapping
        // on 2026-10-01, and Resources/Segond1910Strong/LICENCE.md has the statement and the reading.
        new("concordance-segond-strong", "Strong numbering of Louis Segond 1910",
            "Concordances et Traductions de la Bible (concordance.bible), 2026; the copy read is CrossWire's "
            + "FreSegond1910 module, version 4.0, whose text is Richard Lemay's",
            "Copyrighted; Permission to distribute granted to CrossWire",
            "https://www.crosswire.org/ftpmirror/pub/sword/raw/mods.d/fresegond1910.conf",
            "https://concordance.bible/",
            "Which words of Louis Segond 1910 render which Hebrew or Greek word, matched within the verse "
            + "on the Strong numbers Concordances et Traductions de la Bible keyed to the Segond. The "
            + "numbering is read for the length of one run, laid onto the Segond the corpus loads from "
            + "eBible, and never stored or served: the corpus holds only the links drawn from it, each "
            + "carrying a confidence, because a number is a lemma and which occurrence a word renders is "
            + "this project's inference.",
            Sources.SegondStrongCredit, Links: true, Unlisted: true,
            Obliges: "The module's terms are a permission granted to CrossWire to distribute it; the corpus "
                + "distributes neither the module nor its numbers, only correspondences derived from them."),

        // The same project and the same terms as the King James mapping (mapping/LICENCE.md);
        // Resources/OpenHebrewBible/LICENCE.md has the statement as read and the commit it was read at.
        new("ohb-cuv", "Open Hebrew Bible mapping of the Chinese Union Version",
            "Eliran Wong, Open Hebrew Bible Project, on the Faith Hope Love foundation's tagged text of the Union Version",
            "CC BY-NC 4.0",
            "https://creativecommons.org/licenses/by-nc/4.0/",
            "https://github.com/eliranwong/OpenHebrewBible/tree/master/009-BHS-mapping-CUV",
            "Which BHS word each span of the Chinese Union Version's Old Testament renders, as Eliran Wong "
            + "mapped FHL's Strong-numbered spans to the running word numbers of BHS. Stated by the source: "
            + "where FHL's numbers say only which lexeme a span renders, the mapping names the occurrence.",
            Sources.OpenHebrewCuvCredit, Links: true,
            Obliges: "NonCommercial: these links, and anything published from them, may not be used commercially; "
                + "Eliran Wong and the Open Hebrew Bible Project are credited on every link and claim."),

        new("concordance-darby-strong", "Strong numbering of the Darby French Bible",
            "Concordances et Traductions de la Bible (concordance.bible); the copy read is CrossWire's FreJND "
            + "module, version 3.3",
            "Public Domain",
            "https://www.crosswire.org/ftpmirror/pub/sword/raw/mods.d/frejnd.conf",
            "https://concordance.bible/",
            "Which words of Darby's French Bible render which Hebrew or Greek word, matched within the "
            + "verse on the Strong numbers Concordances et Traductions de la Bible keyed to it. The "
            + "numbering is read from the module the text is loaded from, for the length of one run, and "
            + "never stored: the corpus holds the links drawn from it, each carrying a confidence.",
            Sources.DarbyFrenchStrongCredit, Links: true, Unlisted: true),

        new("schlachter-strong", "Strong numbering of the Schlachter Bible 1951",
            "not named by anyone; the copy read is CrossWire's GerSch module, version 2.1",
            "Copyrighted; Free non-commercial distribution",
            "https://www.crosswire.org/ftpmirror/pub/sword/raw/mods.d/gersch.conf",
            "https://www.crosswire.org/sword/modules/ModInfo.jsp?modName=GerSch",
            "Which words of the Schlachter Bible of 1951 render which Hebrew or Greek word, matched within "
            + "the verse on the Strong numbers the module puts on them. The numbering is read from the "
            + "module the text is loaded from, for the length of one run, and never stored: the corpus "
            + "holds the links drawn from it, each carrying a confidence.",
            Sources.SchlachterStrongCredit, Links: true, Unlisted: true,
            Obliges: "NonCommercial: the module is distributed for non-commercial use only, so these links, "
                + "and anything published from them, may not be used commercially."),

        new("rlt-strong", "Strong numbering of the Revised Literal Translation",
            "the Bible Foundation (Old Testament) and CrossWire's KJV2003 project (New Testament), as Michael "
            + "W. Jones, Sr. carried them onto his revision; the copy read is CrossWire's RLT module, version 1.0",
            "GPL",
            "https://www.crosswire.org/ftpmirror/pub/sword/raw/mods.d/rlt.conf",
            "https://www.crosswire.org/sword/modules/ModInfo.jsp?modName=RLT",
            "Which words of the Revised Literal Translation render which Hebrew or Greek word, matched "
            + "within the verse on the King James's Strong numbers the module carries. The numbering is "
            + "read from the module the text is loaded from, for the length of one run, and never stored: "
            + "the corpus holds the links drawn from it, each carrying a confidence.",
            Sources.RevisedLiteralStrongCredit, Links: true,
            Obliges: RevisedLiteralTerms),

        new("byzantine-apparatus", "Apparatus to the Byzantine Textform",
            "Maurice A. Robinson and William G. Pierpont; the ByzTxt repository's beta-code files, release v3.3.2",
            "Public domain (the Unlicense)",
            "https://github.com/byztxt/byzantine-majority-text/blob/27a45ff1b7be6c17ccbfeac414f3f55732ae8e28/LICENSE.txt",
            "https://github.com/byztxt/byzantine-majority-text/tree/27a45ff1b7be6c17ccbfeac414f3f55732ae8e28/source/CCAT",
            "Where the Byzantine manuscripts divide between two readings, and where Nestle-Aland 28 and the "
            + "Editio Critica Maior read otherwise, as Robinson notes it beside his text. A note is linked only "
            + "where a loaded edition prints its other reading at the place: the critical notes against Nestle "
            + "1904, the divided readings against Scrivener's Received Text.",
            Sources.ByzantineApparatusCredit, Links: true),

        new("rlt-positions", "Received Text positions of the Revised Literal Translation",
            "CrossWire's KJV2003 project, as Michael W. Jones, Sr. carried its New Testament tagging onto his "
            + "revision; the copy read is CrossWire's RLT module, version 1.0",
            "GPL",
            "https://www.crosswire.org/ftpmirror/pub/sword/raw/mods.d/rlt.conf",
            "https://www.crosswire.org/sword/modules/ModInfo.jsp?modName=RLT",
            "Which words of the Revised Literal Translation's New Testament render which words of the Textus "
            + "Receptus: the module names, beside each English word, the place of the Greek word in its verse "
            + "and the Greek word itself. Read for the length of one run and never stored; a place is linked "
            + "only where Scrivener's or Stephanus's printing holds that Greek word there.",
            Sources.RevisedLiteralPositionCredit, Links: true,
            Obliges: RevisedLiteralTerms),

        new("glaux", "GLAUx", "Alek Keersmaekers and the GLAUx contributors", "CC BY-SA 3.0",
            "https://creativecommons.org/licenses/by-sa/3.0/",
            "https://github.com/alekkeersmaekers/glaux",
            "The dictionary form of every word of the Septuagint. Brenton's translation is public "
            + "domain and arrived with no annotation at all, so this is the one thing in the corpus "
            + "that makes the Greek Old Testament searchable by word rather than by spelling. Only "
            + "the lexical table is taken: GLAUx's own Greek text is not loaded, and its lemmas are "
            + "applied to the Brenton text already served.",
            "GLAUx", Lemmas: Sources.BrentonSeptuagintSlug,
            Citation: "Keersmaekers, Alek (2021): The GLAUx corpus: methodological issues in designing a long-term, "
                + "diverse, multi-layered corpus of Ancient Greek. Proceedings of the 2nd International Workshop on "
                + "Computational Approaches to Historical Language Change 2021, 39–50. Online: Association for "
                + "Computational Linguistics. doi:10.18653/v1/2021.lchange-1.6.",
            Obliges: "ShareAlike: the lemmas taken from here, and anything published from them, are "
                + "offered under CC BY-SA 3.0 in turn. Only the lexical table is taken and the "
                + "Brenton text it annotates is public domain, which is what keeps the clause on "
                + "the lemmas rather than on the Septuagint."),

        // Two second analyses of the Greek New Testament, each a row per Nestle word in
        // word_parsing. Both licences require attribution, and MACULA's names the exact string.
        new("macula", "MACULA Greek Linguistic Datasets", "© 2022-2024 by Biblica, Inc., published by Clear Bible",
            "CC BY 4.0",
            "https://creativecommons.org/licenses/by/4.0/",
            "https://github.com/Clear-Bible/macula-greek",
            "A second reading of every word of Nestle 1904: its part of speech, whether it is a proper "
            + "name, its lemma and its grammatical features. It is the one source here that states "
            + "outright which Greek words are names, and it is published over this same edition, so "
            + "each reading sits on the word it was made for without any alignment. Only the word "
            + "table is taken; the syntax trees, semantic roles and the glossing layers bound into "
            + "the same repository are not.",
            "MACULA Greek Linguistic Datasets", Parsings: true,
            Citation: "MACULA Greek Linguistic Datasets, available at "
                + "https://github.com/Clear-Bible/macula-greek/"),

        new("morphgnt", "MorphGNT: SBLGNT Edition", "James K. Tauber", "CC BY-SA 3.0",
            "https://creativecommons.org/licenses/by-sa/3.0/",
            "https://github.com/morphgnt/sblgnt",
            "A second parsing and lemma for the words of Nestle 1904, made against the SBL Greek New "
            + "Testament and placed on the Nestle word standing in the same place. It stands beside "
            + "the edition's own morphology rather than replacing it, so where the two analyses "
            + "disagree both are kept. Only the parsing is taken: none of the SBLGNT's own text is "
            + "stored or served.",
            "morphgnt/sblgnt", Parsings: true,
            Citation: "Tauber, J. K., ed. (2017) MorphGNT: SBLGNT Edition. Version 6.12 [Data set]. "
                + "https://github.com/morphgnt/sblgnt DOI: 10.5281/zenodo.376200",
            Obliges: "ShareAlike: the parsings taken from here, and anything published from them, are "
                + "offered under CC BY-SA 3.0 in turn. They sit beside the Nestle text rather than "
                + "adapting it, which keeps the clause on the parsings and off the edition."),

        // The lexicon every Strong number in the corpus resolves through, and the one dataset with
        // no declaration at all — it contributes neither rows carrying a source nor links, so the
        // undeclared report was blind to it.
        //
        // The licence recorded is Strong's own, long out of copyright. The Hebrew file embeds a
        // second work under its own terms, and it is declared in Contains rather than folded in
        // here: the TWOT reference on 6,070 entries is Archer and Harris's and is under a 1980
        // Moody copyright, and nothing else in the lexicon is.
        new("strong", "Strong's Exhaustive Concordance", "James Strong", "Public Domain",
            "https://en.wikipedia.org/wiki/Public_domain",
            "https://openscriptures.org",
            "The dictionary every Strong number in the corpus resolves to: the lemma, the "
            + "transliteration, the definition, and how the King James renders it. Strong published "
            + "it in 1890 and it is long out of copyright; the machine-readable Greek was prepared by "
            + "Ulrik Petersen in 2006 from the ASCII e-text, whose own prologue reads \"Public "
            + "Domain -- Copy Freely\".",
            "Strong", Lexicon: true, Contains:
            [
                new Work(
                    "Theological Wordbook of the Old Testament",
                    "Gleason L. Archer and R. Laird Harris, published by Moody Publishers",
                    "Copyright © 1980 by the Moody Bible Institute",
                    "https://www.moodypublishers.com",
                    "The TWOT reference carried by 6,070 Hebrew entries, and only that: the file's own "
                    + "header assigns the entry to Strong and the gloss to TWOT, so nothing else in the "
                    + "lexicon is theirs."),
            ]),

        // The short Greek glosses and each lexeme's part of speech. Read for the one-word gloss and
        // the morphology code; the Abbott-Smith entry beside them in the same file is not loaded.
        new("stepbible-tbesg", "STEPBible Brief Greek Lexicon (TBESG)",
            "STEP Bible, based on work at Tyndale House, Cambridge",
            "CC BY 4.0",
            "https://creativecommons.org/licenses/by/4.0/",
            "https://github.com/STEPBible/STEPBible-Data",
            "What a Greek word means, in a word or two, as Tyndale House scholars glossed each lexeme. "
            + "It numbers the Septuagint's vocabulary as well as the New Testament's, so a Greek Old "
            + "Testament word Strong never catalogued still has a meaning. A word reaches a gloss by its "
            + "dictionary form or by the Strong number its edition prints, and the reader is told which. "
            + "The part of speech and gender a Greek entry shows are this lexicon's too, since Strong's "
            + "Greek dictionary gives none. Only those are taken; the full lexicon entry beside them in "
            + "the same file is not.",
            Sources.BriefGreekLexiconPrefix, Glossary: true, HolderUrl: StepBible,
            Obliges: "Attribution only, under CC BY 4.0. The file also asks that its data not be passed "
                + "on as a copy but that others be referred to github.com/STEPBible, so a download of "
                + "the corpus should point there rather than carry the glosses."),

        // The verse numbering every text is placed by. Its header asks that changes come with a note of
        // them that those who use the result can read, and the corpus changes it: Changes is that note,
        // kept beside TvtmsCorrections and TvtmsSupplements, which make the changes.
        new("tvtms", "TVTMS: Translators Versification Traditions with Methodology for Standardisation",
            "STEP Bible, based on work at Tyndale House, Cambridge",
            "CC BY 4.0",
            "https://creativecommons.org/licenses/by/4.0/",
            "https://github.com/STEPBible/STEPBible-Data",
            "Which verse of one numbering is which verse of another — the Hebrew, the Greek, the Latin and "
            + "the English traditions — with the tests that tell an edition's numbering apart. It is what "
            + "puts every verse of every text here at one shared address, so the same words stand side by "
            + "side however each text numbers them.",
            "TVTMS", Frame: true, HolderUrl: StepBible,
            Obliges: "Attribution, under CC BY 4.0. The data asks that changes made to it come with a note "
                + "of them, which is the list of changes here, and that others be referred to "
                + "github.com/STEPBible for the data itself.",
            Changes:
            [
                "Malachi's last three verses in the Greek numberings (Brenton's 3:22-24, Swete's 4:4-6) are "
                + "placed by their words: Elijah, the turning of hearts, then the law of Moses. The data placed "
                + "each where the next one belongs.",
                "The oracle against Ammon, which the Greek prints as Jeremiah 30:1-5, is placed with Jeremiah "
                + "49:1-5 of the Hebrew. The data named it 30:17-21.",
                "In the Song of the Three (Daniel 3), the Greek and Latin 3:77-78 are placed springs before "
                + "seas, and the Greek 3:80-81 birds before beasts, as every edition here prints them.",
                "Brenton's Jeremiah 25 and 30-32: Edom, Ammon, Kedar and Damascus in chapter 30, Elam ending "
                + "at 25:20, and the cup of wrath as 32:15-38. Added; the data describes none of it.",
                "Proverbs 24 in Brenton and Swete, where they print the sayings of Agur and of Lemuel's "
                + "mother, and Swete's chapters numbered straight on past the verses the Greek leaves out. "
                + "Added.",
                "Brenton's division of Leviticus 8:18-30. Added.",
                "Swete's 2 Samuel 19 under the English numbers, his altar on Ebal as Joshua 9:3-8, and his "
                + "order at the end of Joshua. Added.",
                "The unclean birds of Leviticus 11:13-19 in both Greek editions, which name the ostrich, the "
                + "owl and the gull before the raven. Added.",
                "The Reina-Valera of 1909 as eBible publishes it: the Spanish division inside the English "
                + "verse count, with the empty verses that leaves. Added.",
                "Kulish's Bible of 1903, which divides some forty chapters in its own way. Added.",
                "The Synodal as bible4u publishes it: Song of Songs 1, Psalm 90, Isaiah 3, Esther 1 and "
                + "Revelation 20. Added.",
                "Ohienko's Psalm 65 as bible4u publishes it, numbered from the title. Added.",
                "The Segond of 1910 at the end of Job and in Ecclesiastes 11-12. Added.",
                "Brenton's English Nehemiah 4, numbered as the English Bibles number it. Added.",
            ]),

        // The second answer to what the Hebrew of a verse says, beside the Open Hebrew Bible's, and the
        // gloss EVIDENTIA reads a Hebrew word by. Its rows are claims on links others drew, so it is
        // counted by those. The publisher's licence page is the statement it is loaded under;
        // Resources/STEPBible/LICENCE.md has all three of STEP's statements.
        new("tahot", "TAHOT: Translators Amalgamated Hebrew Old Testament",
            "STEP Bible, based on work at Tyndale House, Cambridge",
            "CC BY-NC 3.0",
            "https://creativecommons.org/licenses/by-nc/3.0/",
            "https://github.com/STEPBible/STEPBible-Data",
            "What each Hebrew word and each of its prefixes means where it stands, as Tyndale House "
            + "scholars glossed the Hebrew Old Testament. Beside the Open Hebrew Bible's answer to which "
            + "Hebrew word a King James word renders, it is a second and independent one, and it is the "
            + "gloss a Hebrew word is read by when a translation's words are weighed against it.",
            Sources.TahotPrefix, LinkClaims: true, HolderUrl: StepBible,
            Obliges: "NonCommercial: STEP Bible's datasets may be used freely in projects that are not for "
                + "profit, with attribution to Tyndale House, Cambridge."),

        // The Ge'ez words' own meanings. Dillmann's book is long out of copyright; the digital
        // edition is not, and its ShareAlike binds what is derived from it here, which the owner
        // accepted for this one layer.
        new("dillmann", "Lexicon Linguae Aethiopicae (Dillmann 1865), digital edition",
            "August Dillmann; digitised by Beta maṣāḥǝft and the TraCES project, Hiob-Ludolf-Zentrum für "
            + "Äthiopistik, Universität Hamburg (ed. Alessandro Bausi)",
            "CC BY-NC-SA 4.0",
            "https://creativecommons.org/licenses/by-nc-sa/4.0/",
            "https://github.com/BetaMasaheft/DillmannData",
            "What a word of the Ethiopic Bible means, in Dillmann's Latin, with the Greek he gives for it. "
            + "A word is matched to an entry only where its letters, with the particles and endings "
            + "written onto it taken off, fit one headword and no other, or where several fit and the "
            + "Greek word it is aligned to is among the Greek Dillmann gives for exactly one of them; "
            + "the reader is told which. Nothing of the Latin is translated.",
            Sources.DillmannLexiconPrefix, Glossary: true,
            Citation: "Dillmann, August. Lexicon linguae aethiopicae, cum indice latino. Lipsiae: T. O. Weigel, "
                + "1865. Online Lexicon Linguae Aethiopicae, Beta maṣāḥǝft, Hamburg.",
            Obliges: "Attribution, non-commercial use only, and ShareAlike: which Ge'ez word is taken as a "
                + "form of which entry, and anything made from the entries, is shared under CC BY-NC-SA 4.0 "
                + "too."),

        // The links an EVIDENTIA run proposed and a reviewer accepted: this project's own work, under the
        // one licence the owner set for all of it (2026-10-29). UDPipe's analysis is evidence the rules
        // read, credited in the description, and is not what is published.
        new("evidentia", "EVIDENTIA", "this project",
            "CC BY-NC 4.0",
            "https://creativecommons.org/licenses/by-nc/4.0/",
            "https://essenthos.org",
            "Word correspondences between a translation and its original that this project's rules "
            + "proposed and a reviewer accepted, each with the rule that proposed it, the confidence of "
            + "its tier and the run it came from. Where a translation carries no tagging of its own, the "
            + "rules read its words through UDPipe and the Universal Dependencies models of Milan Straka "
            + "and Jana Straková (ÚFAL, Charles University, http://hdl.handle.net/11234/1-3131) and of "
            + "bnosac, which give the dictionary form, the part of speech and the parse of each word.",
            Sources.EvidentiaRunPrefix, Links: true, OwnWork: true),

        // What this project asserts itself, and it belongs in the list precisely because it is
        // ours: a claim of our own, printed beside the ones we merely carry. The links are nearly
        // all of it — correspondences nobody states, which read exactly like an undeclared third
        // party until they were claimed here.
        // Its licence is the owner's to set for the site, and is filled in where it is served.
        new(Own, "Essenthos", "this project", null, null,
            "https://essenthos.org",
            "What this project works out for itself. Corrections and separations it makes to the "
            + "datasets it carries, each recorded on the row it changed — a correction to a text or a "
            + "dataset whose licence asks for adaptations to be shared alike is shared under that "
            + "licence, as the source's own; and the word "
            + "correspondences no source states — the two Greek editions joined on the Strong "
            + "numbers both of them tag, the two Hebrew witnesses and the two Septuagints joined on "
            + "the letters each pair writes alike, and the English function words the tagging "
            + "skips, recovered from the morphology the Greek states; the names a translation prints, "
            + "placed on the words of the original that name the same person, place or people; and the "
            + "words a translation's own italics or brackets set apart as its translators', marked as "
            + "absent from the original. Every one of them carries a "
            + "confidence, which is how it is told apart from testimony. And the parallel passages — "
            + "Samuel and Kings beside Chronicles, the psalms written twice, the first three gospels — "
            + "found where two passages share a long run of the same words in the Hebrew or the Greek.",
            "Essenthos", Links: true, OwnWork: true, Methods:
            [
                "the Strong numbers both editions carry",
                "the words left over once the Strong numbers were paired",
                "the untagged English function words",
                "the consonants both Hebrew witnesses write",
                "the letters both Greek editions print",
                Sources.VerseReadingPrefix,
                "the possessive beside a word linked to a Hebrew word with a pronominal suffix",
                "records written for people a verse names and no dataset holds, each with the verse "
                + "it rests on and, where the identification is open, who else it might be",
                "a second reader's judgement, where a review of the readings overturned one and "
                + "named the referent it found instead — carrying the reading it replaced and the "
                + "argument that replaced it",
                "the gentilic Strong's Dictionary derives, resolving to exactly one people",
                "a reading of the verse naming a people the encyclopedia did not hold",

                // Names placed on the original's words by whom both texts name, by their letters and
                // by the text's own spellings: inferred here, at a confidence, from no one's links.
                "the names of the verse, placed by whom both texts name",

                // A translation's words its own marks set apart as the translators' (italics, square
                // brackets), stated absent from the originals. The marks are the edition's; reading
                // them as absences, and against which witness, is ours. Each row names its edition.
                "Almeida 1911, the words its italics print as the translator's own",
                "American Standard Version 1901, the words its italics print as the revisers' own",
                "Reina-Valera 1909, the words its italics print as the translators' own",
                "Russian Synodal, the words its square brackets mark as the translators' own",
                "Biblica Open New Ukrainian Translation 2022, the words it marks as added",
                "Brenton 1844, the words its italics print as the translator's own",
                // Literally how the rows begin, because these double as the source-string
                // prefixes the dataset claims: the English of the lexicon is public domain and the
                // rendering into a reader's language is this project's, so the row names the model,
                // the prompt version and the day, and the English stays beside it.
                Sources.StrongTranslationPrefix,

                // The clauses the encyclopedia says an entity is, and the relationships read off
                // them. The row names the model and the day it was asked, so the pass is
                // identifiable and removable, and the string is what it begins with.
                Sources.DescriptorReadingPrefix,

                // Passages that tell the same thing in the same words, found by the dictionary
                // forms the two share; the row says which words those are.
                CrossReferenceSets.ParallelsSource,
            ]),
    ];

    /// <summary>Which dataset a row's source string belongs to, or null if none claims it.</summary>
    public static string? Of(string? source) => Match(source)?.Id;

    /// <summary>
    /// The one dataset a row's source string belongs to: the one whose claiming prefix is longest.
    /// One prefix can begin another — the geocoding's <c>"OpenBible.info"</c> begins the cross
    /// references' <c>"OpenBible.info Cross References"</c> — and the longer is the more particular
    /// claim, so a row is never credited to both, nor to whichever happens to be listed first.
    /// </summary>
    public static Dataset? Match(string? source)
    {
        if (source is null)
        {
            return null;
        }

        Dataset? best = null;
        var length = -1;
        foreach (var dataset in All)
        {
            foreach (var prefix in dataset.Prefixes)
            {
                if (prefix.Length > length && source.StartsWith(prefix, StringComparison.Ordinal))
                {
                    best = dataset;
                    length = prefix.Length;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// The dataset a reader is told a row comes from: the one <see cref="Match"/> finds, unless it is
    /// one the corpus names to no reader.
    /// </summary>
    public static Dataset? Credited(string? source) => Match(source) is { Unlisted: false } dataset ? dataset : null;

    /// <summary>Whether a row carrying this source string is credited to this dataset, and to no other.</summary>
    public static bool Claims(Dataset dataset, string source) => ReferenceEquals(Match(source), dataset);

}
