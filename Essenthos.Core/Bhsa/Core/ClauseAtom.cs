using Essenthos.Core.Bhsa.Attributes;

namespace Essenthos.Core.Bhsa.Core;

public record ClauseAtom(
    int SlotId,
    int Ordinal,
    string Type,
    ClauseAtomRelation Relation,
    bool IsRoot,
    // Tabulation. The level of this clause_atom in the enclosing hierarchy.
    int Tab,
    // Hierarchical paragraph number
    string Paragraph,
    string Instruction,
    IList<Word> Words);