using Essenthos.Core.Bhsa.Attributes;

namespace Essenthos.Core.Bhsa.Core;

public record Clause(
    int SlotId,
    int Ordinal,
    string Type,
    ClauseKind Kind,
    ClauseLinguisticRelation LinguisticRelation,
    ClauseDomain Domain,
    ClauseTextType[] TextTypes,
    IList<Word> Words);