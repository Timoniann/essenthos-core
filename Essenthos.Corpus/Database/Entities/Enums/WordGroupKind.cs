namespace Essenthos.Core.Database.Entities.Enums;

/// <summary>
/// What a span of words is, in the terms the text's own analysis uses.
///
/// Most are BHSA's, because BHSA is the first text with a syntax layer. They are not written as
/// BHSA's — a Greek treebank has sentences, clauses and phrases too, and the ones it does not have
/// it simply will not use. A kind nobody populates costs nothing; a table nobody populates is the
/// mistake this schema was rebuilt to avoid.
///
/// An analysis need not be syntactic to be the text's own. An edition that marks off the words it
/// supplies is analysing its text as surely as a treebank is, and it names a span of words when it
/// does so, which is what this table holds.
/// </summary>
public enum WordGroupKind
{
    Sentence,

    /// <summary>A sentence as it stands in the text, before the parts moved out of order are rejoined.</summary>
    SentenceAtom,

    Clause,

    ClauseAtom,

    Phrase,

    PhraseAtom,

    /// <summary>A phrase inside a phrase — the construct chain, the apposition.</summary>
    Subphrase,

    /// <summary>The Masoretic division of a verse at its main accent, which is not a syntactic unit.</summary>
    HalfVerse,

    /// <summary>
    /// Words the edition prints as its own: the translators put them there and the text they were
    /// translating has no counterpart for them. The Synodal marks 4,247 such spans with square
    /// brackets, the King James marked its own in italics, and the Berean uses brackets and braces.
    ///
    /// It is not <em>a reading taken from the Septuagint</em>, which is what the convention is
    /// usually said to be. Measured over the Synodal's own file: 3,577 of the 4,247 spans are a
    /// single word, the longest is seven, the commonest are the copula <c>был</c>, the pronoun
    /// <c>это</c> and the unit of measure Hebrew leaves implicit in "fifty [shekels] of silver" —
    /// and 884 of them are in the New Testament, where nothing is being compared with the
    /// Masoretic at all. A few of the long ones are genuine Septuagint pluses, and the edition
    /// prints those with the same mark, so this cannot separate them and does not pretend to.
    /// </summary>
    Supplied,
}
