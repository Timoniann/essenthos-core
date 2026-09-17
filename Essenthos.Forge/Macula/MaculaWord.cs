namespace Essenthos.Core.Macula;

/// <param name="Book">The canonical ordinal, so Matthew is 40 and nothing downstream has to know
/// that MACULA writes it <c>MAT</c>.</param>
/// <param name="Position">Its index within the verse, from 1, as the <c>ref</c> writes it after the
/// <c>!</c>. This is the whole of the join: it is the same edition, so the same position in the
/// same verse is the same word.</param>
/// <param name="Class">
/// The part of speech: <c>noun</c>, <c>verb</c>, <c>det</c>, <c>conj</c>, <c>pron</c>, <c>prep</c>,
/// <c>adj</c>, <c>adv</c>, <c>ptcl</c>, <c>num</c>, <c>intj</c>. Finer than Nestle's own on the one
/// point this corpus has already been bitten by: an indeclinable numeral is <c>num</c> here and an
/// adjective there, which is how 476 of them came to be reported nominative.
/// </param>
/// <param name="Type">
/// The nominal type, and the reason this dataset is here at all: <c>proper</c> against
/// <c>common</c> on the nouns, and the pronoun kinds — <c>personal</c>, <c>demonstrative</c>,
/// <c>relative</c>, <c>interrogative</c>, <c>indefinite</c>, <c>possessive</c> — on the pronouns.
/// Empty where the word is of a class that has no type, and empty on 196 nouns where the annotation
/// simply has a gap.
/// </param>
/// <param name="Morph">
/// The form code the word carries — <c>N-NSF</c>, <c>V-FAI-3S-ATT</c>. It is Sandborg-Petersen's
/// tag rather than Clear's own, which is to say the very code this corpus's Nestle words already
/// carry, so it is stored as the evidence for the features beside it and never as a second opinion
/// about them.
/// </param>
/// <param name="Surface">
/// The word as MACULA prints it. **Not stored.** It is here so that the placement can be checked
/// against what this corpus prints in the same slot, and so the 193 words where the two copies of
/// Nestle drifted apart can be named in the row's note.
/// </param>
internal sealed record MaculaWord(
    int Book,
    int Chapter,
    int Verse,
    int Position,
    string? Class,
    string? Type,
    string? Lemma,
    string? Morph,
    string? Person,
    string? Number,
    string? Gender,
    string? Case,
    string? Tense,
    string? Voice,
    string? Mood,
    string? Degree,
    string Surface);
