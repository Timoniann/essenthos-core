using Essenthos.Core.Bhsa.Attributes;

namespace Essenthos.Core.Bhsa.Core;

public record Subphrase(int SlotId, int Ordinal, SubphraseLinguisticRelation LinguisticRelation, IList<Word> Words);