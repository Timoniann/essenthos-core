using Essenthos.Core.Strong;

namespace Essenthos.Core.Loading.Links;

/// <param name="Tag">The number the tagged English word carries.</param>
/// <param name="Greek">Every number the Greek witness carries in that word's verse.</param>
internal readonly record struct NumberOccurrence(string Tag, IReadOnlySet<string> Greek);

/// <param name="Numbers">
/// What to look for in the verse instead. More than one only for a phrase entry, where the
/// concordance has one number and the editions write two words.
/// </param>
/// <param name="Corroborated">
/// The share of this number's failures that the replacement explains. Kept so the load can say how
/// well each admitted redirect held up rather than only that it passed.
/// </param>
internal readonly record struct NumberRedirect(IReadOnlyList<string> Numbers, double Corroborated);

/// <summary>
/// Which of the dictionary's proposed redirects the corpus actually bears out.
///
/// A derivation is a claim about the language and this is a claim about these two texts, so the
/// second is checked rather than assumed. For every number the tagged King James carries that no
/// Greek word in the verse carries, the proposal is asked the only question that matters: is the
/// number it points at in that verse? A redirect that answers yes almost every time is describing
/// what the editions did; one that answers no is describing something else.
///
/// It is what catches the errors nothing else can see. G3450 μοῦ resolves through the dictionary
/// to G3449 μόχθος, a printing error for G3450, and reads as a perfectly well-formed chain: the
/// corpus corroborates it 0 times in 529. G848 αὑτοῦ resolves to G1438 ἑαυτοῦ, which is what the
/// word is, and the editions print αὐτοῦ: 30 times in 835.
/// </summary>
internal static class GreekNumberResolution
{
    /// <summary>
    /// How much of a number's failure a redirect has to explain before it is used.
    ///
    /// The data puts a gap here and nowhere else. Measured over the King James against Nestle 1904,
    /// every proposal the dictionary offers falls on one side or the other of it:
    /// <code>
    ///   0.0%   G3450 μοῦ    → G3449 μόχθος   0/529    a printing error in the derivation
    ///   1.5%   G2400 ἰδού   → G1492 εἴδω     3/198    the editions write ὁράω
    ///   3.6%   G848  αὑτοῦ  → G1438 ἑαυτοῦ  30/835    the editions write αὐτός
    ///   3.8%   G2396 ἴδε    → G1492 εἴδω     1/26
    ///   —————————————————————————————————————————————— the gap
    ///  91.1%   G5127 τούτου → G3778 οὗτος   51/56
    ///  92.9%   G2257 ἡμῶν   → G1473 ἐγώ    354/381
    ///  96.5%   …and 49 more, none of them wrong
    /// </code>
    /// Anywhere in the gap admits the same fifty-three redirects and refuses the same four, so the
    /// line is drawn in the middle of it rather than at the edge of what happens to pass today.
    /// </summary>
    public const double Corroborated = 0.75;

    /// <summary>
    /// Reads the dictionary, adds the suppletion the dictionary cannot state, and returns only what
    /// the occurrences bear out.
    /// </summary>
    public static Dictionary<string, NumberRedirect> Admit(
        IReadOnlyCollection<GreekEntry> dictionary,
        IReadOnlySet<string> attested,
        IEnumerable<NumberOccurrence> occurrences)
    {
        var proposed = GreekFormDerivations.Resolve(dictionary, attested.Contains);
        foreach (var (number, lemma) in GreekSuppletion.Lemmas)
        {
            if (attested.Contains(lemma))
            {
                proposed[number] = [lemma];
            }
        }

        var failures = new Dictionary<string, int>(proposed.Count, StringComparer.Ordinal);
        var explained = new Dictionary<string, int>(proposed.Count, StringComparer.Ordinal);

        foreach (var (tag, greek) in occurrences)
        {
            if (greek.Contains(tag) || !proposed.TryGetValue(tag, out var numbers))
            {
                continue;
            }

            failures[tag] = failures.GetValueOrDefault(tag) + 1;
            if (numbers.All(greek.Contains))
            {
                explained[tag] = explained.GetValueOrDefault(tag) + 1;
            }
        }

        var admitted = new Dictionary<string, NumberRedirect>(failures.Count, StringComparer.Ordinal);
        foreach (var (tag, count) in failures)
        {
            var share = (double)explained.GetValueOrDefault(tag) / count;
            if (share >= Corroborated)
            {
                admitted[tag] = new NumberRedirect(proposed[tag], share);
            }
        }

        return admitted;
    }
}
