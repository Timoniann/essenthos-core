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
/// </summary>
internal sealed class BhsaStrongNumbers : ITextStage
{
    private const string Mapping = "mapping/KJV-OT-mapped-to-BHS-full-mapping.csv";

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

        text.Provenance.Add(new("strong", $"{Mapping} (Open Hebrew Bible, CC BY-NC 4.0): {numbered} words by running number"));
    }

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
