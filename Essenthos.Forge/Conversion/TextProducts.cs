using Essenthos.Core.Products;

namespace Essenthos.Core.Conversion;

/// <summary>
/// One word of a converted text, as <c>texts/&lt;SLUG&gt;/words</c> holds it: the key, the id the database
/// gives it (kept from the corpus it was first exported from, null for a word that is new), and every
/// column of the word row.
/// </summary>
/// <param name="Supplied">Which supplied span of its verse the word stands in, counting runs from 1; null outside one.</param>
/// <param name="Restored">The same for the spans an editor restored from other manuscripts.</param>
/// <remarks>
/// The edition's other marks: <see cref="SuppliedMark"/> is the bracket the Berean prints a supplied span in;
/// <see cref="Doubtful"/> and <see cref="Subscription"/> number, through the whole text, the spans an edition
/// brackets as doubtful (with <see cref="Brackets"/>, how many brackets) or as a subscription.
/// </remarks>
internal sealed record TextWord(
    WordKey Key,
    long? Id,
    string Surface,
    string Trailer,
    string? Lemma,
    string? Strong,
    string? Gloss,
    string? Morphology,
    string? Normalised,
    string? Graphical,
    bool Elided,
    string? Break,
    int? Supplied,
    int? Restored)
{
    public string? SuppliedMark { get; init; }

    public int? Doubtful { get; init; }

    public int? Brackets { get; init; }

    public int? Subscription { get; init; }

    public static string ProductOf(string text) => $"texts/{text}/words";

    public static ProductSchema<TextWord> Schema(string text) => new ProductSchema<TextWord>()
        .OwnWordKey("own", w => w.Key)
        .OptionalInteger("id", w => w.Id)
        .Text("surface", w => w.Surface)
        .Text("trailer", w => w.Trailer)
        .OptionalText("lemma", w => w.Lemma)
        .OptionalText("strong", w => w.Strong)
        .OptionalText("gloss", w => w.Gloss)
        .OptionalJson("morphology", w => w.Morphology)
        .OptionalText("normalised", w => w.Normalised)
        .OptionalText("graphical", w => w.Graphical)
        .Boolean("elided", w => w.Elided)
        .OptionalText("break", w => w.Break)
        .OptionalInteger("supplied", w => w.Supplied)
        .OptionalInteger("restored", w => w.Restored)
        .OptionalText("supplied_mark", w => w.SuppliedMark)
        .OptionalInteger("doubtful", w => w.Doubtful)
        .OptionalInteger("brackets", w => w.Brackets)
        .OptionalInteger("subscription", w => w.Subscription)
        .Reads(row => new TextWord(
            WordKey.Parse(row.Text(0), text),
            row.IntegerOrNull(1),
            row.Text(2),
            row.Text(3),
            row.TextOrNull(4),
            row.TextOrNull(5),
            row.TextOrNull(6),
            row.JsonOrNull(7),
            row.TextOrNull(8),
            row.TextOrNull(9),
            row.Boolean(10),
            row.TextOrNull(11),
            row.Int32OrNull(12),
            row.Int32OrNull(13))
        {
            SuppliedMark = row.TextOrNull(14),
            Doubtful = row.Int32OrNull(15),
            Brackets = row.Int32OrNull(16),
            Subscription = row.Int32OrNull(17),
        });

    public VerseWord Hashed => new(Surface, Trailer, Lemma, Strong, Morphology, Gloss);
}

/// <summary>
/// The id a word of the corpus holds, by key, with the word as the corpus reads it: what <c>forge texts ids</c> exports
/// from a corpus, so that every later conversion gives each word the id it already has, and a word a correction moved
/// within its verse is still found by its letters (<see cref="TextWordIds"/>).
/// </summary>
internal sealed record TextWordId(WordKey Key, long Id, string Surface)
{
    public static string ProductOf(string text) => $"texts/{text}/ids";

    public static ProductSchema<TextWordId> Schema(string text) => new ProductSchema<TextWordId>()
        .OwnWordKey("own", w => w.Key)
        .Integer("id", w => w.Id)
        .Text("surface", w => w.Surface)
        .Reads(row => new TextWordId(WordKey.Parse(row.Text(0), text), row.Integer(1), row.Text(2)));
}

/// <summary>
/// What an edition prints beside its words, one row each: a note on a verse, a chapter heading or a book's
/// title, and a verse number the edition states in its own numbering.
/// </summary>
/// <param name="At">Where: <c>19</c> for a book, <c>19.9</c> for a chapter, <c>19.9:1</c> for a verse.</param>
/// <param name="Scope"><c>book</c>, <c>chapter</c>, <c>verse</c> or <c>stated</c>.</param>
/// <param name="Position">Its order among the notes of the same place, from 1.</param>
/// <param name="Kind">footnote or cross-reference; for a stated number, empty.</param>
/// <param name="Content">The note; for a stated number, <c>chapter[label]:verse</c> as the edition prints it.</param>
/// <param name="Anchor">For a verse note, the position of the word its marker follows, where the source says.</param>
/// <param name="Heading">For a chapter note, the heading it is printed on.</param>
internal sealed record TextNote(string At, string Scope, int Position, string Kind, string Content, int? Anchor, string? Heading)
{
    public static string ProductOf(string text) => $"texts/{text}/notes";

    public static readonly ProductSchema<TextNote> Schema = new ProductSchema<TextNote>()
        .Text("at", n => n.At)
        .Text("scope", n => n.Scope)
        .Integer("position", n => n.Position)
        .Text("kind", n => n.Kind)
        .Text("content", n => n.Content)
        .OptionalInteger("anchor", n => n.Anchor)
        .OptionalText("heading", n => n.Heading)
        .Reads(row => new TextNote(row.Text(0), row.Text(1), row.Int32(2), row.Text(3), row.Text(4), row.Int32OrNull(5), row.TextOrNull(6)));
}

/// <summary>A book of a converted text: its canonical ordinal, its place in the edition and its names.</summary>
internal sealed record TextBook(int Book, int Position, string Name, string? NameNative, string? Abbreviation, string Slug)
{
    public static string ProductOf(string text) => $"texts/{text}/books";

    public static readonly ProductSchema<TextBook> Schema = new ProductSchema<TextBook>()
        .Integer("book", b => b.Book)
        .Integer("position", b => b.Position)
        .Text("name", b => b.Name)
        .OptionalText("native", b => b.NameNative)
        .OptionalText("abbreviation", b => b.Abbreviation)
        .Text("slug", b => b.Slug)
        .Reads(row => new TextBook(row.Int32(0), row.Int32(1), row.Text(2), row.TextOrNull(3), row.TextOrNull(4), row.Text(5)));
}

/// <summary>One field of a text's declaration: <c>texts/&lt;SLUG&gt;/text</c> holds one row per field, so a diff names the field.</summary>
internal sealed record TextField(string Field, string? Value)
{
    public static string ProductOf(string text) => $"texts/{text}/text";

    public static readonly ProductSchema<TextField> Schema = new ProductSchema<TextField>()
        .Text("field", f => f.Field)
        .OptionalText("value", f => f.Value)
        .Reads(row => new TextField(row.Text(0), row.TextOrNull(1)));
}
