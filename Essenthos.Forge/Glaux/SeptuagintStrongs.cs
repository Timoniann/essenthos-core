using Essenthos.Core.Utils;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.TextusReceptus;

namespace Essenthos.Core.Glaux;

/// <summary>
/// Strong numbers for the Septuagint, proposed rather than stated.
///
/// **Strong never numbered the Greek Old Testament.** The series catalogues New Testament
/// vocabulary, so a number on a Brenton word is always a claim that this word is the same lexeme as
/// some New Testament word — reasoning of ours, from a lemma GLAUx gave us, through a lexicon built
/// for a different book. It is good reasoning and it is not testimony, and the difference is the
/// whole reason these rows go to <c>word_strong</c> and not to <c>word.strong_number</c>. Anything
/// in the Septuagint's own vocabulary — and there is a great deal of it — correctly reaches no
/// number at all, which is a fact about Strong and not a gap in the load.
///
/// <para>
/// The join is lemma to lemma, both folded, because the two lists are written in different
/// conventions and neither is normalised the way the other is. Where a lemma is claimed by more than
/// one entry every candidate is written: the lexicon cannot say which, so neither can we, and a
/// reader who sees two numbers has been told the truth. That is what the table is for.
/// </para>
///
/// <para>
/// Confidence says what the reasoning was worth, not how common the word is. A lemma one entry
/// claims is the ordinary case and sits high; a lemma several claim is divided between them; a lemma
/// reached only by rewriting its citation form sits lower again, because the bridge is a rule about
/// two conventions rather than a fact about this word.
/// </para>
/// </summary>
internal static class SeptuagintStrongs
{
    /// <summary>A lemma one Greek entry claims, matched as GLAUx wrote it.</summary>
    internal const double Single = 0.9;

    /// <summary>
    /// A lemma reached by rewriting the citation form from one convention to the other. Below a
    /// direct match because the rule is about Attic and Koine practice, not about this word.
    /// </summary>
    internal const double BridgedConfidence = 0.75;

    /// <summary>
    /// A lemma several entries claim, divided between them: two candidates are 0.45 each. Below
    /// anything a single match earns however few the candidates, which is the point.
    /// </summary>
    internal static double Shared(int candidates) => Single / candidates;

    internal const string Source =
        "the lemma GLAUx gives the word, matched against Strong's Greek entries by their own lemma";

    /// <summary>
    /// The numbers a folded lemma reaches, and whether it took the bridge to get there. The
    /// unchanged lemma always wins: the bridge is consulted only where the lexicon has nothing,
    /// so a rule can never displace a match GLAUx and Strong already agree on.
    /// </summary>
    internal static (List<string> Numbers, bool Bridged) Numbers(
        Dictionary<string, List<string>> entries,
        string folded)
    {
        if (entries.TryGetValue(folded, out var direct))
        {
            return (direct, false);
        }

        foreach (var candidate in GreekLemmaBridge.Candidates(folded))
        {
            if (entries.TryGetValue(candidate, out var found))
            {
                return (found, true);
            }
        }

        return ([], false);
    }
}
