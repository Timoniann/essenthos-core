using Essenthos.Core.Bhsa.Attributes;

namespace Essenthos.Core.Bhsa.Core;

public record PhraseAtom(
    int SlotId,
    int Ordinal,
    PhraseAtomLinguisticRelation LinguisticRelation,
    PhraseDetermination Determination,
    IList<Word> Words);