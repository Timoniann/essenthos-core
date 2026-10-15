namespace Essenthos.Core.Endpoints;

/// <summary>
/// What a line says of children beyond their names, in each language: the ones a verse counts and
/// the line names among them (<em>батько сімдесяти синів, з них названі Єтер, Авімелех та
/// Йотам</em>), more than it names (<em>батько Каїна, Авеля, Сифа та інших синів і дочок</em>), a
/// child's place (<em>сьомий син Єссея</em>), and more descendants than the line lists.
/// </summary>
internal static partial class DescriptorPhrasings
{
    /// <summary>
    /// The words between a count and the names it is followed by, which stand in the nominative:
    /// <em>, з них названі</em> Єтер, <em>, darunter</em> Jeter.
    /// </summary>
    public static string AmongThem(string language, int named, bool daughters) => language switch
    {
        Ukrainian => named == 1 ? daughters ? ", з них названа " : ", з них названий " : ", з них названі ",
        German => ", darunter ",
        Spanish => daughters ? ", entre ellas " : ", entre ellos ",
        _ => ", among them ",
    };

    /// <summary>How many of a count are named, where they are too many to list: <em>, з них названо сім</em>.</summary>
    public static string OfThemNamed(string language, int named, bool daughters) => language switch
    {
        Ukrainian => $", з них названо {UkrainianAccusative(named)}",
        German => $", davon {Number(GermanNumbers, named)} genannt",
        Spanish => daughters
            ? $", {Number(SpanishNumbers, named)} de ellas nombradas"
            : $", {Number(SpanishNumbers, named)} de ellos nombrados",
        _ => $", {Number(EnglishNumbers, named)} of them named",
    };

    /// <summary>
    /// The children a verse says there were beyond those it names, after the last name
    /// (<em> та інших синів і дочок</em>) or after a count of the named (<em>, а також інших синів і
    /// дочок</em>). After a German count the phrase is still inside its <em>von</em>, so the dative.
    /// </summary>
    public static string Others(string language, bool sons, bool daughters, bool afterCount)
    {
        var both = sons && daughters;
        return language switch
        {
            Ukrainian => (afterCount ? ", а також інших " : " та інших ")
                         + (both ? "синів і дочок" : sons ? "синів" : "дочок"),
            German => afterCount
                ? " sowie weiteren " + (both ? "Söhnen und Töchtern" : sons ? "Söhnen" : "Töchtern")
                : " und weiterer " + (both ? "Söhne und Töchter" : sons ? "Söhne" : "Töchter"),
            Spanish => (afterCount ? ", y de " : " y ")
                       + (both ? "otros hijos e hijas" : sons ? "otros hijos" : "otras hijas"),
            _ => (afterCount ? ", and of other " : " and other ")
                 + (both ? "sons and daughters" : sons ? "sons" : "daughters"),
        };
    }

    /// <summary>
    /// Children a verse says there were and names none of, after the phrase's own lead: <em>батько
    /// синів і дочок</em>.
    /// </summary>
    public static string Unnamed(string language, bool sons, bool daughters)
    {
        var both = sons && daughters;
        return language switch
        {
            Ukrainian => both ? "синів і дочок" : sons ? "синів" : "дочок",
            German => "von " + (both ? "Söhnen und Töchtern" : sons ? "Söhnen" : "Töchtern"),
            Spanish => both ? "hijos e hijas" : sons ? "hijos" : "hijas",
            _ => both ? "sons and daughters" : sons ? "sons" : "daughters",
        };
    }

    /// <summary>More than the names a line lists, after the last of them: <em>предок Савла, Павла та інших</em>.</summary>
    public static string AndOthers(string language) => language switch
    {
        Ukrainian => " та інших",
        German => " und anderer",
        Spanish => " y otros",
        _ => " and others",
    };

    /// <summary>
    /// A son or daughter said by their place among the parent's children, where a verse states it:
    /// <em>сьомий син</em> Єссея, <em>firstborn son of</em> Jacob. Null past the places a verse numbers.
    /// </summary>
    public static Phrasing? Ordinal(string language, Phrasing plain, bool daughter, int ordinal)
    {
        if (ordinal < 1 || ordinal > LastOrdinal)
        {
            return null;
        }

        var before = language switch
        {
            Ukrainian => ordinal == 1
                ? daughter ? "первістка " : "первісток "
                : (daughter ? UkrainianFeminineOrdinals : UkrainianOrdinals)[ordinal] + (daughter ? " дочка " : " син "),
            German => ordinal == 1
                ? daughter ? "erstgeborene Tochter " : "erstgeborener Sohn "
                : (daughter ? GermanFeminineOrdinals : GermanOrdinals)[ordinal] + (daughter ? " Tochter " : " Sohn "),
            Spanish => ordinal == 1
                ? daughter ? "primogénita de " : "primogénito de "
                : (daughter ? SpanishFeminineOrdinals : SpanishOrdinals)[ordinal] + (daughter ? " hija de " : " hijo de "),
            _ => ordinal == 1
                ? daughter ? "firstborn daughter of " : "firstborn son of "
                : EnglishOrdinals[ordinal] + (daughter ? " daughter of " : " son of "),
        };
        return plain with { Before = before };
    }

    private const int LastOrdinal = 12;

    /// <summary>A count of named people as the object of <em>названо</em>: <em>чотирьох</em>, then the plain cardinal.</summary>
    private static string UkrainianAccusative(int count) => count switch
    {
        2 => "двох",
        3 => "трьох",
        4 => "чотирьох",
        _ when count < UkrainianCardinals.Length => UkrainianCardinals[count],
        _ => count.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private static readonly string[] UkrainianCardinals =
    [
        "", "одного", "двох", "трьох", "чотирьох", "п’ять", "шість", "сім", "вісім", "дев’ять", "десять",
        "одинадцять", "дванадцять", "тринадцять", "чотирнадцять", "п’ятнадцять", "шістнадцять", "сімнадцять",
        "вісімнадцять", "дев’ятнадцять", "двадцять",
    ];

    private static readonly string[] EnglishOrdinals =
    [
        "", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth", "eleventh", "twelfth",
    ];

    private static readonly string[] UkrainianOrdinals =
    [
        "", "перший", "другий", "третій", "четвертий", "п’ятий", "шостий", "сьомий", "восьмий", "дев’ятий", "десятий",
        "одинадцятий", "дванадцятий",
    ];

    private static readonly string[] UkrainianFeminineOrdinals =
    [
        "", "перша", "друга", "третя", "четверта", "п’ята", "шоста", "сьома", "восьма", "дев’ята", "десята",
        "одинадцята", "дванадцята",
    ];

    private static readonly string[] GermanOrdinals =
    [
        "", "erster", "zweiter", "dritter", "vierter", "fünfter", "sechster", "siebter", "achter", "neunter", "zehnter",
        "elfter", "zwölfter",
    ];

    private static readonly string[] GermanFeminineOrdinals =
    [
        "", "erste", "zweite", "dritte", "vierte", "fünfte", "sechste", "siebte", "achte", "neunte", "zehnte",
        "elfte", "zwölfte",
    ];

    private static readonly string[] SpanishOrdinals =
    [
        "", "primer", "segundo", "tercer", "cuarto", "quinto", "sexto", "séptimo", "octavo", "noveno", "décimo",
        "undécimo", "duodécimo",
    ];

    private static readonly string[] SpanishFeminineOrdinals =
    [
        "", "primera", "segunda", "tercera", "cuarta", "quinta", "sexta", "séptima", "octava", "novena", "décima",
        "undécima", "duodécima",
    ];
}
