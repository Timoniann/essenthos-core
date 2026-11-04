using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// The merge's rule on a link whose words stand in two verses: it is withdrawn when only a machine claims it (the
/// aligner, a composition, the names paired by spelling: the corpus's <see cref="LinkMethod.Aligner"/>) and no verse link
/// of the pair, either way round, joins the two verses. The aligner pairs words only inside verses the frame puts at one
/// address, so such a link was drawn under an earlier frame and names a verse that is no longer the other's counterpart.
/// A link any other method claims stands wherever it points; a stated source's crossing makes a verse link of its own
/// (<see cref="VerseLinks"/>). This is the rule the verse-links load step applied to a corpus, unchanged; the owner's
/// question on PRB-1480 is whether it should keep machine links into the next or previous verse, and the answer goes here.
/// </summary>
internal static class CrossingRule
{
    /// <summary>What a withdrawn row's status names as having withdrawn it.</summary>
    public const string Name = "unjoined-verses";

    /// <summary>Whether the rule may withdraw a link with these claims: only when every one is a machine's.</summary>
    public static bool Applies(IEnumerable<string> methods) => methods.All(m => MappingMethods.DatabaseMethod(m) == LinkMethod.Aligner);

    /// <summary>Whether a word pair of the link stands at two primary addresses that no verse link joins.</summary>
    public static bool Withdraws(string from, string to, PairFrames frames, Func<VerseKey, VerseKey, bool> joined)
    {
        foreach (var fromVerse in Verses(from))
        {
            if (frames.From.Primary(fromVerse) is not { } here)
            {
                continue;
            }

            foreach (var toVerse in Verses(to))
            {
                if (frames.To.Primary(toVerse) is { } there && here != there && !joined(fromVerse, toVerse))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<VerseKey> Verses(string keys) =>
        MappingText.Split(keys).Select(key => VerseKey.Parse(key[..key.LastIndexOf('.')])).Distinct();
}
