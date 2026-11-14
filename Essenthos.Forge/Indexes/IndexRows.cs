using System.Globalization;
using Essenthos.Core.Corpus;
using Essenthos.Core.Products;

namespace Essenthos.Core.Indexes;

/// <summary>The product names of a text's indexes (DOC-0224 §10): <c>texts/&lt;SLUG&gt;/index/&lt;name&gt;</c>.</summary>
internal static class IndexProducts
{
    public const string Strong = "strong";
    public const string Occurrences = "occurrences";
    public const string Forms = "forms";
    public const string Spellings = "spellings";

    /// <summary>What stands behind each spelling of a text in each original: <c>renderings</c> and <c>rendered</c>, made together.</summary>
    public const string Renderings = "renderings";

    public const string Rendered = "rendered";

    /// <summary>The indexes every text has, in the order they are made.</summary>
    public static readonly string[] All = [Strong, Occurrences, Forms, Spellings, Renderings];

    public static string Of(string text, string index) => $"texts/{text}/index/{index}";
}

/// <summary>
/// One word of an edition carrying a Strong number, as a text renders it: the row of <c>texts/&lt;SLUG&gt;/index/strong</c>.
/// Every rendering link between the text and one of the editions it is counted over (<see cref="StrongIndex"/>) that holds
/// the word contributes its words of the text and its id.
/// </summary>
/// <param name="Number">The Strong number the edition's word carries.</param>
/// <param name="Witness">The edition.</param>
/// <param name="Original">The edition's word, its own key.</param>
/// <param name="At">The original's verse at its primary place in the shared frame (<c>texts/&lt;SLUG&gt;/frame</c> of the edition), which its list orders by.</param>
/// <param name="Keys">The text's words linked to it, own keys in the order the phrase prints them; empty where the links hold none.</param>
/// <param name="Phrase">Those words lower-cased and joined by a space; null where there are none.</param>
/// <param name="Links">The rendering links holding the edition's word, ids ascending, space-separated.</param>
/// <param name="Methods">Each link's method as the database spells it, in the order of <paramref name="Links"/>.</param>
internal sealed record StrongIndexRow(string Number, string Witness, string Original, VerseKey At, string Keys, string? Phrase, string Links, string Methods)
{
    public static readonly ProductSchema<StrongIndexRow> Schema = new ProductSchema<StrongIndexRow>()
        .Text("number", r => r.Number)
        .Text("witness", r => r.Witness)
        .Text("original", r => r.Original)
        .VerseKey("at", r => r.At)
        .Text("keys", r => r.Keys)
        .OptionalText("phrase", r => r.Phrase)
        .Text("links", r => r.Links)
        .Text("methods", r => r.Methods)
        .Reads(row => new StrongIndexRow(row.Text(0), row.Text(1), row.Text(2), row.VerseKey(3), row.Text(4), row.TextOrNull(5),
            row.Text(6), row.Text(7)));
}

/// <summary>
/// How many words of an edition carry a Strong number in a book, with the part of speech they are stated as:
/// <c>texts/&lt;SLUG&gt;/index/occurrences</c>, made only for a text of an original language whose numbers may be served.
/// </summary>
/// <param name="Pos">The morphology's <c>pos</c>, null where the word states none.</param>
internal sealed record OccurrenceRow(string Number, int Book, string? Pos, int Words)
{
    public static readonly ProductSchema<OccurrenceRow> Schema = new ProductSchema<OccurrenceRow>()
        .Text("number", r => r.Number)
        .Integer("book", r => r.Book)
        .OptionalText("pos", r => r.Pos)
        .Integer("words", r => r.Words)
        .Reads(row => new OccurrenceRow(row.Text(0), row.Int32(1), row.TextOrNull(2), row.Int32(3)));
}

/// <summary>
/// One word form a text prints, as the search compares it, how often, and its stem: <c>texts/&lt;SLUG&gt;/index/forms</c>.
/// </summary>
/// <param name="Stem">
/// Stemmed here where the corpus stems the language itself, the form itself where nothing stems it, and null where the
/// serving database's own dictionary stems it at load, as the search stems a typed word with the same function.
/// </param>
internal sealed record FormRow(string Form, int Occurrences, string? Stem)
{
    public static readonly ProductSchema<FormRow> Schema = new ProductSchema<FormRow>()
        .Text("form", r => r.Form)
        .Integer("occurrences", r => r.Occurrences)
        .OptionalText("stem", r => r.Stem)
        .Reads(row => new FormRow(row.Text(0), row.Int32(1), row.TextOrNull(2)));
}

/// <summary>
/// One word of a text naming a record, as its spellings are counted from: <c>texts/&lt;SLUG&gt;/index/spellings</c>. The
/// word's letters are the words product's; <paramref name="Renders"/> is read through the finals of the text's pairs.
/// </summary>
/// <param name="Own">The word's own key.</param>
/// <param name="Record">The record it names, by slug.</param>
/// <param name="Renders">The lemma of a word of an original that a link pairs with it and that names the same record; null in an original.</param>
/// <param name="Guess">How sure the annotation is where an aligner's link carried it; null otherwise.</param>
/// <param name="Thing">The record is a thing, a feast or a title.</param>
/// <param name="Firm">The annotation is a ruling, a source's statement or highly sure.</param>
/// <param name="Printed">How many words of the text print the word's normalised form.</param>
internal sealed record SpellingRow(
    WordKey Own,
    string Record,
    string Surface,
    string Trailer,
    string? Lemma,
    string? Normalised,
    string? Renders,
    double? Guess,
    bool Thing,
    bool Firm,
    int Printed)
{
    public static ProductSchema<SpellingRow> Schema(string text) => new ProductSchema<SpellingRow>()
        .OwnWordKey("own", r => r.Own)
        .Text("record", r => r.Record)
        .Text("surface", r => r.Surface)
        .Text("trailer", r => r.Trailer)
        .OptionalText("lemma", r => r.Lemma)
        .OptionalText("normalised", r => r.Normalised)
        .OptionalText("renders", r => r.Renders)
        .OptionalText("guess", r => IndexText.Number(r.Guess))
        .Boolean("thing", r => r.Thing)
        .Boolean("firm", r => r.Firm)
        .Integer("printed", r => r.Printed)
        .Reads(row => new SpellingRow(
            WordKey.Parse(row.Text(0), text), row.Text(1), row.Text(2), row.Text(3), row.TextOrNull(4), row.TextOrNull(5),
            row.TextOrNull(6), IndexText.Number(row.TextOrNull(7)), row.Boolean(8), row.Boolean(9), row.Int32(10)));
}

/// <summary>One name on a word of a text as the spellings index reads it, from the names projection (<c>NamesProjection.IndexNames</c>).</summary>
/// <param name="Guess">The annotation's confidence where an aligner's link carried it; null otherwise.</param>
/// <param name="Thing">The record is a thing, a feast or a title.</param>
/// <param name="Firm">A ruling, a source's statement, or sure enough.</param>
/// <param name="Spellable">
/// The word can spell a name at all: not a word its language never names anybody with, and not a pronoun
/// (<c>Annotating.NeverAName</c>, <c>Pronouns.IsWord</c>).
/// </param>
internal sealed record IndexedName(WordKey Own, string Record, double? Guess, bool Thing, bool Firm, bool Spellable);

internal static class IndexText
{
    public static string? Number(double? value) => value?.ToString("R", CultureInfo.InvariantCulture);

    public static double? Number(string? text) => text is null ? null : double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}

/// <summary>
/// One original standing behind one spelling of a text in one edition: the row of <c>texts/&lt;SLUG&gt;/index/renderings</c>.
/// The original is its Strong number, or its lemma where its word carries none. Every renders or equals link between a word
/// of the text printing the spelling and a word of the edition carrying the original counts.
/// </summary>
/// <param name="Form">The spelling, as <c>normalised</c> holds it.</param>
/// <param name="Witness">The edition.</param>
/// <param name="Number">The Strong number the edition's words carry, their own or, where they state none, the least beside them.</param>
/// <param name="Lemma">Where there is no number, the lemma that names the original; beside a number, the lemma of the word at its first place.</param>
/// <param name="Occurrences">The words of the text printing the spelling that it stands behind.</param>
/// <param name="Links">The links those are counted from.</param>
/// <param name="Methods">Each method and how many of those links it made, <c>method count</c> pairs by method.</param>
/// <param name="Shared">
/// The links of these that hold another spelling of the text one request may gather with this one, <c>id method</c> pairs by
/// id: a count over several spellings counts such a link once. Null where there are none.
/// </param>
/// <param name="Places">
/// Every place it stands at in the shared frame, in order, each once: <see cref="RenderingPlaces"/> writes them, with the word
/// printed there.
/// </param>
internal sealed record RenderingRow(string Form, string Witness, string? Number, string? Lemma, int Occurrences, int Links, string Methods, string? Shared, string Places)
{
    public static readonly ProductSchema<RenderingRow> Schema = new ProductSchema<RenderingRow>()
        .Text("form", r => r.Form)
        .Text("witness", r => r.Witness)
        .OptionalText("number", r => r.Number)
        .OptionalText("lemma", r => r.Lemma)
        .Integer("occurrences", r => r.Occurrences)
        .Integer("links", r => r.Links)
        .Text("methods", r => r.Methods)
        .OptionalText("shared", r => r.Shared)
        .Text("places", r => r.Places)
        .Reads(row => new RenderingRow(row.Text(0), row.Text(1), row.TextOrNull(2), row.TextOrNull(3), row.Int32(4), row.Int32(5), row.Text(6),
            row.TextOrNull(7), row.Text(8)));
}

/// <summary>
/// One spelling of a text as one edition is counted against it: the row of <c>texts/&lt;SLUG&gt;/index/rendered</c>, written
/// for every edition the text is linked to whose books print the spelling or that a link of it reaches.
/// </summary>
/// <param name="Rank">The edition's place among the originals the text is linked to (<see cref="Corpus.LinkedOriginals.Ordered"/>), from 0.</param>
/// <param name="Occurrences">The words printing the spelling in the books the edition holds.</param>
/// <param name="Reached">How many of those words some link joins to a word of the edition.</param>
/// <param name="Links">The links those are counted from.</param>
/// <param name="Methods">As <see cref="RenderingRow.Methods"/>.</param>
/// <param name="Shared">As <see cref="RenderingRow.Shared"/>.</param>
/// <param name="Sources">Who states those links: the numbers the product's <c>source</c> notes give them, ascending.</param>
internal sealed record RenderedRow(string Form, string Witness, int Rank, int Occurrences, int Reached, int Links, string Methods, string? Shared, string Sources)
{
    public static readonly ProductSchema<RenderedRow> Schema = new ProductSchema<RenderedRow>()
        .Text("form", r => r.Form)
        .Text("witness", r => r.Witness)
        .Integer("rank", r => r.Rank)
        .Integer("occurrences", r => r.Occurrences)
        .Integer("reached", r => r.Reached)
        .Integer("links", r => r.Links)
        .Text("methods", r => r.Methods)
        .OptionalText("shared", r => r.Shared)
        .Text("sources", r => r.Sources)
        .Reads(row => new RenderedRow(row.Text(0), row.Text(1), row.Int32(2), row.Int32(3), row.Int32(4), row.Int32(5), row.Text(6),
            row.TextOrNull(7), row.Text(8)));
}
