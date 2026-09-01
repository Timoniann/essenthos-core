namespace Essenthos.Core.Bhsa.Attributes;

public class SubphraseLinguisticRelation : StringEnums<SubphraseLinguisticRelation>
{
    public static readonly SubphraseLinguisticRelation Adjunct = new(["ADJ", "adj"]);
    public static readonly SubphraseLinguisticRelation Attribute = new(["ATR", "atr"]);
    public static readonly SubphraseLinguisticRelation Demonstrative = new(["DEM", "dem"]);
    public static readonly SubphraseLinguisticRelation Modifier = new(["MOD", "mod"]);
    public static readonly SubphraseLinguisticRelation Parallel = new(["PAR", "par"]);
    public static readonly SubphraseLinguisticRelation Regens = new(["REG", "rec"]);
    public static readonly SubphraseLinguisticRelation NotApplicable = new(["NA"]);

    private SubphraseLinguisticRelation(string[] values, bool external = false) : base(values, external)
    {
    }

    public static implicit operator SubphraseLinguisticRelation(string value) => Parse(value);
}