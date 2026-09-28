using System.Diagnostics;
using System.Globalization;
using System.Text;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// <see cref="NameConsensus"/> over the corpus: every text read for the names of the people, places
/// and peoples the originals name, measured against the texts whose annotations came across a link
/// the text's own edition states or numbers, and what it would add to the rest. It writes nothing to
/// the database.
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
/// <strong>The bar is chosen without the text it is measured on.</strong> Each measured text is held
/// out in turn, the bar is the one that finds the most names in the others at the precision asked
/// for, and the held-out text is scored at that bar. The bar reported for the texts with no such
/// annotations is the one chosen on all of them.
/// </para>
///
/// <para>
/// A word is judged only where the measured text says who that verse names: right where its
/// annotation names the same entity, and where the verse names the entity with the same form on
/// another word or on the word beside it (see <see cref="Verdict"/>); wrong where it names another or
/// the verse names the entity by another word; unjudged where the text says nothing of the entity
/// there. Recall is over the annotated words that begin with a capital, or any in a script without
/// capitals, since an annotation carried across a link sometimes lands on an <em>of</em>.
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

    private const int AddressBook = 1_000_000;
    private const int AddressChapter = 1_000;

    private const int ExamplesShown = 12;

    /// <param name="texts">The texts to read, or every text.</param>
    /// <param name="precision">The precision the bar must reach on the measured texts.</param>
    /// <param name="output">A folder to write each text's findings and words into, or none.</param>
    /// <param name="bar">The bar to settle at, or null to choose it on the measured texts.</param>
    public async Task<string> Run(
        IReadOnlyCollection<string>? texts,
        double precision,
        string? output,
        ConsensusBar? bar,
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
                .AppendLine("| text | language | entities named | of them new to the text | words | agree | new | disagree | verses reached before | after |")
                .AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            var examples = new StringBuilder();
            var done = new HashSet<string>(StringComparer.Ordinal);
            if (output is not null)
            {
                Directory.CreateDirectory(output);
            }

            async Task Add(Reading reading, ConsensusBar settled)
            {
                Addition(reading, settled, slugs, table, examples);
                if (output is not null)
                {
                    await Write(output, reading, settled, slugs, cancellationToken);
                }

                done.Add(reading.Slug);
            }

            if (bar is null)
            {
                var measured = new List<Reading>();
                foreach (var text in every.Where(text => Measured.Contains(text.Slug)))
                {
                    measured.Add(await Read(connection, text.Id, text.Slug, text.Language, question, cancellationToken));
                    logger.LogInformation("Read {Text}", text.Slug);
                }

                bar = Report(report, measured, precision);
                foreach (var reading in measured.Where(reading => chosen.Any(text => text.Slug == reading.Slug)))
                {
                    await Add(reading, bar);
                }
            }

            foreach (var text in chosen.Where(text => !done.Contains(text.Slug)))
            {
                await Add(await Read(connection, text.Id, text.Slug, text.Language, question, cancellationToken), bar);
                logger.LogInformation("Read {Text}", text.Slug);
            }

            report.AppendLine()
                .AppendLine($"At the bar of at least {bar.LeastVerses} verses, score {bar.LeastScore:0.00}, margin {bar.LeastMargin:0.00}:")
                .AppendLine()
                .Append(table)
                .AppendLine()
                .AppendLine("The entities with the most words named where the text had none for them, or another:")
                .Append(examples);
            if (output is not null)
            {
                report.AppendLine().AppendLine($"Findings and words written to {output}.");
            }

            report.AppendLine().AppendLine($"Read {done.Count} texts in {started.Elapsed:hh\\:mm\\:ss}.");
            return report.ToString();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>One text read: what the consensus found, and what the text's annotations already say.</summary>
    /// <param name="Annotated">Every annotated word of the text and the entities it is annotated to, by any method.</param>
    /// <param name="Gold">The words a stated link annotates, with their entity.</param>
    /// <param name="GoldForms">Each verse and entity a stated link names, and the folded forms it names it with there.</param>
    /// <param name="GoldNames">How many of <paramref name="Gold"/> begin with a capital or are in a script without one.</param>
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
        int GoldNames);

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

    private static Tally Score(Reading reading, IReadOnlyList<ConsensusWord> words)
    {
        int right = 0, repeated = 0, beside = 0, wrong = 0, seconded = 0, found = 0;
        foreach (var word in words)
        {
            switch (Judge(reading, word))
            {
                case Verdict.Right:
                    right++;
                    var printed = reading.Verses[word.Verse].Words;
                    var surface = printed[Position(printed, word.Word)].Surface;
                    if (surface.Length > 0 && !char.IsLower(surface[0]))
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

        return new Tally(words.Count, right, repeated, beside, wrong, seconded, found, reading.GoldNames);
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

    /// <summary>
    /// The held-out measurement, one line per measured text and one per language, and the bar chosen
    /// on all of them, which is returned.
    /// </summary>
    private static ConsensusBar Report(StringBuilder report, IReadOnlyList<Reading> measured, double precision)
    {
        var tallies = measured.ToDictionary(
            reading => reading.Slug,
            reading => Bars().ToDictionary(bar => bar, bar => Score(reading, NameConsensus.Settle(reading.Findings, reading.Claims, bar))));
        var few = measured.ToDictionary(
            reading => reading.Slug,
            reading =>
            {
                var small = reading.Findings.Where(finding => finding.Verses < FewVerses).Select(finding => finding.Entity).ToHashSet();
                return Bars().ToDictionary(bar => bar, bar => Score(
                    reading,
                    NameConsensus.Settle(reading.Findings, reading.Claims, bar).Where(word => small.Contains(word.Entity)).ToList()));
            });

        Tally Pooled(IEnumerable<string> on, ConsensusBar bar) =>
            on.Aggregate(default(Tally), (sum, slug) => sum + tallies[slug][bar]);

        ConsensusBar Choose(IReadOnlyList<string> on) =>
            Bars()
                .Select(bar => (bar, tally: Pooled(on, bar), small: on.Aggregate(default(Tally), (sum, slug) => sum + few[slug][bar])))
                .Where(entry => entry.tally.Precision >= precision && entry.small.Precision >= precision)
                .OrderByDescending(entry => entry.tally.Right)
                .ThenBy(entry => entry.bar.LeastVerses)
                .Select(entry => entry.bar)
                .DefaultIfEmpty(new ConsensusBar(VerseBars[^1], ScoreBars[^1], MarginBars[^1]))
                .First();

        report.AppendLine($"Held out one text at a time; the bar chosen on the others for precision {precision:P0}, overall and on the entities with fewer than {FewVerses} verses.")
            .AppendLine()
            .AppendLine(Header)
            .AppendLine(Rule);

        var byLanguage = new Dictionary<string, Tally>();
        var pooled = default(Tally);
        foreach (var reading in measured)
        {
            var bar = Choose(measured.Where(other => other != reading).Select(other => other.Slug).ToList());
            var tally = tallies[reading.Slug][bar];
            pooled += tally;
            byLanguage[reading.Language] = byLanguage.GetValueOrDefault(reading.Language) + tally;
            report.AppendLine(Row(reading.Slug, reading.Language, Spelled(bar), tally));
        }

        report.AppendLine(Row("all held out", string.Empty, string.Empty, pooled))
            .AppendLine()
            .AppendLine(Header)
            .AppendLine(Rule);
        foreach (var (language, tally) in byLanguage.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            report.AppendLine(Row(language, language, "held out", tally));
        }

        var all = measured.Select(reading => reading.Slug).ToList();
        var chosen = Choose(all);
        report.AppendLine()
            .AppendLine($"Bar chosen on all measured texts: at least {chosen.LeastVerses} verses, score {chosen.LeastScore:0.0}, margin {chosen.LeastMargin:0.00}.")
            .AppendLine()
            .AppendLine("Pooled over the measured texts, one setting moved at a time from the chosen bar:")
            .AppendLine()
            .AppendLine(Header)
            .AppendLine(Rule);
        foreach (var bar in ScoreBars.Select(score => chosen with { LeastScore = score })
                     .Concat(VerseBars.Select(verses => chosen with { LeastVerses = verses }))
                     .Concat(MarginBars.Select(margin => chosen with { LeastMargin = margin }))
                     .Distinct())
        {
            report.AppendLine(Row("pooled", string.Empty, Spelled(bar), Pooled(all, bar)));
        }

        report.AppendLine()
            .AppendLine("At the chosen bar, by how many verses the entity has in the text (all measured texts):")
            .AppendLine()
            .AppendLine(Header)
            .AppendLine(Rule);
        for (var band = 0; band < SizeBands.Length; band++)
        {
            var least = SizeBands[band];
            var most = band + 1 < SizeBands.Length ? SizeBands[band + 1] - 1 : int.MaxValue;
            var tally = measured.Aggregate(default(Tally), (sum, reading) =>
            {
                var sized = reading.Findings
                    .Where(finding => finding.Verses >= least && finding.Verses <= most)
                    .Select(finding => finding.Entity)
                    .ToHashSet();
                var words = NameConsensus.Settle(reading.Findings, reading.Claims, chosen)
                    .Where(word => sized.Contains(word.Entity))
                    .ToList();
                return sum + Score(reading, words) with { Names = 0 };
            });
            report.AppendLine(Row(most == int.MaxValue ? $"{least}+ verses" : $"{least}–{most} verses", string.Empty, Spelled(chosen), tally));
        }

        return chosen;
    }

    /// <summary>
    /// What the bar adds to a text: words named that no annotation names, words it agrees with and
    /// words it names otherwise, and the entities the text had no word for at all.
    /// </summary>
    private static void Addition(
        Reading reading,
        ConsensusBar bar,
        IReadOnlyDictionary<int, string> slugs,
        StringBuilder table,
        StringBuilder examples)
    {
        var words = NameConsensus.Settle(reading.Findings, reading.Claims, bar);
        var accepted = reading.Findings.Where(bar.Accepts).Select(finding => finding.Entity).ToHashSet();
        var annotatedEntities = reading.Annotated.Values.SelectMany(entities => entities).ToHashSet();
        int agree = 0, added = 0, disagree = 0;
        foreach (var word in words)
        {
            if (!reading.Annotated.TryGetValue(word.Word, out var named))
            {
                added++;
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
        table.AppendLine($"| {reading.Slug} | {reading.Language} | {accepted.Count:N0} | {accepted.Count(entity => !annotatedEntities.Contains(entity)):N0} | " +
            $"{words.Count:N0} | {agree:N0} | {added:N0} | {disagree:N0} | {before:N0} | {after:N0} |");

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

    private static async Task Write(
        string output,
        Reading reading,
        ConsensusBar bar,
        IReadOnlyDictionary<int, string> slugs,
        CancellationToken cancellationToken)
    {
        var findings = new StringBuilder("entity\tverses\taccepted\tcore\tforms\tcoverage\tspecificity\tscore\tmargin\n");
        foreach (var finding in reading.Findings.Where(finding => finding.Name is not null)
                     .OrderByDescending(finding => finding.Verses))
        {
            var name = finding.Name!;
            findings.Append(CultureInfo.InvariantCulture,
                $"{slugs[finding.Entity]}\t{finding.Verses}\t{bar.Accepts(finding)}\t{name.Core}\t{string.Join(' ', name.Forms)}\t" +
                $"{name.Coverage:0.000}\t{name.Specificity:0.000}\t{name.Score:0.000}\t{finding.Margin:0.000}\n");
        }

        await File.WriteAllTextAsync(Path.Combine(output, $"{reading.Slug}-findings.tsv"), findings.ToString(), cancellationToken);

        var words = new StringBuilder("address\tposition\tword\tentity\tannotated\tjudged\n");
        foreach (var word in NameConsensus.Settle(reading.Findings, reading.Claims, bar))
        {
            var verse = reading.Verses[word.Verse];
            var position = Position(verse.Words, word.Word);
            var address = verse.Addresses.Min();
            var annotated = reading.Annotated.TryGetValue(word.Word, out var named)
                ? string.Join(' ', named.Select(entity => slugs[entity]))
                : string.Empty;
            words.Append(CultureInfo.InvariantCulture,
                $"{address / AddressBook}:{address / AddressChapter % AddressChapter}:{address % AddressChapter}\t{position + 1}\t" +
                $"{verse.Words[position].Surface}\t{slugs[word.Entity]}\t{annotated}\t" +
                $"{(Measured.Contains(reading.Slug) ? Judge(reading, word) : Verdict.Unjudged)}\n");
        }

        await File.WriteAllTextAsync(Path.Combine(output, $"{reading.Slug}-words.tsv"), words.ToString(), cancellationToken);

        if (!Measured.Contains(reading.Slug))
        {
            return;
        }

        var settled = NameConsensus.Settle(reading.Findings, reading.Claims, bar).Select(word => (word.Word, word.Entity)).ToHashSet();
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
                    if (!reading.Gold.Contains((word, entity)) || settled.Contains((word, entity)))
                    {
                        continue;
                    }

                    var why = !findingOf.TryGetValue(entity, out var finding) || finding.Name is null ? "no name found"
                        : !bar.Accepts(finding) ? "below the bar"
                        : !finding.Name.Forms.Contains(NameConsensus.Fold(surface)) ? "another form"
                        : !claimed.Contains((word, entity)) ? "outside its verses"
                        : "claimed by two";
                    var address = verse.Addresses.DefaultIfEmpty().Min();
                    missed.Append(CultureInfo.InvariantCulture,
                        $"{address / AddressBook}:{address / AddressChapter % AddressChapter}:{address % AddressChapter}\t{position + 1}\t" +
                        $"{surface}\t{slugs[entity]}\t{why}\n");
                }
            }
        }

        await File.WriteAllTextAsync(Path.Combine(output, $"{reading.Slug}-missed.tsv"), missed.ToString(), cancellationToken);
    }

    /// <summary>
    /// Each person's, place's and people's verses, as the originals' settled annotations place them,
    /// and its namesakes: the entities the originals name with a lemma it is also named with.
    /// </summary>
    private static async Task<ConsensusQuestion> Question(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var kinds = new[] { EntityKind.Person, EntityKind.Place, EntityKind.People }.Select(EnumSpelling.Of).ToArray();
        var sql =
            $"""
             WITH {Annotating.Settled}
             SELECT s.entity_id, r.canonical_book, r.canonical_chapter, r.canonical_verse, coalesce(w.lemma, w.normalised_text, w.text)
             FROM settled s
             JOIN entity e ON e.id = s.entity_id AND e.kind = ANY(@kinds)
             JOIN word w ON w.id = s.word_id
             JOIN text t ON t.id = w.text_id AND t.slug = ANY(@originals)
             JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
             """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("kinds", kinds);
        command.Parameters.AddWithValue("originals", Originals);
        command.CommandTimeout = 0;

        var entities = new Dictionary<int, HashSet<int>>();
        var lemmas = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var entity = reader.GetInt32(0);
                if (!entities.TryGetValue(entity, out var addresses))
                {
                    entities[entity] = addresses = [];
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
                .ToDictionary(group => group.Key, group => (IReadOnlySet<string>)group.Select(pair => pair.lemma).ToHashSet()));
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
            command.CommandTimeout = 0;
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
        var goldNames = 0;
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT a.word_id, a.entity_id, w.verse_id, w.text, a.note LIKE ANY(@stated)
                         FROM word_entity a JOIN word w ON w.id = a.word_id
                         WHERE w.text_id = @text
                         """, connection))
        {
            command.Parameters.AddWithValue("text", textId);
            command.Parameters.AddWithValue("stated", StatedLinks);
            command.CommandTimeout = 0;
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

                forms.Add(NameConsensus.Fold(reader.GetString(3)));
                var surface = reader.GetString(3);
                if (surface.Length > 0 && !char.IsLower(surface[0]))
                {
                    goldNames++;
                }
            }
        }

        var (findings, claims) = NameConsensus.Read(verses, question);
        return new Reading(slug, language, verses, findings, claims, annotated, gold, goldForms, goldWords, goldNames);
    }
}
