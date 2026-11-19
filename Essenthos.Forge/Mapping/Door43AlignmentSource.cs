using Essenthos.Core.Corpus;
using Essenthos.Core.Door43;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// Which word of a translation renders which Hebrew or Greek word, as its translators tied them in Door43's USFM 3
/// alignments: unfoldingWord's Ukrainian interlinear of the Ohienko, the Russian Synodal's three books, the unfoldingWord
/// Literal Text and every <see cref="Door43AlignedBible"/>, and the books people aligned in projects of their own
/// (<see cref="Door43BookAlignment"/>). Read from <c>Door43/&lt;folder&gt;/*.usfm</c> and both texts' words and frames; the join
/// is the loader's own (<see cref="InterlinearLinkLoader.Book"/>, <see cref="InterlinearJoin"/>).
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

    private static readonly HashSet<string> Lines =
    [
        .. Translations.Select(slug => Line(InterlinearLinkLoader.Interlinear(slug).Source)),
        .. Door43BookAlignment.All.Select(book => Line(book.Source)),
    ];

    public string Method => MappingMethods.Stated;

    public string Name => "door43";

    public bool States(string pair) =>
        MappingProducts.Split(pair) is var (from, to)
        && Translations.Contains(from)
        && (to == NestleTextSource.Slug || (to == BhsaTextSource.Slug && WithHebrew.Contains(from)));

    /// <summary>
    /// A source names the release its alignment was taken from, so the rows of an earlier release of the same alignment are
    /// this producer's too: they are made again from the release the store holds, never carried beside its rows.
    /// </summary>
    public bool Makes(string source) => Lines.Contains(Line(source));

    /// <summary>
    /// A source without the release or commit it names: the text up to <c>, release </c> or <c>, commit </c>, or the whole
    /// source where it names neither.
    /// </summary>
    internal static string Line(string source) =>
        source.IndexOf(", release ", StringComparison.Ordinal) is var release and >= 0 ? source[..release]
        : source.IndexOf(", commit ", StringComparison.Ordinal) is var commit and >= 0 ? source[..commit]
        : source;

    public IEnumerable<string> Inputs(ProductStore store, string pair)
    {
        var (from, to) = MappingProducts.Split(pair);
        var folder = InterlinearLinkLoader.Interlinear(from).Folder;
        return Books(store, folder, to).Select(book => $"{Folder}/{folder}/{Path.GetFileName(book.File)}")
            .Concat(Projects(store, from, to).Select(project => $"{Folder}/{Door43BookAlignment.Folder}/{project.Book.File}"));
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
        var released = Books(texts.Store, folder, to).ToDictionary(book => book.Ordinal, book => book.File);
        var projects = Projects(texts.Store, from, to).ToDictionary(project => project.Ordinal, project => project.Book);
        foreach (var missing in Door43BookAlignment.Of(from).Where(book => Against(book, to) is not null && !File.Exists(texts.Store.FullPath(Stored(book)))))
        {
            log($"{missing.Repository} is declared and not in the store at {Stored(missing)}: run scripts/fetch-door43-ohienko-books.ps1");
        }

        void Add(Joined joined, string stating)
        {
            foreach (var pair in joined.Pairs)
            {
                var row = new MappingRow(MappingText.Keys(pair.From.Select(translation.Key)),
                    MappingText.Keys(pair.To.Select(witness.Key)),
                    Renders, Method, null, stating, null, null, null);
                rows.TryAdd(row.Shape, row);
            }

            total.Add(joined.Account);
        }

        foreach (var ordinal in released.Keys.Union(projects.Keys).Order())
        {
            var here = Translated(translation, ordinal);
            var there = Witness(witness, ordinal);
            (int, int) Frame((int Chapter, int Number) address) =>
                translation.Primary(new VerseKey(ordinal, address.Chapter, address.Number)) is var (book, chapter, verse) && book == ordinal
                    ? (chapter, verse)
                    : address;

            var stated = released.TryGetValue(ordinal, out var file)
                ? Usfm3AlignmentReader.Read(File.ReadAllText(file), unnumbered)
                : [];
            if (!projects.TryGetValue(ordinal, out var project))
            {
                Add(new Joined(InterlinearLinkLoader.Book(Path.GetFileNameWithoutExtension(file!), stated, here, there, Frame)), source);
                continue;
            }

            var read = Usfm3AlignmentReader.Read(Door43BookAlignment.Spelling(File.ReadAllText(texts.Store.FullPath(Stored(project)))), unnumbered);
            var theirs = read.Where(verse => SpelledAlike(verse, here)).ToList();
            var combined = Combined(stated, theirs,
                verses => InterlinearLinkLoader.Book(Path.GetFileNameWithoutExtension(project.File), verses, here, there, Frame));
            Add(combined.Kept, source);
            Add(combined.Project, project.Source);
            log($"{project.Repository}: {combined.Project.Account.SpansJoined} of {combined.Project.Account.Spans} spans joined, "
                + $"{combined.Project.Account.TranslatedWordsJoined} of {combined.Project.Account.TranslatedWords} translated words in spans, "
                + $"{combined.Project.Pairs.Count} links in {combined.Project.Account.VersesJoined} verses; "
                + $"{read.Count - theirs.Count} of its {read.Count} verses left out, their words not ours"
                + (read.Count > theirs.Count
                    ? $" ({string.Join(", ", read.Except(theirs).Take(8).Select(verse => $"{verse.Chapter}:{verse.Number}"))})"
                    : string.Empty)
                + (stated.Count > 0
                    ? $"; of the {stated.Count} verses {folder} aligns, {combined.Agreed} it states alike stay its own and {combined.Replaced} "
                      + "the project states otherwise are read from the project"
                    : string.Empty));
        }

        log($"{total.VersesJoined} of {total.VersesRead} verses joined, {total.TranslatedWordsJoined} translated words, {rows.Count} links; "
            + $"{total.SpansOverANestJoined} of {total.SpansOverANest} spans over a nest joined ({total.NestsJoinedAsOneWord} as one word of the witness, {total.NestWordsPlacedBeside} words placed beside their nest, {total.NestWordsTheWitnessLacks} Greek words the witness lacks left out), {total.CompoundsJoined} words the witness prints whole joined to the spans over their parts, "
            + $"{total.TranslatedPiecesJoined} pieces of a word the corpus prints whole joined to it, {total.TranslatedFragments} left; "
            + $"{total.SuppliedWordsLeftOut} words the translators supplied in braces left unlinked ({total.SpansOnlySupplied} spans of nothing else)");
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

    /// <summary>The translation's books aligned in a project of its own on Door43 that the store holds, for the witness's testament.</summary>
    private static IEnumerable<(int Ordinal, Door43BookAlignment Book)> Projects(ProductStore store, string translation, string witness)
    {
        foreach (var book in Door43BookAlignment.Of(translation))
        {
            if (Against(book, witness) is { } ordinal && File.Exists(store.FullPath(Stored(book))))
            {
                yield return (ordinal, book);
            }
        }
    }

    /// <summary>The project's book, where the witness is the original of its testament; null where it is not.</summary>
    private static int? Against(Door43BookAlignment book, string witness) =>
        InterlinearLinkLoader.Ordinal(book.File) is { } ordinal
        && (ordinal <= BookReferences.OldTestamentBookCount ? BhsaTextSource.Slug : NestleTextSource.Slug) == witness
            ? ordinal
            : null;

    private static string Stored(Door43BookAlignment book) => $"{Folder}/{Door43BookAlignment.Folder}/{book.File}";

    /// <summary>
    /// Whether a project's verse aligns the words the corpus prints in that verse, letter for letter, case, apostrophes and
    /// punctuation aside. A project's base text is another copy of the Ohienko: in about one verse in a hundred a few words
    /// stand in the next verse, or another printing reads <c>зі</c> for <c>зо</c>, and the join's cursor would put such a
    /// span on another word that happens to be spelled alike (Rev 2:28's «і Я» on the «і» and «Я» of «і дам Я»).
    /// </summary>
    internal static bool SpelledAlike(AlignedVerse verse, Dictionary<(int, int), List<InterlinearWord>> translated) =>
        verse.Printed is { } printed
        && translated.TryGetValue((verse.Chapter, verse.Number), out var ours)
        && Letters(printed) == Letters(ours.Select(word => word.Written));

    private static string Letters(IEnumerable<string> words) =>
        string.Concat(words.SelectMany(word => word.ToLowerInvariant()).Where(character => char.IsLetterOrDigit(character) && character != 'ʼ'));

    /// <summary>
    /// A book the release aligns a part of and a person's project aligns again, verse by verse. The project is the later
    /// state of the same kind of work over the same words, so where it aligns a verse it is read, but a verse whose links come
    /// out exactly as the release's stays the release's: what the release states is not credited to somebody else for having
    /// been stated again. Where the project's verse joins nothing, the release's stands. No verse is linked from both.
    /// </summary>
    internal static (Joined Kept, Joined Project, int Agreed, int Replaced) Combined(
        IReadOnlyList<AlignedVerse> release,
        IReadOnlyList<AlignedVerse> project,
        Func<IReadOnlyList<AlignedVerse>, (List<InterlinearPair> Pairs, InterlinearJoinAccount Account)> join)
    {
        var kept = new Joined();
        var theirs = new Joined();
        var later = project.ToDictionary(verse => (verse.Chapter, verse.Number));
        var (agreed, replaced) = (0, 0);
        foreach (var verse in release)
        {
            var ours = join([verse]);
            if (!later.Remove((verse.Chapter, verse.Number), out var again))
            {
                kept.Add(ours);
                continue;
            }

            var other = join([again]);
            if (other.Pairs.Count == 0 || Same(ours.Pairs, other.Pairs))
            {
                kept.Add(ours);
                agreed += other.Pairs.Count == 0 ? 0 : 1;
            }
            else
            {
                theirs.Add(other);
                replaced++;
            }
        }

        theirs.Add(join([.. project.Where(verse => later.ContainsKey((verse.Chapter, verse.Number)))]));
        return (kept, theirs, agreed, replaced);
    }

    private static bool Same(List<InterlinearPair> one, List<InterlinearPair> other) =>
        one.Select(Shape).ToHashSet(StringComparer.Ordinal).SetEquals(other.Select(Shape));

    private static string Shape(InterlinearPair pair) => $"{string.Join(' ', pair.From)}|{string.Join(' ', pair.To)}";

    /// <summary>The links a join made and its account of them, gathered over verses.</summary>
    internal sealed class Joined
    {
        public List<InterlinearPair> Pairs { get; } = [];

        public InterlinearJoinAccount Account { get; } = new();

        public Joined() { }

        public Joined((List<InterlinearPair> Pairs, InterlinearJoinAccount Account) joined) => Add(joined);

        public void Add((List<InterlinearPair> Pairs, InterlinearJoinAccount Account) joined)
        {
            Pairs.AddRange(joined.Pairs);
            Account.Add(joined.Account);
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
        return new InterlinearWord(id, word.Normalised ?? string.Empty, text.Language, word.Surface, word.Strong, word.Trailer.Length == 0);
    }
}
