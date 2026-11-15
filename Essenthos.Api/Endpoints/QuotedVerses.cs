using System.Text.RegularExpressions;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The verses a BibleData event's description quotes, and whatever of the description is the
/// compiler's own words.
///
/// <para>
/// BibleData writes most descriptions as Bible quotations closed by a citation: <em>So Abram went
/// forth as the LORD had spoken to him… (GEN 12:4)</em>. The words are the New American Standard
/// Bible's, which is The Lockman Foundation's copyright and not under BibleData's CC BY (PRB-1678).
/// The owner's ruling of 2026-11-15: a quotation is not served; its verses are, from the site's own
/// texts in the reader's language. What the compiler wrote himself (<em>Ahaz's sole reign began when
/// his co-regency ended</em>, a bracketed working such as <em>[Terah was 205 when he died…]</em>)
/// stays.
/// </para>
///
/// <para>
/// The rule: every citation in the text closes the quotation before it, wherever it stands — inside
/// a line, at its end, or unclosed at the very end (<em>(1KI 2:10</em>); a line opening a square
/// bracket starts the compiler's working, which runs to the line closing it; what follows the last
/// citation is the compiler's. Checked against every one of the 572 descriptions by word overlap with
/// the American Standard Version the NASB revises (r126, 2026-11-15): the exceptions below are the
/// whole of what the rule gets wrong.
/// </para>
/// </summary>
internal static partial class QuotedVerses
{
    /// <summary>Where a description is the compiler's sentence ending in a citation, not a quotation.</summary>
    private static readonly HashSet<string> Prose = new(StringComparer.Ordinal)
    {
        // "All of Creation was formed by G-d's spoken word. (GEN 1:1-31)"
        "creation",
        // "Jehoshaphat was a regent of Judah under his father… the FIFTH YEAR of Joram, the son of Ahab (2KI 8:16)."
        "beginjehoshaphat3regency",
    };

    /// <summary>A quotation the source gives no citation for: the verse it quotes, read by hand.</summary>
    private static readonly Dictionary<string, string> Uncited = new(StringComparer.Ordinal)
    {
        // "When Esau was FORTY YEARS OLD he married Judith the daughter of Beeri the Hittite…"
        ["esaumarriedjudithandbasemath"] = "GEN 26:34",
    };

    /// <summary>
    /// A citation that names another verse than the one quoted, with the verse quoted: the words of
    /// 2 Chronicles 15:1-2 cited as 1 Chronicles, of 2 Chronicles 11:17 cited as 11:7.
    /// </summary>
    private static readonly Dictionary<(string Slug, string Cited), string> Miscited = new()
    {
        [("azariahsprophecy", "1CH 15:1-2")] = "2CH 15:1-2",
        [("judahwalkswaydavidsolomonbegin", "2CH 11:7")] = "2CH 11:17",
        [("judahwalkswaydavidsolomonend", "2CH 11:7")] = "2CH 11:17",
    };

    /// <summary>
    /// The NASB quotations inside BibleData's notes, each by its first words and its length in characters
    /// (so the quotation itself is not carried here), each followed in the notes by the citation that stays. The notes are the compiler's reasoning and mostly his own words; these are the
    /// spans that are the Bible's (r126: every span closed by a citation in the 572 events' and their
    /// dates' notes checked against the ASV by word overlap, then read by hand; the compiler's paraphrases,
    /// <em>Caleb was 40 years old (JOS 14:7)</em>, stay).
    /// </summary>
    private static readonly Dictionary<string, (string Opening, int Length)[]> NoteQuotations = new(StringComparer.Ordinal)
    {
        ["beginahaziah2reign"] = [("Ahaziah was TWENTY-TWO", 52)],
        ["beginamaziah1reign"] = [("Amaziah was TWENTY-FIVE", 53), ("He was TWENTY-FIVE", 51)],
        // The second is 2 Kings 3:1, quoted without a citation.
        ["beginjehoram1regency"] = [("So Ahaziah died", 203), ("Now JEHORAM the", 141)],
        ["beginjehoshaphat3regency"] = [("In the THIRTY-NINTH", 164)],
        ["beginjehoshaphat3reign"] = [("Jehoshaphat was THIRTY-FIVE", 58)],
        ["beginsolomon1reign"] = [("Now it came", 257)],
        ["destructionofsolomonstemple"] = [("But because our", 202)],
        ["elijahsletter"] = [("Now it came", 208)],
        ["endasa1reign"] = [("So Asa slept", 80)],
        ["endassyrianinvasion"] = [("Then the king", 99)],
        ["endathaliah1reign"] = [("So he was", 104)],
        ["jacobdepartedtopaddanaram"] = [("Now Joseph was", 165), ("For the famine", 138)],
        ["shishakattacksjerusalem"] = [("And it came", 149)],
        ["beginjotham2solereign"] = [("\"He was twenty-five", 50)],
        ["beginjotham2coregencywithazariah"] = [("\"He was twenty-five", 50)],
        ["beginhezekiah1reign"] = [("\"He was twenty-five", 50)],
        ["beginahaz1coregencywithjotham"] = [("\"Ahaz was twenty", 47)],
    };

    /// <summary>
    /// A BibleData note without the NASB's words it quotes, the citation left where the quotation stood;
    /// the note unchanged everywhere else. A note so changed has no stored translation: its English is new.
    /// </summary>
    public static string? WithoutQuotations(string slug, string? source, string? notes)
    {
        if (notes is null || source is null || !source.StartsWith("BibleData", StringComparison.Ordinal)
            || !NoteQuotations.TryGetValue(slug, out var quotations))
        {
            return notes;
        }

        var kept = notes;
        foreach (var (opening, length) in quotations)
        {
            var at = kept.IndexOf(opening, StringComparison.Ordinal);
            if (at >= 0 && at + length <= kept.Length)
            {
                kept = kept.Remove(at, length);
            }
        }

        return kept == notes ? notes : DoubleSpace().Replace(kept, " ").Trim();
    }

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex DoubleSpace();

    /// <summary>BibleData's book codes; anything else is read as a book name.</summary>
    private static readonly Dictionary<string, int> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GEN"] = 1, ["EXO"] = 2, ["LEV"] = 3, ["NUM"] = 4, ["DEU"] = 5, ["JOS"] = 6, ["JDG"] = 7, ["RUT"] = 8,
        ["1SA"] = 9, ["2SA"] = 10, ["1KI"] = 11, ["2KI"] = 12, ["1CH"] = 13, ["2CH"] = 14, ["EZR"] = 15,
        ["NEH"] = 16, ["EST"] = 17, ["JOB"] = 18, ["PSA"] = 19, ["PRO"] = 20, ["ECC"] = 21, ["SNG"] = 22,
        ["ISA"] = 23, ["JER"] = 24, ["LAM"] = 25, ["EZE"] = 26, ["EZK"] = 26, ["DAN"] = 27, ["HOS"] = 28,
        ["JOE"] = 29, ["JOL"] = 29, ["AMO"] = 30, ["OBA"] = 31, ["JON"] = 32, ["MIC"] = 33, ["NAH"] = 34,
        ["HAB"] = 35, ["ZEP"] = 36, ["HAG"] = 37, ["ZEC"] = 38, ["MAL"] = 39, ["MAT"] = 40, ["MRK"] = 41,
        ["LUK"] = 42, ["JHN"] = 43, ["ACT"] = 44, ["ROM"] = 45, ["GAL"] = 48, ["HEB"] = 58,
    };

    /// <summary>The most verses one description is answered with: Genesis 1 whole is 31.</summary>
    private const int MostVerses = 40;

    private const string Book = @"(?:[1-3] ?[A-Z]{2,3}|[A-Z]{3}|Exodus|Ezra|Nehemiah)";

    private const string Refs =
        @"\d+:\d+[ab]?(?:\s*[-–]\s*\d+(?::\d+)?[ab]?)?(?:\s*,\s*\d+(?::\d+)?[ab]?(?:\s*[-–]\s*\d+(?::\d+)?[ab]?)?)*";

    [GeneratedRegex(@"\(\s*(?<cite>" + Book + " " + Refs + @")\s*(?:\)|$)|(?<![\w(])(?<cite>" + Book + " " + Refs + @")\s*$")]
    private static partial Regex Citation();

    [GeneratedRegex(@"^\s*(?<book>[1-3]? ?[A-Za-z]+)\.? (?<refs>.+)$")]
    private static partial Regex Cited();

    /// <summary>
    /// What a description quotes and what of it is the compiler's. Only BibleData's descriptions are
    /// read: Ussher's Annals are his own words, the world layer's are Wikidata's.
    /// </summary>
    /// <returns>
    /// The cited passages, each a run of verses (the last may stand in a later chapter), and the
    /// compiler's words, or null where the description is all quotation. With no quotation the
    /// description comes back whole.
    /// </returns>
    public static Reading Read(string slug, string? source, string? description)
    {
        if (description is not { Length: > 0 } || source is null || !source.StartsWith("BibleData", StringComparison.Ordinal)
            || Prose.Contains(slug))
        {
            return new Reading([], description);
        }

        if (Uncited.TryGetValue(slug, out var own))
        {
            return new Reading(Passages(own), null);
        }

        var compilers = new List<string>();
        var body = new List<string>();
        var bracketed = false;
        foreach (var line in description.Split('\n'))
        {
            var trimmed = line.Trim();
            if (bracketed || trimmed.StartsWith('['))
            {
                compilers.Add(trimmed);
                bracketed = !trimmed.EndsWith(']');
            }
            else
            {
                body.Add(line);
            }
        }

        var text = string.Join('\n', body);
        var passages = new List<Passage>();
        var at = 0;
        foreach (Match match in Citation().Matches(text))
        {
            var cited = match.Groups["cite"].Value.Trim();
            passages.AddRange(Passages(Miscited.GetValueOrDefault((slug, cited), cited)));
            at = match.Index + match.Length;
        }

        if (passages.Count == 0)
        {
            return new Reading([], description);
        }

        var rest = text[at..].Trim(' ', '.', '\n', '\r', '\t');
        if (rest.Length > 0)
        {
            compilers.Insert(0, rest);
        }

        return new Reading(passages, compilers.Count == 0 ? null : string.Join('\n', compilers));
    }

    /// <summary>A citation such as <c>1KI 19:15-16, 19</c> or <c>1KI 14:31-15:1</c> as runs of verses.</summary>
    internal static List<Passage> Passages(string citation)
    {
        var passages = new List<Passage>();
        foreach (var part in citation.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Cited().Match(part);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["book"].Value.Replace(" ", "", StringComparison.Ordinal);
            var book = Codes.TryGetValue(name, out var coded) ? coded : BookReferences.ResolveOrdinal(match.Groups["book"].Value);
            if (book is not { } ordinal)
            {
                continue;
            }

            int? chapter = null;
            foreach (var piece in match.Groups["refs"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var halves = piece.Split(['-', '–'], 2, StringSplitOptions.TrimEntries);
                var (fromChapter, fromVerse) = Verse(halves[0], chapter);
                if (fromChapter is not { } opens || fromVerse is not { } first)
                {
                    continue;
                }

                chapter = opens;
                var (toChapter, toVerse) = halves.Length > 1 ? Verse(halves[1], opens) : (opens, first);
                if (toChapter is not { } closes || toVerse is not { } last || (closes, last).CompareTo((opens, first)) < 0)
                {
                    passages.Add(new Passage(ordinal, opens, first, opens, first));
                    continue;
                }

                chapter = closes;
                passages.Add(new Passage(ordinal, opens, first, closes, last));
            }
        }

        return passages;
    }

    /// <summary><c>19:15</c>, <c>15</c> (in the chapter before it) or <c>31b</c> (a half verse is the verse).</summary>
    private static (int? Chapter, int? Verse) Verse(string written, int? chapter)
    {
        var cut = written.TrimEnd('a', 'b');
        var colon = cut.IndexOf(':');
        if (colon > 0)
        {
            return int.TryParse(cut[..colon], out var c) && int.TryParse(cut[(colon + 1)..], out var v) ? (c, v) : (null, null);
        }

        return int.TryParse(cut, out var only) ? (chapter, only) : (null, null);
    }

    /// <summary>
    /// The verses of these passages, one apiece, in the order cited and none twice. A passage that runs
    /// into a later chapter takes the end of its first chapter from <paramref name="lastVerse"/>, the
    /// last verse the frame holds in that chapter; without it only its first verse is taken there.
    /// </summary>
    public static List<VerseRefResponse> Verses(IEnumerable<Passage> passages, Func<int, int, int?>? lastVerse = null)
    {
        var verses = new List<VerseRefResponse>();
        var seen = new HashSet<(int, int, int)>();
        foreach (var passage in passages)
        {
            for (var chapter = passage.Chapter; chapter <= passage.LastChapter; chapter++)
            {
                var first = chapter == passage.Chapter ? passage.Verse : 1;
                var last = chapter == passage.LastChapter
                    ? passage.LastVerse
                    : lastVerse?.Invoke(passage.Book, chapter) ?? first;
                for (var verse = first; verse <= last && verses.Count < MostVerses; verse++)
                {
                    if (seen.Add((passage.Book, chapter, verse)) && BookReferences.At(passage.Book, chapter, verse) is { } at)
                    {
                        verses.Add(at);
                    }
                }
            }
        }

        return verses;
    }

    /// <summary>A run of verses one citation names: from a verse to a verse, maybe in a later chapter.</summary>
    internal sealed record Passage(int Book, int Chapter, int Verse, int LastChapter, int LastVerse);

    /// <param name="Passages">What the description quotes, as the source cites it; empty where it quotes nothing.</param>
    /// <param name="Remainder">The compiler's own words, or null where the description was all quotation.</param>
    internal sealed record Reading(IReadOnlyList<Passage> Passages, string? Remainder);
}
