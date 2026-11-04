using System.Diagnostics;
using System.Globalization;
using System.Text;
using Essenthos.Core.BetaMasaheft;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading;
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
    private sealed record Table(
        string Name,
        string[] Columns,
        string[] Sources,
        string[] Products,
        Func<ProductStore, Rows> Make,
        Func<ProductStore, IEnumerable<ProductInput>>? More = null);

    private sealed record Rows(IEnumerable<string?[]> Lines, string? Note = null);

    public static string Product(string table) => $"tables/{table}";

    private static readonly Table[] All =
    [
        new("text_relation", ["id", "from_text", "to_text", "relation", "scope", "note", "source"], [], [Relations], TextRelationRows),
        new("strong_entry",
            ["id", "strong_number", "lemma", "transliteration", "pronunciation", "definition", "derivation", "kjv_definition", "morphology",
             "detailed_definition", "see_also", "source_language", "twot_reference"],
            [StrongHebrew, StrongGreek], [], StrongEntryRows),
        new("strong_entry_translation",
            ["id", "strong_number", "language", "definition", "derivation", "kjv_definition", "detailed_definition", "method", "confidence", "source", "note"],
            [StrongHebrew, StrongGreek, "Essenthos/lexicon"], [], StrongTranslationRows),
        new("strong_relation", ["id", "from_number", "to_number", "kind", "hedged", "position", "statement", "source"],
            [StrongHebrew, StrongGreek, CompiledHebrew], [], StrongRelationRows),
        new("strong_profile",
            ["strong_number", "language", "part_of_speech", "gender", "occurrences", "first_book", "first_chapter", "first_verse", "source"],
            [CompiledHebrew, BriefGreek], [], StrongProfileRows),
        new("lexicon_gloss", ["id", "entry", "strong_number", "lemma", "gloss", "source"], [BriefGreek], [], GlossRows),
        new("geez_lexicon_entry", ["id", "entry", "headword", "forms", "consonants", "latin", "greek", "source"], ["Dillmann"], [], GeezRows),
    ];

    private const string StrongHebrew = "Strong/StrongHebrew.xml";
    private const string StrongGreek = "Strong/StrongGreek.xml";
    private const string CompiledHebrew = $"BibleData2026/{CompiledHebrewStrongs.File}";
    private const string BriefGreek = "STEPBibleLexicons/TBESG.txt";

    /// <summary>The tables made here, by name.</summary>
    public static IReadOnlyList<string> Tables => [.. All.Concat(Words).Select(t => t.Name)];

    /// <summary>Writes every table asked for (all by default) whose sources moved since it was written, or every one with <paramref name="force"/>.</summary>
    public static List<Made> Write(ProductStore store, IReadOnlySet<string>? only, bool force)
    {
        SeedRelations(store);
        var made = new List<Made>();
        foreach (var table in All.Concat(Words).Where(t => only is null || only.Contains(t.Name)))
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
            if (!force && !store.NeedsBuild(Product(table.Name), ProductKind.Machine, inputs))
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
                       header => header.Notes.Add(new("table", table.Name))))
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
        var parser = new StrongXmlParser();
        return
        [
            .. parser.ParseHebrew(File.ReadAllText(store.FullPath(StrongHebrew))),
            .. parser.ParseGreek(File.ReadAllText(store.FullPath(StrongGreek))),
        ];
    }

    /// <summary>One entry a number, the first where a file repeats one, as the load wrote them.</summary>
    private static List<StrongParsedEntry> StrongByNumber(ProductStore store) =>
        [.. StrongEntries(store).GroupBy(entry => entry.StrongNumber).Select(number => number.First())];

    private static Rows StrongEntryRows(ProductStore store) =>
        new(Numbered(StrongByNumber(store).Select(e => new[]
        {
            e.StrongNumber, Optional(e.Lemma), Optional(e.Transliteration), Optional(e.Pronunciation), Optional(e.Definition), Optional(e.Derivation),
            Optional(e.KjvDefinition), Optional(e.Morphology), Optional(e.DetailedDefinition), Optional(e.SeeAlso), Optional(e.SourceLanguage),
            Optional(e.TwotReference),
        })));

    private static Rows StrongTranslationRows(ProductStore store)
    {
        var folder = store.FullPath(StrongTranslationFiles.DefaultFolder.Replace('\\', '/'));
        if (!Directory.Exists(folder))
        {
            return new Rows([], "no translated lexicon files");
        }

        var english = StrongByNumber(store).ToDictionary(e => e.StrongNumber, e => new StrongEntry
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
            })),
            $"{records.Count} records over {files} files, {replaced} superseded; refused: {refused}");
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
