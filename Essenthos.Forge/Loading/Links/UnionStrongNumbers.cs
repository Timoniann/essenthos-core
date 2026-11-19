using Essenthos.Core.Strong;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The numbers the Faith Hope Love foundation (信望愛, FHL) wrote on the Chinese Union Version, in the
/// series the corpus's witnesses write.
///
/// <para>
/// **Three kinds of number share one attribute.** A <c>&lt;w&gt;</c> writes its lexemes —
/// <c>strong:H07225</c> — and beside them two things that are not lexemes. The first is the verb's
/// stem and tense in the old Strong's-TVM codes, <c>H8804</c> or <c>G5656</c>: numbers above the
/// last entry of each dictionary (H8674, G5624), which name a parsing and no word, so they are read
/// as nothing. The second is FHL's own numbers for the Hebrew prefixes Strong gave none to.
/// </para>
///
/// <para>
/// **FHL's prefix numbers are not STEPBible's, though both start at 9000.** BHSA carries
/// STEPBible's: H9003 for בְּ, H9004 for כְּ, H9005 for לְ. FHL writes H9002, H9003 and H9001 for the
/// same three, which was established by counting rather than read anywhere: over the Old Testament
/// verses both texts hold at one address, FHL's H9001 stands in a verse holding BHSA's לְ 1.85 times as often as chance
/// would put it there and beside no other prefix above 1.16; H9002 beside בְּ at 2.16, the rest at
/// most 1.18; H9003 beside כְּ at 8.95, the rest at most 1.25. Taken as written, H9003 would have
/// sent every Chinese rendering of כְּ to a בְּ. So the three are translated (<c>strong/numbers.tsv</c>, list
/// <c>fhl-prefix</c>: <see cref="StrongNumberLists.FhlPrefixes"/>), and the one H9004
/// the file writes, in a single verse, is read as nothing: one place is no evidence of a meaning.
/// </para>
/// </summary>
internal static class UnionStrongNumbers
{
    private const string StrongPrefix = "strong:";

    /// <summary>The lexemes a <c>lemma</c> attribute names, in the order written, each once.</summary>
    /// <param name="fhlPrefixes">
    /// FHL's prefix numbers by their value, as the numbers the corpus's Hebrew writes for the same prefix, where the module
    /// writes them; null in any other module, where numbers above the Hebrew dictionary name no lexeme the corpus's Hebrew
    /// writes, and are read as nothing.
    /// </param>
    public static IReadOnlyList<string> Read(string? lemma, IReadOnlyDictionary<int, string>? fhlPrefixes) => Read(lemma, null, fhlPrefixes).Numbers;

    /// <summary>
    /// The lexemes a <c>lemma</c> attribute names, each once, with the code the <c>morph</c> attribute writes at the first
    /// place each is named: CrossWire's King James writes one of Robinson's codes per number, in the same order
    /// (<c>robinson:T-NSM robinson:N-NSM</c>). Null where it writes none.
    /// </summary>
    public static (IReadOnlyList<string> Numbers, IReadOnlyList<string?> Morphology) Read(string? lemma, string? morph, IReadOnlyDictionary<int, string>? fhlPrefixes)
    {
        if (string.IsNullOrWhiteSpace(lemma))
        {
            return ([], []);
        }

        var codes = (morph ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(code => code[(code.IndexOf(':') + 1)..])
            .ToArray();
        var numbers = new List<string>(2);
        var morphology = new List<string?>(2);
        var at = -1;
        foreach (var part in lemma.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!part.StartsWith(StrongPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            at++;
            if (StrongNumbers.Normalize(part[StrongPrefix.Length..]) is not { } number)
            {
                continue;
            }

            var value = int.Parse(number.AsSpan(1));
            var read = number[0] switch
            {
                StrongNumbers.Hebrew when value <= StrongNumbers.LastHebrew => number,
                StrongNumbers.Hebrew when fhlPrefixes is not null => fhlPrefixes.GetValueOrDefault(value),
                StrongNumbers.Hebrew => null,
                _ => value <= StrongNumbers.LastGreek ? number : null,
            };

            if (read is not null && !numbers.Contains(read))
            {
                numbers.Add(read);
                morphology.Add(at < codes.Length ? codes[at] : null);
            }
        }

        return (numbers, morphology);
    }
}
