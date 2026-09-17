using System.Xml;

namespace Essenthos.Core.Glaux;

/// <summary>
/// The GLAUx treebank format, read one word at a time.
///
/// A GLAUx text is a flat <c>treebank/sentence/word</c> tree with everything on attributes:
///
/// <code>
/// &lt;word id="107884004" form="κριτὰς" div_chapter="1" div_section="1.1" lemma="κριτής"
///       postag="n-p---ma-" head="107884002" relation="OBJ" animacy="human"/&gt;
/// </code>
///
/// **It streams, and that is deliberate.** The 57 Septuagint books are 111 MB of XML for 611,561
/// annotated words, and the only thing built from them is a form-to-lemma table of a few tens of
/// thousands of rows. Loading each document into an <see cref="System.Xml.Linq.XDocument"/> the way
/// <c>NestleParser</c> does would hold a hundred times the finished dictionary in memory to produce
/// it.
///
/// Punctuation is tokenised as its own <c>word</c> element with no <c>lemma</c> attribute, and is
/// skipped: a token with no lemma is exactly the token this reader exists to find lemmas for.
/// </summary>
internal static class GlauxReader
{
    private const string WordElement = "word";

    private static readonly XmlReaderSettings Settings = new()
    {
        IgnoreComments = true,
        IgnoreWhitespace = true,
        IgnoreProcessingInstructions = true,
        DtdProcessing = DtdProcessing.Prohibit,
    };

    /// <summary>Every lemmatised word of one GLAUx document, in document order.</summary>
    public static IEnumerable<GlauxWord> Read(string path)
    {
        using var stream = File.OpenRead(path);
        foreach (var word in Read(stream))
        {
            yield return word;
        }
    }

    /// <summary>The same, over a stream, so a test does not need a file.</summary>
    public static IEnumerable<GlauxWord> Read(Stream stream)
    {
        using var reader = XmlReader.Create(stream, Settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != WordElement)
            {
                continue;
            }

            var form = reader.GetAttribute("form");
            var lemma = reader.GetAttribute("lemma");
            if (string.IsNullOrEmpty(form) || string.IsNullOrEmpty(lemma))
            {
                continue;
            }

            var postag = reader.GetAttribute("postag");
            yield return new GlauxWord(form, lemma, postag is { Length: > 0 } ? postag[0] : '-');
        }
    }
}
