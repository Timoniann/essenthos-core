namespace Essenthos.Core.Corpus;

/// <summary>
/// The canons: which books a tradition holds, in what order, under what headings, and what it
/// calls the whole collection.
///
/// A section is not a fact about a book. Ruth is Ketuvim to a Jew and a history book to a
/// Christian; Daniel is Ketuvim and a Major Prophet; both are true at once, and a
/// <c>testament</c> column on a book can only ever record one of them. So a book keeps what is
/// its own — ordinal, name, abbreviation, slug — and order, heading and inclusion are asked of a
/// canon.
///
/// The Tanakh's order is not invented here. It is BHSA's, exactly, and it is already in the
/// database on <c>book.position</c> — the column forced into existence when the old schema
/// shipped Tanakh and canonical order under one name. It was written to stop a defect and
/// it turned out to be the whole Jewish reading order, sitting there loaded.
/// </summary>
internal static class Canons
{
    public const string Default = "protestant";

    /// <summary>
    /// Genesis to Malachi and Matthew to Revelation, the 66 the corpus has always answered. It
    /// stays the default so that nothing reading the API today sees a change.
    /// </summary>
    private static readonly CanonDefinition Protestant = new(
        "protestant",
        "Protestant",
        "Bible",
        "The sixty-six books of the Protestant canon, in the order the English Bibles print them.",
        [
            new CanonSection("old-testament", "Old Testament", [.. Range(1, 39)]),
            new CanonSection("new-testament", "New Testament", [.. Range(40, 66)]),
        ]);

    /// <summary>
    /// The Hebrew Scriptures alone, in their own order and under their own headings — and not
    /// called a Bible, because to the reader this canon is for, it is not one.
    /// </summary>
    private static readonly CanonDefinition Tanakh = new(
        "tanakh",
        "Tanakh",
        "Scripture",
        "The Hebrew Scriptures in their own order: Torah, Nevi'im, Ketuvim. Ruth stands among the " +
        "Writings rather than after Judges, and Chronicles closes the collection.",
        [
            new CanonSection("torah", "Torah", [1, 2, 3, 4, 5]),
            new CanonSection("neviim", "Nevi'im", [
                6, 7, 9, 10, 11, 12,
                23, 24, 26,
                28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39,
            ]),
            new CanonSection("ketuvim", "Ketuvim", [
                19, 18, 20, 8, 22, 21, 25, 17, 27, 15, 16, 13, 14,
            ]),
        ]);

    /// <summary>
    /// The Protestant canon with the seven books and the additions Rome receives. Susanna and Bel stand
    /// after Daniel, whose thirteenth and fourteenth chapters the Vulgate prints them as, and the Letter
    /// of Jeremiah after Lamentations, where Baruch's sixth chapter would be.
    /// </summary>
    private static readonly CanonDefinition Catholic = new(
        "catholic",
        "Catholic",
        "Bible",
        "The Protestant canon with the deuterocanonical books, placed where the Vulgate places them.",
        [
            new CanonSection("old-testament", "Old Testament", [
                .. Range(1, 17), 70, 71, .. Range(18, 22), 75, 72, .. Range(23, 24), 25, 67, 76,
                26, 27, 77, 78, .. Range(28, 39), 73, 74,
            ]),
            new CanonSection("new-testament", "New Testament", [.. Range(40, 66)]),
        ]);

    /// <summary>
    /// What the Greek canon receives beyond the Catholic one: 1 Esdras, the Prayer of Manasseh, 3
    /// Maccabees, and — in the fuller Greek Bibles — Psalm 151 and 4 Maccabees as an appendix.
    /// </summary>
    private static readonly CanonDefinition Orthodox = new(
        "orthodox",
        "Orthodox",
        "Bible",
        "The Greek canon: the deuterocanon, plus 1 Esdras, the Prayer of Manasseh, 3 Maccabees and " +
        "Psalm 151, with 4 Maccabees printed as an appendix.",
        [
            new CanonSection("old-testament", "Old Testament", [
                .. Range(1, 16), 68, 17, 70, 71, .. Range(18, 19), 82, .. Range(20, 22), 75, 72,
                .. Range(23, 25), 67, 76, .. Range(26, 39), 73, 74, 80, 79,
            ]),
            new CanonSection("new-testament", "New Testament", [.. Range(40, 66)]),
            new CanonSection("appendix", "Appendix", [81]),
        ]);

    /// <summary>
    /// Every book the Septuagint witnesses here print, in the order they print them. This is the
    /// canon a Septuagint reader wants; Susanna, Bel and the Letter of Jeremiah stand as books
    /// because both editions print them as books.
    ///
    /// The Psalms of Solomon are in the appendix rather than among the poetry, because that is
    /// where Swete puts them and because no church has ever received them: they are bound into the
    /// great Greek Bibles and listed in the index of Alexandrinus, which is a fact about the
    /// manuscripts rather than a claim about the canon. Brenton does not print them at all.
    /// </summary>
    private static readonly CanonDefinition Septuagint = new(
        "septuagint",
        "Septuagint",
        "Scripture",
        "Every book the Greek Old Testament prints, in its own order. Esther and Daniel are the " +
        "Greek ones, which are longer than the Hebrew rather than different books.",
        [
            new CanonSection("law", "Law", [1, 2, 3, 4, 5]),
            new CanonSection("histories", "Histories", [
                6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 68, 17, 70, 71, 73, 74, 80, 81,
            ]),
            new CanonSection("poetry", "Poetry", [19, 82, 20, 21, 22, 18, 75, 72, 79]),
            new CanonSection("prophets", "Prophets", [
                28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39,
                23, 24, 25, 67, 76, 26, 27, 77, 78,
            ]),
            new CanonSection("appendix", "Appendix", [84]),
        ]);

    /// <summary>
    /// The eighty-one books of the Ethiopian Orthodox Tewahedo Church, as its printed Bibles count and
    /// order them: the narrow canon, fifty-four books of the Old Testament and twenty-seven of the New.
    ///
    /// Several of the Greek books are divided or replaced. Ezra and Nehemiah are two books, 3 Ezra is
    /// the Greek 1 Esdras and Ezra Sutuel the apocalypse the Latin calls 4 Ezra; Proverbs is two books,
    /// Messale and Tägsas; the Maccabees are not received and the three books of Meqabyan, which are a
    /// different work, stand in their place. The Prayer of Manasseh is inside 2 Chronicles and Psalm
    /// 151 inside the Psalter, so neither is a book here. The broader reckoning, which adds eight books
    /// of church order to the New Testament, is a list no printed Bible follows.
    /// </summary>
    private static readonly CanonDefinition Ethiopian = new(
        "ethiopian",
        "Ethiopian Orthodox",
        "Bible",
        "The eighty-one books of the Ethiopian Orthodox Tewahedo Church, with Enoch, Jubilees, the three " +
        "books of Meqabyan and 4 Baruch, and Proverbs as two books, Messale and Tägsas.",
        [
            new CanonSection("old-testament", "Old Testament", [
                .. Range(1, 14), 86, 85, 15, 16, 68, 69, 70, 71, 17, 87, 88, 89, 18, 19, 91, 92, 75, 21, 22,
                72, 23, 24, 67, 25, 76, 90, 26, 27, 28, 30, 33, 29, 31, 32, .. Range(34, 39),
            ]),
            new CanonSection("new-testament", "New Testament", [.. Range(40, 66)]),
        ]);

    public const string Synodal = "synodal";

    /// <summary>
    /// The Russian Synodal Bible in the order the Russian Orthodox Church prints it, with the eleven
    /// books it prints and marks as non-canonical standing where it prints them: 2 Ezra, Tobit and
    /// Judith after Nehemiah, Wisdom and Sirach after the Song, the Letter of Jeremiah and Baruch after
    /// Lamentations, and the three Maccabees and 3 Ezra after Malachi. A run of them is a section of
    /// its own, so the marking is where the book is. Its 2 Ezra is the Greek 1 Esdras and its 3 Ezra
    /// the Latin 4 Ezra. The New Testament is in its order too: the Catholic epistles before Paul's.
    /// </summary>
    private static readonly CanonDefinition RussianSynodal = new(
        Synodal,
        "Russian Synodal",
        "Bible",
        "The Synodal Bible in the order the Russian Orthodox Church prints it, with the eleven books it marks " +
        "as non-canonical standing where it prints them, and the Catholic epistles before Paul's.",
        [
            new CanonSection("old-testament", "Old Testament", [.. Range(1, 16)]),
            new CanonSection(NonCanonical, NonCanonicalName, [68, 70, 71]),
            new CanonSection("old-testament", "Old Testament", [.. Range(17, 22)]),
            new CanonSection(NonCanonical, NonCanonicalName, [75, 72]),
            new CanonSection("old-testament", "Old Testament", [.. Range(23, 25)]),
            new CanonSection(NonCanonical, NonCanonicalName, [76, 67]),
            new CanonSection("old-testament", "Old Testament", [.. Range(26, 39)]),
            new CanonSection(NonCanonical, NonCanonicalName, [73, 74, 80, 69]),
            new CanonSection("new-testament", "New Testament", [
                .. Range(40, 44), .. Range(59, 65), .. Range(45, 58), 66,
            ]),
        ]);

    private const string NonCanonical = "non-canonical";

    private const string NonCanonicalName = "Non-canonical books";

    private static readonly CanonDefinition[] All =
        [Protestant, Tanakh, Catholic, Orthodox, Septuagint, Ethiopian, RussianSynodal];

    public static IReadOnlyList<CanonDefinition> List => All;

    public static CanonDefinition? Find(string? slug) =>
        slug is not { Length: > 0 }
            ? Protestant
            : All.FirstOrDefault(canon => string.Equals(canon.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public static string Names => string.Join(", ", All.Select(canon => canon.Slug));

    /// <summary>The heading a book sits under in one canon, or null where that canon omits it.</summary>
    public static string? SectionOf(CanonDefinition canon, int ordinal) =>
        canon.Sections.FirstOrDefault(section => section.Ordinals.Contains(ordinal))?.Slug;

    private static IEnumerable<int> Range(int from, int to) => Enumerable.Range(from, to - from + 1);
}

/// <param name="Collection">
/// What this canon calls the whole thing. "Bible" for the Christian canons and "Scripture" for the
/// Tanakh — a reader opening the Hebrew Scriptures should not be told by the furniture that they
/// are reading somebody else's book.
/// </param>
internal sealed record CanonDefinition(
    string Slug,
    string Name,
    string Collection,
    string Description,
    IReadOnlyList<CanonSection> Sections)
{
    public IEnumerable<int> Ordinals => Sections.SelectMany(section => section.Ordinals);

    public int BookCount => Sections.Sum(section => section.Ordinals.Count);
}

internal sealed record CanonSection(string Slug, string Name, IReadOnlyList<int> Ordinals);
