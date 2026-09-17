namespace Essenthos.Core.MorphGnt;

/// <param name="Book">The New Testament counted from 1, as the address writes it — Matthew is 1.</param>
/// <param name="PartOfSpeech">
/// Two characters, from a closed list of thirteen: <c>N-</c>, <c>V-</c>, <c>RA</c>, <c>RP</c> and
/// the rest. It separates the article and the four pronoun kinds, which Nestle's own vocabulary
/// runs together as <c>det</c> and <c>pron</c>.
/// </param>
/// <param name="Parse">
/// Eight characters, one slot each for person, tense, voice, mood, case, number, gender and degree,
/// with <c>-</c> where the word has none. Fixed width, so a slot is read by position and never by
/// counting hyphens.
/// </param>
/// <param name="Printed">
/// The word as the SBLGNT prints it, punctuation included. **Not loaded.** It is here so the
/// parsing can be matched to the Nestle word it belongs to, and is dropped afterwards — see
/// <c>Resources/MorphGnt/LICENCE.md</c> for why the text columns and the parsing columns are
/// treated differently.
/// </param>
/// <param name="Word">The same, with punctuation stripped. Not loaded, for the same reason.</param>
/// <param name="Normalised">
/// The dictionary form of the word as printed: the elision restored, the movable nu bracketed, the
/// grave read back as an acute. Not loaded, and not what the join compares — <c>οὐκ</c> normalises
/// to <c>οὐ</c> and <c>ἐξ</c> to <c>ἐκ</c>, where Nestle's own normalisation keeps the printed
/// form, so comparing the two normalisations reports 2,351 words as different that are the same
/// word written the same way.
/// </param>
internal sealed record MorphGntWord(
    int Book,
    int Chapter,
    int Verse,
    string PartOfSpeech,
    string Parse,
    string Printed,
    string Word,
    string Normalised,
    string Lemma);
