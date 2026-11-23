using System.Diagnostics;
using System.Globalization;
using System.Text;
using Essenthos.Core.BetaMasaheft;
using Essenthos.Core.Commentary;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Lexicons;
using Essenthos.Core.Loading;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using Essenthos.Core.StepBible;
using Essenthos.Core.Strong;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Build;

/// <summary>
/// Served tables made whole from files, as <c>tables/&lt;table&gt;</c>: every column as Postgres would print it, in the layout
/// <see cref="CarriedTables"/> loads (a text by its slug, a word by its id), so a table written here replaces its carried
/// copy with nothing else changed. Each is made by the same reading the load step used, from the same sources, and again
/// only when one of them moved.
/// </summary>
internal static partial class TableProducts
{
    public const string Producer = "tables";

    /// <summary>The relations between texts people state (<c>texts/relations</c>, hand TSV): what <c>text_relation</c> holds.</summary>
    public const string Relations = "texts/relations";

    public sealed record Made(string Table, long Rows, bool Current, TimeSpan Elapsed, string? Note = null);

    /// <summary>A served table, the sources it is read from (paths under Resources/) and how its rows are made.</summary>
    /// <param name="Products">Other products it reads, by name.</param>
    /// <param name="More">Inputs read through another producer's reading of its sources, such as a text read again whole.</param>
    /// <param name="Version">
    /// Raised when what the reading takes from unchanged sources changes: the code is not an input, so a table whose
    /// product carries another <c>table-version</c> note is made again.
    /// </param>
    private sealed record Table(
        string Name,
        string[] Columns,
        string[] Sources,
        string[] Products,
        Func<ProductStore, Rows> Make,
        Func<ProductStore, IEnumerable<ProductInput>>? More = null,
        int Version = 1);

    private sealed record Rows(IEnumerable<string?[]> Lines, string? Note = null);

    public static string Product(string table) => $"tables/{table}";

    private static readonly Table[] All =
    [
        new("text_relation", ["id", "from_text", "to_text", "relation", "scope", "note", "source"], [], [Relations], TextRelationRows),
        new("strong_entry",
            ["id", "strong_number", "lemma", "transliteration", "pronunciation", "definition", "derivation", "kjv_definition", "morphology",
             "detailed_definition", "see_also", "source_language", "twot_reference"],
            [StrongHebrew, StrongGreek], [Prefixes], StrongEntryRows),
        new("strong_entry_translation",
            ["id", "strong_number", "language", "definition", "derivation", "kjv_definition", "detailed_definition", "method", "confidence", "source", "note"],
            [StrongHebrew, StrongGreek, "Essenthos/lexicon"], [Prefixes], StrongTranslationRows),
        new("strong_relation", ["id", "from_number", "to_number", "kind", "hedged", "position", "statement", "source"],
            [StrongHebrew, StrongGreek, CompiledHebrew], [], StrongRelationRows),
        new("strong_profile",
            ["strong_number", "language", "part_of_speech", "gender", "occurrences", "first_book", "first_chapter", "first_verse", "source"],
            [CompiledHebrew, BriefGreek], [], StrongProfileRows),
        new("lexicon_gloss", ["id", "entry", "strong_number", "lemma", "gloss", "source"], [BriefGreek], [], GlossRows),
        new("geez_lexicon_entry", ["id", "entry", "headword", "forms", "consonants", "latin", "greek", "source"], ["Dillmann"], [], GeezRows,
            Version: 2),
        new("geez_lexicon_rendering", ["entry", "ordinal", "sense", "en", "uk", "de", "es", "model", "run", "made", "source"], ["Dillmann"], [],
            GeezRenderingRows, store => GeezLayerInputs(store, GeezModelLayers.Renderings)),
        new("geez_word_gloss",
            ["word_id", "lemma", "lemma_source", "sense_en", "sense_uk", "line_en", "line_uk", "greek_word_ids", "morphology", "confidence", "model",
             "run", "made", "source"],
            [], [], GeezGlossRows, store => GeezLayerInputs(store, GeezModelLayers.GlossFolder)),
        new("commentary_entry",
            ["id", "commentary", "source", "book", "first_chapter", "first_verse", "last_chapter", "last_verse", "sequence", "body"],
            [.. CommentaryWorks.All.Select(w => $"{CommentaryReader.Folder}/{w.Module}")], [.. ProductText.Products(LopukhinReader.Synodal)],
            CommentaryRows),
        new("lexicon_article", ["id", "dictionary", "strong_number", "ordinal", "entry", "headword", "status", "body", "source"],
            [BdbReader.Lexicon, BdbReader.Index, ThayerReader.Book, AbbottSmithReader.Lexicon, LsjReader.Lexicon], [], LexiconArticleRows),
    ];

    private const string StrongHebrew = "Strong/StrongHebrew.xml";
    private const string StrongGreek = "Strong/StrongGreek.xml";
    private const string CompiledHebrew = $"BibleData2026/{CompiledHebrewStrongs.File}";
    private const string BriefGreek = "STEPBibleLexicons/TBESG.txt";

    /// <summary>The tables made here, by name.</summary>
    public static IReadOnlyList<string> Tables => [.. All.Concat(Words).Concat(Counterparts).Select(t => t.Name)];

    /// <summary>Writes every table asked for (all by default) whose sources moved since it was written, or every one with <paramref name="force"/>.</summary>
    public static List<Made> Write(ProductStore store, IReadOnlySet<string>? only, bool force)
    {
        SeedRelations(store);
        var made = new List<Made>();
        foreach (var table in All.Concat(Words).Concat(Counterparts).Where(t => only is null || only.Contains(t.Name)))
        {
            var clock = Stopwatch.StartNew();
            IReadOnlyList<ProductInput> inputs =
            [
                .. store.Inputs([
                    .. table.Sources,
                    .. table.Products.Select(p => store.Find(p) ?? throw new FileNotFoundException($"{p} is not under {store.ResourcesPath}.")),
                ]),
                .. table.More?.Invoke(store) ?? [],
            ];
            inputs = [.. inputs.DistinctBy(input => input.Path)];
            if (!force && !store.NeedsBuild(Product(table.Name), ProductKind.Machine, inputs) && SameColumns(store, table) && SameVersion(store, table))
            {
                made.Add(new Made(table.Name, 0, true, clock.Elapsed));
                continue;
            }

            var schema = new ProductSchema<string?[]>();
            for (var i = 0; i < table.Columns.Length; i++)
            {
                var at = i;
                schema = schema.OptionalText(table.Columns[i], row => row[at]);
            }

            schema = schema.Reads(row => row.ToTexts());
            var rows = table.Make(store);
            long count = 0;
            using (var writer = store.Create(Product(table.Name), ProductKind.Machine, schema, ProductHeader.DescribeProducer($"forge {Producer}"), inputs,
                       header =>
                       {
                           header.Notes.Add(new("table", table.Name));
                           if (table.Version > 1)
                           {
                               header.Notes.Add(new(VersionNote, table.Version.ToString(CultureInfo.InvariantCulture)));
                           }
                       }))
            {
                foreach (var line in rows.Lines)
                {
                    writer.Write(line);
                    count++;
                }

                writer.Complete();
            }

            made.Add(new Made(table.Name, count, false, clock.Elapsed, rows.Note));
        }

        return made;
    }

    /// <summary>
    /// Whether the product holds the columns the code writes: the code is not an input, and the load reads the product by its
    /// columns, so a table whose columns changed is made again rather than loaded in the old layout.
    /// </summary>
    private static bool SameColumns(ProductStore store, Table table)
    {
        using var file = Products.Essx.EssxReader.Open(store.FullPath(store.Find(Product(table.Name))!));
        return file.Tables[0].Columns.Select(c => c.Name).SequenceEqual(table.Columns);
    }

    private const string VersionNote = "table-version";

    /// <summary>Whether the product was made by the reading of this version: a version 1 table carries no note.</summary>
    private static bool SameVersion(ProductStore store, Table table)
    {
        using var file = Products.Essx.EssxReader.Open(store.FullPath(store.Find(Product(table.Name))!));
        var note = file.Header.Notes.FirstOrDefault(n => n.Key == VersionNote).Value;
        return (note ?? "1") == table.Version.ToString(CultureInfo.InvariantCulture);
    }

    private static string? Int(long? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? Float(double? value) => value?.ToString("R", CultureInfo.InvariantCulture);

    private static string Bool(bool value) => value ? "true" : "false";

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>A text array as Postgres prints it: an element quoted only where it is empty, NULL, or holds a space or one of <c>{}",\</c>.</summary>
    private static string Array(IEnumerable<string> values)
    {
        var text = new StringBuilder("{");
        foreach (var value in values)
        {
            if (text.Length > 1)
            {
                text.Append(',');
            }

            var quoted = value.Length == 0 || value.Equals("NULL", StringComparison.OrdinalIgnoreCase)
                                           || value.Any(c => char.IsWhiteSpace(c) || c is '{' or '}' or '"' or ',' or '\\');
            text.Append(quoted
                ? '"' + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + '"'
                : value);
        }

        return text.Append('}').ToString();
    }

    private static IEnumerable<string?[]> Numbered(IEnumerable<string?[]> rows) =>
        rows.Select((row, index) => (string?[])[Int(index + 1), .. row]);

    private static List<StrongParsedEntry> StrongEntries(ProductStore store)
    {
        var parser = new StrongXmlParser(StrongReferences.Read(store));
        return
        [
            .. parser.ParseHebrew(File.ReadAllText(store.FullPath(StrongHebrew))),
            .. parser.ParseGreek(File.ReadAllText(store.FullPath(StrongGreek))),
        ];
    }

    /// <summary>One entry a number, the first where a file repeats one, as the load wrote them.</summary>
    private static List<StrongParsedEntry> StrongByNumber(ProductStore store) =>
        [.. StrongEntries(store).GroupBy(entry => entry.StrongNumber).Select(number => number.First())];

    /// <summary>Strong's entries, then the prefixes' after them, so that no entry of the dictionary changes its row.</summary>
    private static Rows StrongEntryRows(ProductStore store)
    {
        var entries = StrongByNumber(store);
        var prefixes = PrefixRows(store, entries.Select(e => e.StrongNumber).ToHashSet(StringComparer.Ordinal));
        return new(Numbered(entries.Select(e => new[]
        {
            e.StrongNumber, Optional(e.Lemma), Optional(e.Transliteration), Optional(e.Pronunciation), Optional(e.Definition), Optional(e.Derivation),
            Optional(e.KjvDefinition), Optional(e.Morphology), Optional(e.DetailedDefinition), Optional(e.SeeAlso), Optional(e.SourceLanguage),
            Optional(e.TwotReference),
        }).Concat(PrefixEntryLines(prefixes))), $"{prefixes.Count(p => p.IsEntry)} prefix entries");
    }

    private static Rows StrongTranslationRows(ProductStore store)
    {
        var folder = store.FullPath(StrongTranslationFiles.DefaultFolder.Replace('\\', '/'));
        if (!Directory.Exists(folder))
        {
            return new Rows([], "no translated lexicon files");
        }

        var strongs = StrongByNumber(store);
        var prefixes = PrefixRows(store, strongs.Select(e => e.StrongNumber).ToHashSet(StringComparer.Ordinal));
        var english = strongs.ToDictionary(e => e.StrongNumber, e => new StrongEntry
        {
            StrongNumber = e.StrongNumber,
            Definition = Optional(e.Definition),
            Derivation = Optional(e.Derivation),
            KjvDefinition = Optional(e.KjvDefinition),
            DetailedDefinition = Optional(e.DetailedDefinition),
        }, StringComparer.Ordinal);
        var (records, files, replaced) = StrongTranslationFiles.Read(folder);
        var rows = StrongTranslationLoader.Accepted(records, english, out var refused);
        return new Rows(Numbered(rows.Select(t => new[]
            {
                t.StrongNumber, t.Language, t.Definition, t.Derivation, t.KjvDefinition, t.DetailedDefinition, EnumSpelling.Of(t.Method),
                Float(t.Confidence), t.Source, t.Note,
            }).Concat(PrefixTranslationLines(prefixes))),
            $"{records.Count} records over {files} files, {replaced} superseded; refused: {refused}; {prefixes.Count(p => !p.IsEntry)} prefix translations");
    }

    private static Rows StrongRelationRows(ProductStore store)
    {
        var stated = StrongRelationReading.Of(StrongEntries(store))
            .Select(r => (Relation: r, Source: r.FromNumber.StartsWith(StrongNumbers.Greek) ? StrongRelationLoader.GreekSource : StrongRelationLoader.HebrewSource))
            .Concat(CompiledHebrewStrongs.Read(store.FullPath(CompiledHebrew)).Roots.Select(r => (Relation: r, Source: CompiledHebrewStrongs.Source)));
        return new(Numbered(stated.Select(s => new[]
        {
            s.Relation.FromNumber, s.Relation.ToNumber, s.Relation.Kind, Bool(s.Relation.Hedged), Int(s.Relation.Position), s.Relation.Statement, s.Source,
        })));
    }

    private static Rows StrongProfileRows(ProductStore store) =>
        new(CompiledHebrewStrongs.Read(store.FullPath(CompiledHebrew)).Profiles
            .Concat(BriefGreekParts.Read(store.FullPath(BriefGreek), Sources.BriefGreekLexicon))
            .OrderBy(p => p.StrongNumber, StringComparer.Ordinal).ThenBy(p => p.Language, StringComparer.Ordinal)
            .Select(p => new[]
            {
                p.StrongNumber, p.Language, p.PartOfSpeech, p.Gender, Int(p.Occurrences), Int(p.FirstBook), Int(p.FirstChapter), Int(p.FirstVerse), p.Source,
            }));

    private static Rows GlossRows(ProductStore store) =>
        new(Numbered(BriefGreekLexicon.Read(store.FullPath(BriefGreek))
            .SelectMany(entry => entry.Lemmas.Distinct(StringComparer.Ordinal).Select(lemma => new[]
            {
                entry.Entry, entry.StrongNumber, UnicodeNormalization.Composed(lemma), entry.Gloss, Sources.BriefGreekLexicon,
            }))));

    private static Rows GeezRows(ProductStore store) =>
        new(Numbered(DillmannLexicon.Headwords(DillmannLexicon.Read(store.FullPath("Dillmann")).ToList()).Select(headword => new[]
        {
            headword.Entry, headword.Headword, Array(headword.Forms), Array(headword.Forms.Select(GeezLexicon.ConsonantsOf).Distinct()),
            Array(headword.Latin), Array(headword.Greek), Sources.DillmannLexicon,
        })));

    /// <summary>
    /// What a table of the model's Ge'ez layers reads, where the store holds the layer: the layer itself, and for the glosses
    /// the words they stand on and the Greek they name. A store without the layer makes the table empty.
    /// </summary>
    private static IEnumerable<ProductInput> GeezLayerInputs(ProductStore store, string layer)
    {
        if (layer == GeezModelLayers.GlossFolder)
        {
            var folder = $"{ProductStore.EssenthosFolder}/{layer}";
            return !Directory.Exists(store.FullPath(folder))
                ? []
                :
                [
                    store.Input(folder),
                    .. new[] { TextWord.ProductOf(GeezGlossRow.Text) }.Concat(GeezGreekTexts.Select(TextWord.ProductOf)).Append(WordIdCollision.Product)
                        .Select(store.Find).OfType<string>().Select(store.Input),
                ];
        }

        return store.Find(layer) is { } found ? [store.Input(found)] : [];
    }

    /// <summary>The Greek a Ge'ez gloss may name: Brenton's Septuagint beside the Old Testament, Nestle beside the New.</summary>
    private static readonly string[] GeezGreekTexts = ["GRCBRENT", "NESTLE1904"];

    private static string Date(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Dillmann's senses as the model rendered them, every entry one the lexicon loads under the same headword, or none.</summary>
    private static Rows GeezRenderingRows(ProductStore store)
    {
        if (store.Find(GeezModelLayers.Renderings) is null)
        {
            return new Rows([], "no rendering of Dillmann in the store");
        }

        using var reader = store.Open(GeezModelLayers.Renderings, DillmannRenderingRow.Schema);
        var rows = reader.ReadAll();
        var loaded = DillmannLexicon.Headwords(DillmannLexicon.Read(store.FullPath("Dillmann")).ToList())
            .ToDictionary(h => h.Entry, h => h.Headword, StringComparer.Ordinal);
        var astray = rows.Where(r => loaded.GetValueOrDefault(r.Entry) != r.Headword).Select(r => $"{r.Entry} {r.Headword}").Distinct().ToList();
        if (astray.Count > 0)
        {
            throw new InvalidDataException(
                $"{GeezModelLayers.Renderings}: {astray.Count} entr(ies) the lexicon no longer loads under that headword, e.g. "
                + string.Join(", ", astray.Take(10)) + ". Render them again or take the run in again with forge geez-layers.");
        }

        return new Rows(rows.Select(r => new[]
            {
                r.Entry, Int(r.Ordinal), r.Sense, r.En, r.Uk, r.De, r.Es, r.Model, r.Run, Date(r.Made),
                $"{Sources.DillmannRenderingPrefix}: {r.Model}, run {r.Run}, {Date(r.Made)}; CC BY-NC-SA 4.0",
            }),
            $"{rows.Select(r => r.Entry).Distinct().Count()} entries");
    }

    /// <summary>
    /// The Ge'ez words as the model read them in context, each on the word its key names, by the id that key gives it. Every
    /// word must be one the text holds, printed as the run saw it, and every Greek word one its text holds; otherwise nothing.
    /// </summary>
    private static Rows GeezGlossRows(ProductStore store)
    {
        var folder = store.FullPath($"{ProductStore.EssenthosFolder}/{GeezModelLayers.GlossFolder}");
        var books = !Directory.Exists(folder)
            ? []
            : Directory.EnumerateFiles(folder).Select(file => $"{GeezModelLayers.GlossFolder}/{Path.GetFileNameWithoutExtension(file)}")
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (books.Count == 0)
        {
            return new Rows([], "no Ge'ez glosses in the store");
        }

        var rows = new List<GeezGlossRow>();
        foreach (var book in books)
        {
            using var reader = store.Open(book, GeezGlossRow.Schema);
            rows.AddRange(reader.Rows());
        }

        var printed = new Dictionary<string, Dictionary<WordKey, string>>(StringComparer.Ordinal);
        Dictionary<WordKey, string> Words(string text)
        {
            if (!printed.TryGetValue(text, out var words))
            {
                words = WordsOf(store, text).ToDictionary(w => w.Key, w => w.Surface);
                printed[text] = words;
            }

            return words;
        }

        var astray = rows.Where(r => Words(GeezGlossRow.Text).GetValueOrDefault(r.Word) != r.Surface).Select(r => r.Word.ToString())
            .Concat(rows.SelectMany(r => r.Greek.Where(key => !Words(r.GreekText).ContainsKey(key)).Select(key => $"{key} (named by {r.Word})")))
            .ToList();
        if (astray.Count > 0 || rows.Select(r => r.Word).Distinct().Count() != rows.Count)
        {
            throw new InvalidDataException(
                $"{GeezModelLayers.GlossFolder}: {astray.Count} word(s) the texts no longer hold as the run read them, e.g. "
                + string.Join(", ", astray.Take(10)) + (astray.Count == 0 ? "; a word glossed twice" : string.Empty)
                + ". Take the run in again with forge geez-layers, which says which.");
        }

        var ids = Ids(store);
        return new Rows(rows.OrderBy(r => r.Word).Select(r => new[]
            {
                Int(ids.Of(r.Word)), r.Lemma, r.LemmaSource, r.SenseEn, r.SenseUk, r.LineEn, r.LineUk,
                "{" + string.Join(',', r.Greek.Select(key => ids.Of(key).ToString(CultureInfo.InvariantCulture))) + "}",
                r.Morphology, r.Confidence, r.Model, r.Run, Date(r.Made),
                $"{Sources.GeezGlossPrefix}: {r.Model}, run {r.Run}, {Date(r.Made)}; CC BY-NC-SA 4.0",
            }),
            $"{rows.Count} words over {books.Count} book(s); {rows.Count(r => r.Greek.Count == 0)} without Greek");
    }

    /// <summary>Every comment of every commentary, each commentary in its module's order.</summary>
    private static Rows CommentaryRows(ProductStore store)
    {
        var unread = new Dictionary<string, int>(StringComparer.Ordinal);
        var rows = CommentaryWorks.All.SelectMany(work => work.Kind == CommentaryWorks.Format.FictionBook
            ? LopukhinReader.Read(store, work, unread)
            : CommentaryReader.Read(store.ResourcesPath, work, unread)).ToList();
        return new(Numbered(rows.Select(r => new[]
            {
                r.Commentary, r.Source, Int(r.Book), Int(r.FirstChapter), Int(r.FirstVerse), Int(r.LastChapter), Int(r.LastVerse), Int(r.Sequence), r.Body,
            })),
            string.Join("; ", CommentaryWorks.All.Select(w => $"{w.Id} {rows.Count(r => r.Commentary == w.Id)}"))
            + (unread.Count == 0 ? string.Empty : "; markup not read: " + string.Join(", ", unread.Select(u => $"{u.Key} {u.Value}"))));
    }

    /// <summary>Every article of every lexicon beside Strong's, lexicon by lexicon, each under its numbers in the lexicon's order.</summary>
    private static Rows LexiconArticleRows(ProductStore store)
    {
        var unread = new Dictionary<string, int>(StringComparer.Ordinal);
        var thayer = new ThayerReader.Left();
        var rows = BdbReader.Read(store.ResourcesPath, unread)
            .Concat(ThayerReader.Read(store.ResourcesPath, thayer))
            .Concat(AbbottSmithReader.Read(store.ResourcesPath, unread))
            .Concat(LsjReader.Read(store.ResourcesPath, unread))
            .ToList();
        return new(Numbered(rows.Select(r => new[]
            {
                r.Dictionary, r.StrongNumber, Int(r.Ordinal), r.Entry, r.Headword, r.Status, r.Body, r.Source,
            })),
            string.Join("; ", LexiconWorks.All.Select(w =>
                $"{w.Id} {rows.Count(r => r.Dictionary == w.Id)} articles under {rows.Where(r => r.Dictionary == w.Id).Select(r => r.StrongNumber).Distinct().Count()} numbers"))
            + $"; thayer left out {thayer.Cards.Count} entries that are cards of Strong-style fields, {thayer.Misfiled.Count} filed under another word's number and {thayer.Extended.Count} the module numbers between Strong's numbers"
            + (unread.Count == 0 ? string.Empty : "; markup not read: " + string.Join(", ", unread.Select(u => $"{u.Key} {u.Value}"))));
    }

    private static Rows TextRelationRows(ProductStore store)
    {
        using var reader = store.Open(Relations, RelationRow.Schema);
        var entries = TextRelationLoader.Validate([.. reader.Rows().Select(r => new TextRelationEntry(r.From, r.To, r.Relation, r.Scope, r.Note, r.Source))]);
        return new(Numbered(entries.Select(e => new[] { e.From, e.To, e.Relation, e.Scope, e.Note, e.Source })));
    }

    /// <summary>Writes <c>texts/relations</c> once from the list the Forge carries, where it is missing; people edit it afterwards.</summary>
    public static void SeedRelations(ProductStore store)
    {
        if (store.Exists(Relations))
        {
            return;
        }

        using var writer = store.Create(Relations, ProductKind.Hand, RelationRow.Schema, ProductHeader.ByHand, [],
            header => header.Comments.Add("Which text was translated from, or is an edition of, which; scope = canonical book ranges (1-39,67-79)."));
        writer.WriteAll(TextRelationLoader.All.Select(e => new RelationRow(e.From, e.To, e.Relation, e.Scope, e.Note, e.Source)));
    }

    internal sealed record RelationRow(string From, string To, string Relation, string? Scope, string? Note, string Source)
    {
        public static readonly ProductSchema<RelationRow> Schema = new ProductSchema<RelationRow>()
            .Text("from", r => r.From)
            .Text("to", r => r.To)
            .Text("relation", r => r.Relation)
            .OptionalText("scope", r => r.Scope)
            .OptionalText("note", r => r.Note)
            .Text("source", r => r.Source)
            .Reads(row => new RelationRow(row.Text(0), row.Text(1), row.Text(2), row.TextOrNull(3), row.TextOrNull(4), row.Text(5)));
    }
}
