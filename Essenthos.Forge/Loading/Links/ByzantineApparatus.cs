using Essenthos.Core.Byzantine;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading.Links;

/// <param name="Kind">The apparatus letter the link comes from.</param>
/// <param name="From">Robinson's words the note is about.</param>
/// <param name="To">The witness's words of the other reading; empty where that reading has none.</param>
internal sealed record ApparatusLink(char Kind, LinkRelation Relation, IReadOnlyList<long> From, IReadOnlyList<long> To);

/// <param name="Verses">Verses of the apparatus whose words are the loaded text's, word for word.</param>
/// <param name="Refused">Verses whose words are not, whose notes are not read.</param>
/// <param name="Notes">Notes of the kinds asked for, placed on their words, in verses that were laid.</param>
/// <param name="Held">Of those, the notes whose other reading the witness holds at the place.</param>
/// <param name="NotHeld">The notes whose other reading the witness does not hold there, or holds more than once.</param>
/// <param name="Conjectures">Notes giving an editor's conjecture, which no manuscript reads and nothing links.</param>
internal sealed record LaidApparatus(
    IReadOnlyList<ApparatusLink> Links,
    int Verses,
    int Refused,
    int Notes,
    int Held,
    int NotHeld,
    int Conjectures)
{
    public override string ToString() =>
        $"{Verses} verses laid onto the loaded text and {Refused} refused; {Notes} notes, of which the witness holds "
        + $"{Held} at the place ({(Notes == 0 ? 0 : (double)Held / Notes):P1}) and {NotHeld} it does not, and "
        + $"{Conjectures} conjectures left out";
}

/// <summary>
/// Robinson's apparatus laid onto the corpus: his words of each verse onto the Byzantine Textform
/// the corpus loaded from his parse, and each note's other reading onto a loaded witness that prints it.
///
/// <para>
/// **A note says what another edition reads, and the corpus holds other editions.** The Nestle-Aland
/// notes describe the 28th edition, which is not loaded; Nestle 1904 is, and agrees with it in most
/// places and not in all. The Byzantine notes give the reading of the other half of a divided
/// tradition, which the Received Text often prints. So a note is linked only where the witness
/// holds its reading at the place: the reading between the same two words Robinson's text has on
/// either side, once in the verse. Where Nestle 1904 reads with Robinson, or otherwise again, the
/// note is about another edition and links nothing.
/// </para>
/// </summary>
internal static class ByzantineApparatus
{
    /// <param name="apparatus">The apparatus's verses, by canonical address.</param>
    /// <param name="robinson">The loaded Byzantine Textform's words, each verse in order, by canonical address.</param>
    /// <param name="witness">The witness's words, each verse in order, by canonical address.</param>
    /// <param name="kinds">The apparatus letters whose notes describe this witness.</param>
    public static LaidApparatus Lay(
        IReadOnlyDictionary<(int Book, int Chapter, int Verse), CcatVerse> apparatus,
        IReadOnlyDictionary<(int Book, int Chapter, int Verse), IReadOnlyList<CorpusWord>> robinson,
        IReadOnlyDictionary<(int Book, int Chapter, int Verse), IReadOnlyList<CorpusWord>> witness,
        IReadOnlySet<char> kinds)
    {
        var links = new List<ApparatusLink>();
        int verses = 0, refused = 0, notes = 0, held = 0, notHeld = 0, conjectures = 0;

        foreach (var (address, verse) in apparatus.OrderBy(entry => entry.Key))
        {
            if (!robinson.TryGetValue(address, out var loaded) || verse.Words.Count == 0)
            {
                continue;
            }

            if (!loaded.Select(word => GreekLetters.Bare(word.Text)).SequenceEqual(verse.Words.Select(GreekLetters.Bare)))
            {
                refused++;
                continue;
            }

            verses++;
            var other = witness.GetValueOrDefault(address) ?? [];
            var bare = other.Select(word => GreekLetters.Bare(word.Text)).ToList();
            foreach (var note in verse.Notes.Where(note => kinds.Contains(note.Kind)))
            {
                notes++;
                if (note.Conjecture)
                {
                    conjectures++;
                    continue;
                }

                var reading = note.Reading;
                var at = Held(verse, note, reading, bare);
                if (at is null && note.Undoubted.Count < note.Reading.Count)
                {
                    reading = note.Undoubted;
                    at = Held(verse, note, reading, bare);
                }

                if (at is null)
                {
                    notHeld++;
                    continue;
                }

                held++;
                var from = loaded.Skip(note.From).Take(note.Lemma.Count).Select(word => word.Id).ToList();
                var to = other.Skip(at.Value).Take(reading.Count).Select(word => word.Id).ToList();
                var relation = to.Count == 0
                    ? LinkRelation.Expands
                    : note.Lemma.Select(GreekLetters.Bare).SequenceEqual(reading.Select(GreekLetters.Bare))
                        ? LinkRelation.Equals
                        : LinkRelation.Renders;
                links.Add(new ApparatusLink(note.Kind, relation, from, to));
            }
        }

        return new LaidApparatus(links, verses, refused, notes, held, notHeld, conjectures);
    }

    /// <summary>
    /// Where the note's other reading begins in the witness's verse: the reading with Robinson's word
    /// before it and his word after it, found once. A reading whose edition brackets some of its words
    /// as doubtful is looked for with them, then without them, which is how Nestle 1904 often prints it. A note at the head of the verse has no word before
    /// it, and the reading must open the witness's verse; at the end, close it. Null where the witness
    /// does not hold it so, or holds it more than once.
    /// </summary>
    private static int? Held(CcatVerse verse, ApparatusNote note, IReadOnlyList<string> reading, List<string> witness)
    {
        var before = note.From > 0 ? GreekLetters.Bare(verse.Words[note.From - 1]) : null;
        var end = note.From + note.Lemma.Count;
        var after = end < verse.Words.Count ? GreekLetters.Bare(verse.Words[end]) : null;
        if (before is null && after is null && reading.Count == 0)
        {
            return null;
        }

        List<string> needle = [.. before is null ? [] : new[] { before }, .. reading.Select(GreekLetters.Bare), .. after is null ? [] : new[] { after }];
        int? found = null;
        for (var start = 0; start + needle.Count <= witness.Count; start++)
        {
            if ((before is null && start != 0)
                || (after is null && start + needle.Count != witness.Count)
                || !witness.Skip(start).Take(needle.Count).SequenceEqual(needle))
            {
                continue;
            }

            if (found is not null)
            {
                return null;
            }

            found = start + (before is null ? 0 : 1);
        }

        return found;
    }
}
