using System.Text;
using System.Xml.Linq;

namespace Essenthos.Core.Swete;

/// <summary>
/// A First1KGreek TEI edition of one book, written out as the one-token-per-line form
/// <see cref="SweteReader"/> reads: <c>48.1.1 Ὅρασις</c>.
///
/// <para>
/// The other books of Swete arrive already in that form, converted by Nathan D. Smith's script; this
/// is the same conversion done here, for the book his script could not give us — his output is named
/// after the work, so where First1KGreek holds two editions of one work the second overwrote the
/// first. Reading the TEI directly also means the edition is read from the file its licence is
/// stated in.
/// </para>
///
/// <para>
/// What is text and what is not follows the page. The apparatus and the marginal sigla are notes and
/// are left out; so is the book's heading and every page and line mark, though the words after a
/// mark are the verse's. Words an editor brackets as a corrector's addition or deletion are printed
/// on the page and are kept. Material printed inside a chapter but outside any numbered verse — an
/// oracle's title, a manuscript's inscription — opens the verse after it, which is where the page
/// prints it, and closes the last verse if nothing follows it.
/// </para>
/// </summary>
internal static class First1KGreekReader
{
    private static readonly XNamespace Tei = "http://www.tei-c.org/ns/1.0";

    private const string Division = "div";

    private const string Chapter = "chapter";

    private const string Verse = "verse";

    private const char LineEndHyphen = '-';

    /// <summary>Elements whose content is not the text: the apparatus, marginal sigla, headings.</summary>
    private static readonly HashSet<string> NotText = ["note", "head"];

    /// <summary>The marks an editor opens a span of words with.</summary>
    private static readonly HashSet<char> OpeningBrackets = ['<', '[', '(', '⟨'];

    /// <summary>
    /// Marks on a stretch of words rather than blocks of them: an addition, a deletion, a placeholder
    /// for what the transcriber could not read.
    /// </summary>
    private static readonly HashSet<string> Inline = ["add", "del", "foreign", "hi", "unclear", "supplied"];

    /// <param name="path">The TEI file.</param>
    /// <param name="book">The work's number in the TLG catalogue, which every line states.</param>
    public static IEnumerable<string> Lines(string path, int book)
    {
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var edition = document.Descendants(Tei + "body").Elements(Tei + Division)
                          .SingleOrDefault(element => (string?)element.Attribute("type") == "edition")
                      ?? throw new InvalidOperationException(
                          $"{path} holds no <div type=\"edition\"> in its body. A First1KGreek file carries its text " +
                          "there; a file without one is not an edition this reader knows how to read.");

        foreach (var chapter in edition.Elements(Tei + Division).Where(IsA(Chapter)))
        {
            var number = Number(chapter, path);
            var verses = new List<(string Number, List<string> Tokens)>();
            var pending = new List<string>();

            foreach (var node in chapter.Nodes())
            {
                if (node is XElement element && element.Name == Tei + Division && IsA(Verse)(element))
                {
                    verses.Add((Number(element, path), [.. pending, .. Tokens(element)]));
                    pending.Clear();
                    continue;
                }

                pending.AddRange(Tokens(node));
            }

            if (pending.Count > 0 && verses.Count > 0)
            {
                verses[^1].Tokens.AddRange(pending);
            }

            foreach (var (verse, tokens) in verses)
            {
                foreach (var token in Joined(tokens))
                {
                    yield return $"{book}.{number}.{verse} {token}";
                }
            }
        }
    }

    private static Func<XElement, bool> IsA(string subtype) =>
        element => (string?)element.Attribute("subtype") == subtype;

    private static string Number(XElement division, string path) =>
        (string?)division.Attribute("n") is { Length: > 0 } number
            ? number
            : throw new InvalidOperationException(
                $"A {division.Attribute("subtype")?.Value} of {path} has no number. Every chapter and verse of a " +
                "First1KGreek edition is numbered in its n attribute; one without is a division this reader " +
                "would have to invent an address for.");

    private static IEnumerable<string> Tokens(XNode node) =>
        Text(node).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// A verse's tokens as the page means them. Composed to NFC, as every other book of the edition
    /// is. A word the printer divided at a line end is one word again, since the hyphen is the
    /// line's and not the word's. And an editor's opening bracket stands alone, so that the reader
    /// hangs it on the word before as it does every other mark rather than taking it for a letter.
    /// </summary>
    private static IEnumerable<string> Joined(List<string> tokens)
    {
        string? held = null;
        foreach (var raw in tokens)
        {
            var token = Composed(raw);
            if (held is not null)
            {
                token = held + token;
                held = null;
            }

            if (token.EndsWith(LineEndHyphen))
            {
                var before = token[..^1];
                if (before.Length > 0 && char.IsLetter(before[^1]))
                {
                    held = before;
                    continue;
                }

                token = before;
                if (token.Length == 0)
                {
                    continue;
                }
            }

            while (token.Length > 1 && OpeningBrackets.Contains(token[0]))
            {
                yield return token[..1];
                token = token[1..];
            }

            yield return token;
        }

        if (held is not null)
        {
            yield return held;
        }
    }

    /// <summary>
    /// NFC, spelled out for the letters these files write. The transcription accents with oxia where
    /// every other book of the edition, composed by its converter, writes tonos — the same accent,
    /// which Unicode decomposes one into the other — and a word spelled with each would fold,
    /// compare and link as two words. <see cref="string.Normalize()"/> alone is not enough: under
    /// invariant globalization, which the tests run in, it leaves these letters as they are.
    /// </summary>
    private static string Composed(string token)
    {
        var builder = new StringBuilder(token.Length);
        foreach (var letter in token)
        {
            builder.Append(Singletons.GetValueOrDefault(letter, letter));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// The Greek characters Unicode maps to another by canonical decomposition, one to one: the
    /// polytonic block's oxia vowels to the tonos ones, and the ano teleia and the question mark to
    /// the punctuation they are defined as.
    /// </summary>
    private static readonly Dictionary<char, char> Singletons = new()
    {
        ['\u1F71'] = '\u03AC', ['\u1F73'] = '\u03AD', ['\u1F75'] = '\u03AE', ['\u1F77'] = '\u03AF',
        ['\u1F79'] = '\u03CC', ['\u1F7B'] = '\u03CD', ['\u1F7D'] = '\u03CE', ['\u1FBB'] = '\u0386',
        ['\u1FC9'] = '\u0388', ['\u1FCB'] = '\u0389', ['\u1FD3'] = '\u0390', ['\u1FDB'] = '\u038A',
        ['\u1FE3'] = '\u03B0', ['\u1FEB'] = '\u038E', ['\u1FF9'] = '\u038C', ['\u1FFB'] = '\u038F',
        ['\u1FBE'] = '\u03B9', ['\u0387'] = '\u00B7', ['\u037E'] = '\u003B',
    };

    /// <summary>
    /// A block's edges are a word boundary; an inline mark's are not, because a bracket closes before
    /// the comma that follows it (<c>&lt;add&gt;Ἰσραήλ&lt;/add&gt;,</c>).
    /// </summary>
    private static string Text(XNode node) => node switch
    {
        XText text => text.Value,
        XElement element when NotText.Contains(element.Name.LocalName) => " ",
        XElement element when Inline.Contains(element.Name.LocalName) =>
            string.Concat(element.Nodes().Select(Text)),
        XElement element => " " + string.Concat(element.Nodes().Select(Text)) + " ",
        _ => string.Empty,
    };
}
