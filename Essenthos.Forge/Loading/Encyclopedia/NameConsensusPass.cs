using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// <see cref="NameConsensus"/> over the corpus: every text read for the words naming each person,
/// place, people, title, thing and appointed time the originals name, measured against the texts
/// whose annotations came across a link the text's own edition states or numbers, and, when asked,
/// written as our own annotations on the words nothing else names.
///
/// <para>
/// <strong>An entity's verses are the originals'.</strong> The verses on an encyclopedia page are
/// read off every text's annotations, the one being measured included, so a text would be scored on
/// verses its own annotations chose. Here they are the canonical addresses where BHSA or a Greek New
/// Testament names the entity — the same claim with the translations left out of it — and a
/// namesake is an entity the originals call by one of the same words.
/// </para>
///
/// <para>
/// <strong>The bar is chosen without the text it is measured on, and for each kind apart.</strong>
/// A person's name and a feast's word are read by different rules (see <see cref="ConsensusRules"/>)
/// and fail differently, so each kind's bar is the one that finds the most words in the other
/// measured texts at the precision asked for, and the held-out text is scored at those bars. The
/// bars used on the texts with no such annotations are the ones chosen on all of them.
/// </para>
///
/// <para>
/// A word is judged only where the measured text says who that verse names: right where its
/// annotation names the same entity, and where the verse names the entity with the same form on
/// another word or on the word beside it (see <see cref="Verdict"/>); wrong where it names another or
/// the verse names the entity by another word; unjudged where the text says nothing of the entity
/// there. Recall is over the annotated words that begin with a capital, or any in a script without
/// capitals or of a kind whose word is a common noun, since an annotation carried across a link
/// sometimes lands on an <em>of</em>.
/// </para>
///
/// <para>
/// <strong>What it writes</strong> is one annotation on each word the settled findings name that
/// no annotation of any method names yet, credited to us, with the precision measured for the
/// entity's kind and size as its confidence. A word already annotated to another entity is not
/// touched: it goes to a review list.
/// </para>
/// </summary>
internal sealed class NameConsensusPass(AppDbContext db, ILogger<NameConsensusPass> logger)
{
    /// <summary>The witnesses whose annotations say which verses name whom.</summary>
    private static readonly string[] Originals = ["BHSA", "NESTLE1904", "TR1894"];

    /// <summary>
    /// The texts whose annotations came across a link their edition states or numbers: the King
    /// James, the Berean and the unfoldingWord text by their stated alignments, the Chinese Union,
    /// Luther and the Synodal by their printed Strong numbers, the Hindi, Arabic, Reina-Valera,
    /// Segond and Almeida by their interlinears.
    /// </summary>
    internal static readonly string[] Measured =
        ["KJV", "BSB", "ULT", "RUSV", "LUTH1912", "RV1909", "LSG1910", "ALM1911", "AVD1865", "IRV2019", "CUV"];

    /// <summary>The kinds read, each measured and settled apart. A term has no verses of its own.</summary>
    internal static readonly EntityKind[] Kinds =
        [EntityKind.Person, EntityKind.Place, EntityKind.People, EntityKind.Title, EntityKind.Object, EntityKind.Observance];

    /// <summary>What each kind's bar must reach on the measured texts, unless a run asks for another.</summary>
    public const double Precision = 0.97;

    /// <summary>What the written annotations are credited to.</summary>
    public const string Source = "Essenthos, read from the verses that name it";

    private static readonly string[] StatedLinks =
    [
        $"{Annotating.CarriedNote}, linked by {EnumSpelling.Of(LinkMethod.StatedBySource)}",
        $"{Annotating.CarriedNote}, linked by {EnumSpelling.Of(LinkMethod.StrongNumber)}",
    ];

    /// <summary>
    /// Never fewer than <see cref="FewVerses"/>. The measured texts put the tail at 96–99%, but read
    /// by eye in the Korean, Swete, the Vulgate, the Ge'ez and the Douay, eleven of forty findings
    /// with two to four verses were a brother's, a father's or a town's name, so below it only a
    /// name spelt like the original's stands.
    /// </summary>
    private static readonly int[] VerseBars = [FewVerses, 8, 12];
    private static readonly double[] ScoreBars = [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9];
    private static readonly double[] MarginBars = [0.05, 0.1, 0.2, 0.3, 0.4];
    private static readonly int[] SizeBands = [1, 2, 5, 20, 100];

    /// <summary>
    /// Entities with fewer verses than this are the tail the bar must hold on by itself: a name read off
    /// one or two verses is where a father's or a place's name gets taken for it, and a pooled figure
    /// that David and Moses dominate would never show it.
    /// </summary>
    private const int FewVerses = 5;

    /// <summary>
    /// How many judged words a size band needs before its own precision is its confidence; below it,
    /// the kind's precision over every size is.
    /// </summary>
    private const int ConfidentBand = 200;

    /// <summary>
    /// The most a written annotation is ever said to be worth: it was read off the text by a rule,
    /// and a rule's reading stays below what a source states however well it measured.
    /// </summary>
    private const double MostConfident = 0.99;

    /// <summary>What a written annotation is worth where no measured text could say: a run over a corpus without them.</summary>
    internal const double Unmeasured = 0.9;

    private const int AddressBook = 1_000_000;
    private const int AddressChapter = 1_000;

    private const int ExamplesShown = 12;

    /// <summary>The review list of the words where a finding and an annotation already there disagree.</summary>
    public static readonly string[] ReviewFile = ["Essenthos", "review", "name-consensus-disagreements.json"];

    /// <param name="texts">The texts to read, or every text.</param>
    /// <param name="precision">The precision each kind's bar must reach on the measured texts.</param>
    /// <param name="output">A folder to write each text's findings and words into, or none.</param>
    /// <param name="bar">One bar for every kind, or null to choose each on the measured texts.</param>
    /// <param name="apply">Whether to write the annotations, rather than only report them.</param>
    /// <param name="replace">Whether to take back what an earlier run wrote first, rather than skip a text that has it.</param>
    /// <param name="resources">The resources folder, where the review list is written when applying.</param>
    public async Task<string> Run(
        IReadOnlyCollection<string>? texts,
        double precision,
        string? output,
        ConsensusBar? bar,
        bool apply = false,
        bool replace = false,
        string? resources = null,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var question = await Question(connection, cancellationToken);
            var slugs = await db.Entities.ToDictionaryAsync(entity => entity.Id, entity => entity.Slug, cancellationToken);
            var every = await db.Texts
                .Where(text => !Originals.Contains(text.Slug))
                .OrderBy(text => text.Slug)
                .Select(text => new { text.Id, text.Slug, text.Language })
                .ToListAsync(cancellationToken);
            var chosen = texts is null ? every : every.Where(text => texts.Contains(text.Slug)).ToList();

            var report = new StringBuilder();
            var table = new StringBuilder()
                .AppendLine("| text | language | entities named | of them new to the text | words | agree | new | disagree | verses reached before | after | new by kind (person, place, people, title, object, observance) | written |")
                .AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|");
            var examples = new StringBuilder();
            var disagreements = new List<Disagreement>();
            var done = new HashSet<string>(StringComparer.Ordinal);
            if (output is not null)
            {
                Directory.CreateDirectory(output);
            }

            var measured = new List<Reading>();
            foreach (var text in every.Where(text => Measured.Contains(text.Slug)))
            {
                measured.Add(await Read(connection, text.Id, text.Slug, text.Language, question, cancellationToken));
                logger.LogInformation("Read {Text}", text.Slug);
            }

            var (bars, confidence) = Report(report, measured, precision, bar);

            async Task Add(Reading reading)
            {
                int? written = apply ? await Apply(connection, reading, bars, confidence, replace, cancellationToken) : null;
                Addition(reading, bars, slugs, table, examples, disagreements, written);
                if (output is not null)
                {
                    await Write(output, reading, bars, slugs, cancellationToken);
                    if (Measured.Contains(reading.Slug))
                    {
                        await Audit(output, reading, bars, slugs, question, cancellationToken);
                    }
                }

                done.Add(reading.Slug);
            }

            foreach (var reading in measured.Where(reading => chosen.Any(text => text.Slug == reading.Slug)))
            {
                await Add(reading);
            }

            foreach (var text in chosen.Where(text => !done.Contains(text.Slug)))
            {
                await Add(await Read(connection, text.Id, text.Slug, text.Language, question, cancellationToken));
                logger.LogInformation("Read {Text}", text.Slug);
            }

            report.AppendLine()
                .AppendLine("At those bars:")
                .AppendLine()
                .Append(table)
                .AppendLine()
                .AppendLine("The entities with the most words named where the text had none for them, or another:")
                .Append(examples);
            if (output is not null)
            {
                report.AppendLine().AppendLine($"Findings and words written to {output}.");
            }

            if (apply && resources is not null)
            {
                var path = Path.Combine([resources, .. ReviewFile]);
                await Review(path, disagreements, texts is null, cancellationToken);
                report.AppendLine().AppendLine($"{disagreements.Count:N0} words where a finding disagrees with an annotation already there, listed in {path}.");
            }

            report.AppendLine().AppendLine($"Read {done.Count} texts in {started.Elapsed:hh\\:mm\\:ss}.");
            return report.ToString();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Whether any text already holds an annotation this pass wrote, which is what the load asks before running it.</summary>
    public async Task<bool> Written(CancellationToken cancellationToken) =>
        await db.WordEntities.AnyAsync(annotation => annotation.Source == Source, cancellationToken);

    /// <summary>One text read: what the consensus found, and what the text's annotations already say.</summary>
    /// <param name="Annotated">
    /// Every annotated word of the text and the entities it is annotated to, by any method but this
    /// pass's own, which is what it would write again.
    /// </param>
    /// <param name="Gold">The words a stated link annotates, with their entity.</param>
    /// <param name="GoldForms">Each verse and entity a stated link names, and the folded forms it names it with there.</param>
    /// <param name="GoldNames">By kind, how many of <paramref name="Gold"/> recall is counted over.</param>
    private sealed record Reading(
        string Slug,
        string Language,
        IReadOnlyList<ConsensusVerse> Verses,
        IReadOnlyList<NameFinding> Findings,
        IReadOnlyList<ConsensusWord> Claims,
        IReadOnlyDictionary<long, HashSet<int>> Annotated,
        IReadOnlySet<(long Word, int Entity)> Gold,
        IReadOnlyDictionary<(int Verse, int Entity), HashSet<string>> GoldForms,
        IReadOnlySet<long> GoldWords,
        IReadOnlyDictionary<EntityKind, int> GoldNames,
        IReadOnlyDictionary<int, EntityKind> KindOf);

    /// <summary>How a word the consensus names is judged against the stated annotations.</summary>
    private enum Verdict
    {
        /// <summary>The annotation names the same entity on the same word.</summary>
        Right,

        /// <summary>
        /// The word has no annotation, but the verse's names the entity with the same form, or one a
        /// letter or two longer or shorter, on another word: the translation printed the name twice,
        /// or where the original has a pronoun, and the link reaches one of them.
        /// </summary>
        Repeated,

        /// <summary>
        /// The word has no annotation, and the word beside it is annotated to the entity: the link
        /// reached the postposition or the article that goes with the name rather than the name.
        /// </summary>
        Beside,

        /// <summary>The annotation names another entity, or the verse names this one by another word.</summary>
        Wrong,

        /// <summary>The annotations say nothing of this entity in this verse.</summary>
        Unjudged,
    }

    /// <param name="Seconded">Of the wrong, the words another annotation of the corpus names the same entity with.</param>
    private readonly record struct Tally(
        int Proposed,
        int Right,
        int Repeated,
        int Beside,
        int Wrong,
        int Seconded,
        int Found,
        int Names)
    {
        public int Judged => Right + Repeated + Beside + Wrong;

        public double Precision => Judged == 0 ? 0 : (double)(Right + Repeated + Beside) / Judged;

        public double Strictly => Judged == 0 ? 0 : (double)Right / Judged;

        public double Recall => Names == 0 ? 0 : (double)Found / Names;

        public static Tally operator +(Tally one, Tally other) => new(
            one.Proposed + other.Proposed, one.Right + other.Right, one.Repeated + other.Repeated,
            one.Beside + other.Beside, one.Wrong + other.Wrong, one.Seconded + other.Seconded,
            one.Found + other.Found, one.Names + other.Names);
    }

    /// <summary>What a written annotation is worth: the precision measured for its kind and the size of its entity.</summary>
    private sealed class Confidence(IReadOnlyDictionary<(EntityKind Kind, int Band), double> measured)
    {
        public double Of(NameFinding finding) =>
            measured.TryGetValue((finding.Kind, Band(finding.Verses)), out var banded) ? banded
            : measured.TryGetValue((finding.Kind, -1), out var kind) ? kind
            : Unmeasured;

        public static int Band(int verses) => SizeBands.Count(least => verses >= least) - 1;
    }

    /// <summary>A word where the finding and an annotation already there name different entities.</summary>
    private sealed record Disagreement(
        string Text,
        string Reference,
        int Position,
        string Word,
        string Found,
        IReadOnlyList<string> Annotated);

    /// <summary>How many letters a form may differ from the one the verse's annotation names to be the same name.</summary>
    private const int RepeatedWithin = 2;

    private static Verdict Judge(Reading reading, ConsensusWord word)
    {
        if (reading.Gold.Contains((word.Word, word.Entity)))
        {
            return Verdict.Right;
        }

        if (reading.GoldWords.Contains(word.Word))
        {
            return Verdict.Wrong;
        }

        if (!reading.GoldForms.TryGetValue((word.Verse, word.Entity), out var forms))
        {
            return Verdict.Unjudged;
        }

        var words = reading.Verses[word.Verse].Words;
        var at = Position(words, word.Word);
        var form = NameConsensus.Fold(words[at].Surface);
        if (forms.Any(named => named == form
                || (Math.Abs(named.Length - form.Length) <= RepeatedWithin
                    && (named.Contains(form, StringComparison.Ordinal) || form.Contains(named, StringComparison.Ordinal)))))
        {
            return Verdict.Repeated;
        }

        return new[] { at - 1, at + 1 }.Any(beside => beside >= 0 && beside < words.Count
            && reading.Gold.Contains((words[beside].Word, word.Entity)))
            ? Verdict.Beside
            : Verdict.Wrong;
    }

    private static int Position(IReadOnlyList<(long Word, string Surface)> words, long word)
    {
        for (var at = 0; at < words.Count; at++)
        {
            if (words[at].Word == word)
            {
                return at;
            }
        }

        throw new InvalidOperationException($"Word {word} is not in the verse the consensus placed it in.");
    }

    /// <summary>The words of one kind scored, or of every kind where none is given.</summary>
    private static Tally Score(Reading reading, IReadOnlyList<ConsensusWord> words, EntityKind? kind = null)
    {
        int proposed = 0, right = 0, repeated = 0, beside = 0, wrong = 0, seconded = 0, found = 0;
        foreach (var word in words)
        {
            var its = reading.KindOf[word.Entity];
            if (kind is { } only && its != only)
            {
                continue;
            }

            proposed++;
            switch (Judge(reading, word))
            {
                case Verdict.Right:
                    right++;
                    var printed = reading.Verses[word.Verse].Words;
                    var surface = printed[Position(printed, word.Word)].Surface;
                    if (!ConsensusRules.For(its).Capitalised || (surface.Length > 0 && !char.IsLower(surface[0])))
                    {
                        found++;
                    }

                    break;
                case Verdict.Repeated:
                    repeated++;
                    break;
                case Verdict.Beside:
                    beside++;
                    break;
                case Verdict.Wrong:
                    wrong++;
                    if (reading.Annotated.TryGetValue(word.Word, out var named) && named.Contains(word.Entity))
                    {
                        seconded++;
                    }

                    break;
            }
        }

        var names = kind is { } counted
            ? reading.GoldNames.GetValueOrDefault(counted)
            : reading.GoldNames.Values.Sum();
        return new Tally(proposed, right, repeated, beside, wrong, seconded, found, names);
    }

    private static IEnumerable<ConsensusBar> Bars() =>
        from verses in VerseBars
        from score in ScoreBars
        from margin in MarginBars
        select new ConsensusBar(verses, score, margin);

    private const string Header =
        "| text | language | bar (verses, score, margin) | proposed | judged | right | repeated | beside | wrong (seconded) | precision | strictly | name words | found | recall |";

    private const string Rule = "|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|";

    private static string Row(string label, string language, string bar, Tally tally) =>
        $"| {label} | {language} | {bar} | {tally.Proposed:N0} | {tally.Judged:N0} | {tally.Right:N0} | {tally.Repeated:N0} | " +
        $"{tally.Beside:N0} | {tally.Wrong:N0} ({tally.Seconded:N0}) | {tally.Precision:P2} | {tally.Strictly:P2} | " +
        $"{tally.Names:N0} | {tally.Found:N0} | {tally.Recall:P1} |";

    private static string Spelled(ConsensusBar bar) => $"{bar.LeastVerses}, {bar.LeastScore:0.0}, {bar.LeastMargin:0.00}";

    private static string Spelled(ConsensusBars bars) =>
        string.Join("; ", Kinds.Select(kind => $"{EnumSpelling.Of(kind)} {Spelled(bars.For(kind))}"));

    /// <summary>
    /// The held-out measurement, overall and kind by kind, per text and per language; the bars chosen
    /// on all measured texts, and what each kind and size measured at them, which is returned as the
    /// confidence of what is written.
    /// </summary>
    private static (ConsensusBars Bars, Confidence Confidence) Report(
        StringBuilder report,
        IReadOnlyList<Reading> measured,
        double precision,
        ConsensusBar? fixedBar)
    {
        var candidates = fixedBar is null ? Bars().ToList() : [fixedBar];
        var tallies = measured.ToDictionary(
            reading => reading.Slug,
            reading => candidates.ToDictionary(
                bar => bar,
                bar =>
                {
                    var words = NameConsensus.Settle(reading.Findings, reading.Claims, bar);
                    var small = reading.Findings.Where(finding => finding.Verses < FewVerses).Select(finding => finding.Entity).ToHashSet();
                    var fewWords = words.Where(word => small.Contains(word.Entity)).ToList();
                    return Kinds.ToDictionary(
                        kind => kind,
                        kind => (All: Score(reading, words, kind), Few: Score(reading, fewWords, kind)));
                }));

        ConsensusBar Choose(IReadOnlyList<string> on, EntityKind kind) =>
            candidates
                .Select(bar => (
                    bar,
                    tally: on.Aggregate(default(Tally), (sum, slug) => sum + tallies[slug][bar][kind].All),
                    small: on.Aggregate(default(Tally), (sum, slug) => sum + tallies[slug][bar][kind].Few)))
                .Where(entry => entry.tally.Precision >= precision
                                && (entry.small.Judged == 0 || entry.small.Precision >= precision))
                .OrderByDescending(entry => entry.tally.Right)
                .ThenBy(entry => entry.bar.LeastVerses)
                .ThenBy(entry => entry.bar.LeastScore)
                .Select(entry => entry.bar)
                .DefaultIfEmpty(fixedBar ?? new ConsensusBar(VerseBars[^1], ScoreBars[^1], MarginBars[^1]))
                .First();

        ConsensusBars ChooseAll(IReadOnlyList<string> on) =>
            new(Kinds.ToDictionary(kind => kind, kind => Choose(on, kind)), fixedBar ?? new ConsensusBar(VerseBars[^1], ScoreBars[^1], MarginBars[^1]));

        report.AppendLine(fixedBar is null
                ? $"Held out one text at a time; each kind's bar chosen on the others for precision {precision:P0}, overall and on the entities with fewer than {FewVerses} verses."
                : $"Held out one text at a time, every kind at the bar {Spelled(fixedBar)}.")
            .AppendLine();

        var heldOut = new Dictionary<string, List<ConsensusWord>>();
        var byText = new List<(Reading Reading, ConsensusBars Bars)>();
        foreach (var reading in measured)
        {
            var bars = ChooseAll(measured.Where(other => other != reading).Select(other => other.Slug).ToList());
            heldOut[reading.Slug] = NameConsensus.Settle(reading.Findings, reading.Claims, bars.Accepts).ToList();
            byText.Add((reading, bars));
        }

        report.AppendLine("Every kind together:").AppendLine().AppendLine(Header).AppendLine(Rule);
        var pooled = default(Tally);
        var byLanguage = new SortedDictionary<string, Tally>(StringComparer.Ordinal);
        foreach (var (reading, bars) in byText)
        {
            var tally = Score(reading, heldOut[reading.Slug]);
            pooled += tally;
            byLanguage[reading.Language] = byLanguage.GetValueOrDefault(reading.Language) + tally;
            report.AppendLine(Row(reading.Slug, reading.Language, "held out", tally));
        }

        report.AppendLine(Row("all held out", string.Empty, string.Empty, pooled));
        foreach (var (language, tally) in byLanguage)
        {
            report.AppendLine(Row(language, language, "held out", tally));
        }

        foreach (var kind in Kinds)
        {
            report.AppendLine()
                .AppendLine($"{EnumSpelling.Of(kind)}, held out:")
                .AppendLine()
                .AppendLine(Header)
                .AppendLine(Rule);
            var ofKind = default(Tally);
            var languages = new SortedDictionary<string, Tally>(StringComparer.Ordinal);
            foreach (var (reading, bars) in byText)
            {
                var tally = Score(reading, heldOut[reading.Slug], kind);
                ofKind += tally;
                languages[reading.Language] = languages.GetValueOrDefault(reading.Language) + tally;
            }

            foreach (var (language, tally) in languages)
            {
                report.AppendLine(Row(language, language, "held out", tally));
            }

            report.AppendLine(Row($"all {EnumSpelling.Of(kind)}", string.Empty, string.Empty, ofKind));
        }

        var all = measured.Select(reading => reading.Slug).ToList();
        var chosen = ChooseAll(all);
        report.AppendLine()
            .AppendLine($"Bars chosen on all measured texts: {Spelled(chosen)}.")
            .AppendLine()
            .AppendLine("At those bars, by kind and by how many verses the entity has in the text (all measured texts):")
            .AppendLine()
            .AppendLine(Header)
            .AppendLine(Rule);

        var confidence = new Dictionary<(EntityKind, int), double>();
        var settled = measured.ToDictionary(
            reading => reading.Slug,
            reading => NameConsensus.Settle(reading.Findings, reading.Claims, chosen.Accepts));
        foreach (var kind in Kinds)
        {
            var ofKind = measured.Aggregate(default(Tally), (sum, reading) => sum + Score(reading, settled[reading.Slug], kind));
            if (ofKind.Judged > 0)
            {
                confidence[(kind, -1)] = Math.Min(MostConfident, ofKind.Precision);
            }

            for (var band = 0; band < SizeBands.Length; band++)
            {
                var least = SizeBands[band];
                var most = band + 1 < SizeBands.Length ? SizeBands[band + 1] - 1 : int.MaxValue;
                var tally = measured.Aggregate(default(Tally), (sum, reading) =>
                {
                    var sized = reading.Findings
                        .Where(finding => finding.Kind == kind && finding.Verses >= least && finding.Verses <= most)
                        .Select(finding => finding.Entity)
                        .ToHashSet();
                    return sum + Score(reading, settled[reading.Slug].Where(word => sized.Contains(word.Entity)).ToList(), kind)
                        with { Names = 0 };
                });
                if (tally.Judged >= ConfidentBand)
                {
                    confidence[(kind, band)] = Math.Min(MostConfident, tally.Precision);
                }

                report.AppendLine(Row(
                    most == int.MaxValue ? $"{EnumSpelling.Of(kind)} {least}+ verses" : $"{EnumSpelling.Of(kind)} {least}–{most} verses",
                    string.Empty,
                    Spelled(chosen.For(kind)),
                    tally));
            }
        }

        return (chosen, new Confidence(confidence));
    }

    /// <summary>
    /// What the bars add to a text: words named that no annotation names, words it agrees with and
    /// words it names otherwise, and the entities the text had no word for at all.
    /// </summary>
    private static void Addition(
        Reading reading,
        ConsensusBars bars,
        IReadOnlyDictionary<int, string> slugs,
        StringBuilder table,
        StringBuilder examples,
        List<Disagreement> disagreements,
        int? written)
    {
        var words = NameConsensus.Settle(reading.Findings, reading.Claims, bars.Accepts);
        var accepted = reading.Findings.Where(bars.Accepts).Select(finding => finding.Entity).ToHashSet();
        var annotatedEntities = reading.Annotated.Values.SelectMany(entities => entities).ToHashSet();
        int agree = 0, added = 0, disagree = 0;
        var newByKind = Kinds.ToDictionary(kind => kind, _ => 0);
        foreach (var word in words)
        {
            if (!reading.Annotated.TryGetValue(word.Word, out var named))
            {
                added++;
                newByKind[reading.KindOf[word.Entity]]++;
            }
            else if (named.Contains(word.Entity))
            {
                agree++;
            }
            else
            {
                disagree++;
                var verse = reading.Verses[word.Verse];
                var position = Position(verse.Words, word.Word);
                disagreements.Add(new Disagreement(
                    reading.Slug,
                    Reference(verse.Addresses.Min()),
                    position + 1,
                    verse.Words[position].Surface,
                    slugs[word.Entity],
                    named.Select(entity => slugs[entity]).Order(StringComparer.Ordinal).ToList()));
            }
        }

        var already = reading.Annotated.SelectMany(entry => entry.Value.Select(entity => (entry.Key, entity))).ToList();
        var before = Reached(reading, already);
        var after = Reached(reading, already.Concat(words.Select(word => (word.Word, word.Entity))));
        table.AppendLine($"| {reading.Slug} | {reading.Language} | {accepted.Count:N0} | {accepted.Count(entity => !annotatedEntities.Contains(entity)):N0} | " +
            $"{words.Count:N0} | {agree:N0} | {added:N0} | {disagree:N0} | {before:N0} | {after:N0} | " +
            $"{string.Join(" / ", Kinds.Select(kind => newByKind[kind].ToString("N0", CultureInfo.InvariantCulture)))} | " +
            $"{(written is { } count ? count.ToString("N0", CultureInfo.InvariantCulture) : "–")} |");

        var entityOf = reading.Findings.ToDictionary(finding => finding.Entity);
        var shown = words
            .Where(word => !reading.Annotated.TryGetValue(word.Word, out var named) || !named.Contains(word.Entity))
            .GroupBy(word => word.Entity)
            .OrderByDescending(group => group.Count())
            .Take(ExamplesShown)
            .Select(group =>
            {
                var name = entityOf[group.Key].Name!;
                var disagreeing = group.Count(word => reading.Annotated.ContainsKey(word.Word));
                return $"{slugs[group.Key]} {string.Join("/", name.Forms.Take(3))} +{group.Count() - disagreeing}" +
                    (disagreeing > 0 ? $" ≠{disagreeing}" : string.Empty);
            });
        examples.AppendLine($"  {reading.Slug}: {string.Join("; ", shown)}");
    }

    private static string Reference(int address) =>
        $"{address / AddressBook}:{address / AddressChapter % AddressChapter}:{address % AddressChapter}";

    /// <summary>How many pairs of a verse and an entity the text names, counting each pair once however many words name it.</summary>
    private static int Reached(Reading reading, IEnumerable<(long Word, int Entity)> named)
    {
        var verseOf = new Dictionary<long, int>();
        for (var verse = 0; verse < reading.Verses.Count; verse++)
        {
            foreach (var (word, _) in reading.Verses[verse].Words)
            {
                verseOf[word] = verse;
            }
        }

        return named.Where(pair => verseOf.ContainsKey(pair.Word)).Select(pair => (verseOf[pair.Word], pair.Entity)).Distinct().Count();
    }

    /// <summary>
    /// Writes the text's settled findings as annotations on the words nothing names yet, in one
    /// transaction, and returns how many it wrote. A text already holding what an earlier run wrote is
    /// left as it is unless <paramref name="replace"/>, which takes those back first.
    /// </summary>
    private async Task<int> Apply(
        NpgsqlConnection connection,
        Reading reading,
        ConsensusBars bars,
        Confidence confidence,
        bool replace,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string ours =
            """
            FROM word_entity a USING word w
            WHERE w.id = a.word_id AND w.text_id = (SELECT id FROM text WHERE slug = @text) AND a.source = @source
            """;
        if (replace)
        {
            await using var withdraw = new NpgsqlCommand($"DELETE {ours}", connection, transaction);
            withdraw.Parameters.AddWithValue("text", reading.Slug);
            withdraw.Parameters.AddWithValue("source", Source);
            var withdrawn = await withdraw.ExecuteNonQueryAsync(cancellationToken);
            logger.LogInformation("{Text}: took back {Count} annotations an earlier run wrote", reading.Slug, withdrawn);
        }
        else
        {
            await using var exists = new NpgsqlCommand(
                """
                SELECT EXISTS (SELECT 1 FROM word_entity a JOIN word w ON w.id = a.word_id
                               WHERE w.text_id = (SELECT id FROM text WHERE slug = @text) AND a.source = @source)
                """, connection, transaction);
            exists.Parameters.AddWithValue("text", reading.Slug);
            exists.Parameters.AddWithValue("source", Source);
            if ((bool)(await exists.ExecuteScalarAsync(cancellationToken))!)
            {
                logger.LogInformation("{Text}: already holds what an earlier run wrote; --replace writes it again", reading.Slug);
                return 0;
            }
        }

        var findingOf = reading.Findings.ToDictionary(finding => finding.Entity);
        var rows = NameConsensus.Settle(reading.Findings, reading.Claims, bars.Accepts)
            .Where(word => !reading.Annotated.ContainsKey(word.Word))
            .Select(word =>
            {
                var finding = findingOf[word.Entity];
                return (word.Word, word.Entity, (double?)confidence.Of(finding), false, Note(finding));
            })
            .ToList();

        await using (var workspace = new NpgsqlCommand(Annotating.Workspace, connection, transaction))
        {
            await workspace.ExecuteNonQueryAsync(cancellationToken);
        }

        await Annotating.Seed(connection, rows, cancellationToken);
        int written;
        await using (var insert = new NpgsqlCommand(
                         """
                         INSERT INTO word_entity (word_id, entity_id, method, confidence, source, note)
                         SELECT a.word_id, a.entity_id, @method, a.confidence, @source, a.note
                         FROM pending_annotation a
                         WHERE NOT EXISTS (SELECT 1 FROM word_entity e WHERE e.word_id = a.word_id)
                         ON CONFLICT (word_id, entity_id) DO NOTHING
                         """, connection, transaction))
        {
            insert.Parameters.AddWithValue("method", EnumSpelling.Of(LinkMethod.RuleBased));
            insert.Parameters.AddWithValue("source", Source);
            written = await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var claim = new NpgsqlCommand(Annotating.Claim, connection, transaction))
        {
            claim.Parameters.AddWithValue("method", EnumSpelling.Of(LinkMethod.RuleBased));
            claim.Parameters.AddWithValue("source", Source);
            await claim.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("{Text}: wrote {Count} annotations", reading.Slug, written);
        return written;
    }

    /// <summary>What a written annotation says of how it was read.</summary>
    private static string Note(NameFinding finding)
    {
        var name = finding.Name!;
        return string.Create(CultureInfo.InvariantCulture,
            $"the word its verses share in this text: {string.Join(", ", name.Forms.Take(3))}, " +
            $"printed in {name.Covered} of its {finding.Verses} verses, {name.Specificity:P0} of the verses printing it being its own");
    }

    /// <summary>
    /// The review list, written whole. Only a run over every text replaces the list; a run over some
    /// of them keeps the other texts' entries and replaces its own.
    /// </summary>
    private static async Task Review(
        string path,
        IReadOnlyList<Disagreement> found,
        bool everyText,
        CancellationToken cancellationToken)
    {
        var json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var kept = new List<Disagreement>();
        if (!everyText && File.Exists(path))
        {
            var texts = found.Select(entry => entry.Text).ToHashSet(StringComparer.Ordinal);
            var earlier = JsonSerializer.Deserialize<ReviewList>(await File.ReadAllTextAsync(path, cancellationToken), json);
            kept.AddRange((earlier?.Words ?? []).Where(entry => !texts.Contains(entry.Text)));
        }

        var list = new ReviewList(
            "Words where the name read from the verses that name an entity disagrees with an annotation the word already carries. "
            + "Nothing here was written: the annotation already there stands. Reference is canonical book:chapter:verse, "
            + "position the word's place in the verse from 1, found the entity the verses read, annotated what the word carries now.",
            DateTimeOffset.UtcNow,
            [.. kept.Concat(found).OrderBy(entry => entry.Text, StringComparer.Ordinal).ThenBy(entry => entry.Reference, StringComparer.Ordinal).ThenBy(entry => entry.Position)]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(list, json) + "\n", cancellationToken);
    }

    private sealed record ReviewList(string About, DateTimeOffset Written, IReadOnlyList<Disagreement> Words);

    private static async Task Write(
        string output,
        Reading reading,
        ConsensusBars bars,
        IReadOnlyDictionary<int, string> slugs,
        CancellationToken cancellationToken)
    {
        var findings = new StringBuilder("entity\tkind\tverses\taccepted\tcore\tforms\tcoverage\tspecificity\tscore\tmargin\n");
        foreach (var finding in reading.Findings.Where(finding => finding.Name is not null)
                     .OrderByDescending(finding => finding.Verses))
        {
            var name = finding.Name!;
            findings.Append(CultureInfo.InvariantCulture,
                $"{slugs[finding.Entity]}\t{EnumSpelling.Of(finding.Kind)}\t{finding.Verses}\t{bars.Accepts(finding)}\t{name.Core}\t{string.Join(' ', name.Forms)}\t" +
                $"{name.Coverage:0.000}\t{name.Specificity:0.000}\t{name.Score:0.000}\t{finding.Margin:0.000}\n");
        }

        await File.WriteAllTextAsync(Path.Combine(output, $"{reading.Slug}-findings.tsv"), findings.ToString(), cancellationToken);

        var settled = NameConsensus.Settle(reading.Findings, reading.Claims, bars.Accepts);
        var words = new StringBuilder("address\tposition\tword\tentity\tkind\tannotated\tjudged\n");
        foreach (var word in settled)
        {
            var verse = reading.Verses[word.Verse];
            var position = Position(verse.Words, word.Word);
            var annotated = reading.Annotated.TryGetValue(word.Word, out var named)
                ? string.Join(' ', named.Select(entity => slugs[entity]))
                : string.Empty;
            words.Append(CultureInfo.InvariantCulture,
                $"{Reference(verse.Addresses.Min())}\t{position + 1}\t" +
                $"{verse.Words[position].Surface}\t{slugs[word.Entity]}\t{EnumSpelling.Of(reading.KindOf[word.Entity])}\t{annotated}\t" +
                $"{(Measured.Contains(reading.Slug) ? Judge(reading, word) : Verdict.Unjudged)}\n");
        }

        await File.WriteAllTextAsync(Path.Combine(output, $"{reading.Slug}-words.tsv"), words.ToString(), cancellationToken);

        if (!Measured.Contains(reading.Slug))
        {
            return;
        }

        var kept = settled.Select(word => (word.Word, word.Entity)).ToHashSet();
        var claimed = reading.Claims.Select(word => (word.Word, word.Entity)).ToHashSet();
        var findingOf = reading.Findings.ToDictionary(finding => finding.Entity);
        var missed = new StringBuilder("address\tposition\tword\tentity\twhy\n");
        for (var at = 0; at < reading.Verses.Count; at++)
        {
            var verse = reading.Verses[at];
            for (var position = 0; position < verse.Words.Count; position++)
            {
                var (word, surface) = verse.Words[position];
                if (surface.Length == 0 || char.IsLower(surface[0]))
                {
                    continue;
                }

                foreach (var entity in reading.Annotated.GetValueOrDefault(word) ?? [])
                {
                    if (!reading.Gold.Contains((word, entity)) || kept.Contains((word, entity)))
                    {
                        continue;
                    }

                    var why = !findingOf.TryGetValue(entity, out var finding) || finding.Name is null ? "no name found"
                        : !bars.Accepts(finding) ? "below the bar"
                        : !finding.Name.Forms.Contains(NameConsensus.Fold(surface)) ? "another form"
                        : !claimed.Contains((word, entity)) ? "outside its verses, or another entity of the verse claims it"
                        : "claimed by two";
                    missed.Append(CultureInfo.InvariantCulture,
                        $"{Reference(verse.Addresses.DefaultIfEmpty().Min())}\t{position + 1}\t{surface}\t{slugs[entity]}\t{why}\n");
                }
            }
        }

        await File.WriteAllTextAsync(Path.Combine(output, $"{reading.Slug}-missed.tsv"), missed.ToString(), cancellationToken);
    }

    /// <summary>
    /// Every word of a measured text the consensus names and the text's stated annotations do not
    /// name the same way, with what a reader needs to say whose fault it is: what the annotations
    /// put on the word and on the rest of the verse, whom the originals name there, and the verse.
    /// </summary>
    private static async Task Audit(
        string output,
        Reading reading,
        ConsensusBars bars,
        IReadOnlyDictionary<int, string> slugs,
        ConsensusQuestion question,
        CancellationToken cancellationToken)
    {
        var goldOn = reading.Gold
            .GroupBy(pair => pair.Word)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Entity).ToList());
        var namedAt = new Dictionary<int, List<int>>();
        foreach (var (entity, addresses) in question.Entities)
        {
            foreach (var address in addresses)
            {
                if (!namedAt.TryGetValue(address, out var entities))
                {
                    namedAt[address] = entities = [];
                }

                entities.Add(entity);
            }
        }

        var findingOf = reading.Findings.ToDictionary(finding => finding.Entity);
        var audit = new StringBuilder("address\tposition\tword\tentity\tkind\tverdict\tgold on word\tgold for entity in verse\toriginals name\tnamesakes\tcore\tforms\tverses\tverse\n");
        foreach (var word in NameConsensus.Settle(reading.Findings, reading.Claims, bars.Accepts))
        {
            var verdict = Judge(reading, word);
            if (verdict is Verdict.Right or Verdict.Unjudged)
            {
                continue;
            }

            var verse = reading.Verses[word.Verse];
            var position = Position(verse.Words, word.Word);
            var goldHere = verse.Words
                .Select((printed, at) => (printed, at))
                .Where(entry => reading.Gold.Contains((entry.printed.Word, word.Entity)))
                .Select(entry => $"{entry.at + 1}:{entry.printed.Surface}");
            var originals = verse.Addresses
                .SelectMany(at => namedAt.GetValueOrDefault(at) ?? [])
                .Distinct()
                .Where(entity => entity != word.Entity)
                .Select(entity => slugs[entity]);
            var namesakes = question.Namesakes.GetValueOrDefault(word.Entity) is { } others
                ? string.Join(' ', others.Select(entity => slugs[entity]).Order(StringComparer.Ordinal).Take(8))
                : string.Empty;
            var name = findingOf[word.Entity];
            audit.Append(CultureInfo.InvariantCulture,
                $"{Reference(verse.Addresses.Min())}\t{position + 1}\t" +
                $"{verse.Words[position].Surface}\t{slugs[word.Entity]}\t{EnumSpelling.Of(name.Kind)}\t{verdict}\t" +
                $"{string.Join(' ', (goldOn.GetValueOrDefault(word.Word) ?? []).Select(entity => slugs[entity]))}\t" +
                $"{string.Join(' ', goldHere)}\t{string.Join(' ', originals)}\t{namesakes}\t{name.Name!.Core}\t" +
                $"{string.Join(' ', name.Name.Forms.Take(6))}\t{name.Verses}\t" +
                $"{string.Join(' ', verse.Words.Select(printed => printed.Surface))}\n");
        }

        await File.WriteAllTextAsync(Path.Combine(output, $"{reading.Slug}-audit.tsv"), audit.ToString(), cancellationToken);
    }

    /// <summary>
    /// Each entity's verses of every kind read, as the originals' settled annotations place them, and
    /// its namesakes: the entities the originals name with a lemma it is also named with.
    /// </summary>
    private static async Task<ConsensusQuestion> Question(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var sql =
            $"""
             WITH {Annotating.Settled}
             SELECT s.entity_id, r.canonical_book, r.canonical_chapter, r.canonical_verse,
                    coalesce(w.lemma, w.normalised_text, w.text), e.kind
             FROM settled s
             JOIN entity e ON e.id = s.entity_id AND e.kind = ANY(@kinds)
             JOIN word w ON w.id = s.word_id
             JOIN text t ON t.id = w.text_id AND t.slug = ANY(@originals)
             JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
             """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("kinds", Kinds.Select(EnumSpelling.Of).ToArray());
        command.Parameters.AddWithValue("originals", Originals);

        var spelled = Kinds.ToDictionary(EnumSpelling.Of, kind => kind);
        var entities = new Dictionary<int, HashSet<int>>();
        var kinds = new Dictionary<int, EntityKind>();
        var lemmas = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var entity = reader.GetInt32(0);
                if (!entities.TryGetValue(entity, out var addresses))
                {
                    entities[entity] = addresses = [];
                    kinds[entity] = spelled[reader.GetString(5)];
                }

                addresses.Add(Address(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)));
                var lemma = reader.GetString(4);
                if (!lemmas.TryGetValue(lemma, out var named))
                {
                    lemmas[lemma] = named = [];
                }

                named.Add(entity);
            }
        }

        var namesakes = new Dictionary<int, HashSet<int>>();
        foreach (var named in lemmas.Values.Where(named => named.Count > 1))
        {
            foreach (var entity in named)
            {
                if (!namesakes.TryGetValue(entity, out var others))
                {
                    namesakes[entity] = others = [];
                }

                others.UnionWith(named.Where(other => other != entity));
            }
        }

        return new ConsensusQuestion(
            entities.ToDictionary(entry => entry.Key, entry => (IReadOnlySet<int>)entry.Value),
            namesakes.ToDictionary(entry => entry.Key, entry => (IReadOnlySet<int>)entry.Value),
            lemmas
                .SelectMany(entry => entry.Value.Select(entity => (entity, lemma: entry.Key)))
                .GroupBy(pair => pair.entity)
                .ToDictionary(group => group.Key, group => (IReadOnlySet<string>)group.Select(pair => pair.lemma).ToHashSet()),
            kinds);
    }

    private static int Address(int book, int chapter, int verse) => book * AddressBook + chapter * AddressChapter + verse;

    private static async Task<Reading> Read(
        NpgsqlConnection connection,
        int textId,
        string slug,
        string language,
        ConsensusQuestion question,
        CancellationToken cancellationToken)
    {
        var addresses = new Dictionary<int, List<int>>();
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT r.verse_id, r.canonical_book, r.canonical_chapter, r.canonical_verse
                         FROM verse_reference r JOIN verse v ON v.id = r.verse_id
                         WHERE v.text_id = @text
                         """, connection))
        {
            command.Parameters.AddWithValue("text", textId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var verse = reader.GetInt32(0);
                if (!addresses.TryGetValue(verse, out var at))
                {
                    addresses[verse] = at = [];
                }

                at.Add(Address(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)));
            }
        }

        var verses = new List<ConsensusVerse>();
        var indexOf = new Dictionary<int, int>();
        await using (var command = new NpgsqlCommand(
                         "SELECT verse_id, id, text FROM word WHERE text_id = @text ORDER BY verse_id, position", connection))
        {
            command.Parameters.AddWithValue("text", textId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            List<(long, string)>? words = null;
            var current = -1;
            while (await reader.ReadAsync(cancellationToken))
            {
                var verse = reader.GetInt32(0);
                if (verse != current)
                {
                    current = verse;
                    words = [];
                    indexOf[verse] = verses.Count;
                    verses.Add(new ConsensusVerse(addresses.GetValueOrDefault(verse) ?? [], words));
                }

                words!.Add((reader.GetInt64(1), reader.GetString(2)));
            }
        }

        var annotated = new Dictionary<long, HashSet<int>>();
        var gold = new HashSet<(long, int)>();
        var goldForms = new Dictionary<(int, int), HashSet<string>>();
        var goldWords = new HashSet<long>();
        var goldNames = new Dictionary<EntityKind, int>();
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT a.word_id, a.entity_id, w.verse_id, w.text, a.note LIKE ANY(@stated)
                         FROM word_entity a JOIN word w ON w.id = a.word_id
                         WHERE w.text_id = @text AND a.source <> @ours
                         """, connection))
        {
            command.Parameters.AddWithValue("text", textId);
            command.Parameters.AddWithValue("ours", Source);
            command.Parameters.AddWithValue("stated", StatedLinks);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var word = reader.GetInt64(0);
                var entity = reader.GetInt32(1);
                if (!annotated.TryGetValue(word, out var named))
                {
                    annotated[word] = named = [];
                }

                named.Add(entity);
                if (!Measured.Contains(slug) || reader.IsDBNull(4) || !reader.GetBoolean(4)
                    || !question.Entities.ContainsKey(entity))
                {
                    continue;
                }

                gold.Add((word, entity));
                goldWords.Add(word);
                var placed = (indexOf[reader.GetInt32(2)], entity);
                if (!goldForms.TryGetValue(placed, out var forms))
                {
                    goldForms[placed] = forms = [];
                }

                var surface = reader.GetString(3);
                forms.Add(NameConsensus.Fold(surface));
                var kind = question.KindOf(entity);
                if (!ConsensusRules.For(kind).Capitalised || (surface.Length > 0 && !char.IsLower(surface[0])))
                {
                    goldNames[kind] = goldNames.GetValueOrDefault(kind) + 1;
                }
            }
        }

        var (findings, claims) = NameConsensus.Read(verses, question);
        return new Reading(
            slug, language, verses, findings, claims, annotated, gold, goldForms, goldWords, goldNames,
            question.Entities.Keys.ToDictionary(entity => entity, question.KindOf));
    }
}
