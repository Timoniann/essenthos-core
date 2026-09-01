namespace Essenthos.Core.Bhsa.Attributes;

public class PhraseDependentPartOfSpeech : StringEnum<PhraseDependentPartOfSpeech>
{
    public static readonly PhraseDependentPartOfSpeech Article = new("art");
    public static readonly PhraseDependentPartOfSpeech Verb = new("verb");
    public static readonly PhraseDependentPartOfSpeech Noun = new("subs");
    public static readonly PhraseDependentPartOfSpeech ProperNoun = new("nmpr");
    public static readonly PhraseDependentPartOfSpeech Adverb = new("advb");
    public static readonly PhraseDependentPartOfSpeech Preposition = new("prep");
    public static readonly PhraseDependentPartOfSpeech Conjunction = new("conj");
    public static readonly PhraseDependentPartOfSpeech PersonalPronoun = new("prps");
    public static readonly PhraseDependentPartOfSpeech DemonstrativePronoun = new("prde");
    public static readonly PhraseDependentPartOfSpeech InterrogativePronoun = new("prin");
    public static readonly PhraseDependentPartOfSpeech Interjection = new("intj");
    public static readonly PhraseDependentPartOfSpeech NegativeParticle = new("nega");
    public static readonly PhraseDependentPartOfSpeech InterrogativeParticle = new("inrg");
    public static readonly PhraseDependentPartOfSpeech Adjective = new("adjv");

    protected PhraseDependentPartOfSpeech(string value, bool external = false) : base(value, external)
    {
    }

    public static implicit operator PhraseDependentPartOfSpeech(string value) => Parse(value);
}