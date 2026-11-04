using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Essenthos.Core.Conversion;
using Essenthos.Core.Indexes;
using Essenthos.Core.Loading;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;

namespace Essenthos.Core.Names;

/// <summary>
/// The names of every text as files (DOC-0224 §7): a reference text's names are its settled ones; every other text's are
/// <c>project(settled(base(T)) over final(T↔base), rules) ∪ overrides.add − overrides.suppress</c>, with an override's
/// <c>differs</c> putting the translation's record first and keeping the base's beside it. Reads no database.
/// </summary>
internal sealed partial class NamesProjection
{
    /// <summary>Raised with any change to what the projection writes or how; a product made by another version is made again.</summary>
    public const string Version = "1";

    public const string VersionNote = "names-version";

    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<string, List<SettledName>>>> _settled = new(StringComparer.Ordinal);

    public NamesProjection(ProductStore store)
    {
        Store = store;
        Context = new IndexContext(store);
        using (var reader = store.Open(NameProducts.Rules, NameRuleRow.Schema))
        {
            Rules = new NameRules(reader.Rows());
        }

        Records = ReadRecords(store);

        if (!store.Exists(NameProducts.Bases))
        {
            SeedBases();
        }

        using (var reader = store.Open(NameProducts.Bases, NameBase.Schema))
        {
            Bases = [.. reader.Rows()];
        }
    }

    public ProductStore Store { get; }

    public IndexContext Context { get; }

    public NameRules Rules { get; }

    /// <summary>Each record's kind and the sex it was given (null where none was stated), from <c>records/records</c>.</summary>
    public IReadOnlyDictionary<string, (string Kind, string? Sex)> Records { get; }

    public const string RecordsProduct = "records/records";

    private static Dictionary<string, (string Kind, string? Sex)> ReadRecords(ProductStore store)
    {
        var records = new Dictionary<string, (string, string?)>(StringComparer.Ordinal);
        if (!store.Exists(RecordsProduct))
        {
            return records;
        }

        using var reader = store.Open(RecordsProduct);
        var columns = reader.Columns.Select(c => c.Name).ToList();
        int At(string name) => columns.IndexOf(name) is var at && at >= 0 ? at : throw new InvalidDataException($"{RecordsProduct} has no column {name}.");
        var (slug, kind, sex, sexSource) = (At("slug"), At("kind"), At("sex"), At("sex_source"));
        foreach (var row in reader.Rows())
        {
            records[row[slug]!] = (row[kind]!, row[sexSource] is null ? row[sex] : null);
        }

        return records;
    }

    public IReadOnlyList<NameBase> Bases { get; }

    /// <summary>The texts the projection writes: every text with words, its declaration and, unless it is a reference text, a base.</summary>
    public IEnumerable<string> Texts =>
        Context.Texts.Keys.Where(t => NameProducts.IsReference(t) ? Store.Exists(NameProducts.Settled(t)) : Bases.Any(b => b.Text == t))
            .Order(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, List<SettledName>> SettledOf(string reference) =>
        _settled.GetOrAdd(reference, r => new Lazy<IReadOnlyDictionary<string, List<SettledName>>>(() =>
        {
            var names = new Dictionary<string, List<SettledName>>(StringComparer.Ordinal);
            if (!Store.Exists(NameProducts.Settled(r)))
            {
                return names;
            }

            using var reader = Store.Open(NameProducts.Settled(r), SettledName.Schema(r));
            foreach (var row in reader.Rows())
            {
                if (!names.TryGetValue(row.Own.Own, out var list))
                {
                    names[row.Own.Own] = list = [];
                }

                list.Add(row);
            }

            return names;
        })).Value;

    private readonly ConcurrentDictionary<string, Lazy<HashSet<(string Verse, string Record)>>> _namedVerses = new(StringComparer.Ordinal);

    /// <summary>The (verse, record) pairs a reference text's settled names hold.</summary>
    public HashSet<(string Verse, string Record)> NamedVerses(string reference) =>
        _namedVerses.GetOrAdd(reference, r => new Lazy<HashSet<(string, string)>>(() =>
            SettledOf(r).Values.SelectMany(list => list).Select(n => (n.Own.Verse.ToString(), n.Record)).ToHashSet())).Value;

    /// <summary>A text's words as the rules read them.</summary>
    public IReadOnlyDictionary<string, Word> WordsOf(string text) => ReadWords(text, Context.Texts[text].Language);

    /// <summary>The reference pairs of a text: each pair with a reference text, the reference, and whether the text is the pair's first.</summary>
    public IEnumerable<(string Pair, string Reference, bool First)> ReferencePairs(string text) =>
        Context.PairsOf(text).Where(p => NameProducts.IsReference(p.Other));

    private void SeedBases()
    {
        var linked = new ConcurrentDictionary<(string Text, string Reference), IReadOnlyDictionary<int, long>>();
        var work = Context.Texts.Keys.Where(t => !NameProducts.IsReference(t))
            .SelectMany(t => ReferencePairs(t).Select(p => (Text: t, p.Pair, p.Reference, p.First)))
            .ToList();
        Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = 16 }, item =>
        {
            var books = new Dictionary<int, long>();
            using var reader = Store.Open(MappingProducts.Of(item.Pair, MappingProducts.Final), FinalRow.Schema);
            foreach (var row in reader.Rows().Where(r => r.Settled))
            {
                var mine = item.First ? row.From : row.To;
                var theirs = item.First ? row.To : row.From;
                if (mine.Length == 0 || theirs.Length == 0)
                {
                    continue;
                }

                var book = BookOf(mine);
                books[book] = books.GetValueOrDefault(book) + 1;
            }

            linked[(item.Text, item.Reference)] = books;
        });

        var languages = Context.Texts.ToDictionary(t => t.Key, t => t.Value.Language, StringComparer.Ordinal);
        using var writer = Store.Create(NameProducts.Bases, ProductKind.Hand, NameBase.Schema,
            ProductHeader.DescribeProducer("forge names-project"), [],
            header => header.Notes.Add(new("seeded", "from the relations between texts and the mapping's pairs; kept by hand from here on")));
        foreach (var row in NameBases.Seed(languages, linked, TextRelationLoader.All))
        {
            writer.Write(row);
        }

        writer.Complete();
    }

    /// <summary>The canonical book of an own key or of the first of several.</summary>
    public static int BookOf(string keys)
    {
        var dot = keys.IndexOf('.');
        return int.Parse(keys.AsSpan(0, dot));
    }

    /// <summary>A word of a text as the rules read it.</summary>
    /// <param name="Sex">The sex a pronoun states, where it states one.</param>
    /// <param name="Verse">The verse's place in the text's print order.</param>
    internal sealed record Word(WordKey Key, string Surface, bool Cased, bool WrittenAsName, bool Pronoun, bool NeverAName, bool Possessive, string? Sex, int Verse);

    /// <summary>What a name was not carried for, by (own key, record): the rule that refused it.</summary>
    internal static class Refusals
    {
        public const string NeverAName = "never-a-name";
        public const string NotHead = "not-the-head";
        public const string Pronoun = "pronoun";
        public const string Stray = "stray";
        public const string Floor = "below-the-floor";
        public const string Suppressed = "suppressed";
    }

    /// <param name="Names">The text's names, sorted by key and record.</param>
    /// <param name="Linked">For each word a link of the final joins to a word of its base: the records the base's words of those links name.</param>
    /// <param name="Refused">Names the base carried to a word that a rule took back, with the rule.</param>
    /// <param name="LinkedTo">When asked for, the base's words each word is linked to (<c>BHSA 1.1:1.3</c>).</param>
    internal sealed record Projection(
        string Text,
        List<ProjectedName> Names,
        Dictionary<string, HashSet<string>> Linked,
        Dictionary<(string Own, string Record), string> Refused,
        Dictionary<string, List<string>> LinkedTo,
        IReadOnlyList<ProductInput> Inputs);

    private sealed class Candidate(string record, string role, SettledName settled, string through, double link)
    {
        public string Record { get; } = record;
        public string Role { get; } = role;
        public SettledName Settled { get; set; } = settled;
        public string Through { get; set; } = through;
        public double Link { get; set; } = link;
    }

    [GeneratedRegex("['’]s?$", RegexOptions.IgnoreCase)]
    private static partial Regex PossessiveEnding();

    public IReadOnlyList<ProductInput> InputsOf(string text)
    {
        var inputs = new List<ProductInput> { Store.ProductInput(NameProducts.Rules) };
        if (NameProducts.IsReference(text))
        {
            inputs.Add(Store.ProductInput(NameProducts.Settled(text)));
            return inputs;
        }

        inputs.Add(Store.ProductInput(NameProducts.Bases));
        inputs.Add(Store.ProductInput(TextWord.ProductOf(text)));
        foreach (var reference in NameBases.Of(text, Bases).Values.SelectMany(b => b).Distinct().Order(StringComparer.Ordinal))
        {
            if (Store.Exists(NameProducts.Settled(reference)))
            {
                inputs.Add(Store.ProductInput(NameProducts.Settled(reference)));
            }

            foreach (var pair in ReferencePairs(text).Where(p => p.Reference == reference))
            {
                inputs.Add(Store.ProductInput(MappingProducts.Of(pair.Pair, MappingProducts.Final)));
            }
        }

        if (Store.Exists(NameProducts.Overrides(text)))
        {
            inputs.Add(Store.ProductInput(NameProducts.Overrides(text)));
        }

        return inputs;
    }

    /// <summary>The names of one text, in memory.</summary>
    public Projection Project(string text, bool diagnose = false)
    {
        var inputs = InputsOf(text);
        var linked = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var refused = new Dictionary<(string, string), string>();
        if (NameProducts.IsReference(text))
        {
            var own = SettledOf(text).Values.SelectMany(list => list)
                .Select(s => new ProjectedName(s.Own, s.Record, s.Role, NameOrigins.Settled, null, s.Method, s.Confidence, s.Source))
                .ToList();
            own.Sort(Order);
            return new Projection(text, own, linked, refused, [], inputs);
        }

        var language = Context.Texts[text].Language;
        var words = ReadWords(text, language);
        var bookBases = NameBases.Of(text, Bases);
        var candidates = new Dictionary<(string Own, string Record), Candidate>();
        var linkedTo = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var priorities = bookBases.Count == 0 ? 0 : bookBases.Values.Max(b => b.Count);
        for (var priority = 0; priority < priorities; priority++)
        {
            // A word a surer base links takes its names from that base alone; the next base speaks only for the words before it left unlinked.
            var linkedBefore = new HashSet<string>(linked.Keys, StringComparer.Ordinal);
            var used = bookBases.Values.Where(b => b.Count > priority).Select(b => b[priority]).ToHashSet(StringComparer.Ordinal);
            foreach (var (pair, reference, first) in ReferencePairs(text).Where(p => used.Contains(p.Reference)))
            {
                var settled = SettledOf(reference);
                using var reader = Store.Open(MappingProducts.Of(pair, MappingProducts.Final), FinalRow.Schema);
                foreach (var row in reader.Rows())
                {
                    if (!row.Settled)
                    {
                        continue;
                    }

                    var mine = first ? row.From : row.To;
                    var theirs = first ? row.To : row.From;
                    if (mine.Length == 0 || theirs.Length == 0
                        || !bookBases.TryGetValue(BookOf(mine), out var bases) || bases.Count <= priority || bases[priority] != reference)
                    {
                        continue;
                    }

                    var mineKeys = MappingText.Split(mine).Where(k => !linkedBefore.Contains(k)).ToList();
                    if (mineKeys.Count == 0)
                    {
                        continue;
                    }

                    List<(string Their, SettledName Name)>? named = null;
                    foreach (var their in MappingText.Split(theirs))
                    {
                        if (settled.TryGetValue(their, out var theirNames))
                        {
                            named ??= [];
                            named.AddRange(theirNames.Select(n => (their, n)));
                        }
                    }

                    foreach (var key in mineKeys)
                    {
                        if (!linked.TryGetValue(key, out var records))
                        {
                            linked[key] = records = new HashSet<string>(StringComparer.Ordinal);
                        }

                        if (named is not null)
                        {
                            records.UnionWith(named.Select(n => n.Name.Record));
                        }

                        if (diagnose)
                        {
                            if (!linkedTo.TryGetValue(key, out var to))
                            {
                                linkedTo[key] = to = [];
                            }

                            to.Add($"{reference} {theirs}");
                        }
                    }

                    if (named is null)
                    {
                        continue;
                    }

                    var heads = Heads(mineKeys, words);
                    foreach (var key in mineKeys.Where(k => !heads.Contains(k)))
                    {
                        var why = words.TryGetValue(key, out var word) && word.NeverAName ? Refusals.NeverAName : Refusals.NotHead;
                        foreach (var (_, name) in named)
                        {
                            refused.TryAdd((key, name.Record), why);
                        }
                    }

                    var link = row.Confidence ?? 1.0;
                    foreach (var head in heads)
                    {
                        foreach (var (their, name) in named)
                        {
                            var through = $"{reference} {their}";
                            if (!candidates.TryGetValue((head, name.Record), out var candidate))
                            {
                                candidates[(head, name.Record)] = new Candidate(name.Record, name.Role, name, through, link);
                            }
                            else if (Worth(link, name) > Worth(candidate.Link, candidate.Settled))
                            {
                                (candidate.Settled, candidate.Through, candidate.Link) = (name, through, link);
                            }
                        }
                    }
                }
            }
        }

        foreach (var (key, candidate) in candidates.Where(c => Worth(c.Value.Link, c.Value.Settled) < Rules.FloorValue).ToList())
        {
            candidates.Remove(key);
            refused[key] = Refusals.Floor;
        }

        foreach (var key in Unclear(candidates, words, language))
        {
            candidates.Remove(key);
            refused[key] = Refusals.Pronoun;
        }

        foreach (var key in Strays(candidates, words))
        {
            candidates.Remove(key);
            refused[key] = Refusals.Stray;
        }

        var names = candidates.Select(c => new ProjectedName(
                WordKey.Parse(c.Key.Own, text), c.Key.Record, c.Value.Role, NameOrigins.Projected, c.Value.Through, c.Value.Settled.Method,
                c.Value.Settled.Confidence is null && c.Value.Link >= 1.0 ? null : (c.Value.Settled.Confidence ?? 1.0) * c.Value.Link,
                c.Value.Settled.Source))
            .ToList();
        Override(text, names, refused);
        names.Sort(Order);
        return new Projection(text, names, linked, refused, linkedTo, inputs);
    }

    private static double Worth(double link, SettledName name) => link * (name.Confidence ?? 1.0);

    private static int Order(ProjectedName a, ProjectedName b) =>
        a.Own.CompareTo(b.Own) is var order && order != 0 ? order : string.CompareOrdinal(a.Record, b.Record) is var record && record != 0
            ? record : string.CompareOrdinal(a.Origin, b.Origin);

    /// <summary>The words of a link's side of the text that take the names of its other side (<see cref="NameRules.Head"/>).</summary>
    private HashSet<string> Heads(List<string> keys, IReadOnlyDictionary<string, Word> words)
    {
        var eligible = keys.Where(k => !words.TryGetValue(k, out var w) || !w.NeverAName).ToList();
        if (eligible.Count == 0 || Rules.HeadRule == NameRules.HeadAll)
        {
            return new HashSet<string>(eligible, StringComparer.Ordinal);
        }

        var head = eligible[^1];
        if (eligible.Count >= 2 && words.TryGetValue(eligible[^2], out var before) && before.Possessive
            && words.TryGetValue(head, out var last) && last.Key.Position == before.Key.Position + 1)
        {
            head = eligible[^2];
        }

        var heads = new HashSet<string>([head], StringComparer.Ordinal);
        if (Rules.HeadRule == NameRules.HeadLastAndCapitals)
        {
            heads.UnionWith(eligible.Where(k => words.TryGetValue(k, out var w) && w.WrittenAsName && !w.Pronoun));
        }

        return heads;
    }

    /// <summary>The records a capitalised pronoun in the middle of a sentence may show.</summary>
    private static readonly HashSet<string> Capitalised = new(["yhvh", "jesus", "holy-spirit"], StringComparer.Ordinal);

    /// <summary>
    /// The names carried onto pronouns that may not stay (the owner's ruling of 2026-10-07, <c>PronounReferents</c>): a
    /// pronoun keeps a person only where it is certainly clear who it is. Either the text capitalises it where a sentence does
    /// not begin and the person is YHVH, Jesus or the Holy Spirit; or the pronoun states a sex the person has (or no sex is
    /// recorded for him), the text names him by a word that is not a pronoun in the same verse or the one before, nobody else
    /// it could be (a person or title of that sex or of none recorded) is named there, and the word beside it does not name
    /// him itself. A place or a people is never clear, and neither is a Greek pronoun.
    /// </summary>
    private IEnumerable<(string Own, string Record)> Unclear(Dictionary<(string Own, string Record), Candidate> candidates, IReadOnlyDictionary<string, Word> words, string language)
    {
        var pronouns = candidates.Keys.Where(k => words.TryGetValue(k.Own, out var w) && w.Pronoun).ToList();
        if (pronouns.Count == 0)
        {
            yield break;
        }

        if (Rules.Referents == NameRules.ReferentNever || language == "grc")
        {
            foreach (var key in pronouns)
            {
                yield return key;
            }

            yield break;
        }

        var named = new Dictionary<int, List<(string Record, Word Word)>>();
        foreach (var key in candidates.Keys)
        {
            if (words.TryGetValue(key.Own, out var word) && !word.Pronoun)
            {
                if (!named.TryGetValue(word.Verse, out var list))
                {
                    named[word.Verse] = list = [];
                }

                list.Add((key.Record, word));
            }
        }

        foreach (var key in pronouns)
        {
            var word = words[key.Own];
            var (kind, sex) = Records.TryGetValue(key.Record, out var record) ? record : ("person", null);
            if (word.WrittenAsName && Capitalised.Contains(key.Record))
            {
                continue;
            }

            var near = named.GetValueOrDefault(word.Verse, []).Concat(named.GetValueOrDefault(word.Verse - 1, [])).ToList();
            bool PersonLike(string other) =>
                !Records.TryGetValue(other, out var r) || (r.Kind is "person" or "title" && (r.Sex is null || r.Sex == word.Sex));
            var clear = kind == "person"
                        && word.Sex is not null && (sex is null || sex == word.Sex)
                        && near.Any(n => n.Record == key.Record)
                        && !near.Any(n => n.Record != key.Record && PersonLike(n.Record))
                        && !near.Any(n => n.Record == key.Record && n.Word.Verse == word.Verse && Math.Abs(n.Word.Key.Position - word.Key.Position) == 1);
            if (!clear)
            {
                yield return key;
            }
        }
    }

    /// <summary>
    /// The stray rule: a person or a place carried over a link below <see cref="NameRules.FaintValue"/> onto a word not written
    /// as a name, where another word of the verse takes the same record over a link at least as sure as the faint line, and,
    /// in a script without capitals, that word is not the next one (a name may be written in two words there).
    /// </summary>
    private IEnumerable<(string Own, string Record)> Strays(Dictionary<(string Own, string Record), Candidate> candidates, IReadOnlyDictionary<string, Word> words)
    {
        var faint = Rules.FaintValue;
        var byVerse = candidates
            .Where(c => c.Value.Role == NameRoles.Name && words.ContainsKey(c.Key.Own))
            .GroupBy(c => (Verse: words[c.Key.Own].Key.Verse, c.Key.Record));
        foreach (var group in byVerse)
        {
            var sure = group.Where(c => c.Value.Link >= faint).Select(c => words[c.Key.Own]).ToList();
            if (sure.Count == 0)
            {
                continue;
            }

            foreach (var (key, candidate) in group.Where(c => c.Value.Link < faint))
            {
                var word = words[key.Own];
                if (word.WrittenAsName)
                {
                    continue;
                }

                if (sure.Any(other => word.Cased || Math.Abs(other.Key.Position - word.Key.Position) > 1))
                {
                    yield return key;
                }
            }
        }
    }

    private Dictionary<string, Word> ReadWords(string text, string language)
    {
        var words = new Dictionary<string, Word>(StringComparer.Ordinal);
        using var reader = Store.Open(TextWord.ProductOf(text), TextWord.Schema(text));
        TextWord? previous = null;
        var verse = 0;
        foreach (var word in reader.Rows())
        {
            if (previous is null || previous.Key.Verse != word.Key.Verse)
            {
                verse++;
            }

            var bare = NameRules.Bare(word.Surface);
            var cased = word.Surface.ToLowerInvariant() != word.Surface.ToUpperInvariant();
            var followsOpenSentence = previous is not null && previous.Key.Verse == word.Key.Verse
                                      && !previous.Trailer.TrimEnd().EndsWith('.') && !previous.Trailer.TrimEnd().EndsWith('!')
                                      && !previous.Trailer.TrimEnd().EndsWith('?');
            var capital = word.Surface.TrimStart('\'', '’', '‘', '"', '“', '«', '(', '[');
            var apostrophe = capital.IndexOfAny(['\'', '’']);
            if (apostrophe is > 0 and <= 2)
            {
                capital = capital[(apostrophe + 1)..];
            }

            var writtenAsName = followsOpenSentence && capital.Length > 0 && char.IsUpper(capital[0]);
            words[word.Key.Own] = new Word(
                word.Key,
                word.Surface,
                cased,
                writtenAsName,
                Rules.IsPronoun(language, bare),
                Rules.IsNeverAName(language, bare, language == "hbo" ? NameRules.PartOfSpeech(word.Morphology) : null, word.Strong, word.Normalised),
                PossessiveEnding().IsMatch(word.Surface),
                Rules.SexOf(language, bare),
                verse);
            previous = word;
        }

        return words;
    }

    /// <summary>Applies the text's overrides to its projected names.</summary>
    private void Override(string text, List<ProjectedName> names, Dictionary<(string, string), string> refused)
    {
        if (!Store.Exists(NameProducts.Overrides(text)))
        {
            return;
        }

        using var reader = Store.Open(NameProducts.Overrides(text), NameOverride.Schema(text));
        foreach (var row in reader.Rows())
        {
            switch (row.Action)
            {
                case OverrideActions.Add:
                    names.RemoveAll(n => n.Own == row.Own && n.Record == row.Record);
                    names.Add(new ProjectedName(row.Own, row.Record, row.Role, NameOrigins.Added, null, "manual", null, row.Source));
                    break;
                case OverrideActions.Suppress:
                    if (names.RemoveAll(n => n.Own == row.Own && n.Record == row.Record) > 0)
                    {
                        refused[(row.Own.Own, row.Record)] = Refusals.Suppressed;
                    }

                    break;
                case OverrideActions.Differs:
                    var index = names.FindIndex(n => n.Own == row.Own && n.Record == row.BaseRecord);
                    if (index >= 0)
                    {
                        names[index] = names[index] with { Origin = NameOrigins.BaseReading };
                    }

                    names.RemoveAll(n => n.Own == row.Own && n.Record == row.Record);
                    names.Add(new ProjectedName(row.Own, row.Record, row.Role, NameOrigins.Differs, null, "manual", null, row.Source));
                    break;
                default:
                    throw new InvalidDataException($"names/overrides/{text}: {row.Action} is not add, suppress or differs.");
            }
        }
    }

    /// <summary>One text's projection written, or found current.</summary>
    public sealed record Outcome(string Text, bool Made, long Rows, TimeSpan Elapsed);

    public Outcome Write(string text, bool force)
    {
        var clock = Stopwatch.StartNew();
        var product = NameProducts.Projected(text);
        var inputs = InputsOf(text);
        if (!force && !NeedsBuild(product, inputs))
        {
            return new Outcome(text, false, 0, clock.Elapsed);
        }

        var projection = Project(text);
        using var writer = Store.Create(product, ProductKind.Machine, ProjectedName.Schema(text), ProductHeader.DescribeProducer("forge names-project"),
            projection.Inputs, header => header.Notes.Add(new(VersionNote, Version)));
        foreach (var name in projection.Names)
        {
            writer.Write(name);
        }

        writer.Complete();
        return new Outcome(text, true, writer.Rows, clock.Elapsed);
    }

    private bool NeedsBuild(string product, IReadOnlyList<ProductInput> inputs)
    {
        if (Store.NeedsBuild(product, ProductKind.Machine, inputs))
        {
            return true;
        }

        using var reader = Store.Open(product);
        return reader.Header?.Notes.LastOrDefault(note => note.Key == VersionNote).Value != Version;
    }

    /// <summary>Every text's projection, the largest first, <paramref name="parallel"/> at a time.</summary>
    public IReadOnlyList<Outcome> WriteAll(IEnumerable<string> texts, bool force, int parallel)
    {
        var outcomes = new ConcurrentBag<Outcome>();
        Parallel.ForEach(texts, new ParallelOptions { MaxDegreeOfParallelism = parallel }, text => outcomes.Add(Write(text, force)));
        Store.Save();
        return [.. outcomes.OrderBy(o => o.Text, StringComparer.Ordinal)];
    }
}
