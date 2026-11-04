using Essenthos.Core.Corpus;
using Essenthos.Core.Door43;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// Which word of a translation renders which Hebrew or Greek word, as its translators tied them in Door43's USFM 3
/// alignments: unfoldingWord's Ukrainian interlinear of the Ohienko, the Russian Synodal's three books, the unfoldingWord
/// Literal Text and every <see cref="Door43AlignedBible"/>. Read from <c>Door43/&lt;folder&gt;/*.usfm</c> and both texts'
/// words and frames; the join is the loader's own (<see cref="InterlinearLinkLoader.Book"/>, <see cref="InterlinearJoin"/>).
///
/// <para>
/// **An interlinear numbers a verse as its translation prints it**, so a verse of the file is the translation's own
/// verse, and the witness's words are those standing at that verse's frame address. The database's loader keyed both
/// sides by frame address, which read the verse of the file against the translation's verse placed at that number,
/// wherever the translation's frame moves a verse.
/// </para>
/// </summary>
internal sealed class Door43AlignmentSource : IPairSource
{
    public const string Folder = "Door43";

    private const string Renders = "renders";

    /// <summary>
    /// The translations whose alignment reaches the Hebrew Bible; the others' Old Testament books, where a release ships
    /// them, are not aligned.
    /// </summary>
    private static readonly HashSet<string> WithHebrew = new(
    [
        Bible4uTextSource.Ohienko, UnfoldingWordTextSource.Slug, EbibleTextSource.VanDyck, EbibleTextSource.IrvHindi,
        Door43TextSource.Simplified, Door43TextSource.VietnameseLiteral,
    ], StringComparer.Ordinal);

    private static readonly IReadOnlyList<string> Translations =
    [
        Bible4uTextSource.Ohienko, Bible4uTextSource.Synodal, UnfoldingWordTextSource.Slug, .. Door43AlignedBible.All.Select(bible => bible.Slug),
    ];

    private static readonly HashSet<string> Sources = Translations.Select(slug => InterlinearLinkLoader.Interlinear(slug).Source).ToHashSet(StringComparer.Ordinal);

    public string Method => MappingMethods.Stated;

    public string Name => "door43";

    public bool States(string pair) =>
        MappingProducts.Split(pair) is var (from, to)
        && Translations.Contains(from)
        && (to == NestleTextSource.Slug || (to == BhsaTextSource.Slug && WithHebrew.Contains(from)));

    public bool Makes(string source) => Sources.Contains(source);

    public IEnumerable<string> Inputs(ProductStore store, string pair)
    {
        var (from, to) = MappingProducts.Split(pair);
        var folder = InterlinearLinkLoader.Interlinear(from).Folder;
        return Books(store, folder, to).Select(book => $"{Folder}/{folder}/{Path.GetFileName(book.File)}");
    }

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var (from, to) = MappingProducts.Split(pair);
        var (folder, source) = InterlinearLinkLoader.Interlinear(from);
        var translation = texts[from];
        var witness = texts[to];
        var unnumbered = InterlinearLinkLoader.Unnumbered(from);

        var rows = new Dictionary<string, MappingRow>(StringComparer.Ordinal);
        var total = new InterlinearJoinAccount();
        foreach (var (file, ordinal) in Books(texts.Store, folder, to))
        {
            var read = Usfm3AlignmentReader.Read(File.ReadAllText(file), unnumbered);
            var (pairs, account) = InterlinearLinkLoader.Book(
                Path.GetFileNameWithoutExtension(file),
                read,
                Translated(translation, ordinal),
                Witness(witness, ordinal),
                address => translation.Primary(new VerseKey(ordinal, address.Chapter, address.Number)) is var (book, chapter, verse)
                           && book == ordinal
                    ? (chapter, verse)
                    : address);
            total.Add(account);
            foreach (var joined in pairs)
            {
                var row = new MappingRow(null,
                    MappingText.Keys(joined.From.Select(translation.Key)),
                    MappingText.Keys(joined.To.Select(witness.Key)),
                    Renders, Method, null, source, null, null, null);
                rows.TryAdd(row.Shape, row);
            }
        }

        log($"{total.VersesJoined} of {total.VersesRead} verses joined, {total.TranslatedWordsJoined} translated words, {rows.Count} links");
        return rows.Values;
    }

    /// <summary>The release's books the witness answers, in name order, as the database's loader read them.</summary>
    private static IEnumerable<(string File, int Ordinal)> Books(ProductStore store, string folder, string witness)
    {
        var path = store.FullPath($"{Folder}/{folder}");
        if (!Directory.Exists(path))
        {
            yield break;
        }

        foreach (var file in Directory.GetFiles(path, "*.usfm").OrderBy(name => name, StringComparer.Ordinal))
        {
            if (InterlinearLinkLoader.Ordinal(Path.GetFileName(file)) is { } ordinal
                && (ordinal <= BookReferences.OldTestamentBookCount ? BhsaTextSource.Slug : NestleTextSource.Slug) == witness)
            {
                yield return (file, ordinal);
            }
        }
    }

    /// <summary>The translation's words of a book by the verse it prints them in.</summary>
    private static Dictionary<(int, int), List<InterlinearWord>> Translated(ProductText text, int book)
    {
        var words = new Dictionary<(int, int), List<InterlinearWord>>();
        foreach (var verse in text.Verses.Where(verse => verse.Book == book))
        {
            var address = (verse.Chapter, verse.Verse);
            if (!words.TryGetValue(address, out var list))
            {
                words[address] = list = [];
            }

            list.AddRange(text.InVerse(verse).Select(id => Word(text, id)));
        }

        return words;
    }

    /// <summary>The witness's words of a book by the frame address their verse stands at first.</summary>
    private static Dictionary<(int, int), List<InterlinearWord>> Witness(ProductText text, int book) =>
        text.ByAddress.Where(address => address.Key.Book == book)
            .ToDictionary(address => (address.Key.Chapter, address.Key.Verse), address => address.Value.Select(id => Word(text, id)).ToList());

    private static InterlinearWord Word(ProductText text, int id)
    {
        var word = text.Words[id];
        return new InterlinearWord(id, word.Normalised ?? string.Empty, text.Language, word.Surface, word.Strong);
    }
}
