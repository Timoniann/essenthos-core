namespace Essenthos.Core.Endpoints;

/// <summary>Whom a counted clause counts: children or siblings, by sex where every one of them has one.</summary>
internal enum Kin
{
    Son,
    Daughter,
    Child,
    Brother,
    Sister,
    Sibling,
}

/// <summary>
/// A family clause said as a count rather than as a list of names: <em>father of twelve sons and a
/// daughter</em>, <em>батько дванадцяти синів та дочки</em>, <em>Vater von zwölf Söhnen und einer
/// Tochter</em>, <em>padre de doce hijos y una hija</em>.
///
/// <para>
/// Each language puts the count in the case its phrase wants: Ukrainian the genitive the kinship
/// phrases already take, German the dative after <em>von</em>, Spanish and English the plain form.
/// Numbers to twenty are words; above that, figures, with the Ukrainian noun agreeing with the last
/// digit — <em>21 сина</em>, <em>28 синів</em>.
/// </para>
/// </summary>
internal static partial class DescriptorPhrasings
{
    /// <summary>The words after the phrase's own lead: the counted people, joined as a list in the language.</summary>
    public static string Counted(string language, IReadOnlyList<(Kin Kin, int Count)> counts)
    {
        var said = counts.Where(c => c.Count > 0).Select(c => Count(language, c.Kin, c.Count)).ToList();
        var list = said.Count <= 1
            ? string.Concat(said)
            : string.Join(Separator, said.Take(said.Count - 1)) + $" {Conjunction(language)} " + said[^1];
        return language == German ? "von " + list : list;
    }

    /// <summary>The word before the last of a list: <em>and</em>, <em>та</em>, <em>und</em>, <em>y</em>.</summary>
    public static string Conjunction(string language) => language switch
    {
        Ukrainian => "та",
        German => "und",
        Spanish => "y",
        _ => "and",
    };

    private static string Count(string language, Kin kin, int count) => language switch
    {
        Ukrainian => UkrainianCount(kin, count),
        German => GermanCount(kin, count),
        Spanish => SpanishCount(kin, count),
        _ => EnglishCount(kin, count),
    };

    private static string EnglishCount(Kin kin, int count)
    {
        var (one, many) = kin switch
        {
            Kin.Son => ("son", "sons"),
            Kin.Daughter => ("daughter", "daughters"),
            Kin.Child => ("child", "children"),
            Kin.Brother => ("brother", "brothers"),
            Kin.Sister => ("sister", "sisters"),
            _ => ("sibling", "siblings"),
        };
        return count == 1 ? $"a {one}" : $"{Number(EnglishNumbers, count)} {many}";
    }

    /// <summary>Genitive throughout, as <em>батько</em>, <em>брат</em> and every other kinship phrase want.</summary>
    private static string UkrainianCount(Kin kin, int count)
    {
        var (one, many) = kin switch
        {
            Kin.Son => ("сина", "синів"),
            Kin.Daughter => ("дочки", "дочок"),
            Kin.Child => ("дитини", "дітей"),
            Kin.Brother => ("брата", "братів"),
            Kin.Sister => ("сестри", "сестер"),
            _ => ("брата чи сестри", "братів і сестер"),
        };
        if (count == 1)
        {
            return one;
        }

        if (count < UkrainianNumbers.Length)
        {
            return $"{UkrainianNumbers[count]} {many}";
        }

        var singular = count % 10 == 1 && count % 100 != 11;
        return $"{count} {(singular ? one : many)}";
    }

    /// <summary>The dative after <em>von</em>: <em>zwölf Söhnen</em>, <em>einer Tochter</em>.</summary>
    private static string GermanCount(Kin kin, int count)
    {
        var (one, many) = kin switch
        {
            Kin.Son => ("einem Sohn", "Söhnen"),
            Kin.Daughter => ("einer Tochter", "Töchtern"),
            Kin.Child => ("einem Kind", "Kindern"),
            Kin.Brother => ("einem Bruder", "Brüdern"),
            Kin.Sister => ("einer Schwester", "Schwestern"),
            _ => ("einem Geschwister", "Geschwistern"),
        };
        return count == 1 ? one : $"{Number(GermanNumbers, count)} {many}";
    }

    /// <summary>A child of either sex is <em>hijo</em>, as the plural <em>hijos</em> says sons and daughters alike.</summary>
    private static string SpanishCount(Kin kin, int count)
    {
        var (one, many) = kin switch
        {
            Kin.Son or Kin.Child => ("un hijo", "hijos"),
            Kin.Daughter => ("una hija", "hijas"),
            Kin.Brother or Kin.Sibling => ("un hermano", "hermanos"),
            _ => ("una hermana", "hermanas"),
        };
        return count == 1 ? one : $"{Number(SpanishNumbers, count)} {many}";
    }

    private static string Number(string[] words, int count) =>
        count < words.Length ? words[count] : count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static readonly string[] EnglishNumbers =
    [
        "", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
        "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty",
    ];

    /// <summary>The cardinals in the genitive: <em>двох</em>, <em>дванадцяти</em>.</summary>
    private static readonly string[] UkrainianNumbers =
    [
        "", "одного", "двох", "трьох", "чотирьох", "п’яти", "шести", "семи", "восьми", "дев’яти", "десяти",
        "одинадцяти", "дванадцяти", "тринадцяти", "чотирнадцяти", "п’ятнадцяти", "шістнадцяти", "сімнадцяти",
        "вісімнадцяти", "дев’ятнадцяти", "двадцяти",
    ];

    private static readonly string[] GermanNumbers =
    [
        "", "eins", "zwei", "drei", "vier", "fünf", "sechs", "sieben", "acht", "neun", "zehn", "elf", "zwölf",
        "dreizehn", "vierzehn", "fünfzehn", "sechzehn", "siebzehn", "achtzehn", "neunzehn", "zwanzig",
    ];

    private static readonly string[] SpanishNumbers =
    [
        "", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve", "diez", "once", "doce",
        "trece", "catorce", "quince", "dieciséis", "diecisiete", "dieciocho", "diecinueve", "veinte",
    ];
}
