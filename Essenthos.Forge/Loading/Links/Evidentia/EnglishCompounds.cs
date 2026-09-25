namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// The compounds English editions write as one word or as two: the King James prints <em>to day</em>,
/// <em>for ever</em> and <em>any thing</em>, a modern translation <em>today</em>, <em>forever</em> and
/// <em>anything</em>, and the World English Bible has <em>cup bearers</em> where the King James has
/// <em>cupbearers</em>. Read as they are printed, the second half is learned as the whole compound's
/// rendering - <em>day</em> as σήμερον - and the joined form is never learned at all.
///
/// The second word of a split compound is read as the compound, when learning and when looking up
/// alike; the first keeps its own reading. Only this list: joining any two words that happen to spell
/// a third (<em>a way</em>, <em>away</em>) would teach the one what the other renders.
/// </summary>
internal static class EnglishCompounds
{
    private static readonly Dictionary<(string First, string Second), string> Joined = new()
    {
        [("to", "day")] = "today",
        [("to", "morrow")] = "tomorrow",
        [("for", "ever")] = "forever",
        [("for", "evermore")] = "forevermore",
        [("any", "thing")] = "anything",
        [("every", "thing")] = "everything",
        [("every", "where")] = "everywhere",
        [("cup", "bearers")] = "cupbearers",
        [("cup", "bearer")] = "cupbearer",
    };

    public static IReadOnlySet<string> FirstHalves { get; } = Joined.Keys.Select(key => key.First).ToHashSet(StringComparer.Ordinal);

    /// <summary>The compound the word completes, when the word before it in the same verse is its first half with only a space between.</summary>
    public static string? Compound(string first, string trailer, string second) =>
        string.IsNullOrWhiteSpace(trailer)
        && Joined.TryGetValue((Bare(first), Bare(second)), out var joined)
            ? joined
            : null;

    /// <summary>Every token that completes a compound, read as the compound; the others unchanged.</summary>
    public static IReadOnlyList<EvidentiaToken> Join(IReadOnlyList<EvidentiaToken> tokens)
    {
        List<EvidentiaToken>? joined = null;
        for (var index = 1; index < tokens.Count; index++)
        {
            var (first, second) = (tokens[index - 1], tokens[index]);
            if (second.Language.Equals("eng", StringComparison.OrdinalIgnoreCase)
                && first.Address == second.Address && first.Position == second.Position - 1
                && Compound(first.Surface, first.Trailer, second.Surface) is { } compound)
            {
                joined ??= [.. tokens];
                joined[index] = second with { Surface = compound, Lemma = compound };
            }
        }

        return joined ?? tokens;
    }

    private static string Bare(string surface) =>
        EnglishSpelling.Bare(new EvidentiaToken(0, default, 0, surface, "eng")).Surface.ToLowerInvariant();
}
