using Essenthos.Core.Bhsa.Attributes;

namespace Essenthos.Core.Bhsa.Core;

public record PhraseAtom(
    int SlotId,
    int Ordinal,
    PhraseType Type,
    PhraseAtomLinguisticRelation LinguisticRelation,
    PhraseDetermination Determination,
    IList<Word> Words);