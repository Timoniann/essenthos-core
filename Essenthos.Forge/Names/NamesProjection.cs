using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Essenthos.Core.Conversion;
using Essenthos.Core.Indexes;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
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
    public const string Version = "4";

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
        _foreign = new Lazy<ForeignNameRule>(() => new ForeignNameRule(store));

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
    /// <param name="Form">The form words are counted by: the normalised form, or the bare surface where the text has none.</param>
    /// <param name="Joined">The word before it ends in a hyphen: a part of a name the text writes as two words (<em>Ramoth–gilead</em>).</param>
    internal sealed record Word(WordKey Key, string Surface, bool Cased, bool WrittenAsName, bool Pronoun, bool NeverAName, bool Possessive, string? Sex, int Verse, string Form, string? Strong = null, bool Joined = false);

    /// <summary>What a name was not carried for, by (own key, record): the rule that refused it.</summary>
    internal static class Refusals
    {
        public const string NeverAName = "never-a-name";
        public const string NotHead = "not-the-head";
        public const string Pronoun = "pronoun";
        public const string Stray = "stray";
        public const string Floor = "below-the-floor";
        public const string Suppressed = "suppressed";
        public const string Foreign = "another-record's-name";
        public const string Several = "a-link-of-several-people";
        public const string Unprinted = "a-name-the-text-does-not-print";
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

    private sealed class Candidate(string record, string role, SettledName settled, string through, double link, string method)
    {
        public string Record { get; } = record;
        public string Role { get; } = role;
        public SettledName Settled { get; set; } = settled;
        public string Through { get; set; } = through;
        public double Link { get; set; } = link;
        public string LinkMethod { get; set; } = method;
    }

    [GeneratedRegex("['’]s?$", RegexOptions.IgnoreCase)]
    private static partial Regex PossessiveEnding();

    /// <summary>The record files the rules read: kinds and sexes for the pronoun referents, names, name forms and titles for the foreign names.</summary>
    private Lazy<IReadOnlyList<ProductInput>> RecordInputs => _recordInputs ??= new Lazy<IReadOnlyList<ProductInput>>(() =>
    {
        var products = new List<string> { RecordsProduct, "records/names", "records/facts/titles" };
        var i18n = Path.Combine(Store.ResourcesPath, ProductStore.EssenthosFolder, "records", "i18n");
        if (Directory.Exists(i18n))
        {
            products.AddRange(Directory.GetDirectories(i18n).Select(Path.GetFileName).Order(StringComparer.Ordinal).Select(l => $"records/i18n/{l}/name-forms"));
        }

        return [.. products.Where(Store.Exists).Select(Store.ProductInput)];
    });

    private Lazy<IReadOnlyList<ProductInput>>? _recordInputs;

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
        inputs.AddRange(RecordInputs.Value);
        foreach (var reference in NameBases.Of(text, Bases).Values.SelectMany(b => b).Distinct().Order(StringComparer.Ordinal))
        {
            if (Store.Exists(NameProducts.Settled(reference)))
            {
                inputs.Add(Store.ProductInput(NameProducts.Settled(reference)));
            }

            // Its proper nouns, for the names a text does not print (Unprinted).
            if (Store.Exists(TextWord.ProductOf(reference)))
            {
                inputs.Add(Store.ProductInput(TextWord.ProductOf(reference)));
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
    /// <param name="over">
    /// The method files of each pair the names are carried over instead of its final, every row of them a link: what the
    /// earlier methods alone would name, for a producer the final reads (<see cref="Loading.Encyclopedia.NameConsensusPass"/>).
    /// </param>
    /// <param name="pairs">The pairs names are carried over, or every reference pair of the text.</param>
    public Projection Project(string text, bool diagnose = false, IReadOnlyList<string>? over = null, Func<string, bool>? pairs = null)
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
        var ofSeveral = new HashSet<(string Own, string Record)>();
        var confirmed = new HashSet<(string Own, string Record)>();
        var priorities = bookBases.Count == 0 ? 0 : bookBases.Values.Max(b => b.Count);
        for (var priority = 0; priority < priorities; priority++)
        {
            // A word a surer base links takes its names from that base alone; the next base speaks only for the words before it left unlinked.
            var linkedBefore = new HashSet<string>(linked.Keys, StringComparer.Ordinal);
            var used = bookBases.Values.Where(b => b.Count > priority).Select(b => b[priority]).ToHashSet(StringComparer.Ordinal);
            foreach (var (pair, reference, first) in ReferencePairs(text).Where(p => used.Contains(p.Reference) && (pairs is null || pairs(p.Pair))))
            {
                var settled = SettledOf(reference);
                foreach (var row in Links(pair, over))
                {
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

                    var (heads, nameHeads) = Heads(mineKeys, words);
                    foreach (var key in mineKeys)
                    {
                        var why = words.TryGetValue(key, out var word) && word.NeverAName ? Refusals.NeverAName : Refusals.NotHead;
                        foreach (var (_, name) in named.Where(n => !(n.Name.Role == NameRoles.Name ? nameHeads : heads).Contains(key)))
                        {
                            refused.TryAdd((key, name.Record), why);
                        }
                    }

                    var link = row.Confidence ?? 1.0;
                    var several = named.Where(n => n.Name.Role == NameRoles.Name).Select(n => n.Name.Record).Distinct(StringComparer.Ordinal).Count() > 1
                                  && mineKeys.Count(k => !words.TryGetValue(k, out var w) || !w.NeverAName) > 1;
                    foreach (var head in heads.Union(nameHeads))
                    {
                        foreach (var (their, name) in named.Where(n => (n.Name.Role == NameRoles.Name ? nameHeads : heads).Contains(head)))
                        {
                            if (name.Role == NameRoles.Name)
                            {
                                (several ? ofSeveral : confirmed).Add((head, name.Record));
                            }

                            var through = $"{reference} {their}";
                            if (!candidates.TryGetValue((head, name.Record), out var candidate))
                            {
                                candidates[(head, name.Record)] = new Candidate(name.Record, name.Role, name, through, link, row.Method);
                            }
                            else if (Worth(link, name) > Worth(candidate.Link, candidate.Settled))
                            {
                                (candidate.Settled, candidate.Through, candidate.Link, candidate.LinkMethod) = (name, through, link, row.Method);
                            }
                        }
                    }
                }
            }
        }

        // A link of several words on both sides naming several people says that they are there, not which word is which, so
        // a person goes to a word only where another link joins it to him (RP2018's variant units: Martha and Mary, Aquila
        // and Priscilla).
        foreach (var key in ofSeveral.Where(k => candidates.ContainsKey(k) && !confirmed.Contains(k)).ToList())
        {
            candidates.Remove(key);
            refused[key] = Refusals.Several;
        }

        Unpaired(candidates, words);
        foreach (var (key, candidate) in candidates.Where(c => Worth(c.Value.Link, c.Value.Settled) < Rules.FloorOver(c.Value.LinkMethod)).ToList())
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

        if (Rules.ForeignNames)
        {
            foreach (var key in Foreign(candidates, words).ToList())
            {
                candidates.Remove(key);
                refused[key] = Refusals.Foreign;
            }
        }

        foreach (var key in Unprinted(candidates, words).ToList())
        {
            candidates.Remove(key);
            refused[key] = Refusals.Unprinted;
        }

        var names = candidates.Select(c => new ProjectedName(
                WordKey.Parse(c.Key.Own, text), c.Key.Record, c.Value.Role, NameOrigins.Projected, c.Value.Through, c.Value.Settled.Method,
                c.Value.Settled.Confidence is null && c.Value.Link >= 1.0 ? null : (c.Value.Settled.Confidence ?? 1.0) * c.Value.Link,
                c.Value.Settled.Source,
                c.Value.LinkMethod))
            .ToList();
        Override(text, names, refused);
        names.Sort(Order);
        return new Projection(text, names, linked, refused, linkedTo, inputs);
    }

    /// <summary>The links of a pair names are carried over: its final's settled claims, or every row of the method files named.</summary>
    private IEnumerable<(string From, string To, double? Confidence, string Method)> Links(string pair, IReadOnlyList<string>? methods)
    {
        if (methods is null)
        {
            using var final = Store.Open(MappingProducts.Of(pair, MappingProducts.Final), FinalRow.Schema);
            foreach (var row in final.Rows().Where(row => row.Settled))
            {
                yield return (row.From, row.To, row.Confidence, row.Method);
            }

            yield break;
        }

        foreach (var method in methods.Where(method => Store.Exists(MappingProducts.Of(pair, method))))
        {
            using var reader = Store.Open(MappingProducts.Of(pair, method), MappingRow.Schema);
            foreach (var row in reader.Rows())
            {
                yield return (row.From, row.To, row.Confidence, row.Method);
            }
        }
    }

    private static double Worth(double link, SettledName name) => link * (name.Confidence ?? 1.0);

    /// <summary>
    /// A repeated Strong number paired in the order both texts write it (<see cref="StrongNumberMatch.PairedInOrder"/>) is worth
    /// less than a number written once, because the occurrences could be paired the other way round. Where every word of the
    /// verse tagged with the number takes the same records, either pairing names the same people, so the name over such a
    /// link is worth what it is over an unambiguous match (Matthew 1:2: <em>Abraham begat Isaac; and Isaac begat Jacob</em>).
    /// </summary>
    private static void Unpaired(Dictionary<(string Own, string Record), Candidate> candidates, IReadOnlyDictionary<string, Word> words)
    {
        static bool Paired(double link) => Math.Abs(link - StrongNumberMatch.PairedInOrder) < 1e-9
                                           || Math.Abs(link - (StrongNumberMatch.PairedInOrder - StrongNumberMatch.ResolvedNumber)) < 1e-9;

        var paired = candidates.Where(c => c.Value.LinkMethod == MappingMethods.Strong && Paired(c.Value.Link)
                                           && words.TryGetValue(c.Key.Own, out var w) && w.Strong is not null)
            .Select(c => c.Key).ToList();
        if (paired.Count == 0)
        {
            return;
        }

        var recordsOf = candidates.Keys.GroupBy(k => k.Own, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(k => k.Record).Order(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        var verses = paired.Select(k => words[k.Own].Verse).ToHashSet();
        var tagged = words.Values.Where(w => w.Strong is not null && verses.Contains(w.Verse))
            .GroupBy(w => (w.Verse, w.Strong!))
            .ToDictionary(g => g.Key, g => g.Select(w => w.Key.Own).ToList());
        foreach (var key in paired)
        {
            var word = words[key.Own];
            var same = tagged[(word.Verse, word.Strong!)];
            var records = recordsOf[key.Own];
            if (same.Count > 1 && same.All(own => recordsOf.TryGetValue(own, out var theirs) && theirs.SequenceEqual(records, StringComparer.Ordinal)))
            {
                var candidate = candidates[key];
                candidate.Link = Math.Round(candidate.Link + StrongNumberMatch.Unambiguous - StrongNumberMatch.PairedInOrder, 2);
            }
        }
    }

    private static int Order(ProjectedName a, ProjectedName b) =>
        a.Own.CompareTo(b.Own) is var order && order != 0 ? order : string.CompareOrdinal(a.Record, b.Record) is var record && record != 0
            ? record : string.CompareOrdinal(a.Origin, b.Origin);

    /// <summary>
    /// The words of a link's side of the text that take the names of its other side (<see cref="NameRules.Head"/>), and those that
    /// take a person's or a place's name: where the rule adds the words written as names and the side has one, those alone, so
    /// the last word of <em>to Moses in marriage</em> is not Moses.
    /// </summary>
    private (HashSet<string> All, HashSet<string> Names) Heads(List<string> keys, IReadOnlyDictionary<string, Word> words)
    {
        var eligible = keys.Where(k => !words.TryGetValue(k, out var w) || !w.NeverAName).ToList();
        if (eligible.Count == 0 || Rules.HeadRule == NameRules.HeadAll)
        {
            var all = new HashSet<string>(eligible, StringComparer.Ordinal);
            return (all, all);
        }

        var head = eligible[^1];
        if (eligible.Count >= 2 && words.TryGetValue(eligible[^2], out var before) && before.Possessive
            && words.TryGetValue(head, out var last) && last.Key.Position == before.Key.Position + 1)
        {
            head = eligible[^2];
        }

        var heads = new HashSet<string>([head], StringComparer.Ordinal);
        if (Rules.HeadRule != NameRules.HeadLastAndCapitals)
        {
            return (heads, heads);
        }

        var capitals = eligible.Where(k => words.TryGetValue(k, out var w) && (w.WrittenAsName || w.Joined) && !w.Pronoun).ToHashSet(StringComparer.Ordinal);
        heads.UnionWith(capitals.Where(k => words[k].WrittenAsName));
        if (!capitals.Any(k => words[k].WrittenAsName))
        {
            return (heads, heads);
        }

        if (words.TryGetValue(head, out var final) && final.Surface.TrimStart('\'', '’', '‘', '"', '“', '«', '(', '[') is { Length: > 0 } letters && char.IsUpper(letters[0]))
        {
            capitals.Add(head);
        }

        return (heads, capitals);
    }

    /// <summary>The records a capitalised pronoun in the middle of a sentence may show.</summary>
    private static readonly HashSet<string> Capitalised = new(["yhvh", "jesus", "holy-spirit"], StringComparer.Ordinal);

    /// <summary>
    /// The names carried onto pronouns that may not stay (the owner's ruling of 2026-10-07): a
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

    private readonly Lazy<ForeignNameRule> _foreign;

    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<string, (string Surface, string? Strong)>>> _baseWords = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<string, List<string>>>> _namedByVerse = new(StringComparer.Ordinal);

    private IReadOnlyDictionary<string, (string Surface, string? Strong)> BaseWords(string reference) =>
        _baseWords.GetOrAdd(reference, r => new Lazy<IReadOnlyDictionary<string, (string, string?)>>(() =>
        {
            using var reader = Store.Open(TextWord.ProductOf(r), TextWord.Schema(r));
            return reader.Rows().ToDictionary(w => w.Key.Own, w => (w.Surface, w.Strong), StringComparer.Ordinal);
        })).Value;

    /// <summary>The records a reference text's settled names hold in each verse.</summary>
    private IReadOnlyDictionary<string, List<string>> NamedByVerse(string reference) =>
        _namedByVerse.GetOrAdd(reference, r => new Lazy<IReadOnlyDictionary<string, List<string>>>(() =>
            SettledOf(r).Values.SelectMany(list => list).GroupBy(n => n.Own.Verse.ToString())
                .ToDictionary(g => g.Key, g => g.Select(n => n.Record).Distinct().ToList(), StringComparer.Ordinal))).Value;

    /// <summary>The names the foreign-names rule refuses (<see cref="ForeignNameRule"/>).</summary>
    private IEnumerable<(string Own, string Record)> Foreign(Dictionary<(string Own, string Record), Candidate> candidates, IReadOnlyDictionary<string, Word> words)
    {
        var rule = _foreign.Value;
        var byForm = new Dictionary<string, List<(int Verse, string Record)>>(StringComparer.Ordinal);
        var byVerse = new Dictionary<int, HashSet<string>>();
        foreach (var key in candidates.Keys)
        {
            if (!words.TryGetValue(key.Own, out var word))
            {
                continue;
            }

            if (!byForm.TryGetValue(word.Form, out var list))
            {
                byForm[word.Form] = list = [];
            }

            list.Add((word.Verse, key.Record));
            if (!byVerse.TryGetValue(word.Verse, out var named))
            {
                byVerse[word.Verse] = named = new HashSet<string>(StringComparer.Ordinal);
            }

            named.Add(key.Record);
        }

        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var kinCounts = new Dictionary<(string Form, string Record), int>();
        foreach (var (key, candidate) in candidates)
        {
            if (!words.TryGetValue(key.Own, out var word) || !word.WrittenAsName
                || !Records.TryGetValue(key.Record, out var record) || record.Kind != "person"
                || (candidate.Settled.Confidence is null && candidate.Link >= 1.0))
            {
                continue;
            }

            var space = candidate.Through.IndexOf(' ');
            var (reference, baseOwn) = (candidate.Through[..space], candidate.Through[(space + 1)..]);
            if (!BaseWords(reference).TryGetValue(baseOwn, out var seed)
                || !rule.IsOwnName(key.Record, seed.Surface, seed.Strong)
                || rule.IsSpelling(key.Record, word.Surface, seed.Surface))
            {
                continue;
            }

            var form = byForm[word.Form];
            if (!counts.TryGetValue(word.Form, out var tally))
            {
                counts[word.Form] = tally = form.GroupBy(f => f.Record, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            }

            int Kin(string one) =>
                kinCounts.TryGetValue((word.Form, one), out var n) ? n : kinCounts[(word.Form, one)] = tally.Where(t => rule.Kin(one, t.Key)).Sum(t => t.Value);
            var ours = Kin(key.Record);
            var baseVerse = baseOwn[..baseOwn.LastIndexOf('.')];
            var atBase = NamedByVerse(reference).GetValueOrDefault(baseVerse) ?? [];
            foreach (var other in tally.Keys.Where(o => !rule.Kin(key.Record, o)))
            {
                var theirs = Kin(other);
                if (theirs < Loading.Encyclopedia.ForeignNames.Least || theirs < Loading.Encyclopedia.ForeignNames.Outnumbering * ours || !atBase.Any(named => rule.Kin(other, named)))
                {
                    continue;
                }

                var elsewhere = form.Where(f => rule.Kin(key.Record, f.Record)
                                                && !byVerse[f.Verse].Any(named => rule.Kin(other, named)))
                    .Select(f => f.Verse).Distinct().Take(Loading.Encyclopedia.ForeignNames.Elsewhere).Count();
                if (elsewhere < Loading.Encyclopedia.ForeignNames.Elsewhere)
                {
                    yield return key;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Below this share of a text's persons carried from a proper noun onto a word written with a capital, the text does not
    /// write names with capitals (WH1881, Sinaiticus, Vaticanus, RP2018, Alexandrinus) and <see cref="Unprinted"/> leaves it alone.
    /// </summary>
    internal const double CapitalisedText = 0.9;

    /// <summary>
    /// A form the text writes for a person in fewer than this share of its occurrences is no name of the text's
    /// (<see cref="Unprinted"/>): <em>multitude</em> carries a person once in 346, <em>il</em> 94 times in 6,282.
    /// </summary>
    internal const double NamingForm = 0.1;

    /// <summary>
    /// A person's name carried from the base's proper noun onto a word the text does not print as a name (PRB-1719): the King
    /// James reads the אָמוֹן of Jeremiah 46:25 as the noun <em>multitude</em>, which names nobody, so the god Amon does not go
    /// on it. Refused where the text writes names with capitals (<see cref="CapitalisedText"/>), the word is written without one,
    /// it is no pronoun (those have their own rule) and no part of a hyphened name, and the text gives a person to that form
    /// in under <see cref="NamingForm"/> of its occurrences, so that a name a language writes in lower case (<em>сатана</em>,
    /// <em>кесаря</em>, <em>херувими</em>) is kept. Most of what it takes back is a link's slip onto the word beside the name
    /// (<em>against</em>, <em>when</em>, the <em>son</em> of <em>the son of Heli</em>).
    /// </summary>
    private IEnumerable<(string Own, string Record)> Unprinted(Dictionary<(string Own, string Record), Candidate> candidates, IReadOnlyDictionary<string, Word> words)
    {
        var persons = candidates
            .Where(c => c.Value.Role == NameRoles.Name && Records.TryGetValue(c.Key.Record, out var record) && record.Kind == "person"
                        && words.ContainsKey(c.Key.Own))
            .ToList();
        var proper = persons.Where(c => words[c.Key.Own].Cased && FromProperNoun(c.Value.Through)).ToList();
        if (proper.Count == 0 || proper.Count(c => WrittenWithCapital(words[c.Key.Own].Surface)) < CapitalisedText * proper.Count)
        {
            yield break;
        }

        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var word in words.Values)
        {
            var form = Letters(word.Surface);
            occurrences[form] = occurrences.GetValueOrDefault(form) + 1;
        }

        var named = persons.Select(c => c.Key.Own).Distinct(StringComparer.Ordinal)
            .GroupBy(own => Letters(words[own].Surface), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var (key, _) in proper)
        {
            var word = words[key.Own];
            if (WrittenWithCapital(word.Surface) || word.Pronoun || word.Joined)
            {
                continue;
            }

            var form = Letters(word.Surface);
            if (named.GetValueOrDefault(form) < NamingForm * occurrences.GetValueOrDefault(form, 1))
            {
                yield return key;
            }
        }
    }

    /// <summary>The word's letters as the text writes them, case kept, with its apostrophes and hyphens: the form <see cref="Unprinted"/> counts.</summary>
    private static string Letters(string surface) => string.Concat(surface.Where(c => char.IsLetter(c) || c is '\'' or '’' or '-'));

    /// <summary>Whether a word is written with a capital: its first letter, or the one after an elided article (<em>d’Abraham</em>).</summary>
    internal static bool WrittenWithCapital(string surface)
    {
        var first = surface.FirstOrDefault(char.IsLetter);
        if (first != default && char.IsUpper(first))
        {
            return true;
        }

        var bare = surface.TrimStart('\'', '’', '‘', '"', '“', '„', '«', '(', '[');
        var apostrophe = bare.IndexOfAny(['\'', '’']);
        return apostrophe is > 0 and <= 2 && apostrophe + 1 < bare.Length && char.IsUpper(bare[apostrophe + 1]);
    }

    private readonly ConcurrentDictionary<string, Lazy<HashSet<string>>> _properNouns = new(StringComparer.Ordinal);

    /// <summary>Whether a carried name's base word (<c>BHSA 24.46:25.9</c>) is a proper noun (<see cref="ProperNouns"/>).</summary>
    private bool FromProperNoun(string through)
    {
        var space = through.IndexOf(' ');
        return space > 0 && ProperNouns(through[..space]).Contains(through[(space + 1)..]);
    }

    /// <summary>
    /// A reference text's proper nouns: a word its parsing marks so (BHSA's and the Samaritan's <c>nmpr</c>; a gentilic is
    /// not one, for a language writes the people's word in lower case), a word whose lemma is capitalised (the Greek), or,
    /// with neither, a word the text writes with a capital inside a sentence (the Vulgate).
    /// </summary>
    private HashSet<string> ProperNouns(string reference) =>
        _properNouns.GetOrAdd(reference, r => new Lazy<HashSet<string>>(() =>
        {
            var proper = new HashSet<string>(StringComparer.Ordinal);
            if (!Store.Exists(TextWord.ProductOf(r)))
            {
                return proper;
            }

            using var reader = Store.Open(TextWord.ProductOf(r), TextWord.Schema(r));
            TextWord? previous = null;
            foreach (var word in reader.Rows())
            {
                var pos = NameRules.PartOfSpeech(word.Morphology);
                bool named;
                if (pos == "nmpr")
                {
                    named = true;
                }
                else if (word.Lemma is { Length: > 0 } lemma)
                {
                    named = WrittenWithCapital(lemma);
                }
                else
                {
                    var trailer = previous?.Trailer.TrimEnd() ?? string.Empty;
                    named = pos is null && previous is not null && previous.Key.Verse == word.Key.Verse
                            && !trailer.EndsWith('.') && !trailer.EndsWith('!') && !trailer.EndsWith('?') && WrittenWithCapital(word.Surface);
                }

                if (named)
                {
                    proper.Add(word.Key.Own);
                }

                previous = word;
            }

            return proper;
        })).Value;

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
            var afterApostrophe = previous is not null && previous.Key.Verse == word.Key.Verse && EndsInApostrophe(previous.Trailer);
            words[word.Key.Own] = new Word(
                word.Key,
                word.Surface,
                cased,
                writtenAsName,
                Rules.IsPronoun(language, bare),
                Rules.IsNeverAName(language, bare, language == "hbo" ? NameRules.PartOfSpeech(word.Morphology) : null, word.Strong, word.Normalised)
                || (afterApostrophe && bare.Equals("s", StringComparison.OrdinalIgnoreCase)),
                PossessiveEnding().IsMatch(word.Surface) || EndsInApostrophe(word.Trailer),
                Rules.SexOf(language, bare),
                verse,
                word.Normalised ?? bare,
                word.Strong,
                previous is not null && previous.Key.Verse == word.Key.Verse && previous.Trailer.Trim() is "-" or "‐" or "‑" or "–");
            previous = word;
        }

        return words;
    }

    /// <summary>A trailer that is an apostrophe: the text writes a possessive's <em>s</em> as a word of its own (<em>wife’ s</em>, <em>LORD 'S</em>).</summary>
    private static bool EndsInApostrophe(string trailer) => trailer.Trim() is "'" or "’";

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

    /// <summary>
    /// A text's own names as the spellings index reads them (<see cref="IndexedName"/>), from <c>names/projected</c>. A name is a guess where an aligner's
    /// link carried it; firm where a person or a source settled it, or it is sure enough; spellable where its word is neither
    /// a pronoun nor a word its language never names anybody with.
    /// </summary>
    public List<IndexedName> IndexNames(string text)
    {
        var words = WordsOf(text);
        var names = new List<IndexedName>();
        using var reader = Store.Open(NameProducts.Projected(text), ProjectedName.Schema(text));
        foreach (var name in reader.Rows().Where(n => NameOrigins.IsOwn(n.Origin)))
        {
            var guessed = name.Link is not null && MappingMethods.DatabaseMethod(name.Link) == Database.Entities.Enums.LinkMethod.Aligner;
            var spellable = words.TryGetValue(name.Own.Own, out var word) && !word.NeverAName && !word.Pronoun;
            var thing = Records.TryGetValue(name.Record, out var record)
                        && Loading.Encyclopedia.Renderings.IsThing(record.Kind, word?.Surface ?? string.Empty);
            var firm = guessed
                ? name.Confidence >= Loading.Encyclopedia.SearchSpellings.HighConfidence
                : name.Method is "manual" or "stated-by-source" || name.Confidence is null || name.Confidence >= Loading.Encyclopedia.SearchSpellings.HighConfidence;
            names.Add(new IndexedName(name.Own, name.Record, guessed ? name.Confidence : null, thing, firm, spellable));
        }

        return names;
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
