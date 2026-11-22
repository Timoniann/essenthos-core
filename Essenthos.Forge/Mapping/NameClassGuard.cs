using System.Text.Json;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Strong;

namespace Essenthos.Core.Mapping;

/// <summary>
/// Keeps a laid numbering from sending a name to a common word, or a common word to a name, where its tag has slipped
/// (PRB-1962). Bob Jones University's numbering of the Synodal puts G11 (Abraham) on <em>отец</em> and G3962 (father) on
/// <em>Авраам</em> in Matthew 3:9, and the 0.9 the Strong pairing is worth outranked the aligner's right reading of both.
///
/// <para>
/// **What tells the slip.** The numbering itself: a surface it tags with a name's number almost every time it numbers it
/// (<see cref="Share"/> of at least <see cref="LeastTags"/> tags) is written as a name, and one it almost never does is a
/// common word. Where such a word's tag here crosses that line against its witness word — the name matched onto a common
/// word, the common word onto a name — and the witness's verse holds the word's usual number, the word had its own
/// counterpart in the verse and the tag sent it elsewhere: the guard takes the word out of that match, and the aligner's
/// reading of it stands. A rendering the translation supplies (<em>Иисуса</em> for αὐτόν, where the verse has no
/// Ἰησοῦς) is left alone, and so is a word whose number a correction set (<c>texts/&lt;SLUG&gt;/corrections.tsv</c>).
/// </para>
///
/// <para>
/// **Which numbers are names.** In the Old Testament the witness's own words say it: BHSA marks a proper noun
/// <c>nmpr</c>, and a number is a name where most of the words carrying it are. In the New Testament it is Strong's
/// Greek dictionary, whose entries for names begin with a capital.
/// </para>
/// <para>
/// **Not switched on yet** (no source passes <c>guardsNames</c>). Measured on the Synodal's products (2026-11-22): it takes
/// 13-15 words out of each Greek pair and 138 out of RUSV-BHSA, and of the 13 New Testament words read by hand only 7 were
/// slips (Luke 7:40 Симон, 10:13 в, 23:15 также, Acts 16:36 Павлу, Jude 14 и, 1 Corinthians 2:2 Христа, 2 Corinthians 10:7
/// он). The other 6 are the Synodal moving a name onto a pronoun's place and back (Mark 2:15 Иисус/Ним, Luke 5:12, John 4:1,
/// Matthew 4:18, Mark 15:9), whose tags are right; in the Old Testament it refuses phrases such as Аммонитян over בְּנֵי עַמּוֹן.
/// Before it is switched on it must leave a pronoun on either side alone and judge a phrase by its head word.
/// </para>
/// </summary>
internal sealed class NameClassGuard
{
    /// <summary>How many tags a surface needs before the numbering's habit with it is read as what the surface is.</summary>
    public const int LeastTags = 5;

    /// <summary>The share of a surface's tags that must fall on one side of the line for it to count as a name or a common word.</summary>
    public const double Share = 0.9;

    private readonly Dictionary<string, Surface> _surfaces;
    private readonly IReadOnlySet<string> _proper;
    private readonly IReadOnlySet<long> _corrected;
    private readonly Func<long, string> _surfaceOf;

    private readonly record struct Surface(int Proper, int Total, string Usual);

    private NameClassGuard(Dictionary<string, Surface> surfaces, IReadOnlySet<string> proper, IReadOnlySet<long> corrected, Func<long, string> surfaceOf)
    {
        _surfaces = surfaces;
        _proper = proper;
        _corrected = corrected;
        _surfaceOf = surfaceOf;
    }

    /// <param name="tags">The laid tags by word id, corrections over them.</param>
    /// <param name="corrected">The words whose number a correction set or cleared.</param>
    /// <param name="surfaceOf">A word's surface by its id.</param>
    /// <param name="series">The witness's series: only numbers of it are counted.</param>
    /// <param name="proper">The numbers of the series that are names.</param>
    public static NameClassGuard Of(
        IReadOnlyDictionary<long, WordTag> tags, IReadOnlySet<long> corrected, Func<long, string> surfaceOf, char series, IReadOnlySet<string> proper)
    {
        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var (id, tag) in tags)
        {
            var number = tag.Numbers.FirstOrDefault(n => n.Length > 0 && n[0] == series);
            if (number is null)
            {
                continue;
            }

            var surface = Fold(surfaceOf(id));
            if (!counts.TryGetValue(surface, out var numbers))
            {
                counts[surface] = numbers = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            numbers[number] = numbers.GetValueOrDefault(number) + 1;
        }

        var surfaces = counts.ToDictionary(
            entry => entry.Key,
            entry => new Surface(
                entry.Value.Where(n => proper.Contains(n.Key)).Sum(n => n.Value),
                entry.Value.Values.Sum(),
                entry.Value.OrderByDescending(n => n.Value).ThenBy(n => n.Key, StringComparer.Ordinal).First().Key),
            StringComparer.Ordinal);
        return new NameClassGuard(surfaces, proper, corrected, surfaceOf);
    }

    /// <summary>
    /// Whether the word's match onto these witness words crosses the line between names and common words against what the
    /// numbering makes of its surface, while its usual number stands among the witness verse's.
    /// </summary>
    /// <param name="witness">The numbers of the witness words the match names.</param>
    /// <param name="verse">The numbers of every word of the witness's verse.</param>
    public bool Refuses(long id, IEnumerable<string?> witness, IReadOnlySet<string> verse)
    {
        if (_corrected.Contains(id) || !_surfaces.TryGetValue(Fold(_surfaceOf(id)), out var surface) || surface.Total < LeastTags)
        {
            return false;
        }

        var named = witness.Any(number => number is not null && _proper.Contains(number));
        var share = (double)surface.Proper / surface.Total;
        return share >= Share
            ? !named && _proper.Contains(surface.Usual) && verse.Contains(surface.Usual)
            : share <= 1 - Share && named && !_proper.Contains(surface.Usual) && verse.Contains(surface.Usual);
    }

    /// <summary>The Greek numbers Strong's dictionary gives a name: entries whose lemma begins with a capital.</summary>
    public static HashSet<string> GreekNames(IEnumerable<GreekEntry> entries) =>
    [
        .. entries.Where(entry => entry.Lemma is { Length: > 0 } lemma && char.IsUpper(lemma[0]))
            .Select(entry => StrongNumbers.Normalize(entry.StrongNumber))
            .OfType<string>(),
    ];

    /// <summary>The Hebrew numbers most of whose words in the witness are proper nouns by their morphology (BHSA's <c>nmpr</c>).</summary>
    public static HashSet<string> HebrewNames(IEnumerable<(string? Strong, string? Morphology)> words)
    {
        var counts = new Dictionary<string, (int Proper, int Total)>(StringComparer.Ordinal);
        foreach (var (strong, morphology) in words)
        {
            if (strong is not { Length: > 0 } || strong[0] != StrongNumbers.Hebrew)
            {
                continue;
            }

            var (proper, total) = counts.GetValueOrDefault(strong);
            counts[strong] = (proper + (ProperNoun(morphology) ? 1 : 0), total + 1);
        }

        return [.. counts.Where(entry => entry.Value.Proper * 2 > entry.Value.Total).Select(entry => entry.Key)];
    }

    private static bool ProperNoun(string? morphology)
    {
        if (morphology is null || !morphology.Contains("nmpr", StringComparison.Ordinal))
        {
            return false;
        }

        using var document = JsonDocument.Parse(morphology);
        return document.RootElement.ValueKind == JsonValueKind.Object
               && document.RootElement.TryGetProperty("pos", out var pos)
               && pos.ValueKind == JsonValueKind.String
               && pos.GetString() == "nmpr";
    }

    private static string Fold(string surface)
    {
        var start = 0;
        var end = surface.Length;
        while (start < end && !char.IsLetterOrDigit(surface[start]))
        {
            start++;
        }

        while (end > start && !char.IsLetterOrDigit(surface[end - 1]))
        {
            end--;
        }

        return surface[start..end].ToLowerInvariant();
    }
}
