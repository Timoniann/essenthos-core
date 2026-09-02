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
    /// <param name="lettered">
    /// Whether the edition prints lettered verses at this address — <c>2:35</c> alongside
    /// <c>2:35a</c> to <c>2:35o</c>. A rule for such an address describes the undivided complex:
    /// the Greek rule for 3 Kingdoms 12:24 names everything from 11:1 to 14:43, because in a Bible
    /// that runs the additions into one verse that is what the verse holds. An edition that prints
    /// them apart has already divided it, and giving each of the twenty-five pieces all thirty-six
    /// addresses is a cross product rather than a mapping. So each piece stands where the edition
    /// prints it, and the letter says which piece it is.
    /// </param>
    public IReadOnlyList<CanonicalReference> Resolve(int book, int chapter, int verse, bool lettered = false)
    {
        var own = new CanonicalReference(book, chapter, verse);
        return !lettered && rules.TryGetValue(own, out var mapped) ? mapped : [own];
    }
}
