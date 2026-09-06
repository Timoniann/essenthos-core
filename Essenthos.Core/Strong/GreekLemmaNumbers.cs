namespace Essenthos.Core.Strong;

/// <summary>
/// The numbers one Greek tagging tradition gives to a form and the other gives to its lemma.
///
/// <para>
/// Strong numbered some words twice over: ὑμεῖς has an entry of its own at G5210 beside σύ at
/// G4771, ἡμεῖς at G2249 beside ἐγώ at G1473, and so do a dozen comparatives and adverbs that are
/// plainly forms of a word he numbered elsewhere. Maurice Robinson tags every one of them under the
/// lemma, so his Westcott-Hort, his two Textus Receptus editions and the Byzantine Textform all
/// write G4771 for ὑμῶν; Nestle 1904's tagging follows him. Tischendorf's eighth edition is the one
/// text here that keeps Strong's own numbering for these, because its analysis was ported from
/// Robinson's and the plural pronouns were deliberately un-normalised in the porting.
/// </para>
///
/// <para>
/// Unhandled, that is 2,729 words of Tischendorf whose number nothing else in the corpus states, so
/// every one of them fails to pair and is then recorded as a variant reading — a wrong answer that
/// looks exactly like a textual difference, which is the failure this project files hardest against.
/// </para>
///
/// <para>
/// Every pair below was read off the corpus rather than off the dictionary: for each number, the
/// word-forms Tischendorf tags with it were looked up in the three Robinson-tagged editions, and
/// each one is listed here only because all of those editions answered with the same single number.
/// Three numbers found no answer and are deliberately absent — G1302 διατί, G2444 ἱνατί and G2067
/// ἔσθησις, 31 words, which the other editions write as two words or as a different word. A
/// redirect there would be a guess, and 31 unpaired words are cheaper than one invented one.
/// </para>
/// </summary>
public static class GreekLemmaNumbers
{
    /// <summary>
    /// Read as "where one edition writes this number, another writes that one for the same word".
    /// The counts are how many words of Tischendorf each pair accounts for.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Lemmatised = new Dictionary<string, string>(
        StringComparer.Ordinal)
    {
        // The personal pronouns, and all but 31 of the words this list exists for.
        ["G5210"] = "G4771",  // ὑμεῖς → σύ, 1,829 words
        ["G2249"] = "G1473",  // ἡμεῖς → ἐγώ, 862 words

        // Comparatives, superlatives and the adverbs made from them, which Strong entered beside
        // the positive rather than under it.
        ["G4056"] = "G4057",  // περισσοτέρως → περισσῶς, 12
        ["G4055"] = "G4053",  // περισσότερος → περισσός, 9
        ["G4054"] = "G4053",  // περισσότερον → περισσός, 3
        ["G197"] = "G199",    // ἀκριβέστερον → ἀκριβῶς, 4
        ["G3397"] = "G3398",  // μικρόν → μικρός, 4
        ["G4119"] = "G4183",  // πλείων → πολύς, 1
        ["G4191"] = "G4190",  // πονηρότερος → πονηρός, 1
        ["G4208"] = "G4206",  // πορρωτέρω → πόρρω, 1

        // Two more of the same shape: a neuter singular and a substantive Strong gave its own
        // entry.
        ["G1400"] = "G1401",  // δοῦλον → δοῦλος, 2
        ["G3063"] = "G3062",  // λοιπόν → λοιπός, 1
    };

    /// <summary>
    /// The number to pair on, which is the lemma's where the two traditions disagree and the
    /// word's own everywhere else. Null passes through, because an untagged word pairs on nothing.
    /// </summary>
    public static string? Of(string? number) =>
        number is null ? null : Lemmatised.GetValueOrDefault(number, number);
}
