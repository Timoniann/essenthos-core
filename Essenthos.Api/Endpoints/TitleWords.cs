using Essenthos.Core.Corpus;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The words a reader puts before a name to say who is meant: <em>цар Давид</em>, <em>пророк Ілля</em>,
/// <em>апостол Павло</em>, <em>King David</em>. A search for the person reads the name after them as
/// well as the whole phrase, so the title a reader adds does not hide the person it names.
/// </summary>
/// <remarks>
/// Every language's words are read whatever language the page is in: a reader types the title in the
/// language they think in, and a Ukrainian page is searched in English as often as in Ukrainian.
/// Inflected forms are listed where a reader types them — <em>царя Давида</em>.
/// </remarks>
internal static class TitleWords
{
    private static readonly IReadOnlyDictionary<string, string[]> ByLanguage = new Dictionary<string, string[]>
    {
        ["ukr"] =
        [
            "цар", "царя", "цариця", "царицю", "пророк", "пророка", "пророчиця", "апостол", "апостола",
            "святий", "свята", "святого", "св", "священик", "первосвященик", "суддя", "князь", "євангеліст",
            "патріарх", "праотець", "праведний", "праведна",
        ],
        ["rus"] =
        [
            "царь", "царя", "царица", "пророк", "пророка", "пророчица", "апостол", "апостола", "святой",
            "святая", "святого", "св", "священник", "первосвященник", "судья", "князь", "евангелист",
            "патриарх", "праотец", "праведный", "праведная",
        ],
        ["eng"] =
        [
            "the", "king", "queen", "prophet", "prophetess", "apostle", "saint", "st", "priest",
            "high priest", "judge", "prince", "evangelist", "patriarch",
        ],
        ["deu"] =
        [
            "der", "die", "könig", "königin", "prophet", "prophetin", "apostel", "heilige", "heiliger",
            "hl", "priester", "hohepriester", "richter", "fürst", "evangelist", "patriarch", "erzvater",
        ],
        ["spa"] =
        [
            "el", "la", "rey", "reina", "profeta", "profetisa", "apóstol", "san", "santo", "santa",
            "sacerdote", "sumo sacerdote", "juez", "príncipe", "evangelista", "patriarca",
        ],
    };

    /// <summary>Every title folded the way a typed name is, which runs a two-word title into one.</summary>
    private static readonly HashSet<string> Folded =
    [
        .. ByLanguage.Values.SelectMany(words => words).Select(NameFolding.Fold),
    ];

    /// <summary>The most words one title runs to.</summary>
    private static readonly int LongestTitle = ByLanguage.Values
        .SelectMany(words => words)
        .Max(word => word.Split(' ').Length);

    /// <summary>
    /// What was typed with the titles in front of the name taken off, or null where it opens with
    /// none or is nothing but titles. <em>святий апостол Павло</em> gives <em>Павло</em>.
    /// </summary>
    public static string? Without(string typed)
    {
        var words = typed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var at = 0;
        while (Title(words, at) is var length and > 0)
        {
            at += length;
        }

        return at == 0 ? null : string.Join(' ', words[at..]);
    }

    /// <summary>How many words the title opening at this word runs to, where one does and a name follows it.</summary>
    private static int Title(string[] words, int at)
    {
        for (var length = Math.Min(LongestTitle, words.Length - at - 1); length > 0; length--)
        {
            if (Folded.Contains(NameFolding.Fold(string.Concat(words[at..(at + length)]))))
            {
                return length;
            }
        }

        return 0;
    }
}
