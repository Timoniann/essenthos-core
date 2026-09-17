namespace Essenthos.Core.Bhsa.Attributes;

/// <summary>
/// For clause-like objects this feature is also called clause constituent relation; it indicates the syntactic function
/// of the clause.
/// </summary>
public class ClauseLinguisticRelation : StringEnum<ClauseLinguisticRelation>
{
    public static readonly ClauseLinguisticRelation Adjunctive = new("Adju");
    public static readonly ClauseLinguisticRelation Attributive = new("Attr");
    public static readonly ClauseLinguisticRelation Complement = new("Cmpl");
    public static readonly ClauseLinguisticRelation Coordinated = new("Coor");
    public static readonly ClauseLinguisticRelation Object = new("Objc");
    public static readonly ClauseLinguisticRelation PredicativeAdjunct = new("PrAd");
    public static readonly ClauseLinguisticRelation PredicativeComplement = new("PreC");
    public static readonly ClauseLinguisticRelation ReferralToTheVocative = new("ReVo");
    public static readonly ClauseLinguisticRelation Resumptive = new("Resu");
    public static readonly ClauseLinguisticRelation RegensRectum = new("RgRc");
    public static readonly ClauseLinguisticRelation Specification = new("Spec");
    public static readonly ClauseLinguisticRelation Subject = new("Subj");
    public static readonly ClauseLinguisticRelation NotApplicable = new("NA");

    protected ClauseLinguisticRelation(string value, bool external = false) : base(value, external)
    {
    }
}