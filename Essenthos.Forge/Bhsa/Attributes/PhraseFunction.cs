namespace Essenthos.Core.Bhsa.Attributes;

public class PhraseFunction : StringEnum<PhraseFunction>
{
    public static readonly PhraseFunction Adjunct = new("Adju");
    public static readonly PhraseFunction Complement = new("Cmpl");
    public static readonly PhraseFunction Conjunction = new("Conj");
    public static readonly PhraseFunction EncliticPersonalPronoun = new("EPPr");
    public static readonly PhraseFunction ExistenceWithSubjectSuffix = new("ExsS");
    public static readonly PhraseFunction Existence = new("Exst");
    public static readonly PhraseFunction FrontedElement = new("Frnt");
    public static readonly PhraseFunction Interjection = new("Intj");
    public static readonly PhraseFunction InterjectionWithSubjectSuffix = new("IntS");
    public static readonly PhraseFunction Locative = new("Loca");
    public static readonly PhraseFunction Modifier = new("Modi");
    public static readonly PhraseFunction ModifierWithSubjectSuffix = new("ModS");
    public static readonly PhraseFunction NegativeCopula = new("NCop");
    public static readonly PhraseFunction NegativeCopulaWithSubjectSuffix = new("NCoS");
    public static readonly PhraseFunction Negation = new("Nega");
    public static readonly PhraseFunction Object = new("Objc");
    public static readonly PhraseFunction PredicativeAdjunct = new("PrAd");
    public static readonly PhraseFunction PredicateComplementWithSubjectSuffix = new("PrcS");
    public static readonly PhraseFunction PredicateComplement = new("PreC");
    public static readonly PhraseFunction Predicate = new("Pred");
    public static readonly PhraseFunction PredicateWithObjectSuffix = new("PreO");
    public static readonly PhraseFunction PredicateWithSubjectSuffix = new("PreS");
    public static readonly PhraseFunction ParticipleWithObjectSuffix = new("PtcO");
    public static readonly PhraseFunction Question = new("Ques");
    public static readonly PhraseFunction Relative = new("Rela");
    public static readonly PhraseFunction Subject = new("Subj");
    public static readonly PhraseFunction SupplementaryConstituent = new("Supp");
    public static readonly PhraseFunction TimeReference = new("Time");
    public static readonly PhraseFunction Unknown = new("Unkn");
    public static readonly PhraseFunction Vocative = new("Voct");

    private PhraseFunction(string value, bool external = false) : base(value, external)
    {
    }

    public static implicit operator PhraseFunction(string value) => Parse(value);
}