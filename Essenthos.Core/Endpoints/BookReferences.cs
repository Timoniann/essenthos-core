using Essenthos.Core.Utils;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The canon as the read API presents it: 66 books, addressed by ordinal or slug, named in
/// English regardless of which corpus is being read. BHSA calls book 4 "Numeri" and the KJV calls
/// it "Numbers"; a URL that changes when the reader switches corpus is a URL that cannot be
/// shared, so both resolve to ordinal 4 and both render as "Numbers".
/// </summary>
internal static class BookReferences
{
    public const int OldTestamentBookCount = 39;

    public const int CanonBookCount = 66;

    public const string OldTestament = "old";

    public const string NewTestament = "new";

    private static readonly Dictionary<string, int> OrdinalsBySlug = BuildSlugIndex();

    public static IEnumerable<int> Ordinals => Enumerable.Range(1, CanonBookCount);

    /// <summary>
    /// Resolves the <c>{book}</c> path segment, which is an ordinal ("1"), a slug ("genesis",
    /// "1-samuel") or any name or abbreviation the corpus files use ("Numeri", "Gen").
    /// </summary>
    public static int? ResolveOrdinal(string? book)
    {
        if (string.IsNullOrWhiteSpace(book))
        {
            return null;
        }

        var trimmed = book.Trim();
        if (int.TryParse(trimmed, out var ordinal))
        {
            return IsInCanon(ordinal) ? ordinal : null;
        }

        if (OrdinalsBySlug.TryGetValue(Slugify(trimmed), out var bySlug))
        {
            return bySlug;
        }

        var abbreviation = BibleBookAbbreviation.GetAbbreviation(trimmed);
        return abbreviation is not null && IsInCanon(abbreviation.Ordinal) ? abbreviation.Ordinal : null;
    }

    public static bool IsInCanon(int ordinal)
    {
        return ordinal >= 1 && ordinal <= CanonBookCount;
    }

    public static string Name(int ordinal)
    {
        return BibleBookAbbreviation.GetByOrdinal(ordinal)!.FullName.Full;
    }

    public static string Abbreviation(int ordinal)
    {
        return BibleBookAbbreviation.GetByOrdinal(ordinal)!.TraditionalAbbreviation.Full;
    }

    public static string Slug(int ordinal)
    {
        return Slugify(Name(ordinal));
    }

    public static string Testament(int ordinal)
    {
        return ordinal <= OldTestamentBookCount ? OldTestament : NewTestament;
    }

    public static string FormatHint(string? book)
    {
        return $"'{book}' is not a book of the canon. Expected an ordinal from 1 to {CanonBookCount} " +
               "or a slug such as 'genesis' or '1-samuel'. GET /v1/books lists every accepted slug.";
    }

    private static string Slugify(string name)
    {
        var slug = new System.Text.StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character))
            {
                slug.Append(char.ToLowerInvariant(character));
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        return slug.ToString().TrimEnd('-');
    }

    private static Dictionary<string, int> BuildSlugIndex()
    {
        var index = new Dictionary<string, int>(CanonBookCount, StringComparer.Ordinal);
        for (var ordinal = 1; ordinal <= CanonBookCount; ordinal++)
        {
            index[Slugify(Name(ordinal))] = ordinal;
        }

        return index;
    }
}
