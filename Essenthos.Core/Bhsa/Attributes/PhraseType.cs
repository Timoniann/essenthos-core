namespace Essenthos.Core.Bhsa.Attributes;

public class PhraseType : StringEnum<PhraseType>
{
    public static readonly PhraseType VerbalPhrase = new("VP");
    public static readonly PhraseType NominalPhrase = new("NP");
    public static readonly PhraseType ProperNounPhrase = new("PrNP");
    public static readonly PhraseType AdverbialPhrase = new("AdvP");
    public static readonly PhraseType PrepositionalPhrase = new("PP");
    public static readonly PhraseType ConjunctivePhrase = new("CP");
    public static readonly PhraseType PersonalPronounPhrase = new("PPrP");
    public static readonly PhraseType DemonstrativePronounPhrase = new("DPrP");
    public static readonly PhraseType InterrogativePronounPhrase = new("IPrP");
    public static readonly PhraseType InterjectionalPhrase = new("InjP");
    public static readonly PhraseType NegativePhrase = new("NegP");
    public static readonly PhraseType InterrogativePhrase = new("InrP");
    public static readonly PhraseType AdjectivePhrase = new("AdjP");

    private PhraseType(string value, bool external = false) : base(value, external)
    {
    }

    public static implicit operator PhraseType(string value) => Parse(value);
}