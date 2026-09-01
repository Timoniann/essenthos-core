using Essenthos.Core.Bhsa.Attributes;

namespace Essenthos.Core.Bhsa.Core;

public record Phrase(
    int SlotId,
    int Ordinal,
    PhraseType Type,
    PhraseFunction Function,
    PhraseDetermination Determination,
    PhraseLinguisticRelation LinguisticRelation,
    IList<Word> Words);