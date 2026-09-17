using System.Text;
using System.Xml;
using Essenthos.Core.Strong;
using Essenthos.Core.Utils;
using Essenthos.Core.XmlBible;

namespace Essenthos.Core.Loading.Links;

/// <param name="Text">The word as the edition writes it, one token of the corpus's tokenisation.</param>
/// <param name="Numbers">
/// The Strong numbers of the element the word stands in, in the order the edition writes them, and
/// empty for a word the edition leaves untagged.
/// </param>
/// <param name="Unit">
/// Which rendering the word belongs to, numbered through the file. Words of one rendering are one
/// occurrence of its number, not several; an untagged word is a unit of its own.
/// </param>
internal sealed record EditionWord(string Text, IReadOnlyList<string> Numbers, int Unit);

/// <summary>
/// The Strong-tagged Synodal as swmail/RST publishes it: one OSIS file, every verse in the Synodal's
/// own numbering, and a Strong number on each word the Bob Jones University numbering of 1996 tags —
/// <c>&lt;w lemma="strong:H1254"&gt;сотворил&lt;/w&gt;</c>.
///
/// <para>
/// **It is read as an input to the mapping and nothing else.** Its terms permit use only of the work
/// unmodified (Resources/SynodalStrong/LICENCE.md), so the corpus never stores it: not as a text, not
/// as numbers on the Synodal's words. What reaches the database is the links drawn from it.
/// </para>
///
/// <para>
/// The OSIS header says <c>refSystem Bible.KJV</c> and is wrong about its own file. The verses are
/// numbered the Synodal way — Psalm 22 is <em>Господь – Пастырь мой</em>, Numbers 13:1 is the King
/// James's 12:16, the Romans doxology stands at 14:24–26 — which is what the module's own
/// <c>Versification=Synodal</c> says. So the addresses read here are the edition's and nothing else,
/// and <see cref="SynodalStrongLayer"/> is what places them.
/// </para>
///
/// <para>
/// Section headings and cross-references are the module's apparatus rather than words of the verse,
/// and are skipped.
/// </para>
///
/// <para>
/// **A rendering of several words is one unit.** The edition marks it in two ways, and both are read.
/// It tags a phrase as one element — <em>стал свет</em> over H216. And far more often it tags each
/// word of the rendering apart, with the same number and a <c>G0</c> or <c>H0</c> beside it:
/// <em>начало</em> and <em>быть</em> each carry <c>strong:G1096 strong:G0</c>, and together they are
/// ἐγένετο. 10,381 neighbouring pairs are marked that way, and read as separate words they made John
/// 1:3's three <em>начало быть</em> six claimants for three Greek words, which pairs nothing. Without
/// the zero the same neighbours are two words of the original — <em>смертью умрешь</em> is מוֹת
/// תָּמוּת — so only the marked ones are joined, and only where nothing but a space stands between
/// them. The zero itself names no word and is read as no number.
/// </para>
/// </summary>
internal static class SynodalStrongEdition
{
    private const string VerseElement = "verse";
    private const string WordElement = "w";
    private const string NoteElement = "note";
    private const string TitleElement = "title";
    private const string VerseIdAttribute = "osisID";
    private const string LemmaAttribute = "lemma";
    private const string StrongPrefix = "strong:";
    private const char AddressSeparator = '.';

    public static Dictionary<(int Book, int Chapter, int Verse), List<EditionWord>> Read(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8);
        return Read(reader);
    }

    public static Dictionary<(int Book, int Chapter, int Verse), List<EditionWord>> Read(TextReader source)
    {
        using var xml = XmlReader.Create(source, new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            DtdProcessing = DtdProcessing.Ignore,
        });

        var verses = new Dictionary<(int, int, int), List<EditionWord>>(32_000);
        List<EditionWord>? words = null;
        StringBuilder? tagged = null;
        IReadOnlyList<string> numbers = [];
        var apparatus = 0;
        var unit = 0;
        var current = 0;
        string? previous = null;
        var separated = true;

        while (xml.Read())
        {
            switch (xml.NodeType)
            {
                case XmlNodeType.Element when xml.LocalName == VerseElement && !xml.IsEmptyElement:
                    words = [];
                    verses[Address(xml.GetAttribute(VerseIdAttribute))] = words;
                    separated = true;
                    break;

                case XmlNodeType.Element when xml.LocalName is NoteElement or TitleElement && !xml.IsEmptyElement:
                    apparatus++;
                    separated = true;
                    break;

                case XmlNodeType.Element when xml.LocalName == WordElement && !xml.IsEmptyElement
                                              && apparatus == 0 && words is not null:
                    var lemma = xml.GetAttribute(LemmaAttribute);
                    tagged = new StringBuilder(16);
                    numbers = Numbers(lemma, out var partOfRendering);
                    current = !separated && partOfRendering && lemma == previous ? current : ++unit;
                    previous = lemma;
                    break;

                case XmlNodeType.EndElement when xml.LocalName == VerseElement:
                    words = null;
                    break;

                case XmlNodeType.EndElement when xml.LocalName is NoteElement or TitleElement:
                    apparatus--;
                    break;

                case XmlNodeType.EndElement when xml.LocalName == WordElement && tagged is not null:
                    Add(words!, tagged.ToString(), numbers, current);
                    tagged = null;
                    separated = false;
                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA when apparatus == 0 && words is not null:
                    if (tagged is not null)
                    {
                        tagged.Append(xml.Value);
                        break;
                    }

                    separated |= !string.IsNullOrWhiteSpace(xml.Value);
                    foreach (var token in VerseWords.Parse(xml.Value).Where(token => token.Word.Length > 0))
                    {
                        words.Add(new EditionWord(token.Word, [], ++unit));
                    }

                    break;
            }
        }

        return verses;
    }

    private static void Add(List<EditionWord> words, string text, IReadOnlyList<string> numbers, int unit)
    {
        foreach (var token in VerseWords.Parse(text).Where(token => token.Word.Length > 0))
        {
            words.Add(new EditionWord(token.Word, numbers, unit));
        }
    }

    /// <param name="partOfRendering">
    /// Whether the tag carries the zero the edition writes beside a number one of several words
    /// render.
    /// </param>
    private static IReadOnlyList<string> Numbers(string? lemma, out bool partOfRendering)
    {
        partOfRendering = false;
        if (string.IsNullOrWhiteSpace(lemma))
        {
            return [];
        }

        var numbers = new List<string>(1);
        foreach (var part in lemma.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!part.StartsWith(StrongPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var value = part[StrongPrefix.Length..];
            if (StrongNumbers.Normalize(value) is { } number)
            {
                if (!numbers.Contains(number))
                {
                    numbers.Add(number);
                }
            }
            else if (value.Length > 1 && value.AsSpan(1).Trim('0').IsEmpty)
            {
                partOfRendering = true;
            }
        }

        return numbers;
    }

    private static (int, int, int) Address(string? osisId)
    {
        var parts = osisId?.Split(AddressSeparator) ?? [];
        if (parts.Length != 3
            || BibleBookAbbreviation.GetAbbreviation(parts[0]) is not { } book
            || !int.TryParse(parts[1], out var chapter)
            || !int.TryParse(parts[2], out var verse))
        {
            throw new FormatException(
                $"The verse id \"{osisId}\" is not an OSIS reference this reader can place: it expects " +
                "Book.Chapter.Verse with a book BibleBookAbbreviation knows. If the edition changed, " +
                "re-read it rather than widening the pattern until it parses.");
        }

        return (book.Ordinal, chapter, verse);
    }
}
