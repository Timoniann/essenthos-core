using Essenthos.Core.Configuration;
using Essenthos.Core.Products;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// A line of a language's word list, <c>evidentia/words/&lt;language&gt;</c>, kept by hand: one form of a small word the
/// attached-word rules speak of, the kind of rule that reads it, and what the form itself says of person, number, gender and
/// the case it governs, where it says it, and the Strong numbers of the original words it renders (a subject pronoun: the
/// pronouns that, written free beside the verb, take it instead). The parse's features are not trusted for these: the form is.
/// </summary>
internal sealed record EvidentiaWordRow(string Kind, string Form, string? Person, string? Number, string? Gender, string? Case, string? Original, string Reason)
{
    public static readonly ProductSchema<EvidentiaWordRow> Schema = new ProductSchema<EvidentiaWordRow>()
        .Text("kind", r => r.Kind)
        .Text("form", r => r.Form)
        .OptionalText("person", r => r.Person)
        .OptionalText("number", r => r.Number)
        .OptionalText("gender", r => r.Gender)
        .OptionalText("case", r => r.Case)
        .OptionalText("original", r => r.Original)
        .Text("reason", r => r.Reason)
        .Reads(row => new EvidentiaWordRow(row.Text(0), row.Text(1), row.TextOrNull(2), row.TextOrNull(3), row.TextOrNull(4), row.TextOrNull(5), row.TextOrNull(6), row.Text(7)));
}

/// <summary>The kinds of small word the attached-word rules read, each the name of a list in the word files.</summary>
internal static class EvidentiaWordKinds
{
    /// <summary>A personal pronoun standing for the person a Greek or Hebrew verb carries in its ending.</summary>
    public const string SubjectPronoun = "subject-pronoun";

    /// <summary>A form of <em>be</em> the original writes no word for.</summary>
    public const string Copula = "copula";

    /// <summary>A preposition that says what the original says by the case of its noun alone.</summary>
    public const string CasePreposition = "case-preposition";

    /// <summary>A pronoun that opens a relative clause written for a participle: <em>той, хто</em>.</summary>
    public const string Relative = "relative";

    /// <summary>A negation, on the original's own negation written before its head's rendering.</summary>
    public const string Negation = "negation";

    /// <summary>A conjunction, on the original's own conjunction written before its head's rendering.</summary>
    public const string Conjunction = "conjunction";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        SubjectPronoun, Copula, CasePreposition, Relative, Negation, Conjunction,
    };

    public static readonly IReadOnlySet<string> Persons = new HashSet<string>(StringComparer.Ordinal) { "1", "2", "3" };

    public static readonly IReadOnlySet<string> Numbers = new HashSet<string>(StringComparer.Ordinal) { "sg", "pl" };

    public static readonly IReadOnlySet<string> Genders = new HashSet<string>(StringComparer.Ordinal) { "m", "f", "n" };

    /// <summary>The Greek cases, as Nestle 1904's morphology names them.</summary>
    public static readonly IReadOnlySet<string> Cases = new HashSet<string>(StringComparer.Ordinal) { "genitive", "dative", "accusative" };
}

/// <summary>
/// One language's small words as its word file lists them, read and checked: a malformed line is refused with its line and
/// fault, never skipped.
/// </summary>
internal sealed class EvidentiaWordList
{
    private readonly Dictionary<string, List<EvidentiaWordRow>> _byForm;

    private EvidentiaWordList(string language, IReadOnlyList<EvidentiaWordRow> rows)
    {
        Language = language;
        Rows = rows;
        _byForm = rows.GroupBy(row => row.Form, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
    }

    public string Language { get; }

    public IReadOnlyList<EvidentiaWordRow> Rows { get; }

    public static string Product(string language) => $"evidentia/words/{language}";

    /// <summary>The lines of a kind for a form, lower-cased by the caller; empty where the list has none.</summary>
    public IEnumerable<EvidentiaWordRow> Of(string kind, string form) =>
        _byForm.TryGetValue(form, out var rows) ? rows.Where(row => row.Kind == kind) : [];

    public bool Is(string kind, string form) => Of(kind, form).Any();

    public static EvidentiaWordList Read(ProductStore store, string language)
    {
        using var reader = store.Open(Product(language), EvidentiaWordRow.Schema);
        return Checked(language, [.. reader.Rows()]);
    }

    public static EvidentiaWordList Checked(string language, IReadOnlyList<EvidentiaWordRow> rows)
    {
        var seen = new HashSet<(string, string, string?, string?, string?, string?)>();
        static bool StrongNumber(string value) => value.Length > 1 && value[0] is 'G' or 'H' && value[1..].All(char.IsAsciiDigit);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            string? fault = !EvidentiaWordKinds.All.Contains(row.Kind) ? $"kind '{row.Kind}' is none of {string.Join(", ", EvidentiaWordKinds.All.Order())}"
                : row.Form.Length == 0 || row.Form != row.Form.Trim().ToLowerInvariant() || row.Form.Any(char.IsWhiteSpace) ? $"form '{row.Form}' is not one lower-case word"
                : row.Person is { } person && !EvidentiaWordKinds.Persons.Contains(person) ? $"person '{person}' is not 1, 2 or 3"
                : row.Number is { } number && !EvidentiaWordKinds.Numbers.Contains(number) ? $"number '{number}' is not sg or pl"
                : row.Gender is { } gender && !EvidentiaWordKinds.Genders.Contains(gender) ? $"gender '{gender}' is not m, f or n"
                : row.Case is { } grammaticalCase && !grammaticalCase.Split('|').All(EvidentiaWordKinds.Cases.Contains) ? $"case '{grammaticalCase}' is not genitive, dative or accusative (several with |)"
                : row.Kind == EvidentiaWordKinds.SubjectPronoun && (row.Person is null || row.Number is null) ? "a subject pronoun needs its person and number"
                : row.Kind == EvidentiaWordKinds.CasePreposition && row.Case is null ? "a case preposition needs the case it stands for"
                : row.Original is { } original && !original.Split('|').All(StrongNumber) ? $"original '{original}' is not Strong numbers (G or H and digits, | between several)"
                : row.Kind is EvidentiaWordKinds.Negation or EvidentiaWordKinds.Conjunction or EvidentiaWordKinds.Copula && row.Original is null ? $"a {row.Kind} needs the original words it renders"
                : string.IsNullOrWhiteSpace(row.Reason) ? "the reason is empty"
                : !seen.Add((row.Kind, row.Form, row.Person, row.Number, row.Gender, row.Case)) ? "the line repeats an earlier one"
                : null;
            if (fault is not null)
            {
                throw new FormatException($"{Product(language)}, row {index + 1} ({row.Kind} {row.Form}): {fault}.");
            }
        }

        return new EvidentiaWordList(language, rows);
    }
}

/// <summary>
/// What the attached-word rules need of one Slavic language: its word list and the forms its grammar uses differently from
/// its neighbour. Ukrainian and Russian each implement it; the rules ask it and never test the language themselves.
/// </summary>
internal interface IEvidentiaSlavicWords
{
    /// <summary>The ISO 639-3 code of the language, which UDPipe and the word file are named by.</summary>
    string Language { get; }

    EvidentiaWordList Words { get; }
}

internal sealed class UkrainianWords(EvidentiaWordList words) : IEvidentiaSlavicWords
{
    public const string Code = "ukr";

    public string Language => Code;

    public EvidentiaWordList Words => words;
}

internal sealed class RussianWords(EvidentiaWordList words) : IEvidentiaSlavicWords
{
    public const string Code = "rus";

    public string Language => Code;

    public EvidentiaWordList Words => words;
}

internal static class EvidentiaSlavicLanguages
{
    /// <summary>The language of a translation the attached-word rules speak, read with its word file; null for any other.</summary>
    public static IEvidentiaSlavicWords? For(ProductStore store, string language) => language switch
    {
        UkrainianWords.Code => new UkrainianWords(EvidentiaWordList.Read(store, language)),
        RussianWords.Code => new RussianWords(EvidentiaWordList.Read(store, language)),
        _ => null,
    };
}

/// <summary>
/// A line of <c>evidentia/words/rules</c>, kept by hand: one attached-word rule a stored run applies for one Slavic language,
/// and the tier its placements are stored under, so that a measured rule is switched on, or taken back, by editing the file.
/// </summary>
internal sealed record EvidentiaSlavicRuleRow(string Language, string Original, string Rule, string Tier, string Reason)
{
    public const string Product = "evidentia/words/rules";

    /// <summary>The rules <see cref="EvidentiaSlavicAttachedWords"/> names.</summary>
    public static readonly IReadOnlySet<string> Rules = new HashSet<string>(StringComparer.Ordinal)
    {
        EvidentiaWordKinds.SubjectPronoun, "preposition-before-noun", "preposition-for-case", EvidentiaWordKinds.Negation,
        EvidentiaWordKinds.Conjunction, EvidentiaWordKinds.Copula, EvidentiaWordKinds.Relative,
    };

    public static readonly ProductSchema<EvidentiaSlavicRuleRow> Schema = new ProductSchema<EvidentiaSlavicRuleRow>()
        .Text("language", r => r.Language)
        .Text("original", r => r.Original)
        .Text("rule", r => r.Rule)
        .Text("tier", r => r.Tier)
        .Text("reason", r => r.Reason)
        .Reads(row => new EvidentiaSlavicRuleRow(row.Text(0), row.Text(1), row.Text(2), row.Text(3), row.Text(4)));

    /// <summary>The rules listed for a language, by rule, with their tiers; a malformed line is refused.</summary>
    public static IReadOnlyDictionary<(string Original, string Rule), string> Read(ProductStore store, string language)
    {
        using var reader = store.Open(Product, Schema);
        return Checked([.. reader.Rows()], language);
    }

    public static IReadOnlyDictionary<(string Original, string Rule), string> Checked(IReadOnlyList<EvidentiaSlavicRuleRow> rows, string language)
    {
        var tiers = new Dictionary<(string Original, string Rule), string>();
        var seen = new HashSet<(string, string, string)>();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            string? fault = row.Language is not (UkrainianWords.Code or RussianWords.Code) ? $"language '{row.Language}' is neither ukr nor rus"
                : row.Original is not ("grc" or "hbo") ? $"original '{row.Original}' is neither grc nor hbo"
                : !Rules.Contains(row.Rule) ? $"rule '{row.Rule}' is none of {string.Join(", ", Rules.Order(StringComparer.Ordinal))}"
                : row.Tier.Length == 0 || !row.Tier.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_') ? $"tier '{row.Tier}' is not lower-case letters, digits and _"
                : row.Tier is "safe" or "review" or "attached" ? $"tier '{row.Tier}' is one the run's own tiers already use"
                : string.IsNullOrWhiteSpace(row.Reason) ? "the reason is empty"
                : !seen.Add((row.Language, row.Original, row.Rule)) ? "the line repeats an earlier one"
                : null;
            if (fault is not null)
            {
                throw new FormatException($"{Product}, row {index + 1} ({row.Language} {row.Rule}): {fault}.");
            }

            if (row.Language == language)
            {
                tiers[(row.Original, row.Rule)] = row.Tier;
            }
        }

        return tiers;
    }
}

/// <summary>A Slavic language's words and the rules a stored run applies for it.</summary>
internal sealed record EvidentiaSlavicRun(IEvidentiaSlavicWords Words, IReadOnlyDictionary<(string Original, string Rule), string> Tiers);

/// <summary>
/// The word lists and rule lists of the Slavic languages, read once from the configured resources' store for the runs and
/// measurements that place their small words.
/// </summary>
internal sealed class EvidentiaSlavicRules(IConfiguration configuration, IHostEnvironment environment)
{
    private readonly Dictionary<string, EvidentiaSlavicRun?> byLanguage = new(StringComparer.Ordinal);

    private readonly Lock gate = new();

    /// <summary>The language's words and rules, or null for a language the rules do not speak or one with no rule listed.</summary>
    public EvidentiaSlavicRun? For(string language)
    {
        lock (gate)
        {
            if (!byLanguage.TryGetValue(language, out var run))
            {
                var store = new ProductStore(ResourcePaths.Read(configuration, environment.ContentRootPath));
                run = EvidentiaSlavicLanguages.For(store, language) is { } words
                    && EvidentiaSlavicRuleRow.Read(store, language) is { Count: > 0 } tiers
                    ? new EvidentiaSlavicRun(words, tiers)
                    : null;
                byLanguage[language] = run;
            }

            return run;
        }
    }
}
