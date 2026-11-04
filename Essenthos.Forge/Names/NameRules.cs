using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Essenthos.Core.Loading.Encyclopedia;

namespace Essenthos.Core.Names;

/// <summary>
/// What the projection refuses to carry, read from <c>names/rules</c> (DOC-0224 §7.1). The word lists are data; what each
/// rule does with them is here.
/// <list type="bullet">
/// <item><c>pronoun</c> (the note says the sex it states, if any): a word of the language that is a pronoun takes no name
/// over a link, unless <c>pronoun-referent</c> is <c>clear</c> and it is certainly clear who the pronoun is
/// (<see cref="NamesProjection"/>'s referent test).</item>
/// <item><c>never-a-name</c> (a word, lower-cased and without punctuation), <c>never-a-name-pos</c> (a part of speech of the
/// word's parsing), <c>never-a-name-strong</c> (a Strong number), <c>never-a-name-normalised</c> (a normalised form): a word
/// its language never names anybody with; it is never the word of a link that takes the name.</item>
/// <item><c>head</c>: which words of a link's translation side take the names of its base side: <c>last</c> (the last word
/// that can be a name; before an English possessive, the word that carries it), <c>last+capitals</c> (that word and every
/// other word of the side written as a name, with a capital where the sentence does not begin) or <c>all</c>.</item>
/// <item><c>faint</c>: a person or place carried over a link below it onto a word not written as a name is taken back where
/// another word of the verse takes the same record over a link at least as sure as it (the stray rule).</item>
/// <item><c>floor</c>: a name carried with less than this, the settled name's confidence times the link's, is not carried.</item>
/// <item><c>foreign-names</c> (<c>on</c>|<c>off</c>): a person's name carried onto a word that is another record's name is not
/// carried (<see cref="ForeignNameRule"/>).</item>
/// </list>
/// </summary>
internal sealed class NameRules
{
    public const string Pronoun = "pronoun";
    public const string NeverAName = "never-a-name";
    public const string NeverAPartOfSpeech = "never-a-name-pos";
    public const string NeverAStrongNumber = "never-a-name-strong";
    public const string NeverANormalisedForm = "never-a-name-normalised";
    public const string PronounReferent = "pronoun-referent";
    public const string ReferentClear = "clear";
    public const string ReferentNever = "never";
    public const string Head = "head";
    public const string Faint = "faint";
    public const string Floor = "floor";
    public const string ForeignNamesRule = "foreign-names";

    public const string HeadLast = "last";
    public const string HeadAll = "all";
    public const string HeadLastAndCapitals = "last+capitals";

    private readonly Dictionary<string, Dictionary<string, string?>> _pronouns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _never = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _neverPos = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _neverStrong = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _neverNormalised = new(StringComparer.Ordinal);

    public NameRules(IEnumerable<NameRuleRow> rows)
    {
        foreach (var row in rows)
        {
            switch (row.Rule)
            {
                case Pronoun:
                    if (!_pronouns.TryGetValue(row.Language, out var pronouns))
                    {
                        _pronouns[row.Language] = pronouns = new Dictionary<string, string?>(StringComparer.Ordinal);
                    }

                    pronouns[row.Value] = row.Note is Pronouns.Male or Pronouns.Female ? row.Note : null;
                    break;
                case NeverAName: Add(_never, row); break;
                case NeverAPartOfSpeech: Add(_neverPos, row); break;
                case NeverAStrongNumber: Add(_neverStrong, row); break;
                case NeverANormalisedForm: Add(_neverNormalised, row); break;
                case PronounReferent:
                    Referents = row.Value is ReferentClear or ReferentNever ? row.Value : throw new InvalidDataException($"names/rules: pronoun-referent is {ReferentClear} or {ReferentNever}, not {row.Value}.");
                    break;
                case Head:
                    HeadRule = row.Value is HeadLast or HeadAll or HeadLastAndCapitals ? row.Value : throw new InvalidDataException($"names/rules: head is {HeadLast}, {HeadLastAndCapitals} or {HeadAll}, not {row.Value}.");
                    break;
                case Faint: FaintValue = Parse(row) ?? throw new InvalidDataException("names/rules: faint needs a number."); break;
                case Floor: FloorValue = Parse(row) ?? throw new InvalidDataException("names/rules: floor needs a number."); break;
                case ForeignNamesRule:
                    ForeignNames = row.Value switch { "on" => true, "off" => false, _ => throw new InvalidDataException($"names/rules: foreign-names is on or off, not {row.Value}.") };
                    break;
                default: throw new InvalidDataException($"names/rules: no rule is called {row.Rule}.");
            }
        }
    }

    /// <summary>Whether a pronoun may show the person it refers to where that is certainly clear, or never.</summary>
    public string Referents { get; } = ReferentClear;

    public string HeadRule { get; } = HeadLast;

    public double FaintValue { get; } = Annotating.Faint;

    /// <summary>A name carried with less than this (the settled name's confidence times the link's) is not carried.</summary>
    public double FloorValue { get; }

    /// <summary>Whether a person's name carried onto a word that is another record's name is refused (<see cref="ForeignNameRule"/>).</summary>
    public bool ForeignNames { get; }

    private static void Add(Dictionary<string, HashSet<string>> lists, NameRuleRow row)
    {
        if (!lists.TryGetValue(row.Language, out var words))
        {
            lists[row.Language] = words = new HashSet<string>(StringComparer.Ordinal);
        }

        words.Add(row.Value);
    }

    private static double? Parse(NameRuleRow row) => double.Parse(row.Value, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>A word lower-cased and stripped of punctuation and symbols, as the word lists hold it.</summary>
    public static string Bare(string surface)
    {
        var bare = new StringBuilder(surface.Length);
        foreach (var letter in surface)
        {
            if (!char.IsPunctuation(letter) && !char.IsSymbol(letter))
            {
                bare.Append(char.ToLowerInvariant(letter));
            }
        }

        return bare.ToString();
    }

    public bool IsPronoun(string language, string bare) =>
        _pronouns.TryGetValue(language, out var words) && words.ContainsKey(bare);

    /// <summary>The sex a pronoun states (<c>male</c>, <c>female</c>), or null where it states none or is no pronoun.</summary>
    public string? SexOf(string language, string bare) =>
        _pronouns.TryGetValue(language, out var words) ? words.GetValueOrDefault(bare) : null;

    /// <summary>Whether a word is one its language never names anybody with (<see cref="Annotating.NeverAName"/>).</summary>
    public bool IsNeverAName(string language, string bare, string? partOfSpeech, string? strong, string? normalised) =>
        (_never.TryGetValue(language, out var words) && words.Contains(bare))
        || (partOfSpeech is not null && _neverPos.TryGetValue(language, out var parts) && parts.Contains(partOfSpeech))
        || (strong is not null && _neverStrong.TryGetValue(language, out var numbers) && numbers.Contains(strong))
        || (normalised is not null && _neverNormalised.TryGetValue(language, out var forms) && forms.Contains(normalised));

    /// <summary>The part of speech a word's parsing states, where it states one.</summary>
    public static string? PartOfSpeech(string? morphology)
    {
        if (morphology is null || !morphology.Contains("\"pos\"", StringComparison.Ordinal))
        {
            return null;
        }

        using var document = JsonDocument.Parse(morphology);
        return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("pos", out var pos)
               && pos.ValueKind == JsonValueKind.String
            ? pos.GetString()
            : null;
    }

    /// <summary>
    /// The rules as the load's passes state them today: the pronoun lists of <see cref="Pronouns"/>, the words of
    /// <see cref="Annotating.NeverAName"/>, the faint line of <see cref="Annotating.Faint"/>, the last word as head. Written
    /// once into <c>names/rules</c>, which is then kept by hand.
    /// </summary>
    public static IReadOnlyList<NameRuleRow> Seed()
    {
        var rows = new List<NameRuleRow>
        {
            new(Head, "*", HeadLast, "the last word of a link's side that can be a name takes it; before an English possessive, the word that carries it"),
            new(Faint, "*", Annotating.Faint.ToString("R", CultureInfo.InvariantCulture), "a person or place carried below it onto a word not written as a name yields to the same record on a surer word of the verse"),
            new(Floor, "*", "0.7", "the owner's answer of 2026-11-04 on TSK-1448: a name the settled name's confidence and the link's together make less sure than this is not carried; better correct than rubbish"),
            new(ForeignNamesRule, "*", "on", "a person's name carried onto a word the text writes as another record's name is not carried (the load's foreign-names pass)"),
            new(PronounReferent, "*", ReferentClear, "a pronoun shows the person it refers to only where that is certainly clear (the owner's ruling of 2026-10-07, PronounReferents)"),
        };
        rows.AddRange(Pronouns.Listed()
            .OrderBy(p => p.Language, StringComparer.Ordinal).ThenBy(p => p.Word, StringComparer.Ordinal)
            .Select(p => new NameRuleRow(Pronoun, p.Language, p.Word, p.Sex)));
        rows.AddRange(NeverANameSeed());
        return rows;
    }

    private static IEnumerable<NameRuleRow> NeverANameSeed()
    {
        var sql = Annotating.NeverAName;
        foreach (Match match in Regex.Matches(sql, @"WHEN '(?<language>\w+)' THEN lower\(regexp_replace\(hw\.text[^)]*\)\) = ANY\(ARRAY\[(?<words>[^\]]*)\]\)"))
        {
            foreach (var word in Quoted(match.Groups["words"].Value))
            {
                yield return new NameRuleRow(NeverAName, match.Groups["language"].Value, word, null);
            }
        }

        foreach (Match match in Regex.Matches(sql, @"WHEN '(?<language>\w+)' THEN coalesce\(hw\.morphology ->> 'pos', ''\) IN \((?<parts>[^)]*)\)"))
        {
            foreach (var part in Quoted(match.Groups["parts"].Value))
            {
                yield return new NameRuleRow(NeverAPartOfSpeech, match.Groups["language"].Value, part, null);
            }
        }

        if (Regex.Match(sql, @"WHEN 'grc' THEN coalesce\(hw\.strong_number, ''\) IN \((?<numbers>[^)]*)\)") is { Success: true } numbers)
        {
            foreach (var number in Quoted(numbers.Groups["numbers"].Value))
            {
                yield return new NameRuleRow(NeverAStrongNumber, "grc", number, null);
            }
        }

        if (Regex.Match(sql, @"OR coalesce\(hw\.normalised_text, ''\) IN \((?<forms>[^)]*)\)") is { Success: true } forms)
        {
            foreach (var form in Quoted(forms.Groups["forms"].Value))
            {
                yield return new NameRuleRow(NeverANormalisedForm, "grc", form, null);
            }
        }
    }

    private static IEnumerable<string> Quoted(string list) =>
        Regex.Matches(list, "'([^']*)'").Select(m => m.Groups[1].Value);
}
