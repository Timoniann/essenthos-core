using System.Text.RegularExpressions;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// Slips of spelling and punctuation in the lines the datasets wrote under a record's name, put right
/// in the line the reader is shown. The dataset's own file is left as it came, so a corrected release of
/// it changes nothing here: a slip it no longer makes is simply not found.
///
/// <para>
/// BibleData writes <em>grandaughter</em> under Rebekah and Athaliah, closes Athaliah's references
/// twice and Sheva's never; Strong's Hebrew dictionary writes <em>decendants</em> of the Merarites.
/// Each entry is the slip with enough of its line around it to name nothing else.
/// </para>
/// </summary>
internal static partial class LineSpellings
{
    internal static readonly IReadOnlyList<(string Was, string Is)> Slips =
    [
        ("grandaughter", "granddaughter"),
        ("decendant", "descendant"),
        ("the distrct of", "the district of"),
        ("(huband?)", "(husband?)"),
        ("sent to sieze", "sent to seize"),
        ("fellow solder", "fellow soldier"),
        ("digiligently", "diligently"),
        ("fourteen sons and three daughter (", "fourteen sons and three daughters ("),
        ("2CH 12:13))", "2CH 12:13)"),
        ("2CH 22:2))", "2CH 22:2)"),
        ("(1CH 5:14, Gadite (", "(1CH 5:14), Gadite ("),
    ];

    /// <summary>The line spelled right, or the line itself when nothing in it is a slip.</summary>
    public static string? Correct(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return line;
        }

        var corrected = line;
        foreach (var (was, @is) in Slips)
        {
            corrected = corrected.Replace(was, @is, StringComparison.Ordinal);
        }

        corrected = Spaces().Replace(corrected, " ");

        // A reference opened at the end of a line and never closed: "scribe for King David (2SA 20:25".
        if (corrected.Count(c => c == '(') > corrected.Count(c => c == ')') && UnclosedAtTheEnd().IsMatch(corrected))
        {
            corrected += ")";
        }

        return corrected;
    }

    /// <summary>
    /// Slips in the titles and the glosses of names: a gloss that lost its first letter or its
    /// opening bracket, a word misspelled, a closing quote left behind, a spreadsheet's boolean.
    /// </summary>
    internal static readonly IReadOnlyList<(string Was, string Is)> NameSlips =
    [
        ("Annointed", "Anointed"),
        ("annointed", "anointed"),
        ("Ahasureus", "Ahasuerus"),
        ("Meshash", "Meshach"),
    ];

    internal static readonly IReadOnlyList<(string Was, string Is)> NameOpenings =
    [
        ("on of Deker", "son of Deker"),
        ("rom the right", "from the right"),
        ("awning, shining", "dawning, shining"),
        ("might god [El]", "mighty G-d [El]"),
        ("Oather (?) of", "G-d of"),
    ];

    private static readonly IReadOnlyDictionary<string, string> NameLines =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["TRUE"] = "true" };

    /// <summary>
    /// Whole titles and glosses that a slip of the dictionary's English leaves wrong after everything
    /// above: a verb that does not agree, a stray semicolon or quote, a misspelled place, a name in
    /// lower case, and two that misread their Hebrew (the King of the South of DAN 11:6, the serpent of
    /// old of REV 12:9). Matched whole, so nothing else is touched.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> NameCorrections =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["daughter [of the] prince [of] the south"] = "daughter [of the] king [of] the south",
            ["[my] G-d [El] sit in judgment"] = "[my] G-d [El] sits in judgment",
            ["G-d [El] is given"] = "G-d [El] has given",
            ["Euphrates (mighty river of Mesopotamia);"] = "Euphrates (mighty river of Mesopotamia)",
            ["mount of earth"] = "mound of earth",
            ["y-h-v-h lead"] = "y-h-v-h leads",
            ["Lukios the Cyrene"] = "Lucius of Cyrene",
            ["dissention"] = "dissension",
            ["a district of Arabia Petra"] = "a district of Arabia Petraea",
            ["fond of horses;"] = "fond of horses",
            ["weaver, embroider"] = "weaver, embroiderer",
            ["[poss.] G-d had restored"] = "[poss.] G-d has restored",
            ["the adversary, accuser, one who opposes, slanderer;"] = "the adversary, accuser, one who opposes, slanderer",
            ["adversary, accuser, one who opposes, slanderer;"] = "adversary, accuser, one who opposes, slanderer",
            ["y-h-v-h pays back, [poss.] he restores peace offering of y-h-v-h"] = "y-h-v-h pays back, [poss.] he restores; peace offering of y-h-v-h",
            ["y-h-v-h pays back [poss.] he restores peace offering of y-h-v-h"] = "y-h-v-h pays back, [poss.] he restores; peace offering of y-h-v-h",
            ["y-h-v-h has heard' or 'famous'"] = "y-h-v-h has heard, or famous",
            ["sin is his father"] = "Sin is his father",
            ["simon the zealous"] = "Simon the Zealot",
            ["friend [of] god"] = "friend [of] God",
            ["(of G-d) the Maker, the Creator;"] = "(of G-d) the Maker, the Creator",
            ["Glory [or Emminence] of Israel"] = "Glory [or Eminence] of Israel",
            ["john the immerser"] = "John the immerser",
            ["king herod"] = "king Herod",
            ["the from-law"] = "the lawgiver",
            ["[my]son beloved"] = "[my] son beloved",
            ["savior, deliverer, rescuer;"] = "savior, deliverer, rescuer",
            ["learned writer, scribe, secretary. → scribe; secretary."] = "learned writer, scribe, secretary",
            ["great prince of abiyliyn"] = "great prince of Abilene",
            ["the serpent [of] the east"] = "the serpent [of] old",
        };

    /// <summary>A title or the gloss of a name spelled right, or the line itself.</summary>
    public static string? Name(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return line;
        }

        if (NameLines.TryGetValue(line, out var whole))
        {
            return whole;
        }

        var corrected = line;
        foreach (var (was, @is) in NameOpenings)
        {
            if (corrected.StartsWith(was, StringComparison.Ordinal))
            {
                corrected = @is + corrected[was.Length..];
            }
        }

        foreach (var (was, @is) in NameSlips)
        {
            corrected = corrected.Replace(was, @is, StringComparison.Ordinal);
        }

        // "my] father is Ner [a lamp]": the bracket that opened the gloss was lost.
        var close = corrected.IndexOf(']');
        if (close >= 0 && corrected.IndexOf('[') is var open && (open < 0 || open > close))
        {
            corrected = "[" + corrected;
        }

        if (corrected.EndsWith('”') && !corrected.Contains('“'))
        {
            corrected = corrected[..^1];
        }

        return NameCorrections.GetValueOrDefault(corrected, corrected);
    }

    [GeneratedRegex(" {2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\([^()]*\d$")]
    private static partial Regex UnclosedAtTheEnd();
}
