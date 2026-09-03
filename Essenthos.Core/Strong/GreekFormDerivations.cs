using System.Text.RegularExpressions;

namespace Essenthos.Core.Strong;

/// <param name="Lemma">
/// The headword. A space in it is the mark of a phrase entry — <em>οὐ μή</em>, <em>εἰ μή</em> — for
/// which the printed editions tag two words and the concordance one.
/// </param>
public readonly record struct GreekEntry(string StrongNumber, string? Lemma, string? Derivation);

/// <summary>
/// Strong numbered the Greek by the form he found; Nestle and the Textus Receptus tag by the
/// lemma. So a tagged King James writes G2076 ἐστί where the Greek writes G1510 εἰμί, the two
/// numbers never meet, and 13,756 tagged English words in the New Testament match nothing.
///
/// The dictionary states the relation itself — G2076 reads <c>third person singular present
/// indicative of G1510</c> — and this reads it back out of all 5,624 Greek entries.
///
/// <para>
/// Only a derivation describing a <em>form</em> is followed. G3778 οὗτος reads <c>from the article
/// G3588 and G846</c>, which is where the word came from and not what it is; following it would
/// file every <em>this</em> under <em>the</em>. The test is the vocabulary the phrase is built
/// from: a form is named by case, gender, number, person, tense, mood, voice, or by one of the
/// words Strong uses for a variant of one word — contracted, prolonged, the simpler form of. An
/// origin is named by <em>a derivative of</em>, <em>akin to</em>, <em>the base of</em>. A phrase
/// carrying both vocabularies is stating an origin, so the origin words refuse it outright.
/// </para>
/// </summary>
public static partial class GreekFormDerivations
{
    /// <summary>
    /// How many hops a chain may take. G5213 ὑμῖν reaches σύ in two — through ὑμεῖς — and nothing
    /// in the dictionary needs more; the limit is here so that a cycle Strong left behind cannot
    /// become a hang.
    /// </summary>
    private const int MaximumHops = 8;

    /// <summary>
    /// Words naming a grammatical form. One of these has to appear for a derivation to be read as
    /// saying that this entry is another shape of the entry it points at.
    /// </summary>
    private static readonly string[] Form =
    [
        "nominative", "genitive", "dative", "accusative", "vocative",
        "masculine", "feminine", "neuter", "singular", "plural", "person",
        "indicative", "imperative", "subjunctive", "optative", "infinitive", "participle",
        "active", "middle", "passive",
        "present", "imperfect", "aorist", "perfect", "pluperfect", "future",
        "comparative", "superlative",
        "contract", "prolong", "shorter", "simpler", "strengthen", "intensive", "emphatic",
        "reduplicat", "irregular",
    ];

    /// <summary>
    /// Words naming where a word came from. Any of these refuses the derivation however it is
    /// phrased, because etymology relates two words rather than identifying one.
    /// </summary>
    private static readonly string[] Origin =
    [
        "akin", "derivative", "derived", "base of", "same as", "diminutive",
        "adverb", "ordinal", "compare", "variation", "alternate",
    ];

    /// <summary>
    /// The number a derivation says this entry is a form of, or null when it says anything else.
    /// The reference has to be the last thing in the derivation and the only one in it: two
    /// references are a compound and a reference in the middle of a sentence is an aside.
    /// </summary>
    public static string? Head(string? derivation)
    {
        if (string.IsNullOrWhiteSpace(derivation))
        {
            return null;
        }

        var references = Reference().Matches(derivation);
        if (references.Count != 1)
        {
            return null;
        }

        var reference = references[0];
        if (derivation.AsSpan(reference.Index + reference.Length).TrimEnd([' ', ';', '.', ',']).Length > 0)
        {
            return null;
        }

        var phrase = derivation[..reference.Index].ToLowerInvariant();
        return Origin.Any(phrase.Contains) || !Form.Any(phrase.Contains) ? null : reference.Value;
    }

    /// <summary>
    /// The numbers of the words a phrase entry is made of, or null when the entry is one word.
    /// <em>οὐ μή</em> is G3364 to the concordance and G3756 followed by G3361 to both editions, and
    /// the derivation names exactly those two.
    /// </summary>
    public static IReadOnlyList<string>? Parts(string? lemma, string? derivation)
    {
        if (lemma is null || derivation is null || !lemma.Trim().Contains(' '))
        {
            return null;
        }

        var parts = Reference().Matches(derivation).Select(match => match.Value).Distinct().ToList();
        return parts.Count < 2 ? null : parts;
    }

    /// <summary>
    /// Every number the dictionary can send somewhere the Greek text actually uses, resolved as far
    /// as it goes.
    ///
    /// A number the Greek witness already uses is left alone: it is a lemma there, and redirecting
    /// it would rewrite the text's own tagging. That single condition is what keeps the etymologies
    /// the vocabulary test lets through from doing any harm — Χριστός is derived <c>from G5548</c>
    /// and is also tagged G5547 on every page, so nothing ever asks where to send it.
    /// </summary>
    public static Dictionary<string, IReadOnlyList<string>> Resolve(
        IReadOnlyCollection<GreekEntry> entries,
        Func<string, bool> attested)
    {
        var heads = new Dictionary<string, string>(entries.Count, StringComparer.Ordinal);
        var resolved = new Dictionary<string, IReadOnlyList<string>>(entries.Count, StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (attested(entry.StrongNumber))
            {
                continue;
            }

            if (Parts(entry.Lemma, entry.Derivation) is { } parts)
            {
                resolved[entry.StrongNumber] = parts;
            }
            else if (Head(entry.Derivation) is { } head)
            {
                heads[entry.StrongNumber] = head;
            }
        }

        foreach (var (number, head) in heads)
        {
            if (Walk(heads, attested, number, head) is { } destination)
            {
                resolved[number] = [destination];
            }
        }

        return resolved;
    }

    /// <summary>
    /// Follows the chain to the first number the Greek uses. Walking past it would be walking past
    /// the answer: ὑμῖν is a form of ὑμεῖς, which the editions do not tag either, and only σύ beyond
    /// it is a word on the page.
    /// </summary>
    private static string? Walk(
        Dictionary<string, string> heads,
        Func<string, bool> attested,
        string number,
        string head)
    {
        var seen = new HashSet<string>(MaximumHops, StringComparer.Ordinal) { number };

        for (var hop = 0; hop < MaximumHops; hop++)
        {
            if (attested(head))
            {
                return head;
            }

            if (!seen.Add(head) || !heads.TryGetValue(head, out var next))
            {
                return null;
            }

            head = next;
        }

        return null;
    }

    [GeneratedRegex(@"G[0-9]+")]
    private static partial Regex Reference();
}
