using System.Globalization;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

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
    /// The seed a caller fills and everything below reads.
    ///
    /// <c>confidence</c> is null exactly where a person or a source settled it, which is the same
    /// rule the tables themselves enforce. <c>corroborated</c> is whether the encyclopedia's own
    /// list of verses independently names that entity in this word's verse. <c>through</c> and
    /// <c>link</c> are null on a seed and filled on a word the links reached: the seed word it was
    /// reached from, and what the link crossed to reach it was worth.
    ///
    /// <para>
    /// It drops itself at commit. A temporary table outlives its transaction and belongs to the
    /// connection, and connections here are pooled — so without this, two loaders that both write
    /// annotations in one process fail on the second one with <em>relation already exists</em>,
    /// which is a start-up crash a long way from its cause.
    /// </para>
    /// </summary>
    public const string Workspace =
        """
        CREATE TEMP TABLE pending_annotation (
            word_id bigint PRIMARY KEY,
            entity_id integer NOT NULL,
            confidence double precision,
            claim_confidence double precision,
            corroborated boolean NOT NULL,
            method text,
            source text,
            note text NOT NULL,
            through bigint,
            link double precision)
        ON COMMIT DROP
        """;

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
    /// The confidence at which a link is taken to be the rendering, so that a faint link to the
    /// same witness word in the same verse has nothing left to explain.
    ///
    /// The gap between this and <see cref="Faint"/> is the point. Two words of one text can both
    /// render one Hebrew name — <em>of Abinoam</em> is two words in the King James — and a rule
    /// that dropped the weaker of any pair would take the second half of every such rendering. A
    /// word that is part of the rendering scores near the one beside it; a word the aligner had
    /// nowhere else to put does not.
    /// </summary>
    public const double Firm = 0.90;

    /// <summary>
    /// An English possessive, which is where a name stops and the thing it possesses begins:
    /// <em>Terah's</em> in <em>Terah's lifetime</em>, <em>Jerusalem's</em> in <em>for Jerusalem's
    /// sake</em>. The plural form ends at the apostrophe, so the <c>s</c> is optional, and the
    /// King James writes <em>LORD'S</em>, so the match ignores case.
    /// </summary>
    private const string Possessive = "[''’]s?$";

    /// <summary>
    /// Everything a spelling is compared without — the apostrophe of a possessive, the hyphen of
    /// <em>Beth-shemesh</em>, the point of an abbreviation.
    /// </summary>
    private const string Ornament = "[^[:alnum:]]";

    /// <summary>
    /// The names the encyclopedia records for one entity, each taken whole.
    ///
    /// A label of several words contributes nothing, and that is the point rather than a
    /// simplification: the Nile's <em>River of Egypt</em> and the Euphrates' <em>the great
    /// river</em> would otherwise put <em>of</em> and <em>the</em> among the spellings a person is
    /// known by, and a rule that moves a naming towards a name would move it onto the article.
    /// A title does the same with <em>servant</em> and <em>king</em>, so titles are left out
    /// entirely; what is wanted here is what somebody is called, not what they are.
    ///
    /// <para>
    /// The record's own name and its slug are names as much as the label rows are, and the slug's
    /// trailing number is the encyclopedia telling two men of one name apart rather than part of
    /// either name.
    /// </para>
    /// </summary>
    private const string Names =
        """
        SELECT lower(known.spelling) AS spelling
        FROM (
            SELECT e.name AS spelling FROM entity e WHERE e.id = seed.entity_id
            UNION ALL
            SELECT regexp_replace(e.slug, '-[0-9]+$', '') FROM entity e WHERE e.id = seed.entity_id
            UNION ALL
            SELECT n.label FROM entity_name n
            WHERE n.entity_id = seed.entity_id AND coalesce(n.kind, '') NOT IN ('title', 'description')
            UNION ALL
            SELECT n.hebrew_transliterated FROM entity_name n
            WHERE n.entity_id = seed.entity_id AND coalesce(n.kind, '') NOT IN ('title', 'description')
            UNION ALL
            SELECT n.greek_transliterated FROM entity_name n
            WHERE n.entity_id = seed.entity_id AND coalesce(n.kind, '') NOT IN ('title', 'description')) known
        WHERE known.spelling ~ '^[[:alnum:]]+$'
        """;

    /// <summary>
    /// How alike a word and one of those names must be before the name is allowed to move the
    /// naming off the head. Trigram similarity, so it is a fraction and not a distance.
    ///
    /// <para>
    /// Half is where the measurement puts the line. Below it the corpus offers 86 displacements and
    /// 83 of them are the word <em>the</em>, which shares two trigrams with the Greek
    /// <em>theou</em> and scores 0.43 against it; <em>and</em> against <em>Andrew</em> and
    /// <em>thee</em> against <em>theou</em> are the rest. Above it there are 217 and they are the
    /// entity's own spelling or a form of it — <em>Canaanitish</em> at 0.53, <em>Benjamites</em> at
    /// 0.71, and the exact matches at 1. What the line costs is five displacements the corpus
    /// would have been right to make: <em>Dan</em> against <em>Danites</em> and <em>Judah</em>
    /// against <em>Judahites</em> at 0.33 and 0.45, and <em>Pochereth</em> at 0.45. Those keep the
    /// head, which is where they already were.
    /// </para>
    ///
    /// <para>
    /// Written as text rather than as a number because it is spliced into SQL, and a double
    /// formatted under a comma locale is not a number Postgres can read.
    /// </para>
    /// </summary>
    private const string Convincing = "0.5";

    /// <summary>
    /// The word of a link's other side that the naming lands on, where the link names several and
    /// only one of them can be the name. <see cref="Reached"/> is what decides which case this is.
    ///
    /// A link names a set of words on each side, and a source's own translation table routinely
    /// puts a phrase opposite a single original word, because that is what translation does: תֶּרַח
    /// in construct is <em>of Terah</em>, and the table has nowhere but the name to park the
    /// <em>When</em> that opens the clause. Handing the entity to every word of that set is what
    /// annotates <em>the</em>, <em>of</em> and <em>When</em> as people.
    ///
    /// <para>
    /// The naming goes to the set's head, and the head is its last word. That is not a guess about
    /// English: it is the ruled word in every one of the 332 groups a person has decided, and where
    /// it and the longest word disagree — 1,902 words of the two English texts — the last word is
    /// the name and the longest is <em>then</em>, <em>from</em>, <em>when</em> or
    /// <em>against</em>. The original sides read the same way, an article or a preposition standing
    /// before the name it is prefixed to rather than after it.
    /// </para>
    ///
    /// <para>
    /// The exception is a possessive, which closes the name from the other end: in <em>Terah's
    /// lifetime</em> the last word is the thing possessed and the name is the word before it. Only
    /// the word immediately before the last one is read that way — further back and the group is a
    /// clause rather than a possessive phrase, and its head is the last word again.
    /// </para>
    ///
    /// <para>
    /// Position decides only where the entity's own name does not. Where the source grouped two
    /// names into one correspondence the head is the wrong one of them — the Berean puts
    /// <em>Tahrea and Ahaz</em> opposite <em>וְ תַחְרֵעַ</em>, so the last word says Ahaz is Tahrea
    /// and leaves Tahrea unmarked, which reads as a fact where the article read as noise. So a word
    /// the encyclopedia records as this entity's name takes the naming from the head, and nothing
    /// else does: below <see cref="Convincing"/> every word of the group scores nothing and the
    /// order is the positional one, untouched.
    /// </para>
    ///
    /// <para>
    /// A word that is never a name (<see cref="NeverAName"/>) is not a candidate at all, so a group
    /// of nothing else names nothing.
    /// </para>
    /// </summary>
    public const string Head =
        $"""
         {HeadPeers}
           AND NOT {NeverAName}{HeadChosen}
         """;

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
    /// What <see cref="CommonWord"/> reads, made once per transaction: <c>common_number</c>, the
    /// Strong numbers the lexicon gives a common word — a Greek lemma written without a capital, a
    /// Hebrew entry whose part of speech is not a proper noun — and <c>own_number</c>, the numbers
    /// each record's own names and titles carry, and those of the titles it bears. A value of several
    /// numbers is split, since it is the record's own title word by word: <em>King of Hazor</em> is
    /// Jabin's, and so is the βασιλεύς that renders it. Dropped and made again on each call, because a
    /// pass may add records between two carries of one transaction.
    /// </summary>
    public const string CommonWords =
        """
        DROP TABLE IF EXISTS common_number, own_number;
        CREATE TEMP TABLE common_number ON COMMIT DROP AS
        SELECT s.strong_number AS number
        FROM strong_entry s
        WHERE (s.strong_number LIKE 'G%' AND s.lemma !~ '^[[:upper:]]')
           OR (s.strong_number LIKE 'H%' AND s.morphology !~ 'n-pr');
        CREATE UNIQUE INDEX ON common_number (number);
        CREATE TEMP TABLE own_number ON COMMIT DROP AS
        SELECT DISTINCT owner.entity_id, btrim(number) AS number
        FROM entity_name n
        CROSS JOIN LATERAL unnest(string_to_array(concat_ws(',', n.hebrew_strong_number, n.greek_strong_number), ',')) number
        CROSS JOIN LATERAL (VALUES (n.entity_id), (n.aspect_of_entity_id)) owner (entity_id)
        WHERE owner.entity_id IS NOT NULL
        UNION
        SELECT borne.bearer_entity_id, btrim(number)
        FROM title_bearer borne
        JOIN entity_name n ON n.entity_id = borne.title_entity_id
        CROSS JOIN LATERAL unnest(string_to_array(concat_ws(',', n.hebrew_strong_number, n.greek_strong_number), ',')) number;
        CREATE INDEX ON own_number (entity_id, number)
        """;

    /// <summary>
    /// The pronouns of the two original languages: the Greek by the numbers of the forms
    /// <see cref="Pronouns"/> lists, which hold for the editions printed without accents too, and
    /// BHSA's personal and demonstrative pronouns by its parsing. Whom a pronoun may be shown naming
    /// is <see cref="PronounReferents"/>'s question, not <see cref="CommonWord"/>'s.
    /// </summary>
    private const string GreekPronouns =
        "'G846', 'G3778', 'G1565', 'G1438', 'G1683', 'G4572', 'G1473', 'G4771', 'G2249', 'G5210', 'G1699', 'G4674'";

    /// <summary>
    /// Whether the word <paramref name="word"/> of the text <paramref name="text"/> is a Greek or
    /// Hebrew common word the person or place <paramref name="entity"/> may not be carried onto: its
    /// Strong number is one the lexicon gives a common noun, verb, adjective or particle, and none of
    /// the record's own names or titles. A link is a claim about which words correspond, and a
    /// translation's own alignment is wrong often enough — the Hindi interlinear puts
    /// <em>अब्राहम</em> opposite the βίβλῳ of Mark 12:26 and <em>यीशु का तिरस्कार किया</em>
    /// opposite ἠρνήσασθε of Acts 3:13, and the Chinese Union writes <em>彼拉多定意</em> as one word
    /// numbered for κρίναντος — while the original word's own number is the lexicon's statement of
    /// what the word is. A word BHSA parses as a proper noun is a name whatever its number, a title
    /// the record bears keeps it (Pharaoh's פַּרְעֹה, Jabin's βασιλεύς), and a people, a title, an
    /// object or a feast is not asked: πάσχα is the Passover. Reads the tables
    /// <see cref="CommonWords"/> makes.
    /// </summary>
    public static string CommonWord(string word, string text, string entity) =>
        $"""
         (CASE WHEN {text}.language IN ('grc', 'hbo')
                    AND EXISTS (SELECT 1 FROM common_number common WHERE common.number = {word}.strong_number)
                    AND EXISTS (SELECT 1 FROM entity named WHERE named.id = {entity}
                                AND named.kind IN ('{EnumSpelling.Of(EntityKind.Person)}', '{EnumSpelling.Of(EntityKind.Place)}'))
                    AND NOT EXISTS (SELECT 1 FROM own_number own
                                    WHERE own.entity_id = {entity} AND own.number = {word}.strong_number)
               THEN {word}.strong_number NOT IN ({GreekPronouns})
                    AND coalesce({word}.morphology ->> 'pos', '') NOT IN ('nmpr', 'prps', 'prde')
               ELSE FALSE END)
         """;

    /// <summary>
    /// The postpositions of Hindi, Urdu and Punjabi, which follow the noun they govern: in a phrase
    /// that renders a name, the word before one of them is the name — <em>एसाव के पुत्र</em>,
    /// <em>Esau's sons</em>, is a name, its genitive and what it governs, and the last word is the
    /// sons. They close the name as an English possessive does.
    /// </summary>
    private const string Postpositions =
        "'के', 'ने', 'की', 'को', 'का', 'से', 'में', 'पर', 'تک', 'کے', 'نے', 'کی', 'کا', 'کو', 'سے', 'میں', 'پر', " +
        "'ਦੇ', 'ਨੇ', 'ਨੂੰ', 'ਦਾ', 'ਦੀ', 'ਤੋਂ', 'ਵਿੱਚ'";

    /// <summary>
    /// <see cref="Head"/> as it was chosen before the words that are never a name were passed over,
    /// which a withdrawal still has to find the rows of.
    /// </summary>
    private const string AnyHead =
        $"""
         {HeadPeers}{HeadChosen}
         """;

    private const string HeadPeers =
        $"""
         SELECT peer.word_id
         FROM (
             SELECT lw.word_id, hw.verse_id, hw.position,
                    (hw.text ~* '{Possessive}'
                        AND row_number() OVER (ORDER BY hw.verse_id DESC, hw.position DESC) = 2)
                    OR (ht.language IN ('hin', 'urd', 'pan')
                        AND EXISTS (SELECT 1 FROM link_word after
                                    JOIN word governs ON governs.id = after.word_id
                                    WHERE after.link_id = mine.link_id AND after.side = lw.side
                                      AND governs.verse_id = hw.verse_id AND governs.position = hw.position + 1
                                      AND governs.text = ANY(ARRAY[{Postpositions}])))
                        AS closes,
                    coalesce((
                        SELECT max(similarity(
                            lower(regexp_replace(hw.text, '{Ornament}', '', 'g')), known.spelling))
                        FROM ({Names}) known), 0) AS named
             FROM link_word lw
             JOIN word hw ON hw.id = lw.word_id
             JOIN text ht ON ht.id = hw.text_id
             WHERE lw.link_id = mine.link_id AND lw.side <> mine.side
         """;

    private const string HeadChosen =
        $"""
         ) peer
         ORDER BY CASE WHEN peer.named >= {Convincing} THEN peer.named ELSE 0 END DESC,
                  peer.closes DESC, peer.verse_id DESC, peer.position DESC
         LIMIT 1
         """;

    /// <summary>
    /// A link that pairs several occurrences of one name with several words, which is the shape the
    /// numbering writes where it cannot say which of them answers which.
    ///
    /// <para>
    /// 1 Samuel 30:7 writes דָּוִד twice and <em>Давид</em> three times, and every one of the five
    /// carries H1732. The Synodal's printed numbering can only say that those three render those
    /// two, so <see cref="Loading.Links.StrongNumberMatch"/> writes one link naming all five and the
    /// settlement removes the aligner's one-to-one links it replaced. A head picked out of that set
    /// names one <em>Давид</em> and leaves the other two blank, which is not what the set says.
    /// </para>
    ///
    /// <para>
    /// <strong>What makes it safe is the number, on the seed's own side.</strong> A phrase a
    /// translation's table states is also several words opposite several — <em>the son of Terah</em>
    /// against <em>בֶּן תֶּרַח</em> — and there the naming must land on one word, because only one
    /// of the words on the witness's side is the name. Here that side carries nothing but the
    /// seed's own number, so each word opposite it that is the name again renders the name. There
    /// are 15,394 such links against 90,534 phrases, and the test tells them apart without asking
    /// what wrote either.
    /// </para>
    ///
    /// <para>
    /// A number is not a lexeme, which is why <see cref="LinkWorth"/> gives a name crossing such
    /// a set no more than the link itself is worth, and not an entity, which is why the number is
    /// not enough on its own: <see cref="NamesOne"/> is asked beside it.
    /// </para>
    ///
    /// <para>
    /// A seed word carrying no number is excluded rather than compared: two nulls are not the same
    /// lexeme, and BHSA leaves 9,949 words unnumbered.
    /// </para>
    /// </summary>
    private const string OneNameTwice = $"{OneNumberTwice} AND {NamesOne}";

    /// <summary>The half of <see cref="OneNameTwice"/> that asks only the words and their numbers.</summary>
    private const string OneNumberTwice =
        """
        origin.strong_number IS NOT NULL
        AND (SELECT count(*) FROM link_word ours
             WHERE ours.link_id = mine.link_id AND ours.side = mine.side) > 1
        AND (SELECT count(*) FROM link_word theirs
             WHERE theirs.link_id = mine.link_id AND theirs.side <> mine.side) > 1
        AND NOT EXISTS (
            SELECT 1 FROM link_word ours
            JOIN word said ON said.id = ours.word_id
            WHERE ours.link_id = mine.link_id AND ours.side = mine.side
              AND said.strong_number IS DISTINCT FROM origin.strong_number)
        """;

    /// <summary>
    /// Whether every word on the seed's side of the set that names anybody names the seed's entity.
    ///
    /// <para>
    /// One number can cover two records on that side. Joshua 19:47 writes דן four times under
    /// H1835 — twice for the tribe, once for the town and once for the man their father — and
    /// Genesis 43:32 writes מִּצְרִים twice and מִצְרָיִם once under H4713, the Egyptians and Egypt.
    /// Opposite such a set, which word renders which is who is named, and a set read as one name
    /// written several times put the tribe on the Synodal's town and on its father, and the place on
    /// Luther's <em>Ägyptern</em>, which already named the people. Its head is no better, being one
    /// more of the same choice: the father is the Synodal's last <em>Дана</em>. So such a set names
    /// nothing, and the page says nothing rather than something wrong.
    /// </para>
    ///
    /// <para>
    /// The seeds a pass has not yet settled are asked as well as the annotations already written,
    /// because a pass takes its own rows back before it seeds them again.
    /// </para>
    /// </summary>
    private const string NamesOne =
        """
        NOT EXISTS (
            SELECT 1 FROM link_word ours
            JOIN word_entity named ON named.word_id = ours.word_id
            WHERE ours.link_id = mine.link_id AND ours.side = mine.side
              AND named.entity_id <> seed.entity_id)
        AND NOT EXISTS (
            SELECT 1 FROM link_word ours
            JOIN pending_annotation named ON named.word_id = ours.word_id
            WHERE ours.link_id = mine.link_id AND ours.side = mine.side
              AND named.through IS NULL
              AND named.entity_id <> seed.entity_id)
        """;

    /// <summary>
    /// How alike two words of one text must be before they are taken to be the same word in two
    /// inflections, which is what the several words opposite a repeated name are.
    ///
    /// <para>
    /// It is not <see cref="Convincing"/> and must not be: that compares a word against a citation
    /// form the encyclopedia records in another language, and this compares two spellings of one
    /// word in one language, where the stem is most of the string and only the ending differs.
    /// Measured over the sets the corpus actually holds, the two distributions do not overlap and
    /// do not come close to it — <em>Давид</em> against <em>Давиду</em> is 0.63, <em>Египет</em>
    /// against <em>Египте</em> 0.40, <em>Дана</em> against <em>Даном</em> 0.38, while every word
    /// that is not the name scores zero: the <em>к</em> of Joshua 19:13 against <em>Риммону</em>,
    /// the <em>sister's</em> of Acts 23:16 against <em>Paul's</em>, and Luther's <em>mir</em>,
    /// <em>warst</em>, <em>alles</em>, <em>uns</em>, <em>nicht</em> and <em>hatte</em> against the
    /// name beside them. The line is put where the gap is and not at either edge of it.
    /// </para>
    ///
    /// <para>
    /// Written as text rather than as a number because it is spliced into SQL, and a double
    /// formatted under a comma locale is not a number Postgres can read.
    /// </para>
    /// </summary>
    private const string SameWord = "0.3";

    /// <summary>
    /// The words of the other side a naming lands on: <see cref="Head"/>, and beside it every other
    /// word of the set that is the same word as the head, where the link pairs one name written
    /// several times with several words.
    ///
    /// <para>
    /// Both halves are needed. Without the first, a set names one <em>Давид</em> of three. Without
    /// the second, it names whatever else the numbering swept into the set — the Synodal tags the
    /// preposition <em>к</em> with Rimmon's number at Joshua 19:13, the King James tags
    /// <em>sister's</em> with Paul's at Acts 23:16, and Luther's tagging is looser still. A word of
    /// the set that is the name again is the thing the head rule was picking between; a word that
    /// is not is the thing the head rule was protecting the page from.
    /// </para>
    ///
    /// <para>
    /// The step that takes a carried row back asks <see cref="MayHaveReached"/>, which is never
    /// fewer words than this: a withdrawal that found fewer words than the carry wrote would leave
    /// the King James saying what the Hebrew beside it no longer says.
    /// </para>
    /// </summary>
    public const string Reached =
        $"""
         SELECT alone.word_id
         FROM ({Head}) alone
         WHERE NOT ({OneNumberTwice})
         UNION ALL
         {Together}
         WHERE {OneNameTwice}
           AND {SameAsHead}
         """;

    /// <summary>
    /// Every word <see cref="Reached"/> can have reached from a seed, whatever the seed's side names
    /// now, which is what a withdrawal takes back. What that side names changes between a carry and
    /// the withdrawal of what it wrote, and it only ever takes words away from what the numbers
    /// alone admit, so those are all the words the carry can have written.
    /// </summary>
    public const string MayHaveReached =
        $"""
         SELECT alone.word_id
         FROM ({Head}) alone
         WHERE NOT ({OneNumberTwice})
         UNION ALL
         SELECT alone.word_id
         FROM ({AnyHead}) alone
         WHERE NOT ({OneNumberTwice})
         UNION ALL
         {Together}
         WHERE {OneNumberTwice}
           AND {SameAsHead}
         UNION ALL
         {TogetherWithAnyHead}
         WHERE {OneNumberTwice}
           AND {SameAsHead}
         """;

    private const string Together =
        $"""
         SELECT together.id
         FROM ({Head}) alone
         JOIN word foremost ON foremost.id = alone.word_id
         JOIN link_word beside
              ON beside.link_id = mine.link_id AND beside.side <> mine.side
         JOIN word together ON together.id = beside.word_id
         """;

    private const string TogetherWithAnyHead =
        $"""
         SELECT together.id
         FROM ({AnyHead}) alone
         JOIN word foremost ON foremost.id = alone.word_id
         JOIN link_word beside
              ON beside.link_id = mine.link_id AND beside.side <> mine.side
         JOIN word together ON together.id = beside.word_id
         """;

    private const string SameAsHead =
        $"""
         similarity(
             lower(regexp_replace(together.text, '{Ornament}', '', 'g')),
             lower(regexp_replace(foremost.text, '{Ornament}', '', 'g'))) >= {SameWord}
         """;

    /// <summary>
    /// The words a seed reached in a verse beside its rendering that are not the name at all: a word
    /// the text writes without a capital, reached from a seed that also reached, in the same verse of
    /// the same text, a word the text only ever writes with one, and not the same word as it.
    ///
    /// <para>
    /// An aligner that cannot place a word attaches it to what it can score, and a name is what it
    /// scores best. Confidence does not tell those words apart: the Ukrainian <em>від</em> of Numbers
    /// 26:20 was reached from the Shelanites at 0.93, one word after <em>Шелин</em> at 0.98, and the
    /// Russian <em>из</em> of 1 Kings 7:14 from the Tyrian at 0.86, ten words after the rendering.
    /// What does tell them apart is the page: the rendering is written as a name and the leftover is
    /// written as the preposition it is, everywhere else in the book.
    /// </para>
    ///
    /// <para>
    /// A word written without a capital keeps the name where it is the only thing the seed reached in
    /// its verse, or where nothing reached beside it is written as a name — so the Ukrainian gentilics,
    /// which that text writes in lower case, keep theirs. So does a word the text capitalises here,
    /// in the middle of a sentence, whatever it does elsewhere: <em>Господа Бога</em> renders the
    /// divine name in two words, and the <em>Хору</em> of <em>Хору Ґідґаду</em> is half a place. A word that is the same word as a capitalised
    /// word beside it (<see cref="SameWord"/>) is a second rendering of it and keeps it too. A text that
    /// writes no capitals at all has no word written as a name, and nothing in it is taken back.
    /// </para>
    ///
    /// <para>
    /// Asked of every row <see cref="Carry"/> reached, as the <c>leftover</c> flag of <c>judged</c>,
    /// rather than as a list of words joined back: the words beside a word are the other rows of its
    /// seed, text and verse, gathered by a window over them.
    /// </para>
    /// </summary>
    public static readonly string Leftover =
        $"""
         written AS MATERIALIZED (
             SELECT spelled.text_id, spelled.normalised_text,
                    EXISTS (SELECT 1 FROM word lower_case
                            WHERE lower_case.text_id = spelled.text_id
                              AND lower_case.normalised_text = spelled.normalised_text
                              AND lower_case.text !~ '^[[:upper:]]') AS lower
             FROM (SELECT DISTINCT text_id, normalised_text FROM beside WHERE crowded) spelled
         ),
         word_in_crowd AS (
             SELECT b.*,
                    lower(regexp_replace(b.spelling, '{Ornament}', '', 'g')) AS bare,
                    b.spelling ~ '^[[:upper:]]' AS capital,
                    b.spelling ~ '^[[:upper:]]' AND NOT b.opens AS capitalised_here,
                    b.crowded AND coalesce(written.lower, FALSE) AS lower
             FROM beside b
             LEFT JOIN written ON written.text_id = b.text_id
                  AND written.normalised_text = b.normalised_text
         ),
         crowd AS (
             SELECT c.*,
                    bool_or(c.capital AND NOT c.lower) OVER verse AS named_beside,
                    array_agg(c.word_id) FILTER (WHERE c.capital) OVER verse AS capitals,
                    array_agg(c.bare) FILTER (WHERE c.capital) OVER verse AS capital_bares
             FROM word_in_crowd c
             WINDOW verse AS (PARTITION BY c.through, c.text_id, c.verse_id)
         ),
         judged AS (
             SELECT c.*,
                    c.lower AND NOT c.capitalised_here AND c.named_beside
                        AND NOT EXISTS (
                            SELECT 1 FROM unnest(c.capitals, c.capital_bares) twin(word_id, bare)
                            WHERE twin.word_id <> c.word_id
                              AND similarity(twin.bare, c.bare) >= {SameWord}) AS leftover
             FROM crowd c
         )
         """;

    /// <summary>
    /// The same annotations on the words each link says stand for one of the seeded words — by
    /// <see cref="Reached"/>, so the head of a phrase and every word of a repeated name alike.
    ///
    /// A word reached from two seeded words that name two different entities is left alone: the
    /// links disagree about who is named, and picking between them is the judgement none of these
    /// loaders makes. Where they agree, the strongest link decides, because being reached twice is
    /// not weaker than being reached once.
    ///
    /// <para>
    /// A seed a person settled carries no confidence, and crossing a link that is itself certain
    /// leaves it that way — the person decided who is named, and a mapping the translators state
    /// does not make that less true. Crossing a link that is not certain does add a number, because
    /// then the annotation is only as sure as the correspondence it travelled along.
    /// </para>
    ///
    /// <para>
    /// A faint link is refused where the name is already rendered firmly in the same verse of the
    /// same text. An aligner that cannot place a word attaches it to whatever it can score, and in a
    /// verse that names somebody that is often the name: the Russian <em>От</em> opening Genesis
    /// 10:13 was reached from Ludim at 0.69 while <em>Лудим</em> itself renders the word at 0.90
    /// two places later. The comparison is per seed word and per verse, before unanimity, so a
    /// leftover can neither stand beside the rendering nor veto it by disagreeing.
    /// </para>
    ///
    /// <para>
    /// A person's name is not carried onto a word the text writes as somebody else's name
    /// (<see cref="ForeignNames"/>): a link that pairs <em>Христа</em> with <em>Σατανᾶν</em> is
    /// wrong, and the name would be wrong with it. It is asked before unanimity, so the refused
    /// answer neither stands nor vetoes another.
    /// </para>
    ///
    /// <para>
    /// The note names the text the seed word stands in rather than a text the caller declares, so
    /// one pass can carry seeds from several witnesses and each row still says where it came from.
    /// Takes <c>@faint</c> and <c>@firm</c>; <see cref="CarryAcrossLinks"/> supplies both.
    /// </para>
    ///
    /// <para>
    /// Everything asked of a verse or of a word is a window over the rows reached, never a join of
    /// them to themselves. The planner cannot see inside a CTE, so it takes such a join to match
    /// one row and answers it with a nested loop, which is quadratic in the links reached — and one
    /// source's seeds reach hundreds of thousands of words, a dozen texts over for every Hebrew one.
    /// </para>
    /// </summary>
    public static readonly string Carry =
        $"""
        WITH reached AS (
            SELECT other.word_id,
                   w.text_id,
                   w.verse_id,
                   w.text AS spelling,
                   w.normalised_text,
                   before.id IS NULL OR before.trailer ~ '[.!?]' AS opens,
                   seed.entity_id,
                   crossed.worth AS link,
                   CASE WHEN seed.confidence IS NULL
                        THEN nullif(crossed.worth, 1.0)
                        ELSE seed.confidence * crossed.worth END AS confidence,
                   CASE WHEN seed.claim_confidence IS NULL THEN NULL
                        ELSE seed.claim_confidence * crossed.worth END AS claim_confidence,
                   seed.method,
                   seed.source,
                   l.method AS link_method,
                   seed.word_id AS through,
                   witness.slug AS spoken_by,
                   {CommonWord("w", "wt", "seed.entity_id")} AS common
            FROM pending_annotation seed
            JOIN word origin ON origin.id = seed.word_id
            JOIN text witness ON witness.id = origin.text_id
            JOIN link_word mine ON mine.word_id = seed.word_id
            JOIN link l ON l.id = mine.link_id
            CROSS JOIN LATERAL (SELECT {LinkWorth} AS worth) crossed
            CROSS JOIN LATERAL ({Reached}) other
            JOIN word w ON w.id = other.word_id
            JOIN text wt ON wt.id = w.text_id
            LEFT JOIN word before ON before.verse_id = w.verse_id AND before.position = w.position - 1
            WHERE seed.through IS NULL
        ),
        beside AS MATERIALIZED (
            SELECT r.*,
                   max(r.link) OVER verse AS best,
                   min(r.word_id) OVER verse <> max(r.word_id) OVER verse AS crowded
            FROM reached r
            WHERE NOT r.common
            WINDOW verse AS (PARTITION BY r.through, r.text_id, r.verse_id)
        ),
        {Leftover},
        {ForeignNames.Refusals("judged")},
        supported AS (
            SELECT j.*,
                   min(j.entity_id) OVER word = max(j.entity_id) OVER word AS unanimous
            FROM judged j
            WHERE (j.link >= @faint OR j.best < @firm) AND NOT j.leftover
              AND NOT EXISTS (SELECT 1 FROM foreign_refused refused
                              WHERE refused.word_id = j.word_id AND refused.entity_id = j.entity_id
                                AND refused.through = j.through)
            WINDOW word AS (PARTITION BY j.word_id)
        ),
        strongest AS (
            SELECT DISTINCT ON (r.word_id) r.*
            FROM supported r
            WHERE r.unanimous
            ORDER BY r.word_id, coalesce(r.confidence, 1.0) DESC, r.through
        )
        INSERT INTO pending_annotation
            (word_id, entity_id, confidence, claim_confidence, corroborated, method, source, note, through, link)
        SELECT s.word_id, s.entity_id, s.confidence, s.claim_confidence, agreed.named, s.method, s.source,
               'through ' || s.spoken_by || ' word ' || s.through || ', linked by ' || s.link_method,
               s.through, s.link
        FROM strongest s
        CROSS JOIN LATERAL (SELECT EXISTS (
            SELECT 1 FROM verse_reference r
            JOIN entity_verse ev ON ev.entity_id = s.entity_id
                 AND ev.canonical_book = r.canonical_book
                 AND ev.canonical_chapter = r.canonical_chapter
                 AND ev.canonical_verse = r.canonical_verse
            WHERE r.verse_id = s.verse_id AND r.is_primary) AS named) agreed
        ON CONFLICT (word_id) DO NOTHING
        """;

    /// <summary>
    /// The conclusion. A word that already names this entity keeps the row it has: another method
    /// arriving at the same answer is corroboration, and it belongs in the claims rather than in a
    /// second conclusion saying the same thing twice.
    /// </summary>
    public const string Settle =
        """
        INSERT INTO word_entity (word_id, entity_id, method, confidence, source, note)
        SELECT a.word_id, a.entity_id, @method, a.confidence, @source, a.note
        FROM pending_annotation a
        ON CONFLICT (word_id, entity_id) DO NOTHING
        """;

    /// <summary>
    /// What asserted it, written in the same transaction as the conclusion. An annotation nothing
    /// claims is invisible to every measure of agreement, which is the failure the link claims were
    /// already caught by once — and here it would also be an annotation with no model, no prompt
    /// version and no date, which is the one thing a reading must never be stored without.
    ///
    /// <para>
    /// It joins on the conclusion rather than being written beside it, so a reading that agreed with
    /// an annotation already there lands as a second claim on the existing row instead of being
    /// dropped by the conflict clause above.
    /// </para>
    /// </summary>
    public const string Claim =
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT a.id, @method, w.confidence, @source, w.note
        FROM word_entity a
        JOIN pending_annotation w ON w.word_id = a.word_id AND w.entity_id = a.entity_id
        ON CONFLICT DO NOTHING
        """;

    /// <summary>
    /// The verse list's own claim, wherever it independently names the same entity in the same
    /// verse.
    ///
    /// What it records is exactly what the source states — that the entity is named somewhere in
    /// this verse — so it is testimony and carries no confidence. Reaching from there to the word
    /// is the step the annotation's own number is for, and that number is deliberately not raised
    /// by agreement here: the list is known to put verses on the wrong man, so a reading agreeing
    /// with a wrong entry is a shared error rather than a better answer.
    /// </summary>
    public const string Corroboration =
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT a.id, @method, NULL, @source, @note
        FROM word_entity a
        JOIN pending_annotation w ON w.word_id = a.word_id AND w.entity_id = a.entity_id
        WHERE w.corroborated
        ON CONFLICT DO NOTHING
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
                    row_number() OVER settling AS place,
                    count(*) OVER (PARTITION BY s.word_id) AS claims,
                    lead(s.entity_id) OVER settling AS next_entity,
                    lead(s.kind) OVER settling AS next_kind,
                    lead(s.standing) OVER settling AS next_standing,
                    lead(s.confidence) OVER settling AS next_confidence,
                    lead(s.own) OVER settling AS next_own
             FROM standing s
             WINDOW settling AS (
                 PARTITION BY s.word_id
                 ORDER BY s.own DESC, s.standing DESC, s.confidence DESC, s.kind = '{Title}', s.kind COLLATE "C", s.entity_id)
         ),
         several AS MATERIALIZED (
             SELECT r.word_id, r.entity_id, r.kind, r.place FROM ranked r WHERE r.claims > 1
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
                    OR {Beside("r.kind", "r.entity_id", "r.next_kind", "r.next_entity")})
         )
         """;

    /// <summary>
    /// Every entity each word names, as the common table <c>named (word_id, entity_id)</c>, to be
    /// written after <see cref="Settled"/>: the settled answer, and each other annotation of the
    /// word that stands beside it. It is the rule the reader is shown a word by, asked of the whole
    /// corpus, and what a verse list is read through, so that a word shown naming a title and its
    /// bearer puts the verse on both pages.
    /// </summary>
    public static readonly string Named =
        $"""
         named AS (
             SELECT word_id, entity_id FROM settled
             UNION ALL
             SELECT other.word_id, other.entity_id
             FROM settled first
             JOIN several top ON top.word_id = first.word_id AND top.place = 1
             JOIN several other ON other.word_id = first.word_id AND other.entity_id <> first.entity_id
             WHERE other.place = 1
                OR {Beside("top.kind", "top.entity_id", "other.kind", "other.entity_id")}
         )
         """;

    /// <summary>A property and not a field, because the statements above are fields and are built first.</summary>
    private static string Title => EnumSpelling.Of(EntityKind.Title);

    /// <summary>
    /// Whether one word names both records without either answering for the other: a title and a
    /// record joined to it as its bearer, or a person and a people. Two men on one word, or a man
    /// and a town, are two answers to one question, and the lower is the one the higher outranked.
    /// </summary>
    private static string Beside(string kind, string entity, string otherKind, string otherEntity) =>
        $"""
         (({kind} = '{EnumSpelling.Of(EntityKind.Person)}' AND {otherKind} = '{EnumSpelling.Of(EntityKind.People)}')
          OR ({kind} = '{EnumSpelling.Of(EntityKind.People)}' AND {otherKind} = '{EnumSpelling.Of(EntityKind.Person)}')
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
              WHERE held.word_entity_id = a.id AND held.method <> '{EnumSpelling.Of(LinkMethod.StatedBySource)}'), 0))
         """;

    private static string StandingOf(string method) =>
        $"CASE {method} "
        + string.Concat(Enum.GetValues<LinkMethod>().Select(each =>
            $"WHEN '{EnumSpelling.Of(each)}' THEN "
            + ClaimStanding.Of(each).ToString(CultureInfo.InvariantCulture) + " "))
        + "ELSE 0 END";

    /// <summary>
    /// The seed, sent as a binary copy rather than as thousands of parameterised inserts. Ten
    /// thousand round trips is a minute of start-up on a corpus that already takes long enough.
    /// </summary>
    public static async Task Seed(
        NpgsqlConnection connection,
        IEnumerable<(long WordId, int EntityId, double? Confidence, bool Corroborated, string Note)> rows,
        CancellationToken cancellationToken)
    {
        await using var writer = await connection.BeginBinaryImportAsync(
            "COPY pending_annotation (word_id, entity_id, confidence, corroborated, note) FROM STDIN (FORMAT BINARY)",
            cancellationToken);

        foreach (var (wordId, entityId, confidence, corroborated, note) in rows)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(wordId, NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(entityId, NpgsqlDbType.Integer, cancellationToken);
            if (confidence is { } sure)
            {
                await writer.WriteAsync(sure, NpgsqlDbType.Double, cancellationToken);
            }
            else
            {
                await writer.WriteNullAsync(cancellationToken);
            }

            await writer.WriteAsync(corroborated, NpgsqlDbType.Boolean, cancellationToken);
            await writer.WriteAsync(note, NpgsqlDbType.Text, cancellationToken);
        }

        await writer.CompleteAsync(cancellationToken);
    }

    /// <summary>
    /// Whether the encyclopedia's own verse list names each seeded entity in each seeded word's
    /// verse, asked of the whole seed at once rather than per word.
    /// </summary>
    public const string MarkCorroboration =
        """
        UPDATE pending_annotation a SET corroborated = TRUE
        FROM word w, verse_reference r, entity_verse ev
        WHERE w.id = a.word_id
          AND r.verse_id = w.verse_id AND r.is_primary
          AND ev.entity_id = a.entity_id
          AND ev.canonical_book = r.canonical_book
          AND ev.canonical_chapter = r.canonical_chapter
          AND ev.canonical_verse = r.canonical_verse
        """;

    /// <summary>
    /// What a link is worth to a name carried across it: its own confidence, or, where it pairs one
    /// word with one word, the best of the claims standing on it.
    ///
    /// <para>
    /// The row carries the confidence of the method with the highest standing, and that is not
    /// always the surest number on it. The Synodal's numbering pairs a name written twice in a verse
    /// in the order both texts write it, at 0.70, and where the aligner had already paired the same
    /// two words its 0.97 is folded in as a claim on that link: 25,703 of the 33,678 such links in
    /// Russian and Hebrew hold one. Two methods arriving at one pair are not less sure than the
    /// better of them, and multiplying the name by the weaker would draw both of Lamech's names in
    /// Genesis 4:23 below the line a reader is told to doubt.
    /// </para>
    ///
    /// <para>
    /// Only where the link is one word on each side. A link naming several words says they
    /// correspond as a set, and a claim folded into it may be about any pair within the set, so it
    /// cannot vouch for the one word a name lands on. A set of one number on both sides is not an
    /// exception to that — <see cref="OneNameTwice"/> has what it is and is not evidence of.
    /// </para>
    /// </summary>
    public const string LinkWorth =
        """
        CASE WHEN l.confidence IS NULL THEN 1.0
             WHEN (SELECT count(*) FROM link_word pair WHERE pair.link_id = l.id) = 2
             THEN greatest(l.confidence, coalesce(
                  (SELECT max(agreeing.confidence) FROM link_claim agreeing
                   WHERE agreeing.link_id = l.id), 0))
             ELSE l.confidence END
        """;

    /// <summary>
    /// What a pass wrote under a source it no longer writes under, taken back: the rows it concluded that
    /// nothing else claims, then its claims on rows something else also claims, which stand on those
    /// (<see cref="StandOnTheRest"/>). Takes <c>@former</c>.
    /// </summary>
    private const string WithdrawFormer =
        """
        DELETE FROM word_entity a
        WHERE a.source = @former
          AND NOT EXISTS (SELECT 1 FROM word_entity_claim c WHERE c.word_entity_id = a.id AND c.source <> @former)
        """;

    /// <summary>
    /// A row the former source concluded that another pass also claims stands on that claim now: it
    /// takes the claim's source, method, confidence and note, so the carry and every later pass read
    /// it as that pass's row and not as one of a source that has been taken back.
    /// </summary>
    private const string StandOnTheRest =
        """
        UPDATE word_entity a
        SET source = rest.source, method = rest.method, confidence = rest.confidence, note = rest.note
        FROM (SELECT DISTINCT ON (c.word_entity_id) c.word_entity_id, c.source, c.method, c.confidence, c.note
              FROM word_entity_claim c
              JOIN word_entity owner ON owner.id = c.word_entity_id AND owner.source = @former
              WHERE c.source <> @former
              ORDER BY c.word_entity_id, c.method = 'stated-by-source', coalesce(c.confidence, 1.0) DESC, c.id) rest
        WHERE a.id = rest.word_entity_id
        """;

    private const string WithdrawFormerClaims = "DELETE FROM word_entity_claim c WHERE c.source = @former";

    /// <summary>
    /// Everything written under <paramref name="former"/> taken back, a row another pass also claims
    /// kept on that pass's claim. Returns the rows deleted.
    /// </summary>
    public static async Task<int> Withdraw(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string former,
        CancellationToken cancellationToken)
    {
        var withdrawn = 0;
        foreach (var statement in new[] { WithdrawFormer, StandOnTheRest, WithdrawFormerClaims })
        {
            await using var command = new NpgsqlCommand(
                statement, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
            command.Parameters.AddWithValue("former", former);
            var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
            withdrawn = statement == WithdrawFormer ? deleted : withdrawn;
        }

        return withdrawn;
    }

    /// <summary><see cref="Carry"/>, with the two thresholds it compares links against.</summary>
    public static async Task CarryAcrossLinks(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await Run(connection, transaction, ForeignNames.Prepare, cancellationToken);
        await Run(connection, transaction, CommonWords, cancellationToken);
        await Run(connection, transaction, Carry, cancellationToken, ("faint", Faint), ("firm", Firm));
    }

    public static async Task Run(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(
            sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// What each text ended up with, counted by source rather than by method: two things here write
    /// annotations a person settled, and a total that mixed them would credit one with the other's
    /// work.
    /// </summary>
    public static async Task<IReadOnlyList<(string Text, int Words)>> ByText(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string source,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT t.slug, count(*) FROM word_entity a JOIN word w ON w.id = a.word_id " +
            "JOIN text t ON t.id = w.text_id WHERE a.source = @source GROUP BY 1 ORDER BY 2 DESC",
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("source", source);

        var counts = new List<(string, int)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts.Add((reader.GetString(0), (int)reader.GetInt64(1)));
        }

        return counts;
    }
}
