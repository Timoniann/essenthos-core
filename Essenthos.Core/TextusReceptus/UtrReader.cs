using System.Text.RegularExpressions;

namespace Essenthos.Core.TextusReceptus;

/// <summary>Which of the two editions the composite carries to read out of it.</summary>
public enum Edition
{
    /// <summary>The first alternative of every variant group.</summary>
    Stephanus1550,

    /// <summary>The second alternative of every variant group.</summary>
    Scrivener1894,
}

/// <param name="Strong">
/// Robinson's number, which is lemma-normalised for pronouns: <c>sou</c>, <c>se</c> and <c>soi</c>
/// all carry 4771 rather than the classic 4675, 4571 and 4671. Anything joining this text to a
/// King James tagged with classic numbers will not match on pronouns, and will not say so.
/// </param>
/// <param name="Inflection">
/// The five-digit tense-voice-mood code a verb carries after its Strong number. A reader that
/// expects one number per word takes this for the next word's number and misreads every verb in
/// the New Testament.
/// </param>
/// <param name="Segment">
/// Which piece of the verse this word came out of, counted the same way in both editions. A word
/// outside a variant group is its own segment and is the same word in both; a group is one segment
/// whichever side is taken. So two readings of one verse can be laid against each other without
/// aligning anything — the file already says which words correspond, and where it offers a choice
/// it says that too.
/// </param>
/// <param name="Alternatives">
/// The other parses the file offers for the same word. Matthew 4:15 gives γῆ as both nominative and
/// vocative — <c>gh 1093 {N-NSF} 1093 {N-VSF}</c> — and a reader that takes the repeated number for
/// the next word's turns one word into two and shifts everything after it.
/// </param>
internal sealed record UtrWord(
    string Surface,
    string? Strong,
    string? Inflection,
    string? Morphology,
    IReadOnlyList<string> Alternatives,
    int Segment = 0)
{
    public UtrWord(string surface, string? strong, string? inflection, string? morphology)
        : this(surface, strong, inflection, morphology, [])
    {
    }

    /// <summary>
    /// A record compares a list by reference, so two words parsed identically would be unequal for
    /// no reason a reader would recognise.
    /// </summary>
    public bool Equals(UtrWord? other) =>
        other is not null
        && Surface == other.Surface
        && Strong == other.Strong
        && Inflection == other.Inflection
        && Morphology == other.Morphology
        && Segment == other.Segment
        && Alternatives.SequenceEqual(other.Alternatives);

    public override int GetHashCode() => HashCode.Combine(Surface, Strong, Inflection, Morphology);
}

internal sealed record UtrVerse(int Chapter, int Number, IReadOnlyList<UtrWord> Words);

/// <summary>
/// Robinson's parsed Textus Receptus, which is Stephanus 1550 with Scrivener 1894 readings as
/// inline variants — not a Scrivener edition, whatever it is usually called. Both editions are read
/// out of the same file by taking one side of every variant group, so loading it yields two
/// witnesses for one parse.
///
/// The variant groups come in two shapes and a reader that knows only the first corrupts the text:
///
///     | ek 1537 {PREP} | cwriv 5565 {ADV} |        341 — different words, each fully tagged
///     | nazaret | nazareq | 3478 {N-PRI}           181 — one word, two spellings, tagged once
///                                                        after the group
///
/// Expecting tags inside every alternative drops both spellings of all 181, which is how Nazareth
/// disappears from Matthew 2:23.
/// </summary>
internal static partial class UtrReader
{
    /// <summary>
    /// A group is three pipes: the one that opens it, the one between its two alternatives, and the
    /// one that closes it. Every group in all 27 books has exactly two alternatives — 783 pipes
    /// over 261 groups — so a group with any other count is a file this reader has not seen.
    /// </summary>
    private const int PipesPerGroup = 3;

    /// <summary>
    /// What Robinson writes where a word's Strong number is not simply its own: before the number
    /// of a proper name the concordance lists elsewhere, and before both halves of a crasis.
    /// </summary>
    private const string Unnumbered = "0";

    public static IReadOnlyList<UtrVerse> Read(string content, Edition edition)
    {
        var verses = new List<UtrVerse>(1_200);

        foreach (var (chapter, number, body) in Verses(content))
        {
            verses.Add(new UtrVerse(chapter, number, Words(body, edition, chapter, number)));
        }

        return verses;
    }

    /// <summary>
    /// A verse begins at the start of a line with its address and runs until the next one; the
    /// lines between are continuations, wrapped for a terminal that no longer exists.
    /// </summary>
    private static IEnumerable<(int Chapter, int Number, string Body)> Verses(string content)
    {
        var chapter = 0;
        var number = 0;
        var body = new List<string>();

        foreach (var line in content.Split('\n'))
        {
            var start = VerseStart().Match(line);
            if (!start.Success)
            {
                body.Add(line);
                continue;
            }

            if (number > 0)
            {
                yield return (chapter, number, string.Join(' ', body));
            }

            chapter = int.Parse(start.Groups[1].Value);
            number = int.Parse(start.Groups[2].Value);
            body = [line[start.Length..]];
        }

        if (number > 0)
        {
            yield return (chapter, number, string.Join(' ', body));
        }
    }

    private static List<UtrWord> Words(string body, Edition edition, int chapter, int verse)
    {
        var tokens = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var words = new List<UtrWord>(32);
        var at = 0;
        var segment = 0;

        while (at < tokens.Length)
        {
            // Where the two transcriptions divide a verse differently, the composite writes the
            // other division inline as (15:6). It is a note about versification and not a word, and
            // it is the reason 28 of the 36 verses that differ from the answer key do.
            if (Division().IsMatch(tokens[at]))
            {
                at++;
                continue;
            }

            segment++;

            if (tokens[at] != "|")
            {
                words.Add(Word(tokens, ref at) with { Segment = segment });
                continue;
            }

            words.AddRange(Variant(tokens, ref at, edition, chapter, verse)
                .Select(word => word with { Segment = segment }));
        }

        return words;
    }

    /// <summary>
    /// One alternative of a variant group, and the tags that belong to it — which are inside the
    /// group in one shape and after it in the other. Both alternatives of the second shape share
    /// the tags, because they are the same word spelt two ways.
    /// </summary>
    private static List<UtrWord> Variant(string[] tokens, ref int at, Edition edition, int chapter, int verse)
    {
        // The opening pipe starts the first alternative, so the list begins empty rather than with
        // one already in it — otherwise the tokens before the group, of which there are none, are
        // counted as an alternative of their own.
        List<List<string>> alternatives = [];
        var pipes = 0;

        while (at < tokens.Length && pipes < PipesPerGroup)
        {
            if (tokens[at] == "|")
            {
                pipes++;
                if (pipes < PipesPerGroup)
                {
                    alternatives.Add([]);
                }
            }
            else
            {
                alternatives[^1].Add(tokens[at]);
            }

            at++;
        }

        if (pipes < PipesPerGroup || alternatives.Count != 2)
        {
            throw new InvalidOperationException(
                $"{chapter}:{verse} has a variant group with {alternatives.Count} alternatives and {pipes} " +
                "pipes. Every group in this edition has exactly two alternatives and three pipes; a file that " +
                "does not is one this reader has not seen, and guessing at it would put a reading in the text " +
                "that nobody printed.");
        }

        var chosen = alternatives[edition == Edition.Stephanus1550 ? 0 : 1];

        // 52 groups offer a word against nothing — this is where the two editions genuinely differ
        // rather than spelling one word two ways, and it is the whole reason a second Greek witness
        // is worth having. In this edition there is no word here.
        if (chosen.Count == 0)
        {
            if (!alternatives.Any(alternative => alternative.Any(token => token.StartsWith('{'))))
            {
                Tags(tokens, ref at);
            }

            return [];
        }

        if (chosen.Any(token => token.StartsWith('{')))
        {
            var inner = 0;
            var words = new List<UtrWord>(2);
            var array = chosen.ToArray();
            while (inner < array.Length)
            {
                words.Add(Word(array, ref inner));
            }

            return words;
        }

        // The spellings-only shape: the tags stand after the closing pipe and belong to whichever
        // spelling was taken, because the two are one word written two ways.
        string[] spelt = [.. chosen, .. Tags(tokens, ref at)];
        var start = 0;
        return [Word(spelt, ref start)];
    }

    /// <summary>The tag tokens standing after a group, which run to the closing brace.</summary>
    private static List<string> Tags(string[] tokens, ref int at)
    {
        var tags = new List<string>(3);
        while (at < tokens.Length)
        {
            tags.Add(tokens[at]);
            if (tokens[at++].EndsWith('}'))
            {
                break;
            }
        }

        return tags;
    }

    /// <summary>
    /// One word: its letters, its Strong number, the inflection code a verb adds, and its parse.
    /// Anything but the surface may be absent — a word inside a variant group of the second shape
    /// arrives without tags, and they are attached afterwards.
    /// </summary>
    private static UtrWord Word(string[] tokens, ref int at)
    {
        var surface = tokens[at++];
        string? strong = null;
        string? inflection = null;
        string? morphology = null;
        var alternatives = new List<string>();
        var compound = at < tokens.Length && tokens[at] == Unnumbered;

        // A surface is never all digits and never a brace, so everything up to the next word that
        // is neither belongs to this one. That is what keeps a repeated parse from being read as a
        // word of its own.
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

            // The inflection code stands between the Strong number and the parse; a number after
            // the parse is the alternative reading's own Strong number, which is the same one.
            if (morphology is not null)
            {
                continue;
            }

            // Zero is not a Strong number. Robinson writes it in front of a word whose numbering is
            // not its own — simewn 0 4826, and eanper 0 1437 4007, which is a crasis of ean and per
            // and carries the number of each. Taken as the number, it makes the word unresolvable
            // and pushes the real one into the verb's inflection slot.
            if (token == Unnumbered)
            {
                continue;
            }

            if (strong is null)
            {
                strong = token;
                continue;
            }

            // A second number after the first is the verb's inflection code, unless the word was
            // marked unnumbered — then it is the other half of a compound.
            if (inflection is null && !compound)
            {
                inflection = token;
                continue;
            }

            alternatives.Add(token);
        }

        return new UtrWord(surface, strong, inflection, morphology, alternatives);
    }

    [GeneratedRegex(@"^(\d+):(\d+) ")]
    private static partial Regex VerseStart();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex Number();

    [GeneratedRegex(@"^\(\d+:\d+\)$")]
    private static partial Regex Division();
}
