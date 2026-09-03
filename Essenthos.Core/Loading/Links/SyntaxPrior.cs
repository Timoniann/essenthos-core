using Npgsql;

namespace Essenthos.Core.Loading.Links;

/// <summary>How a candidate's target word stands to the target word of a neighbour's best answer.</summary>
internal enum Cohesion
{
    /// <summary>No neighbour of this word has an answer confident enough to be compared against.</summary>
    Alone,

    /// <summary>The two words are in different sentences of the verse.</summary>
    Apart,

    /// <summary>The two words share a sentence and no clause.</summary>
    Sentence,

    /// <summary>The two words share a clause and no phrase.</summary>
    Clause,

    /// <summary>The two words are inside a single phrase.</summary>
    Phrase,
}

/// <summary>
/// The syntax of the text being aligned into, read as a prior over which of its words two
/// neighbouring source words may reach.
///
/// The aligner sees a verse as a bag of words. BHSA does not: every Hebrew word sits in a phrase,
/// a clause and a sentence, and the ETCBC analysis of where those boundaries fall is in the corpus
/// already. Measured over the 538,442 word correspondences the King James mapping file states, two
/// English words standing next to each other point into one Hebrew phrase 36.4% of the time and
/// into one Hebrew clause 81.9% of the time; two English words thirteen or more apart do so 1.6%
/// and 5.6% of the time. That is a twenty-three-fold and a fifteen-fold separation on a fact no
/// lexicon holds, and it is there for every Hebrew verse in the corpus.
///
/// It cannot propose a pairing of its own — it says nothing about which Hebrew word means
/// <em>light</em> — so it is a rescorer and not a fourth route. What it can do is judge whether an
/// answer coheres with the answers around it, which is exactly the aligner's weak spot: a lexically
/// plausible word picked out of the wrong clause of a long verse.
///
/// What it is worth is smaller than the separation above suggests, and worth saying plainly: at
/// matched coverage on King James against BHSA it moves precision by a fifth to three tenths of a
/// point, and content precision by about half a point. The reason is that the model's own position
/// score already knows part of what the syntax knows. What is left is real — <c>syntax kjv bhsa</c>
/// shows the same ordering inside every band of the model's confidence, so it is not the confidence
/// under another name — but it is a sharpening and not a step change.
/// </summary>
internal sealed class SyntaxPrior
{
    /// <summary>
    /// How far along the source verse a neighbour may stand and still be evidence. Beyond three the
    /// measured rate of sharing a phrase has fallen from 36.4% to under a quarter and is on its way
    /// to the 1.6% of two unrelated words, so a fourth neighbour is closer to noise than to a check.
    /// </summary>
    private const int Window = 3;

    /// <summary>
    /// How sure a proposal has to be before another proposal may be judged against it. An anchor
    /// is used as though it were right, so a doubtful one spreads its own error to its neighbours.
    /// </summary>
    private const double AnchorFloor = 0.5;

    private const string Load =
        """
        SELECT wgw.word_id, wg.kind, wg.id
        FROM word_group wg
        JOIN word_group_word wgw ON wgw.word_group_id = wg.id
        WHERE wg.text_id = @text AND wg.kind IN ('phrase', 'clause', 'sentence')
        """;

    private readonly Dictionary<long, (long Phrase, long Clause, long Sentence)> units;

    private SyntaxPrior(Dictionary<long, (long, long, long)> units) => this.units = units;

    /// <summary>
    /// A prior over a verse written out rather than read from the corpus, so that what the rescorer
    /// does with a syntax can be stated as a small example instead of a database.
    /// </summary>
    public static SyntaxPrior Of(params (long Word, long Phrase, long Clause, long Sentence)[] words) =>
        new(words.ToDictionary(
            word => word.Word,
            word => (word.Phrase, word.Clause, word.Sentence)));

    /// <summary>
    /// What each relation is worth, as the log of how much likelier it is among the proposals a
    /// source agrees with than among the ones it does not.
    ///
    /// These are measurements, not settings. <c>syntax kjv bhsa</c> counts all 490,276 proposals the
    /// model makes against the King James mapping file and reports the five rates; the weight is the
    /// log ratio of the two conditional frequencies, which is what Bayes says to add to the log odds
    /// of a claim on learning a further fact about it. Rerunning that command is how these would be
    /// revised, and it prints them, so a revision is a reading rather than a guess.
    ///
    /// They are taken against the file's own statements together with the lexical matches, rather
    /// than the statements alone, because the file is silent far more often than it contradicts and
    /// the lexical matches fill part of that silence. <c>syntax kjv bhsa --stated</c> shows what
    /// dropping them costs: the same ordering, and a top band that falls to 40.8% for the pairs the
    /// file never mentions — which measures the file's coverage rather than the model's accuracy,
    /// and would bias every weight through it.
    ///
    /// The same command shows the five rates inside each band of the model's own confidence, which
    /// is the check that decides whether any of this is new: a signal that only tells the sure pairs
    /// from the doubtful ones is the confidence over again under another name. It is not. Below 0.25
    /// a proposal sharing a clause with a neighbour's answer agrees with the file 67.8% of the time
    /// against 48.7% for one reaching out of every neighbour's sentence; between 0.5 and 0.8 the
    /// phrase reading is 89.6% against 71.2%; and above 0.8, where there is least room left, still
    /// 90.3% against 83.1%.
    ///
    /// <see cref="Cohesion.Alone"/> is negative rather than neutral, and that is the measurement
    /// too: a word whose neighbours the model has no confident answer for is in a verse the model is
    /// struggling with, and is wrong more often than average for that reason alone. Weighted by how
    /// often each reading occurs these come to +0.024, so they sharpen the ordering of the proposals
    /// without moving the corpus up or down as a whole — which is what makes a comparison at a fixed
    /// threshold a comparison of the ordering rather than of the threshold.
    /// </summary>
    private static readonly double[] Weight =
    [
        -0.325,  // Alone
        -0.573,  // Apart
        -0.376,  // Sentence
        0.024,   // Clause
        0.349,   // Phrase
    ];

    /// <summary>
    /// The most a rescored proposal may claim. The syntax is a check on coherence and not a second
    /// witness to meaning, so no amount of it turns a guess into a citation — the same argument, and
    /// the same number, as <see cref="Routes.Ceiling"/>.
    /// </summary>
    private const double Ceiling = Routes.Ceiling;

    /// <summary>How faint a rescored proposal may be left, so a weight can never erase a claim.</summary>
    private const double Floor = 0.001;

    /// <summary>Whether the text this was built for carries any syntax at all.</summary>
    public bool Known => units.Count > 0;

    /// <summary>
    /// The model's proposals with the target's own syntax read over them.
    ///
    /// The model scores a pair on the lexicon and on where the words stand, and knows nothing of
    /// what the target text says about itself. A verse of BHSA says which of its words form a
    /// phrase and which form a clause, and a proposal that reaches out of the clause its neighbours
    /// all landed in is the aligner's characteristic mistake — a lexically plausible word taken from
    /// the wrong part of a long verse.
    ///
    /// The adjustment is on the log odds, which is what keeps it honest at both ends: a pair the
    /// model is already sure of moves very little, and a pair it is unsure of moves a great deal,
    /// because that is where a further independent fact is worth something.
    /// </summary>
    public List<(int Source, int Target, double Confidence, double Position)> Rescore(
        IReadOnlyList<(int Source, int Target, double Confidence, double Position)> verse,
        IReadOnlyList<long> targets)
    {
        if (!Known)
        {
            return [.. verse];
        }

        var judged = Judge(verse, targets);
        var rescored = new List<(int, int, double, double)>(verse.Count);

        for (var at = 0; at < verse.Count; at++)
        {
            var pair = verse[at];
            rescored.Add((pair.Source, pair.Target, Shift(pair.Confidence, judged[at]), pair.Position));
        }

        return rescored;
    }

    /// <summary>
    /// One confidence, moved by one relation, on the log odds and back. Confidences of exactly zero
    /// and one have no log odds, so they are drawn inside the range first rather than left to
    /// produce an infinity that would silently become the ceiling or the floor for every relation
    /// alike.
    /// </summary>
    public static double Shift(double confidence, Cohesion cohesion)
    {
        var weight = Weight[(int)cohesion];
        if (weight == 0)
        {
            return confidence;
        }

        var bounded = Math.Clamp(confidence, Floor, Ceiling);
        var odds = bounded / (1 - bounded) * Math.Exp(weight);
        return Math.Clamp(odds / (1 + odds), Floor, Ceiling);
    }

    /// <summary>
    /// The phrase, clause and sentence every word of a text belongs to. A text with no analysis —
    /// every Greek witness here, and every translation — yields an empty prior that leaves the
    /// aligner's own scores exactly as it found them, rather than a special case at each call site.
    /// </summary>
    public static async Task<SyntaxPrior> Read(
        NpgsqlConnection connection,
        int textId,
        CancellationToken cancellationToken)
    {
        var units = new Dictionary<long, (long Phrase, long Clause, long Sentence)>(450_000);

        await using var command = new NpgsqlCommand(Load, connection);
        command.Parameters.AddWithValue("text", textId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var word = reader.GetInt64(0);
            var group = reader.GetInt64(2);
            units.TryGetValue(word, out var standing);

            units[word] = reader.GetString(1) switch
            {
                "phrase" => standing with { Phrase = group },
                "clause" => standing with { Clause = group },
                _ => standing with { Sentence = group },
            };
        }

        return new SyntaxPrior(units);
    }

    /// <summary>
    /// How each of a verse's proposals stands to the proposals around it.
    ///
    /// The anchors are one per source word — the model's own best answer for it, where that answer
    /// is confident enough to be worth believing. A candidate is then compared against the anchors
    /// of the source words beside it, and takes the closest relation any of them offers: sharing a
    /// phrase with one neighbour is the finding, and sharing a clause with another does not dilute
    /// it.
    /// </summary>
    /// <param name="targets">
    /// The target word ids the candidates index into. Indices rather than ids are what the tool's
    /// output speaks in, and resolving them here keeps the caller from having to.
    /// </param>
    public Cohesion[] Judge(
        IReadOnlyList<(int Source, int Target, double Confidence, double Position)> verse,
        IReadOnlyList<long> targets)
    {
        var judged = new Cohesion[verse.Count];
        if (!Known || verse.Count == 0)
        {
            return judged;
        }

        var anchors = Anchors(verse, targets);

        for (var at = 0; at < verse.Count; at++)
        {
            var (source, target, _, _) = verse[at];
            judged[at] = Closest(anchors, source, targets[target]);
        }

        return judged;
    }

    /// <summary>
    /// One target word per source word: the model's best answer for it, where that clears the
    /// floor. Ties are broken by taking neither — two answers at one score mean the model has no
    /// preference, and an anchor is only useful when it is a claim.
    /// </summary>
    private static Dictionary<int, long> Anchors(
        IReadOnlyList<(int Source, int Target, double Confidence, double Position)> verse,
        IReadOnlyList<long> targets)
    {
        var best = new Dictionary<int, (long Word, double Confidence, bool Tied)>();

        foreach (var (source, target, confidence, _) in verse)
        {
            if (confidence < AnchorFloor || target >= targets.Count)
            {
                continue;
            }

            var word = targets[target];
            if (!best.TryGetValue(source, out var standing) || confidence > standing.Confidence)
            {
                best[source] = (word, confidence, false);
            }
            else if (confidence == standing.Confidence && word != standing.Word)
            {
                best[source] = standing with { Tied = true };
            }
        }

        return best
            .Where(anchor => !anchor.Value.Tied)
            .ToDictionary(anchor => anchor.Key, anchor => anchor.Value.Word);
    }

    /// <summary>
    /// The nearest source word either side that the model has a confident answer for, and the
    /// smallest unit holding that answer together with this one.
    ///
    /// Best of the window rather than nearest, which was measured against it and is the weaker
    /// reading. What makes <see cref="Cohesion.Apart"/> worth so much is that it is a statement about
    /// all six neighbours at once — not one of them put this word in their clause — and asking only
    /// the nearest turns that into a statement about one, which costs it a fifth of its weight.
    /// </summary>
    private Cohesion Closest(Dictionary<int, long> anchors, int source, long target)
    {
        var closest = Cohesion.Alone;

        for (var step = 1; step <= Window; step++)
        {
            foreach (var neighbour in new[] { source - step, source + step })
            {
                if (!anchors.TryGetValue(neighbour, out var word) || word == target)
                {
                    continue;
                }

                closest = (Cohesion)Math.Max((int)closest, (int)Relation(target, word));
            }
        }

        return closest;
    }

    /// <summary>Two target words, and the smallest unit holding both.</summary>
    private Cohesion Relation(long target, long anchor)
    {
        if (!units.TryGetValue(target, out var mine) || !units.TryGetValue(anchor, out var theirs))
        {
            return Cohesion.Alone;
        }

        return mine.Phrase != 0 && mine.Phrase == theirs.Phrase ? Cohesion.Phrase
            : mine.Clause != 0 && mine.Clause == theirs.Clause ? Cohesion.Clause
            : mine.Sentence != 0 && mine.Sentence == theirs.Sentence ? Cohesion.Sentence
            : Cohesion.Apart;
    }
}
