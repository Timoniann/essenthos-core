using System.Diagnostics;
using System.Globalization;
using System.Text;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Links;

/// <summary>Which pairs <see cref="NamesFirstPass"/> places: two signals always, one only where it was measured to hold.</summary>
/// <param name="Alone">The single signals that place a pair by themselves.</param>
/// <param name="Even">Whether a pair whose entity the two sides name a different number of times is left out.</param>
/// <param name="Repeated">
/// Whether a pair whose entity the verse names more than once is placed, its mentions paired in order.
/// Not in a language that puts the verb last: the Hindi reorders the clauses of a verse, and its
/// repeated names were paired with the right mention 83% of the time against its interlinear.
/// </param>
internal sealed record NamePolicy(NameSignal Alone, bool Even, bool Repeated = true)
{
    /// <summary>The languages here that put the verb last, whose clauses come in another order than the original's.</summary>
    public static readonly IReadOnlySet<string> VerbLast = new HashSet<string>(StringComparer.Ordinal)
    {
        "hin", "ben", "asm", "guj", "pan", "mar", "kan", "mal", "tam", "tel", "urd", "kor", "jpn",
    };

    /// <summary>The policy for a text in a language, as measured.</summary>
    public NamePolicy For(string? language) => this with { Repeated = Repeated && !VerbLast.Contains(language ?? string.Empty) };

    /// <summary>
    /// As measured on the keys (TSK-1086): two signals; whom the verses say a word names alone, or its
    /// letters alone where they are clear; never an entity the two sides name a different number of times.
    /// </summary>
    public static readonly NamePolicy Default = new(NameSignal.Entity | NameSignal.Letters, Even: true);

    /// <summary>
    /// Whether the pair is placed. Letters alone count only where they are clear (<see cref="NamePlacing.Clear"/>).
    /// </summary>
    public bool Places(NamePlacing placing) =>
        (placing.Count >= 2
         || (placing.Count == 1 && (placing.Signals & Alone) != 0 && (placing.Signals != NameSignal.Letters || placing.Clear)))
        && !(Even && placing.Uneven)
        && (Repeated || !placing.Repeated);
}

/// <summary>
/// The names of a translation placed on the words of the original that name the same person, place
/// or people (<see cref="NamePlacement"/>), for every verse at once, so that each name is read in the
/// light of every other mention of it in the text.
///
/// <para>
/// It needs no link of the pair: the original's annotations say whom its words name, the text's
/// verses say whom its words name (<see cref="Encyclopedia.NameConsensus"/>), and the letters and the
/// text's own spellings do the rest. So it reaches a text nothing links yet, and on a text something
/// does link it is measured against the links its edition states, which it never reads for anything
/// else. Without <c>--apply</c> it reports and writes nothing.
/// </para>
/// </summary>
internal sealed class NamesFirstPass(AppDbContext db, AlignmentPipeline aligner, ILogger<NamesFirstPass> logger)
{
    internal const string Source = "the names of the verse, placed by whom both texts name, by their letters and by the text's own spelling of them";

    /// <summary>
    /// What a placed name is worth. Measured against the Berean's and the Reina-Valera's hand
    /// alignments on passages its rules were not chosen on; see the report each run prints.
    /// </summary>
    internal const double Confidence = 0.95;

    private static readonly EntityKind[] Kinds = [EntityKind.Person, EntityKind.Place, EntityKind.People];

    /// <summary>The EVIDENTIA benchmark's passages, by canonical book and chapter: what each set is called.</summary>
    internal static readonly IReadOnlyDictionary<(int Book, int Chapter), string> Sets = BenchmarkSets();

    private static Dictionary<(int, int), string> BenchmarkSets()
    {
        var sets = new Dictionary<(int, int), string>();
        void Add(string set, int book, int first, int last)
        {
            for (var chapter = first; chapter <= last; chapter++)
            {
                sets[(book, chapter)] = set;
            }
        }

        Add("ind", 1, 1, 10);
        Add("ind", 8, 1, 4);
        Add("ind", 32, 1, 4);
        Add("ind", 41, 1, 4);
        Add("val", 9, 17, 17);
        Add("val", 19, 1, 23);
        Add("val", 43, 1, 3);
        Add("val2", 2, 21, 23);
        Add("val2", 23, 40, 42);
        Add("val2", 45, 5, 8);
        foreach (var (book, chapter) in new[] { (4, 33), (13, 7), (15, 9), (23, 34), (27, 10), (27, 12), (40, 25), (58, 10), (66, 17) })
        {
            Add("held", book, chapter, chapter);
        }

        foreach (var (book, chapter) in new[] { (5, 23), (11, 2), (18, 19), (19, 25), (20, 7), (24, 21), (40, 24), (47, 5), (58, 13) })
        {
            Add("held2", book, chapter, chapter);
        }

        return sets;
    }

    /// <param name="names">
    /// A <c>name-consensus --out</c> folder whose <c>&lt;text&gt;-words.tsv</c> says whom the text's
    /// words name, read off its verses alone; or null for the text's own annotations of that source.
    /// A text whose annotations came across a link should be read from the folder, or it is scored
    /// on what the links it is measured against already gave it.
    /// </param>
    /// <param name="held">Whether the held passages are scored. They are measured once, never tuned on; held2 is never read.</param>
    /// <param name="output">A folder for every placed pair and every disagreement with the key, or none.</param>
    public async Task<string> Run(
        string fromSlug,
        string toSlug,
        IReadOnlySet<int>? books,
        string? names,
        NamePolicy policy,
        bool held,
        string? output,
        bool apply,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var from = await db.Texts.AsNoTracking().SingleAsync(text => text.Slug == fromSlug, cancellationToken);
        var to = await db.Texts.AsNoTracking().SingleAsync(text => text.Slug == toSlug, cancellationToken);
        policy = policy.For(from.Language);
        var source = await aligner.Named(fromSlug, toSlug, books, cancellationToken);
        var target = await aligner.Named(toSlug, fromSlug, books, cancellationToken);
        var families = new NameFamilies(await db.Entities.AsNoTracking()
            .Where(entity => entity.AnotherNameForEntityId != null)
            .Select(entity => new ValueTuple<int, int>(entity.Id, entity.AnotherNameForEntityId!.Value))
            .ToListAsync(cancellationToken));
        var slugs = await db.Entities.AsNoTracking().ToDictionaryAsync(entity => entity.Id, entity => entity.Slug, cancellationToken);

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var originals = await Originals(connection, to.Id, families, cancellationToken);
            var named = names is null
                ? await Annotated(connection, from.Id, families, cancellationToken)
                : await Read(connection, from.Id, Path.Combine(names, $"{fromSlug}-words.tsv"), slugs, families, cancellationToken);
            var links = await Links(connection, from.Id, to.Id, cancellationToken);
            var capitalised = await Capitalised(connection, from.Id, cancellationToken);

            var verses = new List<NameVerse>();
            foreach (var address in source.Keys.Intersect(target.Keys).Order())
            {
                var original = target[address];
                var targets = new List<NameTargetWord>();
                for (var index = 0; index < original.Count; index++)
                {
                    if (originals.TryGetValue(original[index].Id, out var annotated))
                    {
                        if (index > 0)
                        {
                            links.Previous[original[index].Id] = original[index - 1].Id;
                        }

                        links.Families[original[index].Id] = annotated.Families;

                        targets.Add(new NameTargetWord(original[index].Id, index, original[index].Letters, annotated.Families, annotated.Kind,
                            Spelling(original[index].Text, to.Language), annotated.Proper));
                    }
                }

                if (targets.Count == 0)
                {
                    continue;
                }

                for (var at = 1; at < source[address].Count; at++)
                {
                    links.PreviousSource[source[address][at].Id] = source[address][at - 1].Id;
                }

                verses.Add(new NameVerse(
                    address,
                    [
                        .. source[address].Select(word => new NameSourceWord(
                            word.Id,
                            word.Text,
                            NameLetters.Of(word.Text, from.Language),
                            NameLetters.Form(word.Text, from.Language),
                            named.GetValueOrDefault(word.Id) ?? Empty,
                            word.Name is not null || capitalised.Contains(word.Id),
                            word.Name is not null)),
                    ],
                    targets,
                    original.Count));
            }

            var placings = NamePlacement.Place(verses);
            var report = new StringBuilder()
                .AppendLine(CultureInfo.InvariantCulture,
                    $"{fromSlug} to {toSlug}: {verses.Count:N0} verses where the original names someone, " +
                    $"{verses.Sum(verse => verse.Targets.Count(target => target.Proper)):N0} words of it naming them; " +
                    $"{named.Count:N0} words of {fromSlug} its verses say name someone ({(names is null ? "its annotations" : "read from " + names)}).");
            Score(report, verses, placings, policy, links, held);
            Before(report, verses, placings, policy, links);

            if (output is not null)
            {
                Directory.CreateDirectory(output);
                await Write(connection, Path.Combine(output, $"{fromSlug}-{toSlug}-names.tsv"), placings, policy, links,
                    originals, slugs, source, held, from.Language, cancellationToken);
                await Missed(Path.Combine(output, $"{fromSlug}-{toSlug}-missed.tsv"), verses, placings, policy, links, slugs, held,
                    cancellationToken);
            }

            if (apply)
            {
                report.AppendLine(await Apply(connection, from.Id, to.Id, placings, policy, links, cancellationToken));
            }

            logger.LogInformation("Placed the names of {From} on {To} in {Elapsed}", fromSlug, toSlug, started.Elapsed);
            return report.ToString();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static readonly IReadOnlySet<int> Empty = new HashSet<int>();

    /// <summary>The original's word as its spelling of a name is keyed: Hebrew by its consonants, Greek by its stem.</summary>
    internal static string Spelling(string written, string? language) =>
        Encyclopedia.NameConsensus.Fold(language is "grc" ? GreekStemmer.Stem(written) : written);

    /// <summary>
    /// Whether the key puts the word on the word just before the name and not on the name: the
    /// Berean links <em>Ammonites</em> to the בְּנֵי of בְּנֵי עַמּוֹן and the name it is written from to
    /// nothing. Counted apart, as the key's convention and not as our mistake, once a sample is read.
    /// </summary>
    private static bool Head(PairLinks links, NamePlacing placing) =>
        links.Previous.TryGetValue(placing.Target, out var previous)
        && links.Key.Contains((placing.Source, previous))
        && !links.KeyTargets.Contains(placing.Target);

    /// <summary>
    /// How the key reads a placed pair. <c>right</c>: it joins them. <c>head</c>: see <see cref="Head"/>.
    /// <c>offset</c>: it leaves our word unjoined and gives the name to the word just before it — the
    /// <em>de</em> of <em>de Siddim</em>, the <em>á</em> of <em>á Jechônías</em> — which is the key's
    /// slip, not ours, as every one read was. <c>same entity</c>: it gives our word another mention of
    /// the same entity in the verse, so whom the word names is right and which mention is not.
    /// </summary>
    private static string Verdict(PairLinks links, NamePlacing placing)
    {
        if (!links.KeySources.Contains(placing.Source) && !links.KeyTargets.Contains(placing.Target))
        {
            return "uncovered";
        }

        if (links.Key.Contains((placing.Source, placing.Target)))
        {
            return "right";
        }

        if (Head(links, placing))
        {
            return "head";
        }

        if (!links.KeySources.Contains(placing.Source)
            && links.PreviousSource.TryGetValue(placing.Source, out var before)
            && links.Key.Contains((before, placing.Target)))
        {
            return "offset";
        }

        var families = links.Families.GetValueOrDefault(placing.Target);
        return families is not null && links.KeyBySource.TryGetValue(placing.Source, out var others)
               && others.Any(other => links.Families.TryGetValue(other, out var named) && named.Overlaps(families))
            ? "same entity"
            : "wrong";
    }

    private static string SetOf((int Book, int Chapter, int Verse) address) =>
        Sets.TryGetValue((address.Book, address.Chapter), out var set) ? set : "rest";

    /// <summary>Whether a set is read: held only when asked, held2 never.</summary>
    private static bool Readable(string set, bool held) => set switch
    {
        "held2" => false,
        "held" => held,
        _ => true,
    };

    private static string Combination(NameSignal signals) => signals == NameSignal.None
        ? "none"
        : string.Join('+', new[] { NameSignal.Entity, NameSignal.Letters, NameSignal.Spelling }
            .Where(signal => (signals & signal) != 0)
            .Select(signal => signal switch
            {
                NameSignal.Entity => "entity",
                NameSignal.Letters => "letters",
                _ => "spelling",
            }));

    private sealed class Tally
    {
        public int Placed;
        public int Covered;
        public int Right;
        public int Head;
        public int Offset;
        public int Same;

        public string Line() => Covered == 0
            ? $"{Placed:N0} placed, none the key covers"
            : $"{Placed:N0} placed, right {Right:N0}/{Covered:N0} = {(double)Right / Covered:P2} of those the key covers" +
              (Head + Offset > 0
                  ? $"; judged (the phrase's head {Head:N0} and the key's offset {Offset:N0} counted right) {Right + Head + Offset:N0} = {(double)(Right + Head + Offset) / Covered:P2}"
                  : string.Empty) +
              (Same > 0
                  ? $"; whom it names right (another mention of the same entity {Same:N0}) {Right + Head + Offset + Same:N0} = {(double)(Right + Head + Offset + Same) / Covered:P2}"
                  : string.Empty);
    }

    private static void Score(
        StringBuilder report,
        IReadOnlyList<NameVerse> verses,
        IReadOnlyList<NamePlacing> placings,
        NamePolicy policy,
        PairLinks links,
        bool held)
    {
        if (links.Key.Count == 0)
        {
            report.AppendLine("No link of the pair is stated by a source or by Strong numbers, so nothing is scored.");
            return;
        }

        var byKind = new SortedDictionary<string, Tally>(StringComparer.Ordinal);
        var bySignals = new SortedDictionary<string, Tally>(StringComparer.Ordinal);
        var recall = new SortedDictionary<string, (int Found, int Key)>(StringComparer.Ordinal);

        foreach (var placing in placings)
        {
            var set = SetOf(placing.Address);
            if (!Readable(set, held))
            {
                continue;
            }

            var group = set is "ind" or "val" or "val2" ? "dev" : set;
            var covered = links.KeySources.Contains(placing.Source) || links.KeyTargets.Contains(placing.Target);
            var right = links.Key.Contains((placing.Source, placing.Target));
            var verdict = Verdict(links, placing);
            var kind = EnumSpelling.Of(placing.Kind);
            void Count(SortedDictionary<string, Tally> tallies, string label)
            {
                if (!tallies.TryGetValue(label, out var tally))
                {
                    tallies[label] = tally = new Tally();
                }

                tally.Placed++;
                tally.Covered += covered ? 1 : 0;
                tally.Right += covered && right ? 1 : 0;
                tally.Head += verdict == "head" ? 1 : 0;
                tally.Offset += verdict == "offset" ? 1 : 0;
                tally.Same += verdict == "same entity" ? 1 : 0;
            }

            var shape = (placing.Uneven ? ", uneven" : placing.Repeated ? ", repeated" : string.Empty) + (placing.Beside ? ", beside" : string.Empty)
                        + (placing.Count == 1 && placing.Clear ? ", clear" : string.Empty);
            Count(bySignals, $"{(group == "held" ? "held" : "dev+rest")}, {Combination(placing.Signals)}{shape}");
            if (policy.Places(placing))
            {
                Count(byKind, $"{group}, {kind}");
                Count(byKind, $"{group}, all");
            }
        }

        var placedByTarget = placings.Where(policy.Places).ToLookup(placing => placing.Target, placing => placing.Source);
        foreach (var verse in verses)
        {
            var set = SetOf(verse.Address);
            if (!Readable(set, held))
            {
                continue;
            }

            var group = set is "ind" or "val" or "val2" ? "dev" : set;
            foreach (var target in verse.Targets.Where(target => target.Proper && links.KeyTargets.Contains(target.Id)))
            {
                var found = placedByTarget[target.Id].Any(word => links.Key.Contains((word, target.Id))) ? 1 : 0;
                foreach (var label in new[] { $"{group}, {EnumSpelling.Of(target.Kind)}", $"{group}, all" })
                {
                    var (before, key) = recall.GetValueOrDefault(label);
                    recall[label] = (before + found, key + 1);
                }
            }
        }

        report.AppendLine("Placed, by the kind of the original's name (the key: the pair's links stated by a source or by Strong numbers):");
        foreach (var (label, tally) in byKind)
        {
            var (found, key) = recall.GetValueOrDefault(label);
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  names {label}: {tally.Line()}; the original's names the key links, placed on a word it links them to: {found:N0}/{key:N0} = {(double)found / Math.Max(1, key):P2}");
        }

        report.AppendLine("Every pair weighed, by the signals naming it (whether placed or not):");
        foreach (var (label, tally) in bySignals)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  signals {label}: {tally.Line()}");
        }
    }

    /// <summary>
    /// What the pair reached before and what the placed names add: names of the original reached by
    /// any link of the pair, and verses no link of the pair reaches that a placed name now does.
    /// </summary>
    private static void Before(
        StringBuilder report,
        IReadOnlyList<NameVerse> verses,
        IReadOnlyList<NamePlacing> placings,
        NamePolicy policy,
        PairLinks links)
    {
        var placed = placings.Where(policy.Places).ToList();
        var names = verses.Sum(verse => verse.Targets.Count(target => target.Proper));
        var before = verses.Sum(verse => verse.Targets.Count(target => target.Proper && links.Targets.Contains(target.Id)));
        var reached = placed.Select(placing => placing.Target).Concat(
            verses.SelectMany(verse => verse.Targets.Where(target => target.Proper)).Select(target => target.Id).Where(links.Targets.Contains)).ToHashSet();
        var linkedVerses = verses.Where(verse => verse.Source.Any(word => links.Sources.Contains(word.Id))).Select(verse => verse.Address).ToHashSet();
        var firstAnchor = placed.Select(placing => placing.Address).Where(address => !linkedVerses.Contains(address)).ToHashSet();
        var byKind = string.Join(", ", Kinds.Select(kind =>
            $"{EnumSpelling.Of(kind)} {placed.Count(placing => placing.Kind == kind):N0}"));
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Reach: the original's naming words reached by a link of the pair {before:N0}/{names:N0} = {(double)before / Math.Max(1, names):P2} before, " +
            $"{reached.Count:N0}/{names:N0} = {(double)reached.Count / Math.Max(1, names):P2} with the names placed; " +
            $"{placed.Count:N0} pairs placed ({byKind}); {firstAnchor.Count:N0} verses gain their first link of the pair.");
    }

    private async Task Write(
        NpgsqlConnection connection,
        string path,
        IReadOnlyList<NamePlacing> placings,
        NamePolicy policy,
        PairLinks links,
        IReadOnlyDictionary<long, (IReadOnlySet<int> Families, EntityKind Kind, IReadOnlyList<int> Entities, bool Proper)> originals,
        IReadOnlyDictionary<int, string> slugs,
        Dictionary<(int, int, int), List<AlignmentPipeline.Word>> source,
        bool held,
        string? language,
        CancellationToken cancellationToken)
    {
        var shown = placings.Where(placing => Readable(SetOf(placing.Address), held)).ToList();
        var words = new Dictionary<long, (string Surface, string? Phono, string Where)>();
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT w.id, w.text, w.morphology ->> 'phono', v.chapter_number || ':' || v.number || ':' || w.position
                         FROM word w JOIN verse v ON v.id = w.verse_id WHERE w.id = ANY(@ids)
                         """, connection))
        {
            command.Parameters.AddWithValue("ids", shown.SelectMany(placing => new[] { placing.Source, placing.Target })
                .Concat(shown.SelectMany(placing => links.KeyBySource.GetValueOrDefault(placing.Source) ?? []))
                .Concat(shown.SelectMany(placing => links.KeyByTarget.GetValueOrDefault(placing.Target) ?? []))
                .Distinct().ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                words[reader.GetInt64(0)] = (reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3));
            }
        }

        var lines = new StringBuilder("set\taddress\tplaced\tsignals\tlikeness\tuneven\tbeside\tkind\tentity\ttranslation\tletters\toriginal\tsounds\tkey\tkey says\tkey gives the name to\tsame entity named\ttranslation at\toriginal at\n");
        var bySource = links.KeyBySource;
        foreach (var placing in shown)
        {
            var (surface, _, sourceAt) = words.GetValueOrDefault(placing.Source);
            var (original, phono, targetAt) = words.GetValueOrDefault(placing.Target);
            var covered = links.KeySources.Contains(placing.Source) || links.KeyTargets.Contains(placing.Target);
            var verdict = Verdict(links, placing);
            var keySays = verdict is not ("right" or "uncovered") && bySource.TryGetValue(placing.Source, out var others)
                ? string.Join(" | ", others.Select(other => words.TryGetValue(other, out var word) ? $"{word.Where} {word.Surface}" : $"#{other}"))
                : string.Empty;
            var keyGives = verdict is not ("right" or "uncovered") && links.KeyByTarget.TryGetValue(placing.Target, out var givers)
                ? string.Join(" | ", givers.Select(other => words.TryGetValue(other, out var word) ? $"{word.Where} {word.Surface}" : $"#{other}"))
                : string.Empty;
            var entities = originals.TryGetValue(placing.Target, out var annotated)
                ? string.Join(' ', annotated.Entities.Select(entity => slugs.GetValueOrDefault(entity, $"#{entity}")))
                : string.Empty;
            lines.Append(CultureInfo.InvariantCulture,
                $"{SetOf(placing.Address)}\t{placing.Address.Book}:{placing.Address.Chapter}:{placing.Address.Verse}\t{policy.Places(placing)}\t" +
                $"{Combination(placing.Signals)}\t{placing.Likeness:0.00}\t{placing.Uneven}\t{placing.Beside}\t{EnumSpelling.Of(placing.Kind)}\t" +
                $"{entities}\t{surface}\t{string.Join('/', NameLetters.Of(surface ?? string.Empty, language))}\t{original}\t{phono}\t{verdict}\t{keySays}\t{keyGives}\t{placing.Repeated}\t{sourceAt} {surface}\t{targetAt} {original}\n");
        }

        await File.WriteAllTextAsync(path, lines.ToString(), cancellationToken);
    }

    /// <summary>
    /// The original's names the key links that nothing placed on a word the key links them to, with
    /// what each signal says of the key's words, which is where the next rule is looked for.
    /// </summary>
    private static async Task Missed(
        string path,
        IReadOnlyList<NameVerse> verses,
        IReadOnlyList<NamePlacing> placings,
        NamePolicy policy,
        PairLinks links,
        IReadOnlyDictionary<int, string> slugs,
        bool held,
        CancellationToken cancellationToken)
    {
        var placed = placings.Where(policy.Places).ToLookup(placing => placing.Target);
        var weighed = placings.ToLookup(placing => placing.Target);
        var lines = new StringBuilder("set\taddress\toriginal\tentity\tkind\tkey's words\tmarked\tnamed\tletters\tweighed\tplaced elsewhere\n");
        foreach (var verse in verses.Where(verse => Readable(SetOf(verse.Address), held)))
        {
            foreach (var target in verse.Targets.Where(target => target.Proper && links.KeyTargets.Contains(target.Id)))
            {
                if (placed[target.Id].Any(placing => links.Key.Contains((placing.Source, target.Id))))
                {
                    continue;
                }

                var keyWords = verse.Source.Where(word => links.Key.Contains((word.Id, target.Id))).ToList();
                var weighing = weighed[target.Id].FirstOrDefault();
                var elsewhere = placed[target.Id].Select(placing => verse.Source.FirstOrDefault(word => word.Id == placing.Source)?.Surface);
                lines.Append(CultureInfo.InvariantCulture,
                    $"{SetOf(verse.Address)}\t{verse.Address.Book}:{verse.Address.Chapter}:{verse.Address.Verse}\t{target.Spelling}\t" +
                    $"{string.Join(' ', target.Families.Select(family => slugs.GetValueOrDefault(family, $"#{family}")))}\t{EnumSpelling.Of(target.Kind)}\t" +
                    $"{string.Join(' ', keyWords.Select(word => word.Surface))}\t{string.Join(' ', keyWords.Select(word => word.Marked))}\t" +
                    $"{string.Join(' ', keyWords.Select(word => word.Families.Overlaps(target.Families)))}\t" +
                    $"{string.Join(' ', keyWords.Select(word => NameLetters.Likeness(word.Letters, target.Letters, word.Surface).ToString("0.00", CultureInfo.InvariantCulture)))}\t" +
                    $"{(weighing is null ? string.Empty : Combination(weighing.Signals) + (weighing.Uneven ? " uneven" : string.Empty))}\t{string.Join(' ', elsewhere)}\n");
            }
        }

        await File.WriteAllTextAsync(path, lines.ToString(), cancellationToken);
    }

    /// <summary>
    /// The placed pairs written as links, as a rule's: only where neither word is linked in the pair
    /// yet, so nothing the pair already states or a review settled is touched.
    /// </summary>
    private async Task<string> Apply(
        NpgsqlConnection connection,
        int fromTextId,
        int toTextId,
        IReadOnlyList<NamePlacing> placings,
        NamePolicy policy,
        PairLinks links,
        CancellationToken cancellationToken)
    {
        var rejected = await RejectedRenderings.Locate(connection, cancellationToken);
        var drafts = placings
            .Where(policy.Places)
            .Where(placing => !links.Sources.Contains(placing.Source) && !links.Targets.Contains(placing.Target))
            .Where(placing => !rejected.Contains((placing.Source, placing.Target)) && !rejected.Contains((placing.Target, placing.Source)))
            .GroupBy(placing => placing.Source)
            .Select(group => new NewLink(
                fromTextId, toTextId, LinkRelation.Renders, LinkMethod.RuleBased, Confidence, Source,
                Combination(group.First().Signals), [group.Key], [.. group.Select(placing => placing.Target).Distinct()]))
            .ToList();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var written = await LinkWriter.Write(connection, (NpgsqlTransaction)transaction.GetDbTransaction(), drafts, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return $"Wrote {written.Written:N0} links of names; {written.Joined:N0} named words a link already joined.";
    }

    /// <summary>
    /// The original's names of a person, a place or a people, with whom they name: the words BHSA marks
    /// as proper nouns or gentilics, and the Greek words whose lemma is capitalised. A common noun
    /// annotated with whom it means — the sons of בְּנֵי יִשְׂרָאֵל, the king who is Ahab, the man of
    /// God who is Elisha — is translated, not written by its letters, and is left to the other steps.
    /// </summary>
    private static async Task<Dictionary<long, (IReadOnlySet<int> Families, EntityKind Kind, IReadOnlyList<int> Entities, bool Proper)>> Originals(
        NpgsqlConnection connection,
        int textId,
        NameFamilies families,
        CancellationToken cancellationToken)
    {
        var rows = new List<(long Word, int Entity, EntityKind Kind, bool Proper)>();
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT we.word_id, we.entity_id, e.kind,
                                w.morphology ->> 'pos' IN ('nmpr', 'adjv')
                                OR (w.lemma IS NOT NULL AND left(w.lemma, 1) <> lower(left(w.lemma, 1)))
                         FROM word_entity we
                         JOIN word w ON w.id = we.word_id
                         JOIN entity e ON e.id = we.entity_id
                         WHERE w.text_id = @text AND e.kind = ANY(@kinds)
                         ORDER BY we.word_id, we.entity_id
                         """, connection))
        {
            command.Parameters.AddWithValue("text", textId);
            command.Parameters.AddWithValue("kinds", Kinds.Select(EnumSpelling.Of).ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((reader.GetInt64(0), reader.GetInt32(1), EnumSpelling.ToEntityKind(reader.GetString(2)),
                    !reader.IsDBNull(3) && reader.GetBoolean(3)));
            }
        }

        return rows.GroupBy(row => row.Word).ToDictionary(
            word => word.Key,
            word => (families.Of(word.Select(row => row.Entity)), word.First().Kind, (IReadOnlyList<int>)[.. word.Select(row => row.Entity)],
                word.First().Proper));
    }

    /// <summary>The text's words its verses say name someone, as the consensus pass wrote them on the text.</summary>
    private static async Task<Dictionary<long, IReadOnlySet<int>>> Annotated(
        NpgsqlConnection connection,
        int textId,
        NameFamilies families,
        CancellationToken cancellationToken)
    {
        var rows = new List<(long Word, int Entity)>();
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT we.word_id, we.entity_id
                         FROM word_entity we
                         JOIN word w ON w.id = we.word_id
                         WHERE w.text_id = @text AND we.source = @consensus
                         """, connection))
        {
            command.Parameters.AddWithValue("text", textId);
            command.Parameters.AddWithValue("consensus", Annotations.Consensus);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((reader.GetInt64(0), reader.GetInt32(1)));
            }
        }

        return rows.GroupBy(row => row.Word).ToDictionary(word => word.Key, word => families.Of(word.Select(row => row.Entity)));
    }

    /// <summary>
    /// A <c>name-consensus --out</c> words file, whose words are addressed by their verse's first
    /// canonical address and their place among the verse's words.
    /// </summary>
    private static async Task<Dictionary<long, IReadOnlySet<int>>> Read(
        NpgsqlConnection connection,
        int textId,
        string path,
        IReadOnlyDictionary<int, string> slugs,
        NameFamilies families,
        CancellationToken cancellationToken)
    {
        var bySlug = slugs.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        var wanted = new Dictionary<(int, int, int, int), List<int>>();
        foreach (var line in (await File.ReadAllLinesAsync(path, cancellationToken)).Skip(1))
        {
            var fields = line.Split('\t');
            var address = fields.Length >= 4 ? fields[0].Split(':') : [];
            if (address.Length != 3 || !bySlug.TryGetValue(fields[3], out var entity))
            {
                continue;
            }

            var key = (int.Parse(address[0], CultureInfo.InvariantCulture), int.Parse(address[1], CultureInfo.InvariantCulture),
                int.Parse(address[2], CultureInfo.InvariantCulture), int.Parse(fields[1], CultureInfo.InvariantCulture));
            if (!wanted.TryGetValue(key, out var entities))
            {
                wanted[key] = entities = [];
            }

            entities.Add(entity);
        }

        var named = new Dictionary<long, IReadOnlySet<int>>();
        await using var command = new NpgsqlCommand(
            """
            WITH first AS (
                SELECT DISTINCT ON (r.verse_id) r.verse_id, r.canonical_book, r.canonical_chapter, r.canonical_verse
                FROM verse_reference r
                JOIN verse v ON v.id = r.verse_id
                WHERE v.text_id = @text
                ORDER BY r.verse_id, r.canonical_book, r.canonical_chapter, r.canonical_verse
            )
            SELECT w.id, f.canonical_book, f.canonical_chapter, f.canonical_verse,
                   row_number() OVER (PARTITION BY w.verse_id ORDER BY w.position)::int
            FROM word w
            JOIN first f ON f.verse_id = w.verse_id
            WHERE w.text_id = @text
            """, connection);
        command.Parameters.AddWithValue("text", textId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (wanted.TryGetValue((reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)), out var entities))
            {
                named[reader.GetInt64(0)] = families.Of(entities);
            }
        }

        return named;
    }

    /// <summary>
    /// The words a text writes with a capital inside its verse. The aligner takes a word the text also
    /// writes in lower case for no name (God and god); here a name is weighed against the original's
    /// names only, so <em>Lot</em> is still Lot where the text writes <em>lots</em> elsewhere.
    /// </summary>
    private static async Task<HashSet<long>> Capitalised(NpgsqlConnection connection, int textId, CancellationToken cancellationToken)
    {
        var capitalised = new HashSet<long>();
        await using var command = new NpgsqlCommand(
            """
            SELECT w.id, w.text
            FROM word w
            WHERE w.text_id = @text AND w.position > 1 AND w.text <> lower(w.text) AND w.text <> upper(w.text)
            """, connection);
        command.Parameters.AddWithValue("text", textId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var trimmed = reader.GetString(1).TrimStart('(', '[', '"', '\'', '\u201C', '\u2018');
            if (trimmed.Length > 0 && char.IsUpper(trimmed[0]))
            {
                capitalised.Add(reader.GetInt64(0));
            }
        }

        return capitalised;
    }

    /// <param name="Key">Pairs of words the pair's links stated by a source or by Strong numbers join.</param>
    /// <param name="Sources">Words of the translation any link of the pair names.</param>
    /// <param name="Targets">Words of the original any link of the pair names.</param>
    private sealed record PairLinks(
        HashSet<(long Source, long Target)> Key,
        HashSet<long> KeySources,
        HashSet<long> KeyTargets,
        Dictionary<long, List<long>> KeyBySource,
        HashSet<long> Sources,
        HashSet<long> Targets)
    {
        /// <summary>The key's words of the translation for each word of the original.</summary>
        public Dictionary<long, List<long>> KeyByTarget { get; } = [];

        /// <summary>For each word of the translation, the word before it in its verse.</summary>
        public Dictionary<long, long> PreviousSource { get; } = [];

        /// <summary>For each of the original's names, whom it names.</summary>
        public Dictionary<long, IReadOnlySet<int>> Families { get; } = [];

        /// <summary>For each of the original's names, the word before it in its verse.</summary>
        public Dictionary<long, long> Previous { get; } = [];
    }

    private static async Task<PairLinks> Links(NpgsqlConnection connection, int fromTextId, int toTextId, CancellationToken cancellationToken)
    {
        var links = new PairLinks([], [], [], [], [], []);
        await using var command = new NpgsqlCommand(
            """
            SELECT l.method IN ('stated-by-source', 'strong-number'), f.word_id, t.word_id
            FROM link l
            LEFT JOIN link_word f ON f.link_id = l.id AND f.side = 'from'
            LEFT JOIN link_word t ON t.link_id = l.id AND t.side = 'to'
            WHERE l.from_text_id = @from AND l.to_text_id = @to
            """, connection);
        command.Parameters.AddWithValue("from", fromTextId);
        command.Parameters.AddWithValue("to", toTextId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var stated = reader.GetBoolean(0);
            long? fromWord = reader.IsDBNull(1) ? null : reader.GetInt64(1);
            long? toWord = reader.IsDBNull(2) ? null : reader.GetInt64(2);
            if (fromWord is { } sourceWord)
            {
                links.Sources.Add(sourceWord);
            }

            if (toWord is { } targetWord)
            {
                links.Targets.Add(targetWord);
            }

            if (!stated || fromWord is null || toWord is null)
            {
                continue;
            }

            links.KeySources.Add(fromWord.Value);
            links.KeyTargets.Add(toWord.Value);
            if (links.Key.Add((fromWord.Value, toWord.Value)))
            {
                if (!links.KeyBySource.TryGetValue(fromWord.Value, out var targets))
                {
                    links.KeyBySource[fromWord.Value] = targets = [];
                }

                targets.Add(toWord.Value);
                if (!links.KeyByTarget.TryGetValue(toWord.Value, out var sources))
                {
                    links.KeyByTarget[toWord.Value] = sources = [];
                }

                sources.Add(fromWord.Value);
            }
        }

        return links;
    }
}
