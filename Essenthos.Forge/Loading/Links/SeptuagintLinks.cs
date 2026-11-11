using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.TextusReceptus;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// Swete's Septuagint against Brenton's, word for word.
///
/// Two Greek editions of one work, which is the pairing this corpus was rebuilt to hold and the one
/// it has been unable to make: Swete arrived linked to nothing at all, and Brenton's deuterocanon —
/// Tobit, Judith, the Maccabees, Sirach, Wisdom — stood against nothing, because BHSA has no such
/// books and BHSA was the only Old Testament witness here.
///
/// <para>
/// Nothing about it is the aligner's problem. Both sides are Greek, so the evidence is the letters
/// each edition prints, and <see cref="WitnessAlignment"/> either finds them or reports that it did
/// not. That is the same method the Samaritan Pentateuch is joined to BHSA by, and it is why these
/// links carry a confidence near certainty where a Slavic translation's carry a model's guess.
/// </para>
///
/// <para>
/// The two do not read the same text and are not meant to. Swete printed Codex Vaticanus as it
/// stands; Brenton printed a text to be translated from. So this writes far more absences than the
/// two Greek New Testaments do against each other, and every one of them is a reading a scholar can
/// ask about rather than a defect in the join.
/// </para>
///
/// <para>
/// **The join is the canonical frame, not the editions' own numbering.** Both declare the
/// Septuagint versification and they still divide their text differently in most of the books they
/// share — that difference is the reason to hold both — so the addresses they agree on are the
/// frame's, which is what the frame loaders placed them at. An address either edition
/// fills with more than one verse is taken whole, so the words of both verses stand in one bag and
/// no link crosses a boundary the frame does not already join.
/// </para>
/// </summary>
internal static class SeptuagintLinks
{
    internal const string Source =
        "the letters both Greek editions print, aligned within each verse of the canonical frame";

    /// <summary>
    /// One address of each edition laid against the other. The alignment decides what corresponds to
    /// what and how sure it is; this only turns its answer into rows.
    /// </summary>
    internal static void Pair(List<GreekWord> left, List<GreekWord> right, List<GreekDraft> drafts)
    {
        var forms = WitnessAlignment.Pair(
            [.. left.Select(w => w.Form)], [.. right.Select(w => w.Form)]);

        foreach (var pairing in forms)
        {
            drafts.Add(new GreekDraft(
                pairing.Relation,
                [.. pairing.From.Select(at => left[at].Id)],
                [.. pairing.To.Select(at => right[at].Id)],
                pairing.Confidence));
        }
    }

    internal sealed record GreekWord(long Id, WitnessForm Form);

    internal sealed record GreekDraft(
        LinkRelation Relation,
        List<long> From,
        List<long> To,
        double Confidence);
}
