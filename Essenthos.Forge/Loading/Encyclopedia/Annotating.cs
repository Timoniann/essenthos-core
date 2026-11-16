using System.Globalization;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// The steps every annotation goes through once something has decided which Hebrew word names whom:
/// carry the answer along the links into the other texts, write the row, and write the claim that
/// says what asserted it.
///
/// Written once because the three things that produce annotations — a Strong number that resolves,
/// a model that read the verse, and a person who ruled — differ only in what they put in the seed.
/// The carrying rule is the same for all of them and has to stay the same: one hop, always from the
/// Hebrew, never onto a word two Hebrew words disagree about, onto the head of what the link names
/// unless the link names one name written several times, and the link's own confidence multiplied
/// into the annotation's so that a word reached by a guess is never stored as firmly as one reached
/// by a source's own mapping.
/// </summary>
internal static class Annotating
{
    /// <summary>
    /// How every carried annotation's note begins, which is what tells a row the links reached from
    /// the seed it was reached from.
    /// </summary>
    public const string CarriedNote = "through %";

    /// <summary>
    /// The confidence below which a link is not enough on its own to put a name on a word.
    ///
    /// It is not a floor, and nothing is dropped for being faint alone: a faint link is often the
    /// only account the corpus has of a word, and a great many faint ones are true. Shebnah at
    /// 0.69 and Esau at 0.66 sit here, and so do the 250 places the Ukrainian <em>Бог</em> stands
    /// for <em>адонай</em>, none of which reaches 0.59. What this marks is a link weak enough to
    /// lose to a better one, and losing is the only thing that happens to it.
    /// </summary>
    public const double Faint = 0.70;

    /// <summary>
    /// Whether the word <c>hw</c> of the text <c>ht</c> is one its language never names anybody
    /// with: an article, a preposition, a conjunction, an auxiliary, a postposition, the word for
    /// <em>all</em>. A source's table puts the phrase that renders a name opposite the name, and the
    /// phrase's last word is often one of these: the Hindi <em>एसाव के पुत्र</em> is <em>Esau's
    /// sons</em>, and its last word before <em>sons</em> is the postposition; the Berean's <em>and had been</em> renders the
    /// יָרָבְעָם of 1 Kings 12:2 that it leaves unwritten. The Hebrew says it by its parsing and the
    /// Greek by its numbers, or by its letters where an edition carries none. A pronoun is not among
    /// them.
    /// </summary>
    internal const string NeverAName =
        """
        (CASE ht.language
            WHEN 'eng' THEN lower(regexp_replace(hw.text, '[[:punct:]]', '', 'g')) = ANY(ARRAY['all', 'a', 'an', 'the', 'of', 'and', 'to', 'in', 'at', 'by', 'for', 'from', 'with', 'into', 'unto', 'upon', 'when', 'then', 'that', 'as', 'but', 'or', 'nor', 'not', 'was', 'were', 'is', 'are', 'be', 'been', 'being', 'had', 'have', 'has', 'hath', 'did', 'do', 'doth', 'shall', 'will', 'should', 'would', 'also', 'there', 'thus', 'so', 'which', 'while'])
            WHEN 'deu' THEN lower(regexp_replace(hw.text, '[[:punct:]]', '', 'g')) = ANY(ARRAY['alle', 'alles', 'allen', 'aller', 'der', 'die', 'das', 'des', 'dem', 'den', 'ein', 'eine', 'einen', 'einem', 'einer', 'eines', 'und', 'von', 'vom', 'zu', 'zum', 'zur', 'in', 'im', 'an', 'am', 'auf', 'aus', 'mit', 'bei', 'nach', 'über', 'unter', 'vor', 'für', 'gegen', 'durch', 'wie', 'als', 'da', 'daß', 'dass', 'war', 'waren', 'ist', 'sind', 'hatte', 'hatten', 'hat', 'ward', 'wurde'])
            WHEN 'fra' THEN lower(regexp_replace(hw.text, '[[:punct:]]', '', 'g')) = ANY(ARRAY['tout', 'tous', 'toute', 'toutes', 'le', 'la', 'les', 'l', 'de', 'du', 'des', 'd', 'un', 'une', 'et', 'à', 'au', 'aux', 'en', 'dans', 'par', 'pour', 'avec', 'que', 'qui', 'est', 'était', 'fut'])
            WHEN 'spa' THEN lower(regexp_replace(hw.text, '[[:punct:]]', '', 'g')) = ANY(ARRAY['todo', 'todos', 'toda', 'todas', 'el', 'la', 'los', 'las', 'lo', 'un', 'una', 'de', 'del', 'y', 'e', 'a', 'al', 'en', 'con', 'por', 'para', 'que', 'como', 'fue', 'era', 'es'])
            WHEN 'por' THEN lower(regexp_replace(hw.text, '[[:punct:]]', '', 'g')) = ANY(ARRAY['todo', 'todos', 'toda', 'todas', 'o', 'a', 'os', 'as', 'um', 'uma', 'de', 'do', 'da', 'dos', 'das', 'e', 'em', 'no', 'na', 'nos', 'nas', 'ao', 'aos', 'à', 'com', 'por', 'pelo', 'pela', 'que', 'como', 'foi', 'era', 'é'])
            WHEN 'lat' THEN lower(regexp_replace(hw.text, '[[:punct:]]', '', 'g')) = ANY(ARRAY['et', 'in', 'de', 'ad', 'ex', 'e', 'cum', 'a', 'ab', 'per', 'est', 'erat', 'ut'])
            WHEN 'rus' THEN lower(regexp_replace(hw.text, '[[:punct:]]', '', 'g')) = ANY(ARRAY['весь', 'вся', 'всё', 'все', 'и', 'в', 'во', 'к', 'ко', 'от', 'из', 'с', 'со', 'на', 'по', 'о', 'об', 'у', 'за', 'для', 'до', 'при', 'а', 'но', 'же', 'был', 'была', 'было', 'были', 'как', 'что'])
            WHEN 'ukr' THEN lower(regexp_replace(hw.text, '[[:punct:]]', '', 'g')) = ANY(ARRAY['весь', 'вся', 'все', 'всі', 'і', 'й', 'та', 'в', 'у', 'до', 'від', 'з', 'із', 'зі', 'на', 'по', 'о', 'об', 'за', 'для', 'при', 'а', 'але', 'ж', 'же', 'був', 'була', 'було', 'були', 'як', 'що'])
            WHEN 'hin' THEN lower(regexp_replace(hw.text, '[[:punct:]]', '', 'g')) = ANY(ARRAY['के', 'ने', 'की', 'को', 'का', 'से', 'में', 'पर', 'और', 'तक', 'है', 'था', 'थी', 'थे', 'हैं'])
            WHEN 'urd' THEN lower(regexp_replace(hw.text, '[[:punct:]]', '', 'g')) = ANY(ARRAY['के', 'ने', 'की', 'को', 'का', 'से', 'में', 'पर', 'और', 'तक', 'है', 'था', 'थी', 'थे', 'हैं', 'کے', 'نے', 'کی', 'کا', 'کو', 'سے', 'میں', 'پر', 'اور'])
            WHEN 'pan' THEN lower(regexp_replace(hw.text, '[[:punct:]]', '', 'g')) = ANY(ARRAY['ਦੇ', 'ਨੇ', 'ਨੂੰ', 'ਦਾ', 'ਦੀ', 'ਤੋਂ', 'ਵਿੱਚ', 'ਅਤੇ', 'ਨਾਲ'])
            WHEN 'hbo' THEN coalesce(hw.morphology ->> 'pos', '') IN ('prep', 'conj', 'art', 'nega')
            WHEN 'grc' THEN coalesce(hw.strong_number, '') IN ('G3588', 'G2532', 'G1161', 'G1063', 'G3754', 'G1537', 'G1519', 'G1722', 'G575', 'G4314', 'G1223', 'G2596', 'G3326', 'G5228', 'G5259', 'G1909', 'G3844', 'G4012', 'G4862')
                              OR coalesce(hw.normalised_text, '') IN ('ο', 'η', 'το', 'του', 'τησ', 'τω', 'τη', 'τον', 'την', 'οι', 'αι', 'τα', 'των', 'τοισ', 'ταισ', 'τουσ', 'τασ', 'και', 'δε', 'εν', 'εισ', 'εκ', 'εξ', 'απο', 'απ', 'αφ', 'προσ', 'παρα', 'παρ', 'επι', 'επ', 'εφ', 'δια', 'δι', 'κατα', 'κατ', 'καθ', 'μετα', 'μετ', 'μεθ', 'υπο', 'υπ', 'υφ', 'περι', 'συν', 'οτι', 'γαρ')
            ELSE FALSE END)
        """;

    /// <summary>
    /// The entity each word stands named as, as the common table <c>settled (word_id, entity_id)</c>:
    /// an annotation read of the word itself before any the links carried onto it or the verses'
    /// consensus inferred, then the one of highest claim standing and then confidence, and nothing
    /// for a word where two of equal footing, standing and confidence name two entities, unless the
    /// two stand beside each other (<see cref="Beside"/>). A carried annotation is a reading of
    /// another word, so whatever method made it, it does not outrank what was read of this one; the
    /// rule is <c>Annotations.Settle</c>'s, and the two must say the same. A title gives way to the record that bears it, whatever
    /// their standing: the title says what the word is and the bearer whom it names, so the bearer
    /// is the word's first answer. Every pass that reads an entity's verses off the words reads
    /// them through this, so a verse is never listed on a page for a word the word panel shows
    /// nothing at.
    /// </summary>
    public static readonly string Settled =
        $"""
         standing AS (
             SELECT a.word_id,
                    a.entity_id,
                    e.kind,
                    {Standing} AS standing,
                    coalesce(a.confidence, 1.0) AS confidence,
                    a.source,
                    coalesce(a.note, '') AS note,
                    coalesce(a.note, '') NOT LIKE '{CarriedNote}' AND a.source <> '{Essenthos.Core.Corpus.Annotations.Consensus}' AS own
             FROM word_entity a
             JOIN entity e ON e.id = a.entity_id
         ),
         ranked AS (
             SELECT s.word_id,
                    s.entity_id,
                    s.kind,
                    s.standing,
                    s.confidence,
                    s.own,
                    s.source,
                    s.note,
                    row_number() OVER settling AS place,
                    count(*) OVER (PARTITION BY s.word_id) AS claims,
                    lead(s.entity_id) OVER settling AS next_entity,
                    lead(s.kind) OVER settling AS next_kind,
                    lead(s.standing) OVER settling AS next_standing,
                    lead(s.confidence) OVER settling AS next_confidence,
                    lead(s.own) OVER settling AS next_own,
                    lead(s.source) OVER settling AS next_source,
                    lead(s.note) OVER settling AS next_note
             FROM standing s
             WINDOW settling AS (
                 PARTITION BY s.word_id
                 ORDER BY s.own DESC, s.standing DESC, s.confidence DESC, s.kind = '{Title}', s.kind COLLATE "C", s.entity_id)
         ),
         several AS MATERIALIZED (
             SELECT r.word_id, r.entity_id, r.kind, r.standing, r.place FROM ranked r WHERE r.claims > 1
         ),
         bearer_first AS (
             SELECT DISTINCT ON (top.word_id) top.word_id, other.entity_id
             FROM several top
             JOIN several other ON other.word_id = top.word_id AND other.place > 1
             JOIN title_bearer borne
                  ON borne.title_entity_id = top.entity_id AND borne.bearer_entity_id = other.entity_id
             WHERE top.place = 1 AND top.kind = '{Title}'
             ORDER BY top.word_id, other.place
         ),
         settled AS (
             SELECT r.word_id, coalesce(bearer_first.entity_id, r.entity_id) AS entity_id
             FROM ranked r
             LEFT JOIN bearer_first ON bearer_first.word_id = r.word_id
             WHERE r.place = 1
               AND (r.claims = 1
                    OR NOT (r.next_entity <> r.entity_id
                            AND r.next_own = r.own
                            AND r.next_standing = r.standing
                            AND r.next_confidence = r.confidence)
                    OR {Beside("r", "r.next_")})
         )
         """;

    /// <summary>A property and not a field, because the statements above are fields and are built first.</summary>
    private static string Title => EnumSpelling.Of(EntityKind.Title);

    private static string Being => EnumSpelling.Of(EntityKind.Being);

    /// <summary>The standing of an answer a person ruled, as a property for the same reason.</summary>
    private static string Ruled => ClaimStanding.Of(LinkMethod.Manual).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether one word names both records without either answering for the other: a title and a
    /// record joined to it as its bearer, a person and a people, a place and a people a person
    /// ruled on the word together, or records one ruling names on the word together: two things, or a creature and
    /// the nation or ruler the text calls by its name (<c>Annotations.Together</c>). Two men on one word, or a man and a town, are two answers to one
    /// question, and the lower is the one the higher outranked. <paramref name="one"/> and
    /// <paramref name="other"/> are the column prefixes of the two answers (<c>r.</c> and <c>r.next_</c>).
    /// </summary>
    private static string Beside(string one, string other) =>
        Beside($"{one}.kind", $"{one}.entity_id", $"{one}.standing", $"{one}.source", $"{one}.note",
            $"{other}kind", $"{other}entity", $"{other}standing", $"{other}source", $"{other}note");

    private static string Beside(
        string kind, string entity, string standing, string source, string note,
        string otherKind, string otherEntity, string otherStanding, string otherSource, string otherNote) =>
        $"""
         (({kind} = '{EnumSpelling.Of(EntityKind.Person)}' AND {otherKind} = '{EnumSpelling.Of(EntityKind.People)}')
          OR ({kind} = '{EnumSpelling.Of(EntityKind.People)}' AND {otherKind} = '{EnumSpelling.Of(EntityKind.Person)}')
          OR ({kind} IN ('{EnumSpelling.Of(EntityKind.Place)}', '{EnumSpelling.Of(EntityKind.People)}')
              AND {otherKind} IN ('{EnumSpelling.Of(EntityKind.Place)}', '{EnumSpelling.Of(EntityKind.People)}')
              AND {kind} <> {otherKind}
              AND {standing} = {Ruled} AND {otherStanding} = {Ruled})
          OR ((({kind} = '{EnumSpelling.Of(EntityKind.Object)}' AND {otherKind} = '{EnumSpelling.Of(EntityKind.Object)}')
               OR (({kind} = '{Being}') <> ({otherKind} = '{Being}')))
              AND {standing} = {Ruled} AND {otherStanding} = {Ruled}
              AND {source} = {otherSource} AND {note} = {otherNote})
          OR ({kind} = '{Title}' AND EXISTS (
                  SELECT 1 FROM title_bearer borne
                  WHERE borne.title_entity_id = {entity} AND borne.bearer_entity_id = {otherEntity}))
          OR ({otherKind} = '{Title}' AND EXISTS (
                  SELECT 1 FROM title_bearer borne
                  WHERE borne.title_entity_id = {otherEntity} AND borne.bearer_entity_id = {entity})))
         """;

    /// <summary>
    /// How much the answer's strongest claim knew before it started — a ruling or a rule that arrives
    /// at an answer a word already has is held as a claim on that row, and it stands at the ruling's
    /// standing, not at the first method's; a source's testimony about the verse is not a claim about
    /// the word and is left out. Written out of <see cref="ClaimStanding"/> so
    /// that renumbering it moves this statement too. A second copy of those ordinals is a second
    /// answer to <em>who does this word name</em>, and the whole point of deriving the references
    /// from the annotations is that a page and a word cannot disagree.
    /// </summary>
    private static string Standing =>
        $"""
         greatest({StandingOf("a.method")}, coalesce(
             (SELECT max({StandingOf("held.method")}) FROM word_entity_claim held
              WHERE held.word_id = a.word_id AND held.entity_id = a.entity_id AND held.method <> '{EnumSpelling.Of(LinkMethod.StatedBySource)}'), 0))
         """;

    private static string StandingOf(string method) =>
        $"CASE {method} "
        + string.Concat(Enum.GetValues<LinkMethod>().Select(each =>
            $"WHEN '{EnumSpelling.Of(each)}' THEN "
            + ClaimStanding.Of(each).ToString(CultureInfo.InvariantCulture) + " "))
        + "ELSE 0 END";
}
