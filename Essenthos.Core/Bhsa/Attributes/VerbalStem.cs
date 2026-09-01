namespace Essenthos.Core.Bhsa.Attributes;

public class VerbalStem : StringEnum<VerbalStem>
{
    public static readonly VerbalStem Hifil = new("hif");
    public static readonly VerbalStem Hitpael = new("hit");
    public static readonly VerbalStem Hitpoel = new("htpo");
    public static readonly VerbalStem Hofal = new("hof");
    public static readonly VerbalStem Nifal = new("nif");
    public static readonly VerbalStem Piel = new("piel");
    public static readonly VerbalStem Poal = new("poal");
    public static readonly VerbalStem Poel = new("poel");
    public static readonly VerbalStem Pual = new("pual");
    public static readonly VerbalStem Qal = new("qal");

    public static readonly VerbalStem Afel = new("afel", false, false);
    public static readonly VerbalStem Etpaal = new("etpa", false, false);
    public static readonly VerbalStem Etpeel = new("etpe", false, false);
    public static readonly VerbalStem Hafel = new("haf", false, false);
    public static readonly VerbalStem Hotpaal = new("hotp", false, false);
    public static readonly VerbalStem Hishtafal = new("hsht", false, false);
    public static readonly VerbalStem Hitpaal = new("htpa", false, false);
    public static readonly VerbalStem Hitpeel = new("htpe", false, false);
    public static readonly VerbalStem Nitpael = new("nit", false, false);
    public static readonly VerbalStem Pael = new("pael", false, false);
    public static readonly VerbalStem Peal = new("peal", false, false);
    public static readonly VerbalStem Peil = new("peil", false, false);
    public static readonly VerbalStem Shafel = new("shaf", false, false);
    public static readonly VerbalStem Tifal = new("tif", false, false);
    public static readonly VerbalStem PassiveQal = new("pasq", false, false);

    public static readonly VerbalStem NotApplicable = new("NA", false, false);

    private VerbalStem(string value, bool external = false, bool hebrew = true) : base(value, external)
    {
        Hebrew = hebrew;
    }

    public bool Hebrew { get; }

    public static implicit operator VerbalStem(string value) => Parse(value);
}