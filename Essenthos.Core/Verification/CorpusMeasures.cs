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
    /// <summary>
    /// The words this section had something to reach: every word but the unpaired ones.
    ///
    /// It is the denominator of <see cref="Share"/> because a share taken over words with no
    /// counterpart in the corpus measures the shape of the canon and not the alignment. Brenton's
    /// deuterocanon is 98,670 words of it — no Hebrew of Tobit, Judith, Wisdom, Sirach, Baruch or
    /// the Maccabees exists here to be reached, and for most of them none exists anywhere.
    /// </summary>
    public int Promised => Words - Unpaired;

    public double Share => Promised <= 0 ? 0 : (double)Rendered / Promised;
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
/// <param name="Contended">
/// Words one source gives more than one counterpart. A defect in that source's load, and the number
/// this measure was built for; it should be zero.
/// </param>
/// <param name="Disputed">
/// Words two sources answer differently, each with one counterpart. Not a defect — two people who
/// both looked, differing about which word renders which, which is a fact about translation. Kept
/// apart from <paramref name="Contended"/> because counted together the second hides the first.
/// </param>
internal sealed record Contention(string Text, string Against, int Contended, int Worst, int Disputed);

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
/// <summary>
/// How many links carry how many independent methods saying they are true.
/// </summary>
/// <param name="Claims">
/// How many independent answers stand on these links. One is the ordinary case and says nothing
/// about whether the link is right; two or more is the corpus's cheapest evidence, because two
/// sources that did not consult each other landing on the same pair of words is worth more than
/// either alone.
///
/// It counts claims and not methods. The Berean's publisher and Clear Bible's team are both
/// <c>stated-by-source</c> and neither knew what the other wrote, so counting methods reported
/// 98,989 corroborated links as none at all.
/// </param>
/// <param name="Links">How many links have exactly that many.</param>
internal sealed record Agreement(int Claims, int Links);

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
    IReadOnlyList<Agreement> Agreement,
    IReadOnlyList<IntegrityCheck> Integrity)
{
    /// <summary>
    /// The share of links more than one method claims. It is the number DOC-0170 says the corpus
    /// could not compute: a link four methods agree on and a link one model guessed at were stored
    /// identically, so *92.1% correct* could be measured and *which 8%* could not.
    ///
    /// It should rise, and it will stay small for a long time, because most pairs of texts have
    /// only one method that can speak about them at all.
    /// </summary>
    public double Corroborated => Agreement.Sum(a => a.Links) is var links and > 0
        ? (double)Agreement.Where(a => a.Claims > 1).Sum(a => a.Links) / links
        : 0;

    /// <summary>Integrity checks are the only measure with a right answer, and it is zero.</summary>
    public bool Sound => Integrity.All(check => check.Found == 0);

    public int Broken => Integrity.Sum(check => check.Found);

    /// <summary>
    /// The share of words that reach a witness, over every text the corpus has linked to one. It is
    /// a trend line and nothing more — no text has this share, and the per-section rows are where a
    /// reader looks for a number that describes something.
    ///
    /// Taken over the words that had a counterpart to reach, which is the same line
    /// <see cref="Weakest"/> draws. A word in a verse no witness holds cannot reach one, and
    /// counting it as a failure publishes the shape of the canon as a defect in the alignment.
    /// </summary>
    public double Rendered => Words is > 0
        ? (double)RenderedWords / Words
        : 0;

    /// <summary>
    /// The two numbers <see cref="Rendered"/> is the ratio of, published beside it.
    ///
    /// A share on its own cannot be checked or compared. Two measurements of this corpus a day apart
    /// differed by four points and neither could be reproduced from the other, because each was a
    /// ratio with no numerator and no denominator recorded — and the question "which words did you
    /// count" has four defensible answers here: words reaching any link at all, words reaching a
    /// non-translation witness, words reaching an original-language text, and any of those taken
    /// over the words that had one to reach. These are the counts behind the last, which is the one
    /// this measure means. <see cref="UnpairedWords"/> is what it leaves out, published so that the
    /// exclusion is visible and every word in a linked text is still accounted for.
    /// </summary>
    public int Words => Coverage.Sum(c => c.Promised);

    /// <inheritdoc cref="Words"/>
    public int RenderedWords => Coverage.Sum(c => c.Rendered);

    /// <summary>
    /// Words in a verse no witness the text is linked to holds at all, and therefore outside
    /// <see cref="Words"/>. Nothing is missing here that was ever promised, and a corpus that
    /// reported it as unreached would be reporting which books the canon contains.
    ///
    /// It is not small: Brenton's deuterocanon alone is 98,670 words, and no text in this corpus
    /// holds a single book beyond the sixty-six for any of it to correspond to.
    /// </summary>
    public int UnpairedWords => Coverage.Sum(c => c.Unpaired);

    /// <summary>
    /// The lowest share any one section of any one text reaches, over the sections where something
    /// was promised. A section whose every word is unpaired has no coverage to be worst at — the
    /// Septuagint's deuterocanon has no Hebrew counterpart, and reporting it as 0% would put a fact
    /// about the canon at the bottom of a list about the alignment.
    /// </summary>
    public double Weakest => Coverage.Where(c => c.Promised > 0).ToList() is { Count: > 0 } promised
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

        report.AppendLine($"  {RenderedWords} of {Words} words had a counterpart to reach and reached it; " +
                          $"{UnpairedWords} more have none in this corpus and are outside the share");

        report.AppendLine("reach         lexical    reached");
        foreach (var r in Reach)
        {
            report.AppendLine($"  {r.Witness} from {r.From,-6} {r.Lexical,7} {r.Reached,10}   {r.Share,7:P1}");
        }

        report.AppendLine("contention    words one source claims twice, the worst one, and words two sources dispute");
        foreach (var c in Contention)
        {
            report.AppendLine($"  {c.Text} to {c.Against,-12} {c.Contended,7} {c.Worst,10} {c.Disputed,10}");
        }

        report.AppendLine("crowding      witness words claimed by more than two, and the worst one");
        foreach (var c in Crowding)
        {
            report.AppendLine($"  {c.Text} on {c.Witness,-12} {c.Crowded,7} {c.Worst,10}");
        }

        report.AppendLine("agreement     links, by how many independent answers stand on them");
        foreach (var a in Agreement.OrderBy(a => a.Claims))
        {
            report.AppendLine($"  {a.Claims} claim{(a.Claims == 1 ? " " : "s")}     {a.Links,10}" +
                              (a.Claims > 1 ? "   corroborated" : string.Empty));
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
