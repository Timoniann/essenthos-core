using System.Text.RegularExpressions;

namespace Essenthos.Core.Byzantine;

/// <param name="Strong">
/// Robinson's number, which is lemma-normalised the same way the Textus Receptus composite's is:
/// <c>sou</c>, <c>se</c> and <c>soi</c> all carry 4771 rather than the classic 4675, 4571 and 4671.
/// Every word in these files has one — there is no untagged word in the New Testament here.
/// </param>
/// <param name="Alternatives">
/// The other parses the file offers for the same word. Matthew 4:15 gives γῆ as both nominative and
/// vocative — <c>gh 1093 {N-NSF} 1093 {N-VSF}</c> — and a reader that takes the repeated number for
/// the next word's turns one word into two and shifts every word after it in the verse. Twenty-eight
/// words in the 27 books are written this way.
/// </param>
internal sealed record Bp5Word(
    string Surface,
    string Strong,
    string Morphology,
    IReadOnlyList<string> Alternatives)
{
    public Bp5Word(string surface, string strong, string morphology)
        : this(surface, strong, morphology, [])
    {
    }

    /// <summary>
    /// A record compares a list by reference, so two words parsed identically would be unequal for
    /// no reason a reader would recognise.
    /// </summary>
    public bool Equals(Bp5Word? other) =>
        other is not null
        && Surface == other.Surface
        && Strong == other.Strong
        && Morphology == other.Morphology
        && Alternatives.SequenceEqual(other.Alternatives);

    public override int GetHashCode() => HashCode.Combine(Surface, Strong, Morphology);
}

internal sealed record Bp5Verse(int Chapter, int Number, IReadOnlyList<Bp5Word> Words);

/// <summary>
/// Robinson and Pierpont's Byzantine Textform in its parsed form: one verse per line, a
/// <c>chapter.verse</c> address, then a word, its Strong number and its parse, repeating.
///
/// It is a plainer file than the Textus Receptus composite this project already reads. There are no
/// variant groups, because this is one edition rather than two written into one token stream; no
/// verse-division notes; no unnumbered word; and no inflection code beside a verb's number. Every
/// one of the 140,149 words carries a number and a parse, which is why linking this text to the
/// other Greek editions needs no aligner at all.
///
/// The apparatus — the places where the Byzantine tradition itself divides, and where it parts from
/// the critical text — is not in these files. It is in <c>source/CCAT</c>, in accented beta code,
/// and nothing here reads it yet.
/// </summary>
internal static partial class Bp5Reader
{
    public static IReadOnlyList<Bp5Verse> Read(string content)
    {
        var verses = new List<Bp5Verse>(1_200);

        foreach (var line in content.Split('\n'))
        {
            var start = VerseStart().Match(line);
            if (!start.Success)
            {
                continue;
            }

            var chapter = int.Parse(start.Groups[1].Value);
            var number = int.Parse(start.Groups[2].Value);
            verses.Add(new Bp5Verse(chapter, number, Words(line[start.Length..], chapter, number)));
        }

        return verses;
    }

    private static List<Bp5Word> Words(string body, int chapter, int verse)
    {
        var tokens = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var words = new List<Bp5Word>(32);
        var at = 0;

        while (at < tokens.Length)
        {
            words.Add(Word(tokens, ref at, chapter, verse));
        }

        return words;
    }

    /// <summary>
    /// One word: its letters, its number and its parse. A surface is never all digits and never a
    /// brace, so everything up to the next token that is neither belongs to this word — which is
    /// what keeps a repeated number or a second parse from being read as a word of its own.
    /// </summary>
    private static Bp5Word Word(string[] tokens, ref int at, int chapter, int verse)
    {
        var surface = tokens[at++];
        string? strong = null;
        string? morphology = null;
        var alternatives = new List<string>();

        while (at < tokens.Length && (Number().IsMatch(tokens[at]) || tokens[at].StartsWith('{')))
        {
            var token = tokens[at++];
            if (token.StartsWith('{'))
            {
                var parse = token.Trim('{', '}');
                if (morphology is null)
                {
                    morphology = parse;
                }
                else
                {
                    alternatives.Add(parse);
                }

                continue;
            }

            if (strong is null)
            {
                strong = token;
                continue;
            }

            alternatives.Add(token);
        }

        if (strong is null || morphology is null)
        {
            throw new InvalidOperationException(
                $"{chapter}:{verse} has the word \"{surface}\" with " +
                $"{(strong is null ? "no Strong number" : "no parse")}. Every word in this edition carries " +
                "both, so a file where one does not is one this reader has not seen; read the line before " +
                "deciding what the word means, rather than loading it without.");
        }

        return new Bp5Word(surface, strong, morphology, alternatives);
    }

    [GeneratedRegex(@"^(\d+)\.(\d+) ")]
    private static partial Regex VerseStart();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex Number();
}
