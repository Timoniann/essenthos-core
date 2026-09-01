namespace Essenthos.Core.Bhsa.Attributes;

public class PhraseAtomLinguisticRelation : StringEnum<PhraseAtomLinguisticRelation>
{
    public static readonly PhraseAtomLinguisticRelation Apposition = new("Appo");
    public static readonly PhraseAtomLinguisticRelation SuffixSpecification = new("Sfxs");
    public static readonly PhraseAtomLinguisticRelation Conjunction = new("Link");
    public static readonly PhraseAtomLinguisticRelation Specification = new("Spec");
    public static readonly PhraseAtomLinguisticRelation Parallel = new("Para");
    public static readonly PhraseAtomLinguisticRelation NotApplicable = new("NA");

    private PhraseAtomLinguisticRelation(string value, bool external = false) : base(value, external)
    {
    }

    public static implicit operator PhraseAtomLinguisticRelation(string value) => Parse(value);
}