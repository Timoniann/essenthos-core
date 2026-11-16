using System.Globalization;
using System.Text;
using Essenthos.Core.Conversion;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// A sheet to read what the merge decided against, by hand: a sample of a pair's rows the merge did not make claims (its
/// <c>statuses</c>), each with both verses and the links that stand on the same words. Every word is printed with its key,
/// so that of two words spelt alike in a verse the reader knows which one a link names.
/// </summary>
internal static class MergeSheet
{
    /// <param name="status">The beginning of the statuses to sample (<c>withdrawn-by</c>, <c>withdrawn-by stated</c>); empty takes every one.</param>
    /// <param name="before">A store whose statuses for the same pair are left out where a row has the same status there: what changed.</param>
    public static string Write(ProductStore store, string pair, string status, int count, int seed, ProductStore? before = null)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var rows = Statuses(store, pair).Where(r => r.Status!.StartsWith(status, StringComparison.Ordinal)).ToList();
        if (before is not null)
        {
            var known = Statuses(before, pair).Select(Identity).ToHashSet(StringComparer.Ordinal);
            rows = [.. rows.Where(r => !known.Contains(Identity(r)))];
        }

        rows.Sort((a, b) => string.CompareOrdinal(Identity(a), Identity(b)));
        var random = new Random(seed);
        var sample = rows.OrderBy(_ => random.Next()).Take(count).ToList();

        var standing = new Dictionary<string, List<FinalRow>>(StringComparer.Ordinal);
        using (var reader = store.Open(MappingProducts.Of(pair, MappingProducts.Final), FinalRow.Schema))
        {
            foreach (var row in reader.Rows().Where(r => r.Settled))
            {
                foreach (var key in MappingText.Split(row.From).Select(k => "F" + k).Concat(MappingText.Split(row.To).Select(k => "T" + k)))
                {
                    (standing.TryGetValue(key, out var list) ? list : standing[key] = []).Add(row);
                }
            }
        }

        var verses = sample.SelectMany(r => MappingText.Split(r.From).Select(k => (fromText, Verse(k)))
            .Concat(MappingText.Split(r.To).Select(k => (toText, Verse(k))))).ToHashSet();
        var words = Words(store, fromText, verses).Concat(Words(store, toText, verses))
            .ToDictionary(w => w.Key, w => w.Value, StringComparer.Ordinal);

        string Word(string text, string key) => $"{words.GetValueOrDefault(text + " " + key, "?")} ({key})";
        string Side(string text, string keys) => keys.Length == 0 ? "nothing" : string.Join(" · ", MappingText.Split(keys).Select(k => Word(text, k)));
        string Line(string text, string verse) => $"- {text} {verse}: " + string.Join(' ',
            words.Where(w => w.Key.StartsWith(text + " " + verse + ".", StringComparison.Ordinal))
                .Select(w => (Position: int.Parse(w.Key[(w.Key.LastIndexOf('.') + 1)..], CultureInfo.InvariantCulture), w.Value))
                .OrderBy(w => w.Position).Select(w => $"{w.Position}:{w.Value}"));

        var sheet = new StringBuilder();
        sheet.AppendLine(CultureInfo.InvariantCulture, $"# {pair}: {sample.Count} of {rows.Count} rows{(status.Length > 0 ? $" {status}" : string.Empty)}{(before is null ? string.Empty : ", new against the store compared")}");
        var number = 0;
        foreach (var row in sample)
        {
            var fromVerses = MappingText.Split(row.From).Select(Verse).Distinct().ToList();
            var toVerses = MappingText.Split(row.To).Select(Verse).Distinct().ToList();
            sheet.AppendLine();
            sheet.AppendLine(CultureInfo.InvariantCulture, $"### {++number}. {pair} {string.Join(", ", fromVerses.Concat(toVerses).Distinct())}: {row.Method} {row.Status}");
            foreach (var verse in fromVerses)
            {
                sheet.AppendLine(Line(fromText, verse));
            }

            foreach (var verse in toVerses)
            {
                sheet.AppendLine(Line(toText, verse));
            }

            sheet.AppendLine(CultureInfo.InvariantCulture,
                $"- **{row.Status}** ({row.Method} {MappingText.Confidence(row.Confidence)}): {Side(fromText, row.From)}  →  {Side(toText, row.To)}");
            var touching = MappingText.Split(row.From).Select(k => "F" + k).Concat(MappingText.Split(row.To).Select(k => "T" + k))
                .SelectMany(k => standing.GetValueOrDefault(k) ?? []).Distinct().OrderBy(r => r.From, StringComparer.Ordinal).ThenBy(r => r.To, StringComparer.Ordinal);
            foreach (var link in touching)
            {
                sheet.AppendLine(CultureInfo.InvariantCulture,
                    $"- **stands** ({Claim(link.Method, link.Confidence)}): {Side(fromText, link.From)}  →  {Side(toText, link.To)}");
            }
        }

        return sheet.ToString();
    }

    private static string Claim(string method, double? confidence) =>
        confidence is null ? method : $"{method} {MappingText.Confidence(confidence)}";

    private static string Identity(MappingRow row) => $"{row.From}|{row.To}|{row.Relation}|{row.Method}|{row.Source}|{row.Status}";

    private static string Verse(string key) => key[..key.LastIndexOf('.')];

    private static List<MappingRow> Statuses(ProductStore store, string pair)
    {
        var product = MappingProducts.Of(pair, MappingProducts.Statuses);
        if (!store.Exists(product))
        {
            return [];
        }

        using var reader = store.Open(product, MappingRow.Schema);
        return [.. reader.Rows().Where(r => r.Status is not null)];
    }

    private static IEnumerable<KeyValuePair<string, string>> Words(ProductStore store, string text, HashSet<(string Text, string Verse)> verses)
    {
        using var reader = store.Open(TextWord.ProductOf(text), TextWord.Schema(text));
        foreach (var word in reader.Rows())
        {
            if (verses.Contains((text, word.Key.Verse.ToString())))
            {
                yield return new(text + " " + word.Key.Own, word.Surface);
            }
        }
    }
}
