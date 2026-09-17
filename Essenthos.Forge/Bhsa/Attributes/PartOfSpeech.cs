namespace Essenthos.Core.Bhsa.Attributes;

/// <summary>
/// The part of speech a word (or rather lexeme) belongs to.
/// This feature is present on objects of type word and lex.
/// The values consist of an abbreviation, here is the explanation:
/// </summary>
public class PartOfSpeech : StringEnum<PartOfSpeech>
{
    public static readonly PartOfSpeech Article = new("art");
    public static readonly PartOfSpeech Verb = new("verb");
    public static readonly PartOfSpeech Noun = new("subs");
    public static readonly PartOfSpeech ProperNoun = new("nmpr");
    public static readonly PartOfSpeech Adverb = new("advb");
    public static readonly PartOfSpeech Preposition = new("prep");
    public static readonly PartOfSpeech Conjunction = new("conj");
    public static readonly PartOfSpeech PersonalPronoun = new("prps");
    public static readonly PartOfSpeech DemonstrativePronoun = new("prde");
    public static readonly PartOfSpeech InterrogativePronoun = new("prin");
    public static readonly PartOfSpeech Interjection = new("intj");
    public static readonly PartOfSpeech NegativeParticle = new("nega");
    public static readonly PartOfSpeech InterrogativeParticle = new("inrg");
    public static readonly PartOfSpeech Adjective = new("adjv");

    protected PartOfSpeech(string value, bool external = false) : base(value, external)
    {
    }

    public static implicit operator PartOfSpeech(string value) => Parse(value);
}