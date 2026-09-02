namespace Essenthos.Core.Verification;

/// <param name="Section">
/// Which half of the canon these words are in, or the deuterocanon. A text is not one thing — the
/// King James renders 97% of the Hebrew and 58% of the Greek — and a single share for it is an
/// average that describes neither part.
/// </param>
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
/// Words no link names, in a verse no witness this text is linked to has at all. Nothing is missing
/// here that was ever promised — the Septuagint's deuterocanon has no Hebrew counterpart, and the
/// sixty-five verses its Daniel 3 holds beyond the Masoretic text have none either.
/// </param>
internal sealed record Coverage(
    string Text,
    string Section,
    int Words,
    int Rendered,
    int StatedAbsent,
    int Silent,
    int Unpaired)
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

/// <param name="Crowded">
/// Witness words claimed by more than two words of one text in the same verse. Some sharing is
/// right — the Synodal writes <em>по роду</em> where Hebrew writes one word — but a witness word
/// claimed by four or five is what a reader sees as half a verse lighting up when one word is
/// touched, and it is the shape a repeated word makes when a model cannot tell its occurrences
/// apart.
/// </param>
/// <param name="Worst">The most words of this text that claim one witness word.</param>
internal sealed record Crowding(string Text, string Witness, int Crowded, int Worst);

/// <param name="Chapters">Chapters both texts place in the canonical frame.</param>
/// <param name="Divided">
/// Of those, the ones the two divide into a different number of verses. Nothing can be aligned
/// verse by verse across such a chapter without something being laid against the wrong thing, and
/// this is the half of the problem that is visible without reading a single link.
/// </param>
/// <param name="Verses">Verse pairs the links cross, and whose strength can therefore be read.</param>
/// <param name="Suspect">
/// Verse pairs whose links are uniformly faint. This is the other half, and the dangerous one: the
/// counts agree, the division does not, and every link in the verse is a claim about the word next
/// to the right one. Nothing else in the corpus reports it.
/// </param>
/// <param name="Worst">The weakest of those, named, because a count nobody can check is a rumour.</param>
internal sealed record Pairing(
    string Text,
    string Against,
    int Chapters,
    int Divided,
    int Verses,
    int Suspect,
    IReadOnlyList<string> Worst);

/// <param name="Found">
/// How many rows break the check. Every one of these should be zero, so the name says what is
/// wrong rather than what was counted.
/// </param>
internal sealed record IntegrityCheck(string Breaks, int Found);

/// <summary>
/// What one load produced. Every field is a query, and the point of storing it is that the next
/// load can be compared with it.
/// </summary>
internal sealed record CorpusMeasures(
    IReadOnlyList<Coverage> Coverage,
    IReadOnlyList<Reach> Reach,
    IReadOnlyList<Contention> Contention,
    IReadOnlyList<Crowding> Crowding,
    IReadOnlyList<Pairing> Pairing,
    IReadOnlyList<IntegrityCheck> Integrity)
{
    /// <summary>Integrity checks are the only measure with a right answer, and it is zero.</summary>
    public bool Sound => Integrity.All(check => check.Found == 0);

    public int Broken => Integrity.Sum(check => check.Found);

    /// <summary>
    /// The share of words that reach a witness, over every text the corpus has linked to one. It is
    /// a trend line and nothing more — no text has this share, and the per-section rows are where a
    /// reader looks for a number that describes something.
    /// </summary>
    public double Rendered => Coverage.Sum(c => c.Words) is var words and > 0
        ? (double)Coverage.Sum(c => c.Rendered) / words
        : 0;

    /// <summary>
    /// The lowest share any one section of any one text reaches, over the sections where something
    /// was promised. A section whose every word is unpaired has no coverage to be worst at — the
    /// Septuagint's deuterocanon has no Hebrew counterpart, and reporting it as 0% would put a fact
    /// about the canon at the bottom of a list about the alignment.
    /// </summary>
    public double Weakest => Coverage.Where(c => c.Words > c.Unpaired).ToList() is { Count: > 0 } promised
        ? promised.Min(c => c.Share)
        : 0;

    /// <summary>The measures as a person reads them, for a build log and a terminal.</summary>
    public string Describe()
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("coverage                          words   rendered   stated absent     silent   unpaired");
        foreach (var c in Coverage)
        {
            report.AppendLine($"  {c.Text,-13} {c.Section,-15} {c.Words,7} {c.Rendered,10} {c.StatedAbsent,15} " +
                              $"{c.Silent,10} {c.Unpaired,10}   {c.Share,7:P1}");
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

        report.AppendLine("crowding      witness words claimed by more than two, and the worst one");
        foreach (var c in Crowding)
        {
            report.AppendLine($"  {c.Text} on {c.Witness,-12} {c.Crowded,7} {c.Worst,10}");
        }

        report.AppendLine("pairing       chapters shared, chapters divided differently, verses, verses too weak to trust");
        foreach (var p in Pairing)
        {
            report.AppendLine($"  {p.Text} to {p.Against,-14} {p.Chapters,6} {p.Divided,7} {p.Verses,8} {p.Suspect,7}" +
                              (p.Worst.Count == 0 ? string.Empty : $"   {string.Join(", ", p.Worst)}"));
        }

        report.AppendLine("integrity     every one of these should be zero");
        foreach (var i in Integrity)
        {
            report.AppendLine($"  {i.Found,7}  {i.Breaks}");
        }

        return report.ToString();
    }
}
