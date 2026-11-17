using System.Globalization;
using System.Text.Json;
using Essenthos.Core.Conversion;
using Essenthos.Core.Products;

namespace Essenthos.Core.BetaMasaheft;

/// <summary>One Ge'ez word as a model read it in its verse (<c>geez/glosses/&lt;book&gt;</c>), by the word's key.</summary>
/// <param name="Greek">The words of <paramref name="GreekText"/> it renders, by their keys, in the order the model gave them.</param>
internal sealed record GeezGlossRow(
    WordKey Word,
    string Surface,
    string Lemma,
    string LemmaSource,
    string SenseEn,
    string SenseUk,
    string LineEn,
    string LineUk,
    string GreekText,
    IReadOnlyList<WordKey> Greek,
    string? Morphology,
    string Confidence,
    string Model,
    string Run,
    string PromptVersion,
    DateOnly Made)
{
    public const string Text = "GEEZ81";

    public static readonly ProductSchema<GeezGlossRow> Schema = new ProductSchema<GeezGlossRow>()
        .OwnWordKey("own", r => r.Word)
        .Text("surface", r => r.Surface)
        .Text("lemma", r => r.Lemma)
        .Text("lemma_source", r => r.LemmaSource)
        .Text("sense_en", r => r.SenseEn)
        .Text("sense_uk", r => r.SenseUk)
        .Text("line_en", r => r.LineEn)
        .Text("line_uk", r => r.LineUk)
        .Text("greek_text", r => r.GreekText)
        .Text("greek", r => string.Join(' ', r.Greek.Select(key => key.Own)))
        .OptionalText("morphology", r => r.Morphology)
        .Text("confidence", r => r.Confidence)
        .Text("model", r => r.Model)
        .Text("run", r => r.Run)
        .Text("prompt_version", r => r.PromptVersion)
        .Text("made", r => r.Made.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        .Reads(row => new GeezGlossRow(
            WordKey.Parse(row.Text(0), Text), row.Text(1), row.Text(2), row.Text(3), row.Text(4), row.Text(5), row.Text(6), row.Text(7),
            row.Text(8),
            [.. row.Text(9).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(own => WordKey.Parse(own, row.Text(8)))],
            row.TextOrNull(10), row.Text(11), row.Text(12), row.Text(13), row.Text(14),
            DateOnly.ParseExact(row.Text(15), "yyyy-MM-dd", CultureInfo.InvariantCulture)));
}

/// <summary>One sense of an entry of Dillmann's lexicon as a model rendered his Latin (<c>geez/dillmann-renderings</c>).</summary>
/// <param name="Ordinal">The sense's place in the entry, from 1, in his order.</param>
/// <param name="Sense">His number for it, <c>main</c> for the sense an entry opens with.</param>
internal sealed record DillmannRenderingRow(
    string Entry,
    string Headword,
    int Ordinal,
    string Sense,
    string? En,
    string? Uk,
    string? De,
    string? Es,
    string Model,
    string Run,
    string PromptVersion,
    DateOnly Made)
{
    public static readonly ProductSchema<DillmannRenderingRow> Schema = new ProductSchema<DillmannRenderingRow>()
        .Text("entry", r => r.Entry)
        .Text("headword", r => r.Headword)
        .Integer("ordinal", r => r.Ordinal)
        .Text("sense", r => r.Sense)
        .OptionalText("en", r => r.En)
        .OptionalText("uk", r => r.Uk)
        .OptionalText("de", r => r.De)
        .OptionalText("es", r => r.Es)
        .Text("model", r => r.Model)
        .Text("run", r => r.Run)
        .Text("prompt_version", r => r.PromptVersion)
        .Text("made", r => r.Made.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        .Reads(row => new DillmannRenderingRow(
            row.Text(0), row.Text(1), row.Int32(2), row.Text(3), row.TextOrNull(4), row.TextOrNull(5), row.TextOrNull(6), row.TextOrNull(7),
            row.Text(8), row.Text(9), row.Text(10), DateOnly.ParseExact(row.Text(11), "yyyy-MM-dd", CultureInfo.InvariantCulture)));
}

/// <summary>
/// The two layers a language model made over the Ge'ez (FTR-0824, FTR-1141), taken from a finished run under
/// <c>Resources/Essenthos/generation/</c> into the store, where the build reads them: Dillmann's senses rendered from his
/// Latin into the reader's languages (<c>geez/dillmann-renderings</c>), and each word of GEEZ81 read in its verse beside
/// the Greek (<c>geez/glosses/&lt;book&gt;</c>, one file per book).
///
/// <para>
/// Only the run's checked lines are taken (<c>clean.jsonl</c>; a request its checks refused has none). A word is named by
/// its key, found from the id the run was asked with: the run's packets say where each id stood, and the key is taken only
/// when its id is that key's id today and the store's words print the same letters there, so an id that has come to name
/// another word, or a word the text no longer holds, stops the whole import and nothing is written. The same holds for every
/// Greek word a line names. An entry of Dillmann's must be one the lexicon loads, under the same headword.
/// </para>
///
/// <para>
/// The files are <c>kept</c>: no build makes them again, the store and its backup are their only copy once the run is
/// archived, and the build stops if one the manifest records goes missing. Their header says which run, model, prompt and
/// day they came from. Raw answers stay with the run.
/// </para>
/// </summary>
internal static class GeezModelLayers
{
    public const string Producer = "forge geez-layers";

    public const string Renderings = "geez/dillmann-renderings";

    public const string GlossFolder = "geez/glosses";

    public static string Glosses(int book) => $"{GlossFolder}/{book:00}";

    /// <summary>What an import found: the lines taken, and every line it could not place, which stops it.</summary>
    public sealed record Outcome(string Layer, string Run, int Lines, int Written, IReadOnlyList<string> Unplaced, IReadOnlyDictionary<string, int> Counts)
    {
        public bool Failed => Unplaced.Count > 0;

        public override string ToString() =>
            Failed
                ? $"{Layer} from {Run}: {Unplaced.Count} of {Lines} line(s) could not be placed, nothing written:\n  "
                  + string.Join("\n  ", Unplaced.Take(40)) + (Unplaced.Count > 40 ? $"\n  ... and {Unplaced.Count - 40} more" : string.Empty)
                : $"{Layer} from {Run}: {Lines} line(s), {Written} row(s) written; "
                  + string.Join(", ", Counts.Select(c => $"{c.Key} {c.Value}"));
    }

    /// <summary>The run's settings and day, as its manifest and its submissions record them.</summary>
    private sealed record RunFacts(string Run, string Model, string PromptVersion, DateOnly Made, string CleanSha256);

    /// <summary>Takes the contextual glosses of a run into one file per book. Writes nothing if any line cannot be placed.</summary>
    public static Outcome ImportGlosses(ProductStore store, string runFolder)
    {
        var facts = Facts(runFolder);
        var lines = ReadJsonLines(Path.Combine(runFolder, "clean.jsonl"));
        var refused = ReadJsonLines(Path.Combine(runFolder, "rejects.jsonl")).Select(r => r.GetProperty("custom_id").GetString()!).ToHashSet(StringComparer.Ordinal);

        // Where each id the run was asked with stood when the packets were made, Ge'ez and Greek alike.
        var asked = new Dictionary<long, (string Text, WordKey Key, string Surface)>();
        foreach (var chapter in ReadJsonLines(Path.Combine(runFolder, "inputs", "gloss-chapters.jsonl")))
        {
            var book = chapter.GetProperty("ordinal").GetInt32();
            Place(chapter, "geez", GeezGlossRow.Text);
            if (chapter.TryGetProperty("greek_text", out var greekText) && greekText.GetString() is { } greekSlug)
            {
                Place(chapter, "greek", greekSlug);
            }

            void Place(JsonElement packet, string property, string text)
            {
                if (!packet.TryGetProperty(property, out var words) || words.ValueKind != JsonValueKind.Array)
                {
                    return;
                }

                foreach (var word in words.EnumerateArray())
                {
                    var key = new WordKey(text, new VerseKey(book, word.GetProperty("c").GetInt32(), word.GetProperty("v").GetInt32(),
                        word.GetProperty("label").GetString() ?? string.Empty), word.GetProperty("p").GetInt32());
                    asked[long.Parse(word.GetProperty("id").GetString()!, CultureInfo.InvariantCulture)] = (text, key, word.GetProperty("t").GetString()!);
                }
            }
        }

        var ids = WordIdTable.For(store);
        var printed = new Dictionary<string, Dictionary<WordKey, string>>(StringComparer.Ordinal);
        Dictionary<WordKey, string> WordsOf(string text)
        {
            if (!printed.TryGetValue(text, out var words))
            {
                using var reader = store.Open(TextWord.ProductOf(text), TextWord.Schema(text));
                words = reader.Rows().ToDictionary(w => w.Key, w => w.Surface);
                printed[text] = words;
            }

            return words;
        }

        var unplaced = new List<string>();
        var seen = new HashSet<WordKey>();
        var rows = new List<GeezGlossRow>(lines.Count);
        foreach (var line in lines)
        {
            var request = line.GetProperty("custom_id").GetString()!;
            var id = long.Parse(line.GetProperty("word_id").GetString()!, CultureInfo.InvariantCulture);
            if (refused.Contains(request))
            {
                unplaced.Add($"{request} word {id}: its request was refused, yet it has a checked line");
                continue;
            }

            if (Resolve(id, GeezGlossRow.Text, request) is not { } word)
            {
                continue;
            }

            if (!seen.Add(word))
            {
                unplaced.Add($"{request} {word}: a second line for the same word");
                continue;
            }

            var greekText = line.GetProperty("greek_text").GetString()!;
            var greek = new List<WordKey>();
            var whole = true;
            foreach (var greekId in line.GetProperty("greek_word_ids").EnumerateArray())
            {
                if (Resolve(long.Parse(greekId.GetString()!, CultureInfo.InvariantCulture), greekText, request) is { } key)
                {
                    greek.Add(key);
                }
                else
                {
                    whole = false;
                }
            }

            if (!whole)
            {
                continue;
            }

            rows.Add(new GeezGlossRow(
                word, WordsOf(GeezGlossRow.Text)[word], Text(line, "lemma"), Text(line, "lemma_source"), Text(line, "sense_en"), Text(line, "sense_uk"),
                Text(line, "en"), Text(line, "uk"), greekText, greek, Optional(line.GetProperty("morph").GetString()), Text(line, "conf"),
                Text(line, "model"), Text(line, "run"), Text(line, "prompt_version"), facts.Made));
            if (rows[^1].Model != facts.Model || rows[^1].Run != facts.Run)
            {
                unplaced.Add($"{request} {word}: made by {rows[^1].Model} in {rows[^1].Run}, not the run's {facts.Model} in {facts.Run}");
            }
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (unplaced.Count == 0)
        {
            foreach (var book in rows.GroupBy(r => r.Word.Verse.Book).OrderBy(b => b.Key))
            {
                using var writer = store.Create(Glosses(book.Key), ProductKind.Machine, GeezGlossRow.Schema, ProductHeader.DescribeProducer(Producer), [],
                    header => Annotate(header, facts));
                writer.WriteAll(book.OrderBy(r => r.Word));
                counts[$"book {book.Key}"] = book.Count();
            }

            foreach (var confidence in rows.GroupBy(r => r.Confidence).OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                counts[confidence.Key] = confidence.Count();
            }

            counts["without Greek"] = rows.Count(r => r.Greek.Count == 0);
            store.Save();
        }

        return new Outcome("contextual glosses", facts.Run, lines.Count, unplaced.Count == 0 ? rows.Count : 0, unplaced, counts);

        WordKey? Resolve(long id, string text, string request)
        {
            if (!asked.TryGetValue(id, out var where) || where.Text != text)
            {
                unplaced.Add($"{request} word {id}: not a {text} word of the run's packets");
                return null;
            }

            if (ids.Of(where.Key) != id)
            {
                unplaced.Add($"{request} {where.Key}: its id is {ids.Of(where.Key)} today, not the run's {id}");
                return null;
            }

            if (!WordsOf(text).TryGetValue(where.Key, out var surface))
            {
                unplaced.Add($"{request} {where.Key}: the text holds no such word any more");
                return null;
            }

            if (surface != where.Surface)
            {
                unplaced.Add($"{request} {where.Key}: the text prints {surface} there, the run was asked about {where.Surface}");
                return null;
            }

            return where.Key;
        }
    }

    /// <summary>Takes Dillmann's senses as a run rendered them. Writes nothing if any entry is not one the lexicon loads.</summary>
    public static Outcome ImportRenderings(ProductStore store, string runFolder, IReadOnlyList<DillmannHeadword> lexicon)
    {
        var facts = Facts(runFolder);
        var lines = ReadJsonLines(Path.Combine(runFolder, "clean.jsonl"));
        var headwords = lexicon.ToDictionary(h => h.Entry, h => h.Headword, StringComparer.Ordinal);
        var unplaced = new List<string>();
        var rows = new List<DillmannRenderingRow>();
        var entries = new HashSet<string>(StringComparer.Ordinal);
        var silent = 0;
        foreach (var line in lines)
        {
            var entry = Text(line, "entry");
            var headword = Text(line, "headword");
            if (!headwords.TryGetValue(entry, out var loaded))
            {
                unplaced.Add($"{entry} {headword}: not an entry the lexicon loads");
                continue;
            }

            if (loaded != headword)
            {
                unplaced.Add($"{entry}: the lexicon's headword is {loaded}, the run rendered {headword}");
                continue;
            }

            if (!entries.Add(entry))
            {
                unplaced.Add($"{entry} {headword}: a second line for the same entry");
                continue;
            }

            if (Text(line, "model") != facts.Model || Text(line, "run") != facts.Run)
            {
                unplaced.Add($"{entry}: made by {Text(line, "model")} in {Text(line, "run")}, not the run's {facts.Model} in {facts.Run}");
            }

            var ordinal = 0;
            foreach (var sense in line.GetProperty("senses").EnumerateArray())
            {
                ordinal++;
                string? Of(string language) => sense.TryGetProperty(language, out var value) ? Optional(value.GetString()) : null;
                var (en, uk, de, es) = (Of("en"), Of("uk"), Of("de"), Of("es"));
                if (en is null && uk is null && de is null && es is null)
                {
                    // A sense whose "glosses" are the digitisation's abbreviations comes back empty, as asked.
                    silent++;
                    continue;
                }

                rows.Add(new DillmannRenderingRow(entry, headword, ordinal, Text(sense, "n"), en, uk, de, es, facts.Model, facts.Run, facts.PromptVersion,
                    facts.Made));
            }
        }

        if (unplaced.Count == 0)
        {
            using var writer = store.Create(Renderings, ProductKind.Machine, DillmannRenderingRow.Schema, ProductHeader.DescribeProducer(Producer), [],
                header => Annotate(header, facts));
            writer.WriteAll(rows.OrderBy(r => r.Entry, StringComparer.Ordinal).ThenBy(r => r.Ordinal));
            store.Save();
        }

        return new Outcome("Dillmann's renderings", facts.Run, lines.Count, unplaced.Count == 0 ? rows.Count : 0, unplaced,
            new Dictionary<string, int>
            {
                ["entries"] = entries.Count,
                ["senses"] = rows.Count,
                ["senses rendered empty"] = silent,
                ["without uk"] = rows.Count(r => r.Uk is null),
            });
    }

    private static void Annotate(ProductHeader header, RunFacts facts)
    {
        header.Notes.Add(new("run", facts.Run));
        header.Notes.Add(new("model", facts.Model));
        header.Notes.Add(new("prompt", facts.PromptVersion));
        header.Notes.Add(new("made", facts.Made.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        header.Notes.Add(new("clean", facts.CleanSha256));
        header.Comments.Add("A language model's work, checked by its run's rules and by hand on a sample, never row by row. Kept: no build makes it again.");
    }

    private static RunFacts Facts(string runFolder)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(runFolder, "manifest.json")));
        var root = manifest.RootElement;
        var submitted = ReadJsonLines(Path.Combine(runFolder, "submissions.jsonl"))
            .Select(s => DateTimeOffset.Parse(s.GetProperty("at").GetString()!, CultureInfo.InvariantCulture))
            .DefaultIfEmpty(DateTimeOffset.Parse(root.GetProperty("at").GetString()!, CultureInfo.InvariantCulture))
            .Max();
        return new RunFacts(
            root.GetProperty("run").GetString()!, root.GetProperty("model").GetString()!, root.GetProperty("prompt_version").GetString()!,
            DateOnly.FromDateTime(submitted.UtcDateTime), InputHasher.Sha256Of(Path.Combine(runFolder, "clean.jsonl")));
    }

    private static List<JsonElement> ReadJsonLines(string path) =>
        !File.Exists(path)
            ? []
            : [.. File.ReadLines(path).Where(line => line.Length > 0).Select(line => JsonDocument.Parse(line).RootElement.Clone())];

    private static string Text(JsonElement element, string property) => element.GetProperty(property).GetString()!;

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
