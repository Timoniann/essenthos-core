using System.Globalization;
using System.Text;
using Npgsql;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The words a composition would be measured over: the source text's words in the books the target
/// holds, which of them anything links already, and which target words a source states for each.
/// </summary>
/// <param name="Words">Each word in scope, by id: its canonical book and its form in lower case.</param>
/// <param name="Linked">Words any link names already, to any text, which is what the console counts.</param>
/// <param name="Kept">
/// Of those, the words a run would leave linked: all but the ones only this pair's own earlier
/// aligner links name, which a run replaces.
/// </param>
/// <param name="Stated">
/// The target words each source word is joined to by a link that is not the aligner's — a statement,
/// a printed Strong number — which no reading of the composition looks at, with the prefixes written
/// joined to them (<see cref="CompositionPipeline.Key"/>).
/// </param>
/// <param name="Frequent">
/// The text's commonest forms, which in every language here are its articles, conjunctions,
/// prepositions and pronouns: the function words, told apart without a list for each language.
/// </param>
internal sealed record CompositionScope(
    IReadOnlyDictionary<long, (int Book, string Form)> Words,
    IReadOnlySet<long> Linked,
    IReadOnlySet<long> Kept,
    IReadOnlyDictionary<long, HashSet<long>> Stated,
    IReadOnlySet<string> Frequent);

/// <summary>
/// A dry run of a composition, scored. What the run would add is counted against what the text
/// already has, and where a source states a word the merged answer is checked against it.
/// </summary>
internal sealed class CompositionTrial
{
    /// <summary>How many of a text's commonest forms count as its function words.</summary>
    public const int FrequentForms = 100;

    private static readonly double[] Bands = [0.25, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9];

    private readonly SortedDictionary<int, (int Words, int Before, int After)> _books = [];
    private readonly Tally _stated = new();
    private readonly Tally _statedFrequent = new();
    private readonly Tally _statedOther = new();
    private readonly SortedDictionary<Route, Tally> _byRoute = [];
    private readonly SortedDictionary<Route, Tally> _frequentByRoute = [];
    private readonly SortedDictionary<Route, int> _addedByRoute = [];
    private readonly Tally[][] _byBand = [[.. Bands.Select(_ => new Tally())], [.. Bands.Select(_ => new Tally())]];
    private readonly int[][] _addedByBand = [new int[Bands.Length], new int[Bands.Length]];

    private string[] _vias = [];
    private int _statedWords;
    private int _rivalWords;
    private int _addedFrequent;
    private int _addedOther;

    public int Words => _books.Values.Sum(book => book.Words);
    public int Before => _books.Values.Sum(book => book.Before);
    public int After => _books.Values.Sum(book => book.After);
    public int Added => _addedFrequent + _addedOther;
    public int RivalWords => _rivalWords;
    public (int Proposed, int Agreed) Held => (_stated.Proposed, _stated.Agreed);

    public static async Task<CompositionScope> Scope(
        NpgsqlConnection connection,
        int fromTextId,
        int toTextId,
        IReadOnlyDictionary<long, HashSet<long>> stated,
        IReadOnlySet<int>? books,
        CancellationToken cancellationToken)
    {
        var within = books?.ToArray() ?? await TargetBooks(connection, toTextId, cancellationToken);

        var words = new Dictionary<long, (int, string)>(700_000);
        await using (var command = new NpgsqlCommand(
            """
            SELECT w.id, r.canonical_book, lower(w.text)
            FROM word w
            JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
            WHERE w.text_id = @from AND NOT w.elided AND r.canonical_book = ANY(@books)
            """, connection))
        {
            command.Parameters.AddWithValue("from", fromTextId);
            command.Parameters.AddWithValue("books", within);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                words[reader.GetInt64(0)] = (reader.GetInt32(1), reader.GetString(2));
            }
        }

        var linked = new HashSet<long>(words.Count);
        var kept = new HashSet<long>(words.Count);
        await using (var command = new NpgsqlCommand(
            """
            SELECT lw.word_id,
                   bool_and(l.method = 'aligner' AND l.from_text_id = @from AND l.to_text_id = @to)
            FROM link_word lw
            JOIN link l ON l.id = lw.link_id
            JOIN word w ON w.id = lw.word_id
            WHERE w.text_id = @from
            GROUP BY lw.word_id
            """,
            connection))
        {
            command.Parameters.AddWithValue("from", fromTextId);
            command.Parameters.AddWithValue("to", toTextId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                linked.Add(reader.GetInt64(0));
                if (!reader.GetBoolean(1))
                {
                    kept.Add(reader.GetInt64(0));
                }
            }
        }

        return new CompositionScope(words, linked, kept, stated, Frequent(words.Values.Select(word => word.Item2)));
    }

    public static IReadOnlySet<string> Frequent(IEnumerable<string> forms) =>
        forms.GroupBy(form => form, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(FrequentForms)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Scores the merged pairs against the scope. A word the source states is never counted as
    /// added, because it is linked already; the question there is only whether the aligner agrees.
    /// </summary>
    public static CompositionTrial Score(
        CompositionScope scope,
        IReadOnlyList<RoutedLink> merged,
        string[] vias)
    {
        var trial = new CompositionTrial { _vias = vias };
        var reached = new HashSet<long>();

        foreach (var group in merged.Where(link => scope.Words.ContainsKey(link.From)).GroupBy(link => link.From))
        {
            reached.Add(group.Key);
            var frequent = scope.Frequent.Contains(scope.Words[group.Key].Form);

            if (scope.Stated.TryGetValue(group.Key, out var targets))
            {
                trial._statedWords++;
                var rival = false;
                foreach (var link in group)
                {
                    var agrees = targets.Contains(link.To);
                    rival |= !agrees;
                    trial._stated.Add(agrees);
                    (frequent ? trial._statedFrequent : trial._statedOther).Add(agrees);
                    Tallied(trial._byRoute, link.Route).Add(agrees);
                    if (frequent)
                    {
                        Tallied(trial._frequentByRoute, link.Route).Add(agrees);
                    }

                    trial._byBand[Agreed(link.Route)][Band(link.Confidence)].Add(agrees);
                }

                if (rival)
                {
                    trial._rivalWords++;
                }
            }
            else if (!scope.Linked.Contains(group.Key))
            {
                if (frequent)
                {
                    trial._addedFrequent++;
                }
                else
                {
                    trial._addedOther++;
                }

                var strongest = group.MaxBy(link => link.Confidence)!;
                trial._addedByBand[Agreed(strongest.Route)][Band(strongest.Confidence)]++;
                trial._addedByRoute[strongest.Route] = trial._addedByRoute.GetValueOrDefault(strongest.Route) + 1;
            }
        }

        foreach (var (id, (book, _)) in scope.Words)
        {
            var before = scope.Linked.Contains(id);
            var after = scope.Kept.Contains(id) || reached.Contains(id);
            var standing = trial._books.GetValueOrDefault(book);
            trial._books[book] = (standing.Words + 1, standing.Before + (before ? 1 : 0), standing.After + (after ? 1 : 0));
        }

        return trial;
    }

    public string Describe(
        string from,
        string via,
        string to,
        double minimumConfidence,
        IReadOnlySet<int>? books,
        TimeSpan trained,
        TimeSpan elapsed)
    {
        var report = new StringBuilder()
            .AppendLine($"{from} to {to} through {via}, nothing written — " +
                        (books is null ? "the whole text" : $"books {string.Join(", ", books.Order())}, trained on those alone") +
                        $", min {minimumConfidence.ToString("F2", CultureInfo.InvariantCulture)}, " +
                        $"{trained.TotalSeconds:F0}s to propose, {elapsed.TotalSeconds:F0}s in all")
            .AppendLine($"  words {Words}, linked before {Before} ({Share(Before, Words)}), " +
                        $"after {After} ({Share(After, Words)}), added {Added}: " +
                        $"{_addedFrequent} of the {FrequentForms} commonest forms, {_addedOther} others")
            .AppendLine($"  where a source states the word ({_statedWords} words reached, held out): " +
                        $"agrees {_stated}; commonest forms {_statedFrequent}; others {_statedOther}")
            .AppendLine($"  words a source states where the aligner names another: {_rivalWords}")
            .AppendLine("  by the readings that found it: " +
                        string.Join("; ", _byRoute.Select(entry => $"{Name(entry.Key)} {entry.Value}")))
            .AppendLine("  of those, the commonest forms: " +
                        string.Join("; ", _frequentByRoute.Select(entry => $"{Name(entry.Key)} {entry.Value}")))
            .AppendLine("  words added, by the readings that found them: " +
                        string.Join("; ", _addedByRoute.Select(entry => $"{Name(entry.Key)} {entry.Value}")))
            .AppendLine("  by confidence, agreement where stated / words added — one family of readings | both:");

        for (var band = 0; band < Bands.Length; band++)
        {
            var upper = band + 1 < Bands.Length ? Bands[band + 1].ToString("F2", CultureInfo.InvariantCulture) : "1";
            report.AppendLine($"    {Bands[band].ToString("F2", CultureInfo.InvariantCulture)}–{upper}  " +
                              $"{_byBand[0][band]} / {_addedByBand[0][band]}  |  {_byBand[1][band]} / {_addedByBand[1][band]}");
        }

        report.AppendLine("  book   words   before   after");
        foreach (var (book, (words, before, after)) in _books)
        {
            report.AppendLine($"  {book,4} {words,7}   {Share(before, words),6}  {Share(after, words),6}");
        }

        return report.ToString();
    }

    private static int Band(double confidence)
    {
        for (var band = Bands.Length - 1; band > 0; band--)
        {
            if (confidence >= Bands[band])
            {
                return band;
            }
        }

        return 0;
    }

    private static int Agreed(Route route) => Routes.Families(route) > 1 ? 1 : 0;

    private string Name(Route route) => Admission.Name(route, _vias);

    private static Tally Tallied(SortedDictionary<Route, Tally> tallies, Route key)
    {
        if (!tallies.TryGetValue(key, out var tally))
        {
            tallies[key] = tally = new Tally();
        }

        return tally;
    }

    private static string Share(int part, int whole) =>
        whole == 0 ? "–" : ((double)part / whole).ToString("P1", CultureInfo.InvariantCulture);

    private static async Task<int[]> TargetBooks(NpgsqlConnection connection, int textId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT DISTINCT r.canonical_book
            FROM verse_reference r JOIN verse v ON v.id = r.verse_id
            WHERE v.text_id = @text
            """, connection);
        command.Parameters.AddWithValue("text", textId);
        var books = new List<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            books.Add(reader.GetInt32(0));
        }

        return [.. books];
    }

    private sealed class Tally
    {
        public int Proposed { get; private set; }
        public int Agreed { get; private set; }

        public void Add(bool agrees)
        {
            Proposed++;
            Agreed += agrees ? 1 : 0;
        }

        public override string ToString() =>
            Proposed == 0 ? "–" : $"{Share(Agreed, Proposed)} of {Proposed}";
    }
}
