using Essenthos.Core.Configuration;
using Essenthos.Core.Loading.Frame;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// A translation EVIDENTIA measures from its files instead of from the corpus: one the corpus does
/// not hold yet, or one it may never hold because its terms forbid serving it. The words are read
/// by the same reader that would load them and placed in the canonical frame by the same rules, so
/// a word stands at the address it would have in the database; its id is a negative number of this
/// process's own, which no row of the corpus can carry, so nothing is ever scored against a link
/// that was not made for it.
/// </summary>
internal sealed class EvidentiaFileSourceTexts
{
    private readonly Func<string, TextSource> read;
    private readonly Func<VersificationRules> rules;
    private readonly Dictionary<string, IReadOnlyList<EvidentiaToken>> tokensByText = new(StringComparer.OrdinalIgnoreCase);
    private long nextId = -1;

    public EvidentiaFileSourceTexts(IConfiguration configuration, IHostEnvironment environment)
    {
        var resources = new Lazy<string>(() => ResourcePaths.Read(configuration, environment.ContentRootPath));
        read = slug => Read(resources.Value, slug);
        var loaded = new Lazy<VersificationRules>(() =>
            TvtmsReader.Read(ResourcePaths.File(resources.Value, "Versification", "TVTMS.txt")));
        rules = () => loaded.Value;
    }

    internal EvidentiaFileSourceTexts(Func<string, TextSource> read, Func<VersificationRules> rules)
    {
        this.read = read;
        this.rules = rules;
    }

    /// <summary>The canonical chapters of a book the text holds a word in, within the range asked.</summary>
    public IReadOnlyList<int> Chapters(string slug, int canonicalBook, int? firstChapter, int? lastChapter) =>
        [.. Words(slug)
            .Where(token => token.Address.Book == canonicalBook)
            .Select(token => token.Address.Chapter)
            .Where(chapter => (!firstChapter.HasValue || chapter >= firstChapter.Value)
                && (!lastChapter.HasValue || chapter <= lastChapter.Value))
            .Distinct()
            .Order()];

    public List<EvidentiaToken> Tokens(string slug, int canonicalBook, int canonicalChapter, int? canonicalVerse)
    {
        var tokens = Words(slug)
            .Where(token => token.Address.Book == canonicalBook
                && token.Address.Chapter == canonicalChapter
                && (!canonicalVerse.HasValue || token.Address.Verse == canonicalVerse.Value))
            .ToList();
        return tokens.Count > 0
            ? tokens
            : throw new InvalidOperationException(
                $"{slug} has no words at canonical address {canonicalBook}:{canonicalChapter}" +
                (canonicalVerse.HasValue ? $":{canonicalVerse.Value}." : "."));
    }

    /// <summary>
    /// Every word of the text at its canonical address. A verse the frame places at two addresses is
    /// read at its primary one, as the corpus reader reads it, so a chapter never holds a word twice.
    /// </summary>
    private IReadOnlyList<EvidentiaToken> Words(string slug)
    {
        // Several books of a run read at once, and a text's words must be numbered once, in one order.
        lock (tokensByText)
        {
            return tokensByText.TryGetValue(slug, out var known) ? known : tokensByText[slug] = Numbered(slug);
        }
    }

    private List<EvidentiaToken> Numbered(string slug)
    {
        var source = read(slug);
        var verses = source.Books
            .SelectMany(book => book.Chapters.SelectMany(chapter => chapter.Verses
                .Where(verse => verse.Words.Count > 0)
                .Select(verse => (Book: book.CanonicalOrdinal, Chapter: chapter.Number, Verse: verse))))
            .ToList();
        var primary = CanonicalFrameLoader.Expected(
                rules(),
                source.Definition.Versification,
                [
                    .. verses.Select((verse, index) => new PlacedVerse(index, verse.Book, verse.Chapter,
                        verse.Verse.Number, verse.Verse.Label, verse.Verse.Words.Sum(word => word.Surface.Length))),
                ],
                BookTraditions.For(slug))
            .Where(reference => reference.IsPrimary)
            .ToDictionary(reference => reference.VerseId);

        var tokens = new List<EvidentiaToken>();
        foreach (var (index, (_, _, verse)) in verses.Index())
        {
            var placed = primary[index];
            var address = new EvidentiaAddress(placed.Book, placed.Chapter, placed.Verse);
            tokens.AddRange(verse.Words.Select((word, index) => new EvidentiaToken(
                Id: nextId--,
                Address: address,
                Position: index + 1,
                Surface: word.Surface,
                Language: source.Definition.Language,
                Trailer: word.Trailer,
                Lemma: word.Lemma,
                StrongNumber: word.StrongNumber,
                Gloss: word.Gloss)));
        }

        return [.. tokens.OrderBy(token => token.Address.Book)
            .ThenBy(token => token.Address.Chapter)
            .ThenBy(token => token.Address.Verse)
            .ThenBy(token => token.Position)];
    }

    /// <summary>
    /// The texts that can be read this way, by slug: every English text the corpus loads from its
    /// own folder, and the New World Translation, which the corpus never loads.
    /// </summary>
    private static TextSource Read(string resources, string slug)
    {
        if (string.Equals(slug, NewWorldTextSource.Slug, StringComparison.OrdinalIgnoreCase))
        {
            return NewWorldTextSource.Read(Path.Combine(resources, NewWorldTextSource.Folder));
        }

        var english = EnglishTextSource.Definitions
            .FirstOrDefault(entry => string.Equals(entry.Value.Slug, slug, StringComparison.OrdinalIgnoreCase));
        return english.Key is { } folder
            ? EnglishTextSource.Read(Path.Combine(resources, folder))
            : throw new ArgumentException(
                $"{slug} cannot be read from its files: only the English texts read from their own folders and " +
                $"{NewWorldTextSource.Slug} can. Measure any other text from the corpus, without --source-from-files.",
                nameof(slug));
    }
}
