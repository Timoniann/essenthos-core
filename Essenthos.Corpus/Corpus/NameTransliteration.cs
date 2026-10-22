using System.Text;

namespace Essenthos.Core.Corpus;

/// <summary>
/// A name typed in Cyrillic, spelled in Latin letters the ways an English Bible might spell it, so
/// <em>Рамсес</em> meets the <em>Ramsès</em> a French text prints and <em>Голіаф</em> meets
/// <em>Goliath</em>'s neighbours.
///
/// <para>
/// Two spellings at most, because <em>г</em> is <em>g</em> in a name that came through Greek and Russian
/// and <em>h</em> in a Ukrainian mouth, and English Bibles have both (<em>Gideon</em>, <em>Hannah</em>).
/// Only ever a way in: what is found is still a record whose own names hold the spelling, never a
/// name made up here.
/// </para>
/// </summary>
internal static class NameTransliteration
{
    private static readonly IReadOnlyDictionary<char, string> Letters = new Dictionary<char, string>
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['д'] = "d", ['е'] = "e", ['є'] = "e", ['ё'] = "e",
        ['ж'] = "zh", ['з'] = "z", ['и'] = "i", ['і'] = "i", ['ї'] = "i", ['й'] = "i", ['к'] = "k",
        ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s",
        ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh",
        ['щ'] = "shch", ['ь'] = "", ['ъ'] = "", ['ы'] = "y", ['э'] = "e", ['ю'] = "iu", ['я'] = "ia",
        ['ґ'] = "g",
    };

    /// <summary>The Latin spellings of a Cyrillic name, folded; empty where the name holds no Cyrillic letter.</summary>
    public static IReadOnlyList<string> Latin(string name)
    {
        var lower = name.ToLowerInvariant();
        if (!lower.Any(Letters.ContainsKey) && !lower.Contains('г'))
        {
            return [];
        }

        return new[] { "g", "h" }
            .Select(g => Spell(lower, g))
            .Select(NameFolding.Fold)
            .Where(spelling => spelling.Length > 0)
            .Distinct()
            .ToList();
    }

    private static string Spell(string lower, string g)
    {
        var spelled = new StringBuilder(lower.Length + 4);
        foreach (var letter in lower)
        {
            spelled.Append(letter == 'г' ? g : Letters.GetValueOrDefault(letter) ?? letter.ToString());
        }

        return spelled.ToString();
    }
}
