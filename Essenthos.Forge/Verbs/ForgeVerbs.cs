using System.Globalization;
using System.Text;
using System.Text.Json;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Loading.Links.Evidentia;

namespace Essenthos.Core.Verbs;

/// <summary>
/// Every verb the Forge answers to, in the order <c>forge help</c> lists them. The dispatch and the help
/// both read this one list, so a verb cannot be runnable and unlisted.
/// </summary>
internal static partial class ForgeVerbs
{
    public static IReadOnlyList<ForgeVerb> All { get; } =
    [
        new("help", "", "This list.", Help),

        // The build, the corpus measures and the releases.
        new("maintain", "[<table> ...]", "VACUUM (ANALYZE) of the tables that need it, or of the ones named.", Maintain),
        new("build", "[--into <database> [--seed <database>] [--replace]] [--apply] [--owner-rows [--rows <file>]...] [--level skip|essentials|all] [--parallel <n>] [--producers <n>] [--no-products]",
            "Apply the owner's decided rows to the store when asked (--owner-rows, as owner-rows does); make every product whose inputs moved, "
            + "in the order build/products declares, and fail on a read it does not declare; then fill a new database of the configured server "
            + "from the products (--into), or clone the seed into it and replace there what moved since the seed was built (--into --seed, the "
            + "daily path), or replace in the configured one what moved since it was built (--apply); then verify at the level asked and write "
            + "build/report-<n>. Never builds into the live corpus or the snapshot.",
            BuildVerb),
        new("clone", "<from> <to> [--replace] [--served-only]",
            "Copy a database build --into or --apply made into another of the configured server (CREATE DATABASE ... TEMPLATE, nobody may be "
            + "connected to the first) with its build/served state, so build --apply on the copy takes only what moved. Before a swap: "
            + "clone the next database into the seed. Never copies into or from the live corpus or the snapshot. --served-only copies the "
            + "build/served state alone and touches no database: for a database restored whole on another server from a dump of <from>.",
            CloneVerb, Least: 2, Most: 4),
        new("panes", "[<text,...>] [--force] [--parallel <n>]",
            "Write the parallel view's panes (one row of pane per text and canonical chapter) from the configured database, for the texts "
            + "named or every text whose inputs moved, and panes/<SLUG> beside them. Never writes the live corpus or the snapshot.",
            PanesVerb),
        new("record-words", "[<text,...>] [--force]",
            "Count what a record page reads of the words from the configured database: how often each text prints each form for each "
            + "record under each Strong number (record_spelling, record-spellings/<SLUG>) and where each record first stands in each "
            + "King James verse (record_position, record-positions/KJV), for the texts named or every text whose inputs moved. Never "
            + "writes the live corpus or the snapshot.",
            RecordWordsVerb),
        new("families", "[--force]",
            "Compose every record's card (record_name, record_line, record-cards/<prose>) and every family's document (family_component, "
            + "family-components/<prose>) from the configured database where their inputs moved (all with --force), copying them from the "
            + "products where not. Never writes the live corpus or the snapshot.",
            FamiliesVerb, Most: 1),
        new("context", "[--force]",
            "Compose every chapter's context skeleton (context_chapter, context/chapters) and every record's line in every pair of "
            + "languages (context_line, context/lines/<prose>) with the whole descriptors it is cut from (record_descriptor, "
            + "record-descriptors/<prose>) from the configured database where their inputs moved (all with --force), "
            + "copying them from the products where not. Never writes the live corpus or the snapshot.",
            ContextVerb, Most: 1),
        new("facts", "[--force]",
            "Count the constants of the release the API reads instead of counting them at every start (release_fact, release-facts): "
            + "each text's counts and links, each dataset's rows, the Chapter Context's weights, from the configured database where what "
            + "it was built from or the counting code moved (always with --force), copying them from the product where not. Never "
            + "writes the live corpus or the snapshot.",
            FactsVerb, Most: 1),
        new("pages", "[--force] [--parallel <n>]",
            "Compose the document every record's, Strong number's and occupation's page is written from, in each site language (page_document, "
            + "page-documents/<kind>/<language>), from the configured database where what it was built from, the site's switches or "
            + "the API's code moved (all with --force), copying them from the products where not; n pages at once (Pages:Parallel, "
            + "else half the cores). Never writes the live corpus or the snapshot.",
            PagesVerb, Most: 3),
        new("strength", "[<text,...>] [--force]",
            "Count how strongly each text's verses are linked to every text linked to it, per canonical chapter, from the configured "
            + "database (verse_strength, strength/<SLUG>), for the texts named or every text whose inputs moved; a current product is "
            + "copied into a table that lacks it. Never writes the live corpus or the snapshot.",
            StrengthVerb),
        new("tables", "[<table,...>] [--from <Resources>] [--force]",
            "Write the served tables made whole from files (the lexicon, text relations, ...) as tables/<table>, each only when its sources moved. Reads no database.",
            TablesVerb),
        new("geez-layers", "[--gloss <run>] [--dillmann <run>] [--into <Resources>]",
            "Take a finished model run over the Ge'ez into the store as the files the build reads: its contextual glosses as "
            + "geez/glosses/<book> by word key (--gloss), Dillmann's senses rendered into the reader's languages as "
            + "geez/dillmann-renderings (--dillmann). A run is a folder, or its name under Resources/Essenthos/generation. "
            + "Only the run's checked lines; any line it cannot place on today's words or entries stops it before a file is written. "
            + "Reads no database.",
            GeezLayersVerb),
        new("carried-export", "[--into <Resources>] [--all]",
            "Write the served tables no producer makes yet (every one with --all, for a comparison) from the configured database as carried/<table>.",
            CarriedExport),
        new("verify", "[--level skip|essentials|all] [--only <measure,...>] [--texts <text,...>] [--floor <share>] [--served]",
            "Measure the corpus and fail on anything broken: all (the default) records what it finds for /v1/health; essentials holds the tables "
            + "to the build's row counts and keys and coverage to the floor; --only measures those named, --texts keeps those texts; "
            + "--served gates a restored release as a publication does, recording nothing.", VerifyVerb),
        new("release", "[--allow-dirty] [--allow-unrecorded] [--dry-run]",
            "Verify this machine's corpus and dump it into .releases/ as the next release.", Release),
        new("export", "[--to <folder>] [--dry-run] | <text> --as tsv|csv|jsonl [--to <file>]",
            "Write the texts that may be passed on, one file each with its licence and attribution, and a manifest of checksums; "
            + "with a text and --as, write that text's converted words in a common shape.",
            (forge, args) => args.Contains("--as") ? ExportText(forge, args) : Export(forge, args)),

        // The texts as converted products (TSK-1443): beside the load until the build reads them.
        new("convert", "<text,...|all> [--force] [--parallel <n>]",
            "Convert texts from their declared sources and corrections into their products under Resources/Essenthos/texts; "
            + "a text whose inputs did not move is left as it is.",
            ConvertVerb, Least: 1),
        new("texts", "compare <text,...|all> [--out <folder>] [--parallel <n>]",
            "compare: hold the converted products against the configured database by key and list every difference.",
            TextsVerb, Least: 2),
        new("publish", "--to <target> [--release <name>] [--without-rehearsal] [--every-table] [--copy-served] [--dry-run]",
            "Restore a release on a target, or apply its difference from the copy the target keeps, verify it there and swap it in.", Publish),
        new("difference", "--base <release> [--release <name>]",
            "Make the difference from one release to another (the newest by default) out of their dumps: the rows a database holding the base "
            + "loses and gains, with every table's sum, as one psql script beside them.", Difference),
        new("publish-downloads", "--to <target> [--from <folder>] [--dry-run]",
            "Send the export to a target's downloads: what it lacks, then the manifest, then away with the files no manifest names.", PublishDownloads),
        new("rollback", "--to <target> [--dry-run]", "Swap a target's live corpus with the previous release.", Rollback),
        new("releases", "[--on <target>]", "The releases this machine has built, and where each was published.", Releases),
        new("frame-files", "[<text,...>] [--from <Resources>] [--force] [--parallel <n>]",
            "Write the frame files of the texts named, or of every text, whose inputs moved: from each text's verse manifest, words, titles and "
            + "declaration and the versification data. Reads no database.",
            FrameFiles),
        new("frame-check", "[--database <name>] [--out <file>]",
            "Compute every text's canonical frame and compare it with the one the corpus holds, verse by verse. "
            + "Writes nothing to the corpus; --database reads another database on its server, --out writes the computed frame as a file.",
            FrameCheck),

        // Links between texts.
        new("align", "<from> <to> [--min <confidence>] [--model <model>] [--outside <text>] [--symmetrisation och|intersection] [--lexemes] [--consonants] [--books <b,...>|--all-books] [--workspace <name>] [--verses <range,...>] [--from <Resources>]",
            "Align two texts with SIL's statistical aligner from the store's files into the pair's aligner method file, training the answer "
            + "where none stored was made from the words as they stand. A declared pair runs as declared unless an option says otherwise; --verses writes only the rows of FROM words in those verses and keeps every other row of the file. Reads no database.",
            Align, Least: 2),
        new("aligner-pairs", "[<FROM-TO> ...] [--from <Resources>] [--force] [--parallel <n>]",
            "Write each aligned pair's aligner method file from the answer stored in Resources/Alignments for both texts' words, where an input moved. "
            + "Never trains: a pair with no answer for its words keeps its file and waits for align. Reads no database.",
            (forge, args) => SourcePairs(forge, args, Mapping.MappingMethods.Aligner)),
        new("compose", "<from> <via[,via[,via]]> <to> [--min <c>] [--precision <p>] [--agreeing <n>] [--daughter] [--unmeasured] [--dry-run] [--verses <range,...>] [--from <Resources>]",
            "Compose a text's links to an original through aligned intermediates from the store's files into the pair's composed method file, training "
            + "the hops it lacks; --dry-run scores it and writes nothing; --verses draws a declared composition as declared and writes only the rows of "
            + "FROM words in those verses, keeping every other row of the file. Reads no database.", Compose, Least: 3),
        new("composed-pairs", "[<FROM-TO> ...] [--from <Resources>] [--force] [--parallel <n>]",
            "Write each composed pair's composed method file as its composition is declared, from the stored answers, the middle texts' finals and the "
            + "pair's final, where an input moved. Never trains. Reads no database.",
            (forge, args) => SourcePairs(forge, args, Mapping.MappingMethods.Composed)),
        new("score", "<from> <to> [--min <c,...>] [--model <m>] [--surface] [--stated] [--suppletion] [--pairs <file>] [--from <Resources>]",
            "What an aligner threshold costs, against the stated links of the pair's final in the store. Reads no database; writes nothing but the aligner's workspace.", Score, Least: 2),
        new("score-anchors", "<from> <to> [--min <c,...>] [--model <m>] [--fold <n>] [--from <Resources>]",
            "The aligner scored out of sample with held-out Strong anchors, from the store. Reads no database; writes nothing but the aligner's workspace.", ScoreAnchors, Least: 2),
        new("syntax", "<from> <to> [--model <m>] [--stated] [--from <Resources>]",
            "What the target's syntax is worth as a check on the aligner, from the store. Reads no database; writes nothing but the aligner's workspace.", Syntax, Least: 2),
        new("reina-valera-strong-score", "",
            "Score what Gómez's numbers would draw for the Reina-Valera against Clear Bible's alignment. Writes nothing.",
            ReinaValeraStrongScore),
        new("names-first", "<from> <to> [--books b,...] [--names-from <names-consensus --out folder>] [--target-lemmas glaux] [--alone entity,letters,spelling|none] [--uneven] [--single] [--held] [--out <folder>]",
            "Measure placing every name of a translation on the original's word naming the same person, place or people, by whom both name, their letters and the text's own spelling, against the configured database's links. Writes no database.",
            NamesFirst, Least: 2),
        new("possessives", "<from> <to>",
            "Count the possessives a Slavic text writes beside the word rendering a suffixed Hebrew word that the configured database does not link. Writes nothing.", Possessives,
            Least: 2),
        new("unshare", "<from> <to>",
            "Count the configured database's aligner links that put a pronoun or particle on a word another word renders. Writes nothing.", Unshare,
            Least: 2),

        // EVIDENTIA.
        new("evidentia-preview", "<from> <to> <book> <chapter> [--verse <v>] [--without-source-strong] [--without-known-renderings]",
            "Read one chapter through EVIDENTIA's evidence graph. Writes nothing.", EvidentiaPreview, Least: 4),
        new("evidentia-measure", "<from> <to> <book> <chapter> [options]",
            "Measure EVIDENTIA on one chapter against an answer key. Writes nothing.", EvidentiaMeasure, Least: 4),
        new("evidentia-measure-book", "<from> <to> <book> [--from-chapter <c>] [--to-chapter <c>] [options]",
            "Measure EVIDENTIA on one book against an answer key. Writes nothing.", EvidentiaMeasureBook, Least: 3),
        new("evidentia-run", "<from> <to> <book[:chapters]> ... [--parallel <n>] [options]",
            "Store a run: a decision for every word of the books given.", EvidentiaRun, Least: 2),
        new("evidentia-runs", "", "The stored runs.", EvidentiaRuns),
        new("evidentia-witness", "<FROM-TO> --books <b,...> [--from <Resources>] [--words <Resources>] [--gold stated|strong] [--heads gold] [--held] [--out <folder>] [--parallel <n>]",
            "Measure the attached-word rules of a Slavic translation as a second witness over the pair's links, from files, against the hand alignment the pair states; the sealed held part only with --held. Writes no product and no database.",
            EvidentiaWitnessVerb, Least: 2),
        new("evidentia-second-witness", "<FROM-TO> --books <b,...|a-b> [--from <Resources>] [--decisions <tsv>] [--out <folder>]",
            "Read the aligner's links beside names-first and a stored run's decisions (a file: source, target, tier, kind, content): agreement, disagreement and silence by the original word's class, who the hand alignment says is right, and the aligner's own precision on the hand alignment and the Strong layer. Writes no product and no database.",
            EvidentiaSecondWitnessVerb, Least: 2),
        new("evidentia-queue", "<run> [--tier <t>] [--book <b>] [--chapter <c>] [--verse <v>] [--take <n>] [--kind <k,...>]",
            "What a stored run still waits on. Reads only.", EvidentiaQueue, Least: 1),
        new("evidentia-approve", "<decision> ... [--reviewer <name>] [--note <text>]", "Approve decisions of a stored run.",
            EvidentiaVerdict),
        new("evidentia-reject", "<decision> ... [--reviewer <name>] [--note <text>]", "Reject decisions of a stored run.",
            EvidentiaVerdict),
        new("evidentia-correct", "<decision> <word> [--reviewer <name>] [--note <text>]",
            "Correct a decision to another word.", EvidentiaCorrect, Least: 2),
        new("evidentia-accept-tier", "<run> [--tier <t>] [filters] [--reviewer <name>] [--note <text>]",
            "Accept one tier of a stored run unread.", EvidentiaAcceptTier, Least: 1),
        new("evidentia-export", "[<run> ...]", "Write the verdicts to the ledger under Resources/Essenthos/evidentia.",
            EvidentiaExport),
        new("evidentia-pairs", "[<FROM-TO> ...] [--into <Resources>] [--ledger <Resources>] [--leave-out <run,...>]",
            "Write each pair's evidentia method file from the ledger and the texts' words and frames. Reads no database.", EvidentiaPairs),
        new("evidentia-problems", "<run,...> [--take <n>] [--min-words <n>] [--flagged]",
            "The verses a run did worst on.", EvidentiaProblems, Least: 1),
        new("evidentia-rerun", "<run> [--take <n>] [--min-words <n>]", "Run a stored run's worst verses again.",
            EvidentiaRerun, Least: 1),
        new("evidentia-compare", "<before> <after>", "Compare two stored runs.", EvidentiaCompare, Least: 2),

        // The encyclopedia's files that come from a source, and what still reads a database to measure or to ask the owner.
        new("occupations", "[--from <Resources>] [--force]",
            "Write the occupation record files from person-occupations.json, the records and the texts' words, frames, finals and Strong indexes, words named by key, where an input moved. Reads no database.",
            Occupations),
        new("dillmann-measure", "[--sample <file>]", "How many Ge'ez words reach Dillmann's lexicon. Reads only.",
            DillmannMeasure),
        new("images", "[--from <Resources>]", "Draw the pictures of the things whose 3D model changed, for the records/records things, and make the pictures' record files again. Writes picture files and records/facts/images; reads no database.", Images),
        new("wikipedia", "", "Match the records of the configured database to Wikidata and write the owner's list of the records the matching cannot tell from a namesake. Writes no database.", Wikipedia),
        new("commandments", "[--from <Resources>] [--force]", "Write the 613 commandments and the verses each rests on as the files the build loads, from Maimonides/commandments.tsv, where it moved. Reads no database.", Commandments),
        new("lands", "[--from <Resources>] [--force]", "Write the periods of the lands from PeriodO as the files the build loads, where the dataset moved. Reads no database.", Lands),
        new("cross-references", "[--from <Resources>] [--force] [--measure]",
            "Write OpenBible's and the Treasury's cross references and the parallels found in the originals' lemmas as the files the build loads, each where its inputs moved; --measure prints the parallel passages and writes nothing. Reads no database.",
            CrossReferences),
        new("topics", "[--from <Resources>] [--force]", "Write Nave's topics and the verses filed under each as the files the build loads, where the index moved. Reads no database.", Topics),
        new("names-consensus", "[--from <Resources>] [--force] [--parallel <n>] [--out <folder>] [--precision <p>] [--bar v,s,m]",
            "Write names/consensus for every text, whom its verses say its words name, from every text's words and frames, the originals' settled names, the records and the names the earlier method files carry, where an input moved; measured against the texts whose names a stated link carries. --precision or --bar measures only and writes no product; --out writes each text's findings. Reads no database.", NamesConsensus),
        new("records-export", "[--into <Resources>] [--force]",
            "Write our records, facts, languages and external datasets as files, from the database; refuses a store that holds them unless --force.", RecordsExport),
        new("records-load", "[--from <Resources>]", "Put the record files into the database (never live): records only from records/records.", RecordsLoad),
        new("records-derive", "[--from <Resources>] [--into <Resources>]", "Make the derived record files whose inputs are files (relationships, locations, pictures, Wikipedia links) from the hand files and the console's files, without a database.", RecordsDerive),
        new("records-canon", "[--from <Resources>] [--force]", "Write records/canon, which canons attest each record, from the names projection and the frames, without a database.", RecordsCanon),
        new("records-verses", "[--from <Resources>] [--force]", "Write records/derived/verses, the verses our words and relationships put each record in, from the names projection, the frames and the relationships, without a database.", RecordsVerses),
        new("owner-apply", "[--from <Resources>] [--dry-run]",
            "Apply the owner's console decisions that wait on the files step (owner-changes.jsonl, needs: files) to the record files "
            + "they concern, with his stamp on each row, then append the apply line. Reads no database.", OwnerApply, Most: 3),
        new("owner-rows", "[--from <Resources>] [--rows <file>]... [--dry-run]",
            "Apply the decided rows the Forge ships (Records/OwnerRows, or --rows, repeatable) to the names, dataset and record files they edit, "
            + "with the owner's stamp on each row; a row the files cannot take stops it before anything is written. Reads no database.",
            OwnerRowsVerb, Most: 9),
        new("records-compare", "<port/db> <port/db> [--out <file>]", "Compare two databases' records table by table by natural key. Reads only.", RecordsCompare, Least: 2),
        new("mapping-export", "[--into <Resources>] [--pairs A-B,...] [--parallel <n>] [--force]",
            "Write the database's links no build producer makes (the aligner's, the compositions') as the mapping's per-pair method files, "
            + "words by key, with the pairs' ordinals and verse hashes; refuses a store that holds them unless --force. Reads the database only.",
            MappingExportVerb),
        new("merge", "<pair> | --all [--from <Resources>] [--force] [--parallel <n>]",
            "Merge a pair's method files into its final and report by the stated precedence; losers stay in their files, marked.", Merge, Least: 1),
        new("verse-pairs", "[<FROM-TO>,...] [--from <Resources>] [--force] [--parallel <n>]",
            "Write each pair's verse links (mapping/<pair>/verses) from the two texts' frames and the pair's final, for every pair "
            + "whose inputs moved: every pair with a final and every declared pair. Reads no database.", VerseLinksVerb),
        new("strong-pairs", "[<FROM-TO> ...] [--from <Resources>] [--force] [--parallel <n>]",
            "Write each pair's strong method file from the translation's Strong numbers (its own, or laid from a numbered edition) and both texts' words and frames, where an input moved. Reads no database.",
            (forge, args) => SourcePairs(forge, args, Mapping.MappingMethods.Strong)),
        new("lexical-pairs", "[<FROM-TO> ...] [--from <Resources>] [--force] [--parallel <n>]",
            "Write each pair's lexical method file from the rules that read both texts' words (letters, glosses, morphology, the words Strong numbers leave), where an input moved. Reads no database.",
            (forge, args) => SourcePairs(forge, args, Mapping.MappingMethods.Lexical)),
        new("stated-pairs", "[<FROM-TO> ...] [--from <Resources>] [--force] [--parallel <n>]",
            "Write each pair's stated method file from the published sources that state it (tables, alignments, marks) and both texts' words and frames, where an input moved. Reads no database.",
            (forge, args) => SourcePairs(forge, args, Mapping.MappingMethods.Stated)),
        new("stated-laid-pairs", "[<FROM-TO> ...] [--from <Resources>] [--force] [--parallel <n>]",
            "Write each pair's stated-laid file, the stated rows a source lays on the pair's other links (the editions' supplied words, Robinson's apparatus), from both texts' words and the pair's other method files, where an input moved. Reads no database.",
            (forge, args) => SourcePairs(forge, args, Mapping.PairSources.Laid)),
        new("possessive-pairs", "[<FROM-TO> ...] [--from <Resources>] [--force] [--parallel <n>]",
            "Write each Slavic pair's possessive method file from both texts' words and the pair's earlier method files, where an input moved. Reads no database.",
            (forge, args) => SourcePairs(forge, args, Mapping.MappingMethods.Possessive)),
        new("names-pairs", "[<FROM-TO> ...] [--from <Resources>] [--force] [--parallel <n>]",
            "Write each pair's names method file, the translation's names placed on the original's by whom both name, their letters and the text's own spelling, from both texts' words and frames, the original's settled names, the records, the text's names and the pair's earlier method files, where an input moved. Reads no database.",
            (forge, args) => SourcePairs(forge, args, Mapping.MappingMethods.Names)),
        new("merge-sheet", "<FROM-TO> [--from <Resources>] [--status <beginning>] [--against <Resources>] [--count <n>] [--seed <n>] [--out <file>]",
            "Print a sample of a pair's rows the merge did not make claims (withdrawn-by by default), with both verses and the links standing on the same words, every word with its key; --against leaves out rows with the same status in another store. Reads only.",
            MergeSheetVerb, Least: 1),
        new("mapping-diff", "<before Resources> [--from <Resources>] [--pairs A-B,...] [--methods m,...] [--out <folder>] [--examples <n>]",
            "Compare the method files of two Resources folders by key (words, relation, source), per pair, method and source; --out writes up to --examples rows (4) of each kind. Reads only.", MappingDiff, Least: 1),
        new("mapping-load", "[--from <Resources>]",
            "Put every pair's final into the database (never live): link, link_word, link_claim and provenance, by COPY.", MappingLoadVerb),
        new("mapping-compare", "<port/db> <port/db> [--out <folder>] [--pairs A-B,...] [--parallel <n>]",
            "Compare two databases' links and claims by key, grouped by pair and method. Reads only.", MappingCompare, Least: 2),
        new("indexes", "[--texts A,B|all] [--only strong,occurrences,forms,spellings,renderings] [--from <Resources>] [--force] [--parallel <n>]",
            "Make each text's indexes (Strong renderings, occurrences, search forms, spellings, what stands behind each spelling) from its words, its pairs' finals and the names, where an input moved. Reads no database.", IndexesVerb),
        new("strong-numbers", "[--from <Resources>] [--force] [--parallel <n>]",
            "Write each text's texts/<SLUG>/numbers, the one Strong number every word carries for marking (its own, else an original's it renders, else a translation's it renders by the same rule), where an input moved, and build/strong-disagreements. Reads no database.",
            StrongNumbersVerb),
        new("tagnt-editions", "[--from <Resources>] [--force]",
            "Write editions/tagnt: which printed editions STEPBible's TAGNT says hold each word, joined to the words of SBLGNT, WH1881, "
            + "RP2018 and TR1550 by Strong number, where TAGNT or those words moved. Reads no database.", TaggedEditionsVerb),
        new("stored-strong", "[--from <Resources>] [--force] [--parallel <n>]",
            "Write each text's texts/<SLUG>/strong-occurrences (its numbered words at their verses' primary frame places) and texts/<SLUG>/pair-links (the links joining it to each edition that is not a translation), where an input moved. Reads no database.",
            StoredStrongVerb),
        new("indexes-load", "[--texts A,B|all] [--only strong,forms,renderings,spellings] [--from <Resources>]",
            "Put the texts' indexes into the strong, text_form, word_rendering and entity spelling tables of the database (never live).", IndexesLoadVerb),
        new("names-project", "[--texts A,B|all] [--from <Resources>] [--force] [--parallel <n>]",
            "Project the reference texts' settled names onto every text over the final mappings, with the rules and each text's overrides, where an input moved. Reads no database.", NamesVerb),
        new("names-load", "[--texts A,B|all] [--from <Resources>]",
            "Put the names projection into word_entity of the database (never live), then read it back by key against the products.", NamesLoadVerb),
    ];

    public static ForgeVerb? Find(string name) =>
        All.FirstOrDefault(verb => string.Equals(verb.Name, name, StringComparison.Ordinal));

    private static Task<int> Help(ForgeRun forge, string[] args)
    {
        var width = All.Max(verb => verb.Name.Length);
        var help = new StringBuilder();
        foreach (var verb in All)
        {
            help.Append("  ").Append(verb.Name.PadRight(width)).Append("  ").AppendLine(verb.Help);
            if (verb.Arguments.Length > 0)
            {
                help.Append("  ").Append(new string(' ', width)).Append("    ").AppendLine(verb.Usage);
            }
        }

        Console.Write(help);
        return Task.FromResult(0);
    }

    // A text identifier as the corpus spells it, from one typed at a shell in whatever case came to
    // hand. The pipelines compare it to the column, and the workspace they leave in the temp folder is
    // named after it: without this, `align kjv bhsa` finds no text, and on a file system that tells
    // KJV-BHSA from kjv-bhsa the same pair would train a second model beside the first.
    private static string Identifier(string typed) => typed.ToUpperInvariant();

    // --agreeing 3 writes only what the direct model and both middle texts all found.
    private static int Agreeing(string[] args) => Option(args, "--agreeing") is { } agreeing ? int.Parse(agreeing) : 1;

    private static string? Option(string[] args, string name) =>
        Array.IndexOf(args, name) is var at and >= 0 && at + 1 < args.Length ? args[at + 1] : null;

    // The arguments from a position up to the first option.
    private static IEnumerable<string> Positional(string[] arguments, int from) =>
        arguments.Skip(from).TakeWhile(argument => !argument.StartsWith("--", StringComparison.Ordinal));

    private static int? OptionalInt(string[] arguments, string option)
    {
        var index = Array.IndexOf(arguments, option);
        return index >= 0 && index + 1 < arguments.Length ? int.Parse(arguments[index + 1]) : null;
    }

    private static string? OptionalText(string[] arguments, string option)
    {
        var index = Array.IndexOf(arguments, option);
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }

    private static EvidentiaQueueFilter QueueFilter(string[] arguments) => new(
        Tier: OptionalText(arguments, "--tier"),
        Book: OptionalInt(arguments, "--book"),
        Chapter: OptionalInt(arguments, "--chapter"),
        Verse: OptionalInt(arguments, "--verse"),
        Take: OptionalInt(arguments, "--take") ?? EvidentiaReviewQueue.DefaultTake,
        Kinds: OptionalText(arguments, "--kind") is { } kinds
            ? [.. kinds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : null);

    // Contradicted proposals and per-word outcomes go to files rather than to the report, because they
    // are read one at a time and there are thousands of them: a classification pass or a comparison of
    // two runs needs the rows the aggregate counted, and a console report is neither a stable record of
    // them nor wide enough to hold a whole verse.
    private static async Task WriteRows<T>(string[] arguments, string option, IReadOnlyList<T> rows)
    {
        if (OptionalText(arguments, option) is not { } path)
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = false }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // A measurement is only worth the isolation it can state, so every one of these is a way of saying
    // which evidence the run was allowed to see and which answer key it was scored against.
    private static EvidentiaMeasurementOptions EvidentiaOptions(string[] arguments, string resourcesPath) => new(
        AllowSourceStrongEvidence: !arguments.Contains("--without-source-strong"),
        AllowKnownRenderingEvidence: !arguments.Contains("--without-known-renderings"),
        LearnRenderingsFrom: OptionalText(arguments, "--learn-from") is { } learnFrom ? Identifier(learnFrom) : null,
        LearnedRenderingMethods: arguments.Contains("--learn-from-strong-numbers")
            ? [LinkMethod.StatedBySource, LinkMethod.StrongNumber]
            : null,
        GoldSource: OptionalText(arguments, "--gold-source"),
        SampleSize: OptionalInt(arguments, "--sample") ?? 0,
        RecordDisagreements: OptionalText(arguments, "--disagreements") is not null,
        RecordWords: OptionalText(arguments, "--words") is not null,
        RecordKeyDoubts: OptionalText(arguments, "--key-doubts") is not null,
        GoldInterlinear: arguments.Contains("--gold-interlinear") ? resourcesPath : null,
        LearnAcrossLanguages: arguments.Contains("--learn-across-languages"),
        SourceFromFiles: arguments.Contains("--source-from-files"),
        RouteTexts: OptionalText(arguments, "--routes") is { } routes
            ? [.. routes.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Identifier)]
            : null,
        NeighbourVerseDistance: OptionalInt(arguments, "--neighbour-verses") ?? EvidentiaDefaults.NeighbourVerseDistance,
        EntityAnchors: arguments.Contains("--entity-anchors"),
        EntityNamesFrom: OptionalText(arguments, "--entity-names"),
        Learns: OptionalText(arguments, "--confirmed-out") is not null ? new EvidentiaConfirmedRenderings() : null,
        SecondPass: arguments.Contains("--second-pass"),
        ConfirmedByRuns: OptionalText(arguments, "--confirmed-by") is { } confirmedBy
            ? [.. confirmedBy.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(run => int.Parse(run, CultureInfo.InvariantCulture))]
            : null,
        ConfirmedByFiles: OptionalText(arguments, "--confirmed")?.Split(',', StringSplitOptions.RemoveEmptyEntries),
        AlignerLinks: arguments.Contains("--aligner-links"),
        AlignerPairs: AlignerPairFiles(arguments) is { } alignerPairs ? EvidentiaAlignerPairs.Read(alignerPairs) : null,
        AlignerPairFiles: AlignerPairFiles(arguments),
        PivotTexts: CombinerPivots(arguments, OptionalText(arguments, "--pivot") is { } pivot
            ? [.. pivot.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Identifier)]
            : null),
        PivotPolicy: OptionalText(arguments, "--pivot-policy") is { } pivotPolicy
            ? pivotPolicy.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Aggregate(EvidentiaPivotPolicy.None, (policy, flag) => policy | Enum.Parse<EvidentiaPivotPolicy>(flag, ignoreCase: true))
            : EvidentiaPivotPolicy.None,
        Combiner: arguments.Contains("--combiner") ? EvidentiaCombiner.Default : null,
        RecordCombinerEdges: OptionalText(arguments, "--combiner-export") is not null,
        DefiniteNouns: arguments.Contains("--definite-nouns"),
        VerseMap: arguments.Contains("--verse-map"),
        RenderingFamilies: arguments.Contains("--rendering-families"),
        FunctionWords: arguments.Contains("--function-words"),
        WithoutDictionary: arguments.Contains("--without-dictionary"),
        GateTranslatedDictionary: arguments.Contains("--gate-translated-dictionary"));

    private static string[]? AlignerPairFiles(string[] arguments) =>
        OptionalText(arguments, "--aligner-pairs")?.Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// The pivot texts a run reads: the combiner's weights were fitted with its pivots' readings as features, so it
    /// reads them whether or not they were named, and refuses a run that names others in their place.
    /// </summary>
    private static IReadOnlyList<string>? CombinerPivots(string[] arguments, IReadOnlyList<string>? named)
    {
        if (!arguments.Contains("--combiner"))
        {
            return named;
        }

        var wanted = EvidentiaCombiner.Default.Pivots;
        if (named is null)
        {
            return wanted;
        }

        return wanted.All(named.Contains)
            ? named
            : throw new ArgumentException(
                $"The combiner was fitted reading the pivots {string.Join(",", wanted)}; pass them with --pivot, or leave --pivot out.");
    }
}
