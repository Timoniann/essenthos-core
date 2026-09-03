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
    /// What Strong's printed text puts between a definition and the King James renderings that
    /// follow it. It belongs to neither, and the Greek file leaves it in the renderings.
    /// </summary>
    private const string RenderingSeparator = ":--";

    /// <summary>
    /// The same separator where the file cut between the two elements after the colon, leaving the
    /// colon on the end of the definition and these two characters at the head of the renderings.
    /// </summary>
    private const string SplitSeparator = "--";

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

            var definition = CleanText(GetInnerTextWithRefs(entry.Element("strongs_def")));
            var derivation = CleanText(GetInnerTextWithRefs(entry.Element("strongs_derivation")));
            var kjvDefinition = CleanText(GetInnerTextWithRefs(entry.Element("kjv_def")));

            // Strong's Greek numbering has holes in it: 2717, and the whole block 3203-3302, were
            // never assigned to a word. The source spells each one out as an entry whose entire
            // body is the words "Not Used" — 101 of the 5,624, with no lemma, no definition and
            // nothing to say. Stored, they are 101 rows of nulls, and a reader who asks for one
            // gets an empty dictionary page answered 200 while a number past the end of the range
            // is answered 404. An entry with nothing in it is not an entry.
            if (lemma is null && definition is null && derivation is null && kjvDefinition is null)
            {
                continue;
            }

            (definition, derivation, kjvDefinition) = Untangle(definition, derivation, kjvDefinition);

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
                Definition = definition,
                Derivation = derivation,
                KjvDefinition = kjvDefinition,
                SeeAlso = seeAlso,
            });
        }

        return entries;
    }

    /// <summary>
    /// The Greek entry is one printed paragraph — derivation, then definition, then the King James
    /// renderings after a <c>:--</c> — and the XML puts it in three elements without taking the
    /// separator out or always cutting in the same place. Three shapes come out of that, and all
    /// three are visible on the page if they are stored as they are found.
    ///
    /// <para>
    /// <b>The separator is kept.</b> 5,510 of the 5,523 assigned entries open their renderings with
    /// the literal <c>:--</c>, and 10 more open with <c>--</c> because the colon stayed behind on
    /// the definition. The Hebrew file has none: 0 of 8,674. So it is a delimiter the Greek side
    /// failed to drop rather than anything Strong wrote, and the trailing colon it leaves on those
    /// 10 definitions is the other half of the same mark.
    /// </para>
    ///
    /// <para>
    /// <b>The definition runs on past it.</b> Three entries carry a second <c>:--</c> inside the
    /// renderings, and what stands before it is never a rendering: G2384 Ἰακώβ reads
    /// <c>:--also an Israelite:--Jacob.</c>, and <em>also an Israelite</em> is the rest of the
    /// definition. Nobody translates Ἰακώβ as <em>also an Israelite</em>. For G2022 ἐπιχέω that
    /// stray clause is the whole definition — its <c>strongs_def</c> element is one space.
    /// </para>
    ///
    /// <para>
    /// <b>There is no definition element at all.</b> 19 entries have only a derivation, and it
    /// carries the sense: G1473 ἐγώ reads <em>a primary pronoun of the first person I</em> and
    /// G2570 καλός <em>of uncertain affinity; properly, beautiful, but chiefly (figuratively)
    /// good</em>. Left where they are, two of the commonest words in the New Testament answer with
    /// an empty definition. There is no reliable place to cut the etymology off the sense — G976
    /// has no etymology and G1473 fuses the two in one clause — so the block moves whole into the
    /// field a reader reads. Nothing downstream loses by it: measured over the file, not one of
    /// those 19 derivations states a form or a compound, so <see cref="GreekFormDerivations"/>
    /// reads nothing out of any of them.
    /// </para>
    /// </summary>
    private static (string? Definition, string? Derivation, string? Renderings) Untangle(
        string? definition,
        string? derivation,
        string? renderings)
    {
        if (renderings is not null)
        {
            if (renderings.StartsWith(RenderingSeparator, StringComparison.Ordinal))
            {
                renderings = CleanText(renderings[RenderingSeparator.Length..]);
            }
            else if (renderings.StartsWith(SplitSeparator, StringComparison.Ordinal))
            {
                renderings = CleanText(renderings[SplitSeparator.Length..]);
                if (definition is not null && definition.EndsWith(':'))
                {
                    definition = CleanText(definition[..^1]);
                }
            }
        }

        var stray = renderings?.IndexOf(RenderingSeparator, StringComparison.Ordinal) ?? -1;
        if (stray >= 0)
        {
            var strayed = CleanText(renderings![..stray]);
            renderings = CleanText(renderings[(stray + RenderingSeparator.Length)..]);
            definition = definition is null || strayed is null
                ? definition ?? strayed
                : $"{definition}; {strayed}";
        }

        if (definition is null && derivation is not null)
        {
            (definition, derivation) = (derivation, null);
        }

        return (definition, derivation, renderings);
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

