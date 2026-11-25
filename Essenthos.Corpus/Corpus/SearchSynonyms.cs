namespace Essenthos.Core.Corpus;

/// <summary>
/// Spellings a search takes for one word, where a language names one thing two ways: Ukrainian calls
/// Hebrew <em>гебрейський</em>, <em>єврейський</em> and <em>давньоєврейський</em>, and a reader typing
/// any of them is looking for what the others find.
///
/// <para>
/// The groups are <c>SearchSynonyms.tsv</c>, embedded beside this class. A group lists parts of words —
/// a root or a front, <em>єврейськ</em> — rather than whole words, so every case and gender of the word
/// and a form built on it (<em>по-єврейськи</em>) is covered by one row.
/// </para>
/// </summary>
internal static class SearchSynonyms
{
    /// <summary>The embedded list's resource name.</summary>
    public const string Resource = "SearchSynonyms.tsv";

    private static readonly Lazy<IReadOnlyDictionary<string, string[][]>> Groups = new(Read);

    /// <summary>
    /// The other spellings of a folded word: the word with the part of a group it holds replaced by each
    /// other part of the group. Empty where it holds no part. The longest part held is the one replaced,
    /// so <em>давньоєврейською</em> is read as <em>давньоєврейськ-</em>, not as <em>єврейськ-</em>
    /// with a front.
    /// </summary>
    public static IReadOnlyList<string> Others(string folded, string? language)
    {
        if (language is null || !Groups.Value.TryGetValue(language, out var groups))
        {
            return [];
        }

        var others = new List<string>();
        foreach (var group in groups)
        {
            var held = group
                .Select(part => (Part: part, At: folded.IndexOf(part, StringComparison.Ordinal)))
                .Where(found => found.At >= 0)
                .OrderByDescending(found => found.Part.Length)
                .FirstOrDefault();
            if (held.Part is null)
            {
                continue;
            }

            var front = folded[..held.At];
            var rest = folded[(held.At + held.Part.Length)..];
            others.AddRange(group.Where(part => part != held.Part).Select(part => front + part + rest));
        }

        return others;
    }

    private static IReadOnlyDictionary<string, string[][]> Read()
    {
        using var stream = typeof(SearchSynonyms).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"{Resource} is not embedded in the Corpus assembly.");
        using var reader = new StreamReader(stream);
        var groups = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split('\t', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 3)
            {
                throw new InvalidDataException($"{Resource}: a group needs a language and two parts at least: «{line}».");
            }

            var language = fields[0];
            var parts = fields[1..].Select(part => WordFolding.Fold(part, language)).Distinct(StringComparer.Ordinal).ToArray();
            if (!groups.TryGetValue(language, out var list))
            {
                groups[language] = list = [];
            }

            list.Add(parts);
        }

        return groups.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }
}
