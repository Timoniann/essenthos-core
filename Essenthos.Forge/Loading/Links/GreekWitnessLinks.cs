using Essenthos.Core.Utils;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Strong;
using Essenthos.Core.TextusReceptus;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The Greek witnesses to each other: Nestle 1904 against Scrivener's Textus Receptus.
///
/// Until this existed the two Greek panes were dark to one another. Every translation reached
/// both — the King James is linked to Nestle and to Scrivener, and so are the Synodal and the
/// Ukrainian — but Nestle and the Textus Receptus were linked to nothing in common, so hovering a
/// Greek word lit the translations and left the other Greek text alone. Two witnesses of the same
/// sentence, and the reader could compare either against English and neither against the other.
///
/// Scrivener is the hub on purpose. Stephanus is already linked to Scrivener word for word, so
/// linking Nestle to Scrivener alone joins all four Greek panes: a word's witness set carries the
/// ids it reaches, and both sides then hold Scrivener's.
///
/// The pairing is by Strong number within a verse, which both editions state for themselves — no
/// aligner, no statistics. Where a number stands once on each side the pairing is certain enough
/// to say so; where it repeats, the repeats are handed out in order, which is right far more often
/// than not and is recorded at a lower confidence because "far more often than not" is what it is.
///
/// The one thing done to a number before it is compared is <see cref="GreekLemmaNumbers"/>, which
/// is not a heuristic: Strong gave ὑμεῖς an entry beside σύ and most digital editions tag the lemma
/// where Tischendorf's tags the form. Without it 2,729 words of that edition pair with nothing and
/// are then written out as textual variants, which is a stated difference between two editions that
/// do not differ.
/// The words no number answers are then read as variants where they stand at the same place, and
/// that one step is positional rather than stated, so it says <c>lexical</c> and says it cheaply.
/// </summary>
internal static class GreekWitnessLinks
{
    internal const string Source = "the Strong numbers both editions carry, paired within each verse";

    internal const string SubstitutionSource =
        "the words left over once the Strong numbers were paired, matched by their place in the verse";

    /// <summary>
    /// What the pairing rested on where the two editions do not write the same number for the same
    /// word. Said on the link rather than folded into the ordinary source string, because a reader
    /// checking a pairing against the two numbers printed would otherwise find them different and
    /// have nothing to tell them why.
    /// </summary>
    internal const string LemmatisedSource =
        "the Strong numbers both editions carry, paired within each verse under the number each " +
        "concordance gives the lemma";

    /// <summary>A number standing once on each side of the verse. There is nothing to choose between.</summary>
    private const double Unique = 0.95;

    /// <summary>A number standing more than once, handed out in the order it occurs.</summary>
    private const double Repeated = 0.85;

    /// <summary>
    /// Two words the editions do not share, standing at the same place in the verse. Position is
    /// the whole evidence, so this sits below every number match and well above nothing.
    /// </summary>
    private const double Substituted = 0.6;

    /// <summary>
    /// How far apart two leftovers may stand and still be one reading. The editions run word for
    /// word between variants, so a substitution moves a word by one at most; anything further is
    /// two unrelated absences that happened to fall in the same verse.
    /// </summary>
    private const int SamePlace = 1;

    /// <summary>
    /// One verse of each edition, paired by the Strong numbers they both state. A number on one
    /// side only is written positively as an absence rather than left as a hole — that is the
    /// variant, and recording it is the whole point.
    /// </summary>
    internal static void Pair(List<GreekWord> left, List<GreekWord> right, List<GreekDraft> drafts)
    {
        var mine = left.Where(w => w.Strong is not null).GroupBy(w => GreekLemmaNumbers.Of(w.Strong)!)
            .ToDictionary(g => g.Key, g => g.OrderBy(w => w.Position).ToList());
        var yours = right.Where(w => w.Strong is not null).GroupBy(w => GreekLemmaNumbers.Of(w.Strong)!)
            .ToDictionary(g => g.Key, g => g.OrderBy(w => w.Position).ToList());

        var hereOnly = new List<Leftover>();
        var thereOnly = new List<Leftover>();

        foreach (var strong in mine.Keys.Union(yours.Keys))
        {
            var ours = mine.GetValueOrDefault(strong, []);
            var theirs = yours.GetValueOrDefault(strong, []);
            var shared = Math.Min(ours.Count, theirs.Count);
            var certainty = ours.Count == 1 && theirs.Count == 1 ? Unique : Repeated;

            for (var i = 0; i < shared; i++)
            {
                drafts.Add(new GreekDraft(
                    GreekLetters.Same(ours[i].Surface, theirs[i].Surface)
                        ? LinkRelation.Equals
                        : LinkRelation.Renders,
                    [ours[i].Id],
                    [theirs[i].Id],
                    certainty,
                    LinkMethod.StrongNumber,
                    ours[i].Strong == theirs[i].Strong ? Source : LemmatisedSource));
            }

            hereOnly.AddRange(ours.Skip(shared).Select(word => new Leftover(word, certainty)));
            thereOnly.AddRange(theirs.Skip(shared).Select(word => new Leftover(word, certainty)));
        }

        Unpaired(hereOnly, thereOnly, drafts);
    }

    /// <summary>
    /// What is left once the numbers have been paired, which is where the variants are.
    ///
    /// Two leftovers standing at the same place in their verses are one reading written two ways —
    /// Matthew 1:10 has Ἀμώς where Scrivener has αμων, twice — and four independent absences say
    /// that far worse than two substitutions do. The place is the only evidence for it, so the link
    /// says <c>lexical</c> and carries a confidence well below a matched number.
    ///
    /// Everything else stands in one edition and not the other, and the side it is missing from is
    /// what the relation has to name: <c>omits</c> where the first edition lacks the word,
    /// <c>expands</c> where it has one the second does not.
    /// </summary>
    private static void Unpaired(List<Leftover> here, List<Leftover> there, List<GreekDraft> drafts)
    {
        here.Sort((a, b) => a.Word.Position.CompareTo(b.Word.Position));
        there.Sort((a, b) => a.Word.Position.CompareTo(b.Word.Position));

        var substituted = new HashSet<long>();
        for (var i = 0; i < here.Count && i < there.Count; i++)
        {
            if (Math.Abs(here[i].Word.Position - there[i].Word.Position) > SamePlace)
            {
                continue;
            }

            substituted.Add(here[i].Word.Id);
            substituted.Add(there[i].Word.Id);
            drafts.Add(new GreekDraft(
                LinkRelation.Renders,
                [here[i].Word.Id],
                [there[i].Word.Id],
                Substituted,
                LinkMethod.Lexical,
                SubstitutionSource));
        }

        foreach (var leftover in here.Where(l => !substituted.Contains(l.Word.Id)))
        {
            drafts.Add(new GreekDraft(
                LinkRelation.Expands, [leftover.Word.Id], [], leftover.Certainty, LinkMethod.StrongNumber,
                Source));
        }

        foreach (var leftover in there.Where(l => !substituted.Contains(l.Word.Id)))
        {
            drafts.Add(new GreekDraft(
                LinkRelation.Omits, [], [leftover.Word.Id], leftover.Certainty, LinkMethod.StrongNumber,
                Source));
        }
    }

    internal sealed record GreekWord(long Id, int Position, string Surface, string? Strong);

    /// <summary>A word whose number the other edition did not answer, and how sure that is.</summary>
    private sealed record Leftover(GreekWord Word, double Certainty);

    internal sealed record GreekDraft(
        LinkRelation Relation,
        List<long> From,
        List<long> To,
        double Confidence,
        LinkMethod Method,
        string Source);
}
