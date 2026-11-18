using System.Text.RegularExpressions;
using Essenthos.Core.Loading.Links;

namespace Essenthos.Core.ClearBible;

/// <summary>
/// Where a set's records are numbered over its source edition as the team edited it, verse by verse, before
/// aligning: a word added (Matthew 3:16's αὐτῷ, which Young translates and the SBLGNT lacks), a word taken
/// out (Matthew 12:46's δέ) or a word moved (John 16:23's δώσει ὑμῖν), each moving every number after it in
/// its verse. The edited text was not released; this reads it back from the records themselves, and gives each
/// identifier the source word it names, or nothing where that cannot be told.
///
/// <para>
/// The evidence is the translation's own words. Over the whole set, a source word's Strong number is
/// rendered by a few English words again and again (<em>heavens</em> for οὐρανός, <em>and</em> for καί), and
/// the edition's gloss says the same. In a verse numbered over the edition as it stands, every record fits
/// the word its identifier names; from an added word on, each fits the word one place earlier instead,
/// <em>the heavens</em> on καί and <em>he saw</em> on πνεῦμα. So each verse is read as a path through the
/// edition's words, the identifiers advancing with the words except where a word is added or taken out. An
/// addition or an omission costs as much as a record that fits nothing (<see cref="Edit"/>), so a single record
/// that fits the word beside its own better than its own is not enough to move the verse; one the other
/// editions read there (<see cref="Edition"/>) costs less, because the edited text took its words from such
/// readings — its additions are mostly the Nestle-Aland's bracketed words, which Nestle 1904 or the Textus
/// Receptus print. A word another edition has in a place where the source edition lacks it, with the letters
/// of a word the source edition has elsewhere in the verse, is that word moved.
/// </para>
///
/// <para>
/// An identifier is placed only where its word is <see cref="Certain"/>: every reading of the verse that gives
/// it another word, or makes it a word the edition lacks, is at least that much less likely. One that names
/// an added word, or whose word is not certain, is placed on nothing, and so is every record naming it,
/// because a record half-placed would be a claim about fewer words than the team made. So are two or three
/// records beside each other that read far better in another order (<see cref="Crossed"/>): words the edited text
/// orders otherwise, as Revelation 13:1's <em>seven heads</em> on κεφαλὰς ἑπτά and Mark 3:3's <em>withered hand
/// having</em> on χεῖρα ἔχοντι ξηράν.
/// </para>
/// </summary>
internal static partial class ClearBibleSourceEdits
{
    /// <summary>
    /// How much likelier, as a natural logarithm, an identifier's word has to be than any other reading of it
    /// before it is placed: 3 is twenty to one.
    /// </summary>
    internal const double Certain = 3.0;

    /// <summary>
    /// The cost of a word added to or taken out of the edition where no other edition reads otherwise, as a natural
    /// logarithm: a thousand to one against at any one place.
    /// </summary>
    private const double Edit = 7.0;

    /// <summary>
    /// The likelihood floor of a record against a word: what a record that fits nothing the set or the
    /// gloss says of the word is worth.
    /// </summary>
    private const double Floor = 0.01;

    /// <summary>The likelihood of a record against a word the edition lacks, which nothing can say anything about.</summary>
    private const double Added = 0.03;

    /// <summary>What a gloss containing one of a record's words says the record fits, at least.</summary>
    private const double Glossed = 0.5;

    /// <summary>
    /// How much likelier, as a natural logarithm, another order of a few records has to read before the order they are
    /// numbered in is taken for crossed: e^4 is fifty-five to one.
    /// </summary>
    private const double Reordering = 4.0;

    /// <summary>The fewest and most words a verse's numbering is read as being behind or ahead of the edition.</summary>
    private const int Fewest = -6, Most = 10;

    private const int States = (Most - Fewest + 1) * 2;

    /// <summary>A word of another edition of the source text: its letters as the join compares them, and its Strong number.</summary>
    internal sealed record Reading(string Letters, string? Strong);

    /// <summary>
    /// Another edition of the source text, by verse as <see cref="ClearBibleAlignment.Verse(string)"/> numbers it, and what
    /// it costs the edited text to take a word added or taken out where this edition reads so.
    /// </summary>
    internal sealed record Edition(IReadOnlyDictionary<int, List<Reading>> Verses, double Cost);

    /// <summary>
    /// The source word every identifier the records name stands for, where that is not the word the identifier
    /// itself names: a source unit, or null where the identifier names a word the edition lacks or one that
    /// cannot be told. An identifier not in it names its own word.
    /// </summary>
    /// <param name="Verses">The verses whose records are numbered over an edited text.</param>
    /// <param name="Crossed">The identifiers placed on nothing because their records are crossed with the one beside.</param>
    internal sealed record Read(IReadOnlyDictionary<string, string?> Placed, int Verses, int Crossed);

    /// <summary>Reads the edited numbering back from the records, the source edition, the translation's words and the other editions.</summary>
    internal static Read Of(
        IReadOnlyList<ClearBibleRecord> records,
        IEnumerable<ClearBibleToken> source,
        IEnumerable<ClearBibleToken> target,
        IReadOnlyList<Edition> editions,
        string? language)
    {
        var english = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var token in target)
        {
            if (!token.Excluded)
            {
                english[ClearBibleAlignment.Word(token.Id)] = token.Text.ToLowerInvariant();
            }
        }

        var words = new Dictionary<string, Word>(StringComparer.Ordinal);
        var edition = new Dictionary<int, List<string>>();
        foreach (var token in source)
        {
            var unit = ClearBibleAlignment.Unit(token.Id);
            if (ClearBibleAlignment.Verse(unit) is not { } verse)
            {
                continue;
            }

            words[unit] = new Word(Number(token.Strong), Gloss(token.Gloss), ClearBibleLinks.Comparable(token.Text, language));
            if (!edition.TryGetValue(verse, out var list))
            {
                edition[verse] = list = [];
            }

            list.Add(unit);
        }

        var lexicon = Lexicon.Of(records, words, english);

        var named = new Dictionary<int, Dictionary<int, List<string>>>();
        foreach (var record in records)
        {
            var rendering = record.Target.Select(ClearBibleAlignment.Word).Where(english.ContainsKey).Select(id => english[id]).ToList();
            foreach (var id in record.Source)
            {
                var unit = ClearBibleAlignment.Unit(id);
                if (ClearBibleAlignment.Verse(unit) is not { } verse || Position(unit) is not { } position)
                {
                    continue;
                }

                if (!named.TryGetValue(verse, out var positions))
                {
                    named[verse] = positions = [];
                }

                if (!positions.TryGetValue(position, out var list))
                {
                    positions[position] = list = [];
                }

                list.AddRange(rendering);
            }
        }

        var placed = new Dictionary<string, string?>(StringComparer.Ordinal);
        int edited = 0, crossed = 0;
        foreach (var (verse, positions) in named)
        {
            if (!edition.TryGetValue(verse, out var units))
            {
                continue;
            }

            units.Sort(StringComparer.Ordinal);
            var own = units.Select(unit => words[unit]).ToList();
            var others = editions.Select(other => (other.Verses.TryGetValue(verse, out var read) ? read : [], other.Cost)).ToList();
            var readings = new VerseReading(own, positions, lexicon, others).Read();

            var certain = new Dictionary<int, int>();
            var moved = false;
            foreach (var position in positions.Keys)
            {
                var (word, margin) = readings[position];
                moved |= word != position - 1;
                if (margin >= Certain && word is { } index && index < units.Count)
                {
                    certain[position] = index;
                }
            }

            foreach (var position in Crossed(certain, positions, own, lexicon))
            {
                certain.Remove(position);
                crossed++;
            }

            foreach (var position in positions.Keys)
            {
                var unit = Unit(verse, position);
                var at = certain.TryGetValue(position, out var index) ? units[index] : null;
                if (at != unit)
                {
                    placed[unit] = at;
                }
            }

            if (moved)
            {
                edited++;
            }
        }

        return new Read(placed, edited, crossed);
    }

    /// <summary>
    /// The positions whose records read better in another order: two or three records placed on words at most that many
    /// places apart, which some order of the same words fits at least <see cref="Reordering"/> likelier than the order they
    /// are numbered in. Every record that order would move is returned.
    /// </summary>
    private static HashSet<int> Crossed(
        IReadOnlyDictionary<int, int> certain,
        IReadOnlyDictionary<int, List<string>> named,
        IReadOnlyList<Word> edition,
        Lexicon lexicon)
    {
        var placed = certain.Where(pair => named[pair.Key].Count > 0).OrderBy(pair => pair.Value).ToList();
        var crossed = new HashSet<int>();
        double Score(int position, int word) => Math.Log(lexicon.Fit(edition[word], named[position]) + Floor);

        foreach (var size in new[] { 2, 3 })
        {
            for (var first = 0; first + size <= placed.Count; first++)
            {
                var window = placed.GetRange(first, size);
                if (window[^1].Value - window[0].Value > size)
                {
                    continue;
                }

                var words = window.Select(pair => pair.Value).ToList();
                var standing = window.Sum(pair => Score(pair.Key, pair.Value));
                var (best, order) = Orders(words)
                    .Select(order => (Score: window.Select((pair, index) => Score(pair.Key, order[index])).Sum(), Order: order))
                    .MaxBy(candidate => candidate.Score);
                if (best - standing >= Reordering)
                {
                    crossed.UnionWith(window.Where((pair, index) => order[index] != pair.Value).Select(pair => pair.Key));
                }
            }
        }

        return crossed;
    }

    /// <summary>Every order of a few words.</summary>
    private static IEnumerable<IReadOnlyList<int>> Orders(IReadOnlyList<int> words) =>
        words.Count <= 1
            ? [words]
            : words.SelectMany((word, index) =>
                Orders([.. words.Where((_, other) => other != index)]).Select(rest => (IReadOnlyList<int>)[word, .. rest]));

    /// <summary>
    /// One verse read as a path through the source edition's words. A state is an offset (how far the numbering has run
    /// ahead of the edition) and whether the position is a word of the edition or one the edited text adds.
    /// </summary>
    private sealed class VerseReading
    {
        private readonly IReadOnlyList<Word> _edition;
        private readonly IReadOnlyDictionary<int, List<string>> _named;
        private readonly Lexicon _lexicon;
        private readonly int _count;

        /// <summary>What it costs to take out each word of the edition, by its one-based place; past either end, <see cref="Edit"/>.</summary>
        private readonly double[] _omitted;

        /// <summary>What it costs to add a word after each word of the edition (0 before the first), and the words added there.</summary>
        private readonly double[] _addedCost;

        private readonly List<(Reading Reading, int? Moved)>[] _added;

        public VerseReading(
            IReadOnlyList<Word> edition,
            IReadOnlyDictionary<int, List<string>> named,
            Lexicon lexicon,
            IReadOnlyList<(List<Reading> Words, double Cost)> others)
        {
            _edition = edition;
            _named = named;
            _lexicon = lexicon;
            _count = Math.Max(named.Keys.Max(), edition.Count);
            _omitted = Enumerable.Repeat(Edit, edition.Count + 1).ToArray();
            _addedCost = Enumerable.Repeat(Edit, edition.Count + 1).ToArray();
            _added = [.. Enumerable.Range(0, edition.Count + 1).Select(_ => new List<(Reading, int?)>())];

            foreach (var (words, cost) in others)
            {
                if (words.Count > 0)
                {
                    Compare(words, cost);
                }
            }
        }

        /// <summary>Where another edition reads otherwise: the edition's words it lacks, and the words it has that the edition lacks, by place.</summary>
        private void Compare(List<Reading> other, double cost)
        {
            var pairs = Common(other);
            var mine = pairs.Select(pair => pair.Mine).ToHashSet();
            var theirs = pairs.ToDictionary(pair => pair.Theirs, pair => pair.Mine);
            var lacking = Enumerable.Range(0, _edition.Count).Where(index => !mine.Contains(index)).ToList();
            foreach (var index in lacking)
            {
                _omitted[index + 1] = Math.Min(_omitted[index + 1], cost);
            }

            var after = 0;
            for (var index = 0; index < other.Count; index++)
            {
                if (theirs.TryGetValue(index, out var at))
                {
                    after = at + 1;
                    continue;
                }

                var letters = Unsuffixed(other[index].Letters);
                var same = lacking.Where(own => Unsuffixed(_edition[own].Letters) == letters).ToList();
                _added[after].Add((other[index], same.Count == 1 ? same[0] : null));
                _addedCost[after] = Math.Min(_addedCost[after], cost);
            }
        }

        /// <summary>The longest run of words the two editions share in order, as pairs of indexes.</summary>
        private List<(int Mine, int Theirs)> Common(List<Reading> other)
        {
            int n = _edition.Count, m = other.Count;
            var lengths = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
            {
                for (var j = m - 1; j >= 0; j--)
                {
                    lengths[i, j] = Same(_edition[i], other[j]) ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
                }
            }

            var pairs = new List<(int, int)>();
            int a = 0, b = 0;
            while (a < n && b < m)
            {
                if (Same(_edition[a], other[b]) && lengths[a, b] == lengths[a + 1, b + 1] + 1)
                {
                    pairs.Add((a++, b++));
                }
                else if (lengths[a + 1, b] >= lengths[a, b + 1])
                {
                    a++;
                }
                else
                {
                    b++;
                }
            }

            return pairs;
        }

        /// <summary>The same word in two editions: the same letters but for a movable ν, or the same Strong number and first two letters.</summary>
        private static bool Same(Word mine, Reading theirs) =>
            Unsuffixed(mine.Letters) == Unsuffixed(theirs.Letters)
            || (mine.Strong == Number(theirs.Strong) && mine.Letters.Length >= 2 && theirs.Letters.Length >= 2
                && string.CompareOrdinal(mine.Letters, 0, theirs.Letters, 0, 2) == 0);

        private static string Unsuffixed(string letters) =>
            letters.Length > 2 && letters[^1] == 'ν' ? letters[..^1] : letters;

        private bool Rendered(int position, out List<string> rendering) =>
            _named.TryGetValue(position, out rendering!) && rendering.Count > 0;

        /// <summary>A state's likelihood at a position, and the edition's word it reads there (zero-based), or -1 for one it lacks.</summary>
        private (double Score, int Word) Emit(int position, int state)
        {
            var (offset, added) = Of(state);
            var rendered = Rendered(position, out var rendering);
            if (added)
            {
                var gap = position - offset;
                if (gap < 0 || gap > _edition.Count)
                {
                    return (double.NegativeInfinity, -1);
                }

                if (!rendered)
                {
                    return (0, -1);
                }

                var best = (Score: Math.Log(Added), Word: -1);
                foreach (var (reading, moved) in _added[gap])
                {
                    var fit = moved is { } own ? _lexicon.Fit(_edition[own], rendering) : _lexicon.Fit(new Word(Number(reading.Strong), [], reading.Letters), rendering);
                    if (Math.Log(fit + Floor) > best.Score)
                    {
                        best = (Math.Log(fit + Floor), moved ?? -1);
                    }
                }

                return best;
            }

            var word = position - offset;
            if (word < 1 || (word > _edition.Count && rendered))
            {
                return (double.NegativeInfinity, word - 1);
            }

            return (rendered && word <= _edition.Count ? Math.Log(_lexicon.Fit(_edition[word - 1], rendering) + Floor) : 0, word - 1);
        }

        /// <summary>The cost of taking out the edition's words from one place to another, both one-based and inclusive.</summary>
        private double Omitting(int first, int last)
        {
            var cost = 0.0;
            for (var index = first; index <= last; index++)
            {
                cost += index >= 1 && index <= _edition.Count ? _omitted[index] : Edit;
            }

            return cost;
        }

        private double Adding(int after) => after >= 0 && after <= _edition.Count ? _addedCost[after] : Edit;

        private double Step(int position, int from, int to)
        {
            var before = Of(from).Offset;
            var (after, added) = Of(to);
            var last = position - 1 - before;
            if (added)
            {
                return after == before + 1 ? -Adding(last) : double.NegativeInfinity;
            }

            return after == before ? 0 : after < before ? -Omitting(last + 1, position - after - 1) : double.NegativeInfinity;
        }

        private double Start(int state)
        {
            var (offset, added) = Of(state);
            return added ? (offset == 1 ? -Adding(0) : double.NegativeInfinity) : offset <= 0 ? -Omitting(1, -offset) : double.NegativeInfinity;
        }

        /// <summary>
        /// Every numbered position that a record names, with the edition's word it most likely names (a zero-based index, or
        /// null for a word the edition lacks) and how much likelier that is than the next reading, as a logarithm.
        /// </summary>
        public Dictionary<int, (int? Word, double Margin)> Read()
        {
            var emitted = new (double Score, int Word)[_count + 1, States];
            for (var position = 1; position <= _count; position++)
            {
                for (var state = 0; state < States; state++)
                {
                    emitted[position, state] = Emit(position, state);
                }
            }

            var forward = new double[_count + 1, States];
            var backward = new double[_count + 1, States];
            for (var state = 0; state < States; state++)
            {
                forward[1, state] = Start(state) + emitted[1, state].Score;
            }

            for (var position = 2; position <= _count; position++)
            {
                for (var to = 0; to < States; to++)
                {
                    var best = double.NegativeInfinity;
                    for (var from = 0; from < States; from++)
                    {
                        best = Math.Max(best, forward[position - 1, from] + Step(position, from, to));
                    }

                    forward[position, to] = best + emitted[position, to].Score;
                }
            }

            for (var position = _count - 1; position >= 1; position--)
            {
                for (var from = 0; from < States; from++)
                {
                    var best = double.NegativeInfinity;
                    for (var to = 0; to < States; to++)
                    {
                        best = Math.Max(best, Step(position + 1, from, to) + emitted[position + 1, to].Score + backward[position + 1, to]);
                    }

                    backward[position, from] = best;
                }
            }

            var readings = new Dictionary<int, (int?, double)>(_named.Count);
            foreach (var position in _named.Keys)
            {
                var byWord = new Dictionary<int, double>();
                for (var state = 0; state < States; state++)
                {
                    var word = emitted[position, state].Word;
                    var total = forward[position, state] + backward[position, state];
                    byWord[word] = byWord.TryGetValue(word, out var seen) ? Math.Max(seen, total) : total;
                }

                var ranked = byWord.OrderByDescending(pair => pair.Value).Take(2).ToList();
                var margin = double.IsNegativeInfinity(ranked[0].Value) ? 0 : ranked[0].Value - ranked[1].Value;
                readings[position] = (ranked[0].Key < 0 ? null : ranked[0].Key, margin);
            }

            return readings;
        }
    }

    private static (int Offset, bool Added) Of(int state) => (state / 2 + Fewest, state % 2 == 1);

    private static int? Position(string unit) =>
        unit.Length >= 11 && int.TryParse(unit.AsSpan(8, 3), out var position) ? position : null;

    private static string Unit(int verse, int position) =>
        $"{verse:D8}{position:D3}";

    private static int Number(string? strong) =>
        strong is not null && Digits().Match(strong) is { Success: true } match ? int.Parse(match.Value) : -1;

    private static HashSet<string> Gloss(string? gloss) =>
        gloss is null ? [] : [.. Letters().Matches(gloss.ToLowerInvariant()).Select(match => match.Value)];

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    [GeneratedRegex("[a-z']+")]
    private static partial Regex Letters();

    /// <param name="Strong">The word's Strong number, or -1.</param>
    /// <param name="Gloss">The words of the edition's gloss for it, in lower case.</param>
    /// <param name="Letters">Its letters as the join compares them.</param>
    private sealed record Word(int Strong, HashSet<string> Gloss, string Letters);

    /// <summary>What the set's own records render each Strong number with, counted over the records naming one source word.</summary>
    private sealed class Lexicon(Dictionary<(int, string), int> pairs, Dictionary<int, int> counts)
    {
        public static Lexicon Of(
            IReadOnlyList<ClearBibleRecord> records,
            IReadOnlyDictionary<string, Word> words,
            IReadOnlyDictionary<string, string> english)
        {
            var pairs = new Dictionary<(int, string), int>();
            var counts = new Dictionary<int, int>();
            foreach (var record in records)
            {
                if (record.Source.Count != 1 || !words.TryGetValue(ClearBibleAlignment.Unit(record.Source[0]), out var word))
                {
                    continue;
                }

                counts[word.Strong] = counts.GetValueOrDefault(word.Strong) + 1;
                foreach (var rendering in record.Target.Select(ClearBibleAlignment.Word).Where(english.ContainsKey).Select(id => english[id]).Distinct())
                {
                    pairs[(word.Strong, rendering)] = pairs.GetValueOrDefault((word.Strong, rendering)) + 1;
                }
            }

            return new Lexicon(pairs, counts);
        }

        /// <summary>How well the best of a record's words fits a source word, from 0 to 1.</summary>
        public double Fit(Word word, IReadOnlyList<string> rendering)
        {
            var count = counts.GetValueOrDefault(word.Strong);
            var best = 0.0;
            foreach (var english in rendering)
            {
                var fit = (double)pairs.GetValueOrDefault((word.Strong, english)) / (count + 2);
                if (english.Length > 2 && word.Gloss.Contains(english))
                {
                    fit = Math.Max(fit, Glossed);
                }

                best = Math.Max(best, fit);
            }

            return best;
        }
    }
}
