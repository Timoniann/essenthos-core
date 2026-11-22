using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Macula;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Catss;

/// <summary>
/// CATSS's alignment of the Masoretic text with Rahlfs laid on BHSA and Brenton's Greek. CATSS's Hebrew is the Leningrad
/// codex as BHS prints it, numbered as BHSA is, so a line's Hebrew is placed on BHSA's words by the consonants of the verse
/// (<see cref="MaculaHebrewJoin"/>). Its Greek is Rahlfs, which Brenton differs from in a word here and there and in
/// numbering, so a line's Greek is placed on Brenton's words by their letters, within the Brenton verses the frame puts at
/// the Hebrew verse's address and those CATSS names as the Greek's own; a word two verses claim goes to neither.
/// </summary>
internal static class CatssLinks
{
    /// <summary>CATSS's books by their code, with the canonical book and whether Brenton prints that form of the text.</summary>
    /// <remarks>Brenton follows Judges B, Joshua B and Theodotion's Daniel; Judges A, Joshua A's chapters and the Old Greek of Daniel are other texts.</remarks>
    public static readonly IReadOnlyDictionary<string, (int Book, bool Brenton)> Books = new Dictionary<string, (int, bool)>(StringComparer.Ordinal)
    {
        ["Gen"] = (1, true), ["Exod"] = (2, true), ["Lev"] = (3, true), ["Num"] = (4, true), ["Dt"] = (5, true),
        ["Jsh"] = (6, true), ["Jsha"] = (6, false), ["Jdgb"] = (7, true), ["Jdga"] = (7, false), ["Ruth"] = (8, true),
        ["1Sm"] = (9, true), ["2Sm"] = (10, true), ["1Kg"] = (11, true), ["2Kg"] = (12, true), ["1Ch"] = (13, true), ["2Ch"] = (14, true),
        // The archive's "Esth" is a second copy of Ezra, byte for byte: its copy holds no Esther.
        ["Ezr"] = (15, true), ["Neh"] = (16, true), ["Qoh"] = (21, true), ["Song"] = (22, true),
        ["Isa"] = (23, true), ["Jer"] = (24, true), ["Lam"] = (25, true), ["Ezk"] = (26, true), ["DanTh"] = (27, true), ["Dan"] = (27, false),
        ["Hos"] = (28, true), ["Joel"] = (29, true), ["Amos"] = (30, true), ["Obad"] = (31, true), ["Jonah"] = (32, true),
        ["Micah"] = (33, true), ["Nahum"] = (34, true), ["Hab"] = (35, true), ["Zeph"] = (36, true), ["Hag"] = (37, true),
        ["Zach"] = (38, true), ["Mal"] = (39, true),
    };

    /// <summary>A link CATSS states: Brenton's words and BHSA's, either side empty for a plus or a minus.</summary>
    public sealed record Draft(LinkRelation Relation, List<int> Greek, List<int> Hebrew, string Evidence);

    /// <summary>What the join and the projection did in one book.</summary>
    public sealed class Tally(string book)
    {
        public string Book { get; } = book;
        public int Verses, VersesJoined, VersesDiffering, HebrewLines, HebrewLinesPlaced, GreekTokens, GreekPlaced, GreekContested, GreekFilled;
        public int Renders, Pluses, Minuses, MinusesUnproved, Transposed, SkippedShifted, SkippedRead, SkippedBroad;
        public int BhsaWords, BrentonWords;

        public override string ToString() =>
            $"{Book}: verses {VersesJoined}/{Verses} joined ({VersesDiffering} by aligned letters), Hebrew lines placed {HebrewLinesPlaced}/{HebrewLines}"
            + $" ({Percent(HebrewLinesPlaced, HebrewLines)}), Greek words placed {GreekPlaced}/{GreekTokens} ({Percent(GreekPlaced, GreekTokens)}, {GreekContested} contested)"
            + $" and {GreekFilled} spelled otherwise placed between them;"
            + $" renders {Renders}, pluses {Pluses}, minuses {Minuses} ({MinusesUnproved} not shown absent in Brenton), {Transposed} transposed;"
            + $" left: {SkippedShifted} shifted lines, {SkippedRead} lines whose Greek read other Hebrew, {SkippedBroad} pluses and minuses of long unequal stretches;"
            + $" BHSA words {BhsaWords}, Brenton words {BrentonWords}";

        private static string Percent(int a, int b) => b == 0 ? "-" : $"{100.0 * a / b:0.0}%";
    }

    public static (List<Draft> Drafts, List<Tally> Tallies) Draw(IReadOnlyList<CatssVerse> catss, ProductText bhsa, ProductText greek)
    {
        var drafts = new List<Draft>();
        var tallies = new List<Tally>();
        var filled = new HashSet<int>();
        foreach (var book in catss.GroupBy(v => v.Book))
        {
            if (!Books.TryGetValue(book.Key, out var target) || !target.Brenton)
            {
                continue;
            }

            var tally = new Tally(book.Key);
            tallies.Add(tally);
            var verses = book.ToList();
            var hebrew = verses.Select(v => JoinHebrew(v, target.Book, bhsa, tally)).ToList();
            var placed = ProjectGreek(verses, target.Book, bhsa, greek, tally, filled);
            for (var i = 0; i < verses.Count; i++)
            {
                Link(verses[i], hebrew[i], placed[i], drafts, tally, filled);
            }

            tally.BhsaWords = drafts.Where(d => d.Relation == LinkRelation.Renders).SelectMany(d => d.Hebrew)
                .Count(id => bhsa.Words[id].Key.Verse.Book == target.Book);
            tally.BrentonWords = drafts.Where(d => d.Relation == LinkRelation.Renders).SelectMany(d => d.Greek)
                .Count(id => greek.Words[id].Key.Verse.Book == target.Book);
        }

        return (drafts, tallies);
    }

    /// <summary>Each line's BHSA words by the consonants of the verse; empty for a line with no Hebrew or none placed.</summary>
    internal static List<int>[] JoinHebrew(CatssVerse verse, int book, ProductText bhsa, Tally tally)
    {
        var lines = new List<int>[verse.Lines.Count];
        for (var i = 0; i < lines.Length; i++) lines[i] = [];
        var own = new VerseKey(book, verse.Chapter, verse.Verse);
        var ids = bhsa.InVerse(own).ToList();
        var withHebrew = verse.Lines.Select((line, i) => (line, i)).Where(x => x.line.Kind == CatssHebrew.Words).ToList();
        tally.Verses++;
        tally.HebrewLines += withHebrew.Count;
        if (ids.Count == 0 || withHebrew.Count == 0)
        {
            return lines;
        }

        var mine = string.Concat(withHebrew.Select(x => string.Concat(x.line.Hebrew.Select(HebrewBetacode.Letters))));
        var starts = new int[withHebrew.Count + 1];
        for (var k = 0; k < withHebrew.Count; k++)
        {
            starts[k + 1] = starts[k] + withHebrew[k].line.Hebrew.Sum(w => HebrewBetacode.Letters(w).Length);
        }

        var (theirs, theirStarts) = MaculaHebrewJoin.Letters(ids.Select(id => bhsa.Words[id].Surface));
        int[] to;
        if (mine == theirs)
        {
            to = [.. Enumerable.Range(0, mine.Length)];
        }
        else
        {
            tally.VersesDiffering++;
            to = MaculaHebrewJoin.Align(mine, theirs);
            if (to.Count(t => t >= 0) < MaculaHebrewJoin.Agreement * Math.Max(mine.Length, theirs.Length))
            {
                return lines;
            }
        }

        tally.VersesJoined++;
        // Each BHSA word goes to the line holding most of its letters.
        var best = new Dictionary<int, (int Line, int Letters)>();
        for (var k = 0; k < withHebrew.Count; k++)
        {
            foreach (var group in Enumerable.Range(starts[k], starts[k + 1] - starts[k]).Select(c => to[c]).Where(c => c >= 0)
                         .GroupBy(c => Owner(theirStarts, c)))
            {
                if (!best.TryGetValue(group.Key, out var held) || group.Count() > held.Letters)
                {
                    best[group.Key] = (withHebrew[k].i, group.Count());
                }
            }
        }

        foreach (var (word, (line, _)) in best.OrderBy(entry => entry.Key))
        {
            lines[line].Add(ids[word]);
        }

        tally.HebrewLinesPlaced += withHebrew.Count(x => lines[x.i].Count > 0);
        return lines;
    }

    private static int Owner(int[] starts, int letter)
    {
        var at = Array.BinarySearch(starts, letter);
        at = at >= 0 ? at : ~at - 1;
        while (at + 1 < starts.Length - 1 && starts[at + 1] == letter)
        {
            at++;
        }

        return at;
    }

    /// <summary>One Greek word of the running text of a verse: its line and its place among the line's words.</summary>
    private readonly record struct Token(int Line, int Word, string Bare);

    /// <summary>For each verse, each line's Greek words' Brenton ids, -1 where a word is not placed.</summary>
    internal static List<int[]>[] ProjectGreek(List<CatssVerse> verses, int book, ProductText bhsa, ProductText greek, Tally tally, HashSet<int> filled)
    {
        var order = greek.Verses.Where(v => v.Book == book).ToList();
        var index = order.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);
        var byAddress = new Dictionary<VerseKey, List<int>>();
        for (var i = 0; i < order.Count; i++)
        {
            foreach (var address in greek.Addresses(order[i]))
            {
                var at = new VerseKey(address.Book, address.Chapter, address.Verse);
                if (!byAddress.TryGetValue(at, out var list)) byAddress[at] = list = [];
                list.Add(i);
            }
        }

        var bare = new Dictionary<int, string>();
        string Bare(int id) => bare.TryGetValue(id, out var b) ? b : bare[id] = Letters(GreekLetters.Bare(greek.Words[id].Surface));

        var result = new List<int[]>[verses.Count];
        var claims = new Dictionary<int, List<(int Verse, int Line, int Word, bool Core)>>();
        for (var v = 0; v < verses.Count; v++)
        {
            var verse = verses[v];
            result[v] = [.. verse.Lines.Select(line => Enumerable.Repeat(-1, line.Greek.Count).ToArray())];
            var tokens = verse.Lines.SelectMany((line, l) => line.Greek.Select((word, w) => new Token(l, w, GreekBetacode.Bare(word)))).ToList();
            tally.GreekTokens += tokens.Count;
            if (tokens.Count == 0)
            {
                continue;
            }

            var core = new HashSet<int>();
            var own = new VerseKey(book, verse.Chapter, verse.Verse);
            foreach (var address in bhsa.Has(own) ? bhsa.Addresses(own) : [own])
            {
                if (byAddress.TryGetValue(new VerseKey(address.Book, address.Chapter, address.Verse), out var list)) core.UnionWith(list);
            }

            if (index.TryGetValue(own, out var same) && core.Count == 0) core.Add(same);
            foreach (var named in verse.Lines.SelectMany(line => line.Verses).Select(r => Named(r, verse.Chapter)).OfType<(int, int)>())
            {
                if (index.TryGetValue(new VerseKey(book, named.Item1, named.Item2), out var at)) core.Add(at);
            }

            var window = core.SelectMany(i => new[] { i - 1, i, i + 1 }).Where(i => i >= 0 && i < order.Count).Distinct().Order().ToList();
            var words = window.SelectMany(i => greek.InVerse(order[i]).Select(id => (Id: id, Core: core.Contains(i)))).ToList();
            foreach (var (t, w) in Match(tokens.Select(t => t.Bare).ToList(), words.Select(x => (Bare(x.Id), x.Core)).ToList()))
            {
                var token = tokens[t];
                if (!claims.TryGetValue(words[w].Id, out var list)) claims[words[w].Id] = list = [];
                list.Add((v, token.Line, token.Word, words[w].Core));
            }
        }

        foreach (var (id, list) in claims)
        {
            var keep = list.Count == 1 ? list : [.. list.Where(c => c.Core)];
            if (keep.Count == 1)
            {
                result[keep[0].Verse][keep[0].Line][keep[0].Word] = id;
                tally.GreekPlaced++;
            }
            else
            {
                tally.GreekContested += list.Count;
            }
        }

        // A word Brenton spells otherwise than Rahlfs, a name most often, stands where CATSS's does: as many words of
        // CATSS left between two placed words as Brenton words nobody placed between theirs, at most three, pair in order.
        var used = new HashSet<int>(result.SelectMany(verse => verse.SelectMany(line => line)).Where(id => id >= 0));
        foreach (var verse in result)
        {
            var places = verse.SelectMany((line, l) => line.Select((id, w) => (Line: l, Word: w, Id: id))).ToList();
            var last = -1;
            for (var i = 0; i < places.Count; i++)
            {
                if (places[i].Id < 0) continue;
                if (last >= 0 && i - last - 1 is var gap and > 0 and <= 3 && places[i].Id - places[last].Id - 1 == gap
                    && Enumerable.Range(places[last].Id + 1, gap).All(id => !used.Contains(id)))
                {
                    for (var k = 1; k <= gap; k++)
                    {
                        var place = places[last + k];
                        verse[place.Line][place.Word] = places[last].Id + k;
                        used.Add(places[last].Id + k);
                        filled.Add(places[last].Id + k);
                    }

                    tally.GreekFilled += gap;
                }

                last = i;
            }
        }

        return result;
    }

    /// <summary>A verse CATSS names as the Greek's own: <c>[6]</c> in the chapter, <c>[38.16]</c>, <c>[1:28]</c>, <c>[[29.20]]</c>.</summary>
    internal static (int Chapter, int Verse)? Named(string reference, int chapter)
    {
        var inner = reference.Trim('[', ']').Split(',')[0].Trim();
        var parts = inner.Split('.', ':');
        if (parts.Length == 1 && int.TryParse(parts[0], out var verse)) return (chapter, verse);
        if (parts.Length == 2 && int.TryParse(parts[0], out var c) && int.TryParse(parts[1], out verse)) return (c, verse);
        return null;
    }

    private static string Letters(string bare) => string.Concat(bare.Where(char.IsLetter));

    /// <summary>
    /// The pairs of a weighted longest common subsequence of CATSS's words and Brenton's: the same letters weigh most, a
    /// spelling apart (movable nu, ει for ι, a letter or two) less, and a word of a verse at the address a little more.
    /// </summary>
    internal static List<(int Token, int Word)> Match(List<string> tokens, List<(string Bare, bool Core)> words)
    {
        int n = tokens.Count, m = words.Count;
        var score = new double[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                var best = Math.Max(score[i + 1, j], score[i, j + 1]);
                var pair = Weight(tokens[i], words[j].Bare);
                if (pair > 0)
                {
                    best = Math.Max(best, score[i + 1, j + 1] + pair + (words[j].Core ? 0.01 : 0));
                }

                score[i, j] = best;
            }
        }

        var pairs = new List<(int, int)>();
        for (int i = 0, j = 0; i < n && j < m;)
        {
            var pair = Weight(tokens[i], words[j].Bare);
            if (pair > 0 && Math.Abs(score[i, j] - (score[i + 1, j + 1] + pair + (words[j].Core ? 0.01 : 0))) < 1e-9)
            {
                pairs.Add((i++, j++));
            }
            else if (score[i + 1, j] >= score[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return pairs;
    }

    internal static double Weight(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 3;
        if (Folded(a) == Folded(b)) return 2;
        if (Math.Min(a.Length, b.Length) >= 5 && Distance(a, b) <= Math.Max(a.Length, b.Length) / 5) return 1.5;
        return 0;
    }

    /// <summary>A spelling with the variations editions of the Septuagint differ in folded: movable nu, ει for ι, doubled consonants.</summary>
    internal static string Folded(string bare)
    {
        var s = bare.Replace("ει", "ι", StringComparison.Ordinal);
        if (s.Length > 2 && s[^1] == 'ν' && s[^2] is 'ε' or 'ι') s = s[..^1];
        var folded = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (folded.Length == 0 || folded[^1] != c) folded.Append(c);
        }

        return folded.ToString();
    }

    private static int Distance(string a, string b)
    {
        var row = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) row[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var diagonal = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var up = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                diagonal = up;
            }
        }

        return row[b.Length];
    }

    /// <summary>The verse's statements: each line's Hebrew with the Brenton words of its Greek, and the pluses and minuses Brenton shares.</summary>
    private static void Link(CatssVerse verse, List<int>[] hebrew, List<int[]> greek, List<Draft> drafts, Tally tally, HashSet<int> filled)
    {
        var lines = verse.Lines;
        var groups = new List<int>[lines.Count];
        for (var i = 0; i < lines.Count; i++) groups[i] = [.. greek[i].Where(id => id >= 0)];
        var taken = new HashSet<int>();

        // {..^X} and {..pX}: the Greek words X of a line whose Hebrew is {...} belong to this line's statement.
        for (var i = 0; i < lines.Count; i++)
        {
            foreach (var reference in lines[i].References.Where(r => r.Kind is '^' or 'p'))
            {
                var found = Find(lines, reference.Words, i, taken);
                if (found is var (line, start))
                {
                    for (var k = 0; k < reference.Words.Count; k++)
                    {
                        taken.Add(line);
                        if (greek[line][start + k] >= 0) groups[i].Add(greek[line][start + k]);
                    }
                }
            }
        }

        // ^^^ in the Hebrew column: the Hebrew of an earlier or later line whose Greek is ^^^, named in column b.
        var pending = Enumerable.Range(0, lines.Count).Where(i => lines[i].Kind == CatssHebrew.Words && lines[i].Greek.Count == 0
            && lines[i].Marks.Contains("^^^") && hebrew[i].Count > 0).ToList();
        var moved = new List<int>[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind != CatssHebrew.Transposed || lines[i].Retroversion is null) continue;
            var named = string.Concat(CatssReader.Tokens(lines[i].Retroversion!).Select(t => t.TrimStart('=', ';')).Where(CatssReader.IsHebrew)
                .Select(HebrewBetacode.Letters));
            var partner = pending.FirstOrDefault(p => string.Concat(lines[p].Hebrew.Select(HebrewBetacode.Letters)) == named, -1);
            if (partner >= 0)
            {
                pending.Remove(partner);
                moved[i] = hebrew[partner];
                tally.Transposed++;
            }
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var words = moved[i] ?? (line.Kind == CatssHebrew.Words ? hebrew[i] : []);
            var evidence = $"CATSS {verse.Book} {verse.Chapter}:{verse.Verse}, line {line.Line}" + (line.Retroversion is null ? string.Empty : $", column b {line.Retroversion}");
            if (line.Kind == CatssHebrew.Words && line.Marks.Contains("h:^") && line.Retroversion is not null)
            {
                // Its Greek renders the Hebrew of column b, which stands on a neighbouring line.
                tally.SkippedShifted++;
                continue;
            }

            if (line.Kind == CatssHebrew.Words && Read(line.Retroversion))
            {
                // Column b gives the Hebrew the translator read instead (B/DWD =B/)KY$: "in Achish" for "in David"): the Greek does not render this word.
                tally.SkippedRead++;
                continue;
            }

            if ((line.Minus || line.Kind == CatssHebrew.Plus) && (line.Marked("{x}") || line.Marked("h:{x}")))
            {
                tally.SkippedBroad++;
                continue;
            }

            if (groups[i].Any(filled.Contains))
            {
                evidence += ", a word Brenton spells otherwise placed between its neighbours";
            }

            if (words.Count > 0 && groups[i].Count > 0)
            {
                drafts.Add(new Draft(LinkRelation.Renders, groups[i], words, evidence));
                tally.Renders++;
            }
            else if (line.Kind == CatssHebrew.Plus && groups[i].Count > 0 && !taken.Contains(i))
            {
                drafts.Add(new Draft(LinkRelation.Expands, groups[i], [], evidence));
                tally.Pluses++;
            }
            else if (line.Minus && words.Count > 0)
            {
                if (Adjacent(greek, i))
                {
                    drafts.Add(new Draft(LinkRelation.Omits, [], words, evidence));
                    tally.Minuses++;
                }
                else
                {
                    tally.MinusesUnproved++;
                }
            }
        }
    }

    /// <summary>
    /// Whether column b names Hebrew the translator read instead of the Masoretic word: a retroversion (<c>=)XD</c>), a doubtful one
    /// (<c>=?</c>), another vocalisation (<c>=v</c>) or another number (<c>=+</c>); not a note on technique (<c>=%</c>), an
    /// etymological rendering (<c>=@</c>), a reconstructed name (<c>=:</c>) or the source of a plus (<c>=;</c>).
    /// </summary>
    internal static bool Read(string? retroversion) =>
        retroversion is { Length: > 0 } b && b[0] == '=' && (b.Length == 1 || b[1] is not ('%' or '@' or ':' or ';'));

    /// <summary>The nearest line from <paramref name="from"/> (Hebrew <c>{...}</c> first) whose running words begin with these words.</summary>
    private static (int Line, int Start)? Find(IReadOnlyList<CatssLine> lines, IReadOnlyList<string> words, int from, HashSet<int> taken)
    {
        var bare = words.Select(GreekBetacode.Bare).ToList();
        foreach (var i in Enumerable.Range(0, lines.Count).Where(i => i != from && lines[i].Kind == CatssHebrew.Elsewhere && !taken.Contains(i))
                     .OrderBy(i => Math.Abs(i - from)))
        {
            var line = lines[i].Greek.Select(GreekBetacode.Bare).ToList();
            for (var s = 0; s + bare.Count <= line.Count; s++)
            {
                if (bare.Select((b, k) => line[s + k] == b).All(x => x)) return (i, s);
            }
        }

        return null;
    }

    /// <summary>Whether the Brenton words placed just before and just after a line stand next to each other: Brenton has nothing there either.</summary>
    private static bool Adjacent(List<int[]> greek, int line)
    {
        var before = Enumerable.Range(0, line).Reverse().SelectMany(i => greek[i].Reverse()).FirstOrDefault(id => id >= 0, -1);
        var after = Enumerable.Range(line + 1, greek.Count - line - 1).SelectMany(i => greek[i]).FirstOrDefault(id => id >= 0, -1);
        if (before < 0 || after < 0) return false;
        var lineHasGreek = greek[line].Length > 0;
        return !lineHasGreek && after == before + 1;
    }
}
