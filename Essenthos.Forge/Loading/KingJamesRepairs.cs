using System.Text.RegularExpressions;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Usfm;
using Essenthos.Core.XmlBible;

namespace Essenthos.Core.Loading;

/// <summary>
/// What bible4u's King James gets wrong against the standardised 1769 text eBible publishes, read
/// from that text verse by verse.
///
/// <para>
/// The two agree on the address of all 31,102 verses and on 22,154 of them character for
/// character. Three things of the rest are taken from the 1769, and nothing else: its spelling
/// (<em>asswaged</em>, <em>Cæsar</em>), its hyphens in names and its pilcrows stay as bible4u has
/// them, because those are the modern text's conventions and not faults of the file.
/// </para>
///
/// <list type="bullet">
/// <item>
/// <b>The capitals.</b> The 1769 sets the divine name in small capitals — LORD for the
/// tetragrammaton, Lord for adonai, GOD for the name read as elohim — and the inscriptions it prints
/// in capitals the same way: HOLINESS TO THE LORD, MENE, MENE, TEKEL, UPHARSIN. The file lowercases
/// all of it. Wherever the 1769 prints a word of two or more letters wholly in capitals and the file
/// prints the same letters otherwise, the word takes the 1769's capitals: 6,949 words in 5,830
/// verses, which the Strong-tagged Zefania copy capitalises the same way at all but 19 of the
/// places compared. Where the 1769 prints <em>LORD’s</em>, the file printed <em>Lord 's</em> 98 times,
/// a word of its own made of the <em>s</em>; that is one word again, the name's.
/// </item>
/// <item>
/// <b>Twenty-seven readings</b> the file garbles — a word dropped, doubled or moved, and a stray
/// quotation mark — each written out in <see cref="Readings"/> with the 1769 reading, which the
/// Zefania copy reads too.
/// </item>
/// <item>
/// <b>The fourteen subscriptions</b> the 1769 prints under the Pauline epistles and Hebrews —
/// <em>Written to the Romans from Corinthus, and sent by Phebe servant of the church at
/// Cenchrea.</em> They are not a verse and are given no number: they close the epistle's last verse,
/// which is where the Geneva, Tyndale, the Kulish Bible and the Stephanus Greek beside it already
/// carry theirs.
/// </item>
/// </list>
///
/// <para>
/// Silent where the 1769 text has not been fetched, as the psalm superscriptions are: nothing is
/// corrected against a text nobody can read.
/// </para>
/// </summary>
internal static class KingJamesRepairs
{
    /// <summary>What the text's row says about the words taken from the 1769, on a cold load and on a warm one.</summary>
    public const string Note =
        "Corrected by Essenthos against eBible's eng-kjv2006, the standardised 1769 text, which states Public "
        + "Domain: the divine name and the inscriptions the 1769 prints in capitals, which bible4u's file "
        + "lowercases and whose possessive it prints apart; 27 verses the file garbles; and the fourteen "
        + "epistle subscriptions it omits.";

    public static readonly TextPartSource Source = LostPsalmOpenings.KingJamesSource with
    {
        Covers = "The capitals of the divine name and of the inscriptions, 27 readings the file the text is loaded "
                 + "from garbles, and the subscriptions of fourteen epistles, which it does not print.",
    };

    /// <summary>The epistles the 1769 closes with a subscription: Romans to Hebrews.</summary>
    private const int FirstSubscribed = 45;

    private const int LastSubscribed = 58;

    private const int FewestCapitals = 2;

    /// <param name="Digitised">What the file prints, once in the verse.</param>
    /// <param name="Printed">What the 1769 prints in its place, in the file's own spelling.</param>
    private sealed record Reading(int Book, int Chapter, int Verse, string Digitised, string Printed);

    /// <summary>
    /// The verses the file garbles, found by comparing every verse with the 1769 word for word and
    /// read one by one: only where the file has a word the 1769 has not, lacks one it has, or has
    /// two in another order. A word spelt another way is the modern text's spelling and is not here.
    /// </summary>
    private static readonly Reading[] Readings =
    [
        new(1, 5, 3, "likeness, and after his image", "likeness, after his image"),
        new(1, 27, 43, "obey my voice; arise", "obey my voice; and arise"),
        new(1, 29, 33, "hath heard I was hated", "hath heard that I was hated"),
        new(2, 38, 22, "the son Uri", "the son of Uri"),
        new(3, 22, 13, "there shall be no stranger", "there shall no stranger"),
        new(3, 26, 11, "And I set my tabernacle", "And I will set my tabernacle"),
        new(6, 7, 2, "on the east of", "on the east side of"),
        new(9, 10, 27, "brought no presents", "brought him no presents"),
        new(12, 4, 34, "his hands: and stretched", "his hands: and he stretched"),
        new(12, 18, 10, "that is in the ninth year", "that is the ninth year"),
        new(14, 10, 2, "whither he fled", "whither he had fled"),
        new(14, 18, 29, "and I will go to the battle", "and will go to the battle"),
        new(17, 7, 9, "who spoken good", "who had spoken good"),
        new(17, 8, 5, "if I have favour", "if I have found favour"),
        new(23, 22, 2, "a tumultuous city, joyous city", "a tumultuous city, a joyous city"),
        new(24, 30, 11, "yet I will not make", "yet will I not make"),
        new(26, 31, 4, "and sent her little rivers", "and sent out her little rivers"),
        new(27, 11, 27, "both of these kings", "both these kings"),
        new(29, 2, 31, "the great and terrible day", "the great and the terrible day"),
        new(29, 3, 18, "come forth out of the house", "come forth of the house"),
        new(41, 12, 7, "shall be ours.'", "shall be ours."),
        new(44, 10, 24, "and he had called together", "and had called together"),
        new(44, 10, 41, "chosen before God", "chosen before of God"),
        new(44, 13, 22, "he gave their testimony", "he gave testimony"),
        new(45, 11, 6, "then it is no more grace", "then is it no more grace"),
        new(46, 9, 1, "Am I am not an apostle?", "Am I not an apostle?"),
        new(62, 2, 23, "the Father: he that acknowledgeth", "the Father: but he that acknowledgeth"),
    ];

    public static TextRepairs Read(XmlBible.XmlBible bible, string folder)
    {
        var (edition, subscriptions) = Edition(folder);
        if (edition.Count == 0)
        {
            return TextRepairs.None(Bible4uTextSource.KingJames);
        }

        var readings = Readings.ToDictionary(r => (r.Book, r.Chapter, r.Verse));
        var repairs = new List<VerseRepair>();

        foreach (var book in bible.Books)
        {
            var ordinal = Bible4uTextSource.Canonical(book, Bible4uTextSource.KingJames);
            foreach (var chapter in book.Chapters)
            {
                foreach (var verse in chapter.Verses)
                {
                    var address = (ordinal, chapter.CNumber, verse.VNumber);
                    if (!edition.TryGetValue(address, out var printed))
                    {
                        throw new InvalidOperationException(
                            $"The 1769 text has no {BookReferences.Name(ordinal)} {chapter.CNumber}:{verse.VNumber}, " +
                            "which bible4u's King James prints. The two were measured to agree on every address; " +
                            "one of the files is not the one they were measured on. Run scripts/fetch-ebible.ps1 " +
                            "-Only KingJames2006.");
                    }

                    var text = SplitPossessive.Replace(verse.Text, string.Empty);
                    if (readings.TryGetValue(address, out var reading))
                    {
                        text = Read(reading, text, printed);
                    }

                    var tokens = Capitalised(VerseWords.Parse(text), printed);
                    var repaired = Rebuild(tokens);
                    if (subscriptions.TryGetValue(address, out var subscription))
                    {
                        repaired = $"{repaired.TrimEnd()} {subscription}";
                    }

                    if (repaired != Rebuild(VerseWords.Parse(verse.Text)))
                    {
                        repairs.Add(new VerseRepair(ordinal, chapter.CNumber, verse.VNumber, verse.Text, repaired));
                    }
                }
            }
        }

        return new TextRepairs(Bible4uTextSource.KingJames, repairs, Note, Source);
    }

    /// <summary>
    /// A reading replaces its words once, and is refused where the file does not print them or the
    /// 1769 does not print what replaces them — then one of the two is not the text it was read against.
    /// </summary>
    private static string Read(Reading reading, string text, IReadOnlyList<string> printed)
    {
        var at = text.IndexOf(reading.Digitised, StringComparison.Ordinal);
        var place = $"{BookReferences.Name(reading.Book)} {reading.Chapter}:{reading.Verse}";
        if (at < 0 || text.IndexOf(reading.Digitised, at + 1, StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException(
                $"bible4u's King James does not print \"{reading.Digitised}\" once at {place}, which is the reading " +
                "corrected there. The file is not the one the readings were checked against; nothing was corrected.");
        }

        var words = VerseWords.Parse(reading.Printed).Select(token => Key(token.Word)).Where(word => word.Length > 0).ToList();
        var theirs = printed.Select(Key).ToList();
        if (!Enumerable.Range(0, Math.Max(0, theirs.Count - words.Count + 1))
                .Any(start => theirs.Skip(start).Take(words.Count).SequenceEqual(words)))
        {
            throw new InvalidOperationException(
                $"The 1769 text does not read \"{reading.Printed}\" at {place}, which is what the file is corrected " +
                "to there. Read the verse in both before changing either.");
        }

        return string.Concat(text.AsSpan(0, at), reading.Printed, text.AsSpan(at + reading.Digitised.Length));
    }

    /// <summary>
    /// The file's words with the 1769's capitals, where the 1769 prints a word wholly in capitals and
    /// the file prints the same letters otherwise. The two verses are aligned word for word first,
    /// so a word the two spell differently, or one only one of them has, moves nothing.
    /// </summary>
    private static List<VerseToken> Capitalised(List<VerseToken> tokens, IReadOnlyList<string> printed)
    {
        if (!printed.Any(word => Shouted(Bare(word))))
        {
            return tokens;
        }

        var mine = tokens.Select((token, at) => (token, at)).Where(pair => pair.token.Word.Length > 0).ToList();
        var matched = Lcs([.. mine.Select(pair => Key(pair.token.Word))], [.. printed.Select(Key)]);
        var result = tokens.ToList();
        foreach (var (i, j) in matched)
        {
            var (token, at) = mine[i];
            var theirs = Bare(printed[j]);
            if (Shouted(theirs) && theirs.Length == token.Word.Length && token.Word != theirs)
            {
                result[at] = token with { Word = WithCapitals(token.Word, theirs) };
            }
        }

        return result;
    }

    /// <summary>
    /// Wholly in capitals, up to the apostrophe: eBible marks the small capitals of <em>LORD’s</em>
    /// on the name and leaves the possessive after it in lower case.
    /// </summary>
    private static bool Shouted(string word)
    {
        var name = word.Split(Apostrophes)[0];
        return name.Count(char.IsLetter) >= FewestCapitals && name.Where(char.IsLetter).All(char.IsUpper);
    }

    private static readonly char[] Apostrophes = ['\'', '’'];

    /// <summary>The file's characters, with the letter case of the 1769's. The two are the same length by construction.</summary>
    private static string WithCapitals(string mine, string theirs) =>
        string.Concat(mine.Zip(theirs, (own, printed) => char.IsLetter(own) && char.IsUpper(printed) ? char.ToUpperInvariant(own) : own));

    /// <summary>The same word however it is cased and whichever apostrophe it is written with.</summary>
    private static string Key(string word) => Bare(word).ToLowerInvariant().Replace('’', '\'');

    /// <summary>
    /// The word without the brackets the 1769 prints against it — <em>[but]</em> in 1 John 2:23 —
    /// which the reader leaves on the word because nothing separates them.
    /// </summary>
    private static string Bare(string word) => word.Trim(Brackets);

    private static readonly char[] Brackets = ['[', ']', '(', ')'];

    private static string Rebuild(IEnumerable<VerseToken> tokens) => string.Concat(tokens.Select(t => t.Word + t.Trailer));

    /// <summary>The pairs of positions a longest common subsequence of the two lists matches.</summary>
    private static List<(int, int)> Lcs(IReadOnlyList<string> one, IReadOnlyList<string> other)
    {
        var lengths = new int[one.Count + 1, other.Count + 1];
        for (var i = one.Count - 1; i >= 0; i--)
        {
            for (var j = other.Count - 1; j >= 0; j--)
            {
                lengths[i, j] = one[i] == other[j]
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var pairs = new List<(int, int)>();
        for (int i = 0, j = 0; i < one.Count && j < other.Count;)
        {
            if (one[i] == other[j])
            {
                pairs.Add((i++, j++));
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
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

    /// <summary>
    /// The 1769's words, verse by verse, each as its surface alone; and the subscription under each
    /// epistle, from the heading line it prints after the epistle's last verse, keyed by that verse.
    /// The subscription is read with the same reader as the verses, so its words are what any other
    /// verse's would be.
    /// </summary>
    private static (Dictionary<(int, int, int), IReadOnlyList<string>> Verses, Dictionary<(int, int, int), string> Subscriptions)
        Edition(string folder)
    {
        var verses = new Dictionary<(int, int, int), IReadOnlyList<string>>(31_200);
        var subscriptions = new Dictionary<(int, int, int), string>();
        if (!Directory.Exists(folder))
        {
            return (verses, subscriptions);
        }

        foreach (var path in Directory.EnumerateFiles(folder, "*.usfm"))
        {
            var content = File.ReadAllText(path);
            var book = UsfmReader.Read(content);
            var ordinal = BookReferences.ResolveOrdinal(book.Book)
                          ?? throw new InvalidOperationException(
                              $"{path} is for a book, {book.Book}, that has no canonical ordinal. The folder holds " +
                              "something other than eBible's eng-kjv2006; run scripts/fetch-ebible.ps1 -Only KingJames2006.");
            foreach (var chapter in book.Chapters)
            {
                foreach (var verse in chapter.Verses)
                {
                    verses[(ordinal, chapter.Number, verse.Number)] =
                        [.. verse.Words.Select(word => word.Surface).Where(surface => surface.Any(char.IsLetter))];
                }
            }

            if (ordinal is < FirstSubscribed or > LastSubscribed)
            {
                continue;
            }

            var lines = content.Split('\n');
            var last = Array.FindLastIndex(lines, line => line.StartsWith(VerseMarker, StringComparison.Ordinal));
            if (lines.Skip(last + 1).FirstOrDefault(line => line.StartsWith(SubscriptionMarker, StringComparison.Ordinal))
                is not { } heading)
            {
                continue;
            }

            var words = UsfmReader.Read($"\\id {book.Book}\n\\c 1\n\\v 1 {heading[SubscriptionMarker.Length..]}")
                .Chapters[0].Verses[0].Words;
            var closing = book.Chapters[^1];
            subscriptions[(ordinal, closing.Number, closing.Verses[^1].Number)] =
                string.Concat(words.Select(word => word.Surface + word.Trailer)).Trim();
        }

        return (verses, subscriptions);
    }

    /// <summary>
    /// The space the file prints inside the possessive of the divine name, <em>the Lord 's passover</em>,
    /// 98 times: where the 1769 sets LORD’s in small capitals the file lost the capitals and kept a gap,
    /// which made a word of the <em>s</em>.
    /// </summary>
    private static readonly Regex SplitPossessive = new(@"(?<=\w) (?='s\b)");

    private const string SubscriptionMarker = "\\s1 ";

    private const string VerseMarker = "\\v ";
}
