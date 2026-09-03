namespace Essenthos.Core.Strong;

/// <summary>
/// The twelve numbers the dictionary's own derivations cannot resolve, and why each one is here.
///
/// <para>
/// Two things defeat <see cref="GreekFormDerivations"/>, and both are properties of the
/// concordance rather than of the language. A suppletive verb has no single stem, so Strong's
/// derivation names the numbers the missing tenses were borrowed from and never the lemma an
/// editor would print — G2036 ἔπω says its other tenses come from G2046, G4483 and G5346, and the
/// word Nestle actually writes it under, λέγω, appears nowhere in it. And an oblique paradigm that
/// Strong numbered form by form derives each form from the form beside it rather than from the
/// nominative, so the chain closes on itself before it reaches a lemma.
/// </para>
///
/// <para>
/// Every entry below is still measured against the corpus before it is used: a pair no verse
/// corroborates is not written, whoever asserted it. The list is short on purpose — anything that
/// can be read out of the dictionary is read out of the dictionary.
/// </para>
/// </summary>
public static class GreekSuppletion
{
    /// <summary>
    /// Read as "where the tagged edition's number matches nothing in the verse, this is the number
    /// to try instead". Unlike a derivation these apply to numbers the Greek does use elsewhere:
    /// both editions tag G1492 for οἶδα and G3708 for εἶδον, and the tagged King James writes G1492
    /// for both.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Lemmas = new Dictionary<string, string>(
        StringComparer.Ordinal)
    {
        // εἶπον, the aorist of "say". Strong's own cross-references name G3004 and the printed
        // editions lemmatise every form of it under λέγω.
        ["G2036"] = "G3004",

        // εἴδω. Its entry says the tenses it lacks are "borrowed from the equivalent G3700 and
        // G3708"; the editions keep οἶδα under G1492 and put εἶδον under ὁράω, so this only fires
        // in the verses where G1492 itself is not there.
        ["G1492"] = "G3708",

        // ἰδού and ἴδε, which the dictionary derives from G1492 and the editions tag as the
        // imperatives of ὁράω they are.
        ["G2400"] = "G3708",
        ["G2396"] = "G3708",

        // ὀπτάνομαι, the same paradigm again. Its derivation names the alternation in prose rather
        // than as a form, so the vocabulary test refuses it.
        ["G3700"] = "G3708",

        // αὑτοῦ, the aspirated contraction of ἑαυτοῦ that the tagged edition writes. Both Greek
        // editions here read αὐτοῦ and tag αὐτός. The dictionary sends this to G1438 ἑαυτοῦ, which
        // is what the word is and not what the page says: 30 verses of 835 bear it out.
        ["G848"] = "G846",

        // The oblique cases of the first person singular. Strong numbered each form and derived it
        // from its neighbour — μοῦ from ἐμοῦ, ἐμοῦ from μοῦ — so no chain reaches the nominative.
        // G1700's printed derivation, G3449 μόχθος, is an error for G3450 and sends the whole
        // paradigm to a noun meaning toil, which no verse in the corpus corroborates.
        ["G3450"] = "G1473",
        ["G3427"] = "G1473",
        ["G3165"] = "G1473",
        ["G1700"] = "G1473",
        ["G1698"] = "G1473",
        ["G1691"] = "G1473",
    };
}
