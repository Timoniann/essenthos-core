using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Essenthos.Core.Loading.Links;

namespace Essenthos.Core.BetaMasaheft;

/// <summary>One entry as the lexicon gives it, before anything is merged or resolved.</summary>
/// <param name="Refers">
/// Where the entry says nothing of its own and only sends the reader elsewhere — <em>vid. sub ዘ</em> —
/// the headword it sends him to, and the entry id where it links one.
/// </param>
internal sealed record DillmannEntry(
    string Entry,
    string Headword,
    IReadOnlyList<string> Latin,
    IReadOnlyList<string> Greek,
    (string? Headword, string? Entry)? Refers);

/// <summary>An entry as it is loaded: the spellings filed under it included, the entries it absorbed gone.</summary>
internal sealed record DillmannHeadword(
    string Entry,
    string Headword,
    IReadOnlyList<string> Forms,
    IReadOnlyList<string> Latin,
    IReadOnlyList<string> Greek);

/// <summary>
/// Reads Dillmann's Lexicon Linguae Aethiopicae from the digital edition's TEI, one file per entry.
///
/// <para>
/// Only Dillmann's own sense is read — the one marked <c>source="#dillmann"</c>. The TraCES project
/// added English senses to some entries and entries of its own from later literature, several
/// glossed from Leslau's dictionary; those are not taken, so what the reader is shown under
/// Dillmann's name is Dillmann's.
/// </para>
///
/// <para>
/// His Latin glosses are the <c>cit type="translation"</c> of the sense, in his order, except those
/// inside his square-bracketed notes on the cognates, which gloss the Arabic or the Syriac and not
/// the Ge'ez: <em>ብህለ</em> opens with what the Arabic <em>بهل</em> means. An entry with no gloss of
/// that kind — most proper names — is given the plain Latin it consists of, where that is a word or
/// two and nothing else: <em>Epicurus</em>.
/// </para>
/// </summary>
internal static class DillmannLexicon
{
    private static readonly XNamespace Tei = "http://www.tei-c.org/ns/1.0";

    private static readonly XNamespace Xml = XNamespace.Xml;

    private const string Dillmann = "#dillmann";

    private const string See = "videas";

    /// <summary>The folders of the repository that hold entries, as the fetch script keeps them.</summary>
    public static readonly string[] Folders = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "new", "new1"];

    /// <summary>A gloss has at least this many letters: <em>ê</em> in "the vowel ê" is not one.</summary>
    private const int ShortestGloss = 2;

    /// <summary>More than this many Latin glosses is a long article, and the first ones are the meaning.</summary>
    private const int LatinKept = 12;

    private static readonly Regex PlainLatin = new(@"^[A-Za-zÀ-ÿ' -]{2,40}$", RegexOptions.Compiled);

    private static readonly Regex LinkedEntry = new(@"/lemma/(L\w+)", RegexOptions.Compiled);

    public static IEnumerable<DillmannEntry> Read(string folder) =>
        Folders
            .Select(name => Path.Combine(folder, name))
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "L*.xml"))
            .Order(StringComparer.Ordinal)
            .Select(file => Parse(XDocument.Load(file)))
            .OfType<DillmannEntry>();

    public static DillmannEntry? Parse(XDocument document)
    {
        var entry = document.Descendants(Tei + "entry").FirstOrDefault();
        var headword = entry?.Element(Tei + "form")?.Element(Tei + "foreign") is { } form
            ? GeezStemmer.Bare(form.Value)
            : string.Empty;
        var senses = entry?.Elements(Tei + "sense").Where(sense => (string?)sense.Attribute("source") == Dillmann).ToList() ?? [];
        if (entry is null || headword.Length == 0 || senses.Count == 0)
        {
            return null;
        }

        var latin = new List<string>();
        var greek = new List<string>();
        foreach (var sense in senses)
        {
            Glosses(sense, 0, latin);
            greek.AddRange(sense.Descendants(Tei + "foreign")
                .Where(foreign => Language(foreign) == "grc")
                .Select(foreign => Collapse(foreign.Value))
                .Where(text => text.Length > 0));
        }

        var main = senses[0];
        if (latin.Count == 0 && PlainLatin.Match(Own(main).Trim().TrimEnd('.').Trim()) is { Success: true } name)
        {
            latin.Add(name.Value);
        }

        var refers = latin.Count == 0 && greek.Count == 0 && SendsElsewhere(main)
            ? ((string? Headword, string? Entry)?)(
                main.Descendants(Tei + "foreign").LastOrDefault(foreign => Language(foreign) == "gez") is { } there
                    ? GeezStemmer.Bare(there.Value)
                    : null,
                main.Descendants(Tei + "ref").Select(reference => LinkedEntry.Match((string?)reference.Attribute("target") ?? string.Empty))
                    .FirstOrDefault(match => match.Success)?.Groups[1].Value)
            : null;

        return new DillmannEntry(
            (string?)entry.Attribute(Xml + "id") ?? string.Empty,
            headword,
            [.. latin.Distinct(StringComparer.Ordinal).Take(LatinKept)],
            [.. greek.Distinct(StringComparer.Ordinal)],
            refers);
    }

    /// <summary>
    /// The entries as they are loaded. One that only sends the reader to another headword becomes a
    /// spelling of that headword's entry, where the headword names one entry; otherwise it is
    /// dropped, having nothing to show, as is one with neither a gloss nor Greek — a name Dillmann
    /// explains in Ge'ez or cites without a Latin form. One whose glosses and Greek are all another entry's under the
    /// same headword — the same lexeme digitised twice — is folded into the fuller one.
    /// </summary>
    public static IReadOnlyList<DillmannHeadword> Headwords(IEnumerable<DillmannEntry> entries)
    {
        var all = entries.ToList();
        var own = all.Where(entry => entry.Refers is null && (entry.Latin.Count > 0 || entry.Greek.Count > 0)).ToList();
        var byId = own.ToDictionary(entry => entry.Entry, StringComparer.Ordinal);
        var byHeadword = own.ToLookup(entry => entry.Headword, StringComparer.Ordinal);

        var forms = own.ToDictionary(entry => entry.Entry, entry => new List<string> { entry.Headword }, StringComparer.Ordinal);
        foreach (var pointer in all.Where(entry => entry.Refers is not null))
        {
            var (headword, id) = pointer.Refers!.Value;
            var target = id is not null && byId.TryGetValue(id, out var linked)
                ? linked
                : headword is not null && headword != pointer.Headword && byHeadword[headword].ToList() is [var only]
                    ? only
                    : null;
            if (target is not null && !forms[target.Entry].Contains(pointer.Headword))
            {
                forms[target.Entry].Add(pointer.Headword);
            }
        }

        var absorbed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in byHeadword)
        {
            var namesakes = group.ToList();
            foreach (var entry in namesakes)
            {
                var fuller = namesakes.FirstOrDefault(other => other != entry
                                                               && !absorbed.Contains(other.Entry)
                                                               && entry.Latin.All(other.Latin.Contains)
                                                               && entry.Greek.All(other.Greek.Contains)
                                                               && Weight(other) >= Weight(entry));
                if (fuller is null)
                {
                    continue;
                }

                absorbed.Add(entry.Entry);
                forms[fuller.Entry].AddRange(forms[entry.Entry].Where(form => !forms[fuller.Entry].Contains(form)));
            }
        }

        return
        [
            .. own
                .Where(entry => !absorbed.Contains(entry.Entry))
                .Select(entry => new DillmannHeadword(entry.Entry, entry.Headword, forms[entry.Entry], entry.Latin, entry.Greek)),
        ];

        static int Weight(DillmannEntry entry) => entry.Latin.Count + entry.Greek.Count;
    }

    /// <summary>
    /// The sense's Latin glosses outside square brackets. The brackets are Dillmann's, in the text
    /// between the elements, so their depth is counted as the text is walked.
    /// </summary>
    private static int Glosses(XElement element, int depth, List<string> latin)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    depth += text.Value.Count(c => c == '[') - text.Value.Count(c => c == ']');
                    break;
                case XElement { Name.LocalName: "cit" } cit when (string?)cit.Attribute("type") == "translation" && Language(cit) == "la":
                    if (depth <= 0 && cit.Element(Tei + "quote") is { } quote
                                   && Collapse(quote.Value).Trim(' ', ',', ';', ':') is var gloss
                                   && gloss.Count(char.IsLetter) >= ShortestGloss)
                    {
                        latin.Add(gloss);
                    }

                    break;
                case XElement { Name.LocalName: "cit" }:
                    break;
                case XElement child:
                    depth = Glosses(child, depth, latin);
                    break;
            }
        }

        return depth;
    }

    private static bool SendsElsewhere(XElement sense) =>
        sense.Descendants(Tei + "lbl").Any(label => ((string?)label.Attribute("expand") ?? string.Empty).StartsWith(See, StringComparison.Ordinal))
        || Own(sense).Contains("vid.", StringComparison.Ordinal);

    /// <summary>The sense's own words, without its references, sub-senses and grammatical labels.</summary>
    private static string Own(XElement sense)
    {
        var text = new StringBuilder();
        Collect(sense);
        return Collapse(text.ToString());

        void Collect(XElement element)
        {
            foreach (var node in element.Nodes())
            {
                if (node is XText plain)
                {
                    text.Append(plain.Value);
                }
                else if (node is XElement child && child.Name.LocalName is not ("ref" or "bibl" or "sense" or "gramGrp" or "lbl" or "pos"))
                {
                    Collect(child);
                }
            }
        }
    }

    private static string? Language(XElement element) => (string?)element.Attribute(Xml + "lang");

    private static string Collapse(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
