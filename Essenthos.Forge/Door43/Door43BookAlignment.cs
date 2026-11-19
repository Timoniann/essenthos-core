using Essenthos.Core.Loading;

namespace Essenthos.Core.Door43;

/// <summary>
/// One book of a text aligned in somebody's own translationCore project on Door43, outside the release the text's
/// interlinear comes from: the same base text, the same USFM 3 alignment, a different person and often different terms.
/// The Ohienko's interlinear (<c>uk_ts/uk_ubio</c>) aligns parts of eight New Testament books; these projects align
/// Romans, Philippians and Revelation nearly whole, about a quarter of John's words, and more verses of 1 Peter, which the
/// interlinear holds a part of.
///
/// Each project is pinned to the commit its file was taken at (<c>scripts/fetch-door43-ohienko-books.ps1</c>), and the
/// file stands in <c>Door43/<see cref="Folder"/></c> under the Paratext name of its book, beside the project's licence and
/// manifest. Nobody checked these projects: no checker is listed in any manifest, so a book is listed here only after 40
/// of its links were read by hand and about 95% of them were right. Hebrews (IlliaTs) and Luke (AndreyParf) are fetched
/// and not listed: about one link in eight read was wrong, most of them a small word tied to the next Greek word in order
/// (TSK-1905, Resources/Door43/LICENCE.md).
/// </summary>
/// <param name="Slug">The text whose words the alignment ties.</param>
/// <param name="File">The file under <c>Door43/<see cref="Folder"/></c>.</param>
/// <param name="Book">The book's English name, as the source of its links says it.</param>
/// <param name="Repository">The project on git.door43.org, <c>owner/name</c>; its owner is the person who aligned it.</param>
/// <param name="Commit">The commit the file was taken at.</param>
/// <param name="Licence">The licence the project's own LICENSE.md and manifest state.</param>
internal sealed record Door43BookAlignment(string Slug, string File, string Book, string Repository, string Commit, string Licence)
{
    public const string Folder = "uk_ubio_books";

    /// <summary>
    /// What the links of a project dedicated to the public domain begin with, and what <c>Datasets</c> credits them by.
    /// </summary>
    public const string PublicDomain = "Door43 Ohienko alignment, CC0 1.0";

    /// <summary>What the links of a project under CC BY-SA 4.0 begin with.</summary>
    public const string ShareAlike = "Door43 Ohienko alignment, CC BY-SA 4.0";

    /// <summary>
    /// The letter one project's files print for the apostrophe of a Ukrainian word, U+0185 (Імƅя for Ім'я, обƅявився): no
    /// Ukrainian spelling has it, and the projects that print it print the same words with ’ elsewhere.
    /// </summary>
    private const char ToneSix = 'ƅ';

    public static IReadOnlyList<Door43BookAlignment> All { get; } =
    [
        new(Bible4uTextSource.Ohienko, "44-JHN.usfm", "John", "davidmax/uk_ubi_jhn_book",
            "356181788046b9a96bf601cb8366befaf3ea90fa", "CC0 1.0"),
        new(Bible4uTextSource.Ohienko, "46-ROM.usfm", "Romans", "ViraVlasiuk/uk_ubio_rom_book",
            "10f17542785e4fa33a8a54373b06bb6131f760bf", "CC0 1.0"),
        new(Bible4uTextSource.Ohienko, "51-PHP.usfm", "Philippians", "ViraVlasiuk/uk_ubio_php_book",
            "07959c15f22a8b147195a06c1c1d75e30dcb4545", "CC BY-SA 4.0"),
        new(Bible4uTextSource.Ohienko, "61-1PE.usfm", "1 Peter", "AndreyParf/uk_ubio_1pe_book",
            "241510b52609dde8a4edf4df9b2e3618fed32718", "CC BY-SA 4.0"),
        new(Bible4uTextSource.Ohienko, "67-REV.usfm", "Revelation", "ViraVlasiuk/uk_ogi_rev_book",
            "324455619b1e2966d2593672daca61bf050822d5", "CC BY-SA 4.0"),
    ];

    /// <summary>The person who aligned it: the project's owner on Door43.</summary>
    public string Author => Repository[..Repository.IndexOf('/')];

    /// <summary>
    /// What every link drawn from the project says about itself: its terms first, which is what the credit is found by,
    /// then the book, the person and the commit. A project taken again at a later commit is the same line
    /// (<see cref="Mapping.Door43AlignmentSource.Line"/>).
    /// </summary>
    public string Source =>
        $"{Terms}: {Book} by {Author} in translationCore, git.door43.org/{Repository}, commit {Commit[..8]}";

    private string Terms => Licence switch
    {
        "CC0 1.0" => PublicDomain,
        "CC BY-SA 4.0" => ShareAlike,
        _ => throw new InvalidOperationException(
            $"{Repository} states {Licence}, and no dataset credits Ohienko links under it. Declare one in Datasets first."),
    };

    public static IEnumerable<Door43BookAlignment> Of(string slug) => All.Where(book => book.Slug == slug);

    /// <summary>The project's file as the reader reads it, with the apostrophe it prints as a letter made one.</summary>
    public static string Spelling(string usfm) => usfm.Replace(ToneSix, '’');
}
