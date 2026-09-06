namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// Which people and places a word of the Hebrew Bible could be naming — the one statement of it,
/// so that the loader that annotates and the harness that measures are answering the same question.
///
/// <para>
/// Everything downstream of this is a choice between candidates: the annotation loader takes a
/// number that answers with exactly one of them, the reading harness shows the list to a model and
/// scores what it picks, and an ambiguity measure counts how long the list is. A list that is
/// wrong in either direction poisons all three, and it was wrong in both.
/// </para>
///
/// <para>
/// <strong>Too many.</strong> The encyclopedia records, on a name, the Strong number of the name —
/// and it records the <em>Hebrew</em> number on a person who appears only in Greek, because Judas
/// is Judah in Greek and Mary is Miriam. That is a true statement about the names and a false one
/// about the text: Judas Iscariot is not who a word of Numbers means. So a candidate has to be
/// reachable from the text being read. Reachable is established, not assumed: the entity is named
/// somewhere in a book that text actually holds. An entity the encyclopedia attests nowhere at all
/// is kept, because no attestation is not the same evidence as an attestation elsewhere, and
/// dropping it would be inventing a fact about the text from a silence in the dataset.
/// </para>
///
/// <para>
/// <strong>Too few.</strong> The Strong number is the join, and the geocoding dataset that supplies
/// nine tenths of the corpus's places carries no Strong numbers at all — so a word BHSA marks as a
/// place had only people to choose from, and the honest answer, that the referent is a place the
/// list does not hold, was the only one left even where the corpus held the place. The join is
/// built here rather than read, from three statements and no guess:
/// </para>
///
/// <list type="number">
/// <item>the geocoding dataset says this place is named in this verse;</item>
/// <item>the King James text of that verse prints the place's own name at a word which the corpus's
/// links say renders one particular Hebrew word;</item>
/// <item>BHSA marks that Hebrew word as a name and gives it a Strong number.</item>
/// </list>
///
/// <para>
/// A name, not a place name. BHSA's name type belongs to the lemma rather than to the occurrence,
/// and a great many places are marked as people because they are named after one — Senaah is a man
/// to BHSA and a settlement near Jericho to everyone else. Reading only the words BHSA calls
/// places would have made the join agree with BHSA about the very question it exists to open.
/// </para>
///
/// <para>
/// What is concluded from them is a fact about the name and not about the verse: <em>this place is
/// called by this Hebrew name</em>. That distinction is what makes it safe. If the dataset has put
/// the wrong Ziph in the verse, the conclusion — that Ziph is H2128 — survives it, because both
/// Ziphs are called Ziph; only a claim about which Ziph this word means would be damaged, and no
/// such claim is made here.
/// </para>
///
/// <para>
/// <strong>Where the evidence divides, nothing is taken.</strong> A King James word that stands
/// opposite more than one Hebrew name is a compound — <em>Jabesh-gilead</em> against Jabesh
/// and Gilead — and nothing in the link says which half is the name. Taking the first would make
/// Jabesh-gilead a candidate for every occurrence of Gilead in the Bible. So the link is refused
/// whole and the place stays unreachable, which is the smaller error and the visible one.
/// </para>
///
/// <para>
/// Measured against the places the other dataset had already joined by hand, the rule reproduced
/// the number stated for 78 of 108, proposed a different one for 10 and none for 20; every one of
/// the 10 is a second true name of the same place that the hand join did not carry — Bethel where
/// only Luz was stated, Jerusalem where only Salem was, Zoar where only Bela was — so on the part
/// that can be checked it has yet to be caught putting a wrong place on a name.
/// </para>
/// </summary>
internal static class EntityCandidates
{
    /// <summary>
    /// The text whose proper nouns are read rather than derived. It is the only one that marks
    /// them, and every other text reaches an entity through the links to this one.
    /// </summary>
    public const string Witness = "bhsa";

    /// <summary>
    /// The rendering the place join is read through. It has to be a text whose words are linked to
    /// the witness's and whose names are the names the geocoding dataset spells its places with,
    /// and the King James is both.
    /// </summary>
    public const string Rendering = "kjv";

    /// <summary>
    /// The books the text being read actually holds, which is the grain reachability is decided at.
    ///
    /// Not the verse: the encyclopedia's own list of verses is the second, independent answer that
    /// corroborates an annotation, and a candidate list filtered by it would be the answer key. The
    /// book says only that the person appears in this part of the Bible at all, which is what the
    /// question needs.
    /// </summary>
    private const string Held =
        """
        SELECT DISTINCT r.canonical_book
        FROM verse v
        JOIN text t ON t.id = v.text_id AND t.slug = @witness
        JOIN verse_reference r ON r.verse_id = v.id AND r.is_primary
        """;

    /// <summary>
    /// The numbers the encyclopedia states outright.
    ///
    /// Only a label that is a single number is read. A comma-joined value is the numbers of the
    /// words of a title, and the words of a title are not the entity's name: taken as one, H3389
    /// stops being Jerusalem and becomes Adonizedek, who is called king of it.
    ///
    /// <para>
    /// A people is never a candidate here, and leaving it out is what keeps this list from getting
    /// worse as the encyclopedia grows. Everything downstream asks whether a number names exactly
    /// one thing, and a word BHSA marks <c>pers</c> or <c>topo</c> can only ever be answered by a
    /// person or a place — so a people sharing the number could never be the answer and could only
    /// take the count from one to two. Judah's tribe carries H3063 and the Chaldeans carry H3778,
    /// and without this the first would make nothing resolvable and the second would quietly drop
    /// fifteen annotations that are right. Whether a reading pass should be <em>offered</em> a
    /// people as a candidate is a different question, and one that would need the names asking
    /// again.
    /// </para>
    /// </summary>
    private const string Stated =
        """
        SELECT DISTINCT n.hebrew_strong_number AS number, n.entity_id
        FROM entity_name n
        JOIN entity e ON e.id = n.entity_id AND e.kind <> 'people'
        WHERE n.hebrew_strong_number IS NOT NULL AND position(',' IN n.hebrew_strong_number) = 0
        """;

    /// <summary>
    /// The numbers the text establishes for a place the geocoding dataset supplied, by the three
    /// statements the class comment sets out. <c>names</c> counts the names on the witness's
    /// side of the link, and only a link carrying one of them is read.
    /// </summary>
    private const string Read =
        """
        WITH placed AS (
            SELECT e.id AS entity_id,
                   lower(regexp_replace(e.name, '[^A-Za-z]', '', 'g')) AS spelling,
                   ev.canonical_book AS book,
                   ev.canonical_chapter AS chapter,
                   ev.canonical_verse AS verse
            FROM entity e
            JOIN entity_verse ev ON ev.entity_id = e.id
            WHERE e.kind = 'place' AND e.open_bible_id IS NOT NULL
        ),
        naming AS (
            SELECT mine.link_id, mine.word_id, mine.side,
                   count(*) OVER (PARTITION BY mine.link_id) AS names
            FROM link_word mine
            JOIN word w ON w.id = mine.word_id
            JOIN text t ON t.id = w.text_id AND t.slug = @witness
            WHERE w.morphology->>'nameType' IS NOT NULL
              AND w.strong_number IS NOT NULL AND position(',' IN w.strong_number) = 0
        ),
        rendered AS (
            SELECT DISTINCT r.canonical_book AS book,
                            r.canonical_chapter AS chapter,
                            r.canonical_verse AS verse,
                            hebrew.strong_number AS number,
                            lower(regexp_replace(english.text, '[^A-Za-z]', '', 'g')) AS spelling
            FROM naming
            JOIN word hebrew ON hebrew.id = naming.word_id
            JOIN verse_reference r ON r.verse_id = hebrew.verse_id AND r.is_primary
            JOIN link_word opposite
                 ON opposite.link_id = naming.link_id AND opposite.side <> naming.side
            JOIN word english ON english.id = opposite.word_id
            JOIN text et ON et.id = english.text_id AND et.slug = @rendering
            WHERE naming.names = 1
        )
        SELECT DISTINCT rendered.number, placed.entity_id
        FROM placed
        JOIN rendered
             ON rendered.book = placed.book
            AND rendered.chapter = placed.chapter
            AND rendered.verse = placed.verse
            AND rendered.spelling = placed.spelling
        """;

    /// <summary>
    /// Every entity a Strong number could be naming in the text being read, with whether the
    /// encyclopedia said so or the corpus worked it out. Takes <c>@witness</c> and
    /// <c>@rendering</c>.
    /// </summary>
    public static readonly string Naming =
        $"""
         WITH held AS ({Held}),
         stated AS ({Stated}),
         read AS ({Read}),
         named AS (
             SELECT number, entity_id, true AS stated FROM stated
             UNION
             SELECT number, entity_id, false FROM read r
             WHERE NOT EXISTS (
                 SELECT 1 FROM stated s WHERE s.number = r.number AND s.entity_id = r.entity_id)
         )
         SELECT named.number, named.entity_id, named.stated
         FROM named
         WHERE NOT EXISTS (SELECT 1 FROM entity_verse ev WHERE ev.entity_id = named.entity_id)
            OR EXISTS (
                SELECT 1 FROM entity_verse ev
                JOIN held ON held.canonical_book = ev.canonical_book
                WHERE ev.entity_id = named.entity_id)
         """;
}
