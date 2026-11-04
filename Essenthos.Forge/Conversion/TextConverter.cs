using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Loading;
using Essenthos.Core.Products;

namespace Essenthos.Core.Conversion;

/// <summary>A word while the converter holds it: every column, open to the stages that fill them.</summary>
internal sealed class ConvertedWord
{
    public required string Surface { get; set; }
    public required string Trailer { get; set; }
    public string? Lemma { get; set; }
    public string? Strong { get; set; }
    public string? Gloss { get; set; }
    public string? Morphology { get; set; }
    public string? Normalised { get; set; }
    public string? Graphical { get; set; }
    public bool Elided { get; set; }
    public string? Break { get; set; }
    public int? Supplied { get; set; }
    public int? Restored { get; set; }
    public string? SuppliedMark { get; set; }
    public int? Doubtful { get; set; }
    public int? Brackets { get; set; }
    public int? Subscription { get; set; }

    /// <summary>A corrections row added the word: no source printed it where it stands. Not written.</summary>
    public bool Added { get; set; }
}

internal sealed class ConvertedVerse(VerseKey key, int sequence, List<ConvertedWord> words)
{
    public VerseKey Key { get; } = key;
    public int Sequence { get; } = sequence;
    public List<ConvertedWord> Words { get; } = words;

    /// <summary>The addresses the edition prints for the verse in its own numbering, where it states them. Not written here; the notes hold them.</summary>
    public IReadOnlyList<(int Chapter, int Number)> Stated { get; init; } = [];

    /// <summary>Why the verse holds a psalm's superscription (<see cref="TextTitle"/>), or null.</summary>
    public string? Title { get; init; }
}

/// <summary>A text while it is converted: its declaration, books, verses in print order and notes.</summary>
internal sealed class ConvertedText
{
    public required string Slug { get; init; }
    public required TextDefinition Definition { get; init; }
    public required List<TextBook> Books { get; init; }
    public required List<ConvertedVerse> Verses { get; init; }
    public required List<TextNote> Notes { get; init; }

    /// <summary>Where each column the stages filled came from, for the words product's header.</summary>
    public List<KeyValuePair<string, string>> Provenance { get; } = [];

    public IEnumerable<(ConvertedVerse Verse, int Position, ConvertedWord Word)> Words() =>
        Verses.SelectMany(verse => verse.Words.Select((word, at) => (verse, at + 1, word)));
}

/// <summary>
/// Something the converter does to a text after reading and correcting it, from files it declares:
/// Strong numbers from a side source, lemmas, the printed forms. Each is a function of the words and its
/// sources, so the text is made again whenever one of them moves.
/// </summary>
internal interface ITextStage
{
    bool AppliesTo(string slug);

    IReadOnlyList<string> Sources(string slug);

    /// <summary>
    /// Other texts' products the stage reads, by product name (<c>texts/NESTLE1904/words</c>): inputs like
    /// <see cref="Sources"/>, found in whichever format they were written, and made before this text is converted.
    /// </summary>
    IReadOnlyList<string> Products(string slug) => [];

    void Apply(ConvertedText text, string resources);
}

internal sealed record ConversionOutcome(string Slug, bool Unchanged, int Verses, long Words, TimeSpan Elapsed, WordIdOutcome? Ids = null)
{
    public override string ToString() => Unchanged
        ? $"{Slug}: inputs unchanged, nothing written"
        : $"{Slug}: {Verses} verses, {Words} words in {Elapsed.TotalSeconds:0.0} s; ids: {Ids}";
}

/// <summary>
/// <c>forge convert</c>: a text from its declared sources into its products. Reads through the load's
/// reader, applies the corrections file, runs the stages that apply to it, gives each word the id it held
/// (<see cref="TextWordIds"/>), and writes <c>words</c>, <c>verses</c>, <c>notes</c>, <c>books</c> and
/// <c>text</c>. Nothing is written when no input moved since the products were made.
/// </summary>
internal static class TextConverter
{
    /// <summary>The book whose chapters open with a superscription, by its canonical ordinal.</summary>
    private const int Psalms = 19;

    public static IReadOnlyList<ITextStage> Stages { get; } = TextStages.All;

    /// <summary>Whether a text's stages read another text's products, so that it is converted after the texts that do not.</summary>
    public static bool ReadsOtherTexts(string slug) => Stages.Any(stage => stage.AppliesTo(slug) && stage.Products(slug).Count > 0);

    public static IReadOnlyList<ProductInput> Inputs(ProductStore store, CataloguedText text)
    {
        var paths = new List<string>(text.Sources);
        paths.AddRange(Stages.Where(stage => stage.AppliesTo(text.Slug)).SelectMany(stage => stage.Sources(text.Slug)));
        paths.AddRange(Stages.Where(stage => stage.AppliesTo(text.Slug)).SelectMany(stage => stage.Products(text.Slug))
            .Select(product => store.Find(product) ?? throw new FileNotFoundException(
                $"{text.Slug} is converted from {product}, which has not been made under {store.ResourcesPath}; convert that text first.")));
        foreach (var product in (string[])[TextCorrections.ProductOf(text.Slug), TextWordId.ProductOf(text.Slug)])
        {
            if (store.Find(product) is { } found)
            {
                paths.Add(found);
            }
        }

        return store.Inputs(paths.Distinct(StringComparer.Ordinal).Where(path => Exists(store, path)));
    }

    private static bool Exists(ProductStore store, string relative)
    {
        var full = store.FullPath(relative);
        return File.Exists(full) || Directory.Exists(full);
    }

    public static bool IsCurrent(ProductStore store, string slug, IReadOnlyList<ProductInput> inputs) =>
        !store.NeedsBuild(TextWord.ProductOf(slug), ProductKind.Machine, inputs)
        && !store.NeedsBuild(VerseManifestRow.ProductOf(slug), ProductKind.Machine, inputs)
        && !store.NeedsBuild(TextNote.ProductOf(slug), ProductKind.Machine, inputs)
        && !store.NeedsBuild(TextTitle.ProductOf(slug), ProductKind.Machine, inputs)
        && !store.NeedsBuild(TextBook.ProductOf(slug), ProductKind.Machine, inputs)
        && !store.NeedsBuild(TextField.ProductOf(slug), ProductKind.Hand, inputs);

    public static ConversionOutcome Convert(ProductStore store, string slug, bool force = false) =>
        Convert(store, TextCatalogue.Find(slug), force);

    public static ConversionOutcome Convert(ProductStore store, CataloguedText declared, bool force = false)
    {
        var started = Stopwatch.StartNew();
        var slug = declared.Slug;
        var inputs = Inputs(store, declared);
        if (!force && IsCurrent(store, slug, inputs))
        {
            return new ConversionOutcome(slug, true, 0, 0, started.Elapsed);
        }

        var text = Read(store, declared);
        var (ids, outcome) = TextWordIds.Assign(store, text);
        text.Provenance.Add(new("ids", outcome.ToString()));
        text.Provenance.Add(new(WordIdOutcome.GivenNote, outcome.Given.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Write(store, text, inputs, ids);
        return new ConversionOutcome(slug, false, text.Verses.Count, text.Verses.Sum(v => (long)v.Words.Count), started.Elapsed, outcome);
    }

    /// <summary>The text read, corrected and put through its stages, without writing anything.</summary>
    public static ConvertedText Read(ProductStore store, CataloguedText declared)
    {
        var (text, corrections) = Staged(store, declared, null);
        var (set, cleared) = corrections.ApplyNumbers(text);
        if (set + cleared > 0)
        {
            text.Provenance.Add(new("strong-corrected", $"{set} numbers set and {cleared} cleared by the corrections file"));
        }

        return text;
    }

    /// <summary>
    /// The text read and corrected and put through its stages up to the first <paramref name="until"/> accepts, which is not
    /// applied (null: every stage); the number corrections, applied after every stage, are not. Its words stand where the
    /// words product puts them.
    /// </summary>
    public static (ConvertedText Text, TextCorrections Corrections) Staged(ProductStore store, CataloguedText declared, Func<ITextStage, bool>? until)
    {
        var corrections = TextCorrections.Read(store, declared.Slug);
        var source = corrections.Apply(declared.Read(store.ResourcesPath, corrections));
        var text = From(source, corrections.Added);
        foreach (var stage in Stages.Where(stage => stage.AppliesTo(declared.Slug)))
        {
            if (until?.Invoke(stage) == true)
            {
                break;
            }

            stage.Apply(text, store.ResourcesPath);
        }

        return (text, corrections);
    }

    /// <summary>
    /// Each verse's length as the frame's rules read it: the characters of its words' surfaces, trailers left out,
    /// as <see cref="CanonicalFrameLoader"/> counts them in the database. One pass over the words product.
    /// </summary>
    public static Dictionary<VerseKey, int> Lengths(ProductStore store, string slug)
    {
        var lengths = new Dictionary<VerseKey, int>();
        using var reader = store.Open(TextWord.ProductOf(slug), TextWord.Schema(slug));
        foreach (var word in reader.Rows())
        {
            lengths[word.Key.Verse] = lengths.GetValueOrDefault(word.Key.Verse) + word.Surface.Length;
        }

        return lengths;
    }

    public static ConvertedText From(TextSource source, IReadOnlySet<WordDraft>? added = null)
    {
        var definition = source.Definition;
        var language = definition.Language;
        var books = new List<TextBook>(source.Books.Count);
        var verses = new List<ConvertedVerse>(32_000);
        var notes = new List<TextNote>();

        foreach (var book in source.Books)
        {
            books.Add(new TextBook(book.CanonicalOrdinal, book.Position, book.Name, book.NameNative, book.Abbreviation, book.Slug));
            foreach (var (at, note) in book.Notes.Index())
            {
                notes.Add(new TextNote($"{book.CanonicalOrdinal}", "book", at + 1, EnumSpelling.Of(note.Kind), note.Content, null, null));
            }

            foreach (var chapter in book.Chapters)
            {
                foreach (var (at, note) in chapter.Notes.Index())
                {
                    notes.Add(new TextNote($"{book.CanonicalOrdinal}.{chapter.Number}", "chapter", at + 1,
                        EnumSpelling.Of(note.Kind), note.Content, null, note.Heading));
                }

                foreach (var (index, verse) in chapter.Verses.Index())
                {
                    var key = new VerseKey(book.CanonicalOrdinal, chapter.Number, verse.Number, verse.Label);
                    var words = new List<ConvertedWord>(verse.Words.Count);
                    var supplied = new SpanRuns();
                    var restored = new SpanRuns();
                    foreach (var draft in verse.Words)
                    {
                        words.Add(new ConvertedWord
                        {
                            Surface = draft.Surface,
                            Trailer = draft.Trailer,
                            Lemma = draft.Lemma,
                            Strong = draft.StrongNumber,
                            Gloss = draft.Gloss,
                            Morphology = JsonbText.Canonical(draft.Morphology),
                            Normalised = WordFolding.Fold(draft.Surface, language),
                            Elided = draft.Elided,
                            Break = draft.Break is { } opening ? EnumSpelling.Of(opening) : null,
                            Supplied = supplied.Next(draft.SuppliedSpan),
                            Restored = restored.Next(draft.RestoredSpan),
                            Added = added?.Contains(draft) == true,
                        });
                    }

                    verses.Add(new ConvertedVerse(key, index + 1, words)
                    {
                        Stated = [.. verse.Stated.Select(s => (s.Chapter, s.Number))],
                        Title = verse.MarksASuperscription || verse.OpensBeforeItsStatedAddress
                            ? TextTitle.Marked
                            : key is { Book: Psalms, ChapterLabel: "", Verse: 1, VerseLabel: "" } && words.Count > 0 && (words[0].Added || words[^1].Added)
                                ? TextTitle.Opening
                                : null,
                    });
                    foreach (var (at, note) in verse.Notes.Index())
                    {
                        notes.Add(new TextNote(key.ToString(), "verse", at + 1, EnumSpelling.Of(note.Kind), note.Content,
                            note.AnchorWordPosition is { } anchor && anchor >= 1 && anchor <= words.Count ? anchor : null, null));
                    }

                    foreach (var (at, stated) in verse.Stated.Index())
                    {
                        notes.Add(new TextNote(key.ToString(), "stated", at + 1, string.Empty,
                            $"{stated.Chapter}{stated.ChapterLabel}:{stated.Number}", null, null));
                    }
                }
            }
        }

        return new ConvertedText { Slug = definition.Slug, Definition = definition, Books = books, Verses = verses, Notes = notes };
    }

    private static void Write(ProductStore store, ConvertedText text, IReadOnlyList<ProductInput> inputs, IReadOnlyDictionary<VerseKey, long?[]> ids)
    {
        var slug = text.Slug;
        var producer = ProductHeader.DescribeProducer($"forge convert {slug}");
        var manifest = new List<VerseManifestRow>(text.Verses.Count);

        using (var writer = store.Create(TextWord.ProductOf(slug), ProductKind.Machine, TextWord.Schema(slug), producer, inputs,
                   header => header.Notes.AddRange(text.Provenance)))
        {
            foreach (var verse in text.Verses)
            {
                using var hasher = new VerseHasher();
                var given = ids[verse.Key];
                foreach (var (at, word) in verse.Words.Index())
                {
                    var key = new WordKey(slug, verse.Key, at + 1);
                    var row = new TextWord(key, given[at], word.Surface, word.Trailer, word.Lemma,
                        word.Strong, word.Gloss, word.Morphology, word.Normalised, word.Graphical, word.Elided, word.Break,
                        word.Supplied, word.Restored)
                    {
                        SuppliedMark = word.SuppliedMark,
                        Doubtful = word.Doubtful,
                        Brackets = word.Brackets,
                        Subscription = word.Subscription,
                    };
                    hasher.Add(row.Hashed);
                    writer.Write(row);
                }

                manifest.Add(new VerseManifestRow(verse.Key, verse.Sequence, verse.Words.Count, hasher.Finish()));
            }

            writer.Complete();
        }

        using (var writer = store.Create(VerseManifestRow.ProductOf(slug), ProductKind.Machine, VerseManifestRow.Schema, producer, inputs))
        {
            writer.WriteAll(manifest);
        }

        using (var writer = store.Create(TextTitle.ProductOf(slug), ProductKind.Machine, TextTitle.Schema, producer, inputs))
        {
            writer.WriteAll(text.Verses.Where(verse => verse.Title is not null).Select(verse => new TextTitle(verse.Key, verse.Title!)));
        }

        using (var writer = store.Create(TextNote.ProductOf(slug), ProductKind.Machine, TextNote.Schema, producer, inputs))
        {
            writer.WriteAll(text.Notes);
        }

        using (var writer = store.Create(TextBook.ProductOf(slug), ProductKind.Machine, TextBook.Schema, producer, inputs))
        {
            writer.WriteAll(text.Books);
        }

        using (var writer = store.Create(TextField.ProductOf(slug), ProductKind.Hand, TextField.Schema, producer, inputs))
        {
            writer.WriteAll(TextDeclaration.Fields(text.Definition, inputs));
        }
    }

    /// <summary>Numbers a verse's spans as the load groups them: a new group wherever a spanned word's number differs from the previous spanned word's.</summary>
    private sealed class SpanRuns
    {
        private int _runs;
        private int? _current;

        public int? Next(int? span)
        {
            if (span is null)
            {
                return null;
            }

            if (span != _current)
            {
                _runs++;
                _current = span;
            }

            return _runs;
        }
    }
}
