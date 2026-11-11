using Essenthos.Core.TextusReceptus;

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
///
/// <para>
/// <strong>The Greek asks the same question and needs the same discipline.</strong> The
/// encyclopedia carries a Greek Strong number beside the Hebrew one, and it carries it on Old
/// Testament people as freely as the other column carries Hebrew numbers on New Testament ones:
/// sixty-seven kings of Judah, Moab and Assyria are offered as the referent of βασιλεύς, thirteen
/// Zechariahs of Chronicles and Nehemiah for Ζαχαρίας, and nine men called Jeshua for Ἰησοῦς.
/// Reachability is the same rule read the other way round, and it removes 335 of those candidacies
/// over 116 numbers.
/// </para>
/// </summary>
internal static class EntityCandidates
{
    /// <summary>
    /// The record a name row of <c>n</c> resolves to.
    ///
    /// Itself, all but always. Where the row says it bears another record's name — Mount Zion
    /// bearing Zion's, Samaria the country bearing Samaria the city's, the Nile bearing Egypt's —
    /// it is that record, so that a name several records carry still answers with one place. The
    /// corpus check reads it here.
    /// </summary>
    public const string Resolves = "coalesce(n.aspect_of_entity_id, n.entity_id)";
}
