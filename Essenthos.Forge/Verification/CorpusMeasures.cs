namespace Essenthos.Core.Verification;

/// <param name="Section">
/// Which half of the canon these words are in, or the deuterocanon. A text is not one thing — the
/// King James renders 94% of the Hebrew and 77% of the Greek — and a single share for it is an
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
/// <param name="Attached">
/// Of <paramref name="Rendered"/>, the words that render only as part of another word of their text:
/// <em>did</em> of <em>did see</em>, on the verb whose tense it writes. A word with a rendering of its
/// own as well is counted as linked.
/// </param>
/// <param name="PhraseMember">
/// Of <paramref name="Rendered"/>, the words that render only together with another word of their
/// text, <em>out</em> of <em>went out</em>, and are not attached.
/// </param>
internal sealed record Coverage(
    string Text,
    string Section,
    int Words,
    int Rendered,
    int StatedAbsent,
    int Silent,
    int Unpaired,
    int Attached = 0,
    int PhraseMember = 0)
{
    /// <summary>The words of <see cref="Rendered"/> that render on their own: the five states' first.</summary>
    public int Linked => Rendered - Attached - PhraseMember;

    /// <summary>
    /// The words this section had something to reach: every word but the unpaired ones.
    ///
    /// It is the denominator of <see cref="Share"/> because a share taken over words with no
    /// counterpart in the corpus measures the shape of the canon and not the alignment. Brenton's
    /// deuterocanon is 98,670 words of it — no Hebrew of Tobit, Judith, Wisdom, Sirach, Baruch or
    /// the Maccabees exists here to be reached, and for most of them none exists anywhere.
    /// </summary>
    public int Promised => Words - Unpaired;

    /// <summary>
    /// The words this section has answered for: those that reach a witness, and those a link says
    /// have no counterpart there — a supplied *the*, an unrendered ו. A word shown to be the
    /// translator's own has been placed as surely as one shown to render something, and leaving it
    /// out made every absence written read as a word lost.
    /// </summary>
    public int Accounted => Rendered + StatedAbsent;

    public double Share => Promised <= 0 ? 0 : (double)Accounted / Promised;
}

/// <param name="Lexical">
/// Witness words carrying lexical content. The prefixes and the object marker are excluded: a
/// translation renders them inside the word they attach to, so counting them as unreached would
/// report a failure that is really a fact about Hebrew.
/// </param>
/// <param name="Reached">Lexical words at least one word of the other text points at.</param>
/// <param name="ByMethod">
/// How many of them each method reached, keyed as the rows spell it.
///
/// **Read as one number this table ranks translations by quality, and what it ranks them by is how
/// much testimony each has.** The King James reaches 99.4% of the Hebrew and every word of that is
/// stated by a source; the Ukrainian reaches 91.4% of the same Hebrew with 0.7% of it stated and
/// the rest found by a model. Those are not the same achievement and the share alone cannot tell
/// them apart — the number was read as a ranking of alignment quality, which is the one thing it
/// cannot be.
///
/// A dictionary rather than four fields, because the split that matters is not stated against the
/// rest: a Strong number carried on both sides is far stronger than an aligner's guess and far
/// weaker than a source's claim, and collapsing the middle loses the distinction the corpus was
/// built to keep. A method added later appears here without a schema change.
///
/// The counts overlap. A word two methods both reach is counted by both, so they sum to more than
/// <paramref name="Reached"/> — which is a fact worth seeing rather than an error to normalise
/// away.
/// </param>
internal sealed record Reach(
    string Witness, string From, int Lexical, int Reached, IReadOnlyDictionary<string, int> ByMethod)
{
    public double Share => Lexical == 0 ? 0 : (double)Reached / Lexical;

    /// <summary>What a source claims, which is the strongest thing this corpus can say.</summary>
    public int Stated => ByMethod.GetValueOrDefault("stated-by-source");

    /// <summary>What a publisher printed on the words themselves, which we then matched.</summary>
    public int Tagged => ByMethod.GetValueOrDefault("strong-number");

    /// <summary>
    /// The share of this pair where a source says these words correspond. Two pairs with the same
    /// <see cref="Share"/> and different values here are not comparable, and this says so.
    ///
    /// It is the narrow reading on purpose and it must be read beside <see cref="Attested"/>, never
    /// alone: everything that is not this word is not therefore a guess. Read alone it has already
    /// misled once — the King James against Nestle scored 0.0000 here on 129,626 links made from
    /// Strong numbers two publishers printed, which put the corpus's own headline claim in the same
    /// column as a model that has never seen a Strong number.
    /// </summary>
    public double Testimony => Reached == 0 ? 0 : (double)Stated / Reached;

    /// <summary>
    /// The share resting on a publisher's own annotation of each side, joined by us.
    ///
    /// Between the other two and belonging to neither. A Strong number is a fact somebody printed,
    /// so this is not inference; but the correspondence is ours — two words carrying H430 in one
    /// verse are matched by a rule, and where a verse holds the same number twice the rule chooses.
    /// Folding it into <see cref="Testimony"/> would claim a source said something it never said;
    /// leaving it with the aligner calls a printed number a guess. Three grades, because there are
    /// three things.
    /// </summary>
    public double Attested => Reached == 0 ? 0 : (double)Tagged / Reached;

    /// <summary>
    /// What neither a source nor a publisher's annotation supports: our own model, our own lexical
    /// rules, our own composition. One minus the other two, floored at zero because the per-method
    /// counts overlap and a word two methods reach is counted by both.
    /// </summary>
    public double Inferred => Math.Max(0, 1 - Testimony - Attested);

    /// <summary>
    /// One row per method for one pair, plus the pair's own row, folded into one.
    ///
    /// The total is read off the row whose method is null and never summed from the others. A word
    /// two methods both reach belongs to both, so adding the per-method counts gives more words
    /// than the text has — which is how the share first came out above 100%.
    /// </summary>
    public static IReadOnlyList<Reach> Gather(
        IEnumerable<(string Witness, string From, int Lexical, int Reached, string? Method)> rows) =>
    [
        .. rows
            .GroupBy(r => (r.Witness, r.From, r.Lexical))
            .Select(pair => new Reach(
                pair.Key.Witness, pair.Key.From, pair.Key.Lexical,
                pair.Single(r => r.Method is null).Reached,
                pair.Where(r => r.Method is not null)
                    .ToDictionary(r => r.Method!, r => r.Reached, StringComparer.Ordinal)))
            .OrderBy(r => r.Witness, StringComparer.Ordinal)
            .ThenBy(r => r.From, StringComparer.Ordinal),
    ];
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
/// <param name="Corroborated">
/// Words two sources answer the same way, each with one counterpart. The opposite of a dispute, and
/// counted apart from it: a load that folds one method's agreeing links into claims on another's
/// adds hundreds of thousands of these at once.
/// </param>
internal sealed record Contention(
    string Text, string Against, int Contended, int Worst, int Disputed, int Corroborated);

/// <param name="Crowded">
/// Witness words claimed by more than two words of one text in the same verse. Some sharing is
/// right — the Synodal writes <em>по роду</em> where Hebrew writes one word — but a witness word
/// claimed by four or five is what a reader sees as half a verse lighting up when one word is
/// touched, and it is the shape a repeated word makes when a model cannot tell its occurrences
/// apart.
/// </param>
/// <param name="Worst">The most words of this text that claim one witness word.</param>
internal sealed record Crowding(string Text, string Witness, int Crowded, int Worst);

/// <param name="Omits">
/// Words of <paramref name="Against"/> that <paramref name="Text"/> does not have, where a link
/// says so. It is the one thing this corpus can state and a bare text cannot: silence about a word
/// and a recorded claim that the word is not there are different facts, and every other measure
/// here counts correspondences rather than their absence.
/// </param>
/// <param name="Expands">
/// The reverse — words <paramref name="Text"/> has that <paramref name="Against"/> does not. The
/// King James italics are 21,393 of these against BHSA, the words the translators added to make
/// English of the Hebrew and printed in a different face to say so.
/// </param>
/// <param name="Books">
/// The same two counts per book, which is where a variant tradition becomes readable. A number for
/// the whole Pentateuch says the Samaritan differs; the per-book rows say it differs most in Exodus
/// and Numbers and least in Leviticus, which is a claim a scholar can go and check.
/// </param>
/// <summary>
/// Absence, recorded rather than inferred, for one ordered pair of texts.
///
/// The pair is ordered as the link stores it and is never folded with its reverse. The relation
/// alone does not say which of the two lacks the word — the same variant has been written as
/// <c>omits</c> from both ends — so a fold would turn one disagreement into two, and the direction
/// it was written in is the only thing that says which text the words belong to.
/// </summary>
internal sealed record Absence(
    string Text, string Against, int Omits, int Expands, IReadOnlyList<BookAbsence> Books)
{
    /// <summary>Words this pair records as missing on one side or the other.</summary>
    public int Words => Omits + Expands;

    /// <summary>
    /// The per-book rows folded into one row per pair, whose totals are their sum.
    ///
    /// Summed, and not read off a total the query computed separately, because a word is in exactly
    /// one book and the books therefore partition it. Reach cannot do this — a word two methods
    /// both reach belongs to both — and the difference is worth stating, because copying its
    /// grouping sets here would be machinery guarding against something that cannot happen.
    /// </summary>
    public static IReadOnlyList<Absence> Gather(
        IEnumerable<(string Text, string Against, int Ordinal, string Book, int Omits, int Expands)> rows) =>
    [
        .. rows
            .GroupBy(r => (r.Text, r.Against))
            .Select(pair => new Absence(
                pair.Key.Text,
                pair.Key.Against,
                pair.Sum(r => r.Omits),
                pair.Sum(r => r.Expands),
                [
                    .. pair
                        .OrderBy(r => r.Ordinal)
                        .Select(r => new BookAbsence(r.Ordinal, r.Book, r.Omits, r.Expands)),
                ]))
            .OrderBy(a => a.Text, StringComparer.Ordinal)
            .ThenBy(a => a.Against, StringComparer.Ordinal),
    ];
}

/// <param name="Book">
/// The book as the text holding the words names it. Every text in this corpus names them in
/// English, and the ordinal beside it is what a reader should sort and join on.
/// </param>
internal sealed record BookAbsence(int Ordinal, string Book, int Omits, int Expands);

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

/// <param name="Text">The edition that was voted, which is Nestle 1904.</param>
/// <param name="First">One of the editions it was voted from.</param>
/// <param name="Second">The other.</param>
/// <param name="Words">Words of <paramref name="Text"/> in the New Testament.</param>
/// <param name="Both">Words both voters write the same way, which is most of them.</param>
/// <param name="FirstOnly">
/// Words <paramref name="First"/> writes as Nestle does and <paramref name="Second"/> does not, so
/// Nestle followed the first against the second. Reading them beside <paramref name="SecondOnly"/>
/// is the decomposition: it says which two editions outvoted which one.
/// </param>
/// <param name="SecondOnly">The same the other way round.</param>
/// <param name="Neither">
/// Words neither voter writes as Nestle does — which is where the third voter decided.
///
/// This is the interesting number and the one the corpus cannot explain. Nestle's rule was a
/// majority of Tischendorf's eighth edition, Westcott and Hort, and Weiss; where the first two
/// both disagree with what he printed, Weiss is why, and no free machine-readable Weiss was found.
/// A reader shown a complete apparatus here would be shown one that is two thirds of a vote.
/// </param>
internal sealed record Vote(
    string Text,
    string First,
    string Second,
    int Words,
    int Both,
    int FirstOnly,
    int SecondOnly,
    int Neither);

internal sealed record IntegrityCheck(string Breaks, int Found);

/// <summary>
/// Canonical addresses that two or more verses of one text stand at as their primary address.
///
/// Not a defect, which is why it is a measure and not an integrity check: the Hebrew numbers the
/// title of Psalm 51 as two verses where the frame has one address for it, and the Septuagint prints
/// Genesis 31:50a beside 50. What it is for is noticing when the count moves, because a frame
/// that starts putting two verses at one address where it did not before has lost a distinction.
/// </summary>
/// <param name="Text">The text's slug.</param>
/// <param name="Labelled">
/// Whether a verse at the address carries a letter — the edition itself saying the material
/// extends a verse. Those are expected wherever the Greek has additions; the unlettered ones are a
/// division the frame does not make, and there should be few.
/// </param>
/// <param name="Addresses">Addresses claimed more than once.</param>
/// <param name="Verses">The verses claiming them.</param>
internal sealed record SharedAddresses(string Text, bool Labelled, int Addresses, int Verses);

/// <summary>
/// A book of a text that has a counterpart in a witness the text is linked to, and not one word of
/// it linked: alignment work not yet done there, rather than alignment that lost something.
/// </summary>
/// <param name="Book">The book's name as the text prints it.</param>
/// <param name="Words">Its words that had a counterpart to reach, all of them silent.</param>
internal sealed record Unaligned(string Text, int Ordinal, string Book, int Words);

/// <summary>
/// Words of a text that no link joins to a witness, whose Strong number the witness prints in the
/// verse next door and not in their own, where the witness's word is silent too. A link may name
/// words in two verses, but a pairing that only looks inside one canonical address can never make
/// it, so these are the words a verse boundary strands: a psalm's title printed inside its first
/// verse where the frame numbers it as a verse of its own, a clause a translator carried across.
///
/// <para>
/// Only where both sides carry Strong numbers, because the number is what says the counterpart is
/// there rather than merely that something next door went unlinked. It is a floor and not a count
/// of every stranded word: a text without numbers strands words the same way and nothing here can
/// see them.
/// </para>
/// </summary>
/// <param name="Text">The text holding the silent words.</param>
/// <param name="Witness">The text whose next verse holds their number.</param>
/// <param name="Chapters">The chapters holding the most of them, as the text names its books.</param>
internal sealed record Stranded(string Text, string Witness, int Words, IReadOnlyList<string> Chapters);

/// <summary>
/// A compiler's count of each Hebrew Strong number against the number of BHSA words carrying it. Two
/// counts of one thing made by different hands: where they part, one of the two tags something the
/// other does not, and which is a question for the number, not a correction to make. Neither is
/// served as the other.
/// </summary>
/// <param name="Numbers">Hebrew numbers the compiler counts.</param>
/// <param name="Agreeing">Of them, those BHSA carries exactly as often.</param>
/// <param name="Absent">Those the compiler counts and no BHSA word carries.</param>
/// <param name="Largest">The numbers the two counts part on most, the compiler's count first.</param>
internal sealed record LexiconCounts(
    int Numbers,
    int Agreeing,
    int Absent,
    IReadOnlyList<CountDisagreement> Largest)
{
    public int Disagreeing => Numbers - Agreeing;
}

internal sealed record CountDisagreement(string Number, int Stated, int Counted);

/// <summary>
/// What one load produced. Every field is a query, and the point of storing it is that the next
/// load can be compared with it.
/// </summary>
internal sealed record CorpusMeasures(
    IReadOnlyList<Coverage> Coverage,
    IReadOnlyList<Reach> Reach,
    IReadOnlyList<Contention> Contention,
    IReadOnlyList<Crowding> Crowding,
    IReadOnlyList<Absence> Absence,
    IReadOnlyList<Pairing> Pairing,
    IReadOnlyList<Agreement> Agreement,
    IReadOnlyList<Vote> Vote,
    IReadOnlyList<IntegrityCheck> Integrity,
    IReadOnlyList<SharedAddresses> Shared,
    IReadOnlyList<Unaligned> Unaligned,
    IReadOnlyList<Stranded>? Stranded = null,
    LexiconCounts? Lexicon = null)
{
    /// <summary>
    /// The share of links more than one method claims. It is the number the corpus could not
    /// compute before: a link four methods agree on and a link one model guessed at were stored
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
        ? (double)(RenderedWords + AbsentWords) / Words
        : 0;

    /// <summary>
    /// <see cref="Rendered"/> over the books alignment has reached: the books in
    /// <see cref="Unaligned"/> leave the denominator, and nothing leaves the numerator, since not
    /// one of their words is linked. This is the share the floor holds, because a book nobody has
    /// aligned yet is work still to do and not something a load lost; the books it sets aside are
    /// named in the report, so nothing unaligned is hidden by it.
    /// </summary>
    public double Aligned => Words - UnalignedWords is var reached and > 0
        ? (double)(RenderedWords + AbsentWords) / reached
        : 0;

    /// <summary>Words in the books of <see cref="Unaligned"/>, all inside <see cref="Words"/>.</summary>
    public int UnalignedWords => (Unaligned ?? []).Sum(u => u.Words);

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

    /// <summary>Words a link says have no counterpart, which <see cref="Rendered"/> counts as answered for.</summary>
    public int AbsentWords => Coverage.Sum(c => c.StatedAbsent);

    /// <summary>Of <see cref="RenderedWords"/>, those that render only as part of another word of their text.</summary>
    public int AttachedWords => Coverage.Sum(c => c.Attached);

    /// <summary>Of <see cref="RenderedWords"/>, those that render only together with another word of their text.</summary>
    public int PhraseMemberWords => Coverage.Sum(c => c.PhraseMember);

    /// <summary>Words no link names in a verse a witness holds: what the corpus has said nothing about.</summary>
    public int SilentWords => Coverage.Sum(c => c.Silent);

    /// <summary>
    /// Words in a verse no witness the text is linked to holds at all, and therefore outside
    /// <see cref="Words"/>. Nothing is missing here that was ever promised, and a corpus that
    /// reported it as unreached would be reporting which books the canon contains.
    ///
    /// It is not small: Brenton's deuterocanon alone is 98,670 words. Swete prints those books too
    /// and answers 84,771 of them, and they are still counted here — coverage reads a link from its
    /// <c>from</c> side, and Brenton is the <c>to</c> of that pair, so a text reached only as a
    /// target is reported as reached by nothing.
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

    /// <summary>
    /// How many books each absence row names in the written report. All of them are in the stored
    /// measures and on the endpoint; a terminal wants the shape of the disagreement, which the
    /// heaviest few give.
    /// </summary>
    private const int BooksNamed = 3;

    /// <summary>The measures as a person reads them, for a build log and a terminal.</summary>
    public string Describe()
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("coverage                          words     linked   attached     phrase   supplied unresolved   unpaired");
        foreach (var c in Coverage)
        {
            report.AppendLine($"  {c.Text,-13} {c.Section,-15} {c.Words,7} {c.Linked,10} {c.Attached,10} {c.PhraseMember,10} " +
                              $"{c.StatedAbsent,10} {c.Silent,10} {c.Unpaired,10}   {c.Share,7:P1}");
        }

        report.AppendLine($"  {RenderedWords} of {Words} words had a counterpart to reach and reached it, and {AbsentWords} are shown to have none; " +
                          $"{UnpairedWords} more have none in this corpus and are outside the share");

        report.AppendLine($"  by state, of {Words} words: linked {RenderedWords - AttachedWords - PhraseMemberWords}, attached {AttachedWords}, " +
                          $"phrase member {PhraseMemberWords}, supplied {AbsentWords}, unresolved {SilentWords}");

        report.AppendLine($"  {Aligned:P1} over the books alignment has reached; {UnalignedWords} words in {(Unaligned ?? []).Count} books " +
                          "of a linked text are not aligned yet and are outside that share");
        foreach (var text in (Unaligned ?? []).GroupBy(u => u.Text))
        {
            report.AppendLine($"    {text.Key}: " + string.Join(", ", text.Select(u => $"{u.Book} {u.Words}")));
        }

        report.AppendLine("reach         lexical    reached   share   stated  attested  inferred, then what reached them");
        foreach (var r in Reach)
        {
            var by = string.Join(" ", r.ByMethod.OrderByDescending(m => m.Value)
                .Select(m => $"{m.Key} {m.Value}"));
            report.AppendLine($"  {r.Witness} from {r.From,-6} {r.Lexical,7} {r.Reached,10}   {r.Share,7:P1}   " +
                              $"{r.Testimony,6:P0} {r.Attested,9:P0} {r.Inferred,9:P0}   {by}");
        }

        report.AppendLine("contention    words one source claims twice, the worst one, words two sources dispute, and words two sources agree on");
        foreach (var c in Contention)
        {
            report.AppendLine($"  {c.Text} to {c.Against,-12} {c.Contended,7} {c.Worst,10} {c.Disputed,10} {c.Corroborated,10}");
        }

        report.AppendLine("crowding      witness words claimed by more than two, and the worst one");
        foreach (var c in Crowding)
        {
            report.AppendLine($"  {c.Text} on {c.Witness,-12} {c.Crowded,7} {c.Worst,10}");
        }

        report.AppendLine("absence       words the pair records as missing, and the books holding most of them");
        foreach (var a in Absence)
        {
            var books = string.Join(" ", a.Books
                .OrderByDescending(b => b.Omits + b.Expands)
                .Take(BooksNamed)
                .Select(b => $"{b.Book} {b.Omits}/{b.Expands}"));
            report.AppendLine($"  {a.Text} to {a.Against,-22} {a.Omits,7} {a.Expands,10}   {books}");
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

        report.AppendLine("stranded      words no link joins whose number the witness prints only in the verse next door");
        foreach (var s in Stranded ?? [])
        {
            report.AppendLine($"  {s.Text} to {s.Witness,-14} {s.Words,7}   {string.Join(", ", s.Chapters)}");
        }

        if (Lexicon is { } lexicon)
        {
            report.AppendLine(
                $"lexicon       Hebrew numbers BibleData counts, {lexicon.Numbers}: as often as BHSA {lexicon.Agreeing}, " +
                $"differently {lexicon.Disagreeing}, never in BHSA {lexicon.Absent}");
            foreach (var d in lexicon.Largest.Take(BooksNamed * 2))
            {
                report.AppendLine($"  {d.Number,-8} BibleData {d.Stated,6}   BHSA {d.Counted,6}");
            }
        }

        report.AppendLine("integrity     every one of these should be zero");
        foreach (var i in Integrity)
        {
            report.AppendLine($"  {i.Found,7}  {i.Breaks}");
        }

        return report.ToString();
    }
}
