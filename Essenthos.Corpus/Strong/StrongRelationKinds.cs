namespace Essenthos.Core.Strong;

/// <summary>
/// What one Strong entry says about another. The vocabulary is read from the words Strong writes
/// before a reference, and it is closed: a reference whose words fit none of these is kept as
/// <see cref="Unclassified"/> rather than given the nearest kind.
/// </summary>
public static class StrongRelationKinds
{
    /// <summary><em>the same as</em>, <em>identical with</em>: one word under two numbers, often a name and its common noun.</summary>
    public const string SameAs = "same-as";

    /// <summary><em>from</em>, <em>a derivative of</em>, <em>the base of</em>, each part of <em>a compound of</em>.</summary>
    public const string From = "from";

    /// <summary>
    /// <em>from the same as</em>: derived from the root the other is derived from. A sibling, not a
    /// parent — H3 is <em>from the same as</em> H24, and neither comes from the other.
    /// </summary>
    public const string SameRootAs = "same-root-as";

    /// <summary><em>feminine of</em>, <em>plural of</em>, <em>passive participle of</em>: a grammatical form of the other.</summary>
    public const string FormOf = "form-of";

    /// <summary><em>a variation of</em>, <em>another form of</em>, <em>a prolonged form of</em>, <em>for</em>.</summary>
    public const string Variant = "variant";

    public const string ContractedFrom = "contracted-from";

    /// <summary>The Aramaic word <em>corresponding to</em> a Hebrew one.</summary>
    public const string CorrespondsTo = "corresponds-to";

    /// <summary>A Greek word <em>of Hebrew origin</em>, or of Aramaic, written in Greek letters.</summary>
    public const string LoanFrom = "loan-from";

    /// <summary>A people named after the man it descends from, where the gentilic reading accepts it.</summary>
    public const string Patronymic = "patronymic";

    /// <summary>A people named after the place it lives in, where the gentilic reading accepts it.</summary>
    public const string Patrial = "patrial";

    /// <summary>Strong writes both and chooses neither.</summary>
    public const string PatronymicOrPatrial = "patronymic-or-patrial";

    /// <summary><em>compare</em>: a cross-reference Strong offers, not a derivation.</summary>
    public const string Compare = "compare";

    /// <summary>
    /// <em>a primitive root</em>, <em>a primitive word</em>, <em>a primary verb</em>: Strong derives it
    /// from nothing. The one kind with no other number.
    /// </summary>
    public const string Primitive = "primitive";

    /// <summary>One of the roots a compiler lists for an entry, where the source does not say how it is related.</summary>
    public const string Root = "root";

    /// <summary>A reference the source writes in words none of the kinds above reads.</summary>
    public const string Unclassified = "unclassified";

    public static readonly IReadOnlyList<string> All =
    [
        SameAs, From, SameRootAs, FormOf, Variant, ContractedFrom, CorrespondsTo, LoanFrom,
        Patronymic, Patrial, PatronymicOrPatrial, Compare, Primitive, Root, Unclassified,
    ];
}
