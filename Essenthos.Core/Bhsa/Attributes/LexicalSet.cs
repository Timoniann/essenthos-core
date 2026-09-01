using System.Diagnostics.Contracts;

namespace Essenthos.Core.Bhsa.Attributes;

public class LexicalSet : StringEnum<LexicalSet>
{
    public static readonly LexicalSet DistributiveNoun = new("nmdi");
    public static readonly LexicalSet CopulativeNoun = new("nmcp");
    public static readonly LexicalSet PotentialAdverb = new("padv");
    public static readonly LexicalSet AnaphoricAdverb = new("afad");
    public static readonly LexicalSet PotentialPreposition = new("ppre");
    public static readonly LexicalSet ConjunctiveAdverb = new("cjad");
    public static readonly LexicalSet Ordinal = new("ordn");
    public static readonly LexicalSet CopulativeVerb = new("vbcp");
    public static readonly LexicalSet NounOfMultitude = new("mult");
    public static readonly LexicalSet FocusParticle = new("focp");
    public static readonly LexicalSet InterrogativeParticle = new("ques");
    public static readonly LexicalSet Gentilic = new("gntl");
    public static readonly LexicalSet QuotationVerb = new("quot");
    public static readonly LexicalSet Cardinal = new("card");
    public static readonly LexicalSet None = new("none");

    private LexicalSet(string value, bool external = false) : base(value, external)
    {
    }

    public static implicit operator LexicalSet?(string? value)
    {
        // ReSharper disable once InvocationIsSkipped
        Contract.Ensures(
            value == null ? Contract.Result<LexicalSet?>() == null : Contract.Result<LexicalSet?>() != null);
        return value == null ? null : Parse(value);
    }
}