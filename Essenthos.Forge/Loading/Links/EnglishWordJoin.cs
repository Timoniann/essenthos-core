namespace Essenthos.Core.Loading.Links;

/// <summary>
/// Which of the loaded King James's words each word of the mapping file is, over one verse.
///
/// <para>
/// The file and the loaded text are two digitisations of one translation, and they part in small
/// ways: the file writes <em>firstborn</em>, <em>any thing</em> and <em>LORD'S</em> where bible4u
/// writes <em>first born</em>, <em>anything</em> and <em>Lord 'S</em>; it heads each part of Psalm
/// 119 with the letter's name, which the loaded verse does not carry; and one word in a verse is
/// often spelt otherwise — <em>Emims</em> for bible4u's <em>Emins</em>, <em>theirs</em> for
/// <em>their's</em>, <em>asswaged</em> for <em>assuaged</em>. Read word against word at the same
/// index, each of these refuses the whole verse, and every name in it reaches no Hebrew word.
/// </para>
///
/// <para>
/// So the two are walked together. Words the same once case, hyphens and apostrophes are set aside
/// pair; a word one side writes as two pairs with both; and where neither holds, the words after
/// decide: a different word between two that pair is the same word spelt or printed otherwise, and
/// a word with nothing opposite it while the next ones pair is a word one side lacks. Nothing else
/// is guessed. A word spelt otherwise by a letter or two counts as paired; any other different
/// word counts as unmatched. A verse needing more than <see cref="MostUnmatched"/> such steps, or one whose
/// paired words do not outnumber them <see cref="PairedPerUnmatched"/> to one, is not taken to be
/// the same verse, and the caller refuses it as before.
/// </para>
/// </summary>
internal static class EnglishWordJoin
{
    /// <summary>
    /// Measured over the 285 Old Testament verses the word-for-word reading left unlinked: every one
    /// needs at most two, and 159 need none.
    /// </summary>
    private const int MostUnmatched = 2;

    private const int PairedPerUnmatched = 3;

    private const int Respelt = 2;

    /// <returns>
    /// For each word of the file, the indexes of the loaded words it is, which may be none; null
    /// where the two are not the same verse. A loaded word no file word names is in no list.
    /// </returns>
    public static int[][]? Of(IReadOnlyList<string> file, IReadOnlyList<string> loaded)
    {
        var words = new List<int>(loaded.Count);
        for (var i = 0; i < loaded.Count; i++)
        {
            if (loaded[i].Any(char.IsLetterOrDigit))
            {
                words.Add(i);
            }
        }

        var joined = new int[file.Count][];
        var unmatched = 0;
        var paired = 0;
        int f = 0, k = 0;
        while (f < file.Count && k < words.Count)
        {
            var here = Spelled(file[f]);
            if (Same(here, loaded[words[k]]))
            {
                joined[f++] = [words[k++]];
                paired++;
            }
            else if (k + 1 < words.Count && Same(here, Spelled(loaded[words[k]]) + Spelled(loaded[words[k + 1]])))
            {
                joined[f++] = [words[k], words[k + 1]];
                k += 2;
                paired++;
            }
            else if (f + 1 < file.Count && Same(here + Spelled(file[f + 1]), loaded[words[k]]))
            {
                joined[f++] = [words[k]];
                joined[f++] = [words[k++]];
                paired++;
            }
            else if (f + 1 == file.Count && k + 1 == words.Count
                     || f + 1 < file.Count && k + 1 < words.Count && Same(Spelled(file[f + 1]), loaded[words[k + 1]]))
            {
                if (SpeltOtherwise(here, Spelled(loaded[words[k]])))
                {
                    paired++;
                }
                else
                {
                    unmatched++;
                }

                joined[f++] = [words[k++]];
            }
            else if (f + 1 < file.Count && Same(Spelled(file[f + 1]), loaded[words[k]]))
            {
                joined[f++] = [];
                unmatched++;
            }
            else if (k + 1 < words.Count && Same(here, loaded[words[k + 1]]))
            {
                k++;
                unmatched++;
            }
            else
            {
                return null;
            }
        }

        unmatched += file.Count - f + (words.Count - k);
        while (f < file.Count)
        {
            joined[f++] = [];
        }

        return unmatched <= MostUnmatched && paired >= unmatched * PairedPerUnmatched ? joined : null;
    }

    /// <summary>
    /// The letters alone. The file writes a name whole where the loaded text hyphenates it
    /// (<em>Bathsheba</em>, <em>Elelohe-Israel</em>), its apostrophes straight where the text's are
    /// curly, and the possessive without one where bible4u puts one in (<em>theirs</em>).
    /// </summary>
    private static string Spelled(string word) =>
        word.Replace("-", "").Replace("'", "").Replace("’", "");

    private static bool Same(string spelled, string loaded) =>
        string.Equals(spelled, Spelled(loaded), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// One word in two spellings rather than two words: no more than <see cref="Respelt"/> letters
    /// added, dropped or changed, as <em>Emims</em> and <em>Emins</em>, <em>Shion</em> and
    /// <em>Shihon</em>. A verse of names alone, which has few words to pair, is still taken.
    /// </summary>
    private static bool SpeltOtherwise(string one, string other)
    {
        var a = one.ToLowerInvariant();
        var b = other.ToLowerInvariant();
        if (Math.Abs(a.Length - b.Length) > Respelt)
        {
            return false;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(
                    Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length] <= Respelt;
    }
}
