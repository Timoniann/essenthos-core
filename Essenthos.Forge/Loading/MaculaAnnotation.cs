using System.Text.Json;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Macula;

namespace Essenthos.Core.Loading;

/// <summary>
/// Puts MACULA Greek's word-level annotation on Nestle 1904, which is the first source in this
/// corpus that states which Greek words are names instead of leaving it to be worked out.
///
/// <para>
/// Nestle's own morphology says <c>noun</c> 28,394 times and never *proper noun*, so the pass that
/// annotates Greek words with the people and places they name had to build its gate out of the
/// lexicon's capital letter and measure it three ways before it could be trusted. MACULA writes
/// <c>type="proper"</c> on the word — 4,639 of them — and it writes it on these words: the same
/// edition, from the same upstream repository, addressed as <c>MAT 1:1!1</c>.
/// </para>
///
/// <para>
/// **So there is no join, and that is checked rather than asserted.** Both sides hold 7,943 verses
/// and 137,779 words and no verse differs in length. <see cref="Check"/> compares every verse before a
/// row is written and stops on the first that disagrees, because a source whose whole value is that
/// it needs no alignment must not be allowed to quietly acquire one: an off-by-one in Matthew would
/// otherwise put the rest of the book's annotations on the wrong words, each one looking exactly as
/// authoritative as the rest.
/// </para>
///
/// <para>
/// Every row is <see cref="LinkMethod.StatedBySource"/> and carries no confidence, which is the
/// difference from every other second opinion here. Nothing was inferred: MACULA named the word and
/// the word is ours. The <c>note</c> is written only where the two copies of the edition print
/// different letters, so a reader can see which 193 rows rest on a spelling that has drifted.
/// </para>
/// </summary>
internal static class MaculaAnnotation
{
    /// <summary>
    /// Named in every row. CC BY 4.0's one condition is this exact string, given verbatim in the
    /// licence, so the attribution is carried by the data rather than by a file somebody has to
    /// think to read. Which commit it was taken at is in <c>Resources/Macula/LICENCE.md</c>, once,
    /// rather than 137,779 times here.
    /// </summary>
    internal const string Source =
        "MACULA Greek Linguistic Datasets, available at https://github.com/Clear-Bible/macula-greek/";

    /// <param name="Id">The word's key, kept beside the address so the write never re-queries.</param>
    internal sealed record WitnessWord(long Id, int Book, int Chapter, int Verse, int Position, string Surface);

    /// <summary>
    /// That the two really are the same words in the same order, which is the assumption every row
    /// this loader writes rests on.
    ///
    /// It is checked address by address rather than by comparing totals, because two files can hold
    /// the same number of words and disagree about where a verse ends — and the failure that would
    /// cause is the worst kind this corpus can produce: not an error, but 137,779 confident
    /// annotations, most of them on the wrong word.
    /// </summary>
    internal static void Check(WitnessWord[] witness, IReadOnlyList<MaculaWord> annotation)
    {
        if (witness.Length != annotation.Count)
        {
            throw new InvalidDataException(
                $"Nestle 1904 holds {witness.Length} words here and MACULA's table holds " +
                $"{annotation.Count}. This dataset is loaded by position because it is published " +
                "over the same edition, so a difference in the total means it is not. Re-run " +
                "scripts/fetch-macula.ps1; if the counts still differ, the correspondence has to " +
                "be re-measured before anything is written.");
        }

        for (var at = 0; at < witness.Length; at++)
        {
            var ours = witness[at];
            var theirs = annotation[at];

            if (ours.Book != theirs.Book || ours.Chapter != theirs.Chapter
                || ours.Verse != theirs.Verse || ours.Position != theirs.Position)
            {
                throw new InvalidDataException(
                    $"MACULA's word {at + 1} is addressed {theirs.Book} {theirs.Chapter}:{theirs.Verse}" +
                    $"!{theirs.Position} where Nestle 1904's word {at + 1} is " +
                    $"{ours.Book} {ours.Chapter}:{ours.Verse}!{ours.Position}. The two copies of the " +
                    "edition have stopped agreeing about where a verse ends, so loading by position " +
                    "would put the rest of the book's annotations on the wrong words. Nothing was " +
                    "written. Re-run scripts/fetch-macula.ps1, and if the disagreement survives, the " +
                    "two editions need a join rather than an address.");
            }
        }
    }

    /// <summary>
    /// The features, under the keys <c>word.morphology</c> uses so that the two can be compared
    /// without a translation table between them, plus the two this corpus had no way to state.
    ///
    /// <c>class</c> is MACULA's part of speech, which separates the indeclinable numeral from the
    /// adjective, and <c>type</c> is the nominal type — <c>proper</c> against <c>common</c> — which
    /// is the field the whole dataset was fetched for. <c>morph</c> is the form code the annotation
    /// was read from, kept so that a disagreement with the word's own features can be traced to the
    /// reading rather than argued about: it is the same Sandborg-Petersen tag Nestle carries, so
    /// where the two differ one of them has misread a code both were looking at.
    /// </summary>
    internal static string Morphology(MaculaWord word)
    {
        var features = new Dictionary<string, string>(12, StringComparer.Ordinal);

        Add(features, "class", word.Class);
        Add(features, "type", word.Type);
        Add(features, "morph", word.Morph);
        Add(features, "case", word.Case);
        Add(features, "number", word.Number);
        Add(features, "gender", word.Gender);
        Add(features, "tense", word.Tense);
        Add(features, "voice", word.Voice);
        Add(features, "mood", word.Mood);
        Add(features, "person", word.Person);
        Add(features, "degree", word.Degree);

        return JsonSerializer.Serialize(features, MorphologyJson);
    }

    private static readonly JsonSerializerOptions MorphologyJson = new() { WriteIndented = false };

    private static void Add(Dictionary<string, string> features, string name, string? value)
    {
        if (value is not null)
        {
            features[name] = value;
        }
    }

    /// <summary>
    /// Every word that refers back to another, by index into <paramref name="annotation"/>, in the file's order and, for a word
    /// referring to several, in the order MACULA lists them. MACULA points by its own ids; an id that names no word of the
    /// table stops the build rather than being dropped, since it would mean the file is not the one these rows were read from.
    /// </summary>
    internal static IEnumerable<(int Word, int Referent)> Referents(IReadOnlyList<MaculaWord> annotation)
    {
        var at = new Dictionary<string, int>(annotation.Count, StringComparer.Ordinal);
        for (var i = 0; i < annotation.Count; i++)
        {
            at[annotation[i].Id] = i;
        }

        for (var i = 0; i < annotation.Count; i++)
        {
            foreach (var id in (annotation[i].Referents ?? []).Distinct(StringComparer.Ordinal))
            {
                if (!at.TryGetValue(id, out var referent))
                {
                    throw new InvalidDataException(
                        $"MACULA's word {annotation[i].Id} refers to {id}, which is no word of its table. Re-run scripts/fetch-macula.ps1.");
                }

                if (referent != i)
                {
                    yield return (i, referent);
                }
            }
        }
    }

    internal static string Note(WitnessWord witness, MaculaWord annotation) =>
        $"this corpus prints {witness.Surface} where MACULA prints {annotation.Surface}";
}
