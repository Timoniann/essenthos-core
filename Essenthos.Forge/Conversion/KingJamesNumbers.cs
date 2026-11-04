using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Usfm;

namespace Essenthos.Core.Conversion;

/// <summary>
/// The King James's Strong numbers and the words its translators supplied, from the editions that state them:
/// <list type="bullet">
/// <item>the New Testament from Zefania's KJV+, whose tags are matched to the words verse by verse as the load's
/// <c>new-testament-links</c> step matches them, both sides in one spelling (<see cref="AmericanSpelling"/>): a word
/// takes the first number of its tag, and a word the edition prints in italics and numbers nothing is supplied;</item>
/// <item>the Old Testament's supplied words from the Open Hebrew Bible's mapping file, whose italic words are joined to
/// the verse's words as the load's <c>old-testament-links</c> step joins them, a function word the mapping gives a
/// Hebrew prefix excepted;</item>
/// <item>the Old Testament's numbers from eBible's eng-kjv2006, the 1769 text, which tags its words with Strong's: each
/// verse's words are paired with eBible's by their letters, and a paired word takes eBible's number.</item>
/// </list>
/// </summary>
internal sealed class KingJamesNumbers : ITextStage
{
    private const string Zefania = "Zefania/SF_2009-01-20_ENG_KJV_(KJV+).xml";
    private const string Mapping = "mapping/KJV-OT-mapped-to-BHS-full-mapping.csv";
    private const string Tahot = "STEPBible";
    private const string Ebible = LostPsalmOpenings.KingJamesFolder;
    private const int LastOldTestamentBook = 39;
    private const int Psalms = 19;

    public bool AppliesTo(string slug) => slug == Bible4uTextSource.KingJames;

    public IReadOnlyList<string> Sources(string slug) => [Zefania, Mapping, Tahot, Ebible];

    public void Apply(ConvertedText text, string resources)
    {
        var verses = text.Verses.Where(v => v.Key.VerseLabel.Length == 0).ToDictionary(v => (v.Key.Book, v.Key.Chapter, v.Key.Verse));
        var (newTestament, supplied) = NewTestament(verses, Path.Combine(resources, Zefania));
        var oldSupplied = OldTestamentSupplied(verses, resources);
        var oldTestament = OldTestament(verses, Path.Combine(resources, Ebible));

        foreach (var verse in text.Verses)
        {
            foreach (var word in verse.Words)
            {
                word.Supplied = null;
            }
        }

        foreach (var verse in text.Verses)
        {
            // Supplied words are grouped by run, as the load groups the King James's stated absences.
            var run = 0;
            var previous = -2;
            for (var at = 0; at < verse.Words.Count; at++)
            {
                if (!verse.Words[at].Supplied.HasValue && (supplied.Contains(verse.Words[at]) || oldSupplied.Contains(verse.Words[at])))
                {
                    if (at != previous + 1)
                    {
                        run++;
                    }

                    verse.Words[at].Supplied = run;
                    previous = at;
                }
            }
        }

        text.Provenance.Add(new("strong", $"New Testament: {Zefania} (Zefania KJV+), {newTestament} words; "
                                          + $"Old Testament: {Ebible} (eBible eng-kjv2006, Public Domain), {oldTestament} words"));
        text.Provenance.Add(new("supplied", $"the italics of {Zefania} (New Testament) and of {Mapping} (Old Testament)"));
    }

    private static (int Numbered, HashSet<ConvertedWord> Supplied) NewTestament(
        Dictionary<(int, int, int), ConvertedVerse> verses, string zefania)
    {
        var numbered = 0;
        var supplied = new HashSet<ConvertedWord>(ReferenceEqualityComparer.Instance);
        foreach (var (address, tags) in NewTestamentLinkLoader.Tagged(zefania))
        {
            if (!verses.TryGetValue(address, out var verse))
            {
                continue;
            }

            // The words the source printed: an epistle's subscription, added by a correction, is not in the tagged edition.
            var words = verse.Words.Where(word => !word.Added).ToList();
            // Zefania spells the American way (honorable, offense, heretic); the words are put in step on one spelling.
            var spans = TaggedEdition.Align([.. tags.Select(tag => tag.Text)], [.. words.Select(word => word.Surface)], AmericanSpelling.Fold);
            if (TaggedEdition.Agreement(spans, words.Count) < NewTestamentLinkLoader.SameVerse)
            {
                continue;
            }

            var projected = NewTestamentLinkLoader.Project(tags, words.Count, spans);
            for (var i = 0; i < projected.Count; i++)
            {
                if (projected[i].Numbers.Count > 0)
                {
                    words[i].Strong = projected[i].Numbers[0];
                    numbered++;
                }
                else if (projected[i].Supplied)
                {
                    supplied.Add(words[i]);
                }
            }
        }

        return (numbered, supplied);
    }

    private static HashSet<ConvertedWord> OldTestamentSupplied(Dictionary<(int, int, int), ConvertedVerse> verses, string resources)
    {
        var volumes = Directory.GetFiles(Path.Combine(resources, Tahot), "TAHOT *.txt").Order(StringComparer.Ordinal).ToArray();
        var segmentation = volumes.Length > 0 ? TahotSegmentation.Read(volumes) : null;
        var supplied = new HashSet<ConvertedWord>(ReferenceEqualityComparer.Instance);
        foreach (var record in KjvBhsMapping.Read(Path.Combine(resources, Mapping)))
        {
            if (!verses.TryGetValue((record.Book, record.Chapter, record.Verse), out var verse))
            {
                continue;
            }

            var fileWords = record.English.SelectMany(segment => segment.Words).ToList();
            if (EnglishWordJoin.Of([.. fileWords.Select(word => word.Text)], [.. verse.Words.Select(word => word.Surface)]) is not { } joined)
            {
                continue;
            }

            var claimed = new HashSet<int>();
            List<int> Loaded(int file) => [.. joined[file].Where(claimed.Add)];

            var prefixes = HebrewPrefixes.Match(record.Hebrew, record.English,
                    segmentation?.Align(record.Book, record.Chapter, record.Verse, record.Hebrew))
                .ToDictionary(match => match.EnglishWord, match => match);
            foreach (var english in prefixes.Keys)
            {
                Loaded(english);
            }

            for (var i = 0; i < fileWords.Count; i++)
            {
                if (fileWords[i].Supplied && !prefixes.ContainsKey(i))
                {
                    foreach (var at in Loaded(i))
                    {
                        supplied.Add(verse.Words[at]);
                    }
                }
            }
        }

        return supplied;
    }

    private static int OldTestament(Dictionary<(int, int, int), ConvertedVerse> verses, string folder)
    {
        var numbered = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*.usfm").Order(StringComparer.Ordinal))
        {
            var usfm = File.ReadAllText(file);
            var book = UsfmReader.Read(usfm);
            if (BookCodes.TryGetOrdinal(book.Book, out var ordinal) is false || ordinal > LastOldTestamentBook)
            {
                continue;
            }

            var titles = ordinal == Psalms ? UsfmReader.Superscriptions(usfm) : null;
            foreach (var chapter in book.Chapters)
            {
                foreach (var verse in chapter.Verses.Where(v => v.Label.Length == 0))
                {
                    if (!verses.TryGetValue((ordinal, chapter.Number, verse.Number), out var ours))
                    {
                        continue;
                    }

                    var theirs = verse.Words.Select(w => (w.Surface, w.StrongNumber)).ToList();
                    if (titles is not null && verse.Number == 1 && titles.TryGetValue(chapter.Number, out var title))
                    {
                        theirs.InsertRange(0, title.Select(w => (w.Surface, w.StrongNumber)));
                    }

                    foreach (var (mine, other) in Paired(ours.Words, theirs))
                    {
                        if (theirs[other].StrongNumber is { } strong && ours.Words[mine].Strong is null)
                        {
                            ours.Words[mine].Strong = strong;
                            numbered++;
                        }
                    }
                }
            }
        }

        return numbered;
    }

    private static List<(int Mine, int Theirs)> Paired(List<ConvertedWord> mine, List<(string Surface, string? StrongNumber)> theirs)
    {
        static string Key(string word) => word.Trim('[', ']', '(', ')').ToLowerInvariant().Replace('’', '\'');

        var a = mine.Select(w => Key(w.Surface)).ToList();
        var b = theirs.Select(w => Key(w.Surface)).ToList();
        var longest = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
        {
            for (var j = b.Count - 1; j >= 0; j--)
            {
                longest[i, j] = a[i].Length > 0 && a[i] == b[j] ? longest[i + 1, j + 1] + 1 : Math.Max(longest[i + 1, j], longest[i, j + 1]);
            }
        }

        var pairs = new List<(int, int)>();
        for (int i = 0, j = 0; i < a.Count && j < b.Count;)
        {
            if (a[i].Length > 0 && a[i] == b[j])
            {
                pairs.Add((i++, j++));
            }
            else if (longest[i + 1, j] >= longest[i, j + 1])
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
}
