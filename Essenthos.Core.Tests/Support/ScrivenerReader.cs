using System.Text.RegularExpressions;

namespace Essenthos.Core.TextusReceptus;

/// <summary>
/// The plain Scrivener 1894 text, which carries no Strong numbers and no morphology and is
/// therefore not the text to load. It is the answer key: taking the second alternative of every
/// variant group is a rule, and this is what says whether the rule was right.
///
/// Its beta-code is not the composite's. Here <c>y</c> is theta and <c>q</c> is psi; in the parsed
/// files it is the other way round, so <c>kayolikh</c> there is <c>kaqolikh</c> here. Compared
/// without folding both onto one alphabet, every theta and every psi in the New Testament is a
/// false mismatch — and there are enough of them to bury the 36 real ones.
/// </summary>
internal static partial class ScrivenerReader
{
    public static IReadOnlyList<UtrVerse> Read(string content)
    {
        var verses = new List<UtrVerse>(1_200);
        var chapter = 0;
        var number = 0;
        var words = new List<UtrWord>(32);
        var inTitle = false;

        foreach (var piece in Pieces(content))
        {
            // A book's title stands in brackets and runs over several lines, so it is skipped as a
            // span rather than by its first and last words — otherwise the middle of it is loaded
            // as scripture and every verse of the book is compared against the wrong words.
            if (piece.StartsWith('['))
            {
                inTitle = !piece.EndsWith(']');
                continue;
            }

            if (inTitle)
            {
                inTitle = !piece.EndsWith(']');
                continue;
            }

            var start = VerseStart().Match(piece);
            if (start.Success)
            {
                if (number > 0)
                {
                    verses.Add(new UtrVerse(chapter, number, words));
                }

                chapter = int.Parse(start.Groups[1].Value);
                number = int.Parse(start.Groups[2].Value);
                words = [];
                continue;
            }

            if (number == 0)
            {
                continue;
            }

            words.Add(new UtrWord(Fold(piece), null, null, null));
        }

        if (number > 0)
        {
            verses.Add(new UtrVerse(chapter, number, words));
        }

        return verses;
    }

    /// <summary>
    /// Folds this file's alphabet onto the composite's, so the two can be compared at all: theta
    /// and psi trade letters between the repositories.
    /// </summary>
    public static string Fold(string word)
    {
        Span<char> folded = stackalloc char[word.Length];
        for (var i = 0; i < word.Length; i++)
        {
            folded[i] = word[i] switch { 'y' => 'q', 'q' => 'y', var other => other };
        }

        return new string(folded);
    }

    /// <summary>
    /// The verse addresses and the words, in order. An address may sit against a word with no space
    /// between them, so it is split off rather than assumed to be its own token.
    /// </summary>
    private static IEnumerable<string> Pieces(string content)
    {
        foreach (var token in content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var start = VerseStart().Match(token);
            if (start.Success && start.Length < token.Length)
            {
                yield return token[..start.Length];
                yield return token[start.Length..];
                continue;
            }

            yield return token;
        }
    }

    [GeneratedRegex(@"^(\d+):(\d+)")]
    private static partial Regex VerseStart();
}
