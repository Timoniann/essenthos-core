using System.Text.RegularExpressions;

namespace Essenthos.Core.Strong;

/// <summary>What Strong's own part of speech says a headword names.</summary>
public enum StrongNameKind
{
    /// <summary>
    /// Not a name. A common noun, a verb, an adjective — a gentilic adjective included, which is
    /// the case worth naming: <em>Arvadite</em> is parted <c>a</c> and describes a people rather
    /// than naming the man the encyclopedia files under it.
    /// </summary>
    Common,

    Person,

    Place,

    People,

    /// <summary>
    /// A god. Apart from <see cref="Person"/> because the encyclopedia holds no kind for one and a
    /// register of men must not quietly gain a deity.
    /// </summary>
    Deity,
}

/// <param name="Item">The number Strong gave the clause, kept as his rather than as a position.</param>
/// <param name="Kind">
/// What his tag says this one is, or null where the entry parts the headword two ways at once and
/// writes no heading over the clause — which decides nothing and must not be read as if it did.
/// </param>
/// <param name="Says">The clause in his words, for a reader shown the claim it stands behind.</param>
public readonly record struct StrongBearer(int Item, StrongNameKind? Kind, string Says);

/// <summary>
/// Whom Strong's dictionary heads a name for, read off his own two fields and nothing else.
///
/// <c>morphology</c> is his part of speech — <c>n-pr-m</c>, <c>n-pr-loc</c> — and
/// <c>detailed_definition</c> is a numbered list of the bearers, with a heading over each run of
/// clauses wherever one entry heads two kinds at once: <em>Berachah</em> is <c>n pr m</c> for
/// David's warrior and <c>n pr loc</c> for the valley near Tekoa, and he writes both words above
/// the two clauses.
///
/// <para>
/// <strong>Nothing here reads his prose.</strong> A rule that classified a clause by the nouns in
/// it calls Gaddiel a tribe because he is <em>the spy from the tribe of Zebulun</em>, and Medad a
/// place because he prophesied <em>in the camp</em>. What the clause says is carried out whole for
/// the reader and never parsed: the tag is the evidence, and where he wrote no tag the answer is
/// null rather than a guess (RUL-0024).
/// </para>
///
/// <para>
/// <strong>It is Hebrew only, and that is a fact about the dataset rather than a choice.</strong>
/// All 8,674 Hebrew entries carry a part of speech and a numbered definition; not one of the 5,523
/// Greek entries carries either. So a name attested only in Greek has no witness of this kind at
/// all, and the caller is left to say so rather than being handed an empty list to misread.
/// </para>
/// </summary>
public static partial class StrongNameEntries
{
    /// <summary>
    /// A numbered bearer: <c>1)</c> at the head of a line. Sub-senses — <c>1a)</c>, <c>1a1)</c> —
    /// are deliberately not matched, because they divide one bearer's clause rather than adding a
    /// bearer.
    /// </summary>
    [GeneratedRegex(@"^\s*(\d+)\)\s*(.*)$")]
    private static partial Regex Item();

    /// <summary>
    /// A line that is a part of speech and nothing else, which is how one entry says that what
    /// follows is a different kind of thing from what came before.
    /// </summary>
    [GeneratedRegex(@"^(?:n|adj|v|adv|pron|prep|conj|interj|part|prt)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Heading();

    /// <summary>Strong's mark for a proper name, in the two spellings the dictionary uses.</summary>
    [GeneratedRegex(@"\bn[- ]?pr\b|\bnp\b", RegexOptions.IgnoreCase)]
    private static partial Regex Proper();

    [GeneratedRegex(@"[^a-z]+")]
    private static partial Regex NotALetter();

    /// <summary>
    /// The longest a line may run and still be a heading. A heading is two or three words; a clause
    /// that happens to open with one of those words is a sentence, and the difference has to be
    /// drawn somewhere.
    /// </summary>
    private const int HeadingRoom = 40;

    /// <summary>
    /// Whether Strong heads a proper name here at all — in the part of speech, or in a heading over
    /// one run of clauses where the entry is two kinds of word at once.
    ///
    /// It is asked before anything else because it is the question his tag answers best and the one
    /// no other field can: <em>a stream, especially a winter torrent</em> and <em>Abanah, a river
    /// near Damascus</em> both name a river, and only the part of speech says that the first is the
    /// common noun and the second the name of one.
    /// </summary>
    public static bool HeadsAName(string? morphology, string? detailedDefinition)
    {
        if (Proper().IsMatch(morphology ?? string.Empty))
        {
            return true;
        }

        return Headings(detailedDefinition).Any(heading => Proper().IsMatch(heading));
    }

    /// <summary>
    /// The bearers the entry enumerates, each under the kind Strong tagged it.
    ///
    /// The part of speech carries every clause until a heading says otherwise, which is the ordinary
    /// case: <c>n-pr-m</c> and one clause is one man. Where the part of speech names two kinds and
    /// no heading stands over a clause, that clause's kind is null — <c>n-pr-m n-pr-loc</c> says
    /// the entry holds a man and a place and does not say which of them the clause is.
    /// </summary>
    public static IReadOnlyList<StrongBearer> Bearers(string? morphology, string? detailedDefinition)
    {
        var stated = Kinds(morphology);
        var section = stated.Count == 1 ? stated[0] : (StrongNameKind?)null;

        var bearers = new List<StrongBearer>();
        var says = new List<string>();

        // Closes twice on the clause a heading stands after — once for the heading and once for the
        // clause below it. So nothing gathered leaves what was said alone, and the headword line,
        // which belongs to no bearer, is dropped rather than folded into the first clause.
        void Close()
        {
            if (bearers.Count > 0 && says.Count > 0)
            {
                bearers[^1] = bearers[^1] with { Says = string.Join(' ', says).Trim() };
            }

            says.Clear();
        }

        foreach (var line in Lines(detailedDefinition))
        {
            if (Item().Match(line) is { Success: true } item)
            {
                Close();
                bearers.Add(new StrongBearer(int.Parse(item.Groups[1].Value), section, string.Empty));
                says.Add(item.Groups[2].Value);
                continue;
            }

            if (IsHeading(line))
            {
                Close();
                section = Kind(line);
                continue;
            }

            says.Add(line);
        }

        Close();
        return bearers;
    }

    /// <summary>
    /// What one part of speech names. <c>pr</c> is the whole of it: without that mark the headword
    /// is a word rather than a name, however the rest of the code reads.
    /// </summary>
    private static StrongNameKind Kind(string code)
    {
        var parts = NotALetter().Split(code.ToLowerInvariant()).Where(part => part.Length > 0).ToList();
        if (!parts.Contains("pr"))
        {
            return StrongNameKind.Common;
        }

        if (parts.Contains("deity"))
        {
            return StrongNameKind.Deity;
        }

        if (parts.Contains("loc") || parts.Contains("terr"))
        {
            return StrongNameKind.Place;
        }

        if (parts.Contains("gent") || parts.Contains("patr")
                                   || parts.Contains("people") || parts.Contains("coll"))
        {
            return StrongNameKind.People;
        }

        return StrongNameKind.Person;
    }

    /// <summary>
    /// The kinds the part of speech names, in order and without repeats. Two of them —
    /// <c>n-pr-m n-pr-loc</c> — is Strong saying that the entry heads a man and a place, which is
    /// why a clause with no heading over it cannot be assigned to either.
    /// </summary>
    private static IReadOnlyList<StrongNameKind> Kinds(string? morphology)
    {
        var kinds = new List<StrongNameKind>();
        foreach (var code in (morphology ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var kind = Kind(code);
            if (!kinds.Contains(kind))
            {
                kinds.Add(kind);
            }
        }

        return kinds;
    }

    private static IEnumerable<string> Headings(string? detailedDefinition) =>
        Lines(detailedDefinition).Where(IsHeading);

    /// <summary>
    /// A heading is short, opens with a part of speech and states no gloss. The last of those is
    /// what keeps the opening line out: <em>Berachah = "blessing"</em> is the name and its meaning,
    /// and it opens with an <c>n</c> often enough to be caught by the other two.
    /// </summary>
    private static bool IsHeading(string line) =>
        line.Length <= HeadingRoom && !line.Contains('=') && Heading().IsMatch(line);

    private static IEnumerable<string> Lines(string? text) =>
        (text ?? string.Empty)
        .Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 0);
}
