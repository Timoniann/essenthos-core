namespace Essenthos.Core.Endpoints;

/// <summary>
/// The words of a topic's heading that carry its meaning, and the words of a verse, brought to one
/// form so the two can be compared: <em>Creation</em> and <em>created</em>, <em>Birds</em> and
/// <em>bird</em>, <em>Women</em> and <em>woman</em>.
///
/// <para>
/// A few suffixes and a handful of irregular plurals, not a stemmer: a heading is a word or two of
/// plain English, and a false match costs a topic a small lift in its rank, not a claim on screen.
/// </para>
/// </summary>
internal static class HeadingWords
{
    /// <summary>Words a heading is built with that name nothing a verse could say.</summary>
    private static readonly HashSet<string> Empty = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "as", "at", "by", "for", "from", "in", "into", "of", "on", "or", "the", "to",
        "with", "his", "her", "its", "their", "concerning", "general", "select", "miscellany",
    };

    private static readonly IReadOnlyDictionary<string, string> Irregular = new Dictionary<string, string>
    {
        ["women"] = "woman",
        ["men"] = "man",
        ["children"] = "child",
        ["wives"] = "wife",
        ["feet"] = "foot",
        ["teeth"] = "tooth",
        ["oxen"] = "ox",
        ["brethren"] = "brother",
        ["mice"] = "mouse",
        ["geese"] = "goose",
        ["lice"] = "louse",
        ["sheaves"] = "sheaf",
        ["loaves"] = "loaf",
        ["leaves"] = "leaf",
        ["knives"] = "knife",
        ["lives"] = "life",
        ["thieves"] = "thief",
        ["wolves"] = "wolf",
        ["calves"] = "calf",
    };

    /// <summary>A stem needs this many letters left before a suffix is taken off it.</summary>
    private const int ShortestStem = 3;

    /// <summary>The words of a heading that carry meaning, each stemmed.</summary>
    public static IReadOnlyList<string> Of(string heading) =>
        [.. Stems(heading).Where(stem => !Empty.Contains(stem)).Distinct()];

    /// <summary>The stems of every word in a run of text.</summary>
    public static IEnumerable<string> Stems(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var letter = i < text.Length && char.IsLetter(text[i]);
            if (letter && start < 0)
            {
                start = i;
            }
            else if (!letter && start >= 0)
            {
                // A possessive's s is the name's, not a plural: LORD'S SUPPER is the Lord's.
                var word = text[start..i].ToLowerInvariant();
                if (i + 1 < text.Length && text[i] is '\'' or '’' && char.ToLowerInvariant(text[i + 1]) == 's'
                    && (i + 2 == text.Length || !char.IsLetter(text[i + 2])))
                {
                    i++;
                }

                yield return Stem(word);
                start = -1;
            }
        }
    }

    public static string Stem(string word)
    {
        if (Irregular.TryGetValue(word, out var singular))
        {
            return singular;
        }

        if (word.EndsWith("ies", StringComparison.Ordinal) && word.Length - 3 >= ShortestStem - 1)
        {
            return word[..^3] + "y";
        }

        foreach (var suffix in Suffixes)
        {
            if (word.EndsWith(suffix, StringComparison.Ordinal) && word.Length - suffix.Length >= ShortestStem)
            {
                return word[..^suffix.Length];
            }
        }

        if (word.EndsWith("es", StringComparison.Ordinal) && Sibilants.Any(ending => word[..^2].EndsWith(ending, StringComparison.Ordinal))
            && word.Length - 2 >= ShortestStem)
        {
            return word[..^2];
        }

        return word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal)
                                  && !word.EndsWith("us", StringComparison.Ordinal)
                                  && !word.EndsWith("is", StringComparison.Ordinal)
                                  && word.Length - 1 >= ShortestStem
            ? word[..^1]
            : word;
    }

    /// <summary>Longest first, so <em>offerings</em> loses <em>ings</em> and not only its <em>s</em>.</summary>
    private static readonly string[] Suffixes = ["ings", "ing", "ions", "ion", "ed"];

    /// <summary>The endings a plural adds <em>es</em> to rather than <em>s</em>: boxes, churches, but trees.</summary>
    private static readonly string[] Sibilants = ["s", "x", "z", "ch", "sh"];
}
