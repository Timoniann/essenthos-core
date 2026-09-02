namespace Essenthos.Core.Loading.Links;

/// <param name="Compared">
/// Words where both sides state a gloss. A witness that carries none makes the comparison vacuous,
/// and a check that silently passed on nothing is what a count of zero here says out loud.
/// </param>
/// <param name="Same">Of those, the words the two sides gloss the same way.</param>
internal readonly record struct GlossAgreement(int Compared, int Same)
{
    public double Share => Compared == 0 ? 1 : (double)Same / Compared;
}

/// <summary>
/// Comparing the gloss a source states for a word against the one the witness carries.
///
/// This is what makes a positional join checkable. Two files can agree on how many words a verse
/// has and still divide it differently — 1 Kings 22:43, 1 Samuel 20:42, 1 Chronicles 12:4 and
/// Numbers 26:1 all do, and in each the counts match and every word is off by one or more. A count
/// cannot see that; a gloss sequence can.
/// </summary>
internal static class Glosses
{
    /// <summary>
    /// The same gloss written two ways. ETCBC brackets its non-lexical glosses in angle brackets
    /// and the mapping file in square ones, so <c>&lt;object marker&gt;</c> and
    /// <c>[object marker]</c> are one gloss and 43,000 words of the Hebrew hang on saying so.
    /// </summary>
    public static bool Same(string? stated, string? witness) =>
        string.Equals(Bare(stated), Bare(witness), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// How much of a verse the two sides gloss the same way, over the words where both state one.
    /// </summary>
    public static GlossAgreement Agreement(IReadOnlyList<string?> stated, IReadOnlyList<string?> witness)
    {
        var compared = 0;
        var same = 0;

        for (var i = 0; i < stated.Count && i < witness.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(stated[i]) || string.IsNullOrWhiteSpace(witness[i]))
            {
                continue;
            }

            compared++;
            if (Same(stated[i], witness[i]))
            {
                same++;
            }
        }

        return new GlossAgreement(compared, same);
    }

    private static string Bare(string? gloss)
    {
        var value = gloss?.Trim() ?? string.Empty;
        return value.Length > 1 && value[0] is '<' or '[' && value[^1] is '>' or ']'
            ? value[1..^1]
            : value;
    }
}
