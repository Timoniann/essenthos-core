using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Frame;

/// <summary>
/// Where one numbering tradition's verses sit in the shared frame.
///
/// It holds only the differences. A tradition agrees with the frame almost everywhere, so a verse
/// with no rule is at its own address — <see cref="Resolve"/> answers that without a lookup miss
/// being an error. This is why the whole of the Hebrew Bible's placement is five thousand rules
/// rather than twenty-three thousand.
/// </summary>
internal sealed class VersificationFrame(
    Versification tradition,
    IReadOnlyDictionary<CanonicalReference, IReadOnlyList<CanonicalReference>> rules)
{
    public Versification Tradition { get; } = tradition;

    public int RuleCount => rules.Count;

    /// <summary>
    /// Where a verse of this tradition sits. The first reference is its primary placement; further
    /// ones are the rest of what it spans. A verse nobody wrote a rule about is where it says it is.
    /// </summary>
    public IReadOnlyList<CanonicalReference> Resolve(int book, int chapter, int verse)
    {
        var own = new CanonicalReference(book, chapter, verse);
        return rules.TryGetValue(own, out var mapped) ? mapped : [own];
    }
}
