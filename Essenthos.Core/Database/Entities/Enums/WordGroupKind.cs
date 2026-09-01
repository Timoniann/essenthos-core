namespace Essenthos.Core.Database.Entities.Enums;

/// <summary>
/// What a span of words is, in the terms the text's own analysis uses.
///
/// These are BHSA's, because BHSA is the first text with a syntax layer. They are not written as
/// BHSA's — a Greek treebank has sentences, clauses and phrases too, and the ones it does not have
/// it simply will not use. A kind nobody populates costs nothing; a table nobody populates is the
/// mistake this schema was rebuilt to avoid.
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
}
