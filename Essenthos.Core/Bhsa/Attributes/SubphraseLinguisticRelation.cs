namespace Essenthos.Core.Bhsa.Attributes;

/// <summary>
/// How a subphrase stands to the one it depends on — a rectum to its regens, an attribute to what
/// it qualifies. The first spelling of each is the code <c>rela.tf</c> writes; the uppercase one
/// beside it is what releases before 2017 wrote, and is here so those still parse.
/// </summary>
public class SubphraseLinguisticRelation : StringEnums<SubphraseLinguisticRelation>
{
    public static readonly SubphraseLinguisticRelation Adjunct = new(["adj", "ADJ"]);
    public static readonly SubphraseLinguisticRelation Attribute = new(["atr", "ATR"]);
    public static readonly SubphraseLinguisticRelation Demonstrative = new(["dem", "DEM"]);
    public static readonly SubphraseLinguisticRelation Modifier = new(["mod", "MOD"]);
    public static readonly SubphraseLinguisticRelation Parallel = new(["par", "PAR"]);
    public static readonly SubphraseLinguisticRelation Regens = new(["rec", "REG"]);
    public static readonly SubphraseLinguisticRelation NotApplicable = new(["NA"]);

    private SubphraseLinguisticRelation(string[] values, bool external = false) : base(values, external)
    {
    }

    public static implicit operator SubphraseLinguisticRelation(string value) => Parse(value);
}