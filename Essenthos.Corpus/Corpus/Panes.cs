using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Corpus;

/// <summary>One pane as it is stored: its chapter, the gzip-compressed body, the body's hash, and its size.</summary>
internal sealed record PaneBody(int Book, int Chapter, byte[] Gzip, string Hash, int Words, int Bytes);

/// <summary>
/// One text's side of the parallel view for one canonical chapter, read from the served tables the way the reader's
/// <c>/v1/parallel</c> reads it (<see cref="ChapterReading"/>), and written once as compact JSON (<see cref="Format"/>).
///
/// <para>
/// The body, format 1:
/// <code>
/// { "format": 1, "text": "KJV", "book": 19, "chapter": 119, "chapters": 150,
///   "records": [["david", "person"], …],            every record a word names, once; cards point into it
///   "provenance": ["stated-by-source", "aligner:0.26", …], what established words' links; words point into it
///   "sources": ["…"],                                  who said a word names a record; cards point into it
///   "bookNotes": { "title": "…", "notes": [note] },    on the chapter the book's title notes stand with
///   "headings": [{ "verse": 1, "heading": "…", "notes": [note] }],
///   "verses": [{ "n": 1, "own": ["119:1"], "ref": [19, 119, 1], "stated": ["118:1"], "notes": [note], "words": [word] }] }
/// note: { "kind", "content", "anchorWordId", "anchor" }
/// word: { "id", "s" surface, "t" trailer, "g" gloss, "l" lemma, "n" Strong number, "w" witness word ids,
///         "p" provenance index, "a" absence, "m" mark, "e" [card] the records the word names, "eb" [card] the base
///         text's records where the translation names another (origin base-reading), "mo" morphology, "ph" phono,
///         "pt" phono trailer, "lg" language, "el" 1 when elided, "br" break, "sc" { "n", "m", "c" } Strong candidate,
///         "lx" { "g", "via" } lexicon gloss, "tg" { "corpus", "word", "lemma", "glosses", "method", "confidence" },
///         "gz" { "headword", "latin", "greek", "via" }, "at" { "kind", "headId", "head" }, "ro" [{ "occupation",
///         "category", "person" }] }
/// card: { "r" record index, "m" method, "c" confidence, "src" source index, "via" route, "as" named as, "role", "origin" }
/// </code>
/// A field that is null, false, empty or (for the trailer) the empty string is left out. A verse is there when the text
/// has words or notes at it, as a cell of <c>/v1/parallel</c> is. <c>supplied</c> is <c>m = "supplied"</c>.
/// </para>
///
/// <para>
/// Every id is a word id, below 2^53 (the corpus holds under 40 million), so they are JSON numbers; a link id, which
/// can exceed 2^53, is never written. Names are not here: a record is its slug, and the reader takes its name from the
/// record-names dictionary of its language.
/// </para>
/// </summary>
internal static class Panes
{
    public const int Format = 1;

    /// <summary>The largest integer a JSON number carries exactly in a browser.</summary>
    private const long SafeInteger = (1L << 53) - 1;

    private static readonly JsonWriterOptions Options = new()
    {
        // Every letter as itself rather than escaped: a Hebrew or Greek pane would be several times longer. The body is only
        // ever read as JSON, never placed in HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    /// <summary>
    /// The canonical chapters a text has a pane for: every chapter it has a verse in, and each chapter holding a passage
    /// of one of them under another name (<see cref="TwinPassages"/>), where the pane reads it under that name.
    /// </summary>
    public static IReadOnlyList<(int Book, int Chapter)> Chapters(IEnumerable<(int Book, int Chapter)> held)
    {
        var chapters = new SortedSet<(int Book, int Chapter)>();
        foreach (var (book, chapter) in held)
        {
            chapters.Add((book, chapter));
            foreach (var twin in TwinPassages.NamedElsewhere(book, chapter))
            {
                chapters.Add((twin.Book, twin.Chapter));
            }
        }

        return [.. chapters];
    }

    /// <summary>
    /// The pane of one text for one canonical chapter, or null where the text has nothing there: no words, no notes,
    /// no heading and no title notes.
    /// </summary>
    public static async Task<PaneBody?> Read(
        AppDbContext db,
        int textId,
        string slug,
        int book,
        int chapter,
        int chapters,
        CancellationToken cancellationToken)
    {
        var words = new Dictionary<int, List<TextWordResponse>>();
        var references = new Dictionary<int, VerseRefResponse>();
        var own = new Dictionary<int, List<string>>();
        var stated = new Dictionary<int, List<string>>();
        var notes = new Dictionary<int, List<SourceNoteResponse>>();
        var headings = new Dictionary<int, List<ChapterNotesResponse>>();
        foreach (var (heldBook, heldChapter, first, last, shift) in ChapterReading.Held(book, chapter))
        {
            ChapterReading.Merge(words, await ChapterReading.ReadByCanonicalVerse(db, textId, heldBook, heldChapter, cancellationToken), first, last, shift);
            ChapterReading.Merge(references, await ChapterReading.OwnReferences(db, textId, heldBook, heldChapter, cancellationToken), first, last, shift);
            ChapterReading.Merge(own, await ChapterReading.OwnVerses(db, textId, heldBook, heldChapter, cancellationToken), first, last, shift);
            ChapterReading.Merge(stated, await ChapterReading.StatedVerses(db, textId, heldBook, heldChapter, cancellationToken), first, last, shift);
            ChapterReading.Merge(notes, await ChapterReading.SourceNotes(db, textId, heldBook, heldChapter, cancellationToken), first, last, shift);
            ChapterReading.Merge(headings, await ChapterReading.ChapterNotesByCanonicalVerse(db, textId, heldBook, heldChapter, cancellationToken), first, last, shift);
        }

        var bookNotes = (await ChapterReading.BookNotes(db, [textId], book, chapter, cancellationToken)).GetValueOrDefault(textId);
        var numbers = words.Keys.Concat(notes.Keys).Distinct().Order().ToList();
        if (numbers.Count == 0 && headings.Count == 0 && bookNotes is null)
        {
            return null;
        }

        var ids = words.Values.SelectMany(verse => verse.Select(word => word.Id)).ToList();
        var named = await Named(db, ids, cancellationToken);
        var json = Write(new PaneContent(slug, book, chapter, chapters, numbers, words, references, own, stated, notes, headings, bookNotes, named));
        var (gzip, hash) = Pack(json);
        return new PaneBody(book, chapter, gzip, hash, ids.Count, json.Length);
    }

    /// <summary>A record a word names as the row says it, with the two columns the reader's card does not carry yet.</summary>
    internal sealed record NamedRow(long WordId, string Slug, string Type, string Method, double? Confidence, string Source, string? Note, string? Role, string? Origin);

    /// <summary>
    /// Every row naming a record on these words, the base text's reading beside a word that names another included
    /// (the query filter hides it from every other reader): what gives a card its role and origin, and the second card.
    /// </summary>
    private static async Task<ILookup<long, NamedRow>> Named(AppDbContext db, List<long> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<NamedRow>().ToLookup(row => row.WordId);
        }

        var rows = await db.WordEntities.IgnoreQueryFilters().AsNoTracking()
            .Where(a => ids.Contains(a.WordId))
            .Select(a => new { a.WordId, a.Entity!.Slug, a.Entity.Kind, a.Method, a.Confidence, a.Source, a.Note, a.Role, a.Origin })
            .ToListAsync(cancellationToken);
        return rows
            .Select(a => new NamedRow(a.WordId, a.Slug, EnumSpelling.Of(a.Kind), EnumSpelling.Of(a.Method), a.Confidence, a.Source, a.Note, a.Role, a.Origin))
            .OrderBy(row => row.Slug, StringComparer.Ordinal).ThenBy(row => row.Method, StringComparer.Ordinal).ThenBy(row => row.Source, StringComparer.Ordinal)
            .ToLookup(row => row.WordId);
    }

    /// <summary>What one pane holds before it is written.</summary>
    internal sealed record PaneContent(
        string Text,
        int Book,
        int Chapter,
        int Chapters,
        IReadOnlyList<int> Numbers,
        IReadOnlyDictionary<int, List<TextWordResponse>> Words,
        IReadOnlyDictionary<int, VerseRefResponse> References,
        IReadOnlyDictionary<int, List<string>> Own,
        IReadOnlyDictionary<int, List<string>> Stated,
        IReadOnlyDictionary<int, List<SourceNoteResponse>> Notes,
        IReadOnlyDictionary<int, List<ChapterNotesResponse>> Headings,
        BookNotesResponse? BookNotes,
        ILookup<long, NamedRow> Named);

    /// <summary>A string table: each distinct value once, in the order first met.</summary>
    private sealed class Table
    {
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

        public List<string> Values { get; } = [];

        public int this[string value]
        {
            get
            {
                if (!_index.TryGetValue(value, out var index))
                {
                    index = Values.Count;
                    _index[value] = index;
                    Values.Add(value);
                }

                return index;
            }
        }
    }

    /// <summary>The pane as UTF-8 JSON, format <see cref="Format"/>.</summary>
    internal static byte[] Write(PaneContent pane)
    {
        // The string tables are filled while the verses are written, and written before them: the verses go to a
        // buffer of their own first.
        var records = new Table();
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        var provenance = new Table();
        var sources = new Table();

        var verses = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(verses, Options))
        {
            json.WriteStartArray();
            foreach (var number in pane.Numbers)
            {
                json.WriteStartObject();
                json.WriteNumber("n", number);
                Strings(json, "own", pane.Own.GetValueOrDefault(number));
                if (pane.References.GetValueOrDefault(number) is { } reference)
                {
                    json.WriteStartArray("ref");
                    json.WriteNumberValue(reference.BookOrdinal);
                    json.WriteNumberValue(reference.Chapter);
                    json.WriteNumberValue(reference.Verse);
                    json.WriteEndArray();
                }

                Strings(json, "stated", pane.Stated.GetValueOrDefault(number));
                if (pane.Notes.GetValueOrDefault(number) is { Count: > 0 } notes)
                {
                    json.WritePropertyName("notes");
                    Notes(json, notes);
                }

                json.WriteStartArray("words");
                foreach (var word in pane.Words.GetValueOrDefault(number) ?? [])
                {
                    Word(json, word, pane.Named[word.Id], records, types, provenance, sources);
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        var body = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(body, Options))
        {
            json.WriteStartObject();
            json.WriteNumber("format", Format);
            json.WriteString("text", pane.Text);
            json.WriteNumber("book", pane.Book);
            json.WriteNumber("chapter", pane.Chapter);
            json.WriteNumber("chapters", pane.Chapters);
            json.WriteStartArray("records");
            foreach (var slug in records.Values)
            {
                json.WriteStartArray();
                json.WriteStringValue(slug);
                json.WriteStringValue(types[slug]);
                json.WriteEndArray();
            }

            json.WriteEndArray();
            json.WriteStartArray("provenance");
            provenance.Values.ForEach(json.WriteStringValue);
            json.WriteEndArray();
            json.WriteStartArray("sources");
            sources.Values.ForEach(json.WriteStringValue);
            json.WriteEndArray();
            if (pane.BookNotes is { } book)
            {
                json.WriteStartObject("bookNotes");
                Optional(json, "title", book.Title);
                json.WritePropertyName("notes");
                Notes(json, book.Notes);
                json.WriteEndObject();
            }

            if (pane.Headings.Count > 0)
            {
                json.WriteStartArray("headings");
                foreach (var (verse, headings) in pane.Headings.OrderBy(row => row.Key))
                {
                    foreach (var heading in headings)
                    {
                        json.WriteStartObject();
                        json.WriteNumber("verse", verse);
                        json.WriteString("heading", heading.Heading);
                        json.WritePropertyName("notes");
                        Notes(json, heading.Notes);
                        json.WriteEndObject();
                    }
                }

                json.WriteEndArray();
            }

            json.WritePropertyName("verses");
            json.WriteRawValue(verses.WrittenSpan, skipInputValidation: true);
            json.WriteEndObject();
        }

        return body.WrittenSpan.ToArray();
    }

    private static void Word(
        Utf8JsonWriter json,
        TextWordResponse word,
        IEnumerable<NamedRow> named,
        Table records,
        Dictionary<string, string> types,
        Table provenance,
        Table sources)
    {
        json.WriteStartObject();
        json.WriteNumber("id", Id(word.Id));
        json.WriteString("s", word.Text);
        if (word.Trailer.Length > 0)
        {
            json.WriteString("t", word.Trailer);
        }

        Optional(json, "g", word.Gloss);
        Optional(json, "l", word.Lexeme);
        Optional(json, "n", word.StrongNo);
        if (word.OriginalWordIds.Length > 0)
        {
            // A set the reader intersects; in order, so the same corpus writes the same body.
            json.WriteStartArray("w");
            foreach (var id in word.OriginalWordIds.Order())
            {
                json.WriteNumberValue(Id(id));
            }

            json.WriteEndArray();
        }

        if (word.MappingProvenance is { } established)
        {
            json.WriteNumber("p", provenance[established]);
        }

        Optional(json, "a", word.Absence);
        Optional(json, "m", word.Mark);
        var rows = named.ToList();
        if (word.Entities.Count > 0)
        {
            json.WriteStartArray("e");
            foreach (var entity in word.Entities)
            {
                types[entity.Slug] = entity.Type;
                var row = rows.FirstOrDefault(r => r.Slug == entity.Slug && r.Origin != WordEntityOrigins.BaseReading
                                                   && r.Method == entity.Method && r.Source == entity.Source)
                          ?? rows.FirstOrDefault(r => r.Slug == entity.Slug && r.Origin != WordEntityOrigins.BaseReading);
                Card(json, records[entity.Slug], entity.Method, entity.Confidence, entity.Source, entity.Note, entity.NamedAs, row?.Role, row?.Origin, sources);
            }

            json.WriteEndArray();
        }

        var baseReadings = rows.Where(r => r.Origin == WordEntityOrigins.BaseReading).ToList();
        if (baseReadings.Count > 0)
        {
            json.WriteStartArray("eb");
            foreach (var row in baseReadings)
            {
                types[row.Slug] = row.Type;
                Card(json, records[row.Slug], row.Method, row.Confidence, row.Source, Annotations.Route(row.Note), null, row.Role, row.Origin, sources);
            }

            json.WriteEndArray();
        }

        if (word.Morphology is { } morphology)
        {
            json.WriteStartObject("mo");
            Optional(json, "pos", morphology.PartOfSpeech);
            Optional(json, "case", morphology.Case);
            Optional(json, "gender", morphology.Gender);
            Optional(json, "number", morphology.Number);
            Optional(json, "person", morphology.Person);
            Optional(json, "state", morphology.State);
            Optional(json, "stem", morphology.Stem);
            Optional(json, "tense", morphology.Tense);
            Optional(json, "lexicalSet", morphology.LexicalSet);
            Optional(json, "phrasePos", morphology.PhraseDependentPartOfSpeech);
            Optional(json, "suffixGender", morphology.PronominalGender);
            Optional(json, "suffixNumber", morphology.PronominalNumber);
            Optional(json, "suffixPerson", morphology.PronominalPerson);
            Strings(json, "nameType", morphology.Nametypes);
            json.WriteEndObject();
        }

        Optional(json, "ph", word.Phono);
        Optional(json, "pt", word.PhonoTrailer);
        Optional(json, "lg", word.Language);
        if (word.Elided)
        {
            json.WriteNumber("el", 1);
        }

        Optional(json, "br", word.Break);
        if (word.StrongCandidate is { } candidate)
        {
            json.WriteStartObject("sc");
            json.WriteString("n", candidate.Number);
            json.WriteString("m", candidate.Method);
            Optional(json, "c", candidate.Confidence);
            json.WriteEndObject();
        }

        if (word.LexiconGloss is { } lexicon)
        {
            json.WriteStartObject("lx");
            Strings(json, "g", lexicon.Glosses, always: true);
            json.WriteString("via", lexicon.Via);
            json.WriteEndObject();
        }

        if (word.ThroughGreek is { } greek)
        {
            json.WriteStartObject("tg");
            json.WriteString("corpus", greek.Corpus);
            json.WriteString("word", greek.Word);
            Optional(json, "lemma", greek.Lemma);
            Strings(json, "glosses", greek.Glosses, always: true);
            json.WriteString("method", greek.Method);
            Optional(json, "confidence", greek.Confidence);
            json.WriteEndObject();
        }

        if (word.GeezEntry is { } geez)
        {
            json.WriteStartObject("gz");
            json.WriteString("headword", geez.Headword);
            Strings(json, "latin", geez.Latin, always: true);
            Strings(json, "greek", geez.Greek, always: true);
            json.WriteString("via", geez.Via);
            json.WriteEndObject();
        }

        if (word.Attached is { } attached)
        {
            json.WriteStartObject("at");
            json.WriteString("kind", attached.Kind);
            json.WriteNumber("headId", Id(attached.HeadId));
            json.WriteString("head", attached.Head);
            json.WriteEndObject();
        }

        if (word.Roles is { Count: > 0 } roles)
        {
            json.WriteStartArray("ro");
            foreach (var role in roles)
            {
                json.WriteStartObject();
                json.WriteString("occupation", role.Occupation);
                json.WriteString("category", role.Category);
                json.WriteString("person", role.Person);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        json.WriteEndObject();
    }

    private static void Card(
        Utf8JsonWriter json,
        int record,
        string? method,
        double? confidence,
        string? source,
        string? route,
        string? namedAs,
        string? role,
        string? origin,
        Table sources)
    {
        json.WriteStartObject();
        json.WriteNumber("r", record);
        Optional(json, "m", method);
        Optional(json, "c", confidence);
        if (source is not null)
        {
            json.WriteNumber("src", sources[source]);
        }

        Optional(json, "via", route);
        Optional(json, "as", namedAs);
        Optional(json, "role", role);
        Optional(json, "origin", origin);
        json.WriteEndObject();
    }

    private static void Notes(Utf8JsonWriter json, IEnumerable<SourceNoteResponse> notes)
    {
        json.WriteStartArray();
        foreach (var note in notes)
        {
            json.WriteStartObject();
            json.WriteString("kind", note.Kind);
            json.WriteString("content", note.Content);
            if (note.AnchorWordId is { } anchor)
            {
                json.WriteNumber("anchorWordId", Id(anchor));
            }

            Optional(json, "anchor", note.Anchor);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static long Id(long id) =>
        id is >= 0 and <= SafeInteger ? id : throw new InvalidOperationException($"Word id {id} does not fit a JSON number exactly; a pane writes word ids as numbers.");

    private static void Optional(Utf8JsonWriter json, string name, string? value)
    {
        if (value is not null)
        {
            json.WriteString(name, value);
        }
    }

    private static void Optional(Utf8JsonWriter json, string name, double? value)
    {
        if (value is { } number)
        {
            json.WriteNumber(name, number);
        }
    }

    private static void Strings(Utf8JsonWriter json, string name, IEnumerable<string>? values, bool always = false)
    {
        var list = values?.ToList();
        if (list is not { Count: > 0 } && !always)
        {
            return;
        }

        json.WriteStartArray(name);
        foreach (var value in list ?? [])
        {
            json.WriteStringValue(value);
        }

        json.WriteEndArray();
    }

    /// <summary>The body gzip-compressed (as a browser takes it with <c>Content-Encoding: gzip</c>) and its hash.</summary>
    public static (byte[] Gzip, string Hash) Pack(byte[] json)
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(json);
        }

        return (compressed.ToArray(), Convert.ToHexStringLower(SHA256.HashData(json))[..16]);
    }

    /// <summary>A stored body uncompressed, for a client that does not take gzip.</summary>
    public static byte[] Unpack(byte[] gzip)
    {
        using var input = new GZipStream(new MemoryStream(gzip), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }
}
