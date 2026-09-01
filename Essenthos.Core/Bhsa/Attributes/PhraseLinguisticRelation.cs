namespace Essenthos.Core.Bhsa.Attributes;

public class PhraseLinguisticRelation : StringEnum<PhraseLinguisticRelation>
{
    public static readonly PhraseLinguisticRelation PredicativeAdjunct = new("PrAd");
    public static readonly PhraseLinguisticRelation Resumption = new("Resu");
    public static readonly PhraseLinguisticRelation NotApplicable = new("NA");

    private PhraseLinguisticRelation(string value, bool external = false) : base(value, external)
    {
    }

    public static implicit operator PhraseLinguisticRelation(string value) => Parse(value);
}