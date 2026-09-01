using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Essenthos.Core.Strong;

public class StrongParsedEntry
{
    public required string StrongNumber { get; init; }
    public string? Lemma { get; init; }
    public string? Transliteration { get; init; }
    public string? Pronunciation { get; init; }
    public string? Definition { get; init; }
    public string? Derivation { get; init; }
    public string? KjvDefinition { get; init; }
    public string? Morphology { get; init; }
    public string? DetailedDefinition { get; init; }
    public string? SeeAlso { get; init; }
    public string? SourceLanguage { get; init; }
    public string? TwotReference { get; init; }
}

public partial class StrongXmlParser
{
    /// <summary>
    /// Parses the Greek Strong's XML file (format from openscriptures/strongs).
    /// </summary>
    public List<StrongParsedEntry> ParseGreek(string xmlContent)
    {
        var entries = new List<StrongParsedEntry>();

        var doc = XDocument.Parse(xmlContent);
        var entryElements = doc.Descendants("entry");

        foreach (var entry in entryElements)
        {
            var strongsAttr = entry.Attribute("strongs")?.Value;
            if (string.IsNullOrEmpty(strongsAttr)) continue;

            var strongNumber = "G" + int.Parse(strongsAttr);

            var greekElement = entry.Element("greek");
            var lemma = greekElement?.Attribute("unicode")?.Value;
            var transliteration = greekElement?.Attribute("translit")?.Value;

            var pronunciation = entry.Element("pronunciation")?.Attribute("strongs")?.Value;

            var definition = GetInnerTextWithRefs(entry.Element("strongs_def"));
            var derivation = GetInnerTextWithRefs(entry.Element("strongs_derivation"));
            var kjvDefinition = GetInnerTextWithRefs(entry.Element("kjv_def"));

            // Parse <see> elements for cross-references
            var seeElements = entry.Elements("see").ToList();
            string? seeAlso = null;
            if (seeElements.Count > 0)
            {
                var refs = seeElements.Select(see =>
                    {
                        var lang = see.Attribute("language")?.Value;
                        var num = see.Attribute("strongs")?.Value;
                        if (num == null) return null;
                        var prefix = lang == "HEBREW" ? "H" : "G";
                        return prefix + int.Parse(num);
                    })
                    .Where(r => r != null)
                    .ToList();
                if (refs.Count > 0) seeAlso = string.Join(",", refs);
            }

            entries.Add(new StrongParsedEntry
            {
                StrongNumber = strongNumber,
                Lemma = lemma,
                Transliteration = transliteration,
                Pronunciation = pronunciation,
                Definition = CleanText(definition),
                Derivation = CleanText(derivation),
                KjvDefinition = CleanText(kjvDefinition),
                SeeAlso = seeAlso,
            });
        }

        return entries;
    }

    /// <summary>
    /// Parses the Hebrew Strong's XML file (OSIS-style format from openscriptures/strongs).
    /// </summary>
    public List<StrongParsedEntry> ParseHebrew(string xmlContent)
    {
        var entries = new List<StrongParsedEntry>();

        var doc = XDocument.Parse(xmlContent);
        XNamespace ns = "http://www.bibletechnologies.net/2003/OSIS/namespace";
        var entryElements = doc.Descendants(ns + "div")
            .Where(e => e.Attribute("type")?.Value == "entry");

        foreach (var entry in entryElements)
        {
            var nAttr = entry.Attribute("n")?.Value;
            if (string.IsNullOrEmpty(nAttr)) continue;

            var strongNumber = "H" + nAttr;

            var wElement = entry.Element(ns + "w");
            var lemma = wElement?.Attribute("lemma")?.Value;
            var transliteration = wElement?.Attribute("xlit")?.Value;
            var pronunciation = wElement?.Attribute("POS")?.Value;
            var morphology = wElement?.Attribute("morph")?.Value;
            var sourceLanguage = wElement?.Attribute(XNamespace.Xml + "lang")?.Value;
            var twotReference = wElement?.Attribute("gloss")?.Value;

            // Notes
            var notes = entry.Elements(ns + "note").ToList();
            var exegesis = notes.FirstOrDefault(n => n.Attribute("type")?.Value == "exegesis");
            var explanation = notes.FirstOrDefault(n => n.Attribute("type")?.Value == "explanation");
            var translation = notes.FirstOrDefault(n => n.Attribute("type")?.Value == "translation");

            var derivation = GetHebrewNoteText(exegesis, ns);
            var definition = GetHebrewNoteText(explanation, ns);
            var kjvDefinition = GetHebrewNoteText(translation, ns);

            // Detailed definition from list items
            var listElement = entry.Element(ns + "list");
            string? detailedDefinition = null;
            if (listElement != null)
            {
                var items = listElement.Elements(ns + "item")
                    .Select(item => item.Value.Trim())
                    .Where(text => !string.IsNullOrEmpty(text))
                    .ToList();
                if (items.Count > 0)
                {
                    detailedDefinition = string.Join("\n", items);
                }
            }

            // Greek cross-references from <foreign xml:lang="grc"> block
            var foreignElement = entry.Element(ns + "foreign");
            string? seeAlso = null;
            if (foreignElement?.Attribute(XNamespace.Xml + "lang")?.Value == "grc")
            {
                var glossRefs = foreignElement.Elements(ns + "w")
                    .Select(w => w.Attribute("gloss")?.Value)
                    .Where(g => g != null && g.StartsWith("G:"))
                    .Select(g => "G" + g!.Substring(2))
                    .ToList();
                if (glossRefs.Count > 0) seeAlso = string.Join(",", glossRefs);
            }

            entries.Add(new StrongParsedEntry
            {
                StrongNumber = strongNumber,
                Lemma = lemma,
                Transliteration = transliteration,
                Pronunciation = pronunciation,
                Morphology = morphology,
                SourceLanguage = sourceLanguage,
                TwotReference = twotReference,
                Definition = CleanText(definition),
                Derivation = CleanText(derivation),
                KjvDefinition = CleanText(kjvDefinition),
                DetailedDefinition = detailedDefinition,
                SeeAlso = seeAlso,
            });
        }

        return entries;
    }

    /// <summary>
    /// Extracts text from a Greek XML element, replacing inline reference elements
    /// (strongsref, greek, pronunciation) with readable text so references are not lost.
    /// E.g. &lt;strongsref language="GREEK" strongs="3786"/&gt; becomes "G3786".
    /// </summary>
    private static string? GetInnerTextWithRefs(XElement? element)
    {
        if (element == null) return null;

        var sb = new StringBuilder();
        foreach (var node in element.Nodes())
        {
            if (node is XText text)
            {
                sb.Append(text.Value);
            }
            else if (node is XElement child)
            {
                switch (child.Name.LocalName)
                {
                    case "strongsref":
                    {
                        var lang = child.Attribute("language")?.Value;
                        var num = child.Attribute("strongs")?.Value;
                        if (num != null)
                        {
                            var prefix = lang == "HEBREW" ? "H" : "G";
                            sb.Append(prefix + int.Parse(num));
                        }

                        break;
                    }
                    case "greek":
                    {
                        var unicode = child.Attribute("unicode")?.Value;
                        if (unicode != null)
                            sb.Append(unicode);
                        break;
                    }
                    case "pronunciation":
                    {
                        var pron = child.Attribute("strongs")?.Value;
                        if (pron != null)
                            sb.Append(pron);
                        break;
                    }
                    default:
                        // For any other element (latin, etc.), just take its text value
                        sb.Append(child.Value);
                        break;
                }
            }
        }

        var result = sb.ToString();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    /// <summary>
    /// Extracts text from a Hebrew note element, replacing inline &lt;w&gt; elements
    /// with their lemma text and Strong's reference (from src attribute), and &lt;hi&gt;
    /// elements with their text content.
    /// E.g. &lt;w lemma="אָבִיב" src="24"/&gt; becomes "אָבִיב (H24)".
    /// </summary>
    private static string? GetHebrewNoteText(XElement? element, XNamespace ns)
    {
        if (element == null) return null;

        var sb = new StringBuilder();
        foreach (var node in element.Nodes())
        {
            if (node is XText text)
            {
                sb.Append(text.Value);
            }
            else if (node is XElement child)
            {
                var localName = child.Name.LocalName;
                if (localName == "w")
                {
                    var lemma = child.Attribute("lemma")?.Value ?? child.Value;
                    var src = child.Attribute("src")?.Value;
                    if (!string.IsNullOrEmpty(lemma))
                        sb.Append(lemma);
                    if (!string.IsNullOrEmpty(src))
                        sb.Append(" (H" + src + ")");
                }
                else if (localName == "hi")
                {
                    sb.Append(child.Value);
                }
                else
                {
                    sb.Append(child.Value);
                }
            }
        }

        var result = sb.ToString();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    /// <summary>
    /// Cleans up whitespace in text: collapses multiple spaces/newlines into single spaces and trims.
    /// </summary>
    private static string? CleanText(string? text)
    {
        if (text == null) return null;

        var cleaned = WhitespaceRegex().Replace(text, " ").Trim();
        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

