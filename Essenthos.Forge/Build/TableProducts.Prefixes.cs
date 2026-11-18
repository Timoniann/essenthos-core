using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Products;
using Essenthos.Core.Strong;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Build;

internal static partial class TableProducts
{
    /// <summary>
    /// The entries of the prefixes BHSA writes as words and numbers from the Open Hebrew Bible (<c>H9000</c> וְ, <c>H9005</c> לְ, ...),
    /// which Strong's dictionary does not hold (hand TSV, ours): the <c>eng</c> row of a number is its <c>strong_entry</c>, every
    /// other language's row its <c>strong_entry_translation</c>.
    /// </summary>
    public const string Prefixes = "lexicon/prefixes";

    private const string EntryLanguage = "eng";

    /// <summary>The language of the word, as Strong's Hebrew file tags its own: <c>heb</c>.</summary>
    private const string PrefixLanguage = "heb";

    internal sealed record PrefixRow(
        string Number,
        string Language,
        string? Lemma,
        string? Transliteration,
        string? Morphology,
        string Definition,
        string DetailedDefinition,
        string Source)
    {
        public static readonly ProductSchema<PrefixRow> Schema = new ProductSchema<PrefixRow>()
            .Text("number", r => r.Number)
            .Text("language", r => r.Language)
            .OptionalText("lemma", r => r.Lemma)
            .OptionalText("transliteration", r => r.Transliteration)
            .OptionalText("morphology", r => r.Morphology)
            .Text("definition", r => r.Definition)
            .Text("detailed_definition", r => r.DetailedDefinition)
            .Text("source", r => r.Source)
            .Reads(row => new PrefixRow(row.Text(0), row.Text(1), row.TextOrNull(2), row.TextOrNull(3), row.TextOrNull(4), row.Text(5), row.Text(6),
                row.Text(7)));

        public bool IsEntry => Language == EntryLanguage;
    }

    /// <summary>
    /// The rows of <see cref="Prefixes"/>, refused whole where one would stand in for a number Strong's dictionary holds, name a
    /// number outside the prefix range, repeat a number in a language, render a number that has no entry, or translate an identifier.
    /// </summary>
    internal static List<PrefixRow> PrefixRows(ProductStore store, IReadOnlySet<string> strongs)
    {
        using var reader = store.Open(Prefixes, PrefixRow.Schema);
        var rows = reader.ReadAll().ToList();
        var file = $"Essenthos/{Prefixes}.tsv";
        var entries = rows.Where(r => r.IsEntry).Select(r => r.Number).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<(string, string)>();
        foreach (var row in rows)
        {
            var problem =
                StrongNumbers.Normalize(row.Number) != row.Number ? "is not a Strong number in its canonical spelling (H9005)"
                : !StrongMorphemeCodes.IsMorphemeCode(row.Number) ? "is outside the prefix numbers H9000-H9099"
                : strongs.Contains(row.Number) ? "is an entry of Strong's dictionary, which this file must not stand in for"
                : row.Language.Length != 3 || !row.Language.All(char.IsAsciiLetterLower) ? "names its language in other than three lower-case letters (eng, ukr)"
                : !seen.Add((row.Number, row.Language)) ? "is written twice in this language"
                : row.IsEntry && (row.Lemma is null || row.Transliteration is null) ? "is the entry and gives no lemma or transliteration"
                : !row.IsEntry && (row.Lemma ?? row.Transliteration ?? row.Morphology) is not null
                    ? "translates an identifier: lemma, transliteration and morphology belong on the eng row alone"
                : !row.IsEntry && !entries.Contains(row.Number) ? "renders a number that has no eng row"
                : null;
            if (problem is not null)
            {
                throw new InvalidDataException($"{file}: {row.Number} {row.Language} {problem}. Correct the row in the file and run forge tables again.");
            }
        }

        return rows;
    }

    private static IEnumerable<string?[]> PrefixEntryLines(IEnumerable<PrefixRow> rows) =>
        rows.Where(r => r.IsEntry).Select(r => new[]
        {
            r.Number, r.Lemma, r.Transliteration, null, r.Definition, null, null, r.Morphology, r.DetailedDefinition, null, PrefixLanguage, null,
        });

    private static IEnumerable<string?[]> PrefixTranslationLines(IEnumerable<PrefixRow> rows) =>
        rows.Where(r => !r.IsEntry).Select(r => new[]
        {
            r.Number, r.Language, r.Definition, null, null, r.DetailedDefinition, EnumSpelling.Of(LinkMethod.ModelReading), null, r.Source, null,
        });
}
