using System.Text;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Strong;

/// <summary>One piece of an etymology as the file writes it: words, or a reference to another entry.</summary>
/// <param name="Text">What a reader sees for it: the words, or the lemma and its number.</param>
/// <param name="Number">The entry a reference points at, as <c>H1004</c>; null for words.</param>
public readonly record struct EtymologyPart(string Text, string? Number = null);

/// <param name="ToNumber">The other entry; null only for <see cref="StrongRelationKinds.Primitive"/>.</param>
/// <param name="Hedged">Strong qualified it himself — <em>probably</em>, <em>perhaps</em>, <em>apparently</em>, <em>presumed</em>.</param>
/// <param name="Position">Its order among the entry's references, from 1; 0 for the entry's own primitive statement.</param>
/// <param name="Statement">The clause it was read from, as Strong wrote it, so a wrong reading can be traced back.</param>
public readonly record struct StatedRelation(
    string FromNumber,
    string? ToNumber,
    string Kind,
    bool Hedged,
    int Position,
    string Statement);

/// <summary>
/// The relations Strong states between his entries, read out of the etymology the parser otherwise
/// flattens to prose. H1006 is <em>the same as</em> H1004, H1007 is <em>from</em> H1004
/// <em>and</em> H205, G2076 is the third person singular of G1510.
///
/// <para>
/// A reference is read with the words between it and the reference or clause before it, and the
/// kind comes from those words. A reference with nothing of its own but a joining <em>and</em> takes
/// the kind of the one before it in the clause, because that is how Strong writes a compound —
/// <em>from X and Y</em>. A hedge anywhere in the clause before a reference hedges it.
/// </para>
/// </summary>
public static partial class StrongEtymology
{
    /// <summary>The relations stated in one entry's etymology, in the order Strong wrote them.</summary>
    public static IReadOnlyList<StatedRelation> Read(string number, IReadOnlyList<EtymologyPart> parts)
    {
        var greek = number.StartsWith('G');
        var relations = new List<StatedRelation>();
        var clause = new StringBuilder();
        var lead = new StringBuilder();
        var pending = new List<(string To, string Kind, bool Hedged, int Position)>();
        string? previousKind = null;
        var previousHedged = false;
        var position = 0;
        var primitive = false;
        var head = string.Empty;

        void Close()
        {
            var statement = Statement(clause.ToString());
            if (!primitive && Primitive().IsMatch(Words(pending.Count == 0 ? clause.ToString() : head)))
            {
                primitive = true;
                relations.Add(new StatedRelation(number, null, StrongRelationKinds.Primitive, false, 0, statement));
            }

            relations.AddRange(pending.Select(p => new StatedRelation(number, p.To, p.Kind, p.Hedged, p.Position, statement)));
            pending.Clear();
            clause.Clear();
            lead.Clear();
            previousKind = null;
            previousHedged = false;
        }

        foreach (var part in parts)
        {
            if (part.Number is null)
            {
                var text = part.Text;
                int stop;
                while ((stop = text.IndexOf(';')) >= 0)
                {
                    clause.Append(text[..stop]);
                    Close();
                    text = text[(stop + 1)..];
                }

                clause.Append(text);
                lead.Append(text);
                continue;
            }

            position++;
            if (pending.Count == 0)
            {
                head = clause.ToString();
            }

            var (kind, own) = Classify(Words(lead.ToString()), greek);
            var hedged = Hedge().IsMatch(clause.ToString());
            if (!own && previousKind is not null)
            {
                kind = previousKind;
                hedged |= previousHedged;
            }

            if (own && previousKind is StrongRelationKinds.From && kind == StrongRelationKinds.SameAs
                && Joiner().IsMatch(FirstWord(Words(lead.ToString()))))
            {
                kind = StrongRelationKinds.SameRootAs;
            }

            pending.Add((part.Number, kind, hedged, position));
            clause.Append(part.Text);
            lead.Clear();
            previousKind = kind;
            previousHedged = hedged;
        }

        Close();
        return relations;
    }

    /// <summary>
    /// The kind the words before a reference state, and whether they state one at all: a lead of
    /// nothing but <em>and</em> states none, and the reference takes the kind before it.
    /// </summary>
    internal static (string Kind, bool Own) Classify(string lead, bool greek)
    {
        var bare = Unbracketed(lead);
        if (Compare().IsMatch(lead))
        {
            return (StrongRelationKinds.Compare, true);
        }

        if (bare.Length == 0 || Joiner().IsMatch(bare))
        {
            return (StrongRelationKinds.Unclassified, false);
        }

        if (Corresponding().IsMatch(bare))
        {
            return (StrongRelationKinds.CorrespondsTo, true);
        }

        if (Origin().IsMatch(lead))
        {
            return (StrongRelationKinds.LoanFrom, true);
        }

        var from = FromWord().IsMatch(bare);
        if (greek && GreekFormDerivations.NamesAForm(lead))
        {
            return (StrongRelationKinds.FormOf, true);
        }

        if (SameRoot().IsMatch(bare))
        {
            return (StrongRelationKinds.SameRootAs, true);
        }

        if (Same().IsMatch(bare))
        {
            return (StrongRelationKinds.SameAs, true);
        }

        if (Contracted().IsMatch(bare))
        {
            return (StrongRelationKinds.ContractedFrom, true);
        }

        if (!greek && !from && HebrewForm().IsMatch(bare))
        {
            return (StrongRelationKinds.FormOf, true);
        }

        if (!from && Variation().IsMatch(bare))
        {
            return (StrongRelationKinds.Variant, true);
        }

        if (from || Derived().IsMatch(bare))
        {
            return (StrongRelationKinds.From, true);
        }

        return (StrongRelationKinds.Unclassified, true);
    }

    /// <summary>Lower case, letters of the original languages taken out, spaces collapsed.</summary>
    internal static string Words(string text)
    {
        var plain = OriginalLetters().Replace(text.ToLowerInvariant(), " ");
        return Spaces().Replace(plain, " ").Trim(' ', ',', '.', ':');
    }

    private static string Unbracketed(string text) => Words(Bracketed().Replace(text, " "));

    private static string FirstWord(string text) => text.Split(' ', 2)[0];

    private static string Statement(string clause) =>
        Spaces().Replace(clause, " ").Trim(' ', ',', '.', ';', ':');

    [GeneratedRegex(@"\bcompare\b|\bcomp\.|\bsee\b|\blike\b|similar|\bas in\b|collateral|connection|equivalent")]
    private static partial Regex Compare();

    [GeneratedRegex(@"^(?:(?:and|or|with|also|,|&|\+)\s*)+$")]
    private static partial Regex Joiner();

    [GeneratedRegex(@"correspond")]
    private static partial Regex Corresponding();

    [GeneratedRegex(@"\borigin\b")]
    private static partial Regex Origin();

    [GeneratedRegex(@"\b(?:from|of)\s+(?:the\s+)?same\b|\bsame\s+root\b")]
    private static partial Regex SameRoot();

    [GeneratedRegex(@"\bsame\s+as\b|\bidentical\b")]
    private static partial Regex Same();

    [GeneratedRegex(@"\bcontract")]
    private static partial Regex Contracted();

    [GeneratedRegex(@"\bfrom\b")]
    private static partial Regex FromWord();

    /// <summary>A grammatical form named, then <em>of</em>: <em>feminine of</em>, <em>feminine passive participle of</em>.</summary>
    [GeneratedRegex(
        @"\b(?:feminine|masculine|neuter|plural|dual|singular|participle|infinitive|imperative|construct|case|voice|comparative|superlative)\b(?:\s+\w+){0,2}\s+of$")]
    private static partial Regex HebrewForm();

    [GeneratedRegex(@"variation|variant|\bform\b|alternat|orthograph|prolong|abbreviat|shorten|corruption|modification|\bfor$")]
    private static partial Regex Variation();

    [GeneratedRegex(@"derivative|derived|\bbase\b|compound|denominative|reduplicat|\bakin\b|intensive|multiple")]
    private static partial Regex Derived();

    [GeneratedRegex(@"\b(?:probabl|perhaps|apparent|possibl|seem|presum|doubtful|uncertain)", RegexOptions.IgnoreCase)]
    private static partial Regex Hedge();

    /// <summary>A clause saying the entry comes from nothing: <em>a primitive root</em>, <em>a primary verb</em>.</summary>
    [GeneratedRegex(@"^(?:\(?aramaic\)?\s*,?\s*)?(?:a|an)\s+(?:primitive|primary)\b")]
    private static partial Regex Primitive();

    [GeneratedRegex(@"\([^()]*\)")]
    private static partial Regex Bracketed();

    [GeneratedRegex(@"[\p{IsHebrew}יִ-ﭏ\p{IsGreek}\p{IsGreekExtended}̀-ͯ]+")]
    private static partial Regex OriginalLetters();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

/// <summary>
/// Every relation both dictionaries state, with the gentilics given the kind the gentilic reading
/// gives them. That reading is stricter than a kind read from the words before one reference — it
/// refuses a hedge, two candidates and a people named after a people — so where it accepts a clause
/// the derivation is a gentilic, and where it refuses one the derivation is still a derivation.
/// </summary>
public static class StrongRelationReading
{
    public static IReadOnlyList<StatedRelation> Of(IReadOnlyCollection<StrongParsedEntry> entries)
    {
        var stated = entries
            .Where(e => e.StrongNumber.StartsWith('H'))
            .Select(e => GentilicDerivations.Read(e.StrongNumber, e.Derivation, out _))
            .OfType<StatedGentilic>()
            .ToList();
        var gentilics = GentilicDerivations.Together(stated, out _)
            .ToDictionary(g => g.StrongNumber, StringComparer.Ordinal);

        var relations = new List<StatedRelation>();
        foreach (var entry in entries)
        {
            var origin = gentilics.GetValueOrDefault(entry.StrongNumber);
            var named = false;
            foreach (var relation in entry.Relations)
            {
                if (!named && origin.OriginNumber is { } number && relation.ToNumber == number)
                {
                    named = true;
                    relations.Add(relation with { Kind = KindOf(origin) });
                    continue;
                }

                relations.Add(relation);
            }
        }

        return relations;
    }

    public static string KindOf(StatedGentilic gentilic) => gentilic.Kind switch
    {
        GentilicKinds.Patrial => StrongRelationKinds.Patrial,
        GentilicKinds.Either => StrongRelationKinds.PatronymicOrPatrial,
        _ => StrongRelationKinds.Patronymic,
    };
}
