namespace Essenthos.Core.Verification;

/// <param name="Rendered">Words a link names as corresponding to something.</param>
/// <param name="StatedAbsent">
/// Words named by a link that records an absence — <c>omits</c> where the other text has nothing
/// for this word, <c>expands</c> where this text supplies what the other only implies. Stored
/// positively, so the absence is a claim rather than a hole.
/// </param>
/// <param name="Silent">
/// Words no link names at all, where the text does have links to a witness. This is the number that
/// matters: it is the corpus admitting it has nothing to say, and it must not be confused with the
/// two kinds of absence above.
/// </param>
/// <param name="Unpaired">
/// Words no link names, in a text that has no links to any witness. Nothing is missing here that
/// was ever promised — the pair has not been aligned.
/// </param>
internal sealed record Coverage(string Text, int Words, int Rendered, int StatedAbsent, int Silent, int Unpaired)
{
    public double Share => Words == 0 ? 0 : (double)Rendered / Words;
}

/// <param name="Lexical">
/// Witness words carrying lexical content. The prefixes and the object marker are excluded: a
/// translation renders them inside the word they attach to, so counting them as unreached would
/// report a failure that is really a fact about Hebrew.
/// </param>
/// <param name="Reached">Lexical words at least one word of the other text points at.</param>
internal sealed record Reach(string Witness, string From, int Lexical, int Reached)
{
    public double Share => Lexical == 0 ? 0 : (double)Reached / Lexical;
}

/// <param name="Contended">Words named by more than one link between the same pair of texts.</param>
/// <param name="Worst">The most links any single word of this text carries.</param>
internal sealed record Contention(string Text, string Against, int Contended, int Worst);

/// <param name="Found">
/// How many rows break the check. Every one of these should be zero, so the name says what is
/// wrong rather than what was counted.
/// </param>
internal sealed record IntegrityCheck(string Breaks, int Found);

/// <summary>
/// What one load produced, in the four measures TSK-0014 asks for. Every field is a query, and the
/// point of storing it is that the next load can be compared with it.
/// </summary>
internal sealed record CorpusMeasures(
    IReadOnlyList<Coverage> Coverage,
    IReadOnlyList<Reach> Reach,
    IReadOnlyList<Contention> Contention,
    IReadOnlyList<IntegrityCheck> Integrity)
{
    /// <summary>Integrity checks are the only measure with a right answer, and it is zero.</summary>
    public bool Sound => Integrity.All(check => check.Found == 0);

    public int Broken => Integrity.Sum(check => check.Found);

    /// <summary>The share of translated words that reach a witness, over the whole corpus.</summary>
    public double Rendered => Coverage.Sum(c => c.Words) is var words and > 0
        ? (double)Coverage.Sum(c => c.Rendered) / words
        : 0;

    /// <summary>The four measures as a person reads them, for a build log and a terminal.</summary>
    public string Describe()
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("coverage      words   rendered   stated absent     silent   unpaired");
        foreach (var c in Coverage)
        {
            report.AppendLine($"  {c.Text,-10} {c.Words,7} {c.Rendered,10} {c.StatedAbsent,15} {c.Silent,10} " +
                              $"{c.Unpaired,10}   {c.Share,7:P1}");
        }

        report.AppendLine("reach         lexical    reached");
        foreach (var r in Reach)
        {
            report.AppendLine($"  {r.Witness} from {r.From,-6} {r.Lexical,7} {r.Reached,10}   {r.Share,7:P1}");
        }

        report.AppendLine("contention    words claimed more than once, and the worst one");
        foreach (var c in Contention)
        {
            report.AppendLine($"  {c.Text} to {c.Against,-12} {c.Contended,7} {c.Worst,10}");
        }

        report.AppendLine("integrity     every one of these should be zero");
        foreach (var i in Integrity)
        {
            report.AppendLine($"  {i.Found,7}  {i.Breaks}");
        }

        return report.ToString();
    }
}
