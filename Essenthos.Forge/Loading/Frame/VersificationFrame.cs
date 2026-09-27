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
    IReadOnlyDictionary<CanonicalReference, IReadOnlyList<CanonicalReference>> rules,
    IReadOnlyDictionary<PrintedAddress, IReadOnlyList<CanonicalReference>>? printed = null)
{
    public Versification Tradition { get; } = tradition;

    public int RuleCount => rules.Count;

    /// <summary>
    /// Where a verse of this tradition sits. The first reference is its primary placement; further
    /// ones are the rest of what it spans. A verse nobody wrote a rule about is where it says it is.
    /// </summary>
    /// <param name="lettered">
    /// Whether the edition prints lettered verses at this address — <c>2:35</c> alongside
    /// <c>2:35a</c> to <c>2:35o</c>. A rule that spans several verses for such an address describes
    /// the undivided complex: the Greek rule for 3 Kingdoms 12:24 names everything from 11:1 to
    /// 14:43, because in a Bible that runs the additions into one verse that is what the verse holds.
    /// An edition that prints them apart has already divided it, and giving each of the twenty-five
    /// pieces all thirty-six addresses is a cross product rather than a mapping. So each piece stands
    /// where the edition prints it, and the letter says which piece it is. A rule that moves the
    /// address to one other verse is about the verse and not the complex — Joshua 24:31 is the
    /// Hebrew's 24:30, and the knives buried with Joshua at 24:31a go with it.
    /// </param>
    public IReadOnlyList<CanonicalReference> Resolve(int book, int chapter, int verse, bool lettered = false)
    {
        var own = new CanonicalReference(book, chapter, verse);
        return rules.TryGetValue(own, out var mapped) && (!lettered || mapped.Count == 1) ? mapped : [own];
    }

    /// <summary>
    /// Where a verse of this edition sits, knowing its letter. Where the edition's own lettering has
    /// been read verse by verse (<see cref="LetteredEditions"/>), the letter decides the place and
    /// nothing else is consulted: Brenton's Esther 1:1b is the second verse of the first addition,
    /// which stands at 11:3.
    /// </summary>
    public IReadOnlyList<CanonicalReference> Resolve(int book, int chapter, int verse, bool lettered, string label) =>
        printed is not null &&
        printed.TryGetValue(new PrintedAddress(new CanonicalReference(book, chapter, verse), label), out var read)
            ? read
            : Resolve(book, chapter, verse, lettered);
}

/// <summary>A verse as an edition prints it: its address and its letter, empty for none.</summary>
internal readonly record struct PrintedAddress(CanonicalReference Address, string Label)
{
    public override string ToString() => $"{Address}{Label}";
}
