using System.Text.Json;

namespace Essenthos.Core.Macula;

/// <summary>
/// One row of MACULA Hebrew's word table: a morph of the Westminster Leningrad Codex. <c>בְּ</c> and <c>רֵאשִׁית</c> are
/// two rows of <c>GEN 1:1!1</c>, and a pronoun suffix (<c>וֹ</c> of <c>מְלַאכְתּוֹ</c>) is a row of its own.
/// </summary>
/// <param name="Word">The orthographic word's index within the verse, from 1: every morph of one written word shares it.</param>
/// <param name="Text">The morph as MACULA prints it, pointed. Read to join it to BHSA by its letters, never stored.</param>
/// <param name="Referents">The ids of the morphs this one refers back to (<c>participantref</c>), each with its <c>o</c>.</param>
internal sealed record MaculaMorph(
    string Id,
    int Book,
    int Chapter,
    int Verse,
    int Word,
    string Class,
    string Pos,
    string Type,
    string Text,
    string Language,
    string Lemma,
    string Person,
    string Gender,
    string Number,
    string[] Referents)
{
    /// <summary>A pronoun suffix: the <c>וֹ</c> of <c>מְלַאכְתּוֹ</c> "his work", of <c>לוֹ</c> "to him".</summary>
    public bool IsSuffix => Pos == "suffix" && Type == "pronominal";

    /// <summary>A word standing for someone: a pronoun suffix, a personal pronoun (<c>הוּא</c>), a demonstrative (<c>זֶה</c>).</summary>
    public bool IsPronoun => IsSuffix || Pos == "pronoun";

    /// <summary>Person, gender and number as three letters, <c>3ms</c>; null where MACULA gives none.</summary>
    public string? Agreement =>
        Person.Length == 0 || Gender.Length == 0 || Number.Length == 0 ? null : $"{Person[0] switch { 'f' => '1', 's' => '2', _ => '3' }}{Gender[0]}{Number[0]}";
}

/// <summary>
/// MACULA Hebrew's word table, <c>Resources/MaculaHebrew/tsv/macula-hebrew.tsv</c>: a row per morph, columns read by name.
/// Only what places a morph, what it is and what it refers to is read; the Semantic Dictionary of Biblical Hebrew's columns
/// (<c>sdbh</c>, <c>lexdomain</c>, <c>contextualdomain</c>, <c>coredomain</c>), used by MACULA with permission and under no
/// licence anybody else can rely on, and the glossing layers are never touched.
/// </summary>
internal static class MaculaHebrewReader
{
    public const string Folder = "MaculaHebrew";

    public const string File = "macula-hebrew.tsv";

    /// <summary>The 39 books as MACULA addresses them, in canonical order: a code's place is its ordinal.</summary>
    private static readonly string[] Canon =
    [
        "GEN", "EXO", "LEV", "NUM", "DEU", "JOS", "JDG", "RUT", "1SA", "2SA", "1KI", "2KI", "1CH", "2CH", "EZR", "NEH", "EST",
        "JOB", "PSA", "PRO", "ECC", "SNG", "ISA", "JER", "LAM", "EZK", "DAN", "HOS", "JOL", "AMO", "OBA", "JON", "MIC", "NAM",
        "HAB", "ZEP", "HAG", "ZEC", "MAL",
    ];

    private static readonly string[] Required =
        ["xml:id", "ref", "class", "text", "lang", "lemma", "pos", "person", "gender", "number", "type", "participantref"];

    public static string Path(string folder) => System.IO.Path.Combine(folder, "tsv", File);

    public static List<MaculaMorph> ReadFile(string folder) => Read(System.IO.File.ReadLines(Path(folder)));

    public static List<MaculaMorph> Read(IEnumerable<string> lines)
    {
        Dictionary<string, int>? columns = null;
        var morphs = new List<MaculaMorph>(480_000);
        var line = 0;
        foreach (var raw in lines)
        {
            line++;
            var text = raw.TrimEnd('\r');
            if (columns is null)
            {
                var names = text.Split('\t');
                columns = names.Select((name, at) => (name, at)).ToDictionary(c => c.name, c => c.at, StringComparer.Ordinal);
                if (Required.FirstOrDefault(c => !columns.ContainsKey(c)) is { } missing)
                {
                    throw new InvalidDataException(
                        $"{File} has no column named \"{missing}\", so it is not the table this reader was written for. Re-run " +
                        "scripts/fetch-macula-hebrew.ps1, or read the new header and change the reader.");
                }

                continue;
            }

            if (text.Length == 0)
            {
                continue;
            }

            var fields = text.Split('\t');
            string Field(string name) => columns[name] < fields.Length ? fields[columns[name]] : "";
            var reference = Field("ref");
            var space = reference.IndexOf(' ');
            var colon = reference.IndexOf(':');
            var bang = reference.IndexOf('!');
            var book = space < 0 ? -1 : Array.IndexOf(Canon, reference[..space]);
            if (book < 0 || colon < space || bang < colon)
            {
                throw new InvalidDataException($"{File} line {line}: '{reference}' is not an address of the form GEN 1:1!1.");
            }

            morphs.Add(new MaculaMorph(
                Field("xml:id"),
                book + 1,
                int.Parse(reference[(space + 1)..colon]),
                int.Parse(reference[(colon + 1)..bang]),
                int.Parse(reference[(bang + 1)..]),
                Field("class"),
                Field("pos"),
                Field("type"),
                Field("text"),
                Field("lang") == "A" ? "arc" : "hbo",
                Field("lemma"),
                Field("person"),
                Field("gender"),
                Field("number"),
                [.. Field("participantref").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(id => "o" + id)]));
        }

        return morphs;
    }
}

/// <summary>
/// MACULA Hebrew's morphs placed on BHSA's words, by their letters.
///
/// <para>
/// MACULA is published over the Westminster Leningrad Codex a row per morph; BHSA writes the prefixes (<c>וְ</c>, <c>בְּ</c>,
/// <c>הַ</c>) as words of their own and keeps a pronoun suffix on its host (<c>מְלַאכְתּוֹ</c> is one word). Both are the
/// Leningrad Codex, so per verse the consonants of the one are the consonants of the other in 22,114 of 23,213 verses, and
/// a morph is the BHSA word whose letters hold its letters. Where a verse's consonants differ, nearly always where MACULA
/// writes the qere and BHSA the ketiv (GEN 8:17 הוצא against היצא), the two strings are aligned letter by letter and a morph
/// takes the word its matched letters fall in. A pronoun suffix none of whose letters match (the ketiv's ה against the
/// qere's ו of "his") goes with the morph before it in the same written word, its host.
/// </para>
///
/// <para>
/// Refused, so nothing rests on a guess: a morph whose letters fall in two BHSA words (the article written with
/// <c>לזה</c>, 70 in identical verses), a verse whose consonants agree in less than nine letters of ten, a verse one side
/// does not have.
/// </para>
/// </summary>
internal static class MaculaHebrewJoin
{
    /// <summary>A BHSA word as the join reads it: its place and its letters.</summary>
    internal sealed record Word(long Id, int Book, int Chapter, int Verse, int Position, string Surface);

    /// <summary>How much of a differing verse's letters must align for its morphs to be placed.</summary>
    internal const double Agreement = 0.9;

    /// <summary>Each morph's BHSA word, by index into <paramref name="morphs"/>; -1 where it is refused. And the verses aligned past a difference.</summary>
    internal static (long[] Placed, HashSet<(int, int, int)> Differing) Place(IReadOnlyList<MaculaMorph> morphs, IReadOnlyList<Word> words)
    {
        var placed = new long[morphs.Count];
        Array.Fill(placed, -1L);
        var differing = new HashSet<(int, int, int)>();
        var ours = words.GroupBy(w => (w.Book, w.Chapter, w.Verse))
            .ToDictionary(g => g.Key, g => g.OrderBy(w => w.Position).ToList());
        var theirs = Enumerable.Range(0, morphs.Count).GroupBy(i => (morphs[i].Book, morphs[i].Chapter, morphs[i].Verse));
        foreach (var verse in theirs)
        {
            if (!ours.TryGetValue(verse.Key, out var held))
            {
                continue;
            }

            var indices = verse.ToList();
            var (mine, mineAt) = Letters(indices.Select(i => morphs[i].Text));
            var (bhsa, bhsaAt) = Letters(held.Select(w => w.Surface));
            int[] to;
            if (mine == bhsa)
            {
                to = [.. Enumerable.Range(0, mine.Length)];
            }
            else
            {
                differing.Add(verse.Key);
                to = Align(mine, bhsa);
                if (to.Count(t => t >= 0) < Agreement * Math.Max(mine.Length, bhsa.Length))
                {
                    continue;
                }
            }

            for (var k = 0; k < indices.Count; k++)
            {
                var hit = Enumerable.Range(mineAt[k], mineAt[k + 1] - mineAt[k])
                    .Select(c => to[c]).Where(c => c >= 0).Select(c => Owner(bhsaAt, c)).Distinct().ToList();
                var morph = morphs[indices[k]];
                if (hit.Count == 1)
                {
                    placed[indices[k]] = held[hit[0]].Id;
                }
                else if (hit.Count == 0 && morph.IsSuffix && k > 0 && morphs[indices[k - 1]].Word == morph.Word)
                {
                    placed[indices[k]] = placed[indices[k - 1]];
                }
            }
        }

        return (placed, differing);
    }

    /// <summary>The consonants of a run of words, final forms written as the others, and where each word's begin.</summary>
    internal static (string Letters, int[] Starts) Letters(IEnumerable<string> words)
    {
        var letters = new System.Text.StringBuilder();
        var starts = new List<int>();
        foreach (var word in words)
        {
            starts.Add(letters.Length);
            foreach (var ch in word)
            {
                if (ch is >= 'א' and <= 'ת')
                {
                    letters.Append(ch switch { 'ך' => 'כ', 'ם' => 'מ', 'ן' => 'נ', 'ף' => 'פ', 'ץ' => 'צ', _ => ch });
                }
            }
        }

        starts.Add(letters.Length);
        return (letters.ToString(), [.. starts]);
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

    /// <summary>Each letter of <paramref name="a"/>'s partner in <paramref name="b"/> by the longest common subsequence, -1 where none.</summary>
    internal static int[] Align(string a, string b)
    {
        var table = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                table[i, j] = a[i] == b[j] ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        var to = new int[a.Length];
        Array.Fill(to, -1);
        for (int i = 0, j = 0; i < a.Length && j < b.Length;)
        {
            if (a[i] == b[j])
            {
                to[i++] = j++;
            }
            else if (table[i + 1, j] >= table[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return to;
    }
}

/// <summary>
/// What MACULA Hebrew gives a BHSA word: a parsing (<c>word_parsing</c>) and the words a pronoun in it refers back to
/// (<c>word_referent</c>).
///
/// <para>
/// **A pronoun inside a word.** 42,689 of MACULA's 51,759 referring morphs are pronoun suffixes, which BHSA keeps on their
/// host: <c>מְלַאכְתּוֹ</c> "his work" refers to God by its <c>וֹ</c>. The rows go on the host word, and its parsing says
/// so (<c>"refers": "suffix"</c>), so the reader can show the reference on the translation's word rendering the suffix
/// ("his") and not on the one rendering the host ("work"). A word whose pronoun is the whole of it (<c>הוּא</c>) refers as
/// a Greek pronoun does.
/// </para>
///
/// <para>
/// **Only pronouns refer here**: suffixes, personal pronouns and demonstratives. MACULA's few references on nouns, verbs
/// and adjectives (about 470) are left, as the reader leaves them on the Greek. A chain is followed here, in MACULA's own
/// morphs: a morph referring to a pronoun refers to what that pronoun refers to, so a reference to a suffix inside another
/// word never stands for its host. All or nothing: a morph whose references do not all end at a morph placed on a BHSA word
/// that is not a pronoun gives no row at all, as the reader would give it no record. A BHSA word holding two referring
/// morphs (7) gives none.
/// </para>
/// </summary>
internal static class MaculaHebrewAnnotation
{
    /// <summary>CC BY 4.0's one condition, verbatim from LICENSE.md, carried by every row.</summary>
    internal const string Source = "MACULA Hebrew Linguistic Datasets, available at https://github.com/Clear-Bible/macula-hebrew/";

    /// <summary>How many references a chain through pronouns is followed through.</summary>
    private const int Steps = 8;

    /// <param name="Word">The BHSA word.</param>
    /// <param name="Lemma">The lemma of its main morph (the first that is no suffix).</param>
    /// <param name="Morphology">The parsing's JSON.</param>
    /// <param name="Note">Where the verse's letters were aligned past a difference: what MACULA prints for the word.</param>
    internal sealed record Parsing(long Word, string Lemma, string Morphology, string? Note);

    /// <summary>The parsings and the references of every BHSA word MACULA's morphs were placed on.</summary>
    internal static (List<Parsing> Parsings, List<(long Word, long Referent)> Referents) Of(
        IReadOnlyList<MaculaMorph> morphs, IReadOnlyList<MaculaHebrewJoin.Word> words)
    {
        var (placed, differing) = MaculaHebrewJoin.Place(morphs, words);
        var byWord = new Dictionary<long, List<int>>();
        for (var i = 0; i < morphs.Count; i++)
        {
            if (placed[i] >= 0)
            {
                (byWord.TryGetValue(placed[i], out var list) ? list : byWord[placed[i]] = []).Add(i);
            }
        }

        var at = new Dictionary<string, int>(morphs.Count, StringComparer.Ordinal);
        for (var i = 0; i < morphs.Count; i++)
        {
            at[morphs[i].Id] = i;
        }

        var surfaces = words.ToDictionary(w => w.Id, w => w);
        var parsings = new List<Parsing>(byWord.Count);
        var referents = new List<(long, long)>();
        foreach (var word in words.Select(w => w.Id).Where(byWord.ContainsKey))
        {
            var held = byWord[word].Where(i => MaculaHebrewJoin.Letters([morphs[i].Text]).Letters.Length > 0 || morphs[i].IsSuffix).ToList();
            if (held.Count == 0)
            {
                continue;
            }

            var main = held.FirstOrDefault(i => !morphs[i].IsSuffix, held[0]);
            var suffix = held.Where(i => morphs[i].IsSuffix).Select(i => morphs[i].Agreement).FirstOrDefault(a => a is not null);
            var referring = held.Where(i => morphs[i].IsPronoun && morphs[i].Referents.Length > 0).ToList();
            var part = referring.Count == 1 && held.Count > 1 ? "suffix" : null;
            var features = new Dictionary<string, string>(StringComparer.Ordinal) { ["class"] = morphs[main].Class, ["pos"] = morphs[main].Pos };
            if (morphs[main].Type.Length > 0)
            {
                features["type"] = morphs[main].Type;
            }

            features["language"] = morphs[main].Language;
            if (suffix is not null)
            {
                features["suffix"] = suffix;
            }

            if (part is not null)
            {
                features["refers"] = part;
            }

            var w = surfaces[word];
            var key = (w.Book, w.Chapter, w.Verse);
            var theirs = string.Concat(byWord[word].Select(i => morphs[i].Text));
            var note = differing.Contains(key) && MaculaHebrewJoin.Letters([theirs]).Letters != MaculaHebrewJoin.Letters([w.Surface]).Letters
                ? $"this corpus prints {w.Surface} where MACULA prints {theirs}"
                : null;
            parsings.Add(new Parsing(word, morphs[main].Lemma, JsonSerializer.Serialize(features), note));

            if (referring.Count != 1)
            {
                continue;
            }

            if (Resolved(morphs, at, placed, referring[0]) is { } targets)
            {
                referents.AddRange(targets.Select(t => placed[t]).Where(t => t != word).Distinct().Select(t => (word, t)));
            }
        }

        return (parsings, referents);
    }

    /// <summary>
    /// The morphs one morph refers to, a pronoun on the way followed to what it refers to; null where any branch ends at a
    /// morph not placed on a BHSA word, at a pronoun referring to nothing, past <see cref="Steps"/>, or at an id that is no
    /// row of the table (1,372 of the 59,231 references at the commit read, such as GEN 6:3's o010060030063, where the
    /// table's rows are 061, 061ה and 062: which morph was meant is not guessed).
    /// </summary>
    private static List<int>? Resolved(IReadOnlyList<MaculaMorph> morphs, Dictionary<string, int> at, long[] placed, int morph)
    {
        var found = new List<int>();
        var seen = new HashSet<int> { morph };
        var queue = new Queue<(int Morph, int Step)>([(morph, 0)]);
        while (queue.Count > 0)
        {
            var (from, step) = queue.Dequeue();
            foreach (var id in morphs[from].Referents)
            {
                if (!at.TryGetValue(id, out var to))
                {
                    return null;
                }

                if (!seen.Add(to))
                {
                    continue;
                }

                if (!morphs[to].IsPronoun)
                {
                    if (placed[to] < 0)
                    {
                        return null;
                    }

                    found.Add(to);
                }
                else if (morphs[to].Referents.Length > 0 && step + 1 < Steps)
                {
                    queue.Enqueue((to, step + 1));
                }
                else
                {
                    return null;
                }
            }
        }

        return found.Count > 0 ? found : null;
    }
}
