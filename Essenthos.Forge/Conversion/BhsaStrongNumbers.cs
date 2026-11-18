using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Strong;

namespace Essenthos.Core.Conversion;

/// <summary>
/// BHSA's Strong numbers, read from the Open Hebrew Bible's King James mapping file, which lists every
/// word of BHS under its running number with the number and the gloss of its lexeme. The file's words and
/// BHSA's are both in the order of the Hebrew text, so they are paired in that order: through the words
/// whose glosses agree, and, between two such words, one for one where both sides hold as many words as
/// each other. Where they do not, a word is paired only by an agreeing gloss, and one left unpaired keeps
/// no number. No frame is needed, because neither side is read by verse address.
/// The divine name is numbered by its own pointing, not by the file: Strong's H3069 is the name pointed with the
/// vowels of Elohim (a hireq under the vav), read so where it stands beside אֲדֹנָי; every other spelling is H3068.
/// The file gives H3069 to several hundred words pointed to be read Adonai, and H3068 to some pointed as Elohim.
/// The relative particle שֶׁ is H7945 wherever it stands: the file gives most of its words the number of the word
/// after them (Song of Songs 3:1-3, Ecclesiastes, Psalms 122-146).
/// אֵת is numbered by BHSA's lexeme, as MACULA Hebrew numbers it: the object marker H853, "together with" H854.
/// The file follows the King James's sense instead in some 390 words.
/// </summary>
internal sealed class BhsaStrongNumbers : ITextStage
{
    private const string Mapping = "mapping/KJV-OT-mapped-to-BHS-full-mapping.csv";

    /// <summary>BHSA's lexeme of the relative particle שֶׁ.</summary>
    private const string RelativeLemma = "שַׁ";

    private const string RelativeNumber = "H7945";

    /// <summary>BHSA's lexeme shared by the object marker and the preposition "together with", told apart by their glosses.</summary>
    private const string EtLemma = "אֵת";

    /// <summary>How many glosses in a row must agree before the two sides are taken to be in step again.</summary>
    private const int Resync = 3;

    /// <summary>How far ahead on either side a resynchronisation is looked for.</summary>
    private const int Reach = 400;

    public bool AppliesTo(string slug) => slug == BhsaTextSource.Slug;

    public IReadOnlyList<string> Sources(string slug) => [Mapping];

    public void Apply(ConvertedText text, string resources)
    {
        var stated = new SortedDictionary<int, (string? Strong, string Gloss)>();
        foreach (var entry in KjvBhsMapping.Read(Path.Combine(resources, Mapping)).SelectMany(record => record.Hebrew))
        {
            stated.TryAdd(entry.Position, (StrongNumbers.Normalize(entry.Strong), entry.Gloss));
        }

        var entries = stated.Values.ToList();
        var words = text.Words().Select(w => w.Word).ToList();
        var numbered = 0;
        foreach (var (word, entry) in Pair(words, entries))
        {
            if (entries[entry].Strong is { } strong)
            {
                words[word].Strong = strong;
                numbered++;
            }
        }

        var pointed = 0;
        foreach (var word in words)
        {
            if (DivineName(word.Lemma, word.Surface) is { } number && word.Strong != number)
            {
                word.Strong = number;
                pointed++;
            }
        }

        var relatives = 0;
        foreach (var word in words)
        {
            if (Relative(word.Lemma) is { } number && word.Strong != number)
            {
                word.Strong = number;
                relatives++;
            }
        }

        var ets = 0;
        foreach (var word in words)
        {
            if (Et(word.Lemma, word.Gloss) is { } number && word.Strong != number)
            {
                word.Strong = number;
                ets++;
            }
        }

        text.Provenance.Add(new("strong", $"{Mapping} (Open Hebrew Bible, CC BY-NC 4.0): {numbered} words by running number"));
        text.Provenance.Add(new("strong-pointed", $"{pointed} numbers of the divine name set by its pointing (H3069 pointed as Elohim, else H3068)"));
        text.Provenance.Add(new("strong-relative", $"{relatives} relative particles שֶׁ set to {RelativeNumber}, where the file gave another word's number"));
        text.Provenance.Add(new("strong-et", $"{ets} words of אֵת set by BHSA's lexeme (object marker H853, together with H854), where the file followed the King James's sense"));
    }

    /// <summary>The number of a word of the divine name by its pointing: H3069 with a hireq under the vav, else H3068; null for any other lemma.</summary>
    public static string? DivineName(string? lemma, string surface)
    {
        if (lemma is null || Consonants(lemma) != "יהוה")
        {
            return null;
        }

        var vav = surface.LastIndexOf('ו');
        var he = surface.LastIndexOf('ה');
        return vav >= 0 && he > vav && surface.AsSpan(vav + 1, he - vav - 1).Contains('ִ') ? "H3069" : "H3068";
    }

    /// <summary>The number of the relative particle שֶׁ; null for any other lemma.</summary>
    public static string? Relative(string? lemma) => lemma == RelativeLemma ? RelativeNumber : null;

    /// <summary>The number of אֵת by BHSA's lexeme: H853 for the object marker, H854 for "together with"; null for any other word.</summary>
    public static string? Et(string? lemma, string? gloss) => lemma != EtLemma ? null : gloss switch
    {
        "<object marker>" => "H853",
        "together with" => "H854",
        _ => null,
    };

    private static string Consonants(string word) => string.Concat(word.Where(c => c is >= 'א' and <= 'ת'));

    private static IEnumerable<(int Word, int Entry)> Pair(List<ConvertedWord> words, List<(string? Strong, string Gloss)> entries)
    {
        bool Same(int w, int e) => w < words.Count && e < entries.Count && Glosses.Same(entries[e].Gloss, words[w].Gloss);

        bool InStep(int w, int e)
        {
            for (var k = 0; k < Resync; k++)
            {
                if (!Same(w + k, e + k))
                {
                    return false;
                }
            }

            return true;
        }

        var i = 0;
        var j = 0;
        while (i < words.Count && j < entries.Count)
        {
            if (Same(i, j))
            {
                yield return (i++, j++);
                continue;
            }

            // The nearest point both sides are in step again, by the fewest words skipped in all.
            (int Words, int Entries)? next = null;
            for (var skipped = 1; skipped <= 2 * Reach && next is null; skipped++)
            {
                for (var a = Math.Max(0, skipped - Reach); a <= Math.Min(skipped, Reach); a++)
                {
                    if (InStep(i + a, j + skipped - a))
                    {
                        next = (a, skipped - a);
                        break;
                    }
                }
            }

            var (wordsBetween, entriesBetween) = next ?? (words.Count - i, entries.Count - j);
            if (wordsBetween == entriesBetween)
            {
                for (var k = 0; k < wordsBetween; k++)
                {
                    yield return (i + k, j + k);
                }
            }
            else
            {
                foreach (var (w, e) in Lcs(i, wordsBetween, j, entriesBetween, Same))
                {
                    yield return (w, e);
                }
            }

            i += wordsBetween;
            j += entriesBetween;
        }
    }

    private static List<(int, int)> Lcs(int wordFrom, int wordCount, int entryFrom, int entryCount, Func<int, int, bool> same)
    {
        var longest = new int[wordCount + 1, entryCount + 1];
        for (var w = wordCount - 1; w >= 0; w--)
        {
            for (var e = entryCount - 1; e >= 0; e--)
            {
                longest[w, e] = same(wordFrom + w, entryFrom + e) ? longest[w + 1, e + 1] + 1 : Math.Max(longest[w + 1, e], longest[w, e + 1]);
            }
        }

        var pairs = new List<(int, int)>();
        for (int w = 0, e = 0; w < wordCount && e < entryCount;)
        {
            if (same(wordFrom + w, entryFrom + e))
            {
                pairs.Add((wordFrom + w++, entryFrom + e++));
            }
            else if (longest[w + 1, e] >= longest[w, e + 1])
            {
                w++;
            }
            else
            {
                e++;
            }
        }

        return pairs;
    }
}
