using System.Collections.Concurrent;
using Essenthos.Core.Products;

namespace Essenthos.Core.Door43;

/// <summary>
/// One book of a text aligned in somebody's own translationCore project on Door43, outside the release the text's
/// interlinear comes from: the same base text, the same USFM 3 alignment, a different person and often different terms.
/// The Ohienko's interlinear (<c>uk_ts/uk_ubio</c>) aligns parts of eight New Testament books; these projects align
/// Romans, Philippians and Revelation nearly whole, and more verses of 1 Peter, which the interlinear holds a part of.
///
/// The projects are declared in <c>sources/door43-projects.tsv</c>, each pinned to the commit its file was taken at and
/// fetched from it by <c>scripts/fetch-door43-ohienko-books.ps1</c>; the file stands in <c>Door43/<see cref="Folder"/></c>
/// under the Paratext name of its book, beside the project's licence and manifest. Nobody checked these projects: no checker
/// is listed in any manifest, so a project is read (its row's <c>read</c> is <c>t</c>) only after its links were read by
/// hand, and the row says what was found (TSK-1905, Resources/Door43/LICENCE.md, NOT-0228).
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

    public const string Product = "sources/door43-projects";

    /// <summary>The file that declares the projects, under Resources; the pairs that read one name it among their inputs.</summary>
    public const string Declared = "Essenthos/sources/door43-projects.tsv";

    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<Door43BookAlignment>>> Loaded = new(StringComparer.Ordinal);

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

    /// <summary>The projects the store's file says are read, in its order, read once a store.</summary>
    public static IReadOnlyList<Door43BookAlignment> All(ProductStore store) =>
        Loaded.GetOrAdd(Path.GetFullPath(store.ResourcesPath), _ => new Lazy<IReadOnlyList<Door43BookAlignment>>(() => Read(store))).Value;

    private static List<Door43BookAlignment> Read(ProductStore store)
    {
        if (!store.Exists(Product))
        {
            throw new FileNotFoundException($"The Door43 projects are not declared at {store.FullPath(Declared)}.", store.FullPath(Declared));
        }

        using var reader = store.Open(Product, Door43ProjectRow.Schema);
        var rows = reader.Rows().ToList();
        if (rows.GroupBy(row => row.File, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } twice)
        {
            throw new InvalidDataException($"{Declared}: {twice.Key} is declared twice.");
        }

        if (rows.FirstOrDefault(row => row.Commit.Length != 40 || !row.Commit.All(char.IsAsciiHexDigitLower)) is { } unpinned)
        {
            throw new InvalidDataException($"{Declared}: {unpinned.Repository} is pinned to \"{unpinned.Commit}\", which is no commit.");
        }

        var read = rows.Where(row => row.Read).Select(row => new Door43BookAlignment(row.Text, row.File, row.Book, row.Repository, row.Commit, row.Licence)).ToList();
        // A licence no dataset credits is refused here, before any link is drawn under it.
        foreach (var book in read)
        {
            _ = book.Source;
        }

        return read;
    }

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

    public static IEnumerable<Door43BookAlignment> Of(ProductStore store, string slug) => All(store).Where(book => book.Slug == slug);

    /// <summary>The project's file as the reader reads it, with the apostrophe it prints as a letter made one.</summary>
    public static string Spelling(string usfm) => usfm.Replace(ToneSix, '’');
}

/// <summary>One row of <c>sources/door43-projects.tsv</c>, as the file writes it (<see cref="Door43BookAlignment"/>; the fetch script reads the hashes).</summary>
internal sealed record Door43ProjectRow(
    string Text,
    string File,
    string Book,
    string Repository,
    string Commit,
    string Licence,
    bool Read,
    string BookSha256,
    string LicenceSha256,
    string ManifestSha256,
    long Milestones,
    string Reason)
{
    public static readonly ProductSchema<Door43ProjectRow> Schema = new ProductSchema<Door43ProjectRow>()
        .Text("text", r => r.Text)
        .Text("file", r => r.File)
        .Text("book", r => r.Book)
        .Text("repository", r => r.Repository)
        .Text("commit", r => r.Commit)
        .Text("licence", r => r.Licence)
        .Boolean("read", r => r.Read)
        .Text("book_sha256", r => r.BookSha256)
        .Text("licence_sha256", r => r.LicenceSha256)
        .Text("manifest_sha256", r => r.ManifestSha256)
        .Integer("milestones", r => r.Milestones)
        .Text("reason", r => r.Reason)
        .Reads(row => new Door43ProjectRow(row.Text(0), row.Text(1), row.Text(2), row.Text(3), row.Text(4), row.Text(5), row.Boolean(6), row.Text(7),
            row.Text(8), row.Text(9), row.Integer(10), row.Text(11)));
}
