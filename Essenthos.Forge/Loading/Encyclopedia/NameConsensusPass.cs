using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Essenthos.Core.Conversion;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Indexes;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Names;
using Essenthos.Core.Products;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// <see cref="NameConsensus"/> over the corpus, read from files: every text read for the words naming each person,
/// place, people, title, thing and appointed time the originals name, measured against the texts whose names came across
/// a link the text's own edition states or numbers, and written as <c>names/consensus/&lt;SLUG&gt;</c>, whom each
/// translation's verses say its words name, which names-pairs reads (<see cref="NamesFirstSource"/>). Reads no database.
///
/// <para>
/// <strong>An entity's verses are the originals'.</strong> The verses on an encyclopedia page are
/// read off every text's annotations, the one being measured included, so a text would be scored on
/// verses its own annotations chose. Here they are the canonical addresses where BHSA or a Greek New
/// Testament names the entity in its settled names (<c>names/&lt;REF&gt;</c>, one record a word as
/// <see cref="Annotating.Settled"/> settles it) — the same claim with the translations left out of it — and a
/// namesake is an entity the originals call by one of the same words.
/// </para>
///
/// <para>
/// <strong>A text's own names are what the earlier methods carry.</strong> What a word is already named, which the
/// consensus writes nothing over, is the names projection carried over the method files that come before the names
/// (<see cref="NamesFirstSource.Before"/>: stated, Strong, aligner, composed, lexical, possessive) of the pairs names-pairs
/// places, never over the final: the final holds the names this file places, so reading it would make the graph read
/// itself. Such a word is linked in the pair already, so names-pairs never places it; a word named only over another pair
/// (Brenton over his Greek) keeps the consensus, which names-pairs needs there. A name carried by EVIDENTIA's or the names'
/// own links does not keep a word from the consensus. The measured texts are scored against what their stated and Strong
/// files alone carry.
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
/// </summary>
internal static class NameConsensusPass
{
    /// <summary>The witnesses whose annotations say which verses name whom.</summary>
    private static readonly string[] Originals = ["BHSA", "NESTLE1904", "TR1894"];

    /// <summary>
    /// The originals whose own words are never read: no pair places a translation's names on theirs. The Textus Receptus
    /// is read, as names-pairs places its names on the Nestle's.
    /// </summary>
    private static readonly string[] Unread = ["BHSA", "NESTLE1904"];

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

    /// <summary>The method files a measured text's names are scored against: the ones its edition states or numbers.</summary>
    private static readonly string[] StatedMethods = [MappingMethods.Stated, MappingMethods.Strong];

    public const string Producer = "forge names-consensus";

    /// <summary>
    /// The source a name this pass once wrote is credited to. A settled or carried name of that source is the consensus's
    /// own reading kept as a decision (31,177 of the Vulgate's settled names): it neither keeps a word from the consensus nor
    /// scores it.
    /// </summary>
    public const string Source = Essenthos.Core.Corpus.Annotations.Consensus;

    /// <summary>Raised with any change to what the consensus writes or how.</summary>
    public const string Version = "1";

    public const string VersionNote = "consensus-version";

    /// <summary>The records the consensus reads: each one's kind.</summary>
    private const string KindsProduct = Essenthos.Core.Records.RecordKinds.Product;

    /// <summary>The titles and the records that bear them: a title settled on a word gives way to its bearer there.</summary>
    private const string TitlesProduct = "records/facts/titles";

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

    private const int AddressBook = 1_000_000;
    private const int AddressChapter = 1_000;

    private const int ExamplesShown = 12;

    /// <param name="Written">Whether the files were written; false where they were current or the run only measured.</param>
    public sealed record Outcome(bool Written, int Texts, long Rows, string Report);

    /// <summary>The texts whose words the consensus names, in name order.</summary>
    internal static IReadOnlyList<string> Texts(IndexContext context) =>
        [.. context.Texts.Keys.Where(slug => !Unread.Contains(slug)).Order(StringComparer.Ordinal)];

    /// <summary>
    /// Writes every text's <c>names/consensus/&lt;SLUG&gt;</c> where an input moved, or measures only: a run at another
    /// precision or bar than the build's reports and writes no product.
    /// </summary>
    /// <param name="precision">The precision each kind's bar must reach on the measured texts.</param>
    /// <param name="bar">One bar for every kind, or null to choose each on the measured texts.</param>
    /// <param name="output">A folder to write each text's findings and words into, or none.</param>
    public static Outcome Write(
        ProductStore store,
        bool force,
        double precision,
        ConsensusBar? bar,
        string? output,
        int parallel,
        Action<string> log)
    {
        var started = Stopwatch.StartNew();
        var writes = bar is null && precision == Precision;
        var projection = new NamesProjection(store);
        var context = projection.Context;
        var texts = Texts(context);
        var inputs = Inputs(store, projection, texts);
        if (writes && !force && !NeedsBuild(store, texts, inputs))
        {
            return new Outcome(false, texts.Count, 0, "names/consensus is current");
        }

        var records = Records.Read(store);
        var question = Question(store, records);
        log($"question: {question.Entities.Count} entities the originals name, {question.Namesakes.Count} with namesakes ({started.Elapsed:mm\\:ss})");

        var report = new StringBuilder();
        var table = new StringBuilder()
            .AppendLine("| text | language | entities named | of them new to the text | words | agree | new | disagree | verses reached before | after | new by kind (person, place, people, title, object, observance) | written |")
            .AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|");
        var examples = new StringBuilder();
        if (output is not null)
        {
            Directory.CreateDirectory(output);
        }

        var measured = texts.Where(Measured.Contains)
            .AsParallel().AsOrdered().WithDegreeOfParallelism(Math.Max(1, parallel))
            .Select(slug => Read(store, projection, slug, question, records, true, log))
            .ToList();
        log($"read the {measured.Count} measured texts ({started.Elapsed:mm\\:ss})");

        var bars = Report(report, measured, precision, bar);

        var results = new ConcurrentDictionary<string, (Reading Reading, IReadOnlyList<ConsensusWord> Words, string Row, string Example)>(StringComparer.Ordinal);
        void Add(Reading reading)
        {
            var words = Written(reading, bars);
            var (row, example) = Addition(reading, bars, records.Slugs, words.Count);
            results[reading.Slug] = (reading, words, row, example);
            if (output is not null)
            {
                Write(output, reading, bars, records.Slugs);
                Unnamed(output, reading, bars, records.Slugs, question);
                if (Measured.Contains(reading.Slug))
                {
                    Audit(output, reading, bars, records.Slugs, question);
                }
            }
        }

        var attested = new Dictionary<string, Dictionary<int, HashSet<string>>>(StringComparer.Ordinal);
        foreach (var reading in measured)
        {
            if (!attested.TryGetValue(reading.Language, out var language))
            {
                attested[reading.Language] = language = [];
            }

            NameConsensus.Witness(language, reading.Findings, bars);
        }

        foreach (var reading in measured)
        {
            Add(reading);
        }

        // A text is read against the fuller texts of its language before it: each language's texts in turn, the one holding
        // the most verses first, the languages at once.
        var rest = texts.Where(slug => !Measured.Contains(slug))
            .Select(slug => (Slug: slug, context.Texts[slug].Language, Verses: VerseCount(store, slug)))
            .GroupBy(text => text.Language, StringComparer.Ordinal)
            .ToList();
        Parallel.ForEach(rest, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel) }, language =>
        {
            var witnessed = attested.GetValueOrDefault(language.Key) ?? [];
            foreach (var text in language.OrderByDescending(text => text.Verses).ThenBy(text => text.Slug, StringComparer.Ordinal))
            {
                var reading = Read(store, projection, text.Slug, question with { Attested = witnessed }, records, false, log);
                Add(reading);
                NameConsensus.Witness(witnessed, reading.Findings, bars);
                log($"read {text.Slug} ({started.Elapsed:mm\\:ss})");
            }
        });

        foreach (var slug in texts)
        {
            table.AppendLine(results[slug].Row);
            examples.AppendLine(results[slug].Example);
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

        long rows = 0;
        if (writes)
        {
            foreach (var slug in texts)
            {
                rows += WriteFile(store, slug, results[slug].Reading, results[slug].Words, records.Slugs, inputs);
            }
        }

        report.AppendLine().AppendLine($"Read {texts.Count} texts in {started.Elapsed:hh\\:mm\\:ss}" +
                                       (writes ? $"; {rows} words written to names/consensus." : "; measured only, nothing written."));
        return new Outcome(writes, texts.Count, rows, report.ToString());
    }

    /// <summary>The words a text's file holds: what the bars settle, on the words nothing the earlier methods carry names.</summary>
    private static IReadOnlyList<ConsensusWord> Written(Reading reading, ConsensusBars bars) =>
        [.. NameConsensus.Settle(reading.Findings, reading.Claims, bars.Accepts).Where(word => !reading.Annotated.ContainsKey(word.Word))];

    private static long WriteFile(
        ProductStore store,
        string slug,
        Reading reading,
        IReadOnlyList<ConsensusWord> words,
        IReadOnlyList<string> slugs,
        IReadOnlyList<ProductInput> inputs)
    {
        var rows = words
            .Select(word => new ConsensusName(reading.Keys[(int)word.Word], slugs[word.Entity]))
            .OrderBy(row => row.Own)
            .ThenBy(row => row.Record, StringComparer.Ordinal)
            .ToList();
        using var writer = store.Create(NameProducts.Consensus(slug), ProductKind.Machine, ConsensusName.Schema(slug),
            ProductHeader.DescribeProducer(Producer), inputs,
            header =>
            {
                header.Notes.Add(new(VersionNote, Version));
                header.Comments.Add("whom the text's verses say its words name: the word every verse naming a record prints and few others " +
                                    "do, on the words the earlier methods carry no name to");
            });
        writer.WriteAll(rows);
        writer.Complete();
        return writer.Rows;
    }

    private static bool NeedsBuild(ProductStore store, IReadOnlyList<string> texts, IReadOnlyList<ProductInput> inputs)
    {
        foreach (var slug in texts)
        {
            var product = NameProducts.Consensus(slug);
            if (store.NeedsBuild(product, ProductKind.Machine, inputs))
            {
                return true;
            }

            using var reader = store.Open(product);
            if (reader.Header?.Notes.LastOrDefault(note => note.Key == VersionNote).Value != Version)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What the run reads: every text's words, frame and declaration, the originals' settled names, the records' kinds and
    /// titles, and what the names projection reads of each text: the earlier method files of the pairs names-pairs places,
    /// and a measured text's stated and Strong files of every pair.
    /// </summary>
    private static IReadOnlyList<ProductInput> Inputs(ProductStore store, NamesProjection projection, IReadOnlyList<string> texts)
    {
        var read = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var slug in texts.Concat(Originals).Distinct(StringComparer.Ordinal)
                     .Where(slug => projection.Context.Texts.ContainsKey(slug) && (!NameProducts.IsReference(slug) || store.Exists(NameProducts.Settled(slug)))))
        {
            read.UnionWith(ProductText.Products(slug).Append(VerseManifestRow.ProductOf(slug)).Select(store.Find).OfType<string>());
            read.UnionWith(projection.InputsOf(slug).Select(input => input.Path)
                .Where(path => Path.GetFileNameWithoutExtension(path) != MappingProducts.Final));
            foreach (var (pair, _, _) in projection.ReferencePairs(slug))
            {
                IEnumerable<string> methods = NamesFirstSource.Placed.Contains(pair) ? NamesFirstSource.Before : Measured.Contains(slug) ? StatedMethods : [];
                read.UnionWith(methods.Select(method => store.Find(MappingProducts.Of(pair, method))).OfType<string>());
            }
        }

        read.UnionWith(Originals.Select(NameProducts.Settled).Append(KindsProduct).Append(TitlesProduct).Select(store.Find).OfType<string>());
        return store.Inputs(read);
    }

    private static int VerseCount(ProductStore store, string slug)
    {
        if (!store.Exists(VerseManifestRow.ProductOf(slug)))
        {
            return 0;
        }

        using var reader = store.Open(VerseManifestRow.ProductOf(slug), VerseManifestRow.Schema);
        return reader.Rows().Count();
    }

    /// <summary>One text read: what the consensus found, and what the text's annotations already say.</summary>
    /// <param name="Annotated">
    /// Every word of the text the names projection carries a name to over the earlier method files, and the entities it
    /// names, but for the names of the consensus's own source.
    /// </param>
    /// <param name="Gold">The words a stated or Strong link carries a name to, with their entity.</param>
    /// <param name="GoldForms">Each verse and entity a stated link names, and the folded forms it names it with there.</param>
    /// <param name="GoldNames">By kind, how many of <paramref name="Gold"/> recall is counted over.</param>
    /// <param name="Keys">Each word's key, by its number in the text.</param>
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
        IReadOnlyDictionary<int, EntityKind> KindOf,
        IReadOnlyList<WordKey> Keys);

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
    /// on all measured texts, which are returned, and what each kind and size measured at them.
    /// </summary>
    private static ConsensusBars Report(
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

        var settled = measured.ToDictionary(
            reading => reading.Slug,
            reading => NameConsensus.Settle(reading.Findings, reading.Claims, chosen.Accepts));
        foreach (var kind in Kinds)
        {
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
                report.AppendLine(Row(
                    most == int.MaxValue ? $"{EnumSpelling.Of(kind)} {least}+ verses" : $"{EnumSpelling.Of(kind)} {least}–{most} verses",
                    string.Empty,
                    Spelled(chosen.For(kind)),
                    tally));
            }
        }

        return chosen;
    }

    /// <summary>
    /// What the bars add to a text: words named that no annotation names, words it agrees with and
    /// words it names otherwise, and the entities the text had no word for at all.
    /// </summary>
    /// <returns>The text's row of the table, and its line of examples.</returns>
    private static (string Row, string Example) Addition(
        Reading reading,
        ConsensusBars bars,
        IReadOnlyList<string> slugs,
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
            }
        }

        var already = reading.Annotated.SelectMany(entry => entry.Value.Select(entity => (entry.Key, entity))).ToList();
        var before = Reached(reading, already);
        var after = Reached(reading, already.Concat(words.Select(word => (word.Word, word.Entity))));
        var row = ($"| {reading.Slug} | {reading.Language} | {accepted.Count:N0} | {accepted.Count(entity => !annotatedEntities.Contains(entity)):N0} | " +
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
        return (row, $"  {reading.Slug}: {string.Join("; ", shown)}");
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

    private static void Write(
        string output,
        Reading reading,
        ConsensusBars bars,
        IReadOnlyList<string> slugs)
    {
        var findings = new StringBuilder("entity\tkind\tverses\taccepted\tcore\tforms\tcoverage\tspecificity\tscore\tmargin\tlikeness\n");
        foreach (var finding in reading.Findings.Where(finding => finding.Name is not null)
                     .OrderByDescending(finding => finding.Verses))
        {
            var name = finding.Name!;
            findings.Append(CultureInfo.InvariantCulture,
                $"{slugs[finding.Entity]}\t{EnumSpelling.Of(finding.Kind)}\t{finding.Verses}\t{bars.Accepts(finding)}\t{name.Core}\t{string.Join(' ', name.Forms)}\t" +
                $"{name.Coverage:0.000}\t{name.Specificity:0.000}\t{name.Score:0.000}\t{finding.Margin:0.000}\t{finding.Likeness:0.00}\n");
        }

        File.WriteAllText(Path.Combine(output, $"{reading.Slug}-findings.tsv"), findings.ToString());

        var settled = NameConsensus.Settle(reading.Findings, reading.Claims, bars.Accepts);
        var words = new StringBuilder("address\tposition\tword\tentity\tkind\tannotated\tjudged\town\n");
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
                $"{(Measured.Contains(reading.Slug) ? Judge(reading, word) : Verdict.Unjudged)}\t{reading.Keys[(int)word.Word].Own}\n");
        }

        File.WriteAllText(Path.Combine(output, $"{reading.Slug}-words.tsv"), words.ToString());

        var carried = new StringBuilder("own\trecords\n");
        foreach (var (word, named) in reading.Annotated.OrderBy(entry => entry.Key))
        {
            carried.Append(CultureInfo.InvariantCulture,
                $"{reading.Keys[(int)word].Own}\t{string.Join(' ', named.Select(entity => slugs[entity]).Order(StringComparer.Ordinal))}\n");
        }

        File.WriteAllText(Path.Combine(output, $"{reading.Slug}-carried.tsv"), carried.ToString());

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

        File.WriteAllText(Path.Combine(output, $"{reading.Slug}-missed.tsv"), missed.ToString());
    }

    /// <summary>The last book of the Old Testament in canonical order.</summary>
    private const int Malachi = 39;

    /// <summary>
    /// Every Old Testament verse of the text the originals name somebody in and the text names nobody
    /// in, with each entity named there and why the consensus did not name it: no word particular to
    /// its verses, a finding under the bar and which part of the bar, or a finding accepted whose
    /// name the verse prints in a form outside it, in lower case, left to another entity or to
    /// nobody. Beside the reason, the words of the verse the text's own annotations name the entity
    /// with elsewhere, and the capitalised words spelt like the original's lemma.
    /// </summary>
    private static void Unnamed(
        string output,
        Reading reading,
        ConsensusBars bars,
        IReadOnlyList<string> slugs,
        ConsensusQuestion question)
    {
        var settled = NameConsensus.Settle(reading.Findings, reading.Claims, bars.Accepts);
        var named = reading.Annotated.Keys.Concat(settled.Select(word => word.Word)).ToHashSet();
        var claimed = reading.Claims.Select(word => (word.Word, word.Entity)).ToHashSet();
        var findingOf = reading.Findings.ToDictionary(finding => finding.Entity);
        var namedAt = new Dictionary<int, List<int>>();
        foreach (var (entity, addresses) in question.Entities)
        {
            foreach (var address in addresses.Where(address => address / AddressBook <= Malachi))
            {
                if (!namedAt.TryGetValue(address, out var entities))
                {
                    namedAt[address] = entities = [];
                }

                entities.Add(entity);
            }
        }

        var formOf = reading.Verses.SelectMany(verse => verse.Words).ToDictionary(word => word.Word, word => NameConsensus.Fold(word.Surface));
        var known = new Dictionary<int, HashSet<string>>();
        foreach (var (word, entities) in reading.Annotated
                     .Select(entry => (entry.Key, (IEnumerable<int>)entry.Value))
                     .Concat(settled.Select(word => (word.Word, (IEnumerable<int>)[word.Entity]))))
        {
            foreach (var entity in entities)
            {
                if (!known.TryGetValue(entity, out var forms))
                {
                    known[entity] = forms = [];
                }

                if (formOf.TryGetValue(word, out var form) && form.Length > 0)
                {
                    forms.Add(form);
                }
            }
        }

        var language = reading.Language;
        var lemmas = question.Spellings?.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Select(lemma => NameLists.Skeleton(lemma)).ToList()) ?? [];
        var rows = new StringBuilder("address\tentity\tkind\tits verses\tin the text\twhy\tknown form here\tspelt like the original\tcore\tverse\n");
        for (var at = 0; at < reading.Verses.Count; at++)
        {
            var verse = reading.Verses[at];
            if (verse.Words.Any(word => named.Contains(word.Word)))
            {
                continue;
            }

            var entities = verse.Addresses.SelectMany(address => namedAt.GetValueOrDefault(address) ?? []).Distinct().ToList();
            if (entities.Count == 0)
            {
                continue;
            }

            var folded = verse.Words.Select(word => NameConsensus.Fold(word.Surface)).ToList();
            foreach (var entity in entities)
            {
                var finding = findingOf.GetValueOrDefault(entity);
                var bar = bars.For(question.KindOf(entity));
                string why;
                if (finding?.Name is not { } name)
                {
                    why = "no word particular to its verses";
                }
                else if (!bars.Accepts(finding))
                {
                    why = finding.Verses < bar.LeastVerses
                          && !(ConsensusRules.For(finding.Kind).Spelt && finding.Likeness >= NameLists.LeastLikeness)
                        ? question.Entities[entity].Count >= bar.LeastVerses ? "partial text: fewer of its verses than the bar" : "few verses"
                        : name.Coverage < NameConsensus.LeastCoverage ? "printed in few of its verses"
                        : name.Score < bar.LeastScore ? "score under the bar"
                        : "margin under the bar";
                }
                else
                {
                    var printing = Enumerable.Range(0, folded.Count).Where(position => name.Forms.Contains(folded[position])).ToList();
                    why = printing.Count == 0
                        ? folded.Any(form => form.Contains(name.Core, StringComparison.Ordinal)) ? "a form outside the name" : "the name not printed"
                        : printing.Any(position => claimed.Contains((verse.Words[position].Word, entity))) ? "claimed by two"
                        : printing.All(position => verse.Words[position].Surface.FirstOrDefault(char.IsLetter) is var first && char.IsLower(first)) ? "lower case"
                        : "left to another entity of the verse";
                }

                var knownHere = known.TryGetValue(entity, out var forms)
                    ? verse.Words.Where((_, position) => forms.Contains(folded[position])).Select(word => word.Surface)
                    : [];
                var spelt = lemmas.TryGetValue(entity, out var skeletons) && NameConsensus.Spelt(folded.FirstOrDefault(form => form.Length > 0) ?? string.Empty)
                    ? verse.Words
                        .Where(word => word.Surface.FirstOrDefault(char.IsLetter) is var first && !char.IsLower(first))
                        .Where(word => skeletons.Max(lemma => NameLists.Alike(NameLists.Skeleton(word.Surface, language), lemma)) >= NameLists.LeastLikeness)
                        .Select(word => word.Surface)
                    : [];
                rows.Append(CultureInfo.InvariantCulture,
                    $"{Reference(verse.Addresses.Min())}\t{slugs[entity]}\t{EnumSpelling.Of(question.KindOf(entity))}\t" +
                    $"{question.Entities[entity].Count}\t{finding?.Verses ?? 0}\t{why}\t{string.Join(' ', knownHere)}\t{string.Join(' ', spelt)}\t" +
                    $"{finding?.Name?.Core}\t{string.Join(' ', verse.Words.Select(word => word.Surface))}\n");
            }
        }

        File.WriteAllText(Path.Combine(output, $"{reading.Slug}-unnamed.tsv"), rows.ToString());
    }

    /// <summary>
    /// Every word of a measured text the consensus names and the text's stated annotations do not
    /// name the same way, with what a reader needs to say whose fault it is: what the annotations
    /// put on the word and on the rest of the verse, whom the originals name there, and the verse.
    /// </summary>
    private static void Audit(
        string output,
        Reading reading,
        ConsensusBars bars,
        IReadOnlyList<string> slugs,
        ConsensusQuestion question)
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

        File.WriteAllText(Path.Combine(output, $"{reading.Slug}-audit.tsv"), audit.ToString());
    }

    /// <summary>The records by slug, each a number in slug order, with its kind, and the titles each record bears.</summary>
    internal sealed record Records(
        IReadOnlyDictionary<string, int> Ids,
        IReadOnlyList<string> Slugs,
        IReadOnlyList<EntityKind?> Kinds,
        IReadOnlySet<(int Title, int Bearer)> Bearers)
    {
        public static Records Read(ProductStore store)
        {
            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            var slugs = new List<string>();
            var kinds = new List<EntityKind?>();
            using (var reader = store.Open(KindsProduct, Essenthos.Core.Records.RecordKind.Schema))
            {
                foreach (var record in reader.Rows().OrderBy(record => record.Slug, StringComparer.Ordinal))
                {
                    ids[record.Slug] = slugs.Count;
                    slugs.Add(record.Slug);
                    kinds.Add(Kind(record.Kind));
                }
            }

            var bearers = new HashSet<(int, int)>();
            if (store.Exists(TitlesProduct))
            {
                using var reader = store.Open(TitlesProduct);
                var columns = reader.Columns.Select(column => column.Name).ToList();
                var (title, bearer) = (columns.IndexOf("title"), columns.IndexOf("bearer"));
                foreach (var row in reader.Rows())
                {
                    if (ids.TryGetValue(row[title]!, out var one) && ids.TryGetValue(row[bearer]!, out var other))
                    {
                        bearers.Add((one, other));
                    }
                }
            }

            return new Records(ids, slugs, kinds, bearers);
        }

        private static EntityKind? Kind(string spelled)
        {
            try
            {
                return EnumSpelling.ToEntityKind(spelled);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>One name settled on a word, as the settling weighs it.</summary>
    /// <param name="Own">Whether it is a reading of the word, and not the consensus's (<see cref="Source"/>).</param>
    internal readonly record struct Settling(int Entity, EntityKind Kind, int Standing, double Confidence, string Source, string Note, bool Own = true);

    /// <summary>
    /// The one record a word of an original is named as, as <see cref="Annotating.Settled"/> settles a word's names: a reading
    /// of the word before the consensus's, then the highest standing, then confidence, a title after the record it is not, then kind and record; none where the next
    /// stands as high and names another record, unless the two stand beside each other; and a title gives way to its
    /// bearer named on the same word.
    /// </summary>
    internal static int? Settle(IReadOnlyCollection<Settling> names, IReadOnlySet<(int Title, int Bearer)> bearers)
    {
        var ranked = names
            .GroupBy(name => name.Entity)
            .Select(group => group.OrderByDescending(name => name.Own).ThenByDescending(name => name.Standing)
                .ThenByDescending(name => name.Confidence).First())
            .OrderByDescending(name => name.Own)
            .ThenByDescending(name => name.Standing)
            .ThenByDescending(name => name.Confidence)
            .ThenBy(name => name.Kind == EntityKind.Title)
            .ThenBy(name => EnumSpelling.Of(name.Kind), StringComparer.Ordinal)
            .ThenBy(name => name.Entity)
            .ToList();
        if (ranked.Count == 0)
        {
            return null;
        }

        var (top, next) = (ranked[0], ranked.Count > 1 ? ranked[1] : (Settling?)null);
        if (next is { } rival && rival.Own == top.Own && rival.Standing == top.Standing && rival.Confidence == top.Confidence && !Beside(top, rival, bearers))
        {
            return null;
        }

        if (top.Kind == EntityKind.Title)
        {
            foreach (var other in ranked.Skip(1).Where(other => bearers.Contains((top.Entity, other.Entity))))
            {
                return other.Entity;
            }
        }

        return top.Entity;
    }

    private static readonly int Ruled = ClaimStanding.Of(LinkMethod.Manual);

    private static bool Beside(Settling one, Settling other, IReadOnlySet<(int Title, int Bearer)> bearers) =>
        (one.Kind, other.Kind) is (EntityKind.Person, EntityKind.People) or (EntityKind.People, EntityKind.Person)
        || ((one.Kind, other.Kind) is (EntityKind.Place, EntityKind.People) or (EntityKind.People, EntityKind.Place)
            && one.Standing == Ruled && other.Standing == Ruled)
        || (((one.Kind, other.Kind) is (EntityKind.Object, EntityKind.Object) || (one.Kind == EntityKind.Being) != (other.Kind == EntityKind.Being))
            && one.Standing == Ruled && other.Standing == Ruled
            && one.Source == other.Source && one.Note == other.Note)
        || (one.Kind == EntityKind.Title && bearers.Contains((one.Entity, other.Entity)))
        || (other.Kind == EntityKind.Title && bearers.Contains((other.Entity, one.Entity)));

    /// <summary>
    /// Each entity's verses of every kind read, as the originals' settled names place them at their verse's first frame
    /// address, and its namesakes: the entities the originals name with a lemma it is also named with.
    /// </summary>
    private static ConsensusQuestion Question(ProductStore store, Records records)
    {
        var entities = new Dictionary<int, HashSet<int>>();
        var kinds = new Dictionary<int, EntityKind>();
        var lemmas = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        var naming = new Dictionary<(string Lemma, int Entity), int>();
        var originals = new List<ProductText>();
        foreach (var slug in Originals.Where(slug => store.Exists(TextWord.ProductOf(slug)) && store.Exists(NameProducts.Settled(slug))))
        {
            var text = ProductText.Read(store, slug);
            originals.Add(text);
            var ids = PairLinkFiles.Ids(text);
            var byWord = new Dictionary<int, List<Settling>>();
            using (var reader = store.Open(NameProducts.Settled(slug), SettledName.Schema(slug)))
            {
                foreach (var name in reader.Rows())
                {
                    if (!ids.TryGetValue(name.Own.Own, out var word) || !records.Ids.TryGetValue(name.Record, out var entity)
                        || records.Kinds[entity] is not { } kind)
                    {
                        continue;
                    }

                    if (!byWord.TryGetValue(word, out var settling))
                    {
                        byWord[word] = settling = [];
                    }

                    settling.Add(new Settling(entity, kind, ClaimStanding.Of(EnumSpelling.ToLinkMethod(name.Method)), name.Confidence ?? 1.0,
                        name.Source, name.Note ?? string.Empty, name.Source != Source));
                }
            }

            foreach (var (word, names) in byWord)
            {
                if (Settle(names, records.Bearers) is not { } entity || records.Kinds[entity] is not { } kind || !Kinds.Contains(kind))
                {
                    continue;
                }

                if (!entities.TryGetValue(entity, out var addresses))
                {
                    entities[entity] = addresses = [];
                    kinds[entity] = kind;
                }

                var (book, chapter, verse) = text.Primary(text.Words[word].Key.Verse);
                addresses.Add(Address(book, chapter, verse));
                var lemma = Lemma(text.Words[word]);
                if (!lemmas.TryGetValue(lemma, out var named))
                {
                    lemmas[lemma] = named = [];
                }

                named.Add(entity);
                naming[(lemma, entity)] = naming.GetValueOrDefault((lemma, entity)) + 1;
            }
        }

        var shared = lemmas.Where(entry => entry.Value.Count > 1).Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        var printed = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var lemma in originals.SelectMany(text => text.Words).Select(Lemma).Where(shared.Contains))
        {
            printed[lemma] = printed.GetValueOrDefault(lemma) + 1;
        }

        return new ConsensusQuestion(
            entities.ToDictionary(entry => entry.Key, entry => (IReadOnlySet<int>)entry.Value),
            NameConsensus.Namesakes(naming, printed),
            NameConsensus.OwnNames(naming),
            kinds);
    }

    private static string Lemma(TextWord word) => word.Lemma ?? word.Normalised ?? word.Surface;

    private static int Address(int book, int chapter, int verse) => book * AddressBook + chapter * AddressChapter + verse;

    /// <summary>
    /// One text read: its verses at every frame address they stand at, what the consensus finds in them, the names the
    /// projection carries over the earlier method files, and for a measured text what its stated and Strong files carry.
    /// </summary>
    private static Reading Read(
        ProductStore store,
        NamesProjection projection,
        string slug,
        ConsensusQuestion question,
        Records records,
        bool measured,
        Action<string> log)
    {
        var clock = Stopwatch.StartNew();
        var text = ProductText.Read(store, slug);
        var ids = PairLinkFiles.Ids(text);
        var verses = new List<ConsensusVerse>(text.Verses.Count);
        var verseOf = new Dictionary<int, int>(text.Words.Count);
        foreach (var verse in text.Verses)
        {
            var words = text.InVerse(verse).ToList();
            if (words.Count == 0)
            {
                continue;
            }

            foreach (var word in words)
            {
                verseOf[word] = verses.Count;
            }

            verses.Add(new ConsensusVerse(
                [.. text.Addresses(verse).Select(address => Address(address.Book, address.Chapter, address.Verse)).Distinct()],
                [.. words.Select(word => ((long)word, text.Words[word].Surface))]));
        }

        var read = clock.Elapsed;
        var annotated = new Dictionary<long, HashSet<int>>();
        foreach (var name in projection.Project(slug, over: NamesFirstSource.Before, pairs: NamesFirstSource.Placed.Contains).Names.Where(name => name.Source != Source))
        {
            if (ids.TryGetValue(name.Own.Own, out var word) && records.Ids.TryGetValue(name.Record, out var entity))
            {
                if (!annotated.TryGetValue(word, out var named))
                {
                    annotated[word] = named = [];
                }

                named.Add(entity);
            }
        }

        var carried = clock.Elapsed;
        var gold = new HashSet<(long, int)>();
        var goldForms = new Dictionary<(int, int), HashSet<string>>();
        var goldWords = new HashSet<long>();
        var goldNames = new Dictionary<EntityKind, int>();
        if (measured)
        {
            foreach (var name in projection.Project(slug, over: StatedMethods).Names.Where(name => name.Source != Source))
            {
                if (!ids.TryGetValue(name.Own.Own, out var word) || !records.Ids.TryGetValue(name.Record, out var entity)
                    || !question.Entities.ContainsKey(entity) || !gold.Add((word, entity)))
                {
                    continue;
                }

                goldWords.Add(word);
                var placed = (verseOf[word], entity);
                if (!goldForms.TryGetValue(placed, out var forms))
                {
                    goldForms[placed] = forms = [];
                }

                var surface = text.Words[word].Surface;
                forms.Add(NameConsensus.Fold(surface));
                var kind = question.KindOf(entity);
                if (!ConsensusRules.For(kind).Capitalised || (surface.Length > 0 && !char.IsLower(surface[0])))
                {
                    goldNames[kind] = goldNames.GetValueOrDefault(kind) + 1;
                }
            }
        }

        var scored = clock.Elapsed;
        var (findings, claims) = NameConsensus.Read(verses, question);
        log($"{slug}: words {read.TotalSeconds:0.0} s, carried names {(carried - read).TotalSeconds:0.0} s, " +
            $"stated names {(scored - carried).TotalSeconds:0.0} s, consensus {(clock.Elapsed - scored).TotalSeconds:0.0} s");
        return new Reading(
            slug, text.Language, verses, findings, claims, annotated, gold, goldForms, goldWords, goldNames,
            question.Entities.Keys.ToDictionary(entity => entity, question.KindOf), [.. text.Words.Select(word => word.Key)]);
    }
}
