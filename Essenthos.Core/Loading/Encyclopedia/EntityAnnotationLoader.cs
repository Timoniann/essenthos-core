using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Resolved">
/// Proper-noun Strong numbers the encyclopedia answers with exactly one person or place, so that
/// the occurrence needs nobody to choose. On the Hebrew side the unit is the number under the kind
/// its words are marked, which is the pair the resolution is asked of.
/// </param>
/// <param name="Contested">
/// Numbers it answers with several. These are left unannotated on purpose and are the work the
/// reading has to do — twenty-three men are called Zechariah and no amount of counting says which
/// of them this verse means.
/// </param>
/// <param name="Unanswered">Numbers it answers with nobody at all.</param>
internal sealed record NameAnswers(int Resolved, int Contested, int Unanswered)
{
    public override string ToString() =>
        $"{Resolved} answer with exactly one, {Contested} with several and {Unanswered} with nobody";
}

/// <param name="Hebrew">What the encyclopedia answers for the names BHSA marks.</param>
/// <param name="Greek">The same for the names the lexicon writes with a capital.</param>
/// <param name="Refused">
/// Of the Greek numbers that answered with exactly one entity, how many no Greek text vouches for —
/// the encyclopedia spells the name in a way no witness and no lexicon writes under that number, so
/// the join is somebody's slip rather than a fact about the text and nothing is written from it.
/// </param>
/// <param name="Derived">
/// Of the annotations, how many rest on a name the corpus worked out rather than one the
/// encyclopedia stated. They are the places the geocoding dataset supplied, which carry no Strong
/// number of their own, and they are counted apart because they are worth less.
/// </param>
/// <param name="Withdrawn">
/// Resolutions taken back because the encyclopedia grew a second place under the name they rest on.
/// Zero on a cold corpus and zero on every boot after the one the second place arrived on.
/// </param>
/// <param name="Written">
/// What this pass added, which is every annotation on a cold corpus and only the records nothing
/// had spoken for on any boot after it.
/// </param>
/// <param name="ByText">What each text ended up with, so the reach is a count rather than a hope.</param>
internal sealed record AnnotationOutcome(
    bool AlreadyLoaded,
    NameAnswers Hebrew,
    NameAnswers Greek,
    int Refused,
    int Withdrawn,
    int Written,
    int Annotated,
    int Corroborated,
    int Derived,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? $"the words are already annotated with the people and places they name, checked in " +
              $"{Elapsed}, with {Withdrawn} resolutions withdrawn"
            : $"{Withdrawn} resolutions withdrawn and {Written} words newly name a person or a " +
              $"place in {Elapsed}, of {Annotated} the corpus " +
              $"now holds: {Corroborated} of them in a verse the encyclopedia independently says that " +
              $"entity is named in, and {Derived} on a name the corpus worked out rather than read. Of " +
              $"the Hebrew numbers {Hebrew}; of the Greek {Greek}, and {Refused} of the resolved ones " +
              "are refused because no Greek text spells the name the way the encyclopedia does. Per " +
              "text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// Says which word names which person or place, for the case that needs nobody's judgement.
///
/// The encyclopedia knows 4,361 people and places and where each is named — but only to the verse.
/// A verse naming four people cannot tell a reader which word is which, so the two halves of this
/// project have never met at the word, and the reader's hover card has had nothing to show.
///
/// <para>
/// **What is annotated is the case where nothing has to be chosen.** BHSA marks a word as a name
/// and says what kind of name it is; the encyclopedia records, for each entity, the Strong number
/// its name is. Where that number names exactly one entity, and the kind BHSA marks is the kind
/// that entity is, the occurrence resolves without anyone weighing anything. Everything else is
/// left null, and that is the answer rather than a gap: a number naming twenty-three Zechariahs is
/// not resolved by taking the most frequent or the nearest, and a number the encyclopedia does not
/// hold is not resolved at all.
/// </para>
///
/// <para>
/// **Who the candidates are is not decided here.** <see cref="EntityCandidates"/> states that once,
/// for this loader and for the reading harness alike, and it is worth reading before this file: it
/// is where a title's Strong numbers are refused as names, where a person who appears only in Greek
/// is refused as the referent of a Masoretic word, and where the geocoding dataset's places are
/// given the Hebrew names that make them reachable at all.
/// </para>
///
/// <para>
/// **The one thing decided here** is the kind. BHSA's name type is a property of the lemma rather
/// than of the occurrence: all 2,467 occurrences of Israel are marked <c>pers,gens,topo</c>, which
/// says the name can be a person, a people or a place and never that it is one here. So an
/// occurrence is taken only where BHSA commits to a single kind, and the resolution is asked of
/// that kind — which is the discipline that keeps the land of Canaan from being annotated as the
/// person Canaan, and the same question that lets the man Jephthah and the town named after him
/// each answer for the words marked as them.
/// </para>
///
/// <para>
/// **The Greek has no such marking, so its gate is built rather than read.** Nestle's morphology
/// says <c>noun</c> 28,394 times and never <em>proper noun</em>, and the lexicon has no
/// proper-noun flag for a Greek entry either. What it does have is a capital letter: 587 of its
/// 5,523 Greek lemmas are written with one, and that is what a lexicographer writing Παῦλος rather
/// than παῦλος is saying. Three things were checked before it was trusted. Robinson's parsing of
/// the Byzantine text tags 177 numbers as proper nouns independently, and the capital agrees with
/// 175 of them — the two it misses are <em>Gabbatha</em> and <em>cherubim</em>, transliterated
/// once each and nobody's name. Nestle's own lemmatisation is a second independent capitalisation
/// and the two agree on 5,318 of 5,330 numbers, the twelve disagreements being titles and
/// loanwords totalling thirty words. And against the encyclopedia's own list of the verses each
/// entity is named in, the capitalised numbers land where it says the entity is 93.2% of the time
/// and the uncapitalised ones 22.2% — because the uncapitalised ones are ἔρχομαι offered as Jesus,
/// ἡμέρα as Jemimah, ζωή as Eve and μέν as Menna, 2,622 words of the New Testament that would
/// otherwise have been annotated with a person.
/// </para>
///
/// <para>
/// **And the Greek join is checked against the text before it is used.** The encyclopedia's Greek
/// Strong numbers are less careful than its Hebrew ones: it gives Ἰωδά the number of Ἰούδας, which
/// resolves to exactly one entity and would have put a walk-on of Luke's genealogy on all 151
/// occurrences of Judas in four Greek texts. So a number is taken only where the encyclopedia's own
/// spelling of the name is one the Greek actually writes under it — the lexicon's lemma, a witness's
/// lemma, or a form some witness prints. That refuses two numbers of 269: Ἰωδά, and Philemon, whose
/// Greek column holds the transliteration rather than the Greek.
/// </para>
///
/// <para>
/// **Where either gate is doing more than agreeing, the row says so.** Most numbers name one record
/// of the encyclopedia and the gate only confirms it, and those annotations needed nobody. Some are
/// borne by two — H3778 is Chaldea and the Chaldeans both, and a Greek number is as readily an Old
/// Testament man's as a New Testament one's — and there the gate is not confirming an answer but
/// picking one: on the Hebrew side by the word's own form, on the Greek by that and by where the
/// encyclopedia attests each rival. That is a different claim and it is written as a different
/// method, so a reader can tell the occurrences nothing had to decide from the ones a lexical
/// analysis decided. The question is asked under whichever column the word's number lives in,
/// which is the same question the corpus check asks of what was written.
/// </para>
///
/// <para>
/// **The annotation then travels on the links that already exist.** A King James word linked to an
/// annotated Hebrew word names what that Hebrew word names, and the confidence of the link is
/// carried into the confidence of the annotation, so a word reached by a source's own mapping is
/// not stored looking like one an aligner guessed at. One hop only, and always from a witness: a
/// second hop through another translation would be an inference about an inference, and the reader
/// would have no way to see that it was.
/// </para>
///
/// <para>
/// **A faint link is refused where the name is already rendered.** An aligner that cannot find a
/// home for a translated word does not say so — it attaches the word to whatever it can score, and
/// in a verse that names somebody that is often the name. So the Ukrainian <em>зійшов</em>, which
/// renders <em>went up</em>, was reached from Abinoam at 0.53 and underlined as the man, in a verse
/// that had already said Abinoam confidently one word earlier. That earlier word is the test: where
/// the name already reaches this verse of this text by a firm link, a faint one is the aligner's
/// leftover and is dropped. Where it does not, the faint link is the only account the corpus has
/// and it is kept at what it is worth — <em>Шевна</em> at 0.69 is Shebnah, and a floor low enough
/// to catch the leftovers would have taken him too.
/// </para>
///
/// <para>
/// **What it does on a boot is decided per record rather than per pass.** It sits in the start-up
/// pipeline and asks, of every record, whether anything it wrote already names that record's words;
/// the ones nothing has spoken for are the work, and on a corpus where the rest is already there
/// nothing is written. Asking instead whether the pass had ever run is the same question only while
/// no record can arrive after it — and the peoples, the records this corpus writes for itself and
/// the place register all add entities, so on the corpus as it stands that question leaves every
/// later record with no words at all.
/// </para>
///
/// <para>
/// The cost of that on a corpus that is already complete is the candidate list, the seeds and the
/// counts, and it is seconds rather than the minutes the carrying step takes when there is
/// something to carry: the carrying reads only what the seeds put in the workspace, and on such a
/// corpus the seeds put nothing there.
/// </para>
/// </summary>
internal sealed class EntityAnnotationLoader(AppDbContext db, ILogger<EntityAnnotationLoader> logger)
{
    private const string Witness = EntityCandidates.Witness;

    private const string Rendering = EntityCandidates.Rendering;

    /// <summary>The language the Greek spellings are folded as, which is what the fold keys on.</summary>
    private const string Greek = "grc";

    /// <summary>
    /// How sure the corpus is that an occurrence of a name names the one entity that bears it.
    ///
    /// It is short of certainty for one reason, and the reason is not the resolution: the
    /// encyclopedia is 3,010 people and 1,351 places and the Bible names more than that, so a number
    /// answering with exactly one entity today can answer with two once somebody is added. Nothing
    /// in the data contradicts the annotation — this is the room left for what the data does not
    /// yet hold.
    /// </summary>
    private const double NameResolution = 0.9;

    /// <summary>
    /// The same for a Greek name, and lower, because two of its three supports are weaker.
    ///
    /// It carries the same room for an encyclopedia that will grow. On top of that the proper-noun
    /// class is the lexicon's capital letter rather than the witness's own marking — measured, and
    /// the measurements are in the class comment, but measured against two sources rather than
    /// stated by the text being read. And the encyclopedia's Greek numbers have been caught being
    /// wrong in a way its Hebrew ones have not: one is another name's number outright, and
    /// thirty-four rows carry an extended numbering that is not Strong's, whose homograph letter is
    /// dropped on the way in and lands them on a different lemma.
    /// </summary>
    private const double GreekNameResolution = 0.85;

    /// <summary>
    /// The same, where the encyclopedia's own list of verses says this entity is named in this
    /// verse. That list is compiled from the datasets' reading of the text and not from Strong
    /// numbers, so it is a second and independent answer to the same question, and where the two
    /// agree the only thing left open is the one above. Measured over the Hebrew, they agree on
    /// 13,587 of 14,138 occurrences; over the Greek, on 5,458 of 5,722. Almost every disagreement
    /// is the list being silent about a verse rather than naming somebody else.
    /// </summary>
    private const double Corroborated = 0.99;

    /// <summary>
    /// The same, where the name itself is the corpus's own conclusion rather than the
    /// encyclopedia's statement — the places the geocoding dataset supplied, which carry no Strong
    /// number and are reached by reading the King James rendering back onto the Hebrew word.
    ///
    /// Lower than a stated name, and deliberately so. It carries the same room for an encyclopedia
    /// that will grow, plus the derivation's own risk: on the hundred-odd places the other dataset
    /// had already joined by hand it has yet to be caught naming the wrong one, but a hundred is
    /// what could be checked out of six hundred and fifty, and a number that hid that would be
    /// claiming more than was measured.
    /// </summary>
    private const double DerivedName = 0.8;

    private const string Resolution =
        "BHSA's proper-noun marking, and the Strong number the encyclopedia records for the name";

    private const string GreekResolution =
        "the lexicon's capitalised lemma, the Greek Strong number the encyclopedia records for the " +
        "name, and the Greek text spelling that name the way the encyclopedia spells it";

    /// <summary>
    /// The same where the number is borne by more than one record, which on the Greek side is not
    /// the same statement.
    ///
    /// The Hebrew names its chooser already — the marking is half of what the resolution rests on,
    /// and the marking is what rules the rival out. Here the gate is the lexicon's and the rival is
    /// ruled out by reachability: every one of the forty-six is an Old Testament namesake the
    /// encyclopedia attests in no book any Greek witness holds. Leaving that unsaid would credit
    /// the answer to the spelling, which did not make it.
    /// </summary>
    private const string GreekDistinction =
        GreekResolution + ", and, the number being borne by several records, the others being " +
        "named in no book the Greek holds";

    /// <summary>
    /// The method for an occurrence whose number is borne by more than one record, where the gate
    /// is therefore not agreeing with the only answer but choosing among several.
    ///
    /// <see cref="LinkMethod.StrongNumber"/> means that nothing had to be chosen, and saying it
    /// where something was is a claim of the wrong kind however right the answer: the source string
    /// on the row already names the marking, or the lexicon's lemma, as half of what established
    /// it, and the method contradicted it. <see cref="LinkMethod.Lexical"/> is the method for a
    /// conclusion the word's form reached, which is what both gates are, and it is what the reader
    /// is shown as <em>by the form of the word</em>.
    ///
    /// <para>
    /// It stands below a reading of the verse rather than above one, and that is the point rather
    /// than a cost. A marking says which kind of thing the lexeme names; somebody who read the
    /// sentence knows more than that, and where the two disagree the sentence should win.
    /// </para>
    /// </summary>
    private const LinkMethod ByTheForm = LinkMethod.Lexical;

    private const string Derivation =
        "BHSA's proper-noun marking, and the Hebrew name read off the King James word that renders " +
        "it in a verse the geocoding dataset says the place is named in";

    private const string VerseList =
        "the encyclopedia's own list of the verses each entity is named in";

    /// <summary>
    /// Long enough for a pass over four and a half million words and their links, which is what the
    /// carrying step is. The default thirty seconds is what a start-up pass gets on the day the
    /// counts are cold, and a start-up pass that throws does not fail its own step — it fails every
    /// step after it.
    /// </summary>
    private const int Patient = 1800;

    /// <summary>What a language answers when nothing of it is loaded, or nothing was asked.</summary>
    private static readonly NameAnswers Nothing = new(0, 0, 0);

    /// <summary>
    /// Where a verse list is this corpus's own. Every one of them is written under a source that
    /// begins with the project's name, and every dataset's list under the dataset's.
    /// </summary>
    private const string Ours = "Essenthos%";

    /// <summary>
    /// The word Chronicles names a town by: <em>Raham the father of Jorkeam</em>, <em>Maon the
    /// father of Bethzur</em>. H1 standing immediately before a name is the founder formula, and
    /// what follows it is the place founded and not a second man.
    /// </summary>
    private const string FatherOf = "H1";

    /// <summary>
    /// The places in a clause where a name is what the clause is about — its subject, its object,
    /// or what its subject is said to be. Everything else is circumstance: <em>the field of
    /// Aram</em> that Jacob fled to is where he went, not somebody the verse names.
    /// </summary>
    private const string Nominated = "'Subj', 'Objc', 'PreC'";

    /// <summary>
    /// Whether the verse itself says a word BHSA marks a place is a person.
    ///
    /// <para>
    /// BHSA's marking belongs to the lexeme, so H4031 Magog is <c>topo</c> on every occurrence
    /// because Ezekiel's Magog is a land — including in the table of the sons of Japheth, where it
    /// is a man. Reading it as the place there is not a judgement the corpus made; it is a
    /// judgement it was never given the chance to make, because the marking had settled the kind
    /// before the resolution was asked.
    /// </para>
    ///
    /// <para>
    /// <strong>The verse is what tells them apart</strong>, and it is the same statement the Greek
    /// namesakes are told apart by: the encyclopedia records the verses each record is named in, a
    /// dataset's lists and never this corpus's, and where those name exactly one person bearing the
    /// word's number and no place bearing it, they contradict the lexeme and the occurrence is the
    /// person. A title filed under a person is not a bearer — Rezin is <em>king of Aram</em> and
    /// the encyclopedia files the phrase's numbers under him, which would otherwise put Rezin on
    /// every אֲרָם of Isaiah 7.
    /// </para>
    ///
    /// <para>
    /// <strong>Two shapes are refused because a genealogy names towns in them.</strong> A name after
    /// <see cref="FatherOf"/> is the town its founder is called the father of, which is how
    /// Chronicles writes Keilah, Mareshah, Lecah and Bethzur; and a name that is a circumstance of
    /// its clause rather than <see cref="Nominated"/> is where the verse went or whom it went with,
    /// which is Hosea's field of Aram and the Argob of 2 Kings 15:25.
    /// </para>
    /// </summary>
    private static readonly string ReadAsAPerson =
        $"""
         EXISTS (
             SELECT 1 FROM verse_reference r
             WHERE r.verse_id = w.verse_id AND r.is_primary
               AND (SELECT count(DISTINCT {EntityCandidates.Resolves})
                    FROM entity_name n
                    JOIN entity bearer ON bearer.id = {EntityCandidates.Resolves}
                         AND bearer.kind = 'person'
                    JOIN entity_verse ev ON ev.entity_id = bearer.id
                         AND ev.canonical_book = r.canonical_book
                         AND ev.canonical_chapter = r.canonical_chapter
                         AND ev.canonical_verse = r.canonical_verse
                         AND ev.source NOT LIKE '{Ours}'
                    WHERE n.hebrew_strong_number = w.strong_number
                      AND coalesce(n.kind, '') NOT IN ('title', 'description')) = 1
               AND NOT EXISTS (
                    SELECT 1
                    FROM entity_name n
                    JOIN entity bearer ON bearer.id = {EntityCandidates.Resolves}
                         AND bearer.kind = 'place'
                    JOIN entity_verse ev ON ev.entity_id = bearer.id
                         AND ev.canonical_book = r.canonical_book
                         AND ev.canonical_chapter = r.canonical_chapter
                         AND ev.canonical_verse = r.canonical_verse
                         AND ev.source NOT LIKE '{Ours}'
                    WHERE n.hebrew_strong_number = w.strong_number
                      AND coalesce(n.kind, '') NOT IN ('title', 'description')))
         AND NOT EXISTS (
             SELECT 1 FROM word before
             WHERE before.verse_id = w.verse_id AND before.position = w.position - 1
               AND before.strong_number = '{FatherOf}')
         AND EXISTS (
             SELECT 1 FROM word_group g
             JOIN word_group_word gw ON gw.word_group_id = g.id AND gw.word_id = w.id
             WHERE g.kind = 'phrase' AND g.features->>'function' IN ({Nominated}))
         """;

    /// <summary>
    /// The kind of record BHSA's marking commits a word to, and nothing where it commits to none.
    ///
    /// The marking belongs to the lemma rather than to the occurrence, so a lexeme that can be a
    /// person, a people or a place carries all three at once and settles nothing: only a marking
    /// naming one kind is read, and every occurrence of Israel is left out by it.
    ///
    /// <para>
    /// A word it marks a place is asked of the verse before the kind is taken —
    /// <see cref="ReadAsAPerson"/> — because that is the one case where the lexeme and the
    /// occurrence are known to disagree and something else can say so. The test is written inside
    /// the <c>topo</c> arm rather than beside it so that it is asked of six thousand words and not
    /// of four hundred thousand.
    /// </para>
    /// </summary>
    private static readonly string Marked =
        $"""
         CASE w.morphology->>'nameType'
              WHEN 'pers' THEN 'person'
              WHEN 'topo' THEN CASE WHEN {ReadAsAPerson} THEN 'person' ELSE 'place' END
         END
         """;

    /// <summary>
    /// The numbers that name exactly one entity of a kind, which are the only ones annotated.
    ///
    /// <para>
    /// Grouped by the kind and not by the number alone, because Strong heads one entry for a man
    /// and for the town named after him — Jephthah, Cain, Ephron, Terah — and a place is never an
    /// aspect of a person, so nothing folds those into one record and nothing should. Asked of the
    /// number alone such a name answers with two and is refused, and <see cref="Marked"/>, which
    /// says outright which of the two this word is, never gets to speak: the kind was tested in
    /// <see cref="Seed"/>'s <c>WHERE</c>, where it could only ever remove an occurrence the
    /// resolution had already allowed. As the grouping it is the join, which is where it was doing
    /// the work all along.
    /// </para>
    ///
    /// <para>
    /// It can only add, and that is a property of the grouping rather than a hope: a number naming
    /// one record answers the same either way, and a number naming several of one kind is still
    /// refused for that kind. What it reaches is the number whose rivals are all of the other kind
    /// — 597 occurrences of Egypt, which the encyclopedia holds as the land and as Mizraim son of
    /// Ham alike, and 1,400 words over 102 numbers.
    /// </para>
    ///
    /// <para>
    /// The Greek has no counterpart and must not be given one. Nothing in it marks a name at all,
    /// and its rival is ruled out by reachability instead.
    /// </para>
    /// </summary>
    private static readonly string Resolvable =
        $"""
         SELECT named.number, e.kind, min(named.entity_id) AS entity_id,
                bool_and(named.stated) AS stated
         FROM ({EntityCandidates.Naming}) named
         JOIN entity e ON e.id = named.entity_id
         GROUP BY 1, 2 HAVING count(*) = 1
         """;

    /// <summary>
    /// A word of a Greek witness that is a noun, in whichever dialect of morphology its text
    /// carries: Nestle's own part of speech, or Robinson's tag, whose first field is the class.
    ///
    /// It is the occurrence-level half of the gate, and what it keeps out is the gentilic — 197
    /// occurrences of Ἰουδαῖος, <em>Jewish</em>, and thirty other adjectives and adverbs formed
    /// from a name. Being Roman is not being Rome.
    /// </summary>
    internal const string GreekNoun =
        "(w.morphology->>'pos' = 'noun' OR w.morphology->>'robinson' LIKE 'N-%')";

    /// <summary>
    /// The Greek numbers the lexicon writes as a name and the encyclopedia answers with exactly one
    /// entity. Takes <c>@witnesses</c>.
    /// </summary>
    private static readonly string GreekResolvable =
        $"""
         SELECT number, min(entity_id) AS entity_id
         FROM ({EntityCandidates.GreekNaming}) named
         WHERE EXISTS (
             SELECT 1 FROM strong_entry lexicon
             WHERE lexicon.strong_number = named.number
               AND lower(left(lexicon.lemma, 1)) <> left(lexicon.lemma, 1))
         GROUP BY 1 HAVING count(*) = 1
         """;

    /// <summary>
    /// What the encyclopedia calls each of those names, beside what the lexicon calls it. One row
    /// per spelling the encyclopedia offers, so a name it records twice is checked twice.
    /// </summary>
    private static readonly string Claimed =
        $"""
         SELECT DISTINCT resolved.number, n.greek, lexicon.lemma
         FROM ({GreekResolvable}) resolved
         JOIN entity_name n ON n.entity_id = resolved.entity_id
              AND n.greek_strong_number = resolved.number AND n.greek IS NOT NULL
         JOIN strong_entry lexicon ON lexicon.strong_number = resolved.number
         """;

    /// <summary>
    /// Every spelling the Greek texts themselves write under one of those numbers: the form as
    /// printed and the lemma the edition gives it. Both are needed. A name the New Testament never
    /// puts in the nominative — Ἰορδάνης, Δαμασκός, Ζεβεδαῖος — is printed only in oblique cases
    /// and would be unrecognisable against the encyclopedia's citation form; a name whose editions
    /// spell it differently — Μαθθίας against Ματθίας, Σάπφιρα against Σαπφείρη — matches the lemma
    /// of one edition where it matches neither the lexicon nor the other.
    /// </summary>
    private static readonly string Printed =
        $"""
         SELECT DISTINCT w.strong_number, w.normalised_text, w.lemma
         FROM word w
         JOIN text t ON t.id = w.text_id AND t.slug = ANY(@witnesses)
         JOIN ({GreekResolvable}) resolved ON resolved.number = w.strong_number
         """;

    /// <summary>
    /// Whether the number the word carries is borne by more than one record of the encyclopedia, so
    /// that the occurrence resolved by ruling a rival out rather than by agreeing with the only
    /// answer there was.
    ///
    /// <para>
    /// It asks of the whole encyclopedia and not of the candidate list, which is deliberate: the
    /// Hebrew list holds people and places because those are the only things a word marked
    /// <c>pers</c> or <c>topo</c> can be, the Greek list holds only what the Greek witnesses reach,
    /// and it is those very exclusions that this is recording. H3778 is Chaldea to the encyclopedia
    /// and the Chaldeans as well, and the fifteen occurrences of the land are the land because BHSA
    /// analyses them as a singular toponym rather than as the plural gentilic it keeps as a
    /// separate lexeme. Ἐλεάζαρ is Matthew's Eleazar and nine men of the Old Testament, and the
    /// occurrence is Matthew's because the other nine are named nowhere the Greek reaches.
    /// </para>
    ///
    /// <para>
    /// Asked of both columns at once, because that is the question the corpus check asks of what
    /// was written and the two have to be the same question. The columns never hold each other's
    /// prefix, so one comparison against either is exact; a loader asking only the Hebrew one would
    /// leave the Greek half writing on 46 numbers the very claim the check exists to refuse.
    /// </para>
    ///
    /// <para>
    /// It counts places and not rows. Zion and Mount Zion are two records of one place, and a row
    /// that says so — <see cref="EntityCandidates.Resolves"/> — is not a rival to be ruled out but
    /// the same answer written twice.
    /// </para>
    /// </summary>
    internal static readonly string Distinguished =
        $"""
         (SELECT count(DISTINCT {EntityCandidates.Resolves}) FROM entity_name n
          WHERE n.hebrew_strong_number = w.strong_number
             OR n.greek_strong_number = w.strong_number) > 1
         """;

    /// <summary>
    /// Whether this word does not already name this record. It is the unit of work, and it is the
    /// pair because the pair is what gets written: a pass that asked only whether it had ever run
    /// would leave every record added after it — the peoples, the records this corpus writes for
    /// itself, the place register — with no words and no verses.
    ///
    /// <para>
    /// The pair and not the record. Asked of the record it means <em>nothing this loader wrote
    /// names it</em>, which is coarser than what is being decided and goes wrong the moment the
    /// resolution itself changes: asking it per kind newly resolves 597 occurrences of Egypt onto a
    /// record the loader had already spoken for by another number, so a warm corpus would take 587
    /// of the 1,400 and a cold load all of them, and the two states would disagree about what the
    /// corpus says. Per pair they agree, which is the property PRB-0468 was about.
    /// </para>
    ///
    /// <para>
    /// A word another source has already annotated with this record is left alone rather than
    /// seeded and discarded at the insert: seeding it would carry it across the links a second time
    /// and claim it a second time, both of which are work and one of which is visible.
    /// </para>
    ///
    /// <para>
    /// It is asked after the resolution and never before it. Whether a number names exactly one
    /// record is a question about the whole encyclopedia, and a candidate list narrowed to what is
    /// still waiting would answer it with one where the answer is two — annotating a new place with
    /// a number an older record already bears, which is the very case the resolution exists to
    /// refuse.
    /// </para>
    /// </summary>
    private const string Unspoken =
        """
        NOT EXISTS (SELECT 1 FROM word_entity spoken
                    WHERE spoken.word_id = w.id AND spoken.entity_id = resolved.entity_id)
        """;

    /// <summary>
    /// Where the annotation is assembled before anything is written.
    ///
    /// <c>resolution</c> is what the name is worth before the verse list and the links are taken
    /// into account, <c>source</c> is what established it in the words the row will carry, and
    /// <c>distinguished</c> is whether anything had to be chosen. All three are on the row rather
    /// than derived from it, because a Hebrew name, a Greek one and a place the corpus worked out
    /// are three different assertions and the step that carries them onto a translation does not
    /// know which it is carrying.
    ///
    /// <para>
    /// Both drop themselves at commit. A temporary table outlives its transaction and belongs to
    /// the connection, and connections here are pooled, so without this a second load in one
    /// process fails on <em>relation already exists</em> — a start-up crash a long way from its
    /// cause.
    /// </para>
    /// </summary>
    private const string Workspace =
        """
        CREATE TEMP TABLE annotation (
            word_id bigint PRIMARY KEY,
            entity_id integer NOT NULL,
            carried double precision NOT NULL,
            corroborated boolean NOT NULL,
            stated boolean NOT NULL,
            distinguished boolean NOT NULL,
            resolution double precision NOT NULL,
            source text NOT NULL,
            note text NOT NULL)
        ON COMMIT DROP;
        CREATE TEMP TABLE attested (number text PRIMARY KEY) ON COMMIT DROP
        """;

    /// <summary>
    /// The Hebrew occurrences that resolve without anyone choosing. <c>carried</c> is 1 because
    /// nothing was crossed to reach them; the words of other texts divide it by what their link is
    /// worth.
    ///
    /// <para>
    /// The marking is the join rather than a filter over it. A word BHSA does not commit on matches
    /// no kind and is left alone, which it was before; a word it does commit on asks the resolution
    /// of that kind, which is what lets the man Jephthah and the town of Joshua 15:43 both answer.
    /// </para>
    ///
    /// <para>
    /// Where the number is several records' it is that marking which rules the rivals out, and
    /// <see cref="Distinguished"/> is what records that something was ruled out at all.
    /// </para>
    /// </summary>
    private static readonly string Seed =
        $"""
         INSERT INTO annotation
             (word_id, entity_id, carried, corroborated, stated, distinguished, resolution, source,
              note)
         SELECT w.id, resolved.entity_id, 1.0, agreed.named, resolved.stated,
                {Distinguished},
                CASE WHEN resolved.stated THEN @resolution ELSE @derived END,
                CASE WHEN resolved.stated THEN @source ELSE @derivation END,
                w.strong_number || ', which BHSA marks ' || (w.morphology->>'nameType')
         FROM word w
         JOIN text t ON t.id = w.text_id AND t.slug = @witness
         JOIN ({Resolvable}) resolved
              ON resolved.number = w.strong_number AND resolved.kind = {Marked}
         CROSS JOIN LATERAL (SELECT EXISTS (
             SELECT 1 FROM verse_reference r
             JOIN entity_verse ev ON ev.entity_id = resolved.entity_id
                  AND ev.canonical_book = r.canonical_book
                  AND ev.canonical_chapter = r.canonical_chapter
                  AND ev.canonical_verse = r.canonical_verse
             WHERE r.verse_id = w.verse_id AND r.is_primary) AS named) agreed
         WHERE {Unspoken}
         """;

    /// <summary>
    /// The same for the Greek, read from each of the four witnesses rather than carried into three
    /// of them, because every one of them states the number this rests on.
    ///
    /// There is no kind test to make. The Hebrew needs one because BHSA's marking says what a name
    /// <em>can</em> be and the entity says what it is; here the entity is the only answer either
    /// side gives, and a number that answers with one entity has nothing to disagree with.
    ///
    /// <para>
    /// <see cref="Distinguished"/> is asked here for the same reason it is asked of the Hebrew and
    /// with more to catch: the encyclopedia bears a Greek number on an Old Testament namesake as
    /// readily as it bears a Hebrew one on a New Testament man, so on 46 of the 273 numbers that
    /// resolve, something was ruled out. What ruled it out is reachability rather than the
    /// spelling, and <see cref="GreekDistinction"/> is the source that says so.
    /// </para>
    /// </summary>
    private static readonly string GreekSeed =
        $"""
         INSERT INTO annotation
             (word_id, entity_id, carried, corroborated, stated, distinguished, resolution, source,
              note)
         SELECT w.id, resolved.entity_id, 1.0, agreed.named, true, rivals.several, @resolution,
                CASE WHEN rivals.several THEN @distinction ELSE @source END,
                w.strong_number || ', which the lexicon writes as the name ' || lexicon.lemma
         FROM word w
         JOIN text t ON t.id = w.text_id AND t.slug = ANY(@witnesses)
         JOIN ({GreekResolvable}) resolved ON resolved.number = w.strong_number
         JOIN attested ON attested.number = resolved.number
         JOIN strong_entry lexicon ON lexicon.strong_number = w.strong_number
         CROSS JOIN LATERAL (SELECT {Distinguished} AS several) rivals
         CROSS JOIN LATERAL (SELECT EXISTS (
             SELECT 1 FROM verse_reference r
             JOIN entity_verse ev ON ev.entity_id = resolved.entity_id
                  AND ev.canonical_book = r.canonical_book
                  AND ev.canonical_chapter = r.canonical_chapter
                  AND ev.canonical_verse = r.canonical_verse
             WHERE r.verse_id = w.verse_id AND r.is_primary) AS named) agreed
         WHERE {GreekNoun} AND {Unspoken}
         ON CONFLICT (word_id) DO NOTHING
         """;

    /// <summary>
    /// The same annotations on the word each link says stands for one of those witness words —
    /// its head, by <see cref="Annotating.Head"/>, and not every word of the set the link names.
    ///
    /// A word reached from two witness words that name two different entities is left alone: the
    /// links disagree about who is named and picking between them is the thing this loader does not
    /// do. Where they agree, the strongest link decides the confidence, because being reached twice
    /// is not weaker than being reached once.
    ///
    /// <para>
    /// <c>rendered</c> asks of each witness name, and of each verse it reaches, how well that verse
    /// renders it at best; <c>supported</c> then drops the reaches that are faint in a verse where
    /// the name is already rendered firmly. The comparison is per verse rather than per text on
    /// purpose: a link may land in a verse the translation divided differently, and a firm
    /// rendering three verses away is no reason to take away the only account this verse has.
    /// </para>
    ///
    /// <para>
    /// It happens before unanimity rather than after, so that a leftover cannot veto a good reading
    /// by disagreeing with it.
    /// </para>
    ///
    /// <para>
    /// A reached word is <see cref="Distinguished"/> if the witness word was, and also if its own
    /// name is several records' — which is not the same question. The editions differ, and where
    /// they do the reached word is not the word that resolved: Scrivener prints Ἰωσῆς, which one
    /// man bears, at four places Nestle prints Ἰωσήφ or Ἰησοῦς, which many do. Reading the Nestle
    /// word as that man is a conclusion about a variant, and calling it a resolution that needed
    /// nobody would say of the word on the page something true only of the word beside it.
    /// </para>
    /// </summary>
    private static readonly string Carry =
        $"""
        WITH reached AS (
            SELECT other.word_id,
                   w.text_id,
                   w.verse_id,
                   seed.entity_id,
                   crossed.worth AS carried,
                   seed.stated,
                   seed.distinguished,
                   seed.resolution,
                   seed.source,
                   l.method,
                   seed.word_id AS through,
                   witness.slug AS spoken_by
            FROM annotation seed
            JOIN word origin ON origin.id = seed.word_id
            JOIN text witness ON witness.id = origin.text_id
            JOIN link_word mine ON mine.word_id = seed.word_id
            JOIN link l ON l.id = mine.link_id
            CROSS JOIN LATERAL (SELECT {Annotating.LinkWorth} AS worth) crossed
            CROSS JOIN LATERAL ({Annotating.Head}) other
            JOIN word w ON w.id = other.word_id
        ),
        rendered AS (
            SELECT through, text_id, verse_id, max(carried) AS best
            FROM reached GROUP BY 1, 2, 3
        ),
        supported AS (
            SELECT r.*
            FROM reached r
            JOIN rendered d ON d.through = r.through
                 AND d.text_id = r.text_id AND d.verse_id = r.verse_id
            WHERE r.carried >= @faint OR d.best < @firm
        ),
        unanimous AS (
            SELECT word_id FROM supported GROUP BY 1 HAVING count(DISTINCT entity_id) = 1
        ),
        strongest AS (
            SELECT DISTINCT ON (r.word_id) r.*
            FROM supported r JOIN unanimous u ON u.word_id = r.word_id
            ORDER BY r.word_id, r.carried DESC, r.through
        )
        INSERT INTO annotation
            (word_id, entity_id, carried, corroborated, stated, distinguished, resolution, source,
             note)
        SELECT s.word_id, s.entity_id, s.carried, agreed.named, s.stated,
               s.distinguished OR {Distinguished},
               s.resolution, s.source,
               'through ' || s.spoken_by || ' word ' || s.through || ', linked by ' || s.method
        FROM strongest s
        JOIN word w ON w.id = s.word_id
        CROSS JOIN LATERAL (SELECT EXISTS (
            SELECT 1 FROM verse_reference r
            JOIN entity_verse ev ON ev.entity_id = s.entity_id
                 AND ev.canonical_book = r.canonical_book
                 AND ev.canonical_chapter = r.canonical_chapter
                 AND ev.canonical_verse = r.canonical_verse
            WHERE r.verse_id = w.verse_id AND r.is_primary) AS named) agreed
        ON CONFLICT (word_id) DO NOTHING
        """;

    /// <summary>
    /// What this loader wrote and would not write today, taken back: a number the encyclopedia no
    /// longer answers with one place, and a place the word's own verse contradicts.
    ///
    /// An annotation is written when the resolution needs nobody, and what makes that true is the
    /// encyclopedia at the moment it is asked. The encyclopedia grows: the peoples made H3778 two
    /// records' (PRB-0340) and the place register made H3405 two towns'. Where what is added is the
    /// same place under another name the row now says so and nothing changes, but where it is a
    /// second Jericho four kilometres from the first, the words already annotated go on asserting a
    /// certainty nothing supports — and a cold load of the same corpus writes nothing for them, so
    /// the two states disagree about what the corpus says.
    ///
    /// <para>
    /// So the resolutions that would not be written today are withdrawn. Only this loader's own
    /// rows, and only those whose method is the one that means nothing had to be chosen: a
    /// <see cref="ByTheForm"/> row is a resolution that named its chooser and stands, and a reading
    /// or a person's ruling was never this pass's to remove. The words are then unannotated, which
    /// is the right answer for a name two places bear — the page says nothing rather than something
    /// wrong, and which Jericho a verse means is the namesake pass's work.
    /// </para>
    ///
    /// <para>
    /// The words carried into the translations go with them. They are the same claim moved one hop
    /// along a link and they carry the same source, so leaving them would keep the King James
    /// saying what the Hebrew beside it no longer says. They are found the way they were made, from
    /// the seed through <see cref="Annotating.Head"/>, rather than by reading them back out of a
    /// note.
    /// </para>
    ///
    /// <para>
    /// The second arm is <see cref="ReadAsAPerson"/>, and it is here rather than left to the next
    /// cold load for the same reason as the first: the place was written because BHSA's marking
    /// settled the kind before the verse was asked, and a warm corpus that only stopped writing it
    /// would keep every one already there. It is not restricted by method — the marking is what
    /// made the row whatever method it carries, and the marking is what the verse overrules.
    /// </para>
    ///
    /// <para>
    /// One statement, inside the pass's own transaction. <c>ExecuteDelete</c> commits on its own
    /// and would leave the corpus half-withdrawn if anything after it failed (MST-0184).
    /// </para>
    /// </summary>
    private static readonly string Withdraw =
        $"""
         WITH seed AS (
             SELECT a.word_id, a.entity_id
             FROM word_entity a
             JOIN word w ON w.id = a.word_id
             WHERE a.source = ANY(@written)
               AND a.method = @method
               AND w.strong_number IS NOT NULL
               AND {Distinguished}
             UNION
             SELECT a.word_id, a.entity_id
             FROM word_entity a
             JOIN word w ON w.id = a.word_id
             JOIN text t ON t.id = w.text_id AND t.slug = @witness
             JOIN entity named ON named.id = a.entity_id AND named.kind = 'place'
             WHERE a.source = ANY(@written)
               AND w.morphology->>'nameType' = 'topo'
               AND w.strong_number IS NOT NULL
               AND {ReadAsAPerson}
         ),
         carried AS (
             SELECT other.word_id, seed.entity_id
             FROM seed
             JOIN link_word mine ON mine.word_id = seed.word_id
             CROSS JOIN LATERAL ({Annotating.Head}) other
         ),
         gone AS (
             SELECT word_id, entity_id FROM seed
             UNION
             SELECT word_id, entity_id FROM carried
         )
         DELETE FROM word_entity a
         USING gone
         WHERE a.word_id = gone.word_id AND a.entity_id = gone.entity_id
           AND a.source = ANY(@written)
         """;

    /// <summary>
    /// The conclusion, with the method the row actually earned.
    ///
    /// <c>@method</c> is the resolution that needed nobody, and it is the honest answer for the
    /// overwhelming majority: the number named one record in the whole encyclopedia and the gate
    /// only agreed with it. <c>@form</c> is for the rest, where the number is several records' and
    /// the gate is what chose between them — the annotation is then as good as BHSA's analysis of
    /// that word, or as the encyclopedia's account of where the rivals are named, and no better. A
    /// reader is owed the difference, and the row's own source says which of the two it was.
    ///
    /// <para>
    /// A word this loader has already named somebody else at is left as it is. Two links can reach
    /// one word from two witness words naming two records, and the carrying step refuses both where
    /// it can see both — but it can only see what this run seeded, so the one already written is
    /// invisible to it. Writing the second would say at that word what the corpus refuses to say
    /// within a single pass, and would say it about a word that was already answered.
    /// </para>
    /// </summary>
    private const string Settle =
        """
        INSERT INTO word_entity (word_id, entity_id, method, confidence, source, note)
        SELECT a.word_id, a.entity_id,
               CASE WHEN a.distinguished THEN @form ELSE @method END,
               (CASE WHEN a.stated AND a.corroborated THEN @corroborated
                     ELSE a.resolution END) * a.carried,
               a.source, a.note
        FROM annotation a
        WHERE NOT EXISTS (
            SELECT 1 FROM word_entity spoken
            WHERE spoken.word_id = a.word_id AND spoken.entity_id <> a.entity_id
              AND spoken.source = ANY(@written))
        ON CONFLICT (word_id, entity_id) DO NOTHING
        """;

    /// <summary>
    /// The resolution's own claim. Written in the same transaction as the annotation: an annotation
    /// nothing claims is invisible to the agreement measure, which is the failure link_claim was
    /// already caught by once.
    /// </summary>
    private const string Claim =
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT a.id, CASE WHEN w.distinguished THEN @form ELSE @method END,
               w.resolution * w.carried, w.source, a.note
        FROM word_entity a
        JOIN annotation w ON w.word_id = a.word_id AND w.entity_id = a.entity_id
        ON CONFLICT DO NOTHING
        """;

    /// <summary>
    /// The verse list's claim, where it agrees.
    ///
    /// It is a claim only about the stated half: for a derived name the verse list is not a second
    /// opinion but the very evidence the derivation was read from, and writing it as corroboration
    /// would be the corpus agreeing with itself.
    /// </summary>
    private const string Agreement =
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT a.id, CASE WHEN w.distinguished THEN @form ELSE @method END,
               @confidence * w.carried, @source, a.note
        FROM word_entity a
        JOIN annotation w ON w.word_id = a.word_id AND w.entity_id = a.entity_id
        WHERE w.stated AND w.corroborated
        ON CONFLICT DO NOTHING
        """;

    /// <summary>
    /// Everything this loader writes into <c>word_entity</c>, which is what its idempotence is
    /// asked about.
    ///
    /// Asking whether <em>anything</em> is annotated answers yes on a corpus where the peoples have
    /// been written, because they are annotated onto the gentilic words a step earlier — so on a
    /// cold database this loader would find rows it did not write and skip the whole pass, and the
    /// corpus would come up with no name resolutions in it and nothing saying so. Its own rows are
    /// the only question it can ask, and it is the question <c>SenseReadingLoader</c> already asks.
    /// </summary>
    internal static readonly string[] Written =
        [Resolution, GreekResolution, GreekDistinction, Derivation];

    public async Task<AnnotationOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        // Before anything is asked about what to add, because it is about what is already there and
        // it is true whether or not this boot has anything to write.
        var withdrawn = await Recall(connection, cancellationToken);
        if (withdrawn > 0)
        {
            logger.LogInformation(
                "Withdrew {Rows} name resolutions the encyclopedia no longer bears out: the number " +
                "now answers with more than one place, or the word's own verse names a person and " +
                "no place bearing it", withdrawn);
        }

        var unspoken = await db.Entities.CountAsync(
            e => !db.WordEntities.Any(a => a.EntityId == e.Id && Written.Contains(a.Source)),
            cancellationToken);

        if (unspoken == 0)
        {
            logger.LogInformation("Every record this pass could reach already names its words; nothing to do");
            return new AnnotationOutcome(
                true, Nothing, Nothing, 0, withdrawn, 0, 0, 0, 0, [], started.Elapsed);
        }

        var hebrew = await Answers(connection, HebrewNumbers, EntityCandidates.Naming,
            cancellationToken, ("witness", Witness), ("rendering", Rendering));
        var greek = await Answers(connection, GreekNumbers, EntityCandidates.GreekNaming,
            cancellationToken, ("witnesses", EntityCandidates.GreekWitnesses));

        if (hebrew.Resolved == 0 && greek.Resolved == 0)
        {
            logger.LogWarning(
                "No proper-noun Strong number resolves to a single person or place, so nothing can be " +
                "annotated. Either the encyclopedia has not been loaded yet or neither {Witness} nor " +
                "the Greek witnesses are in the corpus; both are earlier steps of the same pipeline",
                Witness);
            return new AnnotationOutcome(
                false, hebrew, greek, 0, withdrawn, 0, 0, 0, 0, [], started.Elapsed);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Run(connection, transaction, Workspace, cancellationToken);
        await Run(connection, transaction, Seed, cancellationToken,
            ("witness", Witness), ("rendering", Rendering), ("source", Resolution),
            ("derivation", Derivation), ("resolution", NameResolution), ("derived", DerivedName));

        var refused = await Attest(connection, transaction, cancellationToken);
        await Run(connection, transaction, GreekSeed, cancellationToken,
            ("witnesses", EntityCandidates.GreekWitnesses), ("source", GreekResolution),
            ("distinction", GreekDistinction), ("resolution", GreekNameResolution));

        await Run(connection, transaction, Carry, cancellationToken,
            ("faint", Annotating.Faint), ("firm", Annotating.Firm));

        var method = EnumSpelling.Of(LinkMethod.StrongNumber);
        var form = EnumSpelling.Of(ByTheForm);
        var settled = await Run(connection, transaction, Settle, cancellationToken,
            ("method", method), ("form", form), ("corroborated", Corroborated),
            ("written", Written));
        await Run(connection, transaction, Claim, cancellationToken,
            ("method", method), ("form", form));
        await Run(connection, transaction, Agreement, cancellationToken,
            ("method", method), ("form", form), ("source", VerseList),
            ("confidence", Corroborated));

        var byText = await ByText(connection, transaction, cancellationToken);
        var corroborated = await Corroboration(connection, transaction, VerseList, cancellationToken);
        var derived = await Corroboration(connection, transaction, Derivation, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new AnnotationOutcome(
            settled == 0 && byText.Count > 0, hebrew, greek, refused, withdrawn, settled,
            byText.Sum(t => t.Words), corroborated, derived, byText, started.Elapsed);
        logger.LogInformation("Annotated: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The numbers BHSA marks as names, which is the population the Hebrew half answers — one row
    /// per number and per kind its words are marked, because that is the pair the resolution is
    /// asked of. A number whose words are marked both ways is two questions, and a marking that
    /// commits to no kind carries none.
    /// </summary>
    private static readonly string HebrewNumbers =
        $"""
         SELECT DISTINCT w.strong_number AS number, {Marked} AS kind
         FROM word w JOIN text t ON t.id = w.text_id AND t.slug = @witness
         WHERE w.morphology->>'nameType' IS NOT NULL AND w.strong_number IS NOT NULL
         """;

    /// <summary>
    /// The same for the Greek: a noun whose lexicon lemma is written with a capital. It carries no
    /// kind because nothing in the Greek marks one.
    /// </summary>
    private static readonly string GreekNumbers =
        $"""
         SELECT DISTINCT w.strong_number AS number, NULL::text AS kind
         FROM word w
         JOIN text t ON t.id = w.text_id AND t.slug = ANY(@witnesses)
         JOIN strong_entry lexicon ON lexicon.strong_number = w.strong_number
              AND lower(left(lexicon.lemma, 1)) <> left(lexicon.lemma, 1)
         WHERE {GreekNoun}
         """;

    /// <summary>
    /// Which Greek numbers the text vouches for, and how many resolved ones it does not.
    ///
    /// The comparison is on the folded spelling, and the fold is the one the corpus already ran
    /// over every word — <see cref="WordFolding"/>, in C#, because a fold written a second time in
    /// SQL is a fold that will one day disagree with itself and refuse a name that is there. So the
    /// spellings are read out, folded here against the folded forms the words already carry, and
    /// the numbers that survive are handed back to the seed as a table.
    /// </summary>
    private async Task<int> Attest(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var claimed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var writes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        await foreach (var row in Rows(connection, transaction, Claimed, cancellationToken,
                           ("witnesses", EntityCandidates.GreekWitnesses)))
        {
            Remember(claimed, row.GetString(0), row.IsDBNull(1) ? null : row.GetString(1));
            Remember(writes, row.GetString(0), row.IsDBNull(2) ? null : row.GetString(2));
        }

        await foreach (var row in Rows(connection, transaction, Printed, cancellationToken,
                           ("witnesses", EntityCandidates.GreekWitnesses)))
        {
            var number = row.GetString(0);
            if (!row.IsDBNull(1))
            {
                (writes.TryGetValue(number, out var folded) ? folded : writes[number] = [])
                    .Add(row.GetString(1));
            }

            Remember(writes, number, row.IsDBNull(2) ? null : row.GetString(2));
        }

        var attested = claimed
            .Where(name => writes.TryGetValue(name.Key, out var spellings) && spellings.Overlaps(name.Value))
            .Select(name => name.Key)
            .ToArray();

        await Run(connection, transaction,
            "INSERT INTO attested (number) SELECT unnest(@numbers)", cancellationToken,
            ("numbers", attested));

        return claimed.Count - attested.Length;
    }

    private static void Remember(Dictionary<string, HashSet<string>> spellings, string number, string? spelling)
    {
        if (spelling is null)
        {
            return;
        }

        if (!spellings.TryGetValue(number, out var folded))
        {
            spellings[number] = folded = new HashSet<string>(StringComparer.Ordinal);
        }

        folded.Add(WordFolding.Fold(spelling, Greek));
    }

    /// <summary>
    /// How many of one language's proper-noun numbers the encyclopedia answers with one entity,
    /// with several, and with nobody. The second and third are the size of the work this loader
    /// deliberately does not do, and they belong in the record beside what it did.
    ///
    /// <para>
    /// The count is of what the loader would be allowed to write, so it is asked of the same pair
    /// the resolution is: where the population states a kind, only records of that kind answer;
    /// where it states none — the Greek throughout, and a Hebrew marking that commits to nothing —
    /// every record does. Counting the Hebrew per number alone reported eighteen names a man and a
    /// place share as work refused, when the marking settles every one of them.
    /// </para>
    /// </summary>
    private static async Task<NameAnswers> Answers(
        NpgsqlConnection connection,
        string numbers,
        string naming,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        var sql =
            $"""
             WITH proper AS ({numbers}),
             answered AS (
                 SELECT p.number, p.kind,
                        count(DISTINCT n.entity_id)
                            FILTER (WHERE p.kind IS NULL OR e.kind = p.kind) AS entities
                 FROM proper p
                 LEFT JOIN ({naming}) n ON n.number = p.number
                 LEFT JOIN entity e ON e.id = n.entity_id
                 GROUP BY 1, 2
             )
             SELECT count(*) FILTER (WHERE entities = 1),
                    count(*) FILTER (WHERE entities > 1),
                    count(*) FILTER (WHERE entities = 0)
             FROM answered
             """;

        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.CommandTimeout = Patient;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new NameAnswers(
            (int)reader.GetInt64(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2));
    }

    private static async Task<IReadOnlyList<(string Text, int Words)>> ByText(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var counts = new List<(string, int)>();
        await foreach (var row in Rows(connection, transaction,
                           "SELECT t.slug, count(*) FROM word_entity a JOIN word w ON w.id = a.word_id " +
                           "JOIN text t ON t.id = w.text_id GROUP BY 1 ORDER BY 2 DESC",
                           cancellationToken))
        {
            counts.Add((row.GetString(0), (int)row.GetInt64(1)));
        }

        return counts;
    }

    /// <summary>How many annotations one named source spoke for.</summary>
    private static async Task<int> Corroboration(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string source,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM word_entity a WHERE EXISTS (SELECT 1 FROM word_entity_claim c " +
            "WHERE c.word_entity_id = a.id AND c.source = @source)",
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("source", source);
        command.CommandTimeout = Patient;
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async IAsyncEnumerable<NpgsqlDataReader> Rows(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(
            sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.CommandTimeout = Patient;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return reader;
        }
    }

    /// <summary>One statement, and how many rows it wrote.</summary>
    /// <summary>
    /// <see cref="Withdraw"/>, in a transaction of its own, so that the seeds and the words carried
    /// from them go together or not at all.
    /// </summary>
    private async Task<int> Recall(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var withdrawn = await Run(connection, transaction, Withdraw, cancellationToken,
            ("written", Written), ("method", EnumSpelling.Of(LinkMethod.StrongNumber)),
            ("witness", Witness));
        await transaction.CommitAsync(cancellationToken);
        return withdrawn;
    }

    private static async Task<int> Run(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(
            sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.CommandTimeout = Patient;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
